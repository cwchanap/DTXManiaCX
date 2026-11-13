# HPA-797 — Reviewed implementation plan (single PR)

**Design:** [revised HPA-797 design](./2026-11-11-hpa-797-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-797/tests-slim-the-unit-tests-merge-the-mac-test-project-add-an-e2e  
**Reviews addressed:** [initial](https://github.com/cwchanap/DTXManiaCX/pull/236#pullrequestreview-5870335264) and [follow-up](https://github.com/cwchanap/DTXManiaCX/pull/236#pullrequestreview-5870550476)  
**Status:** At drafting time this plan was planning only, with no implementation, tests or screenshot generation. Implementation and local verification now exist on this branch; final Windows acceptance (CI plus manual screenshot review) remains pending.  
**Delivery:** **One PR (this same branch)**, target ~2 engineer days; <=3 engineer days max. Commit coherent stages of work to this branch; do not split into separate PRs.

## Agent directions

Read the design, current main and these files first. Changes are limited to tests, test project files, `codecov.yml`, `.github/workflows/build-and-test.yml`, `justfile` and active contributor docs. Keep existing Windows E2E job and process/API helpers. Do not refactor game code, DTXCreator or unrelated tests. The original goal of reducing duplicated suites is not a demand to eliminate all tests larger than game code.

### Step 0 — Capture baseline and inventory (~0.2 day)

- Confirm HPA-795 on main; note exact commit, current Windows/macOS CI status and test discovery/pass/skip counts. The reviewer's 9,401/9,402 macOS run had one checkout-specific sound asset failure; validate a clean worktree before attributing it to production.
- Generate the same Windows Cobertura project report CI uploads, and save **per-`DTXMania.Game` file line and branch covered/total** counts; do not settle for an overall percentage. Keep the baseline as PR evidence before editing coverage ignores. Use a **one-off, documented Python/PowerShell XML-to-CSV command**: parse Cobertura `<class filename>` entries, aggregate by filename, count distinct `<line number hits>` where hits > 0, and read available branch/condition counts (`<condition coverage>` or equivalent metadata). Emit a **sorted CSV** with `file,lines-covered,lines-total,branches-covered,branches-total` for the baseline and repeat the same command after consolidation. Verify the report's branch schema and avoid counting the same line twice across classes; don't commit a new analyzer or rely only on rounded rates.
- Inventory `Coverage`, `Additional`, `Patch` test file names and line counts; group by owning component. Reviewer reports 45 / 18,092 lines on 5db4218, with ~8 large files requiring manual decisions; remeasure.
- Inventory actual env-gated GraphicsDevice methods, including theories and mixed classes. Locate `MAC_BUILD` guards and active Mac csproj references. Confirm existing E2E captures and shared artifact directory.

**Exit:** Short PR note with baseline test counts, per-file coverage artifact/location, graphics-gated method list and bucket files grouped by size/owner.

### Step 1 — Canonical unit suite, truthful graphics skips, and coverage policy (~0.4 day)

Files: `DTXMania.Test/DTXMania.Test.csproj`, delete `DTXMania.Test/DTXMania.Test.Mac.csproj`, `DTXMania.Test/Helpers/`, existing graphics tests, `SongSelectionStageTests.cs`, `codecov.yml`, `build-and-test.yml`, `justfile`, `CLAUDE.md`.

1. Keep `DTXMania.Test.csproj` as the sole unit project. Remove the `MAC_BUILD` conditional code from `SongSelectionStageTests`, using the already-working normal code path. Delete the duplicate Mac project and change both OS jobs, commands, and active docs to the shared csproj. Keep the macOS bundled FFmpeg verification path and focused audio tests; fix any discovered real compilation issue minimally.
2. Add two small test-only xUnit attributes in `DTXMania.Test/Helpers/`: `GraphicsFactAttribute : FactAttribute` and `GraphicsTheoryAttribute : TheoryAttribute`. Share the existing `DTXMANIACX_ENABLE_GRAPHICS_TESTS == 1` check; when disabled, each reports an explicit `Skip` reason. Replace `[Fact]` and `[Theory]` **only on device-required methods**, keeping normal logic facts/theories in mixed classes. Existing GPU-only theories include `GPURenderingSnapshotTests.GPU_SpriteScaling_VariousScales_VerifyCorrectSizing`, `ResourceManagerTests.SetBoxDefSkinPath_WithEmptyOrNullPath_ShouldClearPath`, and `SongBarRendererTests.GenerateClearLampTexture_WithDifferentDifficulties_ShouldReturnTexture` / `GenerateTitleTexture_WithDifferentNodeTypes_ShouldGenerateAppropriateText`. Preserve all their InlineData rows. No new traits or platform filters.
3. For opted-in graphics Facts **and Theories**, a missing GraphicsDevice is a **failure** rather than a silent `return`. Consolidate redundant env checks and ensure all remaining device tests actually execute under opt-in. When not opted in, test discovery must report explicit **Skipped** (including every InlineData case), not fake **Passed**. Keep portable non-device tests running on both systems.
4. Use `dotnet test DTXMania.Test/DTXMania.Test.csproj` on Windows **and** macOS with no OS-specific filter. Keep Automation and VideoRecorder tests separate.
5. Update `codecov.yml`: project 80%, zero threshold, non-informational and missing-report failure; patch informational, **delete** the `NotationStripRenderer.cs` ignore. On the Windows job, missing Cobertura becomes a hard failure and Codecov upload uses `fail_ci_if_error: true`. Verify project coverage status is a required check if branch settings allow it; record if it is not, instead of asserting enforcement. On macOS, drop unused XPlat coverage collection **and its unused coverage-file check**; keep test reporting/artifact uploads and the test exit-code gate.

**Exit:** One unit project, normal cross-platform test command, truthful GPU Skipped reporting, 80% project coverage target and fail-closed Windows Codecov workflow. Build/tests green on Windows/macOS (except documented environment issues). Make one green commit on this PR branch.

### Step 2 — Complete screenshots using current E2E tests (~0.4 day)

Files: `DTXMania.E2E/DefaultSkinFirstRunSmokeTests.cs`, `GameplayAutoPlaySmokeTests.cs` and (only if a helper is reused) `Support/E2EStageHelpers.cs`. **Do not create `StageScreenshotTourTests.cs` or a new test fixture/runner.**

1. Extend the existing `DefaultSkinFirstRunSmokeTests` journey, which already builds with `useBundledDefaultSkin: true`. Keep its Title and Config screenshots. From Config, call `ChangeStageAsync("DrumConfig")`, wait for DrumConfig to become active and finish fading, capture DrumConfig and return to Config. Then navigate normally to SongSelect and add a required screenshot before opening the song status panel.
2. Continue first-run autoplay through Lanes and Result. The fixture already uses `extraChartBars: 8` (about 24 seconds); strengthen the Lanes capture guard to require effective Lanes display, gameplay readiness, **mid-song clock/visible unjudged notes**, and no transition, and recheck state around capture. **After capturing Lanes**, call the existing `WaitForCompletedResultAsync(client, cancellationToken)` (move it from `GameplayAutoPlaySmokeTests` into `E2EStageHelpers` if reused) to wait for `Result` and `StageCompleted`. This does **not** mean the result PNG is ready: independently reuse the existing game-written `result-*.png` poller from `GameplayAutoPlaySmokeTests` to wait until the signature and trailing PNG IEND are complete. Then copy the file from temp `AppDataRoot/Screenshots` to the tour destination and validate it. Use bounded polling rather than a fixed unbounded sleep.
3. Extend existing `GameplayTabAutoPlay_ShouldJudgeEveryNoteInEffectiveTabModeAndComplete` fixture with `useBundledDefaultSkin: true`. Preserve its mid-song effective Tab + BGA guard. Save its already-required screenshot to the same tour artifact folder; put the test's generic logs/fixture evidence in a **separate `tab-autoplay/` artifact subfolder** first.
4. Preserve **per-test fixture artifact roots**, e.g. `TestResults/e2e/default-skin/` (existing) and `TestResults/e2e/tab-autoplay/` (new for the Tab test), so `E2EArtifactWriter.CopyFixtureFiles` and common log filenames cannot overwrite one another. Route **only seven distinct required PNGs** to the sibling `TestResults/e2e/hpa-797-tour/` folder with an explicit capture destination or a small validated `File.Copy`; never set the two `fixture.ArtifactRoot` values to this shared tour folder. `DTXMania.E2E/AssemblyInfo.cs` already has `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, **so test concurrency is not the actual problem**; separate roots prevent sequential overwrites and future accidental coupling. Reuse `CaptureRequiredScreenshotAsync` PNG integrity checks and promote completion/capture helpers to `E2EStageHelpers` only if they are genuinely reused. Validate all seven tour PNGs are present/complete at the end of Windows E2E (required test captures plus a simple post-run CI file check if needed, no new CI job).
5. Preserve existing failure log/screenshot behavior. The `gameplay-e2e-windows` job already uploads `TestResults/e2e`; do not replace it. No new process launch or image-diff tooling.

**Exit:** Windows `Category=E2E` produces **seven** valid, bundled-default-skin screenshots; visually inspect each image in the uploaded artifact for layout, clipping, readability and correct stage. Make one green commit.

### Step 3 — Consolidate bucket tests last; explain coverage movement (~0.9 day)

- Work from small to large. For the small `*Coverage*`, `*Additional*`, `*Patch*` test files, move whole distinct test methods into the natural existing `*Tests` suite, adjusting only name/fixtures as needed. Remove obvious duplicates and line-touch-only assertions.
- Manually review the ~8 largest bucket files. Keep meaningful parsing/timing, input, score persistence, judge/score, stage lifecycle and error-handling cases. Prefer minimal relocation into their owner suites over test/harness redesign. Do not rewrite reflection-built stage tests; later architecture tickets own those.
- After each domain, run focused tests. Re-generate the **same Cobertura coverage report** as baseline; run the **same XML-to-sorted-CSV command** from Step 0 on before/after reports and diff per-game-file covered/total **line and branch** counts. Document a per-file justification for any loss. Coverage drift alone cannot detect semantic test loss, so also document unique behavior kept/dropped from the larger buckets.
- No `Coverage`, `Additional` or `Patch`-named test source remains. Do not delete a unique test to make that naming goal easy: move it intact instead.
- Verify project coverage still meets 80% with the renderer ignore removed. If not, find genuine lost coverage or keep meaningful tests; **do not** add ignores or change the target to hide it.

**Exit:** All bucket-named files eliminated, focused tests green, before/after test counts and per-file line/branch coverage delta (including explanations for decreases) in PR. Make a separate green **commit**, **same PR**.

### Step 4 — Final acceptance and handoff (~0.2 day)

- **Windows:** build `DTXMania.Game.Windows.csproj`, run canonical unit suite, Automation and VideoRecorder tests, full `Category=E2E` including seven screenshot evidence files. Check that a normal headless unit run reports Skipped device tests rather than Passed, and opted-in GPU test failures are visible if no device can initialize.
- **macOS:** build `DTXMania.Game.Mac.csproj`; run exact same canonical unit project with `ALSOFT_DRIVERS=null` and no filter, plus Automation/VideoRecorder tests and existing audio E2E. Check bundled FFmpeg runtime and no stale Mac project commands.
- Verify Windows coverage report exists, upload succeeds, Codecov project >=80%, and project check is required for merging where possible. Patch status informational. Windows CI screenshot artifacts must exist **in the uploaded artifact**, not just under a temp sandbox.
- Check screenshots against actual rendered UI and any Linear UI mockup (none is attached to HPA-797 at planning time). Annotate visual findings rather than automatically expanding scope.
- Attach command results, pass/skip counts, before/after coverage comparison, screenshot artifact link and any documented exceptions. Only then mark this **same** PR ready for review.

## Acceptance checklist

- [ ] One `DTXMania.Test.csproj` on Windows/macOS; no active `MAC_BUILD` or Test.Mac references
- [ ] GPU Facts **and Theories (all rows)** explicitly Skipped by default; fail visibly if graphics requested but unavailable
- [ ] Whole-project 80% gate; advisory patch; obsolete renderer exclusion removed
- [ ] Windows Cobertura missing or upload failure fails CI; Codecov merge-status requirement verified/reported
- [ ] No coverage/additional/patch bucket source remains; unique behavior retained; game-file line/branch coverage comparison reviewed
- [ ] Existing E2E journeys upload seven **bundled CX Neon** stage PNGs including real mid-song Lanes and Tab, Result after completion + IEND; separate per-test diagnostic roots from the shared tour PNG directory
- [ ] Windows and macOS builds/unit/E2E acceptance applicable to each OS pass; no Android/Linux manual acceptance
- [ ] Changes and evidence remain in **one PR**, with no production code or gameplay changes

**Review handling:** Accept the review's simplifications and reordering; intentionally **do not** adopt its optional separate PR suggestion because the project requires one PR per ticket. Do not claim a coverage delta fully proves preservation of meaningful assertions.
