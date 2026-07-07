# Release Installer Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GitHub Actions workflow (`.github/workflows/release.yml`) that, on every published GitHub Release, builds a Windows Inno Setup `.exe` installer and a macOS `.app`-in-`.dmg` (arm64), and attaches both to the release.

**Architecture:** A new `release.yml` triggers on `release: published`, runs two parallel jobs (windows-installer, mac-installer). Each job does a self-contained `dotnet publish`, then invokes a committed installer-definition script (`installer/windows/dtxmania.iss` via Inno Setup; `installer/macos/build-dmg.sh` to assemble the bundle + `hdiutil`). Each job uploads its asset via `softprops/action-gh-release@v2`. The existing `build-and-test.yml` is untouched.

**Tech Stack:** .NET 8 self-contained publish, Inno Setup 6, `hdiutil`/`codesign`/`iconutil` (macOS built-ins), GitHub Actions, `softprops/action-gh-release@v2`.

**Spec:** `docs/superpowers/specs/2026-07-07-release-installer-workflow-design.md`

## Global Constraints

- Self-contained publish only: `--self-contained -p:PublishReadyToRun=false -p:TieredCompilation=false`. Matches the csproj's existing `PublishReadyToRun=false`/`TieredCompilation=false`.
- Mac target: `osx-arm64` ONLY (no x64, no universal).
- Windows target: `win-x64`.
- Version comes from `github.event.release.tag_name` with a leading `v` stripped (e.g. `v1.2.3` → `1.2.3`).
- Game executable name: `DTXMania.Game` (AssemblyName defaults from the project filename — no `<AssemblyName>` in either csproj). Windows exe: `DTXMania.Game.exe`; Mac apphost: `DTXMania.Game`.
- Game reads `System/` from per-user app-data only (`AppPaths.GetDefaultSystemSkinRoot()`): Windows `%LOCALAPPDATA%\DTXManiaCX\System`; Mac `~/Library/Application Support/DTXManiaCX/System`. Windows installer must populate this during install; Mac seeding is deferred to a follow-up PR (per spec).
- Permissions `contents: write` is required on the release workflow (to upload assets).
- No code signing (Windows Authenticode / Apple Developer ID). Acceptable for first release.
- Working directory: `/Users/chanwaichan/workspace/DTXmaniaCX`. The repo's `System/` directory (contains `Graphics/`, `Script/`, `Sounds/`) ships inside both installers.

---

## Task 1: Mac installer builder script + Info.plist template + smoke test

**Files:**
- Create: `installer/macos/Info.plist.template`
- Create: `installer/macos/build-dmg.sh`
- Create: `installer/macos/test-build-dmg.sh`

**Interfaces:**
- Produces: `build-dmg.sh` with the contract
  `build-dmg.sh <publish_dir> <system_dir> <version> <ico_path> <output_dir>`
  → assembles `<output_dir>/DTXMania.app` and creates `<output_dir>/DTXMania-<version>-arm64.dmg`.
- Produces: `Info.plist.template` with placeholder `__APP_VERSION__` that `build-dmg.sh` substitutes via `sed`.

- [ ] **Step 1: Create the `installer/macos/` directory**

```bash
mkdir -p installer/macos
```

- [ ] **Step 2: Write the Info.plist template**

Create `installer/macos/Info.plist.template` with exactly:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>DTXMania</string>
    <key>CFBundleExecutable</key>
    <string>DTXMania.Game</string>
    <key>CFBundleIconFile</key>
    <string>DTXMania.icns</string>
    <key>CFBundleIdentifier</key>
    <string>com.dtxmaniacx.DTXMania</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>DTXMania</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>__APP_VERSION__</string>
    <key>CFBundleSignature</key>
    <string>DTXK</string>
    <key>CFBundleVersion</key>
    <string>__APP_VERSION__</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.games</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>LSArchitecturePriority</key>
    <array>
        <string>arm64</string>
    </array>
    <key>LSRequiresNativeExecution</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright © 2026 DTXManiaCX. All rights reserved.</string>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
```

- [ ] **Step 3: Write the smoke test (the failing test)**

Create `installer/macos/test-build-dmg.sh` with exactly:

```bash
#!/usr/bin/env bash
# Smoke test for build-dmg.sh: assembles a fake publish dir, runs the script,
# and asserts the bundle structure and DMG are produced.
# On macOS it also asserts the DMG is created (hdiutil); on other platforms
# build-dmg.sh skips DMG creation, so we assert bundle structure only.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Fake publish dir with a dummy executable + dll + Content/
PUBLISH="$TMP/publish"
mkdir -p "$PUBLISH/Content"
printf '#!/bin/sh\nexit 0\n' > "$PUBLISH/DTXMania.Game"
echo "fake-dll" > "$PUBLISH/DTXMania.Game.dll"
echo "fake-content" > "$PUBLISH/Content/test.xnb"

