# HPA-15 Hit Timing Feedback — Design

## Goal

Add one optional Drums gameplay aid that shows the signed timing error for a manual hit without changing judgement, scoring, latency compensation, AutoPlay, or result data.

This remains one focused HPA-15 PR and should fit comfortably within 2–3 engineer days.

## Scope

HPA-15 adds only:

- a persisted `Hit Timing Feedback` toggle in the existing Drums config list
- a short lane-local signed timing value for manual successful judgements
- reuse of the existing `JudgementEvent.DeltaMs` value and bundled lag-number art

Default is **Off**, preserving current gameplay visuals.

Do not add `ShowLagTimeColor`, aggregate FAST/SLOW counters, a debug HUD, runtime trace/log controls, telemetry fields, new judgement windows, or latency-calibration behavior.

## Current seams

CX already has everything needed to decide what to display:

- `JudgementEvent.DeltaMs` is the signed timing error: negative is early, positive is late.
- `JudgementEvent.IsHit()` identifies resolved hits; `Miss` is not useful hit timing feedback.
- `PerformanceStage.OnJudgementMade` is the existing fan-out point for judgement presentation.
- `FrozenAutoPlayLanes` distinguishes automated lanes from player-controlled lanes for the current run.
- `FreezeRunConfiguration` already snapshots Drums visual settings for one performance activation.
- `TexturePath.LagNumbers` points to bundled `Graphics/7_lag numbers.png`.

`PerformanceStage` currently has an unused `_lagNumbersTexture` field that actually loads `TexturePath.LagIndicator`. HPA-15 should remove that stale field/load rather than creating a second timing-texture owner.

## UX contract

### Config

Add `Hit Timing Feedback` immediately after `Combo` in the Drums category.

- Off by default.
- Changes apply to the next performance activation, matching the other frozen Drums visual settings added by HPA-19.
- No hot-reload event is needed.

Persist one boolean:

- `ConfigData.ShowHitTimingFeedback`, default `false`
- SQLite key `ShowHitTimingFeedback`
- `IConfigManager.SetShowHitTimingFeedback(bool)` using the existing changed/no-op/`MarkDirty()` pattern

No schema migration is required; a missing key naturally keeps the default Off value.

### Gameplay presentation

For an eligible judgement, display the rounded signed millisecond delta below the existing lane-local judgement word.

Examples:

- `-18` = early / FAST
- `+24` = late / SLOW
- `0` = exactly on time

The unit is implicit from the setting name and the established lag-number asset; do not add another font or `ms` label solely for this feature.

Use one active timing value per lane. A newer hit on the same lane replaces and restarts that lane's value instead of stacking multiple numbers. Different lanes may display concurrently. This matches the legacy NX lane-local status model and avoids unreadable overlap on fast patterns.

Use the same total lifetime as the current sprite judgement popup (`PerformanceUILayout.SpriteJudgementTextAssets.TotalDurationSeconds`) so the judgement word and timing value disappear together. A simple alpha fade is sufficient; do not add another animation system.

Place the timing number 34 px below the sprite judgement word, matching the legacy NX lag-number relationship while keeping it above the judgement line.

### Early/late styling

Reuse `Graphics/7_lag numbers.png` rather than introducing a new font HUD.

Port only the stable glyph geometry from NX:

- 128×128 sheet
- 15×19 glyph cells
- 12 glyph slots per color bank
- first bank starts at `(0, 0)`
- second bank starts at `(64, 64)`
- digits are slots `0..9`
- plus/minus sign slots are `10/11`

Use one fixed direction mapping, equivalent to NX's default lag-color behavior:

- early / negative: FAST-colored bank
- late / positive: SLOW-colored bank
- zero: use the FAST-colored zero glyph with no sign; it has no directional meaning and is rare enough that a third palette is not justified

Do not expose the old `ShowLagTimeColor` color-flip option. HPA-15 owns one presentation rule only.

If the lag-number texture is missing or invalid in a custom skin, skip timing feedback without affecting gameplay or judgement text. Do not add a second fallback renderer for this optional aid.

## Eligibility and data flow

`PerformanceStage` owns the policy decision; the display component owns only transient presentation.

```text
JudgementManager.JudgementMade
  -> existing score/combo/gauge/skill/effect/judgement-text flow
  -> if ShowHitTimingFeedback
       and e.IsHit()
       and e.Lane is not in FrozenAutoPlayLanes
       -> HitTimingFeedbackDisplay.Spawn(e)
```

Consequences:

- `Perfect`, `Great`, `Good`, and `Poor` manual hits may show feedback.
- `Miss` does not show a timing number.
- A judgement resolved by AutoPlay never shows player timing feedback.
- With partial AutoPlay, manual lanes still show timing feedback while automated lanes do not.
- The displayed value is exactly the existing `DeltaMs` rounded for presentation. Do not recalculate timing from note timestamps or apply another latency offset.

