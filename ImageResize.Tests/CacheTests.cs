using ImageResize.Cache;
using ImageResize.Configuration;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace ImageResize.Tests;

/// <summary>
/// Tests for cache functionality.
/// </summary>
[TestFixture]
public class CacheTests
{
    private ImageResizeOptions _options = null!;
    private Mock<IOptions<ImageResizeOptions>> _optionsMock = null!;
    private Mock<ILogger<FileSystemImageCache>> _logger = null!;
    private FileSystemImageCache _cache = null!;

    [SetUp]
    public void Setup()
    {
        _options = new ImageResizeOptions
        {
            CacheRoot = "/tmp/cache",
            AllowUpscale = false,
            DefaultQuality = 80,
            Cache = new ImageResizeOptions.CacheOptions { FolderSharding = 2 }
        };

        _optionsMock = new Mock<IOptions<ImageResizeOptions>>();
        _optionsMock.Setup(x => x.Value).Returns(_options);

        _logger = new Mock<ILogger<FileSystemImageCache>>();
        _cache = new FileSystemImageCache(_optionsMock.Object, _logger.Object);
    }

    [Test]
    public void GetCachedFilePath_GeneratesConsistentKeys()
    {
        var relPath = "photos/cat.jpg";
        var options = new ResizeOptions(Width: 800, Height: 600, Quality: 85);
        var signature = "123456789:1024";

        var path1 = _cache.GetCachedFilePath(relPath, options, signature);
        var path2 = _cache.GetCachedFilePath(relPath, options, signature);

        path1.ShouldBe(path2);
        path1.ShouldEndWith(".jpg");
    }

    [Test]
    public void GetCachedFilePath_IncludesSharding()
    {
        var relPath = "test.png";
        var options = new ResizeOptions(Width: 400, Height: null, Quality: null);
        var signature = "987654321:2048";

        var path = _cache.GetCachedFilePath(relPath, options, signature);

        // Should have sharding structure like aa/bb/hash.png
        var parts = path.Split(Path.DirectorySeparatorChar);
        parts.Length.ShouldBeGreaterThan(1);
        parts[^1].ShouldEndWith(".png");
        parts[^1].Length.ShouldBeGreaterThan(6); // hash + .png
    }

    [Test]
    public void GetCachedFilePath_DifferentOptions_ProduceDifferentKeys()
    {
        var relPath = "photos/cat.jpg";
        var options1 = new ResizeOptions(Width: 800, Height: null, Quality: 80);
        var options2 = new ResizeOptions(Width: 800, Height: null, Quality: 85);
        var signature = "123456789:1024";

        var path1 = _cache.GetCachedFilePath(relPath, options1, signature);
        var path2 = _cache.GetCachedFilePath(relPath, options2, signature);

        path1.ShouldNotBe(path2);
    }

    [Test]
    public void GetCachedFilePath_DifferentSignature_ProduceDifferentKeys()
    {
        var relPath = "photos/cat.jpg";
        var options = new ResizeOptions(Width: 800, Height: null, Quality: 80);
        var signature1 = "123456789:1024";
        var signature2 = "123456789:1025"; // Different size

        var path1 = _cache.GetCachedFilePath(relPath, options, signature1);
        var path2 = _cache.GetCachedFilePath(relPath, options, signature2);

        path1.ShouldNotBe(path2);
    }
}
