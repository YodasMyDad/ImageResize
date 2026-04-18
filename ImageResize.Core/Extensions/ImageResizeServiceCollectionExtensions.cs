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
    /// Adds ImageResize services bound to the <c>ImageResize</c> configuration section.
    /// </summary>
    public static IServiceCollection AddImageResize(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ImageResizeOptions>(
            builder.Configuration.GetSection("ImageResize"));

        return builder.Services.AddImageResize(builder.Environment);
    }

    /// <summary>
    /// Adds ImageResize services, defaulting <see cref="ImageResizeOptions.WebRoot"/> and
    /// <see cref="ImageResizeOptions.CacheRoot"/> from the host environment.
    /// </summary>
    public static IServiceCollection AddImageResize(this IServiceCollection services, IWebHostEnvironment environment)
        => services.AddImageResize(options =>
        {
            options.WebRoot = environment.WebRootPath ?? "wwwroot";
            options.CacheRoot = Path.Combine(environment.WebRootPath ?? "wwwroot", "_imgcache");
        });

    /// <summary>
    /// Adds ImageResize services with an optional configuration callback.
    /// </summary>
    public static IServiceCollection AddImageResize(this IServiceCollection services, Action<ImageResizeOptions>? configure = null)
    {
        services.AddOptions<ImageResizeOptions>()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ImageResizeOptions>, ImageResizeOptionsValidator>();

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

        services.AddHostedService<CachePruningHostedService>();

        if (configure is not null)
            services.Configure(configure);

        return services;
    }

    /// <summary>
    /// Adds ImageResize middleware to the application pipeline.
    /// </summary>
    public static IApplicationBuilder UseImageResize(this IApplicationBuilder app)
        => app.UseMiddleware<ImageResizeMiddleware>();
}

/// <summary>
/// Hosted service that sweeps orphaned <c>.tmp</c> files and (optionally) prunes the cache on
/// application startup.
/// </summary>
internal sealed class CachePruningHostedService(IImageCache cache) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cache is FileSystemImageCache fsCache)
            return Task.Run(() => fsCache.PruneCacheOnStartup(), cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
