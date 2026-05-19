using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Verifies PerformanceSummary skill fields are populated correctly when
    /// PerformanceStage builds it at completion. Done as a unit-level check by
    /// constructing the summary the same way PerformanceStage does, rather than
    /// running the full stage (which needs a graphics device).
    /// </summary>
    [Trait("Category", "Integration")]
    public class PerformanceStageSkillIntegrationTests
    {
        [Fact]
        public void BuildSummary_AllPerfect_ShouldPopulateMaxPlayingSkill()
        {
            int totalNotes = 100;
            int perfect    = 100;
            int great      = 0;
            int maxCombo   = 100;
            int level      = 78;
            int levelDec   = 33;

            double playing = SongScore.CalculatePlayingSkill(totalNotes, perfect, great, maxCombo);
            double game    = SongScore.CalculateGameSkill(playing, level, levelDec);

            var summary = new PerformanceSummary
            {
                TotalNotes    = totalNotes,
                PerfectCount  = perfect,
                MaxCombo      = maxCombo,
                PlayingSkill  = playing,
                GameSkill     = game,
                ChartLevel    = level,
                ChartLevelDec = levelDec
            };

            Assert.Equal(100.0, summary.PlayingSkill, 6);
            // actualLevel = 78/10 + 33/100 = 8.13; 100 * 8.13 * 0.2 = 162.6
            Assert.Equal(162.6, summary.GameSkill, 4);
            Assert.Equal(78, summary.ChartLevel);
            Assert.Equal(33, summary.ChartLevelDec);
        }

        [Fact]
        public void BuildSummary_NoHits_ShouldZeroSkill()
        {
            var summary = new PerformanceSummary
            {
                TotalNotes    = 100,
                PerfectCount  = 0,
                MaxCombo      = 0,
                PlayingSkill  = SongScore.CalculatePlayingSkill(100, 0, 0, 0),
                GameSkill     = SongScore.CalculateGameSkill(0.0, 78, 33),
                ChartLevel    = 78,
                ChartLevelDec = 33
            };

            Assert.Equal(0.0, summary.PlayingSkill);
            Assert.Equal(0.0, summary.GameSkill);
        }
    }
}
