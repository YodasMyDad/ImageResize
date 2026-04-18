# CLAUDE.md

Orientation for Claude / new agents working in this repo. Keep this file short and up to date. For end-user / NuGet-consumer docs see [README.md](README.md).

## Project overview

ImageResize is a minimal, cross-platform image-resize middleware for ASP.NET Core, backed by SkiaSharp, plus a cross-platform desktop companion app (Windows + macOS, built on Avalonia UI) that exposes the same engine through an Explorer context-menu (Windows) / Finder Services (macOS).

- Current version: **3.2.0** (published as the `ImageResize` NuGet package). Version lives in [Directory.Build.props](Directory.Build.props) — do NOT re-add `<Version>` in any csproj.
- Target runtime: **.NET 10** (`net10.0` across all projects — ContextMenu is Avalonia UI and cross-platform)
- License: MIT

## Solution layout

The solution is [ImageResize.sln](ImageResize.sln) with four projects:

- [ImageResize.Core/](ImageResize.Core/) — the NuGet library. ASP.NET Core middleware + SkiaSharp codec + disk cache. `net10.0`, framework-refs `Microsoft.AspNetCore.App`. `GeneratePackageOnBuild=true`, so a Release build emits a `.nupkg`.
- [ImageResize.ContextMenu/](ImageResize.ContextMenu/) — Avalonia UI desktop app (`net10.0`, cross-platform). Surfaces via a Windows 11 Explorer context-menu entry and a macOS Finder Quick Action. Single-instance + arg-forwarding is platform-split behind `ISingleInstance`: named pipe + mutex on Windows, Unix domain socket + exclusive lockfile on macOS/Linux.
- [ImageResize.Example/](ImageResize.Example/) — Razor Pages demo that consumes the middleware; useful as a smoke test.
- [ImageResize.Tests/](ImageResize.Tests/) — NUnit 4 + Shouldly + Moq unit tests for resize math, cache, path matching, and bounds validation.

## Tech stack & versions

- .NET 10 SDK; `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`
- SkiaSharp **3.119.2** (+ Linux / macOS / Win32 native assets)
- Avalonia UI **11.2.5** (ContextMenu); `MessageBox.Avalonia` **3.3.1.1**; `Tmds.DBus.Protocol` pinned to **0.92.0** to clear GHSA-xrw6-gwf8-vvr9
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

macOS universal `.app` + `.dmg` (must run on macOS — needs `lipo` from Xcode CLT; `brew install create-dmg` optional):

```bash
./build-macos.sh                    # Release, default
./build-macos.sh Debug
```

Output: `publish/installer/ImageResize-ContextMenu-Setup-<version>-universal.dmg`. CI at `macos-latest` runs this on every PR and uploads the DMG as an artefact.

## Key architectural patterns

- DI + middleware registration: `AddImageResize()` / `UseImageResize()` in [ImageResizeServiceCollectionExtensions.cs](ImageResize.Core/Extensions/ImageResizeServiceCollectionExtensions.cs). Registers `IValidateOptions<ImageResizeOptions>` with validate-on-start.
- Pluggable interfaces in [ImageResize.Core/Interfaces/](ImageResize.Core/Interfaces/): `IImageResizerService`, `IImageCodec`, `IImageCache`.
- Thundering-herd protection via `AsyncKeyedLocker` in [ImageResizerService.cs](ImageResize.Core/Services/ImageResizerService.cs).
- Atomic cache writes (temp file → flush → rename) + XxHash128-keyed folder sharding in [FileSystemImageCache.cs](ImageResize.Core/Cache/FileSystemImageCache.cs). Hashing helpers centralised in [HashingUtilities.cs](ImageResize.Core/Utilities/HashingUtilities.cs).
- Options-pattern config in [ImageResizeOptions.cs](ImageResize.Core/Configuration/ImageResizeOptions.cs) with nested `Bounds` / `Cache` / `ResponseCache` sub-options and a `MaxSourceBytes` decompression-bomb cap; validated by [ImageResizeOptionsValidator.cs](ImageResize.Core/Configuration/ImageResizeOptionsValidator.cs).
- ContextMenu single-instance + IPC: [`ISingleInstance`](ImageResize.ContextMenu/Services/ISingleInstance.cs) with [`WindowsSingleInstance`](ImageResize.ContextMenu/Services/WindowsSingleInstance.cs) (mutex + named pipe) and [`UnixSingleInstance`](ImageResize.ContextMenu/Services/UnixSingleInstance.cs) (exclusive lockfile + Unix domain socket); wired in [App.axaml.cs](ImageResize.ContextMenu/App.axaml.cs). Shared per-OS paths / logging in [AppPaths.cs](ImageResize.ContextMenu/Services/AppPaths.cs) + [AppLog.cs](ImageResize.ContextMenu/Services/AppLog.cs). Version-string resolution in [VersionInfo.cs](ImageResize.ContextMenu/VersionInfo.cs) (reads `AssemblyInformationalVersion` → flows from `Directory.Build.props`).

