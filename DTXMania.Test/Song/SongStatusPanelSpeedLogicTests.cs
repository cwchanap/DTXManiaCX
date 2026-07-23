using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public sealed class SongStatusPanelSpeedLogicTests
{
    [Fact]
    public void ResolveScore_ReturnsExactSpeedVariant()
    {
        var node = new SongListNode { Type = NodeType.Score };
        node.SetScore(0, new SongScore
        {
            PlaySpeedPercent = 100,
            BestScore = 1_000_000,
        });
        node.SetScoreVariant(0, 75, new SongScore
        {
            PlaySpeedPercent = 75,
            BestScore = 750_000,
        });

        var defaultScore = SongStatusPanel.ResolveScore(node, 0, 100);
        var slowerScore = SongStatusPanel.ResolveScore(node, 0, 75);

        Assert.Equal(1_000_000, defaultScore!.BestScore);
        Assert.Equal(750_000, slowerScore!.BestScore);
        Assert.Null(SongStatusPanel.ResolveScore(node, 0, 95));
    }

    [Fact]
    public void ResolveChartScore_DoesNotBorrowAnotherSpeed()
    {
        var chart = new SongChart
        {
            Scores =
            [
                new SongScore
                {
                    Instrument = EInstrumentPart.DRUMS,
                    PlaySpeedPercent = 100,
                    BestAchievementRate = 99.5,
                },
                new SongScore
                {
                    Instrument = EInstrumentPart.DRUMS,
                    PlaySpeedPercent = 75,
                    BestAchievementRate = 75.5,
                },
            ],
        };

        Assert.Equal(
            75.5,
            SongStatusPanel.ResolveChartScore(
                chart,
                EInstrumentPart.DRUMS,
                75)!.BestAchievementRate);
        Assert.Null(SongStatusPanel.ResolveChartScore(
            chart,
            EInstrumentPart.DRUMS,
            95));
    }

    [Fact]
    public void UpdateSongInfo_StoresExplicitSpeed()
    {
        var panel = new SongStatusPanel();

        panel.UpdateSongInfo(
            new SongListNode { Type = NodeType.Score },
            difficulty: 2,
            playSpeedPercent: 65);

        Assert.Equal(65, panel.PlaySpeedPercent);
    }
}