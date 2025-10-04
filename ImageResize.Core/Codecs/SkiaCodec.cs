using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ImageResize.Core.Codecs;

/// <summary>
/// SkiaSharp-based image codec implementation.
/// </summary>
public sealed class SkiaCodec(IOptions<ImageResizeOptions> options, ILogger<SkiaCodec> logger) : IImageCodec
{
    /// <inheritdoc />
    public async Task<(int Width, int Height, string ContentType)> ProbeAsync(Stream input, CancellationToken ct)
    {
        using var ms = await CopyToMemoryStream(input, ct);
        using var codec = SKCodec.Create(ms);

        if (codec == null)
            throw new InvalidOperationException("Unable to decode image");

        var info = codec.Info;
        var mime = MimeFromEncodedFormat(codec.EncodedFormat);

        return (info.Width, info.Height, mime);
    }

    /// <inheritdoc />
    public async Task<(Stream Output, string ContentType, int OutW, int OutH)> ResizeAsync(
        Stream input,
        string? originalContentType,
        ResizeOptions options1,
        CancellationToken ct)
    {
        using var ms = await CopyToMemoryStream(input, ct);
        using var codec = SKCodec.Create(ms);

        if (codec == null)
            throw new InvalidOperationException("Unable to decode image");

        var info = codec.Info;
        var (outW, outH) = Fit(info.Width, info.Height, options1.Width, options1.Height, options.Value.AllowUpscale);

        // Reset stream position after codec creation
        ms.Position = 0;
        using var bitmap = SKBitmap.Decode(ms);

        if (bitmap == null)
            throw new InvalidOperationException("Unable to decode bitmap from image data");

        // Use the highest quality resize with proper color handling
        // Mitchell resampler provides sharper results than CatmullRom for downscaling
        var samplingOptions = new SKSamplingOptions(SKCubicResampler.Mitchell);
        using var resized = bitmap.Resize(
            new SKImageInfo(outW, outH, bitmap.ColorType, bitmap.AlphaType, bitmap.ColorSpace),
            samplingOptions);

        if (resized == null)
            throw new InvalidOperationException("Failed to resize image");

        using var image = SKImage.FromBitmap(resized);
        var fmt = codec.EncodedFormat; // Keep original format

        var outStream = new MemoryStream();
        var quality = options1.Quality ?? options.Value.DefaultQuality;

        switch (fmt)
        {
            case SKEncodedImageFormat.Jpeg:
                // Use high-quality JPEG encoding with better quality settings
                using (var data = image.Encode(SKEncodedImageFormat.Jpeg, quality))
                {
                    data.SaveTo(outStream);
                }
                break;
            case SKEncodedImageFormat.Webp:
                using (var data = image.Encode(SKEncodedImageFormat.Webp, quality))
                {
                    data.SaveTo(outStream);
                }
                break;
            case SKEncodedImageFormat.Png:
                // PNG quality parameter is compression level (0-9), doesn't affect visual quality
                // Use configured compression level (higher = smaller file, slightly slower)
                using (var data = image.Encode(SKEncodedImageFormat.Png, options.Value.PngCompressionLevel))
                {
                    data.SaveTo(outStream);
                }
                break;
            default:
                using (var data = image.Encode(fmt, quality))
                {
                    data.SaveTo(outStream);
                }
                break;
        }

        outStream.Position = 0;
        var mime = MimeFromEncodedFormat(fmt);

        logger.LogDebug("Resized image from {SrcW}x{SrcH} to {OutW}x{OutH}, format: {Format}",
            info.Width, info.Height, outW, outH, fmt);

        return (outStream, mime, outW, outH);
    }

    private static async Task<MemoryStream> CopyToMemoryStream(Stream input, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await input.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

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

    private static string MimeFromEncodedFormat(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => "image/jpeg",
        SKEncodedImageFormat.Png => "image/png",
        SKEncodedImageFormat.Webp => "image/webp",
        SKEncodedImageFormat.Gif => "image/gif",
        SKEncodedImageFormat.Bmp => "image/bmp",
        _ => "application/octet-stream"
    };
}
