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
- Inventory production `Debug.WriteLine`, `Console.Write/WriteLine`, `Trace.Write*` **and `Trace.Trace*`** in `DTXMania.Game` by context: disposable chatter, optional missing asset, unexpected decode/I/O error, intentionally Release-visible failure, and pre-logger `Console.Error`. Note `SongSelectionStage.AssignInputManager`'s `Trace.TraceWarning`. Do not count `Debug.Assert`/`Trace.Assert`.
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

1. Acquire `IResourceManager.CreateTextureFromColor(Color.White)` **once per owning stage/component** (as `LaneBackgroundRenderer` already does). Hold `ITexture`, lend `.Texture` to children, and release with `RemoveReference` after disposing child renderers; children borrowing `Texture2D` never dispose/release it. No child-specific pixel cache or new ResourceManager instance. Reacquire after stage deactivation; preserve skin reload semantics.
2. Make the **missing wiring seams explicit** before deleting factories:
   - `PerformanceStage`: acquire cached `ITexture` before rendering components (the existing raw `_fallbackWhiteTexture` is created too late, after `JudgementLineRenderer`). Replace its raw allocation and `CleanupComponents`'s `.Dispose()` with `ITexture` + `RemoveReference()`. Pass borrowed `.Texture` to `JudgementLineRenderer(GraphicsDevice, Texture2D)` and `TabHudDisplay`; neither child owns it. Dispose the children before releasing the stage's cached reference.
   - `DefaultGraphicsGenerator`: change its constructor from `(GraphicsDevice, RenderTarget2D)` to accept a **borrowed `Texture2D`** and stop creating/disposing its own pixel. Forward the existing stage-owned pixel through `SongListDisplay.WhitePixel`, `SongStatusPanel.WhitePixel`, `SongBarRenderer` and `SongBar.InitializeGraphicsGenerator` to every generator instance. `SongBar` has no `IResourceManager`; **do not inject one solely for a white pixel**. Update the four production generator call paths and constructor/unit-test call sites; retain `SongSelectionStage`'s status-panel render-target initialization order. Require generator consumers to dispose before the owning stage releases its pixel.
   - Remaining active stages/renderers: acquire an `ITexture` via the resource manager at an existing owner and forward a borrowed raw texture where a child lacks a manager seam. Do not add a new global service.
   - **Delete redundant virtual white-pixel factories** (`StartupStage.CreateWhitePixelCore`, `TitleStage.CreateTitleWhitePixel/SetTitleWhitePixelData`, `ResultStage.CreateWhitePixel`, `PadRenderer.CreateFallbackTextureCore`). Replace test overrides with `IResourceManager` mocks or fixture-owned managed pixels. Do not keep two seams for the same pixel.
3. Expose `ChamferedBox.Fill` publicly and add `ChamferedBox.Border` supporting configurable thickness and **overlapping corners** with optional depth. Reuse those for **matching** rectangle/border geometry only. For `ChamferedBox.Draw`, preserve its chamfered diagonals unchanged; only delegate the zero-chamfer rectangular case to the new Border helper. Preserve fill-transparent skip behavior.
4. Keep `NotationStripRenderer.DrawOutline` local: its non-overlapping translucent corner geometry is intentionally different. Respect `GaugeDisplay`'s 2px border and `DefaultGraphicsGenerator`'s thickness. Retain `UIPanel`'s textured border; delete only the unused/no-op untextured border branch. Keep all blend, depth, alpha and drawing coordinates identical.
5. Use existing resource/renderer tests to verify reference counts, **owner-before-child and child-before-release ordering**, SongBar/DefaultGraphicsGenerator borrowed-pixel propagation, stage re-entry/skin switch and exact drawing geometry. Mock `IResourceManager` in headless tests, and ensure all direct generator construction sites/tests compile with the new parameter. GPU draw assertions require graphics opt-in; do not assert visuals from headless skips.

**Exit:** No independent game white-pixel allocations beyond the ResourceManager cache/fallback source; focused tests pass; make a green commit in this PR.

## Step 3 — Delete chatter and retain valuable error logging (~0.65 day)

Files: production `DTXMania.Game` callers, `Lib/Resources/ResourceManager.cs` and factory, select stages and `SongManager` bootstrap. **No mass logger injection into leaf renderers.**

