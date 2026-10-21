using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

public class NotationStripLayoutTests
{
    private const double Speed100 = 100.0;

    /// <summary>
    /// Variable-length bars with a BPM change between bars 1 and 2; the last line is a terminal
    /// end bound, not a measure start.
    /// </summary>
    private static List<MeasureLine> CreateVariableLengthMeasureLines()
    {
        return new List<MeasureLine>
        {
            new MeasureLine { Bar = 0, TimeMs = 0 },
            new MeasureLine { Bar = 1, TimeMs = 1000 },
            new MeasureLine { Bar = 2, TimeMs = 3000 },
            new MeasureLine { Bar = 3, TimeMs = 3500 },
            new MeasureLine { Bar = 4, TimeMs = 6000 }, // terminal end bound
        };
    }

    [Fact]
    public void ToScreenX_AtHitTime_ShouldProjectOntoHitMarkerX()
    {
        var x = NotationStripLayout.ToScreenX(10000, 10000, Speed100);

        Assert.Equal(PerformanceUILayout.NotationStrip.HitMarkerX, x, 6);
    }

    [Theory]
    // 380px per second at speed 100: past times project left of the hit marker, future times right.
    [InlineData(9000, -160)]
    [InlineData(11000, 600)]
    public void ToScreenX_PastAndFutureTimes_ShouldProjectLinearly(double timeMs, double expectedX)
    {
        var x = NotationStripLayout.ToScreenX(timeMs, 10000, Speed100);

        Assert.Equal(expectedX, x, 6);
    }

    [Theory]
    [InlineData(50, 410)]
    [InlineData(100, 600)]
    [InlineData(200, 980)]
    public void ToScreenX_SpeedPercentages_ShouldScaleProjection(double speedPercent, double expectedX)
    {
        var x = NotationStripLayout.ToScreenX(1000, 0, speedPercent);

        Assert.Equal(expectedX, x, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public void ToScreenX_NonPositiveSpeed_ShouldThrowArgumentException(double speedPercent)
    {
        Assert.Throws<ArgumentException>(() => NotationStripLayout.ToScreenX(0, 0, speedPercent));
    }

    [Theory]
    // Staff lines are 2 diatonic steps (20px) apart: E4=510, G4=490, B4=470, D5=450, F5=430.
    [InlineData(0, 510)]
    [InlineData(2, 490)]
    [InlineData(4, 470)]
    [InlineData(6, 450)]
    [InlineData(8, 430)]
    public void StaffStepToY_StaffLineSteps_ShouldMatchDesignTable(int staffStep, float expectedY)
    {
        var y = NotationStripLayout.StaffStepToY(staffStep);

        Assert.Equal(expectedY, y);
    }

    [Theory]
    // Snare sits on C5 (step 5), ride on F5 (step 8), bass on F4 (step 1) per DrumStaffMapping.
    [InlineData(0x12, 460)]
    [InlineData(0x19, 430)]
    [InlineData(0x13, 500)]
    public void GetChannelLabelY_DrumChannels_ShouldUseStaffMappingSteps(int channel, float expectedY)
    {
        var y = NotationStripLayout.GetChannelLabelY(channel);

        Assert.Equal(expectedY, y);
    }

    [Theory]
    // Measures: [0,1000) [1000,3000) [3000,3500) [3500,6000). Visible range is half-open.
    [InlineData(0, 6000, 0, 3)]
    [InlineData(1000, 3500, 1, 2)]    // measure ending exactly at the left edge is excluded;
    [InlineData(3100, 3400, 2, 2)]    // a measure starting exactly at the right edge is excluded
    [InlineData(6000, 7000, 0, -1)]   // past the final end bound: nothing visible
    public void SelectVisibleMeasures_VisibleWindowStartEnd_ShouldSelectWholeMeasures(
        double leftMs, double rightMs, int expectedFirst, int expectedLast)
    {
        var window = NotationStripLayout.SelectVisibleMeasures(CreateVariableLengthMeasureLines(), leftMs, rightMs);

        Assert.Equal(expectedFirst, window.FirstIndex);
        Assert.Equal(expectedLast, window.LastIndexInclusive);
    }

    [Fact]
    public void SelectVisibleMeasures_ChartWithoutMeasures_ShouldReturnEmptyWindow()
    {
        Assert.True(NotationStripLayout.SelectVisibleMeasures(new List<MeasureLine>(), 0, 5000).LastIndexInclusive < 0);
        // A single MeasureLine is only a terminal end bound, so there are no measures either.
        var singleBound = new List<MeasureLine> { new MeasureLine { Bar = 0, TimeMs = 0 } };
        Assert.True(NotationStripLayout.SelectVisibleMeasures(singleBound, 0, 5000).LastIndexInclusive < 0);
    }

    [Fact]
    public void GetVisibleBarlineAnchors_BpmChangesAndVariableBars_ShouldAlignToCompiledMeasureStarts()
    {
        var measureLines = CreateVariableLengthMeasureLines();

        var anchors = NotationStripLayout.GetVisibleBarlineAnchors(measureLines, 0, 6000);

        // One barline per measure, each exactly at the compiled boundary time (not at uniform indices).
        var expectedTimes = new[] { 0.0, 1000.0, 3000.0, 3500.0 };
        Assert.Equal(expectedTimes, anchors.Select(a => a.TimeMs));

        // Projected x follows compiled TimeMs through the BPM change and bar-length changes.
        var projectedX = anchors.Select(a => NotationStripLayout.ToScreenX(a.TimeMs, 0, Speed100)).ToArray();
        var expectedX = new[] { 220.0, 600.0, 1360.0, 1550.0 };
        for (int i = 0; i < expectedX.Length; i++)
            Assert.Equal(expectedX[i], projectedX[i], 6);
    }

    [Fact]
    public void GetVisibleBarlineAnchors_TerminalMeasureLine_ShouldNotEmitBarline()
    {
        var measureLines = CreateVariableLengthMeasureLines();

        // Window extends past the terminal end bound.
        var anchors = NotationStripLayout.GetVisibleBarlineAnchors(measureLines, 0, 10000);

        Assert.DoesNotContain(anchors, a => a.TimeMs == measureLines[^1].TimeMs);
        Assert.Equal(4, anchors.Count);
        Assert.All(anchors, a => Assert.Equal(PerformanceUILayout.NotationStrip.StaffTopY, a.YPx));
    }
}
