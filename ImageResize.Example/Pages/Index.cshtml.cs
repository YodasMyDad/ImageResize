using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ImageResize.Example.Pages;

public class IndexModel(IWebHostEnvironment env, ILogger<IndexModel> logger) : PageModel
{
    public List<ResizedImageExample>? ResizedImages { get; set; }
    public string? UploadError { get; set; }
    public bool IsCustomImage { get; set; }

    [BindProperty]
    public IFormFile? UploadedImage { get; set; }

    public class ResizedImageExample
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required int Width { get; set; }
        public required int Quality { get; set; }
        public required string Url { get; set; }
        public required string SizeLabel { get; set; }
    }

    public void OnGet(string? image)
    {
        var imagePath = GetImagePath(image);
        IsCustomImage = !string.IsNullOrEmpty(image);

        ResizedImages = new List<ResizedImageExample>
        {
            new ResizedImageExample
            {
                Title = "Thumbnail",
                Description = "Small 200px wide image, perfect for previews and galleries",
                Width = 200,
                Quality = 85,
                Url = $"{imagePath}?width=200&quality=85",
                SizeLabel = "~200x150"
            },
            new ResizedImageExample
            {
                Title = "Medium",
                Description = "Balanced 300px wide image, good for blog posts and content",
                Width = 300,
                Quality = 90,
                Url = $"{imagePath}?width=300&quality=90",
                SizeLabel = "~300x450"
            },
            new ResizedImageExample
            {
                Title = "Large",
                Description = "High-quality 1200px wide image, ideal for detailed viewing",
                Width = 1200,
                Quality = 95,
                Url = $"{imagePath}?width=1200&quality=95",
                SizeLabel = "~1200x900"
            }
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (UploadedImage == null || UploadedImage.Length == 0)
        {
            UploadError = "Please select an image file to upload.";
            OnGet(null);
            return Page();
        }

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff" };
        var extension = Path.GetExtension(UploadedImage.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
        {
            UploadError = "Invalid file type. Please upload a valid image file.";
            OnGet(null);
            return Page();
        }

        if (UploadedImage.Length > 10 * 1024 * 1024) // 10MB limit
        {
            UploadError = "File size exceeds 10MB limit.";
            OnGet(null);
            return Page();
        }

        try
        {
            var uploadsPath = Path.Combine(env.WebRootPath, "images", "uploads");
            Directory.CreateDirectory(uploadsPath);

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsPath, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await UploadedImage.CopyToAsync(stream);
            }

            logger.LogInformation("Image uploaded: {FileName}", fileName);
            return RedirectToPage(new { image = $"uploads/{fileName}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading image");
            UploadError = "An error occurred while uploading the image. Please try again.";
            OnGet(null);
            return Page();
        }
    }

    public IActionResult OnPostReset()
    {
        // Clear any uploaded images from session and return to default
        return RedirectToPage();
    }

    private static string GetImagePath(string? image)
    {
        return string.IsNullOrEmpty(image) ? "/images/sample.jpg" : $"/images/{image}";
    }
}