## Component boundary

Add one small presentation owner under `DTXMania.Game/Lib/Stage/Performance/`:

### `HitTimingFeedbackDisplay`

Responsibilities:

- load and release `TexturePath.LagNumbers`
- own at most one active timing value per lane
- round/format the signed `DeltaMs` for display
- map characters to the fixed lag-number source rectangles
- update the short fade lifecycle
- draw each active value centered on its lane

It must not read config, inspect AutoPlay state, calculate judgement timing, update counters, or publish telemetry. Those decisions stay in `PerformanceStage`.

Expose only a narrow test seam similar to the existing sprite judgement popup tests; do not create a generic popup framework or renderer hierarchy.

### `PerformanceUILayout.HitTimingFeedback`

Keep glyph dimensions, bank offsets, vertical placement, and source-rectangle mapping in the layout owner rather than scattering magic numbers through the display class.

Do not add skin metadata or a configurable layout model for this one fixed bundled asset.

## PerformanceStage integration

- Construct `HitTimingFeedbackDisplay` with the other performance presentation components.
- Update it from the existing component-update path.
- Draw it in the normal alpha-blended gameplay pass alongside judgement text.
- Dispose it with the other performance components.
- Extend `PerformanceVisualGates` with positive `ShowHitTimingFeedback` state.

The all-zero/default `PerformanceVisualGates` value must continue to mean current CX visuals. Adding a positive boolean preserves the reflection-created deterministic test behavior established by HPA-19.

`FreezeRunConfiguration` copies `Config.ShowHitTimingFeedback` once per run. No live config subscription is required.

Remove the existing unused `_lagNumbersTexture` field/load/release from `PerformanceStage`; the new display is the sole owner of `TexturePath.LagNumbers`.

## Persistence and config-stage behavior

Follow the exact boolean pattern already used by `ShowCombo`:

- default in `ConfigData`
- parse only valid booleans
- write the value in `BuildPersistedEntries`
- setter is a no-op when unchanged and otherwise calls `MarkDirty()`
- add a `ToggleConfigItem` in `ConfigStage`

Do not create a dedicated settings object, event, migration, or compatibility parser.

## Test strategy

Keep tests on existing owners.

### Config

- `ConfigDataTests`: default is Off.
- `ConfigManagerTests`: setter changes value and no-op behavior matches other boolean setters.
- `ConfigManagerSqlitePersistenceTests`: true/false round trip; missing key leaves Off.
- `ConfigStageLogicTests`: exact Drums item ordering includes `Hit Timing Feedback` after `Combo`, and toggle dispatches to the typed setter.
- Update only existing hand-written `IConfigManager` test stubs required by the new interface member.

### Presentation

Add focused `HitTimingFeedbackDisplayTests` covering:

- negative delta rounds/formats as early and uses the FAST bank
- positive delta rounds/formats as late and uses the SLOW bank
- zero has no sign
- source rectangles for digits and both sign slots
- lane centering and the 34 px offset below judgement text
- a same-lane hit replaces/restarts the old value
- separate lanes can coexist
- expiry at the shared judgement-popup lifetime
- missing/invalid texture is a safe no-op

No live `GraphicsDevice` harness is needed; use the same texture/draw seams already used by other performance presentation tests.

### PerformanceStage

Extend `PerformanceStageDeterministicTests` rather than creating another stage harness:

- `FreezeRunConfiguration` captures the toggle and keeps it frozen for the run
- enabled manual hit spawns timing feedback
- disabled manual hit does not
- enabled AutoPlay-lane hit does not
- `Miss` does not
- mixed AutoPlay still permits feedback on manual lanes

Existing score/combo/gauge/skill assertions remain the regression proof that this presentation feature does not change gameplay rules.

Run the whole performance namespace because `OnJudgementMade` is also exercised by additional-coverage tests.

## Manual smoke

With a simple chart and AutoPlay Off:

1. Confirm default Off produces current visuals with no timing number.
2. Enable `Hit Timing Feedback`, start a new run, and deliberately hit early and late.
3. Confirm early and late values use different bundled color banks and the displayed magnitude/sign tracks the hit direction.
4. Confirm rapid hits on one lane replace the prior value rather than stacking.
5. Enable AutoPlay for one lane and confirm that automated lane emits no timing number while manual lanes still do.
6. Restart the game and confirm the toggle reloads from SQLite.

## Non-goals

- no FAST/SLOW aggregate counters
- no result-screen timing statistics
- no `ShowLagTimeColor`
- no debug-info overlay
- no runtime logging/TraceLog switch
- no timing-window or `AudioLatencyOffsetMs` changes
- no telemetry/API changes
- no generic popup/animation framework
- no new art
