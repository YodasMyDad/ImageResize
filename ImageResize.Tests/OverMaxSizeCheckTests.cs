using ImageResize.Core.Codecs;
using ImageResize.Core.Configuration;
using ImageResize.Core.Extensions;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Services;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace ImageResize.Tests;

/// <summary>
/// Tests for the OverMaxSizeCheckAsync extension method.
/// </summary>
[TestFixture]
public class OverMaxSizeCheckTests
{
    private ImageResizeOptions _options = null!;
    private Mock<IOptions<ImageResizeOptions>> _optionsMock = null!;
    private Mock<ILogger<SkiaCodec>> _codecLogger = null!;
    private Mock<ILogger<ImageResizerService>> _serviceLogger = null!;
    private SkiaCodec _codec = null!;
    private ImageResizerService _service = null!;

    [SetUp]
    public void Setup()
    {
        _options = new ImageResizeOptions
        {
            ContentRoot = Path.GetTempPath(),
            CacheRoot = Path.Combine(Path.GetTempPath(), "cache"),
            AllowUpscale = false,
            DefaultQuality = 85,
            PngCompressionLevel = 6
        };

        _optionsMock = new Mock<IOptions<ImageResizeOptions>>();
        _optionsMock.Setup(x => x.Value).Returns(_options);

        _codecLogger = new Mock<ILogger<SkiaCodec>>();
        _serviceLogger = new Mock<ILogger<ImageResizerService>>();

        _codec = new SkiaCodec(_optionsMock.Object, _codecLogger.Object);
        _service = new ImageResizerService(_optionsMock.Object, Mock.Of<IImageCache>(), _codec, _serviceLogger.Object);
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithSmallImage_ReturnsOriginalImage()
    {
        // Arrange - Create a small test image (100x100 pixels = 10,000 pixels total)
        using var bitmap = new SkiaSharp.SKBitmap(100, 100);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Set max pixel size to 50,000 (larger than our 10,000 pixel image)
        var result = await stream.OverMaxSizeCheckByPixelCountAsync(50_000, _service);

        // Assert
        result.ShouldNotBeNull();
        result.Width.ShouldBe(100);
        result.Height.ShouldBe(100);
        result.OriginalWidth.ShouldBe(100);
        result.OriginalHeight.ShouldBe(100);
        result.WasResized.ShouldBeFalse();
        result.ContentType.ShouldBe("image/jpeg");
        result.Format.ShouldBe("JPEG");
        result.PixelCount.ShouldBe(10_000);
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithLargeImage_ResizesDown()
    {
        // Arrange - Create a large test image (2000x1500 pixels = 3,000,000 pixels total)
        using var bitmap = new SkiaSharp.SKBitmap(2000, 1500);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Set max pixel size to 1,000,000 pixels
        var result = await stream.OverMaxSizeCheckByPixelCountAsync(1_000_000, _service);

        // Assert
        result.ShouldNotBeNull();
        result.OriginalWidth.ShouldBe(2000);
        result.OriginalHeight.ShouldBe(1500);
        result.WasResized.ShouldBeTrue();
        result.ContentType.ShouldBe("image/jpeg");
        result.Format.ShouldBe("JPEG");

        // Check aspect ratio is maintained (4:3)
        var aspectRatio = (double)result.Width / result.Height;
        aspectRatio.ShouldBeInRange(1.32, 1.34); // ~4:3 aspect ratio

        // Total pixels should be <= 1,000,000
        result.PixelCount.ShouldBeLessThanOrEqualTo(1_000_000);
        
        // Image should be smaller than original
        result.Width.ShouldBeLessThan(2000);
        result.Height.ShouldBeLessThan(1500);
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithLandscapeImage_MaintainsAspectRatio()
    {
        // Arrange - Create a wide landscape image (1600x900 pixels)
        using var bitmap = new SkiaSharp.SKBitmap(1600, 900);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Set max pixel size to 500,000 pixels
        var result = await stream.OverMaxSizeCheckByPixelCountAsync(500_000, _service);

        // Assert
        result.ShouldNotBeNull();
        result.OriginalWidth.ShouldBe(1600);
        result.OriginalHeight.ShouldBe(900);
        result.WasResized.ShouldBeTrue();

        // Check aspect ratio is maintained (approximately 16:9)
        var aspectRatio = (double)result.Width / result.Height;
        aspectRatio.ShouldBeInRange(1.77, 1.79); // ~16:9 aspect ratio

        // Total pixels should be <= 500,000
        result.PixelCount.ShouldBeLessThanOrEqualTo(500_000);
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithPortraitImage_MaintainsAspectRatio()
    {
        // Arrange - Create a tall portrait image (900x1600 pixels)
        using var bitmap = new SkiaSharp.SKBitmap(900, 1600);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Set max pixel size to 500,000 pixels
        var result = await stream.OverMaxSizeCheckByPixelCountAsync(500_000, _service);

        // Assert
        result.ShouldNotBeNull();
        result.OriginalWidth.ShouldBe(900);
        result.OriginalHeight.ShouldBe(1600);
        result.WasResized.ShouldBeTrue();

        // Check aspect ratio is maintained (approximately 9:16)
        var aspectRatio = (double)result.Height / result.Width;
        aspectRatio.ShouldBeInRange(1.77, 1.79); // ~16:9 aspect ratio (tall)

        // Total pixels should be <= 500,000
        result.PixelCount.ShouldBeLessThanOrEqualTo(500_000);
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithPngImage_WorksCorrectly()
    {
        // Arrange - Create a PNG test image
        using var bitmap = new SkiaSharp.SKBitmap(800, 600);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Set max pixel size to 200,000 pixels (smaller than 800x600=480,000)
        var result = await stream.OverMaxSizeCheckByPixelCountAsync(200_000, _service);

        // Assert
        result.ShouldNotBeNull();
        result.OriginalWidth.ShouldBe(800);
        result.OriginalHeight.ShouldBe(600);
        result.WasResized.ShouldBeTrue();
        result.ContentType.ShouldBe("image/png");
        result.Format.ShouldBe("PNG");

        // Total pixels should be <= 200,000
        result.PixelCount.ShouldBeLessThanOrEqualTo(200_000);
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_StreamPositionReset_AfterProcessing()
    {
        // Arrange - Create a test image
        using var bitmap = new SkiaSharp.SKBitmap(500, 500);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        var stream = new MemoryStream(data.ToArray());

        // Set position to middle of stream
        stream.Position = data.ToArray().Length / 2;

        // Act
        var result = await stream.OverMaxSizeCheckByPixelCountAsync(1_000_000, _service);

        // Assert - Stream should be reset to beginning after processing
        stream.Position.ShouldBe(0);
        result.ShouldNotBeNull();
        result.WasResized.ShouldBeFalse(); // 500x500 = 250,000 pixels, less than 1M limit
    }

    [Test]
    public void OverMaxSizeCheckAsync_WithNullService_ThrowsArgumentNullException()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }); // Minimal JPEG header

        // Act & Assert
        Should.ThrowAsync<ArgumentNullException>(async () =>
            await stream.OverMaxSizeCheckByPixelCountAsync(100_000, null!));
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithMaxDimension_SmallImage_ReturnsOriginal()
    {
        // Arrange - Create a 900x600 image
        using var bitmap = new SkiaSharp.SKBitmap(900, 600);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Max dimension 1500px (larger than both dimensions)
        var result = await stream.OverMaxSizeCheckAsync(1500, _service);

        // Assert - Should NOT resize
        result.ShouldNotBeNull();
        result.Width.ShouldBe(900);
        result.Height.ShouldBe(600);
        result.OriginalWidth.ShouldBe(900);
        result.OriginalHeight.ShouldBe(600);
        result.WasResized.ShouldBeFalse();
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithMaxDimension_LargeImage_Resizes()
    {
        // Arrange - Create a 2000x1500 image
        using var bitmap = new SkiaSharp.SKBitmap(2000, 1500);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Max dimension 1000px (smaller than both dimensions)
        var result = await stream.OverMaxSizeCheckAsync(1000, _service);

        // Assert - Should resize
        result.ShouldNotBeNull();
        result.OriginalWidth.ShouldBe(2000);
        result.OriginalHeight.ShouldBe(1500);
        result.WasResized.ShouldBeTrue();
        
        // The larger dimension should be constrained to ~1000px
        result.Width.ShouldBeLessThanOrEqualTo(1000);
        result.Height.ShouldBeLessThanOrEqualTo(1000);
        
        // Aspect ratio should be maintained (4:3)
        var aspectRatio = (double)result.Width / result.Height;
        aspectRatio.ShouldBeInRange(1.32, 1.34);
    }

    [Test]
    public async Task OverMaxSizeCheckAsync_WithMaxDimension_PortraitImage_Resizes()
    {
        // Arrange - Create a 1200x1800 portrait image
        using var bitmap = new SkiaSharp.SKBitmap(1200, 1800);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        using var stream = new MemoryStream(data.ToArray());

        // Act - Max dimension 1000px
        var result = await stream.OverMaxSizeCheckAsync(1000, _service);

        // Assert - Should resize, height is the larger dimension
        result.ShouldNotBeNull();
        result.OriginalWidth.ShouldBe(1200);
        result.OriginalHeight.ShouldBe(1800);
        result.WasResized.ShouldBeTrue();
        
        // Both dimensions should be <= 1000px
        result.Width.ShouldBeLessThanOrEqualTo(1000);
        result.Height.ShouldBeLessThanOrEqualTo(1000);
        
        // Aspect ratio should be maintained (2:3)
        var aspectRatio = (double)result.Height / result.Width;
        aspectRatio.ShouldBeInRange(1.49, 1.51);
    }
}
