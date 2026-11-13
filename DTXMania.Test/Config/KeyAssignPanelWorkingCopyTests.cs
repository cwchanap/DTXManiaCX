using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
namespace DTXMania.Test.Config;

[Trait("Category", "Config")]
public class KeyAssignPanelWorkingCopyTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void SystemPanel_Save_ShouldLeaveLiveStateUntouched()
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

        // Navigate to FooterSave (index = ActionCount)
        var footerSave = ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterSave");
        for (int i = 0; i < footerSave; i++)
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
    [Theory]
    [InlineData(7, Keys.PageUp, InputCommandType.IncreaseScrollSpeed)]
    [InlineData(8, Keys.PageDown, InputCommandType.DecreaseScrollSpeed)]
    public void SystemPanel_DeleteOnOptionalAction_ShouldClearBinding(int selectedIndex, Keys expectedKey, InputCommandType command)
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new System.Collections.Generic.Dictionary<string, int>();
        panel.Activate();

        for (int i = 0; i < selectedIndex; i++)
            PressKey(panel, Keys.Down);

        var before = panel.GetWorkingMappingSnapshot();
        Assert.True(before.ContainsKey(expectedKey));
        Assert.Equal(command, before[expectedKey]);

        PressKey(panel, Keys.Delete);

        var after = panel.GetWorkingMappingSnapshot();
        Assert.False(after.ContainsKey(expectedKey),
            $"{command} is non-required and should be clearable via Delete");
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

        var footerSave = ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterSave");
        for (int i = 0; i < footerSave; i++)
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

        // From row 2 (MoveLeft), FooterSave - 2 MoveDown presses land on SAVE.
        var downsToSave = ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterSave") - 2;
        for (int i = 0; i < downsToSave; i++)
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
        Assert.Equal("W/S: Navigate | F: Assign | Q: Cancel", panel.GetInstructionText());
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
        Assert.Equal("W/S: Navigate | F: Assign | BACK: Cancel", panel.GetInstructionText());
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

        var footerSave = ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterSave");
        for (int i = 0; i < footerSave; i++)
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

    [Fact]
    public void SystemPanel_Actions_ShouldMatchExactNineRowOrdering()
    {
        // Full-array pin: OpenSearch sits between Back and the scroll-speed actions,
        // and no future row may silently reorder or drop the panel's actions.
        var field = typeof(SystemKeyAssignPanel).GetField(
            "Actions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var actions = Assert.IsType<InputCommandType[]>(field!.GetValue(null));

        Assert.Equal(new[]
        {
            InputCommandType.MoveUp,
            InputCommandType.MoveDown,
            InputCommandType.MoveLeft,
            InputCommandType.MoveRight,
            InputCommandType.Activate,
            InputCommandType.Back,
            InputCommandType.OpenSearch,
            InputCommandType.IncreaseScrollSpeed,
            InputCommandType.DecreaseScrollSpeed,
        }, actions);
    }

    [Fact]
    public void SystemPanel_MoveUpFromTop_ShouldWrapToFooterCancel()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel.Activate();

        PressKey(panel, Keys.Up);

        Assert.Equal(ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"), ReflectionHelpers.GetPrivateField<int>(panel, "_selectedIndex"));
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

        for (int i = 0; i < ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"); i++)
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

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"));
        ReflectionHelpers.InvokePrivateMethod(panel, "ShowConflict", "System conflict");

        var drawException = Record.Exception(() => panel.Draw(null!, null, null, null, 1280, 720));
        panel.Update(2.1, new KeyboardState(), new KeyboardState());

        Assert.Null(drawException);
        Assert.Equal("Browsing", GetStateName(panel));
        Assert.Null(ReflectionHelpers.GetPrivateField<string>(panel, "_conflictMessage"));
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
            ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterSave"));

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

        var drawCalls = new List<(string Text, Vector2 Position)>();
        fontMock
            .Setup(f => f.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()))
            .Callback<SpriteBatch, string, Vector2, Color>((_, text, position, _) => drawCalls.Add((text, position)));

        panel.Draw(spriteBatch, fontMock.Object, boldFontMock.Object, null, 1280, 720);

        fontMock.Verify(
            f => f.DrawString(spriteBatch, "Conflict: System conflict",
                It.IsAny<Vector2>(), It.Is<Color>(c => c == new Color(255, 96, 96))),
            Times.Once,
            "conflict message should be drawn with ConflictColor");

        // Layout invariant: the conflict line must not overlap the instruction line
        // pinned near the board bottom, whatever the board height ends up being.
        var conflictCall = Assert.Single(drawCalls, c => c.Text == "Conflict: System conflict");
        var instructionCall = Assert.Single(drawCalls, c => c.Text == panel.GetInstructionText());
        var conflictBottom = conflictCall.Position.Y + fontMock.Object.MeasureString(conflictCall.Text).Y;
        Assert.True(conflictBottom <= instructionCall.Position.Y,
            $"conflict line bottom ({conflictBottom}) must not overlap instruction line top ({instructionCall.Position.Y})");
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

    #region SystemKeyAssignPanel Tests

    [Fact]
    public void SystemPanel_Update_WhenShowingConflict_ShouldCountDownTimer()
    {
        using var inputManager = new InputManager();
        var panel = new SystemKeyAssignPanel(inputManager);
        panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
        panel.Activate();

        ReflectionHelpers.SetPrivateField(panel, "_selectedIndex", ReflectionHelpers.GetStaticIntField(typeof(SystemKeyAssignPanel), "FooterCancel"));
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

        for (int i = 0; i < 7; i++)
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

        for (int i = 0; i < 7; i++)
            PressKey(panel, Keys.Down);

        var before = panel.GetWorkingMappingSnapshot();
        Assert.True(before.ContainsKey(Keys.PageUp));

        panel.Update(0.0, new KeyboardState(Keys.Q), new KeyboardState());

        var after = panel.GetWorkingMappingSnapshot();
        Assert.False(after.ContainsKey(Keys.PageUp));
    }

    #endregion

    private static string? GetStateName(object panel)
    {
        return ReflectionHelpers.GetPrivateField<object>(panel, "_state")?.ToString();
    }
}
