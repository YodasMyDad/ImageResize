using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using ImageResize.ContextMenu.Models;
using ImageResize.ContextMenu.Services;
using Microsoft.CSharp.RuntimeBinder;

namespace ImageResize.ContextMenu;

public partial class MainWindow : Window
{
    private static readonly Regex NumberOnly = new("[^0-9]+", RegexOptions.Compiled);
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff"
    };

    private readonly ImageProcessor _imageProcessor;
    private readonly List<string> _imagePaths = [];
    private int _originalWidth;
    private int _originalHeight;
    private bool _isUpdatingFromSlider;
    private bool _isUpdatingFromTextBox;
    private bool _isMultipleImages;
    private CancellationTokenSource? _cts;
    private bool _resizeInProgress;

    public MainWindow(ImageProcessor imageProcessor)
    {
        InitializeComponent();
        _imageProcessor = imageProcessor;

        Title = $"Resize Images — v{VersionInfo.AppVersion}";

        Loaded += async (_, _) => await InitializeAsync().ConfigureAwait(false);
    }

    private async Task InitializeAsync()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();

            var initial = (args ?? [])
                .Skip(1)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(File.Exists)
                .Where(IsImageFile)
                .ToList();

            if (initial.Count == 0)
                initial = GetSelectedExplorerImagePaths();

            if (initial.Count == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowInfo("No valid image files were provided. Please select images and use the context menu, or drag images onto this window.");
                    ResizeButton.IsEnabled = false;
                });
                return;
            }

            _imagePaths.AddRange(initial);
            _isMultipleImages = _imagePaths.Count > 1;

            var imageInfo = await _imageProcessor.GetImageInfoAsync(_imagePaths[0]).ConfigureAwait(false);

            Dispatcher.Invoke(() =>
            {
                _originalWidth = imageInfo.Width;
                _originalHeight = imageInfo.Height;
                RefreshHeader();
                WidthBox.Text = _originalWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
                HeightBox.Text = _originalHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ConfigureUiForMode();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                ShowError($"Failed to load image: {ex.Message}");
                ResizeButton.IsEnabled = false;
            });
        }
    }

    private void RefreshHeader()
    {
        if (_imagePaths.Count == 1)
        {
            FileCountText.Text = $"Resizing: {Path.GetFileName(_imagePaths[0])}";
            OriginalDimensionsText.Text = _originalWidth > 0 && _originalHeight > 0
                ? $"Original: {_originalWidth} × {_originalHeight} px"
                : string.Empty;
            OriginalDimensionsText.Visibility = Visibility.Visible;
        }
        else
        {
            FileCountText.Text = $"Resizing {_imagePaths.Count} images";
            OriginalDimensionsText.Text = string.Empty;
            OriginalDimensionsText.Visibility = Visibility.Collapsed;
        }
    }

    private static List<string> GetSelectedExplorerImagePaths()
    {
        var results = new List<string>();
        dynamic? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
                return results;

            shell = Activator.CreateInstance(shellType);
            var windows = (IEnumerable)shell!.Windows();
            foreach (var win in windows)
            {
                try
                {
                    dynamic? doc = ((dynamic)win).Document;
                    if (doc == null) continue;
                    var selected = doc.SelectedItems();
                    if (selected == null) continue;

                    foreach (var item in selected)
                    {
                        try
                        {
                            string path = (string)((dynamic)item).Path;
                            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsImageFile(path))
                                results.Add(path);
                        }
                        catch (RuntimeBinderException) { }
                        catch (COMException) { }
                    }
                }
                catch (RuntimeBinderException) { }
                catch (COMException) { }
            }
        }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell))
            {
                try { Marshal.FinalReleaseComObject(shell); }
                catch (InvalidComObjectException) { }
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ConfigureUiForMode()
    {
        if (_isMultipleImages)
        {
            DimensionsPanel.Visibility = Visibility.Collapsed;
            PercentageLabel.Text = "Size Percentage (applies to all images)";
            OriginalDimensionsText.Text = string.Empty;
            OriginalDimensionsText.Visibility = Visibility.Collapsed;
        }
        else
        {
            DimensionsPanel.Visibility = Visibility.Visible;
            PercentageLabel.Text = "Size Percentage";
            OriginalDimensionsText.Visibility = Visibility.Visible;
        }
    }

    // Called by App when a secondary instance forwards more files
    public void AddFiles(IEnumerable<string> files)
    {
        if (_resizeInProgress) return;

        var toAdd = files
            .Where(File.Exists)
            .Where(IsImageFile)
            .ToList();

        if (toAdd.Count == 0) return;

        foreach (var p in toAdd)
        {
            if (!_imagePaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                _imagePaths.Add(p);
        }

        _isMultipleImages = _imagePaths.Count > 1;
        ConfigureUiForMode();
        RefreshHeader();

        if (!_isMultipleImages && _originalWidth > 0 && _originalHeight > 0)
        {
            WidthBox.Text = _originalWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            HeightBox.Text = _originalHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        ErrorBorder.Visibility = Visibility.Collapsed;
        ResizeButton.IsEnabled = true;
    }

    private void PercentageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingFromTextBox || _originalWidth == 0 || _originalHeight == 0)
            return;

        _isUpdatingFromSlider = true;
        var percentage = (int)e.NewValue;
        PercentageText.Text = $"{percentage}%";

        if (!_isMultipleImages)
        {
            var scale = percentage / 100.0;
            WidthBox.Text = ((int)Math.Round(_originalWidth * scale)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            HeightBox.Text = ((int)Math.Round(_originalHeight * scale)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        _isUpdatingFromSlider = false;
    }

    private void DimensionBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isUpdatingFromSlider || _originalWidth == 0 || _originalHeight == 0 || _isMultipleImages)
            return;

        if (!int.TryParse(WidthBox.Text, out var newWidth) || newWidth <= 0)
            return;

        _isUpdatingFromTextBox = true;
        var percentage = newWidth / (double)_originalWidth * 100.0;
        PercentageSlider.Value = Math.Clamp(percentage, PercentageSlider.Minimum, PercentageSlider.Maximum);
        PercentageText.Text = $"{(int)Math.Round(percentage)}%";
        _isUpdatingFromTextBox = false;
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityText != null)
            QualityText.Text = ((int)e.NewValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private async void ResizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_imagePaths.Count == 0)
        {
            ShowError("Nothing to resize — queue some images first.");
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _resizeInProgress = true;

        try
        {
            ResizeButton.IsEnabled = false;
            CancelButton.Content = "Stop";
            ProgressPanel.Visibility = Visibility.Visible;
            ErrorBorder.Visibility = Visibility.Collapsed;
            ProgressBar.Value = 0;
            ProgressText.Text = "Starting…";
            ProgressEtaText.Text = string.Empty;

            ResizeSettings settings;
            if (_isMultipleImages)
            {
                settings = new ResizeSettings
                {
                    UsePercentage = true,
                    Percentage = (int)PercentageSlider.Value,
                    Quality = (int)QualitySlider.Value,
                    Overwrite = OverwriteCheckBox.IsChecked ?? true
                };
            }
            else
            {
                if (!int.TryParse(WidthBox.Text, out var width) || !int.TryParse(HeightBox.Text, out var height))
                {
                    ShowError("Invalid dimensions. Please enter valid numbers.");
                    return;
                }

                settings = new ResizeSettings
                {
                    UsePercentage = false,
                    TargetWidth = width,
                    TargetHeight = height,
                    Quality = (int)QualitySlider.Value,
                    Overwrite = OverwriteCheckBox.IsChecked ?? true
                };
            }

            var progress = new Progress<ResizeProgress>(p =>
            {
                ProgressText.Text = $"{p.CurrentFile}/{p.TotalFiles} — {p.FileName}";
                ProgressBar.Value = p.CurrentFile / (double)p.TotalFiles * 100;
                ProgressEtaText.Text = p.Eta > TimeSpan.Zero
                    ? $"{FormatDuration(p.Elapsed)} elapsed · ~{FormatDuration(p.Eta)} left"
                    : FormatDuration(p.Elapsed) + " elapsed";
            });

            await _imageProcessor.ResizeImagesAsync(_imagePaths, settings, progress, _cts.Token).ConfigureAwait(true);

            Close();
        }
        catch (OperationCanceledException)
        {
            ShowInfo("Cancelled.");
            ResetPostRunUi();
        }
        catch (BatchResizeException batchEx)
        {
            ShowError(FormatBatchFailure(batchEx));
            ResetPostRunUi();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to resize images: {ex.Message}");
            ResetPostRunUi();
        }
        finally
        {
            _resizeInProgress = false;
        }
    }

    private void ResetPostRunUi()
    {
        ResizeButton.IsEnabled = true;
        CancelButton.Content = "Cancel";
        ProgressPanel.Visibility = Visibility.Collapsed;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 1) return "<1s";
        if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s";
        return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
    }

    private static string FormatBatchFailure(BatchResizeException ex)
    {
        var failures = string.Join(Environment.NewLine,
            ex.Failures.Select(f => $"  • {Path.GetFileName(f.Path)}: {f.Reason}"));

        var header = ex.SuccessCount == 0
            ? $"All {ex.TotalCount} files failed. Check folder permissions or OneDrive sync status."
            : $"{ex.SuccessCount} of {ex.TotalCount} succeeded, {ex.Failures.Count} failed:";

        return $"{header}{Environment.NewLine}{failures}";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resizeInProgress)
        {
            _cts?.Cancel();
            CancelButton.IsEnabled = false;
            ProgressText.Text = "Cancelling…";
        }
        else
        {
            Close();
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e) => ShowAbout();

    private void Help_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => ShowAbout();

    public void ShowAbout()
    {
        var dlg = new AboutWindow { Owner = this };
        dlg.ShowDialog();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasImageFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_resizeInProgress) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        AddFiles(files);
        e.Handled = true;
    }

    private static bool HasImageFiles(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] files)
            return false;
        return files.Any(IsImageFile);
    }

    private void ShowInfo(string message)
    {
        ErrorHeading.Text = "Info";
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void ShowError(string message)
    {
        ErrorHeading.Text = "Error";
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        => e.Handled = NumberOnly.IsMatch(e.Text);

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
