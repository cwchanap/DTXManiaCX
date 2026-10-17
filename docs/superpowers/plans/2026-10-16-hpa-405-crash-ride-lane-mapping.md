# HPA-405 Crash/Ride Lane Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct DTX crash/ride channel placement so `0x16` is CY/lane 8 and `0x19` is RD/lane 9, while deleting obsolete secondary mapping paths that contradict the parser.

**Architecture:** Keep the existing gameplay lane model and make `DTXChartParser.ChannelToLaneMap` the executable channel-to-lane contract. Preserve the already-correct channel-based renderer lookup; remove unused lane-keyed renderer mapping and the unused `InputLane` channel table instead of maintaining parallel mappings. Keep the separate drum-binding/config mismatch in HPA-443.

**Tech Stack:** .NET 8, C#, xUnit, MonoGame rendering helpers already in the repository.

**Spec:** `docs/superpowers/specs/2026-10-16-hpa-405-crash-ride-lane-mapping-design.md`

## Global Constraints

- One PR for HPA-405; implementation continues on this branch/PR.
- Preserve gameplay lane order `LC, HH, LP, SN, HT, DB, LT, FT, CY, RD`.
- Canonical parser fix: `0x16 -> lane 8 (CY)` and `0x19 -> lane 9 (RD)`.
- Do not change `GetSpriteColumnForChannel` numeric behavior: `0x16 -> 6`, `0x19 -> 1` remains correct.
- Delete `DrawOverlayEffect` / `GetSpriteColumnForLane` rather than repairing their unused lane-to-art table.
- Delete the unused contradictory `InputLane` / `InputLaneExtensions` table; `JudgementEvent.ToString()` should print the raw lane integer.
- Keep HPA-443-owned binding/config files (`KeyBindings`, `DrumKitLayout`, `SongSelectionStage`) out of this PR.
- Do not add compatibility handling for the old incorrect parser mapping.
- No Tab Mode implementation in this PR.

## Review Focus

- `0x16` parses to lane 8, never lane 9.
- `0x19` parses to lane 9, never lane 8.
- Every other mapped drum channel retains its current parser lane.
- No second production/test channel-to-lane table survives with contradictory values.
- Channel-based sprite art remains unchanged while obsolete lane-keyed overlay mapping is removed.

---

### Task 1: Correct and pin the parser mapping

**Files:**
- Modify: `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/Note.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`
- Modify: `DTXMania.Test/Song/DTXChartParserAdditionalTests.cs`
- Modify: `DTXMania.Test/Song/DTXChartParserTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/JudgementManagerTests.cs`
- Modify: `DTXMania.Test/Song/ChartManagerTests.cs`

**Interfaces:**
- Consumes: existing `DTXChartParser.ParseAsync`, `Note.Channel`, and `Note.LaneIndex`.
- Produces: canonical parser mapping `0x16 -> 8`, `0x19 -> 9`; no new public API.

- [ ] **Step 1: Make the self-contained parser regression describe the canonical map**

In `DTXChartParserAdditionalTests.ParseAsync_WithDrumNotes_ShouldParseAllMappedChannels`, replace the count-only assertions with an expected channel-to-lane table covering every currently mapped drum channel. Assert each authored channel resolves to exactly one note with the expected `LaneIndex`, including:

```csharp
0x16 => 8
0x19 => 9
```

Keep the shared-lane cases (`0x11`/`0x18 -> 1`, `0x1B`/`0x1C -> 2`) in the same table.

In `DTXChartParserTests.Note_GetLaneName_ReturnsCorrectNames`, keep the lane-name strings but swap the CY/RD channel examples so lane 8 uses `0x16` and lane 9 uses `0x19`.

- [ ] **Step 2: Run the parser tests and verify RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXChartParserAdditionalTests|FullyQualifiedName~DTXChartParserTests"
```

Expected before production changes: the new table fails on the current `0x16 -> 9` / `0x19 -> 8` parser behavior.

- [ ] **Step 3: Apply the minimal live mapping correction and remove stale fixture assertions**

In `DTXChartParser.ChannelToLaneMap`:

- map `0x16` to lane 8 and label it crash/CY;
- map `0x19` to lane 9 and label it ride/RD;
- leave every other entry unchanged.

In `Note.cs`, correct the `LaneIndex` mapping summary so the tail is `0x17=7 (FT), 0x16=8 (CY), 0x19=9 (RD)`.

In `NoteRenderer.GetSpriteColumnForChannel`, change comments/docs only: numeric cases stay `0x16 -> 6`, `0x19 -> 1`.

Delete `DTXChartParserTests.ParseAsync_ValidDtxFile_SetsCorrectChannelMapping`; `sample.dtx` is not copied by the active test projects, so this test returns before asserting and is replaced by the temp-file table regression.

Correct direct `Note` fixtures that still encode the reversed semantic pairing:

- `JudgementManagerTests`: lane 8 uses channel `0x16`;
- `ChartManagerTests`: lane 8 uses channel `0x16`.

Do not broaden this into cleanup of the other `_sampleDtxPath` tests.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXChartParserAdditionalTests|FullyQualifiedName~DTXChartParserTests|FullyQualifiedName~JudgementManagerTests|FullyQualifiedName~ChartManagerTests"
```

