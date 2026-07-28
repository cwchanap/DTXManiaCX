using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.TestData;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public sealed class SongManagerBulkEnumerationTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(), "HPA-192-SongManager", Guid.NewGuid().ToString("N"));
    private readonly string _songsRoot;
    private readonly string _databasePath;
    private readonly SongManager _manager;
    private int _seededChartCount;
    private int _bulkImportCalls;

    public SongManagerBulkEnumerationTests()
    {
        _songsRoot = Path.Combine(_testRoot, "Songs");
        _databasePath = Path.Combine(_testRoot, "songs.db");
        Directory.CreateDirectory(_songsRoot);
        SongManager.ResetInstanceForTesting();
        _manager = SongManager.Instance;
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_ShouldPublishCommittedHierarchyWithoutReload()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/A/basic.dtx", title: "Grouped", drumLevel: 20);
        WriteChart("Songs/A/extreme.dtx", title: "Grouped", drumLevel: 80);
        var realImporter = _manager.ImportSongsCoreAsync;
        _manager.ImportSongsCoreAsync = async (database, request, progress, token) =>
        {
            _bulkImportCalls++;
            return await realImporter(database, request, progress, token);
        };

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Batch.IsComplete);
        Assert.Equal(2, result.Batch.DiscoveredChartPaths.Count);
        Assert.Equal(2, result.Batch.Candidates.Count);
        Assert.Single(result.Batch.PendingSongs);
        Assert.Equal(1, _bulkImportCalls);
        Assert.Equal(2, _manager.EnumeratedFileCount);
        Assert.Equal(1, _manager.DiscoveredScoreCount);
        var node = Assert.Single(FlattenScoreNodes(_manager.RootSongs));
        Assert.Equal(2, node.AvailableDifficulties);
        Assert.All(node.Scores.Where(score => score != null), score =>
        {
            Assert.True(score!.ChartId > 0);
            Assert.True(node.ScoreVariants.ContainsKey(
                new ScoreVariantKey(
                    Array.IndexOf(node.Scores, score),
                    PlaySpeedRange.Default)));
        });
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WithoutDatabase_ShouldFailBeforeTraversal()
    {
        var traversalCalls = 0;
        _manager.EnumerateFilesCore = _ =>
        {
            traversalCalls++;
            return Array.Empty<string>();
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Equal(0, traversalCalls);
        Assert.Empty(_manager.RootSongs);
        Assert.Equal(0, _manager.DiscoveredScoreCount);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenCancelled_ShouldLeaveDatabaseAndRootSongsUnchanged()
    {
        await SeedPublishedLibraryAsync();
        WriteChart("Songs/New/new.dtx", "New", 70);
        var originalRoots = _manager.RootSongs.ToArray();
        var originalRows = await ReadPersistedChartSnapshotsAsync();
        var originalDiscoveredScoreCount = _manager.DiscoveredScoreCount;
        var originalEnumeratedFileCount = _manager.EnumeratedFileCount;
        var originalActiveRoots = ReflectionHelpers.GetPrivateField<string[]>(
            _manager,
            "_currentSearchPaths");
        var discoveredEvents = 0;
        var completedEvents = 0;
        _manager.SongDiscovered += (_, _) => discoveredEvents++;
        _manager.EnumerationCompleted += (_, _) => completedEvents++;
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<EnumerationProgress>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, progress, cancellation.Token));

        Assert.Equal(originalRoots.Length, _manager.RootSongs.Count);
        for (var index = 0; index < originalRoots.Length; index++)
            Assert.Same(originalRoots[index], _manager.RootSongs[index]);
        Assert.Equal(originalRows, await ReadPersistedChartSnapshotsAsync());
        Assert.Equal(_seededChartCount, await CountChartsAsync());
        Assert.Equal(
            originalDiscoveredScoreCount,
            _manager.DiscoveredScoreCount);
        Assert.Equal(
            originalEnumeratedFileCount,
            _manager.EnumeratedFileCount);
        Assert.Equal(
            originalActiveRoots,
            ReflectionHelpers.GetPrivateField<string[]>(
                _manager,
                "_currentSearchPaths"));
        Assert.Equal(0, discoveredEvents);
        Assert.Equal(0, completedEvents);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSqliteWriteFails_ShouldLeaveDatabaseAndRootsUnchanged()
    {
        await SeedPublishedLibraryAsync();
        var originalRoots = _manager.RootSongs.ToArray();
        var originalRows = await ReadPersistedChartSnapshotsAsync();
        var originalDiscoveredScoreCount = _manager.DiscoveredScoreCount;
        var originalEnumeratedFileCount = _manager.EnumeratedFileCount;
        var originalActiveRoots = ReflectionHelpers.GetPrivateField<string[]>(
            _manager,
            "_currentSearchPaths");
        var discoveredEvents = 0;
        var completedEvents = 0;
        _manager.SongDiscovered += (_, _) => discoveredEvents++;
        _manager.EnumerationCompleted += (_, _) => completedEvents++;
        await ExecuteDatabaseSqlAsync(
            "CREATE TRIGGER fail_chart BEFORE INSERT ON SongCharts " +
            "BEGIN SELECT RAISE(ABORT, 'forced manager import failure'); END;");
        WriteChart("Songs/New/new.dtx", "New", 70);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Equal(originalRoots.Length, _manager.RootSongs.Count);
        for (var index = 0; index < originalRoots.Length; index++)
            Assert.Same(originalRoots[index], _manager.RootSongs[index]);
        Assert.Equal(originalRows, await ReadPersistedChartSnapshotsAsync());
        Assert.Equal(_seededChartCount, await CountChartsAsync());
        Assert.Equal(
            originalDiscoveredScoreCount,
            _manager.DiscoveredScoreCount);
        Assert.Equal(
            originalEnumeratedFileCount,
            _manager.EnumeratedFileCount);
        Assert.Equal(
            originalActiveRoots,
            ReflectionHelpers.GetPrivateField<string[]>(
                _manager,
                "_currentSearchPaths"));
        Assert.Equal(0, discoveredEvents);
        Assert.Equal(0, completedEvents);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WhenChartParseFails_ShouldRetainDiscoveredPath()
    {
        var chartPath = WriteChart("Songs/Broken/chart.dtx", "Broken", 50);
        _manager.ParseSongEntitiesCoreAsync = path =>
            Task.FromException<(SongEntity, SongChart)>(
                new InvalidDataException($"malformed {path}"));

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.True(batch.IsComplete);
        Assert.Contains(SongPathIdentity.Normalize(chartPath), batch.DiscoveredChartPaths);
        Assert.Empty(batch.Candidates);
        Assert.Contains(batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(chartPath) &&
            !error.IsRootFailure);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenChartParseFails_ShouldImportValidCandidatesAndPreserveFailedChart()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var protectedPath = WriteChart(
            "Songs/Protected/chart.dtx",
            "Protected",
            50);
        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        var protectedChart = Assert.Single(
            Assert.Single(await _manager.DatabaseService!.GetSongsAsync()).Charts);
        var newPath = WriteChart("Songs/Valid/new.dtx", "Valid", 70);
        var realParser = _manager.ParseSongEntitiesCoreAsync;
        var realImporter = _manager.ImportSongsCoreAsync;
        _manager.ParseSongEntitiesCoreAsync = path =>
            SongPathIdentity.CanonicalComparer.Equals(path, protectedPath)
                ? Task.FromException<(SongEntity, SongChart)>(
                    new InvalidDataException($"malformed {path}"))
                : realParser(path);
        _manager.ImportSongsCoreAsync = async (database, request, progress, token) =>
        {
            _bulkImportCalls++;
            return await realImporter(database, request, progress, token);
        };

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(1, _bulkImportCalls);
        Assert.Contains(result.Batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(protectedPath) &&
            !error.IsRootFailure);
        Assert.Contains(
            FlattenScoreNodes(_manager.RootSongs),
            node => node.Title == "Valid" &&
                node.Scores.Any(score => score?.ChartId > 0));
        var persisted = await _manager.DatabaseService.GetSongsAsync();
        var preserved = Assert.Single(
            persisted.SelectMany(song => song.Charts),
            chart => SongPathIdentity.CanonicalComparer.Equals(
                chart.FilePath,
                protectedPath));
        Assert.Equal(protectedChart.Id, preserved.Id);
        Assert.Equal(50, preserved.DrumLevel);
        Assert.Contains(
            persisted.SelectMany(song => song.Charts),
            chart => SongPathIdentity.CanonicalComparer.Equals(
                chart.FilePath,
                newPath));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetChartParseFails_ShouldImportOtherDefinitionCharts()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var setRoot = Path.Combine(_songsRoot, "Set Song");
        Directory.CreateDirectory(setRoot);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE Recoverable Set
            #L1LABEL BASIC
            #L1FILE basic.dtx
            #L3LABEL EXTREME
            #L3FILE extreme.dtx
            """);
        var basicPath = WriteChart(
            "Songs/Set Song/basic.dtx",
            "Basic",
            20);
        var failedPath = WriteChart(
            "Songs/Set Song/extreme.dtx",
            "Extreme",
            80);
        var realParser = _manager.ParseSongEntitiesCoreAsync;
        _manager.ParseSongEntitiesCoreAsync = path =>
            SongPathIdentity.CanonicalComparer.Equals(path, failedPath)
                ? Task.FromException<(SongEntity, SongChart)>(
                    new InvalidDataException($"malformed {path}"))
                : realParser(path);
        var realImporter = _manager.ImportSongsCoreAsync;
        _manager.ImportSongsCoreAsync = async (database, request, progress, token) =>
        {
            _bulkImportCalls++;
            return await realImporter(database, request, progress, token);
        };

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(1, _bulkImportCalls);
        Assert.Contains(result.Batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(failedPath) &&
            !error.IsRootFailure);
        var node = Assert.Single(FlattenScoreNodes(_manager.RootSongs));
        var score = Assert.Single(node.Scores, score => score != null)!;
        Assert.Equal("BASIC", score.DifficultyLabel);
        Assert.True(score.ChartId > 0);
        Assert.Contains(
            (await _manager.DatabaseService!.GetSongsAsync())
                .SelectMany(song => song.Charts),
            chart => SongPathIdentity.CanonicalComparer.Equals(
                chart.FilePath,
                basicPath));
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_ShouldParseChartsSerially()
    {
        WriteChart("Songs/Serial/one.dtx", "One", 30);
        WriteChart("Songs/Serial/two.dtx", "Two", 40);
        var realParser = _manager.ParseSongEntitiesCoreAsync;
        var inFlight = 0;
        var maximumInFlight = 0;
        _manager.ParseSongEntitiesCoreAsync = async path =>
        {
            var current = Interlocked.Increment(ref inFlight);
            maximumInFlight = Math.Max(maximumInFlight, current);
            await Task.Yield();
            try
            {
                return await realParser(path);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        };

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.True(batch.IsComplete);
        Assert.Equal(2, batch.Candidates.Count);
        Assert.Equal(1, maximumInFlight);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenRootTraversalFails_ShouldNotCallImporter()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var realImporter = _manager.ImportSongsCoreAsync;
        _manager.ImportSongsCoreAsync = async (database, request, progress, token) =>
        {
            _bulkImportCalls++;
            return await realImporter(database, request, progress, token);
        };
        _manager.EnumerateDirectoriesCore = _ =>
            throw new IOException("root unavailable");

        await Assert.ThrowsAsync<IOException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Equal(0, _bulkImportCalls);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task FinalizePendingNodes_ShouldPreserveSetAndBoxPlaceholderIdentityAndPresentation()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var boxRoot = Path.Combine(_songsRoot, "DTXFiles.Authored");
        var setRoot = Path.Combine(boxRoot, "Set Song");
        Directory.CreateDirectory(setRoot);
        await File.WriteAllTextAsync(
            Path.Combine(boxRoot, "box.def"),
            """
            #TITLE: Authored Box
            #GENRE: Fusion
            #SKINPATH: skins/authored
            #TEXTCOLOR: #33FF57
            """);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE Authored Set
            #L1LABEL NOVICE
            #L1FILE basic.dtx
            #L3LABEL EXPERT
            #L3FILE expert.dtx
            """);
        WriteChart("Songs/DTXFiles.Authored/Set Song/basic.dtx", "Ignored Basic", 25);
        WriteChart("Songs/DTXFiles.Authored/Set Song/expert.dtx", "Ignored Expert", 85);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        var box = Assert.Single(batch.RootNodes);
        var placeholder = Assert.Single(box.Children);
        var parent = placeholder.Parent;
        var index = parent!.Children.IndexOf(placeholder);
        var breadcrumb = placeholder.BreadcrumbPath;
        var labels = placeholder.DifficultyLabels.ToArray();

        var import = await _manager.DatabaseService!.ImportSongsAsync(
            new SongBulkImportRequest(
                batch.ActiveRoots,
                batch.DiscoveredChartPaths,
                batch.Candidates),
            progress: null,
            CancellationToken.None);
        _manager.FinalizePendingNodes(batch, import.ChartsByPath);
        _manager.PublishEnumeration(batch);

        var publishedBox = Assert.Single(_manager.RootSongs);
        var published = Assert.Single(publishedBox.Children);
        Assert.Same(box, publishedBox);
        Assert.Same(placeholder, published);
        Assert.Same(parent, published.Parent);
        Assert.Equal(index, published.Parent!.Children.IndexOf(published));
        Assert.Equal(breadcrumb, published.BreadcrumbPath);
        Assert.Equal(labels, published.DifficultyLabels);
        Assert.Equal("Authored Box", publishedBox.Title);
        Assert.Equal("Fusion", publishedBox.Genre);
        Assert.Equal("skins/authored", publishedBox.SkinPath);
        Assert.Equal(new Microsoft.Xna.Framework.Color(0x33, 0xFF, 0x57), publishedBox.TextColor);
        Assert.Equal(new[] { "NOVICE", "EXPERT" }, published.DifficultyLabels.Take(2));
        Assert.Equal(new[] { 25, 85 }, published.Scores
            .Where(score => score != null)
            .Select(score => score!.DifficultyLevel));
        Assert.All(published.Scores.Where(score => score != null),
            score => Assert.True(score!.ChartId > 0));
    }

    [Fact]
    public async Task BuildHierarchyFromDatabaseOnceAsync_ShouldGroupOnlyBySongId()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var firstPath = WriteChart("Songs/One/basic.dtx", "Duplicate", 20);
        var secondPath = WriteChart("Songs/Two/basic.dtx", "Duplicate", 40);
        var multiBasic = WriteChart("Songs/Multi/basic.dtx", "Multi", 30);
        var multiExtreme = WriteChart("Songs/Multi/extreme.dtx", "Multi", 80);
        await using (var context = _manager.DatabaseService!.CreateContext())
        {
            context.Songs.AddRange(
                CreatePersistedSong("Duplicate", firstPath, 20),
                CreatePersistedSong("Duplicate", secondPath, 40),
                CreatePersistedSong("Multi", multiBasic, 30, multiExtreme, 80));
            await context.SaveChangesAsync();
        }

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { _songsRoot });

        var scores = FlattenScoreNodes(_manager.RootSongs).ToArray();
        Assert.Equal(2, scores.Count(node => node.Title == "Duplicate"));
        var multi = Assert.Single(scores, node => node.Title == "Multi");
        Assert.Equal(2, multi.AvailableDifficulties);
    }

    [Fact]
    public async Task BuildHierarchyFromDatabaseOnceAsync_WithLegacySharedSongId_ShouldNotSplitIt()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var firstPath = WriteChart("Songs/LegacyOne/basic.dtx", "Legacy", 20);
        var secondPath = WriteChart("Songs/LegacyTwo/extreme.dtx", "Legacy", 80);
        await using (var context = _manager.DatabaseService!.CreateContext())
        {
            context.Songs.Add(CreatePersistedSong(
                "Legacy", firstPath, 20, secondPath, 80));
            await context.SaveChangesAsync();
        }

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { _songsRoot });

        var legacy = Assert.Single(
            FlattenScoreNodes(_manager.RootSongs),
            node => node.Title == "Legacy");
        Assert.Equal(2, legacy.AvailableDifficulties);
    }

    [Fact]
    public async Task BuildHierarchyFromDatabaseOnceAsync_ShouldLoadOnceAndNotCleanupStaleRows()
    {
        var options = new DbContextOptionsBuilder<SongDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
        var activePath = WriteChart("Songs/Active/active.dtx", "Active", 50);
        var stalePath = Path.Combine(_songsRoot, "Removed", "stale.dtx");
        await using (var seed = new SongDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Songs.AddRange(
                CreatePersistedSong("Active", activePath, 50),
                CreatePersistedSong("Stale", stalePath, 60));
            await seed.SaveChangesAsync();
        }

        var contextCreations = 0;
        var service = new SongDatabaseService(
            options,
            () =>
            {
                Interlocked.Increment(ref contextCreations);
                return new SongDbContext(options);
            });
        ReflectionHelpers.SetPrivateField(_manager, "_databaseService", service);

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { _songsRoot });

        Assert.Equal(1, contextCreations);
        await using var verification = new SongDbContext(options);
        Assert.Equal(2, await verification.SongCharts.CountAsync());
        Assert.Single(FlattenScoreNodes(_manager.RootSongs));
    }

    [Fact]
    public async Task ActiveSessionQueries_ShouldExcludeSongsOutsideCurrentSearchRoots()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var activeRoot = Path.Combine(_testRoot, "Active");
        var removedRoot = Path.Combine(_testRoot, "Removed");
        var activePath = WriteChart("Active/active.dtx", "Shared Search Active", 45);
        var removedPath = WriteChart("Removed/removed.dtx", "Shared Search Removed", 65);
        await using (var context = _manager.DatabaseService!.CreateContext())
        {
            var active = CreatePersistedSong("Shared Search Active", activePath, 45);
            active.IsBookmarked = true;
            active.Charts.Single().Scores.Add(CreatePlayedScore(DateTime.UtcNow.AddMinutes(-1)));
            var removed = CreatePersistedSong("Shared Search Removed", removedPath, 65);
            removed.IsBookmarked = true;
            removed.Charts.Single().Scores.Add(CreatePlayedScore(DateTime.UtcNow));
            context.Songs.AddRange(active, removed);
            await context.SaveChangesAsync();
        }

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { activeRoot });

        var bookmarks = await _manager.GetBookmarkedNodesAsync();
        var recent = await _manager.GetRecentlyPlayedNodesAsync();
        var search = await _manager.FindSongsBySearchAsync("Shared Search");
        Assert.Equal(new[] { "Shared Search Active" }, bookmarks.Select(node => node.Title));
        Assert.Equal(new[] { "Shared Search Active" }, recent.Select(node => node.Title));
        Assert.Equal(new[] { "Shared Search Active" }, search.Select(song => song.Title));
        Assert.All(
            bookmarks.Concat(recent),
            node => Assert.All(
                node.DatabaseSong!.Charts,
                chart => Assert.True(SongPathIdentity.IsUnderRoot(chart.FilePath, activeRoot))));
    }

    [Fact]
    public async Task GetRecentlyPlayedNodesAsync_ShouldOrderSharedSongByActiveChartHistory()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var activeRoot = Path.Combine(_testRoot, "Active");
        var activeSharedPath = WriteChart(
            "Active/Shared/active.dtx",
            "Shared Song",
            45);
        var inactiveSharedPath = WriteChart(
            "Removed/Shared/inactive.dtx",
            "Shared Song",
            65);
        var activeOnlyPath = WriteChart(
            "Active/Only/active.dtx",
            "Active Only",
            55);
        var now = DateTime.UtcNow;
        await using (var context = _manager.DatabaseService!.CreateContext())
        {
            var shared = new SongEntity
            {
                Title = "Shared Song",
                Artist = "Fixture Artist",
                Genre = "Fixture"
            };
            var activeShared = CreatePersistedChart(activeSharedPath, 45);
            activeShared.Scores.Add(CreatePlayedScore(now.AddMinutes(-10)));
            var inactiveShared = CreatePersistedChart(inactiveSharedPath, 65);
            inactiveShared.Scores.Add(CreatePlayedScore(now));
            shared.Charts.Add(activeShared);
            shared.Charts.Add(inactiveShared);

            var activeOnly = CreatePersistedSong(
                "Active Only",
                activeOnlyPath,
                55);
            activeOnly.Charts.Single().Scores.Add(
                CreatePlayedScore(now.AddMinutes(-5)));
            context.Songs.AddRange(shared, activeOnly);
            await context.SaveChangesAsync();
        }
        await _manager.BuildSongListFromDatabasePublicAsync(
            new[] { activeRoot });

        var recent = await _manager.GetRecentlyPlayedNodesAsync();

        Assert.Equal(
            new[] { "Active Only", "Shared Song" },
            recent.Select(node => node.Title));
        Assert.All(
            recent,
            node => Assert.All(
                node.DatabaseSong!.Charts,
                chart => Assert.True(
                    SongPathIdentity.IsUnderRoot(chart.FilePath, activeRoot))));
    }

    [Fact]
    public async Task CompatibilityWrappers_WithoutDatabase_ShouldReturnZeroAndNotPublish()
    {
        WriteChart("Songs/Unpublished/chart.dtx", "Unpublished", 50);

        Assert.Equal(0, await _manager.EnumerateSongsAsync(new[] { _songsRoot }));
        Assert.Equal(0, await _manager.EnumerateSongsOnlyAsync(new[] { _songsRoot }));
        Assert.Empty(_manager.RootSongs);
        Assert.Equal(0, _manager.DiscoveredScoreCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompatibilityWrapper_WithSetDefinition_ShouldPublishSameNodeWithDurableIds(
        bool enumerateOnly)
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var setRoot = Path.Combine(_songsRoot, "Durable Set");
        Directory.CreateDirectory(setRoot);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE Durable Set
            #L1LABEL BASIC
            #L1FILE basic.dtx
            #L3LABEL EXTREME
            #L3FILE extreme.dtx
            """);
        WriteChart("Songs/Durable Set/basic.dtx", "Basic", 20);
        WriteChart("Songs/Durable Set/extreme.dtx", "Extreme", 80);
        SongListNode? discovered = null;
        _manager.SongDiscovered += (_, args) => discovered = args.Song;

        var count = enumerateOnly
            ? await _manager.EnumerateSongsOnlyAsync(new[] { _songsRoot })
            : await _manager.EnumerateSongsAsync(new[] { _songsRoot });

        Assert.Equal(2, count);
        var published = Assert.Single(FlattenScoreNodes(_manager.RootSongs));
        Assert.Same(published, discovered);
        Assert.True(published.DatabaseSongId > 0);
        Assert.All(
            published.Scores.Where(score => score != null),
            score => Assert.True(score!.ChartId > 0));
    }

    [Fact]
    public async Task CompatibilityWrapper_WhenImportFails_ShouldRethrowAndNotPublish()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/Failure/chart.dtx", "Failure", 50);
        _manager.ImportSongsCoreAsync = (_, _, _, _) =>
            Task.FromException<SongBulkImportResult>(
                new IOException("persistence unavailable"));

        await Assert.ThrowsAsync<IOException>(() =>
            _manager.EnumerateSongsAsync(new[] { _songsRoot }));

        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task SaveSongsDBAsync_ShouldNotQueryDatabaseStatistics()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var statisticsCalls = 0;
        _manager.GetDatabaseStatsCoreAsync = _ =>
        {
            statisticsCalls++;
            return Task.FromResult<DatabaseStats?>(null);
        };

        var saved = await _manager.SaveSongsDBAsync();

        Assert.True(saved);
        Assert.Equal(0, statisticsCalls);
    }

    [Fact]
    public async Task Clear_ShouldResetEnumerationDelegateSeams()
    {
        var chartPath = WriteChart("Songs/Reset/chart.dtx", "Reset", 50);
        _manager.ParseSongEntitiesCoreAsync = _ =>
            Task.FromException<(SongEntity, SongChart)>(
                new InvalidDataException("contaminated parser"));
        _manager.EnumerateFilesCore = _ => throw new IOException("contaminated files");
        _manager.EnumerateDirectoriesCore = _ => throw new IOException("contaminated directories");
        _manager.ImportSongsCoreAsync = (_, _, _, _) =>
            Task.FromException<SongBulkImportResult>(
                new IOException("contaminated importer"));

        _manager.Clear();
        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Single(batch.Candidates);
        Assert.Contains(SongPathIdentity.Normalize(chartPath), batch.DiscoveredChartPaths);
    }

    public void Dispose()
    {
        SongManager.ResetInstanceForTesting();
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    private string WriteChart(string relativePath, string title, int drumLevel)
    {
        var path = Path.Combine(_testRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, new[]
        {
            $"#TITLE: {title}",
            "#ARTIST: Fixture Artist",
            "#BPM: 120",
            $"#DLEVEL: {drumLevel}"
        });
        return path;
    }

    private static IEnumerable<SongListNode> FlattenScoreNodes(
        IEnumerable<SongListNode> roots)
    {
        foreach (var node in roots)
        {
            if (node.Type == NodeType.Score)
                yield return node;
            foreach (var child in FlattenScoreNodes(node.Children))
                yield return child;
        }
    }

    private async Task<int> CountChartsAsync()
    {
        await using var context = _manager.DatabaseService!.CreateContext();
        return await context.SongCharts.CountAsync();
    }

    private async Task<PersistedChartSnapshot[]> ReadPersistedChartSnapshotsAsync()
    {
        var songs = await _manager.DatabaseService!.GetSongsAsync();
        return songs
            .SelectMany(song => song.Charts.Select(chart =>
                new PersistedChartSnapshot(
                    song.Id,
                    chart.Id,
                    song.Title,
                    song.Artist,
                    chart.FilePath,
                    chart.DrumLevel,
                    chart.Scores.Count,
                    chart.Scores.Sum(score => score.PlayCount))))
            .OrderBy(snapshot => snapshot.ChartId)
            .ToArray();
    }

    private async Task SeedPublishedLibraryAsync()
    {
        WriteChart("Songs/Seed/seed.dtx", "Seed", 40);
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        _seededChartCount = await CountChartsAsync();
    }

    private async Task ExecuteDatabaseSqlAsync(string sql)
    {
        await using var context = _manager.DatabaseService!.CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static SongEntity CreatePersistedSong(
        string title,
        string firstPath,
        int firstLevel,
        string? secondPath = null,
        int secondLevel = 0)
    {
        var song = new SongEntity
        {
            Title = title,
            Artist = "Fixture Artist",
            Genre = "Fixture"
        };
        song.Charts.Add(CreatePersistedChart(firstPath, firstLevel));
        if (secondPath != null)
            song.Charts.Add(CreatePersistedChart(secondPath, secondLevel));
        return song;
    }

    private static SongChart CreatePersistedChart(string path, int level) =>
        new()
        {
            FilePath = path,
            HasDrumChart = true,
            DrumLevel = level,
            DifficultyLevel = level
        };

    private static SongScore CreatePlayedScore(DateTime playedAt) =>
        new()
        {
            Instrument = EInstrumentPart.DRUMS,
            PlaySpeedPercent = PlaySpeedRange.Default,
            LastPlayedAt = playedAt,
            PlayCount = 1
        };

    private sealed record PersistedChartSnapshot(
        int SongId,
        int ChartId,
        string Title,
        string Artist,
        string FilePath,
        int DrumLevel,
        int ScoreCount,
        int PlayCount);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