1. **Delete** noisy `Debug.WriteLine`/`Trace.Write*`, including per-note, per-frame, cursor, cache and parsing chatter. These calls were already absent in Release; converting all to `LogDebug` is not useful under the current Information minimum.
2. Wire **only the missing high-value logger paths**. Stages use `IStageGame.LoggerFactory`. Extend `ResourceManagerFactory.CreateResourceManager` and the concrete `ResourceManager` constructor to accept optional `ILogger<ResourceManager>`; supply `_loggerFactory.CreateLogger<ResourceManager>()` in `Game1.CreateLoadContentServices`, defaulting to `NullLogger` in independent tests. `SongManager` has **no constructor injection seam**: add an internal `ConfigureLogger(ILogger<SongManager>?)` on the existing `SongManager.Instance` singleton and invoke it from `Game1.CreateLoadContentServices`, with a null-logger default for tests. For `TabHudDisplay.AcquireDrawResources`, pass optional `ILogger<TabHudDisplay>` from `PerformanceStage.ConstructTabHud` (or return the original exception for `PerformanceStage.LogPerformanceError` to record once). Prefer the smallest of these two strategies and preserve the original exception. Do not add global locators, full DI or new providers.
3. In `ResourceManager.OnResourceLoadFailed`, **preserve the event** (required by `TryLoadTexture/TryLoadSound`). Distinguish ordinary missing optional assets (`FileNotFoundException` from `LoadTexture/LoadSound`) from unexpected non-missing decode/I/O failures: log missing/probed paths at `LogDebug` (filtered in ordinary Release runs), and log genuine decode/I/O exceptions at **`LogWarning(ex, "Resource load failed: {Path}", path)`** once. Do **not** globally warn for optional file misses: `PadRenderer` probes four legacy candidates, Config/DrumConfig probe optional art, and `PerformanceStage.TryLoadTexture` may probe unavailable assets. Stages with a genuinely mandatory asset may warn **once** at their boundary. Delete duplicate leaf failures only if the owner now reports the same problem. Preserve deliberate `PerformanceStage.LogPerformanceError` and direct `TabHudDisplay` font errors at Error in Release, with the original exception and existing Lanes fallback; do not assume direct font creation went through ResourceManager.
4. Convert the real `SongSelectionStage.AssignInputManager` `Trace.TraceWarning` (missing shared InputManager) to its stage `ILogger.LogWarning`, using `IStageGame.LoggerFactory` without an unnecessary new service. Preserve `Program.cs`/`CrashReportRuntime.cs` bootstrap `Console.Error` and other truly pre-logger emergency output. Leave `Debug.Assert`/`Trace.Assert` intact.
5. **No new `CrashLogEvents` registrations, privacy-field policy, buffer size, or minimum-level changes.** Default Information-level filtering means `LogDebug` is not a substitute for a needed error. A new ordinary `LogWarning` enters the buffer only as an **unclassified sanitized record**. Check Release console volume and avoid repeated Warning/Error output for the same failure.

**Exit:** No game production `Debug.WriteLine`, `Console.Write/Console.WriteLine`, `Trace.Write*` or `Trace.Trace*` remains (assertions and pre-logger `Console.Error` are exempt). Verify expected optional-asset misses remain below Warning, real decode/I/O errors warn once, and intentional performance/Tab font errors remain visible with exceptions. Existing classified crash events are unchanged. Focused tests pass; green commit.

## Step 4 — Add three cheap architecture tests (~0.20 day)

Add a single source-scanning test file using the pattern in `DTXMania.Test/Config/DTXPathCompatibilityArchitectureTests.cs`, with `[Trait("Category", "Architecture")]` and three `[Fact]` tests:

1. Ban game production `Debug.WriteLine`, `Console.Write`/`Console.WriteLine` and diagnostic **`Trace.Write*` + `Trace.Trace*`** (including `Trace.TraceWarning/TraceError/TraceInformation`). A simple source check may match `\bTrace\.(Write|Trace)` plus corresponding Debug/Console calls, with formatting/whitespace handling and comments excluded. Do **not** ban `Debug.Assert`/`Trace.Assert` or early `Console.Error`. Scope to game `*.cs` excluding bin/obj.
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
- [ ] Cached white pixel is borrowed/released correctly with explicit PerformanceStage -> JudgementLineRenderer/TabHudDisplay and SongSelection -> DefaultGraphicsGenerator/SongBar propagation. `ChamferedBox` is reused, specialized translucent geometry is preserved, and redundant factory hooks are removed.
- [ ] Chatter is deleted; optional missing-asset probes remain Debug-only; genuine resource decode/I/O failures and deliberate Release-visible performance/Tab font failures are logged once through `ILogger`. The `SongSelectionStage` Trace warning has moved to `ILogger`, crash-report allowlist is unchanged and no new Warning spam appears.
- [ ] Three architecture guard facts pass in the normal suite, including `Trace.Trace*` detection and `Console.Write`/`Console.WriteLine`.
- [ ] Android/Linux acceptance remains CI-only if relevant; no human acceptance gate.

## Scope control

Do not introduce a logging framework, new UI primitive file, second pixel cache, automated visual-diff infrastructure or compatibility adapters. If a primitive's exact geometry is different, keep it local. If a logger requires broad constructor changes for a low-value message, delete that message. If a real requirement exceeds the 3-day cap, keep HPA-796 focused and create a separate follow-up rather than stretching this PR.
