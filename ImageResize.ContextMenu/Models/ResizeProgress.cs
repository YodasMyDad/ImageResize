namespace ImageResize.ContextMenu.Models;

public sealed class ResizeProgress
{
    public int CurrentFile { get; set; }
    public int TotalFiles { get; set; }
    public string FileName { get; set; } = string.Empty;
}

