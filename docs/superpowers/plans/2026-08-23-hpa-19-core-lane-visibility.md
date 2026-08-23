# HPA-19 Core Drums Lane Visibility — Implementation Plan

## Objective

Implement `Lane Display`, `Judge Line`, `Lane Flush`, and `Combo` as persisted Drums settings, then apply them through the existing `PerformanceStage` rendering/judgement seams.

This work stays in the existing HPA-19 branch and pull request. Do not split implementation into additional PRs.

Design reference: `docs/superpowers/specs/2026-08-23-hpa-19-core-lane-visibility-design.md`.

## Scope guardrails

Keep these decisions fixed:

- Preserve the current Drums-only 50–400% Scroll Speed implementation and live hotkeys.
- Defaults reproduce current CX visuals: `AllOn`, judgement line on, lane flush on, combo on.
- Do not implement or stub HID-SUD, Dark, AttackEffect variants, Reverse, JudgePosition, LaneType, NumOfLanes, JudgeLinePos, shutters, HHOGraphics, LBDGraphics, RDPosition, or Graph.
- Do not add a visual-modifier service, renderer hierarchy, preset framework, live config events, new GraphicsDevice harness, or E2E test.
- Config-stage edits to these four settings take effect on the next performance activation.

Expected effort remains within 2–3 engineer days.

## Task 1 — Add the config contract, lane matrix, and SQLite persistence

### Files

- Add `DTXMania.Game/Lib/Config/DrumsLaneDisplayMode.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Add `DTXMania.Test/Config/DrumsLaneDisplayModeTests.cs`
- Modify `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- Modify only the `StubConfigManager` in `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` so the new `IConfigManager` methods compile; do not add HPA-19 behavior tests there

### Tests first

Pin the reusable lane-display contract with a four-row theory:

| Mode | `ShowsLaneBackground()` | `ShowsMeasureLines()` |
| --- | --- | --- |
| `AllOn` | true | true |
| `LaneOff` | false | true |
| `LineOff` | true | false |
| `AllOff` | false | false |

Then cover:

- `ConfigData` defaults: `AllOn`, judgement line on, lane flush on, combo on
- `SetLaneDisplayMode` normalizes an undefined enum to `AllOn`, no-ops when unchanged, and marks dirty when changed, matching `SetDamageLevel`
- the three boolean setters follow the existing changed/no-op setter pattern
- all four settings survive SQLite save/reload
- `LaneDisplayMode` is persisted as its enum name such as `LaneOff`, not `1` or `LANE OFF`
- missing or unrecognized `LaneDisplayMode` rows leave the `AllOn` default unchanged

Do not add a migration layer or numeric enum parser.

### Implementation

Define:

```csharp
public enum DrumsLaneDisplayMode
{
    AllOn = 0,
    LaneOff = 1,
    LineOff = 2,
    AllOff = 3,
}
```

Add the two required matrix helpers beside the enum:

```csharp
public static bool ShowsLaneBackground(this DrumsLaneDisplayMode mode) =>
    mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LineOff;

public static bool ShowsMeasureLines(this DrumsLaneDisplayMode mode) =>
    mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LaneOff;
```

Add to `ConfigData`:

- `LaneDisplayMode`
- `ShowJudgementLine`
- `EnableLaneFlush`
- `ShowCombo`

For `ConfigManager`:

- load `LaneDisplayMode` by case-insensitive enum name, like `DamageLevel`
- persist `Config.LaneDisplayMode.ToString()` in `BuildPersistedEntries`
- implement `SetLaneDisplayMode` like `SetDamageLevel`, with invalid values normalized to `AllOn`
- implement the three booleans like `SetMetronome`
- add the four methods to `IConfigManager`
- add matching no-op methods to the existing test-only `StubConfigManager`

### Verify

On macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumsLaneDisplayModeTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerSqlitePersistenceTests"
```

Use `DTXMania.Test/DTXMania.Test.csproj` on Windows.

## Task 2 — Expose the four settings in the existing Drums Config list

### Files

- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify `DTXMania.Test/Config/ConfigStageLogicTests.cs`

### Tests first

Extend `SetupConfigItems_ShouldBuildSystemDrumsExitCategories` so the Drums `Assert.Collection` includes, immediately after `Scroll Speed`:

1. `Lane Display`
2. `Judge Line`
3. `Lane Flush`
4. `Combo`

Add focused Config-stage logic tests that pin:

- `Lane Display` exposes exactly `ALL ON`, `LANE OFF`, `LINE OFF`, `ALL OFF` in that order
- the current display label maps from `ConfigData.LaneDisplayMode`
- changing `Lane Display` calls `SetLaneDisplayMode` with the corresponding enum value
- each toggle calls its matching config setter
- existing `Scroll Speed` remains present with its current range/formatter and behavior

Do not put these tests in `DrumConfigStageTests`; that suite owns drum-kit key assignment, not the Drums settings list.

### Implementation

Add the four items after `Scroll Speed` in `ConfigStage.SetupConfigItems`:

- one `DropdownConfigItem` for `Lane Display`
- three `ToggleConfigItem`s for `Judge Line`, `Lane Flush`, and `Combo`

Map UI labels explicitly to/from enum values. Persisted values remain enum names; the uppercase labels are presentation only.

Do not add another screen, category, or generic UI model.

### Verify

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStageLogicTests"
```

