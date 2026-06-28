using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class ConfigStageCoverageTests
{
    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new ConfigStage(game), configManager, inputManager);
    }

    private static void InitializeStageMenu(ConfigStage stage, bool includePanels)
    {
        // Config is truth; only the item list (and optionally the system panel) need setup.
        ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
        if (includePanels)
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "InitializePanels");
        }
    }

    private static void SetKeyboardStates(ConfigStage stage, KeyboardState current, KeyboardState previous)
    {
        ReflectionHelpers.SetPrivateField(stage, "_currentKeyboardState", current);
        ReflectionHelpers.SetPrivateField(stage, "_previousKeyboardState", previous);
    }

    private static void SetupRuntimeSystemBindings(InputManagerCompat inputManager, Dictionary<Keys, InputCommandType> bindings)
    {
        var cmField = typeof(InputManagerCompat).GetField("_configManager",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var cm = (ConfigManager)cmField!.GetValue(inputManager)!;
        cm.SetSystemKeyBindings(bindings);
    }

    [Fact]
    public void HandleInput_WhenBackCommandPressed_ShouldChangeStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            SetKeyboardStates(stage, new KeyboardState(Keys.Escape), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                m => m.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Times.Once);
        }
    }

    [Fact]
    public void OnDeactivate_ShouldReleaseFontReferences()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

            font.Verify(f => f.RemoveReference(), Times.Once);
            boldFont.Verify(f => f.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
        }
    }

    [Fact]
    public void Dispose_WhenDisposing_ShouldReleaseFontReferences()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_disposed", false);

            var method = typeof(ConfigStage).GetMethod(
                "Dispose",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);
            method!.Invoke(stage, new object[] { true });

            font.Verify(f => f.RemoveReference(), Times.Once);
            boldFont.Verify(f => f.RemoveReference(), Times.Once);
        }
    }

    [Fact]
    public void HandleInput_WhenMoveDownPressed_ShouldIncrementCategoryIndex()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Up] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Left] = InputCommandType.MoveLeft,
                [Keys.Right] = InputCommandType.MoveRight,
            });
            SetPrivateField(stage, "_focusOnMenu", true);
            SetPrivateField(stage, "_currentCategoryIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, GetPrivateField<int>(stage, "_currentCategoryIndex"));
        }
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
        => ReflectionHelpers.SetPrivateField(target, fieldName, value);

    private static T? GetPrivateField<T>(object target, string fieldName)
        => ReflectionHelpers.GetPrivateField<T>(target, fieldName);
}
