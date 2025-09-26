using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ImageResize.Example.Pages;

public class IndexModel : PageModel
{
    public List<ResizedImageExample>? ResizedImages { get; set; }

    public class ResizedImageExample
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required int Width { get; set; }
        public required int Quality { get; set; }
        public required string Url { get; set; }
        public required string SizeLabel { get; set; }
    }

    public void OnGet()
    {
        ResizedImages = new List<ResizedImageExample>
        {
            new ResizedImageExample
            {
                Title = "Thumbnail",
                Description = "Small 200px wide image, perfect for previews and galleries",
                Width = 200,
                Quality = 85,
                Url = "/media/sample.jpg?width=200&quality=85",
                SizeLabel = "~200x150"
            },
            new ResizedImageExample
            {
                Title = "Medium",
                Description = "Balanced 300px wide image, good for blog posts and content",
                Width = 300,
                Quality = 90,
                Url = "/media/sample.jpg?width=300&quality=90",
                SizeLabel = "~300x450"
            },
            new ResizedImageExample
            {
                Title = "Large",
                Description = "High-quality 1200px wide image, ideal for detailed viewing",
                Width = 1200,
                Quality = 95,
                Url = "/media/sample.jpg?width=1200&quality=95",
                SizeLabel = "~1200x900"
            }
        };
    }
}