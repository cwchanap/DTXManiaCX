# HPA-798 — Implementation plan (one PR)

**Design:** [2026-11-09 HPA-798 design](./2026-11-09-hpa-798-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-798/repo-make-cx-neon-the-system-skin-and-refresh-claudemd-and-docs  
**Status:** Plan only. No code, assets or release workflow changes have been implemented by this documentation-only draft.  
**Delivery:** Implement on **this existing draft PR/branch**, <=3 engineer days. One ticket = one PR.

## Instructions for implementation agent

Read the linked design, inspect current main/branch and preserve unrelated changes. Prefer renames, deletion and existing build/packaging paths over new classes or abstraction layers. Do **not** create a second PR or break the work into separate PRs. Do not generate art or sound; only relocate existing CX Neon assets. Keep runtime behavior, config resolution, user-selected custom skins, gameplay and installer-managed versus user-managed files unchanged.

A successful result is a clean dev launch and release using the **same** canonical `System/` tree, with documented tests and package inspection. Implement in the order below; make coherent commits on this one branch.

### 0. Reconcile baseline (short audit)

Inspect and search the current repo for:
- `System/Graphics`, `System/Theme.ini`, `System/CXNeon`; `tools/skingen/{skingen.py,test_skingen.py,manifest.json}` and `tools/sfxgen/{sfxgen.py,manifest.json}`.
- `DTXMania.Game/DTXMania.Game.Windows.csproj` and `DTXMania.Game/DTXMania.Game.Mac.csproj`; `AppPaths.GetBundledSystemSkinRootCandidates` and `ConfigManager.ResolveSkinPath`.
- `.github/workflows/{build-and-test,release}.yml`, `installer/windows/dtxmania.iss`, `installer/macos/{build-dmg.sh,test-build-dmg.sh}`, `DTXMania.Test/Resources/CxNeonPackTests.cs`, `justfile`, `tools/README.md`, `CLAUDE.md`, `AGENTS.md` and `docs/`.
- Existing links to `docs/superpowers`, including active test/tool references. Note any currently merged or ongoing changes before deleting files.

Record expected asset inventory (Graphics/Sounds/Theme.ini) and, if useful, checksums before relocation. Do not commit any old NX-derived files into the new default pack.

### 1. Replace the source skin with the release skin

1. Move `System/CXNeon/Theme.ini` to `System/Theme.ini` (replace NX values). Move `System/CXNeon/Graphics/**` to `System/Graphics/**`, **after removing** the old NX-derived `System/Graphics`. Move `System/CXNeon/Sounds/**` to `System/Sounds/**`. Rewrite/move the pack README for the new root. Delete the empty `System/CXNeon` tree.
2. Confirm expected file inventory and image/audio bytes stay the same; prefer `git mv` where practical, but never mistake similarly named NX assets for original CX Neon art.
3. Update tool defaults and manifests:
   - `tools/skingen/skingen.py`: compose/validate target `System` and help text. Review the old `NX_PACK_ROOT` used for bootstrap; ensure bootstrap does not treat the new CX Neon tree as source NX artwork. Remove an obsolete NX-only code path if unnecessary.
   - `tools/sfxgen/manifest.json`: `output_dir` becomes `System/Sounds`; verify any corresponding tests use the manifest rather than old paths.
   - Update relevant skingen tests (including lag atlas), `CxNeonPackTests`, `tools/README.md` and asset references elsewhere in active tools.
4. Change pack tests from obsolete “not yet generated, skip” assumptions to **required** inventory checks. Keep integrity decoding/dimension validation in Python and required runtime-asset completeness in C#.
5. Remove or simplify `justfile`'s obsolete `install-cx-neon` recipe; do not leave instructions telling users to symlink the pack into app-data.

**Gate:** `python3 tools/skingen/skingen.py validate`, `python3 tools/sfxgen/sfxgen.py validate` and the Python tool unit tests pass against `System/`. Search active code/scripts for remaining `System/CXNeon` or old NX-default assumptions (historical PR text does not count).

### 2. Copy the canonical System pack through MSBuild

1. In the two platform game `.csproj` files, include `../System/Theme.ini`, `../System/Graphics/**` and `../System/Sounds/**` with appropriate `Link=System/...`, `CopyToOutputDirectory=PreserveNewest` and `CopyToPublishDirectory=PreserveNewest` metadata. Small parallel item groups are acceptable; introduce a shared MSBuild target only if it reduces real duplication without obscuring behavior.
2. Preserve all existing Content/`BundledSongs`, font-license and native FFmpeg items; do not touch `AppPaths`/`SkinManager` just to work around missing copied assets.
3. Verify `dotnet build` output and `dotnet publish` output on each platform contain **one** `System/{Theme.ini,Graphics,Sounds}`, with neither nested `System/CXNeon` nor files from the removed NX pack.
4. Add/update one focused filesystem assertion in the existing pack/config test suite if needed. Reuse existing `ConfigManager` default-token/relocation tests; a custom path must still win when selected.

**Gate:** clean default config resolves the bundled theme from output on both platforms; normal `dotnet run` needs no `just install-cx-neon` or manual link.

### 3. Simplify installer, DMG and workflows

**Windows:**
- `release.yml`: remove the flatten/stage step and `SYSTEM_SKIN_DIR` environment plumbing. Keep fail-closed validation against `System/` before installer creation.
- `installer/windows/dtxmania.iss`: remove `SystemSkinDir` macro, its duplicate `[Files]` source, and comments implying a nested source pack. Recursive `SourceDir` already includes the published `System`. **Keep** `[InstallDelete] {app}\System` and per-user custom-skin separation.
- Update the Inno compilation smoke invocation in `build-and-test.yml` to use the new installer arguments. Inspect compiled installer layout to confirm it contains CX Neon once.

**macOS:**
- `installer/macos/build-dmg.sh`: change invocation to `build-dmg.sh <publish_dir> <version> <ico_path> <output_dir>`. After copying publish contents to `Contents/MacOS`, move `Contents/MacOS/System` to `Contents/Resources/System` (fail if missing/incomplete); this avoids a second skin inside the app and matches current `AppPaths` candidate priority.
- Update `installer/macos/test-build-dmg.sh` and `release.yml` caller. Assert `Resources/System/Theme.ini` and representative Graphics/Sounds are present, while `MacOS/System` is absent. Keep executable, FFmpeg, license and icon packaging unchanged.

**Common CI:**
- Retarget `build-and-test.yml` validation and bundle checks to `System`. The publish step now copies the pack by MSBuild, so delete manual “Bundle CX Neon” copy and nested-root comparisons; assert required skin files are present and no NX/nested content is packaged.
- Fail closed for a missing pack. Do not keep CI branches that report successful validation when `System/Graphics` or `System/Sounds` is absent.
- Audit the other release/archive/portable jobs for duplicate copying. Retain integrity checks, existing paths for non-skin resources, and current Windows/macOS test gates.

**Gate:** portable publish, Inno installer smoke, macOS bundle smoke and current release workflows all use the same assets, with one skin per artifact.

### 4. Refresh only useful documentation

1. Update `CLAUDE.md` and `AGENTS.md` together (both are agent entry points): current .NET 10 and MonoGame projects; actual stage list including DrumConfig; accurate Performance-stage organization without hard-coded file counts; `DTXMania.Automation` JSON-RPC/process helpers; `DTXMania.VideoRecorder` and tests; E2E responsibilities; skin default/output/path rules; live build, test and tool commands. Re-read current code before claiming counts or namespace locations.
2. Update README, `tools/README.md`, skin README and relevant installer documentation to show `System/` as the single canonical location. Remove developer-only manual skin symlink guidance.
3. Delete `docs/superpowers/` as instructed by HPA-798. Audit top-level `docs/*-implementation.md` / historical architecture snapshots for false descriptions; prune only pages that are no longer true and not needed as current references. **Retain** live `docs/proposals/*` (including this pair), useful `docs/verification`, actively referenced `docs/evidence`, recorder docs and accurate analysis.
4. Search active docs/README/scripts and tests for dangling `docs/superpowers` links and obsolete `System/CXNeon` instructions; fix them. Add a concise deletion summary to the implementation PR.

**Gate:** active entry-point docs agree with code and each other; kept docs have valid links and no materially obsolete implementation instructions.

### 5. Required verification and evidence

Run at least:

```bash
# Asset generators and path-sensitive unit tests
python3 -m unittest discover -s tools/skingen -p 'test_*.py' -v
python3 -m unittest discover -s tools/sfxgen -p 'test_*.py' -v
python3 tools/skingen/skingen.py validate
python3 tools/sfxgen/sfxgen.py validate   # requires runtime-compatible ffmpeg

# macOS
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj -c Release -o ./publish/mac-check
bash installer/macos/test-build-dmg.sh

# Windows (on Windows runner)
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
dotnet publish DTXMania.Game/DTXMania.Game.Windows.csproj -c Release -o ./publish/win-check
```

Additionally:
- Clean-first-run manual smoke on supported Windows and macOS: `dotnet run --project ...` launches Title using **CX Neon**, no symlink or custom skin; exercise Config, Song Select and one Tab/Lanes game view and attach a small screenshot set.
- Inspect published files and assembled Windows installer/macOS `.app` (Windows `System` under app root, macOS `Contents/Resources/System` only) and compare the manifest-required inventory. Confirm no NX-derived or nested CXNeon directory, no duplicate installed skin copy.
- Test an existing custom app-data skin and the `Default` token (including relocated portable/app paths); neither changes user preference or default fallback semantics.
- Run full Windows/macOS CI and applicable release packaging tests. Draft PRs skip normal `pull_request` CI in this repo: use an existing manual workflow/`workflow_dispatch` on this branch for verification if needed; do not mark Ready solely to trigger CI.
- Record exact commands, platform, test totals, artifact evidence and any environment-limited checks. **Do not claim tests or a first-run smoke passed until executed.**

## Acceptance checklist

- [ ] Canonical root `System` holds the **existing CX Neon** assets; NX-derived source assets removed from current tree, history untouched.
- [ ] Fresh build/run/publish copies and resolves the default skin without symlinks on both platforms.
- [ ] No duplicate or nested skin in Windows portable/installer or macOS app; upgrade cleanup and app-data custom skins preserved.
- [ ] Integrity/completeness checks fail closed; source-art tools still compose/validate.
- [ ] Entry-point agent docs are accurate; obsolete `docs/superpowers` is gone, other live docs retained.
- [ ] Targeted and full tests + real visual/package evidence attached to **this PR**.

## Stop conditions / scope discipline

If a runtime defect requires changing gameplay, schema, asset visuals or a broader architecture, document it separately for later review instead of expanding HPA-798. If the work threatens the three-day budget, prioritize canonical skin + packaging + CI correctness and document any remaining stale-doc audit rather than splitting into multiple PRs without approval.