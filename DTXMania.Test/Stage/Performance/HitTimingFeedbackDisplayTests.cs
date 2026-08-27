using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class HitTimingFeedbackDisplayTests
{
    [Fact]
    public void CreateForTesting_ShouldOwnFixedLaneSizedState()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());

        Assert.Equal(PerformanceUILayout.LaneCount, display.ActiveStatesForTesting.Count);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Spawn_SameLane_ShouldReplaceAndRestartExistingState()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());

        display.Spawn(new JudgementEvent(1, 3, -18, JudgementType.Great));
        display.Update(0.2);
        var firstState = display.ActiveStatesForTesting[3];

        display.Spawn(new JudgementEvent(2, 3, 25, JudgementType.Good));
        var replacement = display.ActiveStatesForTesting[3];
        Assert.NotNull(replacement);

        Assert.NotSame(firstState, replacement);
        Assert.Equal("25", replacement!.Projection.Text);
        Assert.Equal(0, replacement.ElapsedSeconds);
        Assert.Equal(1f, replacement.Alpha);
        Assert.Equal(1, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Spawn_DifferentLanes_ShouldKeepBothStatesActive()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());

        display.Spawn(new JudgementEvent(1, 1, -2, JudgementType.Great));
        display.Spawn(new JudgementEvent(2, 8, 3, JudgementType.Good));

        Assert.NotNull(display.ActiveStatesForTesting[1]);
        Assert.NotNull(display.ActiveStatesForTesting[8]);
        Assert.Equal(2, display.ActiveLaneCountForTesting);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(99)]
    public void Spawn_OutOfRangeLane_ShouldIgnoreEvent(int lane)
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());

        var exception = Record.Exception(() =>
            display.Spawn(new JudgementEvent(1, lane, 1, JudgementType.Great)));

        Assert.Null(exception);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Update_ShouldFadeWithoutScalingAndExpireAtSharedDuration()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        display.Spawn(new JudgementEvent(1, 4, -12, JudgementType.Great));
        var state = display.ActiveStatesForTesting[4];
        Assert.NotNull(state);
        var initialGlyphCount = state!.GlyphCount;

        display.Update(PerformanceUILayout.HitTimingFeedback.TotalDurationSeconds / 2.0);

        var faded = display.ActiveStatesForTesting[4];
        Assert.NotNull(faded);
        Assert.Equal(initialGlyphCount, faded!.GlyphCount);
        Assert.InRange(faded.Alpha, 0.49f, 0.51f);

        display.Update(PerformanceUILayout.HitTimingFeedback.TotalDurationSeconds / 2.0);

        Assert.Null(display.ActiveStatesForTesting[4]);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
    }

    [Theory]
    [InlineData(-18.5, "-19", false, 3)]
    [InlineData(24.5, "25", true, 2)]
    [InlineData(0.4, "0", false, 1)]
    public void Draw_ShouldUseLayoutProjectionSourcesRunPositionAndBankTint(
        double deltaMs, string expectedText, bool expectedSlowBank, int lane)
    {
        var texture = new CapturingTexture();
        using var display = HitTimingFeedbackDisplay.CreateForTesting(texture);
        display.Spawn(new JudgementEvent(1, lane, deltaMs, JudgementType.Great));

        display.Draw(CreateSpriteBatchStub());

        var projection = PerformanceUILayout.HitTimingFeedback.ProjectDelta(deltaMs);
        var position = PerformanceUILayout.HitTimingFeedback.GetLaneRunPosition(lane, projection.GlyphSlots.Count);
        Assert.Equal(expectedText, projection.Text);
        Assert.Equal(projection.GlyphSlots.Count, texture.Draws.Count);

        for (var i = 0; i < texture.Draws.Count; i++)
        {
            var draw = texture.Draws[i];
            Assert.Equal(
                PerformanceUILayout.HitTimingFeedback.GetSourceRectangle(
                    projection.GlyphSlots[i], expectedSlowBank),
                draw.SourceRectangle);
            Assert.Equal((int)MathF.Round(position.X + i * PerformanceUILayout.HitTimingFeedback.GlyphWidth), draw.Destination.X);
            Assert.Equal((int)MathF.Round(position.Y), draw.Destination.Y);
            Assert.Equal(PerformanceUILayout.HitTimingFeedback.GlyphWidth, draw.Destination.Width);
            Assert.Equal(PerformanceUILayout.HitTimingFeedback.GlyphHeight, draw.Destination.Height);
            Assert.Equal(expectedSlowBank ? Color.Red : Color.Cyan, draw.Color);
        }
    }

    [Fact]
    public void InitialMissingTexture_ShouldBeSafeNoOpAndNotLoad()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(false);
        using var display = new HitTimingFeedbackDisplay(resourceManager.Object);

        var exception = Record.Exception(() =>
        {
            display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));
            display.Update(0.1);
            display.Draw(null!);
        });

        Assert.Null(exception);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void InitialUndersizedTexture_ShouldReleaseAndRemainUnavailable()
    {
        var texture = new MutableTexture
        {
            Width = PerformanceUILayout.HitTimingFeedback.RequiredTextureWidth - 1,
            Height = PerformanceUILayout.HitTimingFeedback.RequiredTextureHeight
        };
        var resourceManager = CreateResourceManager(texture);
        using var display = new HitTimingFeedbackDisplay(resourceManager.Object);

        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));
        display.Draw(null!);

        Assert.Equal(0, display.ActiveLaneCountForTesting);
        Assert.Equal(1, texture.RemoveReferenceCount);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Once);
    }

    [Fact]
    public void InitialDisposedTexture_ShouldReleaseAndRemainUnavailable()
    {
        var texture = new MutableTexture { IsDisposed = true };
        var resourceManager = CreateResourceManager(texture);
        using var display = new HitTimingFeedbackDisplay(resourceManager.Object);

        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        Assert.Equal(0, display.ActiveLaneCountForTesting);
        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void Draw_WhenHeldTextureInvalidates_ShouldReloadOnceForEpisode()
    {
        var initialTexture = new MutableTexture();
        var reloadedTexture = new CapturingTexture();
        var resourceManager = CreateResourceManager(reloadedTexture);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(initialTexture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 2, 2, JudgementType.Great));

        initialTexture.IsDisposed = true;
        display.Draw(CreateSpriteBatchStub());

        Assert.Equal(1, initialTexture.RemoveReferenceCount);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Once);
        Assert.NotEmpty(reloadedTexture.Draws);
    }

    [Fact]
    public void Draw_WhenReloadFails_ShouldNotRetryEveryFrame()
    {
        var initialTexture = new MutableTexture();
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(false);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(initialTexture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 2, 2, JudgementType.Great));

        initialTexture.IsDisposed = true;
        display.Draw(null!);
        display.Draw(null!);
        display.Spawn(new JudgementEvent(2, 3, 3, JudgementType.Great));

        resourceManager.Verify(x => x.ResourceExists(TexturePath.LagNumbers), Times.Once);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Never);
        Assert.Equal(1, initialTexture.RemoveReferenceCount);
        Assert.Equal(1, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Draw_WhenReloadSucceeds_ShouldPermitFutureInvalidationEpisode()
    {
        var initialTexture = new MutableTexture();
        var firstReload = new MutableTexture();
        var secondReload = new MutableTexture();
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(true);
        resourceManager.SetupSequence(x => x.LoadTexture(TexturePath.LagNumbers))
            .Returns(firstReload)
            .Returns(secondReload);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(initialTexture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 2, 2, JudgementType.Great));

        initialTexture.IsDisposed = true;
        display.Draw(null!);
        firstReload.IsDisposed = true;
        display.Draw(null!);

        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Exactly(2));
        Assert.Equal(1, initialTexture.RemoveReferenceCount);
        Assert.Equal(1, firstReload.RemoveReferenceCount);
    }

    [Fact]
    public void Dispose_ShouldReleaseHeldTextureOnce()
    {
        var texture = new MutableTexture();
        var display = HitTimingFeedbackDisplay.CreateForTesting(texture);

        display.Dispose();
        display.Dispose();

        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void Constructor_WhenResourceManagerIsNull_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HitTimingFeedbackDisplay(null!));
    }

    [Fact]
    public void Spawn_AfterDispose_ShouldBeNoOp()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        display.Dispose();

        var exception = Record.Exception(() =>
            display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great)));

        Assert.Null(exception);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Spawn_WhenJudgementEventIsNull_ShouldBeNoOp()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());

        var exception = Record.Exception(() => display.Spawn(null!));

        Assert.Null(exception);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Update_AfterDispose_ShouldBeNoOp()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));
        display.Dispose();

        var exception = Record.Exception(() => display.Update(0.1));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_AfterDispose_ShouldBeNoOp()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));
        display.Dispose();

        var exception = Record.Exception(() => display.Draw(CreateSpriteBatchStub()));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WhenSpriteBatchIsNull_ShouldBeNoOp()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        var exception = Record.Exception(() => display.Draw(null!));

        Assert.Null(exception);
        Assert.Equal(1, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Update_WithNegativeDeltaTime_ShouldClampToZeroAndRemainAtFullAlpha()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        display.Update(-0.5);

        var state = display.ActiveStatesForTesting[0];
        Assert.NotNull(state);
        Assert.Equal(0.0, state!.ElapsedSeconds);
        Assert.Equal(1f, state.Alpha);
        Assert.True(state.IsActive);
    }

    [Fact]
    public void Update_WhenStateAlreadyInactive_ShouldClearLaneSlot()
    {
        using var display = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));
        // Expire the state by advancing past the total duration.
        display.Update(PerformanceUILayout.HitTimingFeedback.TotalDurationSeconds + 0.01);
        Assert.Null(display.ActiveStatesForTesting[0]);

        // A subsequent update on the cleared slot must remain a no-op.
        var exception = Record.Exception(() => display.Update(0.1));
        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WhenStateAlphaIsZero_ShouldSkipLane()
    {
        var texture = new CapturingTexture();
        using var display = HitTimingFeedbackDisplay.CreateForTesting(texture);
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));
        // Advance exactly to expiry so the state is cleared (Alpha == 0 path is reached
        // via the IsActive check in Draw; here we verify cleared states draw nothing).
        display.Update(PerformanceUILayout.HitTimingFeedback.TotalDurationSeconds);

        display.Draw(CreateSpriteBatchStub());

        Assert.Empty(texture.Draws);
    }

    [Fact]
    public void LoadLagNumbersTexture_WhenLoadTextureThrows_ShouldSwallowAndReturnNull()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.LagNumbers))
            .Throws(new InvalidOperationException("boom"));
        using var display = new HitTimingFeedbackDisplay(resourceManager.Object);

        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        Assert.Equal(0, display.ActiveLaneCountForTesting);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Once);
    }

    [Fact]
    public void LoadLagNumbersTexture_WhenLoadReturnsThenThrowsOnRemoveReference_ShouldStillReturnNull()
    {
        var texture = new MutableTexture { ThrowOnRemoveReference = true, IsDisposed = true };
        var resourceManager = CreateResourceManager(texture);
        using var display = new HitTimingFeedbackDisplay(resourceManager.Object);

        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        Assert.Equal(0, display.ActiveLaneCountForTesting);
        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void TryEnsureTextureAvailable_WhenValidationThrows_ShouldReleaseAndReload()
    {
        var initialTexture = new MutableTexture { ThrowOnWidthGet = true };
        var reloadedTexture = new CapturingTexture();
        var resourceManager = CreateResourceManager(reloadedTexture);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(initialTexture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 2, 2, JudgementType.Great));

        display.Draw(CreateSpriteBatchStub());

        Assert.Equal(1, initialTexture.RemoveReferenceCount);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Once);
        Assert.NotEmpty(reloadedTexture.Draws);
    }

    [Fact]
    public void ReleaseHeldTexture_WhenRemoveReferenceThrows_ShouldSwallowFailure()
    {
        var texture = new MutableTexture { ThrowOnRemoveReference = true };
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(false);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(texture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 2, 2, JudgementType.Great));

        // Force an invalidation episode (disposed texture) so ReleaseHeldTexture runs and
        // the reload fails (ResourceExists=false). The thrown RemoveReference must be
        // swallowed, not propagated to the caller.
        texture.IsDisposed = true;
        var exception = Record.Exception(() => display.Draw(null!));

        Assert.Null(exception);
        Assert.Equal(1, texture.RemoveReferenceCount);
    }

    [Fact]
    public void Draw_WhenTextureDrawThrowsAndTextureStillValid_ShouldRethrow()
    {
        // A valid texture (not disposed, adequately sized) that throws on Draw must
        // surface the exception rather than silently swallowing a non-texture failure.
        // InvalidateOnDraw=false keeps the texture valid after the throw, so
        // HandleTextureDrawFailure returns false and the catch rethrows.
        var texture = new ThrowingDrawTexture { InvalidateOnDraw = false };
        using var display = HitTimingFeedbackDisplay.CreateForTesting(texture);
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        Assert.Throws<InvalidOperationException>(() => display.Draw(CreateSpriteBatchStub()));
    }

    [Fact]
    public void Draw_WhenTextureDrawThrowsAndTextureInvalid_ShouldSwallowAndReload()
    {
        // The texture is valid at validation time but disposes itself when Draw is called,
        // so HandleTextureDrawFailure observes an invalid texture, swallows the exception,
        // releases the bad texture, and attempts a reload so later frames can recover.
        var initialTexture = new ThrowingDrawTexture();
        var reloadedTexture = new CapturingTexture();
        var resourceManager = CreateResourceManager(reloadedTexture);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(initialTexture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        var exception = Record.Exception(() => display.Draw(CreateSpriteBatchStub()));

        Assert.Null(exception);
        Assert.Equal(1, initialTexture.RemoveReferenceCount);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Once);
    }

    [Fact]
    public void IsInvalidTexture_WhenUnderlyingTextureIsNull_ShouldTreatAsInvalid()
    {
        var texture = new MutableTexture { Texture = null! };
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(false);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(texture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        // The null underlying texture triggers invalidation; reload fails (ResourceExists=false),
        // so the spawn is dropped and the held texture is released.
        display.Draw(null!);

        Assert.Equal(1, texture.RemoveReferenceCount);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void LoadLagNumbersTexture_WhenLoadTextureReturnsNull_ShouldReturnNullWithoutThrowing()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.LagNumbers)).Returns((ITexture?)null);
        using var display = new HitTimingFeedbackDisplay(resourceManager.Object);

        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        Assert.Equal(0, display.ActiveLaneCountForTesting);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Once);
    }

    [Fact]
    public void LoadLagNumbersTexture_WhenValidationThrowsAndRemoveReferenceThrows_ShouldSwallowInnerCatch()
    {
        // LoadTexture returns a texture whose Width getter throws, so IsInvalidTexture throws
        // and the outer catch runs. The texture's RemoveReference also throws, exercising the
        // inner catch that protects the loader from a double-fault during cleanup.
        var texture = new MutableTexture { ThrowOnWidthGet = true, ThrowOnRemoveReference = true };
        var resourceManager = CreateResourceManager(texture);
        using var display = new HitTimingFeedbackDisplay(resourceManager.Object);

        var exception = Record.Exception(() =>
            display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great)));

        Assert.Null(exception);
        Assert.Equal(0, display.ActiveLaneCountForTesting);
    }

    [Fact]
    public void Draw_WhenTextureDrawThrowsAndValidityCheckThrows_ShouldSwallowAndReload()
    {
        // The texture is valid during TryEnsureTextureAvailable (Width succeeds), but its Draw
        // call flips Width to throw and then throws. HandleTextureDrawFailure's IsInvalidTexture
        // then throws, exercising its catch (which keeps invalid=true) so the failure is treated
        // as a texture episode: swallow, release, reload.
        var initialTexture = new ThrowingDrawTexture { InvalidateWidthOnDraw = true, InvalidateOnDraw = false };
        var reloadedTexture = new CapturingTexture();
        var resourceManager = CreateResourceManager(reloadedTexture);
        using var display = HitTimingFeedbackDisplay.CreateForTesting(initialTexture, resourceManager.Object);
        display.Spawn(new JudgementEvent(1, 0, 1, JudgementType.Great));

        var exception = Record.Exception(() => display.Draw(CreateSpriteBatchStub()));

        Assert.Null(exception);
        Assert.Equal(1, initialTexture.RemoveReferenceCount);
        resourceManager.Verify(x => x.LoadTexture(TexturePath.LagNumbers), Times.Once);
    }

    [Fact]
    public void HitTimingFeedbackState_Update_WhenAlreadyInactive_ShouldReturnFalseWithoutMutating()
    {
        var projection = PerformanceUILayout.HitTimingFeedback.ProjectDelta(12.5);
        var state = new HitTimingFeedbackState(0, projection);
        // Expire the state.
        while (state.Update(PerformanceUILayout.HitTimingFeedback.TotalDurationSeconds))
        {
        }

        Assert.False(state.IsActive);
        Assert.Equal(0f, state.Alpha);

        // A subsequent Update on an inactive state must short-circuit without changing alpha.
        var elapsedBefore = state.ElapsedSeconds;
        var result = state.Update(0.1);

        Assert.False(result);
        Assert.Equal(elapsedBefore, state.ElapsedSeconds);
        Assert.False(state.IsActive);
    }

    private static Mock<IResourceManager> CreateResourceManager(ITexture texture)
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(TexturePath.LagNumbers)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.LagNumbers)).Returns(texture);
        return resourceManager;
    }

    private static SpriteBatch CreateSpriteBatchStub()
    {
        return (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));
    }

    private sealed class CapturingTexture : MutableTexture
    {
        public List<DrawCall> Draws { get; } = [];

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
            Draws.Add(new DrawCall(destinationRectangle, sourceRectangle, color));
        }
    }

    /// <summary>
    /// Texture double whose Draw call always throws. When <see cref="InvalidateOnDraw"/>
    /// is set, the texture marks itself disposed before throwing so the subsequent
    /// HandleTextureDrawFailure validity check treats it as an invalid texture episode.
    /// </summary>
    private sealed class ThrowingDrawTexture : MutableTexture
    {
        public bool InvalidateOnDraw { get; set; } = true;
        public bool InvalidateWidthOnDraw { get; set; }

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
            if (InvalidateOnDraw)
                IsDisposed = true;
            if (InvalidateWidthOnDraw)
                ThrowOnWidthGet = true;
            throw new InvalidOperationException("Draw failed");
        }
    }

    private readonly record struct DrawCall(Rectangle Destination, Rectangle? SourceRectangle, Color Color);
}
