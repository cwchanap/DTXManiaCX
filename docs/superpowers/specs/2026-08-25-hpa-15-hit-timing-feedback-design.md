# HPA-15 Hit Timing Feedback — Design

## Goal

Add one optional Drums gameplay aid that shows timing error for a manual hit without changing judgement, scoring, latency compensation, AutoPlay, or result data.

This remains one focused HPA-15 PR and should fit within 2–3 engineer days.

## Scope

HPA-15 adds only:

- a persisted `Hit Timing Feedback` toggle in the existing Drums config list
- a short lane-local timing value for manual successful judgements
- reuse of the existing `JudgementEvent.DeltaMs`
- a corrected CX Neon `Graphics/7_lag numbers.png` atlas using the existing file/path

Default is **Off**, preserving current gameplay visuals.

Do not add `ShowLagTimeColor`, aggregate FAST/SLOW counters, a debug HUD, runtime trace/log controls, telemetry fields, new judgement windows, or latency-calibration behavior.

## Current seams

CX already has the required gameplay policy/data seams:

- `JudgementEvent.DeltaMs` is the signed timing error: negative is early, positive is late.
- `JudgementEvent.IsHit()` identifies resolved hits; `Miss` is not useful hit timing feedback.
- `PerformanceStage.OnJudgementMade` is the existing fan-out point for judgement presentation.
- `FrozenAutoPlayLanes` distinguishes automated lanes from player-controlled lanes for the current run.
- `FreezeRunConfiguration` already snapshots Drums visual settings for one performance activation.
- `TexturePath.LagNumbers` points to `Graphics/7_lag numbers.png`.
- `SpriteJudgementTextPopupManager` already establishes single-texture load/validate/reload/dispose behavior for a presentation component in this draw path.

`PerformanceStage` currently has an unused `_lagNumbersTexture` field that actually loads `TexturePath.LagIndicator`. HPA-15 removes that stale field/load rather than creating a second timing-texture owner.

## Shipping-asset reality

The repo-root `System/Graphics` tree is a development/reference pack and is explicitly not shipped. Release packaging stages `System/CXNeon/Graphics` as the base `Graphics/` directory.

That matters because the two existing lag-number sheets do not currently share a layout:

- the NX-derived reference sheet under `System/Graphics` uses the legacy 4×3 / 15×19 bank geometry
- the generated CX Neon sheet currently uses a 5-column, two-row digits-only layout

The current CX Neon sheet is still 128×128, so a dimension-only runtime check cannot detect this mismatch. HPA-15 therefore must correct the generated CX Neon atlas before the gameplay renderer pins the legacy-compatible source rectangles.

### CX Neon atlas contract

Keep the existing asset path and 128×128 authored canvas, but regenerate its contents as:

- FAST bank origin `(0, 0)`
- SLOW bank origin `(64, 64)`
- 4 columns × 3 rows per bank
- 15×19 glyph cells
- digit slots `0..9`
- slot `10` reserved/unused
- minus slot `11`
- FAST styling follows existing CX Neon FAST semantics: cyan
- SLOW styling follows existing CX Neon SLOW semantics: danger/red

No new asset file or hand-authored artwork is introduced; this is a layout correction to the existing generated CX Neon atlas.

`tools/skingen/test_skingen.py` must pin the semantic atlas contract against both the generated source asset and the committed `System/CXNeon` artifact, because those are the source/artifact pair that can drift and the latter is what releases actually ship.

Do not use the repo-root NX sheet as release proof.

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

For an eligible judgement, display the rounded millisecond delta below the existing lane-local judgement word.

Examples:

- `-18` = early / FAST
- `24` = late / SLOW
- `0` = rounded on-time

Late values are intentionally unsigned. Legacy NX converts positive lag with `ToString()` and uses the color bank to distinguish direction; it never renders a positive `+` sign. Slot 10 is therefore not part of the HPA-15 glyph contract.

The unit is implicit from the setting name and lag-number presentation; do not add another font or `ms` label solely for this feature.

Use one active timing value per lane. A newer hit on the same lane replaces and restarts that lane's value instead of stacking multiple numbers. Different lanes may display concurrently.

Use `PerformanceUILayout.SpriteJudgementTextAssets.TotalDurationSeconds` as the timing value lifetime. This is only a shared duration: judgement words currently stack on repeated same-lane hits while timing feedback deliberately replaces per lane. Do not claim or implement a coupled lifecycle, and do not change judgement-word stacking in HPA-15.

A simple alpha fade is sufficient; do not copy the judgement word's pop/scale animation.

