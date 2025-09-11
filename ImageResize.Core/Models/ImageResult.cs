namespace ImageResize.Core.Models;

/// <summary>
/// Enhanced result of image loading/processing operations.
/// Equivalent to ImageSharp's Image class with comprehensive metadata.
/// </summary>
public sealed class ImageResult : IDisposable
{
    /// <summary>
    /// The image data stream.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// Current width of the image in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Current height of the image in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// MIME content type of the image.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Size of the image file in bytes.
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    /// File extension derived from content type.
    /// </summary>
    public string FileExtension { get; }

    /// <summary>
    /// Image format (JPEG, PNG, GIF, etc.).
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Original width before any processing.
    /// </summary>
    public int OriginalWidth { get; }

    /// <summary>
    /// Original height before any processing.
    /// </summary>
    public int OriginalHeight { get; }

    /// <summary>
    /// Quality setting used for compression (if applicable).
    /// </summary>
    public int? Quality { get; }

    /// <summary>
    /// Whether the image has been processed (resized, compressed, etc.).
    /// </summary>
    public bool IsProcessed { get; }

    /// <summary>
    /// Aspect ratio of the current image.
    /// </summary>
    public double AspectRatio => Width > 0 && Height > 0 ? (double)Width / Height : 0;

    /// <summary>
    /// Whether the image is wider than it is tall.
    /// </summary>
    public bool IsLandscape => Width > Height;

    /// <summary>
    /// Whether the image is taller than it is wide.
    /// </summary>
    public bool IsPortrait => Height > Width;

    /// <summary>
    /// Whether the image is square.
    /// </summary>
    public bool IsSquare => Width == Height;

    /// <summary>
    /// File size in human-readable format (e.g., "1.2 MB").
    /// </summary>
    public string FileSizeHumanReadable => GetHumanReadableFileSize(FileSize);

    /// <summary>
    /// Pixel count of the current image.
    /// </summary>
    public long PixelCount => (long)Width * Height;

    /// <summary>
    /// Whether the image dimensions have changed from original.
    /// </summary>
    public bool WasResized => Width != OriginalWidth || Height != OriginalHeight;

    /// <summary>
    /// Creates an ImageResult with basic properties (backward compatibility).
    /// </summary>
    public ImageResult(Stream stream, int width, int height, string contentType)
        : this(stream, width, height, contentType, 0, GetFileExtensionFromContentType(contentType),
               GetFormatFromContentType(contentType), width, height, null, false)
    {
    }

    /// <summary>
    /// Creates an ImageResult with full metadata.
    /// </summary>
    public ImageResult(
        Stream stream,
        int width,
        int height,
        string contentType,
        long fileSize,
        string fileExtension,
        string format,
        int originalWidth,
        int originalHeight,
        int? quality,
        bool isProcessed)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Width = width;
        Height = height;
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        FileSize = fileSize;
        FileExtension = fileExtension ?? throw new ArgumentNullException(nameof(fileExtension));
        Format = format ?? throw new ArgumentNullException(nameof(format));
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
        Quality = quality;
        IsProcessed = isProcessed;
    }

    /// <summary>
    /// Creates a copy of the ImageResult with a new stream.
    /// Useful when you need to reuse the image data multiple times.
    /// </summary>
    public async Task<ImageResult> CloneAsync(CancellationToken ct = default)
    {
        var clonedStream = new MemoryStream();
        Stream.Position = 0;
        await Stream.CopyToAsync(clonedStream, ct);
        clonedStream.Position = 0;

        return new ImageResult(
            clonedStream,
            Width,
            Height,
            ContentType,
            FileSize,
            FileExtension,
            Format,
            OriginalWidth,
            OriginalHeight,
            Quality,
            IsProcessed);
    }

    public void Dispose()
    {
        Stream?.Dispose();
    }

    private static string GetHumanReadableFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
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
}