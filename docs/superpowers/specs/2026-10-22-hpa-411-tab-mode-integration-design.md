# HPA-411 — Tab-mode configuration and gameplay integration (design)

**Linear:** https://linear.app/cwchanap/issue/HPA-411/tab-mode-config-option-and-gameplay-integration  
**Parent:** HPA-394 · **Prerequisites:** HPA-410 + HPA-443 (Done, on `main`)  
**Delivery:** implement on this same draft PR, <=3 engineer days.  
**Follow-up:** HPA-509 owns the dedicated Tab HUD, label/pad feedback, and the visibility/geometry decision for notehead judgement tint and fade.

## Goal and verified reuse

Enable the existing HPA-408/409/410 scrolling notation as an **opt-in, playable** alternative to falling lanes. Same source `Note` objects, chart/song clock, input, scoring, judgement, AutoPlay, gauge and Result behavior. Lanes remains the default. No new rendering engine or parallel gameplay state.

- Extend `DrumsLaneDisplayMode`'s existing `ConfigData` / `ConfigManager` / `IConfigManager` / `ConfigStage` pattern. SQLite `config.db` stores preferences; `Config.ini` is imported only for a fresh database/isolated E2E fixture.
- `PerformanceStage.FreezeRunConfiguration` and `PerformanceVisualGates` already freeze display policy per activation. An all-zero/uninitialized stage must select Lanes.
- `ChartManager` assigns IDs and finalizes source notes. `NotationChartBuilder.Build(_parsedChart)` uses existing model and remains pure; build once after `new ChartManager(_parsedChart)`, not per frame.
- Reuse `NotationStripRenderer` unchanged except the **one** background-depth correction below. Its constructor catches font load errors, validates Bravura glyph coverage and exposes `IsReady`. No new font resource/cache abstraction.
- Preserve `PerformanceStage.OnDraw`'s four existing sprite batches and `OnJudgementMade` event fan-out. `GameTelemetrySnapshot` is the producer; `DTXMania.Automation/Telemetry/GameStateSnapshot` is the consumer.
- Existing `JudgementManager.GetNoteRuntimeData(Note.Id)?.JudgementEvent` already owns each judgement (including Miss). **Do not** add a second stage-owned judgement-results dictionary or alter the renderer API in HPA-411.

## Config and mode lifetime

Add `DrumsNoteDisplayStyle { Lanes = 0, Tab = 1 }` beside `DrumsLaneDisplayMode` with labels "LANES"/"TAB"; `ConfigData.NoteDisplayStyle` defaults to Lanes. Read the `NoteDisplayStyle` key by **name**, case-insensitively, refusing numeric/unknown values; save the canonical enum name. Reuse the deferred-save `IConfigManager.SetNoteDisplayStyle` convention and add a "Note Display" selector next to "Lane Display" in the Drums config panel. No new table or migration. This option is independent of the existing `LaneDisplayMode`.

Freeze the **requested** style in `PerformanceVisualGates`; live config edits do not flip the run. The **effective** style is derived from the nullable `_notationStrip` renderer reference: non-null means Tab (retain it only after `IsReady`); null means Lanes. Do not introduce a second active-style field. Cleanup nulls the reference **before** disposing it. Telemetry reports effective style, not requested style; keep the requested Tab preference on fallback.

Chart parsing may `await ParseAsync` and resume off-thread. Build the pure `NotationChart` after `ChartManager`, but **defer resource/font construction to the game's update thread**. Use the minimal pending-chart/one-shot-attempt state required to initialize once, never a per-frame retry. Respect existing generation/cancellation checks before publishing pending work, and discard it during cleanup. A failed notation build or unavailable Bravura logs a specific reason, disposes partial resources and keeps **playable Lanes** for that song; don't fail audio/gameplay loading. Until the one-shot attempt finishes, Lanes remains active.

The existing strip draws with the same logical song clock as falling notes and the current (live) scroll-speed percentage. No per-frame rebuilding.

## Rendering: playable interim layout

