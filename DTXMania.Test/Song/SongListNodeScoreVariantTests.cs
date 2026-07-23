using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public sealed class SongListNodeScoreVariantTests
    {
        [Fact]
        public void SetScoreVariant_DifferentSpeeds_ReturnDistinctDetachedScores()
        {
            var node = new SongListNode();
            var slow = new SongScore { BestScore = 750000 };
            var normal = new SongScore { BestScore = 1000000 };

            node.SetScoreVariant(2, 75, slow);
            node.SetScoreVariant(2, 100, normal);

            var publishedSlow = node.GetScore(2, 75);
            var publishedNormal = node.GetScore(2, 100);

            Assert.NotNull(publishedSlow);
            Assert.NotNull(publishedNormal);
            Assert.NotSame(slow, publishedSlow);
            Assert.NotSame(normal, publishedNormal);
            Assert.NotSame(publishedSlow, publishedNormal);
            Assert.Equal(750000, publishedSlow!.BestScore);
            Assert.Equal(1000000, publishedNormal!.BestScore);
            Assert.Equal(75, publishedSlow.PlaySpeedPercent);
            Assert.Equal(100, publishedNormal.PlaySpeedPercent);
        }

        [Fact]
        public void GetScore_UnplayedSpeed_DoesNotBorrowDefaultSpeed()
        {
            var node = new SongListNode();
            node.SetScore(1, new SongScore { BestScore = 900000 });

            Assert.NotNull(node.GetDefaultSpeedScore(1));
            Assert.NotNull(node.GetScore(1));
            Assert.Null(node.GetScore(1, 95));
        }

        [Fact]
        public void SetScoreVariant_RefreshesOneVariantWithoutMutatingPriorSnapshot()
        {
            var node = new SongListNode();
            var slowInput = new SongScore { BestScore = 700000 };
            node.SetScoreVariant(0, 75, slowInput);
            node.SetScoreVariant(0, 100, new SongScore { BestScore = 800000 });
            var priorSnapshot = node.ScoreVariants;

            slowInput.BestScore = 1;
            node.SetScoreVariant(0, 100, new SongScore { BestScore = 900000 });

            var currentSnapshot = node.ScoreVariants;
            Assert.NotSame(priorSnapshot, currentSnapshot);
            Assert.Equal(
                800000,
                priorSnapshot[new ScoreVariantKey(0, 100)].BestScore);
            Assert.Equal(
                700000,
                currentSnapshot[new ScoreVariantKey(0, 75)].BestScore);
            Assert.Equal(
                900000,
                currentSnapshot[new ScoreVariantKey(0, 100)].BestScore);
        }

        [Fact]
        public void ScoreVariants_DictionaryCannotBeMutatedByReader()
        {
            var node = new SongListNode();
            node.SetScoreVariant(0, 100, new SongScore());
            var dictionary = Assert.IsAssignableFrom<
                IDictionary<ScoreVariantKey, SongScore>>(node.ScoreVariants);

            Assert.Throws<NotSupportedException>(() =>
                dictionary.Add(
                    new ScoreVariantKey(0, 75),
                    new SongScore()));
        }

        [Fact]
        public void CreateSongNode_PublishesDefaultSpeedCompatibilityVariants()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Variants",
            };
            var chart = new SongChart
            {
                Id = 42,
                HasDrumChart = true,
                DrumLevel = 80,
            };

            var node = SongListNode.CreateSongNode(song, chart);

            var score = node.GetDefaultSpeedScore(0);
            Assert.NotNull(score);
            Assert.Equal(42, score!.ChartId);
            Assert.Equal(100, score.PlaySpeedPercent);
            Assert.True(
                node.ScoreVariants.ContainsKey(new ScoreVariantKey(0, 100)));
        }

        [Fact]
        public void SetScoreVariant_PreservesIdentityAndDeepCopiesPitchHistory()
        {
            var history = new PerformanceHistory
            {
                Id = 13,
                SongId = 5,
                SongScoreId = 7,
                PitchSemitones = -6,
                HistoryLine = "Cleared",
                DisplayOrder = 1,
            };
            var source = new SongScore
            {
                Id = 7,
                ChartId = 3,
                PerformanceHistory = new[] { history },
            };

            var node = new SongListNode();
            node.SetScoreVariant(0, 75, source);
            history.PitchSemitones = 12;

            var published = node.GetScore(0, 75);
            Assert.NotNull(published);
            Assert.Equal(7, published!.Id);
            var publishedHistory = Assert.Single(published.PerformanceHistory);
            Assert.NotSame(history, publishedHistory);
            Assert.Equal(-6, publishedHistory.PitchSemitones);
        }

        [Theory]
        [InlineData(49)]
        [InlineData(76)]
        [InlineData(151)]
        public void SetScoreVariant_NonCanonicalSpeed_Throws(int playSpeedPercent)
        {
            var node = new SongListNode();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                node.SetScoreVariant(0, playSpeedPercent, new SongScore()));
        }

        [Fact]
        public async Task PublishScoreVariants_ConcurrentReadersSeeWholeSnapshots()
        {
            var node = new SongListNode();
            node.PublishScoreVariants(CreateGeneration(0));
            var mismatchDetected = 0;

            var writer = Task.Run(() =>
            {
                for (int generation = 1; generation <= 1000; generation++)
                    node.PublishScoreVariants(CreateGeneration(generation));
            });

            var readers = new Task[4];
            for (int readerIndex = 0; readerIndex < readers.Length; readerIndex++)
            {
                readers[readerIndex] = Task.Run(() =>
                {
                    for (int iteration = 0; iteration < 5000; iteration++)
                    {
                        var snapshot = node.ScoreVariants;
                        var slow = snapshot[new ScoreVariantKey(0, 75)];
                        var normal = snapshot[new ScoreVariantKey(0, 100)];
                        if (slow.BestScore != normal.BestScore)
                            Interlocked.Exchange(ref mismatchDetected, 1);
                    }
                });
            }

            await Task.WhenAll(readers);
            await writer;

            Assert.Equal(0, Volatile.Read(ref mismatchDetected));
        }

        private static IEnumerable<KeyValuePair<ScoreVariantKey, SongScore>>
            CreateGeneration(int generation)
        {
            return new[]
            {
                new KeyValuePair<ScoreVariantKey, SongScore>(
                    new ScoreVariantKey(0, 75),
                    new SongScore { BestScore = generation }),
                new KeyValuePair<ScoreVariantKey, SongScore>(
                    new ScoreVariantKey(0, 100),
                    new SongScore { BestScore = generation }),
            };
        }
    }
}