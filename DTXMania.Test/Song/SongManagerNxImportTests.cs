using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    [Collection("SongManager")]
    public class SongManagerNxImportTests : IDisposable
    {
        private readonly string _root;
        private readonly string _dbPath;
        private readonly SongManager _manager;

        public SongManagerNxImportTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"nximport_{Guid.NewGuid()}");
            Directory.CreateDirectory(_root);
            _dbPath = Path.Combine(_root, "songs.db");
            SongManager.ResetInstanceForTesting();
            _manager = SongManager.Instance;
        }

        public void Dispose()
        {
            SongManager.ResetInstanceForTesting();
            try { Directory.Delete(_root, true); } catch { }
        }

        private string WriteChartAndScore(string fileName, int playCount, int score, bool withScoreIni = true)
        {
            var dtxPath = Path.Combine(_root, fileName);
            File.WriteAllText(dtxPath, "; dummy chart");
            if (withScoreIni)
            {
                File.WriteAllText(dtxPath + ".score.ini",
                    "[File]\n" +
                    $"PlayCountDrums={playCount}\nClearCountDrums={playCount}\nBestRankDrums=1\n" +
                    "[HiScore.Drums]\n" +
                    $"Score={score}\nPerfect=10\nMaxCombo=10\nTotalChips=10\nUseMIDIIN=1\n" +
                    "[HiSkill.Drums]\nSkill=100.0\n" +
                    "[LastPlay.Drums]\n" +
                    $"Score={score}\nSkill=100.0\nDateTime=5/15/2026 5:54:24 PM\n");
            }
            return dtxPath;
        }

        private async Task<int> SeedChartAsync(string dtxPath, string title)
        {
            var db = _manager.DatabaseService!;
            using var ctx = db.CreateContext();
            var chart = new SongChart
            {
                Song = new SongEntity { Title = title },
                FilePath = dtxPath, HasDrumChart = true, DrumLevel = 50
            };
            ctx.SongCharts.Add(chart);
            await ctx.SaveChangesAsync();
            return chart.Id;
        }

        [Fact]
        public async Task SiblingIniPresent_ShouldImportDrumScore()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx = WriteChartAndScore("mas.dtx", playCount: 79, score: 958247);
            var chartId = await SeedChartAsync(dtx, "Against The Wind");

            var result = await _manager.ImportNxScoresAsync();

            Assert.Equal(1, result.Scanned);
            Assert.Equal(1, result.Imported);

            using var ctx = _manager.DatabaseService!.CreateContext();
            var s = ctx.SongScores.AsNoTracking().First(x => x.ChartId == chartId);
            Assert.Equal(958247, s.BestScore);
            Assert.Equal(79, s.PlayCount);
        }

        [Fact]
        public async Task ChartsWithoutScoreIni_ShouldBeSkipped()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx = WriteChartAndScore("no.dtx", 0, 0, withScoreIni: false);
            await SeedChartAsync(dtx, "Lonely");

            var result = await _manager.ImportNxScoresAsync();

            Assert.Equal(1, result.Scanned);
            Assert.Equal(0, result.Imported);
            Assert.Equal(1, result.Skipped);
        }

        [Fact]
        public async Task RepeatedRun_ShouldNotInflatePlayCount()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx = WriteChartAndScore("mas.dtx", playCount: 79, score: 958247);
            var chartId = await SeedChartAsync(dtx, "Against The Wind");

            await _manager.ImportNxScoresAsync();
            await _manager.ImportNxScoresAsync();

            using var ctx = _manager.DatabaseService!.CreateContext();
            var s = ctx.SongScores.AsNoTracking().First(x => x.ChartId == chartId);
            Assert.Equal(79, s.PlayCount);
        }

        [Fact]
        public async Task NoDatabaseService_ShouldReturnEmptyResult()
        {
            // Do not initialize the database service.
            var result = await _manager.ImportNxScoresAsync();
            Assert.Equal(0, result.Scanned);
            Assert.Equal(0, result.Imported);
            Assert.Equal(0, result.Skipped);
            Assert.Equal(0, result.Errors);
        }

        [Fact]
        public async Task ChartWithEmptyFilePath_ShouldBeSkipped()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            using var ctx = _manager.DatabaseService!.CreateContext();
            var chart = new SongChart
            {
                Song = new SongEntity { Title = "Ghost" },
                FilePath = "", HasDrumChart = true, DrumLevel = 50
            };
            ctx.SongCharts.Add(chart);
            await ctx.SaveChangesAsync();

            var result = await _manager.ImportNxScoresAsync();

            Assert.Equal(1, result.Scanned);
            Assert.Equal(0, result.Imported);
            Assert.Equal(1, result.Skipped);
        }

        [Fact]
        public async Task CanceledToken_ShouldThrowOperationCanceledException()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _manager.ImportNxScoresAsync(cancellationToken: cts.Token));
        }

        private sealed class ImmediateProgress : IProgress<NxImportProgress>
        {
            public readonly System.Collections.Generic.List<NxImportProgress> Reports = new();
            public void Report(NxImportProgress value) => Reports.Add(value);
        }

        [Fact]
        public async Task ProgressReporter_ShouldReceiveUpdates()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx = WriteChartAndScore("prog.dtx", playCount: 10, score: 50000);
            await SeedChartAsync(dtx, "Progress Song");

            var progress = new ImmediateProgress();

            await _manager.ImportNxScoresAsync(progress);

            Assert.NotEmpty(progress.Reports);
            var last = progress.Reports[^1];
            Assert.Equal(1, last.Scanned);
            Assert.Equal(1, last.Imported);
            Assert.Equal(0, last.Skipped);
            Assert.Equal("prog.dtx", last.CurrentFile);
        }

        [Fact]
        public async Task MultipleCharts_ShouldImportAllWithoutMemoryLeak()
        {
            // Verify that ChangeTracker.Clear() after each successful merge
            // does not break multi-chart imports.
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx1 = WriteChartAndScore("chart1.dtx", playCount: 10, score: 50000);
            var dtx2 = WriteChartAndScore("chart2.dtx", playCount: 20, score: 60000);
            var dtx3 = WriteChartAndScore("chart3.dtx", playCount: 30, score: 70000);
            var id1 = await SeedChartAsync(dtx1, "Song A");
            var id2 = await SeedChartAsync(dtx2, "Song B");
            var id3 = await SeedChartAsync(dtx3, "Song C");

            var result = await _manager.ImportNxScoresAsync();

            Assert.Equal(3, result.Scanned);
            Assert.Equal(3, result.Imported);
            Assert.Equal(0, result.Errors);

            using var ctx = _manager.DatabaseService!.CreateContext();
            var s1 = ctx.SongScores.AsNoTracking().First(x => x.ChartId == id1);
            var s2 = ctx.SongScores.AsNoTracking().First(x => x.ChartId == id2);
            var s3 = ctx.SongScores.AsNoTracking().First(x => x.ChartId == id3);
            Assert.Equal(50000, s1.BestScore);
            Assert.Equal(60000, s2.BestScore);
            Assert.Equal(70000, s3.BestScore);
            Assert.Equal(10, s1.PlayCount);
            Assert.Equal(20, s2.PlayCount);
            Assert.Equal(30, s3.PlayCount);
        }
    }
}
