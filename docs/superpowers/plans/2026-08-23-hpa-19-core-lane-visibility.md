# HPA-19 Core Drums Lane Visibility — Implementation Plan

## Objective

Implement `Lane Display`, `Judge Line`, `Lane Flush`, and `Combo` as persisted Drums settings, then apply them through the existing `PerformanceStage` rendering/judgement seams.

This work stays in the existing HPA-19 branch and pull request. Do not split implementation into additional PRs.

Design reference: `docs/superpowers/specs/2026-08-23-hpa-19-core-lane-visibility-design.md`.

## Scope guardrails

Before changing code, keep these decisions fixed:

- Preserve the current Drums-only 50–400% Scroll Speed implementation and live hotkeys.
- Defaults must reproduce current CX visuals.
- Do not implement or stub HID-SUD, Dark, AttackEffect variants, Reverse, JudgePosition, LaneType, NumOfLanes, JudgeLinePos, shutters, HHOGraphics, LBDGraphics, RDPosition, or Graph.
- Do not add a visual-modifier service, renderer hierarchy, preset framework, or new GraphicsDevice test harness.
- Config-stage edits to these four settings take effect on the next performance activation.

Expected effort: 2–3 engineer days.

## Task 1 — Add the config contract and SQLite persistence

### Files

- Add `DTXMania.Game/Lib/Config/DrumsLaneDisplayMode.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- Extend `DTXMania.Test/Config/ConfigManagerTests.cs` only if that is where setter behavior is already characterized

### Tests first

Add focused tests that pin:

- defaults: `AllOn`, judgement line on, lane flush on, combo on
- setters update `ConfigData` through `IConfigManager`
- all four values survive a SQLite save/reload
- an absent key keeps the `ConfigData` default instead of creating a migration requirement

Follow existing ConfigManager handling for malformed persisted values; do not create a one-off recovery subsystem for this feature.

### Implementation

Add `DrumsLaneDisplayMode` with explicit values:

- `AllOn = 0`
- `LaneOff = 1`
- `LineOff = 2`
- `AllOff = 3`

Add to `ConfigData`:

- `LaneDisplayMode`
- `ShowJudgementLine`
- `EnableLaneFlush`
- `ShowCombo`

Add one direct setter per setting to `IConfigManager` / `ConfigManager`, following the existing Metronome/Risky/NoFail style. Persist through the existing `ConfigEntries(Key, Value)` mechanism. No change event is required.

### Verify

Run the relevant config tests on the current platform, for example on macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigDataTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerSqlitePersistenceTests"
```

## Task 2 — Expose the four settings in Drums Config

### Files

- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

### Tests first

Extend the existing Drums config tests to pin:

- the menu contains `Lane Display`, `Judge Line`, `Lane Flush`, and `Combo`
- `Lane Display` exposes exactly `ALL ON`, `LANE OFF`, `LINE OFF`, `ALL OFF` in that order
- the initial displayed value follows `ConfigData`
- changing each item uses the corresponding ConfigManager setter
- existing `Scroll Speed` remains present with its current range/formatter

Do not test rendering from ConfigStage.

### Implementation

Add the four items near `Scroll Speed` in the existing Drums list:

- one `DropdownConfigItem` for `Lane Display`
- three `ToggleConfigItem`s for the boolean settings

Keep labels/descriptions concise and user-facing. Do not add another screen or category.

### Verify

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumConfigStageTests"
```

Use `DTXMania.Test/DTXMania.Test.csproj` instead on Windows.

## Task 3 — Apply visibility settings in PerformanceStage

### Files

- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify `DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs`, or add a small `DTXMania.Test/Stage/Performance/PerformanceStageVisualConfigTests.cs` if keeping the new assertions separate is clearer

Do not modify `LaneBackgroundRenderer`, `JudgementLineRenderer`, or `ComboDisplay` merely to create a new abstraction. `PerformanceStage` is the required integration point because its skin-texture paths can bypass those fallback components.

### Tests first

Pin the lane-display matrix:

| Mode | Show lane background | Show measure lines |
| --- | --- | --- |
| `AllOn` | true | true |
| `LaneOff` | false | true |
| `LineOff` | true | false |
| `AllOff` | false | false |

Also pin that:

- judgement-line disabled suppresses the stage-level judgement-line draw path
- combo disabled suppresses only combo drawing, not combo state updates
- defaults keep all current draw paths enabled

Use existing reflection/state helpers if needed. Do not extract a production framework solely to make the tests easy; a tiny local predicate/helper is acceptable if it also makes `PerformanceStage` clearer.

### Implementation

When a performance activates, snapshot the four config values into stage-local state.

Then gate existing orchestration points:

- lane background: guard before both the skin-texture path and `LaneBackgroundRenderer` fallback
- measure/bar lines: guard the existing `NoteRenderer.DrawMeasureLines` call
- judgement line: guard before both textured hit-bar and fallback renderer paths
- combo: guard the existing `ComboDisplay.Draw` call only

Notes, pads, judgement text, attack effects, score, gauge, and result calculations must remain unchanged.

### Verify

Run the focused performance visual tests plus the existing component state tests.

## Task 4 — Wire Lane Flush to successful judgements

### Files

- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify `DTXMania.Test/Stage/Performance/PerformanceStageJudgementIntegrationTests.cs`
- `DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs` should normally remain unchanged because it already pins `TriggerLaneFlash` and flash decay

### Tests first

Add characterization around the stage judgement path:

- a non-Miss judgement triggers the judged lane when lane flush is enabled
- a non-Miss judgement does not trigger lane flash when disabled
- a Miss never triggers lane flash
- auto-play should rely on the same judgement event path; do not add a duplicate direct trigger in `ProcessAutoPlay`

Reuse the existing judgement integration helpers. Avoid a fake graphics stack.

### Implementation

In the existing `PerformanceStage` judgement event handler, after a judgement is known:

- if `EnableLaneFlush` is true and the judgement type is not `Miss`, call the existing `NoteRenderer.TriggerLaneFlash` for that lane
- otherwise do nothing

Leave `NoteRenderer` animation/decay/drawing untouched. Do not wire the unused lane-flash skin texture or add a second effect manager.

### Verify

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceStageJudgementIntegrationTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NoteRendererLogicTests"
```

Use the Windows test project on Windows.

## Task 5 — Regression verification and smoke check

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

No new E2E test is required for this visual/config slice.

Perform one manual gameplay smoke using a simple chart:

1. Verify all four `Lane Display` combinations against the design table.
2. Toggle `Judge Line` independently.
3. Toggle `Lane Flush` and confirm successful hits change only the lane flash, not pad/attack effects.
4. Toggle `Combo` and confirm score/combo progression still occurs while the number is hidden.
5. Use the existing PageUp/PageDown Scroll Speed hotkeys and confirm behavior is unchanged.
6. Restart once and confirm the four settings persisted through SQLite.

## Completion criteria

HPA-19 is ready to merge when:

- the four settings are persisted and editable in Drums Config
- textured and fallback lane/judgement rendering obey the same visibility rules
- lane flush uses the existing `NoteRenderer` flash path and is controlled by judgements
- hiding combo does not alter combo/scoring state
- Scroll Speed is untouched
- deferred legacy modifiers have no placeholder production code
- focused tests, full platform Game tests, build, and manual smoke pass

Keep all implementation commits on this same HPA-19 branch/PR.
