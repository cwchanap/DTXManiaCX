using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class ConfigStageCoverageTests
{
    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new ConfigStage(game), configManager, inputManager);
    }

    private static (RenderSpyConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateRenderSpyStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new RenderSpyConfigStage(game), configManager, inputManager);
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
    public void OnExitButtonClicked_ShouldChangeStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            ReflectionHelpers.InvokePrivateMethod(stage, "OnExitButtonClicked", null, EventArgs.Empty);

            stageManager.Verify(
                m => m.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Times.Once);
        }
    }

    [Fact]
    public void OnBackButtonClicked_ShouldChangeStageToTitle()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            ReflectionHelpers.InvokePrivateMethod(stage, "OnBackButtonClicked", null, EventArgs.Empty);

            stageManager.Verify(
                m => m.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Times.Once);
        }
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
    public void DrawTitle_WhenFontExists_ShouldUseMeasureStringAndDrawString()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(200, 20));
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawTitle");

            font.Verify(f => f.MeasureString("CONFIGURATION"), Times.Once);
            font.Verify(f => f.DrawString(spriteBatch, "CONFIGURATION", new Vector2(540, 50), new Color(26, 30, 46)), Times.Once);
        }
    }

    [Fact]
    public void DrawTitle_WhenFontNull_ShouldNotThrow()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            ReflectionHelpers.SetPrivateField(stage, "_font", null);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawTitle"));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void DrawConfigItems_WhenSelected_ShouldUseBoldFont()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawConfigItems");

            boldFont.Verify(f => f.DrawString(spriteBatch, It.IsAny<string>(), It.IsAny<Vector2>(), Color.Yellow), Times.AtLeastOnce);
        }
    }

    [Fact]
    public void DrawConfigItems_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
    {
        var (stage, _, inputManager) = CreateRenderSpyStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);
            SetFallbackDrawingState(stage);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawConfigItems"));

            Assert.Null(exception);
            var expectedWidths = configItems
                .ConvertAll(item => item.GetDisplayText().Length * 8)
                .OrderBy(width => width)
                .ToArray();
            var actualWidths = stage.RectangleDrawCalls
                .FindAll(call => call.Rectangle.Height == 16 && call.Color == new Color(26, 30, 46))
                .ConvertAll(call => call.Rectangle.Width)
                .OrderBy(width => width)
                .ToArray();
            Assert.Equal(expectedWidths, actualWidths);
        }
    }

    [Fact]
    public void DrawButtons_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
    {
        var (stage, _, inputManager) = CreateRenderSpyStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);
            SetFallbackDrawingState(stage);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawButtons"));

            Assert.Null(exception);
            var actualButtonRects = stage.RectangleDrawCalls
                .FindAll(call => call.Rectangle.Height == 16)
                .ConvertAll(call => (Width: call.Rectangle.Width, call.Color))
                .OrderBy(call => call.Width)
                .ToArray();
            var expectedButtonRects = new[]
            {
                (Width: "BACK".Length * 8, Color: new Color(26, 30, 46)),
                (Width: "EXIT".Length * 8, Color: Color.Green)
            }
            .OrderBy(call => call.Width)
            .ToArray();
            Assert.Equal(expectedButtonRects, actualButtonRects);
        }
    }

    [Fact]
    public void DrawInstructions_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
    {
        var (stage, _, inputManager) = CreateRenderSpyStage();
        using (inputManager)
        {
            SetFallbackDrawingState(stage);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawInstructions"));

            Assert.Null(exception);
            Assert.Contains(
                stage.RectangleDrawCalls,
                call => call.Rectangle.Height == 12 && call.Rectangle.Width > 0 && call.Color == new Color(26, 30, 46));
        }
    }

    [Fact]
    public void DrawButtons_WhenBackSelectedAndFontPresent_ShouldUseBoldFont()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            var boldFont = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", new Mock<IFont>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawButtons");

            boldFont.Verify(f => f.DrawString(spriteBatch, "BACK", It.IsAny<Vector2>(), Color.Yellow), Times.Once);
        }
    }

    [Fact]
    public void DrawButtons_WhenExitSelectedAndFontPresent_ShouldUseBoldFont()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            var boldFont = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", new Mock<IFont>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count + 1);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawButtons");

            boldFont.Verify(f => f.DrawString(spriteBatch, "EXIT", It.IsAny<Vector2>(), Color.Yellow), Times.Once);
        }
    }

    [Fact]
    public void DrawInstructions_WhenFontPresent_ShouldDrawText()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawInstructions");

            font.Verify(f => f.DrawString(spriteBatch, It.Is<string>(s => s.Contains("UP/DOWN")), It.IsAny<Vector2>(), new Color(26, 30, 46)), Times.Once);
        }
    }

    [Fact]
    public void HandleInput_WhenMoveDownPressed_ShouldIncrementSelectedIndex()
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
            SetPrivateField(stage, "_selectedIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    private static void SetFallbackDrawingState(ConfigStage stage)
    {
        ReflectionHelpers.SetPrivateField(stage, "_font", null);
        ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateUninitializedSpriteBatch());
        ReflectionHelpers.SetPrivateField(stage, "_whitePixel", CreateUninitializedTexture());
    }

    private static SpriteBatch CreateUninitializedSpriteBatch()
    {
#pragma warning disable SYSLIB0050
        var sb = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(sb);
        return sb;
    }

    private static Texture2D CreateUninitializedTexture()
    {
#pragma warning disable SYSLIB0050
        var texture = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(texture);
        return texture;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
        => ReflectionHelpers.SetPrivateField(target, fieldName, value);

    private static T? GetPrivateField<T>(object target, string fieldName)
        => ReflectionHelpers.GetPrivateField<T>(target, fieldName);

    private sealed class RenderSpyConfigStage : ConfigStage
    {
        public RenderSpyConfigStage(BaseGame game)
            : base(game)
        {
        }

        public List<(Rectangle Rectangle, Color Color)> RectangleDrawCalls { get; } = [];

        protected override void DrawFilledRectangle(Rectangle destinationRectangle, Color color)
        {
            RectangleDrawCalls.Add((destinationRectangle, color));
        }
    }
}
