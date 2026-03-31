using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

namespace DTXMania.Test.Config;

public class ConfigStageTests
{
    // Note: ConfigStage constructor tests are skipped because BaseGame.ConfigManager
    // is not virtual and cannot be mocked. In a real scenario, integration tests
    // would be used instead.

    [Fact]
    public void DropdownConfigItem_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var currentValue = "1280x720";
        var availableValues = new[] { "1280x720", "1920x1080", "2560x1440" };
        var setValue = new Mock<Action<string>>();

        // Act
        var item = new DropdownConfigItem("Resolution", () => currentValue, availableValues, setValue.Object);

        // Assert
        Assert.Equal("Resolution", item.Name);
        Assert.Equal("Resolution: 1280x720", item.GetDisplayText());
    }

    [Fact]
    public void DropdownConfigItem_NextValue_ShouldCycleValues()
    {
        // Arrange
        var currentValue = "1280x720";
        var availableValues = new[] { "1280x720", "1920x1080", "2560x1440" };
        var capturedValue = string.Empty;
        var setValue = new Action<string>(value => capturedValue = value);

        var item = new DropdownConfigItem("Resolution", () => currentValue, availableValues, setValue);

        // Act
        item.NextValue();

        // Assert
        Assert.Equal("1920x1080", capturedValue);
    }

    [Fact]
    public void DropdownConfigItem_PreviousValue_ShouldCycleBackwards()
    {
        // Arrange
        var currentValue = "1280x720";
        var availableValues = new[] { "1280x720", "1920x1080", "2560x1440" };
        var capturedValue = string.Empty;
        var setValue = new Action<string>(value => capturedValue = value);

        var item = new DropdownConfigItem("Resolution", () => currentValue, availableValues, setValue);

        // Act
        item.PreviousValue();

        // Assert
        Assert.Equal("2560x1440", capturedValue); // Should wrap to end
    }

    [Fact]
    public void ToggleConfigItem_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var currentValue = true;
        var setValue = new Mock<Action<bool>>();

        // Act
        var item = new ToggleConfigItem("Fullscreen", () => currentValue, setValue.Object);

        // Assert
        Assert.Equal("Fullscreen", item.Name);
        Assert.Equal("Fullscreen: ON", item.GetDisplayText());
    }

    [Fact]
    public void ToggleConfigItem_ToggleValue_ShouldInvertBoolean()
    {
        // Arrange
        var currentValue = true;
        var capturedValue = false;
        var setValue = new Action<bool>(value => capturedValue = value);

        var item = new ToggleConfigItem("Fullscreen", () => currentValue, setValue);

        // Act
        item.ToggleValue();

        // Assert
        Assert.False(capturedValue);
    }

    [Fact]
    public void ToggleConfigItem_NextValue_ShouldInvertBoolean()
    {
        // Arrange
        var currentValue = false;
        var capturedValue = true;
        var setValue = new Action<bool>(value => capturedValue = value);

        var item = new ToggleConfigItem("VSync", () => currentValue, setValue);

        // Act
        item.NextValue();

        // Assert
        Assert.True(capturedValue);
    }

    [Fact]
    public void IntegerConfigItem_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var currentValue = 100;
        var setValue = new Mock<Action<int>>();

        // Act
        var item = new IntegerConfigItem("Volume", () => currentValue, setValue.Object, 0, 100, 10);

        // Assert
        Assert.Equal("Volume", item.Name);
        Assert.Equal("Volume: 100", item.GetDisplayText());
    }

    [Fact]
    public void IntegerConfigItem_NextValue_ShouldIncreaseByStep()
    {
        // Arrange
        var currentValue = 50;
        var capturedValue = 0;
        var setValue = new Action<int>(value => capturedValue = value);

        var item = new IntegerConfigItem("Volume", () => currentValue, setValue, 0, 100, 10);

        // Act
        item.NextValue();

        // Assert
        Assert.Equal(60, capturedValue);
    }

    [Fact]
    public void IntegerConfigItem_NextValue_ShouldRespectMaxValue()
    {
        // Arrange
        var currentValue = 100;
        var capturedValue = 0;
        var setValue = new Action<int>(value => capturedValue = value);

        var item = new IntegerConfigItem("Volume", () => currentValue, setValue, 0, 100, 10);

        // Act
        item.NextValue();

        // Assert
        Assert.Equal(100, capturedValue); // Should not exceed max
    }

    [Fact]
    public void IntegerConfigItem_PreviousValue_ShouldDecreaseByStep()
    {
        // Arrange
        var currentValue = 50;
        var capturedValue = 0;
        var setValue = new Action<int>(value => capturedValue = value);

        var item = new IntegerConfigItem("Volume", () => currentValue, setValue, 0, 100, 10);

        // Act
        item.PreviousValue();

        // Assert
        Assert.Equal(40, capturedValue);
    }

    [Fact]
    public void IntegerConfigItem_PreviousValue_ShouldRespectMinValue()
    {
        // Arrange
        var currentValue = 0;
        var capturedValue = 100;
        var setValue = new Action<int>(value => capturedValue = value);

        var item = new IntegerConfigItem("Volume", () => currentValue, setValue, 0, 100, 10);

        // Act
        item.PreviousValue();

        // Assert
        Assert.Equal(0, capturedValue); // Should not go below min
    }

    [Fact]
    public void ConfigStage_RemappedWorkingBack_ShouldShowUpdatedCancelLabelInSystemPanel()
    {
        using var inputManager = new InputManagerCompat(new ConfigManager());
        var stage = CreateConfigStage(inputManager);

        InvokePrivateMethod(stage, "LoadWorkingInputBindings");
        InvokePrivateMethod(stage, "InitializePanels");
        SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Microsoft.Xna.Framework.Input.Keys, InputCommandType>
        {
            [Microsoft.Xna.Framework.Input.Keys.W] = InputCommandType.MoveUp,
            [Microsoft.Xna.Framework.Input.Keys.S] = InputCommandType.MoveDown,
            [Microsoft.Xna.Framework.Input.Keys.A] = InputCommandType.MoveLeft,
            [Microsoft.Xna.Framework.Input.Keys.D] = InputCommandType.MoveRight,
            [Microsoft.Xna.Framework.Input.Keys.F] = InputCommandType.Activate,
            [Microsoft.Xna.Framework.Input.Keys.Q] = InputCommandType.Back,
        });

        var panel = GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
        Assert.NotNull(panel);

        panel!.Activate();

        Assert.Equal("CANCEL (Q)", panel.GetFooterCancelLabel());

        SetPrivateField(stage, "_previousKeyboardState", new Microsoft.Xna.Framework.Input.KeyboardState());
        SetPrivateField(stage, "_currentKeyboardState", new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.Escape));
        panel.Update(0.0,
            new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.Escape),
            new Microsoft.Xna.Framework.Input.KeyboardState());

        Assert.True(panel.IsActive);
    }

    [Fact]
    public void ConfigStage_RemappedWorkingBack_ShouldAllowEscapeAssignmentInDrumPanel()
    {
        using var inputManager = new InputManagerCompat(new ConfigManager());
        var stage = CreateConfigStage(inputManager);

        InvokePrivateMethod(stage, "LoadWorkingInputBindings");
        InvokePrivateMethod(stage, "InitializePanels");
        SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Microsoft.Xna.Framework.Input.Keys, InputCommandType>
        {
            [Microsoft.Xna.Framework.Input.Keys.W] = InputCommandType.MoveUp,
            [Microsoft.Xna.Framework.Input.Keys.S] = InputCommandType.MoveDown,
            [Microsoft.Xna.Framework.Input.Keys.A] = InputCommandType.MoveLeft,
            [Microsoft.Xna.Framework.Input.Keys.D] = InputCommandType.MoveRight,
            [Microsoft.Xna.Framework.Input.Keys.F] = InputCommandType.Activate,
            [Microsoft.Xna.Framework.Input.Keys.Q] = InputCommandType.Back,
        });

        var panel = GetPrivateField<DrumKeyAssignPanel>(stage, "_drumPanel");
        Assert.NotNull(panel);

        panel!.Activate();

        Assert.Equal("CANCEL (Q)", panel.GetFooterCancelLabel());

        SetPrivateField(stage, "_previousKeyboardState", new Microsoft.Xna.Framework.Input.KeyboardState());
        SetPrivateField(stage, "_currentKeyboardState", new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.F));
        panel.Update(0.0,
            new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.F),
            new Microsoft.Xna.Framework.Input.KeyboardState());

        SetPrivateField(stage, "_previousKeyboardState", new Microsoft.Xna.Framework.Input.KeyboardState());
        SetPrivateField(stage, "_currentKeyboardState", new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.Escape));
        panel.Update(0.0,
            new Microsoft.Xna.Framework.Input.KeyboardState(Microsoft.Xna.Framework.Input.Keys.Escape),
            new Microsoft.Xna.Framework.Input.KeyboardState());

        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.True(panel.IsActive);
        Assert.Equal(0, snapshot.GetLane(KeyBindings.CreateKeyButtonId(Microsoft.Xna.Framework.Input.Keys.Escape)));
    }

    private static ConfigStage CreateConfigStage(InputManagerCompat inputManager)
    {
#pragma warning disable SYSLIB0050
        var game = (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));
#pragma warning restore SYSLIB0050

        SetBackingField(game, "<ConfigManager>k__BackingField", new ConfigManager());
        SetBackingField(game, "<InputManager>k__BackingField", inputManager);

        return new ConfigStage(game);
    }

    private static void InvokePrivateMethod(object target, string methodName, params object[]? args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
    }

    private static T? GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T?)field!.GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static void SetBackingField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
