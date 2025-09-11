namespace ImageResize.Core.Configuration;

/// <summary>
/// Configuration options for ImageResize middleware.
/// </summary>
public class ImageResizeOptions
{
    /// <summary>
    /// Whether to enable the middleware.
    /// </summary>
    public bool EnableMiddleware { get; set; } = true;

    /// <summary>
    /// URL prefix under which to intercept resize requests.
    /// </summary>
    public string RequestPathPrefix { get; set; } = "/media";

    /// <summary>
    /// Root directory where original images are located.
    /// </summary>
    public string ContentRoot { get; set; } = "wwwroot";

    /// <summary>
    /// Root directory where resized images are cached.
    /// </summary>
    public string CacheRoot { get; set; } = "wwwroot/_imgcache";

    /// <summary>
    /// Whether to allow upscaling beyond original dimensions.
    /// </summary>
    public bool AllowUpscale { get; set; } = false;

    /// <summary>
    /// Default quality when only width/height specified (JPEG/WebP).
    /// </summary>
    public int DefaultQuality { get; set; } = 80;

    /// <summary>
    /// PNG compression level (0-9).
    /// </summary>
    public int PngCompressionLevel { get; set; } = 6;

    /// <summary>
    /// Bounds for width, height, and quality parameters.
    /// </summary>
    public BoundsOptions Bounds { get; set; } = new();

    /// <summary>
    /// Whether to include content hash in cache key.
    /// </summary>
    public bool HashOriginalContent { get; set; } = false;

    /// <summary>
    /// Cache configuration.
    /// </summary>
    public CacheOptions Cache { get; set; } = new();

    /// <summary>
    /// HTTP response cache configuration.
    /// </summary>
    public ResponseCacheOptions ResponseCache { get; set; } = new();

    /// <summary>
    /// Allowed file extensions.
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff"];

    /// <summary>
    /// Backend codec to use.
    /// </summary>
    public ImageBackend Backend { get; set; } = ImageBackend.SkiaSharp;

    /// <summary>
    /// Bounds configuration for validation.
    /// </summary>
    public class BoundsOptions
    {
        public int MinWidth { get; set; } = 16;
        public int MaxWidth { get; set; } = 4096;
        public int MinHeight { get; set; } = 16;
        public int MaxHeight { get; set; } = 4096;
        public int MinQuality { get; set; } = 10;
        public int MaxQuality { get; set; } = 95;
    }

    /// <summary>
    /// Cache-specific configuration.
    /// </summary>
    public class CacheOptions
    {
        public int FolderSharding { get; set; } = 2;
        public bool PruneOnStartup { get; set; } = false;
        public long MaxCacheBytes { get; set; } = 0; // 0 = unlimited
    }

    /// <summary>
    /// HTTP response cache configuration.
    /// </summary>
    public class ResponseCacheOptions
    {
        public int ClientCacheSeconds { get; set; } = 604800; // 7 days
        public bool SendETag { get; set; } = true;
        public bool SendLastModified { get; set; } = true;
    }
}

/// <summary>
/// Supported image backends.
/// </summary>
public enum ImageBackend
{
    SkiaSharp,
    MagickNet,
    SystemDrawing
}
