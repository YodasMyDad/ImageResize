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
    /// Extension method to check if image exceeds max dimension and resize if needed.
    /// This overload accepts a maximum width OR height - the image will be resized to fit within this dimension while maintaining aspect ratio.
    /// </summary>
    /// <param name="imageStream">The image stream to check and resize</param>
    /// <param name="maxDimension">Maximum width OR height in pixels (whichever is larger will be constrained to this value)</param>
    /// <param name="resizerService">The resizer service to use</param>
    /// <param name="contentType">Optional content type of the image</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task<ImageResult> OverMaxSizeCheckAsync(
        this Stream imageStream,
        int maxDimension,
        IImageResizerService resizerService,
        string? contentType = null,
        CancellationToken ct = default)
    {
        // Handle non-seekable streams (like BrowserFileStream) by buffering to MemoryStream
        Stream workingStream = imageStream;
        bool isBufferedStream = false;

        if (!imageStream.CanSeek)
        {
            var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            workingStream = memoryStream;
            isBufferedStream = true;
        }
        else
        {
            workingStream.Position = 0;
        }

        try
        {
            // First, probe the image to get original dimensions
            var codec = resizerService.GetCodec();
            var (width, height, detectedContentType) = await codec.ProbeAsync(workingStream, ct);

            // Reset stream position after probing (only if seekable)
            if (workingStream.CanSeek)
            {
                workingStream.Position = 0;
            }

            // Check if resizing is needed - if both dimensions are within max, no resize needed
            var maxCurrentDimension = Math.Max(width, height);
            if (maxCurrentDimension <= maxDimension)
            {
                // No resize needed, return the stream
                var streamLength = workingStream.Length;
                var detectedType = detectedContentType ?? contentType ?? "application/octet-stream";
                var fileExt = GetFileExtensionFromContentType(detectedType);
                var imgFormat = GetFormatFromContentType(detectedType);

                var resultStream = isBufferedStream ? workingStream : imageStream;
                if (!isBufferedStream && imageStream.CanSeek)
                {
                    imageStream.Position = 0;
                }

                return new ImageResult(resultStream, width, height, detectedType,
                                       streamLength, fileExt, imgFormat, width, height, null, false);
            }

            // Calculate scale factor based on the larger dimension
            var scaleFactor = (double)maxDimension / maxCurrentDimension;
            var newWidth = (int)Math.Round(width * scaleFactor);
            var newHeight = (int)Math.Round(height * scaleFactor);

            // Ensure we don't exceed maxDimension due to rounding
            if (newWidth > maxDimension)
                newWidth = maxDimension;
            if (newHeight > maxDimension)
                newHeight = maxDimension;

            // Ensure dimensions are at least 1 pixel
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);

            // Resize the image
            var options = new ResizeOptions(Width: newWidth, Height: newHeight, Quality: null);
            var (resizedStream, resultContentType, resultWidth, resultHeight) =
                await resizerService.ResizeToStreamAsync(workingStream, contentType, options, ct);

            var resizedLength = resizedStream.Length;
            var resultExt = GetFileExtensionFromContentType(resultContentType);
            var resultFormat = GetFormatFromContentType(resultContentType);

            return new ImageResult(resizedStream, resultWidth, resultHeight, resultContentType,
                                   resizedLength, resultExt, resultFormat, width, height, options.Quality, true);
        }
        finally
        {
            // Clean up buffered stream if we created one and it's not being returned
            if (isBufferedStream && workingStream != null)
            {
                // Don't dispose here - it will be disposed by the caller via ImageResult
            }
        }
    }

    /// <summary>
    /// Extension method to check if image exceeds max total pixel count and resize if needed.
    /// This overload accepts total pixels (width × height).
    /// </summary>
    /// <param name="imageStream">The image stream to check and resize</param>
    /// <param name="maxPixelCount">Maximum total pixel count (width × height)</param>
    /// <param name="resizerService">The resizer service to use</param>
    /// <param name="contentType">Optional content type of the image</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task<ImageResult> OverMaxSizeCheckByPixelCountAsync(
        this Stream imageStream,
        long maxPixelCount,
        IImageResizerService resizerService,
        string? contentType = null,
        CancellationToken ct = default)
    {
        // Handle non-seekable streams (like BrowserFileStream) by buffering to MemoryStream
        Stream workingStream = imageStream;
        bool isBufferedStream = false;

        if (!imageStream.CanSeek)
        {
            // Buffer the entire stream for non-seekable streams
            var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            workingStream = memoryStream;
            isBufferedStream = true;
        }
        else
        {
            // Reset seekable stream to beginning
            workingStream.Position = 0;
        }

        try
        {
            // First, probe the image to get original dimensions
            var codec = resizerService.GetCodec();
            var (width, height, detectedContentType) = await codec.ProbeAsync(workingStream, ct);

            // Reset stream position after probing (only if seekable)
            if (workingStream.CanSeek)
            {
                workingStream.Position = 0;
            }

            // Check if resizing is needed based on total pixel count
            var totalPixels = (long)width * height;
            if (totalPixels <= maxPixelCount)
            {
                // No resize needed, return the stream
                var streamLength = workingStream.Length;
                var detectedType = detectedContentType ?? contentType ?? "application/octet-stream";
                var fileExt = GetFileExtensionFromContentType(detectedType);
                var imgFormat = GetFormatFromContentType(detectedType);

                // If we buffered the stream, return the buffered version
                // Otherwise return the original stream (position already reset if possible)
                var resultStream = isBufferedStream ? workingStream : imageStream;
                if (!isBufferedStream && imageStream.CanSeek)
                {
                    imageStream.Position = 0; // Reset original stream if possible
                }

                return new ImageResult(resultStream, width, height, detectedType,
                                       streamLength, fileExt, imgFormat, width, height, null, false);
            }

            // Determine resize dimensions to fit within max pixel count while maintaining aspect ratio
            // Calculate the scale factor needed to reduce total pixels to maxPixelCount
            var scaleFactor = Math.Sqrt((double)maxPixelCount / totalPixels);
            var newWidth = (int)Math.Round(width * scaleFactor);
            var newHeight = (int)Math.Round(height * scaleFactor);
            
            // Due to rounding, we might exceed maxPixelCount - adjust if needed
            while (newWidth * newHeight > maxPixelCount && (newWidth > 1 || newHeight > 1))
            {
                // Reduce the larger dimension by 1
                if (newWidth >= newHeight && newWidth > 1)
                    newWidth--;
                else if (newHeight > 1)
                    newHeight--;
                else
                    break;
            }
            
            // Ensure dimensions are at least 1 pixel
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);

            // Resize the image
            var options = new ResizeOptions(Width: newWidth, Height: newHeight, Quality: null);
            var (resizedStream, resultContentType, resultWidth, resultHeight) =
                await resizerService.ResizeToStreamAsync(workingStream, contentType, options, ct);

            var resizedLength = resizedStream.Length;
            var resultExt = GetFileExtensionFromContentType(resultContentType);
            var resultFormat = GetFormatFromContentType(resultContentType);

            return new ImageResult(resizedStream, resultWidth, resultHeight, resultContentType,
                                   resizedLength, resultExt, resultFormat, width, height, options.Quality, true);
        }
        finally
        {
            // Clean up buffered stream if we created one and it's not being returned
            if (isBufferedStream && workingStream != null)
            {
                // Don't dispose here - it will be disposed by the caller via ImageResult
            }
        }
    }

    /// <summary>
    /// Saves the image stream to a file asynchronously.
    /// Equivalent to ImageSharp's SaveAsync method.
    /// </summary>
    public static async Task SaveAsync(this ImageResult imageResult, string filePath, CancellationToken ct = default)
    {
        await using var fileStream = File.Create(filePath);
        if (imageResult.Stream.CanSeek)
        {
            imageResult.Stream.Position = 0;
        }
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

        // Reset stream position after probing (only if seekable)
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

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
