using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User has no default browser; silently ignore.
        }
        e.Handled = true;
    }
}
