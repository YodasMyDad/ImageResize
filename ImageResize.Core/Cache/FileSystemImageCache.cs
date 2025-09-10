using ImageResize.Abstractions.Configuration;
using ImageResize.Abstractions.Interfaces;
using ImageResize.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace ImageResize.Core.Cache;

/// <summary>
/// File system-based image cache with atomic writes and folder sharding.
/// </summary>
public sealed class FileSystemImageCache(ImageResizeOptions options, ILogger<FileSystemImageCache> logger)
    : IImageCache
{
    /// <inheritdoc />
    public string GetCachedFilePath(string relPath, ResizeOptions options1, string sourceSignature)
    {
        var cacheKey = GenerateCacheKey(relPath, options1, sourceSignature);
        var shardedPath = GetShardedPath(cacheKey);
        var extension = Path.GetExtension(relPath).ToLowerInvariant();

        return Path.Combine(options.CacheRoot, shardedPath + extension);
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

        logger.LogDebug("Atomically wrote cache file {Path}", cachedPath);
    }

    private string GenerateCacheKey(string relPath, ResizeOptions options1, string sourceSignature)
    {
        // Normalize path for consistent keying
        var normalizedPath = Path.GetFullPath(relPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .ToLowerInvariant()
            .TrimStart('/');

        var optionsPart = $"w={options1.Width ?? 0},h={options1.Height ?? 0},q={options1.Quality ?? options.DefaultQuality},up={options.AllowUpscale}";
        var keyInput = $"{normalizedPath}|{optionsPart}|{sourceSignature}";

        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyInput));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return hashString;
    }

    private string GetShardedPath(string cacheKey)
    {
        if (options.Cache.FolderSharding <= 0)
            return cacheKey;

        var sharding = options.Cache.FolderSharding * 2; // 2 chars per level
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
}
