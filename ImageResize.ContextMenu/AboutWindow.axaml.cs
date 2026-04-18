using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ImageResize.ContextMenu;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        AppVersionText.Text = $"Version {VersionInfo.AppVersion}";
        CoreVersionText.Text = $"ImageResize.Core: {VersionInfo.CoreVersion}";
        SkiaVersionText.Text = $"SkiaSharp: {VersionInfo.SkiaSharpVersion}";
        RuntimeVersionText.Text = $"Runtime: {VersionInfo.Runtime}";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
