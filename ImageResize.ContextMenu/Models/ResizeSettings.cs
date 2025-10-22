namespace ImageResize.ContextMenu.Models;

public sealed class ResizeSettings
{
    public int TargetWidth { get; set; }
    public int TargetHeight { get; set; }
    public int Quality { get; set; } = 99;
    public bool Overwrite { get; set; } = true;
    public bool UsePercentage { get; set; }
    public int Percentage { get; set; } = 100;
}

