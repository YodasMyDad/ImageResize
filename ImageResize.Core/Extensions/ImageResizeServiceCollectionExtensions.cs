using ImageResize.Configuration;
using ImageResize.Interfaces;
using ImageResize.Codecs;
using ImageResize.Cache;
using ImageResize.Middleware;
using ImageResize.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ImageResize.Core.Extensions;

/// <summary>
/// Extension methods for registering ImageResize services.
/// </summary>
public static class ImageResizeServiceCollectionExtensions
{
    /// <summary>
    /// Adds ImageResize services to the service collection with default configuration.
    /// </summary>
    public static IServiceCollection AddImageResize(this WebApplicationBuilder builder)
    {
        return builder.Services.AddImageResize(builder.Environment);
    }

    /// <summary>
    /// Adds ImageResize services to the service collection with default configuration.
    /// </summary>
    public static IServiceCollection AddImageResize(this IServiceCollection services, IWebHostEnvironment environment)
    {
        return services.AddImageResize(options =>
        {
            // Set default paths relative to web root
            options.ContentRoot = Path.Combine(environment.WebRootPath ?? "wwwroot", "images");
            options.CacheRoot = Path.Combine(environment.WebRootPath ?? "wwwroot", "_imgcache");
        });
    }

    /// <summary>
    /// Adds ImageResize services to the service collection.
    /// </summary>
    public static IServiceCollection AddImageResize(this IServiceCollection services, Action<ImageResizeOptions>? configure = null)
    {
        var options = new ImageResizeOptions();

        // Apply configuration if provided
        if (configure is not null)
        {
            configure(options);
        }

        // Register options as singleton for direct injection
        services.AddSingleton(options);

        // Register core services
        services.AddSingleton<IImageCache, FileSystemImageCache>();
        services.AddSingleton<IImageCodec, SkiaCodec>();
        services.AddSingleton<IImageResizerService, ImageResizerService>();

        return services;
    }

    /// <summary>
    /// Adds ImageResize middleware to the application pipeline.
    /// </summary>
    public static IApplicationBuilder UseImageResize(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ImageResizeMiddleware>();
    }
}