Place the timing glyph run 34 px below the sprite judgement word baseline and center the entire run on the lane. For `glyphCount` 15 px-wide glyphs:

```text
x = PerformanceUILayout.GetLaneX(lane) - (glyphCount * 15) / 2f
y = judgement-word Y + 34
```

The Y calculation shares the existing `SpriteJudgementTextAssets.JudgementLineOffsetY` contract rather than introducing an unrelated gameplay coordinate.

## Lag-number projection and glyph packing

### Fixed geometry

`PerformanceUILayout.HitTimingFeedback` owns the runtime atlas contract:

- `GlyphWidth = 15`
- `GlyphHeight = 19`
- `ColumnsPerBank = 4`
- `SlotsPerBank = 12`
- FAST bank origin `(0, 0)`
- SLOW bank origin `(64, 64)`
- digit slots `0..9`
- slot `10` unused
- minus slot `11`

For a slot `0..11` and bank origin `(bankX, bankY)`:

```text
sourceX = bankX + (slot % ColumnsPerBank) * GlyphWidth
sourceY = bankY + (slot / ColumnsPerBank) * GlyphHeight
source  = Rectangle(sourceX, sourceY, GlyphWidth, GlyphHeight)
```

The runtime minimum texture dimensions must be **derived from the actual extents**, not hardcoded to the authored 128×128 canvas:

```text
RowsPerBank = ceil(SlotsPerBank / ColumnsPerBank) = 3
RequiredTextureWidth  = SlowBankOrigin.X + ColumnsPerBank * GlyphWidth  = 124
RequiredTextureHeight = SlowBankOrigin.Y + RowsPerBank * GlyphHeight   = 121
```

This matches the `SpriteJudgementTextAssets` precedent of deriving required bounds from source rectangles. A correctly packed custom sheet may therefore be 124×121 or larger; the committed CX Neon asset remains 128×128 because that is its manifest/authored size.

The source-rectangle math belongs only in `PerformanceUILayout.HitTimingFeedback`; `HitTimingFeedbackDisplay` must not duplicate it.

### Round before choosing direction/bank

Projection is based on the rounded display value, not the raw floating-point sign.

Use midpoint-away-from-zero rounding:

```text
rounded = Math.Round(deltaMs, MidpointRounding.AwayFromZero)
```

Then apply exactly:

- `rounded == 0`: text `"0"`, FAST bank, no sign regardless of raw delta sign
- `rounded < 0`: negative text such as `"-18"`, FAST bank
- `rounded > 0`: unsigned magnitude such as `"24"`, SLOW bank

Examples:

- `-0.4` -> `0`, FAST
- `+0.4` -> `0`, FAST
- `-0.5` -> `-1`, FAST
- `+0.5` -> `1`, SLOW

Keep this as one pure projection helper owned by `PerformanceUILayout.HitTimingFeedback`. `Draw` receives the already-projected text/bank and does not reconsider the raw sign.

Do not expose the old `ShowLagTimeColor` color-flip option. HPA-15 owns one fixed direction mapping.

## Eligibility and data flow

`PerformanceStage` owns policy; the display owns transient presentation.

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
- AutoPlay never shows player timing feedback.
- With partial AutoPlay, manual lanes still show timing feedback while automated lanes do not.
- AutoPlay currently produces `DeltaMs == 0`, but do not use delta as the AutoPlay detector; use the frozen lane policy.
- Display the existing `DeltaMs` rounded only for presentation. Do not recalculate note timing or apply latency compensation again.

## Component boundary

Add one small owner under `DTXMania.Game/Lib/Stage/Performance/`.

### `HitTimingFeedbackDisplay`

Responsibilities:

- load/release `TexturePath.LagNumbers`
- validate against `PerformanceUILayout.HitTimingFeedback.RequiredTextureWidth/Height`
- own a `PerformanceUILayout.LaneCount`-sized set of per-lane active states
- reject `Spawn` lanes outside `0..LaneCount-1` before indexing state or calling layout helpers
- ask `PerformanceUILayout.HitTimingFeedback` to project `DeltaMs` and map source rectangles
- replace/restart a lane's state on `Spawn`
- update the short fade lifecycle
- draw each active glyph run at `GetLaneRunPosition(lane, glyphCount)`
- recover from mid-stage texture invalidation using the same once-per-invalidation single-texture retry behavior as `SpriteJudgementTextPopupManager`

It must not read config, inspect AutoPlay state, calculate judgement timing, update counters, or publish telemetry.

