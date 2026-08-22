# HPA-18 Per-Pad Drums AutoPlay Design

**Status:** Approved for implementation after plan review  
**Linear:** HPA-18  
**Scope:** DTXManiaCX Drums-only per-lane AutoPlay

## Context

DTXManiaCX currently models AutoPlay as one global `ConfigData.AutoPlay` boolean. HPA-18 replaces that with per-lane automation so a player can automate selected drum parts and play the remaining parts manually.

CX has 10 gameplay lanes. DTX channel `0x1B` (Left Pedal) and `0x1C` (Left Bass Drum) both normalize to gameplay lane 2, so HPA-18 deliberately exposes one lane-2 AutoPlay setting rather than copying DTXManiaNX's 11-switch model.

HPA-10 established the lifecycle rule that gameplay-affecting configuration is frozen when `PerformanceStage` activates. HPA-18 follows the same rule: edits affect the next performance, never the currently running one.

## Goals

- Replace global Drums AutoPlay with one toggle per CX gameplay lane.
- Add an `AutoPlay (All)` master that displays `None`, `Mixed`, or `All`.
- Allow automated and manually played lanes in the same performance.
- Preserve `AutoAddGauge` at lane granularity.
- Prevent any run that used AutoPlay, partial or full, from being persisted as a player score.
- Preserve recorder and gameplay-E2E deterministic full-AutoPlay launch behavior.
- Keep the change inside existing config, performance, judgement, result-summary, diagnostics, recorder, and E2E seams.

## Non-goals

- Guitar/Bass AutoPlay.
- Recreating NX's 11-switch Drums AutoPlay model.
- Introducing a shared lane-domain enum/refactor.
- Migrating the old global `AutoPlay` value.
- Adding a judgement-source/event-origin abstraction.
- Changing `NoteRenderer` or gameplay rendering architecture.
- Expanding Game API/automation telemetry with per-lane AutoPlay state.
- Adding a `DTXMania.VideoRecorder -> DTXMania.Game` project reference.
- Reworking Drum Key Mapping lane/visual-zone semantics. HPA-619 already owns that separate non-identity visual mapping contract.
- Adding an AutoPlay sub-screen solely to avoid a longer scrolling Drums settings list.

## Runtime lane contract

AutoPlay uses the same integer gameplay lane IDs already consumed by `DTXChartParser`, `PerformanceStage`, and judgement:

| Lane | AutoPlay label | Gameplay identity | Notes |
| ---: | --- | --- | --- |
| 0 | Left Cymbal | LC | |
| 1 | Hi-Hat | HH | closed/open Hi-Hat share the lane |
| 2 | Left Pedal | LP | DTX `0x1B` and `0x1C` both map here |
| 3 | Snare | SN | |
| 4 | High Tom | HT | |
| 5 | Bass Drum | DB | |
| 6 | Low Tom | LT | |
| 7 | Floor Tom | FT | |
| 8 | Cymbal | CY | right crash/cymbal lane |
| 9 | Ride | RD | |

The persisted identity is the integer lane, not another textual lane-code contract:

`AutoPlay.0` ... `AutoPlay.9`

This deliberately mirrors the existing `Key.Unbound.{lane}` persistence shape. It removes the need for `AutoPlayLaneDefinitions.cs`, avoids DB/BD naming drift, and lets recorder/E2E full-AutoPlay bootstraps generate the contract with a `0..9` loop.

The human-readable labels above are Config-stage copy only. Do **not** source them from `KeyBindings.GetLaneName`: Drum Key Mapping uses an existing authored visual/binding mapping whose semantics differ from the gameplay-lane order and were explicitly preserved by HPA-619. HPA-18 does not reopen that mapping.

## Configuration model and persistence

Add a **get-only** `HashSet<int> AutoPlayLanes` to `ConfigData`; empty means fully manual play. It follows the same collection-safety pattern as `MidiVelocityThresholds`: callers may mutate through the owning manager but cannot replace the collection with `null` or a shared reference.

`IConfigManager`/`ConfigManager` expose:

- `SetAutoPlayLane(int lane, bool enabled)` for one lane;
- `SetAllAutoPlayLanes(bool enabled)` for all 10 lanes.

Only lane IDs `0..9` are accepted. Mutations mark the existing deferred save dirty only when state changes.

Persistence uses the existing sparse-set pattern:

- enabled lane: persist `AutoPlay.{lane}=true`;
- disabled lane: omit the key;
- load only canonical integer suffixes `0..9` whose value parses as true.

