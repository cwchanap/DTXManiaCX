# HPA-411 — Tab-mode configuration and gameplay integration (implementation plan)

**Issue:** https://linear.app/cwchanap/issue/HPA-411/tab-mode-config-option-and-gameplay-integration  
**Design:** [2026-10-22-hpa-411-tab-mode-integration-design.md](../specs/2026-10-22-hpa-411-tab-mode-integration-design.md)  
**Execution:** Continue implementation **in this same draft PR** after design approval; do not merge a docs-only PR and open a second one. Three bounded tasks, total <=3 engineer days. Work against the current `main`; HPA-410 and HPA-443 are already merged.

## Step 0 — Reuse check and baseline (before production edits, ~0.15 day)

Read these actual code paths and nearby tests: `ConfigData`, `ConfigManager`, `IConfigManager`, `ConfigStage` and `DrumsLaneDisplayMode`; `PerformanceStage` activation/freeze/chart init/draw/judgement/cleanup/telemetry; `NotationChartBuilder`, `NotationModel`, `NotationStripLayout`, `NotationStripRenderer`; `E2EFixtureBuilder`, `GameplayAutoPlaySmokeTests` and `GameStateSnapshot`. Reuse the existing event flow, font ownership and test fakes. Confirm current SDK from `CLAUDE.md` (**.NET 10**) rather than the old migration target.

Before coding, check `NotationStripRenderer.Draw` and `DrawEngravedGlyph`: tint requires **source note references in head anchors**, not just `TimeMs`; timestamp collision alone cannot distinguish simultaneous notes. Check `PerformanceStage.OnDraw` uses separate `BackToFront` base/overlay/UI passes. No speculative abstraction/polish refactor.

## Task 1 — Config, frozen style and unit tests (~0.5 day)

**Production**
- Add `Lib/Config/DrumsNoteDisplayStyle.cs` (Lanes=0, Tab=1; two labels).
- Update `ConfigData`, `ConfigManager` read/write + deferred setter, `IConfigManager`, `ConfigStage` dropdown adjacent to Lane Display. Follow case-insensitive parse and invalid-value handling already used for `LaneDisplayMode`. Preserve the unrelated lane visibility controls.
- Extend `PerformanceVisualGates` to snapshot requested style on activation, zero/default Lanes. Do not read live config to select a different style mid-song. Current scroll speed remains live.

**Tests**
- Extend `DTXMania.Test/Config/ConfigDataTests.cs`, `ConfigManagerTests.cs`, `ConfigManagerSqlitePersistenceTests.cs`, `ConfigStageLogicTests.cs`, and any in-repo `IConfigManager` mock/stub broken by the new setter.
- Assert default Lanes; round-trip Tab; case-insensitive tab; garbage persisted value remains Lanes; unchanged setter does not trigger save; deferred save and reload work in SQLite.
- Extend `PerformanceStageDeterministicTests` for frozen style, including the reflection/uninitialized default path.

**Checkpoint:** config and freeze tests green; Lanes still renders identically.

## Task 2 — Stage renderer lifecycle + tint with focused tests (~1.5 days)

**Stage integration**
- Keep an effective active style separate from requested frozen style (default Lanes). After successful `_chartManager = new ChartManager(_parsedChart)`, build `NotationChartBuilder.Build(_parsedChart)` once, construct `NotationStripRenderer(_resourceManager, notationChart, _parsedChart.MeasureLines)`, validate `IsReady`, then activate Tab. This work must be **once per activation**, not in draw/update. Handle loading failures with a reason logged through the existing performance logger and dispose the partial renderer. Fall back to Lanes for this run; do not edit persistent config.
- Update `OnDraw` base pass to choose falling-lane draw block **or** notation strip draw. Keep background, `DrawJudgementTexts`, UI and feedback passes; suppress `DrawNoteOverlays` only for active Tab. Strip draws with the same logical song clock as notes and current scroll speed. Preserve all `SpriteBatch.Begin/End` ordering.
- On every `OnJudgementMade`, record `NoteRef -> JudgementType` once in the stage's run-scoped dictionary without changing the existing score/combo/gauge/skill/AutoPlay/event forwards. Clear state/dispose strip in existing stage deactivation/cleanup; avoid accepting a stale initialization generation.

