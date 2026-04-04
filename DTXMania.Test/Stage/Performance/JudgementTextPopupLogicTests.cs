using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;

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
        disposedManager.SpawnPopup(new JudgementEvent(0, 1, 0.0, JudgementType.Just));

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

    private static JudgementTextPopupManager CreateManager(bool disposed = false)
    {
#pragma warning disable SYSLIB0050
        var manager = (JudgementTextPopupManager)FormatterServices.GetUninitializedObject(typeof(JudgementTextPopupManager));
#pragma warning restore SYSLIB0050

        ReflectionHelpers.SetPrivateField(manager, "_activePopups", new List<JudgementTextPopup>());
        ReflectionHelpers.SetPrivateField(manager, "_disposed", disposed);
        return manager;
    }

    private static List<JudgementTextPopup> GetActivePopups(JudgementTextPopupManager manager)
    {
        return ReflectionHelpers.GetPrivateField<List<JudgementTextPopup>>(manager, "_activePopups")!;
    }
}
