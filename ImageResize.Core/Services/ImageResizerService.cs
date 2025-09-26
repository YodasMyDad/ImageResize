using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Models;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageResize.Core.Services;

/// <summary>
/// Main service for image resizing operations.
/// </summary>
public sealed class ImageResizerService(
    IOptions<ImageResizeOptions> options,
    IImageCache cache,
    IImageCodec codec,
    ILogger<ImageResizerService> logger)
    : IImageResizerService
{
    private readonly AsyncKeyedLocker _locker = new();

    /// <inheritdoc />
    public async Task<ResizeResult> EnsureResizedAsync(string relativePath, ResizeOptions options, CancellationToken ct = default)
    {
        // Validate and resolve original file path
        var originalPath = ResolveOriginalPath(relativePath);
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original image not found", originalPath);
        }

        // Generate source signature
        var sourceSignature = await GenerateSourceSignatureAsync(originalPath, ct);

        // Get cache path
        var cachedPath = cache.GetCachedFilePath(relativePath, options, sourceSignature);

        // Use keyed lock to prevent thundering herd
        await using var lockHandle = await _locker.LockAsync(cachedPath, ct);

        // Check cache first
        if (await cache.ExistsAsync(cachedPath, ct))
        {
            logger.LogDebug("Cache hit for {Path}", cachedPath);
            var fileInfo = new FileInfo(cachedPath);
            return new ResizeResult(
                OriginalPath: originalPath,
                CachedPath: cachedPath,
                ContentType: GetContentTypeFromPath(cachedPath),
                OutputWidth: 0, // Would need to probe cached file to get this
                OutputHeight: 0,
                BytesWritten: fileInfo.Length
            );
        }

        logger.LogDebug("Cache miss for {Path}, resizing...", cachedPath);

        // Load and resize original
        await using var originalStream = File.OpenRead(originalPath);
        var (resizedStream, contentType, outW, outH) = await codec.ResizeAsync(
            originalStream, null, options, ct);

        // Write to cache
        await cache.WriteAtomicallyAsync(cachedPath, resizedStream, ct);

        var bytesWritten = resizedStream.Length;
        logger.LogInformation("Resized {Original} to {Cached} ({Width}x{Height}, {Bytes} bytes)",
            originalPath, cachedPath, outW, outH, bytesWritten);

        return new ResizeResult(
            OriginalPath: originalPath,
            CachedPath: cachedPath,
            ContentType: contentType,
            OutputWidth: outW,
            OutputHeight: outH,
            BytesWritten: bytesWritten
        );
    }

    /// <inheritdoc />
    public IImageCodec GetCodec() => codec;

    /// <inheritdoc />
    public async Task<(Stream Stream, string ContentType, int Width, int Height)> ResizeToStreamAsync(
        Stream original,
        string? originalContentType,
        ResizeOptions options,
        CancellationToken ct = default)
    {
        return await codec.ResizeAsync(original, originalContentType, options, ct);
    }

    /// <inheritdoc />
    public async Task<ImageResult> ResizeAsync(
        Stream original,
        string? originalContentType,
        ResizeOptions options,
        CancellationToken ct = default)
    {
        // Probe original image to get metadata
        var originalPosition = original.Position;
        var (originalWidth, originalHeight, detectedContentType) = await codec.ProbeAsync(original, ct);
        original.Position = originalPosition; // Reset stream position

        // Resize the image
        var (resizedStream, contentType, newWidth, newHeight) = await codec.ResizeAsync(
            original, originalContentType, options, ct);

        // Create helper functions for content type detection
        string GetFileExtensionFromContentType(string ct) => ct.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            _ => ".bin"
        };

        string GetFormatFromContentType(string ct) => ct.ToLowerInvariant() switch
        {
            "image/jpeg" => "JPEG",
            "image/png" => "PNG",
            "image/gif" => "GIF",
            "image/webp" => "WebP",
            "image/bmp" => "BMP",
            "image/tiff" => "TIFF",
            _ => "Unknown"
        };

        // Create and return ImageResult with full metadata
        return new ImageResult(
            resizedStream,
            newWidth,
            newHeight,
            contentType,
            resizedStream.Length,
            GetFileExtensionFromContentType(contentType),
            GetFormatFromContentType(contentType),
            originalWidth,
            originalHeight,
            options.Quality,
            true); // IsProcessed = true since we resized it
    }

    private string ResolveOriginalPath(string relativePath)
    {
        // Security: Prevent path traversal
        var fullPath = Path.GetFullPath(Path.Combine(options.Value.ContentRoot, relativePath));

        // Ensure the resolved path is within ContentRoot
        var contentRootFull = Path.GetFullPath(options.Value.ContentRoot);
        if (!fullPath.StartsWith(contentRootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path traversal attempt detected");
        }

        return fullPath;
    }

    private async Task<string> GenerateSourceSignatureAsync(string filePath, CancellationToken ct)
    {
        var fileInfo = new FileInfo(filePath);
        var lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var length = fileInfo.Length;

        var signature = $"{lastWriteTicks}:{length}";

        if (options.Value.HashOriginalContent)
        {
            await using var stream = File.OpenRead(filePath);
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hashBytes = await sha1.ComputeHashAsync(stream, ct);
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            signature += $":{hashString}";
        }

        return signature;
    }

    private static string GetContentTypeFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream"
        };
    }
}

/// <summary>
/// Simple async keyed locker for preventing concurrent operations on the same key.
/// </summary>
internal sealed class AsyncKeyedLocker
{
    private readonly Dictionary<string, AsyncLock> _locks = new();
    private readonly object _lock = new();

    public async Task<AsyncLockHandle> LockAsync(string key, CancellationToken ct = default)
    {
        AsyncLock asyncLock;
        lock (_lock)
        {
            if (!_locks.TryGetValue(key, out asyncLock!))
            {
                asyncLock = new AsyncLock();
                _locks[key] = asyncLock;
            }
        }

        await asyncLock.WaitAsync();
        return new AsyncLockHandle(asyncLock, key, this);
    }

    private void ReleaseLock(string key)
    {
        lock (_lock)
        {
            _locks.Remove(key);
        }
    }

    public readonly struct AsyncLockHandle : IDisposable, IAsyncDisposable
    {
        private readonly AsyncLock _asyncLock;
        private readonly string _key;
        private readonly AsyncKeyedLocker _locker;

        public AsyncLockHandle(AsyncLock asyncLock, string key, AsyncKeyedLocker locker)
        {
            _asyncLock = asyncLock;
            _key = key;
            _locker = locker;
        }

        public async ValueTask DisposeAsync()
        {
            _asyncLock.Release();
            _locker.ReleaseLock(_key);
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            _asyncLock.Release();
            _locker.ReleaseLock(_key);
        }
    }
}

internal sealed class AsyncLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task WaitAsync() => await _semaphore.WaitAsync();
    public void Wait() => _semaphore.Wait();
    public void Release() => _semaphore.Release();
}
