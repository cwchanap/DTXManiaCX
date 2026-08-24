# HPA-23 Remappable Song Search Design

## Summary

HPA-23 closes one existing configuration gap: `InputCommandType.OpenSearch` already exists and Song Select already consumes it, but `SystemKeyAssignPanel` does not expose it for user assignment.

The implementation should reuse the current system-key pipeline end to end. Add `OpenSearch` to the panel's existing action list, keep it optional and unbound by default, and add focused regression coverage proving the existing SQLite persistence and Song Select dispatch paths work with the newly reachable command.

Adding the ninth action row also requires a small layout adjustment in the same panel: increase the board height from 540px to 580px so conflict text does not overlap the bottom instruction line.

No new input command, configuration schema, modal, or command framework is needed.

## Current state

The current `main` behavior already contains almost all of the feature:

- `InputCommandType.OpenSearch` exists in `InputManager.cs`.
- `InputManager` has no default key for `OpenSearch`, which is appropriate for this optional action.
- `SystemKeyAssignPanel.Actions` exposes navigation and scroll-speed commands but omits `OpenSearch`.
- `SystemKeyAssignPanel.FormatActionName` already humanizes `OpenSearch` as `Open Search`.
- `ConfigManager.ApplySystemKeyBindings` iterates every `InputCommandType`, persists optional unbound commands as empty values, and `LoadSystemKeyBindings` parses command names generically. `OpenSearch` therefore needs no dedicated config code.
- `SongSelectionStage.ExecuteInputCommand` already handles `OpenSearch` by calling `OpenSearchFilterModal()` and returning `false`, which stops the remaining queued commands from acting on the song list in the same frame.
- The current raw Backspace shortcut calls the same `OpenSearchFilterModal()` path and remains useful as the built-in shortcut.
- Existing key-assignment tests encode the current eight-action ordering and footer indices in several places. Adding a ninth action therefore requires updating those tests as part of the same panel slice.

This means production behavior changes should remain limited to `SystemKeyAssignPanel` unless a focused characterization test proves an existing generic seam is broken.

## Product contract

### System Key Mapping

Add one `Open Search` row to the existing System Key Mapping panel.

Ordering:

1. Move Up
2. Move Down
3. Move Left
4. Move Right
5. Activate
6. Back
7. Open Search
8. Increase Scroll Speed
9. Decrease Scroll Speed

`OpenSearch` remains a non-required command:

- it has no default mapped key;
- the row initially displays `(unbound)` unless the user already saved a binding;
- the user may bind, rebind, or unbind it through the existing panel behavior;
- normal drum/system conflict checks continue to apply.

The ninth row makes the existing 540px board too tight for the conflict-message path. With nine 40px rows, the conflict message would be drawn at approximately the same vertical position as the bottom instruction line. Increase `boardH` to 580px. This preserves roughly the same footer/conflict/instruction spacing the current eight-row panel has while still fitting comfortably inside the 720px virtual height.

No responsive layout system or generalized scrolling panel is needed.

### Persistence

Saving System Key Mapping must continue through the existing path:

`SystemKeyAssignPanel working mapping -> ConfigStage -> ConfigManager.SetSystemKeyBindings -> config.db`

A saved binding is represented by the existing generic row shape, for example:

```text
SystemKey.OpenSearch=F1
```

No SQLite schema or `ConfigData` shape changes are required. An unbound Search action should remain represented by the existing optional-command empty-value behavior.

### Song Select behavior

A mapped Search key follows the current input pipeline:

`hardware/API key -> InputManager -> InputCommandType.OpenSearch -> SongSelectionStage.ProcessInputCommands -> OpenSearchFilterModal`

When `OpenSearch` is processed:

- the existing search/filter modal opens through `OpenSearchFilterModal()`;
- the stage leaves the status panel first, matching current behavior;
- command draining stops for that frame so a second queued navigation/Activate/Back command does not leak through behind the modal;
- Search keeps the existing availability semantics of the Backspace shortcut: the modal is an All Songs feature and remains unavailable on Recent Plays or Bookmarks.

The raw Backspace shortcut stays unchanged. Backspace is also the key-capture cancel key in `SystemKeyAssignPanel`; HPA-23 does not normalize that special case into the remappable command path.

`Tab` also remains the existing raw Song Select tab-switch shortcut. HPA-23 does not refactor raw stage shortcuts or create a reserved-key framework. Acceptance therefore uses an ordinary non-reserved key such as `F1`; this ticket does not promise that keys already owned by raw stage shortcuts will be normalized into the command map.

