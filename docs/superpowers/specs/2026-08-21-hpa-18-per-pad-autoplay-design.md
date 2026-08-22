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
- Preserve the recorder and gameplay-E2E deterministic all-AutoPlay launch contracts.
- Keep the change within the existing config, performance, judgement, recorder, and E2E bootstrap seams.

## Non-goals

- Guitar/Bass AutoPlay or any Guitar/Bass mode work.
- Recreating NX's 11-switch model when CX has only 10 gameplay lanes.
- Introducing a second shared drum-lane enum or refactoring all lane-index consumers.
- Migrating the old global `AutoPlay` value. There are no compatibility requirements for current CX configs.
- Adding a judgement-source/event-origin abstraction.
- Changing `NoteRenderer` or gameplay rendering architecture.
- Expanding the Game API/telemetry schema to publish per-lane AutoPlay state.
- Adding a `DTXMania.Game` project reference to `DTXMania.VideoRecorder` solely to share AutoPlay constants.

## Canonical lane and key contract

HPA-18 must not invent a second set of lane codes. `PerformanceUILayout.LaneType` and `PerformanceUILayout.LaneNames` already define the ten CX gameplay identities in order:

`LC, HH, LP, SN, HT, DB, LT, FT, CY, RD`.

Add one small Game-side AutoPlay lane-definition table next to configuration persistence. Each row owns:

- the integer lane ID;
- the existing `PerformanceUILayout.LaneType` value;
- the persisted suffix derived from the existing lane identity (`LaneType.ToString()` / `LaneNames`), not a separately invented token;
- the Config-stage display label.

The table is configuration metadata, not a new lane-domain model. Gameplay continues to use integer lane IDs, and `PerformanceUILayout.LaneType` remains the existing enum.

| Lane | Existing lane type / suffix | Config label | Persisted key | Notes |
| ---: | --- | --- | --- | --- |
| 0 | `LC` | Left Cymbal | `AutoPlay.LC` | Existing LC lane |
| 1 | `HH` | HiHat | `AutoPlay.HH` | Closed/open HiHat share the lane |
| 2 | `LP` | Left Pedal | `AutoPlay.LP` | Covers DTX `0x1B` and `0x1C` because CX maps both to lane 2 |
| 3 | `SN` | Snare | `AutoPlay.SN` | Existing SN lane |
| 4 | `HT` | High Tom | `AutoPlay.HT` | Existing HT lane |
| 5 | `DB` | Bass Drum | `AutoPlay.DB` | Reuse CX's existing `DB` identity; do not introduce `BD` |
| 6 | `LT` | Low Tom | `AutoPlay.LT` | Existing LT lane |
| 7 | `FT` | Floor Tom | `AutoPlay.FT` | Existing FT lane |
| 8 | `CY` | Cymbal | `AutoPlay.CY` | Existing CY lane |
| 9 | `RD` | Ride | `AutoPlay.RD` | Existing RD lane |

This table is the HPA-18 boundary. Do not add a separate Left Bass Drum setting until CX itself gains a distinct gameplay lane for it.

## Configuration model and persistence

Replace `ConfigData.AutoPlay` with a **get-only** `HashSet<int> AutoPlayLanes`. Membership means that lane is automated. The empty set is the default.

The collection is get-only for the same reason `MidiVelocityThresholds` is get-only: callers must not replace it with `null` or swap in a shared mutable reference. `ConfigManager` remains the mutation owner.

`IConfigManager`/`ConfigManager` expose narrow mutation operations for one lane and all lanes rather than allowing callers to replace the set. Lane inputs are limited to the canonical ten-lane table. Mutations follow the existing deferred persist-on-edit pattern.

Persist the ten explicit `AutoPlay.*` keys from the canonical table. Remove the old global `AutoPlay` load/write path. No compatibility shim or one-time migration is required; an old key left in an existing database is ignored by the new runtime.

This keeps persistence human-readable and avoids serializing a new collection format for one ten-bit setting.

## Config-stage interaction

The Drums category contains:

1. `AutoPlay (All)`
2. the ten lane toggles in canonical gameplay-lane order

Build the per-lane rows from the shared Game-side AutoPlay lane table so ConfigStage does not duplicate suffix or lane mappings.

The master is a small computed tri-state config item:

- `None` when zero lanes are enabled;
- `All` when all ten lanes are enabled;
- `Mixed` otherwise.

Activating/previous/next on the master uses one deterministic action:

- `All` -> disable all lanes;
- `None` or `Mixed` -> enable all lanes.

Per-lane rows remain ordinary `ToggleConfigItem`s backed by the config manager. `DropdownConfigItem` is not appropriate because `Mixed` is computed state, not a selectable value. Do not add a generic tri-state settings framework; one focused config-item type is sufficient.

## Performance lifecycle and AutoPlay scheduling

`InitializeAutoPlay()` takes a defensive copy of `ConfigData.AutoPlayLanes` into a performance-owned set. The running stage never retains the live ConfigData collection reference, so later config edits cannot mutate the current performance.

Keep the current single sorted AutoPlay scan. For each due chart note:

