using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using Microsoft.Xna.Framework.Input;
using System.Reflection;
using System.Runtime.Serialization;

namespace DTXMania.Test.Config;

public class KeyAssignPanelWorkingCopyTests
{
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

    // ─── SystemKeyAssignPanel AwaitingKey capture ─────────────────────────────

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

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void PressKey(IKeyAssignPanel panel, Keys key)
    {
        panel.Update(0.0, new KeyboardState(key), new KeyboardState());
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
