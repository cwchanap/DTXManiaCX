# HPA-411 — Tab-mode configuration and gameplay integration (implementation plan)

**Issue:** https://linear.app/cwchanap/issue/HPA-411/tab-mode-config-option-and-gameplay-integration  
**Design:** [2026-10-22-hpa-411-tab-mode-integration-design.md](../specs/2026-10-22-hpa-411-tab-mode-integration-design.md)  
**Execution:** Implement **in this same draft PR** after review, not a separate PR. Target ~2 engineer days, hard cap <=3. SDK on `main`: **.NET 10**. HPA-410 + HPA-443 are merged.

## Step 0 — Confirm reused seams (~0.15 day)

Read the existing `DrumsLaneDisplayMode`, `ConfigData`, `ConfigManager`, `IConfigManager`, `ConfigStage`; `PerformanceStage` freeze/chart init/draw/UI/cleanup/telemetry; `NotationChartBuilder` + `NotationStripRenderer` + `PerformanceUILayout.NotationStrip`; `E2EFixtureBuilder` and current telemetry contract. Do not add a new renderer, per-note result map, color resolver or graphics-test abstraction. Preserve `OnJudgementMade` scoring/event fan-out.

## Task 1 — Config and frozen requested style (~0.5 day)

**Production**
- Add `DrumsNoteDisplayStyle { Lanes = 0, Tab = 1 }` plus two labels. Add `ConfigData.NoteDisplayStyle`, `ConfigManager` case-insensitive **name-only** read/canonical write + existing deferred setter, `IConfigManager` and `ConfigStage` selector next to Lane Display.
- Freeze requested style in `PerformanceVisualGates`. Default/zero-initialized struct means Lanes. No mid-song style change; live scroll speed still works.

**Tests**
- Extend `ConfigDataTests`, `ConfigManagerTests`, `ConfigManagerSqlitePersistenceTests`, `ConfigStageLogicTests` and broken `IConfigManager` mocks for default, labels, Tab round-trip, case-insensitive name, invalid/numeric ignored, deferred save, no-op setter.
- Extend deterministic stage freeze test for Tab across live edits and zero/default Lanes.

**Checkpoint:** config/freeze tests pass; no existing Lanes behavior changes.

## Task 2 — Renderer integration, visibility gate, early screenshot (~0.75 day)

**Init/lifecycle**
- After `new ChartManager(_parsedChart)`, **only if frozen Tab**, build pure `NotationChartBuilder.Build(_parsedChart)` once. Catch/log its failure locally and allow gameplay in Lanes. Chart parsing's fallback `await ParseAsync` can resume on a thread-pool thread: **never load Bravura/fonts there**.
- Stage the built chart for a **one-shot renderer construction in `OnUpdate` (game thread)**. Guard current activation/cancellation before staging or using the pending chart. Track only the minimal pending/attempt state needed to avoid per-frame retries; no separate active-style flag. Construct existing `NotationStripRenderer` and keep `_notationStrip` only if `IsReady`; otherwise log fallback and dispose. No retries this song.
- In cleanup clear pending state and `_notationStrip` reference **before** disposing the renderer; effective mode is `_notationStrip != null` only.

**Draw/UI**
- In the existing base draw pass replace lane backgrounds, measure lines, notes and judgement line with the strip only when renderer is active; skip note overlays only in Tab. Keep song clock, live scroll percent, judgement popups and score/AutoPlay semantics; do not restructure sprite batches.
- Gate exactly four overlay calls in `DrawUIElements` while Tab is effective: `DrawSkillPanel`, `_skillPanelDisplay.Draw`, `DrawProgressBar`, `_skillMeterDisplay.Draw`. They cover the hit line/key labels or notation look-ahead. Leave score/combo, life gauge, pads/feedback and backing state updates in place. No HPA-509 HUD redesign.
- Set `PerformanceUILayout.NotationStrip.DepthBackground = 0.90f` (chart-video background remains 0.95f in the same BackToFront pass; staff is 0.80f). No other strip geometry changes.
- **Do not implement judgement notehead tint, fade, source-ID anchor changes or duplicate judgement map**. Miss heads are already under the key mask when judged; those features move to HPA-509 after layout/feedback visibility is settled. Existing judgement popup handles Miss.

**Tests and early visual checkpoint**
- Deterministic tests for frozen versus effective mode, one-shot success/failure/fallback, cleanup/disposal, no retry and default Lanes. Do not build new GPU testing seams or claim that the `OnDraw` branch can be verified headlessly.
- **Immediately after first wiring, before further polish/E2E:** capture a real **1280x720 Tab screenshot** with a chart (use existing `game_screenshot` if available). Inspect hit marker x=220, key labels, visible note approach, omitted obscuring HUD, and opaque strip over chart BGA. Compare unchanged Lanes screenshot. Report evidence or any capture limitation explicitly.

**Checkpoint:** Tab is readable and playable; font/chart failure keeps Lanes working.

## Task 3 — Telemetry, E2E, validation (~0.6 day)

- Report effective `NoteDisplayStyle` in `GameTelemetrySnapshot` as Tab iff a ready `_notationStrip` reference exists; otherwise Lanes. Add `noteDisplayStyle` to existing `DTXMania.Automation/Telemetry/GameStateSnapshot` and `DTXMania.E2E/AutomationContractTests` camelCase JSON contract. No new MCP API or independent mode flag.
- Add one optional note-display-style parameter to `E2EFixtureBuilder.Build` (default Lanes) and write `NoteDisplayStyle=Tab` into its **fresh bootstrap** `Config.ini` only; no INI rewrites after SQLite database exists.
- One existing-category `Category=E2E` AutoPlay smoke: assert actual Tab mode during Performance and successful Result, while judgement totals are unchanged; no new CI workflow.
- Run and report actual commands/results: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`, `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`, plus existing Windows CI build/test and `dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"`. Confirm tests were discovered, fallback behaves, Lanes visuals are unchanged, and screenshot evidence is attached if obtainable.
- Mark HPA-411 Done **only after implementation merges**. Then HPA-509 can address its dedicated HUD and the deferred tint/late-Miss geometry decision.

## Guardrails / completion

No parallel state owner, per-note result-map, color-table copy, unnecessary renderer signature change, speculative animation, new art/SFX or DTXManiaNX edits. If Tab screenshot shows a new obstruction, fix only the existing-UI visibility gate needed to play, not the whole HPA-509 layout. Keep <=3 engineer days.

- [ ] Config/persistence and frozen requested mode tests pass.
- [ ] One-shot main-thread Tab renderer; logged fallback and safe cleanup; no duplicate effective-style state.
- [ ] Tab lane-world replacement, four obscuring HUD draws gated, strip depth 0.90; Lanes unchanged.
- [ ] Early real visual specimen validates readable labels/hit line and BGA layering, or limitation explicitly documented.
- [ ] Effective telemetry + one Tab E2E clear, Mac/Windows build/test evidence.
- [ ] Same PR changed from draft to ready **after** implementation/CI evidence.
