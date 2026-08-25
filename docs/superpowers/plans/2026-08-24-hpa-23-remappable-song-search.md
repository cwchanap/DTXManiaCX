# HPA-23 Remappable Song Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the existing `OpenSearch` command in System Key Mapping while preserving correct panel layout, persistence, and Song Select input behavior.

**Architecture:** Reuse the existing command, generic SQLite system-key persistence, and `OpenSearchFilterModal()`. Production changes are limited to `SystemKeyAssignPanel` (new row + 580px board) and one conditional return in `SongSelectionStage` so unavailable Search does not swallow later commands.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, Moq, SQLite-backed `ConfigManager`.

**Spec:** `docs/superpowers/specs/2026-08-24-hpa-23-remappable-song-search-design.md`

## Global Constraints

- One PR for planning + implementation.
- Reuse `InputCommandType.OpenSearch`; no new command or input framework.
- Search remains optional and unbound by default.
- No `ConfigManager` production change.
- Preserve raw Backspace Search and raw Tab switching.
- Search UI remains All Songs only.
- Use `F1` as the acceptance binding.

---

### Task 1: Expose the row and preserve panel/runtime semantics

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify: `DTXMania.Test/TestData/ReflectionHelpers.cs`
- Modify: `DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs`
- Modify: `DTXMania.Test/Config/KeyAssignPanelWorkingCopyTests.cs`
- Modify: `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageTests.cs`
- Modify: `DTXMania.Test/Stage/KeyAssign/KeyConflictCheckerTests.cs`
- Modify: `DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs`

**Produces:** exact nine-row action ordering, non-overlapping panel layout, Search optionality, correct All Songs suppression, and correct off-tab command continuation.

- [ ] **Step 1: Pin the exact action order**

Replace the previous contains-only idea with a full-array assertion in `KeyAssignPanelCoverageTests`:

```csharp
Assert.Equal(new[]
{
    InputCommandType.MoveUp,
    InputCommandType.MoveDown,
    InputCommandType.MoveLeft,
    InputCommandType.MoveRight,
    InputCommandType.Activate,
    InputCommandType.Back,
    InputCommandType.OpenSearch,
    InputCommandType.IncreaseScrollSpeed,
    InputCommandType.DecreaseScrollSpeed,
}, actions);
```

Run only that test first. Expected on current main: FAIL because `OpenSearch` is absent.

- [ ] **Step 2: Add the row and restore the panel's vertical spacing**

In `SystemKeyAssignPanel.Actions`, insert:

```csharp
InputCommandType.OpenSearch,
```

after `Back` and before the scroll-speed actions.

In `Draw`, change:

```csharp
const int boardH = 580;
```

Do not add OpenSearch-specific bind/unbind/save logic.

- [ ] **Step 3: Centralize the static-int reflection helper once**

Add to `DTXMania.Test/TestData/ReflectionHelpers.cs`:

```csharp
internal static int GetStaticIntField(Type type, string fieldName)
{
    var field = type.GetField(
        fieldName,
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    Assert.NotNull(field);
    return Assert.IsType<int>(field!.GetValue(null));
}
```

Remove the duplicate local `GetStaticIntField(Type, string)` helpers from:

- `KeyAssignPanelCoverageTests.cs`
- `KeyAssignPanelAdditionalCoverageTests.cs`

Use the shared helper in those files and in `KeyAssignPanelWorkingCopyTests.cs` / `ConfigStageTests.cs` where Save navigation now needs the derived `FooterSave` value. Do not create a third file-local copy.

- [ ] **Step 4: Update only the ordinal-sensitive tests**

In `KeyAssignPanelWorkingCopyTests`:

- derive Save loops from `FooterSave` rather than hard-coded `8`;
- use `FooterSave - 2` in `SystemPanel_RemappedNavigation_ShouldKeepRequiredMoveLeftBindingAndSave`;
- keep `SystemPanel_DeleteOnOptionalAction_ShouldClearBinding` sharp: shift scroll-speed rows from `6/7` to `7/8` only. Do **not** add a nullable `OpenSearch` theory case.

In `KeyAssignPanelAdditionalCoverageTests`, change the two PageUp-navigation counts from `6` to `7`.

In `ConfigStage_SystemPanel_ShouldNavigateAndSaveFromInjectedCommandsWithoutKeyboardStateChange`, derive the count from `FooterSave` instead of hard-coding `8` or `9`.

In `KeyConflictCheckerTests`, extend the existing non-required test with:

```csharp
Assert.False(KeyConflictChecker.IsRequiredCommand(InputCommandType.OpenSearch));
```

- [ ] **Step 5: Pin the layout invariant, not the 580 constant**

Extend `SystemPanel_Draw_WithConflictMessage_ShouldDrawConflictText`.

Use the existing `Mock<IFont>` to capture the `DrawString` calls for:

- the conflict line (`"Conflict: ..."`);
- the instruction line (`GetInstructionText()`).

Assert:

