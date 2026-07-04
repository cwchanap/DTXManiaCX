using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;

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
    public void SystemPanel_Draw_WithFont_ShouldCallDrawString()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel.Activate();
        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", 0);

        var spriteBatch = FakeSpriteBatch.Create();
        var fontMock = new Mock<IFont>();
        var boldFontMock = new Mock<IFont>();

        panel.Draw(spriteBatch, fontMock.Object, boldFontMock.Object, null, 1280, 720);

        boldFontMock.Verify(
            f => f.DrawString(spriteBatch, It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()),
            Times.AtLeastOnce);
        fontMock.Verify(
            f => f.DrawString(spriteBatch, It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void SystemPanel_Draw_WhenInactive_ShouldNotDraw()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel.Activate();
        panel.Deactivate();

        var fontMock = new Mock<IFont>();
        var boldFontMock = new Mock<IFont>();

        panel.Draw(null!, fontMock.Object, boldFontMock.Object, null, 1280, 720);

        fontMock.Verify(
            f => f.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()),
            Times.Never);
        boldFontMock.Verify(
            f => f.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()),
            Times.Never);
    }

    [Fact]
    public void SystemPanel_ConflictTimeoutAndDraw_ShouldRecoverWithoutResources()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel.Activate();

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"));
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "System conflict");

        var drawException = Record.Exception(() => panel.Draw(null!, null, null, null, 1280, 720));
        panel.Update(2.1, new KeyboardState(), new KeyboardState());

        Assert.Null(drawException);
        Assert.Equal("Browsing", GetStateName(panel));
        Assert.Null(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
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

    private sealed class FakeSpriteBatch
    {
        public static SpriteBatch Create()
        {
#pragma warning disable SYSLIB0050
            var sb = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
#pragma warning restore SYSLIB0050
            GC.SuppressFinalize(sb);
            return sb;
        }
    }

    // ---- Patch coverage: whitePixel rendering + conflict message drawing ----

    [Fact]
    public void SystemPanel_Draw_WithWhitePixel_ShouldDrawBackdropBoardAndSelectionBar()
    {
        // With a non-null whitePixel, the Draw method renders the backdrop dim, the framed board
        // (border + fill), and the selected row's selection bar — all via the DrawWhitePixel seam.
        // The spy overrides DrawWhitePixel to record calls instead of hitting the uninitialized
        // SpriteBatch.
        using var inputManager = new InputManager();
        var panel = new SpySystemKeyAssignPanel(inputManager);
        panel.Activate();
        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", 0); // first action row (selected)

        var spriteBatch = FakeSpriteBatch.Create();
        var fontMock = new Mock<IFont>();
        fontMock.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 14f));
        var boldFontMock = new Mock<IFont>();
        boldFontMock.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 14f));
        var whitePixel = CreateFakeTexture2D();

        panel.Draw(spriteBatch, fontMock.Object, boldFontMock.Object, whitePixel, 1280, 720);

        // Backdrop: full viewport dim.
        Assert.Contains(panel.WhitePixelDraws,
            d => d.Rectangle == new Rectangle(0, 0, 1280, 720) && d.Color == new Color(0, 0, 0, 200));
        // Board border (4px larger than the board) and board fill.
        Assert.Contains(panel.WhitePixelDraws, d => d.Color == new Color(74, 62, 150, 235));
        Assert.Contains(panel.WhitePixelDraws, d => d.Color == new Color(14, 16, 34, 236));
        // Selection bar on the first row.
        Assert.Contains(panel.WhitePixelDraws, d => d.Color == new Color(120, 92, 30, 190));
    }

    [Fact]
    public void SystemPanel_Draw_WithWhitePixel_ShouldDrawFooterButtonBackgrounds()
    {
        // DrawFooterButton draws a border rect and a fill rect (selected or unselected) via the
        // DrawWhitePixel seam when whitePixel is non-null.
        using var inputManager = new InputManager();
        var panel = new SpySystemKeyAssignPanel(inputManager);
        panel.Activate();
        // Select the SAVE footer button so its fill uses SelectionBarColor.
        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex",
            GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterSave"));

        var spriteBatch = FakeSpriteBatch.Create();
        var fontMock = new Mock<IFont>();
        fontMock.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 14f));
        var boldFontMock = new Mock<IFont>();
        boldFontMock.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 14f));
        var whitePixel = CreateFakeTexture2D();

        panel.Draw(spriteBatch, fontMock.Object, boldFontMock.Object, whitePixel, 1280, 720);

        // The SAVE button's fill should use SelectionBarColor (selected).
        Assert.Contains(panel.WhitePixelDraws, d => d.Color == new Color(120, 92, 30, 190));
        // The CANCEL button's fill should use RowFillColor (unselected).
        Assert.Contains(panel.WhitePixelDraws, d => d.Color == new Color(30, 34, 60, 220));
        // Both buttons have a border rect.
        var borderDraws = panel.WhitePixelDraws.Where(d => d.Color == new Color(74, 62, 150, 235));
        // At least 2 border draws: one from the board, two from the footer buttons = 3 total.
        Assert.True(borderDraws.Count() >= 3, "expected at least 3 BoardBorderColor draws (board + 2 footer buttons)");
    }

    [Fact]
    public void SystemPanel_Draw_WithConflictMessage_ShouldDrawConflictText()
    {
        // When _conflictMessage is set and a font is provided, the conflict message is drawn
        // centered on the board with ConflictColor. This covers the conflict-message drawing path.
        using var inputManager = new InputManager();
        var panel = new SpySystemKeyAssignPanel(inputManager);
        panel.Activate();
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "System conflict");

        var spriteBatch = FakeSpriteBatch.Create();
        var fontMock = new Mock<IFont>();
        fontMock.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(100f, 14f));
        var boldFontMock = new Mock<IFont>();
        boldFontMock.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 14f));

        panel.Draw(spriteBatch, fontMock.Object, boldFontMock.Object, null, 1280, 720);

        fontMock.Verify(
            f => f.DrawString(spriteBatch, "Conflict: System conflict",
                It.IsAny<Vector2>(), It.Is<Color>(c => c == new Color(255, 96, 96))),
            Times.Once,
            "conflict message should be drawn with ConflictColor");
    }

    private static Texture2D CreateFakeTexture2D()
    {
#pragma warning disable SYSLIB0050
        var tex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(tex);
        return tex;
    }

    [Theory]
    [InlineData(InputCommandType.MoveUp, "Move Up")]
    [InlineData(InputCommandType.MoveDown, "Move Down")]
    [InlineData(InputCommandType.MoveLeft, "Move Left")]
    [InlineData(InputCommandType.MoveRight, "Move Right")]
    [InlineData(InputCommandType.Activate, "Activate")]
    [InlineData(InputCommandType.Back, "Back")]
    [InlineData(InputCommandType.IncreaseScrollSpeed, "Increase Scroll Speed")]
    [InlineData(InputCommandType.DecreaseScrollSpeed, "Decrease Scroll Speed")]
    [InlineData(InputCommandType.OpenSearch, "Open Search")]
    public void FormatActionName_ShouldHumanizeEnumNames(InputCommandType action, string expected)
    {
        var method = typeof(SystemKeyAssignPanel).GetMethod(
            "FormatActionName", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = (string)method!.Invoke(null, new object[] { action })!;
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Test-only <see cref="SystemKeyAssignPanel"/> that overrides the <see cref="SystemKeyAssignPanel.DrawWhitePixel"/>
    /// seam to record draw calls instead of hitting the uninitialized SpriteBatch.
    /// </summary>
    private sealed class SpySystemKeyAssignPanel : SystemKeyAssignPanel
    {
        public List<(Rectangle Rectangle, Color Color)> WhitePixelDraws { get; } = [];

        public SpySystemKeyAssignPanel(InputManager inputManager)
            : base(inputManager)
        {
        }

        protected override void DrawWhitePixel(SpriteBatch spriteBatch, Texture2D whitePixel,
            Rectangle rectangle, Color color)
        {
            WhitePixelDraws.Add((rectangle, color));
        }
    }
}
