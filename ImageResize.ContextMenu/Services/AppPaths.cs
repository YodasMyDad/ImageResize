using System.IO;

namespace ImageResize.ContextMenu.Services;

internal static class AppPaths
{
    private const string AppFolder = "ImageResize";
    private const string SubFolder = "ContextMenu";

    public static string GetUserDataDir()
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(home, "Library", "Application Support");
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            root = !string.IsNullOrEmpty(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var dir = Path.Combine(root, AppFolder, SubFolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetLogFilePath() => Path.Combine(GetUserDataDir(), "log.txt");

    public static string GetLockFilePath() => Path.Combine(GetUserDataDir(), "instance.lock");

    public static string GetUnixSocketPath() => Path.Combine(GetUserDataDir(), "instance.sock");
}
