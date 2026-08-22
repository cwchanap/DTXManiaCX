# HPA-18 Per-Pad Drums AutoPlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **PR rule:** Continue implementation on this branch and this draft PR. Do not open a second PR for HPA-18.

**Goal:** Replace the single global Drums AutoPlay flag with a frozen 10-lane AutoPlay set that supports mixed manual/automatic play while preserving recorder and gameplay-E2E full-AutoPlay behavior.

**Architecture:** One small Game-side lane-definition table reuses the existing `PerformanceUILayout.LaneType` identity and supplies config labels/keys to `ConfigManager`, `ConfigStage`, and Game-referencing E2E fixtures. `ConfigData` owns a get-only mutable set; `ConfigManager` is the mutation/persistence owner. `PerformanceStage` defensively copies the set at activation and keeps the current single AutoPlay scan. `JudgementManager` only filters physical input for automated lanes. The recorder keeps its intentional no-Game-dependency boundary and proves its local ten-key patch through the existing real-`ConfigManager` compatibility test.

**Tech Stack:** .NET 8, MonoGame, xUnit, SQLite-backed configuration.

**Spec:** `docs/superpowers/specs/2026-08-21-hpa-18-per-pad-autoplay-design.md`

## Global constraints

- Drums only; exactly the current 10 CX gameplay lanes.
- Canonical lane order/codes are `LC, HH, LP, SN, HT, DB, LT, FT, CY, RD`; lane 5 persists as `AutoPlay.DB`, not `AutoPlay.BD`.
- DTX `0x1B` and `0x1C` remain one lane-2 Left Pedal setting.
- No second lane enum/domain refactor, judgement-source field, NoteRenderer change, telemetry schema expansion, Guitar/Bass work, or legacy global-AutoPlay migration.
- `AutoPlayLanes` is get-only; running performance receives a defensive copy, never the live ConfigData set.
- Keep one AutoPlay scan and advance past manual notes so they cannot block later automated notes.
- Do not add a `DTXMania.VideoRecorder -> DTXMania.Game` project dependency merely to share AutoPlay constants.
- One HPA-18 PR only.

---

### Task 1: Establish the canonical ten-lane config contract and persistence

**Files:**
- Create: `DTXMania.Game/Lib/Config/AutoPlayLaneDefinitions.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Test: `DTXMania.Test/Config/ConfigDataTests.cs`
- Test: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Test: `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`

**Interfaces:**
- Canonical table: ten entries in lane order, each carrying existing `PerformanceUILayout.LaneType` plus Config label; lane ID comes from the enum value and suffix/key comes from the existing lane identity.
- `ConfigData.AutoPlayLanes`: get-only `HashSet<int>`, empty by default.
- `IConfigManager.SetAutoPlayLane(int lane, bool enabled)`: mutate one valid canonical lane and mark deferred save when state changes.
- `IConfigManager.SetAllAutoPlayLanes(bool enabled)`: enable exactly all ten canonical lanes or clear the set, then mark deferred save when state changes.

- [ ] **Step 1: Write failing canonical-table/config-model tests**

Pin all of these before production edits:

```text
AutoPlay definitions count == 10
lanes == 0..9 in order and unique
suffixes == LC,HH,LP,SN,HT,DB,LT,FT,CY,RD
suffix for lane 5 == DB
suffixes match PerformanceUILayout.LaneType.ToString() and LaneNames[lane]
new ConfigData().AutoPlayLanes is empty and cannot be replaced through a public setter
```

- [ ] **Step 2: Run the focused config tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests"
```

Expected: failures are limited to missing per-lane AutoPlay model/table/persistence behavior.

- [ ] **Step 3: Add the Game-side AutoPlay lane-definition table**

Keep it configuration metadata only. Reuse `PerformanceUILayout.LaneType`; do not create another lane enum. The only new authored per-lane data should be the Config display labels. Derive the lane integer and persisted suffix/key from existing lane identity.

- [ ] **Step 4: Replace the global config property with a protected collection**

Remove `ConfigData.AutoPlay`. Add get-only `AutoPlayLanes`, following the same collection-safety reasoning as get-only `MidiVelocityThresholds`.

