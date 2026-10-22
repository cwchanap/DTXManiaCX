# HPA-411 — Tab-mode configuration and gameplay integration (design)

**Linear:** https://linear.app/cwchanap/issue/HPA-411/tab-mode-config-option-and-gameplay-integration  
**Parent:** HPA-394 · **Prerequisites:** HPA-410 + HPA-443 (Done, on `main`)  
**Delivery:** this draft PR becomes the **single implementation PR** after review; target <=3 engineer days.  
**Follow-up:** HPA-509 owns the dedicated Tab HUD and pad/label presentation.

## Goal

Make the finished HPA-408/409/410 notation strip playable as an **opt-in** alternative to classic falling lanes. The switch must be cosmetic: **same Note objects, timing, judgement, input, AutoPlay, score, gauge, skill, and song completion**. Lanes remains the default. Do not introduce a new engine, data owner, or custom rendering framework.

## Verified reuse and boundaries

- `Lib/Config/DrumsLaneDisplayMode.cs`, `ConfigData`, `ConfigManager`, `IConfigManager`, and `ConfigStage` are the complete enum/label/UI/persistence precedent. Config is stored in **SQLite** (`config.db`); `Config.ini` is only imported at first launch for fresh E2E sandboxes.
- `PerformanceStage.FreezeRunConfiguration` snapshots visual policy once per activation in `PerformanceVisualGates`. The all-zero/uninitialized gates must keep today's Lanes behavior (headless reflection tests).
- `NotationChartBuilder.Build(ParsedChart)` consumes finalized chart timing and retains references to source `Note` objects. `ChartManager` assigns `Note.Id` on those objects. Build the notation chart exactly once **after** `new ChartManager(_parsedChart)`, never in `OnUpdate`/`OnDraw`.
- `NotationStripRenderer(IResourceManager, NotationChart, IReadOnlyList<MeasureLine>)` already owns fonts, glyph validation, lazy pixel texture and disposal. `IsReady` and `Draw(SpriteBatch, nowMs, speedPercent)` are the integration seam. `NotationStripLayout` already handles music positions, measure culling, and immutable engraving coordinates. Reuse these; do not fork them.
- `PerformanceStage.OnDraw` has separate `BackToFront` base, alpha-overlay, UI, and additive-effect passes. Preserve existing batch lifecycle and judgement text/UI passes. Strip primitives use explicit existing layer depths.
- `JudgementEvent.NoteRef` is the runtime `Note.Id`; `OnJudgementMade` is already the event fan-out. Keep its existing forwards unchanged.
- `GameTelemetrySnapshot` is the producer, `DTXMania.Automation/Telemetry/GameStateSnapshot` the E2E consumer, and `AutomationContractTests` pins the camel-case JSON contract.

## Config and run policy

Create `DrumsNoteDisplayStyle { Lanes = 0, Tab = 1 }` and the minimal two display labels ("LANES", "TAB") beside `DrumsLaneDisplayMode`, without changing `LaneDisplayMode` semantics. `ConfigData.NoteDisplayStyle` defaults to `Lanes`. Read `NoteDisplayStyle` case-insensitively, ignore invalid persisted values and keep Lanes; save canonical enum names. `IConfigManager.SetNoteDisplayStyle` follows the existing deferred-save/unchanged-value pattern. Add a "Note Display" dropdown next to "Lane Display" in the Drums config panel. No new storage table/migration.

Snapshot the requested style in `PerformanceVisualGates` on stage activation. The zero/default enum value is Lanes, so existing uninitialized tests remain unaffected. Track a separate **effective active style** as Lanes until the Tab renderer successfully initializes. If requested Tab cannot build its chart or validate/acquire Bravura, log a reason, dispose partial resources, run in Lanes for the whole current song, and keep scoring/input working; do **not** rewrite the user's persisted preference. Telemetry reports the *effective* style, not merely the request. Deactivation clears renderer, judgement colors and active style. Avoid accepting a stale async activation into a newer stage generation; follow the existing activation/cancellation conventions.

The style stays frozen until next performance; ordinary user scroll-speed changes must continue to work. Pass current scroll speed to the strip's existing `Draw` method at render time, using the same timing and percentage semantics as the falling lanes.

## Drawing and judgement tint