# Fake System/ skin dir
SYSTEM="$TMP/system"
mkdir -p "$SYSTEM/Graphics"
echo "fake-texture" > "$SYSTEM/Graphics/1_Background.png"

# Fake ICO
ICO="$TMP/icon.ico"
printf 'fake-ico' > "$ICO"

# Run the builder
OUTPUT="$TMP/out"
mkdir -p "$OUTPUT"
bash "$SCRIPT_DIR/build-dmg.sh" "$PUBLISH" "$SYSTEM" "0.0.0-test" "$ICO" "$OUTPUT"

APP="$OUTPUT/DTXMania.app"

assert_exists() {
    if [[ ! -e "$1" ]]; then
        echo "FAIL: expected $1 to exist" >&2
        exit 1
    fi
}

assert_contains() {
    if ! grep -q -- "$2" "$1" 2>/dev/null; then
        echo "FAIL: expected $1 to contain '$2'" >&2
        exit 1
    fi
}

# Bundle layout
assert_exists "$APP/Contents/MacOS/DTXMania.Game"
assert_exists "$APP/Contents/MacOS/DTXMania.Game.dll"
assert_exists "$APP/Contents/MacOS/Content/test.xnb"
assert_exists "$APP/Contents/Resources/System/Graphics/1_Background.png"
assert_exists "$APP/Contents/Info.plist"

# Plist substitution + bundle id
assert_contains "$APP/Contents/Info.plist" "0.0.0-test"
assert_contains "$APP/Contents/Info.plist" "com.dtxmaniacx.DTXMania"

# Executable bit set on the apphost stub
if [[ ! -x "$APP/Contents/MacOS/DTXMania.Game" ]]; then
    echo "FAIL: expected executable to have +x bit" >&2
    exit 1
fi

# DMG (macOS only — hdiutil)
if [[ "$(uname)" == "Darwin" ]]; then
    assert_exists "$OUTPUT/DTXMania-0.0.0-test-arm64.dmg"
fi

echo "PASS: build-dmg.sh smoke test"
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `bash installer/macos/test-build-dmg.sh`
Expected: FAIL with `No such file or directory` (or similar) because `build-dmg.sh` does not exist yet.

- [ ] **Step 5: Write `build-dmg.sh` (minimal implementation to make the test pass)**

Create `installer/macos/build-dmg.sh` with exactly:

```bash
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
EXEC_NAME="DTXMania.Game"
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
```

- [ ] **Step 6: Make the scripts executable and run the test to verify it passes**

```bash
chmod +x installer/macos/build-dmg.sh installer/macos/test-build-dmg.sh
bash installer/macos/test-build-dmg.sh
```

Expected: `PASS: build-dmg.sh smoke test`

- [ ] **Step 7: Lint both scripts with shellcheck (install if missing)**

```bash
brew install shellcheck   # only if not already installed
shellcheck installer/macos/build-dmg.sh installer/macos/test-build-dmg.sh
```

