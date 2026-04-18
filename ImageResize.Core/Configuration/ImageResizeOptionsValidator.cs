using Microsoft.Extensions.Options;

namespace ImageResize.Core.Configuration;

/// <summary>
/// Fails startup if <see cref="ImageResizeOptions"/> is misconfigured, so problems surface at
/// service bring-up rather than at the first incoming request.
/// </summary>
internal sealed class ImageResizeOptionsValidator : IValidateOptions<ImageResizeOptions>
{
    public ValidateOptionsResult Validate(string? name, ImageResizeOptions options)
    {
        List<string>? failures = null;

        void Fail(string msg) => (failures ??= []).Add(msg);

        if (options.DefaultQuality is < 1 or > 100)
            Fail($"{nameof(options.DefaultQuality)} must be between 1 and 100.");

        if (options.PngCompressionLevel is < 0 or > 9)
            Fail($"{nameof(options.PngCompressionLevel)} must be between 0 and 9.");

        if (options.MaxSourceBytes < 0)
            Fail($"{nameof(options.MaxSourceBytes)} must be non-negative.");

        var b = options.Bounds;
        if (b.MinWidth < 1 || b.MaxWidth < b.MinWidth)
            Fail($"Bounds: MinWidth must be >= 1 and MaxWidth >= MinWidth.");
        if (b.MinHeight < 1 || b.MaxHeight < b.MinHeight)
            Fail($"Bounds: MinHeight must be >= 1 and MaxHeight >= MinHeight.");
        if (b.MinQuality < 1 || b.MaxQuality < b.MinQuality || b.MaxQuality > 100)
            Fail($"Bounds: MinQuality/MaxQuality must be within 1..100 and MaxQuality >= MinQuality.");

        if (options.Cache.MaxCacheBytes < 0)
            Fail($"Cache.MaxCacheBytes must be non-negative (0 means unlimited).");
        if (options.Cache.FolderSharding < 0)
            Fail($"Cache.FolderSharding must be non-negative.");

        if (options.ResponseCache.ClientCacheSeconds < 0)
            Fail($"ResponseCache.ClientCacheSeconds must be non-negative.");

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
