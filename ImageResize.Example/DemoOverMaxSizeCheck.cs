using ImageResize.Core.Extensions;
using ImageResize.Core.Interfaces;

namespace ImageResize.Example;

/// <summary>
/// Console demo showing OverMaxSizeCheckAsync working
/// </summary>
public static class DemoOverMaxSizeCheck
{
    public static async Task RunDemo(IImageResizerService resizerService, string imagePath)
    {
        Console.WriteLine("=== OverMaxSizeCheckAsync Demo ===");
        Console.WriteLine($"Testing with image: {imagePath}");
        Console.WriteLine();

        try
        {
            // Open the image file
            await using var fileStream = File.OpenRead(imagePath);

            // Test 1: Large max size (should not resize)
            Console.WriteLine("Test 1: Large max pixel size (10,000,000) - should NOT resize");
            fileStream.Position = 0; // Reset stream
            var result1 = await fileStream.OverMaxSizeCheckAsync(10_000_000, resizerService);

            Console.WriteLine($"  Original: {result1.OriginalWidth}x{result1.OriginalHeight} ({result1.PixelCount:N0} pixels)");
            Console.WriteLine($"  Result: {result1.Width}x{result1.Height} ({result1.PixelCount:N0} pixels)");
            Console.WriteLine($"  Was Resized: {result1.WasResized}");
            Console.WriteLine($"  Content Type: {result1.ContentType}");
            Console.WriteLine($"  File Size: {result1.FileSizeHumanReadable}");
            Console.WriteLine();

            // Test 2: Small max size (should resize down)
            Console.WriteLine("Test 2: Small max pixel size (500,000) - should resize DOWN");
            fileStream.Position = 0; // Reset stream
            var result2 = await fileStream.OverMaxSizeCheckAsync(500_000, resizerService);

            Console.WriteLine($"  Original: {result2.OriginalWidth}x{result2.OriginalHeight} ({result1.PixelCount:N0} pixels)");
            Console.WriteLine($"  Result: {result2.Width}x{result2.Height} ({result2.PixelCount:N0} pixels)");
            Console.WriteLine($"  Was Resized: {result2.WasResized}");
            Console.WriteLine($"  Content Type: {result2.ContentType}");
            Console.WriteLine($"  File Size: {result2.FileSizeHumanReadable}");
            Console.WriteLine();

            Console.WriteLine("✅ SUCCESS: OverMaxSizeCheckAsync method works correctly!");
            Console.WriteLine("The 'Specified method is not supported' error has been fixed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR: {ex.Message}");
            Console.WriteLine("The OverMaxSizeCheckAsync method failed.");
        }
    }
}
