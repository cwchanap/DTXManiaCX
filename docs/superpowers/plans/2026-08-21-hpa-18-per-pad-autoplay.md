# HPA-18 Per-Pad Drums AutoPlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **PR rule:** Continue implementation on this branch and this draft PR. Do not open a second PR for HPA-18.

**Goal:** Replace global Drums AutoPlay with a frozen 10-lane AutoPlay set that supports mixed manual/automatic play, excludes assisted runs from score persistence, and preserves recorder/E2E full-AutoPlay behavior.

**Architecture:** Use integer gameplay lane IDs directly. `ConfigData` owns a get-only `AutoPlayLanes` set and `ConfigManager` persists enabled lanes as sparse `AutoPlay.0..9=true` rows, mirroring `Key.Unbound.{lane}`. `PerformanceStage` snapshots the set, keeps the existing single AutoPlay cursor, and supplies lane-scoped input filtering to `JudgementManager`. `PerformanceSummary` records whether AutoPlay was used so existing `IsSavable` gating rejects assisted runs. Recorder/E2E generate full-AutoPlay keys with `0..9` loops; no shared lane-definition component or recorder -> Game dependency is added.

**Tech Stack:** .NET 8, MonoGame, xUnit, SQLite-backed configuration.

**Spec:** `docs/superpowers/specs/2026-08-21-hpa-18-per-pad-autoplay-design.md`

## Global constraints

- Drums only; exactly integer lanes `0..9` in current parser/performance order: LC, HH, LP, SN, HT, DB, LT, FT, CY, RD.
- Persist enabled lanes as sparse `AutoPlay.{lane}=true`; do not introduce textual lane-code keys or `AutoPlayLaneDefinitions.cs`.
- DTX `0x1B` and `0x1C` remain one lane-2 Left Pedal setting.
- `AutoPlayLanes` is get-only; running performance receives a defensive copy.
- Keep one AutoPlay cursor and advance past due manual notes.
- Any run with at least one automated lane is not score-savable.
- No new lane enum/refactor, judgement-source field, renderer redesign, telemetry schema, Guitar/Bass work, config migration, AutoPlay sub-screen, or recorder -> Game project reference.
- Do not change `KeyBindings.GetLaneName` or `DrumKitLayout`; HPA-619 owns the separate Drum Key Mapping visual/binding contract.
- Implementation order is **add -> migrate -> delete** so every intermediate focused test gate can compile and become GREEN.
- One HPA-18 PR only.

## Validation command matrix

Use the project matching the machine. Every focused Game command below uses the same filter with one of these projects.

**macOS Game tests:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "<FILTER>"
```

**Windows Game tests:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "<FILTER>"
```

**Recorder tests (cross-platform):**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

`DTXMania.E2E` targets `net8.0-windows7.0`; run its support/gameplay tests on Windows/CI only. Do not skip a Mac Game gate by substituting the Windows test project.

---

### Task 1: Add the per-lane config model beside the legacy global contract

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Test: `DTXMania.Test/Config/ConfigDataTests.cs`
- Test: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Test: `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`

**Interfaces produced for later tasks:**

```csharp
public HashSet<int> AutoPlayLanes { get; } = new();
void SetAutoPlayLane(int lane, bool enabled);
void SetAllAutoPlayLanes(bool enabled);
```

For this task only, keep existing `ConfigData.AutoPlay`, `IConfigManager.SetAutoPlay(bool)`, the bare `AutoPlay` load/write path, and their existing tests. They are transitional branch scaffolding and are deleted in Task 6.

- [ ] **Step 1: Write failing model/mutator tests**

Pin:

```text
new ConfigData().AutoPlayLanes is empty
AutoPlayLanes has no public setter
SetAutoPlayLane(3,true) adds only lane 3
SetAutoPlayLane(3,false) removes lane 3
invalid lanes (<0 or >9) do not mutate/dirty the set
SetAllAutoPlayLanes(true) yields exactly 0..9
SetAllAutoPlayLanes(false) clears the set
no-op mutations do not create extra dirty work
```

- [ ] **Step 2: Run focused config tests and confirm RED**

Filter:

```text
FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests
```

Expected: failure only for missing per-lane APIs/behavior; the project still compiles because the legacy global API remains.

- [ ] **Step 3: Add the get-only set and narrow manager mutations**

