#!/usr/bin/env bash
# Builds a DTXMania.app bundle and packages it into a DMG.
#
# Usage:
#   build-dmg.sh <publish_dir> <system_dir> <version> <ico_path> <output_dir>
#
# Produces:
#   <output_dir>/DTXMania.app
#   <output_dir>/DTXMania-<version>-arm64.dmg   (macOS only, via hdiutil)

set -euo pipefail

if [[ "$#" -ne 5 ]]; then
    echo "Usage: $0 <publish_dir> <system_dir> <version> <ico_path> <output_dir>" >&2
    exit 2
fi

PUBLISH_DIR="$1"
SYSTEM_DIR="$2"
VERSION="$3"
ICO_PATH="$4"
OUTPUT_DIR="$5"

APP_NAME="DTXMania"
EXEC_NAME="DTXMania.Game.Mac"
APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Fresh bundle
rm -rf "$APP_BUNDLE"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

# Copy published output (executable, dlls, runtime, Content) into MacOS/
cp -R "$PUBLISH_DIR/." "$MACOS_DIR/"

# Default skin — bundled for the future first-run seeding fix (not yet consumed by the game)
if [[ -d "$SYSTEM_DIR" ]]; then
    cp -R "$SYSTEM_DIR" "$RESOURCES_DIR/System"
fi

# Render Info.plist from template (substitute __APP_VERSION__)
sed "s/__APP_VERSION__/$VERSION/g" "$SCRIPT_DIR/Info.plist.template" > "$CONTENTS_DIR/Info.plist"

# Convert ICO to icns (best-effort). iconutil needs an .iconset of sized PNGs;
# we scale the ICO with sips into the standard sizes. Fall back to copying the
# ICO verbatim if iconutil/sips is unavailable (e.g. on non-macOS test runners).
ICNS_PATH="$RESOURCES_DIR/$APP_NAME.icns"
if command -v iconutil >/dev/null 2>&1 && command -v sips >/dev/null 2>&1; then
    ICONSET_DIR="$OUTPUT_DIR/$APP_NAME.iconset"
    rm -rf "$ICONSET_DIR"
    mkdir -p "$ICONSET_DIR"
    for size in 16 32 128 256 512; do
        sips -z "$size" "$size" "$ICO_PATH" --out "$ICONSET_DIR/icon_${size}x${size}.png" >/dev/null 2>&1 || true
    done
    if ! iconutil -c icns "$ICONSET_DIR" -o "$ICNS_PATH" >/dev/null 2>&1; then
        cp "$ICO_PATH" "$RESOURCES_DIR/$APP_NAME.ico"
    fi
    rm -rf "$ICONSET_DIR"
else
    cp "$ICO_PATH" "$RESOURCES_DIR/$APP_NAME.ico"
fi

# Make the apphost stub executable
if [[ -f "$MACOS_DIR/$EXEC_NAME" ]]; then
    chmod +x "$MACOS_DIR/$EXEC_NAME"
fi

# Ad-hoc codesign (no Apple Developer ID). Gatekeeper still warns on first
# launch — users right-click → Open, or run xattr -dr com.apple.quarantine.
# Failures are non-fatal (a bad sign shouldn't block a developer build).
if command -v codesign >/dev/null 2>&1; then
    codesign --force --deep --sign - "$APP_BUNDLE" >/dev/null 2>&1 || true
fi

# Build the DMG (macOS only). On other platforms the bundle is still produced;
# the test asserts bundle structure without requiring a DMG.
DMG_PATH="$OUTPUT_DIR/$APP_NAME-$VERSION-arm64.dmg"
if command -v hdiutil >/dev/null 2>&1; then
    rm -f "$DMG_PATH"
    STAGING="$OUTPUT_DIR/dmg-staging"
    rm -rf "$STAGING"
    mkdir -p "$STAGING"
    cp -R "$APP_BUNDLE" "$STAGING/"
    hdiutil create -volname "$APP_NAME" \
        -srcfolder "$STAGING" \
        -fs HFS+ \
        -format UDZO \
        -imagekey zlib-level=9 \
        "$DMG_PATH" >/dev/null
    rm -rf "$STAGING"
fi

echo "Built: $DMG_PATH"
