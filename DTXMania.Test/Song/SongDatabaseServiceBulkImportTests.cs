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

        private static SongBulkImportRequest CreateEmptyRequest(
            IReadOnlyList<string> activeRoots,
            IReadOnlyList<string> discoveredPaths) =>
            new(
                activeRoots.Select(SongPathIdentity.Normalize).ToArray(),
                discoveredPaths.Select(SongPathIdentity.Normalize)
                    .ToHashSet(SongPathIdentity.CanonicalComparer),
                Array.Empty<SongImportCandidate>());

        private static SongImportCandidate OneCandidate() =>
            Candidate("One", "dir|one", 0, "/songs/one/chart.dtx", 50);

        private sealed record SeededSong(SongEntity Song, SongChart Chart);

        private sealed record ScoreSnapshot(
            int Id,
            int ChartId,
            EInstrumentPart Instrument,
            int PlaySpeedPercent,
            string DifficultyLabel,
            int BestScore,
            int BestRank,
            double BestSkillPoint,
            double BestAchievementRate,
            bool FullCombo,
            bool Excellent,
            int PlayCount,
            int ClearCount,
            int MaxCombo,
            int NxImportedPlayCount,
            int NxImportedClearCount,
            double HighSkill,
            double SongSkill,
            int TotalNotes,
            int BestPerfect,
            int BestGreat,
            int BestGood,
            int BestPoor,
            int BestMiss,
            string ProgressBar,
            DateTime? LastPlayedAt,
            int LastScore,
            double LastSkillPoint,
            bool UsedDrumPad,
            bool UsedKeyboard,
            bool UsedMidi,
            bool UsedJoypad,
            bool UsedMouse);

        private async Task<SeededSong> SeedPlayedSongAsync()
        {
            await using var context = new SongDbContext(_options);
            var song = new SongEntity
            {
                Title = "Original",
                Artist = "Fixture Artist",
                Genre = "Original Genre",
                IsBookmarked = true,
                CreatedAt = new DateTime(
                    2020, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(
                    2020, 1, 3, 0, 0, 0, DateTimeKind.Utc)
            };
            var chart = new SongChart
            {
                Song = song,
                FilePath = SongPathIdentity.Normalize(
                    "/songs/played/chart.dtx"),
                FileHash = "legacy-md5",
                DrumLevel = 50,
                HasDrumChart = true
            };
            var defaultScore = new SongScore
            {
                Chart = chart,
                Instrument = EInstrumentPart.DRUMS,
                PlaySpeedPercent = 100,
                DifficultyLabel = "played-default",
                BestScore = 900_000,
                LastScore = 800_000,
                BestRank = 90,
                BestSkillPoint = 88.5,
                BestAchievementRate = 97.25,
                PlayCount = 4,
                ClearCount = 3,
                MaxCombo = 456,
                FullCombo = true,
                Excellent = true,
                LastPlayedAt = new DateTime(
                    2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                LastSkillPoint = 77.5,
                HighSkill = 66.5,
                SongSkill = 55.5,
                TotalNotes = 500,
                BestPerfect = 450,
                BestGreat = 30,
                BestGood = 10,
                BestPoor = 5,
                BestMiss = 5,
                ProgressBar = "played",
                UsedDrumPad = true,
                UsedKeyboard = true,
                UsedMidi = true,
                UsedJoypad = true,
                UsedMouse = true,
                NxImportedPlayCount = 2,
                NxImportedClearCount = 1
            };
            var fastScore = new SongScore
            {
                Chart = chart,
                Instrument = EInstrumentPart.DRUMS,
                PlaySpeedPercent = 150,
                DifficultyLabel = "played-fast",
                BestScore = 700_000,
                PlayCount = 1
            };
            defaultScore.PerformanceHistory.Add(new PerformanceHistory
            {
                Song = song,
                SongScore = defaultScore,
                PerformedAt = new DateTime(
                    2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                DisplayOrder = 1,
                HistoryLine = "default"
            });
            fastScore.PerformanceHistory.Add(new PerformanceHistory
            {
                Song = song,
                SongScore = fastScore,
                PerformedAt = new DateTime(
                    2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
                DisplayOrder = 1,
                HistoryLine = "fast"
            });
            chart.Scores.Add(defaultScore);
            chart.Scores.Add(fastScore);
            song.Charts.Add(chart);
            context.Songs.Add(song);
            await context.SaveChangesAsync();
            return new SeededSong(song, chart);
        }

        private async Task<SongChart> SeedChartAsync(
            string path,
            string songTitle = "Seed")
        {
            await using var context = new SongDbContext(_options);
            var song = new SongEntity
            {
                Title = songTitle,
                Artist = "Fixture Artist"
            };
            var chart = new SongChart
            {
                Song = song,
                FilePath = SongPathIdentity.Normalize(path),
                DrumLevel = 50,
                HasDrumChart = true
            };
            song.Charts.Add(chart);
            context.Songs.Add(song);
            await context.SaveChangesAsync();
            return chart;
        }

        private async Task<SongChart> SeedChartWithStoredPathAsync(
            string storedPath,
            string songTitle = "Seed")
        {
            await using var context = new SongDbContext(_options);
            var song = new SongEntity
            {
                Title = songTitle,
                Artist = "Fixture Artist"
            };
            var chart = new SongChart
            {
                Song = song,
                FilePath = storedPath,
                DrumLevel = 50,
                HasDrumChart = true
            };
            song.Charts.Add(chart);
            context.Songs.Add(song);
            await context.SaveChangesAsync();
            return chart;
        }

        private async Task<bool> ChartExistsAsync(int chartId)
        {
            await using var context = new SongDbContext(_options);
            return await context.SongCharts.AnyAsync(chart => chart.Id == chartId);
        }

        private async Task<bool> SongExistsAsync(int songId)
        {
            await using var context = new SongDbContext(_options);
            return await context.Songs.AnyAsync(song => song.Id == songId);
        }

        private async Task<ScoreSnapshot[]> LoadScoreSnapshotsAsync(int chartId)
        {
            await using var context = new SongDbContext(_options);
            return await context.SongScores
                .AsNoTracking()
                .Where(score => score.ChartId == chartId)
                .OrderBy(score => score.Instrument)
                .ThenBy(score => score.PlaySpeedPercent)
                .Select(score => new ScoreSnapshot(
                    score.Id,
                    score.ChartId,
                    score.Instrument,
                    score.PlaySpeedPercent,
                    score.DifficultyLabel,
                    score.BestScore,
                    score.BestRank,
                    score.BestSkillPoint,
                    score.BestAchievementRate,
                    score.FullCombo,
                    score.Excellent,
                    score.PlayCount,
                    score.ClearCount,
                    score.MaxCombo,
                    score.NxImportedPlayCount,
                    score.NxImportedClearCount,
                    score.HighSkill,
                    score.SongSkill,
                    score.TotalNotes,
                    score.BestPerfect,
                    score.BestGreat,
                    score.BestGood,
                    score.BestPoor,
                    score.BestMiss,
                    score.ProgressBar,
                    score.LastPlayedAt,
                    score.LastScore,
                    score.LastSkillPoint,
                    score.UsedDrumPad,
                    score.UsedKeyboard,
                    score.UsedMidi,
                    score.UsedJoypad,
                    score.UsedMouse))
                .ToArrayAsync();
        }

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
        public async Task ImportSongsAsync_Rescan_ShouldUpdateMetadataAndPreserveUserState()
        {
            var seeded = await SeedPlayedSongAsync();
            var originalCreatedAt = seeded.Song.CreatedAt;
            var originalUpdatedAt = seeded.Song.UpdatedAt;
            var candidate = Candidate(
                title: "Renamed",
                groupKey: "dir|same",
                groupOrder: 0,
                path: seeded.Chart.FilePath,
                drumLevel: 75);

            var result = await _service.ImportSongsAsync(
                CreateRequest(candidate),
                progress: null,
                CancellationToken.None);

            var normalizedPath = SongPathIdentity.Normalize(
                seeded.Chart.FilePath);
            var chart = result.ChartsByPath[normalizedPath];
            Assert.Equal(seeded.Song.Id, chart.SongId);
            Assert.Equal(seeded.Chart.Id, chart.Id);
            Assert.True(chart.Song.IsBookmarked);
            Assert.Equal("Renamed", chart.Song.Title);
            Assert.Equal(75, chart.DrumLevel);
            Assert.Equal(seeded.Chart.FileHash, chart.FileHash);
            Assert.Equal(originalCreatedAt, chart.Song.CreatedAt);
            Assert.True(chart.Song.UpdatedAt > originalUpdatedAt);
            Assert.Equal(
                new[] { 100, 150 },
                chart.Scores
                    .Select(score => score.PlaySpeedPercent)
                    .OrderBy(speed => speed));
            var defaultScore = chart.Scores.Single(
                score => score.PlaySpeedPercent == 100);
            Assert.Equal(900_000, defaultScore.BestScore);
            Assert.Equal(800_000, defaultScore.LastScore);
            Assert.Equal(90, defaultScore.BestRank);
            Assert.True(defaultScore.FullCombo);
            Assert.Equal(4, defaultScore.PlayCount);
            Assert.Equal(3, defaultScore.ClearCount);
            Assert.Equal(2, defaultScore.NxImportedPlayCount);
            Assert.Equal(1, defaultScore.NxImportedClearCount);
            Assert.All(
                chart.Scores,
                score => Assert.NotEmpty(score.PerformanceHistory));
            Assert.Equal(1, result.Updated);
            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.Preserved);
        }

        [Fact]
        public async Task ImportSongsAsync_GainedInstrument_ShouldPreserveScoresAndAddOneDefaultKey()
        {
            var seeded = await SeedPlayedSongAsync();
            var before = await LoadScoreSnapshotsAsync(seeded.Chart.Id);
            var candidate = Candidate(
                "Original",
                "dir|played",
                0,
                seeded.Chart.FilePath,
                50);
            candidate.ParsedSong.Genre = "Original Genre";
            candidate.ParsedChart.HasGuitarChart = true;
            candidate.ParsedChart.GuitarLevel = 70;

            await _service.ImportSongsAsync(
                CreateRequest(candidate), progress: null, CancellationToken.None);
            await _service.ImportSongsAsync(
                CreateRequest(candidate), progress: null, CancellationToken.None);

            var after = await LoadScoreSnapshotsAsync(seeded.Chart.Id);
            Assert.Equal(before, after
                .Where(score => score.Instrument == EInstrumentPart.DRUMS)
                .ToArray());
            var guitar = Assert.Single(
                after,
                score => score.Instrument == EInstrumentPart.GUITAR);
            Assert.Equal(100, guitar.PlaySpeedPercent);
            Assert.Equal(0, guitar.BestScore);
        }

        [Fact]
        public async Task ImportSongsAsync_UnchangedRescan_ShouldCountPreserved()
        {
            var candidate = Candidate(
                "Same", "dir|same", 0, "/songs/same/chart.dtx", 50);
            await _service.ImportSongsAsync(
                CreateRequest(candidate), progress: null, CancellationToken.None);

            var result = await _service.ImportSongsAsync(
                CreateRequest(candidate), progress: null, CancellationToken.None);

            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.Updated);
            Assert.Equal(1, result.Preserved);
            Assert.Equal(0, result.Skipped);
            Assert.Single(result.ChartsByPath.Values.Single().Scores);
        }

        [Fact]
        public async Task ImportSongsAsync_SameMetadataInDifferentDirectories_ShouldCreateTwoSongs()
        {
            var request = CreateRequest(
                Candidate(
                    "Same", "dir|a", 0, "/songs/a/chart.dtx", 40),
                Candidate(
                    "Same", "dir|b", 0, "/songs/b/chart.dtx", 40));

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.Equal(
                2,
                result.ChartsByPath.Values
                    .Select(chart => chart.SongId)
                    .Distinct()
                    .Count());
        }

        [Fact]
        public async Task ImportSongsAsync_SetDefinitionGroup_ShouldCreateOneSong()
        {
            var request = CreateRequest(
                Candidate(
                    "Basic",
                    "set|/songs/group/set.def",
                    1,
                    "/songs/group/basic.dtx",
                    20),
                Candidate(
                    "Extreme",
                    "set|/songs/group/set.def",
                    2,
                    "/songs/group/extreme.dtx",
                    80));

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.Single(result.ChartsByPath.Values
                .Select(chart => chart.SongId)
                .Distinct());
        }

        [Fact]
        public async Task ImportSongsAsync_DiscoveryDiff_ShouldDeleteOnlyInsideActiveRoots()
        {
            var staleInside = await SeedChartAsync(
                "/songs/active/stale.dtx");
            var outside = await SeedChartAsync("/songs/other/keep.dtx");
            var request = CreateEmptyRequest(
                activeRoots: new[] { "/songs/active" },
                discoveredPaths: Array.Empty<string>());

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.Equal(1, result.StaleCharts);
            Assert.False(await ChartExistsAsync(staleInside.Id));
            Assert.True(await ChartExistsAsync(outside.Id));
        }

        [Fact]
        public async Task ImportSongsAsync_RemovedConfiguredRoot_ShouldRetainRowsOutsideActiveRoots()
        {
            var removedRootChart = await SeedChartAsync(
                "/songs/removed/keep.dtx");
            var request = CreateEmptyRequest(
                activeRoots: new[] { "/songs/current" },
                discoveredPaths: Array.Empty<string>());

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.Equal(0, result.StaleCharts);
            Assert.Equal(0, result.StaleSongs);
            Assert.True(await ChartExistsAsync(removedRootChart.Id));
            Assert.True(await SongExistsAsync(removedRootChart.SongId));
        }

        [Fact]
        public async Task ImportSongsAsync_AmbiguousLegacyGroup_ShouldKeepAssociationsAndReportConflict()
        {
            var first = await SeedChartAsync(
                "/songs/group/basic.dtx", songTitle: "A");
            var second = await SeedChartAsync(
                "/songs/group/extreme.dtx", songTitle: "B");
            var request = CreateRequest(
                Candidate(
                    "Unified",
                    "set|group",
                    1,
                    first.FilePath,
                    20),
                Candidate(
                    "Unified",
                    "set|group",
                    2,
                    second.FilePath,
                    80),
                Candidate(
                    "Unified",
                    "set|group",
                    3,
                    "/songs/group/master.dtx",
                    95));

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.Equal(
                first.SongId,
                result.ChartsByPath[
                    SongPathIdentity.Normalize(first.FilePath)].SongId);
            Assert.Equal(
                second.SongId,
                result.ChartsByPath[
                    SongPathIdentity.Normalize(second.FilePath)].SongId);
            Assert.DoesNotContain(
                result.ChartsByPath[
                    SongPathIdentity.Normalize(
                        "/songs/group/master.dtx")].SongId,
                new[] { first.SongId, second.SongId });
            Assert.Equal(1, result.Conflicts);
        }

        [Fact]
        public async Task ImportSongsAsync_LegacyNonCanonicalPath_ShouldMatchAndMigrateInPlace()
        {
            var chart = await SeedChartWithStoredPathAsync(
                "/songs/active/nested/../chart.dtx");
            var originalChartId = chart.Id;
            var candidate = Candidate(
                "Migrated",
                "dir|active",
                0,
                "/songs/active/chart.dtx",
                60);

            var result = await _service.ImportSongsAsync(
                CreateRequest(candidate), progress: null, CancellationToken.None);

            var migrated = result.ChartsByPath[candidate.NormalizedChartPath];
            Assert.Equal(originalChartId, migrated.Id);
            Assert.Equal(candidate.NormalizedChartPath, migrated.FilePath);
            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.StaleCharts);
        }

        [Fact]
        public async Task ImportSongsAsync_AmbiguousLegacyAliases_ShouldRetainEveryRow()
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                return;

            var first = await SeedChartWithStoredPathAsync(
                "/songs/active/Case/chart.dtx", "First");
            var second = await SeedChartWithStoredPathAsync(
                "/songs/active/case/chart.dtx", "Second");
            var candidate = Candidate(
                "Discovered",
                "dir|active",
                0,
                "/songs/active/CASE/chart.dtx",
                60);

            var result = await _service.ImportSongsAsync(
                CreateRequest(candidate), progress: null, CancellationToken.None);

            Assert.True(await ChartExistsAsync(first.Id));
            Assert.True(await ChartExistsAsync(second.Id));
            Assert.Equal(1, result.Conflicts);
            Assert.Equal(1, result.Skipped);
            Assert.False(
                result.ChartsByPath.ContainsKey(candidate.NormalizedChartPath));
        }

        [Fact]
        public async Task ImportSongsAsync_CaseDistinctExactMatches_ShouldResolveBeforeLegacyAliases()
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                return;

            var first = await SeedChartWithStoredPathAsync(
                "/songs/active/Case/chart.dtx", "First");
            var second = await SeedChartWithStoredPathAsync(
                "/songs/active/case/chart.dtx", "Second");
            var firstCandidate = Candidate(
                "First",
                "dir|first",
                0,
                first.FilePath,
                50);
            var secondCandidate = Candidate(
                "Second",
                "dir|second",
                0,
                second.FilePath,
                50);

            var result = await _service.ImportSongsAsync(
                CreateRequest(firstCandidate, secondCandidate),
                progress: null,
                CancellationToken.None);

            Assert.Equal(
                first.Id,
                result.ChartsByPath[firstCandidate.NormalizedChartPath].Id);
            Assert.Equal(
                second.Id,
                result.ChartsByPath[secondCandidate.NormalizedChartPath].Id);
            Assert.Equal(0, result.Conflicts);
            Assert.Equal(0, result.Skipped);
        }

        [Fact]
        public async Task ImportSongsAsync_CanonicalPathCollision_ShouldPreserveEveryRow()
        {
            var legacy = await SeedChartWithStoredPathAsync(
                "/songs/active/nested/../chart.dtx", "Legacy");
            var target = await SeedChartWithStoredPathAsync(
                "/songs/active/chart.dtx", "Target");
            var candidate = Candidate(
                "Discovered",
                "dir|active",
                0,
                "/songs/active/chart.dtx",
                60);
            var request = new SongBulkImportRequest(
                new[] { SongPathIdentity.Normalize("/songs/active") },
                new HashSet<string>(
                    new[] { candidate.NormalizedChartPath },
                    SongPathIdentity.CanonicalComparer),
                new[] { candidate });

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.True(await ChartExistsAsync(legacy.Id));
            Assert.True(await ChartExistsAsync(target.Id));
            Assert.Equal(1, result.Conflicts);
            Assert.Equal(1, result.Skipped);
            Assert.False(
                result.ChartsByPath.ContainsKey(candidate.NormalizedChartPath));
        }

        [Fact]
        public async Task ImportSongsAsync_ConflictProtectedExactGroup_ShouldBlockUnmatchedAlias()
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                return;

            var legacy = await SeedChartWithStoredPathAsync(
                "/songs/active/nested/../chart.dtx", "Legacy");
            var exact = await SeedChartWithStoredPathAsync(
                "/songs/active/chart.dtx", "Exact");
            var exactCandidate = Candidate(
                "Exact Candidate",
                "dir|exact",
                0,
                "/songs/active/chart.dtx",
                60);
            var aliasCandidate = Candidate(
                "Alias Candidate",
                "dir|alias",
                0,
                "/songs/active/CHART.dtx",
                60);

            var result = await _service.ImportSongsAsync(
                CreateRequest(exactCandidate, aliasCandidate),
                progress: null,
                CancellationToken.None);

            await using var context = new SongDbContext(_options);
            var storedCharts = await context.SongCharts
                .AsNoTracking()
                .OrderBy(chart => chart.Id)
                .Select(chart => new { chart.Id, chart.FilePath })
                .ToArrayAsync();
            Assert.Equal(2, storedCharts.Length);
            Assert.Contains(
                storedCharts,
                chart => chart.Id == legacy.Id &&
                    chart.FilePath ==
                        "/songs/active/nested/../chart.dtx");
            Assert.Contains(
                storedCharts,
                chart => chart.Id == exact.Id &&
                    chart.FilePath == "/songs/active/chart.dtx");
            Assert.Equal(2, await context.Songs.CountAsync());
            Assert.Equal(2, result.Conflicts);
            Assert.Equal(2, result.Skipped);
            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.StaleCharts);
            Assert.Equal(0, result.StaleSongs);
            Assert.False(
                result.ChartsByPath.ContainsKey(
                    exactCandidate.NormalizedChartPath));
            Assert.False(
                result.ChartsByPath.ContainsKey(
                    aliasCandidate.NormalizedChartPath));
        }

        [Fact]
        public async Task ImportSongsAsync_GlobalBinaryTargetOutsideTrackedGraph_ShouldBlockLegacyRewrite()
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                return;

            var legacy = await SeedChartWithStoredPathAsync(
                "/songs/active/Chart.dtx", "Legacy");
            var target = await SeedChartWithStoredPathAsync(
                "/songs/active/chart.dtx", "Target");
            var candidate = Candidate(
                "Discovered",
                "dir|active",
                0,
                "/songs/active/chart.dtx",
                60);
            var service = new SongDatabaseService(
                _options,
                () => new SongDbContext(_options),
                activeChartIdentityFilter:
                    chartId => chartId != target.Id);

            var result = await service.ImportSongsAsync(
                CreateRequest(candidate),
                progress: null,
                CancellationToken.None);

            await using var context = new SongDbContext(_options);
            var storedCharts = await context.SongCharts
                .AsNoTracking()
                .OrderBy(chart => chart.Id)
                .Select(chart => new { chart.Id, chart.FilePath })
                .ToArrayAsync();
            Assert.Equal(2, storedCharts.Length);
            Assert.Contains(
                storedCharts,
                chart => chart.Id == legacy.Id &&
                    chart.FilePath == "/songs/active/Chart.dtx");
            Assert.Contains(
                storedCharts,
                chart => chart.Id == target.Id &&
                    chart.FilePath == "/songs/active/chart.dtx");
            Assert.Equal(1, result.Conflicts);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.StaleCharts);
            Assert.False(
                result.ChartsByPath.ContainsKey(candidate.NormalizedChartPath));
        }

        [Fact]
        public async Task ImportSongsAsync_MalformedPersistedPath_ShouldRetainRow()
        {
            var malformed = await SeedChartWithStoredPathAsync(
                string.Empty, "Malformed");
            var request = CreateEmptyRequest(
                activeRoots: new[] { "/songs" },
                discoveredPaths: Array.Empty<string>());

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.True(await ChartExistsAsync(malformed.Id));
            Assert.Equal(0, result.StaleCharts);
            Assert.Equal(0, result.StaleSongs);
        }

        [Fact]
        public async Task ImportSongsAsync_DiscoveredSkippedPath_ShouldProtectExistingChart()
        {
            var chart = await SeedChartAsync("/songs/active/skipped.dtx");
            var request = CreateEmptyRequest(
                activeRoots: new[] { "/songs/active" },
                discoveredPaths: new[] { chart.FilePath });

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.True(await ChartExistsAsync(chart.Id));
            Assert.Equal(0, result.StaleCharts);
            Assert.Equal(1, result.Skipped);
        }

        [Fact]
        public async Task ImportSongsAsync_FinalStaleChart_ShouldRemoveEmptySong()
        {
            var stale = await SeedChartAsync("/songs/active/stale.dtx");
            var request = CreateEmptyRequest(
                activeRoots: new[] { "/songs/active" },
                discoveredPaths: Array.Empty<string>());

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.False(await ChartExistsAsync(stale.Id));
            Assert.False(await SongExistsAsync(stale.SongId));
            Assert.Equal(1, result.StaleCharts);
            Assert.Equal(1, result.StaleSongs);
        }

        [Fact]
        public async Task ImportSongsAsync_ActiveStaleChartWithInactiveSibling_ShouldRetainSong()
        {
            await using (var context = new SongDbContext(_options))
            {
                var song = new SongEntity
                {
                    Title = "Shared",
                    Artist = "Fixture Artist"
                };
                song.Charts.Add(new SongChart
                {
                    FilePath = SongPathIdentity.Normalize(
                        "/songs/active/stale.dtx"),
                    DrumLevel = 50,
                    HasDrumChart = true
                });
                song.Charts.Add(new SongChart
                {
                    FilePath = SongPathIdentity.Normalize(
                        "/songs/inactive/keep.dtx"),
                    DrumLevel = 60,
                    HasDrumChart = true
                });
                context.Songs.Add(song);
                await context.SaveChangesAsync();
            }

            int songId;
            int activeChartId;
            int inactiveChartId;
            await using (var context = new SongDbContext(_options))
            {
                var song = await context.Songs
                    .Include(entity => entity.Charts)
                    .SingleAsync();
                songId = song.Id;
                activeChartId = song.Charts.Single(chart =>
                    chart.FilePath.Contains("/active/")).Id;
                inactiveChartId = song.Charts.Single(chart =>
                    chart.FilePath.Contains("/inactive/")).Id;
            }
            var request = CreateEmptyRequest(
                activeRoots: new[] { "/songs/active" },
                discoveredPaths: Array.Empty<string>());

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.False(await ChartExistsAsync(activeChartId));
            Assert.True(await ChartExistsAsync(inactiveChartId));
            Assert.True(await SongExistsAsync(songId));
            Assert.Equal(1, result.StaleCharts);
            Assert.Equal(0, result.StaleSongs);
        }

        [Fact]
        public async Task ImportSongsAsync_MixedBatch_ShouldReportAggregateCounts()
        {
            var stale = await SeedChartAsync("/songs/stale/chart.dtx");
            var candidate = Candidate(
                "Fresh", "dir|fresh", 0, "/songs/fresh/chart.dtx", 50);
            var request = new SongBulkImportRequest(
                new[] { SongPathIdentity.Normalize("/songs") },
                new HashSet<string>(
                    new[] { candidate.NormalizedChartPath },
                    SongPathIdentity.CanonicalComparer),
                new[] { candidate, candidate });

            var result = await _service.ImportSongsAsync(
                request, progress: null, CancellationToken.None);

            Assert.Equal(1, result.Added);
            Assert.Equal(0, result.Updated);
            Assert.Equal(0, result.Preserved);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Conflicts);
            Assert.Equal(1, result.StaleCharts);
            Assert.Equal(1, result.StaleSongs);
            Assert.False(await ChartExistsAsync(stale.Id));
            Assert.Equal(
                SongPathIdentity.Normalize("/songs/fresh/chart.dtx"),
                Assert.Single(result.ChartsByPath.Keys));
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