- [ ] **Step 5: Replace `SetAutoPlay(bool)` with lane/all-lane mutations**

Reject/ignore lane values outside the canonical table. Mutate the existing set in place and use the current deferred `MarkDirty()` persistence pattern. Do not expose a whole-set replacement API.

- [ ] **Step 6: Replace the global load/write path with the canonical ten keys**

`BuildPersistedEntries()` iterates the canonical table and writes the ten explicit keys. Config loading recognizes only canonical `AutoPlay.*` entries and adds enabled lanes to the set. Remove the old `AutoPlay` switch case/write. Do not migrate it.

- [ ] **Step 7: Add round-trip and mutation tests**

Prove one-lane enable/disable, all-lanes enable/clear, invalid-lane rejection, exact ten-key SQLite round-trip including `AutoPlay.DB`, and that the legacy global `AutoPlay` key is neither required nor written.

- [ ] **Step 8: Re-run focused tests until GREEN**

Use the Step 2 command.

---

### Task 2: Build Config-stage rows from the canonical table

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigItems.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Test: `DTXMania.Test/Config/ConfigItemTests.cs`
- Test: `DTXMania.Test/Config/ConfigStageLogicTests.cs`

**Interfaces:**
- New focused computed master item: display is `None`, `Mixed`, or `All`; previous/next/toggle invoke one supplied action.
- Per-lane rows consume the Task 1 canonical lane table and `IConfigManager.SetAutoPlayLane`.

- [ ] **Step 1: Add failing master-item tests**

Pin:

```text
0 enabled -> AutoPlay (All): None
10 enabled -> AutoPlay (All): All
1..9 enabled -> AutoPlay (All): Mixed
All action -> clear all
None action -> enable all
Mixed action -> enable all
Previous/Next/Toggle all invoke the same master action
```

- [ ] **Step 2: Add failing ConfigStage row tests**

Assert one master plus exactly ten lane rows in canonical order, with lane 5 using the existing DB identity and the Left Pedal row documenting that lane 2 includes both DTX `0x1B` and `0x1C`.

- [ ] **Step 3: Run focused tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~ConfigItemTests|FullyQualifiedName~ConfigStageLogicTests"
```

- [ ] **Step 4: Add the small computed master config item**

Do not use `DropdownConfigItem`: `Mixed` is computed state, not a selectable value. Do not create a general tri-state framework.

- [ ] **Step 5: Generate lane rows from the Task 1 table**

Remove the old global AutoPlay row. Do not hand-author a second lane/suffix mapping inside `ConfigStage`.

- [ ] **Step 6: Re-run focused tests until GREEN**

Use the Step 3 command.

---

### Task 3: Make physical judgement input lane-scoped

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`
- Test: `DTXMania.Test/Stage/Performance/JudgementManagerTests.cs`

**Interfaces:**
- `ResolveAutoHit(noteId)` and `JudgementEvent` stay unchanged.
- Replace global `IgnorePlayerInput` behavior with the minimum lane collection/predicate needed by `OnLaneHit`.

- [ ] **Step 1: Add a failing two-lane characterization**

With one lane automated, prove a physical hit on that lane is ignored while a hit on a different manual lane is queued/judged normally.

- [ ] **Step 2: Run `JudgementManagerTests` and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~JudgementManagerTests"
```

- [ ] **Step 3: Replace the global gate with lane-scoped filtering**

Keep filtering at `OnLaneHit`; do not add an AutoPlay/manual source field or redesign judgement processing.

- [ ] **Step 4: Re-run `JudgementManagerTests` until GREEN**

Use the Step 2 command.

---

### Task 4: Convert the real PerformanceStage AutoPlay path to mixed lanes

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Primary test: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Supporting test: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`
- Do not use `AutomatedPlaySimulationTests.cs` as the primary proof; it injects `JudgementEvent`s and does not execute `ProcessAutoPlay`.

