# HPA-796 — Reviewed implementation plan (single PR)

**Design:** [HPA-796 revised design](./2026-11-14-hpa-796-design.md)  
**Review addressed:** https://github.com/cwchanap/DTXManiaCX/pull/240#pullrequestreview-5908533829  
**Linear:** https://linear.app/cwchanap/issue/HPA-796/cleanup-log-through-ilogger-share-drawing-primitives-type-the-stage  
**Branch:** `plan/hpa-796-logging-primitives-stage-payloads`  
**Reviewed baseline:** `main` at `1c2ac979e7263e8c4eb52ebc7b1f5a35e4795a3a`  
**Status:** Docs-only draft. No implementation or tests have yet been run for this planning change.

**Contract for implementing agent:** Implement **on this same PR branch**. One PR, <=3 engineer days. The repository code, not stale line numbers below, is authoritative. Keep a green commit per decision; refresh base before code changes. Avoid needless abstractions, retained compatibility code, and unrelated cleanup.

## Step 0 — Baseline and source inventory (~0.25 day)

- Confirm the current head of `main` and record the base SHA. Run canonical `dotnet test DTXMania.Test/DTXMania.Test.csproj` and log any existing failures.
- Inventory the production `Debug.WriteLine`/`Console.WriteLine`/diagnostic `Trace.Write` calls in `DTXMania.Game` by context: disposable chatter, meaningful failure, intentionally Release-visible failure, pre-logger Console.Error. Do not count `Debug.Assert`/`Trace.Assert`.
- Inventory direct 1x1 white `Texture2D` allocations; local rectangle/border helpers with their thickness, blend, corner overlap and depth; and all stage dictionary contracts plus tests, including Game API command mocks.
- Capture **before** CX Neon PNGs from the existing HPA-797 test tours with the **bundled skin**; preserve deterministic capture conditions where practical. Do this **before** any implementation edit. Keep baseline copies as temporary evidence (not repository art).

**Exit:** Baseline tests/captures and scoped inventory recorded in the PR description. Do not expand scope to other application projects.

## Step 1 — Two typed stage payloads (~0.50 day)

Files: `Lib/Stage/IStageManager.cs`, `StageManager.cs`, `BaseStage.cs`, `SongSelectionStage.cs`, `SongTransitionStage.cs`, `PerformanceStage.cs`, `ResultStage.cs`, their stage tests, and `DTXMania.Test/GameApi/GameApiPreparedChartCommandTests.cs`.

1. Add abstract `StageTransitionPayload` and sealed records `SongPlayRequest(SongListNode Song, int Difficulty, ParsedChart? Chart = null)` and `PerformanceResult(PerformanceSummary Summary, SongListNode? Song, int Difficulty)`. No separate PreparedSongPayload. Request sender creates only a non-null song.
2. Replace `IStageManager.ChangeStage(..., Dictionary<string, object>)` and `IStage.Activate(Dictionary<...>)` with typed `StageTransitionPayload?`. Carry through `StageManager` and `BaseStage` unchanged in lifecycle. Retain no-payload overloads, passing null.
3. SongSelection sends the request; SongTransition forwards it using `with { Chart = _parsedChart }`; Performance receives request (including optional chart) and Result receives performance result. Keep direct-Result activation's safe missing-summary fallback. Remove unused `_songId` fields and payload keys; obtain the selected song's ID from `Song.DatabaseSongId` if needed.
4. Update transition tests and mocks, including the **five** dictionary `It.IsAny` verifications in `GameApiPreparedChartCommandTests` and `StageManagerTransitionTests`. Delete arbitrary `"Mode"` and `"Screen"` dictionary assertions (they do not model production) while retaining assertions for activation count, ordering, no payload, debounce, chart/difficulty reuse and result content. Remove `BaseStage` Get/Set/HasSharedData and obsolete tests. No dictionary shim.

**Exit:** Game and focused stage/Game API tests compile/pass. Make a green commit in this PR.

## Step 2 — Shared cached pixel and exact border reuse (~0.75 day)

Files: current active stages/renderers that allocate 1x1 white `Texture2D`, `Lib/UI/ChamferedBox.cs`, relevant UI/render and resource tests. **Do not add `UiPrimitives.cs`.**