Expose only a narrow test seam:

- `CreateForTesting(...)` accepts an injected `ITexture?`, optional `IResourceManager`, and active-state storage only as required by focused tests
- expose an internal active-lane count/read view sufficient for stage routing assertions

### Texture lifetime decision

Do **not** extract a cross-component reload framework in HPA-15.

`SpriteJudgementTextPopupManager` is a single-texture owner with font-fallback migration; `NxAttackEffectManager` is a multi-texture owner with lane arrays and different reload semantics. Refactoring both stable owners while adding HPA-15 would widen risk without changing user-visible scope.

Instead, `HitTimingFeedbackDisplay` should mirror the existing single-texture invalidation behavior locally:

- initial missing/invalid asset => optional feature stays unavailable without font fallback
- held texture becomes disposed/invalid mid-stage => release safely and attempt one reload for that invalidation episode
- successful reload permits future invalidation/retry
- failed reload does not retry every frame
- `Dispose` releases the held reference once

This deliberately accepts a small amount of local duplication to keep HPA-15 bounded while preserving current device-reset behavior.

Do not add a generic popup abstraction, renderer hierarchy, font fallback, or shared texture-lifetime refactor in this PR.

### `PerformanceUILayout.HitTimingFeedback`

This nested owner is the single source for:

- glyph dimensions and 4×3 packing
- FAST/SLOW bank origins
- derived required texture extents
- digit/minus slot mapping (slot 10 unused)
- midpoint-away-from-zero projection
- row-major source rectangles
- full-run lane centering
- vertical placement 34 px below judgement text
- lifetime equal to `SpriteJudgementTextAssets.TotalDurationSeconds`

Do not add skin metadata or a configurable layout model for this fixed atlas contract.

## PerformanceStage integration

- Construct `HitTimingFeedbackDisplay` with the other performance presentation components.
- Update it immediately beside the existing sprite/font judgement-popup updates.
- Draw it inside `DrawJudgementTexts`, beside the current judgement-word draws, in the existing alpha-blended base pass.
- Dispose it with the other performance components.
- Extend `PerformanceVisualGates` with positive `ShowHitTimingFeedback` state.

The all-zero/default `PerformanceVisualGates` value must continue to mean current CX visuals. Adding a positive boolean preserves reflection-created deterministic test behavior.

`FreezeRunConfiguration` copies `Config.ShowHitTimingFeedback` once per run. No live config subscription is required.

Inside `OnJudgementMade`, spawn only when:

```text
_visualGates.ShowHitTimingFeedback
&& e.IsHit()
&& !FrozenAutoPlayLanes.Contains(e.Lane)
```

Keep all existing score/combo/gauge/skill/effect/pad/judgement-word flow unchanged.

Remove the unused `_lagNumbersTexture` field/load/release from `PerformanceStage`; the new display becomes the timing-number texture owner.

### Cleanup test migration

`PerformanceStageDeterministicTests` currently injects `_lagNumbersTexture` and verifies `RemoveReference()` during cleanup. Deleting the stale field must not silently delete resource-ownership coverage.

Retarget that test:

- create `HitTimingFeedbackDisplay` through its test factory with the lag texture mock
- inject it into `_hitTimingFeedbackDisplay`
- invoke `CleanupComponents`
- verify the injected texture reference is released once through display disposal

The standalone display tests own detailed texture invalidation/reload/dispose behavior; the stage cleanup assertion proves lifecycle wiring calls `Dispose`.

## Persistence and config-stage behavior

Follow the boolean pattern already used by `ShowCombo`:

- default in `ConfigData`
- parse only valid booleans
- write the value in `BuildPersistedEntries`
- setter is a no-op when unchanged and otherwise calls `MarkDirty()`
- add a `ToggleConfigItem` in `ConfigStage`

Do not create a dedicated settings object, event, migration, or compatibility parser.

Also extend `ConfigManager_PerLaneAutoPlayMutators_ShouldBeExposedByInterface` with `SetShowHitTimingFeedback`; setter behavior tests alone do not prove the method was added to `IConfigManager`.

## Test strategy

### Generated/shipped asset

Extend `tools/skingen/test_skingen.py` and use Pillow, which CI already installs for skingen tests.

Pin the existing lag atlas contract against:

- generated `tools/skingen/source/7_lag numbers.png`
- committed `System/CXNeon/Graphics/7_lag numbers.png`

For each asset assert:

