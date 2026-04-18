using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ImageResize.ContextMenu.Services;

/// <summary>
/// Unix single-instance coordinator: an exclusive lockfile (held for the lifetime of the
/// primary process) + a Unix domain socket that secondaries connect to when forwarding args.
/// </summary>
internal sealed class UnixSingleInstance : ISingleInstance
{
    private const int ClientConnectTimeoutMs = 2000;
    private const int ClientRetryAttempts = 5;
    private const int ClientRetryDelayMs = 200;

    private FileStream? _lock;
    private Socket? _server;

    public bool TryAcquire()
    {
        var path = AppPaths.GetLockFilePath();
        try
        {
            _lock = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void ForwardArgs(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return;

        var socketPath = AppPaths.GetUnixSocketPath();
        var endpoint = new UnixDomainSocketEndPoint(socketPath);

        for (var attempt = 0; attempt < ClientRetryAttempts; attempt++)
        {
            Socket? client = null;
            try
            {
                client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                using var connectCts = new CancellationTokenSource(ClientConnectTimeoutMs);
                try
                {
                    client.ConnectAsync(endpoint, connectCts.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("Unix socket connect timed out");
                }

                using var stream = new NetworkStream(client, ownsSocket: false);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                foreach (var a in args)
                {
                    if (File.Exists(a))
                    {
                        AppLog.Write($" -> send '{a}'");
                        writer.WriteLine(a);
                    }
                }
                writer.WriteLine();
                return;
            }
            catch (TimeoutException ex)
            {
                AppLog.Write(ex, $"IPC forward attempt {attempt + 1}: socket busy");
            }
            catch (SocketException ex)
            {
                AppLog.Write(ex, $"IPC forward attempt {attempt + 1}");
            }
            catch (IOException ex)
            {
                AppLog.Write(ex, $"IPC forward attempt {attempt + 1}");
            }
            finally
            {
                try { client?.Dispose(); } catch (ObjectDisposedException) { }
            }
            Thread.Sleep(ClientRetryDelayMs);
        }

        AppLog.Write("IPC forward failed after retries; giving up.");
    }

    public void StartServer(Action<IReadOnlyList<string>> onArgsReceived, CancellationToken ct)
    {
        var socketPath = AppPaths.GetUnixSocketPath();

        // A stale socket file from a previous crash would block bind() with EADDRINUSE — remove it.
        // We hold the exclusive lockfile, so no live primary is using this path.
        try { if (File.Exists(socketPath)) File.Delete(socketPath); }
        catch (IOException ex) { AppLog.Write(ex, "Removing stale socket"); }

        _server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _server.Bind(new UnixDomainSocketEndPoint(socketPath));
        _server.Listen(backlog: 8);

        ct.Register(() =>
        {
            try { _server?.Close(); } catch (ObjectDisposedException) { }
            try { if (File.Exists(socketPath)) File.Delete(socketPath); } catch (IOException) { }
        });

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                Socket? client = null;
                try
                {
                    client = await _server.AcceptAsync(ct).ConfigureAwait(false);
                    using var stream = new NetworkStream(client, ownsSocket: true);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

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
                catch (SocketException ex)
                {
                    AppLog.Write(ex, "IPC server socket error");
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
        try { _server?.Close(); } catch (ObjectDisposedException) { }
        _server = null;

        // FileOptions.DeleteOnClose removes the lockfile; closing the stream releases the exclusive lock.
        try { _lock?.Dispose(); } catch (IOException) { }
        _lock = null;
    }
}
