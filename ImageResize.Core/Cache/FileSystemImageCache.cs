using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageResize.Core.Cache;

/// <summary>
/// File system-based image cache with atomic writes and folder sharding.
/// </summary>
public sealed class FileSystemImageCache(IOptions<ImageResizeOptions> options, ILogger<FileSystemImageCache> logger)
    : IImageCache
{
    private readonly object _cacheLock = new();

    /// <inheritdoc />
    public string GetCachedFilePath(string relPath, ResizeOptions options1, string sourceSignature)
    {
        var cacheKey = GenerateCacheKey(relPath, options1, sourceSignature);
        var shardedPath = GetShardedPath(cacheKey);
        var extension = Path.GetExtension(relPath).ToLowerInvariant();

        return Path.Combine(options.Value.CacheRoot, shardedPath + extension);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string cachedPath, CancellationToken ct = default)
    {
        var exists = File.Exists(cachedPath);
        logger.LogDebug("Cache file {Path} exists: {Exists}", cachedPath, exists);
        return exists;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(string cachedPath, CancellationToken ct = default)
    {
        return File.OpenRead(cachedPath);
    }

    /// <inheritdoc />
    public async Task WriteAtomicallyAsync(string cachedPath, Stream data, CancellationToken ct = default)
    {
        // Ensure cache directory exists
        var directory = Path.GetDirectoryName(cachedPath)!;
        Directory.CreateDirectory(directory);

        // Check cache size limits before writing
        var dataSize = data.Length;
        await EnforceCacheSizeLimitAsync(dataSize, ct);

        // Write to temporary file first
        var tempPath = cachedPath + ".tmp";

        try
        {
            await using var tempFile = File.Create(tempPath);
            await data.CopyToAsync(tempFile, ct);
            await tempFile.FlushAsync(ct);
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }

        // Atomic move/rename
        File.Move(tempPath, cachedPath, overwrite: true);

        logger.LogDebug("Atomically wrote cache file {Path} ({Size} bytes)", cachedPath, dataSize);
    }

    private string GenerateCacheKey(string relPath, ResizeOptions options1, string sourceSignature)
    {
        // Normalize path for consistent keying
        var normalizedPath = Path.GetFullPath(relPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .ToLowerInvariant()
            .TrimStart('/');

        var optionsPart = $"w={options1.Width ?? 0},h={options1.Height ?? 0},q={options1.Quality ?? options.Value.DefaultQuality},up={options.Value.AllowUpscale}";
        var keyInput = $"{normalizedPath}|{optionsPart}|{sourceSignature}";

        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyInput));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return hashString;
    }

    private string GetShardedPath(string cacheKey)
    {
        if (options.Value.Cache.FolderSharding <= 0)
            return cacheKey;

        var sharding = options.Value.Cache.FolderSharding * 2; // 2 chars per level
        if (cacheKey.Length < sharding)
            return cacheKey;

        var parts = new List<string>();
        for (var i = 0; i < sharding; i += 2)
        {
            if (i + 2 <= cacheKey.Length)
            {
                parts.Add(cacheKey.Substring(i, 2));
            }
        }

        parts.Add(cacheKey[sharding..]);
        return Path.Combine([.. parts]);
    }

    private async Task EnforceCacheSizeLimitAsync(long newFileSize, CancellationToken ct)
    {
        var maxCacheBytes = options.Value.Cache.MaxCacheBytes;
        if (maxCacheBytes <= 0) // 0 = unlimited
            return;

        lock (_cacheLock)
        {
            var currentSize = GetCurrentCacheSize();
            if (currentSize + newFileSize <= maxCacheBytes)
                return;

            // Need to clean up old files
            var filesToDelete = GetFilesToDelete(currentSize + newFileSize - maxCacheBytes);
            foreach (var file in filesToDelete)
            {
                try
                {
                    File.Delete(file);
                    logger.LogDebug("Deleted cache file {Path} to enforce size limit", file);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete cache file {Path}", file);
                }
            }
        }
    }

    private long GetCurrentCacheSize()
    {
        if (!Directory.Exists(options.Value.CacheRoot))
            return 0;

        try
        {
            return Directory.EnumerateFiles(options.Value.CacheRoot, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to calculate current cache size");
            return 0;
        }
    }

    private List<string> GetFilesToDelete(long bytesToFree)
    {
        if (!Directory.Exists(options.Value.CacheRoot))
            return [];

        try
        {
            // Get all cache files sorted by last access time (oldest first)
            var files = Directory.EnumerateFiles(options.Value.CacheRoot, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Where(fi => !fi.Name.EndsWith(".tmp")) // Don't delete temp files
                .OrderBy(fi => fi.LastAccessTimeUtc)
                .ToList();

            var filesToDelete = new List<string>();
            long freedBytes = 0;

            foreach (var file in files)
            {
                if (freedBytes >= bytesToFree)
                    break;

                filesToDelete.Add(file.FullName);
                freedBytes += file.Length;
            }

            return filesToDelete;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to determine files to delete for cache cleanup");
            return [];
        }
    }

    /// <summary>
    /// Prunes the cache by deleting old files if PruneOnStartup is enabled.
    /// </summary>
    public void PruneCacheOnStartup()
    {
        if (!options.Value.Cache.PruneOnStartup)
            return;

        logger.LogInformation("Starting cache pruning on startup");

        try
        {
            var filesToDelete = Directory.EnumerateFiles(options.Value.CacheRoot, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Where(fi => !fi.Name.EndsWith(".tmp")) // Don't delete temp files
                .OrderBy(fi => fi.LastAccessTimeUtc)
                .Take(100) // Delete oldest 100 files
                .Select(fi => fi.FullName)
                .ToList();

            foreach (var file in filesToDelete)
            {
                try
                {
                    File.Delete(file);
                    logger.LogDebug("Pruned cache file {Path}", file);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to prune cache file {Path}", file);
                }
            }

            logger.LogInformation("Cache pruning completed, deleted {Count} files", filesToDelete.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cache pruning failed");
        }
    }
}
