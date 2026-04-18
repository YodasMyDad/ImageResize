using NUnit.Framework;

namespace ImageResize.Tests;

/// <summary>
/// Allocates and cleans up a unique temp directory per fixture. Prevents cross-fixture bleed so
/// tests can safely run in parallel.
/// </summary>
public static class TestPaths
{
    public static string AllocateTempDir(string prefix)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ImageResize.Tests.{prefix}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void SafeDeleteDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            TestContext.Progress.WriteLine($"Leaked temp dir: {path}");
        }
        catch (UnauthorizedAccessException)
        {
            TestContext.Progress.WriteLine($"Leaked temp dir (perm): {path}");
        }
    }
}
