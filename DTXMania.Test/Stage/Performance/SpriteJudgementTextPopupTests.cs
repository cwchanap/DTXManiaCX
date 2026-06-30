using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public void Popup_Update_DuringFade_ShouldRemainActiveWithSettledScaleAndPartialAlpha()
    {
        var popup = new SpriteJudgementTextPopup(
            JudgementType.Perfect,
            new Rectangle(3, 6, 82, 22),
            new Vector2(100, 200));
        var fadeTime = PerformanceUILayout.SpriteJudgementTextAssets.PopDurationSeconds
            + (PerformanceUILayout.SpriteJudgementTextAssets.TotalDurationSeconds
                - PerformanceUILayout.SpriteJudgementTextAssets.PopDurationSeconds) / 2.0;

        var active = popup.Update(fadeTime);

        Assert.True(active);
        Assert.True(popup.IsActive);
        Assert.Equal(PerformanceUILayout.SpriteJudgementTextAssets.SettledScale, popup.Scale);
        Assert.True(popup.Alpha < 1f);
        Assert.True(popup.Alpha > 0f);
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
    public void Manager_PublicConstructor_WhenSpriteTextureNull_ShouldUseFontFallbackAndReleaseTexture()
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
    public void Manager_PublicConstructor_WhenSpriteUndersized_ShouldUseFontFallbackAndReleaseTexture()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(100);
        texture.SetupGet(x => x.Height).Returns(100);
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
    public void Manager_PublicConstructor_WhenTextureWrapperDisposed_ShouldUseFontFallbackAndReleaseTexture()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.IsDisposed).Returns(true);
        texture.SetupGet(x => x.Width).Returns(448);
        texture.SetupGet(x => x.Height).Returns(256);
        texture.SetupGet(x => x.Texture).Throws(new InvalidOperationException("disposed texture should not be dereferenced"));
        var resourceManager = CreateResourceManager(texture.Object);
        var fallbackEvents = new List<JudgementEvent>();
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        texture.VerifyGet(x => x.Texture, Times.Never);
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
    public void Manager_PublicConstructor_WhenUnderlyingTextureDisposed_ShouldUseFontFallbackAndReleaseTexture()
    {
        var texture = new MutableTexture();
        texture.SetUnderlyingDisposed(true);
        Assert.False(texture.IsDisposed);
        Assert.True(texture.Texture.IsDisposed);
        Assert.Equal(448, texture.Width);
        Assert.Equal(256, texture.Height);
        var resourceManager = CreateResourceManager(texture);
        var fallbackEvents = new List<JudgementEvent>();
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        Assert.Equal(1, texture.RemoveReferenceCount);

        manager.Dispose();

        Assert.Equal(1, texture.RemoveReferenceCount);
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
    public void Manager_SpawnPopup_WhenHeldSpriteTextureBecomesDisposed_ShouldFallbackAndReleaseReferenceOnce()
    {
        var texture = new MutableTexture();
        var fallbackEvents = new List<JudgementEvent>();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture, e => fallbackEvents.Add(e));
        manager.SpawnPopup(new JudgementEvent(10, 4, 0.0, JudgementType.Good));

        texture.IsDisposed = true;
        var fallbackJudgement = new JudgementEvent(11, 4, 0.0, JudgementType.Great);
        manager.SpawnPopup(fallbackJudgement);
        manager.SpawnPopup(new JudgementEvent(12, 4, 0.0, JudgementType.Perfect));

        Assert.Single(manager.ActivePopupsForTesting);
        Assert.Same(fallbackJudgement, fallbackEvents[0]);
        Assert.Equal(2, fallbackEvents.Count);
        Assert.Equal(1, texture.RemoveReferenceCount);

        manager.Dispose();

        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void Manager_Draw_WhenHeldSpriteTextureBecomesDisposed_ShouldFallbackActivePopupsAndReleaseReferenceOnce()
    {
        var texture = new MutableTexture();
        var fallbackEvents = new List<JudgementEvent>();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);
        manager.SpawnPopup(judgement);
        Assert.Single(manager.ActivePopupsForTesting);

        texture.IsDisposed = true;
        var exception = Record.Exception(() => manager.Draw(null!));

        Assert.Null(exception);
        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        Assert.Equal(1, texture.RemoveReferenceCount);

        manager.Dispose();

        Assert.Equal(1, texture.RemoveReferenceCount);
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

        return SpriteJudgementTextPopupManager.CreateForTesting(new MutableTexture(), fontFallback);
    }

    private static Mock<IResourceManager> CreateResourceManager(ITexture texture)
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.JudgeStringsXg)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.JudgeStringsXg)).Returns(texture);
        return resourceManager;
    }

    private class MutableTexture : ITexture
    {
        public Texture2D Texture { get; set; } = CreateTexture2DStub();
        public string SourcePath => TexturePath.JudgeStringsXg;
        public int Width
        {
            get
            {
                if (ThrowOnWidthGet)
                    throw new InvalidOperationException("Width getter failed");
                return _width;
            }
            set => _width = value;
        }
        public int Height { get; set; } = 256;
        public Vector2 Size => new Vector2(Width, Height);
        public bool IsDisposed { get; set; }
        public int ReferenceCount => 1;
        public long MemoryUsage => Width * Height * 4L;
        public int Transparency { get; set; } = 255;
        public Vector3 ScaleRatio { get; set; } = Vector3.One;
        public float ZAxisRotation { get; set; }
        public bool AdditiveBlending { get; set; }
        public int RemoveReferenceCount { get; private set; }
        public bool ThrowOnRemoveReference { get; set; }
        public bool ThrowOnWidthGet { get; set; }
        private int _width = 448;

        public void AddReference()
        {
        }

        public void RemoveReference()
        {
            RemoveReferenceCount++;
            if (ThrowOnRemoveReference)
                throw new InvalidOperationException("RemoveReference failed");
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 position)
        {
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle)
        {
        }

        public virtual void Draw(
            SpriteBatch spriteBatch,
            Rectangle destinationRectangle,
            Rectangle? sourceRectangle,
            Color color,
            float rotation,
            Vector2 origin,
            SpriteEffects effects,
            float layerDepth)
        {
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position, Vector2 scale, float rotation, Vector2 origin)
        {
        }

        public ITexture Clone()
        {
            return this;
        }

        public Color[] GetColorData()
        {
            return [];
        }

        public void SetColorData(Color[] colorData)
        {
        }

        public void SaveToFile(string filePath)
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public void SetUnderlyingDisposed(bool isDisposed)
        {
            SetDisposedFlag(Texture, isDisposed);
        }

        private static Texture2D CreateTexture2DStub()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            SetDisposedFlag(texture, false);
            return texture;
        }

        private static void SetDisposedFlag(Texture2D texture, bool isDisposed)
        {
            for (var type = texture.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? type.GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? type.GetField("<IsDisposed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.FieldType == typeof(bool))
                {
                    field.SetValue(texture, isDisposed);
                    return;
                }
            }

            throw new InvalidOperationException("Texture2D disposed flag field not found.");
        }
    }

    [Fact]
    public void Popup_Update_WithNegativeDeltaTime_ShouldClampToZero()
    {
        var popup = new SpriteJudgementTextPopup(
            JudgementType.Perfect,
            new Rectangle(3, 6, 82, 22),
            new Vector2(100, 200));

        var active = popup.Update(-0.5);

        // Negative delta is clamped to 0, so the popup stays at initial scale/alpha.
        Assert.True(active);
        Assert.True(popup.IsActive);
        Assert.Equal(PerformanceUILayout.SpriteJudgementTextAssets.InitialScale, popup.Scale);
        Assert.Equal(1f, popup.Alpha);
    }

    [Fact]
    public void Popup_Update_WhenAlreadyInactive_ShouldReturnFalse()
    {
        var popup = new SpriteJudgementTextPopup(
            JudgementType.Perfect,
            new Rectangle(3, 6, 82, 22),
            new Vector2(100, 200));

        popup.Update(0.5); // Expire the popup
        Assert.False(popup.IsActive);

        var active = popup.Update(0.1);

        Assert.False(active);
    }

    [Fact]
    public void Manager_Draw_WithValidTexture_ShouldDrawActivePopup()
    {
        var texture = new MutableTexture();
        var drawCalls = new List<Rectangle>();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));
        Assert.Single(manager.ActivePopupsForTesting);
        var spriteBatch = (SpriteBatch)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(SpriteBatch));

        var exception = Record.Exception(() => manager.Draw(spriteBatch));

        Assert.Null(exception);
        // MutableTexture.Draw is a no-op, but we verify the popup is still active (not migrated).
        Assert.Single(manager.ActivePopupsForTesting);
    }

    [Fact]
    public void Manager_Draw_WithNullSpriteBatch_ShouldNotThrow()
    {
        var texture = new MutableTexture();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));

        var exception = Record.Exception(() => manager.Draw(null!));

        Assert.Null(exception);
        Assert.Single(manager.ActivePopupsForTesting);
    }

    [Fact]
    public void Manager_Draw_WithInactivePopup_ShouldSkipDrawing()
    {
        var texture = new MutableTexture();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));
        // Expire the popup via Update
        manager.Update(0.5);
        Assert.Empty(manager.ActivePopupsForTesting);

        var spriteBatch = (SpriteBatch)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(SpriteBatch));

        var exception = Record.Exception(() => manager.Draw(spriteBatch));
        Assert.Null(exception);
    }

    [Fact]
    public void Manager_Draw_WhenDisposed_ShouldNotThrow()
    {
        var texture = new MutableTexture();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));
        manager.Dispose();

        var exception = Record.Exception(() => manager.Draw(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void Manager_SpawnPopup_WhenDisposed_ShouldNotSpawn()
    {
        var texture = new MutableTexture();
        var fallbackEvents = new List<JudgementEvent>();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture, e => fallbackEvents.Add(e));
        manager.Dispose();

        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Empty(fallbackEvents);
    }

    [Fact]
    public void Manager_SpawnPopup_WithNullJudgementEvent_ShouldNotThrow()
    {
        var texture = new MutableTexture();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);

        var exception = Record.Exception(() => manager.SpawnPopup(null!));

        Assert.Null(exception);
        Assert.Empty(manager.ActivePopupsForTesting);
    }

    [Fact]
    public void Manager_Update_WhenDisposed_ShouldNotThrow()
    {
        var texture = new MutableTexture();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));
        manager.Dispose();

        var exception = Record.Exception(() => manager.Update(0.1));
        Assert.Null(exception);
    }

    [Fact]
    public void Manager_Draw_WhenSpriteDrawThrowsButTextureValid_ShouldRethrow()
    {
        // When Draw throws but the texture is still valid (not disposed/undersized),
        // HandleSpriteDrawFailure returns false and the exception is rethrown.
        var texture = new ThrowingDrawTexture(new InvalidOperationException("GPU error"));
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));
        var spriteBatch = (SpriteBatch)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(SpriteBatch));

        Assert.Throws<InvalidOperationException>(() => manager.Draw(spriteBatch));
    }

    [Fact]
    public void Manager_Draw_WhenSpriteDrawThrowsAndTextureInvalid_ShouldMigrateToFontFallback()
    {
        // When Draw throws and the texture is invalid (disposed), HandleSpriteDrawFailure
        // returns true, releases the texture, and migrates popups to font fallback.
        var texture = new ThrowingDrawTexture(new InvalidOperationException("GPU error"));
        var fallbackEvents = new List<JudgementEvent>();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 0, 0.0, JudgementType.Perfect);
        manager.SpawnPopup(judgement);
        var spriteBatch = (SpriteBatch)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(SpriteBatch));

        // Mark texture as disposed so HandleSpriteDrawFailure treats it as invalid.
        texture.IsDisposed = true;

        var exception = Record.Exception(() => manager.Draw(spriteBatch));

        Assert.Null(exception);
        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void Manager_MigrateActivePopupsToFontFallback_WithNullSourceJudgementEvent_ShouldSkipThatPopup()
    {
        // Popups created without a source JudgementEvent (via the 3-arg constructor)
        // cannot be migrated to font fallback and should be silently skipped.
        var texture = new MutableTexture();
        var fallbackEvents = new List<JudgementEvent>();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture, e => fallbackEvents.Add(e));
        // Manually add a popup without a source event via the internal list.
        var popups = (List<SpriteJudgementTextPopup>)typeof(SpriteJudgementTextPopupManager)
            .GetField("_activePopups", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
        popups.Add(new SpriteJudgementTextPopup(
            JudgementType.Perfect,
            new Rectangle(3, 6, 82, 22),
            new Vector2(100, 200)));
        Assert.Single(manager.ActivePopupsForTesting);

        // Dispose the texture so Draw triggers MigrateActivePopupsToFontFallback.
        texture.IsDisposed = true;
        manager.Draw(null!);

        // The popup had no SourceJudgementEvent, so no fallback is invoked,
        // but the list is still cleared.
        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Empty(fallbackEvents);
    }

    [Fact]
    public void Manager_ReleaseHeldSpriteTexture_WhenRemoveReferenceThrows_ShouldNotThrow()
    {
        var texture = new MutableTexture { ThrowOnRemoveReference = true };
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture);

        var exception = Record.Exception(() => manager.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Manager_TryEnsureSpriteTextureAvailable_WhenValidationThrows_ShouldReleaseAndReturnFalse()
    {
        // When IsInvalidSpriteTexture throws (e.g. Width getter throws), the exception
        // is caught, the texture is released, and false is returned.
        var texture = new MutableTexture { ThrowOnWidthGet = true };
        var fallbackEvents = new List<JudgementEvent>();
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture, e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 0, 0.0, JudgementType.Perfect);

        manager.SpawnPopup(judgement);

        // The spawn should have fallen back since validation threw.
        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void Manager_PublicConstructor_WhenValidTextureLoaded_ShouldHoldReference()
    {
        // Exercises the LoadSpriteTexture success path: a valid texture is loaded
        // and held, allowing SpawnPopup to create sprite-based popups.
        var texture = new MutableTexture();
        var resourceManager = CreateResourceManager(texture);
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object);

        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));

        Assert.Single(manager.ActivePopupsForTesting);

        manager.Dispose();

        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void Manager_PublicConstructor_WhenResourceExistsReturnsFalse_ShouldNotLoadTexture()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.JudgeStringsXg)).Returns(false);
        var manager = new SpriteJudgementTextPopupManager(resourceManager.Object);

        // No texture loaded, so spawning should produce no popups.
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));

        Assert.Empty(manager.ActivePopupsForTesting);
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Manager_PublicConstructor_WhenResourceManagerNull_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpriteJudgementTextPopupManager(null!));
    }

    private sealed class ThrowingDrawTexture : MutableTexture
    {
        private readonly Exception _drawException;

        public ThrowingDrawTexture(Exception drawException)
        {
            _drawException = drawException;
        }

        public override void Draw(
            SpriteBatch spriteBatch,
            Rectangle destinationRectangle,
            Rectangle? sourceRectangle,
            Color color,
            float rotation,
            Vector2 origin,
            SpriteEffects effects,
            float layerDepth)
        {
            throw _drawException;
        }
    }
}
