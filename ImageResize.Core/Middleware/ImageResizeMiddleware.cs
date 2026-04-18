using ImageResize.Core.Configuration;
using ImageResize.Core.Interfaces;
using ImageResize.Core.Utilities;
using ImageResize.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageResize.Core.Middleware;

/// <summary>
/// Middleware for handling image resize requests.
/// </summary>
public sealed partial class ImageResizeMiddleware(
    RequestDelegate next,
    IOptions<ImageResizeOptions> opts,
    IImageResizerService svc,
    ILogger<ImageResizeMiddleware> log)
{
    /// <summary>
    /// Entry point called by the ASP.NET Core request pipeline.
    /// </summary>
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!opts.Value.EnableMiddleware)
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        var requestPath = ctx.Request.Path.Value?.TrimStart('/') ?? string.Empty;
        if (string.IsNullOrEmpty(requestPath))
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        var matchingRoot = opts.Value.ContentRoots.FirstOrDefault(root =>
        {
            var rootPath = root.TrimStart('/');
            return requestPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
                   requestPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase);
        });

        if (matchingRoot is null)
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        var ext = Path.GetExtension(requestPath).ToLowerInvariant();
        if (!opts.Value.AllowedExtensions.Contains(ext))
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        using var _ = log.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = ctx.TraceIdentifier,
            ["RequestPath"] = requestPath,
        });

        var w = TryParseInt(ctx.Request.Query["width"]);
        var h = TryParseInt(ctx.Request.Query["height"]);
        var q = TryParseInt(ctx.Request.Query["quality"]);

        if (w is null && h is null)
        {
            await ServeOriginalImageAsync(ctx, requestPath).ConfigureAwait(false);
            return;
        }

        if (!ValidateBounds(w, h, q, out var problem))
        {
            LogInvalidParameters(requestPath, problem!);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(problem!, ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        var options = new ResizeOptions(w, h, q);

        try
        {
            var result = await svc.EnsureResizedAsync(requestPath, options, ctx.RequestAborted).ConfigureAwait(false);

            if (ClientHasFreshCopy(ctx.Request, result.CachedPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            ctx.Response.ContentType = result.ContentType;
            ApplyCacheHeaders(ctx.Response, result.CachedPath);

            await using var fs = File.OpenRead(result.CachedPath);
            ctx.Response.ContentLength = fs.Length;
            await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);

            log.LogDebug("Served resized image {Path} ({Size} bytes)", result.CachedPath, fs.Length);
        }
        catch (FileNotFoundException)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning(ex, "Path traversal or permission error for {Path}", requestPath);
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Client disconnect — nothing to do, don't treat as server error.
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Bad image input for {Path}", requestPath);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        catch (IOException ex)
        {
            log.LogError(ex, "I/O error resizing {Path}", requestPath);
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    private static int? TryParseInt(string? value)
        => int.TryParse(value, out var result) ? result : null;

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

    private static bool ClientHasFreshCopy(HttpRequest request, string filePath)
    {
        if (request.Headers.TryGetValue("If-None-Match", out var etag) &&
            !string.IsNullOrEmpty(etag) &&
            ETagMatches(etag!, filePath))
        {
            return true;
        }

        if (request.Headers.TryGetValue("If-Modified-Since", out var ifModified) &&
            !string.IsNullOrEmpty(ifModified) &&
            LastModifiedMatches(ifModified!, filePath))
        {
            return true;
        }

        return false;
    }

    private static bool ETagMatches(string etag, string filePath)
        => etag.Contains(HashingUtilities.ComputeFileETag(filePath), StringComparison.Ordinal);

    private static bool LastModifiedMatches(string ifModified, string filePath)
    {
        if (!DateTime.TryParse(ifModified, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var clientDate))
        {
            return false;
        }

        var fileDate = File.GetLastWriteTimeUtc(filePath);
        // Strip sub-second precision — HTTP dates are second-resolution
        var fileDateTrim = new DateTime(fileDate.Year, fileDate.Month, fileDate.Day, fileDate.Hour, fileDate.Minute, fileDate.Second, DateTimeKind.Utc);
        return clientDate >= fileDateTrim;
    }

    private void ApplyCacheHeaders(HttpResponse response, string filePath)
    {
        if (opts.Value.ResponseCache.SendETag)
            response.Headers["ETag"] = HashingUtilities.ComputeFileETag(filePath);

        if (opts.Value.ResponseCache.SendLastModified)
            response.Headers["Last-Modified"] = File.GetLastWriteTimeUtc(filePath).ToString("R");

        response.Headers["Cache-Control"] = $"public, max-age={opts.Value.ResponseCache.ClientCacheSeconds}";
        response.Headers["Vary"] = "width, height, quality";
    }

    private async Task ServeOriginalImageAsync(HttpContext ctx, string relativePath)
    {
        try
        {
            var originalPath = ResolveOriginalPath(relativePath);

            if (!File.Exists(originalPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var fileInfo = new FileInfo(originalPath);
            var contentType = GetContentTypeFromPath(originalPath);

            if (ClientHasFreshCopy(ctx.Request, originalPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = fileInfo.Length;
            ApplyCacheHeaders(ctx.Response, originalPath);

            await using var fs = File.OpenRead(originalPath);
            await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);

            log.LogDebug("Served original image {Path} ({Size} bytes)", originalPath, fileInfo.Length);
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning(ex, "Path traversal or permission error serving {Path}", relativePath);
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            log.LogError(ex, "I/O error serving original {Path}", relativePath);
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    private string ResolveOriginalPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(opts.Value.WebRoot, relativePath));
        var webRootFull = Path.GetFullPath(opts.Value.WebRoot);
        if (!fullPath.StartsWith(webRootFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal attempt detected");
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

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Invalid resize parameters for {Path}: {Problem}")]
    partial void LogInvalidParameters(string path, string problem);
}
