using ImageResize.Models;

namespace ImageResize.Interfaces;

/// <summary>
/// Abstraction for image cache operations.
/// </summary>
public interface IImageCache
{
    /// <summary>
    /// Gets the cached file path for the given parameters.
    /// </summary>
    string GetCachedFilePath(string relPath, ResizeOptions options, string sourceSignature);

    /// <summary>
    /// Checks if a cached file exists.
    /// </summary>
    Task<bool> ExistsAsync(string cachedPath, CancellationToken ct = default);

    /// <summary>
    /// Opens a cached file for reading.
    /// </summary>
    Task<Stream> OpenReadAsync(string cachedPath, CancellationToken ct = default);

    /// <summary>
    /// Writes data to cache atomically.
    /// </summary>
    Task WriteAtomicallyAsync(string cachedPath, Stream data, CancellationToken ct = default);
}
