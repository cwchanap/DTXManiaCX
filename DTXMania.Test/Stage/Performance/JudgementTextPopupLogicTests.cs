using System;
using System.Collections.Generic;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class JudgementTextPopupLogicTests
{
    [Fact]
    public void Constructor_ShouldSetInitialPopupState()
    {
        var position = new Vector2(100, 200);

        var popup = new JudgementTextPopup("Perfect", position);

        Assert.Equal("Perfect", popup.Text);
        Assert.Equal(1.0f, popup.Alpha);
        Assert.Equal(0f, popup.YOffset);
        Assert.True(popup.IsActive);
        Assert.Equal(position, popup.CurrentPosition);
    }

    [Fact]
    public void Constructor_WhenTextIsNull_ShouldNormalizeToEmptyString()
    {
        var popup = new JudgementTextPopup(null!, Vector2.Zero);

        Assert.Equal(string.Empty, popup.Text);
        Assert.True(popup.IsActive);
    }

    [Fact]
    public void Update_WhenAnimationCompletes_ShouldDeactivatePopup()
    {
        var popup = new JudgementTextPopup("Great", Vector2.Zero);

        var stillActive = popup.Update(0.7);

        Assert.False(stillActive);
        Assert.False(popup.IsActive);
        Assert.Equal(0f, popup.Alpha);
        Assert.Equal(30f, popup.YOffset);
    }

    [Fact]
    public void Update_WhenPopupIsAlreadyInactive_ShouldKeepCompletedState()
    {
        var popup = new JudgementTextPopup("Great", Vector2.Zero);
        popup.Update(0.7);

        var stillActive = popup.Update(0.1);

        Assert.False(stillActive);
        Assert.False(popup.IsActive);
        Assert.Equal(0f, popup.Alpha);
        Assert.Equal(30f, popup.YOffset);
    }

    [Fact]
    public void CurrentPosition_ShouldRiseAsPopupAnimates()
    {
        var initialPosition = new Vector2(320, 480);
        var popup = new JudgementTextPopup("Good", initialPosition);

        popup.Update(0.3);

        Assert.Equal(initialPosition.X, popup.CurrentPosition.X);
        Assert.True(popup.CurrentPosition.Y < initialPosition.Y);
    }

    [Fact]
    public void SpawnPopup_WhenJudgementIsMapped_ShouldAddPopupAtLaneCenter()
    {
        var manager = CreateManager();
        var judgementEvent = new JudgementEvent(99, 3, 0.0, JudgementType.Great);

        manager.SpawnPopup(judgementEvent);

        var popup = Assert.Single(GetActivePopups(manager));
        Assert.Equal("Great", popup.Text);
        Assert.Equal(
            new Vector2(PerformanceUILayout.GetLaneX(judgementEvent.Lane), PerformanceUILayout.JudgementLineY - 50),
            popup.CurrentPosition);
    }

    [Fact]
    public void SpawnPopup_WhenJudgementTypeIsUnknown_ShouldIgnoreEvent()
    {
        var manager = CreateManager();

        manager.SpawnPopup(new JudgementEvent(0, 1, 0.0, (JudgementType)999));

        Assert.Empty(GetActivePopups(manager));
        Assert.Equal(0, manager.ActivePopupCount);
    }

    [Fact]
    public void SpawnPopup_WhenManagerIsDisposedOrEventIsNull_ShouldIgnoreRequest()
    {
        var disposedManager = CreateManager(disposed: true);
        disposedManager.SpawnPopup(new JudgementEvent(0, 1, 0.0, JudgementType.Perfect));

        var manager = CreateManager();
        manager.SpawnPopup(null!);

        Assert.Empty(GetActivePopups(disposedManager));
        Assert.Empty(GetActivePopups(manager));
    }

    [Fact]
    public void Update_ShouldRemoveCompletedPopups()
    {
        var manager = CreateManager();
        var popups = GetActivePopups(manager);
        popups.Add(new JudgementTextPopup("Perfect", Vector2.Zero));
        popups.Add(new JudgementTextPopup("Miss", new Vector2(10, 20)));

        manager.Update(0.7);

        Assert.Empty(popups);
        Assert.Equal(0, manager.ActivePopupCount);
    }

    [Fact]
    public void Update_WhenManagerIsDisposed_ShouldReturnWithoutUpdatingPopups()
    {
        var manager = CreateManager(disposed: true);
        var popup = new JudgementTextPopup("Perfect", Vector2.Zero);
        GetActivePopups(manager).Add(popup);

        manager.Update(0.7);

        Assert.True(popup.IsActive);
        Assert.Equal(1.0f, popup.Alpha);
        Assert.Equal(0f, popup.YOffset);
        Assert.Equal(1, manager.ActivePopupCount);
    }

    [Fact]
    public void Draw_WhenSpriteBatchIsNull_ShouldReturnWithoutTouchingPopups()
    {
        var font = new Mock<IFont>().Object;
        var drawCount = 0;
        var manager = CreateManager(font: font, drawText: (_, _, _, _, _, _) => drawCount++);
        GetActivePopups(manager).Add(new JudgementTextPopup("Perfect", Vector2.Zero));

        manager.Draw(null!);

        Assert.Equal(1, manager.ActivePopupCount);
        Assert.Equal(0, drawCount);
    }

    [Fact]
    public void Draw_WhenFontIsMissingOrManagerDisposed_ShouldReturnWithoutTouchingPopups()
    {
        var drawCount = 0;
        var managerWithoutFont = CreateManager();
        GetActivePopups(managerWithoutFont).Add(new JudgementTextPopup("Perfect", Vector2.Zero));

        var fontForDisposedManager = new Mock<IFont>().Object;
        var disposedManager = CreateManager(font: fontForDisposedManager, drawText: (_, _, _, _, _, _) => drawCount++, disposed: true);

        var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

        managerWithoutFont.Draw(spriteBatch);
        disposedManager.Draw(spriteBatch);

        Assert.Equal(1, managerWithoutFont.ActivePopupCount);
        Assert.Equal(0, disposedManager.ActivePopupCount);
        Assert.Equal(0, drawCount);
    }

    [Fact]
    public void Draw_WhenPopupIsActiveAndAnotherIsInactive_ShouldEvaluateDrawAndSkipBranches()
    {
        var font = new Mock<IFont>().Object;
        string? drawnText = null;
        int? drawnX = null;
        int? drawnY = null;
        Color? drawnColor = null;
        var manager = CreateManager(
            font: font,
            drawText: (_, _, text, x, y, color) =>
            {
                drawnText = text;
                drawnX = x;
                drawnY = y;
                drawnColor = color;
            });
        var popups = GetActivePopups(manager);
        popups.Add(new JudgementTextPopup("Perfect", new Vector2(100, 200)));
        var completedPopup = new JudgementTextPopup("Miss", new Vector2(120, 220));
        completedPopup.Update(0.7);
        popups.Add(completedPopup);

        manager.Draw(ReflectionHelpers.CreateUninitialized<SpriteBatch>());

        Assert.Equal("Perfect", drawnText);
        Assert.Equal(100, drawnX);
        Assert.Equal(200, drawnY);
        Assert.Equal(Color.Yellow, drawnColor);
        Assert.Equal(2, manager.ActivePopupCount);
    }

    [Fact]
    public void Draw_WhenUsingDefaultDrawHelper_ShouldInvokeFontDrawString()
    {
        var fontMock = new Mock<IFont>();
        var manager = CreateManager(font: fontMock.Object);
        GetActivePopups(manager).Add(new JudgementTextPopup("Perfect", new Vector2(100, 200)));
        var drawText = ReflectionHelpers.GetPrivateField<Action<IFont, SpriteBatch, string, int, int, Color>>(manager, "_drawText");

        var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();
        var exception = Record.Exception(() => manager.Draw(spriteBatch));

        Assert.Null(exception);
        Assert.Equal("DrawTextWithFont", drawText!.Method.Name);
        fontMock.Verify(f => f.DrawString(spriteBatch, "Perfect", new Vector2(100, 200), It.IsAny<Color>()), Times.Once);
        Assert.Equal(1, manager.ActivePopupCount);
    }

    [Fact]
    public void LoadJudgementFont_WhenArgumentsAreNull_ShouldThrowArgumentNullException()
    {
        var loadMethod = GetLoadJudgementFontMethod();

        var nullGraphicsDevice = Assert.Throws<TargetInvocationException>(
            () => loadMethod.Invoke(null, [null!, new Mock<IResourceManager>().Object]));
        var nullResourceManager = Assert.Throws<TargetInvocationException>(
            () => loadMethod.Invoke(null, [ReflectionHelpers.CreateUninitialized<GraphicsDevice>(), null!]));

        Assert.IsType<ArgumentNullException>(nullGraphicsDevice.InnerException);
        Assert.IsType<ArgumentNullException>(nullResourceManager.InnerException);
    }

    [Fact]
    public void CreateForTesting_WhenGraphicsDeviceIsNull_ShouldThrowArgumentNullException()
    {
        var resourceManager = new Mock<IResourceManager>().Object;

        var exception = Assert.Throws<ArgumentNullException>(
            () => JudgementTextPopupManager.CreateForTesting(null!, resourceManager));

        Assert.Equal("graphicsDevice", exception.ParamName);
    }

    [Fact]
    public void CreateForTesting_WhenResourceManagerIsNull_ShouldThrowArgumentNullException()
    {
        var graphicsDevice = ReflectionHelpers.CreateUninitialized<GraphicsDevice>();

        var exception = Assert.Throws<ArgumentNullException>(
            () => JudgementTextPopupManager.CreateForTesting(graphicsDevice, null!));

        Assert.Equal("resourceManager", exception.ParamName);
    }

    [Fact]
    public void LoadJudgementFont_WhenResourceManagerReturnsFont_ShouldReturnFont()
    {
        var loadMethod = GetLoadJudgementFontMethod();
        var expectedFont = new Mock<IFont>().Object;
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.LoadFont("NotoSerifJP", 48)).Returns(expectedFont);

        var font = (IFont?)loadMethod.Invoke(
            null,
            [ReflectionHelpers.CreateUninitialized<GraphicsDevice>(), resourceManager.Object]);

        Assert.Same(expectedFont, font);
    }

    [Fact]
    public void LoadJudgementFont_WhenResourceManagerThrows_ShouldReturnNull()
    {
        var loadMethod = GetLoadJudgementFontMethod();
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.LoadFont(It.IsAny<string>(), It.IsAny<int>()))
            .Throws(new InvalidOperationException("font load failed"));

        var font = (IFont?)loadMethod.Invoke(
            null,
            [ReflectionHelpers.CreateUninitialized<GraphicsDevice>(), resourceManager.Object]);

        Assert.Null(font);
    }

    [Fact]
    public void Constructor_WithGraphicsDeviceAndResourceManager_ShouldLoadJudgementFont()
    {
        var fontMock = new Mock<IFont>().Object;
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.LoadFont("NotoSerifJP", 48)).Returns(fontMock);

        using var manager = new JudgementTextPopupManager(
            ReflectionHelpers.CreateUninitialized<GraphicsDevice>(),
            resourceManager.Object);

        var font = ReflectionHelpers.GetPrivateField<IFont>(manager, "_font");
        var drawText = ReflectionHelpers.GetPrivateField<Action<IFont, SpriteBatch, string, int, int, Color>>(manager, "_drawText");

        Assert.Same(fontMock, font);
        Assert.NotNull(drawText);
        Assert.Equal("DrawTextWithFont", drawText!.Method.Name);
    }

    [Fact]
    public void ClearAll_ShouldRemoveActivePopups()
    {
        var manager = CreateManager();
        var popups = GetActivePopups(manager);
        popups.Add(new JudgementTextPopup("Perfect", Vector2.Zero));
        popups.Add(new JudgementTextPopup("Miss", Vector2.One));

        manager.ClearAll();

        Assert.Empty(popups);
        Assert.Equal(0, manager.ActivePopupCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void GetLaneCenterPosition_WhenLaneIsInvalid_ShouldReturnScreenCenter(int laneIndex)
    {
        var manager = CreateManager();

        var position = ReflectionHelpers.InvokePrivateMethod<Vector2>(manager, "GetLaneCenterPosition", laneIndex);

        Assert.Equal(
            new Vector2(PerformanceUILayout.ScreenWidth / 2, PerformanceUILayout.JudgementLineY - 50),
            position);
    }

    [Fact]
    public void GetJudgementColor_ShouldMapKnownAndUnknownLabels()
    {
        var manager = CreateManager();

        Assert.Equal(Color.Yellow, ReflectionHelpers.InvokePrivateMethod<Color>(manager, "GetJudgementColor", "Perfect"));
        Assert.Equal(Color.LightGreen, ReflectionHelpers.InvokePrivateMethod<Color>(manager, "GetJudgementColor", "Great"));
        Assert.Equal(Color.LightBlue, ReflectionHelpers.InvokePrivateMethod<Color>(manager, "GetJudgementColor", "Good"));
        Assert.Equal(Color.Orange, ReflectionHelpers.InvokePrivateMethod<Color>(manager, "GetJudgementColor", "OK"));
        Assert.Equal(Color.Red, ReflectionHelpers.InvokePrivateMethod<Color>(manager, "GetJudgementColor", "Miss"));
        Assert.Equal(Color.White, ReflectionHelpers.InvokePrivateMethod<Color>(manager, "GetJudgementColor", "Unknown"));
    }

    [Fact]
    public void Dispose_ShouldClearPopupsAndMarkManagerDisposed()
    {
        var manager = CreateManager();
        GetActivePopups(manager).Add(new JudgementTextPopup("Great", Vector2.Zero));

        manager.Dispose();

        Assert.Empty(GetActivePopups(manager));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(manager, "_disposed"));
    }

    private static JudgementTextPopupManager CreateManager(
        IFont? font = null,
        Action<IFont, SpriteBatch, string, int, int, Color>? drawText = null,
        bool disposed = false)
    {
        return JudgementTextPopupManager.CreateForTesting(
            ReflectionHelpers.CreateUninitialized<GraphicsDevice>(),
            new Mock<IResourceManager>().Object,
            font: font,
            activePopups: null,
            drawText: drawText,
            disposed: disposed);
    }

    private static MethodInfo GetLoadJudgementFontMethod()
    {
        return typeof(JudgementTextPopupManager).GetMethod("LoadJudgementFont", BindingFlags.Static | BindingFlags.NonPublic)!;
    }

    private static List<JudgementTextPopup> GetActivePopups(JudgementTextPopupManager manager)
    {
        return ReflectionHelpers.GetPrivateField<List<JudgementTextPopup>>(manager, "_activePopups")!;
    }
}
