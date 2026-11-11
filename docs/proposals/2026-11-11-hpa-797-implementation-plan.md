# HPA-797 — Implementation plan (one PR)

**Design:** [HPA-797 design](./2026-11-11-hpa-797-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-797/tests-slim-the-unit-tests-merge-the-mac-test-project-add-an-e2e  
**Status:** Plan only; implementation and verification are not started.  
**Delivery:** Implement on **this same draft PR branch**, then mark ready for review after evidence. <=3 engineer days. No separate PRs for the sections.

## Instructions for implementation agent

Read the design and current code before editing. Keep the change confined to tests, test projects, CI, Codecov configuration, `justfile` and active contributor docs. Optimize for deletion/consolidation, not new helpers. Keep functioning behavioral coverage and use a single existing Windows E2E job. Do not introduce breaking changes to the game or touch DTXCreator.

### Step 0 — Baseline and source inventory (0.25 day)

- Verify HPA-795 is in main and record the current commit.
- Capture the full test discovery/count, pass/fail and project coverage on the applicable local platform before deletions. Record current Windows/macOS CI baseline or the most recent successful runs; do not assume the earlier 9,364 Mac tests or 89% coverage still apply.
- List `DTXMania.Test/**/*Coverage*.cs`, `*Additional*.cs`, and `*Patch*.cs` and their owning test files; inventory Mac project's old Compile exclusion list. Flag tests with device-only dependencies or `MAC_BUILD` guards. Check active mentions in `CLAUDE.md`, `justfile` and `.github/workflows/build-and-test.yml`.
- Establish that `Category=E2E` Windows CI is already uploading `TestResults/e2e`. No additional CI job is necessary.

**Exit:** Short before-state table (test count/coverage if obtainable), the bucket ownership list, and the GPU-only list in PR notes.

### Step 1 — Consolidate useful unit tests (1 day)

- Work by domain (song/chart, stage, UI/resources). Move unique behavioral cases out of each bucket-named class into an existing canonical suite, with assertions intact. Delete duplicates, line-hit-only tests and leftover bucket files.
- Prefer consolidation into `SongSelectionStageLogicTests`, `PerformanceStageDeterministicTests`, `StartupStageLogicTests`, `ResultStageScoreSaveTests` and each component's `*Tests` class when those are the actual owners. Use the closest real owner if a listed class was changed or removed.
- Do not migrate pure shape/reflection-only tests or rewrite the remaining reflection-constructed stage suites here. Preserve meaningful regression cases for new behavior and regressions found in production.
- Run focused tests per domain as files move. Finish with no bucket-named tests and a verified test count. Explain any test-count reduction by categorizing redundant vs preserved cases.

**Exit:** No `Coverage/Additional/Patch` bucket suites, no missing behavioral assertion without an explicit justification, and focused tests pass.

### Step 2 — Retire the Mac unit test project (0.75 day)

Files: `DTXMania.Test/DTXMania.Test.csproj`, delete `DTXMania.Test/DTXMania.Test.Mac.csproj`, tests formerly in the Mac exclusion list, `DTXMania.Test/Helpers/TestGraphicsDeviceService.cs`, `justfile`, `CLAUDE.md`, and `.github/workflows/build-and-test.yml`.

- Mark only genuine GraphicsDevice-dependent test methods/classes with `[Trait("Category", "GraphicsDevice")]`. Mac CI uses `dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "Category!=GraphicsDevice"` (with the existing coverage and reporter options). The same main project is restored and built on macOS; keep existing FFmpeg bundle verification using the unified output path.
- Remove `MAC_BUILD`-only test code/guards, especially in `SongSelectionStageTests`. Check that files formerly excluded from *compilation* now compile, then classify their runtime needs. Fix genuine portability issues without blanket source exclusion or fake success.
- Leave Windows test selection as broad as before; keep existing optional real-graphics opt-in. Leave the Automation/VideoRecorder/E2E test projects separate.
- Update Mac unit and focused audio commands, xUnit/coverlet inputs, `justfile`, `CLAUDE.md`, and any active workflow references to the deleted Mac csproj. Preserve existing native FFmpeg assertions and Codecov coverage uploads.

**Exit:** The one unit csproj compiles on both systems; Mac CI runs the portable suite, excluding only annotated real-device tests. No `MAC_BUILD` or active `DTXMania.Test.Mac.csproj` references remain.

### Step 3 — Add a player-stage screenshot tour (0.75 day)

Files: new `DTXMania.E2E/StageScreenshotTourTests.cs` (or similar) and, **only if necessary**, a tiny extension to `DTXMania.E2E/Support/E2EStageHelpers.cs`.

- Reuse `E2EGameLaunch`, `E2EFixtureBuilder`, `E2EStageHelpers.WaitForStageAsync`, `StartSongViaStatusPanelAsync`, `SaveScreenshotAsync`, and existing PNG validity validation from `DefaultSkinFirstRunSmokeTests`. Do not duplicate the process driver or JSON-RPC implementation.
- First fixture (Lanes, autoplay): start at Title and capture; open Config and capture; use the documented Game API `ChangeStageAsync("DrumConfig")` for the mapping screenshot and return; reach SongSelect and capture; start generated chart, wait for PerformanceReady/display style Lanes, capture; wait for completed Result and rendered reveal, capture.
- Second fixture (Tab, autoplay, long enough bars): configure `DrumsNoteDisplayStyle.Tab`, reach Performance, assert Tab readiness and capture. Two processes under one xUnit Fact are acceptable; keep artifact folders isolated and avoid shared mutable config.
- Name the seven PNGs clearly within `TestResults/e2e/hpa-797-tour/`, with separate subfolders for the two fixtures if needed. Assert each PNG is complete and nonempty, and log stage/style/selected chart with the artifact. If capture fails, preserve a failure screenshot and process stdout/stderr without hiding the test failure.
- Use existing `Category=E2E`, Windows gameplay E2E job and artifact upload. No visual baseline comparison, CI image classifier or new rendering changes.

**Exit:** One full Windows E2E run uploads seven readable PNGs corresponding to the specified stages. A failure is visible as a failed test, not merely missing files.

### Step 4 — Recalibrate coverage and verify (0.25 day)

- Change `codecov.yml`: mandatory project target 80% / threshold 0 / missing report failure; patch informational. Remove the `NotationStripRenderer.cs` special ignore if project coverage remains >=80% without it.
- Confirm test/coverage reports are actually produced and uploaded; verify status check semantics on the implementation PR. Do not lower the project gate further to get a green run.
- **Windows acceptance:** build `DTXMania.Game.Windows.csproj`; run `DTXMania.Test.csproj`, Automation and VideoRecorder tests, and Windows graphical `Category=E2E` including the tour. Attach screenshot artifact link and check all seven PNGs.
- **macOS acceptance:** build `DTXMania.Game.Mac.csproj`; run the unified unit project with `Category!=GraphicsDevice` and its coverage job; preserve existing audio E2E. Confirm packaged FFmpeg runtime checks pass.
- Explicitly check CI workflows on the **implementation** changes; draft docs-only PR checks may be skipped by existing workflow draft guards. Include pass/fail counts, coverage percentage, and screenshot proof in PR description. No manual Android/Linux acceptance.

## Final PR checklist

- [ ] No bucket-named test source files, with important unique tests preserved
- [ ] `DTXMania.Test.Mac.csproj` deleted and all active references changed
- [ ] All former compile exclusions considered, `MAC_BUILD` removed, GPU tests explicitly tagged
- [ ] Single unit csproj and coverage work on Windows/macOS
- [ ] Codecov project 80% mandatory; patch advisory; report upload verified
- [ ] Screenshot tour CI artifact has Title, SongSelect, Lanes, Tab, Result, Config and DrumConfig PNGs
- [ ] Gameplay and existing test behavior unchanged; no production changes
- [ ] Evidence attached before marking PR ready

**Scope handling:** If a unique old test would require a major rewrite, preserve that test minimally and note why; do not silently discard it or start a second PR. Escalate any issue that would materially exceed the 3-day budget rather than widening scope.
