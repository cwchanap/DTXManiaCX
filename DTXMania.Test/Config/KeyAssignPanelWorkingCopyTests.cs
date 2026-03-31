using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using Microsoft.Xna.Framework.Input;
using System.Reflection;
using System.Runtime.Serialization;

namespace DTXMania.Test.Config;

[Trait("Category", "Config")]
public class KeyAssignPanelWorkingCopyTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void KeyAssignPanels_Save_ShouldLeaveLiveStateUntouchedUntilStageApplies()
    {
        using var inputManager = new InputManager();
        var systemPanel = new SystemKeyAssignPanel(inputManager);

        systemPanel.Activate();

        for (int i = 0; i < 5; i++)
        {
            PressKey(systemPanel, Keys.Down);
        }

        PressKey(systemPanel, Keys.Delete);
        PressKey(systemPanel, Keys.Down);
        PressKey(systemPanel, Keys.Enter);

        var liveSnapshot = inputManager.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Back, liveSnapshot[Keys.Escape]);

        var liveBindings = new KeyBindings();
        var drumPanel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));

        drumPanel.Activate();
        PressKey(drumPanel, Keys.Delete);
        for (int i = 0; i < 10; i++)
        {
            PressKey(drumPanel, Keys.Down);
        }

        PressKey(drumPanel, Keys.Enter);

        Assert.Equal(0, liveBindings.GetLane("Key.A"));
    }

    // ─── DrumKeyAssignPanel event sequencing ─────────────────────────────────

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_CommitAndClose_ShouldRaiseSavedThenClosed()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel.Activate();

        bool savedFired = false;
        bool closedFired = false;
        bool savedBeforeClosed = false;

        panel.Saved += (_, _) => savedFired = true;
        panel.Closed += (_, _) => { closedFired = true; savedBeforeClosed = savedFired; };

        // Navigate to FooterSave (index 10 = LaneCount)
        for (int i = 0; i < 10; i++)
            PressKey(panel, Keys.Down);
        PressKey(panel, Keys.Enter);

        Assert.True(savedFired, "Saved event should fire on commit");
        Assert.True(closedFired, "Closed event should fire on commit");
        Assert.True(savedBeforeClosed, "Saved must fire before Closed");
        Assert.False(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_CancelAndClose_ShouldRaiseOnlyClosedNotSaved()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel.Activate();

        bool savedFired = false;
        bool closedFired = false;

        panel.Saved += (_, _) => savedFired = true;
        panel.Closed += (_, _) => closedFired = true;

        PressKey(panel, Keys.Escape);

        Assert.False(savedFired, "Saved must NOT fire on cancel");
        Assert.True(closedFired, "Closed must fire on cancel");
        Assert.False(panel.IsActive);
    }

    // ─── SystemKeyAssignPanel event sequencing ────────────────────────────────

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_CommitAndClose_ShouldRaiseSavedThenClosed()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel.Activate();

        bool savedFired = false;
        bool closedFired = false;
        bool savedBeforeClosed = false;

        panel.Saved += (_, _) => savedFired = true;
        panel.Closed += (_, _) => { closedFired = true; savedBeforeClosed = savedFired; };

        // Navigate to FooterSave (index 6 = ActionCount)
        for (int i = 0; i < 6; i++)
            PressKey(panel, Keys.Down);
        PressKey(panel, Keys.Enter);

        Assert.True(savedFired);
        Assert.True(closedFired);
        Assert.True(savedBeforeClosed, "Saved must fire before Closed");
        Assert.False(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_CancelAndClose_ShouldRaiseOnlyClosedNotSaved()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel.Activate();

        bool savedFired = false;
        bool closedFired = false;

        panel.Saved += (_, _) => savedFired = true;
        panel.Closed += (_, _) => closedFired = true;

        PressKey(panel, Keys.Escape);

        Assert.False(savedFired);
        Assert.True(closedFired);
        Assert.False(panel.IsActive);
    }

    // ─── DrumKeyAssignPanel AwaitingKey capture ───────────────────────────────

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_AwaitingKey_ShouldBindPressedKeyToSelectedLane()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel.Activate();

        // Lane 0 selected by default; Enter to enter AwaitingKey
        PressKey(panel, Keys.Enter);

        // Press H — first frame counts as just-pressed (previous is empty)
        panel.Update(0.0, new KeyboardState(Keys.H), new KeyboardState());

        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(0, snapshot.GetLane(KeyBindings.CreateKeyButtonId(Keys.H)));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_AwaitingKey_EscapeShouldCancelCapture()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel.Activate();

        PressKey(panel, Keys.Enter);   // Enter AwaitingKey
        PressKey(panel, Keys.Escape);  // Cancel — should return to Browsing

        Assert.True(panel.IsActive, "Panel should remain active after Escape cancels capture");

        var snapshot = panel.GetWorkingBindingsSnapshot();
        var buttonId = KeyBindings.CreateKeyButtonId(Keys.Escape);
        Assert.False(snapshot.ButtonToLane.ContainsKey(buttonId),
            "Escape must not be bound to any lane when it cancels capture");
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_AwaitingKey_RemappedBackShouldAllowEscapeAssignment()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel._navigationMappingProvider = CreateNavigationMapping;
        panel.Activate();

        PressKey(panel, Keys.F);
        PressKey(panel, Keys.Escape);

        Assert.True(panel.IsActive, "Panel should remain active after Escape binds when Back is remapped");

        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(0, snapshot.GetLane(KeyBindings.CreateKeyButtonId(Keys.Escape)));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_Clear_ShouldPreserveNonKeyboardBindingsOnSelectedLane()
    {
        var liveBindings = new KeyBindings();
        liveBindings.BindButton("MIDI.36", 0);
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));

        panel.Activate();
        PressKey(panel, Keys.Delete);

        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(-1, snapshot.GetLane(KeyBindings.CreateKeyButtonId(Keys.A)));
        Assert.Equal(0, snapshot.GetLane("MIDI.36"));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_RemappedNavigation_ShouldSaveAndClearLane()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel._navigationMappingProvider = CreateNavigationMapping;

        bool savedFired = false;
        panel.Saved += (_, _) => savedFired = true;

        panel.Activate();

        PressKey(panel, Keys.A);

        var clearedSnapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(-1, clearedSnapshot.GetLane(KeyBindings.CreateKeyButtonId(Keys.A)));

        for (int i = 0; i < 10; i++)
            PressKey(panel, Keys.S);

        PressKey(panel, Keys.F);

        Assert.True(savedFired);
        Assert.False(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_RemappedBack_ShouldCancelPanelAndCapture()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel._navigationMappingProvider = CreateNavigationMapping;

        bool closedFired = false;
        panel.Closed += (_, _) => closedFired = true;

        panel.Activate();
        PressKey(panel, Keys.F);
        PressKey(panel, Keys.Q);

        Assert.True(panel.IsActive);

        PressKey(panel, Keys.Q);

        Assert.True(closedFired);
        Assert.False(panel.IsActive);
    }

    // ─── SystemKeyAssignPanel AwaitingKey capture ─────────────────────────────

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_AwaitingKey_EscapeShouldBindNotCancel()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel.Activate();

        // Row 0 (MoveUp) selected; Enter to enter AwaitingKey
        PressKey(panel, Keys.Enter);

        // Press Escape — in SystemPanel Escape is bindable, not a cancel key
        panel.Update(0.0, new KeyboardState(Keys.Escape), new KeyboardState());

        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.True(snapshot.ContainsKey(Keys.Escape), "Escape should be bound as a system key");
        Assert.True(panel.IsActive, "Panel should remain active");
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_AwaitingKey_BackspaceShouldCancelCapture()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel.Activate();

        PressKey(panel, Keys.Enter);   // Enter AwaitingKey for row 0 (MoveUp)

        // Press Backspace — should cancel capture, not bind
        panel.Update(0.0, new KeyboardState(Keys.Back), new KeyboardState());

        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.False(snapshot.ContainsKey(Keys.Back), "Backspace must not be bound");
        Assert.True(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_DeleteOnActivate_ShouldKeepActivateBound()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel.Activate();

        for (int i = 0; i < 4; i++)
            PressKey(panel, Keys.Down);

        var before = panel.GetWorkingMappingSnapshot();
        Assert.True(before.ContainsKey(Keys.Enter));
        Assert.Equal(InputCommandType.Activate, before[Keys.Enter]);

        PressKey(panel, Keys.Delete);

        var after = panel.GetWorkingMappingSnapshot();
        Assert.True(after.ContainsKey(Keys.Enter));
        Assert.Equal(InputCommandType.Activate, after[Keys.Enter]);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(0, Keys.Up, InputCommandType.MoveUp)]
    [InlineData(1, Keys.Down, InputCommandType.MoveDown)]
    [InlineData(2, Keys.Left, InputCommandType.MoveLeft)]
    [InlineData(3, Keys.Right, InputCommandType.MoveRight)]
    [InlineData(5, Keys.Escape, InputCommandType.Back)]
    public void SystemPanel_DeleteOnRequiredAction_ShouldKeepBinding(int selectedIndex, Keys expectedKey, InputCommandType command)
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel.Activate();

        for (int i = 0; i < selectedIndex; i++)
            PressKey(panel, Keys.Down);

        PressKey(panel, Keys.Delete);

        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.True(snapshot.ContainsKey(expectedKey));
        Assert.Equal(command, snapshot[expectedKey]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_SaveWithoutEditingAction_ShouldPreserveSecondaryBindingForDisplayedAction()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel._workingMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>(inputManager.GetKeyMappingSnapshot())
        {
            [Keys.Space] = InputCommandType.Activate,
        };
        panel.Activate();

        for (int i = 0; i < 6; i++)
            PressKey(panel, Keys.Down);

        PressKey(panel, Keys.Enter);

        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Enter]);
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Space]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_RebindingToExistingKey_ShouldPreserveSecondaryBindingForAction()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel._workingMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>(inputManager.GetKeyMappingSnapshot())
        {
            [Keys.Space] = InputCommandType.Activate,
        };
        panel.Activate();

        for (int i = 0; i < 4; i++)
            PressKey(panel, Keys.Down);

        PressKey(panel, Keys.Enter);
        panel.Update(0.0, new KeyboardState(Keys.Enter), new KeyboardState());

        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Enter]);
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Space]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_RemappedNavigation_ShouldKeepRequiredMoveLeftBindingAndSave()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel._navigationMappingProvider = CreateNavigationMapping;

        bool savedFired = false;
        panel.Saved += (_, _) => savedFired = true;

        panel.Activate();

        PressKey(panel, Keys.S);
        PressKey(panel, Keys.S);
        PressKey(panel, Keys.A);

        var snapshotAfterUnbindAttempt = panel.GetWorkingMappingSnapshot();
        Assert.Equal(InputCommandType.MoveLeft, snapshotAfterUnbindAttempt[Keys.Left]);

        panel.Update(2.1, new KeyboardState(), new KeyboardState());

        for (int i = 0; i < 4; i++)
            PressKey(panel, Keys.S);

        PressKey(panel, Keys.F);

        Assert.True(savedFired);
        Assert.False(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_RemappedBack_ShouldCancelPanel()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel._navigationMappingProvider = CreateNavigationMapping;

        bool closedFired = false;
        panel.Closed += (_, _) => closedFired = true;

        panel.Activate();
        PressKey(panel, Keys.Q);

        Assert.True(closedFired);
        Assert.False(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_RemappedBack_ShouldExposeFooterAndInstructionLabelsFromNavigationMapping()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel._navigationMappingProvider = CreateNavigationMapping;

        panel.Activate();

        Assert.Equal("CANCEL (Q)", panel.GetFooterCancelLabel());
        Assert.Equal("UP/DOWN: Navigate | ENTER: Assign | Q: Cancel", panel.GetInstructionText());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_WithoutBackBinding_ShouldExposeGenericCancelLabels()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel._navigationMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>
        {
            [Keys.W] = InputCommandType.MoveUp,
            [Keys.S] = InputCommandType.MoveDown,
            [Keys.F] = InputCommandType.Activate,
        };

        panel.Activate();

        Assert.Equal("CANCEL", panel.GetFooterCancelLabel());
        Assert.Equal("UP/DOWN: Navigate | ENTER: Assign | BACK: Cancel", panel.GetInstructionText());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_CommandProvider_ShouldNavigateAndSaveWithoutKeyboardState()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();

        InputCommandType? pressedCommand = null;
        panel._commandPressedProvider = command => pressedCommand == command;

        bool savedFired = false;
        panel.Saved += (_, _) => savedFired = true;

        panel.Activate();

        for (int i = 0; i < 10; i++)
        {
            pressedCommand = InputCommandType.MoveDown;
            panel.Update(0.0, new KeyboardState(), new KeyboardState());
            pressedCommand = null;
        }

        pressedCommand = InputCommandType.Activate;
        panel.Update(0.0, new KeyboardState(), new KeyboardState());
        pressedCommand = null;

        Assert.True(savedFired);
        Assert.False(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_CommandProvider_ShouldNavigateAndSaveWithoutKeyboardState()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();

        InputCommandType? pressedCommand = null;
        panel._commandPressedProvider = command => pressedCommand == command;

        bool savedFired = false;
        panel.Saved += (_, _) => savedFired = true;

        panel.Activate();

        for (int i = 0; i < 6; i++)
        {
            pressedCommand = InputCommandType.MoveDown;
            panel.Update(0.0, new KeyboardState(), new KeyboardState());
            pressedCommand = null;
        }

        pressedCommand = InputCommandType.Activate;
        panel.Update(0.0, new KeyboardState(), new KeyboardState());
        pressedCommand = null;

        Assert.True(savedFired);
        Assert.False(panel.IsActive);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_RemappedBack_ShouldExposeFooterAndInstructionLabelsFromNavigationMapping()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel._navigationMappingProvider = CreateNavigationMapping;

        panel.Activate();

        Assert.Equal("CANCEL (Q)", panel.GetFooterCancelLabel());
        Assert.Equal("UP/DOWN: Navigate | ENTER: Assign | DELETE: Clear lane | Q: Cancel", panel.GetInstructionText());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DrumPanel_WithoutBackBinding_ShouldExposeGenericCancelLabels()
    {
        var liveBindings = new KeyBindings();
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>();
        panel._navigationMappingProvider = () => new System.Collections.Generic.Dictionary<Keys, InputCommandType>
        {
            [Keys.W] = InputCommandType.MoveUp,
            [Keys.S] = InputCommandType.MoveDown,
            [Keys.F] = InputCommandType.Activate,
        };

        panel.Activate();

        Assert.Equal("CANCEL", panel.GetFooterCancelLabel());
        Assert.Equal("UP/DOWN: Navigate | ENTER: Assign | DELETE: Clear lane | BACK: Cancel", panel.GetInstructionText());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void PressKey(IKeyAssignPanel panel, Keys key)
    {
        panel.Update(0.0, new KeyboardState(key), new KeyboardState());
    }

    private static IReadOnlyDictionary<Keys, InputCommandType> CreateNavigationMapping()
    {
        return new System.Collections.Generic.Dictionary<Keys, InputCommandType>
        {
            [Keys.W] = InputCommandType.MoveUp,
            [Keys.S] = InputCommandType.MoveDown,
            [Keys.A] = InputCommandType.MoveLeft,
            [Keys.D] = InputCommandType.MoveRight,
            [Keys.F] = InputCommandType.Activate,
            [Keys.Q] = InputCommandType.Back,
        };
    }

    // Creates a ModularInputManager with a pre-set _keyBindings field, bypassing the constructor so
    // that no real input sources or threads are initialised during tests (test-isolation requirement).
    // FormatterServices.GetUninitializedObject (SYSLIB0050) is used intentionally here; the suppression
    // is test-only code and must never appear in production paths.  If SYSLIB0050 is eventually removed,
    // replace this with a test-only factory constructor or a protected virtual hook on ModularInputManager
    // that allows _keyBindings to be injected without the full initialisation sequence.
    private static ModularInputManager CreateUnusedModularInputManager(KeyBindings liveBindings)
    {
#pragma warning disable SYSLIB0050
        var manager = (ModularInputManager)FormatterServices.GetUninitializedObject(typeof(ModularInputManager));
#pragma warning restore SYSLIB0050
        var keyBindingsField = typeof(ModularInputManager)
            .GetField("_keyBindings", BindingFlags.Instance | BindingFlags.NonPublic);

        keyBindingsField?.SetValue(manager, liveBindings);
        return manager;
    }
}