No collection serializer or schema table is added.

The old bare `AutoPlay` key is **not migrated**. Once the legacy property is removed, encountering a bare `AutoPlay` entry logs one warning explaining that the obsolete global setting is ignored. This preserves the no-compatibility stance without silently changing behavior.

### Safe migration order inside the PR

The global property/API must not be deleted in the first implementation task because many existing tests/fakes and bootstrap paths still compile against it. Implementation therefore follows **add -> migrate consumers -> delete**:

1. add `AutoPlayLanes` and new mutators while the global API temporarily remains;
2. migrate ConfigStage, judgement/performance, diagnostics, recorder/E2E, and tests;
3. remove `ConfigData.AutoPlay`, `SetAutoPlay(bool)`, global persistence, and transitional input gate in one final sweep.

This is temporary branch sequencing only, not a shipped compatibility layer.

## Config-stage interaction

Keep AutoPlay controls inline in the existing scrolling Drums category. Replacing one global row with one master plus 10 lane rows grows the current list from 10 to 20 items, but adding a new stage/sub-screen would cost more machinery for a simple set of toggles and make quick editing less direct.

Order:

1. `AutoPlay (All)`
2. Left Cymbal
3. Hi-Hat
4. Left Pedal
5. Snare
6. High Tom
7. Bass Drum
8. Low Tom
9. Floor Tom
10. Cymbal
11. Ride

The master state is:

- `None` when zero lanes are enabled;
- `All` when all 10 lanes are enabled;
- `Mixed` otherwise.

No new tri-state config-item class is necessary. Extend `ToggleConfigItem` with an optional display formatter, following `IntegerConfigItem`'s existing formatter pattern. For the master:

- getter returns `AutoPlayLanes.Count == 10`;
- setter calls `SetAllAutoPlayLanes(value)`;
- formatter derives `None` / `Mixed` / `All` from the current lane count.

Existing Toggle semantics naturally give `All -> None` and `None/Mixed -> All` for Previous, Next, and Toggle. Normal per-lane `ToggleConfigItem`s keep the default `ON/OFF` display.

## Performance lifecycle and AutoPlay scheduling

`InitializeAutoPlay()` takes a defensive copy of `ConfigData.AutoPlayLanes` into a performance-owned set. Later config edits cannot mutate the running performance.

Keep one `_autoPlayNoteIndex` over the sorted chart. For every due note:

- automated lane: resolve with existing `ResolveAutoHit`, trigger AutoPlay pad feedback, and play the chip;
- manual lane: do not auto-resolve it;
- in either case, advance the AutoPlay cursor once the note is due.

Advancing past a manual note is required so it cannot block later automated notes. JudgementManager retains ownership of manual hits/misses through its own runtime state.

Run the AutoPlay scan only when the frozen set is non-empty.

## Player input and judgement ownership

Replace global input suppression with lane-scoped filtering. During the migration, the new lane filter may coexist temporarily with `IgnorePlayerInput` so intermediate commits continue compiling; the global gate is removed after all consumers move.

Physical hits on automated lanes are dropped. Physical hits on manual lanes continue through the normal judgement path.

`PerformanceStage.OnLaneHitForPadFeedback` applies the same membership check: automated lanes get feedback from AutoPlay; manual lanes get feedback from physical input.

No judgement-source field is needed. The frozen AutoPlay set is authoritative for the run.

## Gauge behavior

- manual-lane judgements always affect gauge normally;
- automated-lane judgements affect gauge only when frozen `AutoAddGauge` is true.

`PerformanceStage` evaluates this from `JudgementEvent.Lane` plus its frozen set; `GaugeManager` itself does not change.

## Score persistence

Any run that used at least one automated lane must not update player score/history. Partial AutoPlay is assisted play just as full AutoPlay is.

Carry one internal run fact on `PerformanceSummary`, e.g. `UsedAutoPlay`, set by `PerformanceStage.FinalizePerformance` from `frozenAutoPlayLanes.Count > 0`. Extend `PerformanceSummary.IsSavable` to require `!UsedAutoPlay` in addition to its existing RunId/completion rules.

`ResultStage` already calls score persistence only when `PerformanceSummary.IsSavable`, so no ResultStage persistence branch or score-database schema is required. This field is an internal stage/result DTO fact, not an automation telemetry expansion.

## Crash-reporting contract

Crash configuration context currently publishes the global boolean. Replace it with the bounded count:

`AutoPlayLaneCount = config.AutoPlayLanes.Count`

