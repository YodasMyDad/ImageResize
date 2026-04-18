namespace ImageResize.ContextMenu.Services;

/// <summary>
/// Cross-platform single-instance coordinator. On startup, call <see cref="TryAcquire"/>:
/// a <c>true</c> return means this process is primary and should continue running;
/// a <c>false</c> return means another process is already primary — forward the command-line
/// args with <see cref="ForwardArgs"/> and exit.
/// Primary instances call <see cref="StartServer"/> to receive forwarded args from secondaries.
/// </summary>
internal interface ISingleInstance : IDisposable
{
    bool TryAcquire();

    void ForwardArgs(IReadOnlyList<string> args);

    void StartServer(Action<IReadOnlyList<string>> onArgsReceived, CancellationToken ct);
}

internal static class SingleInstance
{
    public static ISingleInstance Create()
        => OperatingSystem.IsWindows()
            ? new WindowsSingleInstance()
            : new UnixSingleInstance();
}
