# HPA-23 Remappable Song Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the existing `OpenSearch` command in System Key Mapping and prove the existing SQLite persistence and Song Select command paths support it without adding new input architecture.

**Architecture:** Keep production changes inside `SystemKeyAssignPanel`: add `InputCommandType.OpenSearch` to the existing action list and increase the panel board height from 540px to 580px for the ninth row. Persistence remains owned by enum-driven `ConfigManager` code, and runtime behavior remains owned by the existing `SongSelectionStage.ExecuteInputCommand` case; focused tests characterize those seams rather than duplicating them.

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
- Use `F1` for acceptance coverage; do not normalize raw-stage shortcut keys in this ticket.
- Do not add unsupported NX actions, a reserved-key framework, a generic key-panel layout system, or unrelated input refactors.

## Risks and watchpoints

- **Ordinal test blast radius:** runtime footer indices derive from `Actions.Length`, but multiple tests hard-code the current eight-row ordinals. Task 1 must update those tests before the broader suite is run.
- **Panel height:** a ninth 40px row makes the current 540px board place conflict text on top of the instruction line. The implementation must use 580px and pin that geometry in draw coverage.
- **Raw shortcut precedence:** Backspace and Tab remain raw Song Select shortcuts by design. HPA-23 validates a normal non-reserved binding (`F1`) and does not create reserved-key policy.
- **Generic seams may already be green:** persistence and mapped-command dispatch are characterization tasks. A green test is evidence to leave production code untouched, not a reason to manufacture a red change.

---

### Task 1: Expose Open Search and update the real system-panel blast radius

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs`
- Modify: `DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs`
- Modify: `DTXMania.Test/Config/KeyAssignPanelWorkingCopyTests.cs`
- Modify: `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageTests.cs`
- Modify: `DTXMania.Test/Stage/KeyAssign/KeyConflictCheckerTests.cs`

**Interfaces:**
- Consumes: existing private static `SystemKeyAssignPanel.Actions`, derived `ActionCount` / `FooterSave`, existing `FormatActionName(InputCommandType)`, panel bind/unbind/conflict behavior, and `KeyConflictChecker.IsRequiredCommand`.
- Produces: one visible/bindable `Open Search` row backed by `InputCommandType.OpenSearch`, with stable ninth-row layout and updated tests that match the new action ordering.

- [ ] **Step 1: Add a failing panel-exposure test**

Add this focused test to `KeyAssignPanelCoverageTests` so the feature fails before the production row exists:

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

Keep the existing `FormatActionName_ShouldHumanizeEnumNames` case for `OpenSearch -> "Open Search"`.

- [ ] **Step 2: Run the exposure test and confirm it fails for the intended reason**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName=DTXMania.Test.Config.KeyAssignPanelCoverageTests.SystemPanel_Actions_ShouldExposeOpenSearch"
```

Expected before production change: FAIL because `SystemKeyAssignPanel.Actions` does not contain `InputCommandType.OpenSearch`.

- [ ] **Step 3: Add the row and make the board tall enough for nine actions**

In `SystemKeyAssignPanel.cs`, insert Search after Back:

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

In `Draw`, change only the board height constant:

```csharp
const int boardW = 720;
const int boardH = 580;
```

Do not add special OpenSearch branches to bind/unbind/conflict/save logic. The existing generic row loop and `Actions.Length`-derived footer fields remain authoritative.

- [ ] **Step 4: Update `KeyAssignPanelWorkingCopyTests` for the ninth row**

Add `using System.Reflection;` and a small file-local helper:

```csharp
private static int GetStaticIntField(string fieldName)
{
    var field = typeof(SystemKeyAssignPanel).GetField(
        fieldName,
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert.NotNull(field);
    return (int)field!.GetValue(null)!;
}
```

Replace loops whose purpose is "navigate to Save" so they derive the footer index instead of encoding eight actions:

