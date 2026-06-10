using System;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class NxScoreIniParserTests
    {
        private static string Fixture(string name) =>
            Path.Combine(AppContext.BaseDirectory, "TestData", "NxScores", name);

        [Fact]
        public void Parse_Mas_ReadsDrumBestStats()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(958247, data!.BestScore);
            Assert.Equal(2293, data.BestPerfect);
            Assert.Equal(271, data.BestGreat);
            Assert.Equal(11, data.BestGood);
            Assert.Equal(0, data.BestPoor);
            Assert.Equal(0, data.BestMiss);
            Assert.Equal(2575, data.BestMaxCombo);
            Assert.Equal(2575, data.TotalChips);
            Assert.Equal(79, data.PlayCount);
            Assert.Equal(72, data.ClearCount);
            Assert.Equal(1, data.BestRankOrdinal);
            Assert.True(data.UsedMidi);
            Assert.Equal(5, data.History.Count);
            Assert.True(data.HasDrumData);
        }

        [Fact]
        public void Parse_Mas_ReadsSkillAndLastPlay()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"))!;
            Assert.Equal(154.774601941748, data.HighSkill, 4);
            Assert.Equal(94.3747572815534, data.BestAchievementRate, 4);
            Assert.NotNull(data.LastPlayedAt);
            Assert.Equal(new DateTime(2026, 5, 15), data.LastPlayedAt!.Value.Date);
            Assert.Equal(958247, data.LastScore);
        }

        [Fact]
        public void Parse_Mas_FirstHistoryLineParsesNewestDate()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"))!;
            var newest = data.History.OrderByDescending(h => h.Date).First();
            Assert.Equal(new DateTime(2026, 5, 15), newest.Date);
            Assert.Contains("Cleared", newest.Text);
        }

        [Fact]
        public void Parse_Ext_ReadsNxVersionVariant()
        {
            var data = NxScoreIniParser.Parse(Fixture("ext.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(811924, data!.BestScore);
            Assert.Equal(1, data.PlayCount);
            Assert.Equal(1, data.ClearCount);
            Assert.Single(data.History);
        }

        [Fact]
        public void Parse_Full_IgnoresMojibakeTitleAndReadsDrums()
        {
            var data = NxScoreIniParser.Parse(Fixture("full.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(707780, data!.BestScore);
            Assert.Equal(9, data.PlayCount);
        }

        [Fact]
        public void Parse_MissingFile_ReturnsNull()
        {
            Assert.Null(NxScoreIniParser.Parse(Fixture("does-not-exist.score.ini")));
        }

        [Fact]
        public void Parse_NoDrumData_ReturnsNull()
        {
            var path = Path.Combine(Path.GetTempPath(), $"nodrum_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=0\n[HiScore.Drums]\nScore=0\nPerfect=0\n");
            try { Assert.Null(NxScoreIniParser.Parse(path)); }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parse_NullPath_ReturnsNull()
        {
            Assert.Null(NxScoreIniParser.Parse(null));
        }

        [Fact]
        public void Parse_EmptyPath_ReturnsNull()
        {
            Assert.Null(NxScoreIniParser.Parse(""));
        }

        [Fact]
        public void Parse_MissingKeys_ShouldUseDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=5\n" +
                "[HiScore.Drums]\nScore=1000\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.Equal(1000, data!.BestScore);
                Assert.Equal(0, data.BestPerfect);   // default fallback
                Assert.Equal(0, data.BestGreat);     // default fallback
                Assert.Equal(0, data.BestAchievementRate); // default fallback
                Assert.Equal(99, data.BestRankOrdinal);    // default fallback
                Assert.Equal(5, data.PlayCount);
                Assert.Equal(0, data.ClearCount);    // missing key
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parse_InvalidDateTime_ReturnsNullDate()
        {
            var path = Path.Combine(Path.GetTempPath(), $"baddate_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n" +
                "[LastPlay.Drums]\nScore=1000\nSkill=50.0\nDateTime=not-a-date\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.Null(data!.LastPlayedAt);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parse_HistoryWithInvalidDate_ShouldUseMinValue()
        {
            var path = Path.Combine(Path.GetTempPath(), $"badhistory_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\nHistory0=99.no-date-here Cleared (S: 90)\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.Single(data!.History);
                Assert.Equal(DateTime.MinValue, data.History[0].Date);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parse_NoHistory_ShouldReturnEmptyList()
        {
            var path = Path.Combine(Path.GetTempPath(), $"nohist_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.Empty(data!.History);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parse_DoubleRegistration_ShouldNotThrow()
        {
            // A second parse after the first should reuse the already-registered encoding provider.
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"));
            Assert.NotNull(data);
        }
    }
}
