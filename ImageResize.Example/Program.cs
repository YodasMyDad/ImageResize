using ImageResize.Abstractions.Interfaces;
using ImageResize.Abstractions.Models;
using ImageResize.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services - now with automatic defaults!
builder.Services.AddImageResize(builder.Environment);

// Add services to the container.
builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Image resize middleware must be before routing to intercept requests
app.UseImageResize();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

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