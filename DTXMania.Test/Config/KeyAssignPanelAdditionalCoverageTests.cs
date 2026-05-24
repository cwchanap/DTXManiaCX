using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class KeyAssignPanelAdditionalCoverageTests
{
    #region DrumKeyAssignPanel Tests

    [Fact]
    public void DrumPanel_Update_WhenShowingConflict_ShouldCountDownTimer()
    {
        var panel = CreateDrumPanel();
        panel.Activate();

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", GetStaticIntField(typeof(DrumKeyAssignPanel), "FooterCancel"));
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "Test conflict");

        Assert.Equal("ShowingConflict", GetStateName(panel));
        Assert.Equal(2.0, ReflectionHelpers.GetPrivateField<double>(panel, "_conflictTimer"));

        panel.Update(0.5, new KeyboardState(), new KeyboardState());

        Assert.Equal(1.5, ReflectionHelpers.GetPrivateField<double>(panel, "_conflictTimer"));
        Assert.Equal("ShowingConflict", GetStateName(panel));
    }

    [Fact]
    public void DrumPanel_Update_WhenShowingConflictTimerExpires_ShouldReturnToBrowsing()
    {
        var panel = CreateDrumPanel();
        panel.Activate();

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", GetStaticIntField(typeof(DrumKeyAssignPanel), "FooterCancel"));
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "Test conflict");

        panel.Update(2.1, new KeyboardState(), new KeyboardState());

        Assert.Equal("Browsing", GetStateName(panel));
        Assert.Null(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
    }

    [Fact]
    public void DrumPanel_Update_WhenAwaitingKeyAndNewKeyPressed_ShouldAssignKey()
    {
        var panel = CreateDrumPanel();
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();
        panel.Activate();

        PressKey(panel, Keys.Enter);

        Assert.Equal("AwaitingKey", GetStateName(panel));

        panel.Update(0.0, new KeyboardState(Keys.Z), new KeyboardState());

        Assert.Equal("Browsing", GetStateName(panel));
        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(0, snapshot.GetLane(KeyBindings.CreateKeyButtonId(Keys.Z)));
    }

    [Fact]
    public void DrumPanel_AssignKey_WhenKeyIsBoundToRequiredSystemCommand_ShouldShowConflict()
    {
        var panel = CreateDrumPanel();
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.Up] = InputCommandType.MoveUp
        };
        panel.Activate();

        PressKey(panel, Keys.Enter);
        panel.Update(0.0, new KeyboardState(Keys.Up), new KeyboardState());

        Assert.Equal("ShowingConflict", GetStateName(panel));
        Assert.NotNull(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
    }

    [Fact]
    public void DrumPanel_Update_WhenClearBindingPressed_ShouldUnbindLane()
    {
        var liveBindings = new KeyBindings();
        liveBindings.BindButton("Key.A", 0);
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();

        panel.Activate();
        Assert.True(panel.GetWorkingBindingsSnapshot().ButtonToLane.ContainsKey("Key.A"));

        PressKey(panel, Keys.Delete);

        Assert.Equal(-1, panel.GetWorkingBindingsSnapshot().GetLane("Key.A"));
    }

    [Fact]
    public void DrumPanel_Update_WhenMoveLeftCommandPressed_ShouldClearBinding()
    {
        var liveBindings = new KeyBindings();
        liveBindings.BindButton("Key.A", 0);
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();
        panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.Q] = InputCommandType.MoveLeft
        };

        panel.Activate();
        Assert.True(panel.GetWorkingBindingsSnapshot().ButtonToLane.ContainsKey("Key.A"));

        panel.Update(0.0, new KeyboardState(Keys.Q), new KeyboardState());

        Assert.Equal(-1, panel.GetWorkingBindingsSnapshot().GetLane("Key.A"));
    }

    [Fact]
    public void DrumPanel_Update_WhenBackCommandPressedWhileAwaitingKey_ShouldReturnToBrowsing()
    {
        var panel = CreateDrumPanel();
        panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.Enter] = InputCommandType.Activate,
            [Keys.Q] = InputCommandType.Back
        };

        panel.Activate();
        PressKey(panel, Keys.Enter);
        Assert.Equal("AwaitingKey", GetStateName(panel));

        panel.Update(0.0, new KeyboardState(Keys.Q), new KeyboardState());

        Assert.Equal("Browsing", GetStateName(panel));
        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(-1, snapshot.GetLane(KeyBindings.CreateKeyButtonId(Keys.Q)));
    }

    [Fact]
    public void DrumPanel_Deactivate_ShouldSetInactive()
    {
        var panel = CreateDrumPanel();
        panel.Activate();
        Assert.True(panel.IsActive);

        panel.Deactivate();

        Assert.False(panel.IsActive);
    }

    #endregion

    #region SystemKeyAssignPanel Tests

    [Fact]
    public void SystemPanel_Update_WhenShowingConflict_ShouldCountDownTimer()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"));
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "System conflict");

        Assert.Equal("ShowingConflict", GetStateName(panel));

        panel.Update(0.5, new KeyboardState(), new KeyboardState());

        Assert.Equal(1.5, ReflectionHelpers.GetPrivateField<double>(panel, "_conflictTimer"));
        Assert.Equal("ShowingConflict", GetStateName(panel));
    }

    [Fact]
    public void SystemPanel_Update_WhenAwaitingKeyAndCancelKeyPressed_ShouldReturnToBrowsing()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        PressKey(panel, Keys.Enter);
        Assert.Equal("AwaitingKey", GetStateName(panel));

        panel.Update(0.0, new KeyboardState(Keys.Back), new KeyboardState());

        Assert.Equal("Browsing", GetStateName(panel));
        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.False(snapshot.ContainsKey(Keys.Back));
    }

    [Fact]
    public void SystemPanel_Update_WhenAwaitingKeyAndNewKeyPressed_ShouldAssignKey()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        PressKey(panel, Keys.Enter);
        Assert.Equal("AwaitingKey", GetStateName(panel));

        panel.Update(0.0, new KeyboardState(Keys.W), new KeyboardState());

        Assert.Equal("Browsing", GetStateName(panel));
        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.W]);
    }

    [Fact]
    public void SystemPanel_AssignKey_WhenSameKeyAlreadyBoundToSameAction_ShouldReturnToBrowsing()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        var before = panel.GetWorkingMappingSnapshot();
        Assert.Equal(InputCommandType.MoveUp, before[Keys.Up]);

        PressKey(panel, Keys.Enter);
        panel.Update(0.0, new KeyboardState(Keys.Up), new KeyboardState());

        Assert.Equal("Browsing", GetStateName(panel));
        var after = panel.GetWorkingMappingSnapshot();
        Assert.Equal(InputCommandType.MoveUp, after[Keys.Up]);
    }

    [Fact]
    public void SystemPanel_TryUnbindSelectedAction_WhenRequiredCommand_ShouldShowConflict()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        PressKey(panel, Keys.Delete);

        Assert.Equal("ShowingConflict", GetStateName(panel));
        Assert.NotNull(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
    }

    [Fact]
    public void SystemPanel_Update_WhenUnbindPressed_ShouldRemoveBinding()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        for (int i = 0; i < 6; i++)
            PressKey(panel, Keys.Down);

        var before = panel.GetWorkingMappingSnapshot();
        Assert.True(before.ContainsKey(Keys.PageUp));

        PressKey(panel, Keys.Delete);

        var after = panel.GetWorkingMappingSnapshot();
        Assert.False(after.ContainsKey(Keys.PageUp));
    }

    [Fact]
    public void SystemPanel_Update_WhenMoveLeftCommandPressed_ShouldUnbindOptionalAction()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.Down] = InputCommandType.MoveDown,
            [Keys.Q] = InputCommandType.MoveLeft
        };

        panel.Activate();

        for (int i = 0; i < 6; i++)
            PressKey(panel, Keys.Down);

        var before = panel.GetWorkingMappingSnapshot();
        Assert.True(before.ContainsKey(Keys.PageUp));

        panel.Update(0.0, new KeyboardState(Keys.Q), new KeyboardState());

        var after = panel.GetWorkingMappingSnapshot();
        Assert.False(after.ContainsKey(Keys.PageUp));
    }

    #endregion

    #region Helpers

    private static DrumKeyAssignPanel CreateDrumPanel()
    {
        var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(new KeyBindings()));
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();
        return panel;
    }

    private static void PressKey(IKeyAssignPanel panel, Keys key)
    {
        panel.Update(0.0, new KeyboardState(key), new KeyboardState());
    }

    private static string? GetStateName(object panel)
    {
        return ReflectionHelpers.GetPrivateField<object>(panel, "_state")?.ToString();
    }

    private static int GetStaticIntField(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (int)field!.GetValue(null)!;
    }

    private static ModularInputManager CreateUnusedModularInputManager(KeyBindings liveBindings)
    {
#pragma warning disable SYSLIB0050
        var manager = (ModularInputManager)FormatterServices.GetUninitializedObject(typeof(ModularInputManager));
#pragma warning restore SYSLIB0050
        var keyBindingsField = typeof(ModularInputManager).GetField("_keyBindings", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(keyBindingsField);
        keyBindingsField!.SetValue(manager, liveBindings);
        return manager;
    }

    #endregion
}