**Tinting — minimal change to existing notation code**
- Extend only **notehead** `NotationStripGlyphAnchor` instances emitted by `NotationStripLayout.EngraveEntryGlyphs` to include the source `NotationHead.Notes` references. Keep other glyphs and all engraving geometry unchanged.
- Add an optional `IReadOnlyDictionary<int, JudgementType>? results = null` parameter to the existing `NotationStripRenderer.Draw(SpriteBatch, double, int)` call; `null` preserves HPA-410 rendering and existing tests without a mutable renderer property. Resolve color for each head using source `Note.Id` and the existing judgement colors: unresolved = neutral, Miss = red, hits = matching popup color; simultaneous heads must not appear resolved while any component note remains unjudged. Apply the minimal x-based fade between hit marker 220 and mask boundary 195 for **resolved heads only**.
- No per-frame dictionary rebuild from all chart notes, no cache, no alternate color table, no per-note animation scheduler, no touch to Quantizer/ChartManager/NoteRenderer internals beyond direct integration calls.

**Tests**
- Pure resolver/layout tests for NoteRef identity, simultaneous sources, Miss, neutral rests/flags, fade positions, source reference IDs assigned by ChartManager.
- Deterministic stage tests for 1x chart construction, success/failure fallback, frozen requested vs effective mode, renderer disposal on repeated activations, no lane rendering in Tab, and preserved baseline Lanes gates. Use existing test seams; only introduce a tiny seam if GPU-free tests truly need it.
- A manual 1280x720 Tab specimen of real chart + AutoPlay and Lanes visual regression check. Specifically verify that the ~25px (~66ms at 100% / 0.38px/ms) post-hit fade is perceptible rather than an instant disappearance; record observations and defer cosmetic widening to HPA-509 if needed. Do **not** invent screenshots or claim a GPU test ran headlessly.

**Checkpoint:** Tab notes and actual judgements render correctly; fallback stays playable.

## Task 3 — Telemetry, E2E, final checks (~0.7 day)

- Add nullable or stage-specific `GameTelemetrySnapshot.NoteDisplayStyle` string and populate from **effective** mode in `PerformanceStage.PopulateTelemetry`. Expose through existing `DTXMania.Automation/Telemetry/GameStateSnapshot` getter and pin camelCase in `DTXMania.E2E/AutomationContractTests.cs`; no extra MCP tool.
- Extend the existing `E2EFixtureBuilder.Build` with **one optional** note-style parameter defaulting to Lanes, writing `NoteDisplayStyle=Tab` to fresh **bootstrap** `Config.ini` only for the new scenario. Do not write the INI after DB creation.
- Add one `Category=E2E` Tab AutoPlay smoke using the established launch/game-state/Result flow; assert telemetry reports **Tab during Performance**, successful stage completion and existing judgement totals. Keep tests focused; no new workflow or framework.
- Run and record exact commands/results (do not claim runs not performed):
  - `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
  - `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
  - Windows CI: `dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj`, `dotnet test DTXMania.Test/DTXMania.Test.csproj`
  - Windows CI E2E: `dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"`
- Confirm test discovery is nonzero, Lanes unchanged (including HUD and pad visuals), config reload, Tab color/fade, and fallback when Bravura is not available. Attach a visual specimen to this PR if a real screenshot can be captured.
- Mark HPA-411 Done only after **implementation is merged**; HPA-509 becomes unblocked then.

## Review guardrails

One implementation PR, <=3 engineer days. If full E2E cannot run locally, explicitly request the existing Windows CI gate rather than silently replacing it with unit tests. Defer all HPA-509 layout/gauge/progress/label-pad work; the prototype HUD may look imperfect. Never change DTXManiaNX, score semantics, chart timing, Autoplay, or the existing `NotationStripLayout` geometric constants as part of integration. No new sprites/art/SFX work in this ticket.

### Completion checklist

- [ ] Mode selectable/persistent, valid default.
- [ ] Run-frozen style and logged safe fallback.
- [ ] Existing notation strip wired only for Tab, lane path intact.
- [ ] Source-note judgement tint and post-marker fade.
- [ ] Lifecycle disposes resources / clears note-result state.
- [ ] Effective style telemetry + one Tab E2E.
- [ ] Mac/Windows build/tests, CI E2E, real specimen or documented limitation.
- [ ] PR changed from draft to ready only when implementation/CI evidence is complete.
