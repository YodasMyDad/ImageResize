# CLAUDE.md

Orientation for Claude / new agents working in this repo. Keep this file short and up to date. For end-user / NuGet-consumer docs see [README.md](README.md).

## Project overview

ImageResize is a minimal, cross-platform image-resize middleware for ASP.NET Core, backed by SkiaSharp, plus a Windows desktop companion app that exposes the same engine through an Explorer context-menu.

- Current version: **3.0.0** (published as the `ImageResize` NuGet package)
- Target runtime: **.NET 10** (`net10.0`, plus `net10.0-windows` for the WPF app)
- License: MIT

## Solution layout

The solution is [ImageResize.sln](ImageResize.sln) with four projects:

- [ImageResize.Core/](ImageResize.Core/) — the NuGet library. ASP.NET Core middleware + SkiaSharp codec + disk cache. `net10.0`, framework-refs `Microsoft.AspNetCore.App`. `GeneratePackageOnBuild=true`, so a Release build emits a `.nupkg`.
- [ImageResize.ContextMenu/](ImageResize.ContextMenu/) — WPF desktop app (`net10.0-windows`, `WinExe`, `UseWPF=true`). Registers a Windows 11 Explorer context-menu entry; single-instance via mutex, forwards additional invocations over a named pipe.
- [ImageResize.Example/](ImageResize.Example/) — Razor Pages demo that consumes the middleware; useful as a smoke test.
- [ImageResize.Tests/](ImageResize.Tests/) — NUnit 4 + Shouldly + Moq unit tests for resize math, cache, path matching, and bounds validation.

## Tech stack & versions

- .NET 10 SDK; `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`
- SkiaSharp **3.119.2** (+ Linux / macOS / Win32 native assets)
- Microsoft.Extensions.{DependencyInjection,Logging,Options} **10.0.6**
- NUnit **4.5.1**, NUnit3TestAdapter **6.2.0**, Shouldly **4.3.0**, Moq **4.20.72**, Coverlet **10.0.0**

## Build / run / test

Shell is **bash on Windows** — use forward slashes in paths and `/dev/null`, not `NUL`.

```bash
dotnet build                                # build whole solution
dotnet build -c Release                     # release (produces nupkgs/)
dotnet test ImageResize.Tests               # run all tests
dotnet run --project ImageResize.Example    # run the demo web app
```

Windows installer for the ContextMenu app (PowerShell; requires Inno Setup on PATH unless `-SkipInnoSetup`):

```powershell
.\build-installer.ps1                       # default: x64 + x86, Release
.\build-installer.ps1 -Platform x64
.\build-installer.ps1 -Platform ARM64 -Configuration Debug
.\build-installer.ps1 -SkipInnoSetup        # build only, no installer
```

Output: `publish/installer/ImageResize-ContextMenu-Setup-*.exe`.

## Key architectural patterns

- DI + middleware registration: `AddImageResize()` / `UseImageResize()` in [ImageResizeServiceCollectionExtensions.cs](ImageResize.Core/Extensions/ImageResizeServiceCollectionExtensions.cs).
- Pluggable interfaces in [ImageResize.Core/Interfaces/](ImageResize.Core/Interfaces/): `IImageResizerService`, `IImageCodec`, `IImageCache`.
- Thundering-herd protection via `AsyncKeyedLocker` in [ImageResizerService.cs](ImageResize.Core/Services/ImageResizerService.cs).
- Atomic cache writes (temp file → flush → rename) + SHA1-keyed folder sharding in [FileSystemImageCache.cs](ImageResize.Core/Cache/FileSystemImageCache.cs).
- Options-pattern config in [ImageResizeOptions.cs](ImageResize.Core/Configuration/ImageResizeOptions.cs) with nested `Bounds` / `Cache` / `ResponseCache` sub-options.
- ContextMenu single-instance + IPC (mutex + named pipe) in [App.xaml.cs](ImageResize.ContextMenu/App.xaml.cs).

## Entry points

- Middleware pipeline: [ImageResizeMiddleware.cs](ImageResize.Core/Middleware/ImageResizeMiddleware.cs)
- Web demo: [ImageResize.Example/Program.cs](ImageResize.Example/Program.cs)
- Desktop app: [ImageResize.ContextMenu/App.xaml.cs](ImageResize.ContextMenu/App.xaml.cs) → [MainWindow.xaml.cs](ImageResize.ContextMenu/MainWindow.xaml.cs)

## Configuration

- Canonical options shape: [ImageResizeOptions.cs](ImageResize.Core/Configuration/ImageResizeOptions.cs)
- Realistic example values: [ImageResize.Example/appsettings.json](ImageResize.Example/appsettings.json)
- `.gitignore` excludes `bin/`, `obj/`, `nupkgs/`, `wwwroot/_imgcache`, `publish/`

## Conventions

- Nullable reference types on across all projects; treat warnings seriously.
- Common usings live in [GlobalUsings.cs](ImageResize.Core/GlobalUsings.cs) — prefer adding there over per-file `using` duplication.
- No `.editorconfig`, no CI workflows — this is a local-build / manual-release repo.
- No test coverage for the WPF app — changes in [ImageResize.ContextMenu/](ImageResize.ContextMenu/) must be smoke-tested by running the app.

## Don'ts / gotchas

- v2.0 **deliberately removed** URL rewriting and masked URLs. Do not re-introduce them — the project is explicitly simpler than the old behaviour.
- `Cache.MaxCacheBytes = 0` means **unlimited**, not disabled.
- `AllowUpscale` defaults to `false`; resizes never enlarge past the source.
- The middleware must be registered **before** `UseStaticFiles()` and before routing — otherwise static files win and the middleware never sees the request.
- `GeneratePackageOnBuild` is on for `ImageResize.Core`, so every Release build drops a `.nupkg` into `nupkgs/`. Don't commit them; the `.gitignore` already excludes them.
- ContextMenu is `net10.0-windows` — it will not build on Linux/macOS. Core + Example + Tests do build cross-platform.

## Where to read more

- Consumer-facing API docs, features, querystring options: [README.md](README.md)
- ContextMenu install / registry-entry details: [ImageResize.ContextMenu/README.md](ImageResize.ContextMenu/README.md)
- Demo usage: [ImageResize.Example/README.md](ImageResize.Example/README.md)
