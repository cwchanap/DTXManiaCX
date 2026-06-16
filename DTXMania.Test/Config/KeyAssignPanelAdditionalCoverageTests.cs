using System;
using System.Collections.Generic;
using System.Reflection;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class KeyAssignPanelAdditionalCoverageTests
{
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

    #endregion
}
