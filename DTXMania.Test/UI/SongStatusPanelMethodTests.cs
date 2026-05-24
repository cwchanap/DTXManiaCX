using System;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class SongStatusPanelMethodTests
    {
        [Fact]
        public void FormatDuration_UnderOneHour_ShouldReturnMSSFormat()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 90.0);
            Assert.Equal("1:30", result);
        }

        [Fact]
        public void FormatDuration_ExactlyOneHour_ShouldReturnHMMSSFormat()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 3600.0);
            Assert.Equal("1:00:00", result);
        }

        [Fact]
        public void FormatDuration_OverOneHour_ShouldReturnHMMSSFormat()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 5430.0);
            Assert.Equal("1:30:30", result);
        }

        [Fact]
        public void FormatDuration_Zero_ShouldReturnZeroMinutes()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 0.0);
            Assert.Equal("0:00", result);
        }

        [Fact]
        public void GetCurrentScore_NullSong_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", null!, 0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_NullScoresArrayEntry_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            node.Scores[0] = null;

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, 0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_NegativeDifficulty_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            node.Scores[0] = new SongScore();

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, -1);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_DifficultyOutOfRange_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            node.Scores[0] = new SongScore();

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, 5);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_ValidDifficulty_ShouldReturnScore()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            var expected = new SongScore { PlayCount = 42 };
            node.Scores[0] = expected;

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, 0);
            Assert.Same(expected, result);
            Assert.Equal(42, result!.PlayCount);
        }

        [Fact]
        public void GetInstrumentFromDifficulty_AlwaysReturnsDrums()
        {
            var panel = new SongStatusPanel();
            for (int i = 0; i < 5; i++)
            {
                var result = InvokePrivateMethod<string>(panel, "GetInstrumentFromDifficulty", i);
                Assert.Equal("DRUMS", result);
            }
        }

        [Fact]
        public void GetCurrentDifficultyChart_NullSong_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", null!, 0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_NullDatabaseSong_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode { Type = NodeType.Score, DatabaseSong = null };
            var result = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_EmptyCharts_ShouldReturnFallbackChart()
        {
            var panel = new SongStatusPanel();
            var fallback = new SongChart { FilePath = "fallback.dtx", DrumLevel = 10, HasDrumChart = true };
            var song = new SongEntity { Charts = new List<SongChart>() };
            var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song, DatabaseChart = fallback };
            var result = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 0);
            Assert.Same(fallback, result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_SingleChart_ShouldReturnThatChart()
        {
            var panel = new SongStatusPanel();
            var only = new SongChart { FilePath = "single.dtx", DrumLevel = 45, HasDrumChart = true };
            var song = new SongEntity { Charts = new List<SongChart> { only } };
            var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song };
            var result = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 3);
            Assert.Same(only, result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_NoMatchingInstrument_ShouldFallbackToFirst()
        {
            var panel = new SongStatusPanel();
            var first = new SongChart { FilePath = "g1.dtx", HasGuitarChart = true, GuitarLevel = 20 };
            var second = new SongChart { FilePath = "g2.dtx", HasGuitarChart = true, GuitarLevel = 40 };
            var song = new SongEntity { Charts = new List<SongChart> { first, second } };
            var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song };
            var result = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 1);
            Assert.Same(first, result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_MultipleDrumCharts_ShouldSortAndPickByDifficulty()
        {
            var panel = new SongStatusPanel();
            var basic = new SongChart { FilePath = "basic.dtx", HasDrumChart = true, DrumLevel = 20 };
            var advanced = new SongChart { FilePath = "advanced.dtx", HasDrumChart = true, DrumLevel = 50 };
            var extreme = new SongChart { FilePath = "extreme.dtx", HasDrumChart = true, DrumLevel = 80 };
            var song = new SongEntity { Charts = new List<SongChart> { basic, advanced, extreme } };
            var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song };

            var r0 = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 0);
            var r1 = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 1);
            var r2 = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 2);
            var r9 = InvokePrivateMethod<SongChart?>(panel, "GetCurrentDifficultyChart", node, 9);

            Assert.Same(basic, r0);
            Assert.Same(advanced, r1);
            Assert.Same(extreme, r2);
            Assert.Same(extreme, r9);
        }

        [Fact]
        public void GetAvailableChartsWithLevels_NullCurrentSong_ShouldReturnEmpty()
        {
            var panel = new SongStatusPanel();
            panel.UpdateSongInfo(null, 0);
            var result = InvokePrivateMethod<List<SongStatusPanel.ChartLevelInfo>>(panel, "GetAvailableChartsWithLevels");
            Assert.Empty(result);
        }

        [Fact]
        public void GetAvailableChartsWithLevels_MixedInstruments_ShouldReturnUniqueLevels()
        {
            var panel = new SongStatusPanel();
            var drum = new SongChart { FilePath = "d.dtx", HasDrumChart = true, DrumLevel = 30 };
            var guitar = new SongChart { FilePath = "g.dtx", HasGuitarChart = true, GuitarLevel = 50 };
            var bass = new SongChart { FilePath = "b.dtx", HasBassChart = true, BassLevel = 70 };
            var song = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseSong = new SongEntity { Charts = new List<SongChart> { drum, guitar, bass } }
            };
            panel.UpdateSongInfo(song, 0);
            var result = InvokePrivateMethod<List<SongStatusPanel.ChartLevelInfo>>(panel, "GetAvailableChartsWithLevels");
            Assert.Equal(3, result.Count);
            Assert.Contains(result, c => c.InstrumentName == "DRUMS" && c.Level == 30);
            Assert.Contains(result, c => c.InstrumentName == "GUITAR" && c.Level == 50);
            Assert.Contains(result, c => c.InstrumentName == "BASS" && c.Level == 70);
        }

        [Fact]
        public void IsChartSelected_NullChartInfo_ShouldReturnFalse()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<bool>(panel, "IsChartSelected", (SongStatusPanel.ChartLevelInfo?)null!);
            Assert.False(result);
        }

        [Fact]
        public void IsChartSelected_WhenMatchesCurrent_ShouldReturnTrue()
        {
            var panel = new SongStatusPanel();
            var chart = new SongChart { FilePath = "d.dtx", HasDrumChart = true, DrumLevel = 30 };
            var song = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseSong = new SongEntity { Charts = new List<SongChart> { chart } }
            };
            panel.UpdateSongInfo(song, 0);
            var info = new SongStatusPanel.ChartLevelInfo
            {
                InstrumentName = "DRUMS",
                Chart = chart
            };
            var result = InvokePrivateMethod<bool>(panel, "IsChartSelected", info);
            Assert.True(result);
        }

        [Fact]
        public void IsChartSelected_WhenDifferentInstrument_ShouldReturnFalse()
        {
            var panel = new SongStatusPanel();
            var chart = new SongChart { FilePath = "d.dtx", HasDrumChart = true, DrumLevel = 30 };
            var song = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseSong = new SongEntity { Charts = new List<SongChart> { chart } }
            };
            panel.UpdateSongInfo(song, 0);
            var info = new SongStatusPanel.ChartLevelInfo
            {
                InstrumentName = "GUITAR",
                Chart = chart
            };
            var result = InvokePrivateMethod<bool>(panel, "IsChartSelected", info);
            Assert.False(result);
        }

        [Fact]
        public void ReleaseManagedTexture_NullTexture_ShouldNotThrow()
        {
            var method = typeof(SongStatusPanel).GetMethod(
                "ReleaseManagedTexture",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            ITexture? texture = null;
            var args = new object[] { texture };
            var exception = Record.Exception(() => method!.Invoke(null, args));
            Assert.Null(exception);
        }

        [Fact]
        public void ReleaseManagedTexture_NonNullTexture_ShouldCallRemoveReference()
        {
            var method = typeof(SongStatusPanel).GetMethod(
                "ReleaseManagedTexture",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var mock = new Mock<ITexture>();
            ITexture? texture = mock.Object;
            var args = new object[] { texture };
            method!.Invoke(null, args);

            mock.Verify(t => t.RemoveReference(), Times.Once);
        }
    }
}
