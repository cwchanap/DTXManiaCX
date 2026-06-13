using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
        public void Mas_ShouldReadDrumBestStats()
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
        public void Mas_ShouldReadSkillAndLastPlay()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"))!;
            Assert.Equal(154.774601941748, data.HighSkill, 4);
            Assert.Equal(94.3747572815534, data.BestAchievementRate, 4);
            Assert.NotNull(data.LastPlayedAt);
            Assert.Equal(new DateTime(2026, 5, 15), data.LastPlayedAt!.Value.Date);
            Assert.Equal(958247, data.LastScore);
        }

        [Fact]
        public void Mas_FirstHistoryLine_ShouldParseNewestDate()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"))!;
            var newest = data.History.OrderByDescending(h => h.Date).First();
            Assert.Equal(new DateTime(2026, 5, 15), newest.Date);
            Assert.Contains("Cleared", newest.Text);
        }

        [Fact]
        public void Ext_ShouldReadNxVersionVariant()
        {
            var data = NxScoreIniParser.Parse(Fixture("ext.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(811924, data!.BestScore);
            Assert.Equal(1, data.PlayCount);
            Assert.Equal(1, data.ClearCount);
            Assert.Single(data.History);
        }

        [Fact]
        public void Full_ShouldIgnoreMojibakeTitleAndReadDrums()
        {
            var data = NxScoreIniParser.Parse(Fixture("full.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(707780, data!.BestScore);
            Assert.Equal(9, data.PlayCount);
        }

        [Fact]
        public void MissingFile_ShouldReturnNull()
        {
            Assert.Null(NxScoreIniParser.Parse(Fixture("does-not-exist.score.ini")));
        }

        [Fact]
        public void NoDrumData_ShouldReturnNull()
        {
            var path = Path.Combine(Path.GetTempPath(), $"nodrum_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=0\n[HiScore.Drums]\nScore=0\nPerfect=0\n");
            try { Assert.Null(NxScoreIniParser.Parse(path)); }
            finally { File.Delete(path); }
        }

        [Fact]
        public void NullPath_ShouldReturnNull()
        {
            Assert.Null(NxScoreIniParser.Parse(null));
        }

        [Fact]
        public void EmptyPath_ShouldReturnNull()
        {
            Assert.Null(NxScoreIniParser.Parse(""));
        }

        [Fact]
        public void MissingKeys_ShouldUseDefaults()
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
        public void InvalidDateTime_ShouldReturnNullDate()
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
        public void HistoryWithInvalidDate_ShouldUseMinValue()
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
        public void FailedHistoryWithScore_ShouldNormalizeToCleared()
        {
            var path = Path.Combine(Path.GetTempPath(), $"failedscore_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\nHistory0=9.26/5/28 Failed (B: 70.10)\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.Equal("9.26/5/28 Cleared (B: 70.10)", data!.History[0].Text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void BareFailedHistory_ShouldRemainFailed()
        {
            var path = Path.Combine(Path.GetTempPath(), $"barefailed_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\nHistory0=9.26/5/28 Failed\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.Equal("9.26/5/28 Failed", data!.History[0].Text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void NoHistory_ShouldReturnEmptyList()
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
        public void DoubleRegistration_ShouldNotThrow()
        {
            // A second parse after the first should reuse the already-registered encoding provider.
            var data1 = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"));
            Assert.NotNull(data1);

            // Exercise the idempotent encoding-provider registration path.
            var data2 = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"));
            Assert.NotNull(data2);
        }

        [Fact]
        public void JapaneseLocaleDateTime_ShouldRoundTrip()
        {
            // NX writes timestamps in the user's locale format. The most common real-world
            // format from the Japanese-locale user base is "2026/05/15 17:54:24".
            // ParseDateTime tries known NX cultures first (including ja-JP), then
            // CurrentCulture, then InvariantCulture; all handle yyyy/MM/dd correctly.
            var path = Path.Combine(Path.GetTempPath(), $"jpdate_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n" +
                "[LastPlay.Drums]\nScore=1000\nSkill=50.0\nDateTime=2026/05/15 17:54:24\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.NotNull(data!.LastPlayedAt);
                Assert.Equal(new DateTime(2026, 5, 15, 17, 54, 24), data.LastPlayedAt!.Value);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void EuropeanLocaleDateTime_ShouldParseGermanDotFormat()
        {
            // NX writes via DateTime.Now.ToString() which on German Windows produces
            // "15.05.2026 17:54:24". The parser now tries known NX locales, so this
            // should parse correctly regardless of the runner's current culture.
            var path = Path.Combine(Path.GetTempPath(), $"eudate_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n" +
                "[LastPlay.Drums]\nScore=1000\nSkill=50.0\nDateTime=15.05.2026 17:54:24\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.NotNull(data!.LastPlayedAt);
                Assert.Equal(new DateTime(2026, 5, 15, 17, 54, 24), data.LastPlayedAt!.Value);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void KoreanLocaleDateTime_ShouldParseDashFormat()
        {
            // Korean Windows produces "2026-05-15 오후 5:54:24" but the ASCII-safe
            // subset that NX actually writes for the DateTime key is typically
            // "2026-05-15 17:54:24" (sortable format). Verify it round-trips.
            var path = Path.Combine(Path.GetTempPath(), $"krdate_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n" +
                "[LastPlay.Drums]\nScore=1000\nSkill=50.0\nDateTime=2026-05-15 17:54:24\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.NotNull(data!.LastPlayedAt);
                Assert.Equal(new DateTime(2026, 5, 15, 17, 54, 24), data.LastPlayedAt!.Value);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void BritishLocaleDateTime_ShouldPreferDdMmForAmbiguousSlashDate()
        {
            // NX writes via DateTime.Now.ToString() which on British Windows produces
            // "05/06/2026 17:54:24" meaning June 5 (dd/MM). The parser must prefer
            // dd/MM cultures over InvariantCulture (MM/dd) for ambiguous slash-form
            // dates so that both parts ≤ 12 are interpreted as dd/MM, not MM/dd.
            var path = Path.Combine(Path.GetTempPath(), $"gbdate_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n" +
                "[LastPlay.Drums]\nScore=1000\nSkill=50.0\nDateTime=05/06/2026 17:54:24\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.NotNull(data!.LastPlayedAt);
                // 05/06 in dd/MM = June 5; must NOT be May 6 (MM/dd from InvariantCulture).
                Assert.Equal(new DateTime(2026, 6, 5, 17, 54, 24), data.LastPlayedAt!.Value);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void AmbiguousDate_UsCurrentCulture_ShouldNotPreemptKnownNxCultures()
        {
            // Regression: when CX runs under en-US (MM/dd) and imports an NX score.ini
            // written under en-GB (dd/MM), "05/06/2026" must resolve as June 5 (dd/MM),
            // not May 6 (MM/dd).  Known NX cultures must take priority over CurrentCulture
            // so that the source locale wins for ambiguous dates.
            var savedCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
                var path = Path.Combine(Path.GetTempPath(), $"uspreempt_{Guid.NewGuid()}.score.ini");
                File.WriteAllText(path,
                    "[File]\nPlayCountDrums=1\n" +
                    "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n" +
                    "[LastPlay.Drums]\nScore=1000\nSkill=50.0\nDateTime=05/06/2026 17:54:24\n");
                try
                {
                    var data = NxScoreIniParser.Parse(path);
                    Assert.NotNull(data);
                    Assert.NotNull(data!.LastPlayedAt);
                    // Must be June 5 (dd/MM from en-GB in known NX cultures), NOT May 6.
                    Assert.Equal(new DateTime(2026, 6, 5, 17, 54, 24), data.LastPlayedAt!.Value);
                }
                finally { File.Delete(path); }
            }
            finally { Thread.CurrentThread.CurrentCulture = savedCulture; }
        }

        [Fact]
        public void UnambiguousDdMmSlashDate_ShouldParseFromDdMmCulture()
        {
            // "15/05/2026" is unambiguous (day 15 > 12) so any culture that
            // handles slash dates parses it correctly. Verifies the known-locale
            // fallback path for dd/MM slash-form dates with day > 12.
            var path = Path.Combine(Path.GetTempPath(), $"frdate_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n" +
                "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n" +
                "[LastPlay.Drums]\nScore=1000\nSkill=50.0\nDateTime=15/05/2026 17:54:24\n");
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.NotNull(data!.LastPlayedAt);
                Assert.Equal(new DateTime(2026, 5, 15, 17, 54, 24), data.LastPlayedAt!.Value);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void UnreadableFile_ShouldThrow()
        {
            // I/O failures must propagate so the orchestrator counts them as errors,
            // not silently returning null (which would be counted as Skipped).
            // File.SetUnixFileMode is Unix-only; skip on Windows.
            if (!System.OperatingSystem.IsLinux() && !System.OperatingSystem.IsMacOS())
                return;

            var path = Path.Combine(Path.GetTempPath(), $"unreadable_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=1\n[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n");
            try
            {
                // Make the file unreadable (no permissions).
                File.SetUnixFileMode(path, System.IO.UnixFileMode.None);
                Assert.Throws<UnauthorizedAccessException>(() => NxScoreIniParser.Parse(path));
            }
            finally
            {
                // Restore permissions so cleanup can delete.
                try { File.SetUnixFileMode(path, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite); } catch { }
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void Utf8BomFile_ShouldParseAllSections()
        {
            // UTF-8 with BOM: .NET's StreamReader auto-detects the BOM and reads the file
            // as UTF-8 regardless of the Shift-JIS encoding passed to ReadAllLines.
            // All ASCII-valued fields parse correctly; this pins the actual behavior.
            var path = Path.Combine(Path.GetTempPath(), $"bom_{Guid.NewGuid()}.score.ini");
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            using (var fs = new FileStream(path, FileMode.Create))
            {
                fs.Write(bom, 0, bom.Length);
                var content = "[File]\nPlayCountDrums=5\nClearCountDrums=3\n" +
                    "[HiScore.Drums]\nScore=1000\nPerfect=10\nMaxCombo=10\nTotalChips=10\n";
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                fs.Write(bytes, 0, bytes.Length);
            }
            try
            {
                var data = NxScoreIniParser.Parse(path);
                Assert.NotNull(data);
                Assert.Equal(5, data!.PlayCount);
                Assert.Equal(3, data.ClearCount);
                Assert.Equal(1000, data.BestScore);
            }
            finally { File.Delete(path); }
        }
    }
}
