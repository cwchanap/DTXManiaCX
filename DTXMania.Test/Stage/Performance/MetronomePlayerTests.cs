#nullable enable

using System.Collections.Generic;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public sealed class MetronomePlayerTests
{
    [Fact]
    public void Update_BeforeNextMarkerIsDue_ShouldNotPlayClick()
    {
        var marker = new BeatMarker { TimeMs = 100.0 };
        var played = new List<BeatMarker>();
        var player = new MetronomePlayer(
            new[] { marker },
            maxLateChartMs: 25.0,
            played.Add);

        player.Update(99.0);

        Assert.Empty(played);
    }

    [Fact]
    public void Update_DueRegularAndAccentMarkers_ShouldForwardEachMarker()
    {
        var regular = new BeatMarker { TimeMs = 100.0, IsMeasureStart = false };
        var accent = new BeatMarker { TimeMs = 200.0, IsMeasureStart = true };
        var played = new List<BeatMarker>();
        var player = new MetronomePlayer(
            new[] { regular, accent },
            maxLateChartMs: 0.0,
            played.Add);

        player.Update(100.0);
        player.Update(200.0);

        Assert.Collection(
            played,
            marker =>
            {
                Assert.Same(regular, marker);
                Assert.False(marker.IsMeasureStart);
            },
            marker =>
            {
                Assert.Same(accent, marker);
                Assert.True(marker.IsMeasureStart);
            });
    }

    [Fact]
    public void Update_MarkerZeroWithinLateTolerance_ShouldPlayClick()
    {
        var marker = new BeatMarker { TimeMs = 0.0 };
        var played = new List<BeatMarker>();
        var player = new MetronomePlayer(
            new[] { marker },
            maxLateChartMs: 50.0,
            played.Add);

        player.Update(40.0);

        Assert.Same(marker, Assert.Single(played));
    }

    [Fact]
    public void Update_WithSeveralOverdueMarkers_ShouldPlayOnlyLatestConsumedMarker()
    {
        var first = new BeatMarker { TimeMs = 0.0 };
        var second = new BeatMarker { TimeMs = 100.0 };
        var latest = new BeatMarker { TimeMs = 200.0, IsMeasureStart = true };
        var played = new List<BeatMarker>();
        var player = new MetronomePlayer(
            new[] { first, second, latest },
            maxLateChartMs: 75.0,
            played.Add);

        player.Update(250.0);

        Assert.Same(latest, Assert.Single(played));
    }

    [Fact]
    public void Update_WhenLatestConsumedMarkerExceedsLateTolerance_ShouldDropIt()
    {
        var marker = new BeatMarker { TimeMs = 100.0 };
        var played = new List<BeatMarker>();
        var player = new MetronomePlayer(
            new[] { marker },
            maxLateChartMs: 25.0,
            played.Add);

        player.Update(126.0);

        Assert.Empty(played);
    }

    [Fact]
    public void Update_WhenCalledAgainForConsumedMarker_ShouldNotReplayIt()
    {
        var marker = new BeatMarker { TimeMs = 100.0 };
        var played = new List<BeatMarker>();
        var player = new MetronomePlayer(
            new[] { marker },
            maxLateChartMs: 25.0,
            played.Add);

        player.Update(100.0);
        player.Update(100.0);

        Assert.Same(marker, Assert.Single(played));
    }

    [Fact]
    public void Update_WithEmptyMarkerList_ShouldDoNothing()
    {
        var played = new List<BeatMarker>();
        var player = new MetronomePlayer(
            new List<BeatMarker>(),
            maxLateChartMs: 25.0,
            played.Add);

        player.Update(100.0);

        Assert.Empty(played);
    }
}
