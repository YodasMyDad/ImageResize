using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using ImageResize.Core.Interfaces;
using ImageResize.ContextMenu.Models;
using ImageResize.Models;
using Microsoft.Extensions.Logging;

namespace ImageResize.ContextMenu.Services;

public sealed class ImageProcessor(IImageResizerService resizerService, ILogger<ImageProcessor> logger)
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private static readonly int[] RetryDelaysMs = [100, 300, 800, 2000];

    public async Task<ImageInfo> GetImageInfoAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var codec = resizerService.GetCodec();
        var (width, height, contentType) = await codec.ProbeAsync(stream, ct).ConfigureAwait(false);

        return new ImageInfo
        {
            Width = width,
            Height = height,
            ContentType = contentType
        };
    }

    /// <summary>
    /// Resizes a batch of images in parallel, reporting per-file progress and isolating per-file
    /// failures so one broken image does not abort the batch. Respects
    /// <paramref name="ct"/> for user-initiated cancellation.
    /// </summary>
    public async Task ResizeImagesAsync(
        IReadOnlyList<string> imagePaths,
        ResizeSettings settings,
        IProgress<ResizeProgress> progress,
        CancellationToken ct = default)
    {
        var totalFiles = imagePaths.Count;
        var completed = 0;
        var successCount = 0;
        var failures = new ConcurrentBag<(string Path, string Reason)>();
        var started = Stopwatch.StartNew();

        var parallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(imagePaths, parallelOptions, async (imagePath, cancel) =>
        {
            var fileName = Path.GetFileName(imagePath);
            try
            {
                await ResizeSingleImageAsync(imagePath, settings, cancel).ConfigureAwait(false);
                Interlocked.Increment(ref successCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to resize image: {Path}", imagePath);
                var reason = IsTransientLock(ex)
                    ? "file is locked (OneDrive sync, antivirus, or preview pane may be holding it)"
                    : ex.Message;
                failures.Add((imagePath, reason));
            }
            finally
            {
                var current = Interlocked.Increment(ref completed);
                var elapsed = started.Elapsed;
                var eta = current > 0 && current < totalFiles
                    ? TimeSpan.FromTicks((long)(elapsed.Ticks * ((double)(totalFiles - current) / current)))
                    : TimeSpan.Zero;

                progress?.Report(new ResizeProgress
                {
                    CurrentFile = current,
                    TotalFiles = totalFiles,
                    FileName = fileName,
                    Elapsed = elapsed,
                    Eta = eta
                });
            }
        }).ConfigureAwait(false);

        if (!failures.IsEmpty)
            throw new BatchResizeException(totalFiles, successCount, [.. failures]);
    }

    private async Task ResizeSingleImageAsync(
        string imagePath,
        ResizeSettings settings,
        CancellationToken ct)
    {
        var outputPath = GetOutputPath(imagePath, settings.Overwrite);
        var fileName = Path.GetFileName(imagePath);

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
            await fs.CopyToAsync(inputData, ct).ConfigureAwait(false);
        }, $"read {fileName}", ct).ConfigureAwait(false);
        inputData.Position = 0;

        int targetWidth, targetHeight;
        if (settings.UsePercentage)
        {
            var codec = resizerService.GetCodec();
            var (w, h, _) = await codec.ProbeAsync(inputData, ct).ConfigureAwait(false);
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
            Quality: settings.Quality);

        using var result = await resizerService.ResizeAsync(inputData, null, resizeOptions, ct).ConfigureAwait(false);

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
                await result.Stream.CopyToAsync(outFs, ct).ConfigureAwait(false);
                await outFs.FlushAsync(ct).ConfigureAwait(false);
            }, $"write temp for {fileName}", ct).ConfigureAwait(false);

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
            }, $"rename to {fileName}", ct).ConfigureAwait(false);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException) { /* best effort */ }
            throw;
        }

        logger.LogInformation("Resized {Input} to {Output} ({Width}x{Height})",
            imagePath, outputPath, result.Width, result.Height);
    }

    private async Task WithRetryAsync(Func<Task> op, string context, CancellationToken ct)
    {
        // Retry transient sharing-violation IOExceptions using the configured backoff. On the
        // final attempt the `when` filter evaluates false, so the IOException propagates to the
        // caller untouched.
        for (var attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            try
            {
                await op().ConfigureAwait(false);
                return;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < RetryDelaysMs.Length)
            {
                var delay = RetryDelaysMs[attempt];
                logger.LogWarning(
                    "Transient lock on {Context} (attempt {Attempt}/{Max}): {Msg}. Retrying in {Delay}ms.",
                    context, attempt + 1, RetryDelaysMs.Length, ex.Message, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsSharingViolation(IOException ex)
        => ex.HResult == ErrorSharingViolation || ex.HResult == ErrorLockViolation;

    private static bool IsTransientLock(Exception ex)
        => ex is IOException io && IsSharingViolation(io);

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
