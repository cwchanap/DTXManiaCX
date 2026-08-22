# HPA-18 Per-Pad Drums AutoPlay Implementation Plan

> **PR rule:** Continue implementation on this branch and this draft PR. Do not open a second PR for HPA-18.

**Goal:** Replace the single global Drums AutoPlay flag with a frozen 10-lane AutoPlay set that supports mixed manual/automatic play while preserving recorder automation.

**Architecture:** `ConfigData` owns the mutable configured lane set and `ConfigManager` owns persistence. `PerformanceStage` copies that set at activation and remains the orchestration owner for automatic note resolution. `JudgementManager` only filters physical input for automated lanes. Reuse current integer lane identities and existing judgement/gauge/rendering flows; do not add a new lane model or event-source framework.

**Tech:** .NET 8, MonoGame, xUnit, SQLite-backed configuration.

## Task 1: Replace the persisted global AutoPlay setting with the 10-lane contract

**Production files:**
- Modify `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigManager.cs`

**Test files:**
- Modify `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- Update existing config-manager fakes/callers only where the interface change requires it

**Steps:**

1. Add failing tests for these contracts:
   - default `AutoPlayLanes` is empty;
   - one-lane enable/disable affects only that lane;
   - all-lanes enable creates exactly lanes `0..9` and all-lanes disable clears them;
   - invalid lane IDs are rejected by the config-manager mutation seam;
   - SQLite persistence round-trips the exact ten keys `AutoPlay.LC`, `.HH`, `.LP`, `.SN`, `.HT`, `.BD`, `.LT`, `.FT`, `.CY`, `.RD`;
   - the old global `AutoPlay` key is not required or written.
2. Run the focused tests and confirm the new cases fail for the expected missing behavior.
3. Replace `ConfigData.AutoPlay` with `HashSet<int> AutoPlayLanes`.
4. Replace `IConfigManager.SetAutoPlay(bool)` with narrow lane/all-lanes mutation operations. Do not retain a compatibility overload.
5. Load/write the ten explicit keys. Keep parsing and SQLite snapshot behavior on the existing `ConfigManager` paths; do not create a collection serializer or schema table.
6. Update only compile-broken fakes/callers to the new interface.
7. Re-run focused config tests until green.

**Focused validation:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests"
```

## Task 2: Add the tri-state master and ten per-lane Config rows

**Production files:**
- Modify `DTXMania.Game/Lib/Config/ConfigItems.cs`
- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs`

**Test files:**
- Modify `DTXMania.Test/Config/ConfigItemTests.cs`
- Modify `DTXMania.Test/Config/ConfigStageLogicTests.cs`

**Steps:**

1. Add failing tests for master display and action semantics:
   - zero enabled -> `AutoPlay (All): None`;
   - ten enabled -> `AutoPlay (All): All`;
   - any other count -> `AutoPlay (All): Mixed`;
   - activating `All` clears all lanes;
   - activating `None` or `Mixed` enables all lanes.
2. Add failing ConfigStage tests that pin the ten rows in lane order and verify each row updates only its matching lane.
3. Add one small tri-state toggle config-item type that accepts a computed display value and one toggle action. `PreviousValue`, `NextValue`, and `ToggleValue` should all execute the same action, matching existing ConfigStage interaction conventions.
4. Replace the global Drums `Auto Play` row with:
   - `AutoPlay (All)`;
   - Left Cymbal;
   - HiHat;
   - Left Pedal;
   - Snare;
   - High Tom;
   - Bass Drum;
   - Low Tom;
   - Floor Tom;
   - Cymbal;
   - Ride.
5. Add a concise description for Left Pedal noting that CX lane 2 includes both DTX Left Pedal and Left Bass Drum events. Do not add an eleventh switch.
6. Re-run focused ConfigStage/config-item tests until green.

**Focused validation:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~ConfigItemTests|FullyQualifiedName~ConfigStageLogicTests"
```

## Task 3: Make physical-input suppression lane-scoped

**Production file:**
- Modify `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`

**Test file:**
- Modify `DTXMania.Test/Stage/Performance/JudgementManagerTests.cs`

**Steps:**

1. Add a failing characterization showing that when one lane is automated:
   - a physical hit on that lane is ignored;
   - a physical hit on another lane is still judged normally.
2. Replace the global `IgnorePlayerInput` switch with a frozen/read-only lane collection supplied by the performance stage (or an equivalent minimal lane predicate).
3. Keep `ResolveAutoHit(noteId)` and `JudgementEvent` unchanged. Do not add an AutoPlay/manual source field.
4. Re-run `JudgementManagerTests` until green.