In the **base** drawing pass:
- Always draw the background. In Lanes preserve the existing draw path unchanged.
- In active Tab, replace `DrawLaneBackgrounds`, `DrawMeasureLines`, `DrawNotes`, and `DrawJudgementLine` with **one** `NotationStripRenderer.Draw` using the live song-clock position. Skip `DrawNoteOverlays` in the separate overlay pass (no falling-lane overlays).
- Retain the existing HUD, pad presentation, judgement popups, scroll indicator and additive feedback for now; **HPA-509** replaces/repositions the HUD, removes pad drawings and lights the instrument-label column. Do not pre-implement HPA-509.

The current HPA-410 engraving anchors do **not** carry note identity: `NotationStripGlyphAnchor` is currently (TimeMs, Glyph, DxPx, YPx) and the renderer applies one static color. Make the **smallest extension** needed to carry source-note references on **notehead glyphs only**, using `NotationEntry.Heads[].Notes` already provided by the notation model. Stems, beams, flags, rests, barlines and tuplet glyphs retain neutral ink. Do not duplicate source notes, invent another `NoteId`, or mutate the quantizer.

Stage owns a run-scoped `Dictionary<int, JudgementType>` updated in existing `OnJudgementMade` with `NoteRef`; it is read by the renderer during Draw (e.g. an `IReadOnlyDictionary<int, JudgementType>` argument). Use one small pure color resolver, based on the existing judgement popup colors rather than a second unexplained palette. Missing/unresolved note IDs stay neutral; a head representing multiple notes is tinted only when all of its source notes are resolved, with deterministic worst-judgement precedence. Miss is red; Perfect/Great/Good/Poor use the matching judgement colors. A short, **position-based** alpha fade starts as judged noteheads cross x=220 and finishes as they reach the already-masked key column at x=195; no timers or retained per-frame animation objects. Unjudged notes, rests and structural marks should not fade due to this mechanic. Ensure the renderer's `BackToFront` depths and opaque key-column mask remain unchanged.

Important: pad/label flash feedback, new gauge/progress bar, and redesigned score/skill placement belong to HPA-509. This ticket introduces no art/SFX assets.

## Telemetry and tests

Expose `NoteDisplayStyle` as a string on `GameTelemetrySnapshot` during Performance, and consume the serialized `noteDisplayStyle` in `GameStateSnapshot` for E2E. Test that a requested Tab run reports Tab, and a failed initialization reports Lanes; stage transition snapshots do not need invented values.

Focus on:
1. Config default, labels, case-insensitive parse, invalid-value default, deferred setter and real SQLite save/reload (plus existing ConfigStage UI tests/mocks).
2. Frozen run style across config edits and zero/default `PerformanceVisualGates`; one-time chart/renderer creation and cleanup/fallback. Tests should use existing deterministic stage and renderer fakes where possible, no new graphics harness.
3. Pure NoteRef-to-color behavior: hits, Miss, unresolved notes, simultaneous heads, neutral structural marks, and fade boundary x=220/195; one renderer integration test if a headless seam exists.
4. Automation JSON wire contract and **one** fresh-sandbox E2E AutoPlay smoke with `NoteDisplayStyle=Tab`, actual active style telemetry and successful clear. Use current `E2EFixtureBuilder` and the established `Category=E2E` job, not a new CI workflow. A screenshot/manual gameplay check should verify notation is visible at 1280x720, the mode is actually Tab, and Lanes still looks unchanged.

## Out of scope

HPA-509 HUD/pad/label redesign; chart parser, notation quantizer, music glyph assets, input mapping, judgement/scoring/AutoPlay behavior, performance caching, new abstraction layers, settings migration framework, and legacy DTXManiaNX changes. Any polish beyond the focused goal should be deferred rather than exceed 3 engineer days.

## Acceptance

- Default/new and invalid config launch in unchanged Lanes; Tab can be selected and persisted/reloaded.
- A Tab run draws real moving notation replacing lane elements without throwing, while the existing HUD and gameplay work.
- Perfect/Great/Good/Poor/Miss visibly tint the correct **source notehead** and resolved noteheads fade only after the hit marker; no cross-song state leaks.
- Missing Bravura/invalid notation causes a logged, clean fallback **to active Lanes**, without persisting a preference change.
- API/E2E confirms the actual active mode and a Tab AutoPlay run reaches Result without score/judgement regression.
- One implementation PR <=3 engineer days, Mac + Windows build/unit tests and Windows E2E passing; report any platform not actually executed.