```csharp
for (int i = 0; i < GetStaticIntField("FooterSave"); i++)
    PressKey(panel, Keys.Down);
```

Use that form in:

- `SystemPanel_CommitAndClose_ShouldRaiseSavedThenClosed`
- `SystemPanel_SaveWithoutEditingAction_ShouldPreserveSecondaryBindingForDisplayedAction`
- `SystemPanel_CommandProvider_ShouldNavigateAndSaveWithoutKeyboardState`

For `SystemPanel_RemappedNavigation_ShouldKeepRequiredMoveLeftBindingAndSave`, the test is already at action index 2 before its final navigation. Replace the hard-coded six moves with:

```csharp
for (int i = 0; i < GetStaticIntField("FooterSave") - 2; i++)
    PressKey(panel, Keys.S);
```

Refactor the optional-action theory so OpenSearch is explicitly covered as unbound-by-default and scroll-speed ordinals shift to 7/8:

```csharp
[Theory]
[InlineData(6, null, InputCommandType.OpenSearch)]
[InlineData(7, Keys.PageUp, InputCommandType.IncreaseScrollSpeed)]
[InlineData(8, Keys.PageDown, InputCommandType.DecreaseScrollSpeed)]
public void SystemPanel_DeleteOnOptionalAction_ShouldClearBinding(
    int selectedIndex,
    Keys? expectedKey,
    InputCommandType command)
{
    using var inputManager = new InputManager();
    var panel = new SystemKeyAssignPanel(inputManager);
    panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
    panel.Activate();

    for (int i = 0; i < selectedIndex; i++)
        PressKey(panel, Keys.Down);

    var before = panel.GetWorkingMappingSnapshot();
    if (expectedKey is { } key)
    {
        Assert.Equal(command, before[key]);
    }
    else
    {
        Assert.DoesNotContain(before, kvp => kvp.Value == command);
    }

    PressKey(panel, Keys.Delete);

    var after = panel.GetWorkingMappingSnapshot();
    Assert.DoesNotContain(after, kvp => kvp.Value == command);
}
```

This is the explicit regression pin that Search is optional and starts unbound.

- [ ] **Step 5: Shift the two scroll-speed-row tests in `KeyAssignPanelAdditionalCoverageTests`**

Both tests currently use six Down presses to reach PageUp. Search now occupies index 6, so change those loops to seven presses:

```csharp
for (int i = 0; i < 7; i++)
    PressKey(panel, Keys.Down);
```

Apply this to:

- `SystemPanel_Update_WhenUnbindPressed_ShouldRemoveBinding`
- `SystemPanel_Update_WhenMoveLeftCommandPressed_ShouldUnbindOptionalAction`

Keep their PageUp assertions unchanged; these tests still own scroll-speed unbinding, not Search.

- [ ] **Step 6: Update the ConfigStage integration navigation count**

In `ConfigStage_SystemPanel_ShouldNavigateAndSaveFromInjectedCommandsWithoutKeyboardStateChange`, nine action rows now precede Save. Change:

```csharp
for (int i = 0; i < 9; i++)
{
    DispatchInjectedPanelCommand(stage, inputManager, panel, "Key.Down");
}
```

Do not change ConfigStage production code.

- [ ] **Step 7: Pin OpenSearch as non-required in `KeyConflictCheckerTests`**

Extend the existing non-required test:

```csharp
[Fact]
public void IsRequiredCommand_NonRequiredCommand_ShouldReturnFalse()
{
    Assert.False(KeyConflictChecker.IsRequiredCommand(InputCommandType.IncreaseScrollSpeed));
    Assert.False(KeyConflictChecker.IsRequiredCommand(InputCommandType.DecreaseScrollSpeed));
    Assert.False(KeyConflictChecker.IsRequiredCommand(InputCommandType.OpenSearch));
}
```

Do not modify `KeyConflictChecker.RequiredCommands`.

- [ ] **Step 8: Pin the 580px board in existing draw-spy coverage**