Use integer bounds `0..9`. Mutate the collection in place and call the existing deferred `MarkDirty()` only when membership actually changes. Do not add a whole-set replacement API.

- [ ] **Step 4: Add sparse integer persistence beside the legacy row**

Use a private `AutoPlay.` prefix in `ConfigManager`, matching the existing `Key.Unbound.` pattern:

```text
enabled lane 0 -> AutoPlay.0=true
enabled lane 9 -> AutoPlay.9=true
disabled lane -> no AutoPlay.<lane> row
```

Load only numeric suffixes `0..9` whose value parses true. Malformed/out-of-range entries do not enter the set.

During this task, continue writing/loading the old bare `AutoPlay` row as well so unrelated existing consumers still compile and behave until migrated.

- [ ] **Step 5: Add SQLite round-trip tests**

Prove empty, one-lane, and all-lane round trips. Assert there is no `AutoPlayLaneDefinitions`/textual code contract and enabled rows are `AutoPlay.0..9` only.

- [ ] **Step 6: Re-run the focused config tests until GREEN**

Use the Step 2 filter with the platform-appropriate Game test project.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Config DTXMania.Test/Config
git commit -m "feat: add per-lane autoplay config model"
```

---

### Task 2: Migrate ConfigStage to inline per-lane controls

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigItems.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Test: `DTXMania.Test/Config/ConfigItemTests.cs`
- Test: `DTXMania.Test/Config/ConfigStageLogicTests.cs`

**Interfaces consumed:** Task 1 `AutoPlayLanes`, `SetAutoPlayLane`, `SetAllAutoPlayLanes`.

**UI contract:** keep the rows in the existing scrolling Drums category. Do not add a sub-screen. Use these local gameplay labels in lane order:

```text
Left Cymbal, Hi-Hat, Left Pedal, Snare, High Tom,
Bass Drum, Low Tom, Floor Tom, Cymbal, Ride
```

Do not use `KeyBindings.GetLaneName`; that belongs to the separate Drum Key Mapping contract.

- [ ] **Step 1: Add failing `ToggleConfigItem` formatter tests**

Extend the existing class rather than create a tri-state class. Tests prove:

```text
ordinary ToggleConfigItem still renders ON/OFF when no formatter is supplied
optional formatter can replace only the displayed value
Previous/Next/Toggle still invert the bool and raise ValueChanged
```

- [ ] **Step 2: Add failing ConfigStage AutoPlay tests**

Pin:

```text
master display: 0 -> None, 1..9 -> Mixed, 10 -> All
master action: All -> clear, None/Mixed -> enable all
exactly 10 gameplay-lane rows appear in integer order 0..9
one lane row mutates only its matching lane
Left Pedal description states DTX 0x1B and 0x1C share lane 2
Drums category remains one scrolling list; no AutoPlay navigation item/stage is introduced
```

- [ ] **Step 3: Run focused UI tests and confirm RED**

Filter:

```text
FullyQualifiedName~ConfigItemTests|FullyQualifiedName~ConfigStageLogicTests
```

- [ ] **Step 4: Add optional display formatting to `ToggleConfigItem`**

Follow the existing optional-formatter pattern from `IntegerConfigItem`. The master getter is true only when all 10 lanes are enabled; its formatter derives `None/Mixed/All` from the current count. This makes Mixed naturally toggle to All without a new config-item type.

- [ ] **Step 5: Replace the ConfigStage global row with master + 10 rows**

Use a small local label array/list in `ConfigStage`; persistence keys are integer-derived and are not part of UI metadata. Per-lane rows call `SetAutoPlayLane`.

Do **not** delete the legacy config property/API yet; other projects still use it until Task 6.

- [ ] **Step 6: Re-run focused UI tests until GREEN**

Use the Step 3 filter and platform-appropriate Game test project.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Config/ConfigItems.cs DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Test/Config
git commit -m "feat: add per-lane autoplay settings UI"
```

---

