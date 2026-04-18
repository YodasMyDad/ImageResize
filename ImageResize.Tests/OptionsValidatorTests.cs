using ImageResize.Core.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

namespace ImageResize.Tests;

/// <summary>
/// Tests for <see cref="ImageResizeOptionsValidator"/>. The validator is internal but we can
/// exercise it via reflection or the same DI path consumers use; here we instantiate it directly
/// via <c>InternalsVisibleTo</c>-free reflection since the class is in the Core assembly.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class OptionsValidatorTests
{
    private static IValidateOptions<ImageResizeOptions> CreateValidator()
    {
        var type = typeof(ImageResizeOptions).Assembly
            .GetType("ImageResize.Core.Configuration.ImageResizeOptionsValidator", throwOnError: true)!;
        return (IValidateOptions<ImageResizeOptions>)Activator.CreateInstance(type)!;
    }

    private static ImageResizeOptions ValidOptions() => new()
    {
        DefaultQuality = 85,
        PngCompressionLevel = 6,
        MaxSourceBytes = 1024 * 1024,
        Bounds = new()
        {
            MinWidth = 16, MaxWidth = 4096,
            MinHeight = 16, MaxHeight = 4096,
            MinQuality = 10, MaxQuality = 100,
        },
        Cache = new() { MaxCacheBytes = 0, FolderSharding = 2 },
        ResponseCache = new() { ClientCacheSeconds = 3600 }
    };

    [Test]
    public void Validate_Defaults_AreAccepted()
    {
        var result = CreateValidator().Validate(null, ValidOptions());
        result.Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Validate_RejectsInvalidDefaultQuality()
    {
        var opts = ValidOptions();
        opts.DefaultQuality = 0;
        var result = CreateValidator().Validate(null, opts);
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DefaultQuality");
    }

    [Test]
    public void Validate_RejectsInvalidPngCompressionLevel()
    {
        var opts = ValidOptions();
        opts.PngCompressionLevel = 10;
        var result = CreateValidator().Validate(null, opts);
        result.Failed.ShouldBeTrue();
    }

    [Test]
    public void Validate_RejectsNegativeMaxSourceBytes()
    {
        var opts = ValidOptions();
        opts.MaxSourceBytes = -1;
        var result = CreateValidator().Validate(null, opts);
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxSourceBytes");
    }

    [Test]
    public void Validate_RejectsBoundsWithMaxBelowMin()
    {
        var opts = ValidOptions();
        opts.Bounds.MaxWidth = 10;
        opts.Bounds.MinWidth = 100;
        var result = CreateValidator().Validate(null, opts);
        result.Failed.ShouldBeTrue();
    }

    [Test]
    public void Validate_RejectsNegativeCacheMaxBytes()
    {
        var opts = ValidOptions();
        opts.Cache.MaxCacheBytes = -1;
        var result = CreateValidator().Validate(null, opts);
        result.Failed.ShouldBeTrue();
    }
}
