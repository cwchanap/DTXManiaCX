using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class SpriteJudgementTextPopupTests
{
    [Fact]
    public void Popup_Update_ShouldScaleThenFade()
    {
        var popup = new SpriteJudgementTextPopup(
            JudgementType.Perfect,
            new Rectangle(3, 6, 82, 22),
            new Vector2(100, 200));

        popup.Update(0.06);

        Assert.True(popup.Scale < PerformanceUILayout.SpriteJudgementTextAssets.InitialScale);
        Assert.True(popup.Scale >= PerformanceUILayout.SpriteJudgementTextAssets.SettledScale);
        Assert.True(popup.Alpha > 0f);
        Assert.True(popup.IsActive);
    }

    [Fact]
    public void Popup_Update_AfterTotalDuration_ShouldExpire()
    {
        var popup = new SpriteJudgementTextPopup(
            JudgementType.Great,
            new Rectangle(95, 6, 75, 22),
            Vector2.Zero);

        var active = popup.Update(0.5);

        Assert.False(active);
        Assert.False(popup.IsActive);
        Assert.Equal(0f, popup.Alpha);
    }

    [Theory]
    [InlineData(JudgementType.Perfect, 3, 6, 82, 22)]
    [InlineData(JudgementType.Great, 95, 6, 75, 22)]
    [InlineData(JudgementType.Good, 4, 44, 80, 22)]
    [InlineData(JudgementType.Poor, 114, 44, 38, 22)]
    [InlineData(JudgementType.Miss, 17, 82, 52, 22)]
    public void Manager_SpawnPopup_ShouldUseBundledSourceRect(
        JudgementType judgementType,
        int x,
        int y,
        int width,
        int height)
    {
        var manager = CreateManager(spriteTextureAvailable: true);

        manager.SpawnPopup(new JudgementEvent(10, 3, 0.0, judgementType));

        var popup = Assert.Single(manager.ActivePopupsForTesting);
        Assert.Equal(new Rectangle(x, y, width, height), popup.SourceRectangle);
        Assert.Equal(PerformanceUILayout.SpriteJudgementTextAssets.GetLaneTextPosition(3, popup.SourceRectangle), popup.Position);
    }

    [Fact]
    public void Manager_SpawnPopup_WhenSpriteTextureMissing_ShouldUseFontFallback()
    {
        var fallbackEvents = new List<JudgementEvent>();
        var manager = CreateManager(
            spriteTextureAvailable: false,
            fontFallback: e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
    }

    [Fact]
    public void Manager_PublicConstructor_WhenSpriteMissing_ShouldUseFontFallback()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.JudgeStringsXg)).Returns(false);
        var fallbackEvents = new List<JudgementEvent>();
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Manager_PublicConstructor_WhenSpriteUndersized_ShouldUseFontFallbackAndReleaseTexture()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(241);
        texture.SetupGet(x => x.Height).Returns(169);
        var resourceManager = CreateResourceManager(texture.Object);
        var fallbackEvents = new List<JudgementEvent>();
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Manager_PublicConstructor_WhenUnderlyingTextureNull_ShouldUseFontFallbackAndReleaseTexture()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(448);
        texture.SetupGet(x => x.Height).Returns(256);
        texture.SetupGet(x => x.Texture).Returns((Texture2D)null!);
        var resourceManager = CreateResourceManager(texture.Object);
        var fallbackEvents = new List<JudgementEvent>();
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Manager_PublicConstructor_WhenTextureValidationThrows_ShouldUseFontFallbackAndReleaseTexture()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Throws(new InvalidOperationException("invalid texture"));
        var resourceManager = CreateResourceManager(texture.Object);
        var fallbackEvents = new List<JudgementEvent>();
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Manager_PublicConstructor_WhenTextureLoadThrows_ShouldUseFontFallback()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.JudgeStringsXg)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.JudgeStringsXg)).Throws(new InvalidOperationException("load failed"));
        var fallbackEvents = new List<JudgementEvent>();
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
    }

    [Fact]
    public void Manager_SpawnPopup_WhenLaneInvalid_ShouldUseFontFallback()
    {
        var fallbackEvents = new List<JudgementEvent>();
        var manager = CreateManager(
            spriteTextureAvailable: true,
            fontFallback: e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, PerformanceUILayout.LaneCount, 0.0, JudgementType.Good);

        var exception = Record.Exception(() => manager.SpawnPopup(judgement));

        Assert.Null(exception);
        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
    }

    [Fact]
    public void Manager_Update_ShouldRemoveExpiredPopups()
    {
        var manager = CreateManager(spriteTextureAvailable: true);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));

        manager.Update(0.5);

        Assert.Empty(manager.ActivePopupsForTesting);
    }

    [Fact]
    public void Manager_ClearAll_ShouldRemoveEveryPopup()
    {
        var manager = CreateManager(spriteTextureAvailable: true);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));
        manager.SpawnPopup(new JudgementEvent(11, 1, 0.0, JudgementType.Great));

        manager.ClearAll();

        Assert.Empty(manager.ActivePopupsForTesting);
    }

    [Fact]
    public void Dispose_ShouldReleaseSpriteTextureReference()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(448);
        texture.SetupGet(x => x.Height).Returns(256);
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture.Object);

        manager.Dispose();

        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    private static SpriteJudgementTextPopupManager CreateManager(
        bool spriteTextureAvailable,
        System.Action<JudgementEvent>? fontFallback = null)
    {
        if (!spriteTextureAvailable)
        {
            return SpriteJudgementTextPopupManager.CreateForTesting(null, fontFallback);
        }

        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(448);
        texture.SetupGet(x => x.Height).Returns(256);
        return SpriteJudgementTextPopupManager.CreateForTesting(texture.Object, fontFallback);
    }

    private static Mock<IResourceManager> CreateResourceManager(ITexture texture)
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.JudgeStringsXg)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.JudgeStringsXg)).Returns(texture);
        return resourceManager;
    }
}
