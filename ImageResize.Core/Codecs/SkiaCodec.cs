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
        using var ms = await CopyToMemoryStreamAsync(input, ct).ConfigureAwait(false);
        using var codec = SKCodec.Create(ms)
            ?? throw new InvalidOperationException("Unable to decode image");

        var info = codec.Info;
        var mime = MimeFromEncodedFormat(codec.EncodedFormat);

        return (info.Width, info.Height, mime);
    }

    /// <inheritdoc />
    public async Task<(Stream Output, string ContentType, int OutW, int OutH)> ResizeAsync(
        Stream input,
        string? originalContentType,
        ResizeOptions resizeOptions,
        CancellationToken ct)
    {
        using var ms = await CopyToMemoryStreamAsync(input, ct).ConfigureAwait(false);
        using var codec = SKCodec.Create(ms)
            ?? throw new InvalidOperationException("Unable to decode image");

        var info = codec.Info;
        var (outW, outH) = Fit(info.Width, info.Height, resizeOptions.Width, resizeOptions.Height, options.Value.AllowUpscale);

        // Reset stream position after codec creation
        ms.Position = 0;
        using var bitmap = SKBitmap.Decode(ms)
            ?? throw new InvalidOperationException("Unable to decode bitmap from image data");

        // Mitchell resampler: sharper than CatmullRom for downscaling.
        var samplingOptions = new SKSamplingOptions(SKCubicResampler.Mitchell);
        using var resized = bitmap.Resize(
            new SKImageInfo(outW, outH, bitmap.ColorType, bitmap.AlphaType, bitmap.ColorSpace),
            samplingOptions)
            ?? throw new InvalidOperationException("Failed to resize image");

        using var image = SKImage.FromBitmap(resized);
        var fmt = codec.EncodedFormat; // Keep original format

        var outStream = new MemoryStream();
        var quality = resizeOptions.Quality ?? options.Value.DefaultQuality;

        using (var data = fmt == SKEncodedImageFormat.Png
            ? image.Encode(fmt, options.Value.PngCompressionLevel)
            : image.Encode(fmt, quality))
        {
            data.SaveTo(outStream);
        }

        outStream.Position = 0;
        var mime = MimeFromEncodedFormat(fmt);

        logger.LogDebug("Resized image from {SrcW}x{SrcH} to {OutW}x{OutH}, format: {Format}",
            info.Width, info.Height, outW, outH, fmt);

        return (outStream, mime, outW, outH);
    }

    /// <summary>
    /// Copies <paramref name="input"/> into a seekable <see cref="MemoryStream"/>, enforcing the
    /// configured <see cref="ImageResizeOptions.MaxSourceBytes"/> cap so an oversized or crafted
    /// stream cannot exhaust memory during decode.
    /// </summary>
    private async Task<MemoryStream> CopyToMemoryStreamAsync(Stream input, CancellationToken ct)
    {
        var maxBytes = options.Value.MaxSourceBytes;
        var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (maxBytes > 0 && total > maxBytes)
            {
                ms.Dispose();
                throw new InvalidOperationException(
                    $"Source image exceeds MaxSourceBytes ({maxBytes:N0} bytes).");
            }
            ms.Write(buffer, 0, read);
        }
        ms.Position = 0;
        return ms;
    }

    private static (int outW, int outH) Fit(int srcW, int srcH, int? reqW, int? reqH, bool allowUpscale)
    {
        var scaleW = reqW.HasValue ? (double)reqW.Value / srcW : double.PositiveInfinity;
        var scaleH = reqH.HasValue ? (double)reqH.Value / srcH : double.PositiveInfinity;

        var scale = Math.Min(scaleW, scaleH);
        if (double.IsInfinity(scale))
            scale = reqW.HasValue ? scaleW : scaleH;
        if (!allowUpscale)
            scale = Math.Min(scale, 1.0);

        var outW = Math.Max(1, (int)Math.Round(srcW * scale));
        var outH = Math.Max(1, (int)Math.Round(srcH * scale));
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
