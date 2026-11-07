using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Result;
using Xunit;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class SongStatusPanelSkillTextTests
{
    private static SongScore PlayedScore(double highSkill) =>
        new SongScore { HighSkill = highSkill, PlayCount = 1 };

    [Fact]
    public void ResolveHighSkillText_WhenScoreIsNull_ShouldReturnPlaceholder()
    {
        Assert.Equal("---", SongStatusPanel.ResolveHighSkillText(null));
    }

    [Fact]
    public void ResolveHighSkillText_WhenScoreUnplayed_ShouldReturnPlaceholder()
    {
        var score = new SongScore { HighSkill = 88.5, PlayCount = 0 };

        Assert.Equal("---", SongStatusPanel.ResolveHighSkillText(score));
    }

    [Theory]
    [InlineData(0.345, "0.35")]
    [InlineData(1.005, "1.01")]
    [InlineData(2.675, "2.68")]
    [InlineData(0.0, "0.00")]
    [InlineData(91.25, "91.25")]
    [InlineData(-5.0, "0.00")]
    public void ResolveHighSkillText_WhenScorePlayed_ShouldUseSharedSkillFormatting(double highSkill, string expected)
    {
        Assert.Equal(expected, SongStatusPanel.ResolveHighSkillText(PlayedScore(highSkill)));
    }

    [Theory]
    [InlineData(0.345)]
    [InlineData(78.625)]
    [InlineData(91.25)]
    public void ResolveHighSkillText_WhenGivenSameSkill_ShouldMatchResultScreenText(double skill)
    {
        Assert.Equal(SkillValueFormatter.Format(skill), SongStatusPanel.ResolveHighSkillText(PlayedScore(skill)));
    }
}
