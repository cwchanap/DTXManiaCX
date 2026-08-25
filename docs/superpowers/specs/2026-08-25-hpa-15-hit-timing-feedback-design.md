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
- `0` = rounded exactly on time

The unit is implicit from the setting name and the established lag-number asset; do not add another font or `ms` label solely for this feature.

Use one active timing value per lane. A newer hit on the same lane replaces and restarts that lane's value instead of stacking multiple numbers. Different lanes may display concurrently. This matches the legacy NX lane-local status model and avoids unreadable overlap on fast patterns.

Use `PerformanceUILayout.SpriteJudgementTextAssets.TotalDurationSeconds` as the timing value lifetime so it has the same short persistence as judgement feedback. This is only a shared duration: judgement words currently stack on repeated same-lane hits while timing feedback deliberately replaces per lane. Do not claim or implement a coupled lifecycle, and do not change judgement-word stacking in HPA-15.

A simple alpha fade is sufficient; do not copy the judgement word's pop/scale animation.

Place the timing glyph run 34 px below the sprite judgement word baseline and center the entire run on the lane. For `glyphCount` 15 px-wide glyphs:

```text
x = PerformanceUILayout.GetLaneX(lane) - (glyphCount * 15) / 2f
y = judgement-word Y + 34
```

The Y calculation must share the existing `SpriteJudgementTextAssets.JudgementLineOffsetY` contract rather than introducing an unrelated gameplay coordinate.

## Lag-number projection and glyph packing

Reuse `Graphics/7_lag numbers.png` rather than introducing a new font HUD.

### Fixed sheet geometry

Port the stable NX geometry exactly:

- required texture size: 128×128 minimum
- glyph size: 15×19
- 12 glyph slots per color bank
- 4 columns per bank, therefore a 4×3 row-major bank
- FAST bank origin: `(0, 0)`
- SLOW bank origin: `(64, 64)`
- digit slots: `0..9`
- plus slot: `10`
- minus slot: `11`

For a slot `0..11` and bank origin `(bankX, bankY)`:

```text
sourceX = bankX + (slot % 4) * 15
sourceY = bankY + (slot / 4) * 19
source  = Rectangle(sourceX, sourceY, 15, 19)
```

This mapping belongs in `PerformanceUILayout.HitTimingFeedback`; `HitTimingFeedbackDisplay` must not duplicate the packing math.

### Round before choosing sign/color

Projection is based on the rounded display value, not the raw floating-point sign.

Use midpoint-away-from-zero rounding:

```text
rounded = Math.Round(deltaMs, MidpointRounding.AwayFromZero)
```

Then apply exactly these rules:

- `rounded == 0`: text `"0"`, FAST bank, no sign regardless of the raw delta sign
- `rounded < 0`: signed negative text such as `"-18"`, FAST bank
- `rounded > 0`: explicit positive text such as `"+24"`, SLOW bank

Examples that pin the edge behavior:

- `-0.4` -> `0`, FAST bank, no minus sign
- `+0.4` -> `0`, FAST bank, no plus sign
- `-0.5` -> `-1`, FAST bank
- `+0.5` -> `+1`, SLOW bank

Keep this projection as one pure helper in `PerformanceUILayout.HitTimingFeedback` (or an equally small adjacent pure value helper owned by that nested layout contract). `Draw` receives the already-projected text/bank and does not reconsider the raw sign.

Do not expose the old `ShowLagTimeColor` color-flip option. HPA-15 owns one presentation rule only.

If the lag-number texture is missing, disposed, or smaller than 128×128 in a custom skin, skip timing feedback without affecting gameplay or judgement text. Do not add a font fallback or reload framework for this optional aid.

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
- AutoPlay currently produces `DeltaMs == 0`, but do not use the delta value to detect AutoPlay; use the frozen lane policy.
- The displayed value is exactly the existing `DeltaMs` rounded for presentation. Do not recalculate timing from note timestamps or apply another latency offset.

