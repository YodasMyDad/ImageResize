using ImageResize.Abstractions.Configuration;
using ImageResize.Abstractions.Interfaces;
using ImageResize.Abstractions.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace ImageResize.Core.Middleware;

/// <summary>
/// Middleware for handling image resize requests.
/// </summary>
public sealed class ImageResizeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ImageResizeMiddleware> _log;
    private readonly ImageResizeOptions _opts;
    private readonly IImageResizerService _svc;

    public ImageResizeMiddleware(
        RequestDelegate next,
        ImageResizeOptions opts,
        IImageResizerService svc,
        ILogger<ImageResizeMiddleware> log)
    {
        _next = next;
        _opts = opts;
        _svc = svc;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!_opts.EnableMiddleware)
        {
            await _next(ctx);
            return;
        }

        if (!ctx.Request.Path.StartsWithSegments(_opts.RequestPathPrefix, out var remainder))
        {
            await _next(ctx);
            return;
        }

        var relPath = remainder.Value.TrimStart('/');
        if (string.IsNullOrEmpty(relPath))
        {
            await _next(ctx);
            return;
        }

        // Check allowed extensions
        var ext = Path.GetExtension(relPath).ToLowerInvariant();
        if (!_opts.AllowedExtensions.Contains(ext))
        {
            await _next(ctx);
            return;
        }

        // Parse and validate query parameters
        int? w = TryParseInt(ctx.Request.Query["width"]);
        int? h = TryParseInt(ctx.Request.Query["height"]);
        int? q = TryParseInt(ctx.Request.Query["quality"]);

        if (w is null && h is null)
        {
            await _next(ctx);
            return;
        }

        // Validate bounds
        if (!ValidateBounds(w, h, q, out var problem))
        {
            _log.LogWarning("Invalid resize parameters for {Path}: {Problem}", relPath, problem);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(problem!);
            return;
        }

        var options = new ResizeOptions(w, h, q);

        try
        {
            var result = await _svc.EnsureResizedAsync(relPath, options, ctx.RequestAborted);

            // Handle conditional requests
            if (ctx.Request.Headers.TryGetValue("If-None-Match", out var etag) &&
                !string.IsNullOrEmpty(etag) &&
                ETagMatches(etag, result.CachedPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            if (ctx.Request.Headers.TryGetValue("If-Modified-Since", out var ifModified) &&
                !string.IsNullOrEmpty(ifModified) &&
                LastModifiedMatches(ifModified, result.CachedPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            // Serve the cached file
            ctx.Response.ContentType = result.ContentType;
            ApplyCacheHeaders(ctx.Response, result.CachedPath);

            await using var fs = File.OpenRead(result.CachedPath);
            ctx.Response.ContentLength = fs.Length;
            await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);

            _log.LogDebug("Served resized image {Path} ({Size} bytes)", result.CachedPath, fs.Length);
        }
        catch (FileNotFoundException)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error resizing image {Path}", relPath);
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private bool ValidateBounds(int? width, int? height, int? quality, out string? problem)
    {
        if (width.HasValue && (width < _opts.Bounds.MinWidth || width > _opts.Bounds.MaxWidth))
        {
            problem = $"width must be between {_opts.Bounds.MinWidth} and {_opts.Bounds.MaxWidth}";
            return false;
        }

        if (height.HasValue && (height < _opts.Bounds.MinHeight || height > _opts.Bounds.MaxHeight))
        {
            problem = $"height must be between {_opts.Bounds.MinHeight} and {_opts.Bounds.MaxHeight}";
            return false;
        }

        if (quality.HasValue && (quality < _opts.Bounds.MinQuality || quality > _opts.Bounds.MaxQuality))
        {
            problem = $"quality must be between {_opts.Bounds.MinQuality} and {_opts.Bounds.MaxQuality}";
            return false;
        }

        problem = null;
        return true;
    }

    private static bool ETagMatches(string etag, string filePath)
    {
        var fileEtag = GenerateETag(filePath);
        return etag.Contains(fileEtag, StringComparison.Ordinal);
    }

    private static bool LastModifiedMatches(string ifModified, string filePath)
    {
        if (!DateTime.TryParse(ifModified, out var clientDate))
            return false;

        var fileDate = File.GetLastWriteTimeUtc(filePath);
        return clientDate >= fileDate;
    }

    private static string GenerateETag(string filePath)
    {
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(filePath));
        return $"\"{BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()}\"";
    }

    private void ApplyCacheHeaders(HttpResponse response, string filePath)
    {
        if (_opts.ResponseCache.SendETag)
        {
            response.Headers["ETag"] = GenerateETag(filePath);
        }

        if (_opts.ResponseCache.SendLastModified)
        {
            response.Headers["Last-Modified"] = File.GetLastWriteTimeUtc(filePath).ToString("R");
        }

        response.Headers["Cache-Control"] = $"public, max-age={_opts.ResponseCache.ClientCacheSeconds}";
        response.Headers["Vary"] = "width, height, quality";
    }
}
