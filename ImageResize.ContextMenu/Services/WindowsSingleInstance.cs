using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace ImageResize.ContextMenu.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSingleInstance : ISingleInstance
{
    private const string MutexName = @"Global\ImageResize.ContextMenu.SingleInstance";
    private const int ClientConnectTimeoutMs = 2000;
    private const int ClientRetryAttempts = 5;
    private const int ClientRetryDelayMs = 200;

    private Mutex? _mutex;
    private bool _ownsMutex;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
        _ownsMutex = createdNew;
        return createdNew;
    }

    public void ForwardArgs(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return;

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
                        AppLog.Write($" -> send '{a}'");
                        writer.WriteLine(a);
                    }
                }
                writer.WriteLine();
                client.Flush();
                return;
            }
            catch (TimeoutException ex)
            {
                AppLog.Write(ex, $"IPC forward attempt {attempt + 1}: pipe busy");
            }
            catch (IOException ex)
            {
                AppLog.Write(ex, $"IPC forward attempt {attempt + 1}");
            }
            Thread.Sleep(ClientRetryDelayMs);
        }

        AppLog.Write("IPC forward failed after retries; giving up.");
    }

    public void StartServer(Action<IReadOnlyList<string>> onArgsReceived, CancellationToken ct)
    {
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
                        if (string.IsNullOrWhiteSpace(line)) break;
                        if (File.Exists(line)) received.Add(line);
                    }

                    if (received.Count > 0)
                    {
                        AppLog.Write($"IPC received {received.Count} file(s). First='{received[0]}'");
                        onArgsReceived(received);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    AppLog.Write(ex, "IPC server I/O error");
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_ownsMutex && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex?.Dispose();
        _mutex = null;
    }

    private static string GetPipeName()
    {
        var sid = WindowsIdentity.GetCurrent()?.User?.Value ?? "Default";
        return $"ImageResize.ContextMenu.Pipe.{sid}";
    }
}
