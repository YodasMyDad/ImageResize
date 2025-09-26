using ImageResize.Core.Extensions;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ImageResize.Example.Pages;

public class IndexModel(
    ILogger<IndexModel> logger,
    IImageResizerService imageResizerService,
    IWebHostEnvironment environment)
    : PageModel
{
    public ImageResult? ProcessedImage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        try
        {
            // Get the path to the sample image
            var imagePath = Path.Combine(environment.WebRootPath, "images", "sample.jpg");
            
            // Open the image file
            await using var fileStream = System.IO.File.OpenRead(imagePath);

            // Use OverMaxSizeCheckAsync to check and resize if needed
            // Setting max pixel size to 1,000,000 pixels (e.g., 1000x1000)
            ProcessedImage = await fileStream.OverMaxSizeCheckAsync(
                maxPixelSize: 1_000_000,
                resizerService: imageResizerService);

            logger.LogInformation("Successfully processed image: {Width}x{Height}, {Size} bytes",
                ProcessedImage.Width, ProcessedImage.Height, ProcessedImage.FileSize);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error processing image: {ex.Message}";
            logger.LogError(ex, "Error processing image");
        }
    }
}