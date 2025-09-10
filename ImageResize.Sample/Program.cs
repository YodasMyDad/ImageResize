using ImageResize.Abstractions.Interfaces;
using ImageResize.Abstractions.Models;
using ImageResize.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddImageResize(o =>
{
    o.RequestPathPrefix = "/media";
    o.ContentRoot = Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "images");
    o.CacheRoot = Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "_imgcache");
    o.Bounds.MaxWidth = 4096;
    o.Bounds.MaxHeight = 4096;
    o.Bounds.MaxQuality = 95;
});

var app = builder.Build();

// Use middleware BEFORE static files to handle resize requests
app.UseImageResize();
app.UseStaticFiles();

// Demo endpoint showing programmatic usage
app.MapGet("/demo", async (IImageResizerService svc) =>
{
    try
    {
        var result = await svc.EnsureResizedAsync(
            "sample.jpg", // This would be in wwwroot/images/
            new ResizeOptions(Width: 800, Height: 600, Quality: 80)
        );

        return Results.File(result.CachedPath, result.ContentType);
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error: {ex.Message}");
    }
});

// Info endpoint
app.MapGet("/info", (IImageResizerService svc) =>
{
    return Results.Json(new
    {
        Message = "ImageResize middleware is active",
        Endpoints = new[]
        {
            "/media/*?width=800&height=600&quality=80",
            "/demo - programmatic usage example"
        }
    });
});

app.Run();