In `SystemPanel_Draw_WithWhitePixel_ShouldDrawBackdropBoardAndSelectionBar`, add a board-fill geometry assertion using the existing `WhitePixelDraws` spy:

```csharp
Assert.Contains(panel.WhitePixelDraws,
    d => d.Color == new Color(14, 16, 34, 236)
        && d.Rectangle.Height == 580);
```

This makes the ninth-row layout decision automated instead of relying on the final manual smoke to discover the conflict/instruction overlap.

- [ ] **Step 9: Run the complete Task 1 test surface**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXMania.Test.Config.KeyAssignPanelCoverageTests|FullyQualifiedName~DTXMania.Test.Config.KeyAssignPanelWorkingCopyTests|FullyQualifiedName~DTXMania.Test.Config.KeyAssignPanelAdditionalCoverageTests|FullyQualifiedName=DTXMania.Test.Config.ConfigStageTests.ConfigStage_SystemPanel_ShouldNavigateAndSaveFromInjectedCommandsWithoutKeyboardStateChange|FullyQualifiedName~DTXMania.Test.Stage.KeyAssign.KeyConflictCheckerTests"
```

Expected: all selected tests pass with the new nine-action ordering.

- [ ] **Step 10: Review the production diff for scope**

At this checkpoint the only production file changed should be `SystemKeyAssignPanel.cs`, containing:

1. one `OpenSearch` entry in `Actions`;
2. `boardH` changed from 540 to 580.

If implementation has changed `ConfigManager`, `InputManager`, `KeyConflictChecker`, `ConfigStage`, or `SongSelectionStage`, stop and justify the change against a failing test before continuing.

---

### Task 2: Characterize SQLite persistence for OpenSearch

**Files:**
- Modify: `DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs`

**Interfaces:**
- Consumes: `ConfigManager.SetSystemKeyBindings`, `FlushPendingSave`, SQLite `config.db`, `LoadConfig`, `CreateConfiguredInputManager`, and generic `SystemKey.<InputCommandType>` persistence.
- Produces: regression evidence that a saved Search binding survives a real config round trip without OpenSearch-specific production code.

- [ ] **Step 1: Add an OpenSearch round-trip test**

Add a test using non-reserved `F1`:

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

Use the existing SQLite-backed `CreateManager()` test seam; do not replace it with a dictionary-only unit test.

- [ ] **Step 2: Run the persistence class**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXMania.Test.Config.SystemKeyBindingsPersistenceTests"
```

Expected: the new test may already pass on current production code. That is correct characterization evidence.

- [ ] **Step 3: Confirm generic optional-command persistence remains untouched**

Verify the implementation diff contains no `ConfigManager` production change. The existing generic contract is:

- `ApplySystemKeyBindings` iterates `Enum.GetValues<InputCommandType>()`;
- optional commands with no binding persist an empty value;
- `LoadSystemKeyBindings` parses `SystemKey.<Command>` generically;
- `OpenSearch` is not a required fallback command.

If the round-trip test is green, stop there.

---

### Task 3: Pin mapped Search command drain behavior using the existing queue helper

**Files:**
- Modify: `DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs`

**Interfaces:**
- Consumes: that file's existing `QueuedInputManager.Enqueue(InputCommand)`, `CreateScoreNode`, `AttachCoreUi`, `SongSelectionStage.ProcessInputCommands`, existing `ExecuteInputCommand(OpenSearch)` case, and `SongSearchFilterModal`.
- Produces: regression evidence that a mapped Search command opens the modal and prevents a following same-frame navigation command from changing the song list.

- [ ] **Step 1: Add the mapped-command drain test beside the existing queue tests**

Place the test near `ProcessInputCommands_WithQueuedCommands_ShouldExecuteAll` and reuse the existing `QueuedInputManager`; do not add another queue subclass:

```csharp
[Fact]
public void ProcessInputCommands_WhenOpenSearchPrecedesNavigation_ShouldOpenModalAndStopDrain()
{
    var stage = CreateStage();
    var display = new SongListDisplay
    {
        CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
    };
    var inputManager = new QueuedInputManager();
    var textInput = new Mock<ITextInputSource>();
    var modal = new SongSearchFilterModal(textInput.Object);

    AttachCoreUi(stage, display: display);
    SetPrivateField(stage, "_inputManager", inputManager);
    SetPrivateField(stage, "_searchFilterModal", modal);

    inputManager.Enqueue(new InputCommand(InputCommandType.OpenSearch, 0.0));
    inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

    InvokePrivateMethod(stage, "ProcessInputCommands");

    Assert.True(modal.IsOpen);
    Assert.Equal("A", display.SelectedSong!.Title);
}
```

This is complementary to `HandleInput_WhenModalJustOpened_ShouldNotProcessStageCommands`, which covers the raw Backspace-open path rather than `ExecuteInputCommand(OpenSearch)`.

- [ ] **Step 2: Run the SongSelection coverage class**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXMania.Test.Stage.SongSelectionStageCoverageTests"
```

Expected: the new test should pass with existing `SongSelectionStage` production code because the `OpenSearch` case already calls `OpenSearchFilterModal()` and returns `false`.

If it passes, do not modify Song Select production code.

- [ ] **Step 3: Keep the existing Backspace regressions unchanged**

The existing raw-Backspace tests remain the separate proof that the built-in shortcut still works. Do not route Backspace through `OpenSearch`; Backspace remains the key-capture cancel key in the System Key Mapping panel.

---

### Task 4: Verify the complete HPA-23 slice

**Files:**
- No new files expected.

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: one reviewable implementation slice with one production-file change plus focused regression updates.

- [ ] **Step 1: Run all HPA-23-focused tests together**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~KeyAssignPanelCoverageTests|FullyQualifiedName~KeyAssignPanelWorkingCopyTests|FullyQualifiedName~KeyAssignPanelAdditionalCoverageTests|FullyQualifiedName~SystemKeyBindingsPersistenceTests|FullyQualifiedName~KeyConflictCheckerTests|FullyQualifiedName~SongSelectionStageCoverageTests|FullyQualifiedName~OpenSearchInputCommandTests|FullyQualifiedName=DTXMania.Test.Config.ConfigStageTests.ConfigStage_SystemPanel_ShouldNavigateAndSaveFromInjectedCommandsWithoutKeyboardStateChange"
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
2. Confirm `Open Search` appears between Back and Increase Scroll Speed.
3. Confirm the nine-row panel, footer, conflict message, and instruction line are readable without overlap.
4. Confirm Search is initially `(unbound)` on a fresh config.
5. Bind `F1` to Open Search and save.
6. Return to Song Select -> All Songs and press `F1`; the existing search/filter modal opens once.
7. Close the modal and confirm normal song-list navigation still behaves normally.
8. Restart the game and confirm `F1` still opens Search.
9. Confirm Backspace still opens Search through the existing shortcut.

Do not use this smoke step to redesign Tab, Backspace capture semantics, non-All-Songs search, or unrelated key mapping.

- [ ] **Step 5: Final scope audit**

Expected implementation diff:

```text
Production:
  DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs

Tests:
  DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs
  DTXMania.Test/Config/KeyAssignPanelWorkingCopyTests.cs
  DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs
  DTXMania.Test/Config/ConfigStageTests.cs
  DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs
  DTXMania.Test/Stage/KeyAssign/KeyConflictCheckerTests.cs
  DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs
```

Reject unnecessary changes to input architecture, config schema, modal implementation, unsupported NX commands, or DTXManiaNX.

- [ ] **Step 6: Commit the implementation on this same branch/PR**

Use focused commits if useful during execution, but keep all HPA-23 work in this one PR. A suitable final implementation commit message is:

```text
feat: make song search remappable
```
