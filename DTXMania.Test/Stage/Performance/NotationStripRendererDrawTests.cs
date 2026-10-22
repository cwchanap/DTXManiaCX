using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.Helpers;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

/// <summary>
/// Device-backed Draw-path tests for the notation strip renderer (HPA-410). These run the real
/// Draw pipeline — white-texture creation, SpriteBatch rect draws and the nine-argument
/// IFont.DrawString calls — on a real GraphicsDevice, so the file is Windows-only and excluded
/// from the Mac test project (same convention as Graphics/GPURenderingSnapshotTests.cs).
/// Like every other device test in this repo, the fixture is env-gated by
/// DTXMANIACX_ENABLE_GRAPHICS_TESTS (unset on CI/Mac), and when no device can be created the
/// tests silently return — the GPURenderingSnapshotTests convention — so they only execute on
/// a local dev machine with DTXMANIACX_ENABLE_GRAPHICS_TESTS=1.
/// </summary>
[Collection("GraphicsTests")]
public class NotationStripRendererDrawTests : IDisposable
{
    private readonly TestGraphicsDeviceService _graphicsService;
    private readonly SpriteBatch? _spriteBatch;

    public NotationStripRendererDrawTests()
    {
        _graphicsService = new TestGraphicsDeviceService();
        _spriteBatch = _graphicsService.GraphicsDevice is { IsDisposed: false } device
            ? new SpriteBatch(device)
            : null;
    }

    public void Dispose()
    {
        _spriteBatch?.Dispose();
        _graphicsService.Dispose();
    }

    // Same silent-skip contract as GPURenderingSnapshotTests: no device (e.g., in CI) → return.
    private bool EnsureGraphicsReady()
    {
        return _spriteBatch != null && !_spriteBatch.IsDisposed;
    }

    #region Fixtures

