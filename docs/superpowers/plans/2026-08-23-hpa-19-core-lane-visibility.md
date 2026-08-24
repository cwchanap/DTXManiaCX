# HPA-19 Core Drums Lane Visibility — Implementation Plan

## Objective

Implement `Lane Display`, `Judge Line`, `Lane Flush`, and `Combo` as persisted Drums settings, then apply them through existing `PerformanceStage` rendering/judgement seams.

Keep all implementation on this HPA-19 branch/PR. Target <=3 engineer days.

Design reference: `docs/superpowers/specs/2026-08-23-hpa-19-core-lane-visibility-design.md`.

## Scope guardrails

- Keep current Drums-only 50–400% Scroll Speed and live hotkeys unchanged.
- Defaults preserve current production behavior: `AllOn`, Judge Line ON, Combo ON, Lane Flush OFF.
- Do not implement or stub HID-SUD, Dark, AttackEffect variants, Reverse, JudgePosition, LaneType, NumOfLanes, JudgeLinePos, shutters, HHOGraphics, LBDGraphics, RDPosition, or Graph.
- No visual-modifier service, preset framework, renderer hierarchy, live config events, compatibility layer, new GraphicsDevice harness, or E2E test.
- Visual config edits apply on the next performance run.
- Sync/rebase onto current `main` before production implementation.

## Task 1 — Add config contract, lane matrix, labels, and SQLite persistence

### Files

- Add `DTXMania.Game/Lib/Config/DrumsLaneDisplayMode.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Add `DTXMania.Test/Config/DrumsLaneDisplayModeTests.cs`
- Modify `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- Modify only the `StubConfigManager` in `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` for interface compilation

### Tests first

Pin:

| Mode | Lane background | Measure lines | Label |
| --- | --- | --- | --- |
| `AllOn` | true | true | `ALL ON` |
| `LaneOff` | false | true | `LANE OFF` |
| `LineOff` | true | false | `LINE OFF` |
| `AllOff` | false | false | `ALL OFF` |

The enum test should verify both show-helper results and label → mode → label round trip for every row.

Also cover:

- defaults: `AllOn`, Judge Line true, Lane Flush false, Combo true
- `SetLaneDisplayMode` follows `SetDamageLevel`: undefined → `AllOn`, no-op when unchanged, dirty when changed
- three boolean setters follow changed/no-op `SetMetronome` behavior
- all settings survive SQLite save/reload
- enum persists by name (`LaneOff`), not `1` or `LANE OFF`
- missing/unrecognized enum rows retain `AllOn`

### Implementation

Add `DrumsLaneDisplayMode` with explicit values `AllOn = 0`, `LaneOff = 1`, `LineOff = 2`, `AllOff = 3`.

In the same config area, add:

- `ShowsLaneBackground()`
- `ShowsMeasureLines()`
- one ordered `(Mode, Label)` table containing the four NX labels
- lookup helpers/projections derived from that one table for dropdown labels and both conversion directions

Do not implement two independent switch maps.

Add `ConfigData` fields:

- `LaneDisplayMode = AllOn`
- `ShowJudgementLine = true`
- `EnableLaneFlush = false`
- `ShowCombo = true`

Persistence and setters:

- parse `LaneDisplayMode` like `DamageLevel`, by case-insensitive enum name
- save `Config.LaneDisplayMode.ToString()`
- `SetLaneDisplayMode` mirrors `SetDamageLevel`
- boolean setters mirror `SetMetronome`
- add methods to `IConfigManager`
- extend the existing test-only `StubConfigManager`; no HPA-19 UI behavior tests belong in `DrumConfigStageTests`

### Verify

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumsLaneDisplayModeTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigDataTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerSqlitePersistenceTests"
```

Use `DTXMania.Test/DTXMania.Test.csproj` on Windows.

## Task 2 — Expose the four settings in the existing Drums Config list

### Files

- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify `DTXMania.Test/Config/ConfigStageLogicTests.cs`

### Tests first

Extend `SetupConfigItems_ShouldBuildSystemDrumsExitCategories` so the four entries appear immediately after `Scroll Speed`:

1. `Lane Display`
2. `Judge Line`
3. `Lane Flush`
4. `Combo`

Add focused Config-stage tests for:

- exact Lane Display label order from the single option table
- each enum value displays its matching label
- left/right/toggle dispatch maps the selected label to the correct enum setter value
- Judge Line, Lane Flush, and Combo dispatch to their setters
- existing Scroll Speed range/formatter/behavior is unchanged

### Implementation

Add one `DropdownConfigItem` and three `ToggleConfigItem`s in `ConfigStage.SetupConfigItems`.

The dropdown must consume labels and conversions from the single ordered option table. Persisted enum names remain independent of presentation labels.

### Verify

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStageLogicTests"
```

Use the Windows test project on Windows.

## Task 3 — Freeze and apply visual gates in PerformanceStage

### Files

- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Do not edit `PerformanceStageAdditionalCoverageTests.cs` unless the existing tests genuinely need adaptation; include them in verification regardless

### Tests first

