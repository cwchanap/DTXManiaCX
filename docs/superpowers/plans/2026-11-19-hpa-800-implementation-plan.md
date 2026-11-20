# HPA-800 — Implementation plan (single PR; <=3 engineer days)

**Design:** [HPA-800 design](../../proposals/2026-11-19-hpa-800-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-800  
**Branch and PR:** `plan/hpa-800-performance-session-playfields`; continue implementation on **this same PR** after design review.  
**Baseline:** main `241d78d8ed040a3d7d69f01562462b5ad57ad7ab` (HPA-799 merged).  
**Status:** Plan only. No implementation or test execution was performed while writing these documents.

## Agent contract

Deliver one focused, behavior-preserving refactor in at most 3 engineer days. Do **not** open a second PR for this ticket or introduce implementation-only helper tickets. Prefer direct constructors and existing MonoGame abstractions. There is no need for a DI framework, new background-work queue, compatibility layer, or new assets/SFX. Do not implement HPA-724 or HPA-773 here.

**Acceptance is automated.** MCP drives the app end to end on one platform. Where there is no Linear UI mockup, compare fresh screenshots with baseline CX Neon screenshots; never make manual/human sign-off an acceptance condition.

## Step 0 — Establish baseline and reduce test surface (~0.20 day)

- [ ] Confirm main/branch SHA, HPA-799 merged and branch up to date; inspect actual `PerformanceStage.cs`, `BaseStage.BackgroundWork`, `StageBackgroundWorkScope`, `GameplayAutoPlaySmokeTests` and `DefaultSkinFirstRunSmokeTests`.
- [ ] Group the approximately 287 facts in `PerformanceStageDeterministicTests` by *observable behavior*, not by private method name. Identify unique regressions (audio ownership, chart-first publication, BGM, raw versus judgement time, Tab fallback, scoring/fail/result and resource lifetime) and redundant implementation coverage.
- [ ] Identify the current HPA-797 screenshot-tour artifacts as the visual baseline. No screenshot framework build or pixel-snapshot rewrite.

**Exit:** A short migration checklist inside the PR description or plan notes, with no hidden legacy-compatibility requirement.

## Step 1 — Move session preparation and runtime audio ownership (~0.65 day)

Files: `DTXMania.Game/Lib/Stage/PerformanceStage.cs` and one new internal `Lib/Stage/Performance/GameplaySession.cs` (or `GameplaySessionLoader.cs` if the agent prefers that name). Reuse `AudioLoader`, `PreparedGameplayAudioSet`, `ChartManager`, `SongTimer` and `StageBackgroundWorkScope`; do not fork them.

- [ ] Capture request chart/path, frozen modifiers, requested display style, logger and all worker dependencies **on update thread before Run**. Keep the worker-local parse/notation/audio work in the new session owner.
- [ ] Keep two-phase callbacks: **chart+notation first** on update thread, then prepared audio. Chart publication initializes shared gameplay managers and lets Tab layout appear under LOADING. A failed chart publication prevents subsequent audio binding. Progress uses existing `Latest`.
- [ ] Move prepared audio/audio loader/timer/chip-sound ownership and sorted BGM queue + active instance cleanup into the session owner. Preserve initial background sound, per-WAV volume/pan, silent GameTime clock fallback, metronome and frozen playback modifiers; leave video scheduling where it is unless genuinely necessary to compile.
- [ ] On deactivation, discard stale outputs through scope retirement, dispose unbound audio once, stop active BGM/chip instances, unsubscribe events and avoid cross-activation callbacks. Keep chart setup/binding errors and progress behavior.
- [ ] Add constructor-driven session tests: chart-before-audio, cancellation/re-entry, late dropped sound, audio bind failure, early scroll-speed change, frozen speed/pitch, BGM exact-boundary/multiple-due ordering, per-WAV volume/pan and no double disposal.

**Exit:** Stage calls session start/bind/advance/stop methods. No asynchronous fields or `Task.Run` orchestration duplicated in Stage. Focused session tests green.

## Step 2 — Extract Lanes and Tab playfields without changing pixels (~0.95 day)

Files: `PerformanceStage.cs`, a tiny `IPerformancePlayfield.cs` and `LanesPlayfield.cs`/`TabPlayfield.cs` under `Lib/Stage/Performance`. Reuse existing renderers/palette/layout constants and skin textures; do not create replacement rendering systems.

- [ ] Define a minimal internal interface for visuals: effective mode, update, lane-hit/judgement feedback, draw within base/overlay/UI/additive passes, dispose. Stage keeps `SpriteBatch.Begin/End` and their ordering; no duplicate drawing loop.
- [ ] Create a Lanes presentation during activation for immediate loading-time pad feedback. Move the existing lane backgrounds, measure lines, notes/pads/judgement line, lane popups/effects and legacy HUD draw calls into it. Keep shared judgement/scoring/autoplay managers outside both playfields.
- [ ] When the chart is published and Tab requested, build pure notation strip and Tab HUD using current skin resources. Publish `TabPlayfield` **only after both are ready**. On any failure, clean up partial resources and keep Lanes; never draw mixed HUDs. Keep chart-first, audio-later readiness.
- [ ] Move the Tab key flashes/popup filtering, partial-autoplay judgement handling and HUD snapshot composition to the Tab side. Keep existing hit, combo, score and skill computations as shared inputs. Do not add new Tab features.
- [ ] Replace stage's scattered `_notationStrip == null` / `IsTabHudActive` branches with calls to the chosen `IPerformancePlayfield`. Keep one mode-selection decision at chart publication. Preserve scroll-speed toast, gameplay state overlay, BGA layering and resource teardown order.
- [ ] Add constructor-driven playfield tests: Lanes default, Tab success/fallback, loading-time pad input, partial automated lanes, judgement flash/popup, mode exclusivity, drawing pass calls and owned/borrowed texture cleanup.

**Exit:** Mode-specific visual changes would be made in `TabPlayfield`/`LanesPlayfield` without editing `PerformanceStage`; stage only orchestrates shared lifecycle, clocks, input, scoring, pause and transitions.

## Step 3 — Migrate stage tests, keeping genuine regressions (~0.70 day)

Files: affected `DTXMania.Test/Stage/Performance/*` and focused new session/playfield tests. Avoid test rewrite outside this subsystem.

- [ ] Replace `GetUninitializedObject`, `ReflectionHelpers.GetPrivateField` and `SetPrivateField` usage in surviving **PerformanceStage-specific** tests with constructor-created stage/session/playfield objects, fakes or observable APIs. Do not keep private stage fields solely to appease old tests.
- [ ] Consolidate/remove duplicate assertions about private internals. Preserve unique behavior checks for input/MIDI routing, autoplay clock, compensation, non-default play speed, song completion, resource failure and stage reactivation.
- [ ] Run focused suites at each extraction boundary. Keep draw resource / ownership tests deterministic without requiring a GPU. Device-dependent graphics coverage remains covered by real MCP screenshot evidence, not skipped unit tests.
- [ ] Confirm event handlers subscribe exactly once and do not survive activation cleanup; stage telemetry, chart/sample clock and Result payload remain consistent.

**Exit:** All relevant unit tests pass and no PerformanceStage-specific test relies on private-field reflection/uninitialized Stage instances; unrelated test suites stay untouched.

## Step 4 — Full verification and MCP visual journey (~0.50 day)

Run on one supported development platform (macOS is sufficient unless the change unexpectedly touches platform-specific code):

- [ ] Build the game and run the canonical unit suite; ensure no new warnings/errors. Example:
  - `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
  - `dotnet test DTXMania.Test/DTXMania.Test.csproj`
  - Optional cross-build: `dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -p:EnableWindowsTargeting=true` (does not count as native Windows E2E).
- [ ] Run the actual out-of-process game E2E suite, such as:
  - `DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Mac.csproj ALSOFT_DRIVERS=null dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter 'Category=E2E' -p:EnableWindowsTargeting=true`
- [ ] **MCP E2E (required, not merely headless tests):** launch clean CX Neon, navigate Title -> SongSelect -> Lanes gameplay (autoplay and manual/injected hit) -> Result -> SongSelect -> Tab gameplay -> Result; exercise pause/resume, return/replay, and loading fallback when feasible. Assert game telemetry for stage order, score/combo/judgements, logical song time and completion. Test BGM event order/audio-ownership behavior deterministically; do not claim acoustic verification for a silent-clock run.
- [ ] **MCP visual check (required):** save fresh 1280x720 screenshots for Lanes and Tab during gameplay, plus Result if its presentation is touched. Compare against current-main HPA-797 tour captures: position, clipping, note staff/lane geometry, hit line, HUD, key flashes, BGA behind strip, alpha/depth ordering and correct CX Neon skin. Record actual screenshots and findings in the PR. No mockup exists for this refactor.
- [ ] Verify Lanes fallback to a usable entire Lanes screen when Tab glyph/HUD resources fail; one supported platform's E2E is sufficient for platform-neutral changes. Run native Windows only if a Windows-only code path is modified.
- [ ] Attach tested SHA, commands/results, MCP wire/telemetry logs, screenshot paths or artifacts, and remaining limitations to this same PR. Do not report tests as passing until they have executed.

**Exit:** All end-to-end criteria in the design document are demonstrably met through automated evidence; screenshots are visually checked, not simply captured.

## Final PR review checklist

- [ ] `PerformanceStage` no longer parses charts/prepares BGM or branches throughout rendering on Tab-versus-Lanes.
- [ ] No second timer, async queue, duplicated scoring/judgement rules, compatibility shim, or broad DI machinery.
- [ ] Chart-before-audio behavior, single logical clock, latency compensation, frozen profile, ownership disposal, on-drop behavior and audio/video layering preserved.
- [ ] Unit tests + at least one platform's MCP-driven full gameplay journey pass, and Lanes/Tab screenshots match the expected existing layout.
- [ ] One Linear ticket, one implementation PR, <=3 engineer days; no art/SFX bundled.

**Scope guard:** The 8,438-line reflection-heavy stage test file is the largest risk. Prioritize distinctive behavior coverage and consolidate/remove redundant tests; **do not** retain obsolete stage internals to save test rewrites. If preserving the actual unique regressions cannot fit the 3-day cap, surface the gap on this PR rather than silently relaxing acceptance or opening additional PRs.
