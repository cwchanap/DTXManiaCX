# HPA-19 Core Drums Lane Visibility — Design

## Goal

Add the first concrete Drums visual-configuration slice that DTXManiaCX can support with its current 10-lane performance renderer: lane/background visibility, measure-line visibility, judgement-line visibility, optional lane hit flashes, and combo visibility.

This remains one HPA-19 PR and should fit within 2–3 engineer days.

## Scope

HPA-19 covers only the four controls whose rendering/judgement seams already exist:

- `Lane Display`
- `Judge Line`
- `Lane Flush`
- `Combo`

Keep the current Drums-only 50–400% Scroll Speed behavior unchanged, including live PageUp/PageDown updates during performance.

Do not implement or stub HID-SUD, Dark, AttackEffect variants, Reverse, JudgePosition, LaneType, NumOfLanes, JudgeLinePos, ShutterIn/Out, HHOGraphics, LBDGraphics, RDPosition, or Graph. Do not add a generic visual-modifier service, preset framework, renderer hierarchy, config event bus, compatibility layer, GraphicsDevice harness, or E2E flow.

## Legacy behavior to preserve

### Lane display

Keep the NX four-state contract:

| Setting | Lane background | Measure/bar lines |
| --- | --- | --- |
| `ALL ON` | Show | Show |
| `LANE OFF` | Hide | Show |
| `LINE OFF` | Show | Hide |
| `ALL OFF` | Hide | Hide |

This setting does not hide notes, pads, judgement text, or the judgement line.

### Other controls

- `JudgeLineDisp`: show/hide the judgement line.
- `LaneFlush`: enable/disable lane-hit flash feedback.
- `Combo`: show/hide the combo number while keeping combo calculation unchanged.

NX `Dark` is intentionally deferred. NX treats it as a UI preset over `LaneDisp`, `JudgeLineDisp`, and `LaneFlush`, not as independently persisted source of truth.

## Current-visual default contract

The current CX production path never calls `NoteRenderer.TriggerLaneFlash`. The existing flash renderer draws a full-height white lane quad and therefore is not part of the visuals users see today.

Defaults for HPA-19 are therefore:

- `LaneDisplayMode = AllOn`
- `ShowJudgementLine = true`
- `EnableLaneFlush = false`
- `ShowCombo = true`

This preserves current CX behavior by default. Manual smoke must explicitly enable Lane Flush and verify that the existing flash reads as useful hit feedback and does not materially obscure fallback notes. Do not automatically change the default to ON. A human review may approve that flip before merge if the smoke result is clearly acceptable; otherwise keep OFF without redesigning the flash in HPA-19.

## Configuration model

Add one enum plus three booleans:

- `DrumsLaneDisplayMode LaneDisplayMode`
- `bool ShowJudgementLine`
- `bool EnableLaneFlush`
- `bool ShowCombo`

`DrumsLaneDisplayMode` uses explicit numeric values for stable ordering only:

- `AllOn = 0`
- `LaneOff = 1`
- `LineOff = 2`
- `AllOff = 3`

The 2×2 matrix is production logic, not test-only knowledge. Put two small helpers beside the enum:

- `ShowsLaneBackground()`
- `ShowsMeasureLines()`

They are the single definition of the four enum states.

### One source for UI labels

`DropdownConfigItem` is string-based and silently falls back to option index 0 when its current-value string is absent. Avoid separate enum→label and label→enum switch expressions.

Put one ordered `(Mode, Label)` table beside `DrumsLaneDisplayMode` with exactly:

1. `AllOn` / `ALL ON`
2. `LaneOff` / `LANE OFF`
3. `LineOff` / `LINE OFF`
4. `AllOff` / `ALL OFF`

Derive the dropdown option list and both lookup directions from that table. Theory-test all four label → mode → label round trips.

This table is UI metadata only. Persistence stores enum names, never the display labels.

## Persistence

Persist `LaneDisplayMode` exactly like `GaugeDamageLevel`:

- save `Config.LaneDisplayMode.ToString()` such as `LaneOff`
- load by case-insensitive enum-name match
- missing or unrecognized values leave the `ConfigData` default `AllOn`
- `SetLaneDisplayMode` mirrors `SetDamageLevel`, including normalizing undefined values to `AllOn`
- the three boolean setters mirror the changed/no-op/`MarkDirty` pattern used by `SetMetronome`

No schema migration or numeric enum parser is required.

## Config-stage UX

Add the four items immediately after Scroll Speed in the existing Drums list:

1. `Lane Display`
2. `Judge Line`
3. `Lane Flush`
4. `Combo`

Use the existing `DropdownConfigItem` and `ToggleConfigItem` types. Do not add a new category or screen.

Config edits apply to the next performance activation. Scroll Speed remains the only setting in this slice with existing live-performance updates.

## Freeze run configuration in one place

`PerformanceStage.InitializeAutoPlay` already freezes AutoPlay lanes plus `AutoAddGauge`, `DamageLevel`, `Risky`, and `NoFail`. That method is no longer AutoPlay-specific.

Rename it to `FreezeRunConfiguration` and keep one production call from `OnActivate`. Update the existing reflection-based tests to the new name rather than adding a second freeze path.