## Component boundary

Add one small presentation owner under `DTXMania.Game/Lib/Stage/Performance/`.

### `HitTimingFeedbackDisplay`

Responsibilities:

- load and release `TexturePath.LagNumbers`
- validate the texture against the 128×128 minimum contract
- own a `PerformanceUILayout.LaneCount`-sized set of per-lane active states
- ask `PerformanceUILayout.HitTimingFeedback` to project `DeltaMs` and map glyph source rectangles
- replace/restart state for a lane on `Spawn`
- update the short fade lifecycle
- draw each active glyph run at `GetLaneRunPosition(lane, glyphCount)`

It must not read config, inspect AutoPlay state, calculate judgement timing, update counters, or publish telemetry. Those decisions stay in `PerformanceStage`.

Expose only a narrow test seam similar to the existing sprite judgement popup owner:

- `CreateForTesting(...)` accepts an injected `ITexture?` and optional active-state storage as needed by focused tests
- expose an internal active-lane count/read view sufficient for stage routing assertions

Do not introduce an interface, generic popup abstraction, renderer hierarchy, or font fallback.

### `PerformanceUILayout.HitTimingFeedback`

This nested owner is the single source for:

- `GlyphWidth = 15`
- `GlyphHeight = 19`
- `ColumnsPerBank = 4`
- `SlotsPerBank = 12`
- FAST/SLOW bank origins
- `RequiredTextureWidth = 128`
- `RequiredTextureHeight = 128`
- digit/plus/minus slot mapping
- midpoint-away-from-zero delta projection
- row-major source-rectangle mapping
- full-run lane centering
- vertical placement 34 px below judgement text
- lifetime equal to `SpriteJudgementTextAssets.TotalDurationSeconds`

Do not add skin metadata or a configurable layout model for this one fixed bundled asset.

## PerformanceStage integration

- Construct `HitTimingFeedbackDisplay` with the other performance presentation components.
- Update it immediately beside the existing sprite/font judgement-popup updates in the current component-update path.
- Draw it inside `DrawJudgementTexts`, beside the current judgement-word draw calls, so it stays in the existing alpha-blended base pass. Do not add another `SpriteBatch` pass.
- Dispose it with the other performance components.
- Extend `PerformanceVisualGates` with positive `ShowHitTimingFeedback` state.

The all-zero/default `PerformanceVisualGates` value must continue to mean current CX visuals. Adding a positive boolean preserves the reflection-created deterministic test behavior established by HPA-19.

`FreezeRunConfiguration` copies `Config.ShowHitTimingFeedback` once per run. No live config subscription is required.

Inside `OnJudgementMade`, spawn only when:

```text
_visualGates.ShowHitTimingFeedback
&& e.IsHit()
&& !FrozenAutoPlayLanes.Contains(e.Lane)
```

Keep all existing score/combo/gauge/skill/effect/pad/judgement-word flow unchanged.

Remove the existing unused `_lagNumbersTexture` field/load/release from `PerformanceStage`; the new display is the sole owner of `TexturePath.LagNumbers`.

### Cleanup test migration

`PerformanceStageDeterministicTests.CleanupComponents...` currently injects `_lagNumbersTexture` and verifies its `RemoveReference()` call. Deleting the stale field must not silently delete resource-ownership coverage.

Retarget that test:

- create `HitTimingFeedbackDisplay` through its test factory with the existing lag texture mock
- inject it into the stage's `_hitTimingFeedbackDisplay` field
- invoke `CleanupComponents`
- verify the injected texture reference is released once through display disposal

The standalone display tests still own the detailed `Dispose` contract; the stage cleanup assertion proves stage lifecycle wiring calls it.

## Persistence and config-stage behavior

Follow the exact boolean pattern already used by `ShowCombo`:

- default in `ConfigData`
- parse only valid booleans
- write the value in `BuildPersistedEntries`
- setter is a no-op when unchanged and otherwise calls `MarkDirty()`
- add a `ToggleConfigItem` in `ConfigStage`

