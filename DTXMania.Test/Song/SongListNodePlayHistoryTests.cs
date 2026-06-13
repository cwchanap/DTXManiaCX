using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using EInstrumentPart = DTXMania.Game.Lib.Song.Entities.EInstrumentPart;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongListNodePlayHistoryTests
    {
        [Fact]
        public void PopulatePlayHistoryFromCharts_NullCharts_ShouldNotThrow()
        {
            var node = new SongListNode();
            var exception = Record.Exception(() => node.PopulatePlayHistoryFromCharts(null));
            Assert.Null(exception);
        }

        [Fact]
        public void PopulatePlayHistoryFromCharts_NullScoreEntries_ShouldSkipThem()
        {
            var node = new SongListNode();
            node.Scores[0] = null;
            node.Scores[1] = null;

            var chart = new SongChart { Id = 1 };
            chart.Scores = new List<SongScore>
            {
                new SongScore { ChartId = 1, Instrument = EInstrumentPart.DRUMS, PlayCount = 5 }
            };

            var exception = Record.Exception(() => node.PopulatePlayHistoryFromCharts(new[] { chart }));
            Assert.Null(exception);
        }

        [Fact]
        public void PopulatePlayHistoryFromCharts_NonZeroChartId_ShouldMatchByChartId()
        {
            var node = new SongListNode();
            node.Scores[0] = new SongScore
            {
                ChartId = 42,
                Instrument = EInstrumentPart.DRUMS,
                DifficultyLevel = 50
            };

            var chart = new SongChart { Id = 42 };
            chart.Scores = new List<SongScore>
            {
                new SongScore
                {
                    ChartId = 42,
                    Instrument = EInstrumentPart.DRUMS,
                    PlayCount = 10,
                    BestScore = 950000,
                    BestRank = 95
                }
            };

            node.PopulatePlayHistoryFromCharts(new[] { chart });

            Assert.Equal(10, node.Scores[0].PlayCount);
            Assert.Equal(950000, node.Scores[0].BestScore);
            Assert.Equal(95, node.Scores[0].BestRank);
        }

        [Fact]
        public void PopulatePlayHistoryFromCharts_ZeroChartId_ShouldMatchByInstrumentAndDifficulty()
        {
            var node = new SongListNode();
            node.Scores[0] = new SongScore
            {
                ChartId = 0,
                Instrument = EInstrumentPart.DRUMS,
                DifficultyLevel = 50
            };

            var chart = new SongChart { Id = 1 };
            chart.Scores = new List<SongScore>
            {
                new SongScore
                {
                    ChartId = 0,
                    Instrument = EInstrumentPart.DRUMS,
                    DifficultyLevel = 50,
                    PlayCount = 7,
                    BestScore = 800000
                }
            };

            node.PopulatePlayHistoryFromCharts(new[] { chart });

            Assert.Equal(7, node.Scores[0].PlayCount);
            Assert.Equal(800000, node.Scores[0].BestScore);
        }

        [Fact]
        public void PopulatePlayHistoryFromCharts_WhenSomeChartsHaveNullScores_ShouldSkipThem()
        {
            var node = new SongListNode();
            node.Scores[0] = new SongScore
            {
                ChartId = 42,
                Instrument = EInstrumentPart.DRUMS,
                DifficultyLevel = 50
            };
            node.Scores[1] = new SongScore
            {
                ChartId = 0,
                Instrument = EInstrumentPart.GUITAR,
                DifficultyLevel = 40
            };

            var chartWithoutScores = new SongChart { Id = 1, Scores = null };
            var chartWithMatches = new SongChart
            {
                Id = 2,
                Scores = new List<SongScore>
                {
                    new()
                    {
                        ChartId = 42,
                        Instrument = EInstrumentPart.DRUMS,
                        DifficultyLevel = 50,
                        PlayCount = 8
                    },
                    new()
                    {
                        ChartId = 0,
                        Instrument = EInstrumentPart.GUITAR,
                        DifficultyLevel = 40,
                        PlayCount = 6
                    }
                }
            };

            node.PopulatePlayHistoryFromCharts(new[] { chartWithoutScores, chartWithMatches });

            Assert.Equal(8, node.Scores[0].PlayCount);
            Assert.Equal(6, node.Scores[1].PlayCount);
        }

        [Fact]
        public void PopulatePlayHistoryFromCharts_NoPersistedMatch_ShouldNotModifyScore()
        {
            var node = new SongListNode();
            node.Scores[0] = new SongScore
            {
                ChartId = 99,
                Instrument = EInstrumentPart.GUITAR,
                DifficultyLevel = 30,
                PlayCount = 0,
                BestScore = 0
            };

            var chart = new SongChart { Id = 1 };
            chart.Scores = new List<SongScore>
            {
                new SongScore
                {
                    ChartId = 1,
                    Instrument = EInstrumentPart.DRUMS,
                    PlayCount = 10,
                    BestScore = 900000
                }
            };

            node.PopulatePlayHistoryFromCharts(new[] { chart });

            Assert.Equal(0, node.Scores[0].PlayCount);
            Assert.Equal(0, node.Scores[0].BestScore);
        }

        [Fact]
        public void PopulatePlayHistoryFromCharts_PersistedMatch_ShouldCopyAllFields()
        {
            var node = new SongListNode();
            node.Scores[0] = new SongScore
            {
                ChartId = 10,
                Instrument = EInstrumentPart.DRUMS,
                DifficultyLevel = 60
            };

            var now = DateTime.UtcNow;
            var chart = new SongChart { Id = 10 };
            chart.Scores = new List<SongScore>
            {
                new SongScore
                {
                    ChartId = 10,
                    Instrument = EInstrumentPart.DRUMS,
                    PlayCount = 15,
                    BestRank = 90,
                    BestScore = 920000,
                    BestSkillPoint = 85.5,
                    BestAchievementRate = 92.0,
                    FullCombo = true,
                    Excellent = false,
                    ClearCount = 12,
                    MaxCombo = 350,
                    HighSkill = 80.0,
                    SongSkill = 75.5,
                    LastPlayedAt = now,
                    LastScore = 890000,
                    LastSkillPoint = 70.3
                }
            };

            node.PopulatePlayHistoryFromCharts(new[] { chart });

            var s = node.Scores[0];
            Assert.Equal(15, s.PlayCount);
            Assert.Equal(90, s.BestRank);
            Assert.Equal(920000, s.BestScore);
            Assert.Equal(85.5, s.BestSkillPoint);
            Assert.Equal(92.0, s.BestAchievementRate);
            Assert.True(s.FullCombo);
            Assert.False(s.Excellent);
            Assert.Equal(12, s.ClearCount);
            Assert.Equal(350, s.MaxCombo);
            Assert.Equal(80.0, s.HighSkill);
            Assert.Equal(75.5, s.SongSkill);
            Assert.Equal(now, s.LastPlayedAt);
            Assert.Equal(890000, s.LastScore);
            Assert.Equal(70.3, s.LastSkillPoint);
        }

        [Fact]
        public void PopulatePlayHistoryFromCharts_PersistedMatch_ShouldCopyScopedHistoryLines()
        {
            var node = new SongListNode();
            node.Scores[0] = new SongScore
            {
                ChartId = 7,
                Instrument = EInstrumentPart.DRUMS,
                DifficultyLevel = 78
            };

            var persisted = new SongScore
            {
                Id = 42,
                ChartId = 7,
                Instrument = EInstrumentPart.DRUMS,
                DifficultyLevel = 78,
                PerformanceHistory = new List<PerformanceHistory>
                {
                    new() { SongScoreId = 42, DisplayOrder = 2, HistoryLine = "2.26/6/12 Cleared (A: 80.00)" },
                    new() { SongScoreId = 42, DisplayOrder = 1, HistoryLine = "3.26/6/13 Cleared (S: 90.00)" },
                    new() { SongScoreId = 99, DisplayOrder = 1, HistoryLine = "different score row" },
                    new() { SongScoreId = null, DisplayOrder = 1, HistoryLine = "legacy song-wide row" },
                }
            };
            var chart = new SongChart { Id = 7, Scores = new List<SongScore> { persisted } };

            node.PopulatePlayHistoryFromCharts(new[] { chart });

            Assert.Equal(new[]
            {
                "3.26/6/13 Cleared (S: 90.00)",
                "2.26/6/12 Cleared (A: 80.00)"
            }, node.Scores[0].PlayHistoryLines);
        }

        [Fact]
        public void PopulateScoresFromDatabase_NullDatabaseSongId_ShouldReturnImmediately()
        {
            var node = new SongListNode();
            node.DatabaseSongId = null;

            var exception = Record.Exception(() => node.PopulateScoresFromDatabase(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void PopulateScoresFromDatabase_DrumsInstrument_ShouldSetCorrectPart()
        {
            var node = new SongListNode();
            node.DatabaseSongId = 1;

            var chart = new SongChart { Id = 1, DrumLevel = 55 };
            chart.SetDifficultyLevel("DRUMS", 55);

            var song = new SongEntity
            {
                Title = "Test",
                Charts = new List<SongChart> { chart }
            };
            node.DatabaseSong = song;
            node.DifficultyLabels[0] = "BASIC";

            node.PopulateScoresFromDatabase(null!);

            Assert.NotNull(node.Scores[0]);
            Assert.Equal(EInstrumentPart.DRUMS, node.Scores[0].Instrument);
            Assert.Equal(55, node.Scores[0].DifficultyLevel);
        }

        [Fact]
        public void PopulateScoresFromDatabase_GuitarInstrument_ShouldSetCorrectPart()
        {
            var node = new SongListNode();
            node.DatabaseSongId = 1;

            var chart = new SongChart { Id = 1, GuitarLevel = 40 };
            chart.SetDifficultyLevel("GUITAR", 40);

            var song = new SongEntity
            {
                Title = "Test",
                Charts = new List<SongChart> { chart }
            };
            node.DatabaseSong = song;
            node.DifficultyLabels[0] = "ADVANCED";

            node.PopulateScoresFromDatabase(null!);

            Assert.NotNull(node.Scores[0]);
            Assert.Equal(EInstrumentPart.GUITAR, node.Scores[0].Instrument);
            Assert.Equal(40, node.Scores[0].DifficultyLevel);
        }

        [Fact]
        public void PopulateScoresFromDatabase_BassInstrument_ShouldSetCorrectPart()
        {
            var node = new SongListNode();
            node.DatabaseSongId = 1;

            var chart = new SongChart { Id = 1, BassLevel = 35 };
            chart.SetDifficultyLevel("BASS", 35);

            var song = new SongEntity
            {
                Title = "Test",
                Charts = new List<SongChart> { chart }
            };
            node.DatabaseSong = song;
            node.DifficultyLabels[0] = "EXTREME";

            node.PopulateScoresFromDatabase(null!);

            Assert.NotNull(node.Scores[0]);
            Assert.Equal(EInstrumentPart.BASS, node.Scores[0].Instrument);
            Assert.Equal(35, node.Scores[0].DifficultyLevel);
        }

        [Fact]
        public void PopulateScoresFromDatabase_ExceptionDuringProcessing_ShouldNotThrow()
        {
            var node = new SongListNode();
            node.DatabaseSongId = 1;

            var song = new SongEntity { Title = "Test" };
            node.DatabaseSong = song;

            var exception = Record.Exception(() => node.PopulateScoresFromDatabase(null!));
            Assert.Null(exception);
        }
    }
}
