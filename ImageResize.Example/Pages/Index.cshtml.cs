using ImageResize.Core.Extensions;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Models;
using ImageResize.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ImageResize.Example.Pages;

public class IndexModel(
    ILogger<IndexModel> logger,
    IImageResizerService imageResizerService,
    IWebHostEnvironment environment)
    : PageModel
{
    public List<ResizedImageExample>? ResizedImages { get; set; }
    public string? ErrorMessage { get; set; }

    public class ResizedImageExample
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required ImageResult Image { get; set; }
        public required string SizeLabel { get; set; }
    }

    public async Task OnGet()
    {
        try
        {
            // Get the path to the sample image
            var imagePath = Path.Combine(environment.WebRootPath, "images", "sample.jpg");

            ResizedImages = new List<ResizedImageExample>();

            // Define the different resize options
            var resizeOptions = new[]
            {
                new { Title = "Thumbnail", Description = "Small 200px wide image, perfect for previews and galleries", Width = 200, Quality = 85 },
                new { Title = "Medium", Description = "Balanced 600px wide image, good for blog posts and content", Width = 600, Quality = 90 },
                new { Title = "Large", Description = "High-quality 1200px wide image, ideal for detailed viewing", Width = 1200, Quality = 95 }
            };

            foreach (var option in resizeOptions)
            {
                // Open the image file for each resize operation
                await using var fileStream = System.IO.File.OpenRead(imagePath);

                // Resize the image to the specified width (maintains aspect ratio)
                var imageResult = await imageResizerService.ResizeToWidthAsync(
                    fileStream,
                    option.Width,
                    option.Quality);

                var sizeLabel = $"{imageResult.Width}x{imageResult.Height}";

                ResizedImages.Add(new ResizedImageExample
                {
                    Title = option.Title,
                    Description = option.Description,
                    Image = imageResult,
                    SizeLabel = sizeLabel
                });

                logger.LogInformation("Successfully created {Title} version: {Width}x{Height}, {Size} bytes",
                    option.Title, imageResult.Width, imageResult.Height, imageResult.FileSize);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error processing images: {ex.Message}";
            logger.LogError(ex, "Error processing images");
        }
    }
}