- authored size is 128×128
- slots `0..9` and `11` contain non-transparent pixels in both banks
- slot `10` is not required by HPA-15 and must not be interpreted as plus
- FAST and SLOW bank regions are both non-empty and visibly distinct
- source rectangles stay within the derived 124×121 runtime bounds

Run `generate_source.py`, `skingen.py compose`, and `skingen.py validate` during implementation so the committed artifact is regenerated from the corrected source and still satisfies the manifest.

The manifest remains the authored dimension/inventory contract; do not invent a second semantic cell schema there just for this asset. Semantic packing is pinned by the generator test and `PerformanceUILayout` tests.

### Config

- `ConfigDataTests`: default Off.
- `ConfigManagerTests`: interface surface includes `SetShowHitTimingFeedback`; setter/no-op behavior mirrors other boolean setters.
- `ConfigManagerSqlitePersistenceTests`: round-trip and missing-key default.
- `ConfigStageLogicTests`: exact Drums ordering and typed setter dispatch.
- Update only the existing hand-written `IConfigManager` test stub required for compilation.

### Layout projection/geometry

Extend `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs`.

Pin:

- FAST/SLOW source rectangles for slots `0`, `9`, and `11`
- 4-column row-major wrap at slots 4 and 8
- derived `RequiredTextureWidth == 124` and `RequiredTextureHeight == 121`
- `-0.4 -> "0"/FAST`, `+0.4 -> "0"/FAST`, `-0.5 -> "-1"/FAST`, `+0.5 -> "1"/SLOW`
- `GetLaneRunPosition(lane, glyphCount)` centers the full glyph run and uses judgement-text Y + 34

Do not add or test a plus slot.

### Presentation

Add focused `HitTimingFeedbackDisplayTests` covering:

- projected negative/positive/zero runs draw the expected glyph sequence supplied by the layout mapper
- out-of-range lanes (`< 0` or `>= LaneCount`) are ignored without throwing
- same-lane hit replaces/restarts
- separate lanes coexist
- expiry at the shared duration
- missing/invalid initial texture is a safe no-op
- a previously valid texture invalidated mid-stage attempts the bounded reload behavior
- dispose releases only the display's held texture reference

No live `GraphicsDevice` harness is needed; use current `ITexture`/resource-manager seams.

### PerformanceStage

Extend `PerformanceStageDeterministicTests`:

- frozen positive toggle behavior
- inject `HitTimingFeedbackDisplay.CreateForTesting(...)`
- enabled manual hit increases active-lane state
- disabled manual hit does not
- AutoPlay lane does not
- `Miss` does not
- mixed AutoPlay permits manual lanes
- existing cleanup test retargets from `_lagNumbersTexture` to the injected display

Existing score/combo/gauge/skill assertions remain regression proof that gameplay rules are unchanged.

Run the whole performance namespace because `OnJudgementMade` is exercised by additional coverage.

## Risks and mitigations

### Release asset differs from development reference

**Risk:** local testing against repo-root `System/Graphics` can look correct while release ships `System/CXNeon` with incompatible packing.

**Mitigation:** Task 0 corrects the generated CX Neon asset first and skingen tests inspect the committed release artifact directly.

### Atlas semantics cannot be proven by dimensions alone

**Risk:** a 128×128 image can still have the wrong cell layout.

**Mitigation:** semantic cell occupancy/bank tests in `tools/skingen/test_skingen.py`, plus pure source-rectangle tests in `PerformanceUILayoutMoreTests`.

### Graphics-device invalidation

**Risk:** a timing-number texture can become invalid mid-stage after a device reset/alt-tab path.

**Mitigation:** mirror the established single-texture retry behavior rather than leaving timing feedback permanently dead; keep the cross-component refactor out of HPA-15.

## Manual smoke

With a simple chart and AutoPlay Off:

1. Confirm default Off produces current visuals with no timing number.
2. Enable `Hit Timing Feedback`, start a new run, and deliberately hit early and late.
3. Confirm early values are cyan/FAST and include `-`; late values are danger/red/SLOW and are unsigned.
4. Confirm near-zero values that round to zero show unsigned `0`.
5. Confirm rapid same-lane hits replace the prior timing value even though judgement words may overlap independently.
6. Confirm multi-glyph values are centered on the lane.
7. Enable AutoPlay for one lane and confirm only manual lanes emit timing feedback.
8. Restart and confirm the toggle reloads from SQLite.
9. Exercise the normal alt-tab/fullscreen/device-reset smoke path and confirm timing feedback recovers alongside judgement presentation.

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
- no new asset path or hand-authored art
- no cross-component texture-reload refactor
