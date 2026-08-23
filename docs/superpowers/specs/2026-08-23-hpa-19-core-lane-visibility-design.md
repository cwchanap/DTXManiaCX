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

`DrumsLaneDisplayMode` should use the four values `AllOn`, `LaneOff`, `LineOff`, and `AllOff`. Assign stable numeric values in that order so the enum is simple to persist and reason about.

Persist the values through the existing `ConfigManager` SQLite key/value path. Add focused setter methods on `IConfigManager` / `ConfigManager`, following existing settings such as Metronome and Scroll Speed. No event bus or generic visual-settings object is needed because these values do not need live mutation during gameplay.

Missing database rows naturally use the defaults in `ConfigData`; no schema migration or compatibility layer is required.

## Config-stage UX

Expose four items in the existing Drums config list near Scroll Speed:

1. `Lane Display` — dropdown: `ALL ON`, `LANE OFF`, `LINE OFF`, `ALL OFF`
2. `Judge Line` — On/Off
3. `Lane Flush` — On/Off
4. `Combo` — On/Off

Use the existing `DropdownConfigItem` / `ToggleConfigItem` patterns and `ConfigManager` setters. Do not add a sub-page or generic visual-modifier editor for four values.

These settings apply to the next performance activation. The existing Scroll Speed behavior is intentionally unchanged: it remains independently adjustable in Config and live during a performance.

## Performance-stage integration

`PerformanceStage` should own the feature gating because it already decides between skin-texture rendering and fallback renderers. Putting visibility only on leaf renderers would miss the direct textured paths.

At activation, snapshot the four visual settings into stage-local state along with the other performance configuration. This keeps a run deterministic and makes the “next performance” application rule explicit.

### Lane background and measure lines

Derive two booleans from `LaneDisplayMode`:

- show lane background for `AllOn` and `LineOff`
- show measure lines for `AllOn` and `LaneOff`

Use those booleans at the existing orchestration points:

- gate the full lane-background draw path before choosing skin texture vs. `LaneBackgroundRenderer` fallback
- gate the call to `NoteRenderer.DrawMeasureLines`

Keep this mapping local to the performance/config feature. Do not introduce a reusable modifier framework.

### Judgement line

Guard the existing judgement-line draw path in `PerformanceStage` before choosing the textured hit-bar or `JudgementLineRenderer` fallback. This guarantees `ShowJudgementLine=false` works for both paths.

### Lane flush

`NoteRenderer` already owns the lane-flash state, decay, and drawing, and already exposes `TriggerLaneFlash`. The missing piece is gameplay wiring.

On a successful judgement, call `NoteRenderer.TriggerLaneFlash` for the judged lane only when `EnableLaneFlush` is true. Misses must not flash. Let the existing judgement event path cover both player hits and auto-play resolution; do not add a second auto-play-specific flash path or revive the unused lane-flash texture as a separate effect system.

No changes to the flash animation itself are in scope.

### Combo

Keep combo calculation and `ComboDisplay.Combo` updates unchanged. Only guard the existing combo draw call when `ShowCombo` is false. Do not alter `ComboManager`, scoring, result data, or the component’s internal “combo > 0” visibility behavior.

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

Keep tests at existing seams; no new graphics harness, screenshot tests, or E2E flow is required.

- `DTXMania.Test/Config/ConfigDataTests.cs`: defaults preserve current visuals.
- `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`: all four settings round-trip through SQLite; invalid/missing values fall back to defaults using existing ConfigManager conventions.
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`: Drums menu contains the four controls, dropdown ordering matches the NX four-state contract, and edits call the config setters.
- `DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs` or a small focused `PerformanceStageVisualConfigTests.cs`: pin the four-state lane/measure mapping and the stage draw gates without requiring a real `GraphicsDevice`.
- `DTXMania.Test/Stage/Performance/PerformanceStageJudgementIntegrationTests.cs`: pin that successful judgements can produce lane flush when enabled and misses/disabled mode do not. Reuse existing judgement/test helpers rather than inventing a new fake gameplay stack.
- `DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs`: existing `TriggerLaneFlash`/decay tests remain the animation contract; add tests here only if implementation changes that contract, which is not expected.

Existing Scroll Speed tests remain unchanged and serve as regression coverage for the decision not to alter that feature.

## Manual smoke check

After automated tests, run one simple chart and verify:

- each `Lane Display` state matches the 2×2 table
- judgement line toggles independently
- lane flashes toggle without affecting pad-hit effects or attack effects
- combo visibility toggles while score/combo behavior still progresses
- Scroll Speed hotkeys still work during performance

## Non-goals

- No new renderer abstraction or “visual modifier” service.
- No generic preset system.
- No live config-event plumbing for these four controls.
- No NX config import/export changes.
- No new textures or skin contract changes.
- No gameplay/scoring changes.
- No implementation of deferred HPA-19 legacy options.
