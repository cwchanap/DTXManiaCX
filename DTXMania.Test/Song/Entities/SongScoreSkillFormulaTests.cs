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

        #region CalculateGameSkill

        [Fact]
        public void CalculateGameSkill_PlayingSkill100_Level850_ShouldReturn170()
        {
            // level >= 100 branch: actualLevel = 850/100 = 8.5; 100 * 8.5 * 0.2 = 170
            double result = SongScore.CalculateGameSkill(playingSkill: 100.0, level: 850, levelDec: 0);
            Assert.Equal(170.0, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_PlayingSkill60_Level78Dec33_ShouldReturn97_56()
        {
            // level < 100 branch: actualLevel = 78/10.0 + 33/100.0 = 7.8 + 0.33 = 8.13; 60 * 8.13 * 0.2 = 97.56
            double result = SongScore.CalculateGameSkill(playingSkill: 60.0, level: 78, levelDec: 33);
            Assert.Equal(97.56, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_ZeroPlayingSkill_ShouldReturnZero()
        {
            double result = SongScore.CalculateGameSkill(playingSkill: 0.0, level: 78, levelDec: 33);
            Assert.Equal(0.0, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_PlayingSkill100_Level50Dec0_ShouldReturn100()
        {
            // level < 100 branch: actualLevel = 50/10 + 0/100 = 5.0; 100 * 5.0 * 0.2 = 100
            double result = SongScore.CalculateGameSkill(playingSkill: 100.0, level: 50, levelDec: 0);
            Assert.Equal(100.0, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_Level100Boundary_TakesHighBranch()
        {
            // Branch boundary: level == 100 should use level/100 (=1.0), not level/10 (=10.0).
            // 100 * 1.0 * 0.2 = 20.0 (not 200.0)
            double result = SongScore.CalculateGameSkill(playingSkill: 100.0, level: 100, levelDec: 0);
            Assert.Equal(20.0, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_LevelDec_IgnoredWhenLevelAtLeast100()
        {
            // High branch should ignore levelDec entirely.
            double result = SongScore.CalculateGameSkill(playingSkill: 100.0, level: 850, levelDec: 99);
            Assert.Equal(170.0, result, 6);  // same as levelDec=0
        }

        #endregion
    }
}
