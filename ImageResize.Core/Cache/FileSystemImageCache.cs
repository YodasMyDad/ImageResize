using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Utilities;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageResize.Core.Cache;

/// <summary>
/// File-system-backed image cache with atomic writes and folder sharding.
/// </summary>
public sealed class FileSystemImageCache(IOptions<ImageResizeOptions> options, ILogger<FileSystemImageCache> logger)
    : IImageCache
{
    private const string TempSuffix = ".tmp";
    private readonly object _cacheLock = new();

    /// <inheritdoc />
    public string GetCachedFilePath(string relPath, ResizeOptions resizeOptions, string sourceSignature)
    {
        ArgumentNullException.ThrowIfNull(relPath);
        ArgumentNullException.ThrowIfNull(resizeOptions);
        ArgumentNullException.ThrowIfNull(sourceSignature);

        var cacheKey = GenerateCacheKey(relPath, resizeOptions, sourceSignature);
        var shardedPath = GetShardedPath(cacheKey);
        var extension = Path.GetExtension(relPath).ToLowerInvariant();

        return Path.Combine(options.Value.CacheRoot, shardedPath + extension);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string cachedPath, CancellationToken ct = default)
    {
        var exists = File.Exists(cachedPath);
        logger.LogDebug("Cache file {Path} exists: {Exists}", cachedPath, exists);
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string cachedPath, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(cachedPath));

    /// <inheritdoc />
    public async Task WriteAtomicallyAsync(string cachedPath, Stream data, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(cachedPath)!;
        Directory.CreateDirectory(directory);

        var dataSize = data.Length;
        await EnforceCacheSizeLimitAsync(dataSize, ct).ConfigureAwait(false);

        var tempPath = cachedPath + TempSuffix;

        try
        {
            await using (var tempFile = File.Create(tempPath))
            {
                await data.CopyToAsync(tempFile, ct).ConfigureAwait(false);
                await tempFile.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, cachedPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemp(tempPath);
            throw;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "I/O failure writing cache file {Path}", cachedPath);
            TryDeleteTemp(tempPath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Permission denied writing cache file {Path}", cachedPath);
            TryDeleteTemp(tempPath);
            throw;
        }

        logger.LogDebug("Atomically wrote cache file {Path} ({Size} bytes)", cachedPath, dataSize);
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private string GenerateCacheKey(string relPath, ResizeOptions resizeOptions, string sourceSignature)
    {
        var normalizedPath = Path.GetFullPath(relPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .ToLowerInvariant()
            .TrimStart('/');

        var optionsPart = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"w={resizeOptions.Width ?? 0},h={resizeOptions.Height ?? 0},q={resizeOptions.Quality ?? options.Value.DefaultQuality},up={options.Value.AllowUpscale}");
        var keyInput = $"{normalizedPath}|{optionsPart}|{sourceSignature}";

        return HashingUtilities.HashStringToHex(keyInput);
    }

    private string GetShardedPath(string cacheKey)
    {
        if (options.Value.Cache.FolderSharding <= 0)
            return cacheKey;

        var sharding = options.Value.Cache.FolderSharding * 2;
        if (cacheKey.Length < sharding)
            return cacheKey;

        var parts = new List<string>();
        for (var i = 0; i < sharding; i += 2)
        {
            if (i + 2 <= cacheKey.Length)
                parts.Add(cacheKey.Substring(i, 2));
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

            var filesToDelete = GetFilesToDelete(currentSize + newFileSize - maxCacheBytes);
            foreach (var file in filesToDelete)
            {
                try
                {
                    File.Delete(file);
                    logger.LogDebug("Deleted cache file {Path} to enforce size limit", file);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Failed to delete cache file {Path}", file);
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.LogWarning(ex, "Permission denied deleting cache file {Path}", file);
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
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
        catch (IOException ex)
        {
            logger.LogWarning(ex, "I/O error calculating current cache size");
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Permission denied calculating current cache size");
            return 0;
        }
    }

    private List<string> GetFilesToDelete(long bytesToFree)
    {
        if (!Directory.Exists(options.Value.CacheRoot))
            return [];

        try
        {
            var files = Directory.EnumerateFiles(options.Value.CacheRoot, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Where(fi => !fi.Name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
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
        catch (IOException ex)
        {
            logger.LogWarning(ex, "I/O error determining files to delete for cache cleanup");
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Permission denied determining files to delete for cache cleanup");
            return [];
        }
    }

    /// <summary>
    /// Prunes the cache by deleting the oldest files and sweeping any orphaned <c>.tmp</c>
    /// files left behind by a previous crashed write. Invoked on startup when
    /// <see cref="ImageResizeOptions.CacheOptions.PruneOnStartup"/> is enabled; the <c>.tmp</c>
    /// sweep runs unconditionally and is always safe.
    /// </summary>
    public void PruneCacheOnStartup()
    {
        SweepOrphanedTempFiles();

        if (!options.Value.Cache.PruneOnStartup)
            return;

        logger.LogInformation("Starting cache pruning on startup");

        try
        {
            var filesToDelete = Directory.EnumerateFiles(options.Value.CacheRoot, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Where(fi => !fi.Name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(fi => fi.LastAccessTimeUtc)
                .Take(100)
                .Select(fi => fi.FullName)
                .ToList();

            foreach (var file in filesToDelete)
            {
                try
                {
                    File.Delete(file);
                    logger.LogDebug("Pruned cache file {Path}", file);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Failed to prune cache file {Path}", file);
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.LogWarning(ex, "Permission denied pruning cache file {Path}", file);
                }
            }

            logger.LogInformation("Cache pruning completed, deleted {Count} files", filesToDelete.Count);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Cache pruning failed");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Cache pruning failed (permission)");
        }
    }

    private void SweepOrphanedTempFiles()
    {
        var root = options.Value.CacheRoot;
        if (!Directory.Exists(root))
            return;

        var threshold = DateTime.UtcNow.AddHours(-1);
        try
        {
            foreach (var tmp in Directory.EnumerateFiles(root, "*" + TempSuffix, SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(tmp) < threshold)
                        File.Delete(tmp);
                }
                catch (IOException) { /* best effort */ }
                catch (UnauthorizedAccessException) { /* best effort */ }
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Temp-file sweep failed under {Root}", root);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Temp-file sweep denied under {Root}", root);
        }
    }
}
