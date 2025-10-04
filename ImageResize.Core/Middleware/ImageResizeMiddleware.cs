using System.IO;
using System.Security.Cryptography;
using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageResize.Core.Middleware;

/// <summary>
/// Middleware for handling image resize requests.
/// </summary>
public sealed class ImageResizeMiddleware(
    RequestDelegate next,
    IOptions<ImageResizeOptions> opts,
    IImageResizerService svc,
    ILogger<ImageResizeMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!opts.Value.EnableMiddleware)
        {
            await next(ctx);
            return;
        }

        // Check if request path starts with any of the configured content roots
        var requestPath = ctx.Request.Path.Value?.TrimStart('/') ?? string.Empty;
        if (string.IsNullOrEmpty(requestPath))
        {
            await next(ctx);
            return;
        }

        var matchingRoot = opts.Value.ContentRoots
            .FirstOrDefault(root =>
            {
                var rootPath = root.TrimStart('/');
                // Must match at path boundary: either exact match or followed by '/'
                return requestPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
                       requestPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase);
            });

        if (matchingRoot is null)
        {
            await next(ctx);
            return;
        }

        // Check allowed extensions
        var ext = Path.GetExtension(requestPath).ToLowerInvariant();
        if (!opts.Value.AllowedExtensions.Contains(ext))
        {
            await next(ctx);
            return;
        }

        // Parse and validate query parameters
        int? w = TryParseInt(ctx.Request.Query["width"]);
        int? h = TryParseInt(ctx.Request.Query["height"]);
        int? q = TryParseInt(ctx.Request.Query["quality"]);

        if (w is null && h is null)
        {
            // No resize parameters provided, serve original image
            await ServeOriginalImageAsync(ctx, requestPath);
            return;
        }

        // Validate bounds
        if (!ValidateBounds(w, h, q, out var problem))
        {
            log.LogWarning("Invalid resize parameters for {Path}: {Problem}", requestPath, problem);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(problem!);
            return;
        }

        var options = new ResizeOptions(w, h, q);

        try
        {
            var result = await svc.EnsureResizedAsync(requestPath, options, ctx.RequestAborted);

            // Handle conditional requests
            if (ctx.Request.Headers.TryGetValue("If-None-Match", out var etag) &&
                !string.IsNullOrEmpty(etag) &&
                ETagMatches(etag!, result.CachedPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            if (ctx.Request.Headers.TryGetValue("If-Modified-Since", out var ifModified) &&
                !string.IsNullOrEmpty(ifModified) &&
                LastModifiedMatches(ifModified!, result.CachedPath))
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

            log.LogDebug("Served resized image {Path} ({Size} bytes)", result.CachedPath, fs.Length);
        }
        catch (FileNotFoundException)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error resizing image {Path}", requestPath);
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private bool ValidateBounds(int? width, int? height, int? quality, out string? problem)
    {
        if (width.HasValue && (width < opts.Value.Bounds.MinWidth || width > opts.Value.Bounds.MaxWidth))
        {
            problem = $"width must be between {opts.Value.Bounds.MinWidth} and {opts.Value.Bounds.MaxWidth}";
            return false;
        }

        if (height.HasValue && (height < opts.Value.Bounds.MinHeight || height > opts.Value.Bounds.MaxHeight))
        {
            problem = $"height must be between {opts.Value.Bounds.MinHeight} and {opts.Value.Bounds.MaxHeight}";
            return false;
        }

        if (quality.HasValue && (quality < opts.Value.Bounds.MinQuality || quality > opts.Value.Bounds.MaxQuality))
        {
            problem = $"quality must be between {opts.Value.Bounds.MinQuality} and {opts.Value.Bounds.MaxQuality}";
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
        if (opts.Value.ResponseCache.SendETag)
        {
            response.Headers["ETag"] = GenerateETag(filePath);
        }

        if (opts.Value.ResponseCache.SendLastModified)
        {
            response.Headers["Last-Modified"] = File.GetLastWriteTimeUtc(filePath).ToString("R");
        }

        response.Headers["Cache-Control"] = $"public, max-age={opts.Value.ResponseCache.ClientCacheSeconds}";
        response.Headers["Vary"] = "width, height, quality";
    }

    private async Task ServeOriginalImageAsync(HttpContext ctx, string relativePath)
    {
        try
        {
            // Resolve and validate the original file path
            var originalPath = ResolveOriginalPath(relativePath);

            if (!File.Exists(originalPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Get file info and content type
            var fileInfo = new FileInfo(originalPath);
            var contentType = GetContentTypeFromPath(originalPath);

            // Handle conditional requests
            if (ctx.Request.Headers.TryGetValue("If-None-Match", out var etag) &&
                !string.IsNullOrEmpty(etag) &&
                ETagMatches(etag!, originalPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            if (ctx.Request.Headers.TryGetValue("If-Modified-Since", out var ifModified) &&
                !string.IsNullOrEmpty(ifModified) &&
                LastModifiedMatches(ifModified!, originalPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            // Set response headers
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = fileInfo.Length;
            ApplyCacheHeaders(ctx.Response, originalPath);

            // Serve the file
            await using var fs = File.OpenRead(originalPath);
            await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);

            log.LogDebug("Served original image {Path} ({Size} bytes)", originalPath, fileInfo.Length);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error serving original image {Path}", relativePath);
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    private string ResolveOriginalPath(string relativePath)
    {
        // Security: Prevent path traversal
        var fullPath = Path.GetFullPath(Path.Combine(opts.Value.WebRoot, relativePath));

        // Ensure the resolved path is within WebRoot
        var webRootFull = Path.GetFullPath(opts.Value.WebRoot);
        if (!fullPath.StartsWith(webRootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path traversal attempt detected");
        }

        return fullPath;
    }

    private static string GetContentTypeFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream"
        };
    }
}