```text
conflictY + measuredConflictHeight <= instructionY
```

The test should fail if a future extra row recreates the overlap even if `boardH` changes again.

- [ ] **Step 6: Strengthen the existing All Songs OpenSearch drain test**

Do not add another near-identical mapped-command test. Update the existing `ProcessInputCommands_OpenSearch_ShouldDrainRemainingCommands` in `SongSelectionStageCoverageTests`:

- give the display two songs (`A`, `B`);
- queue `OpenSearch`, then `MoveDown`;
- assert the modal opens;
- assert selection stays on `A`.

Remove/reduce the existing queue-emptiness assertion: `ProcessInputCommands()` copies and clears the manager queue before dispatch, so queue emptiness is not meaningful evidence that the second command was suppressed.

This All Songs test is characterization and should already pass on current main.

- [ ] **Step 7: Add the newly-reachable off-tab regression and minimal fix**

Add a test in the same file:

```text
active tab = Recent Plays
visible songs = A, B
queue = OpenSearch, MoveDown
expect modal closed
expect selection moves to B
```

Expected on current main: FAIL because `ExecuteInputCommand(OpenSearch)` returns `false` even though `OpenSearchFilterModal()` early-returns off All Songs.

Then change only the existing case in `SongSelectionStage.ExecuteInputCommand`:

```csharp
case InputCommandType.OpenSearch:
    OpenSearchFilterModal();
    return _searchFilterModal?.IsOpen != true;
```

Meaning:

- modal opened -> return `false`, stop draining;
- Search unavailable/null -> return `true`, continue later commands.

Do not change `OpenSearchFilterModal()` or add Search UI to Recent Plays/Bookmarks.

- [ ] **Step 8: Run the Task 1 focused surface**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~KeyAssignPanelCoverageTests|FullyQualifiedName~KeyAssignPanelWorkingCopyTests|FullyQualifiedName~KeyAssignPanelAdditionalCoverageTests|FullyQualifiedName~ConfigStage_SystemPanel_ShouldNavigateAndSaveFromInjectedCommandsWithoutKeyboardStateChange|FullyQualifiedName~KeyConflictCheckerTests|FullyQualifiedName~ProcessInputCommands_OpenSearch"
```

Expected: all pass.

---

### Task 2: Characterize the real SQLite save shape

**Files:**
- Modify: `DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs`

**Produces:** proof that the full configured system-key snapshot can add `F1 -> OpenSearch` and survive a SQLite round trip without production persistence changes.

- [ ] **Step 1: Add one round-trip test using the real snapshot shape**

Use the existing SQLite-backed `CreateManager()` seam:

1. `LoadConfig()`.
2. Create a configured `InputManager` from that manager.
3. Copy its complete `GetKeyMappingSnapshot()` into a mutable dictionary.
4. Add `[Keys.F1] = InputCommandType.OpenSearch`.
5. Call `SetSystemKeyBindings(fullSnapshot)` and `FlushPendingSave()`.
6. Load a fresh manager and configured `InputManager`.
7. Assert `SystemKey.OpenSearch == "F1"` and runtime `F1 -> OpenSearch`.

This matches `ConfigStage`, which submits the full working snapshot. Do not test persistence with a one-entry dictionary that accidentally exercises required-command fallback restoration.

The test may be green before any persistence production change; that is expected.

- [ ] **Step 2: Run persistence coverage**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~SystemKeyBindingsPersistenceTests"
```

Expected: PASS. Leave `ConfigManager` unchanged.

---

### Task 3: Verify the complete HPA-23 slice

- [ ] **Step 1: Run all focused HPA-23 tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~KeyAssignPanelCoverageTests|FullyQualifiedName~KeyAssignPanelWorkingCopyTests|FullyQualifiedName~KeyAssignPanelAdditionalCoverageTests|FullyQualifiedName~ConfigStageTests|FullyQualifiedName~KeyConflictCheckerTests|FullyQualifiedName~SystemKeyBindingsPersistenceTests|FullyQualifiedName~SongSelectionStageCoverageTests|FullyQualifiedName~OpenSearchInputCommandTests"
```

- [ ] **Step 2: Run the full macOS suite and build**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

Windows remains owned by PR CI.

- [ ] **Step 3: Minimal interactive smoke**

1. Open Config -> System Key Mapping and confirm `Open Search` appears without layout overlap.
2. Bind `F1`, save, enter All Songs, and confirm `F1` opens Search once.
3. Close Search; confirm Backspace still opens it.
4. Restart and confirm `F1` remains bound.

Recent/Bookmarks command-continuation behavior is automated; no manual smoke is required for it.

- [ ] **Step 4: Final scope audit**

Expected production changes only:

```text
DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs
DTXMania.Game/Lib/Stage/SongSelectionStage.cs
```

Expected supporting test/helper changes are limited to the files named in Tasks 1-2. Reject new command enums, config schema, reserved-key frameworks, or Search UI on non-All-Songs tabs.

Keep all implementation commits on this same PR.