`CrashLogFieldPolicy` treats it like the nearby other count fields through `TryNormalizeCount`. Remove the obsolete configuration-context `AutoPlay` boolean allowlist/test when the global property is deleted.

This keeps crash reports useful for diagnosing assisted-play configuration without adding per-lane identifiers.

## Recorder, E2E, and telemetry contracts

Full AutoPlay remains a config-bootstrap concern.

`RecordingSandbox` stays independent from Game and generates `AutoPlay.0=True` through `AutoPlay.9=True` locally with a simple `0..9` loop. `RecorderConfigCompatibilityTests` remains the cross-project drift guard by loading recorder output through the real `ConfigManager` and asserting all 10 lanes are enabled.

`DTXMania.VideoRecorder.Tests/TestSourceConfigDatabase` removes its obsolete bare `AutoPlay=False` source row; its normal source state is represented by having no `AutoPlay.{lane}` rows.

`E2EFixtureBuilder` also emits full AutoPlay with a `0..9` loop. `MidiGameplaySmokeTests` no longer string-replaces `AutoPlay=True`; its fixture simply emits no enabled AutoPlay lane keys.

Keep existing boolean telemetry `AutoPlayEnabled`. Its meaning becomes **all 10 gameplay lanes are automated**. `DTXMania.Automation`, `RecordWorkflow`, and their telemetry DTOs do not gain per-lane fields.

## Platform-specific validation

The normal development machine may be macOS, while gameplay E2E is Windows-only. Validation must use the project that actually builds on the current platform.

### macOS

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Focused Game tests use `DTXMania.Test.Mac.csproj` with the same `--filter` expressions as the plan. Do not claim local execution of `DTXMania.E2E` on macOS; that project targets `net8.0-windows7.0`.

### Windows / CI

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
```

Normal Windows gameplay E2E remains the final black-box gate in CI.

## Testing strategy

Extend existing owning suites; do not add a new harness.

- config: get-only empty default, one/all-lane mutation, invalid lane rejection, sparse `AutoPlay.0..9` SQLite round-trip, obsolete bare-key warning;
- Config UI: Toggle formatter preserves ordinary ON/OFF behavior; master shows None/Mixed/All; exactly 10 gameplay-lane rows mutate matching integers;
- judgement: physical hit ignored only on automated lane;
- performance: `PerformanceStageDeterministicTests` owns freeze, real `ProcessAutoPlay`, cursor advancement, lane-scoped feedback/input/gauge, and all-lanes telemetry; `PerformanceStageAdditionalCoverageTests` updates existing assumptions;
- score: `PerformanceSummaryTests` proves manual runs remain savable and partial/full AutoPlay runs are not;
- diagnostics: crash context publishes `AutoPlayLaneCount`, policy accepts the count and no longer expects the global bool;
- recorder: sandbox loops all 10 integer keys; source DB fixture contains no obsolete global key;
- E2E: full-AutoPlay fixture imports all 10 lanes, MIDI fixture imports none, recorder compatibility proves all 10 through real ConfigManager;
- final repository scan covers Game/Test, VideoRecorder/tests, E2E, Automation/tests. Existing `AutoPlayEnabled` telemetry references are expected to remain.

## Expected production touch points

Game production:

- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/IConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigItems.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`
- `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs`

Recorder/E2E production/bootstrap:

- `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`
- `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- `DTXMania.E2E/MidiGameplaySmokeTests.cs`

Expected existing test/fake fallout includes:

- config suites plus `ConfigDataApiSettingsTests`, `ConfigManagerSkinPathTests`;
- `BaseGameTests`, `DrumConfigStageTests`, crash-reporting tests;
- `JudgementManagerTests`, `PerformanceStageDeterministicTests`, `PerformanceStageAdditionalCoverageTests`, `PerformanceSummaryTests`;
- `RecordingSandboxTests`, `TestSourceConfigDatabase` consumers;
- `E2EFixtureBuilderTests`, `RecorderConfigCompatibilityTests`.

Do not modify `KeyBindings.GetLaneName` or `DrumKitLayout` as part of this ticket. If implementation proves AutoPlay cannot use the parser/performance integer lane identity without changing the HPA-619 mapping contract, stop and spin that out rather than merging two lane domains into HPA-18.

## Size

Expected implementation remains about **2 engineer days**, with a hard ceiling of the existing three-day task budget. The revised shape removes the new lane-definition component and textual cross-project lane-code contract while making score/diagnostics/test migration explicit.