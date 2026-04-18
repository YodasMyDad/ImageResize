using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
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
    private const string MutexName = @"Global\ImageResize.ContextMenu.SingleInstance";
    private const int ClientConnectTimeoutMs = 2000;
    private const int ClientRetryAttempts = 5;
    private const int ClientRetryDelayMs = 200;

    public static IServiceProvider Services { get; private set; } = null!;

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _ipcCts;

    public App()
    {
        ConfigureServices();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            ForwardArgsToRunningInstance(Environment.GetCommandLineArgs().Skip(1).ToArray());
            Shutdown();
            return;
        }

        StartIpcServer();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        var startupArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        SafeLog($"Primary instance. Startup args: {startupArgs.Length}. First='{startupArgs.FirstOrDefault()}'");
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        try { _ipcCts?.Cancel(); } catch (ObjectDisposedException) { }
        _ipcCts?.Dispose();

        if (_ownsMutex && _singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not held on this thread */ }
        }
        _singleInstanceMutex?.Dispose();
    }

    private static string GetPipeName()
    {
        var sid = WindowsIdentity.GetCurrent()?.User?.Value ?? "Default";
        return $"ImageResize.ContextMenu.Pipe.{sid}";
    }

    private static string GetLogPath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageResize", "ContextMenu");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "log.txt");
    }

    private static void SafeLog(string message)
    {
        try
        {
            File.AppendAllText(
                GetLogPath(),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void SafeLog(Exception ex, string context)
        => SafeLog($"{context}: {ex.GetType().Name}: {ex.Message}");

    private static void ForwardArgsToRunningInstance(string[] args)
    {
        SafeLog($"Secondary instance. Forwarding {args.Length} arg(s). First='{args.FirstOrDefault()}'");
        if (args.Length == 0)
            return;

        var pipeName = GetPipeName();

        for (var attempt = 0; attempt < ClientRetryAttempts; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                client.Connect(ClientConnectTimeoutMs);

                using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                foreach (var a in args)
                {
                    if (File.Exists(a))
                    {
                        SafeLog($" -> send '{a}'");
                        writer.WriteLine(a);
                    }
                }
                writer.WriteLine();
                client.Flush();
                return;
            }
            catch (TimeoutException ex)
            {
                SafeLog(ex, $"IPC forward attempt {attempt + 1}: pipe busy");
            }
            catch (IOException ex)
            {
                SafeLog(ex, $"IPC forward attempt {attempt + 1}");
            }
            Thread.Sleep(ClientRetryDelayMs);
        }

        SafeLog("IPC forward failed after retries; giving up.");
    }

    private void ConfigureServices()
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

    private void StartIpcServer()
    {
        _ipcCts = new CancellationTokenSource();
        var ct = _ipcCts.Token;
        var pipeName = GetPipeName();

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var received = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            break;
                        if (File.Exists(line))
                            received.Add(line);
                    }

                    if (received.Count > 0)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (Current?.MainWindow is MainWindow mw)
                            {
                                SafeLog($"IPC received {received.Count} file(s). First='{received.FirstOrDefault()}'");
                                mw.AddFiles(received);
                                if (mw.WindowState == WindowState.Minimized)
                                    mw.WindowState = WindowState.Normal;
                                mw.Activate();
                                mw.Focus();
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    SafeLog(ex, "IPC server I/O error");
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, ct);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        SafeLog(e.Exception, "Dispatcher unhandled");
        try
        {
            MessageBox.Show(
                MainWindow,
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nSee the log in\n%LocalAppData%\\ImageResize\\ContextMenu\\log.txt",
                "Resize Images",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (InvalidOperationException) { /* app shutting down */ }
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            SafeLog(ex, $"AppDomain unhandled (terminating={e.IsTerminating})");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        SafeLog(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}

// Minimal null cache implementation for desktop usage
internal sealed class NullImageCache : IImageCache
{
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public string GetCachedFilePath(string relativePath, ImageResize.Models.ResizeOptions resizeOptions, string sourceSignature) => string.Empty;
    public Task WriteAtomicallyAsync(string path, Stream data, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) => Task.FromResult(Stream.Null);
}