Expected: no errors. (Warnings are acceptable; fix any that look material. The intentional `|| true` patterns may produce SC2086/SC2181 notes — leave them, they're load-bearing.)

- [ ] **Step 8: Commit**

```bash
git add installer/macos/Info.plist.template installer/macos/build-dmg.sh installer/macos/test-build-dmg.sh
git commit -m "feat(installer): add macOS .app bundle + DMG builder script"
```

---

## Task 2: Windows Inno Setup script

**Files:**
- Create: `installer/windows/dtxmania.iss`

**Interfaces:**
- Consumes: Inno Setup preprocessor defines `MyAppVersion` (string) and `SourceDir` (relative path to the `dotnet publish` output, e.g. `publish\win`), passed via `/D` on the `iscc` command line.
- Produces: `installer/windows/Output/DTXMania-Setup-<MyAppVersion>.exe` when compiled with `iscc`.

**Note on testing:** Inno Setup cannot be compiled on macOS (the dev environment). This task has no local compile step — validation happens via review now and via the real CI build in Task 3. The `/D` defines include `#error` guards so a missing argument fails the CI compile loudly instead of producing a broken installer.

- [ ] **Step 1: Create the `installer/windows/` directory**

```bash
mkdir -p installer/windows
```

- [ ] **Step 2: Write `dtxmania.iss`**

Create `installer/windows/dtxmania.iss` with exactly:

```pascal
; DTXManiaCX Windows installer (Inno Setup 6)
;
; Build with:
;   iscc /DMyAppVersion=<x.y.z> /DSourceDir=<publish\win> installer\windows\dtxmania.iss
;
; MyAppVersion and SourceDir MUST be passed on the command line. The #error
; guards below fail the compile clearly if either is missing.

#ifndef MyAppVersion
  #error "MyAppVersion must be defined (pass /DMyAppVersion=x.y.z to iscc)"
#endif

#ifndef SourceDir
  #error "SourceDir must be defined (pass /DSourceDir=publish\win to iscc)"
#endif

#define MyAppName      "DTXManiaCX"
#define MyAppPublisher "DTXManiaCX"
#define MyAppExeName   "DTXMania.Game.exe"
#define MyAppURL       "https://github.com/chanwaichan/DTXmaniaCX"

[Setup]
; AppId must stay stable across versions so upgrades replace the old install.
; Regenerate once if you want a unique value, then never change it.
AppId={{8F4C6B7E-1D2A-4E3F-9B5C-D2A0E7F3B1C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=DTXMania-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained game files from the win-x64 publish output
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; Default System skin → per-user app-data so the game finds it on first launch.
; {localappdata} resolves to %LOCALAPPDATA%, matching AppPaths.GetDefaultSystemSkinRoot().
; Path is relative to this .iss file: installer\windows\..\..\System -> repo-root System\
; uninsneveruninstall: never wipe the user's skin dir (they may have customized it).
Source: "..\..\System\*"; DestDir: "{localappdata}\DTXManiaCX\System"; Flags: recursesubdirs createallsubdirs ignoreversion uninsneveruninstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 3: Review against the spec's Windows section**

Confirm by reading the file back:
- `MyAppVersion` / `SourceDir` are `#ifndef`-guarded with `#error` (Step 2's content has them).
- `{autopf}` + `PrivilegesRequired=lowest` → installs per-user, no admin prompt by default.
- `System\*` installs to `{localappdata}\DTXManiaCX\System` with `uninsneveruninstall`.
- Icons include Start Menu + optional Desktop + uninstall entry.
- `[Run]` offers to launch the game after install.

- [ ] **Step 4: Commit**

```bash
git add installer/windows/dtxmania.iss
git commit -m "feat(installer): add Windows Inno Setup script"
```

---

## Task 3: Release workflow + end-to-end validation

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes (from Task 1): `installer/macos/build-dmg.sh <publish_dir> <system_dir> <version> <ico_path> <output_dir>` and `installer/macos/test-build-dmg.sh`.
- Consumes (from Task 2): `installer/windows/dtxmania.iss` compiled with `/DMyAppVersion=<v> /DSourceDir=publish\win`.
- Produces: a workflow that, on `release.published`, uploads `DTXMania-Setup-<v>.exe` and `DTXMania-<v>-arm64.dmg` to the triggering release.

- [ ] **Step 1: Write `.github/workflows/release.yml`**

Create `.github/workflows/release.yml` with exactly:

```yaml
name: Release

on:
  release:
    types: [published]

permissions:
  contents: write

jobs:
  windows-installer:
    runs-on: windows-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v7

      - name: Normalize version from tag
        shell: pwsh
        run: |
          $tag = "${{ github.event.release.tag_name }}"
          $version = $tag -replace '^v', ''
          if (-not $version) { throw "Could not normalize version from tag '$tag'" }
          "APP_VERSION=$version" | Out-File -FilePath $env:GITHUB_ENV -Append
          Write-Host "Release tag: $tag; normalized version: $version"

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore DTXMania.Game/DTXMania.Game.Windows.csproj

      - name: Publish self-contained (win-x64)
        run: >
          dotnet publish DTXMania.Game/DTXMania.Game.Windows.csproj
          -c Release -r win-x64 --self-contained
          -p:PublishReadyToRun=false -p:TieredCompilation=false
          -o ./publish/win

      - name: Install Inno Setup
        shell: pwsh
        run: choco install innosetup --no-progress -y

      - name: Compile installer
        shell: pwsh
        run: |
          $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
          if (-not (Test-Path $iscc)) { throw "ISCC.exe not found at $iscc" }
          & $iscc /DMyAppVersion="$env:APP_VERSION" "/DSourceDir=publish\win" "installer\windows\dtxmania.iss"

      - name: Upload installer to release
        uses: softprops/action-gh-release@v2
        with:
          files: installer/windows/Output/DTXMania-Setup-${{ env.APP_VERSION }}.exe

  mac-installer:
    runs-on: macos-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v7

      - name: Normalize version from tag
        run: |
          tag="${{ github.event.release.tag_name }}"
          version="${tag#v}"
          if [ -z "$version" ]; then echo "Could not normalize version from tag '$tag'" >&2; exit 1; fi
          echo "APP_VERSION=$version" >> "$GITHUB_ENV"
          echo "Release tag: $tag; normalized version: $version"

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore DTXMania.Game/DTXMania.Game.Mac.csproj

      - name: Smoke test build-dmg.sh (fast-fail on script regression)
        run: |
          chmod +x installer/macos/build-dmg.sh installer/macos/test-build-dmg.sh
          bash installer/macos/test-build-dmg.sh

      - name: Publish self-contained (osx-arm64)
        run: >
          dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj
          -c Release -r osx-arm64 --self-contained
          -p:PublishReadyToRun=false -p:TieredCompilation=false
          -o ./publish/mac

      - name: Build .app bundle and DMG
        run: |
          ./installer/macos/build-dmg.sh \
            ./publish/mac \
            ./System \
            "$APP_VERSION" \
            DTXMania.Game/Icon.ico \
            ./output

      - name: Upload DMG to release
        uses: softprops/action-gh-release@v2
        with:
          files: output/DTXMania-${{ env.APP_VERSION }}-arm64.dmg
```

- [ ] **Step 2: Lint the workflow with actionlint (install if missing)**

```bash
brew install actionlint   # only if not already installed
actionlint .github/workflows/release.yml
```

Expected: no errors. (If actionlint complains about `${{ env.APP_VERSION }}` in `files:`, that's expected — it can't statically resolve env vars; an informational note is fine, an error is not.)

- [ ] **Step 3: Sanity-check YAML parses**

```bash
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml')); print('YAML OK')"
```

Expected: `YAML OK`

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add release workflow that builds Windows + Mac installers"
```

- [ ] **Step 5: End-to-end validation via a prerelease**

This is the real test for Tasks 2 and 3 (Inno Setup can only be compiled on Windows; the .iss and the workflow both validate here).

1. Push the branch and open a PR so CI runs on the change (the existing `build-and-test.yml` will exercise; `release.yml` will NOT run on a PR — it only fires on `release.published`).
2. Merge the PR.
3. On GitHub, create a **prerelease** (mark "Set as a pre-release") with a tag like `v0.0.0-test` from `main`.
4. Publishing the release triggers `release.yml`. Watch both jobs go green in the Actions tab.
5. Confirm both assets appear on the release page:
   - `DTXMania-Setup-0.0.0-test.exe`
   - `DTXMania-0.0.0-test-arm64.dmg`
6. Download-smoke (optional but recommended):
   - Windows: run the `.exe` in a clean Windows VM, confirm it installs, the Start Menu shortcut launches the game, and `%LOCALAPPDATA%\DTXManiaCX\System\Graphics` is populated.
   - Mac: open the `.dmg`, drag `DTXMania.app` to Applications, right-click → Open (Gatekeeper warning expected — no signing). Confirm it launches. (Default-skin textures will be missing on Mac until the follow-up seeding PR — that's the known open issue from the spec.)
7. If anything fails, fix the offending task's file(s) and re-run the failed job from the Actions UI (or delete + republish the prerelease). Once green, mark the prerelease as a Draft or delete it so end users don't see `0.0.0-test`.

- [ ] **Step 6: Final commit (only if Step 5 surfaced fixes)**

If end-to-end validation required edits to any of the three files, stage and commit them with `fix(installer): <what changed>`. If no fixes were needed, skip this step.

---

## Done criteria

- [ ] `installer/macos/Info.plist.template`, `installer/macos/build-dmg.sh`, `installer/macos/test-build-dmg.sh` committed; smoke test passes locally; shellcheck clean.
- [ ] `installer/windows/dtxmania.iss` committed; `#error` guards present; `System\*` installs to `{localappdata}\DTXManiaCX\System`.
- [ ] `.github/workflows/release.yml` committed; actionlint clean; triggers only on `release.published`; both jobs upload to the release.
- [ ] A prerelease end-to-end run produced both downloadable assets on the release page.
- [ ] Existing `build-and-test.yml` is unchanged (`git diff main -- .github/workflows/build-and-test.yml` is empty).

## Follow-up (out of scope, tracked in spec)

Mac first-run `System/` seeding: add game-code that, on launch, copies `<app-bundle>/Contents/Resources/System` → `~/Library/Application Support/DTXManiaCX/System` when the target is missing. Separate PR.
