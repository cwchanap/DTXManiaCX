using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song.Entities
{
    /// <summary>
    /// Tests for SongScore static skill formula helpers.
    /// Reference values verified against DTXManiaNX CScoreIni.tCalculatePlayingSkill
    /// (Score,Song/CScoreIni.cs:1641) and tCalculateGameSkillFromPlayingSkill (line 1623).
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongScoreSkillFormulaTests
    {
        #region CalculatePlayingSkill

        [Fact]
        public void CalculatePlayingSkill_AllPerfectFullCombo_ShouldReturn100()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 100, great: 0, maxCombo: 100);
            Assert.Equal(100.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_AllGreatFullCombo_ShouldReturn50()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 0, great: 100, maxCombo: 100);
            Assert.Equal(50.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_HalfPerfectHalfMiss_ShouldReturn50()
        {
            // Perfect%=50*0.85=42.5, Great%=0, Combo%=50*0.15=7.5 → 50.0
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 50, great: 0, maxCombo: 50);
            Assert.Equal(50.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_MixedPerfectGreatFullCombo_ShouldReturn60()
        {
            // 20*0.85 + 80*0.35 + 100*0.15 = 17 + 28 + 15 = 60
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 20, great: 80, maxCombo: 100);
            Assert.Equal(60.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_NoHits_ShouldReturnZero()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 0, great: 0, maxCombo: 0);
            Assert.Equal(0.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_ZeroTotalNotes_ShouldReturnZero()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 0, perfect: 0, great: 0, maxCombo: 0);
            Assert.Equal(0.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_NegativeTotalNotes_ShouldReturnZero()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: -1, perfect: 0, great: 0, maxCombo: 0);
            Assert.Equal(0.0, result, 6);
        }

        #endregion
    }
}