**Focused validation:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~JudgementManagerTests"
```

## Task 4: Convert PerformanceStage to mixed per-lane AutoPlay

**Production file:**
- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`

**Primary test file:**
- Modify `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`

**Other existing Performance tests:** update only when they directly compile against or characterize the removed global flag.

**Steps:**

1. Add failing tests/characterizations for one mixed chart containing at least one automated-lane note and one manual-lane note:
   - due automated note resolves Perfect automatically;
   - due manual note is not auto-resolved and remains available to normal judgement/miss handling;
   - physical feedback remains active for manual lanes and suppressed for automated lanes;
   - changing configured AutoPlay lanes after performance activation does not change the running performance;
   - manual-lane judgements affect gauge normally;
   - automated-lane judgements affect gauge only when `AutoAddGauge` is true.
2. Replace `_autoPlayEnabled` with a defensive copy of the configured `AutoPlayLanes` captured during performance activation.
3. Supply that frozen lane set to `JudgementManager` for physical-input filtering.
4. Keep one `_autoPlayNoteIndex` scan. When a due note is reached, auto-resolve/trigger it only if its lane is in the frozen set; otherwise advance the scan without resolving it. Let existing judgement/miss ownership handle the manual note.
5. Run the AutoPlay scan only when the frozen set is non-empty.
6. Make manual pad-feedback suppression lane-scoped rather than global.
7. Change the gauge condition to `manual lane || AutoAddGauge`, using `JudgementEvent.Lane` and the frozen set.
8. Keep the existing telemetry field and set `AutoPlayEnabled` true only when all ten lanes are automated. Do not change the telemetry model.
9. Re-run focused performance/judgement tests until green.

**Focused validation:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~AutomatedPlaySimulationTests|FullyQualifiedName~JudgementManagerTests"
```

## Task 5: Preserve recorder all-AutoPlay behavior

**Production file:**
- Modify `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`

**Test file:**
- Modify `DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs`

**Steps:**

1. Add/update a failing sandbox test showing that a recorder launch requests all ten `AutoPlay.*` keys and does not rely on the removed global `AutoPlay=True` entry.
2. Patch all ten lane keys to `True` in the sandbox configuration using the existing in-memory config patch flow.
3. Keep the recorder workflow and Game API contract unchanged; its existing `AutoPlayEnabled` wait/verification should continue to mean that the game entered full AutoPlay.
4. Run the recorder suite.

**Focused validation:**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
```

## Task 6: Whole-slice cleanup and validation

1. Search for stale global assumptions:

```bash
rg "\bAutoPlay\b|SetAutoPlay|IgnorePlayerInput|_autoPlayEnabled" DTXMania.Game DTXMania.Test DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests
```

Review every hit. Remove obsolete global-setting code/tests rather than keeping compatibility shims. Do not mechanically rename unrelated `AutoPlayEnabled` telemetry or `ResolveAutoHit` APIs.

2. Confirm no production changes landed outside HPA-18's expected config/performance/judgement/recorder seams unless required by compile fallout.
3. Run the complete game test project:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

4. Run the complete recorder test project:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

5. Build the game target used by the current development platform and ensure no errors are introduced.
6. Run `git diff --check` and inspect the final diff against the design non-goals.
7. Keep all implementation commits on this branch and update this existing draft PR. Do not create an implementation PR.

## Acceptance checklist

- [ ] Config persists exactly ten CX Drums AutoPlay lane keys; the old global key is no longer authoritative.
- [ ] Config UI shows `AutoPlay (All)` as `None`, `Mixed`, or `All` and offers exactly ten lane toggles.
- [ ] Lane 2 is one Left Pedal AutoPlay setting covering both DTX `0x1B` and `0x1C` inputs.
- [ ] Performance freezes the configured AutoPlay lane set on activation.
- [ ] Mixed automated/manual lanes work concurrently without manual input being globally disabled.
- [ ] AutoPlay scheduling reuses one scan and does not auto-resolve manual-lane notes.
- [ ] `AutoAddGauge` applies only to automated-lane judgements; manual lanes retain normal gauge behavior.
- [ ] Recorder sandbox enables all ten lanes and existing all-AutoPlay telemetry remains valid.
- [ ] No new lane abstraction, judgement-source model, renderer redesign, telemetry schema, Guitar/Bass work, or legacy migration layer is added.
- [ ] Full game and recorder tests pass.

## Expected size

One PR, approximately **1.5-2 engineer days**. If implementation reveals that lane 2 must be split into separate Left Pedal/Left Bass Drum runtime lanes, stop: that is a broader gameplay-model task and should not be absorbed into HPA-18.