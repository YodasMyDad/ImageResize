namespace ImageResize.Abstractions.Models;

/// <summary>
/// Result of an image resize operation.
/// </summary>
public sealed record ResizeResult(
    string OriginalPath,
    string CachedPath,
    string ContentType,
    int OutputWidth,
    int OutputHeight,
    long BytesWritten
);
