using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using System.Collections.Generic;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public sealed class SongBarRendererSpeedLogicTests
{
    [Fact]
    public void ResolveClearStatus_UsesExactSpeedVariant()
    {
        var node = new SongListNode { Type = NodeType.Score };
        node.SetScore(0, new SongScore
        {
            PlaySpeedPercent = 100,
            PlayCount = 1,
            BestRank = 80,
        });
        node.SetScoreVariant(0, 75, new SongScore
        {
            PlaySpeedPercent = 75,
            PlayCount = 1,
            BestRank = 0,
        });

        Assert.Equal(
            ClearStatus.Clear,
            SongBarRenderer.ResolveClearStatus(node, 0, 100));
        Assert.Equal(
            ClearStatus.Failed,
            SongBarRenderer.ResolveClearStatus(node, 0, 75));
    }

    [Fact]
    public void ResolveClearStatus_UnplayedSpeedDoesNotBorrowDefault()
    {
        var node = new SongListNode { Type = NodeType.Score };
        node.SetScore(0, new SongScore
        {
            PlaySpeedPercent = 100,
            PlayCount = 1,
            FullCombo = true,
        });

        Assert.Equal(
            ClearStatus.NotPlayed,
            SongBarRenderer.ResolveClearStatus(node, 0, 95));
    }

    [Fact]
    public void SongListDisplay_DifficultyNavigationRemainsMetadataBased()
    {
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore { DifficultyLevel = 30 };
        node.Scores[2] = new SongScore { DifficultyLevel = 80 };
        var display = new SongListDisplay
        {
            CurrentList = new List<SongListNode> { node },
            CurrentDifficulty = 0,
            PlaySpeedPercent = 75,
        };

        display.CycleDifficulty();

        Assert.Equal(2, display.CurrentDifficulty);
    }
}