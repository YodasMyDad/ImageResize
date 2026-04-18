# Changelog

All notable changes to the `ImageResize` package and the `ImageResize.ContextMenu` app are documented here.
The format is loosely based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.1.0] — 2026-04-18

### Added — Core library
- `ImageResizeOptions.MaxSourceBytes` — reject source images larger than this cap (default 256 MiB) to mitigate decompression-bomb attacks.
- `IValidateOptions<ImageResizeOptions>` with validate-on-start — misconfiguration now fails at startup instead of at first request.
- `Microsoft.SourceLink.GitHub` + `.snupkg` symbol packages — consumers can step into library source with the debugger.
- `PackageIcon`, `PackageProjectUrl`, `EmbedUntrackedSources`, `PublishRepositoryUrl` metadata on the NuGet package.
- XML documentation file shipped alongside the NuGet package (IntelliSense for every public API).
- Request-scope logging with `HttpContext.TraceIdentifier` via `ILogger.BeginScope`.
- High-performance logging via `LoggerMessage` source generator on hot middleware paths.

### Changed — Core library
- Cache-key hashing switched from SHA1 to `System.IO.Hashing.XxHash128` — significantly faster, not cryptographically constrained. **Cache-invalidation note:** the on-disk cache layout changes with this release; existing cache files under `CacheRoot` will be stale and should be deleted (or will simply be ignored and re-created on first access).
- ETag generation uses XxHash of `{path, size, lastWriteUtc}` instead of SHA1 of the path alone, so revalidation correctly invalidates when the source file changes.
- `catch (Exception)` clauses in the middleware and cache writer tightened to specific exception types so that real faults (OOM, cancellation) are no longer swallowed.
- `ConfigureAwait(false)` applied to every `await` in library code (CA2007 clean).
- Public parameter `options1` renamed to `resizeOptions` on the `IImageCache` and `IImageCodec` interfaces.

### Added — ContextMenu app
- **Version number displayed in the title bar** and in a new **About dialog** (opened with `F1` or the `?` button).
- Drag-and-drop: drop image files onto the window to queue them.
- Real progress bar with elapsed / ETA readout; **Cancel** button now aborts mid-batch instead of closing the window.
- Parallel batch processing (`Parallel.ForEachAsync`, bounded by CPU count) — faster resizes on large batches.
- Per-file error isolation: one corrupt image no longer aborts the entire batch.
- Keyboard shortcuts: **Enter** = Resize, **Esc** = Cancel, **F1** = About, proper Tab order.
- Dark-mode-aware styling using `SystemColors` — follows Windows theme.
- Unhandled-exception handler writes to the log file and shows a non-blocking error dialog instead of silent crashes.
- `AutomationProperties.Name` on controls for screen-reader support.

### Fixed — ContextMenu app
- IPC server no longer silently swallows pipe exceptions (they are now logged).
- IPC client connect timeout raised from 500 ms to 2000 ms, with a fallback notification on exhaustion.
- `Topmost = true; Topmost = false` flicker on IPC activation replaced with a clean `Activate()` call.
- Secondary-instance mutex release bug — `ReleaseMutex()` is no longer called on an un-owned mutex.
- `WithRetryAsync` is now bounded to five attempts instead of potentially infinite.

### Infrastructure
- `Directory.Build.props` — single source of truth for version, language, and analyzer level across all projects.
- `Directory.Packages.props` — central package version management.
- `.editorconfig` — consistent formatting, nullability as error, naming rules.
- `.github/workflows/build.yml` — CI on Ubuntu + Windows, uploads test results and `.nupkg` artifacts.
- `SECURITY.md`, `CHANGELOG.md`.
- `build-installer.ps1` and `Installer.iss` now derive the version from the built assembly instead of hard-coding `3.0.0`.

## [3.0.0] — previous release

- First release of the `ImageResize` v3 line.
- Dropped URL rewriting and masked URLs (deliberately simpler API than v2).
- SkiaSharp 3.119.2, .NET 10, ASP.NET Core middleware with atomic disk cache.
- `AllowUpscale=false` by default.
- `Cache.MaxCacheBytes = 0` means unlimited.
- ContextMenu companion app shipped as Windows installer.
