using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using MeasureLine = DTXMania.Game.Lib.Song.Components.MeasureLine;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class TabPlayfieldTests
{
    internal static TabHudDisplay CreateHud(Mock<IFont>? font = null)
    {
        font ??= new Mock<IFont>();
        font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns<string>(s => new Vector2(s.Length * 5, 10));
        font.SetupGet(f => f.LineSpacing).Returns(10);
        return new TabHudDisplay(null, "Title", "Artist", null, null, null, null,
            (_, _) => font.Object, (_, _, _, _) => { }, (_, _, _, _, _) => { });
    }

    internal static TabHudDisplay CreateUnreadyHud() =>
        new(null, "Title", "Artist", null, null, null, null,
            (_, _) => throw new InvalidOperationException("font unavailable"),
            (_, _, _, _) => { }, (_, _, _, _, _) => { });

    internal static Mock<IResourceManager> Resources(bool complete = true)
    {
        var font = new Mock<IFont>();
        font.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(complete);
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>())).Returns(font.Object);
        return resources;
    }

    internal static TabPlayfield? Create(Mock<IResourceManager> resources, TabHudDisplay? hud,
        IReadOnlySet<int>? automated = null, bool hideCombo = false) =>
        TabPlayfield.Create(resources.Object, new NotationChart(Array.Empty<NotationMeasure>()),
            new[] { new MeasureLine { Bar = 0, TimeMs = 0 } }, null,
            automated ?? new HashSet<int>(), hideCombo, (_, _) => hud);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DrawHud_ShouldRespectFrozenHideComboWithPositiveCombo(bool hideCombo)
    {
        var font = new Mock<IFont>();
        var hud = CreateHud(font);
        using var field = Create(Resources(), hud, hideCombo: hideCombo)!;

        field.DrawHud(null!, new PerformanceFrameState { Combo = 17, FastCount = 3, SlowCount = 4 });

        font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "17", It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()),
            Times.Exactly(hideCombo ? 0 : 5));
        font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "COMBO", It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()),
            Times.Exactly(hideCombo ? 0 : 2));
        font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "3/4", It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()),
            Times.Once);
    }

    [Fact]
    public void Feedback_ShouldKeepManualPopupForPartialAutoplayAndFlashHitAndMiss()
    {
        using var hud = CreateHud();
        using var field = Create(Resources(), hud, new HashSet<int> { 3 })!;

        field.OnLaneInput(5);
        Assert.Equal(1f, hud.FlashTimersForTesting[5]);
        Assert.Equal(PerformanceUILayout.GetLaneColor(5), hud.FlashColorsForTesting[5]);
        field.OnJudgement(new JudgementEvent(1, 5, -12, JudgementType.Perfect), default, true);
        var playerPopup = hud.PopupForTesting;
        Assert.Equal("PERFECT", playerPopup!.Text);
        Assert.Equal("-12", hud.PopupTimingForTesting.Text);
        Assert.False(hud.PopupTimingForTesting.IsSlow);
        field.OnJudgement(new JudgementEvent(2, 3, 0, JudgementType.Perfect), default, false);
        Assert.Same(playerPopup, hud.PopupForTesting);
        Assert.Equal(1f, hud.FlashTimersForTesting[3]);
        field.OnJudgement(new JudgementEvent(3, 5, 0, JudgementType.Miss), default, false);
        Assert.Equal("MISS", hud.PopupForTesting!.Text);
        Assert.Null(hud.PopupTimingForTesting.Text);
        Assert.Equal(hud.Palette.Danger, hud.FlashColorsForTesting[5]);
        field.Update(1, gameplayActive: false);
        Assert.Equal(0f, hud.FlashTimersForTesting[5]);
    }

    [Fact]
    public void FeedbackFullAutoplay_ShouldReplacePopupWithoutTiming()
    {
        using var hud = CreateHud();
        using var field = Create(Resources(), hud, Enumerable.Range(0, PerformanceUILayout.LaneCount).ToHashSet())!;

        field.OnJudgement(new JudgementEvent(1, 3, 0, JudgementType.Perfect), default, false);

        Assert.Equal("PERFECT", hud.PopupForTesting!.Text);
        Assert.Null(hud.PopupTimingForTesting.Text);
        Assert.Equal(1f, hud.FlashTimersForTesting[3]);
    }

    [Fact]
    public void FeedbackManualHitWithTimingHidden_ShouldStillShowPopup()
    {
        var hud = CreateHud();
        using var field = Create(Resources(), hud)!;
        field.OnJudgement(new JudgementEvent(1, 3, -12, JudgementType.Perfect), default, false);
        Assert.Equal("PERFECT", hud.PopupForTesting!.Text);
        Assert.Null(hud.PopupTimingForTesting.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructionWhenStripOrHudUnready_ShouldReleasePartialResources(bool completeStrip)
    {
        var resources = Resources(completeStrip);
        var font = resources.Object.LoadFont("Bravura-Notation", 60);
        using var hud = CreateHud();
        var candidate = TabPlayfield.Create(resources.Object, new NotationChart(Array.Empty<NotationMeasure>()),
            Array.Empty<MeasureLine>(), null, new HashSet<int>(), false,
            (_, _) => { hud.Dispose(); return hud; });

        Assert.Null(candidate);
        Mock.Get(font).Verify(f => f.RemoveReference(), Times.Exactly(2));
        Assert.Equal(!completeStrip, hud.IsReady);
    }

    [Fact]
    public void ConstructionWhenHudFactoryThrows_ShouldReleaseStripAndPreserveFailure()
    {
        var resources = Resources();
        var font = resources.Object.LoadFont("Bravura-Notation", 60);
        var failure = new InvalidOperationException("HUD failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            TabPlayfield.Create(resources.Object, new NotationChart(Array.Empty<NotationMeasure>()),
                Array.Empty<MeasureLine>(), null, new HashSet<int>(), false, (_, _) => throw failure)));
        Mock.Get(font).Verify(f => f.RemoveReference(), Times.Exactly(2));
    }

    [Fact]
    public void DisposeWhenStripReleaseThrows_ShouldStillDisposeHudAndReleaseArt()
    {
        var resources = new Mock<IResourceManager>();
        var theme = SkinTheme.Parse(new[] { "Tab.StripTexture=Graphics/tab.png" });
        resources.SetupGet(r => r.CurrentTheme).Returns(theme);
        var art = new Mock<ITexture>();
        art.SetupGet(t => t.Width).Returns(100);
        art.SetupGet(t => t.Height).Returns(100);
        var artValue = art.Object;
        resources.Setup(r => r.TryLoadTexture("Graphics/tab.png", out artValue)).Returns(true);
        var bravura = new Mock<IFont>();
        var labels = new Mock<IFont>();
        bravura.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
        var failure = new InvalidOperationException("font release");
        bravura.Setup(f => f.RemoveReference()).Throws(failure);
        resources.Setup(r => r.LoadFont("Bravura-Notation", It.IsAny<int>())).Returns(bravura.Object);
        resources.Setup(r => r.LoadFont("ShareTechMono", It.IsAny<int>())).Returns(labels.Object);
        var hudFont = new Mock<IFont>();
        var hud = CreateHud(hudFont);
        var field = Create(resources, hud)!;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(field.Dispose));
        field.Dispose();

        bravura.Verify(f => f.RemoveReference(), Times.Once);
        labels.Verify(f => f.RemoveReference(), Times.Once);
        hudFont.Verify(f => f.Dispose(), Times.Exactly(7));
        art.Verify(t => t.RemoveReference(), Times.Once);
        Assert.False(hud.IsReady);
    }

    [Fact]
    public void HudFontFailureWhenEarlierFontDisposeThrows_ShouldStillDisposeRemainingFonts()
    {
        var first = new Mock<IFont>();
        var second = new Mock<IFont>();
        var cleanupFailure = new InvalidOperationException("font cleanup");
        first.Setup(f => f.Dispose()).Throws(cleanupFailure);
        int index = 0;
        Assert.Same(cleanupFailure, Assert.Throws<InvalidOperationException>(() =>
            new TabHudDisplay(null, "Title", "Artist", null, null, null, null,
                (_, _) => ++index switch
                {
                    1 => first.Object,
                    2 => second.Object,
                    _ => throw new InvalidOperationException("font load")
                },
                (_, _, _, _) => { }, (_, _, _, _, _) => { })));
        first.Verify(f => f.Dispose(), Times.Once);
        second.Verify(f => f.Dispose(), Times.Once);
    }
}
