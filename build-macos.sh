#!/usr/bin/env bash
# Builds a universal (arm64 + x86_64) ImageResize.app bundle and wraps it in a
# distributable DMG alongside the Finder Quick Action. Must run on macOS — lipo
# and hdiutil/create-dmg are not available on Linux or Windows.
#
# Usage:  ./build-macos.sh [Release|Debug]
# Output: publish/installer/ImageResize-ContextMenu-<version>-universal.dmg

set -euo pipefail

CONFIG="${1:-Release}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/ImageResize.ContextMenu/ImageResize.ContextMenu.csproj"
MACOS_SRC="$ROOT/ImageResize.ContextMenu/macos"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "ERROR: build-macos.sh must run on macOS (got $(uname -s))." >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet SDK not found on PATH." >&2
    exit 1
fi

if ! command -v lipo >/dev/null 2>&1; then
    echo "ERROR: lipo not found (install Xcode command-line tools: xcode-select --install)." >&2
    exit 1
fi

# Version from Directory.Build.props — single source of truth, matches assemblies.
# Uses grep+sed (BSD/GNU compatible) rather than awk's 3-arg match (gawk-only).
VERSION=$(grep -o '<Version>[^<]*</Version>' "$ROOT/Directory.Build.props" | head -1 | sed -E 's|<Version>(.*)</Version>|\1|')
if [[ -z "$VERSION" ]]; then
    echo "ERROR: could not extract <Version> from Directory.Build.props." >&2
    exit 1
fi

echo "ImageResize.ContextMenu macOS build"
echo "  Version: $VERSION"
echo "  Configuration: $CONFIG"
echo

OUT_ARM64="$ROOT/publish/osx-arm64"
OUT_X64="$ROOT/publish/osx-x64"
OUT_UNIVERSAL="$ROOT/publish/osx-universal"
APP_DIR="$ROOT/publish/mac/ImageResize.app"
DMG_STAGE="$ROOT/publish/mac"
INSTALLER_DIR="$ROOT/publish/installer"

rm -rf "$OUT_ARM64" "$OUT_X64" "$OUT_UNIVERSAL" "$ROOT/publish/mac"
mkdir -p "$INSTALLER_DIR"

echo "==> Publishing for osx-arm64"
dotnet publish "$PROJECT" -c "$CONFIG" -r osx-arm64 --self-contained true \
    -o "$OUT_ARM64" \
    /p:PublishSingleFile=false \
    /p:IncludeNativeLibrariesForSelfExtract=true

echo "==> Publishing for osx-x64"
dotnet publish "$PROJECT" -c "$CONFIG" -r osx-x64 --self-contained true \
    -o "$OUT_X64" \
    /p:PublishSingleFile=false \
    /p:IncludeNativeLibrariesForSelfExtract=true

echo "==> Merging architectures into a universal layout"
# Start from arm64 as the base, then walk every file in x64 and either lipo-merge
# Mach-O binaries or leave the arm64 copy in place (managed DLLs are arch-independent).
cp -R "$OUT_ARM64" "$OUT_UNIVERSAL"

while IFS= read -r -d '' x64_file; do
    rel="${x64_file#$OUT_X64/}"
    arm_file="$OUT_ARM64/$rel"
    uni_file="$OUT_UNIVERSAL/$rel"

    if [[ ! -f "$arm_file" ]]; then
        # File only exists in x64 — copy across.
        mkdir -p "$(dirname "$uni_file")"
        cp "$x64_file" "$uni_file"
        continue
    fi

    # Only Mach-O files (main binary + .dylib) need lipo. Text/managed/json stay as-is.
    if /usr/bin/file -b "$x64_file" | grep -q 'Mach-O'; then
        arm_archs=$(lipo -archs "$arm_file" 2>/dev/null || echo "")
        x64_archs=$(lipo -archs "$x64_file" 2>/dev/null || echo "")

        # If either copy is already universal (contains both archs), the published dylib is
        # shipped as a fat binary (common for SkiaSharp). Prefer that — lipo -create would
        # refuse when archs overlap.
        if [[ "$arm_archs" == *"arm64"* && "$arm_archs" == *"x86_64"* ]]; then
            :  # arm_file is already fat — keep it.
        elif [[ "$x64_archs" == *"arm64"* && "$x64_archs" == *"x86_64"* ]]; then
            cp "$x64_file" "$uni_file"
        else
            lipo -create "$arm_file" "$x64_file" -output "$uni_file"
        fi
    fi
done < <(find "$OUT_X64" -type f -print0)

echo "==> Assembling $APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

# Info.plist with version substituted in-place.
sed "s/{VERSION}/$VERSION/g" "$MACOS_SRC/Info.plist" > "$APP_DIR/Contents/Info.plist"

# Bundle all runtime files into Contents/MacOS. The main executable must match CFBundleExecutable.
cp -R "$OUT_UNIVERSAL"/. "$APP_DIR/Contents/MacOS"/
chmod +x "$APP_DIR/Contents/MacOS/ImageResize.ContextMenu"

# Icon (optional) — use .icns if present, else fall back to the .ico via best effort.
if [[ -f "$MACOS_SRC/icon.icns" ]]; then
    cp "$MACOS_SRC/icon.icns" "$APP_DIR/Contents/Resources/icon.icns"
elif [[ -f "$ROOT/ImageResize.ContextMenu/Assets/icon.icns" ]]; then
    cp "$ROOT/ImageResize.ContextMenu/Assets/icon.icns" "$APP_DIR/Contents/Resources/icon.icns"
else
    echo "  (no .icns icon found; app will use default Finder icon)"
fi

# Stage the DMG contents: the .app for drag-install, the Quick Action to double-click install.
cp -R "$MACOS_SRC/ResizeImages.workflow" "$DMG_STAGE/Resize Images.workflow"
cat > "$DMG_STAGE/README.txt" <<EOF
ImageResize for macOS $VERSION

1. Drag ImageResize.app into the Applications folder.
2. Double-click "Resize Images.workflow" to install the Finder Quick Action.
   macOS will prompt to install it to ~/Library/Services.
3. First launch: right-click ImageResize.app in Applications -> Open, then
   click "Open" again when Gatekeeper warns about the unidentified developer.
   (One-time step; subsequent launches are unprompted.)

Uninstall:
  rm -rf /Applications/ImageResize.app
  rm -rf "\$HOME/Library/Services/Resize Images.workflow"
  rm -rf "\$HOME/Library/Application Support/ImageResize"
EOF

DMG_PATH="$INSTALLER_DIR/ImageResize-ContextMenu-Setup-$VERSION-universal.dmg"
rm -f "$DMG_PATH"

echo "==> Creating $DMG_PATH"
if command -v create-dmg >/dev/null 2>&1; then
    create-dmg \
        --volname "ImageResize $VERSION" \
        --window-size 600 400 \
        --icon-size 96 \
        --icon "ImageResize.app" 150 200 \
        --icon "Resize Images.workflow" 450 200 \
        --app-drop-link 300 340 \
        "$DMG_PATH" \
        "$DMG_STAGE" \
        || true  # create-dmg sometimes reports non-zero on AppleScript UI timing but still produces a valid DMG
fi

# Fallback: hdiutil is guaranteed to be present on macOS.
if [[ ! -f "$DMG_PATH" ]]; then
    hdiutil create \
        -volname "ImageResize $VERSION" \
        -srcfolder "$DMG_STAGE" \
        -ov -format UDZO \
        "$DMG_PATH"
fi

echo
echo "Done."
echo "  DMG:    $DMG_PATH"
echo "  Size:   $(du -h "$DMG_PATH" | cut -f1)"
