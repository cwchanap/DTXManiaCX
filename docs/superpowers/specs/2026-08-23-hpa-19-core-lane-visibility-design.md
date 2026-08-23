# HPA-19 Core Drums Lane Visibility — Design

## Goal

Add the first concrete Drums visual-configuration slice that DTXManiaCX can support with its current 10-lane performance renderer: lane/background visibility, measure-line visibility, judgement-line visibility, lane hit flashes, and combo visibility.

This remains one HPA-19 PR and should fit within 2–3 engineer days. Defaults must preserve the visuals users see today.

## Why HPA-19 is being narrowed

The original HPA-19 bundled roughly eighteen DTXManiaNX display modifiers. That scope no longer matches CX:

- CX currently has one 10-lane Drums performance path, not separate Drums/Guitar/Bass gameplay renderers.
- Scroll Speed is already implemented as a Drums setting with a 50–400% range and live PageUp/PageDown updates during performance.
- Several legacy options (HID-SUD, Reverse, lane layouts, shutter controls, graph placement, etc.) do not have an owning CX rendering model yet.
- Implementing those options now would require speculative abstractions or dormant configuration that cannot affect gameplay.

HPA-19 therefore covers only the four controls whose rendering seams already exist. The remaining legacy candidates are explicitly deferred rather than stubbed.

## Legacy behavior to preserve

### Lane display

DTXManiaNX exposes `LaneDisp` as four states. CX should keep the same user-facing choices and behavior:

| Setting | Lane background | Measure/bar lines |
| --- | --- | --- |
| `ALL ON` | Show | Show |
| `LANE OFF` | Hide | Show |
| `LINE OFF` | Show | Hide |
| `ALL OFF` | Hide | Hide |

This setting does not hide notes, pads, judgement text, or the judgement line.

### Other controls

- `JudgeLineDisp`: show/hide the judgement line.
- `LaneFlush`: enable/disable the short per-lane flash produced by successful hits.
- `Combo`: show/hide the combo number while keeping combo calculation unchanged.

NX `Dark` is not an independent persisted source of truth. It is a UI preset that rewrites `LaneDisp`, `JudgeLineDisp`, and `LaneFlush`. Dark is intentionally deferred until the underlying controls exist and a later ticket decides whether the preset is still useful in CX.

## Configuration model

Keep the data model explicit and small. Add one enum plus three booleans to the existing config model:

- `DrumsLaneDisplayMode LaneDisplayMode`, default `AllOn`
- `bool ShowJudgementLine`, default `true`
- `bool EnableLaneFlush`, default `true`
- `bool ShowCombo`, default `true`

`DrumsLaneDisplayMode` uses explicit values for stable ordering only:

- `AllOn = 0`
- `LaneOff = 1`
- `LineOff = 2`
- `AllOff = 3`

The 2×2 lane-display matrix is part of the production contract, not test-only knowledge. Put two small helpers beside the enum:

```csharp
public static bool ShowsLaneBackground(this DrumsLaneDisplayMode mode) =>
    mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LineOff;

public static bool ShowsMeasureLines(this DrumsLaneDisplayMode mode) =>
    mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LaneOff;
```

These helpers are intentionally not a visual-modifier abstraction. They are the single definition of what the four enum states mean and are reusable by the activation snapshot, tests, and any later `Dark` preset work.

Persist `LaneDisplayMode` the same way CX already persists `GaugeDamageLevel`: store the enum name (`LaneOff`), not its integer discriminator and not the user-facing label (`LANE OFF`). Loading matches enum names case-insensitively. Missing or unrecognized rows leave the `ConfigData` default `AllOn` unchanged.

`SetLaneDisplayMode` should mirror `SetDamageLevel`: normalize undefined enum values to `AllOn`, no-op when unchanged, otherwise update `ConfigData` and mark the deferred save dirty. The three boolean setters follow the existing `SetMetronome` pattern. No event bus or generic visual-settings object is needed because these values do not mutate live during a performance.

Missing database rows naturally use the defaults in `ConfigData`; no schema migration or compatibility layer is required.

## Config-stage UX

Expose four items in the existing Drums config list immediately after Scroll Speed:

1. `Lane Display` — dropdown: `ALL ON`, `LANE OFF`, `LINE OFF`, `ALL OFF`
2. `Judge Line` — On/Off
3. `Lane Flush` — On/Off
4. `Combo` — On/Off

Use the existing `DropdownConfigItem` / `ToggleConfigItem` patterns and `ConfigManager` setters. Map the four display labels explicitly to/from `DrumsLaneDisplayMode`; do not use the display labels as persisted values. Do not add a sub-page or generic visual-modifier editor for four values.

These settings apply to the next performance activation. The existing Scroll Speed behavior is intentionally unchanged: it remains independently adjustable in Config and live during a performance.

## Performance-stage integration

`PerformanceStage` owns the feature gates because it already decides between skin-texture rendering and fallback renderers. Putting visibility only on leaf renderers would miss the direct textured paths.

At activation, snapshot the visual contract into stage-local state next to the other frozen performance configuration:

- lane background visibility from `LaneDisplayMode.ShowsLaneBackground()`
- measure-line visibility from `LaneDisplayMode.ShowsMeasureLines()`
- judgement-line visibility from `ShowJudgementLine`
- lane-flush enablement from `EnableLaneFlush`
- combo visibility from `ShowCombo`