Use the Windows test project on Windows.

## Task 3 — Snapshot and apply the visual contract in PerformanceStage

### Files

- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

`PerformanceRendererStateTests` and `PerformanceStageJudgementIntegrationTests` are not HPA-19 owners: the former tests leaf renderers and the latter drives `JudgementManager` without constructing `PerformanceStage`.

### Tests first

Extend the existing deterministic stage tests instead of creating a parallel test stack:

- add/extend coverage showing the activation snapshot derives lane-background and measure-line flags from the enum helpers and copies the three booleans for the run
- add disabled-gate cases beside the existing `DrawLaneBackgrounds` tests; the gate must return before textured/fallback selection
- add a disabled case beside the existing `DrawMeasureLines` tests
- add a disabled case beside the existing `DrawJudgementLine` textured/fallback tests
- add combo-hidden coverage beside the existing `DrawUIElements` tests while preserving `OnComboChanged` behavior
- rewrite `OnJudgementMade_WhenJudgementIsHit_ShouldForwardToManagersAndTriggerVisualFeedbackWithoutLaneFlash` into the enabled-default contract: the same hit still reaches the managers/attack/pad/popup paths and now sets the judged lane flash to `1.0f`
- add a lane-flush-disabled hit case next to that test
- keep/extend the existing Miss test to assert the lane does not flash

`NoteRendererLogicTests` already pins `TriggerLaneFlash` and decay. Do not change it unless the leaf renderer itself changes, which is not expected.

### Implementation

At `PerformanceStage.OnActivate`, snapshot stage-local values next to the other frozen performance configuration:

- lane background = `config.LaneDisplayMode.ShowsLaneBackground()`
- measure lines = `config.LaneDisplayMode.ShowsMeasureLines()`
- judgement line = `config.ShowJudgementLine`
- lane flush = `config.EnableLaneFlush`
- combo = `config.ShowCombo`

Use those snapped values for the entire run. Do not subscribe to config changes.

Gate only the existing orchestration points:

- `DrawLaneBackgrounds`: early return before skin texture vs. fallback renderer selection
- `DrawMeasureLines`: early return before `NoteRenderer.DrawMeasureLines`
- `DrawJudgementLine`: early return before textured hit-bar vs. fallback renderer selection
- combo: guard only `_comboDisplay?.Draw(...)`; leave `OnComboChanged`, `ComboManager`, scoring, and result state unchanged

For lane flush, add the trigger only inside the existing successful-hit block:

```csharp
if (e.IsHit())
{
    // existing attack/pad feedback
    if (_enableLaneFlush)
        _noteRenderer?.TriggerLaneFlash(e.Lane);
}
```

Use the existing `JudgementEvent.IsHit()` predicate. Do not duplicate it with `Type != Miss`.

Do not add a flash call to `ProcessAutoPlay`. `ResolveAutoHit` already raises `JudgementMade`, so auto-play reaches this same block. Do not revive `_laneFlashTexture` or add another effect system.

### Verify

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceStageDeterministicTests"
```

Use the Windows test project on Windows.

## Task 4 — Regression verification and manual smoke

Run the platform build and full Game test suite.

### macOS

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

### Windows

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

No new E2E test is required.

Perform one manual gameplay smoke using a simple chart:

1. Verify all four `Lane Display` combinations against the design table.
2. Toggle `Judge Line` independently.
3. Toggle `Lane Flush` and confirm successful hits change only lane flash visibility, not pad/attack effects.
4. Toggle `Combo` and confirm combo/scoring still progress while the display is hidden.
5. Use the existing PageUp/PageDown Scroll Speed hotkeys and confirm behavior is unchanged.
6. Restart once and confirm all four settings reload from SQLite.

## Completion criteria

HPA-19 is ready to merge when:

- the four settings are persisted and editable in the existing Drums Config list
- `DrumsLaneDisplayMode` owns and tests the 2×2 lane/measure matrix
- `LaneDisplayMode` persists by enum name and invalid/missing values retain `AllOn`
- textured and fallback lane/judgement rendering obey the same stage-level gates
- lane flush uses the existing `OnJudgementMade` → `e.IsHit()` → `NoteRenderer.TriggerLaneFlash` path exactly once
- hiding combo changes drawing only, not combo/scoring state
- Scroll Speed is untouched
- deferred legacy modifiers have no placeholder production code
- focused tests, full platform Game tests, build, and manual smoke pass

Keep all implementation commits on this same HPA-19 branch/PR.
