using System.IO;

namespace ImageResize.ContextMenu.Services;

internal static class AppLog
{
    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(
                AppPaths.GetLogFilePath(),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Write(Exception ex, string context)
        => Write($"{context}: {ex.GetType().Name}: {ex.Message}");

    public static void LogStartupArgs(string label, string[] args)
    {
        string cwd;
        try { cwd = Environment.CurrentDirectory; }
        catch (Exception ex) { cwd = $"<error: {ex.GetType().Name}>"; }

        Write($"{label}. argc={args.Length} cwd='{cwd}'");
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            bool exists;
            try { exists = !string.IsNullOrWhiteSpace(a) && File.Exists(a); }
            catch (Exception) { exists = false; }
            var ext = string.Empty;
            try { ext = Path.GetExtension(a ?? string.Empty); } catch (ArgumentException) { }
            Write($"  arg[{i}] len={(a?.Length ?? 0)} exists={exists} ext='{ext}' value='{a}'");
        }
    }
}
