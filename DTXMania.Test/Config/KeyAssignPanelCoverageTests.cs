using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class KeyAssignPanelCoverageTests
{
    [Fact]
    public void SystemPanel_MoveUpFromTop_ShouldWrapToFooterCancel()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel.Activate();

        PressKey(panel, Keys.Up);

        Assert.Equal(GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"), ReflectionHelpers.GetPrivateField<int>(panel, "_selectedIndex"));
    }

    [Fact]
    public void SystemPanel_ActivateFooterCancel_ShouldCloseWithoutSaving()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        bool saved = false;
        bool closed = false;

        panel.Saved += (_, _) => saved = true;
        panel.Closed += (_, _) => closed = true;
        panel.Activate();

        for (int i = 0; i < GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"); i++)
        {
            PressKey(panel, Keys.Down);
        }

        PressKey(panel, Keys.Enter);

        Assert.False(saved);
        Assert.True(closed);
        Assert.False(panel.IsActive);
    }

    [Fact]
    public void SystemPanel_HeldKeyDuringCapture_ShouldRemainAwaitingKey()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        PressKey(panel, Keys.Enter);
        panel.Update(0.0, new KeyboardState(Keys.A), new KeyboardState(Keys.A));

        Assert.Equal("AwaitingKey", GetStateName(panel));
    }

    [Fact]
    public void SystemPanel_DisplayLabels_ShouldHandleJoinedAndUnboundMappings()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._workingMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.Up] = InputCommandType.MoveUp,
            [Keys.W] = InputCommandType.MoveUp,
            [Keys.Down] = InputCommandType.MoveDown,
            [Keys.Right] = InputCommandType.MoveRight,
            [Keys.Enter] = InputCommandType.Activate,
            [Keys.Escape] = InputCommandType.Back,
        };
        panel.Activate();

        var joinedLabel = ReflectionHelpers.InvokePrivateMethod<string>(panel, "GetDisplayKeyLabel", InputCommandType.MoveUp);
        var unboundLabel = ReflectionHelpers.InvokePrivateMethod<string>(panel, "GetDisplayKeyLabel", InputCommandType.MoveLeft);

        Assert.NotNull(joinedLabel);
        Assert.Contains("Up", joinedLabel);
        Assert.Contains("W", joinedLabel);
        Assert.Equal("(unbound)", unboundLabel);
    }

    [Fact]
    public void SystemPanel_RebindingAction_ShouldReplacePreviousKey()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        PressKey(panel, Keys.Enter);
        panel.Update(0.0, new KeyboardState(Keys.W), new KeyboardState());

        var snapshot = panel.GetWorkingMappingSnapshot();
        Assert.False(snapshot.ContainsKey(Keys.Up));
        Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.W]);
        Assert.Equal("Browsing", GetStateName(panel));
    }

    [Fact]
    public void SystemPanel_ConflictTimeoutAndDraw_ShouldRecoverWithoutResources()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel.Activate();

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"));
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "System conflict");

        var drawException = Record.Exception(() => panel.Draw(null!, null, null, 1280, 720));
        panel.Update(2.1, new KeyboardState(), new KeyboardState());

        Assert.Null(drawException);
        Assert.Equal("Browsing", GetStateName(panel));
        Assert.Null(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
    }

    [Fact]
    public void DrumPanel_MoveUpFromTop_ShouldWrapToFooterCancel()
    {
        var panel = CreateDrumPanel();
        panel.Activate();

        PressKey(panel, Keys.Up);

        Assert.Equal(GetStaticIntField(typeof(DrumKeyAssignPanel), "FooterCancel"), ReflectionHelpers.GetPrivateField<int>(panel, "_selectedIndex"));
    }

    [Fact]
    public void DrumPanel_ActivateFooterCancel_ShouldCloseWithoutSaving()
    {
        var panel = CreateDrumPanel();
        bool saved = false;
        bool closed = false;

        panel.Saved += (_, _) => saved = true;
        panel.Closed += (_, _) => closed = true;
        panel.Activate();

        for (int i = 0; i < GetStaticIntField(typeof(DrumKeyAssignPanel), "FooterCancel"); i++)
        {
            PressKey(panel, Keys.Down);
        }

        PressKey(panel, Keys.Enter);

        Assert.False(saved);
        Assert.True(closed);
        Assert.False(panel.IsActive);
    }

    [Fact]
    public void DrumPanel_HeldKeyDuringCapture_ShouldRemainAwaitingKey()
    {
        var panel = CreateDrumPanel();
        panel.Activate();

        PressKey(panel, Keys.Enter);
        panel.Update(0.0, new KeyboardState(Keys.H), new KeyboardState(Keys.H));

        Assert.Equal("AwaitingKey", GetStateName(panel));
    }

    [Fact]
    public void DrumPanel_AssigningConflictingKey_ShouldShowConflict()
    {
        var panel = CreateDrumPanel();
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.H] = InputCommandType.MoveUp
        };
        panel.Activate();

        PressKey(panel, Keys.Enter);
        panel.Update(0.0, new KeyboardState(Keys.H), new KeyboardState());

        Assert.Equal("ShowingConflict", GetStateName(panel));
        Assert.NotNull(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
    }

    [Fact]
    public void DrumPanel_CaptureCancelLabel_ShouldUseBackBindingOrFallback()
    {
        var panelWithBack = CreateDrumPanel();
        panelWithBack._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.Q] = InputCommandType.Back
        };
        panelWithBack.Activate();

        var boundLabel = ReflectionHelpers.InvokePrivateMethod<string>(panelWithBack, "GetCaptureCancelLabel");
        Assert.Equal("Q", boundLabel);

        var panelWithoutBack = CreateDrumPanel();
        panelWithoutBack._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.W] = InputCommandType.MoveUp,
            [Keys.S] = InputCommandType.MoveDown,
            [Keys.F] = InputCommandType.Activate,
        };
        panelWithoutBack.Activate();

        var fallbackLabel = ReflectionHelpers.InvokePrivateMethod<string>(panelWithoutBack, "GetCaptureCancelLabel");
        Assert.Equal("BACK", fallbackLabel);
    }

    [Fact]
    public void DrumPanel_ConflictTimeoutAndDraw_ShouldRecoverWithoutResources()
    {
        var panel = CreateDrumPanel();
        panel.Activate();

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", GetStaticIntField(typeof(DrumKeyAssignPanel), "FooterCancel"));
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "Drum conflict");

        var drawException = Record.Exception(() => panel.Draw(null!, null, null, 1280, 720));
        panel.Update(2.1, new KeyboardState(), new KeyboardState());

        Assert.Null(drawException);
        Assert.Equal("Browsing", GetStateName(panel));
        Assert.Null(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
    }

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
}
