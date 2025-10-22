using ImageResize.Core.Codecs;
using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Services;
using ImageResize.ContextMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Windows;

namespace ImageResize.ContextMenu;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _ipcCts;
    private const string MutexName = @"Global\ImageResize.ContextMenu.SingleInstance";
    private static string GetPipeName()
    {
        // Per-user pipe to avoid cross-user interference
        var sid = WindowsIdentity.GetCurrent()?.User?.Value ?? "Default";
        return $"ImageResize.ContextMenu.Pipe.{sid}";
    }
    private static string GetLogPath()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageResize", "ContextMenu");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "log.txt");
    }
    private static void SafeLog(string message)
    {
        try
        {
            File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
    
    public App()
    {
        ConfigureServices();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure single-instance behavior. If an instance is already running,
        // forward our arguments to it and exit immediately.
        var createdNew = false;
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);

        if (!createdNew)
        {
            // Forward args to existing instance via named pipe
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            SafeLog($"Secondary instance. Forwarding {args.Length} arg(s). First='{args.FirstOrDefault()}'");
            if (args.Length > 0)
            {
                try
                {
                    var pipeName = GetPipeName();
                    // Retry a few times in case the server is still initializing
                    for (var attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            using var client = new NamedPipeClientStream(
                                ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                            client.Connect(timeout: 500);
                            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                            // Send each path on its own line
                            foreach (var a in args)
                            {
                                if (File.Exists(a))
                                {
                                    SafeLog($" -> send '{a}'");
                                    writer.WriteLine(a);
                                }
                            }
                            // Terminate with an empty line
                            writer.WriteLine();
                            // Give the pipe time to flush
                            client.Flush();
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(200);
                        }
                    }
                }
                catch
                {
                    // Ignore any IPC errors; just exit.
                }
            }

            Shutdown();
            return;
        }

        // Primary instance: start IPC server to receive additional files
        StartIpcServer();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        // Log startup args for the primary instance too
        var startupArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        SafeLog($"Primary instance. Startup args: {startupArgs.Length}. First='{startupArgs.FirstOrDefault()}'");
        mainWindow.Show();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Configure ImageResize options (minimal config for desktop usage)
        services.Configure<ImageResizeOptions>(options =>
        {
            options.DefaultQuality = 99;
            options.PngCompressionLevel = 6;
            options.AllowUpscale = false;
            options.Backend = ImageBackend.SkiaSharp;
            // WebRoot not needed for desktop usage
            options.WebRoot = string.Empty;
        });

        // Register ImageResize core services
        services.AddSingleton<IImageCodec, SkiaCodec>();
        services.AddSingleton<IImageResizerService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ImageResizeOptions>>();
            var codec = sp.GetRequiredService<IImageCodec>();
            var logger = sp.GetRequiredService<ILogger<ImageResizerService>>();
            
            // Create a minimal cache implementation (not used for desktop, but required)
            var cache = new NullImageCache();
            
            return new ImageResizerService(options, cache, codec, logger);
        });

        // Register application services
        services.AddSingleton<ImageProcessor>();
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();
    }

    private void StartIpcServer()
    {
        _ipcCts = new CancellationTokenSource();
        var ct = _ipcCts.Token;
        var pipeName = GetPipeName();

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                        transmissionMode: PipeTransmissionMode.Byte, options: PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var received = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            break;
                        if (File.Exists(line))
                            received.Add(line);
                    }

                    if (received.Count > 0)
                    {
                        // Marshal to UI thread and add files to the existing window
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (Current?.MainWindow is MainWindow mw)
                            {
                                SafeLog($"IPC received {received.Count} file(s). First='{received.FirstOrDefault()}'");
                                mw.AddFiles(received);
                                if (mw.WindowState == WindowState.Minimized)
                                    mw.WindowState = WindowState.Normal;
                                mw.Activate();
                                mw.Topmost = true;  // ensure front
                                mw.Topmost = false;
                                mw.Focus();
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Shutting down
                    break;
                }
                catch
                {
                    // Swallow and continue listening
                }
            }
        }, ct);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        try { _ipcCts?.Cancel(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _ipcCts?.Dispose();
        _singleInstanceMutex?.Dispose();
    }
}

// Minimal null cache implementation for desktop usage
internal class NullImageCache : IImageCache
{
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    
    public string GetCachedFilePath(string relativePath, ImageResize.Models.ResizeOptions options, string sourceSignature) => string.Empty;
    
    public Task WriteAtomicallyAsync(string path, Stream data, CancellationToken ct = default) => Task.CompletedTask;
    
    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) => Task.FromResult(Stream.Null);
}