## Design decisions

### Reuse the existing command

Do not add another enum such as `Search` or `OpenSongSearch`. `OpenSearch` is already the canonical runtime command and has existing coverage.

### Reuse generic persistence

Do not add `OpenSearch` branches to `ConfigManager`. Its system-key save/load code is deliberately enum-driven and already supports optional commands. The implementation should add characterization coverage and leave production persistence untouched if those tests pass.

### Reuse the existing modal dispatch

Do not add a second Song Select hotkey path. The mapped command must use the existing `ExecuteInputCommand(InputCommand)` case and `OpenSearchFilterModal()` method.

### Keep Search optional

Do not add `OpenSearch` to `KeyConflictChecker.RequiredCommands` or `ConfigManager.RequiredSystemCommands`. Losing a Search binding must not require fallback restoration because Backspace remains available and Search is not core navigation.

### Update ordinal-sensitive tests, not production architecture

`Actions.Length` correctly derives the runtime footer indices, but several tests currently hard-code the eight-row ordering. Those tests must be updated to the nine-row contract instead of introducing new production abstractions to preserve old test ordinals.

Where a test is trying to reach Save, prefer the existing private `FooterSave` value through a small test-only reflection helper. Where a test intentionally selects a specific action row, update the expected ordinal to the new product ordering.

## Testing strategy

Add focused coverage around the newly reachable path and update the existing action-list tests that are legitimately affected:

1. **Panel exposure** — prove `SystemKeyAssignPanel.Actions` contains `OpenSearch`; keep the existing formatter case that pins `Open Search`.
2. **Optional semantics** — extend system-panel and conflict-checker tests so `OpenSearch` is explicitly non-required and unbound by default.
3. **Ordinal blast radius** — update `KeyAssignPanelWorkingCopyTests`, `KeyAssignPanelAdditionalCoverageTests`, and the ConfigStage system-panel integration test for the ninth row/footer index.
4. **Panel layout** — pin the 580px board in the existing draw spy coverage so the ninth row does not regress to the overlapping 540px layout.
5. **Persistence round trip** — save non-reserved `F1` for `OpenSearch`, flush the SQLite-backed config, reload it, and prove a fresh configured `InputManager` restores `F1 -> OpenSearch`.
6. **Song Select command suppression** — add the mapped-command drain test beside the existing `ProcessInputCommands` coverage and reuse that file's local `QueuedInputManager.Enqueue`, `CreateScoreNode`, and `AttachCoreUi` helpers.
7. Keep the existing Backspace tests unchanged as regression coverage for the legacy shortcut.

Characterization tests for persistence and Song Select may already be green on current `main`; that is expected. Do not manufacture production changes when the existing generic path already satisfies the contract.

## Files expected to change during implementation

Production:

- `DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs`

Tests:

- `DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs`
- `DTXMania.Test/Config/KeyAssignPanelWorkingCopyTests.cs`
- `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`
- `DTXMania.Test/Config/ConfigStageTests.cs`
- `DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs`
- `DTXMania.Test/Stage/KeyAssign/KeyConflictCheckerTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs`

No production changes are expected in `InputManager`, `ConfigManager`, `KeyConflictChecker`, `ConfigStage`, or `SongSelectionStage`.

## Out of scope

- New NX actions that have no CX product behavior.
- A default Search key.
- Refactoring Backspace or Tab into the system-command map.
- A generic reserved-key abstraction.
- Search/filter support on Recent Plays or Bookmarks.
- Changes to the search/filter modal UI or filtering behavior.
- Changes to drum-key conflict policy.
- A generic key-assignment panel scrolling/layout framework.
- Any DTXManiaNX code.

## Acceptance

HPA-23 is complete when:

- System Key Mapping shows `Open Search` in the defined ninth-row layout without conflict/instruction overlap.
- A normal user-assigned Search key can be saved and restored through the existing SQLite-backed configuration path.
- Pressing that mapped key on All Songs opens the existing search/filter modal and prevents same-frame command leakage.
- `OpenSearch` remains optional and unbound by default.
- Existing Backspace search behavior and current navigation/conflict rules remain intact.
- Existing system-panel tests are updated for the ninth row rather than failing later in the full suite.
- No unsupported NX action commands or new input architecture are introduced.
