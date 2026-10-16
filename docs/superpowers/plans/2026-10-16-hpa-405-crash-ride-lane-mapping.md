# HPA-405 Crash/Ride Lane Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct DTX crash/ride channel placement so `0x16` is CY/lane 8 and `0x19` is RD/lane 9, including the lane-keyed overlay sprite fallback and regression coverage.

**Architecture:** Keep the existing 10-lane model and parser/renderer ownership. Change only the two reversed parser entries and the corresponding two lane-to-sprite fallback entries; strengthen existing tests so the channel map is executable rather than comment-only. Do not introduce a shared lane abstraction or touch the unrelated input-lane table.

**Tech Stack:** .NET 8, C#, xUnit, MonoGame rendering helpers already in the repository.

**Spec:** `docs/superpowers/specs/2026-10-16-hpa-405-crash-ride-lane-mapping-design.md`

## Global Constraints

- One PR for HPA-405; implementation continues on the same branch/PR as these planning docs.
- Preserve gameplay lane order `LC, HH, LP, SN, HT, DB, LT, FT, CY, RD`.
- Canonical fix: `0x16 -> lane 8 (CY)` and `0x19 -> lane 9 (RD)`.
- Lane-keyed sprite fallback: lane 8 -> column 6; lane 9 -> column 1.
- Do not change `GetSpriteColumnForChannel` numeric behavior.
- Do not modify `InputLaneExtensions.FromChannel` / `JudgementTypes`, key bindings, or physical-pad mapping.
- Do not add compatibility handling for the old incorrect mapping.
- No Tab Mode implementation in this PR.

## Review Focus

- `0x16` must parse to lane 8, never lane 9.
- `0x19` must parse to lane 9, never lane 8.
- Every other mapped drum channel must retain its current parser lane.
- Lane-keyed overlays must resolve CY to crash column 6 and RD to ride column 1.
- Invalid lane fallback must remain `-1`; no new defaulting behavior.

---

### Task 1: Correct and pin the crash/ride mapping

**Files:**
- Modify: `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/Note.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`
- Modify: `DTXMania.Test/Song/DTXChartParserAdditionalTests.cs`
- Modify: `DTXMania.Test/Song/DTXChartParserTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs`

**Interfaces:**
- Consumes: existing `DTXChartParser.ParseAsync`, `Note.LaneIndex`, and private `NoteRenderer.GetSpriteColumnForLane` behavior exercised through current reflection helpers.
- Produces: canonical parser mapping `0x16 -> 8`, `0x19 -> 9` and overlay mapping `8 -> 6`, `9 -> 1`; no new public API.

- [ ] **Step 1: Make the regression tests describe the canonical map**

In `DTXChartParserAdditionalTests.ParseAsync_WithDrumNotes_ShouldParseAllMappedChannels`, replace the count-only coverage with an expected channel-to-lane table covering all currently mapped drum channels. Assert each authored channel resolves to exactly one note with the expected `LaneIndex`; in particular:

```csharp
0x16 => 8
0x19 => 9
```

Update `DTXChartParserTests` so both the sample-chart mapping assertions and `Note_GetLaneName_ReturnsCorrectNames` examples use the same canonical CY/RD mapping. Keep the lane-name strings themselves unchanged.

In `NoteRendererLogicTests.GetSpriteColumnForLane_ShouldReturnExpectedMapping`, change only:

```csharp
lane 8 => column 6
lane 9 => column 1
```

Preserve the existing cases for lanes 0-7 and invalid lane 99.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXChartParserAdditionalTests|FullyQualifiedName~DTXChartParserTests|FullyQualifiedName~NoteRendererLogicTests"
```

Expected before production changes: at least the `0x16`/`0x19` parser assertions and lane 8/9 sprite-column assertions fail against current `main` behavior.

- [ ] **Step 3: Apply the minimal production correction**

In `DTXChartParser.ChannelToLaneMap`:

- map `0x16` to lane 8 and label it CY/right crash;
- map `0x19` to lane 9 and label it RD/ride;
- leave every other entry unchanged.

In `NoteRenderer.GetSpriteColumnForLane`:

- map lane 8 to column 6;
- map lane 9 to column 1;
- leave every other lane unchanged.

Do not change the numeric cases in `GetSpriteColumnForChannel`; correct only the stale crash/ride naming comments there and in the surrounding note-order documentation.

Correct the `Note.LaneIndex` summary so the tail of the mapping reads `0x17=7 (FT), 0x16=8 (CY), 0x19=9 (RD)`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the same focused command:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXChartParserAdditionalTests|FullyQualifiedName~DTXChartParserTests|FullyQualifiedName~NoteRendererLogicTests"
```

Expected: PASS. Confirm the parser table covers all mapped channels and the renderer theory still includes invalid-lane coverage.

- [ ] **Step 5: Run the normal regression gates**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
git diff --check
```

Expected: all Mac-safe game tests pass and `git diff --check` is clean. Hosted Windows/macOS CI on the PR remains the final cross-platform gate.

Before committing, inspect the diff and confirm there are no changes to `JudgementTypes.cs`, `InputLaneExtensionsTests.cs`, key-binding code, Tab Mode files, or unrelated renderer behavior.

- [ ] **Step 6: Commit the implementation**

```bash
git add DTXMania.Game/Lib/Song/DTXChartParser.cs \
        DTXMania.Game/Lib/Song/Components/Note.cs \
        DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs \
        DTXMania.Test/Song/DTXChartParserAdditionalTests.cs \
        DTXMania.Test/Song/DTXChartParserTests.cs \
        DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs
git commit -m "fix: correct crash and ride lane mapping"
```

## Self-review

- Spec coverage: parser lane assignment, renderer lane fallback, documentation cleanup, regression tests, and explicit exclusions are all owned by Task 1.
- Step scan: one atomic bug-fix task avoids an intermediate branch state where parser and lane-keyed overlay semantics disagree.
- Type consistency: no new interfaces or types are introduced.
- Review Focus: all five listed risks are exercised by the strengthened parser table and existing renderer theory.
- Proportion: the plan stays smaller than the implementation surface and does not prescribe method bodies beyond the four corrected mapping values.