- if its lane is in the frozen AutoPlay set, resolve it as the existing AutoPlay Perfect and trigger the existing pad/chip feedback path;
- if its lane is not automated, do not auto-resolve it; still advance the AutoPlay scan so a manual note cannot block later automated notes, while normal judgement/miss ownership remains intact.

Run the AutoPlay scan only when at least one lane is automated. This reuses the existing scheduler instead of introducing one cursor or manager per lane.

## Player input and judgement ownership

Replace global `JudgementManager.IgnorePlayerInput` behavior with lane-scoped ignored input. Physical hits are dropped only when the hit lane is automated; hits on other lanes continue through the existing judgement path.

`PerformanceStage.OnLaneHitForPadFeedback` applies the same lane-specific rule. Automated lanes get feedback from the AutoPlay path; manual lanes get feedback from physical input.

No judgement-source field is needed. During one performance, the frozen AutoPlay lane set is authoritative: a judgement on an automated lane is automated because physical judgement input for that lane is suppressed.

## Gauge behavior

Preserve the existing intent of `AutoAddGauge`:

- judgements on manual lanes affect gauge normally;
- judgements on automated lanes affect gauge only when `AutoAddGauge` is enabled.

The rule is evaluated from `JudgementEvent.Lane` plus the frozen AutoPlay set. No gauge-manager redesign is required.

## Recorder, E2E, and telemetry contracts

Full AutoPlay is still a configuration bootstrap concern, not recorder-specific gameplay.

`RecordingSandbox` must replace its global `AutoPlay=True` override with all ten canonical `AutoPlay.*` keys. The recorder intentionally references `DTXMania.Automation`, not `DTXMania.Game`; HPA-18 must not reverse that boundary merely to import the Game-side lane table. Keep the recorder's ten-key patch local and prove it against the real Game configuration parser in `RecorderConfigCompatibilityTests`.

`E2EFixtureBuilder` already references the Game project and should build its all-AutoPlay bootstrap from the canonical Game-side table. `MidiGameplaySmokeTests` must stop disabling AutoPlay by replacing one global string and instead produce a bootstrap with no enabled AutoPlay lanes. `E2EFixtureBuilderTests` and `RecorderConfigCompatibilityTests` must assert the ten-lane result rather than `Config.AutoPlay`.

Keep the existing boolean telemetry field `AutoPlayEnabled`. Its CX meaning becomes **all ten gameplay lanes are automated**. No changes are required to the `DTXMania.Automation` telemetry schema; existing automation/recorder consumers keep reading the boolean.

## Testing strategy

Tests should pin behavior at the existing owning seams:

- config model/persistence: get-only empty default, one-lane mutation, all-lane mutation, invalid-lane rejection, exact ten-key SQLite round trip using `DB`, and no dependency on the old global key;
- lane metadata: the AutoPlay definition table has exactly ten unique lanes in `0..9` order and its suffixes match existing `PerformanceUILayout.LaneType` / `LaneNames`;
- config UI: master `None`/`Mixed`/`All` display and `All -> None`, `None/Mixed -> All` actions; lane rows are generated from the canonical table and update only their matching lane;
- judgement: physical input is ignored on an automated lane but accepted on a different manual lane;
- performance: use `PerformanceStageDeterministicTests` as the primary owner because it already characterizes `InitializeAutoPlay`, `ProcessAutoPlay`, `IgnorePlayerInput`, pad feedback, gauge gating, and telemetry. Prove mixed-lane scanning, defensive freeze, lane-scoped feedback/input, and per-lane `AutoAddGauge` there. Update `PerformanceStageAdditionalCoverageTests` where its existing global assumptions require it;
- do not use `AutomatedPlaySimulationTests` as the primary HPA-18 proof: that suite fabricates `JudgementEvent`s directly into score/gauge managers and does not execute `PerformanceStage.ProcessAutoPlay`;
- E2E bootstrap: `E2EFixtureBuilderTests` proves all ten keys, `MidiGameplaySmokeTests` proves manual gameplay starts with no automated lanes, and `RecorderConfigCompatibilityTests` loads recorder output through the real `ConfigManager` and proves all ten lanes are enabled;
- recorder unit tests: sandbox output requests all ten keys and no longer emits the removed global key;
- final stale-assumption scan includes `DTXMania.E2E`, `DTXMania.Automation`, and their test projects in addition to Game/recorder projects. `AutoPlayEnabled` telemetry references are expected and must not be mechanically removed.

Prefer extending existing owning suites rather than creating a new test harness.

## Expected production touch points

Game production:

- new small Game-side AutoPlay lane-definition table under `DTXMania.Game/Lib/Config/`
- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/IConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigItems.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`

Recorder/E2E bootstrap:

- `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`
- `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- `DTXMania.E2E/MidiGameplaySmokeTests.cs`

Tests may touch their existing owning files. `DTXMania.Automation` production types should remain unchanged unless implementation discovers a compile break; telemetry schema expansion is still out of scope.

If implementation requires a broader gameplay-lane refactor, a recorder -> Game project dependency, or a new public automation schema, stop and revisit the design rather than expanding HPA-18 opportunistically.

## Size

Expected implementation effort remains roughly **1.5-2 engineer days**. The revised design adds explicit E2E/test wiring but removes duplicate lane/key mappings from the Game-side implementation. Design, implementation, tests, and review remain in one HPA-18 PR.