1. Borrow `IResourceManager.CreateTextureFromColor(Color.White)` through the existing resource manager (as `LaneBackgroundRenderer` already does), hold `ITexture`, draw via `.Texture`, release via `RemoveReference`. Never `Dispose` the cached texture from a consumer. Reacquire after deactivation as needed; preserve staging and skin reload behavior.
2. Remove duplicate white pixel creators across Startup, Title, Config, SongSelection, SongTransition, Result, Performance and standalone active UI/renderers. **Delete redundant virtual test-only factories** (`StartupStage.CreateWhitePixelCore`, `TitleStage.CreateTitleWhitePixel/SetTitleWhitePixelData`, `ResultStage.CreateWhitePixel`, `PadRenderer.CreateFallbackTextureCore`); replace overrides in affected tests with `IResourceManager` mocks. Do not keep two seams for the same pixel.
3. Expose `ChamferedBox.Fill` publicly and add `ChamferedBox.Border` supporting configurable thickness and **overlapping corners** with optional depth. Reuse those for **matching** rectangle/border geometry only. For `ChamferedBox.Draw`, preserve its chamfered diagonals unchanged; only delegate the zero-chamfer rectangular case to the new Border helper. Preserve fill-transparent skip behavior.
4. Keep `NotationStripRenderer.DrawOutline` local: its non-overlapping translucent corner geometry is intentionally different. Respect `GaugeDisplay`'s 2px border and `DefaultGraphicsGenerator`'s thickness. Retain `UIPanel`'s textured border; delete only the unused/no-op untextured border branch. Keep all blend, depth, alpha and drawing coordinates identical.
5. Use existing resource and renderer tests to verify reference counts, safe stage re-entry, no accidental shared Dispose, and exact draw geometry. GPU-dependent draw tests require graphics opt-in; do not assert visuals from headless skips.

**Exit:** No independent game white-pixel allocations beyond the ResourceManager cache/fallback source; focused tests pass; make a green commit in this PR.

## Step 3 — Delete chatter and retain valuable error logging (~0.65 day)

Files: production `DTXMania.Game` callers, `Lib/Resources/ResourceManager.cs` and factory, select stages and `SongManager` bootstrap. **No mass logger injection into leaf renderers.**

1. **Delete** noisy `Debug.WriteLine`/`Trace.Write*`, including per-note, per-frame, cursor, cache and parsing chatter. These calls were already absent in Release; converting all to `LogDebug` is not useful under the current Information minimum.
2. For meaningful errors, use existing `ILogger` sources: `IStageGame.LoggerFactory` at stages, current game creation site and a small optional logger hand-off via `ResourceManagerFactory`, and SongManager's existing startup path only if simple. Reuse existing `NullLogger` conventions in tests. Do not install new providers, static/global locators or full DI.
3. In `ResourceManager.OnResourceLoadFailed`, replace its debug message with one structured `LogWarning` recording path and exception. Remove leaf resource-loading failure logs for the **same** failure so it is not reported twice. Preserve reporting of distinct failures not routed through ResourceManager; in particular `PerformanceStage.LogPerformanceError` and `TabHudDisplay`'s directly-acquired font failures must remain visible in Release via existing console-backed Error logging. Route through stage logger or one narrowly justified logger, not a new abstraction.
4. Preserve `Program.cs`/`CrashReportRuntime.cs` bootstrap `Console.Error` and any truly pre-logger emergency output. Leave `Debug.Assert` and `Trace.Assert` untouched.
5. **No new `CrashLogEvents` registrations, privacy-field policy, buffer size, or minimum-level changes.** Default Information-level filtering means `LogDebug` is not a substitute for a needed error. A new ordinary `LogWarning` enters the buffer only as an **unclassified sanitized record**. Check Release console volume and avoid repeated Warning/Error output for the same failure.

**Exit:** No game production `Debug.WriteLine`/`Console.WriteLine`/diagnostic `Trace.Write*`, except allowed pre-logger `Console.Error`; intentional errors remain visible with exceptions; existing classified crash events are unchanged. Focused tests pass; green commit.

## Step 4 — Add three cheap architecture tests (~0.20 day)