    private static Mock<IFont> CreateFontMock()
    {
        var mock = new Mock<IFont>();
        mock.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
        mock.Setup(f => f.LineSpacing).Returns(322f);
        mock.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(24f, 28f));
        return mock;
    }

    private static Mock<IResourceManager> CreateResourceManager(IFont bravura, IFont labels)
    {
        var mock = new Mock<IResourceManager>();
        mock.Setup(r => r.LoadFont("Bravura-Notation", 60)).Returns(bravura);
        mock.Setup(r => r.LoadFont("ShareTechMono", 14)).Returns(labels);
        return mock;
    }

    private static NotationHead Head(int staffStep) =>
        new(new DrumStaffPosition(staffStep, NoteheadShape.Normal), Array.Empty<Note>());

    private static NotationEntry NoteEntry(int startTick, int durationTicks, double timeMs, params NotationHead[] heads) =>
        new(startTick, durationTicks, timeMs, heads);

    private static NotationEntry RestEntry(int startTick, int durationTicks, double timeMs) =>
        new(startTick, durationTicks, timeMs, Array.Empty<NotationHead>());

    /// <summary>
    /// Two bars at 2000ms each. Measure 0: a quarter note (stem), a quarter rest, then a 32-tick
    /// slot triplet (quarter display values, natural stems, numeral). Measure 1: a lone eighth
    /// note (flag path). Everything is visible at nowMs=600, speed=100 (window ~[21, 3389]ms).
    /// </summary>
    private static NotationChart TwoMeasureChart()
    {
        var measure0 = new NotationMeasure(
            0, 192,
            new[]
            {
                NoteEntry(0, 48, 0.0, Head(1)),
                RestEntry(48, 48, 500.0),
                NoteEntry(96, 32, 1000.0, Head(4)),
                NoteEntry(128, 32, 1000.0 + 2000.0 / 6, Head(6)),
                NoteEntry(160, 32, 1000.0 + 2000.0 / 3, Head(5)),
            },
            new[] { new NotationTuplet(2, 3, 32) });

        var measure1 = new NotationMeasure(
            1, 192,
            new[] { NoteEntry(0, 24, 2000.0, Head(2)) },
            Array.Empty<NotationTuplet>());

        return new NotationChart(new[] { measure0, measure1 });
    }

    private static IReadOnlyList<MeasureLine> MeasureLines() => new[]
    {
        new MeasureLine { Bar = 0, TimeMs = 0 },
        new MeasureLine { Bar = 1, TimeMs = 2000 },
        new MeasureLine { Bar = 2, TimeMs = 4000 }, // terminal end bound, never a measure start
    };

    private static NotationStripRenderer CreateReadyRenderer(Mock<IFont> bravura, Mock<IFont> labels)
    {
        var renderer = new NotationStripRenderer(
            CreateResourceManager(bravura.Object, labels.Object).Object, TwoMeasureChart(), MeasureLines());
        Assert.True(renderer.IsReady);
        return renderer;
    }

    private static void BeginProductionBatch(SpriteBatch spriteBatch) =>
        // Mirror PerformanceStage.OnDraw's exact Begin call for the strip pass.
        spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);

    #endregion

    #region Nine-argument DrawString call counting (exercises Moq overload typing)

    /// <summary>No text at all: neither the nine-argument nor the default-depth overload.</summary>
    private static void VerifyNoDrawString(Mock<IFont> font)
    {
        font.Verify(f => f.DrawString(
            It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(),
            It.IsAny<float>()), Times.Never);
        VerifyNoFourArgDrawString(font);
    }

    /// <summary>The no-depth four-argument overload must never be used (0-depth bypass rule).</summary>
    private static void VerifyNoFourArgDrawString(Mock<IFont> font)
    {
        font.Verify(f => f.DrawString(
            It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()),
            Times.Never);
    }

    #endregion

    [Fact]
    public void Draw_ReadyRendererWithRealChart_ShouldDrawGlyphsAndLabelsWithContractedDepths()
    {
        if (!EnsureGraphicsReady())
            return;
        var bravura = CreateFontMock();
        var labels = CreateFontMock();
        using var renderer = CreateReadyRenderer(bravura, labels);

        var exception = Record.Exception(() =>
        {
            BeginProductionBatch(_spriteBatch!);
            renderer.Draw(_spriteBatch, nowMs: 600, speedPercent: 100);
            _spriteBatch.End();
        });

        Assert.Null(exception);

        const float glyphDepth = PerformanceUILayout.NotationStrip.DepthGlyphs;   // 0.55
        const float labelDepth = PerformanceUILayout.NotationStrip.DepthLabels;   // 0.30
        int expectedLabels = NotationStripLayout.GetKeyColumnLabels().Count;

        // Glyphs: only the nine-argument overload, always at the contracted glyph depth.
        bravura.Verify(f => f.DrawString(
            It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(),
            glyphDepth), Times.AtLeastOnce);
        bravura.Verify(f => f.DrawString(
            It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(),
            It.Is<float>(d => d != glyphDepth)), Times.Never);

        // Labels: exactly one per drum lane, always at the contracted label depth.
        labels.Verify(f => f.DrawString(
            It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(),
            labelDepth), Times.Exactly(expectedLabels));
        labels.Verify(f => f.DrawString(
            It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(),
            It.Is<float>(d => d != labelDepth)), Times.Never);

        // No text may fall back to the default-depth overload and bypass the key-column mask.
        VerifyNoFourArgDrawString(bravura);
        VerifyNoFourArgDrawString(labels);
    }

    [Fact]
    public void Draw_WithNonPositiveSpeed_ShouldNotDrawAnyText()
    {
        if (!EnsureGraphicsReady())
            return;
        var bravura = CreateFontMock();
        var labels = CreateFontMock();
        using var renderer = CreateReadyRenderer(bravura, labels);

        BeginProductionBatch(_spriteBatch!);
        renderer.Draw(_spriteBatch, nowMs: 600, speedPercent: 0);
        _spriteBatch.End();

        VerifyNoDrawString(bravura);
        VerifyNoDrawString(labels);
    }

    [Fact]
    public void Draw_WhenNotReady_ShouldNotDrawAnyText()
    {
        if (!EnsureGraphicsReady())
            return;
        // Partial Bravura font: SMuFL private-use glyphs unresolved, so coverage fails.
        var bravura = new Mock<IFont>();
        bravura.Setup(f => f.SupportsCharacter(It.Is<char>(c => c < 0xE000))).Returns(true);
        bravura.Setup(f => f.SupportsCharacter(It.Is<char>(c => c >= 0xE000))).Returns(false);
        bravura.Setup(f => f.LineSpacing).Returns(322f);
        var labels = CreateFontMock();
        using var renderer = new NotationStripRenderer(
            CreateResourceManager(bravura.Object, labels.Object).Object, TwoMeasureChart(), MeasureLines());

        Assert.False(renderer.IsReady);

        BeginProductionBatch(_spriteBatch!);
        renderer.Draw(_spriteBatch, nowMs: 600, speedPercent: 100);
        _spriteBatch.End();

        VerifyNoDrawString(bravura);
        VerifyNoDrawString(labels);
    }
}
