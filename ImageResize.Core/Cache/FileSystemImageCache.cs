using ImageResize.Abstractions.Configuration;
using ImageResize.Abstractions.Interfaces;
using ImageResize.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace ImageResize.Core.Cache;

/// <summary>
/// File system-based image cache with atomic writes and folder sharding.
/// </summary>
public sealed class FileSystemImageCache : IImageCache
{
    private readonly ImageResizeOptions _options;
    private readonly ILogger<FileSystemImageCache> _logger;

    public FileSystemImageCache(ImageResizeOptions options, ILogger<FileSystemImageCache> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string GetCachedFilePath(string relPath, ResizeOptions options, string sourceSignature)
    {
        var cacheKey = GenerateCacheKey(relPath, options, sourceSignature);
        var shardedPath = GetShardedPath(cacheKey);
        var extension = Path.GetExtension(relPath).ToLowerInvariant();

        return Path.Combine(_options.CacheRoot, shardedPath + extension);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string cachedPath, CancellationToken ct = default)
    {
        var exists = File.Exists(cachedPath);
        _logger.LogDebug("Cache file {Path} exists: {Exists}", cachedPath, exists);
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

        _logger.LogDebug("Atomically wrote cache file {Path}", cachedPath);
    }

    private string GenerateCacheKey(string relPath, ResizeOptions options, string sourceSignature)
    {
        // Normalize path for consistent keying
        var normalizedPath = Path.GetFullPath(relPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .ToLowerInvariant()
            .TrimStart('/');

        var optionsPart = $"w={options.Width ?? 0},h={options.Height ?? 0},q={options.Quality ?? _options.DefaultQuality},up={_options.AllowUpscale}";
        var keyInput = $"{normalizedPath}|{optionsPart}|{sourceSignature}";

        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyInput));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return hashString;
    }

    private string GetShardedPath(string cacheKey)
    {
        if (_options.Cache.FolderSharding <= 0)
            return cacheKey;

        var sharding = _options.Cache.FolderSharding * 2; // 2 chars per level
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
