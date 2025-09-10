using ImageResize.Codecs.Skia;
using NUnit.Framework;
using Shouldly;

namespace ImageResize.Tests;

/// <summary>
/// Tests for resize math and aspect ratio calculations.
/// </summary>
[TestFixture]
public class ResizeMathTests
{
    [TestCase(4000, 3000, 800, null, 800, 600)]
    [TestCase(4000, 3000, 800, 800, 800, 600)]
    [TestCase(800, 600, 1600, null, 800, 600)] // no upscale
    [TestCase(1000, 1000, null, 500, 500, 500)]
    [TestCase(1920, 1080, 800, 600, 800, 450)]
    [TestCase(500, 1000, 250, null, 250, 500)]
    public void Fit_CalculatesExpected(int srcW, int srcH, int? reqW, int? reqH, int expW, int expH)
    {
        var (outW, outH) = Fit(srcW, srcH, reqW, reqH, allowUpscale: false);
        outW.ShouldBe(expW);
        outH.ShouldBe(expH);
    }

    [TestCase(800, 600, 1600, null, 1600, 1200)] // with upscale
    [TestCase(400, 300, 800, 600, 800, 600)]
    public void Fit_WithUpscale_CalculatesExpected(int srcW, int srcH, int? reqW, int? reqH, int expW, int expH)
    {
        var (outW, outH) = Fit(srcW, srcH, reqW, reqH, allowUpscale: true);
        outW.ShouldBe(expW);
        outH.ShouldBe(expH);
    }

    [TestCase(100, 0)]
    [TestCase(0, 9)]
    [TestCase(50, 4)]
    public void MapQualityToPngLevel_MapsCorrectly(int quality, int expectedLevel)
    {
        var level = MapQualityToPngLevel(quality);
        level.ShouldBe(expectedLevel);
    }

    // Extracted from SkiaCodec for testing
    private static (int outW, int outH) Fit(int srcW, int srcH, int? reqW, int? reqH, bool allowUpscale)
    {
        double scaleW = reqW.HasValue ? (double)reqW.Value / srcW : double.PositiveInfinity;
        double scaleH = reqH.HasValue ? (double)reqH.Value / srcH : double.PositiveInfinity;

        double scale = Math.Min(scaleW, scaleH);
        if (double.IsInfinity(scale))
            scale = reqW.HasValue ? scaleW : scaleH;
        if (!allowUpscale)
            scale = Math.Min(scale, 1.0);

        int outW = Math.Max(1, (int)Math.Round(srcW * scale));
        int outH = Math.Max(1, (int)Math.Round(srcH * scale));
        return (outW, outH);
    }

    private static int MapQualityToPngLevel(int quality)
    {
        // Map 1..100 quality to 0..9 compression (simple linear mapping)
        return Math.Clamp((100 - quality) / 11, 0, 9);
    }
}
