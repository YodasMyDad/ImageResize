# ImageResize Context Menu

Desktop integration for quick image resizing on **Windows 11** (Explorer context menu) and **macOS** (Finder → Services). Right-click any image — or multiple images — and resize them instantly.

Built on Avalonia UI, so the same binary (modulo per-OS packaging) runs natively on both platforms. No WPF, no Electron.

## Features

- ✨ **Single & Multi-Image Support**: Select one or multiple images
- 🎯 **Smart UI**: Shows dimension controls for single images, percentage-only for multiple
- 📊 **Percentage Scaling**: Quick resize by percentage (10-200%)
- 📏 **Exact Dimensions**: Manual width/height control for single images
- 🎨 **Quality Control**: Adjustable JPEG/WebP quality (1-100)
- 💾 **Flexible Output**: Overwrite originals or create copies
- 🖼️ **Wide Format Support**: JPG, PNG, GIF, WebP, BMP, TIFF
- ⚡ **Fast Processing**: Uses SkiaSharp for high-performance image processing

## Quick Start

### Windows

**Option 1: Installer (Recommended)**
1. Download `ImageResize-ContextMenu-Setup-<version>-x64.exe`
2. Double-click to install
3. Follow the wizard
4. Right-click any image → "Resize Images..."

**Option 2: From Source**
```powershell
# From repository root
.\build-installer.ps1
# Then run: publish\installer\ImageResize-ContextMenu-Setup-<version>-x64.exe
```

### macOS

1. Download `ImageResize-ContextMenu-Setup-<version>-universal.dmg`
2. Open the DMG, drag **ImageResize.app** into `Applications`
3. Double-click **Resize Images.workflow** — macOS will offer to install it as a Finder Quick Action (copied to `~/Library/Services`)
4. Right-click any image file in Finder → **Services → Resize Images**

**First launch: Gatekeeper warning.** The app is not Authenticode-signed or notarised (no paid Apple Developer Program account), so Gatekeeper will refuse the first launch with _"ImageResize can't be opened because it is from an unidentified developer"_. Bypass it once:

- In Finder: **right-click** `ImageResize.app` (or the Quick Action) → **Open**, then click **Open** again in the dialog. Subsequent launches are unprompted.
- Alternative: **System Settings → Privacy & Security → Open Anyway** after the first blocked launch.
- Or strip the quarantine flag from a terminal: `xattr -dr com.apple.quarantine /Applications/ImageResize.app`.

**To build from source on macOS:**
```bash
./build-macos.sh          # produces publish/installer/ImageResize-ContextMenu-Setup-<version>-universal.dmg
brew install create-dmg   # (optional) nicer DMG layout; falls back to hdiutil without it
```

The build produces a universal binary (arm64 + x86_64 via `lipo`), so one DMG works on both Apple Silicon and Intel Macs.

### First install on Windows: SmartScreen warning

On first run you'll likely see a **"Windows protected your PC — Unknown publisher"** dialog. This is because the installer isn't yet Authenticode-signed (code signing is on the roadmap). The installer is safe to run; SmartScreen flags any unsigned installer from a publisher it doesn't recognise.

To proceed, use any of:

- Click **More info** → **Run anyway** in the SmartScreen dialog.
- Right-click the downloaded `.exe` → **Properties** → tick **Unblock** → **OK**, then double-click.
- In PowerShell, before running: `Unblock-File -Path .\ImageResize-ContextMenu-Setup-*.exe`

### Usage

**Single Image:**
1. Right-click an image file
2. Select "Resize Images..." from context menu
3. Adjust using:
   - **Percentage slider** (all fields sync automatically)
   - **Width/Height boxes** (slider updates automatically)
   - **Quality** (default 99)
4. Choose to overwrite or create a copy
5. Click "Resize"

**Multiple Images:**
1. Select 2+ image files
2. Right-click any selected file
3. Select "Resize Images..." from context menu
4. Adjust percentage (applies to all images proportionally)
5. Each image resizes based on its own dimensions
   - Example: 80% of 1920×1080 = 1536×864, 80% of 800×600 = 640×480
6. Click "Resize"

## Development

### Prerequisites

- .NET 10 SDK
- **Windows builds:** Windows 10 (1809)+ and Inno Setup 6 for the installer
- **macOS builds:** macOS 11+ with Xcode command-line tools (`xcode-select --install`) for `lipo`; `brew install create-dmg` for prettier DMGs (optional)

## Creating the Windows Installer

### Why Inno Setup?

Users want to **double-click an .exe file** to install, not run PowerShell scripts. Inno Setup creates a professional Windows installer that:

✅ Handles UAC (admin permission) automatically  
✅ Creates proper uninstall entries in Windows Settings  
✅ Has a familiar installation wizard  
✅ Checks for .NET runtime automatically  
✅ Is completely free and widely trusted  

