using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ImageResize.Core.Codecs;
using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Services;
using ImageResize.ContextMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageResize.ContextMenu;

public partial class App : Application
{
    // TODO (post-POC): restore single-instance mutex + named-pipe IPC for multi-select
    // context-menu invocations. Stripped for the POC so the Avalonia port can be validated
    // end-to-end without the platform-IPC distraction. Previous implementation in
    // git history on master (pre-avalonia-port).

    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.Configure<ImageResizeOptions>(options =>
        {
            options.DefaultQuality = 99;
            options.PngCompressionLevel = 6;
            options.AllowUpscale = false;
            options.Backend = ImageBackend.SkiaSharp;
            options.WebRoot = string.Empty;
        });

        services.AddSingleton<IImageCodec, SkiaCodec>();
        services.AddSingleton<IImageResizerService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ImageResizeOptions>>();
            var codec = sp.GetRequiredService<IImageCodec>();
            var logger = sp.GetRequiredService<ILogger<ImageResizerService>>();
            var cache = new NullImageCache();
            return new ImageResizerService(options, cache, codec, logger);
        });

        services.AddSingleton<ImageProcessor>();
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();
    }
}

internal sealed class NullImageCache : IImageCache
{
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public string GetCachedFilePath(string relativePath, ImageResize.Models.ResizeOptions resizeOptions, string sourceSignature) => string.Empty;
    public Task WriteAtomicallyAsync(string path, Stream data, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) => Task.FromResult(Stream.Null);
}