### Task 3: Add lane-scoped judgement input filtering without deleting the global gate yet

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`
- Test: `DTXMania.Test/Stage/Performance/JudgementManagerTests.cs`

**Interface produced:** add the minimum copy-owning seam needed to configure ignored physical lanes, for example:

```csharp
internal void SetIgnoredPlayerInputLanes(IEnumerable<int> lanes)
```

It copies the supplied lane IDs into JudgementManager-owned state. For this task only, retain `IgnorePlayerInput`; `OnLaneHit` rejects input when the transitional global gate is true **or** the hit lane is in the ignored set. Task 6 removes the global gate after PerformanceStage is migrated.

- [ ] **Step 1: Add failing two-lane input tests**

Prove:

```text
ignored lane physical hit is dropped
manual lane physical hit is still queued/judged
changing the caller's source collection after SetIgnoredPlayerInputLanes does not mutate JudgementManager's copy
existing IgnorePlayerInput=true behavior remains temporarily green
```

- [ ] **Step 2: Run `JudgementManagerTests` and confirm RED**

Filter:

```text
FullyQualifiedName~JudgementManagerTests
```

- [ ] **Step 3: Implement the lane filter beside the legacy bool**

Keep `ResolveAutoHit(noteId)` and `JudgementEvent` unchanged. Do not add source/origin metadata.

- [ ] **Step 4: Re-run `JudgementManagerTests` until GREEN**

Use the Step 2 filter and platform-appropriate Game test project.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs DTXMania.Test/Stage/Performance/JudgementManagerTests.cs
git commit -m "feat: filter autoplay input by lane"
```

---

### Task 4: Migrate real gameplay to frozen mixed AutoPlay and make assisted runs unsavable

**Production files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs`

**Test files:**
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceSummaryTests.cs`
- Modify `DTXMania.Test/Stage/ResultStageTests.cs` only where needed to prove existing `IsSavable` gating does not call persistence for an assisted summary.

Do not use `AutomatedPlaySimulationTests` as the primary proof; it fabricates judgement events and never executes `PerformanceStage.ProcessAutoPlay`.

**Interface addition:**

```csharp
public bool UsedAutoPlay { get; init; }
```

`PerformanceSummary.IsSavable` additionally requires `!UsedAutoPlay`.

- [ ] **Step 1: Retarget/add failing stage characterizations**

In `PerformanceStageDeterministicTests`, prove:

```text
InitializeAutoPlay copies Config.AutoPlayLanes into stage-owned state
later config mutation cannot alter running ownership
JudgementManager receives automated lanes through lane-scoped input filtering
manual pad feedback remains active only on manual lanes
AutoPlayEnabled telemetry is true only when all 10 lanes are automated
```

- [ ] **Step 2: Add the critical one-cursor mixed-chart RED case**

Arrange a due manual note before a due automated note. Invoke the real AutoPlay path and prove:

```text
manual note remains pending for normal judgement/miss handling
autoplay cursor advances past it
later automated note resolves Perfect
```

This prevents an implementation that stalls on the first manual note.

- [ ] **Step 3: Add failing gauge tests**

Manual-lane judgements always reach GaugeManager. Automated-lane judgements reach it only when frozen `AutoAddGauge` is true.

- [ ] **Step 4: Add failing score-savability tests**

Pin:

```text
manual completed/failed run with RunId remains savable
UsedAutoPlay=true makes partial/full assisted run unsavable
PerformanceStage.FinalizePerformance sets UsedAutoPlay when frozen set is non-empty
ResultStage does not invoke SavePerformanceSummaryAsync for an assisted summary
```

- [ ] **Step 5: Run owning stage/result tests and confirm RED**

Filter:

```text
FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests|FullyQualifiedName~JudgementManagerTests|FullyQualifiedName~PerformanceSummaryTests|FullyQualifiedName~ResultStageTests
```

- [ ] **Step 6: Replace `_autoPlayEnabled` gameplay ownership with a defensive lane set**

`InitializeAutoPlay()` copies `Config.AutoPlayLanes`. Feed that frozen set to JudgementManager. Do not retain the live ConfigData collection.

- [ ] **Step 7: Keep one scheduler cursor**

Run `ProcessAutoPlay` only when the frozen set is non-empty. Once a note is due, always advance `_autoPlayNoteIndex`; resolve/pad/chip only if the note lane is automated.

- [ ] **Step 8: Make feedback, gauge, telemetry, and summary decisions from the frozen set**

- physical pad feedback suppressed only on automated lanes;
- gauge condition is `manual lane || AutoAddGauge`;
- telemetry `AutoPlayEnabled` is true only for all 10 lanes;
- `PerformanceSummary.UsedAutoPlay` is true for any non-empty frozen set;
- `PerformanceSummary.IsSavable` rejects any assisted run.

Do not add a ResultStage-specific AutoPlay branch or score DB/schema field.

