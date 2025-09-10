namespace ImageResize.Models;

/// <summary>
/// Options for image resizing operations.
/// </summary>
public sealed record ResizeOptions(
    int? Width,
    int? Height,
    int? Quality
);
