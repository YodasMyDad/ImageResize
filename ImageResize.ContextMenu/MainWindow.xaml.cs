using ImageResize.ContextMenu.Models;
using ImageResize.ContextMenu.Services;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace ImageResize.ContextMenu;

public partial class MainWindow : Window
{
    private readonly ImageProcessor _imageProcessor;
    private List<string> _imagePaths = [];
    private int _originalWidth;
    private int _originalHeight;
    private bool _isUpdatingFromSlider;
    private bool _isUpdatingFromTextBox;
    private bool _isMultipleImages;

    public MainWindow(ImageProcessor imageProcessor)
    {
        InitializeComponent();
        _imageProcessor = imageProcessor;

        Loaded += async (s, e) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageResize", "ContextMenu");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "log.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] InitializeAsync args: {args?.Length ?? 0} First='{args?.Skip(1).FirstOrDefault() ?? ""}'{Environment.NewLine}");
            }
            catch { }

            _imagePaths = (args ?? Array.Empty<string>()).Skip(1)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(File.Exists)
                .Where(IsImageFile)
                .ToList();

            if (_imagePaths.Count == 0)
            {
                var explorerPaths = GetSelectedExplorerImagePaths();
                if (explorerPaths.Count > 0)
                {
                    _imagePaths = explorerPaths;
                }
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageResize", "ContextMenu");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "log.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Explorer selection fallback: {_imagePaths.Count}{Environment.NewLine}");
                }
                catch { }
            }

            if (_imagePaths.Count == 0)
            {
                ShowError("No valid image files were provided. Please select images and use the context menu.");
                ResizeButton.IsEnabled = false;
                return;
            }

            _isMultipleImages = _imagePaths.Count > 1;

            var firstImagePath = _imagePaths[0];
            var imageInfo = await _imageProcessor.GetImageInfoAsync(firstImagePath);

            _originalWidth = imageInfo.Width;
            _originalHeight = imageInfo.Height;

            // Update UI
            if (_imagePaths.Count == 1)
            {
                FileCountText.Text = $"Resizing: {Path.GetFileName(firstImagePath)}";
                OriginalDimensionsText.Text = $"Original: {_originalWidth} × {_originalHeight} px";
                OriginalDimensionsText.Visibility = Visibility.Visible;
            }
            else
            {
                FileCountText.Text = $"Resizing {_imagePaths.Count} images";
                OriginalDimensionsText.Text = string.Empty;
                OriginalDimensionsText.Visibility = Visibility.Collapsed;
            }

            WidthBox.Text = _originalWidth.ToString();
            HeightBox.Text = _originalHeight.ToString();

            ConfigureUiForMode();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load image: {ex.Message}");
            ResizeButton.IsEnabled = false;
        }
    }

    private static List<string> GetSelectedExplorerImagePaths()
    {
        var results = new List<string>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
                return results;

            dynamic? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                var windows = (IEnumerable)shell.Windows();
                foreach (var win in windows)
                {
                    try
                    {
                        dynamic? doc = ((dynamic)win).Document;
                        if (doc == null)
                            continue;
                        var selected = doc.SelectedItems();
                        if (selected == null)
                            continue;

                        foreach (var item in selected)
                        {
                            try
                            {
                                string path = (string)((dynamic)item).Path;
                                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsImageFile(path))
                                {
                                    results.Add(path);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                if (shell != null && Marshal.IsComObject(shell))
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }
        }
        catch { }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ConfigureUiForMode()
    {
        if (_isMultipleImages)
        {
            // Hide dimension boxes for multiple images
            DimensionsPanel.Visibility = Visibility.Collapsed;
            PercentageLabel.Text = "Size Percentage (applies to all images)";
            OriginalDimensionsText.Text = string.Empty;
            OriginalDimensionsText.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Show dimension boxes for single image
            DimensionsPanel.Visibility = Visibility.Visible;
            PercentageLabel.Text = "Size Percentage";
            OriginalDimensionsText.Visibility = Visibility.Visible;
        }
    }

    // Called by App when a secondary instance forwards more files
    public void AddFiles(IEnumerable<string> files)
    {
        var toAdd = files
            .Where(File.Exists)
            .Where(IsImageFile)
            .ToList();

        if (toAdd.Count == 0)
            return;

        foreach (var p in toAdd)
        {
            if (!_imagePaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                _imagePaths.Add(p);
        }

        _isMultipleImages = _imagePaths.Count > 1;
        ConfigureUiForMode();

        if (_isMultipleImages)
        {
            FileCountText.Text = $"Resizing {_imagePaths.Count} images";
            OriginalDimensionsText.Text = string.Empty;
            OriginalDimensionsText.Visibility = Visibility.Collapsed;
        }
        else if (_imagePaths.Count == 1)
        {
            var first = _imagePaths[0];
            FileCountText.Text = $"Resizing: {Path.GetFileName(first)}";
            if (_originalWidth > 0 && _originalHeight > 0)
            {
                OriginalDimensionsText.Text = $"Original: {_originalWidth} × {_originalHeight} px";
                WidthBox.Text = _originalWidth.ToString();
                HeightBox.Text = _originalHeight.ToString();
                OriginalDimensionsText.Visibility = Visibility.Visible;
            }
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
            WidthBox.Text = ((int)Math.Round(_originalWidth * scale)).ToString();
            HeightBox.Text = ((int)Math.Round(_originalHeight * scale)).ToString();
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

        var percentage = (newWidth / (double)_originalWidth) * 100.0;
        PercentageSlider.Value = Math.Clamp(percentage, PercentageSlider.Minimum, PercentageSlider.Maximum);
        PercentageText.Text = $"{(int)Math.Round(percentage)}%";

        _isUpdatingFromTextBox = false;
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityText != null)
            QualityText.Text = ((int)e.NewValue).ToString();
    }

    private async void ResizeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ResizeButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ErrorBorder.Visibility = Visibility.Collapsed;

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
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = $"Processing {p.CurrentFile} of {p.TotalFiles}: {p.FileName}";
                    ProgressBar.Value = (p.CurrentFile / (double)p.TotalFiles) * 100;
                });
            });

            await _imageProcessor.ResizeImagesAsync(_imagePaths, settings, progress);

            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to resize images: {ex.Message}");
            ResizeButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff";
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        var regex = new Regex("[^0-9]+");
        e.Handled = regex.IsMatch(e.Text);
    }
}

