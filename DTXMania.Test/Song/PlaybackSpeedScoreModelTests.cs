using System;
using System.Linq;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public sealed class PlaybackSpeedScoreModelTests
    {
        [Fact]
        public void ScoreVariantKey_UsesDifficultyAndExactSpeedIdentity()
        {
            var first = new ScoreVariantKey(2, 75);
            var same = new ScoreVariantKey(2, 75);
            var differentSpeed = new ScoreVariantKey(2, 100);
            var differentDifficulty = new ScoreVariantKey(3, 75);

            Assert.Equal(first, same);
            Assert.NotEqual(first, differentSpeed);
            Assert.NotEqual(first, differentDifficulty);
        }

        [Fact]
        public void SongScore_DefaultAndClone_PreservePlaySpeedPercent()
        {
            Assert.Equal(100, new SongScore().PlaySpeedPercent);

            var original = new SongScore
            {
                ChartId = 7,
                Instrument = EInstrumentPart.DRUMS,
                PlaySpeedPercent = 75,
                BestScore = 900000,
            };

            var clone = original.Clone();

            Assert.Equal(75, clone.PlaySpeedPercent);
            Assert.Equal(original.ChartId, clone.ChartId);
            Assert.Equal(original.Instrument, clone.Instrument);
        }

        [Fact]
        public void PerformanceHistory_PitchSemitones_DefaultsToZeroAndRetainsValue()
        {
            Assert.Equal(0, new PerformanceHistory().PitchSemitones);

            var history = new PerformanceHistory { PitchSemitones = -12 };

            Assert.Equal(-12, history.PitchSemitones);
        }

        [Fact]
        public void ScoreSaveReceipt_RetainsStoredIdentity()
        {
            var runId = Guid.NewGuid();
            var savedAt = new DateTime(2026, 7, 23, 8, 0, 0, DateTimeKind.Utc);
            var receipt = new ScoreSaveReceipt
            {
                RunId = runId,
                ChartId = 42,
                Instrument = EInstrumentPart.DRUMS,
                PlaySpeedPercent = 50,
                SongScoreId = 9,
                SavedAtUtc = savedAt,
            };

            Assert.Equal(runId, receipt.RunId);
            Assert.Equal(42, receipt.ChartId);
            Assert.Equal(EInstrumentPart.DRUMS, receipt.Instrument);
            Assert.Equal(50, receipt.PlaySpeedPercent);
            Assert.Equal(9, receipt.SongScoreId);
            Assert.Equal(savedAt, receipt.SavedAtUtc);
        }

        [Fact]
        public void EfModel_ConfiguresSpeedScopedScoresAndDurableReceipts()
        {
            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;
            var context = new SongDbContext(options);
            try
            {
                var scoreType = context.Model.FindEntityType(typeof(SongScore));
                Assert.NotNull(scoreType);
                var scoreIndex = Assert.Single(
                    scoreType!.GetIndexes(),
                    index => index.Properties.Select(property => property.Name)
                        .SequenceEqual(new[]
                        {
                            nameof(SongScore.ChartId),
                            nameof(SongScore.Instrument),
                            nameof(SongScore.PlaySpeedPercent),
                        }));
                Assert.True(scoreIndex.IsUnique);
                Assert.Equal(
                    100,
                    scoreType.FindProperty(nameof(SongScore.PlaySpeedPercent))!
                        .GetDefaultValue());

                var historyType = context.Model.FindEntityType(typeof(PerformanceHistory));
                Assert.Equal(
                    0,
                    historyType!.FindProperty(nameof(PerformanceHistory.PitchSemitones))!
                        .GetDefaultValue());

                var receiptType = context.Model.FindEntityType(typeof(ScoreSaveReceipt));
                Assert.NotNull(receiptType);
                Assert.Equal(
                    nameof(ScoreSaveReceipt.RunId),
                    Assert.Single(receiptType!.FindPrimaryKey()!.Properties).Name);

                foreach (var propertyName in new[]
                {
                    nameof(ScoreSaveReceipt.RunId),
                    nameof(ScoreSaveReceipt.ChartId),
                    nameof(ScoreSaveReceipt.Instrument),
                    nameof(ScoreSaveReceipt.PlaySpeedPercent),
                    nameof(ScoreSaveReceipt.SavedAtUtc),
                })
                {
                    Assert.False(receiptType.FindProperty(propertyName)!.IsNullable);
                }
                Assert.True(
                    receiptType.FindProperty(nameof(ScoreSaveReceipt.SongScoreId))!.IsNullable);

                var scoreForeignKey = Assert.Single(
                    receiptType.GetForeignKeys(),
                    foreignKey => foreignKey.Properties.Single().Name ==
                        nameof(ScoreSaveReceipt.SongScoreId));
                Assert.Equal(DeleteBehavior.SetNull, scoreForeignKey.DeleteBehavior);
                Assert.Equal(typeof(SongScore), scoreForeignKey.PrincipalEntityType.ClrType);

                Assert.DoesNotContain(
                    receiptType.GetForeignKeys(),
                    foreignKey => foreignKey.Properties.Any(
                        property => property.Name == nameof(ScoreSaveReceipt.ChartId)));

                var receiptLookup = Assert.Single(
                    receiptType.GetIndexes(),
                    index => index.Properties.Single().Name ==
                        nameof(ScoreSaveReceipt.SongScoreId));
                Assert.False(receiptLookup.IsUnique);
                Assert.NotNull(context.ScoreSaveReceipts);
            }
            finally
            {
                context.Dispose();
            }
        }

        [Fact]
        public void FreshDatabase_AllowsDifferentSpeedsAndRejectsDuplicateSpeed()
        {
            var (connection, options) = CreateDatabase();
            try
            {
                var context = new SongDbContext(options);
                try
                {
                    var chart = SeedChart(context);
                    context.SongScores.AddRange(
                        new SongScore
                        {
                            ChartId = chart.Id,
                            Instrument = EInstrumentPart.DRUMS,
                            PlaySpeedPercent = 75,
                        },
                        new SongScore
                        {
                            ChartId = chart.Id,
                            Instrument = EInstrumentPart.DRUMS,
                            PlaySpeedPercent = 100,
                        });
                    context.SaveChanges();

                    context.SongScores.Add(new SongScore
                    {
                        ChartId = chart.Id,
                        Instrument = EInstrumentPart.DRUMS,
                        PlaySpeedPercent = 75,
                    });

                    Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                }
                finally
                {
                    context.Dispose();
                }
            }
            finally
            {
                connection.Dispose();
            }
        }

        [Fact]
        public void DeletingSongScore_SetsReceiptForeignKeyNullWithoutDeletingReceipt()
        {
            var (connection, options) = CreateDatabase();
            try
            {
                var runId = Guid.NewGuid();
                var context = new SongDbContext(options);
                try
                {
                    var chart = SeedChart(context);
                    var score = new SongScore
                    {
                        ChartId = chart.Id,
                        Instrument = EInstrumentPart.DRUMS,
                        PlaySpeedPercent = 50,
                    };
                    context.SongScores.Add(score);
                    context.SaveChanges();

                    context.ScoreSaveReceipts.Add(new ScoreSaveReceipt
                    {
                        RunId = runId,
                        ChartId = chart.Id,
                        Instrument = EInstrumentPart.DRUMS,
                        PlaySpeedPercent = 50,
                        SongScoreId = score.Id,
                        SavedAtUtc = DateTime.UtcNow,
                    });
                    context.SaveChanges();

                    context.SongScores.Remove(score);
                    context.SaveChanges();
                }
                finally
                {
                    context.Dispose();
                }

                var verify = new SongDbContext(options);
                try
                {
                    var receipt = verify.ScoreSaveReceipts
                        .AsNoTracking()
                        .Single(value => value.RunId == runId);
                    Assert.Null(receipt.SongScoreId);
                }
                finally
                {
                    verify.Dispose();
                }
            }
            finally
            {
                connection.Dispose();
            }
        }

        private static (SqliteConnection Connection, DbContextOptions<SongDbContext> Options)
            CreateDatabase()
        {
            var connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");
            connection.Open();
            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new SongDbContext(options);
            try
            {
                context.Database.EnsureCreated();
            }
            finally
            {
                context.Dispose();
            }
            return (connection, options);
        }

        private static SongChart SeedChart(SongDbContext context)
        {
            var song = new SongEntity
            {
                Title = "Speed Model",
                Artist = "Tests",
            };
            context.Songs.Add(song);
            context.SaveChanges();

            var chart = new SongChart
            {
                SongId = song.Id,
                FilePath = "/tests/speed-model.dtx",
                FileFormat = "DTX",
                HasDrumChart = true,
                DrumLevel = 50,
            };
            context.SongCharts.Add(chart);
            context.SaveChanges();
            return chart;
        }
    }
}