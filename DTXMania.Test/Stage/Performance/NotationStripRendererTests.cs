using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.Stage.Performance;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

/// <summary>
/// GPU-free lifetime tests for the notation strip renderer (HPA-410 Task 3). A Moq'd
/// IResourceManager/IFont verifies the acquire/release contract; actual drawing needs a
/// GraphicsDevice, so Draw is only exercised through its guards here and validated
/// visually by the retained specimen evidence.
/// </summary>
public class NotationStripRendererTests
{
    private const string BravuraFamily = "Bravura-Notation";
    private const int BravuraSize = 60; // PerformanceUILayout.NotationStrip.BravuraFontSizePt
    private const string LabelFamily = "ShareTechMono";
    private const int LabelSize = 14;   // PerformanceUILayout.NotationStrip.LabelFontSize

    private static Mock<IFont> CreateFontMock(bool supportsAllCharacters = true)
    {
        var mock = new Mock<IFont>();
        mock.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(supportsAllCharacters);
        mock.Setup(f => f.LineSpacing).Returns(322f);
        return mock;
    }

    private static NotationChart EmptyChart() => new(Array.Empty<NotationMeasure>());

    private static IReadOnlyList<MeasureLine> TerminalOnlyMeasureLines() =>
        new List<MeasureLine> { new MeasureLine { Bar = 0, TimeMs = 0 } };

    private static Mock<IResourceManager> CreateResourceManager(Mock<IFont> bravura, Mock<IFont> labels)
    {
        var mock = new Mock<IResourceManager>();
        mock.Setup(r => r.LoadFont(BravuraFamily, BravuraSize)).Returns(bravura.Object);
        mock.Setup(r => r.LoadFont(LabelFamily, LabelSize)).Returns(labels.Object);
        return mock;
    }

    [Fact]
    public void Constructor_WhenFontsLoadAndGlyphCoverageComplete_ShouldBeReady()
    {
        var bravura = CreateFontMock();
        var labels = CreateFontMock();
        var resources = CreateResourceManager(bravura, labels);

        using var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());

        Assert.True(renderer.IsReady);
        resources.Verify(r => r.LoadFont(BravuraFamily, BravuraSize), Times.Once);
        resources.Verify(r => r.LoadFont(LabelFamily, LabelSize), Times.Once);
        bravura.Verify(f => f.AddReference(), Times.Never); // LoadFont owns the initial reference
    }

    [Fact]
    public void Constructor_WhenBravuraFontMissing_ShouldReleaseAcquiredFontsAndNotBeReady()
    {
        // A missing Bravura asset silently loads the NotoSerifJP fallback, which fails coverage.
        var bravura = CreateFontMock(supportsAllCharacters: false);
        var labels = CreateFontMock();
        var resources = CreateResourceManager(bravura, labels);

        using var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());

        Assert.False(renderer.IsReady);
        bravura.Verify(f => f.RemoveReference(), Times.Once);
        labels.Verify(f => f.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Constructor_WhenBravuraCoverageIncomplete_ShouldReleaseAcquiredFontsAndNotBeReady()
    {
        var bravura = new Mock<IFont>();
        // Partial font: regular characters resolve, the SMuFL private-use notation glyphs do not.
        bravura.Setup(f => f.SupportsCharacter(It.Is<char>(c => c < 0xE000))).Returns(true);
        bravura.Setup(f => f.SupportsCharacter(It.Is<char>(c => c >= 0xE000))).Returns(false);
        bravura.Setup(f => f.LineSpacing).Returns(322f);
        var labels = CreateFontMock();
        var resources = CreateResourceManager(bravura, labels);

        using var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());

        Assert.False(renderer.IsReady);
        bravura.Verify(f => f.RemoveReference(), Times.Once);
        labels.Verify(f => f.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Constructor_WhenLabelFontLoadThrows_ShouldReleaseBravuraAndNotBeReady()
    {
        var bravura = CreateFontMock();
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.LoadFont(BravuraFamily, BravuraSize)).Returns(bravura.Object);
        resources.Setup(r => r.LoadFont(LabelFamily, LabelSize)).Throws(new Exception("Font load failed"));

        using var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());

        Assert.False(renderer.IsReady);
        bravura.Verify(f => f.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Dispose_ShouldReleaseAllFonts_AndBeIdempotent()
    {
        var bravura = CreateFontMock();
        var labels = CreateFontMock();
        var resources = CreateResourceManager(bravura, labels);
        var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());
        Assert.True(renderer.IsReady);

        renderer.Dispose();
        renderer.Dispose(); // second call must be a no-op

        bravura.Verify(f => f.RemoveReference(), Times.Once);
        labels.Verify(f => f.RemoveReference(), Times.Once);
        Assert.False(renderer.IsReady);
    }

    [Fact]
    public void Constructor_WithNullArgument_ShouldThrowAndAcquireNothing()
    {
        var resources = new Mock<IResourceManager>();
        var chart = EmptyChart();
        var lines = TerminalOnlyMeasureLines();

        Assert.Throws<ArgumentNullException>(() =>
            new NotationStripRenderer(null, chart, lines));
        Assert.Throws<ArgumentNullException>(() =>
            new NotationStripRenderer(resources.Object, null, lines));
        Assert.Throws<ArgumentNullException>(() =>
            new NotationStripRenderer(resources.Object, chart, null));

        resources.Verify(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Draw_WhenNotReady_ShouldBeANoOp()
    {
        // spriteBatch is null because a real one needs a GraphicsDevice; a not-ready renderer
        // must return before touching it.
        var bravura = CreateFontMock(supportsAllCharacters: false);
        var labels = CreateFontMock();
        var resources = CreateResourceManager(bravura, labels);
        using var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());

        var exception = Record.Exception(() => renderer.Draw(null, nowMs: 0, speedPercent: 100));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WithNullSpriteBatch_ShouldBeANoOp()
    {
        var bravura = CreateFontMock();
        var labels = CreateFontMock();
        var resources = CreateResourceManager(bravura, labels);
        using var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());

        var exception = Record.Exception(() => renderer.Draw(null, nowMs: 0, speedPercent: 100));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WithNonPositiveSpeed_ShouldBeANoOp()
    {
        var bravura = CreateFontMock();
        var labels = CreateFontMock();
        var resources = CreateResourceManager(bravura, labels);
        using var renderer = new NotationStripRenderer(resources.Object, EmptyChart(), TerminalOnlyMeasureLines());

        var exception = Record.Exception(() => renderer.Draw(null, nowMs: 0, speedPercent: 0));

        Assert.Null(exception);
    }
}
