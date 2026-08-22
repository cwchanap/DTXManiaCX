# HPA-18 Per-Pad Drums AutoPlay Design

**Status:** Approved for implementation after plan review  
**Linear:** HPA-18  
**Scope:** DTXManiaCX Drums-only per-lane AutoPlay

## Context

DTXManiaCX currently models AutoPlay as one global `ConfigData.AutoPlay` boolean. That is enough for recorder automation, but it cannot reproduce the useful DTXManiaNX behavior where a player can automate selected drum parts and play the remaining parts manually.

The NX settings cannot be copied literally. NX exposes 11 drum AutoPlay switches, while CX intentionally has 10 gameplay lanes. In CX, DTX channel `0x1B` (Left Pedal) and `0x1C` (Left Bass Drum) both normalize to gameplay lane 2. Exposing two independent switches for one CX lane would create contradictory ownership, so HPA-18 uses the existing 10-lane CX model.

HPA-10 established the lifecycle rule that gameplay-affecting configuration is frozen when `PerformanceStage` activates. HPA-18 follows the same rule: editing config affects the next performance, never an already-running performance.

## Goals

- Replace global Drums AutoPlay with one toggle per CX gameplay lane.
- Add an `AutoPlay (All)` master that displays `All`, `None`, or `Mixed`.
- Allow automated and manually played lanes in the same performance.
- Preserve the existing `AutoAddGauge` behavior at lane granularity.
- Preserve the recorder's deterministic all-AutoPlay launch contract.
- Keep the change within the existing config, performance, and judgement seams.

## Non-goals

- Guitar/Bass AutoPlay or any Guitar/Bass mode work.
- Recreating NX's 11-switch model when CX has only 10 gameplay lanes.
- Introducing a new shared drum-lane domain enum or refactoring all lane-index consumers.
- Migrating the old global `AutoPlay` value. There are no compatibility requirements for current CX configs.
- Adding a judgement-source/event-origin abstraction.
- Changing `NoteRenderer` or gameplay rendering architecture.
- Expanding the Game API/telemetry schema to publish per-lane AutoPlay state.

## Lane contract

The configuration uses the same integer lane identity already used by gameplay and key bindings.

| Lane | Config label | Persisted key | Notes |
| ---: | --- | --- | --- |
| 0 | Left Cymbal | `AutoPlay.LC` | Existing LC lane |
| 1 | HiHat | `AutoPlay.HH` | Closed/open HiHat share the lane |
| 2 | Left Pedal | `AutoPlay.LP` | Covers both DTX `0x1B` and `0x1C` because CX maps both to lane 2 |
| 3 | Snare | `AutoPlay.SN` | Existing SN lane |
| 4 | High Tom | `AutoPlay.HT` | Existing HT lane |
| 5 | Bass Drum | `AutoPlay.BD` | Existing BD lane |
| 6 | Low Tom | `AutoPlay.LT` | Existing LT lane |
| 7 | Floor Tom | `AutoPlay.FT` | Existing FT lane |
| 8 | Cymbal | `AutoPlay.CY` | Existing CY lane |
| 9 | Ride | `AutoPlay.RD` | Existing RD lane |

This table is the HPA-18 boundary. Do not add a separate Left Bass Drum setting until CX itself gains a distinct gameplay lane for it.

## Configuration model and persistence

Replace `ConfigData.AutoPlay` with `HashSet<int> AutoPlayLanes`. Membership means that lane is automated. The empty set is the default.

`IConfigManager`/`ConfigManager` should expose narrow mutation operations for one lane and all lanes rather than requiring callers to replace the whole set. Lane inputs are limited to `0..9`.

Persist the ten explicit keys in the table above. Remove the old global `AutoPlay` load/write path. No compatibility shim or one-time migration is required; an old key left in an existing database is simply ignored by the new runtime.

This keeps persistence human-readable and avoids serializing a new collection format for one ten-bit setting.

## Config-stage interaction

The Drums category contains:

1. `AutoPlay (All)`
2. the ten lane toggles in gameplay-lane order

The master is a small computed tri-state config item:

- `None` when zero lanes are enabled;
- `All` when all ten lanes are enabled;
- `Mixed` otherwise.

Activating/previous/next on the master uses one deterministic action:

- `All` -> disable all lanes;
- `None` or `Mixed` -> enable all lanes.

Per-lane rows remain ordinary `ToggleConfigItem`s backed by the config manager. Do not add a generic tri-state settings framework; one focused config-item type is sufficient.

## Performance lifecycle and AutoPlay scheduling

`PerformanceStage` snapshots `ConfigData.AutoPlayLanes` into its own set during activation. Later config edits cannot mutate the running performance.

Keep the current single sorted AutoPlay scan. For each due chart note:

- if its lane is in the frozen AutoPlay set, resolve it as the existing AutoPlay Perfect and trigger the existing pad/chip feedback path;
- if its lane is not automated, do not auto-resolve it; advance the AutoPlay scan while leaving normal judgement ownership intact so `JudgementManager` can accept a player hit or later resolve a miss.

Run the AutoPlay scan only when at least one lane is automated. This reuses the existing scheduler instead of introducing one cursor or manager per lane.

## Player input and judgement ownership

Replace `JudgementManager.IgnorePlayerInput` with lane-scoped ignored input. Physical hits are ignored only when the hit lane is automated; hits on other lanes continue through the existing judgement path.

`PerformanceStage` should apply the same lane-specific rule to manual pad feedback. Automated lanes get feedback from the AutoPlay path; manual lanes get feedback from physical input.

No judgement-source field is needed. During one performance, the frozen AutoPlay lane set is authoritative: a judgement on an automated lane came from AutoPlay because physical judgement input for that lane is suppressed.

## Gauge behavior

Preserve the existing intent of `AutoAddGauge`:

- judgements on manual lanes affect gauge normally;
- judgements on automated lanes affect gauge only when `AutoAddGauge` is enabled.

The rule is evaluated from `JudgementEvent.Lane` plus the frozen AutoPlay set. No gauge-manager redesign is required.

## Recorder and telemetry contract

`RecordingSandbox` must explicitly enable all ten `AutoPlay.*` keys in the disposable recorder configuration. This preserves the autonomous recording workflow without adding recorder-specific gameplay behavior.

Keep the existing boolean telemetry field `AutoPlayEnabled`. Its CX meaning becomes **all ten gameplay lanes are automated**. The recorder needs the all-AutoPlay fact, not a per-lane telemetry schema, so HPA-18 should not expand the public automation contract.

## Testing strategy

Tests should pin behavior at the existing seams:

- config model/persistence: empty default, one-lane mutation, all-lane mutation, exact ten-key SQLite round trip, and no dependency on the old global key;
- config UI: master `None`/`Mixed`/`All` display and `All -> None`, `None/Mixed -> All` actions; lane rows update the matching persisted lane;
- judgement: physical input is ignored on an automated lane but accepted on a different manual lane;
- performance: a mixed chart auto-resolves only enabled lanes, leaves manual lanes judgeable/missable, freezes the lane set at activation, and applies `AutoAddGauge` only to automated lanes;
- recorder: sandbox output requests all ten lanes and still reports all-AutoPlay through the existing telemetry expectation.

Prefer extending existing `ConfigManagerTests`, `ConfigManagerSqlitePersistenceTests`, `ConfigItemTests`, `ConfigStageLogicTests`, `JudgementManagerTests`, `AutomatedPlaySimulationTests`, and `RecordingSandboxTests` rather than creating a new test harness.

## Expected production touch points

- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigItems.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`
- `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`

Tests may touch their existing owning files. If implementation requires a broader gameplay-lane refactor or a new public automation schema, stop and revisit the design rather than expanding HPA-18 opportunistically.

## Size

Expected implementation effort: roughly **1.5-2 engineer days**, within the single-task three-day limit. Design, implementation, tests, and review remain in one HPA-18 PR.