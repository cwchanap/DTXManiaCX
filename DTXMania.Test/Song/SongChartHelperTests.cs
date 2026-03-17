using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for the SongChartHelper extension methods
    /// </summary>
    public class SongChartHelperTests
    {
        #region Null/Empty Handling

        [Fact]
        public void GetCurrentDifficultyChart_NullNode_ShouldReturnNull()
        {
            SongListNode node = null;
            var result = node.GetCurrentDifficultyChart(0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_NullDatabaseSong_ShouldReturnNull()
        {
            var node = new SongListNode { DatabaseSong = null };
            var result = node.GetCurrentDifficultyChart(0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_SongWithNullCharts_ShouldReturnDatabaseChart()
        {
            var chart = new SongChart { FilePath = "/test/song.dtx" };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = null
            };
            var node = new SongListNode
            {
                DatabaseSong = song,
                DatabaseChart = chart
            };

            var result = node.GetCurrentDifficultyChart(0);
            Assert.Equal(chart, result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_SongWithEmptyCharts_ShouldReturnDatabaseChart()
        {
            var chart = new SongChart { FilePath = "/test/song.dtx" };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = new List<SongChart>()
            };
            var node = new SongListNode
            {
                DatabaseSong = song,
                DatabaseChart = chart
            };

            var result = node.GetCurrentDifficultyChart(0);
            Assert.Equal(chart, result);
        }

        #endregion

        #region Single Chart

        [Fact]
        public void GetCurrentDifficultyChart_SingleChart_ShouldReturnThatChart()
        {
            var chart = new SongChart { FilePath = "/test/song.dtx", DrumLevel = 50, HasDrumChart = true };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = new List<SongChart> { chart }
            };
            var node = new SongListNode { DatabaseSong = song };

            var result = node.GetCurrentDifficultyChart(0);
            Assert.Equal(chart, result);
        }

        #endregion

        #region Multiple Charts - Drum Level Selection

        [Fact]
        public void GetCurrentDifficultyChart_MultipleCharts_DifficultyZero_ShouldReturnEasiestDrum()
        {
            var easyChart = new SongChart { FilePath = "/easy.dtx", DrumLevel = 30, HasDrumChart = true };
            var hardChart = new SongChart { FilePath = "/hard.dtx", DrumLevel = 90, HasDrumChart = true };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = new List<SongChart> { hardChart, easyChart } // unordered
            };
            var node = new SongListNode { DatabaseSong = song };

            var result = node.GetCurrentDifficultyChart(0);
            Assert.Equal(easyChart, result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_MultipleCharts_HighestDifficulty_ShouldReturnHardestDrum()
        {
            var easyChart = new SongChart { FilePath = "/easy.dtx", DrumLevel = 30, HasDrumChart = true };
            var hardChart = new SongChart { FilePath = "/hard.dtx", DrumLevel = 90, HasDrumChart = true };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = new List<SongChart> { easyChart, hardChart }
            };
            var node = new SongListNode { DatabaseSong = song };

            var result = node.GetCurrentDifficultyChart(10); // clamps to max
            Assert.Equal(hardChart, result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_MultipleCharts_DifficultyClamps_ShouldNotExceedBounds()
        {
            var chart1 = new SongChart { FilePath = "/1.dtx", DrumLevel = 30, HasDrumChart = true };
            var chart2 = new SongChart { FilePath = "/2.dtx", DrumLevel = 60, HasDrumChart = true };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = new List<SongChart> { chart1, chart2 }
            };
            var node = new SongListNode { DatabaseSong = song };

            // Difficulty of 999 should clamp to last available chart
            var result = node.GetCurrentDifficultyChart(999);
            Assert.Equal(chart2, result);
        }

        [Fact]
        public void GetCurrentDifficultyChart_NoDrumCharts_ShouldFallbackToFirstChart()
        {
            var guitarOnly = new SongChart { FilePath = "/guitar.dtx", GuitarLevel = 80, HasDrumChart = false, DrumLevel = 0 };
            var anotherGuitar = new SongChart { FilePath = "/guitar2.dtx", GuitarLevel = 50, HasDrumChart = false, DrumLevel = 0 };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Guitar Song",
                Charts = new List<SongChart> { guitarOnly, anotherGuitar }
            };
            var node = new SongListNode { DatabaseSong = song };

            var result = node.GetCurrentDifficultyChart(0);
            Assert.Equal(guitarOnly, result); // First chart fallback
        }

        [Fact]
        public void GetCurrentDifficultyChart_ChartWithZeroDrumLevel_ShouldNotBeIncluded()
        {
            var noDrums = new SongChart { FilePath = "/nodrum.dtx", DrumLevel = 0, HasDrumChart = true };
            var withDrums = new SongChart { FilePath = "/drum.dtx", DrumLevel = 50, HasDrumChart = true };
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = new List<SongChart> { noDrums, withDrums }
            };
            var node = new SongListNode { DatabaseSong = song };

            var result = node.GetCurrentDifficultyChart(0);
            Assert.Equal(withDrums, result);
        }

        #endregion
    }
}
