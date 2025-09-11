# ImageResize

A minimal, cross-platform image resize middleware for .NET Core that provides a drop-in replacement for some common ImageSharp functionality. Built with SkiaSharp for fast, reliable image processing across Windows, Linux, and macOS.

[![NuGet](https://img.shields.io/nuget/v/VibedMediatr.svg)](https://www.nuget.org/packages/ImageResize/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)


**Important:** Make no mistake, this is not a full replacement for ImageSharp and I'm sure it's faster/better than this. This is a 'vibe' coded project I did with cursor to test the new grok code fast model, and I haven't tested it extensively. I have only tested it my own projects and the Example project within this solution - So any help or bugs, please free to do a PR.

## Features

- **Querystring-based resizing**: `?width=800&height=600&quality=80`
- **Aspect ratio preservation**: Always fits within specified dimensions
- **Multiple formats**: JPEG, PNG, WebP, GIF (first frame), BMP, TIFF (first page)
- **Disk caching**: Atomic writes with configurable sharding and size management
- **HTTP caching**: ETags, Last-Modified, Cache-Control headers
- **Concurrency safe**: Prevents thundering herd with keyed locks
- **Security**: Path traversal protection and bounds validation
- **Backend support**: Extensible codec architecture (SkiaSharp, future backends)
- **ImageSharp Compatibility Layer**: Drop-in replacement for common ImageSharp operations
- **OSS-friendly**: MIT licensed with no commercial restrictions

## Installation

```bash
dotnet add package ImageResize
```

## Quick Start

### Program.cs
```csharp
using ImageResize.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Simple setup with automatic defaults
builder.Services.AddImageResize(builder.Environment);

var app = builder.Build();

app.UseImageResize(); // Before UseStaticFiles and before routing app.UseRouting() etc...
app.UseStaticFiles();

app.Run();
```

### Advanced Configuration (Optional)
```csharp
builder.Services.AddImageResize(o =>
{
    o.RequestPathPrefix = "/media";
    o.ContentRoot = Path.Combine(builder.Environment.WebRootPath, "images");
    o.CacheRoot = Path.Combine(builder.Environment.WebRootPath, "_imgcache");
    o.AllowUpscale = false;
    o.DefaultQuality = 85;
    o.PngCompressionLevel = 6;
    o.Backend = ImageBackend.SkiaSharp;
    o.Cache.MaxCacheBytes = 1073741824; // 1GB cache limit
    o.Cache.PruneOnStartup = true;
});
```

### Usage Examples

```bash
# Resize to 800px width (preserves aspect ratio)
GET /media/photos/cat.jpg?width=800

# Fit within 800x600 box
GET /media/photos/cat.jpg?width=800&height=600

# Resize with quality control
GET /media/photos/cat.jpg?height=1080&quality=85
```

## Configuration

### appsettings.json
```json
{
  "ImageResize": {
    "EnableMiddleware": true,
    "RequestPathPrefix": "/media",
    "ContentRoot": "wwwroot/images",
    "CacheRoot": "wwwroot/_imgcache",
    "AllowUpscale": false,
    "DefaultQuality": 80,
    "PngCompressionLevel": 6,
    "Bounds": {
      "MinWidth": 16, "MaxWidth": 4096,
      "MinHeight": 16, "MaxHeight": 4096,
      "MinQuality": 10, "MaxQuality": 95
    },
    "HashOriginalContent": false,
    "Cache": {
      "FolderSharding": 2,
      "PruneOnStartup": false,
      "MaxCacheBytes": 0
    },
    "ResponseCache": {
      "ClientCacheSeconds": 604800,
      "SendETag": true,
      "SendLastModified": true
    },
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff"],
    "Backend": "SkiaSharp"
  }
}
```

## Programmatic Usage

```csharp
// Inject the service
public class MyController : ControllerBase
{
    private readonly IImageResizerService _resizer;

    public MyController(IImageResizerService resizer)
    {
        _resizer = resizer;
    }

    [HttpGet("thumbnail/{id}")]
    public async Task<IActionResult> GetThumbnail(int id)
    {
        var result = await _resizer.EnsureResizedAsync(
            $"photos/{id}.jpg",
            new ResizeOptions(Width: 300, Height: 300, Quality: 80)
        );

        return PhysicalFile(result.CachedPath, result.ContentType);
    }
}
```

### Enhanced ImageResult Properties

The `ImageResult` class provides all the properties you need:

```csharp
using var image = await stream.LoadAsync(resizerService);

// Basic properties (ImageSharp compatible)
int width = image.Width;
int height = image.Height;
string contentType = image.ContentType;

// Enhanced metadata
long fileSize = image.FileSize;                    // File size in bytes
string fileSizeHuman = image.FileSizeHumanReadable; // "1.2 MB"
string format = image.Format;                      // "JPEG", "PNG", etc.
string extension = image.FileExtension;            // ".jpg", ".png", etc.

// Original dimensions
int originalWidth = image.OriginalWidth;
int originalHeight = image.OriginalHeight;

// Processing information
bool wasResized = image.WasResized;
bool isProcessed = image.IsProcessed;
int? quality = image.Quality;

// Computed properties
double aspectRatio = image.AspectRatio;            // Width/Height ratio
bool isLandscape = image.IsLandscape;
bool isPortrait = image.IsPortrait;
bool isSquare = image.IsSquare;
long pixelCount = image.PixelCount;               // Total pixels
```

### Usage Examples

First, get a stream from your image source:

```csharp
// From a file path
using var fileStream = File.OpenRead("path/to/image.jpg");

// From an HTTP file upload (ASP.NET Core)
public async Task<IActionResult> UploadImage(IFormFile uploadedFile)
{
    await using var stream = uploadedFile.OpenReadStream();
    // ... process stream
}

// From a byte array
var imageBytes = await File.ReadAllBytesAsync("path/to/image.jpg");
using var memoryStream = new MemoryStream(imageBytes);

// From a URL
using var httpClient = new HttpClient();
using var response = await httpClient.GetAsync("https://example.com/image.jpg");
using var urlStream = await response.Content.ReadAsStreamAsync();
```

Then process the stream:

```csharp
// Simple resize with one method call
var options = new ResizeOptions(Width: 1920, Height: 1080, Quality: 90);
using var resizedImage = await resizerService.ResizeAsync(stream, null, options);

// Access all metadata
Console.WriteLine($"Resized: {resizedImage.Width}x{resizedImage.Height} " +
                 $"({resizedImage.FileSizeHumanReadable}) " +
                 $"{resizedImage.Format} format");

// Save the result
await resizedImage.SaveAsync(filePath);
```

### Advanced Usage with Custom Quality

```csharp
// Process with custom quality settings
var options = new ResizeOptions(Width: 1920, Height: 1080, Quality: 90);
using var imageResult = await resizerService.ResizeAsync(originalStream, null, options);

// Access computed properties
bool wasResized = imageResult.WasResized;        // true
double aspectRatio = imageResult.AspectRatio;     // 1.6
string sizeDisplay = imageResult.FileSizeHumanReadable; // "245 KB"
bool isLandscape = imageResult.IsLandscape;
long pixelCount = imageResult.PixelCount;
```

### Migration from ImageSharp

```csharp
// OLD (ImageSharp)
using var image = await Image.LoadAsync(stream);
await image.SaveAsync(filePath);
media.Width = image.Width;
media.Height = image.Height;

// NEW (ImageResize) - Simple one-call approach
var options = new ResizeOptions(Width: 1920, Height: 1080, Quality: 85);
using var resizedImage = await resizerService.ResizeAsync(stream, null, options);
await resizedImage.SaveAsync(filePath);

// Access all the metadata you need
media.Width = resizedImage.Width;
media.Height = resizedImage.Height;
media.FileSize = resizedImage.FileSize;
media.ContentType = resizedImage.ContentType;
media.Format = resizedImage.Format;
media.AspectRatio = resizedImage.AspectRatio;
media.WasResized = resizedImage.WasResized;
```

### Convenience Methods

For common resize operations, use these simple extension methods:

```csharp
// Resize to specific width (maintains aspect ratio)
using var resized = await resizerService.ResizeToWidthAsync(stream, 800);

// Resize to specific height (maintains aspect ratio)
using var resized = await resizerService.ResizeToHeightAsync(stream, 600);

// Resize to fit within dimensions (maintains aspect ratio)
using var resized = await resizerService.ResizeToFitAsync(stream, 1920, 1080);

// Create thumbnail (default 300x300)
using var thumbnail = await resizerService.CreateThumbnailAsync(stream);

// Create custom size thumbnail
using var thumbnail = await resizerService.CreateThumbnailAsync(stream, 150);
```

## Cache Design

- **Key generation**: SHA1 hash of normalized path + options + source signature
- **Source signature**: Last modified time + file size (+ optional content hash)
- **Atomic writes**: Temp file → rename for consistency
- **Folder sharding**: Configurable subfolder splitting (e.g., `ab/cd/hash.ext`)
- **Size management**: Automatic cleanup when `MaxCacheBytes` exceeded
- **Startup pruning**: Optional cleanup of old files on application startup

## Supported Formats

| Format | Read | Write | Notes |
|--------|------|-------|-------|
| JPEG   | ✅   | ✅    | Quality 1-100 |
| PNG    | ✅   | ✅    | Compression level 0-9 (configurable) |
| WebP   | ✅   | ✅    | Quality 1-100 |
| GIF    | ✅   | ✅    | First frame only |
| BMP    | ✅   | ✅    | |
| TIFF   | ✅   | ✅    | First page only |

## Backend Support

Currently supports SkiaSharp backend with framework for additional backends:

- **SkiaSharp**: Cross-platform, high-performance (default)
- **SystemDrawing**: Windows-only, .NET Framework compatible (planned)
- **MagickNet**: ImageMagick integration (planned)

Configure via `Backend` setting in appsettings.json.

## Performance

- **Memory efficient**: Streams data without loading entire images
- **Concurrent safe**: Keyed locks prevent duplicate processing
- **HTTP optimized**: Conditional requests (304) and client caching
- **Configurable quality**: Balance file size vs. visual quality
- **Smart caching**: Automatic cache size management and startup pruning
- **Backend flexibility**: Choose optimal codec for your platform


## Security

- Path traversal prevention
- Configurable size bounds
- Input validation on all parameters
- Safe file operations with atomic writes

## License

MIT

## Contributing

PRs welcome! See the Example app for usage examples and tests for implementation details.