- [ ] **Step 9: Re-run the focused stage/result tests until GREEN**

Use the Step 5 filter and platform-appropriate Game test project.

- [ ] **Step 10: Commit**

```bash
git add DTXMania.Game/Lib/Stage DTXMania.Test/Stage
git commit -m "feat: support mixed-lane autoplay gameplay"
```

---

### Task 5: Migrate diagnostics, recorder, and E2E/bootstrap consumers

**Game diagnostics:**
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs`
- Test: `DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs`
- Test: `DTXMania.Test/BaseGameTests.cs`

**Recorder:**
- Modify: `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`
- Modify: `DTXMania.VideoRecorder.Tests/TestSourceConfigDatabase.cs`
- Test: `DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs`

**Windows E2E/bootstrap:**
- Modify: `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- Test: `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`
- Modify: `DTXMania.E2E/MidiGameplaySmokeTests.cs`
- Test: `DTXMania.E2E/RecorderConfigCompatibilityTests.cs`

- [ ] **Step 1: Add failing crash-context tests**

Replace the global boolean expectation with:

```text
AutoPlayLaneCount == config.AutoPlayLanes.Count
CrashLogFieldPolicy accepts AutoPlayLaneCount through count normalization
obsolete configuration-context AutoPlay bool is no longer required after final cleanup
```

- [ ] **Step 2: Migrate crash configuration context**

Publish `AutoPlayLaneCount`; handle it beside `UnboundDrumLaneCount` through `TryNormalizeCount`. Do not publish individual lanes.

- [ ] **Step 3: Add/update recorder bootstrap tests**

Prove `RecordingSandbox` emits `AutoPlay.0=True` through `AutoPlay.9=True` using generated integer keys and no dependency on Game. Remove `AutoPlay=False` from `TestSourceConfigDatabase.BuildValidRows`; absence means manual play.

- [ ] **Step 4: Implement recorder full-AutoPlay with a `0..9` loop**

Do not hand-copy textual lane codes. Keep existing config patch flow and project references unchanged.

- [ ] **Step 5: Retarget recorder compatibility E2E**

After importing recorder output through real `ConfigManager`, assert `AutoPlayLanes` equals exactly `0..9` instead of asserting `Config.AutoPlay`.

- [ ] **Step 6: Retarget gameplay E2E bootstrap**

`E2EFixtureBuilder` generates `AutoPlay.0=True` through `.9=True` with a loop. `E2EFixtureBuilderTests` asserts real ConfigManager imports exactly all 10 lanes. `MidiGameplaySmokeTests` stops replacing one `AutoPlay=True` string and instead runs from a fixture with no `AutoPlay.{lane}` rows.

- [ ] **Step 7: Run cross-platform focused diagnostics/recorder tests**

Game filter:

```text
FullyQualifiedName~BaseGameTests|FullyQualifiedName~CrashLogFieldPolicyTests
```

Recorder:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
```

These gates must be GREEN on the current platform before final legacy deletion.

- [ ] **Step 8: Run Windows-only E2E support tests when on Windows/CI**

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~E2EFixtureBuilderTests|FullyQualifiedName~RecorderConfigCompatibilityTests"
```

On macOS, record this as a Windows CI gate; do not claim local execution.

- [ ] **Step 9: Commit**

```bash
git add DTXMania.Game/Lib/Diagnostics DTXMania.Test/CrashReporting DTXMania.Test/BaseGameTests.cs \
  DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests DTXMania.E2E
git commit -m "test: migrate autoplay diagnostics and bootstraps"
```

---

### Task 6: Delete the legacy global contract and clean compile-only callers

**Production files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`

**Known test/fake fallout to inspect explicitly:**
- `DTXMania.Test/BaseGameTests.cs`
- `DTXMania.Test/Config/ConfigDataApiSettingsTests.cs`
- `DTXMania.Test/Config/ConfigManagerTests.cs`
- `DTXMania.Test/Config/ConfigManagerSkinPathTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`
- `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`
- `DTXMania.E2E/RecorderConfigCompatibilityTests.cs`

- [ ] **Step 1: Search the whole relevant repository before deletion**

```bash
rg "\bAutoPlay\b|SetAutoPlay|IgnorePlayerInput|_autoPlayEnabled|AutoPlayEnabled" \
  DTXMania.Game DTXMania.Test \
  DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests \
  DTXMania.E2E DTXMania.Automation DTXMania.Automation.Tests
