# ImageResize

A minimal, cross-platform image resize middleware for .NET Core that provides a drop-in replacement for some common ImageSharp functionality. Built with SkiaSharp for fast, reliable image processing across Windows, Linux, and macOS.

**Important:** Make no mistake, this is not a full replacement for ImageSharp and I'm sure it's faster/better than this. This is a 'vibe' coded project I did with cursor to test the new grok code fast model, and I haven't tested it extensively. I have only tested it my own projects and the Example project within this solution - So any help or bugs, please free to do a PR.

## Features

- **Querystring-based resizing**: `?width=800&height=600&quality=80`
- **Aspect ratio preservation**: Always fits within specified dimensions
- **Multiple formats**: JPEG, PNG, WebP, GIF (first frame), BMP, TIFF (first page)
- **Disk caching**: Atomic writes with configurable sharding
- **HTTP caching**: ETags, Last-Modified, Cache-Control headers
- **Concurrency safe**: Prevents thundering herd with keyed locks
- **Security**: Path traversal protection and bounds validation
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
    "ContentRoot": "wwwroot",
    "CacheRoot": "wwwroot/_imgcache",
    "AllowUpscale": false,
    "DefaultQuality": 80,
    "Bounds": {
      "MinWidth": 16, "MaxWidth": 4096,
      "MinHeight": 16, "MaxHeight": 4096,
      "MinQuality": 10, "MaxQuality": 95
    },
    "Cache": {
      "FolderSharding": 2
    },
    "ResponseCache": {
      "ClientCacheSeconds": 604800
    }
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

## Cache Design

- **Key generation**: SHA1 hash of normalized path + options + source signature
- **Source signature**: Last modified time + file size (+ optional content hash)
- **Atomic writes**: Temp file → rename for consistency
- **Folder sharding**: Configurable subfolder splitting (e.g., `ab/cd/hash.ext`)

## Supported Formats

| Format | Read | Write | Notes |
|--------|------|-------|-------|
| JPEG   | ✅   | ✅    | Quality 1-100 |
| PNG    | ✅   | ✅    | Compression level mapping |
| WebP   | ✅   | ✅    | Quality 1-100 |
| GIF    | ✅   | ✅    | First frame only |
| BMP    | ✅   | ✅    | |
| TIFF   | ✅   | ✅    | First page only |

## Performance

- **Memory efficient**: Streams data without loading entire images
- **Concurrent safe**: Keyed locks prevent duplicate processing
- **HTTP optimized**: Conditional requests (304) and client caching
- **Configurable quality**: Balance file size vs. visual quality


## Security

- Path traversal prevention
- Configurable size bounds
- Input validation on all parameters
- Safe file operations with atomic writes

## License

MIT

## Contributing

PRs welcome! See the Example app for usage examples and tests for implementation details.
