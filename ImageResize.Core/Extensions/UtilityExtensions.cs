using ImageResize.Core.Codecs;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Models;
using ImageResize.Models;
using Microsoft.Extensions.Options;

namespace ImageResize.Core.Extensions;

/// <summary>
/// Extension methods to provide ImageSharp-compatible API for image processing.
/// </summary>
public static class UtilityExtensions
{
    /// <summary>
    /// Extension method to check if image exceeds max pixel size and resize if needed.
    /// Equivalent to the ImageSharp OverMaxSizeCheck extension method.
    /// </summary>
    public static async Task<ImageResult> OverMaxSizeCheckAsync(
        this Stream imageStream,
        int maxPixelSize,
        IImageResizerService resizerService,
        string? contentType = null,
        CancellationToken ct = default)
    {
        // First, probe the image to get original dimensions
        var codec = resizerService.GetCodec();
        var (width, height, detectedContentType) = await codec.ProbeAsync(imageStream, ct);

        // Reset stream position after probing
        imageStream.Position = 0;

        // Check if resizing is needed
        if (width <= maxPixelSize && height <= maxPixelSize)
        {
            // No resize needed, return original
            var streamLength = imageStream.Length;
            var detectedType = detectedContentType ?? contentType ?? "application/octet-stream";
            var fileExt = GetFileExtensionFromContentType(detectedType);
            var imgFormat = GetFormatFromContentType(detectedType);

            return new ImageResult(imageStream, width, height, detectedType,
                                   streamLength, fileExt, imgFormat, width, height, null, false);
        }

        // Determine resize dimensions (maintain aspect ratio)
        int newWidth, newHeight;
        if (width > height)
        {
            newWidth = maxPixelSize;
            newHeight = (int)Math.Round((double)height * maxPixelSize / width);
        }
        else
        {
            newHeight = maxPixelSize;
            newWidth = (int)Math.Round((double)width * maxPixelSize / height);
        }

        // Resize the image
        var options = new ResizeOptions(Width: newWidth, Height: newHeight, Quality: null);
        var (resizedStream, resultContentType, resultWidth, resultHeight) =
            await resizerService.ResizeToStreamAsync(imageStream, contentType, options, ct);

        var resizedLength = resizedStream.Length;
        var resultExt = GetFileExtensionFromContentType(resultContentType);
        var resultFormat = GetFormatFromContentType(resultContentType);

        return new ImageResult(resizedStream, resultWidth, resultHeight, resultContentType,
                               resizedLength, resultExt, resultFormat, width, height, options.Quality, true);
    }

    /// <summary>
    /// Saves the image stream to a file asynchronously.
    /// Equivalent to ImageSharp's SaveAsync method.
    /// </summary>
    public static async Task SaveAsync(this ImageResult imageResult, string filePath, CancellationToken ct = default)
    {
        await using var fileStream = File.Create(filePath);
        imageResult.Stream.Position = 0;
        await imageResult.Stream.CopyToAsync(fileStream, ct);
    }

    /// <summary>
    /// Creates an ImageResult from a stream by loading it.
    /// Equivalent to ImageSharp's Image.LoadAsync method.
    /// </summary>
    public static async Task<ImageResult> LoadAsync(
        this Stream stream,
        IImageResizerService resizerService,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var codec = resizerService.GetCodec();
        var (width, height, detectedContentType) = await codec.ProbeAsync(stream, ct);

        // Reset stream position after probing
        stream.Position = 0;

        var streamSize = stream.Length;
        var finalType = detectedContentType ?? contentType ?? "application/octet-stream";
        var ext = GetFileExtensionFromContentType(finalType);
        var imgFmt = GetFormatFromContentType(finalType);

        return new ImageResult(stream, width, height, finalType,
                               streamSize, ext, imgFmt, width, height, null, false);
    }

    /// <summary>
    /// Gets the codec from the resizer service (internal extension method).
    /// </summary>
    private static IImageCodec GetCodec(this IImageResizerService resizerService)
    {
        return resizerService.GetCodec();
    }

    private static string GetFileExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "image/svg+xml" => ".svg",
            _ => ".bin"
        };
    }

    private static string GetFormatFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "JPEG",
            "image/png" => "PNG",
            "image/gif" => "GIF",
            "image/webp" => "WebP",
            "image/bmp" => "BMP",
            "image/tiff" => "TIFF",
            "image/svg+xml" => "SVG",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Convenience method to resize to a specific width (maintains aspect ratio).
    /// </summary>
    public static Task<ImageResult> ResizeToWidthAsync(
        this IImageResizerService resizerService,
        Stream stream,
        int width,
        int? quality = null,
        CancellationToken ct = default)
    {
        var options = new ResizeOptions(Width: width, Height: null, Quality: quality);
        return resizerService.ResizeAsync(stream, null, options, ct);
    }

    /// <summary>
    /// Convenience method to resize to a specific height (maintains aspect ratio).
    /// </summary>
    public static Task<ImageResult> ResizeToHeightAsync(
        this IImageResizerService resizerService,
        Stream stream,
        int height,
        int? quality = null,
        CancellationToken ct = default)
    {
        var options = new ResizeOptions(Width: null, Height: height, Quality: quality);
        return resizerService.ResizeAsync(stream, null, options, ct);
    }

    /// <summary>
    /// Convenience method to resize to fit within specific dimensions (maintains aspect ratio).
    /// </summary>
    public static Task<ImageResult> ResizeToFitAsync(
        this IImageResizerService resizerService,
        Stream stream,
        int width,
        int height,
        int? quality = null,
        CancellationToken ct = default)
    {
        var options = new ResizeOptions(Width: width, Height: height, Quality: quality);
        return resizerService.ResizeAsync(stream, null, options, ct);
    }

    /// <summary>
    /// Convenience method to create a thumbnail (common 300x300 square).
    /// </summary>
    public static Task<ImageResult> CreateThumbnailAsync(
        this IImageResizerService resizerService,
        Stream stream,
        int size = 300,
        int? quality = 85,
        CancellationToken ct = default)
    {
        var options = new ResizeOptions(Width: size, Height: size, Quality: quality);
        return resizerService.ResizeAsync(stream, null, options, ct);
    }
}
