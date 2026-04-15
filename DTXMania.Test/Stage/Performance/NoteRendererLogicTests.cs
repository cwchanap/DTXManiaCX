using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class NoteRendererLogicTests
{
    [Fact]
    public void SetScrollSpeed_WhenValueIsInvalid_ShouldThrowArgumentException()
    {
        var renderer = CreateRenderer();

        Assert.Throws<ArgumentException>(() => renderer.SetScrollSpeed(0));
    }

    [Fact]
    public void SetScrollSpeed_WhenValueIsValid_ShouldUpdateLookAheadAndPixelsPerMs()
    {
        var renderer = CreateRenderer();

        renderer.SetScrollSpeed(200);

        Assert.Equal(750.0, renderer.EffectiveLookAheadMs, 3);
        Assert.Equal(renderer.JudgementY / 750.0, renderer.ScrollPixelsPerMs, 3);
    }

    [Fact]
    public void SetBpm_WhenValueIsInvalid_ShouldThrowArgumentException()
    {
        var renderer = CreateRenderer();

        Assert.Throws<ArgumentException>(() => renderer.SetBpm(0));
    }

    [Fact]
    public void SetBpm_WhenValueIsValid_ShouldUpdateProperty()
    {
        var renderer = CreateRenderer();

        renderer.SetBpm(180.0);

        Assert.Equal(180.0, renderer.Bpm);
    }

    [Fact]
    public void FilterVisibleNotes_WhenNotesAreNull_ShouldReturnEmpty()
    {
        var renderer = CreateRenderer();

        var result = renderer.FilterVisibleNotes(null!, 1000.0);

        Assert.Empty(result);
    }

    [Fact]
    public void FilterVisibleNotes_ShouldReturnOnlyNotesWithinVisibleWindow()
    {
        var renderer = CreateRenderer();
        var notes = new[]
        {
            new Note { TimeMs = 799.0 },
            new Note { TimeMs = 800.0 },
            new Note { TimeMs = 2000.0 },
            new Note { TimeMs = 2600.0 }
        };

        var result = renderer.FilterVisibleNotes(notes, 1000.0, 1000.0).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(notes[1], result);
        Assert.Contains(notes[2], result);
    }

    [Fact]
    public void GetNoteScreenY_AndShouldDropNote_ShouldUseConfiguredScrollSpeed()
    {
        var renderer = CreateRenderer();
        ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 0.5);
        var note = new Note { TimeMs = 1000.0 };

        var y = renderer.GetNoteScreenY(1000.0, 900.0);

        Assert.Equal(renderer.JudgementY - 50.0, y, 3);
        Assert.False(renderer.ShouldDropNote(note, 1000.0));
        Assert.True(renderer.ShouldDropNote(note, 1200.0));
    }

    [Fact]
    public void Update_ShouldAdvanceAnimationAndDecayFlashAlpha()
    {
        var renderer = CreateReadyRenderer();
        var flashAlpha = new[] { 1.0f, 0.005f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f };
        ReflectionHelpers.SetPrivateField(renderer, "_laneFlashAlpha", flashAlpha);

        renderer.Update(0.1);

        Assert.Equal(100.0, ReflectionHelpers.GetPrivateField<double>(renderer, "_animationTimeMs"), 3);
        Assert.InRange(flashAlpha[0], 0.44f, 0.46f);
        Assert.Equal(0.0f, flashAlpha[1]);
    }

    [Fact]
    public void TriggerLaneFlash_WhenLaneIsValid_ShouldSetLaneAlphaToOne()
    {
        var renderer = CreateRenderer();
        var flashAlpha = ReflectionHelpers.GetPrivateField<float[]>(renderer, "_laneFlashAlpha");

        renderer.TriggerLaneFlash(3);

        Assert.Equal(1.0f, flashAlpha[3]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PerformanceUILayout.LaneCount)]
    public void TriggerLaneFlash_WhenLaneIsInvalid_ShouldLeaveFlashAlphaUnchanged(int laneIndex)
    {
        var renderer = CreateRenderer();
        var flashAlpha = ReflectionHelpers.GetPrivateField<float[]>(renderer, "_laneFlashAlpha");
        flashAlpha[0] = 0.25f;

        renderer.TriggerLaneFlash(laneIndex);

        Assert.Equal(0.25f, flashAlpha[0]);
    }

    [Fact]
    public void DrawNotes_WhenRendererIsNotReady_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateRenderer();

        var exception = Record.Exception(() =>
            renderer.DrawNotes((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), new[] { new Note() }, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNote_WhenLaneIndexIsInvalid_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRenderer();
        var note = new Note { LaneIndex = -1, TimeMs = 1000.0 };

        var exception = Record.Exception(() =>
            renderer.DrawNote((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), note, 1000.0));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(2500.0)]
    [InlineData(-4000.0)]
    public void DrawNote_WhenNoteIsOutsideVisibleRange_ShouldReturnWithoutThrowing(double currentSongTimeMs)
    {
        var renderer = CreateReadyRenderer();
        var note = new Note { LaneIndex = 0, TimeMs = 1000.0 };

        var exception = Record.Exception(() =>
            renderer.DrawNote((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), note, currentSongTimeMs));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNote_WhenTextureUnavailableAndFallbackTextureMissing_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRenderer();
        var note = new Note { LaneIndex = 0, Channel = 0x13, TimeMs = 1000.0 };
        ReflectionHelpers.SetPrivateField(renderer, "_drumChipsTexture", null);
        ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", null);

        var exception = Record.Exception(() =>
            renderer.DrawNote((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), note, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNote_WhenSpriteColumnIsInvalidAndFallbackTextureMissing_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRendererWithValidSpriteTexture();
        var note = new Note { LaneIndex = 0, Channel = 0x99, TimeMs = 1000.0 };
        ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", null);

        var exception = Record.Exception(() =>
            renderer.DrawNote((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), note, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNoteOverlays_WhenRendererIsNotReady_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateRenderer();

        var exception = Record.Exception(() =>
            renderer.DrawNoteOverlays((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), new[] { new Note() }, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNoteOverlays_WhenNoteUsesSnareOrTomOverlay_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRenderer();
        var note = new Note { LaneIndex = 3, Channel = 0x12, TimeMs = 1000.0 };

        var exception = Record.Exception(() =>
            renderer.DrawNoteOverlays((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), new[] { note }, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNoteOverlays_WhenLaneIndexIsInvalid_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRenderer();
        var note = new Note { LaneIndex = -1, Channel = 0x13, TimeMs = 1000.0 };

        var exception = Record.Exception(() =>
            renderer.DrawNoteOverlays((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), new[] { note }, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNoteOverlays_WhenTextureUnavailable_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRenderer();
        var note = new Note { LaneIndex = 0, Channel = 0x13, TimeMs = 1000.0 };
        ReflectionHelpers.SetPrivateField(renderer, "_drumChipsTexture", null);

        var exception = Record.Exception(() =>
            renderer.DrawNoteOverlays((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), new[] { note }, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNoteOverlays_WhenSpriteColumnIsInvalid_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRendererWithValidSpriteTexture();
        var note = new Note { LaneIndex = 0, Channel = 0x99, TimeMs = 1000.0 };

        var exception = Record.Exception(() =>
            renderer.DrawNoteOverlays((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), new[] { note }, 1000.0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawOverlayEffect_WhenRendererIsNotReady_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateRenderer();

        var exception = Record.Exception(() =>
            renderer.DrawOverlayEffect((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), 0, 0, Vector2.Zero));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawOverlayEffect_WhenOverlayFrameIndexIsInvalid_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRendererWithValidSpriteTexture();

        var exception = Record.Exception(() =>
            renderer.DrawOverlayEffect((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), 0, -1, Vector2.Zero));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawOverlayEffect_WhenLaneIndexIsInvalid_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateReadyRendererWithValidSpriteTexture();

        var exception = Record.Exception(() =>
            renderer.DrawOverlayEffect((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)), PerformanceUILayout.LaneCount, 0, Vector2.Zero));

        Assert.Null(exception);
    }

    [Fact]
    public void CheckAndReloadResources_WhenDrumTextureIsMissing_ShouldAttemptReloadAndLeaveTextureNull()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(manager => manager.LoadTexture(It.IsAny<string>())).Returns((ITexture?)null);
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D)));
        ReflectionHelpers.SetPrivateField(renderer, "_drumChipsTexture", null);

        ReflectionHelpers.InvokePrivateMethod(renderer, "CheckAndReloadResources");

        Assert.Null(ReflectionHelpers.GetPrivateField<ManagedSpriteTexture>(renderer, "_drumChipsTexture"));
        resourceManager.Verify(manager => manager.LoadTexture(TexturePath.DrumChips), Times.Once);
    }

    [Fact]
    public void CreateWhiteTexture_WhenGraphicsDeviceIsMissing_ShouldThrowInvalidOperationException()
    {
        var renderer = CreateRenderer();
        ReflectionHelpers.SetPrivateField(renderer, "_graphicsDevice", null);

        var exception = Assert.Throws<TargetInvocationException>(() => ReflectionHelpers.InvokePrivateMethod(renderer, "CreateWhiteTexture"));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Theory]
    [InlineData(0x13, 0)]
    [InlineData(0x19, 1)]
    [InlineData(0x12, 2)]
    [InlineData(0x14, 3)]
    [InlineData(0x15, 4)]
    [InlineData(0x17, 5)]
    [InlineData(0x16, 6)]
    [InlineData(0x11, 7)]
    [InlineData(0x1C, 8)]
    [InlineData(0x1A, 9)]
    [InlineData(0x18, 10)]
    [InlineData(0x1B, 11)]
    [InlineData(0x99, -1)]
    public void GetSpriteColumnForChannel_ShouldReturnExpectedMapping(int channel, int expectedColumn)
    {
        var renderer = CreateRenderer();

        var result = ReflectionHelpers.InvokePrivateMethod<int>(renderer, "GetSpriteColumnForChannel", channel);

        Assert.Equal(expectedColumn, result);
    }

    [Theory]
    [InlineData(0, 9)]
    [InlineData(1, 10)]
    [InlineData(2, 11)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 0)]
    [InlineData(6, 4)]
    [InlineData(7, 5)]
    [InlineData(8, 1)]
    [InlineData(9, 6)]
    [InlineData(99, -1)]
    public void GetSpriteColumnForLane_ShouldReturnExpectedMapping(int laneIndex, int expectedColumn)
    {
        var renderer = CreateRenderer();

        var result = ReflectionHelpers.InvokePrivateMethod<int>(renderer, "GetSpriteColumnForLane", laneIndex);

        Assert.Equal(expectedColumn, result);
    }

    [Theory]
    [InlineData(0, 70)]
    [InlineData(1, 58)]
    [InlineData(2, 64)]
    [InlineData(3, 56)]
    [InlineData(4, 56)]
    [InlineData(5, 56)]
    [InlineData(6, 74)]
    [InlineData(7, 48)]
    [InlineData(8, 58)]
    [InlineData(9, 74)]
    [InlineData(10, 48)]
    [InlineData(11, 58)]
    [InlineData(99, 64)]
    public void GetSpriteWidthForColumn_ShouldReturnExpectedWidths(int columnIndex, int expectedWidth)
    {
        var renderer = CreateRenderer();

        var result = ReflectionHelpers.InvokePrivateMethod<int>(renderer, "GetSpriteWidthForColumn", columnIndex);

        Assert.Equal(expectedWidth, result);
    }

    [Theory]
    [InlineData(0x12, true)]
    [InlineData(0x14, true)]
    [InlineData(0x15, true)]
    [InlineData(0x17, true)]
    [InlineData(0x13, false)]
    public void IsSnareOrTomChannel_ShouldMatchExpectedChannels(int channel, bool expected)
    {
        var renderer = CreateRenderer();

        var result = ReflectionHelpers.InvokePrivateMethod<bool>(renderer, "IsSnareOrTomChannel", channel);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(0, false)]
    public void IsSnareOrTomLane_ShouldMatchExpectedLanes(int laneIndex, bool expected)
    {
        var renderer = CreateRenderer();

        var result = ReflectionHelpers.InvokePrivateMethod<bool>(renderer, "IsSnareOrTomLane", laneIndex);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1, 0, 64)]
    [InlineData(0, 0, 70)]
    [InlineData(1, 70, 58)]
    [InlineData(2, 128, 64)]
    [InlineData(11, 662, 58)]
    public void GetCustomSpriteSourceRectangleForColumn_ShouldUseExpectedOffsetsAndWidths(int columnIndex, int expectedX, int expectedWidth)
    {
        var renderer = CreateRenderer();

        var result = ReflectionHelpers.InvokePrivateMethod<Rectangle>(renderer, "GetCustomSpriteSourceRectangleForColumn", columnIndex);

        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedWidth, result.Width);
    }

    [Fact]
    public void Dispose_WhenRendererHasNoResources_ShouldMarkDisposed()
    {
        var renderer = CreateRenderer();

        renderer.Dispose();

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(renderer, "_disposed"));
    }

    private static NoteRenderer CreateRenderer()
    {
        var renderer = ReflectionHelpers.CreateUninitialized<NoteRenderer>();
        ReflectionHelpers.SetPrivateField(renderer, "_lanePositions", new Vector2[PerformanceUILayout.LaneCount]);
        ReflectionHelpers.SetPrivateField(renderer, "_laneColors", Enumerable.Repeat(Color.White, PerformanceUILayout.LaneCount).ToArray());
        ReflectionHelpers.SetPrivateField(renderer, "_laneFlashAlpha", new float[PerformanceUILayout.LaneCount]);
        ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 0.5);
        ReflectionHelpers.SetPrivateField(renderer, "<EffectiveLookAheadMs>k__BackingField", 1200.0);
        ReflectionHelpers.SetPrivateField(renderer, "_disposed", false);
        return renderer;
    }

    private static NoteRenderer CreateReadyRenderer()
    {
        var renderer = CreateRenderer();
        ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D)));
        ReflectionHelpers.SetPrivateField(renderer, "_drumChipsTexture", (ManagedSpriteTexture)RuntimeHelpers.GetUninitializedObject(typeof(ManagedSpriteTexture)));
        return renderer;
    }

    private static NoteRenderer CreateReadyRendererWithValidSpriteTexture()
    {
        var renderer = CreateRenderer();
        ReflectionHelpers.SetPrivateField(renderer, "_drumChipsTexture", CreateSpriteTexture());
        return renderer;
    }

    private static ManagedSpriteTexture CreateSpriteTexture()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var spriteTexture = (ManagedSpriteTexture)RuntimeHelpers.GetUninitializedObject(typeof(ManagedSpriteTexture));
        ReflectionHelpers.SetPrivateField(spriteTexture, "_texture", texture);
        return spriteTexture;
    }
}
