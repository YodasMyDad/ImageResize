using ImageResize.Core.Interfaces;
using ImageResize.ContextMenu.Models;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using System.IO;

namespace ImageResize.ContextMenu.Services;

public sealed class ImageProcessor(IImageResizerService resizerService, ILogger<ImageProcessor> logger)
{
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
                await ResizeSingleImageAsync(imagePath, settings);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to resize image: {Path}", imagePath);
                throw new InvalidOperationException($"Failed to resize {Path.GetFileName(imagePath)}: {ex.Message}", ex);
            }
        }
    }

    private async Task ResizeSingleImageAsync(string imagePath, ResizeSettings settings)
    {
        var outputPath = GetOutputPath(imagePath, settings.Overwrite);

        // Calculate target dimensions
        int targetWidth, targetHeight;

        if (settings.UsePercentage)
        {
            // Get original dimensions for this specific image
            var imageInfo = await GetImageInfoAsync(imagePath);
            var scale = settings.Percentage / 100.0;
            targetWidth = (int)Math.Round(imageInfo.Width * scale);
            targetHeight = (int)Math.Round(imageInfo.Height * scale);
        }
        else
        {
            // Use exact dimensions
            targetWidth = settings.TargetWidth;
            targetHeight = settings.TargetHeight;
        }

        // Read and resize image
        await using var inputStream = File.OpenRead(imagePath);
        
        var resizeOptions = new ResizeOptions(
            Width: targetWidth,
            Height: targetHeight,
            Quality: settings.Quality
        );

        using var result = await resizerService.ResizeAsync(inputStream, null, resizeOptions, CancellationToken.None);

        // Write to output
        await using var outputStream = File.Create(outputPath);
        result.Stream.Position = 0;
        await result.Stream.CopyToAsync(outputStream);

        logger.LogInformation("Resized {Input} to {Output} ({Width}x{Height})",
            imagePath, outputPath, result.Width, result.Height);
    }

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

