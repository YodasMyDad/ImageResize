using ImageResize.Core.Cache;
using ImageResize.Core.Codecs;
using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Middleware;
using ImageResize.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        // Configure options from appsettings
        builder.Services.Configure<ImageResizeOptions>(
            builder.Configuration.GetSection("ImageResize"));

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
            options.WebRoot = environment.WebRootPath ?? "wwwroot";
            options.CacheRoot = Path.Combine(environment.WebRootPath ?? "wwwroot", "_imgcache");
        });
    }

    /// <summary>
    /// Adds ImageResize services to the service collection.
    /// </summary>
    public static IServiceCollection AddImageResize(this IServiceCollection services, Action<ImageResizeOptions>? configure = null)
    {
        // Register core services - options will be resolved from DI with configuration binding
        services.AddSingleton<IImageCache, FileSystemImageCache>();
        services.AddSingleton<IImageCodec>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ImageResizeOptions>>();
            var logger = sp.GetRequiredService<ILogger<SkiaCodec>>();
            return options.Value.Backend switch
            {
                ImageBackend.SkiaSharp => new SkiaCodec(options, logger),
                ImageBackend.MagickNet => throw new NotImplementedException("MagickNet backend is not yet implemented"),
                ImageBackend.SystemDrawing => throw new NotImplementedException("SystemDrawing backend is not yet implemented"),
                _ => new SkiaCodec(options, logger)
            };
        });
        services.AddSingleton<IImageResizerService, ImageResizerService>();

        // Add startup cache pruning service
        services.AddHostedService<CachePruningHostedService>();

        // Apply additional configuration if provided
        if (configure is not null)
        {
            services.Configure(configure);
        }

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

/// <summary>
/// Hosted service that performs cache pruning on application startup.
/// </summary>
internal sealed class CachePruningHostedService(IImageCache cache) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (cache is FileSystemImageCache fsCache)
        {
            await Task.Run(() => fsCache.PruneCacheOnStartup(), cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
