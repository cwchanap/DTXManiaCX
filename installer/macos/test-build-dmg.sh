#!/usr/bin/env bash
# Smoke test for build-dmg.sh: assembles a fake publish dir, runs the script,
# and asserts the bundle structure and DMG are produced.
# On macOS it also asserts the DMG is created (hdiutil); on other platforms
# build-dmg.sh skips DMG creation, so we assert bundle structure only.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Fake publish dir with a dummy executable + dll + Content/ and the System/
# skin the publish output now ships next to the executable.
PUBLISH="$TMP/publish"
mkdir -p "$PUBLISH/Content" "$PUBLISH/System/Graphics" "$PUBLISH/System/Sounds"
printf '#!/bin/sh\nexit 0\n' > "$PUBLISH/DTXMania.Game.Mac"
echo "fake-dll" > "$PUBLISH/DTXMania.Game.Mac.dll"
echo "fake-content" > "$PUBLISH/Content/test.xnb"
echo "fake-texture" > "$PUBLISH/System/Graphics/1_Background.png"
echo "fake-sound" > "$PUBLISH/System/Sounds/Decide.ogg"
printf '[Theme]\n' > "$PUBLISH/System/Theme.ini"

# Use the repository icon when available so macOS smoke tests exercise the real
# sips/iconutil .icns conversion path. Fall back to a tiny fake ICO only for
# trimmed checkouts where the game icon is unavailable.
ICO="$TMP/icon.ico"
SOURCE_ICO="$SCRIPT_DIR/../../DTXMania.Game/Icon.ico"
if [[ -f "$SOURCE_ICO" ]]; then
    cp "$SOURCE_ICO" "$ICO"
else
    printf 'fake-ico' > "$ICO"
fi

# Run the builder
OUTPUT="$TMP/out"
mkdir -p "$OUTPUT"
bash "$SCRIPT_DIR/build-dmg.sh" "$PUBLISH" "0.0.0-test" "$ICO" "$OUTPUT"

APP="$OUTPUT/DTXMania.app"

assert_exists() {
    if [[ ! -e "$1" ]]; then
        echo "FAIL: expected $1 to exist" >&2
        exit 1
    fi
}

assert_not_exists() {
    if [[ -e "$1" ]]; then
        echo "FAIL: expected $1 not to exist" >&2
        exit 1
    fi
}

assert_contains() {
    if ! grep -q -- "$2" "$1" 2>/dev/null; then
        echo "FAIL: expected $1 to contain '$2'" >&2
        exit 1
    fi
}

# Bundle layout: the skin is moved out of MacOS/ into Resources/, exactly once.
assert_exists "$APP/Contents/MacOS/DTXMania.Game.Mac"
assert_exists "$APP/Contents/MacOS/DTXMania.Game.Mac.dll"
assert_exists "$APP/Contents/MacOS/Content/test.xnb"
assert_exists "$APP/Contents/Resources/System/Graphics/1_Background.png"
assert_exists "$APP/Contents/Resources/System/Sounds/Decide.ogg"
assert_exists "$APP/Contents/Resources/System/Theme.ini"
assert_not_exists "$APP/Contents/MacOS/System"
assert_exists "$APP/Contents/Info.plist"

# App icon: prefer .icns (macOS with iconutil/sips); fall back to .ico copy
# when those tools are unavailable (e.g. non-macOS test runners) or when
# iconutil rejects the iconset (ICO too small for 512x512 entries).
if [[ ! -e "$APP/Contents/Resources/DTXMania.icns" && ! -e "$APP/Contents/Resources/DTXMania.ico" ]]; then
    echo "FAIL: expected either DTXMania.icns or DTXMania.ico under Resources" >&2
    exit 1
fi
if [[ "$(uname)" == "Darwin" && -f "$SOURCE_ICO" && -x "$(command -v iconutil)" && -x "$(command -v sips)" ]]; then
    assert_exists "$APP/Contents/Resources/DTXMania.icns"
fi

# CFBundleIconFile must reference the icon file actually present in Resources/.
# A stale reference (e.g. DTXMania.icns when only DTXMania.ico exists) makes
# Finder/Dock fall back to the generic app icon. Extract with sed (portable;
# avoids the macOS-only PlistBuddy dependency).
ICON_REF="$(sed -n '/<key>CFBundleIconFile<\/key>/{n; s/.*<string>\(.*\)<\/string>.*/\1/p;}' "$APP/Contents/Info.plist")"
if [[ -z "$ICON_REF" ]]; then
    echo "FAIL: CFBundleIconFile missing from Info.plist" >&2
    exit 1
fi
if [[ ! -e "$APP/Contents/Resources/$ICON_REF" ]]; then
    echo "FAIL: CFBundleIconFile references '$ICON_REF' but no such file exists under Resources/" >&2
    exit 1
fi

# Plist substitution + bundle id
assert_contains "$APP/Contents/Info.plist" "0.0.0-test"
assert_contains "$APP/Contents/Info.plist" "com.dtxmaniacx.DTXMania"

# Executable bit set on the apphost stub
if [[ ! -x "$APP/Contents/MacOS/DTXMania.Game.Mac" ]]; then
    echo "FAIL: expected executable to have +x bit" >&2
    exit 1
fi

# DMG (macOS only — hdiutil)
if [[ "$(uname)" == "Darwin" ]]; then
    assert_exists "$OUTPUT/DTXMania-0.0.0-test-arm64.dmg"
fi

# Fail closed when the publish output is missing the bundled System/ skin.
NO_SKIN_PUBLISH="$TMP/publish-no-skin"
mkdir -p "$NO_SKIN_PUBLISH"
printf '#!/bin/sh\nexit 0\n' > "$NO_SKIN_PUBLISH/DTXMania.Game.Mac"
if bash "$SCRIPT_DIR/build-dmg.sh" "$NO_SKIN_PUBLISH" "0.0.0-test" "$ICO" "$TMP/out-no-skin" >/dev/null 2>&1; then
    echo "FAIL: expected build-dmg.sh to fail when the publish output lacks System/" >&2
    exit 1
fi

echo "PASS: build-dmg.sh smoke test"