Expected: PASS. Confirm the parser table covers all mapped drum channels.

- [ ] **Step 5: Commit the parser correction**

```bash
git add DTXMania.Game/Lib/Song/DTXChartParser.cs \
        DTXMania.Game/Lib/Song/Components/Note.cs \
        DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs \
        DTXMania.Test/Song/DTXChartParserAdditionalTests.cs \
        DTXMania.Test/Song/DTXChartParserTests.cs \
        DTXMania.Test/Stage/Performance/JudgementManagerTests.cs \
        DTXMania.Test/Song/ChartManagerTests.cs
git commit -m "fix: correct crash and ride lane mapping"
```

---

### Task 2: Remove dead contradictory lane mappings

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`
- Modify: `DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/JudgementTypes.cs`
- Delete: `DTXMania.Test/Song/InputLaneExtensionsTests.cs`
- Modify: `DTXMania.Test/Song/JudgementEventTests.cs`

**Interfaces:**
- Consumes: `JudgementEvent.Lane` as the existing gameplay lane integer.
- Produces: no new mapping API; removes `DrawOverlayEffect`, `GetSpriteColumnForLane`, `InputLane`, and `InputLaneExtensions`.

- [ ] **Step 1: Pin the debug-string behavior before deleting `InputLane`**

Add or strengthen a `JudgementEventTests` assertion for a normal known lane so `ToString()` must contain the raw lane identity, for example `Lane 4`.

Current behavior resolves lane 4 through the stale `InputLane` display table, so this assertion should fail before the deletion.

- [ ] **Step 2: Run the focused judgement test and verify RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~JudgementEventTests"
```

Expected before production changes: the new known-lane assertion fails because `ToString()` prints the stale semantic name instead of `Lane 4`.

- [ ] **Step 3: Delete the unused mapping paths**

In `NoteRenderer.cs`, delete:

- public `DrawOverlayEffect`;
- private `GetSpriteColumnForLane`.

In `NoteRendererLogicTests.cs`, delete tests whose sole purpose is `DrawOverlayEffect` or `GetSpriteColumnForLane`. Preserve the tests for `GetSpriteColumnForChannel` and all live rendering behavior.

In `JudgementTypes.cs`:

- change `JudgementEvent.ToString()` to use `$"Lane {Lane}"` directly;
- delete `InputLane`;
- delete `InputLaneExtensions`.

Delete `InputLaneExtensionsTests.cs` and remove the `InputLaneExtensions Tests` region from `JudgementEventTests.cs`. Keep the normal `JudgementEvent` tests, including the raw-lane `ToString()` assertion.

Do not replace either removed mapping with a new abstraction.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~JudgementEventTests|FullyQualifiedName~NoteRendererLogicTests"
```

Expected: PASS.

Search the live/test source and confirm there are no remaining references to `InputLane`, `InputLaneExtensions`, `GetSpriteColumnForLane`, or `DrawOverlayEffect` outside historical docs/plans.

- [ ] **Step 5: Commit the dead-code removal**

```bash
git add DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs \
        DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs \
        DTXMania.Game/Lib/Song/Entities/JudgementTypes.cs \
        DTXMania.Test/Song/JudgementEventTests.cs
git rm DTXMania.Test/Song/InputLaneExtensionsTests.cs
git commit -m "refactor: remove dead lane mapping paths"
```

---

### Task 3: Verify the bounded HPA-405 slice

**Files:** no intended production changes.

**Interfaces:** validates Tasks 1-2 as one shippable PR.

- [ ] **Step 1: Run the full Mac-safe suite**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS. Report any pre-existing failure by name rather than hiding it.

- [ ] **Step 2: Run repository hygiene checks**

```bash
git diff --check
```

Expected: clean.

Inspect the branch diff and confirm:

- no numeric change to `GetSpriteColumnForChannel`;
- no changes to `KeyBindings.cs`, `DrumKitLayout.cs`, or `SongSelectionStage.cs` (owned by HPA-443);
- no Tab Mode implementation files;
- no compatibility shim;
- parser mapping table and direct fixtures use `0x16 -> 8`, `0x19 -> 9` consistently.

Hosted Windows/macOS CI remains the final cross-platform gate.

## Self-review

- Spec coverage: parser correction, executable table regression, stale fixture cleanup, vacuous mapping-test removal, dead renderer mapping deletion, dead `InputLane` deletion, and HPA-443 separation are all assigned.
- Step scan: Task 1 has parser RED→GREEN; Task 2 has debug-string RED→GREEN; Task 3 is verification only.
- Type consistency: no new lane/map type is introduced; the plan removes duplicate sources instead.
- Review Focus: each listed risk is covered by the parser table, raw-lane debug test, retained channel-sprite tests, or final diff inspection.
- Proportion: two implementation commits plus verification stay within the original HPA-405 scale and avoid turning the separate binding/config defect into this PR.