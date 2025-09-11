namespace ImageResize.Core.Models;

/// <summary>
/// Size structure for compatibility.
/// Equivalent to System.Drawing.Size.
/// </summary>
public struct Size(int width, int height)
{
    public int Width { get; set; } = width;
    public int Height { get; set; } = height;
}