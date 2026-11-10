# HPA-798 — Implementation plan (reviewed; single PR)

**Design:** [HPA-798 reviewed design](./2026-11-09-hpa-798-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-798/repo-make-cx-neon-the-system-skin-and-refresh-claudemd-and-docs  
**PR:** https://github.com/cwchanap/DTXManiaCX/pull/235  
**Status:** Planning only; no game code, assets, packaging or tests changed in this draft.  
**Budget:** <=3 engineer days, **one PR for this ticket**, not one PR per section.

## Agent brief

Implement the reviewed design on the **existing PR #235 branch**. Read current main/branch before editing; do not work from stale line numbers. Preserve gameplay, runtime skin resolution, config persistence, custom skins and unrelated code. This is relocation, build/CI and documentation cleanup, **not** a skin architecture rewrite. No new artwork or SFX. Avoid excessive defensive scaffolding, alternate skin loaders, new CI scripts or new MSBuild infrastructure.

Commit coherent, reviewable changes on the same branch. Arrange asset move and all paths/CI retargeting together so the pushed branch is not left in an intermediate broken state; use `git diff -M` to inspect renames.

## 0. Audit current source and local leftovers (before edits)

Inspect:
- `System/{Graphics,Theme.ini,CXNeon/}` and existing untracked/ignored `System/Sounds` and `System/Script`.
- `.gitignore` (currently hides most of `System/`), `tools/skingen/{skingen.py,test_skingen.py,manifest.json}`, `tools/sfxgen/{sfxgen.py,manifest.json}`, `tools/practicegen/practicegen.py`.
- `DTXMania.Game/{DTXMania.Game.Mac.csproj,DTXMania.Game.Windows.csproj}`, `DTXMania.Test/Resources/CxNeonPackTests.cs`, current ConfigManager/ResourceManager test seams.
- `.github/workflows/{build-and-test,release}.yml`, `installer/windows/dtxmania.iss`, `installer/macos/{build-dmg.sh,test-build-dmg.sh}`, `justfile`.
- `CLAUDE.md`, `AGENTS.md` symlink mode, `docs/` and five inbound references in design.

**Before changing ignore rules:** run `git clean -ndX System/` (dry-run, not `-fdX`), `git ls-files -- System/` and a filesystem inventory. Look for untracked NX-era OGG/Script or other contamination. Do not indiscriminately wipe local user assets. Record which files are intended to ship (the currently committed CX Neon pack) and verify them again after moving.

## 1. Canonical asset move, path retarget and .gitignore (one coherent change)

- [ ] Remove tracked old NX `System/Graphics/**` and `System/Theme.ini`. Move existing committed `System/CXNeon/{Graphics,Sounds,Theme.ini}` to `System/{Graphics,Sounds,Theme.ini}`; move/rewrite `System/CXNeon/README.md` as `System/README.md` and remove the empty nested directory. Do not regenerate assets.
- [ ] Change `.gitignore` from `System/*` + unignore allowlist to normal tracking of `System/` while ignoring only junk (e.g. `desktop.ini`, `*.log`, `.DS_Store`). Run `git status --untracked-files=all -- System/`; inspect and remove **known NX leftovers** from the working directory without staging unrelated files. A clean checkout should have only the accepted pack under `System`.
- [ ] In `tools/skingen/skingen.py`, set default compose/validate pack root to `System`. Delete `NX_PACK_ROOT` and the bootstrap branch that auto-measures null dimensions from NX art; retain authored manifest dimensions/requiredness and update help/docstring. Update path-sensitive `tools/skingen/test_skingen.py` tests, including lag atlas.
- [ ] Set `tools/sfxgen/manifest.json` output to `System/Sounds`; retarget `tools/practicegen/practicegen.py` click source to `System/Sounds`. No new SFX generation or changes to click audio.
- [ ] Retarget all `DTXMania.Test/Resources/CxNeonPackTests.cs` source paths to root `System`; remove early-return guards that let missing `Graphics`, `Sounds` or `Theme.ini` pass vacuously. Preserve checks for all runtime and Theme.ini-only assets.
- [ ] Retarget existing `build-and-test.yml` source-pack validator paths **in the same integration change** so CI cannot pass while referencing the obsolete nested pack. Update other active tool/test paths found by search. Remove obsolete `just install-cx-neon` instructions/recipe rather than retargeting the symlink.
- [ ] Search `git grep -n -i cxneon -- ':!docs'` and source path concatenations (`os.path.join`/`Path.Combine`) for unretargeted nested-pack references. Brand names and independently named test fixtures need not be renamed.

**Gate A:**
```bash
python3 -m unittest discover -s tools/skingen -p 'test_*.py' -v
python3 -m unittest discover -s tools/sfxgen -p 'test_*.py' -v
python3 -m unittest discover -s tools/practicegen -p 'test_*.py' -v
python3 tools/skingen/skingen.py validate
python3 tools/sfxgen/sfxgen.py validate  # requires ffmpeg with OGG/Vorbis support
python3 tools/practicegen/practicegen.py generate
git diff --exit-code -- DTXMania.Game/BundledSongs
```

Confirm moved assets match original bytes (Git rename detection/checksums where useful), and tracked+untracked `System` inventory has no legacy artifacts.

## 2. Copy assets by existing MSBuild conventions and test immediately

- [ ] In **both** `DTXMania.Game.Mac.csproj` and `DTXMania.Game.Windows.csproj`, add three `None Include` patterns for `../System/Theme.ini`, `../System/Graphics/**`, `../System/Sounds/**`, linked to the relative `System/...` destination. Set `CopyToOutputDirectory=PreserveNewest` and `CopyToPublishDirectory=PreserveNewest`. Mirror the existing OFL/BundledSongs item pattern; **no shared target**.
- [ ] Build a dev output and inspect `<game-bin>/System`. Run existing skingen validation with `--pack <game-bin>/System`, plus checks for Theme.ini/required OGG files.
- [ ] **Immediately run the full macOS unit suite.** A test project referencing the game may also receive `System` under its own output; some previous tests relied on it being absent. Fix only those broken assumptions with existing `ConfigManager.ResolveSkinPath(..., baseDir)` / `AppPaths.GetBundledSystemSkinRootCandidates(baseDir)` test seams or the ResourceManager test override. Do not suppress copying into test output or modify runtime resolution to satisfy a test.
- [ ] Ensure an existing custom app-data skin and the persistent `Default` token remain intact. Prefer no production runtime changes.

**Gate B:**
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
# pass the actual build-output System folder
python3 tools/skingen/skingen.py validate --pack <game-bin>/System
```

## 3. Simplify installers/DMG and validate the published assets

- [ ] `installer/windows/dtxmania.iss`: remove `SystemSkinDir` define and its second `[Files] Source:` item. Retain the recursive `SourceDir` item (publish output already includes `System`) and **keep** `[InstallDelete] {app}\System` for upgrades. Update the Inno build-and-test smoke to invoke `iscc` without `/DSystemSkinDir`. Gate: successful Inno compilation and exactly one skin source entry; no installer extraction framework.
- [ ] `installer/macos/build-dmg.sh`: simplify to `build-dmg.sh <publish_dir> <version> <ico_path> <output_dir>`. After the copy into `Contents/MacOS`, move `MacOS/System` to `Contents/Resources/System` and assert required content exists. Update `installer/macos/test-build-dmg.sh` and the release caller to the same signature. Check no duplicate `Contents/MacOS/System`.
- [ ] In `build-and-test.yml`, remove the obsolete post-publish “Bundle CX Neon skin” and self-referential “Assert no NX-derived assets” steps. Keep source-pack integrity gates and add/retarget **published** pack validation. Do not leave “pack missing -> exit 0” shortcuts.
- [ ] In `release.yml`, remove both Windows/macOS “Stage base System skin” jobs/steps and `SYSTEM_SKIN_DIR` hand-offs; keep source and published-pack validators. Publish now supplies the skin directly. Update any other current packaging callers (including direct archive jobs) to avoid double copying.
- [ ] Add only a minimal `validate --out-dir <path>` argument for `tools/sfxgen/sfxgen.py`'s **existing** validator, so both source and published `Sounds` can be validated. Reuse `skingen.py validate --pack <published-System>`. No new NX equality-assertion script: asset rights are protected by removing the NX source tree and reviewing the tracked inventory.

**Gate C:**
```bash
dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj -c Release -o ./publish/mac-check
python3 tools/skingen/skingen.py validate --pack ./publish/mac-check/System
python3 tools/sfxgen/sfxgen.py validate --out-dir ./publish/mac-check/System/Sounds
bash installer/macos/test-build-dmg.sh

# Windows on Windows runner:
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
dotnet publish DTXMania.Game/DTXMania.Game.Windows.csproj -c Release -o ./publish/win-check
python tools/skingen/skingen.py validate --pack ./publish/win-check/System
python tools/sfxgen/sfxgen.py validate --out-dir ./publish/win-check/System/Sounds
```

Installer compile smoke must pass without `SystemSkinDir`. Verify macOS `.app` has one skin at `Contents/Resources/System` and no `Contents/MacOS/System`.

## 4. Bounded docs cleanup (do not enlarge scope)

- [ ] Update **`CLAUDE.md` only** to reflect current stages (including DrumConfig), MonoGame/.NET 10 platform builds, `DTXMania.Automation`/`DTXMania.VideoRecorder` projects, test commands, canonical `System` skin, source-art generators and custom-skin behavior. Avoid unstable component counts. Verify `git ls-files -s AGENTS.md` still reports a **symlink** (mode `120000`), not a duplicate content file.
- [ ] Remove `docs/superpowers/` and these five obsolete snapshots: `docs/skin-system-implementation.md`, `docs/graphics-management-implementation.md`, `docs/song-data-management-system.md`, `docs/song-selection-system-implementation.md`, `docs/stage-transition-system-implementation.md`. Keep current `docs/proposals`, evidence, verification, recorder docs and useful analysis. No open-ended “audit everything” deliverable.
- [ ] Repair five code/config links into deleted `docs/superpowers`:
  `.coderabbit.yaml`; `DTXMania.Game/Content/Bravura-Notation.spritefont`; `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`; `DTXMania.Game/Lib/Song/NxScoreData.cs`; `DTXMania.Test/Stage/Performance/PreparedGameplayAudioSetBenchmark.cs`. Remove obsolete references; point relevant CX Neon provenance notes to `System/README.md`, without breaking runtime semantics.
- [ ] Update active `tools/README.md`, root README and `System/README.md` as needed. Search remaining active documentation for nested `System/CXNeon` setup or dead links into deleted docs.

## 5. Final functional and release evidence

- [ ] **Clean first run:** use `DTXMANIA_APPDATA_ROOT` pointing to a fresh temp directory, not the developer's existing configuration. Verify default CX Neon without manual symlinks; exercise Title, Config and one gameplay screen.
- [ ] **Windows:** existing graphical Gameplay E2E already launches from build output with sandboxed app-data; use it as the primary Windows default-skin evidence. Run full Windows CI and upload screenshots/artifacts.
- [ ] **macOS:** capture actual first-run screenshots and run the Mac suite, published-pack validators and DMG smoke. Record commands, test totals and expected paths.
- [ ] **Previously installed dev machines:** document **manual optional** cleanup only. A stale `<appdata>/System/CXNeon` symlink or NX artwork under `<appdata>/System/{Graphics,Sounds,Script}` can override the new default. After checking personal customizations, remove these old local items and reselect `Default`. No automatic deletion or config migration.
- [ ] Review full branch diff for unexpected binary files, Git rename detection, missing assets and stale references. Use CI/manual `workflow_dispatch` if draft PR conditions skip normal PR builds; do not mark Ready simply to run CI.

**Final acceptance:** one CX Neon default, no NX-derived files in the active repo tree or publish/install outputs, one copy in each output/artifact, original asset bytes preserved, current source+published asset validation, no regressions in custom skins, tests/E2E green, docs corrected and `AGENTS.md` still a symlink.

**Stop condition:** If unrelated behavior, newly generated art/SFX or other architecture is required, leave it out of this ticket and record a follow-up. Do **not** create another PR for HPA-798.