This keeps a run deterministic and makes the “next performance” application rule explicit.

### Lane background and measure lines

Gate the existing orchestration points with the snapped booleans:

- `DrawLaneBackgrounds`: return before choosing the skin texture vs. `LaneBackgroundRenderer` fallback when lane backgrounds are hidden.
- `DrawMeasureLines`: return before calling `NoteRenderer.DrawMeasureLines` when measure lines are hidden.

Notes, pads, judgement text, and the judgement line are unaffected by `Lane Display`.

### Judgement line

Guard `DrawJudgementLine` before choosing the textured hit-bar or `JudgementLineRenderer` fallback. This guarantees `ShowJudgementLine=false` applies identically to both paths.

### Lane flush

`NoteRenderer` already owns the lane-flash state, decay, drawing, and `TriggerLaneFlash` API. The missing piece is only gameplay wiring.

`PerformanceStage.OnJudgementMade` already has one `if (e.IsHit())` block for successful-hit visual feedback. Inside that existing block, and only there, call `NoteRenderer.TriggerLaneFlash(e.Lane)` when the snapped lane-flush flag is enabled. Do not add a second `Type != Miss` predicate.

Auto-play already resolves through `JudgementManager.ResolveAutoHit`, which raises the same `JudgementMade` event. Do not add a direct lane-flash call to `ProcessAutoPlay`; that would double-trigger the same visual path. Do not revive `_laneFlashTexture` or add another effect manager.

No changes to the flash animation itself are in scope.

### Combo

Keep `OnComboChanged`, `ComboDisplay.Combo`, `ComboManager`, scoring, and result data unchanged. Only guard the existing `_comboDisplay?.Draw(...)` call with the snapped combo-visibility flag. Hidden combo remains fully calculated and updated.

## Scroll Speed decision

Do not modify Scroll Speed in HPA-19.

The original ticket asked for separate instrument values and the old NX numeric range. That premise is stale for CX: the current game has only a 10-lane Drums performance path, and the existing shared 50–400% setting is already wired to Config, persistence, the note renderer, the indicator, and live hotkeys. Splitting it now would create unused Guitar/Bass configuration and unnecessary migration work.

## Deferred legacy candidates

The following original HPA-19 items are not part of this PR: HID-SUD, Dark, AttackEffect variants, Reverse, JudgePosition, LaneType, NumOfLanes, JudgeLinePos, ShutterIn, ShutterOut, HHOGraphics, LBDGraphics, RDPosition, and Graph.

Do not add placeholder enum values, config rows, UI entries, renderer interfaces, or compatibility code for them. Create focused follow-up work only when the feature is still desired and its renderer/model exists.

Legacy audit corrections worth retaining for later work:

- NX HID-SUD also has `Stealth`; it is not only Off/Hidden/Sudden/HidSud.
- NX `NumOfLanes` is `10 / 9 / 6`, not `8 / 9 / 10`.

## Test strategy

Keep tests on the existing owners; no new graphics harness, screenshot tests, or E2E flow is required.

- `DTXMania.Test/Config/DrumsLaneDisplayModeTests.cs`: theory-test all four rows of the lane-background / measure-line matrix without a `GraphicsDevice`.
- `DTXMania.Test/Config/ConfigDataTests.cs`: defaults preserve current visuals.
- `DTXMania.Test/Config/ConfigManagerTests.cs`: `SetLaneDisplayMode` normalization/no-op behavior and the simple boolean setters, following existing setter tests.
- `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`: all four settings round-trip through SQLite; `LaneDisplayMode` is persisted by enum name; missing/malformed values keep defaults.
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`: extend `SetupConfigItems_ShouldBuildSystemDrumsExitCategories` and related Config-stage logic tests for the four Drums items, dropdown labels/order, current value, and setter dispatch.
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`: no menu behavior belongs here; only update its `StubConfigManager` with the new interface methods so the existing drum-key-assign tests still compile.
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`: extend the existing stage-level draw and judgement tests. Rewrite the current successful-hit characterization that explicitly expects no lane flash into the enabled-default contract, then add disabled-flush coverage beside the existing Miss coverage. Add disabled draw-gate assertions next to the existing `DrawLaneBackgrounds`, `DrawMeasureLines`, `DrawJudgementLine`, and `DrawUIElements` tests.
- `DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs`: existing `TriggerLaneFlash` and decay tests remain the leaf-renderer contract; no change is expected.

Do not add HPA-19 coverage to `PerformanceRendererStateTests` or `PerformanceStageJudgementIntegrationTests`: those suites own leaf renderers and `JudgementManager` behavior respectively, not `PerformanceStage` orchestration.

Existing Scroll Speed tests remain unchanged and serve as regression coverage for the decision not to alter that feature.

## Manual smoke check

After automated tests, run one simple chart and verify:

- each `Lane Display` state matches the 2×2 table
- judgement line toggles independently
- lane flashes toggle without affecting pad-hit effects or attack effects
- combo visibility toggles while score/combo behavior still progresses
- Scroll Speed hotkeys still work during performance
- restart once and confirm the four settings reload from SQLite

## Non-goals

- No new renderer abstraction or “visual modifier” service.
- No generic preset system.
- No live config-event plumbing for these four controls.
- No NX config import/export changes.
- No new textures or skin contract changes.
- No gameplay/scoring changes.
- No implementation of deferred HPA-19 legacy options.
