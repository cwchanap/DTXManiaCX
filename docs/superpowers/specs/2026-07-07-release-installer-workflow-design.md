# Release Installer Workflow — Design

**Date:** 2026-07-07
**Status:** Approved
**Scope:** Add a GitHub Actions workflow that builds platform installers and attaches them to a GitHub Release when one is published.

## Goals

- Produce a Windows installer (Inno Setup `.exe`) and a macOS installer (`.app` bundle inside a `.dmg`) automatically when a GitHub Release is published.
- Installers are **self-contained** (bundle the .NET 8 runtime) — end users need zero prerequisites, per [MonoGame packaging guidance](https://docs.monogame.net/articles/getting_started/packaging_games.html).
- Attach both assets to the release that triggered the build.
- Version installers from the release tag.

## Non-Goals (out of scope)

- Code-signing certificates (Windows Authenticode, Apple Developer ID) — separate procurement.
- Apple Notarization — requires a paid Developer ID and App Store Connect credentials.
- Universal/fat macOS binaries — **arm64 only** (per decision). Intel Mac users are not supported.
- Auto-update mechanism — releases are manual downloads.
- Any change to the existing `build-and-test.yml` or its `build-artifacts` job.
- A first-run `System/` seeding code change for Mac — tracked as a **follow-up PR** (see Open Issues).

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Windows format | Inno Setup `.exe` | Scriptable, free, single setup.exe with wizard + uninstaller, easy to template via a committed `.iss` |
| Mac format | `.app` bundle inside `.dmg` | Most Mac-native; drag-to-Applications; `hdiutil` is built-in (no extra deps) |
| Self-contained | Yes | MonoGame officially recommends it; users need no runtime |
| Mac architecture | arm64 only | Apple Silicon target; `MMTools.Executables.MacOS.X64` runs under Rosetta for that one native component |
| Version source | Release tag (`github.event.release.tag_name`) | User controls version by tagging; no csproj edits |
| Workflow structure | **New** `.github/workflows/release.yml` | Clean separation from CI; release trigger and permissions stay isolated |

## Architecture

### Trigger and permissions

```yaml
name: Release
on:
  release:
    types: [published]
permissions:
  contents: write   # required for uploading assets to the release
```

Two jobs run in parallel; each uploads its own asset to the same release via `softprops/action-gh-release@v2` (idempotent — appends to the existing release created by the publish event).

### Version normalization (shared pattern, run once per job)

`tag_name` (e.g. `v1.2.3`) → strip leading `v` → numeric `1.2.3`. Exposed via `$GITHUB_ENV` as `APP_VERSION`. Used in:
- Inno Setup `MyAppVersion` (passed as `/DMyAppVersion=...` — no file mutation needed)
- `Info.plist` `CFBundleShortVersionString` (substituted via `sed` from a template)
- Output filenames: `DTXMania-Setup-1.2.3.exe`, `DTXMania-1.2.3-arm64.dmg`

## Components

### 1. `windows-installer` job (`runs-on: windows-latest`)

Steps:
1. Checkout code.
2. Setup .NET 8 (`actions/setup-dotnet@v4`, `dotnet-version: '8.0.x'`).
3. `dotnet restore DTXMania.Game/DTXMania.Game.Windows.csproj`.
4. Self-contained publish:
   ```
   dotnet publish DTXMania.Game/DTXMania.Game.Windows.csproj `
     -c Release -r win-x64 --self-contained `
     -p:PublishReadyToRun=false -p:TieredCompilation=false `
     -o ./publish/win
   ```
5. Install Inno Setup: `choco install innosetup` (preinstalled channel on `windows-latest`).
6. Compile installer:
   ```
   iscc /DMyAppVersion="$env:APP_VERSION" `
        /DSourceDir="publish\win" `
        installer\windows\dtxmania.iss
   ```
   → produces `installer/windows/Output/DTXMania-Setup-<version>.exe`.
7. Upload to release via `softprops/action-gh-release@v2`.

### 2. `installer/windows/dtxmania.iss` (new file)

Responsibilities:
- `#define MyAppName "DTXManiaCX"`, `#define MyAppVersion`, `#define MyAppExeName "DTXMania.Game.exe"`.
- Source dir from `#define SourceDir` (overridable via `/D`).
- Default install location `{autopf}\DTXManiaCX` (per-user `{userpf}` to avoid admin elevation).
- Installs all files from `SourceDir` recursively.
- **Copies the default skin:** installs repo `System\` tree to `{localappdata}\DTXManiaCX\System` so the game finds it on first launch (the game reads `System/` from `%LOCALAPPDATA%\DTXManiaCX\System`, not next to the exe — see `AppPaths.GetDefaultSystemSkinRoot()`).
- Start Menu shortcut + optional Desktop shortcut.
- Uninstaller via `CreateUninstallRegKey`.
- No Authenticode signature (SmartScreen will warn — acceptable for first release).

### 3. `mac-installer` job (`runs-on: macos-latest`)

Steps:
1. Checkout code.
2. Setup .NET 8.
3. `dotnet restore DTXMania.Game/DTXMania.Game.Mac.csproj`.
4. Self-contained publish:
   ```
   dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj \
     -c Release -r osx-arm64 --self-contained \
     -p:PublishReadyToRun=false -p:TieredCompilation=false \
     -o ./publish/mac
   ```
5. Assemble `.app` bundle (delegated to `installer/macos/build-dmg.sh`, see below):
   - `DTXMania.app/Contents/MacOS/` ← all published files (exe, dlls, runtime, Content)
   - `DTXMania.app/Contents/Resources/System/` ← repo `System/` default skin (bundled for the future seeding fix; not yet consumed by the game)
   - `DTXMania.app/Contents/Info.plist` ← rendered from `installer/macos/Info.plist.template` with `APP_VERSION` substituted
   - `DTXMania.app/Contents/Resources/DTXMania.icns` ← converted from `DTXMania.Game/Icon.ico` via `iconutil` (build an `.iconset` from the ICO's embedded PNG sizes, then `iconutil -c icns`). If the ICO lacks suitable sizes, fall back to embedding the ICO directly and referencing it via `CFBundleIconFile` without conversion.
6. `chmod +x` the `MacOS/DTXMania.Game` executable.
7. Ad-hoc codesign: `codesign --force --deep --sign - DTXMania.app` (Gatekeeper will still warn; users right-click → Open, or run `xattr -dr com.apple.quarantine DTXMania.app`). No Notarization.
8. Build DMG via built-in `hdiutil create -volname "DTXMania" -srcfolder ... -fs HFS+ -format UDZO DTXMania-<version>-arm64.dmg`.
9. Upload to release via `softprops/action-gh-release@v2`.

### 4. `installer/macos/Info.plist.template` (new file)

Standard MonoGame-style plist (per MonoGame docs) with placeholder `__APP_VERSION__` for `CFBundleShortVersionString`:
- `CFBundleIdentifier`: `com.dtxmaniacx.DTXMania`
- `CFBundleExecutable`: `DTXMania.Game`
- `CFBundleIconFile`: `DTXMania`
- `CFBundleName` / `CFBundleDisplayName`: `DTXMania`
- `LSMinimumSystemVersion`: `11.0` (arm64 requires Big Sur+)
- `LSApplicationCategoryType`: `public.app-category.games`
- `LSArchitecturePriority`: `["arm64"]`
- `LSRequiresNativeExecution`: `true`
- `NSHighResolutionCapable`: `true`
- `NSPrincipalClass`: `NSApplication`

### 5. `installer/macos/build-dmg.sh` (new file)

Bash script that takes `publish_dir`, `system_dir`, `version`, and `output_dir` arguments and performs: bundle assembly → plist substitution → icns conversion (via `sips`/`iconutil` or fallback copy) → chmod → codesign → `hdiutil`. Keeps the YAML job readable and is locally testable on a developer Mac.

## Data Flow

```
GitHub Release "published" event
  └─ release.yml triggers
      ├─ windows-installer job
      │     publish win-x64 → iscc → DTXMania-Setup-<v>.exe
      │     └─ upload to release
      └─ mac-installer job
            publish osx-arm64 → build-dmg.sh → DTXMania-<v>-arm64.dmg
            └─ upload to release
```

## Error Handling

- `softprops/action-gh-release` fails the job if upload errors; both jobs are independent (one platform's failure doesn't block the other).
- `iscc` returns nonzero on compile error; script references missing → fast fail.
- `hdiutil` failing (e.g. bundle assembly problem) returns nonzero.
- No retry logic — contributors rerun the failed job via the Actions UI.

## Testing

This is CI/infra (no unit tests). Verification strategy:
- **Workflow lint:** `actionlint` (if available) or visual YAML review.
- **YAML syntax:** Ensure `dotnet build` of the solution is unaffected (no code changes).
- **Manual smoke (developer):** Run `installer/macos/build-dmg.sh` locally on a Mac to confirm it produces a working `.dmg` before relying on CI.
- **End-to-end:** Trigger via a real (or draft/prerelease) GitHub Release and confirm both assets appear and are downloadable/installable on a clean VM.

## Open Issues

### Mac `System/` seeding (follow-up PR)

The game reads `System/` only from `<app-data>/System` (see `AppPaths.GetDefaultSystemSkinRoot()`):
- Windows: `%LOCALAPPDATA%\DTXManiaCX\System` — populated by the Inno Setup installer. ✅ Works at first launch.
- Mac: `~/Library/Application Support/DTXManiaCX/System` — a `.dmg` drag-install has **no postinstall hook** to populate this directory.

**Resolution chosen:** ship the `.dmg` now with `System/` bundled inside `DTXMania.app/Contents/Resources/System/`. The game does not yet read from there, so first launch on Mac shows missing default-skin textures. A **separate follow-up PR** will add first-run seeding logic: on launch, if `<app-data>/System` does not exist, copy it from the bundled `Contents/Resources/System/`. The first release's notes will document the temporary workaround (`xattr -dr com.apple.quarantine` + optional manual copy).

## File Inventory (new)

| Path | Purpose |
|---|---|
| `.github/workflows/release.yml` | The new release workflow |
| `installer/windows/dtxmania.iss` | Inno Setup script |
| `installer/macos/Info.plist.template` | Mac bundle metadata template |
| `installer/macos/build-dmg.sh` | Mac bundle + DMG assembly script |

No existing files are modified.