### One-Time Setup

**Install Inno Setup (Free):**
1. Go to https://jrsoftware.org/isdl.php
2. Download **Inno Setup 6** (current version)
3. Run the installer
4. Use default installation location: `C:\Program Files (x86)\Inno Setup 6`

That's it! You only do this once.

### Creating the Installer

```powershell
cd C:\Projects\ImageResize
.\build-installer.ps1
```

This will:
1. Build the WPF application
2. Automatically find Inno Setup
3. Compile the installer script (`Installer.iss`)
4. Create: `publish\installer\ImageResize-ContextMenu-Setup-1.0.0.exe`

### What Gets Created

**Installer File:**
- Location: `publish\installer\ImageResize-ContextMenu-Setup-1.0.0.exe`
- Size: ~30-50 MB
- Contains: Application + all dependencies

**What the Installer Does:**
1. Checks for .NET 9.0 Runtime (offers to download if missing)
2. Prompts for admin permission (UAC)
3. Shows a friendly installation wizard
4. Copies files to `C:\Program Files\ImageResize`
5. Creates registry entries for context menu
6. Adds uninstall entry to Windows Settings
7. Refreshes Windows Explorer

### Installer Features

- **License Agreement**: Shows MIT license
- **Choose Install Location**: Default is `C:\Program Files\ImageResize`
- **Desktop Icon**: Optional (unchecked by default)
- **Progress Bar**: Shows installation progress
- **Uninstall**: Properly registered in Windows Settings

### Customizing the Installer

Edit `Installer.iss` to customize:

- **Application version** (line 5): `#define MyAppVersion "3.0.0"`
- **Company name** (line 6): `#define MyAppPublisher "Your Name"`
- **Website URL** (line 7): `#define MyAppURL "https://..."`
- **Icon** (line 26): `SetupIconFile=Assets\icon.ico`
- **Installer filename** (line 25): `OutputBaseFilename=...`

After editing, just run `.\build-installer.ps1` again.

## Uninstalling

Users can uninstall via:
1. **Windows Settings** → Apps → Installed apps → "ImageResize Context Menu" → Uninstall
2. **Start Menu** → ImageResize Context Menu folder → Uninstall
3. **Control Panel** → Programs and Features

All methods work correctly and remove:
- All application files
- All registry entries
- Context menu integration

## Technical Details

### Dependencies

- **ImageResize.Core**: image processing engine (project reference)
- **Avalonia UI 11.2.x**: cross-platform XAML UI toolkit
- **SkiaSharp**: high-performance image codec (pulled in by Core)
- **MessageBox.Avalonia**: standard dialog primitives
- **Microsoft.Extensions.DependencyInjection / Logging**: service container + logging
- **.NET 10 runtime**: self-contained in Mac builds; required system-wide on Windows

### Registry Entries

Context menu entries are registered for each format:
```
HKEY_CLASSES_ROOT\SystemFileAssociations\.jpg\shell\ResizeImage
HKEY_CLASSES_ROOT\SystemFileAssociations\.jpeg\shell\ResizeImage
HKEY_CLASSES_ROOT\SystemFileAssociations\.png\shell\ResizeImage
... (and so on for .gif, .webp, .bmp, .tif, .tiff)
```

Each entry includes:
- Display name: "Resize Images"
- MultiSelectModel: "Player" (enables multi-file selection)
- Command: `"C:\Program Files\ImageResize\ImageResize.ContextMenu.exe" %*`

### Installation Directory

```
C:\Program Files\ImageResize\
  ├── ImageResize.ContextMenu.exe
  ├── ImageResize.dll (NuGet package)
  ├── SkiaSharp.dll
  ├── Microsoft.*.dll (dependencies)
  └── ... (other dependencies)
```

### How Multi-Select Works

1. User selects multiple images in Windows Explorer
2. Windows invokes app once with all file paths as arguments
3. App detects multiple paths and switches to percentage-only mode
4. Each image is resized individually by the specified percentage
5. Original aspect ratios are preserved per image

## Troubleshooting

**Context menu doesn't appear:**
- Make sure you right-clicked an image file (.jpg, .png, etc.)
- Try restarting Windows Explorer
- Reinstall the application

**"No valid image files" error:**
- The app didn't receive any file paths
- Try reinstalling (may be registry issue)

**Multi-select not working:**
- Make sure you're selecting files of the same type
- Try selecting files and right-clicking on one of the selected files
- Check that `MultiSelectModel = "Player"` is in registry

**Quality slider has no effect:**
- Quality only affects lossy formats (JPEG, WebP)
- PNG, GIF, BMP are lossless and ignore quality setting

## License

MIT License - See LICENSE.txt file for details

## Credits

Built with:
- ImageResize.Core library
- SkiaSharp for image processing
- WPF for modern Windows UI
- Inno Setup for professional installation experience

