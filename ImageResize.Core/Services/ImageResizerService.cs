using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Models;
using ImageResize.Core.Utilities;
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
        var originalPath = ResolveOriginalPath(relativePath);
        if (!File.Exists(originalPath))
            throw new FileNotFoundException("Original image not found", originalPath);

        var sourceSignature = await GenerateSourceSignatureAsync(originalPath, ct).ConfigureAwait(false);
        var cachedPath = cache.GetCachedFilePath(relativePath, options, sourceSignature);

        await using var lockHandle = await _locker.LockAsync(cachedPath, ct).ConfigureAwait(false);

        if (await cache.ExistsAsync(cachedPath, ct).ConfigureAwait(false))
        {
            logger.LogDebug("Cache hit for {Path}", cachedPath);
            var fileInfo = new FileInfo(cachedPath);
            return new ResizeResult(
                OriginalPath: originalPath,
                CachedPath: cachedPath,
                ContentType: GetContentTypeFromPath(cachedPath),
                OutputWidth: 0,
                OutputHeight: 0,
                BytesWritten: fileInfo.Length
            );
        }

        logger.LogDebug("Cache miss for {Path}, resizing...", cachedPath);

        await using var originalStream = File.OpenRead(originalPath);
        var (resizedStream, contentType, outW, outH) = await codec.ResizeAsync(
            originalStream, null, options, ct).ConfigureAwait(false);

        await cache.WriteAtomicallyAsync(cachedPath, resizedStream, ct).ConfigureAwait(false);

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
    public Task<(Stream Stream, string ContentType, int Width, int Height)> ResizeToStreamAsync(
        Stream original,
        string? originalContentType,
        ResizeOptions options,
        CancellationToken ct = default)
        => codec.ResizeAsync(original, originalContentType, options, ct);

    /// <inheritdoc />
    public async Task<ImageResult> ResizeAsync(
        Stream original,
        string? originalContentType,
        ResizeOptions options,
        CancellationToken ct = default)
    {
        var originalPosition = original.Position;
        var (originalWidth, originalHeight, _) = await codec.ProbeAsync(original, ct).ConfigureAwait(false);
        original.Position = originalPosition;

        var (resizedStream, contentType, newWidth, newHeight) = await codec.ResizeAsync(
            original, originalContentType, options, ct).ConfigureAwait(false);

        static string FileExtFromCt(string ct) => ct.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            _ => ".bin"
        };

        static string FormatFromCt(string ct) => ct.ToLowerInvariant() switch
        {
            "image/jpeg" => "JPEG",
            "image/png" => "PNG",
            "image/gif" => "GIF",
            "image/webp" => "WebP",
            "image/bmp" => "BMP",
            "image/tiff" => "TIFF",
            _ => "Unknown"
        };

        return new ImageResult(
            resizedStream,
            newWidth,
            newHeight,
            contentType,
            resizedStream.Length,
            FileExtFromCt(contentType),
            FormatFromCt(contentType),
            originalWidth,
            originalHeight,
            options.Quality,
            true);
    }

    private string ResolveOriginalPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(options.Value.WebRoot, relativePath));
        var webRootFull = Path.GetFullPath(options.Value.WebRoot);
        if (!fullPath.StartsWith(webRootFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal attempt detected");
        return fullPath;
    }

    private async Task<string> GenerateSourceSignatureAsync(string filePath, CancellationToken ct)
    {
        var fileInfo = new FileInfo(filePath);
        var signature = $"{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}";

        if (options.Value.HashOriginalContent)
        {
            await using var stream = File.OpenRead(filePath);
            var hash = await HashingUtilities.HashStreamToHexAsync(stream, ct).ConfigureAwait(false);
            signature += $":{hash}";
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
    private readonly Dictionary<string, AsyncLock> _locks = [];
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

        await asyncLock.WaitAsync(ct).ConfigureAwait(false);
        return new AsyncLockHandle(asyncLock, key, this);
    }

    private void ReleaseLock(string key)
    {
        lock (_lock)
        {
            _locks.Remove(key);
        }
    }

    public readonly struct AsyncLockHandle(AsyncLock asyncLock, string key, AsyncKeyedLocker locker)
        : IDisposable, IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            asyncLock.Release();
            locker.ReleaseLock(key);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            asyncLock.Release();
            locker.ReleaseLock(key);
        }
    }
}

internal sealed class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken ct = default) => _semaphore.WaitAsync(ct);
    public void Wait() => _semaphore.Wait();
    public void Release() => _semaphore.Release();
    public void Dispose() => _semaphore.Dispose();
}
