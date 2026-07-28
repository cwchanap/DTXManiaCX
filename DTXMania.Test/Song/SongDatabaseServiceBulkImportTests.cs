using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    public sealed class SongDatabaseServiceBulkImportTests : IDisposable
    {
        private sealed class CountingSongDbContext : SongDbContext
        {
            public int SaveChangesAsyncCalls { get; private set; }
            public bool HadTransactionAtSave { get; private set; }
            public int? CommandTimeoutAtSave { get; private set; }
            public Func<CancellationToken, Task>? BeforeSaveAsync { get; init; }

            public CountingSongDbContext(DbContextOptions<SongDbContext> options)
                : base(options)
            {
            }

            public override async Task<int> SaveChangesAsync(
                CancellationToken cancellationToken = default)
            {
                SaveChangesAsyncCalls++;
                HadTransactionAtSave = Database.CurrentTransaction != null;
                CommandTimeoutAtSave = Database.GetCommandTimeout();
                if (BeforeSaveAsync != null)
                    await BeforeSaveAsync(cancellationToken);
                return await base.SaveChangesAsync(cancellationToken);
            }
        }

        private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
        {
            public void Report(T value) => report(value);
        }

        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly SongDatabaseService _service;
        private CountingSongDbContext _countingContext = null!;
        private Func<CancellationToken, Task>? _beforeSaveAsync;
        private int _createdContexts;

        public SongDatabaseServiceBulkImportTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();
            _options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(_connection)
                .Options;
            using (var schema = new SongDbContext(_options))
                schema.Database.EnsureCreated();

            _service = new SongDatabaseService(_options, () =>
            {
                _createdContexts++;
                _countingContext = new CountingSongDbContext(_options)
                {
                    BeforeSaveAsync = _beforeSaveAsync
                };
                return _countingContext;
            });
        }

        public void Dispose() => _connection.Dispose();

        private static SongImportCandidate Candidate(
            string title,
            string groupKey,
            int groupOrder,
            string path,
            int drumLevel) =>
            new(
                new SongEntity
                {
                    Title = title,
                    Artist = "Fixture Artist",
                    Genre = "Fixture"
                },
                new SongChart
                {
                    FilePath = path,
                    FileSize = 123,
                    LastModified = new DateTime(
                        2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                    DrumLevel = drumLevel,
                    HasDrumChart = drumLevel > 0
                },
                SongPathIdentity.Normalize(path),
                groupKey,
                groupOrder);

        private static SongBulkImportRequest CreateRequest(
            params SongImportCandidate[] candidates)
        {
            var roots = new[] { SongPathIdentity.Normalize("/songs") };
            return new SongBulkImportRequest(
                roots,
                candidates.Select(candidate => candidate.NormalizedChartPath)
                    .ToHashSet(SongPathIdentity.CanonicalComparer),
                candidates);
        }

        private static SongImportCandidate OneCandidate() =>
            Candidate("One", "dir|one", 0, "/songs/one/chart.dtx", 50);

        private async Task ExecuteSqlAsync(string sql)
        {
            await using var context = new SongDbContext(_options);
            await context.Database.ExecuteSqlRawAsync(sql);
        }

        private async Task AssertDatabaseCountsAsync(
            int songs,
            int charts,
            int scores)
        {
            await using var context = new SongDbContext(_options);
            Assert.Equal(songs, await context.Songs.CountAsync());
            Assert.Equal(charts, await context.SongCharts.CountAsync());
            Assert.Equal(scores, await context.SongScores.CountAsync());
        }

        [Fact]
        public async Task ImportSongsAsync_FreshGroup_ShouldUseOneContextTransactionAndSave()
        {
            var request = CreateRequest(
                Candidate(
                    "Basic",
                    "set|group",
                    1,
                    "/songs/group/basic.dtx",
                    drumLevel: 20),
                Candidate(
                    "Extreme",
                    "set|group",
                    2,
                    "/songs/group/extreme.dtx",
                    drumLevel: 80));
            var milestones = new List<SongBulkImportMilestone>();
            var progress = new InlineProgress<SongBulkImportProgress>(
                update => milestones.Add(update.Milestone));

            var result = await _service.ImportSongsAsync(
                request,
                progress,
                CancellationToken.None);

            Assert.Equal(1, _createdContexts);
            Assert.Equal(1, _countingContext.SaveChangesAsyncCalls);
            Assert.True(_countingContext.HadTransactionAtSave);
            Assert.Equal(120, _countingContext.CommandTimeoutAtSave);
            Assert.Equal(2, result.Added);
            Assert.All(
                result.ChartsByPath.Values,
                chart => Assert.True(chart.Id > 0));
            Assert.Single(
                result.ChartsByPath.Values
                    .Select(chart => chart.SongId)
                    .Distinct());
            Assert.All(
                result.ChartsByPath.Values,
                chart => Assert.Single(chart.Scores));
            Assert.All(
                result.ChartsByPath.Values.SelectMany(chart => chart.Scores),
                score =>
                {
                    Assert.True(score.Id > 0);
                    Assert.Equal(100, score.PlaySpeedPercent);
                    Assert.Same(score.Chart, result.ChartsByPath.Values
                        .Single(chart => chart.Id == score.ChartId));
                });
            Assert.All(
                result.ChartsByPath.Values,
                chart => Assert.Equal("", chart.FileHash));
            Assert.Equal(
                new[]
                {
                    SongBulkImportMilestone.PreloadStarted,
                    SongBulkImportMilestone.MatchingCompleted,
                    SongBulkImportMilestone.MutationsStaged,
                    SongBulkImportMilestone.CleanupCompleted,
                    SongBulkImportMilestone.SaveStarted,
                    SongBulkImportMilestone.Committed
                },
                milestones);
            await AssertDatabaseCountsAsync(songs: 1, charts: 2, scores: 2);
        }

        [Fact]
        public async Task ImportSongsAsync_FreshChart_ShouldCopyOnlyParseOwnedFields()
        {
            var parsedSong = new SongEntity
            {
                Id = 91,
                Title = "Owned Title",
                Artist = "Owned Artist",
                Genre = "Owned Genre",
                Comment = "Owned Comment",
                IsBookmarked = true,
                CreatedAt = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2002, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                Charts = new List<SongChart> { new() }
            };
            var parsedChart = new SongChart
            {
                Id = 92,
                SongId = 91,
                FilePath = "/unowned/original.dtx",
                FileHash = "parsed-md5",
                FileSize = 456,
                LastModified = new DateTime(
                    2026, 7, 26, 23, 59, 0, DateTimeKind.Utc),
                FileFormat = "DTX",
                DifficultyLevel = 3,
                DifficultyLabel = "EXTREME",
                Bpm = 123.45,
                Duration = 98.76,
                BGMAdjust = -12,
                DrumLevel = 80,
                DrumLevelDec = 11,
                GuitarLevel = 70,
                GuitarLevelDec = 22,
                BassLevel = 60,
                BassLevelDec = 33,
                HasDrumChart = true,
                HasGuitarChart = true,
                HasBassChart = true,
                IsClassicDrums = true,
                IsClassicGuitar = true,
                IsClassicBass = true,
                DrumNoteCount = 800,
                GuitarNoteCount = 700,
                BassNoteCount = 600,
                PreviewFile = "preview.ogg",
                PreviewImage = "preview.png",
                BackgroundFile = "background.png",
                StageFile = "stage.png",
                Scores = new List<SongScore>
                {
                    new()
                    {
                        Instrument = EInstrumentPart.DRUMS,
                        PlaySpeedPercent = 75,
                        BestScore = 999
                    }
                }
            };
            var normalizedPath =
                SongPathIdentity.Normalize("/songs/owned/chart.dtx");
            var request = CreateRequest(new SongImportCandidate(
                parsedSong,
                parsedChart,
                normalizedPath,
                "dir|owned",
                0));

            var result = await _service.ImportSongsAsync(
                request,
                progress: null,
                CancellationToken.None);

            var chart = Assert.Single(result.ChartsByPath.Values);
            var song = chart.Song;
            Assert.NotEqual(parsedSong.Id, song.Id);
            Assert.Equal(parsedSong.Title, song.Title);
            Assert.Equal(parsedSong.Artist, song.Artist);
            Assert.Equal(parsedSong.Genre, song.Genre);
            Assert.Equal(parsedSong.Comment, song.Comment);
            Assert.False(song.IsBookmarked);
            Assert.Single(song.Charts);

            Assert.NotEqual(parsedChart.Id, chart.Id);
            Assert.Equal(normalizedPath, chart.FilePath);
            Assert.Equal("", chart.FileHash);
            Assert.Equal(parsedChart.FileSize, chart.FileSize);
            Assert.Equal(parsedChart.LastModified, chart.LastModified);
            Assert.Equal(parsedChart.FileFormat, chart.FileFormat);
            Assert.Equal(parsedChart.DifficultyLevel, chart.DifficultyLevel);
            Assert.Equal(parsedChart.DifficultyLabel, chart.DifficultyLabel);
            Assert.Equal(parsedChart.Bpm, chart.Bpm);
            Assert.Equal(parsedChart.Duration, chart.Duration);
            Assert.Equal(parsedChart.BGMAdjust, chart.BGMAdjust);
            Assert.Equal(parsedChart.DrumLevel, chart.DrumLevel);
            Assert.Equal(parsedChart.DrumLevelDec, chart.DrumLevelDec);
            Assert.Equal(parsedChart.GuitarLevel, chart.GuitarLevel);
            Assert.Equal(parsedChart.GuitarLevelDec, chart.GuitarLevelDec);
            Assert.Equal(parsedChart.BassLevel, chart.BassLevel);
            Assert.Equal(parsedChart.BassLevelDec, chart.BassLevelDec);
            Assert.Equal(parsedChart.HasDrumChart, chart.HasDrumChart);
            Assert.Equal(parsedChart.HasGuitarChart, chart.HasGuitarChart);
            Assert.Equal(parsedChart.HasBassChart, chart.HasBassChart);
            Assert.Equal(parsedChart.IsClassicDrums, chart.IsClassicDrums);
            Assert.Equal(parsedChart.IsClassicGuitar, chart.IsClassicGuitar);
            Assert.Equal(parsedChart.IsClassicBass, chart.IsClassicBass);
            Assert.Equal(parsedChart.DrumNoteCount, chart.DrumNoteCount);
            Assert.Equal(parsedChart.GuitarNoteCount, chart.GuitarNoteCount);
            Assert.Equal(parsedChart.BassNoteCount, chart.BassNoteCount);
            Assert.Equal(parsedChart.PreviewFile, chart.PreviewFile);
            Assert.Equal(parsedChart.PreviewImage, chart.PreviewImage);
            Assert.Equal(parsedChart.BackgroundFile, chart.BackgroundFile);
            Assert.Equal(parsedChart.StageFile, chart.StageFile);

            var scores = chart.Scores.OrderBy(score => score.Instrument).ToArray();
            Assert.Equal(
                new[]
                {
                    EInstrumentPart.DRUMS,
                    EInstrumentPart.GUITAR,
                    EInstrumentPart.BASS
                },
                scores.Select(score => score.Instrument));
            Assert.All(scores, score => Assert.Equal(100, score.PlaySpeedPercent));
            Assert.All(scores, score => Assert.Equal(0, score.BestScore));
        }

        [Fact]
        public async Task ImportSongsAsync_WhenCancelledAtSave_ShouldRollBackEverything()
        {
            using var cancellation = new CancellationTokenSource();
            _beforeSaveAsync = token =>
                Task.FromException(new OperationCanceledException(token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _service.ImportSongsAsync(
                    CreateRequest(OneCandidate()),
                    progress: null,
                    cancellation.Token));

            await AssertDatabaseCountsAsync(songs: 0, charts: 0, scores: 0);
        }

        [Fact]
        public async Task ImportSongsAsync_WhenSqliteTriggerRejectsChart_ShouldRollBackSongToo()
        {
            await ExecuteSqlAsync(
                "CREATE TRIGGER fail_chart BEFORE INSERT ON SongCharts " +
                "BEGIN SELECT RAISE(ABORT, 'forced import failure'); END;");

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                _service.ImportSongsAsync(
                    CreateRequest(OneCandidate()),
                    progress: null,
                    CancellationToken.None));

            await AssertDatabaseCountsAsync(songs: 0, charts: 0, scores: 0);
        }
    }
}
