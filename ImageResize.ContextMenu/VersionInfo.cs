using System.Reflection;
using System.Runtime.InteropServices;

namespace ImageResize.ContextMenu;

/// <summary>
/// Centralised resolution of version strings surfaced in the UI (title bar, About dialog, logs).
/// Reads <see cref="AssemblyInformationalVersionAttribute"/> — which flows from the
/// <c>&lt;InformationalVersion&gt;</c> MSBuild property in Directory.Build.props — stripping
/// the SourceLink build-metadata suffix (<c>+gitsha</c>) that appears on CI builds.
/// </summary>
internal static class VersionInfo
{
    public static string AppVersion { get; } = ReadInformationalVersion(typeof(App).Assembly);
    public static string CoreVersion { get; } = ReadInformationalVersion(typeof(Core.Services.ImageResizerService).Assembly);
    public static string SkiaSharpVersion { get; } = ReadInformationalVersion(typeof(SkiaSharp.SKBitmap).Assembly);
    public static string Runtime { get; } = RuntimeInformation.FrameworkDescription;

    private static string ReadInformationalVersion(Assembly asm)
    {
        var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var raw = attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "unknown";
        var plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }
}