The new visual settings join this same run snapshot.

### Reflection-created stage compatibility

`PerformanceStageDeterministicTests.CreateStage()` uses `FormatterServices.GetUninitializedObject`, so field initializers do not run. Positive `showX = true` fields would therefore default to false in many existing tests and incorrectly hide existing draw paths.

Group HPA-19 run-local visual state in one private readonly record struct, `PerformanceVisualGates`, using zero-safe semantics:

- suppression/hide flags for lane backgrounds, measure lines, judgement line, and combo
- positive `EnableLaneFlush`

The all-zero/default struct must mean current CX behavior: draw all four existing paths and do not trigger lane flash. This avoids an extra `IsFrozen` sentinel and preserves existing reflection-created test behavior without teaching old tests about the new feature.

`FreezeRunConfiguration` derives and assigns this record once from the current config.

## Performance-stage integration

`PerformanceStage` owns the gates because it chooses between textured and fallback rendering inside the stage.

### Lane background and measure lines

- `DrawLaneBackgrounds`: return before choosing skin texture vs. fallback when the snapped gate suppresses lane backgrounds.
- `DrawMeasureLines`: return before `NoteRenderer.DrawMeasureLines` when suppressed.

### Judgement line

`DrawJudgementLine` returns before choosing textured hit-bar vs. fallback renderer when suppressed.

### Combo

Guard only the existing `_comboDisplay?.Draw(...)` call. Keep `OnComboChanged`, `ComboManager`, scoring, gauge/result logic, and component updates unchanged.

### Lane flush

`NoteRenderer` already owns flash state, decay, drawing, and `TriggerLaneFlash`.

Inside the existing `PerformanceStage.OnJudgementMade` `if (e.IsHit())` block, call `TriggerLaneFlash(e.Lane)` only when the snapped gate enables Lane Flush.

Do not add another `Type != Miss` predicate. Do not add a second trigger to `ProcessAutoPlay`: `ResolveAutoHit` already raises the same `JudgementMade` event. Do not revive `_laneFlashTexture` or redesign the effect in this ticket.

## Test strategy

Keep tests on existing owners.

### Config

- `DrumsLaneDisplayModeTests`: four-row lane/measure matrix and four-row label round trip.
- `ConfigDataTests`: defaults including `EnableLaneFlush=false`.
- `ConfigManagerTests`: enum normalization/no-op and boolean setter behavior.
- `ConfigManagerSqlitePersistenceTests`: enum-name round trip plus missing/malformed fallback.
- `ConfigStageLogicTests`: exact Drums item order, labels, current value, and setter dispatch.
- `DrumConfigStageTests`: only extend `StubConfigManager` for the new interface methods.

### PerformanceStage

Extend `PerformanceStageDeterministicTests` rather than creating a sibling harness.

- update `InitializeAutoPlay` reflection tests to `FreezeRunConfiguration` and extend the run-freeze characterization with visual gates
- preserve existing enabled draw tests without setting new private fields; the zero/default gate state must keep them green
- disabled lane-background and judgement-line tests use the existing texture mocks and assert `Times.Never`
- measure-line and combo disabled tests assert an observable existing collaborator/draw seam rather than only “no exception”
- existing fallback-renderer no-throw tests remain structural coverage; because the fallback renderers are concrete and expose no spy seam, do not claim a separately observed disabled fallback call
- change the existing successful-judgement characterization from “without lane flash” to Lane Flush enabled, and add Lane Flush disabled plus Miss cases

`PerformanceStageAdditionalCoverageTests` also invokes `OnJudgementMade`. Task-level verification therefore runs the whole `DTXMania.Test.Stage.Performance` namespace, not just `PerformanceStageDeterministicTests`.

`NoteRendererLogicTests` remains the leaf-renderer flash/decay contract unless the renderer itself changes, which is out of scope.

## Manual smoke

Using a simple chart:

- verify all four Lane Display combinations
- verify Judge Line independently
- enable Lane Flush and confirm it reads as hit feedback and does not materially white-out fallback notes
- verify disabling Lane Flush restores current no-flash behavior
- verify Combo hides only the display while combo/scoring still progress
- verify Scroll Speed hotkeys remain unchanged
- restart and verify all four settings reload from SQLite

Lane Flush remains OFF by default unless the human smoke review explicitly approves changing that default in this PR.

## Deferred legacy candidates

Do not pre-model HID-SUD, Dark, AttackEffect variants, Reverse, JudgePosition, LaneType, NumOfLanes, JudgeLinePos, shutters, HHOGraphics, LBDGraphics, RDPosition, or Graph.

Legacy audit corrections retained for later work:

- NX HID-SUD includes `Stealth`.
- NX `NumOfLanes` is `10 / 9 / 6`, not `8 / 9 / 10`.

## Non-goals

- no generic visual modifier abstraction or preset system
- no live config-event plumbing for these controls
- no new graphics test harness or screenshot/E2E test
- no NX config import/export change
- no new textures or flash redesign
- no scoring/gameplay changes
- no implementation of deferred legacy modifiers
