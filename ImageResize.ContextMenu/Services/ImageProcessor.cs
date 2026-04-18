using ImageResize.Core.Interfaces;
using ImageResize.ContextMenu.Models;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using System.IO;

namespace ImageResize.ContextMenu.Services;

public sealed class ImageProcessor(IImageResizerService resizerService, ILogger<ImageProcessor> logger)
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private static readonly int[] RetryDelaysMs = [100, 300, 800, 2000];

    public async Task<ImageInfo> GetImageInfoAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var codec = resizerService.GetCodec();
        var (width, height, contentType) = await codec.ProbeAsync(stream, CancellationToken.None);

        return new ImageInfo
        {
            Width = width,
            Height = height,
            ContentType = contentType
        };
    }

    public async Task ResizeImagesAsync(
        List<string> imagePaths,
        ResizeSettings settings,
        IProgress<ResizeProgress> progress)
    {
        var totalFiles = imagePaths.Count;
        var currentFile = 0;
        var failures = new List<(string Path, string Reason)>();
        var successCount = 0;

        foreach (var imagePath in imagePaths)
        {
            currentFile++;

            progress?.Report(new ResizeProgress
            {
                CurrentFile = currentFile,
                TotalFiles = totalFiles,
                FileName = Path.GetFileName(imagePath)
            });

            try
            {
                await ResizeSingleImageAsync(imagePath, settings, currentFile, totalFiles, progress);
                successCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to resize image: {Path}", imagePath);
                var reason = IsTransientLock(ex)
                    ? "file is locked (OneDrive sync, antivirus, or preview pane may be holding it)"
                    : ex.Message;
                failures.Add((imagePath, reason));
            }
        }

        if (failures.Count > 0)
            throw new BatchResizeException(totalFiles, successCount, failures);
    }

    private async Task ResizeSingleImageAsync(
        string imagePath,
        ResizeSettings settings,
        int currentFile,
        int totalFiles,
        IProgress<ResizeProgress>? progress)
    {
        var outputPath = GetOutputPath(imagePath, settings.Overwrite);
        var fileName = Path.GetFileName(imagePath);

        // 1. Read the input once into memory (share-tolerant open + retry on transient locks).
        var inputData = new MemoryStream();
        await WithRetryAsync(async () =>
        {
            inputData.SetLength(0);
            await using var fs = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            await fs.CopyToAsync(inputData);
        }, $"read {fileName}", currentFile, totalFiles, fileName, progress);
        inputData.Position = 0;

        // 2. Compute target dimensions (probe from buffered data if percentage mode).
        int targetWidth, targetHeight;
        if (settings.UsePercentage)
        {
            var codec = resizerService.GetCodec();
            var (w, h, _) = await codec.ProbeAsync(inputData, CancellationToken.None);
            inputData.Position = 0;
            var scale = settings.Percentage / 100.0;
            targetWidth = (int)Math.Round(w * scale);
            targetHeight = (int)Math.Round(h * scale);
        }
        else
        {
            targetWidth = settings.TargetWidth;
            targetHeight = settings.TargetHeight;
        }

        var resizeOptions = new ResizeOptions(
            Width: targetWidth,
            Height: targetHeight,
            Quality: settings.Quality
        );

        using var result = await resizerService.ResizeAsync(inputData, null, resizeOptions, CancellationToken.None);

        // 3. Atomic write: temp file in the same directory, then File.Move(overwrite: true).
        var dir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var tempPath = Path.Combine(
            dir,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.imgresize-tmp");

        try
        {
            await WithRetryAsync(async () =>
            {
                await using var outFs = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                result.Stream.Position = 0;
                await result.Stream.CopyToAsync(outFs);
                await outFs.FlushAsync();
            }, $"write temp for {fileName}", currentFile, totalFiles, fileName, progress);

            await WithRetryAsync(() =>
            {
                if (File.Exists(outputPath))
                {
                    var attrs = File.GetAttributes(outputPath);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(outputPath, attrs & ~FileAttributes.ReadOnly);
                }
                File.Move(tempPath, outputPath, overwrite: true);
                return Task.CompletedTask;
            }, $"rename to {fileName}", currentFile, totalFiles, fileName, progress);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }

        logger.LogInformation("Resized {Input} to {Output} ({Width}x{Height})",
            imagePath, outputPath, result.Width, result.Height);
    }

    private async Task WithRetryAsync(
        Func<Task> op,
        string context,
        int currentFile,
        int totalFiles,
        string fileName,
        IProgress<ResizeProgress>? progress)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await op();
                return;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < RetryDelaysMs.Length)
            {
                var delay = RetryDelaysMs[attempt];
                logger.LogWarning(
                    "Transient lock on {Context} (attempt {Attempt}/{Max}): {Msg}. Retrying in {Delay}ms.",
                    context, attempt + 1, RetryDelaysMs.Length, ex.Message, delay);

                progress?.Report(new ResizeProgress
                {
                    CurrentFile = currentFile,
                    TotalFiles = totalFiles,
                    FileName = $"{fileName} (retrying {attempt + 1}/{RetryDelaysMs.Length})"
                });

                await Task.Delay(delay);
            }
        }
    }

    private static bool IsSharingViolation(IOException ex) =>
        ex.HResult == ErrorSharingViolation || ex.HResult == ErrorLockViolation;

    private static bool IsTransientLock(Exception ex) =>
        ex is IOException io && IsSharingViolation(io);

    private static string GetOutputPath(string originalPath, bool overwrite)
    {
        if (overwrite)
            return originalPath;

        var directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);

        var counter = 1;
        string newPath;

        do
        {
            var newFileName = $"{fileNameWithoutExtension} ({counter}){extension}";
            newPath = Path.Combine(directory, newFileName);
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }
}