```

Classify each hit before editing. `AutoPlayEnabled` telemetry and `ResolveAutoHit` are expected to remain.

- [ ] **Step 2: Retarget tests that used AutoPlay only as a dirty-save lever**

For persistence/lifecycle tests such as `ConfigManagerSkinPathTests` and the BaseGame exit-flush test, use an unrelated existing deferred setter such as `SetMetronome` and assert that setting's persisted result. Do not preserve a fake global AutoPlay API just to satisfy those tests.

- [ ] **Step 3: Remove obsolete DTO/interface/tests/fake members**

Delete:

```text
ConfigData.AutoPlay
IConfigManager.SetAutoPlay(bool)
ConfigManager.SetAutoPlay(bool)
old tests that only prove the removed global property
hand-written fake implementations of SetAutoPlay
```

- [ ] **Step 4: Remove global persistence but leave an explicit obsolete-key warning**

`BuildPersistedEntries()` no longer writes bare `AutoPlay`.

The loader still recognizes the exact bare key only to log one warning such as:

```text
Ignoring obsolete global AutoPlay setting; configure AutoPlay.0 through AutoPlay.9 instead.
```

Do not translate its value into lanes and do not persist it again. Add a logger-backed config test proving the warning and unchanged empty lane set.

- [ ] **Step 5: Remove transitional global judgement suppression**

Delete `IgnorePlayerInput` now that PerformanceStage uses only lane-scoped ignored-input state. Keep `ResolveAutoHit` unchanged.

- [ ] **Step 6: Re-run the repository-wide search**

Expected remaining hits are intentional history/docs, lane config names, `ResolveAutoHit`, and boolean `AutoPlayEnabled` telemetry. No production consumer may read `Config.AutoPlay` or call `SetAutoPlay`.

- [ ] **Step 7: Run full macOS validation when on macOS**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

- [ ] **Step 8: Run full Windows validation when on Windows/CI**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
```

Then run the normal Windows gameplay E2E gate used by CI.

- [ ] **Step 9: Final hygiene review**

```bash
git diff --check
```

Confirm:

- no `AutoPlayLaneDefinitions.cs`;
- no recorder -> Game reference;
- no KeyBindings/DrumKitLayout lane-semantic edits;
- no AutoPlay sub-screen;
- no score/telemetry database schema additions;
- no global AutoPlay compatibility layer beyond the one warning-only parser case.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "refactor: remove global autoplay contract"
```

## Acceptance checklist

- [ ] `ConfigData.AutoPlayLanes` is get-only and defaults empty.
- [ ] Enabled lane persistence uses only sparse numeric `AutoPlay.0..9=true` rows.
- [ ] Bare global `AutoPlay` is not migrated or rewritten and emits one warning when encountered.
- [ ] Config UI shows one computed `None/Mixed/All` master plus exactly 10 inline gameplay-lane toggles.
- [ ] Existing ordinary `ToggleConfigItem` ON/OFF display remains unchanged.
- [ ] Lane 2 remains one Left Pedal switch covering DTX `0x1B` and `0x1C`.
- [ ] Running performance freezes a defensive copy of configured lanes.
- [ ] One AutoPlay cursor advances past due manual notes and auto-resolves only configured lanes.
- [ ] Physical input/pad feedback is suppressed only on automated lanes.
- [ ] Manual judgements affect gauge normally; automated judgements obey `AutoAddGauge`.
- [ ] Any partial/full AutoPlay run has `UsedAutoPlay=true` and is not score-savable.
- [ ] Crash config context reports `AutoPlayLaneCount`, not the removed global bool.
- [ ] Recorder/E2E full AutoPlay is generated with integer `0..9` loops; MIDI manual smoke enables none.
- [ ] Existing boolean `AutoPlayEnabled` telemetry still means all 10 lanes automated.
- [ ] No Game-side lane-definition component, new lane domain, recorder -> Game dependency, telemetry schema, or KeyBindings/DrumKitLayout scope expansion is added.
- [ ] Applicable macOS and Windows validation commands pass.

## Expected size

One PR, about **2 engineer days** and comfortably inside the three-day task ceiling. If implementation shows that gameplay AutoPlay cannot use parser/performance integer lane IDs without changing the separate HPA-619 Drum Key Mapping contract, stop and split that concern rather than expanding HPA-18.