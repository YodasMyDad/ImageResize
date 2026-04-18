using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ImageResize.Core.Codecs;
using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Services;
using ImageResize.ContextMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace ImageResize.ContextMenu;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private ISingleInstance? _singleInstance;
    private CancellationTokenSource? _ipcCts;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureServices();
        RegisterExceptionHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _singleInstance = SingleInstance.Create();

            if (!_singleInstance.TryAcquire())
            {
                HandleSecondaryInstance(desktop);
            }
            else
            {
                HandlePrimaryInstance(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void HandleSecondaryInstance(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        AppLog.LogStartupArgs("Secondary instance forwarding", args);

        try
        {
            _singleInstance!.ForwardArgs(args);
        }
        finally
        {
            _singleInstance!.Dispose();
            _singleInstance = null;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(() => desktop.TryShutdown(0));
    }

    private void HandlePrimaryInstance(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        AppLog.LogStartupArgs("Primary instance", args);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        desktop.MainWindow = mainWindow;

        _ipcCts = new CancellationTokenSource();
        _singleInstance!.StartServer(OnArgsReceivedFromSecondary, _ipcCts.Token);

        desktop.Exit += OnDesktopExit;
    }

    private static void OnArgsReceivedFromSecondary(IReadOnlyList<string> paths)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
            if (desktop.MainWindow is not MainWindow mw) return;

            mw.AddFiles(paths);
            if (mw.WindowState == WindowState.Minimized)
                mw.WindowState = WindowState.Normal;
            mw.Activate();
            mw.Focus();
        });
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try { _ipcCts?.Cancel(); } catch (ObjectDisposedException) { }
        _ipcCts?.Dispose();
        _ipcCts = null;

        _singleInstance?.Dispose();
        _singleInstance = null;
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

    private static void RegisterExceptionHandlers()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception, "Dispatcher unhandled");

        try
        {
            var owner = Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt ? lt.MainWindow : null;
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Resize Images",
                $"An unexpected error occurred:{Environment.NewLine}{Environment.NewLine}{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}See the log at:{Environment.NewLine}{AppPaths.GetLogFilePath()}",
                ButtonEnum.Ok,
                Icon.Error);

            _ = owner is not null
                ? box.ShowWindowDialogAsync(owner)
                : box.ShowWindowAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Failed to show error dialog");
        }

        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLog.Write(ex, $"AppDomain unhandled (terminating={e.IsTerminating})");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Write(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}

internal sealed class NullImageCache : IImageCache
{
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public string GetCachedFilePath(string relativePath, ImageResize.Models.ResizeOptions resizeOptions, string sourceSignature) => string.Empty;
    public Task WriteAtomicallyAsync(string path, Stream data, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) => Task.FromResult(Stream.Null);
}
