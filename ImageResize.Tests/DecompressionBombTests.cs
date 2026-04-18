using ImageResize.Core.Codecs;
using ImageResize.Core.Configuration;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace ImageResize.Tests;

/// <summary>
/// Validates that <see cref="ImageResizeOptions.MaxSourceBytes"/> rejects oversize inputs before
/// the decoder gets a chance to allocate.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class DecompressionBombTests
{
    private static SkiaCodec MakeCodec(long maxSourceBytes)
    {
        var opts = new ImageResizeOptions { MaxSourceBytes = maxSourceBytes };
        var mock = new Mock<IOptions<ImageResizeOptions>>();
        mock.Setup(x => x.Value).Returns(opts);
        return new SkiaCodec(mock.Object, Mock.Of<ILogger<SkiaCodec>>());
    }

    [Test]
    public async Task Probe_RejectsInputLargerThanMaxSourceBytes()
    {
        var codec = MakeCodec(maxSourceBytes: 1024);
        using var oversize = new MemoryStream(new byte[2048]);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await codec.ProbeAsync(oversize, CancellationToken.None));

        ex.Message.ShouldContain("MaxSourceBytes");
    }

    [Test]
    public async Task Resize_RejectsInputLargerThanMaxSourceBytes()
    {
        var codec = MakeCodec(maxSourceBytes: 512);
        using var oversize = new MemoryStream(new byte[4096]);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await codec.ResizeAsync(
                oversize, null, new ResizeOptions(64, 64, 80), CancellationToken.None));
    }

    [Test]
    public async Task Probe_AllowsValidSmallImage_WhenUnderCap()
    {
        var codec = MakeCodec(maxSourceBytes: 10 * 1024 * 1024);

        using var bitmap = new SkiaSharp.SKBitmap(64, 64);
        using var img = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
        using var stream = new MemoryStream(data.ToArray());

        var (w, h, ct) = await codec.ProbeAsync(stream, CancellationToken.None);

        w.ShouldBe(64);
        h.ShouldBe(64);
        ct.ShouldBe("image/jpeg");
    }

    [Test]
    public async Task MaxSourceBytesZero_DisablesTheCheck()
    {
        var codec = MakeCodec(maxSourceBytes: 0);

        using var bitmap = new SkiaSharp.SKBitmap(16, 16);
        using var img = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        using var stream = new MemoryStream(data.ToArray());

        var (w, h, _) = await codec.ProbeAsync(stream, CancellationToken.None);
        w.ShouldBe(16);
        h.ShouldBe(16);
    }
}