**Interfaces:**
- Performance-owned frozen set is initialized by defensive copy in `InitializeAutoPlay()`.
- The existing `_autoPlayNoteIndex` remains the only AutoPlay cursor.
- `AutoPlayEnabled` telemetry remains boolean and means all ten canonical lanes are automated.

- [ ] **Step 1: Retarget existing global AutoPlay characterizations in `PerformanceStageDeterministicTests`**

Add/adjust tests that execute the real stage path and pin:

```text
InitializeAutoPlay copies configured lanes rather than retaining ConfigData reference
post-activation config mutation does not change running AutoPlay ownership
JudgementManager receives lane-scoped ignored input
manual pad feedback remains active on manual lanes and is suppressed on automated lanes
AutoPlayEnabled telemetry is true only when all ten lanes are frozen as automated
```

- [ ] **Step 2: Add a failing mixed-chart `ProcessAutoPlay` characterization**

Use a chart ordered so a due manual note appears before a later due automated note. Prove the scan advances past the manual note without resolving it, then resolves the automated note Perfect. The manual note must remain available to the normal judgement/miss path.

- [ ] **Step 3: Add failing per-lane gauge tests**

Prove manual-lane judgements always reach `GaugeManager`, while automated-lane judgements reach it only when frozen `AutoAddGauge` is enabled.

- [ ] **Step 4: Run the owning performance suites and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests|FullyQualifiedName~JudgementManagerTests"
```

- [ ] **Step 5: Replace `_autoPlayEnabled` with a defensive frozen set**

`InitializeAutoPlay()` copies `Config.AutoPlayLanes` into a new stage-owned set and continues freezing `AutoAddGauge`, fail rules, and other HPA-10 state as today.

- [ ] **Step 6: Feed the frozen set into `JudgementManager` and pad-feedback filtering**

Physical judgement input and `OnLaneHitForPadFeedback` are suppressed only for lanes in the frozen set.

- [ ] **Step 7: Keep one AutoPlay scan and make resolution membership-based**

Run `ProcessAutoPlay` only when the frozen set is non-empty. For every due note, resolve/pad/chip only when its lane is automated, but always advance `_autoPlayNoteIndex` once the note is due.

- [ ] **Step 8: Make gauge and telemetry decisions from the frozen set**

Gauge condition is `manual lane || AutoAddGauge`. `AutoPlayEnabled` is true only when all ten canonical lanes are present; do not modify telemetry DTOs.

- [ ] **Step 9: Re-run focused stage tests until GREEN**

Use the Step 4 command.

---

### Task 5: Update recorder and E2E bootstrap consumers explicitly

**Files:**
- Modify: `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`
- Test: `DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs`
- Modify: `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- Test: `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`
- Modify: `DTXMania.E2E/MidiGameplaySmokeTests.cs`
- Test: `DTXMania.E2E/RecorderConfigCompatibilityTests.cs`

**Boundary:**
- `DTXMania.E2E` already references Game and may consume the Game-side lane-definition table.
- `DTXMania.VideoRecorder` intentionally does not reference Game. Keep its local ten-key override; do not add a Game reference or another shared assembly. `RecorderConfigCompatibilityTests` is the drift guard because it loads recorder output through the real `ConfigManager`.

- [ ] **Step 1: Add/update failing recorder sandbox tests**

Prove sandbox output writes all ten keys `AutoPlay.LC` through `AutoPlay.RD`, includes `AutoPlay.DB`, and does not emit the removed global `AutoPlay=True` entry.

- [ ] **Step 2: Update `RecordingSandbox` local owned values**

Replace the one global override with ten `True` values. Keep the existing patching flow and recorder project references unchanged.

- [ ] **Step 3: Retarget `RecorderConfigCompatibilityTests`**

After the real `ConfigManager` imports the sandbox INI, assert its `AutoPlayLanes` equals all ten canonical lanes instead of asserting `Config.AutoPlay`.

- [ ] **Step 4: Retarget the gameplay E2E fixture**

`E2EFixtureBuilder` writes the ten full-AutoPlay keys from the Game-side canonical table. `E2EFixtureBuilderTests` asserts those keys and asserts the imported `AutoPlayLanes` set contains all ten canonical lanes.