Do not create a dedicated settings object, event, migration, or compatibility parser.

Also extend the existing `ConfigManager_PerLaneAutoPlayMutators_ShouldBeExposedByInterface` reflection surface test with `SetShowHitTimingFeedback`. Without that assertion, config behavior tests can remain green while the new setter is accidentally omitted from `IConfigManager`.

## Test strategy

Keep tests on existing owners.

### Config

- `ConfigDataTests`: default is Off.
- `ConfigManagerTests`: interface surface includes `SetShowHitTimingFeedback`; setter changes value and no-op behavior matches other boolean setters.
- `ConfigManagerSqlitePersistenceTests`: true/false round trip; missing key leaves Off.
- `ConfigStageLogicTests`: exact Drums item ordering includes `Hit Timing Feedback` after `Combo`, and toggle dispatches to the typed setter.
- Update only the existing hand-written `IConfigManager` test stub required by the new interface member.

### Layout projection/geometry

Extend `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs` rather than hiding the contract inside display tests.

Pin:

- required texture size 128×128
- FAST and SLOW source rectangles for slots `0`, `9`, `10`, and `11`
- 4-column row-major wrap at slots 4 and 8
- `-0.4 -> "0"/FAST`, `+0.4 -> "0"/FAST`, `-0.5 -> "-1"/FAST`, `+0.5 -> "+1"/SLOW`
- `GetLaneRunPosition(lane, glyphCount)` centers the full glyph run and uses judgement-text Y + 34

Production and tests should both call the layout mapper; do not copy the coordinate formula into `HitTimingFeedbackDisplayTests`.

### Presentation

Add focused `HitTimingFeedbackDisplayTests` covering:

- projected negative/positive/zero runs draw the expected glyph sequence supplied by the layout mapper
- a same-lane hit replaces/restarts the old value
- separate lanes can coexist
- expiry at the shared duration
- missing/invalid texture is a safe no-op
- dispose releases only the display's own texture reference

No live `GraphicsDevice` harness is needed; use an `ITexture`/draw seam consistent with current performance presentation tests.

### PerformanceStage

Extend `PerformanceStageDeterministicTests` rather than creating another stage harness:

- `FreezeRunConfiguration` captures the positive toggle and keeps it frozen for the run
- inject `HitTimingFeedbackDisplay.CreateForTesting(...)` into `_hitTimingFeedbackDisplay`
- enabled manual hit increases the display's active-lane count
- disabled manual hit does not
- enabled AutoPlay-lane hit does not
- `Miss` does not
- mixed AutoPlay still permits feedback on manual lanes
- existing cleanup test is retargeted from `_lagNumbersTexture` to the injected display and verifies release through `CleanupComponents`

Existing score/combo/gauge/skill assertions remain the regression proof that this presentation feature does not change gameplay rules.

Run the whole performance namespace because `OnJudgementMade` is also exercised by additional-coverage tests.

## Manual smoke

With a simple chart and AutoPlay Off:

1. Confirm default Off produces current visuals with no timing number.
2. Enable `Hit Timing Feedback`, start a new run, and deliberately hit early and late.
3. Confirm early and late values use different bundled color banks and the displayed magnitude/sign tracks the rounded hit direction.
4. Confirm near-zero values that round to zero show unsigned `0`.
5. Confirm rapid hits on one lane replace the prior timing value even though judgement words may overlap independently.
6. Enable AutoPlay for one lane and confirm that automated lane emits no timing number while manual lanes still do.
7. Restart the game and confirm the toggle reloads from SQLite.

## Non-goals

- no FAST/SLOW aggregate counters
- no result-screen timing statistics
- no `ShowLagTimeColor`
- no debug-info overlay
- no runtime logging/TraceLog switch
- no timing-window or `AudioLatencyOffsetMs` changes
- no telemetry/API changes
- no generic popup/animation framework
- no judgement-word stacking changes
- no new art
