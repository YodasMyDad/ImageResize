using System.IO.Hashing;
using System.Text;

namespace ImageResize.Core.Utilities;

/// <summary>
/// Shared hashing helpers. The algorithm here is <see cref="XxHash128"/> — non-cryptographic,
/// but collision-resistant and significantly faster than SHA1/SHA256 for the cache-key and ETag
/// use cases in this library. Do not use these helpers for anything security-sensitive.
/// </summary>
internal static class HashingUtilities
{
    /// <summary>
    /// Computes a lowercase hex XxHash128 of the UTF-8 bytes of <paramref name="input"/>.
    /// </summary>
    public static string HashStringToHex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = XxHash128.Hash(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Computes a lowercase hex XxHash128 of a stream. The stream is read to completion; position
    /// is not restored on exit (callers should reset if they need to reuse the stream).
    /// </summary>
    public static async Task<string> HashStreamToHexAsync(Stream stream, CancellationToken ct = default)
    {
        var hasher = new XxHash128();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            hasher.Append(buffer.AsSpan(0, read));
        }
        return Convert.ToHexStringLower(hasher.GetCurrentHash());
    }

    /// <summary>
    /// Stable ETag derived from the file's path, size, and last-write time — so cached responses
    /// invalidate correctly when the source file changes on disk.
    /// </summary>
    public static string ComputeFileETag(string filePath)
    {
        var fi = new FileInfo(filePath);
        var key = $"{filePath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        return $"\"{HashStringToHex(key)}\"";
    }
}