In the base `SpriteSortMode.BackToFront` batch keep `DrawBackground`, `DrawJudgementTexts`, gameplay feedback, and standard scoring. In effective Tab mode, call `_notationStrip.Draw(spriteBatch, nowMs, speedPercent)` **instead of** `DrawLaneBackgrounds`, `DrawMeasureLines`, `DrawNotes`, `DrawJudgementLine`. Skip `DrawNoteOverlays` in the separate overlay pass only for Tab. Keep all existing Lanes draw paths untouched.

**Essential interim Tab visibility gate:** the current skill panel covers x=22–279/y=250–689, hiding the Tab hit marker x=220 and key labels. The vertical progress bar and skill-meter graph obscure x=853–1151 of notation look-ahead. Only in effective Tab, skip `DrawSkillPanel`, `_skillPanelDisplay.Draw`, `DrawProgressBar` and `_skillMeterDisplay.Draw`. Continue to **update** underlying metrics; retain the score, combo, life gauge, judgement/timing popups, pad hit feedback and other unrelated UI for now. HPA-509 provides the finished replacement HUD/progress/counts and label flash. Do not relocate HUD elements here.

**Depth fix:** `PerformanceUILayout.NotationStrip.DepthBackground` must change from 0.95f to **0.90f** because chart BGA video draws at 0.95f in the *same* back-to-front batch; equal depths have unstable order. A strip background at 0.90f occludes video correctly and remains behind the 0.80f staff/lines. No other strip geometry changes.

**Explicit deferral — judgement tint/fade:** the opaque key mask spans x=0–195, while the hit marker is x=220 and the strip scroll rate at 100% is 0.38 px/ms. Miss occurs only >200ms late, when its head is at x<144 (already hidden); at 50% it is x<182 (also hidden). On-time heads only remain in the 25px visible-after-hit region for ~66ms, before masking. Tint/fade, notehead-anchor identity, and a color resolver cannot satisfy the previously proposed visible-Miss acceptance. Defer these to HPA-509 with a decision on how late/Miss feedback is displayed (visible head space **or** separate feedback); existing judgement popups remain authoritative in HPA-411. Do not widen/move the mask/hit marker as an unreviewed extra change in this ticket.

## Validation

1. Config unit tests: default Lanes; selector labels; name-only case-insensitive reading; invalid/numeric value ignored; unchanged setter, delayed SQLite save, persisted reload; existing `IConfigManager` test mocks.
2. Deterministic stage tests: requested style frozen per run, zero/default Lanes, one-time pending/init attempt, failure fallback/no persisted change, cleanup resets/disposes renderer. Test state decisions/telemetry, **not GPU pixels**; `OnDraw` and `NotationStripRendererDrawTests` require a graphics device and are not headless CI targets.
3. **Early manual graphics checkpoint, directly after initial strip wiring**: capture a real 1280x720 gameplay specimen (use existing `game_screenshot` integration when available). Confirm hit marker, key labels and oncoming notation are not hidden behind lane HUD; confirm BGA cannot overpaint the notation background. Run a Lanes visual regression. Do not claim a screenshot/headless rendering test without evidence.
4. Wire `noteDisplayStyle` effective value through `GameTelemetrySnapshot`, `GameStateSnapshot` and the `AutomationContractTests` JSON contract. Add one `Category=E2E` Tab AutoPlay smoke with fresh-sandbox `E2EFixtureBuilder.Build` optional style parameter, asserting active Tab during Performance and a successful Result. Reuse existing Windows CI, no new workflow.

## Scope / acceptance

- Selectable persisted Tab, unchanged default Lanes; the requested style stays frozen for an entire run.
- Real notation replaces falling lanes in Tab and its **hit marker/key labels remain readable** at 1280×720; existing score, judgement and AutoPlay still work, while the four legacy HUD overlays are suppressed **only for Tab**.
- Chart video stays behind the opaque strip background; one-time font/notation failure logs a reason and falls back to active Lanes without rewriting the saved preference.
- Effective-mode telemetry and Tab AutoPlay E2E pass; no Lanes visual/score regression. Build/test Mac and Windows, reporting unexecuted platforms honestly.
- Keep one PR and <=3 engineer days. Out of scope: judgement notehead tint/fade/geometry (HPA-509), completed Tab HUD, gauge/progress/pad/label redesign, extra assets, parser/quantizer/input/judgement changes, DTXManiaNX changes.
