# HPA-23 Remappable Song Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the existing `OpenSearch` command in System Key Mapping and prove the existing SQLite persistence and Song Select command paths support it without adding new input architecture.

**Architecture:** `SystemKeyAssignPanel` should become the only production change by adding `InputCommandType.OpenSearch` to its existing action list. Persistence remains owned by enum-driven `ConfigManager` code, and runtime behavior remains owned by the existing `SongSelectionStage.ExecuteInputCommand` case; focused tests characterize those seams rather than duplicating them.

**Tech Stack:** .NET 8, C#, MonoGame input (`Microsoft.Xna.Framework.Input.Keys`), xUnit, Moq, SQLite-backed `ConfigManager`.

**Spec:** `docs/superpowers/specs/2026-08-24-hpa-23-remappable-song-search-design.md`

## Global Constraints

- Deliver HPA-23 in this single PR; do not split planning and implementation into separate PRs.
- Reuse the existing `InputCommandType.OpenSearch`; do not add a new command enum value.
- `OpenSearch` remains optional, unbound by default, and must not be added to required-command fallback logic.
- Reuse the existing enum-driven system-key persistence; do not add OpenSearch-specific config schema or branches unless a focused test proves the generic path is broken.
- Reuse `SongSelectionStage.OpenSearchFilterModal()` and the existing `ExecuteInputCommand` case; do not create a second Search hotkey path.
- Preserve the existing raw Backspace Search shortcut and raw Tab tab-switch behavior.
- Keep Search availability aligned with the current modal contract: All Songs only.
- Do not add unsupported NX actions, a reserved-key framework, or unrelated input refactors.

---

### Task 1: Expose Open Search in System Key Mapping

**Files:**
- Modify: `DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs`

**Interfaces:**
- Consumes: existing private static `SystemKeyAssignPanel.Actions`, existing `FormatActionName(InputCommandType)` and existing panel bind/unbind/conflict behavior.
- Produces: one visible/bindable `Open Search` action row backed by `InputCommandType.OpenSearch`.

- [ ] **Step 1: Add a failing panel-exposure test**

Add a focused test to `KeyAssignPanelCoverageTests` that proves the actual action list contains `OpenSearch`, not merely that the formatter knows how to spell it:

```csharp
[Fact]
public void SystemPanel_Actions_ShouldExposeOpenSearch()
{
    var field = typeof(SystemKeyAssignPanel).GetField(
        "Actions",
        BindingFlags.Static | BindingFlags.NonPublic);

    Assert.NotNull(field);
    var actions = Assert.IsType<InputCommandType[]>(field!.GetValue(null));

    Assert.Contains(InputCommandType.OpenSearch, actions);
}
```

Keep the existing `FormatActionName_ShouldHumanizeEnumNames` `OpenSearch -> "Open Search"` case; it already pins the row label.

- [ ] **Step 2: Run the focused test and confirm it fails for the intended reason**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXMania.Test.Config.KeyAssignPanelCoverageTests"
```

Expected before production change: the new test fails because `SystemKeyAssignPanel.Actions` does not contain `InputCommandType.OpenSearch`.

- [ ] **Step 3: Add OpenSearch to the existing action list**

Modify only the `Actions` initializer in `SystemKeyAssignPanel.cs` and place Search after Back, before the scroll-speed actions:

```csharp
private static readonly InputCommandType[] Actions =
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
};
```

Do not add special handling elsewhere in the panel. Its existing generic draw, assignment, unbind, conflict, save, and footer-index logic is derived from `Actions.Length` and should continue to work unchanged.

- [ ] **Step 4: Re-run the panel tests**

Run the same focused command. Expected: `KeyAssignPanelCoverageTests` passes.

- [ ] **Step 5: Review the production diff for scope**

At this checkpoint the only production diff should be the single `OpenSearch` action-list entry. If implementation has changed `ConfigManager`, `InputManager`, `KeyConflictChecker`, `ConfigStage`, or `SongSelectionStage`, stop and justify the change against a failing test before continuing.

---

### Task 2: Characterize SQLite persistence for OpenSearch

**Files:**
- Modify: `DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs`

**Interfaces:**
- Consumes: `ConfigManager.SetSystemKeyBindings`, `FlushPendingSave`, SQLite `config.db`, `LoadConfig`, `CreateConfiguredInputManager`, and generic `SystemKey.<InputCommandType>` persistence.
- Produces: regression evidence that a saved Search binding survives a real config round trip without OpenSearch-specific production code.

- [ ] **Step 1: Add an OpenSearch round-trip test**

Add a test using a non-reserved key such as `F1`:

```csharp
[Fact]
public void ConfigManager_RoundTrip_OpenSearchBinding_ShouldPreserveValue()
{
    var manager = CreateManager();
    manager.LoadConfig();

    manager.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
    {
        [Keys.F1] = InputCommandType.OpenSearch,
    });
    manager.FlushPendingSave();

    var manager2 = CreateManager();
    manager2.LoadConfig();
    using var inputManager = manager2.CreateConfiguredInputManager();

    Assert.Equal("F1", manager2.Config.SystemKeyBindings["SystemKey.OpenSearch"]);
    Assert.Equal(
        InputCommandType.OpenSearch,
        inputManager.GetKeyMappingSnapshot()[Keys.F1]);
}
```

This must use the existing SQLite-backed `CreateManager()` test seam; do not replace it with a dictionary-only unit test.

- [ ] **Step 2: Run the persistence class**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXMania.Test.Config.SystemKeyBindingsPersistenceTests"
```

