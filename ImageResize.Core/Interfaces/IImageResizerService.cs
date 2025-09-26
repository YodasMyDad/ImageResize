using ImageResize.Core.Models;
using ImageResize.Models;

namespace ImageResize.Core.Interfaces;

/// <summary>
/// Service for resizing images with caching.
/// </summary>
public interface IImageResizerService
{
    /// <summary>
    /// Loads from disk, resizes (fit), caches, and returns info.
    /// </summary>
    Task<ResizeResult> EnsureResizedAsync(
        string relativePath,
        ResizeOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Core resize without touching HTTP. Returns a stream and content-type.
    /// Does NOT write to cache (for advanced callers).
    /// </summary>
    Task<(Stream Stream, string ContentType, int Width, int Height)>
        ResizeToStreamAsync(
            Stream original,
            string? originalContentType,
            ResizeOptions options,
            CancellationToken ct = default);

    /// <summary>
    /// Simple resize that returns a complete ImageResult with all metadata.
    /// Perfect for most use cases - handles everything in one call.
    /// </summary>
    Task<ImageResult> ResizeAsync(
        Stream original,
        string? originalContentType,
        ResizeOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the image codec used by this service for image processing operations.
    /// </summary>
    IImageCodec GetCodec();
}
