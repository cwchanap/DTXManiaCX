using System.Linq;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Test.Config;

public class DrumsLaneDisplayModeTests
{
    [Theory]
    [InlineData(DrumsLaneDisplayMode.AllOn, true, true, "ALL ON")]
    [InlineData(DrumsLaneDisplayMode.LaneOff, false, true, "LANE OFF")]
    [InlineData(DrumsLaneDisplayMode.LineOff, true, false, "LINE OFF")]
    [InlineData(DrumsLaneDisplayMode.AllOff, false, false, "ALL OFF")]
    public void NxMatrixAndLabels_ShouldRoundTrip(
        DrumsLaneDisplayMode mode,
        bool showsLaneBackground,
        bool showsMeasureLines,
        string label)
    {
        Assert.Equal(showsLaneBackground, mode.ShowsLaneBackground());
        Assert.Equal(showsMeasureLines, mode.ShowsMeasureLines());

        var parsedMode = DrumsLaneDisplayModeExtensions.FromLabel(label);
        Assert.Equal(mode, parsedMode);
        Assert.Equal(label, DrumsLaneDisplayModeExtensions.ToLabel(parsedMode));
    }

    [Fact]
    public void Options_ShouldPreserveNxOrderAndExplicitValues()
    {
        Assert.Equal(0, (int)DrumsLaneDisplayMode.AllOn);
        Assert.Equal(1, (int)DrumsLaneDisplayMode.LaneOff);
        Assert.Equal(2, (int)DrumsLaneDisplayMode.LineOff);
        Assert.Equal(3, (int)DrumsLaneDisplayMode.AllOff);

        Assert.Equal(
            new[]
            {
                DrumsLaneDisplayMode.AllOn,
                DrumsLaneDisplayMode.LaneOff,
                DrumsLaneDisplayMode.LineOff,
                DrumsLaneDisplayMode.AllOff,
            },
            DrumsLaneDisplayModeExtensions.Options.Select(option => option.Mode));
        Assert.Equal(
            new[] { "ALL ON", "LANE OFF", "LINE OFF", "ALL OFF" },
            DrumsLaneDisplayModeExtensions.Labels);
    }
}
