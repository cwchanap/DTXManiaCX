# HPA-796 — Implementation plan (single PR)

**Design:** [HPA-796 design](./2026-11-14-hpa-796-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-796/cleanup-log-through-ilogger-share-drawing-primitives-type-the-stage  
**Branch:** plan/hpa-796-logging-primitives-stage-payloads  
**Base:** main at 1c2ac979e7263e8c4eb52ebc7b1f5a35e4795a3a  
**State:** Docs-only draft. No implementation, builds, screenshots, or tests were performed during planning.

**Execution contract for the implementing agent:** Implement this ticket **on this PR branch**, not in multiple PRs. Scope <=3 engineer days. Use the repository's current interfaces and tests as the source of truth. Prefer small, reviewable commits inside the same PR. Do not introduce shims for the old transition dictionary API, new art/SFX or extra infrastructure.

## Step 0 — Baseline and inventory (~0.25 day)

- Rebase/refresh this branch against latest main **before code changes**, then note the base SHA in the PR.
- Inventory Debug.WriteLine, Console.WriteLine and diagnostic Trace.Write calls **inside DTXMania.Game** (exclude assertions, other applications, tests, generated files). Group by class and event frequency; identify constructors without an ILogger.
- Inventory all direct 1x1 white Texture2D creators, local rectangle/border helpers, and production IStageManager.ChangeStage(..., dictionary)/IStage.Activate(dictionary) uses. Note tests that mock these contracts.
- Run the canonical current unit tests to capture a baseline. Retrieve existing HPA-797 E2E tour screenshots (or capture a fresh baseline), especially Title/Config/SongSelect/Tab/Lanes/Result. Record any baseline failures rather than hiding them.

**Exit:** Brief inventory in PR notes, baseline status and no unexpected out-of-scope API consumers.

## Step 1 — Replace hand-off dictionaries with three records (~0.65 day)

Relevant files: Lib/Stage/IStageManager.cs, StageManager.cs, BaseStage.cs, SongSelectionStage.cs, SongTransitionStage.cs, PerformanceStage.cs, ResultStage.cs and associated stage tests.

1. Add StageTransitionPayload and SongSelectionPayload, PreparedSongPayload, PerformanceResultPayload in the stage domain, using current SongListNode, ParsedChart and PerformanceSummary types.
2. Change IStageManager.ChangeStage's payload overload and IStage.Activate payload method; thread the typed value through StageManager._pendingSharedData and BaseStage's activation state. Keep existing no-data overloads and exact lifecycle order.
3. Update three sending sites and receivers. Preserve SongSelection -> Transition difficulty/song ID; Transition -> Performance parsed-chart reuse; Performance -> Result result summary, autoplay/fail data and selection. Keep ResultStage's existing missing-summary fallback if activated without a result payload.
4. Update stage transition, selection, performance, result, mock and helper tests to assert fields **by type**. Delete dictionary-specific helpers and tests for obsolete arbitrary keys. Do not maintain a dictionary overload.

**Exit:** No Dictionary<string, object> is used as a stage transition contract; focused stage tests and builds are green. Keep unrelated dictionary uses untouched.

## Step 2 — Consolidate white pixels and rectangle/border drawing (~0.75 day)

Relevant areas: Lib/Resources/ResourceManager.cs (existing API; avoid changing its caching policy), Lib/Stage/*, Lib/Stage/Performance/*, Lib/UI/*, applicable tests.

1. Use **existing** ResourceManager.CreateTextureFromColor(Color.White), as already modeled by LaneBackgroundRenderer. Store ITexture references, draw using .Texture and release with RemoveReference on stage/render disposal. Remove per-stage raw white-pixel Dispose calls.
2. Migrate all active UI/renderers that create their own 1x1 white texture (including Startup, Title, Config, SongSelection, SongTransition, Result, Performance and standalone performance/UI renderers). Preserve existing headless test hooks while changing resource ownership.
3. Add one modest UiPrimitives utility for filled rectangles and 1px borders, accepting a caller-owned SpriteBatch and borrowed white pixel. Deduplicate matching helpers, preserving optional depth, transparent fills and all geometry. Keep more specialized shape code in place.
4. Extend unit tests using mock ITexture/IResourceManager to verify only one cached underlying texture is used, reference release does not dispose it, re-entry and skin switches remain safe, and border draw coordinates do not change. Require graphics opt-in for actual GPU assertions, not fake passing headless tests.

**Exit:** No duplicate direct 1x1 white allocations outside ResourceManager's cache/fallback internals; existing UI layout coordinates are unchanged; focused tests green.

## Step 3 — Route game diagnostics through ILogger (~0.8 day)

Relevant areas: DTXMania.Game/Lib/Song, Lib/Resources, Lib/Stage, Lib/Graphics and other **game production** call sites, plus tests.

1. Reuse IStageGame.LoggerFactory for stage loggers. At leaf creation sites inject ILogger<T>/ILoggerFactory only as needed. Reuse current logging conventions and NullLogger for safe unit-test construction; do not create a new facade or static service locator.
2. Replace Debug.WriteLine, Console.WriteLine and diagnostic Trace.Write/Trace.WriteLine in game production code with structured logging. Do not change Debug.Assert/Trace.Assert, the MCP app, tools or historical docs. Convert exception-message-only logs to preserve the exception parameter when available.
3. Choose sensible levels: Error for fatal operation failures, Warning for recoverable problems, Information for infrequent lifecycle events, Debug/Trace for verbose loads and hits. Guard event frequency, especially inside Draw, Update, chart parsing and audio loops. Avoid duplicate reports at inner/outer catches.
4. Test a representative resource failure, stage warning and crash-report log capture through the existing CrashLogBufferProvider. Verify Release build sees the event through ILogger without a parallel Debug listener.

**Exit:** Production grep for target write calls is empty (excluding explicitly documented non-logging assertions), no new provider, crash logging works and focused tests green.

## Step 4 — Cross-platform regression and visual evidence (~0.5 day)

- Run on **macOS**: dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj; dotnet test DTXMania.Test/DTXMania.Test.csproj; smoke-run the game and exercise stage transitions with the Game API/graphical runner when possible.
- Run on **Windows**: dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj; dotnet test DTXMania.Test/DTXMania.Test.csproj; run the existing DTXMania.E2E Category=E2E gameplay suite / HPA-797 screenshot tour.
- Verify the complete player journey: Title -> SongSelect -> SongTransition -> Performance (Lanes and Tab) -> Result -> SongSelect, as well as Config and DrumConfig, failure result and back/cancel. Check selected chart/difficulty, score and persisted result remain correct.
- Capture and compare before/after CX Neon screenshots across the seven existing E2E stages. There is no new Linear mockup for this ticket. Agent verifies borders, tint/alpha, layering, clipping, panel positions and absence of missing textures. Include before/after captures or comparison evidence in this PR; never treat headless GPU skips as visual proof.
- Verify stage unload/re-entry and skin switching leave the cached color texture alive; check logger output for crash-buffer visibility and accidental per-frame flood.
- Confirm no compatibility layer, leftover stage-key strings, or duplicate primitive allocations. Update PR body with actual test outcomes, screenshot evidence, deviations and any unverified environment-specific checks. **Do not claim success for checks not run.**

## Final acceptance checklist

- [ ] Windows build, canonical tests and existing gameplay E2E complete with correct stage and score hand-offs.
- [ ] macOS build, canonical tests and graphical stage smoke check complete (or unavailable graphical evidence clearly reported, not claimed passed).
- [ ] CX Neon before/after screenshot tour verifies unchanged Title, Config/DrumConfig, SongSelect, Lanes, Tab and Result appearance against current/reference captures.
- [ ] One resource-manager-owned white texture, correct RemoveReference lifecycle, no direct duplicated 1x1 white sources in active renderers; rectangle/border behavior unchanged.
- [ ] ILogger handles game production diagnostics and crash-report logs; no residual console/debug writes or unexpected hot-loop log volume.
- [ ] Typed payload records handle the three real data hand-offs; no dictionary-based transition keys remain; stage lifecycle, chart reuse, scoring and audio behavior preserved.
- [ ] **One PR**, no new art/SFX, and no Android/Linux acceptance beyond CI.

## Scope control

If the logging inventory grows unexpectedly, first remove redundant or dead diagnostic messages rather than inventing a logging abstraction. If one renderer has specialized pixel geometry, leave that geometry local and only consolidate its white texture. Do not split HPA-796 across PRs or opportunistically start HPA-799/HPA-801. Bring a genuine >3-day scope overrun back to Linear as a separately scoped future ticket, not an unbounded extension of this PR.
