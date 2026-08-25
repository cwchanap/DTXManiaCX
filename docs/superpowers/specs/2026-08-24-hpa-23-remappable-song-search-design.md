# HPA-23 Remappable Song Search Design

## Summary

HPA-23 exposes the already-existing `InputCommandType.OpenSearch` in System Key Mapping. The current config persistence and Song Select modal path are reused; no new command, schema, modal, or input framework is needed.

The implementation has two small production changes:

1. add `OpenSearch` to `SystemKeyAssignPanel.Actions` and increase the panel board height from 540px to 580px for the ninth row;
2. make `SongSelectionStage` stop command draining only when Search actually opens, so a mapped Search key on Recent Plays/Bookmarks does not silently swallow later commands from the same frame.

## Product contract

### System Key Mapping

The action order is fixed as:

1. Move Up
2. Move Down
3. Move Left
4. Move Right
5. Activate
6. Back
7. Open Search
8. Increase Scroll Speed
9. Decrease Scroll Speed

`OpenSearch` remains optional and unbound by default. It must not be added to `KeyConflictChecker.RequiredCommands` or `ConfigManager.RequiredSystemCommands`.

The ninth row requires `boardH = 580`. Tests should pin the actual non-overlap invariant between conflict text and the instruction line, not the numeric height alone.

### Persistence

Keep the existing generic path unchanged:

`SystemKeyAssignPanel -> ConfigStage -> ConfigManager.SetSystemKeyBindings -> config.db`

`ConfigManager.ApplySystemKeyBindings` already iterates every `InputCommandType`, and `LoadSystemKeyBindings` already parses `SystemKey.<Command>` generically. A saved mapping remains the normal row shape:

```text
SystemKey.OpenSearch=F1
```

Persistence coverage should seed from a real configured `InputManager` snapshot, add `F1 -> OpenSearch`, save, reload, and verify the restored mapping. This matches the full snapshot that ConfigStage actually submits.

### Song Select

On All Songs, a mapped Search command must open the existing modal and stop remaining same-frame stage commands from executing behind it.

On Recent Plays or Bookmarks, Search remains unavailable. In that case the command must be ignored without stopping the rest of the frame's queued commands. The minimal dispatch is:

```csharp
case InputCommandType.OpenSearch:
    OpenSearchFilterModal();
    return _searchFilterModal?.IsOpen != true;
```

This keeps the existing `OpenSearchFilterModal()` availability rule and avoids introducing a second hotkey path.

Raw Backspace Search and raw Tab switching stay unchanged. No reserved-key normalization is part of HPA-23.

## Test strategy

### Action-list and panel tests

Pin the full nine-element `Actions` array, not only `Assert.Contains(OpenSearch)`. The exact order is part of this change because existing tests intentionally navigate by row ordinal.

Update the current ordinal-sensitive tests:

- navigation-to-Save loops should derive `FooterSave` through one shared `ReflectionHelpers.GetStaticIntField(Type, string)` helper;
- scroll-speed optional-action rows move from 6/7 to 7/8;
- `KeyAssignPanelAdditionalCoverageTests` PageUp row moves from 6 to 7;
- the ConfigStage injected-command Save test should derive `FooterSave` instead of hard-coding 8;
- `KeyConflictCheckerTests` should explicitly assert `OpenSearch` is non-required.

Do not add a nullable `OpenSearch` case to the existing optional-unbind theory. Full-order coverage plus the required-command test already pins the intended semantics cleanly.

### Layout

Extend the existing conflict-message draw test to capture the conflict and instruction `DrawString` positions and assert the conflict line plus its measured height ends at or above the instruction line. This catches both the ninth-row regression and any future tenth-row squeeze.

### Song Select

Do not add another near-duplicate mapped-command test. `SongSelectionStageCoverageTests` already contains `ProcessInputCommands_OpenSearch_ShouldDrainRemainingCommands` with the existing file-local `QueuedInputManager`.

Strengthen that test to use observable list state: queue `OpenSearch` followed by `MoveDown` on All Songs and prove the modal opens while selection does not move.

Add one new Recent Plays test that queues `OpenSearch` followed by `MoveDown` and proves the modal stays closed while `MoveDown` still executes. That test should fail on current `main` and drive the one-line return fix above.

## Expected implementation files

Production:

- `DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs`
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

Tests:

- `DTXMania.Test/TestData/ReflectionHelpers.cs`
- `DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs`
- `DTXMania.Test/Config/KeyAssignPanelWorkingCopyTests.cs`
- `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`
- `DTXMania.Test/Config/ConfigStageTests.cs`
- `DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs`
- `DTXMania.Test/Stage/KeyAssign/KeyConflictCheckerTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs`

`InputManager`, `ConfigManager`, `KeyConflictChecker`, and `ConfigStage` production code stay unchanged.

## Out of scope

- unsupported NX actions;
- a default Search key;
- Backspace/Tab normalization;
- a reserved-key framework;
- Search UI on non-All-Songs tabs;
- new config schema or input abstractions;
- DTXManiaNX changes.

## Acceptance

HPA-23 is complete when:

- System Key Mapping shows `Open Search` at index 6 in the defined nine-action order;
- the panel has no conflict/instruction overlap;
- `F1 -> OpenSearch` survives a real SQLite round trip;
- All Songs opens Search and suppresses later same-frame commands;
- Recent Plays/Bookmarks ignore Search without swallowing later same-frame commands;
- Search remains optional/unbound by default;
- existing Backspace and Tab behavior remains unchanged.
