using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);
        KeyDown += OnKeyDown;

        Opened += async (_, _) => await InitializeAsync().ConfigureAwait(false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            ShowAbout();
            e.Handled = true;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();

            var initial = (args ?? [])
                .Skip(1)
                .Select(TryResolveArg)
                .Where(p => !string.IsNullOrEmpty(p))
                .Where(IsImageFile)
                .ToList();

            // No command-line args means Windows didn't pass the selection (typical for Desktop
            // shell-verb invocations). Try the Desktop listview first — a stale selection in a
            // background Explorer window would otherwise hijack the result.
            if (initial.Count == 0 && OperatingSystem.IsWindows())
                initial = GetSelectedDesktopImagePaths();

            if (initial.Count == 0 && OperatingSystem.IsWindows())
                initial = GetSelectedExplorerImagePaths();

            if (initial.Count == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowInfo("No valid image files were provided. Please select images and use the context menu, or drag images onto this window.");
                    ResizeButton.IsEnabled = false;
                });
                return;
            }

            _imagePaths.AddRange(initial);
            _isMultipleImages = _imagePaths.Count > 1;

            var imageInfo = await _imageProcessor.GetImageInfoAsync(_imagePaths[0]).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _originalWidth = imageInfo.Width;
                _originalHeight = imageInfo.Height;
                RefreshHeader();
                WidthBox.Text = _originalWidth.ToString(CultureInfo.InvariantCulture);
                HeightBox.Text = _originalHeight.ToString(CultureInfo.InvariantCulture);
                ConfigureUiForMode();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
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
            OriginalDimensionsText.IsVisible = true;
        }
        else
        {
            FileCountText.Text = $"Resizing {_imagePaths.Count} images";
            OriginalDimensionsText.Text = string.Empty;
            OriginalDimensionsText.IsVisible = false;
        }
    }

    // Resolves an incoming argument to a full file path that exists, or "" if it can't be found.
    // Shell-verb launches sometimes hand us bare filenames or paths rooted at the wrong Desktop
    // when OneDrive redirection is in play.
    private static string TryResolveArg(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var arg = raw.Trim().Trim('"');
        if (arg.Length == 0) return string.Empty;

        if (File.Exists(arg)) return arg;

        try
        {
            var full = Path.GetFullPath(arg);
            if (File.Exists(full)) return full;
        }
        catch (ArgumentException) { }
        catch (PathTooLongException) { }
        catch (NotSupportedException) { }

        foreach (var root in EnumerateDesktopRoots())
        {
            try
            {
                var combined = Path.GetFullPath(arg, root);
                if (File.Exists(combined)) return combined;
            }
            catch (ArgumentException) { }
            catch (PathTooLongException) { }
            catch (NotSupportedException) { }
        }

        return string.Empty;
    }

    private static IEnumerable<string> EnumerateDesktopRoots()
    {
        var known = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(known)) yield return known;

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive))
        {
            var oneDriveDesktop = Path.Combine(oneDrive, "Desktop");
            if (!string.Equals(oneDriveDesktop, known, StringComparison.OrdinalIgnoreCase))
                yield return oneDriveDesktop;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            var profileDesktop = Path.Combine(profile, "Desktop");
            if (!string.Equals(profileDesktop, known, StringComparison.OrdinalIgnoreCase))
                yield return profileDesktop;
        }
    }

    [SupportedOSPlatform("windows")]
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

    // Reads the Windows Desktop's current selection by talking to the Progman/WorkerW SysListView32
    // directly. Needed because Shell.Application.Windows() does not expose the Desktop, and Windows
    // does not pass %* command-line args to shell verbs invoked from the Desktop.
    [SupportedOSPlatform("windows")]
    private static List<string> GetSelectedDesktopImagePaths()
    {
        var results = new List<string>();

        var hListView = FindDesktopListView();
        if (hListView == IntPtr.Zero) return results;

        int count = (int)SendMessage(hListView, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0) return results;

        _ = GetWindowThreadProcessId(hListView, out var pid);
        if (pid == 0) return results;

        var hProcess = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION,
            false, pid);
        if (hProcess == IntPtr.Zero) return results;

        IntPtr remoteItem = IntPtr.Zero;
        IntPtr remoteText = IntPtr.Zero;
        const int textBytes = 520 * sizeof(char);
        var itemBytes = Marshal.SizeOf<LVITEMW>();

        try
        {
            remoteItem = VirtualAllocEx(hProcess, IntPtr.Zero, (IntPtr)itemBytes, MEM_COMMIT, PAGE_READWRITE);
            remoteText = VirtualAllocEx(hProcess, IntPtr.Zero, (IntPtr)textBytes, MEM_COMMIT, PAGE_READWRITE);
            if (remoteItem == IntPtr.Zero || remoteText == IntPtr.Zero) return results;

            var desktopRoots = EnumerateDesktopRoots().ToList();
            var buffer = new byte[textBytes];
            var index = -1;
            while (true)
            {
                index = (int)SendMessage(hListView, LVM_GETNEXTITEM, (IntPtr)index, (IntPtr)LVNI_SELECTED);
                if (index < 0) break;

                var lvItem = new LVITEMW
                {
                    mask = LVIF_TEXT,
                    iItem = index,
                    iSubItem = 0,
                    pszText = remoteText,
                    cchTextMax = textBytes / sizeof(char)
                };

                if (!WriteProcessMemory(hProcess, remoteItem, ref lvItem, (IntPtr)itemBytes, out _))
                    continue;

                _ = SendMessage(hListView, LVM_GETITEMTEXTW, (IntPtr)index, remoteItem);

                if (!ReadProcessMemory(hProcess, remoteText, buffer, (IntPtr)textBytes, out _))
                    continue;

                var name = Encoding.Unicode.GetString(buffer);
                var nul = name.IndexOf('\0');
                if (nul >= 0) name = name[..nul];
                if (string.IsNullOrWhiteSpace(name)) continue;

                var resolved = ResolveDesktopItem(name, desktopRoots);
                if (!string.IsNullOrEmpty(resolved) && IsImageFile(resolved))
                    results.Add(resolved);
            }
        }
        finally
        {
            if (remoteItem != IntPtr.Zero) VirtualFreeEx(hProcess, remoteItem, IntPtr.Zero, MEM_RELEASE);
            if (remoteText != IntPtr.Zero) VirtualFreeEx(hProcess, remoteText, IntPtr.Zero, MEM_RELEASE);
            CloseHandle(hProcess);
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolveDesktopItem(string name, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;

            var direct = Path.Combine(root, name);
            if (File.Exists(direct)) return direct;

            if (string.IsNullOrEmpty(Path.GetExtension(name)))
            {
                foreach (var ext in ImageExtensions)
                {
                    var candidate = Path.Combine(root, name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return string.Empty;
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr FindDesktopListView()
    {
        var hProgman = FindWindow("Progman", null);
        if (hProgman != IntPtr.Zero)
        {
            var hDef = FindWindowEx(hProgman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (hDef != IntPtr.Zero)
            {
                var hList = FindWindowEx(hDef, IntPtr.Zero, "SysListView32", null);
                if (hList != IntPtr.Zero) return hList;
            }
        }

        IntPtr found = IntPtr.Zero;
        var classBuf = new char[32];
        EnumWindows((hWnd, _) =>
        {
            var len = GetClassName(hWnd, classBuf, classBuf.Length);
            if (len == 0) return true;
            if (!new ReadOnlySpan<char>(classBuf, 0, len).SequenceEqual("WorkerW")) return true;

            var hDef = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (hDef == IntPtr.Zero) return true;

            var hList = FindWindowEx(hDef, IntPtr.Zero, "SysListView32", null);
            if (hList == IntPtr.Zero) return true;

            found = hList;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    #region Win32 interop for Desktop selection

    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    private const int LVM_FIRST = 0x1000;
    private const int LVM_GETSELECTEDCOUNT = LVM_FIRST + 50;
    private const int LVM_GETNEXTITEM = LVM_FIRST + 12;
    private const int LVM_GETITEMTEXTW = LVM_FIRST + 115;
    private const int LVIF_TEXT = 0x0001;
    private const int LVNI_SELECTED = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEMW
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVITEMW lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    #endregion

    private void ConfigureUiForMode()
    {
        if (_isMultipleImages)
        {
            DimensionsPanel.IsVisible = false;
            PercentageLabel.Text = "Size Percentage (applies to all images)";
            OriginalDimensionsText.Text = string.Empty;
            OriginalDimensionsText.IsVisible = false;
        }
        else
        {
            DimensionsPanel.IsVisible = true;
            PercentageLabel.Text = "Size Percentage";
            OriginalDimensionsText.IsVisible = true;
        }
    }

    public void AddFiles(IEnumerable<string> files)
    {
        if (_resizeInProgress) return;

        var toAdd = files
            .Select(TryResolveArg)
            .Where(p => !string.IsNullOrEmpty(p))
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
            WidthBox.Text = _originalWidth.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = _originalHeight.ToString(CultureInfo.InvariantCulture);
        }

        ErrorBorder.IsVisible = false;
        ResizeButton.IsEnabled = true;
    }

    private void PercentageSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingFromTextBox || _originalWidth == 0 || _originalHeight == 0)
            return;

        _isUpdatingFromSlider = true;
        var percentage = (int)e.NewValue;
        PercentageText.Text = $"{percentage}%";

        if (!_isMultipleImages)
        {
            var scale = percentage / 100.0;
            WidthBox.Text = ((int)Math.Round(_originalWidth * scale)).ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = ((int)Math.Round(_originalHeight * scale)).ToString(CultureInfo.InvariantCulture);
        }
        _isUpdatingFromSlider = false;
    }

    private void DimensionBox_TextChanged(object? sender, TextChangedEventArgs e)
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

    private void QualitySlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (QualityText != null)
            QualityText.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
    }

    private async void ResizeButton_Click(object? sender, RoutedEventArgs e)
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
            ProgressPanel.IsVisible = true;
            ErrorBorder.IsVisible = false;
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
        ProgressPanel.IsVisible = false;
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

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
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

    private void AboutButton_Click(object? sender, RoutedEventArgs e) => ShowAbout();

    public void ShowAbout()
    {
        var dlg = new AboutWindow();
        _ = dlg.ShowDialog(this);
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasImageFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object? sender, DragEventArgs e)
    {
        if (_resizeInProgress) return;

        var files = ExtractImageFilePaths(e.Data);
        if (files.Count == 0) return;

        AddFiles(files);
        e.Handled = true;
    }

    private static bool HasImageFiles(IDataObject data)
        => ExtractImageFilePaths(data).Count > 0;

    private static List<string> ExtractImageFilePaths(IDataObject data)
    {
        var result = new List<string>();
        var files = data.GetFiles();
        if (files == null) return result;

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && IsImageFile(path))
                result.Add(path);
        }
        return result;
    }

    private void ShowInfo(string message)
    {
        ErrorHeading.Text = "Info";
        ErrorText.Text = message;
        ErrorBorder.IsVisible = true;
    }

    private void ShowError(string message)
    {
        ErrorHeading.Text = "Error";
        ErrorText.Text = message;
        ErrorBorder.IsVisible = true;
    }

    private static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    private void NumberValidationTextBox(object? sender, TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text) && NumberOnly.IsMatch(e.Text))
            e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
