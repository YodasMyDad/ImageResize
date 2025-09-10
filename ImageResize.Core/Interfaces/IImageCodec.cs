using ImageResize.Models;

namespace ImageResize.Interfaces;

/// <summary>
/// Abstraction for image codec operations.
/// </summary>
public interface IImageCodec
{
    /// <summary>
    /// Reads basic metadata without fully decoding the image.
    /// </summary>
    Task<(int Width, int Height, string ContentType)> ProbeAsync(
        Stream input,
        CancellationToken ct = default);

    /// <summary>
    /// Decodes, resizes (fit), and encodes back to original format.
    /// </summary>
    Task<(Stream Output, string ContentType, int OutW, int OutH)> ResizeAsync(
        Stream input,
        string? originalContentType,
        ResizeOptions options,
        CancellationToken ct = default);
}