Expected: this test may already pass on current production code. That is correct characterization evidence. Do not modify `ConfigManager` merely to create a red/green cycle.

- [ ] **Step 3: Confirm optional-command semantics remain generic**

Inspect the diff and existing tests rather than adding new production code:

- `OpenSearch` must not be added to `KeyConflictChecker.RequiredCommands`.
- `OpenSearch` must not be added to `ConfigManager.RequiredSystemCommands`.
- Existing `ApplySystemKeyBindings` behavior should continue writing an empty value when an optional command has no key.

If the new round-trip test is green, leave production persistence untouched.

---

### Task 3: Pin Song Select command suppression

**Files:**
- Modify: `DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs`

**Interfaces:**
- Consumes: `InputManager.EnqueueCommand` test subclass seam, `SongSelectionStage.ProcessInputCommands`, existing `ExecuteInputCommand(OpenSearch)` case, and `SongSearchFilterModal`.
- Produces: regression evidence that a mapped Search command opens the modal and prevents a following same-frame command from changing the song list.

- [ ] **Step 1: Add a small queued-input test helper**

Inside `SongSelectionStageInputCoverageTests`, add a test-only subclass that exposes the existing protected enqueue seam:

```csharp
private sealed class QueuedInputManager : InputManager
{
    public void Queue(InputCommand command) => EnqueueCommand(command);
}
```

Do not change production `InputManager` visibility for this test.

- [ ] **Step 2: Add the one-shot modal regression test**

Use two visible songs so command leakage is observable:

```csharp
[Fact]
public void ProcessInputCommands_WhenOpenSearchPrecedesNavigation_ShouldOpenModalAndStopDrain()
{
    var stage = CreateStage();
    var display = new SongListDisplay
    {
        CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
    };
    var textInput = new Mock<ITextInputSource>();
    var modal = new SongSearchFilterModal(textInput.Object);
    var inputManager = new QueuedInputManager();

    AttachCoreUi(stage, display: display);
    SetPrivateField(stage, "_inputManager", inputManager);
    SetPrivateField(stage, "_searchFilterModal", modal);

    inputManager.Queue(new InputCommand(InputCommandType.OpenSearch, 0.0));
    inputManager.Queue(new InputCommand(InputCommandType.MoveDown, 0.0));

    InvokePrivateMethod(stage, "ProcessInputCommands");

    Assert.True(modal.IsOpen);
    Assert.Equal("A", display.SelectedSong!.Title);
}
```

The test deliberately exercises the mapped-command queue rather than `DetectOpenSearchKey`, which already covers the separate raw Backspace path.

- [ ] **Step 3: Run the Song Select input coverage class**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXMania.Test.Stage.SongSelectionStageInputCoverageTests"
```

Expected: the new test should pass with existing `SongSelectionStage` production code because the `OpenSearch` case already returns `false` after opening the modal.

If it passes, do not modify Song Select production code.

- [ ] **Step 4: Keep the existing Backspace regression tests unchanged**

`DetectOpenSearchKey_WhenBackspacePressed_ShouldOpenModal` already proves the legacy shortcut still works. Do not route Backspace through `OpenSearch` as part of HPA-23 because Backspace remains the key-capture cancel key.

---

### Task 4: Verify the complete HPA-23 slice

**Files:**
- No new files expected.

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: one reviewable implementation slice with a single production-file change plus focused tests.

- [ ] **Step 1: Run all HPA-23-focused tests together**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~KeyAssignPanelCoverageTests|FullyQualifiedName~SystemKeyBindingsPersistenceTests|FullyQualifiedName~SongSelectionStageInputCoverageTests|FullyQualifiedName~OpenSearchInputCommandTests"
```

Expected: all selected tests pass.

- [ ] **Step 2: Run the full macOS test project**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
```

Expected: full suite passes with no new failures.

- [ ] **Step 3: Build the macOS game project**

Run:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

Expected: build succeeds. Windows validation remains owned by CI for the PR.

- [ ] **Step 4: Perform the minimal interactive smoke check when a game window is available**

Verify only the product path this ticket owns:

1. Open Config -> System Key Mapping.
2. Confirm an `Open Search` row is visible and initially `(unbound)` on a fresh config.
3. Bind `F1` to Open Search and save.
4. Return to Song Select -> All Songs and press `F1`; the existing search/filter modal opens once.
5. Close the modal and confirm normal song-list navigation still behaves normally.
6. Restart the game and confirm `F1` still opens Search.
7. Confirm Backspace still opens Search through the existing shortcut.

Do not use this smoke step to redesign Tab, Backspace capture semantics, non-All-Songs search, or unrelated key mapping.

- [ ] **Step 5: Final scope audit**

Expected implementation diff:

```text
Production:
  DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs

Tests:
  DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs
  DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs
  DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs
```

Reject unnecessary changes to input architecture, config schema, modal implementation, unsupported NX commands, or DTXManiaNX.

- [ ] **Step 6: Commit the implementation on this same branch/PR**

Use focused commits if useful during execution, but keep all HPA-23 work in this one PR. A suitable final implementation commit message is:

```text
feat: make song search remappable
```
