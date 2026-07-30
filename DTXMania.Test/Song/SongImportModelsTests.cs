using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongImportModelsTests
    {
        [Fact]
        public void PendingSongNode_ShouldExposePositionalProperties()
        {
            var placeholder = new SongListNode { Title = "Group" };
            var ordered = new List<string> { "/a.dtx", "/b.dtx" };

            var node = new PendingSongNode("group-key", placeholder, ordered);

            Assert.Equal("group-key", node.GroupKey);
            Assert.Same(placeholder, node.Placeholder);
            Assert.Same(ordered, node.OrderedChartPaths);
        }

        [Fact]
        public void SongEnumerationError_ShouldExposePositionalProperties()
        {
            var error = new SongEnumerationError("/missing.dtx", "not found", IsRootFailure: true);

            Assert.Equal("/missing.dtx", error.Path);
            Assert.Equal("not found", error.Message);
            Assert.True(error.IsRootFailure);
        }

        [Fact]
        public void SongBulkImportProgress_ShouldExposePositionalProperties()
        {
            var progress = new SongBulkImportProgress(SongBulkImportMilestone.MatchingCompleted, 5, 10);

            Assert.Equal(SongBulkImportMilestone.MatchingCompleted, progress.Milestone);
            Assert.Equal(5, progress.Processed);
            Assert.Equal(10, progress.Total);
        }

        [Fact]
        public void SongImportCandidate_ShouldExposePositionalProperties()
        {
            var song = new SongEntity { Title = "T", Artist = "A" };
            var chart = new SongChart { Song = song, FilePath = "/c.dtx" };
            song.Charts.Add(chart);

            var candidate = new SongImportCandidate(song, chart, "/c.dtx", "group", 3);

            Assert.Same(song, candidate.ParsedSong);
            Assert.Same(chart, candidate.ParsedChart);
            Assert.Equal("/c.dtx", candidate.NormalizedChartPath);
            Assert.Equal("group", candidate.GroupKey);
            Assert.Equal(3, candidate.GroupOrder);
        }

        [Fact]
        public void SongEnumerationBatch_ShouldRoundTripRequiredProperties()
        {
            var batch = new SongEnumerationBatch
            {
                ActiveRoots = new[] { "/songs" },
                DiscoveredChartPaths = new HashSet<string> { "/songs/a.dtx" },
                Candidates = new List<SongImportCandidate>(),
                RootNodes = new List<SongListNode>(),
                PendingSongs = new List<PendingSongNode>(),
                Errors = new List<SongEnumerationError>(),
                DiscoveryAndParsingDuration = TimeSpan.FromMilliseconds(15),
                IsComplete = true
            };

            Assert.Single(batch.ActiveRoots);
            Assert.Single(batch.DiscoveredChartPaths);
            Assert.Empty(batch.Candidates);
            Assert.Empty(batch.RootNodes);
            Assert.Empty(batch.PendingSongs);
            Assert.Empty(batch.Errors);
            Assert.Equal(TimeSpan.FromMilliseconds(15), batch.DiscoveryAndParsingDuration);
            Assert.True(batch.IsComplete);
        }

        [Fact]
        public void SongBulkImportResult_ShouldExposePositionalProperties()
        {
            var charts = new Dictionary<string, SongChart>();
            var result = new SongBulkImportResult(
                charts,
                Added: 1,
                Updated: 2,
                Preserved: 3,
                Skipped: 4,
                Conflicts: 5,
                StaleCharts: 6,
                StaleSongs: 7,
                TimeSpan.FromMilliseconds(8),
                TimeSpan.FromMilliseconds(9));

            Assert.Same(charts, result.ChartsByPath);
            Assert.Equal(1, result.Added);
            Assert.Equal(2, result.Updated);
            Assert.Equal(3, result.Preserved);
            Assert.Equal(4, result.Skipped);
            Assert.Equal(5, result.Conflicts);
            Assert.Equal(6, result.StaleCharts);
            Assert.Equal(7, result.StaleSongs);
            Assert.Equal(TimeSpan.FromMilliseconds(8), result.PersistenceDuration);
            Assert.Equal(TimeSpan.FromMilliseconds(9), result.CleanupDuration);
        }

        [Fact]
        public void SongEnumerationResult_ShouldExposePositionalProperties()
        {
            var batch = new SongEnumerationBatch
            {
                ActiveRoots = Array.Empty<string>(),
                DiscoveredChartPaths = new HashSet<string>(),
                Candidates = new List<SongImportCandidate>(),
                RootNodes = new List<SongListNode>(),
                PendingSongs = new List<PendingSongNode>(),
                Errors = new List<SongEnumerationError>(),
                DiscoveryAndParsingDuration = TimeSpan.Zero,
                IsComplete = false
            };
            var import = new SongBulkImportResult(
                new Dictionary<string, SongChart>(),
                0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero);

            var result = new SongEnumerationResult(batch, import, TimeSpan.FromMilliseconds(3));

            Assert.Same(batch, result.Batch);
            Assert.Same(import, result.Import);
            Assert.Equal(TimeSpan.FromMilliseconds(3), result.HierarchyDuration);
        }
    }
}