#### Run-freeze seam

Rename existing `InitializeAutoPlay` tests/reflection calls to `FreezeRunConfiguration` and extend the existing “freeze all run values together” characterization with visual config.

The test must prove a later config mutation does not alter the snapped run gates.

#### Zero-state compatibility

Existing deterministic `CreateStage()` uses `FormatterServices.GetUninitializedObject`. Do not modify old enabled draw tests merely to set HPA-19 private state.

The default/uninitialized `PerformanceVisualGates` value must retain current behavior:

- lane background drawn
- measure lines drawn
- judgement line drawn
- combo draw path available
- lane flush not triggered

#### Disabled draw gates

Use observable seams:

- lane background disabled: existing lane texture mock gets `Times.Never`
- judgement line disabled: existing judgement-line texture mock gets `Times.Never`
- measure lines disabled: assert the existing observable `NoteRenderer`/draw path is not used; do not settle for only “no exception”
- combo disabled: assert only combo drawing is suppressed while `OnComboChanged` still updates combo state

Keep the current concrete fallback-renderer no-throw tests. Because those classes expose no spy/interface seam, do not claim a separately observed disabled fallback invocation. The early return before the textured/fallback branch is the production structure that makes one gate govern both.

#### Lane Flush

Update the current successful-hit characterization so Lane Flush is explicitly enabled in the snapped gates and the judged lane reaches `1.0f` flash alpha while existing attack/pad/popup/manager assertions still pass.

Add:

- successful hit with Lane Flush disabled → no flash
- Miss → no flash

Do not add an AutoPlay-specific flash test path unless needed to prove a regression: `ResolveAutoHit` already raises the same `JudgementMade` event.

### Implementation

Rename `PerformanceStage.InitializeAutoPlay` to `FreezeRunConfiguration`. Keep its current production call from `OnActivate`; update the existing debug/test references accordingly.

Keep AutoPlay lanes and existing frozen gauge/fail rules in this same method, and add the visual snapshot there.

Add one private readonly record struct `PerformanceVisualGates` using zero-safe semantics:

- hide/suppress lane background
- hide/suppress measure lines
- hide/suppress judgement line
- hide/suppress combo
- positive `EnableLaneFlush`

Do not add a separate `IsFrozen` field. The all-zero/default record must mean current CX behavior: all existing draw paths visible and no lane flash.

Freeze it from config:

- hide lane background = inverse of `LaneDisplayMode.ShowsLaneBackground()`
- hide measure lines = inverse of `LaneDisplayMode.ShowsMeasureLines()`
- hide judgement line = inverse of `ShowJudgementLine`
- hide combo = inverse of `ShowCombo`
- enable lane flush = `EnableLaneFlush`

Then gate the existing stage orchestration methods before their existing draw branches.

For lane flush, trigger only inside the existing `if (e.IsHit())` block and only when the snapped gate enables it. Do not add a second predicate or a `ProcessAutoPlay` trigger. Leave `_laneFlashTexture` unused.

### Verify

Run the whole performance-stage namespace so shared-method callers outside the deterministic class are covered:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXMania.Test.Stage.Performance"
```

Use the Windows test project on Windows.

## Task 4 — Full regression verification and manual smoke

### Automated

macOS:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Windows:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

No new E2E test is required.

### Manual smoke

Using a simple chart:

1. Verify all four Lane Display combinations.
2. Toggle Judge Line independently.
3. Confirm default Lane Flush is OFF and matches current CX behavior.
4. Enable Lane Flush and verify the existing full-lane flash reads as hit feedback and does not materially white-out fallback notes on a representative dense section.
5. Disable Lane Flush again and confirm no flash while pad/attack effects remain unchanged.
6. Hide Combo and confirm combo/scoring still progress.
7. Verify PageUp/PageDown Scroll Speed behavior is unchanged.
8. Restart and confirm all four settings reload from SQLite.

Do not automatically flip the Lane Flush default. If the human smoke review explicitly approves ON as the default, make that one default/test/doc adjustment in this same PR; otherwise ship OFF. Do not redesign the flash in HPA-19.

## Completion criteria

HPA-19 is ready to merge when:

- the four settings persist and are editable in the existing Drums Config list
- enum matrix and label mappings have one tested source of truth
- LaneDisplayMode persists by enum name; invalid/missing values retain `AllOn`
- default config preserves current production behavior, including Lane Flush OFF unless explicitly smoke-approved otherwise
- the zero/default `PerformanceVisualGates` state preserves reflection-created existing test behavior
- textured lane/judgement disabled gates are asserted with `Times.Never`; fallback branches remain structurally governed by the same early return without false claims of a spy seam
- Lane Flush uses `OnJudgementMade` → `e.IsHit()` → `TriggerLaneFlash` exactly once when enabled, and never for Miss
- Combo visibility changes drawing only
- Scroll Speed remains untouched
- deferred modifiers have no placeholder production code
- focused tests, whole Stage.Performance tests, full platform Game tests, build, and manual smoke pass

Keep implementation on this same HPA-19 branch/PR.