- [ ] **Step 5: Fix MIDI smoke manual-play bootstrap**

Remove the `.Replace("AutoPlay=True", "AutoPlay=False")` assumption. Produce a bootstrap with no enabled AutoPlay lanes so MIDI smoke continues to test physical judgement input.

- [ ] **Step 6: Run focused recorder/E2E support tests**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~E2EFixtureBuilderTests|FullyQualifiedName~RecorderConfigCompatibilityTests"
```

Expected: GREEN, with no new recorder -> Game production dependency.

---

### Task 6: Whole-slice stale-assumption sweep and validation

- [ ] **Step 1: Search every production/test project that can retain the global contract**

```bash
rg "\bAutoPlay\b|SetAutoPlay|IgnorePlayerInput|_autoPlayEnabled|AutoPlayEnabled" \
  DTXMania.Game DTXMania.Test \
  DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests \
  DTXMania.E2E \
  DTXMania.Automation DTXMania.Automation.Tests
```

Review every hit. Remove obsolete global-config assumptions. `AutoPlayEnabled` telemetry references are expected and must remain boolean; do not mechanically rename/remove them.

- [ ] **Step 2: Confirm no unplanned architecture expansion**

The implementation may touch config, ConfigStage, PerformanceStage/JudgementManager, recorder bootstrap, and E2E bootstrap/tests. It must not add a second lane enum, new gameplay manager, telemetry DTO fields, recorder -> Game reference, or compatibility migration.

- [ ] **Step 3: Run the complete Game test project**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

- [ ] **Step 4: Run the complete recorder test project**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

- [ ] **Step 5: Run E2E support/contract tests available on the development platform**

At minimum include `E2EFixtureBuilderTests`, `RecorderConfigCompatibilityTests`, and `AutomationContractTests`; the normal Windows gameplay E2E CI remains the authoritative runtime gate for the AutoPlay fixture and MIDI smoke flow.

- [ ] **Step 6: Build the Game target and inspect final diff**

Run the normal platform Game build, then:

```bash
git diff --check
```

Confirm the final diff follows the design non-goals and remains on this existing HPA-18 PR.

## Acceptance checklist

- [ ] One Game-side AutoPlay lane table reuses existing `LaneType`/`LaneNames`; no second lane enum exists.
- [ ] Config persists exactly ten CX Drums AutoPlay lane keys in order `LC, HH, LP, SN, HT, DB, LT, FT, CY, RD`.
- [ ] Lane 5 key is `AutoPlay.DB`; `AutoPlay.BD` is absent.
- [ ] `ConfigData.AutoPlayLanes` is get-only and defaults empty; config manager owns mutation.
- [ ] Config UI shows `AutoPlay (All)` as `None`, `Mixed`, or `All` plus exactly ten lane toggles generated from the canonical table.
- [ ] Lane 2 is one Left Pedal AutoPlay setting covering both DTX `0x1B` and `0x1C`.
- [ ] `InitializeAutoPlay()` defensively copies the configured set.
- [ ] `PerformanceStageDeterministicTests` prove mixed scanning, lane-scoped pad/input suppression, frozen ownership, per-lane gauge behavior, and all-lanes telemetry.
- [ ] One AutoPlay scan advances past manual notes and auto-resolves only automated lanes.
- [ ] Recorder sandbox enables all ten keys without adding a Game dependency, and real-ConfigManager compatibility proves the patch.
- [ ] E2E full-AutoPlay fixture uses all ten keys; MIDI smoke launches with no automated lanes.
- [ ] `DTXMania.Automation` keeps the existing boolean `AutoPlayEnabled` contract; no telemetry schema expansion is added.
- [ ] No Guitar/Bass work, legacy migration layer, judgement-source model, renderer redesign, or lane-domain refactor is added.
- [ ] Full Game/recorder tests plus relevant E2E support tests pass; Windows gameplay E2E is green before merge.

## Expected size

One PR, approximately **1.5-2 engineer days**. The revision adds explicit ownership for existing E2E/bootstrap call sites while avoiding new production abstractions or a recorder -> Game dependency.