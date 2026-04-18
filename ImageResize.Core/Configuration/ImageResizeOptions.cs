namespace ImageResize.Core.Configuration;

/// <summary>
/// Configuration options for ImageResize middleware.
/// </summary>
public class ImageResizeOptions
{
    /// <summary>
    /// Whether to enable the middleware. Defaults to <c>true</c>.
    /// </summary>
    public bool EnableMiddleware { get; set; } = true;

    /// <summary>
    /// URL path prefixes to monitor for image resize requests.
    /// Images will be served from their actual paths (e.g., /img/photo.jpg, /images/banner.png).
    /// </summary>
    public List<string> ContentRoots { get; set; } = ["img", "images", "media"];

    /// <summary>
    /// Root directory where original images are located (typically wwwroot).
    /// </summary>
    public string WebRoot { get; set; } = "wwwroot";

    /// <summary>
    /// Root directory where resized images are cached.
    /// </summary>
    public string CacheRoot { get; set; } = "wwwroot/_imgcache";

    /// <summary>
    /// Whether to allow upscaling beyond original dimensions. Defaults to <c>false</c>.
    /// </summary>
    public bool AllowUpscale { get; set; }

    /// <summary>
    /// Default quality when only width/height specified (JPEG/WebP). Range 1-100.
    /// </summary>
    public int DefaultQuality { get; set; } = 99;

    /// <summary>
    /// PNG compression level (0-9).
    /// </summary>
    public int PngCompressionLevel { get; set; } = 6;

    /// <summary>
    /// Maximum permitted size of the source image stream in bytes. The decoder will refuse to
    /// load inputs larger than this, which mitigates decompression-bomb attacks. Defaults to
    /// 256 MiB. Values &lt;= 0 disable the check (not recommended in production).
    /// </summary>
    public long MaxSourceBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// Bounds for width, height, and quality parameters.
    /// </summary>
    public BoundsOptions Bounds { get; set; } = new();

    /// <summary>
    /// Whether to include content hash in cache key.
    /// </summary>
    public bool HashOriginalContent { get; set; }

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
        /// <summary>Minimum accepted width in pixels.</summary>
        public int MinWidth { get; set; } = 16;
        /// <summary>Maximum accepted width in pixels.</summary>
        public int MaxWidth { get; set; } = 4096;
        /// <summary>Minimum accepted height in pixels.</summary>
        public int MinHeight { get; set; } = 16;
        /// <summary>Maximum accepted height in pixels.</summary>
        public int MaxHeight { get; set; } = 4096;
        /// <summary>Minimum accepted quality value.</summary>
        public int MinQuality { get; set; } = 10;
        /// <summary>Maximum accepted quality value.</summary>
        public int MaxQuality { get; set; } = 100;
    }

    /// <summary>
    /// Cache-specific configuration.
    /// </summary>
    public class CacheOptions
    {
        /// <summary>Number of two-character directory levels to use for sharding the cache (default 2).</summary>
        public int FolderSharding { get; set; } = 2;
        /// <summary>If true, prunes a batch of oldest cache files on startup.</summary>
        public bool PruneOnStartup { get; set; }
        /// <summary>Soft cap on total cache size in bytes. 0 means unlimited.</summary>
        public long MaxCacheBytes { get; set; }
    }

    /// <summary>
    /// HTTP response cache configuration.
    /// </summary>
    public class ResponseCacheOptions
    {
        /// <summary>Value emitted in the <c>Cache-Control: max-age</c> header (default 7 days).</summary>
        public int ClientCacheSeconds { get; set; } = 604800;
        /// <summary>Whether to emit an <c>ETag</c> header.</summary>
        public bool SendETag { get; set; } = true;
        /// <summary>Whether to emit a <c>Last-Modified</c> header.</summary>
        public bool SendLastModified { get; set; } = true;
    }
}

/// <summary>
/// Supported image backends.
/// </summary>
public enum ImageBackend
{
    /// <summary>SkiaSharp-backed codec (default, cross-platform).</summary>
    SkiaSharp,
    /// <summary>Reserved for a future Magick.NET backend.</summary>
    MagickNet,
    /// <summary>Reserved for a future System.Drawing backend.</summary>
    SystemDrawing
}