Add a single source-scanning test file using the pattern in `DTXMania.Test/Config/DTXPathCompatibilityArchitectureTests.cs`, with `[Trait("Category", "Architecture")]` and three `[Fact]` tests:

1. Ban game production `Debug.WriteLine`, `Console.WriteLine` and diagnostic `Trace.Write*`, **not** assertions or early `Console.Error`. Scope to game `*.cs` excluding bin/obj and ignored comments.
2. Ban `new Texture2D(..., 1, 1)` outside `Lib/Resources/ResourceManager.cs`; support simple multi-line whitespace in the source check so it cannot be evaded by formatting.
3. Ban `Dictionary<string, object>` as the stage hand-off in `IStageManager.cs`, `StageManager.cs` and `BaseStage.cs`.

No analyzer packages, new CI job or test framework. Keep the test source-based and small; allow only intentional, documented exceptions rather than weakening the whole guard.

**Exit:** Guard facts pass normally on Windows and macOS; green commit.

## Step 5 — Platform verification and pixel diff (~0.50 day)

- **Windows:** `dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj`; `dotnet test DTXMania.Test/DTXMania.Test.csproj`; run the existing `DTXMania.E2E` `Category=E2E` gameplay tour / HPA-797 screenshot capture.
- **macOS:** `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`; `dotnet test DTXMania.Test/DTXMania.Test.csproj`; graphical stage smoke if available, recording it as unverified otherwise.
- Exercise Title -> SongSelect -> SongTransition -> Performance (Lanes and Tab) -> Result -> SongSelect, plus Config, DrumConfig, cancellation/back, failed run and restart. Verify the exact song, difficulty, prepared chart and result score are retained.
- Capture **after** screenshots of the seven stages, using the same bundled CX Neon art, virtual resolution, fixture and deterministic state as baseline. Run a **one-off Pillow pixel comparison** (do not commit tooling), compare static/border/panel regions at exact coordinates, and report every change in a stable region. Animated notes, timers, score values and asynchronous load timing are legitimate differences; isolate/mask them rather than imposing whole-frame equality. Examine 1px geometry, corner opacity, overlap, layering and missing pixels. Attach before/after PNGs and comparison observations to the PR. No new Linear UI mockup exists.
- Verify retained Release-visible errors via console logging, classified crash breadcrumbs/events and bounded unclassified log volume. Re-enter stages and switch skins to catch accidental shared texture disposal.
- Record **actual** tests/captures/commands and any unverified steps in the PR; don't claim a pass that wasn't run.

**Exit:** Gameplay behavior and CX Neon geometry are unchanged; full platform checks that can run are green and evidence is linked to the PR.

## Final acceptance checklist

- [ ] One implementation PR, completed on this branch (not separate PRs), <=3 engineer days; no new art or SFX.
- [ ] Windows build, canonical unit tests and existing gameplay E2E pass; macOS build and canonical unit tests pass (and graphical smoke runs when available).
- [ ] Existing 7-stage CX Neon tour has before/after screenshot evidence, with stable UI regions pixel-compared and every unintended 1px/alpha/border change fixed.
- [ ] The two typed payloads preserve song/difficulty, optional parsed chart, result summary and stage lifecycle. No dictionary compatibility layer, unused song-ID mirrors or arbitrary-key tests remain; Game API tests compile.
- [ ] Cached white pixel is borrowed/released correctly; `ChamferedBox` is reused, specialized translucent geometry is preserved; duplicate per-site pixel factories are removed.
- [ ] Chatter is deleted; deliberate Release-visible errors remain through `ILogger`; crash-report allowlist is unchanged; no accidental new warning spam.
- [ ] Three architecture guard facts pass in the normal suite.
- [ ] Android/Linux acceptance remains CI-only if relevant; no human acceptance gate.

## Scope control

Do not introduce a logging framework, new UI primitive file, second pixel cache, automated visual-diff infrastructure or compatibility adapters. If a primitive's exact geometry is different, keep it local. If a logger requires broad constructor changes for a low-value message, delete that message. If a real requirement exceeds the 3-day cap, keep HPA-796 focused and create a separate follow-up rather than stretching this PR.