## Entry points

- Middleware pipeline: [ImageResizeMiddleware.cs](ImageResize.Core/Middleware/ImageResizeMiddleware.cs)
- Web demo: [ImageResize.Example/Program.cs](ImageResize.Example/Program.cs)
- Desktop app: [ImageResize.ContextMenu/Program.cs](ImageResize.ContextMenu/Program.cs) → [App.axaml.cs](ImageResize.ContextMenu/App.axaml.cs) → [MainWindow.axaml.cs](ImageResize.ContextMenu/MainWindow.axaml.cs)
- macOS packaging sources: [ImageResize.ContextMenu/macos/](ImageResize.ContextMenu/macos/) (`Info.plist` templated with `{VERSION}`; `ResizeImages.workflow` is a hand-rolled Automator Quick Action)

## Configuration

- Canonical options shape: [ImageResizeOptions.cs](ImageResize.Core/Configuration/ImageResizeOptions.cs)
- Realistic example values: [ImageResize.Example/appsettings.json](ImageResize.Example/appsettings.json)
- `.gitignore` excludes `bin/`, `obj/`, `nupkgs/`, `wwwroot/_imgcache`, `publish/`

## Conventions

- Nullable reference types on across all projects; treat warnings seriously.
- Common usings live in [GlobalUsings.cs](ImageResize.Core/GlobalUsings.cs) — prefer adding there over per-file `using` duplication.
- Common language/versioning settings live in [Directory.Build.props](Directory.Build.props); package versions in [Directory.Packages.props](Directory.Packages.props) (CPM is on — omit `Version=` on `<PackageReference>`).
- [.editorconfig](.editorconfig) enforces formatting and nullability-as-error. CI runs via [.github/workflows/build.yml](.github/workflows/build.yml).
- No test coverage for the WPF app — changes in [ImageResize.ContextMenu/](ImageResize.ContextMenu/) must be smoke-tested by running the app.

## Don'ts / gotchas

- v2.0 **deliberately removed** URL rewriting and masked URLs. Do not re-introduce them — the project is explicitly simpler than the old behaviour.
- `Cache.MaxCacheBytes = 0` means **unlimited**, not disabled.
- `AllowUpscale` defaults to `false`; resizes never enlarge past the source.
- The middleware must be registered **before** `UseStaticFiles()` and before routing — otherwise static files win and the middleware never sees the request.
- `GeneratePackageOnBuild` is on for `ImageResize.Core`, so every Release build drops a `.nupkg` into `nupkgs/`. Don't commit them; the `.gitignore` already excludes them.
- ContextMenu is Avalonia and builds on Windows + macOS + Linux. Windows-only code paths (Explorer COM, Win32 P/Invoke to Progman/WorkerW, named-pipe IPC) are guarded both at compile time with `[SupportedOSPlatform("windows")]` and at runtime inside `OperatingSystem.IsWindows()` blocks — do not hoist them out.
- **Do not re-introduce WPF.** The port to Avalonia was deliberate. No `<UseWPF>`, no `net10.0-windows` TFM on ContextMenu, no `System.Windows.*` references, no `.xaml` files (Avalonia's source generator only picks up `.axaml`).
- **Do not sign macOS builds with a paid Apple cert.** By design: no Apple Developer Program ($99/yr) — the `.dmg` ships unsigned, users right-click → Open once to bypass Gatekeeper. README documents this.

## Code signing (Windows installer)

Windows users see a SmartScreen *"Unknown publisher"* warning on first run because the Inno Setup installer is not Authenticode-signed. Chosen fix: **SignPath.io free OSS program** (no cost, no hardware tokens, signs in CI).

Status:

- ✅ **Part 1 (in-repo, done):** workaround documented in [ImageResize.ContextMenu/README.md](ImageResize.ContextMenu/README.md); [Installer.iss](ImageResize.ContextMenu/Installer.iss) publisher string set, icon enabled.
- ⏳ **Part 2 (offline, waiting on Lee):** register at <https://signpath.io> and request the free OSS plan for `YodasMyDad/ImageResize`. Once approved they issue an org ID, project slug, signing-policy slug, and API token.
- ⏳ **Part 3 (wire into CI, after Part 2):** add `SIGNPATH_API_TOKEN` secret, update [.github/workflows/release.yml](.github/workflows/release.yml) to submit the `.exe` via `signpath-io/github-action-submit-signing-request@v1` and publish the *signed* artefact in the Release. The existing "Build + sign installers" step currently doesn't actually sign.

macOS `.dmg` is intentionally unsigned — no Apple Developer Program. Do not add `codesign` / notarisation steps.

## Where to read more

- Consumer-facing API docs, features, querystring options: [README.md](README.md)
- ContextMenu install / registry-entry details: [ImageResize.ContextMenu/README.md](ImageResize.ContextMenu/README.md)
- Demo usage: [ImageResize.Example/README.md](ImageResize.Example/README.md)
