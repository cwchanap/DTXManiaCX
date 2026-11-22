using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.TestData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public sealed class SongManagerBulkEnumerationTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(), "HPA-192-SongManager", Guid.NewGuid().ToString("N"));
    private readonly string _songsRoot;
    private readonly string _databasePath;
    private SongLibrary _manager;
    private int _seededChartCount;
    private int _bulkImportCalls;

    public SongManagerBulkEnumerationTests()
    {
        _songsRoot = Path.Combine(_testRoot, "Songs");
        _databasePath = Path.Combine(_testRoot, "songs.db");
        Directory.CreateDirectory(_songsRoot);
        _manager = new SongLibrary(new SongDatabase(_databasePath));
    }

    /// <summary>
    /// Builds a test-owned library over this fixture's database with the final
    /// constructor seams (HPA-801 Decision E): the SongLibrary seam properties
    /// are immutable, so collaborators are configured here, once, at construction.
    /// </summary>
    private SongLibrary CreateManager(
        SongRootPolicy? rootPolicy = null,
        string? bundledSongsRoot = null,
        Func<string, Task<(SongEntity song, SongChart chart)>>? parseSongEntitiesAsync = null,
        Func<string, IEnumerable<string>>? enumerateFiles = null,
        Func<string, IEnumerable<string>>? enumerateDirectories = null,
        Func<string, Encoding, CancellationToken, Task<string[]>>? readAllLinesAsync = null,
        Func<
            SongCatalogStore,
            SongBulkImportRequest,
            IProgress<SongBulkImportProgress>?,
            CancellationToken,
            Task<SongBulkImportResult>>? importSongsAsync = null,
        Func<
            string[],
            IProgress<EnumerationProgress>?,
            CancellationToken,
            Task<SongEnumerationBatch>>? buildEnumerationBatchAsync = null,
        Action<SongEnumerationBatch, IReadOnlyDictionary<string, SongChart>>? finalizePendingNodes = null,
        Action<SongEnumerationBatch>? publishEnumeration = null)
    {
        return new SongLibrary(
            new SongDatabase(_databasePath),
            rootPolicy: rootPolicy,
            bundledSongsRoot: bundledSongsRoot,
            parseSongEntitiesAsync: parseSongEntitiesAsync,
            enumerateFiles: enumerateFiles,
            enumerateDirectories: enumerateDirectories,
            readAllLinesAsync: readAllLinesAsync,
            importSongsAsync: importSongsAsync,
            buildEnumerationBatchAsync: buildEnumerationBatchAsync,
            finalizePendingNodes: finalizePendingNodes,
            publishEnumeration: publishEnumeration);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_ShouldPublishCommittedHierarchyWithoutReload()
    {
        _manager = CreateManager(importSongsAsync: (catalog, request, progress, token) =>
        {
            _bulkImportCalls++;
            return catalog.ImportSongsAsync(request, progress, token);
        });
        await _manager.InitializeAsync();
        WriteChart("Songs/A/basic.dtx", title: "Grouped", drumLevel: 20);
        WriteChart("Songs/A/extreme.dtx", title: "Grouped", drumLevel: 80);

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
        Assert.Equal(2, _manager.DiscoveredScoreCount);
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
    public async Task EnumerateAndImportSongsAsync_WithThreeDifficultiesInOneSet_ShouldReportDiscoveredScoreCountAsChartCount()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/Set/basic.dtx", title: "Set Song", drumLevel: 20);
        WriteChart("Songs/Set/advanced.dtx", title: "Set Song", drumLevel: 50);
        WriteChart("Songs/Set/extreme.dtx", title: "Set Song", drumLevel: 80);

        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, progress: null, CancellationToken.None);

        // DiscoveredScoreCount is a count of discovered scores/charts, not of
        // grouped logical songs: a three-difficulty set reports 3.
        Assert.Equal(3, _manager.DiscoveredScoreCount);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_PublicOverload_ShouldCompleteBatch()
    {
        await _manager.InitializeAsync();

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Batch.IsComplete);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSuccessful_ShouldClearCancellationSource()
    {
        await _manager.InitializeAsync();

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Batch.IsComplete);
        Assert.Null(
            ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                _manager,
                "_enumCancellation"));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenFaulted_ShouldPropagateAndClearCancellationSource()
    {
        var expected = new IOException("persistence unavailable");
        _manager = CreateManager(importSongsAsync: (_, _, _, _) =>
            Task.FromException<SongBulkImportResult>(expected));
        await _manager.InitializeAsync();
        WriteChart("Songs/Failure/chart.dtx", "Failure", 50);

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress: null,
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Null(
            ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                _manager,
                "_enumCancellation"));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenCancelled_ShouldClearCancellationSource()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/Cancelled/chart.dtx", "Cancelled", 50);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<EnumerationProgress>(
            _ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress,
                cancellation.Token));

        Assert.Null(
            ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                _manager,
                "_enumCancellation"));
    }

    [Fact]
    public async Task Enumeration_WhenCancelledButNotTerminated_ShouldKeepSlotOccupiedUntilTermination()
    {
        // Park the enumeration inside the batch builder so it holds the slot
        // after cancellation but before EndEnumeration runs.
        var gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _manager = CreateManager(buildEnumerationBatchAsync: (_, _, token) =>
            ParkUntilReleasedThenCancelAsync(gate, token));
        await _manager.InitializeAsync();

        using var cts = new CancellationTokenSource();
        // Fire enumeration A without awaiting: it acquires the slot then parks.
        var enumerationA = _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, progress: null, cts.Token);

        await SpinUntilAsync(
            () => ManagerIsEnumerating,
            "Enumeration slot was never acquired.");

        // Cancel while A is still executing inside the batch builder.
        cts.Cancel();

        try
        {
            // The slot must remain occupied until A actually terminates, so a
            // concurrent caller cannot start a second enumeration against the
            // shared _rootSongs/database state while A winds down.
            Assert.True(ManagerIsEnumerating);

            // A second enumeration must be rejected while A has not terminated.
            await Assert.ThrowsAsync<SongEnumerationBusyException>(() =>
                _manager.EnumerateAndImportSongsAsync(
                    new[] { _songsRoot }, progress: null, CancellationToken.None));
        }
        finally
        {
            // Release A regardless of assertion outcome so it terminates and
            // frees the singleton slot for subsequent tests.
            gate.TrySetResult(true);
            try { await enumerationA; }
            catch (OperationCanceledException) { /* expected termination */ }
        }

        // After A terminates the slot is free again.
        Assert.False(ManagerIsEnumerating);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WithAlreadyCancelledToken_ShouldFreeSlotForSubsequentEnumeration()
    {
        // Regression: an already-cancelled token used to throw from the
        // pre-try cancellation check, skipping EndEnumeration and leaving
        // _enumCancellation non-null permanently. Every subsequent call was
        // then rejected as "already in progress."
        await _manager.InitializeAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress: null,
                cts.Token));

        // The slot must be released even though the throw happened before the
        // batch builder ran.
        Assert.False(ManagerIsEnumerating);
        Assert.Null(
            ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                _manager,
                "_enumCancellation"));

        // A subsequent enumeration must start and complete normally.
        WriteChart("Songs/Recovery/chart.dtx", "Recovery", 50);
        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Batch.IsComplete);
        Assert.Single(result.Batch.DiscoveredChartPaths);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WithoutDatabase_ThenInitialized_ShouldAllowRetry()
    {
        // Regression: calling the direct entry point without an initialized
        // database used to throw from the pre-try database snapshot, skipping
        // EndEnumeration and leaving _enumCancellation non-null permanently.
        // Initializing the database afterward did not free the slot, so the
        // retry was rejected as "already in progress."
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress: null,
                CancellationToken.None));

        Assert.False(ManagerIsEnumerating);
        Assert.Null(
            ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                _manager,
                "_enumCancellation"));

        // Initialize the database after the failed attempt and verify the
        // enumeration can be retried successfully.
        await _manager.InitializeAsync();
        WriteChart("Songs/Retry/chart.dtx", "Retry", 60);
        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Batch.IsComplete);
        Assert.Single(result.Batch.DiscoveredChartPaths);
    }

    private static async Task<SongEnumerationBatch> ParkUntilReleasedThenCancelAsync(
        TaskCompletionSource<bool> gate, CancellationToken token)
    {
        await gate.Task.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        throw new OperationCanceledException(token);
    }

    private static async Task SpinUntilAsync(
        Func<bool> condition, string message)
    {
        // 600 iterations * 10ms = 6s budget. A loaded CI host may schedule
        // the background enumeration's slot acquisition slowly, so the budget
        // is intentionally generous while keeping the 10ms polling cadence.
        for (var i = 0; i < 600; i++)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }
        Assert.True(condition(), message);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSongLibraryPublishedSubscriberThrows_ShouldNotFailOrSuppressOtherSubscribers()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/A/basic.dtx", title: "Grouped", drumLevel: 20);
        WriteChart("Songs/A/extreme.dtx", title: "Grouped", drumLevel: 80);

        var secondPublishedCalls = 0;
        // First subscriber throws; it must not stop the second subscriber, and
        // must not turn the committed/published import into a reported failure.
        // (The per-event SongDiscovered/EnumerationCompleted events were removed
        // with HPA-801 Decision 0; SafeInvoke isolation is proven against the
        // one remaining published event.)
        _manager.SongLibraryPublished += (_, _) => throw new InvalidOperationException("boom");
        _manager.SongLibraryPublished += (_, _) => secondPublishedCalls++;
        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, progress: null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Batch.IsComplete);
        Assert.True(secondPublishedCalls > 0,
            "A throwing SongLibraryPublished subscriber suppressed later subscribers.");
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WithoutDatabase_ShouldFailBeforeTraversal()
    {
        var traversalCalls = 0;
        _manager = CreateManager(enumerateFiles: _ =>
        {
            traversalCalls++;
            return Array.Empty<string>();
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Equal(0, traversalCalls);
        Assert.Empty(_manager.RootSongs);
        Assert.Equal(0, _manager.DiscoveredScoreCount);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WithEmptyRoot_ShouldPublishEmptySnapshot()
    {
        await _manager.InitializeAsync();

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(0, result.Batch.Candidates.Count);
        Assert.True(result.Batch.IsComplete);
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
        var publishEvents = 0;
        _manager.SongLibraryPublished += (_, _) => publishEvents++;
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
        Assert.Equal(0, publishEvents);
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
        var publishEvents = 0;
        _manager.SongLibraryPublished += (_, _) => publishEvents++;
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
        Assert.Equal(0, publishEvents);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WhenChartParseFails_ShouldRetainDiscoveredPath()
    {
        var chartPath = WriteChart("Songs/Broken/chart.dtx", "Broken", 50);
        _manager = CreateManager(parseSongEntitiesAsync: path =>
            Task.FromException<(SongEntity, SongChart)>(
                new InvalidDataException($"malformed {path}")));

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
        await _manager.InitializeAsync();
        var protectedPath = WriteChart(
            "Songs/Protected/chart.dtx",
            "Protected",
            50);
        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        var protectedChart = Assert.Single(
            Assert.Single(await new SongCatalogStore(ManagerDatabaseService!).GetSongsAsync()).Charts);
        var newPath = WriteChart("Songs/Valid/new.dtx", "Valid", 70);
        // Second pass runs on a fresh library over the same database (HPA-801:
        // collaborators are constructor seams, so the failing-parser phase gets
        // its own configured instance).
        var failingParse = CreateManager(
            parseSongEntitiesAsync: path =>
                SongPathIdentity.CanonicalComparer.Equals(path, protectedPath)
                    ? Task.FromException<(SongEntity, SongChart)>(
                        new InvalidDataException($"malformed {path}"))
                    : DTXChartParser.ParseSongEntitiesAsync(path),
            importSongsAsync: (catalog, request, progress, token) =>
            {
                _bulkImportCalls++;
                return catalog.ImportSongsAsync(request, progress, token);
            });

        await failingParse.InitializeAsync();
        var result = await failingParse.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(1, _bulkImportCalls);
        Assert.Contains(result.Batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(protectedPath) &&
            !error.IsRootFailure);
        Assert.Contains(
            FlattenScoreNodes(failingParse.RootSongs),
            node => node.Title == "Valid" &&
                node.Scores.Any(score => score?.ChartId > 0));
        var persisted = await new SongCatalogStore(failingParse.Database).GetSongsAsync();
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
        await _manager.InitializeAsync();
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
        // Second pass runs on a fresh library over the same database (HPA-801
        // constructor seams).
        var failingParse = CreateManager(
            parseSongEntitiesAsync: path =>
                SongPathIdentity.CanonicalComparer.Equals(path, failedPath)
                    ? Task.FromException<(SongEntity, SongChart)>(
                        new InvalidDataException($"malformed {path}"))
                    : DTXChartParser.ParseSongEntitiesAsync(path),
            importSongsAsync: (catalog, request, progress, token) =>
            {
                _bulkImportCalls++;
                return catalog.ImportSongsAsync(request, progress, token);
            });

        await failingParse.InitializeAsync();
        var result = await failingParse.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(1, _bulkImportCalls);
        Assert.Contains(result.Batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(failedPath) &&
            !error.IsRootFailure);
        var node = Assert.Single(FlattenScoreNodes(failingParse.RootSongs));
        var score = Assert.Single(node.Scores, score => score != null)!;
        Assert.Equal("BASIC", score.DifficultyLabel);
        Assert.True(score.ChartId > 0);
        Assert.Contains(
            (await new SongCatalogStore(ManagerDatabaseService!).GetSongsAsync())
                .SelectMany(song => song.Charts),
            chart => SongPathIdentity.CanonicalComparer.Equals(
                chart.FilePath,
                basicPath));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefinitionReadFails_ShouldProtectSubtreeChartsAndImportOtherCandidates()
    {
        await _manager.InitializeAsync();
        var setRoot = Path.Combine(_songsRoot, "Protected Set");
        Directory.CreateDirectory(setRoot);
        var setDefPath = SongPathIdentity.Normalize(
            Path.Combine(setRoot, "set.def"));
        await File.WriteAllTextAsync(
            setDefPath,
            """
            #TITLE Protected Set
            #L2LABEL ADVANCED
            #L2FILE Nested/protected.dtx
            """);
        var protectedPath = WriteChart(
            "Songs/Protected Set/Nested/protected.dtx",
            "Original Parsed Title",
            45);
        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        await using (var context = ManagerDatabaseService!.CreateContext())
        {
            var protectedChart = await context.SongCharts
                .Include(chart => chart.Song)
                .Include(chart => chart.Scores)
                .SingleAsync(chart => chart.FilePath == protectedPath);
            protectedChart.Song.Title = "Persisted Title";
            protectedChart.Song.Artist = "Persisted Artist";
            protectedChart.Song.IsBookmarked = true;
            protectedChart.DrumLevel = 64;
            var playedScore = protectedChart.Scores.Single(score =>
                score.Instrument == EInstrumentPart.DRUMS &&
                score.PlaySpeedPercent == PlaySpeedRange.Default);
            playedScore.PlayCount = 3;
            playedScore.LastPlayedAt = new DateTime(
                2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();
        }

        var original = Assert.Single(
            await ReadPersistedChartSnapshotsAsync());
        var validPath = WriteChart(
            "Songs/Other/valid.dtx",
            "Valid",
            70);
        // Second pass runs on a fresh library over the same database (HPA-801
        // constructor seams).
        var failingRead = CreateManager(readAllLinesAsync: (path, encoding, token) =>
            SongPathIdentity.CanonicalComparer.Equals(path, setDefPath)
                ? Task.FromException<string[]>(
                    new IOException("set definition unavailable"))
                : File.ReadAllLinesAsync(path, encoding, token));

        await failingRead.InitializeAsync();
        var result = await failingRead.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Contains(result.Batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(setDefPath) &&
            !error.IsRootFailure);
        Assert.Contains(
            SongPathIdentity.Normalize(protectedPath),
            result.Batch.DiscoveredChartPaths);
        Assert.DoesNotContain(result.Batch.Candidates, candidate =>
            SongPathIdentity.CanonicalComparer.Equals(
                candidate.NormalizedChartPath,
                protectedPath));
        Assert.Contains(result.Batch.Candidates, candidate =>
            SongPathIdentity.CanonicalComparer.Equals(
                candidate.NormalizedChartPath,
                validPath));
        var persisted = await ReadPersistedChartSnapshotsAsync();
        Assert.Equal(
            original,
            Assert.Single(persisted, snapshot =>
                SongPathIdentity.CanonicalComparer.Equals(
                    snapshot.FilePath,
                    protectedPath)));
        Assert.Contains(persisted, snapshot =>
            SongPathIdentity.CanonicalComparer.Equals(
                snapshot.FilePath,
                validPath));
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_ShouldParseChartsSerially()
    {
        WriteChart("Songs/Serial/one.dtx", "One", 30);
        WriteChart("Songs/Serial/two.dtx", "Two", 40);
        var inFlight = 0;
        var maximumInFlight = 0;
        _manager = CreateManager(parseSongEntitiesAsync: async path =>
        {
            var current = Interlocked.Increment(ref inFlight);
            maximumInFlight = Math.Max(maximumInFlight, current);
            await Task.Yield();
            try
            {
                return await DTXChartParser.ParseSongEntitiesAsync(path);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        });

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.True(batch.IsComplete);
        Assert.Equal(2, batch.Candidates.Count);
        Assert.Equal(1, maximumInFlight);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenRootTraversalFails_ShouldNotCallImporter()
    {
        _manager = CreateManager(
            importSongsAsync: (catalog, request, progress, token) =>
            {
                _bulkImportCalls++;
                return catalog.ImportSongsAsync(request, progress, token);
            },
            enumerateDirectories: _ =>
                throw new IOException("root unavailable"));
        await _manager.InitializeAsync();

        await Assert.ThrowsAsync<IOException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Equal(0, _bulkImportCalls);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task FinalizePendingNodes_ShouldPreserveSetAndBoxPlaceholderIdentityAndPresentation()
    {
        await _manager.InitializeAsync();
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

        var import = await new SongCatalogStore(ManagerDatabaseService!).ImportSongsAsync(
            new SongBulkImportRequest(
                batch.ActiveRoots,
                batch.DiscoveredChartPaths,
                batch.Candidates),
            progress: null,
            CancellationToken.None);
        _manager.Hierarchy.FinalizePendingNodes(batch, import.ChartsByPath);
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
    public async Task EnumerateAndRebuild_WithMultipleRoots_ShouldGroupEachRootIntoItsOwnBox()
    {
        await _manager.InitializeAsync();
        var secondRoot = Path.Combine(_testRoot, "MoreSongs");
        var emptyRoot = Path.Combine(_testRoot, "EmptySongs");
        Directory.CreateDirectory(secondRoot);
        Directory.CreateDirectory(emptyRoot);
        WriteChart("Songs/A/basic.dtx", "First Song", 20);
        WriteChart("MoreSongs/DTXFiles.Pack/B/basic.dtx", "Second Song", 30);
        var roots = new[] { _songsRoot, secondRoot, emptyRoot };

        await _manager.EnumerateAndImportSongsAsync(roots, progress: null, CancellationToken.None);
        AssertGroupedByRoot();

        // The database rebuild path must produce the same per-root grouping.
        await _manager.BuildHierarchyFromDatabaseOnceAsync(roots);
        AssertGroupedByRoot();

        void AssertGroupedByRoot()
        {
            Assert.Equal(2, _manager.RootSongs.Count);

            var firstBox = _manager.RootSongs[0];
            Assert.Equal(NodeType.Box, firstBox.Type);
            Assert.Equal("Songs", firstBox.Title);
            var firstSong = Assert.Single(firstBox.Children);
            Assert.Equal("First Song", firstSong.Title);
            Assert.Same(firstBox, firstSong.Parent);
            Assert.Equal("Songs > First Song", firstSong.BreadcrumbPath);
            Assert.True(firstBox.IsSongRoot);

            var secondBox = _manager.RootSongs[1];
            Assert.Equal(NodeType.Box, secondBox.Type);
            Assert.Equal("MoreSongs", secondBox.Title);
            Assert.True(secondBox.IsSongRoot);
            var pack = Assert.Single(secondBox.Children);
            Assert.Equal(NodeType.Box, pack.Type);
            Assert.False(pack.IsSongRoot);
            var secondSong = Assert.Single(pack.Children);
            Assert.Equal("Second Song", secondSong.Title);
            Assert.Equal("MoreSongs > DTXFiles.Pack > Second Song", secondSong.BreadcrumbPath);
        }
    }

    [Fact]
    public async Task BuildSongListFromDatabase_ShouldMatchTheEnumeratedHierarchy()
    {
        // Startup loads the cached library instead of rescanning when nothing changed,
        // so a cached launch must show exactly what the scan published. Folder names
        // and titles sort in opposite orders to catch title-ordered rebuilds. The gate
        // covers two configured roots (each boxed, headers carrying box.def skin and
        // colour), the bundled root's top-level merge, SET.def songs and nested BOXes.
        WriteChart("Songs/Zeta Folder/zeta.dtx", "Alpha Title", 10);
        WriteChart("Songs/Alpha Folder/alpha.dtx", "Zulu Title", 20);
        WriteChart("Songs/root.dtx", "Middle Root File", 30);
        WriteChart("Songs/DTXFiles.Pack/B/basic.dtx", "Grouped", 20);
        WriteChart("Songs/DTXFiles.Pack/B/extreme.dtx", "Grouped", 80);
        WriteChart("Songs/DTXFiles.Pack/A/song.dtx", "Packed", 40);
        WriteChart("Songs/Boxed/S/song.dtx", "Boxed Song", 50);
        await File.WriteAllTextAsync(
            Path.Combine(_songsRoot, "Boxed", "box.def"),
            "#TITLE: Custom Box\n#GENRE: Rock\n#SKINPATH: skins/boxed\n#TEXTCOLOR: #FF8000\n");
        WriteChart("Songs/Set Song/basic.dtx", "Set Song", 25);
        WriteChart("Songs/Set Song/extreme.dtx", "Set Song", 85);
        await File.WriteAllTextAsync(
            Path.Combine(_songsRoot, "Set Song", "set.def"),
            """
            #TITLE Set Song
            #L1LABEL BASIC
            #L1FILE basic.dtx
            #L3LABEL EXTREME
            #L3FILE extreme.dtx
            """);
        // A set.def may keep its charts in a child folder, even a BOX-shaped one.
        WriteChart("Songs/Nested Set/DTXFiles.Charts/basic.dtx", "Nested Set", 35);
        WriteChart("Songs/Nested Set/DTXFiles.Charts/extreme.dtx", "Nested Set", 95);
        await File.WriteAllTextAsync(
            Path.Combine(_songsRoot, "Nested Set", "set.def"),
            """
            #TITLE Nested Set
            #L1LABEL BASIC
            #L1FILE DTXFiles.Charts/basic.dtx
            #L3LABEL EXTREME
            #L3FILE DTXFiles.Charts/extreme.dtx
            """);
        WriteChart("Songs/Unlabeled Set/only.dtx", "Unlabeled Set", 45);
        await File.WriteAllTextAsync(
            Path.Combine(_songsRoot, "Unlabeled Set", "set.def"),
            "#TITLE Unlabeled Set\n#L3FILE only.dtx\n");
        // File names and #Ln slots that sort opposite to the drum levels.
        WriteChart("Songs/Inverted/a-hard.dtx", "Inverted", 80);
        WriteChart("Songs/Inverted/z-easy.dtx", "Inverted", 20);
        WriteChart("Songs/Inverted Set/hard.dtx", "Inverted Set", 90);
        WriteChart("Songs/Inverted Set/easy.dtx", "Inverted Set", 30);
        await File.WriteAllTextAsync(
            Path.Combine(_songsRoot, "Inverted Set", "set.def"),
            "#TITLE Inverted Set\n#L1LABEL HARD\n#L1FILE hard.dtx\n#L3LABEL EASY\n#L3FILE easy.dtx\n");
        // Gameplay plays drum charts only, so a guitar-only chart takes no slot.
        WriteChart("Songs/Mixed/drum-a.dtx", "Mixed", 30);
        WriteChart("Songs/Mixed/drum-b.dtx", "Mixed", 60);
        File.WriteAllLines(
            Path.Combine(_songsRoot, "Mixed", "guitar.dtx"),
            new[] { "#TITLE: Mixed", "#ARTIST: Fixture Artist", "#BPM: 120", "#GLEVEL: 40" });
        // A set.def that lists one file in two slots; songs.db keeps that chart once.
        WriteChart("Songs/Repeated Set/basic.dtx", "Repeated Set", 25);
        WriteChart("Songs/Repeated Set/extreme.dtx", "Repeated Set", 85);
        await File.WriteAllTextAsync(
            Path.Combine(_songsRoot, "Repeated Set", "set.def"),
            "#TITLE Repeated Set\n#L1FILE basic.dtx\n#L2FILE basic.dtx\n#L3FILE extreme.dtx\n");
        // Six charts for five slots, the easiest one last by file name.
        foreach (var (file, level) in new[] { ("a", 60), ("b", 50), ("c", 40), ("d", 30), ("e", 20), ("f", 10) })
            WriteChart($"Songs/Six Charts/{file}.dtx", "Six Charts", level);
        // A second configured root gets its own BOX header carrying its box.def
        // skin and colour, plus a nested skinned BOX of its own.
        var secondRoot = Path.Combine(_testRoot, "MoreSongs");
        Directory.CreateDirectory(Path.Combine(secondRoot, "DTXFiles.Pack"));
        await File.WriteAllTextAsync(
            Path.Combine(secondRoot, "box.def"),
            "#TITLE: More Box\n#SKINPATH: skins/more\n#TEXTCOLOR: #00AAFF\n");
        await File.WriteAllTextAsync(
            Path.Combine(secondRoot, "DTXFiles.Pack", "box.def"),
            "#TITLE: More Pack\n#GENRE: Electronica\n#SKINPATH: skins/pack\n#TEXTCOLOR: #101010\n");
        WriteChart("MoreSongs/DTXFiles.Pack/B/more.dtx", "Packed More", 55);
        // The bundled root merges into the top level without a root header.
        var bundledRoot = PrepareBundledPracticeBox();
        var roots = new[] { _songsRoot, secondRoot, bundledRoot };

        _manager = CreateManager(bundledSongsRoot: bundledRoot);
        await _manager.InitializeAsync();
        await _manager.EnumerateAndImportSongsAsync(roots, progress: null, CancellationToken.None);
        var enumerated = DescribeHierarchy(_manager.RootSongs);
        Assert.Contains(enumerated, line => line.Contains("Score 'Nested Set'"));
        Assert.Contains(enumerated, line => line.Contains("skin='skins/boxed'"));
        AssertSlotsMatchPlayedCharts(_manager.RootSongs);
        AssertBoxDefinitionPresentation(_manager.RootSongs);
        var coldSnapshot = _manager.GetLibrarySnapshot();

        // A cached launch is a new process that starts with an empty library:
        // construct a fresh library over the same database file (HPA-801).
        var cached = CreateManager(bundledSongsRoot: bundledRoot);
        await cached.InitializeAsync();
        await cached.BuildHierarchyFromDatabaseOnceAsync(roots);

        Assert.Equal(enumerated, DescribeHierarchy(cached.RootSongs));
        AssertSlotsMatchPlayedCharts(cached.RootSongs);
        AssertBoxDefinitionPresentation(cached.RootSongs);
        Assert.Equal(coldSnapshot.ActiveRoots, cached.GetLibrarySnapshot().ActiveRoots);
        Assert.Equal(_manager.EnumeratedFileCount, cached.EnumeratedFileCount);

        // Both paths must carry box.def skin, genre and colour onto root headers,
        // nested BOXes and the bundled root's placement.
        static void AssertBoxDefinitionPresentation(IReadOnlyList<SongListNode> publishedRoots)
        {
            var moreBox = Assert.Single(publishedRoots, node => node.Title == "More Box");
            Assert.True(moreBox.IsSongRoot);
            Assert.Equal("skins/more", moreBox.SkinPath);
            Assert.Equal(new Microsoft.Xna.Framework.Color(0x00, 0xAA, 0xFF), moreBox.TextColor);
            var morePack = Assert.Single(moreBox.Children);
            Assert.Equal(NodeType.Box, morePack.Type);
            Assert.Equal("More Pack", morePack.Title);
            Assert.Equal("Electronica", morePack.Genre);
            Assert.Equal("skins/pack", morePack.SkinPath);
            Assert.Equal(new Microsoft.Xna.Framework.Color(0x10, 0x10, 0x10), morePack.TextColor);
            Assert.Equal("Packed More", Assert.Single(morePack.Children).Title);
            var boxed = AllNodes(publishedRoots).Single(node => node.Title == "Custom Box");
            Assert.Equal("skins/boxed", boxed.SkinPath);

            static IEnumerable<SongListNode> AllNodes(IEnumerable<SongListNode> nodes)
            {
                foreach (var node in nodes)
                {
                    yield return node;
                    foreach (var descendant in AllNodes(node.Children))
                        yield return descendant;
                }
            }
        }

        // Each difficulty slot shows the chart that difficulty plays.
        static void AssertSlotsMatchPlayedCharts(IEnumerable<SongListNode> roots)
        {
            foreach (var (title, slots) in new[] { ("Inverted", 2), ("Inverted Set", 2), ("Mixed", 2) })
            {
                var song = FlattenScoreNodes(roots).Single(node => node.Title == title);
                Assert.Equal(slots, song.Scores.Count(score => score != null));
                for (var difficulty = 0; difficulty < slots; difficulty++)
                {
                    Assert.Equal(
                        song.GetCurrentDifficultyChart(difficulty).Id,
                        song.Scores[difficulty].ChartId);
                }
            }
        }
    }

    [Fact]
    public async Task EnumerateAndRebuild_WithOneRootAndBundledSongs_ShouldMergeBothIntoTopLevel()
    {
        var bundledRoot = PrepareBundledPracticeBox();
        _manager = CreateManager(bundledSongsRoot: bundledRoot);
        await _manager.InitializeAsync();
        WriteChart("Songs/A/basic.dtx", "First Song", 20);
        var roots = new[] { _songsRoot, bundledRoot };

        await _manager.EnumerateAndImportSongsAsync(roots, progress: null, CancellationToken.None);
        AssertMergedIntoTopLevel();

        await _manager.BuildHierarchyFromDatabaseOnceAsync(roots);
        AssertMergedIntoTopLevel();

        // A lone configured root keeps its songs at the top level, beside the bundled box.
        void AssertMergedIntoTopLevel()
        {
            Assert.Equal(2, _manager.RootSongs.Count);
            Assert.Equal("First Song", _manager.RootSongs[0].Title);
            AssertBundledPracticeBox(_manager.RootSongs[1]);
        }
    }

    [Fact]
    public async Task EnumerateAndRebuild_WithMultipleRootsAndBundledSongs_ShouldBoxOnlyConfiguredRoots()
    {
        var secondRoot = Path.Combine(_testRoot, "MoreSongs");
        var bundledRoot = PrepareBundledPracticeBox();
        _manager = CreateManager(bundledSongsRoot: bundledRoot);
        await _manager.InitializeAsync();
        WriteChart("Songs/A/basic.dtx", "First Song", 20);
        WriteChart("MoreSongs/B/basic.dtx", "Second Song", 30);
        var roots = new[] { _songsRoot, secondRoot, bundledRoot };

        await _manager.EnumerateAndImportSongsAsync(roots, progress: null, CancellationToken.None);
        AssertOnlyConfiguredRootsBoxed();

        await _manager.BuildHierarchyFromDatabaseOnceAsync(roots);
        AssertOnlyConfiguredRootsBoxed();

        void AssertOnlyConfiguredRootsBoxed()
        {
            Assert.Equal(3, _manager.RootSongs.Count);
            Assert.Equal("Songs", _manager.RootSongs[0].Title);
            Assert.True(_manager.RootSongs[0].IsSongRoot);
            Assert.Equal("MoreSongs", _manager.RootSongs[1].Title);
            Assert.True(_manager.RootSongs[1].IsSongRoot);
            AssertBundledPracticeBox(_manager.RootSongs[2]);
        }
    }

    [Fact]
    public async Task FinalizePendingNodes_WhenEveryChartInARootIsRejected_ShouldDropItsEmptyBoxes()
    {
        await _manager.InitializeAsync();
        var secondRoot = Path.Combine(_testRoot, "MoreSongs");
        Directory.CreateDirectory(secondRoot);
        WriteChart("Songs/A/basic.dtx", "Kept Song", 20);
        WriteChart("MoreSongs/DTXFiles.Pack/B/basic.dtx", "Rejected Song", 30);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _songsRoot, secondRoot }, null, CancellationToken.None);
        Assert.Equal(2, batch.RootNodes.Count);
        var import = await new SongCatalogStore(ManagerDatabaseService!).ImportSongsAsync(
            new SongBulkImportRequest(
                batch.ActiveRoots,
                batch.DiscoveredChartPaths,
                batch.Candidates),
            progress: null,
            CancellationToken.None);
        var rejectedPrefix = SongPathIdentity.Normalize(secondRoot);
        var keptCharts = import.ChartsByPath
            .Where(pair => !pair.Key.StartsWith(rejectedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        _manager.Hierarchy.FinalizePendingNodes(batch, keptCharts);

        // The rejected root's pack box and root box both end up empty and are dropped.
        var keptBox = Assert.Single(batch.RootNodes);
        Assert.Equal("Songs", keptBox.Title);
        Assert.Equal("Kept Song", Assert.Single(keptBox.Children).Title);
    }

    [Fact]
    public void DisambiguateSongRootTitles_WhenTitlesCollide_ShouldUseFullPath()
    {
        var first = Path.Combine(_testRoot, "DriveA", "Songs");
        var second = Path.Combine(_testRoot, "DriveB", "songs");
        var unique = Path.Combine(_testRoot, "Other");

        var titles = SongHierarchyBuilder.DisambiguateSongRootTitles(
            new[] { first, second, unique },
            new[] { "Songs", "songs", "Other" });

        Assert.Equal(new[] { first, second, "Other" }, titles);
    }

    [Fact]
    public void DisambiguateSongRootTitles_WhenAFallbackPathMatchesAnotherTitle_ShouldKeepTitlesUnique()
    {
        var first = Path.Combine(_testRoot, "DriveA", "Songs");
        var second = Path.Combine(_testRoot, "DriveB", "Songs");
        var third = Path.Combine(_testRoot, "Custom");

        // The third root's box.def #TITLE is the first root's full path.
        var titles = SongHierarchyBuilder.DisambiguateSongRootTitles(
            new[] { first, second, third },
            new[] { "Songs", "Songs", first });

        Assert.Equal(new[] { first, second, third }, titles);
    }

    [Fact]
    public async Task EnumerateAndRebuild_WhenRootsShareABoxDefTitle_ShouldShowFullPaths()
    {
        await _manager.InitializeAsync();
        var secondRoot = Path.Combine(_testRoot, "MoreSongs");
        Directory.CreateDirectory(secondRoot);
        await File.WriteAllTextAsync(Path.Combine(_songsRoot, "box.def"), "#TITLE: Favorites");
        await File.WriteAllTextAsync(Path.Combine(secondRoot, "box.def"), "#TITLE: Favorites");
        WriteChart("Songs/A/basic.dtx", "First Song", 20);
        WriteChart("MoreSongs/B/basic.dtx", "Second Song", 30);
        var roots = new[] { _songsRoot, secondRoot };

        await _manager.EnumerateAndImportSongsAsync(roots, progress: null, CancellationToken.None);
        AssertRootsShowFullPaths();

        // The database rebuild path must disambiguate the same way.
        await _manager.BuildHierarchyFromDatabaseOnceAsync(roots);
        AssertRootsShowFullPaths();

        void AssertRootsShowFullPaths()
        {
            Assert.Equal(2, _manager.RootSongs.Count);
            Assert.Equal(SongPathIdentity.Normalize(_songsRoot), _manager.RootSongs[0].Title);
            Assert.Equal(SongPathIdentity.Normalize(secondRoot), _manager.RootSongs[1].Title);
            Assert.Equal(
                SongPathIdentity.Normalize(_songsRoot) + " > First Song",
                Assert.Single(_manager.RootSongs[0].Children).BreadcrumbPath);
        }
    }

    [Fact]
    public async Task BuildHierarchyFromDatabaseOnceAsync_ShouldGroupOnlyBySongId()
    {
        await _manager.InitializeAsync();
        var firstPath = WriteChart("Songs/One/basic.dtx", "Duplicate", 20);
        var secondPath = WriteChart("Songs/Two/basic.dtx", "Duplicate", 40);
        var multiBasic = WriteChart("Songs/Multi/basic.dtx", "Multi", 30);
        var multiExtreme = WriteChart("Songs/Multi/extreme.dtx", "Multi", 80);
        await using (var context = ManagerDatabaseService!.CreateContext())
        {
            context.Songs.AddRange(
                CreatePersistedSong("Duplicate", firstPath, 20),
                CreatePersistedSong("Duplicate", secondPath, 40),
                CreatePersistedSong("Multi", multiBasic, 30, multiExtreme, 80));
            await context.SaveChangesAsync();
        }

        await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { _songsRoot });

        var scores = FlattenScoreNodes(_manager.RootSongs).ToArray();
        Assert.Equal(2, scores.Count(node => node.Title == "Duplicate"));
        var multi = Assert.Single(scores, node => node.Title == "Multi");
        Assert.Equal(2, multi.AvailableDifficulties);
    }

    [Fact]
    public async Task BuildHierarchyFromDatabaseOnceAsync_WithLegacySharedSongId_ShouldNotSplitIt()
    {
        await _manager.InitializeAsync();
        var firstPath = WriteChart("Songs/LegacyOne/basic.dtx", "Legacy", 20);
        var secondPath = WriteChart("Songs/LegacyTwo/extreme.dtx", "Legacy", 80);
        await using (var context = ManagerDatabaseService!.CreateContext())
        {
            context.Songs.Add(CreatePersistedSong(
                "Legacy", firstPath, 20, secondPath, 80));
            await context.SaveChangesAsync();
        }

        await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { _songsRoot });

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
        var service = new SongDatabase(
            options,
            () =>
            {
                Interlocked.Increment(ref contextCreations);
                return new SongDbContext(options);
            });
        _manager = new SongLibrary(service);

        await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { _songsRoot });

        Assert.Equal(1, contextCreations);
        await using var verification = new SongDbContext(options);
        Assert.Equal(2, await verification.SongCharts.CountAsync());
        Assert.Single(FlattenScoreNodes(_manager.RootSongs));
    }

    [Fact]
    public async Task ActiveSessionQueries_ShouldExcludeSongsOutsideCurrentSearchRoots()
    {
        await _manager.InitializeAsync();
        var activeRoot = Path.Combine(_testRoot, "Active");
        var removedRoot = Path.Combine(_testRoot, "Removed");
        var activePath = WriteChart("Active/active.dtx", "Shared Search Active", 45);
        var removedPath = WriteChart("Removed/removed.dtx", "Shared Search Removed", 65);
        await using (var context = ManagerDatabaseService!.CreateContext())
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

        await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { activeRoot });

        var bookmarks = await _manager.GetBookmarkedNodesAsync();
        var recent = await _manager.GetRecentlyPlayedNodesAsync();
        Assert.Equal(new[] { "Shared Search Active" }, bookmarks.Select(node => node.Title));
        Assert.Equal(new[] { "Shared Search Active" }, recent.Select(node => node.Title));
        Assert.All(
            bookmarks.Concat(recent),
            node => Assert.All(
                node.DatabaseSong!.Charts,
                chart => Assert.True(SongPathIdentity.IsUnderRoot(chart.FilePath, activeRoot))));
    }

    [Fact]
    public async Task GetRecentlyPlayedNodesAsync_ShouldOrderSharedSongByActiveChartHistory()
    {
        await _manager.InitializeAsync();
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
        await using (var context = ManagerDatabaseService!.CreateContext())
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
        await _manager.BuildHierarchyFromDatabaseOnceAsync(
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
    public async Task GetRecentlyPlayedNodesAsync_WhenActiveRecencyRanksAcrossPageBoundary_ShouldOrderGloballyByActiveRecency()
    {
        await _manager.InitializeAsync();
        var activeRoot = Path.Combine(_testRoot, "Active");
        var inactiveRoot = Path.Combine(_testRoot, "Removed");
        Directory.CreateDirectory(activeRoot);
        var now = DateTime.UtcNow;

        await using (var context = ManagerDatabaseService!.CreateContext())
        {
            // 64 decoys: globally recent (an inactive chart just played) but
            // actively ancient (an active chart played over a year ago). They
            // fill page one of an all-chart ordering, which is exactly one more
            // than the batch size used by the legacy paging loop.
            for (var i = 0; i < 64; i++)
            {
                var decoy = new SongEntity
                {
                    Title = $"Decoy {i}",
                    Artist = "Fixture",
                    Genre = "Fixture"
                };
                var inactiveChart = CreatePersistedChart(
                    SongPathIdentity.Normalize(
                        Path.Combine(inactiveRoot, $"d{i}/inactive.dtx")),
                    50);
                inactiveChart.Scores.Add(CreatePlayedScore(now.AddMilliseconds(-(i + 1))));
                var activeChart = CreatePersistedChart(
                    SongPathIdentity.Normalize(
                        Path.Combine(activeRoot, $"d{i}/active.dtx")),
                    50);
                activeChart.Scores.Add(CreatePlayedScore(now.AddYears(-1).AddDays(-i)));
                decoy.Charts.Add(inactiveChart);
                decoy.Charts.Add(activeChart);
                context.Songs.Add(decoy);
            }

            // Target: only an active chart, played a minute ago. Globally it
            // ranks beyond the 64-song page boundary (no recent inactive play),
            // but by active-chart recency it is the most recent of all.
            var target = new SongEntity
            {
                Title = "Target",
                Artist = "Fixture",
                Genre = "Fixture"
            };
            var targetChart = CreatePersistedChart(
                SongPathIdentity.Normalize(
                    Path.Combine(activeRoot, "target/active.dtx")),
                60);
            targetChart.Scores.Add(CreatePlayedScore(now.AddMinutes(-1)));
            target.Charts.Add(targetChart);
            context.Songs.Add(target);

            await context.SaveChangesAsync();
        }
        await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { activeRoot });

        var recent = await _manager.GetRecentlyPlayedNodesAsync(limit: 3);

        // Target has the most-recent ACTIVE play and must rank first even though
        // the decoys' inactive charts pushed it past the first all-chart page.
        // The decoys follow in active-chart recency order (Decoy 0 before Decoy
        // 1 before Decoy 2); with a limit of 3 only Target, Decoy 0, and Decoy
        // 1 are returned.
        Assert.Equal(3, recent.Count);
        Assert.Equal(
            new[] { "Target", "Decoy 0", "Decoy 1" },
            recent.Select(node => node.Title).ToArray());
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WithSetDefinition_ShouldPublishNodeWithDurableIds()
    {
        await _manager.InitializeAsync();
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

        var result = await _manager.EnumerateAndImportSongsAsync(new[] { _songsRoot });

        Assert.Equal(2, result.Batch.Candidates.Count);
        var published = Assert.Single(FlattenScoreNodes(_manager.RootSongs));
        Assert.True(published.DatabaseSongId > 0);
        Assert.All(
            published.Scores.Where(score => score != null),
            score => Assert.True(score!.ChartId > 0));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReadCancelled_ShouldThrowOperationCanceled()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/SetDef/chart.dtx", "SetDef Song", 50);
        File.WriteAllLines(Path.Combine(_songsRoot, "SetDef", "set.def"),
            ["#TITLE: SetDef Song", "#DLEVEL: 50", "1: chart.dtx"]);
        using var cts = new CancellationTokenSource();
        _manager = CreateManager(readAllLinesAsync: (_, _, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Array.Empty<string>());
        });
        await _manager.InitializeAsync();
        WriteChart("Songs/SetDef/chart.dtx", "SetDef Song", 50);
        File.WriteAllLines(Path.Combine(_songsRoot, "SetDef", "set.def"),
            ["#TITLE: SetDef Song", "#DLEVEL: 50", "1: chart.dtx"]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, cts.Token));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReadThrowsCancellation_ShouldThrowOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        // Cancel during set.def read by throwing OperationCanceledException.
        _manager = CreateManager(readAllLinesAsync: (_, _, token) =>
            throw new OperationCanceledException(token));
        await _manager.InitializeAsync();
        WriteChart("Songs/SetDef2/chart.dtx", "SetDef Song 2", 50);
        File.WriteAllLines(Path.Combine(_songsRoot, "SetDef2", "set.def"),
            ["#TITLE: SetDef Song 2", "#DLEVEL: 50", "1: chart.dtx"]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, cts.Token));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReferencesParentTraversal_ShouldRejectOutsideChart()
    {
        // Regression (review item 1): a set.def #LnFILE that escapes the
        // active-root boundary via ../ traversal must be rejected. Without
        // this, the escaped chart is counted by the filesystem inventory but
        // excluded by the database freshness count (which scopes to active
        // roots), producing a permanent count mismatch and perpetual rescans.
        await _manager.InitializeAsync();
        var setRoot = Path.Combine(_songsRoot, "Boundary Set");
        Directory.CreateDirectory(setRoot);
        // Create a chart OUTSIDE the active root (_songsRoot).
        var outsideDir = Path.Combine(_testRoot, "Outside");
        Directory.CreateDirectory(outsideDir);
        var outsideChartPath = Path.Combine(outsideDir, "outside.dtx");
        await File.WriteAllTextAsync(outsideChartPath, """
            #TITLE: Outside
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 50
            """);
        var insideChartPath = WriteChart(
            "Songs/Boundary Set/inside.dtx", "Inside", 30);
        // ../ from "Boundary Set" -> "Songs" -> ../Outside -> outside _songsRoot
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE Boundary Set
            #L1FILE inside.dtx
            #L2FILE ../../Outside/outside.dtx
            """);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        var normalizedOutside = SongPathIdentity.Normalize(outsideChartPath);
        Assert.DoesNotContain(normalizedOutside, result.Batch.DiscoveredChartPaths);
        Assert.DoesNotContain(result.Batch.Candidates, c =>
            SongPathIdentity.CanonicalComparer.Equals(
                c.NormalizedChartPath, normalizedOutside));
        // The inside chart must still be imported.
        Assert.Contains(insideChartPath, result.Batch.DiscoveredChartPaths);
        Assert.Single(result.Batch.Candidates);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReferencesAbsolutePath_ShouldRejectOutsideChart()
    {
        // Regression (review item 1): a set.def #LnFILE that uses an absolute
        // path outside the active root must be rejected. Path.Combine with an
        // absolute second argument returns the absolute path verbatim.
        await _manager.InitializeAsync();
        var setRoot = Path.Combine(_songsRoot, "Abs Set");
        Directory.CreateDirectory(setRoot);
        var outsideDir = Path.Combine(_testRoot, "AbsoluteOutside");
        Directory.CreateDirectory(outsideDir);
        var outsideChartPath = Path.Combine(outsideDir, "outside.dtx");
        await File.WriteAllTextAsync(outsideChartPath, """
            #TITLE: Absolute Outside
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 60
            """);
        var insideChartPath = WriteChart(
            "Songs/Abs Set/inside.dtx", "Inside Abs", 25);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            $"""
            #TITLE Abs Set
            #L1FILE inside.dtx
            #L2FILE {outsideChartPath}
            """);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        var normalizedOutside = SongPathIdentity.Normalize(outsideChartPath);
        Assert.DoesNotContain(normalizedOutside, result.Batch.DiscoveredChartPaths);
        Assert.DoesNotContain(result.Batch.Candidates, c =>
            SongPathIdentity.CanonicalComparer.Equals(
                c.NormalizedChartPath, normalizedOutside));
        Assert.Contains(insideChartPath, result.Batch.DiscoveredChartPaths);
        Assert.Single(result.Batch.Candidates);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenSetDefReferencesOutsideChart_ShouldNotProducePermanentMismatch()
    {
        // Regression (review item 1): after importing a set.def with an
        // outside-root reference, the inventory scanner must NOT count the
        // outside chart. Without the fix, the inventory count exceeds the
        // database count (which scopes to active roots), forcing a perpetual
        // rescan even when nothing changed.
        var songsRoot = Path.Combine(_testRoot, "MismatchSongs");
        Directory.CreateDirectory(songsRoot);
        var setRoot = Path.Combine(songsRoot, "Set");
        Directory.CreateDirectory(setRoot);
        var outsideDir = Path.Combine(_testRoot, "MismatchOutside");
        Directory.CreateDirectory(outsideDir);
        var outsideChartPath = Path.Combine(outsideDir, "outside.dtx");
        await File.WriteAllTextAsync(outsideChartPath, """
            #TITLE: Outside
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 50
            """);
        var insideChartPath = Path.Combine(setRoot, "inside.dtx");
        await File.WriteAllTextAsync(insideChartPath, """
            #TITLE: Inside
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 30
            """);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE Set
            #L1FILE inside.dtx
            #L2FILE ../../MismatchOutside/outside.dtx
            """);
        var oldTime = DateTime.UtcNow.AddMinutes(-10);
        var setDefPath = Path.Combine(setRoot, "set.def");
        File.SetLastWriteTimeUtc(insideChartPath, oldTime);
        File.SetCreationTimeUtc(insideChartPath, oldTime);
        File.SetLastWriteTimeUtc(outsideChartPath, oldTime);
        File.SetCreationTimeUtc(outsideChartPath, oldTime);
        File.SetLastWriteTimeUtc(setDefPath, oldTime);
        File.SetCreationTimeUtc(setDefPath, oldTime);
        Directory.SetLastWriteTimeUtc(setRoot, oldTime);
        Directory.SetCreationTimeUtc(setRoot, oldTime);
        Directory.SetLastWriteTimeUtc(songsRoot, oldTime);
        Directory.SetCreationTimeUtc(songsRoot, oldTime);

        await _manager.InitializeAsync();
        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { songsRoot }, null, CancellationToken.None);
        Assert.Single(result.Batch.Candidates);

        // After a successful enumeration with no subsequent file changes, the
        // freshness check must report no changes needed. Without the fix, the
        // inventory would count the outside chart (1 inside + 1 outside = 2)
        // while the database counts only the inside chart (1), producing a
        // permanent mismatch.
        var needsEnumeration = await _manager.NeedsEnumerationAsync(
            new[] { songsRoot });
        Assert.False(needsEnumeration);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReferencesSiblingWithinSameRoot_ShouldRejectEscapedChart()
    {
        // Regression (review item 1, medium): a set.def #LnFILE that escapes
        // the set.def's own directory via ../ traversal — but stays within the
        // same configured root — must be rejected. Without this, the sibling
        // chart gets a duplicate placeholder: one under the set.def's directory
        // (from the set.def reference) and one under the sibling's own
        // directory (from direct file enumeration). Both resolve to the single
        // persisted chart in the freshly published hierarchy, but on a cached
        // restart the chart is placed only by its physical path, so the
        // duplicate/misplaced node disappears — an inconsistency.
        await _manager.InitializeAsync();
        var setRoot = Path.Combine(_songsRoot, "SongA");
        Directory.CreateDirectory(setRoot);
        var siblingDir = Path.Combine(_songsRoot, "SongB");
        Directory.CreateDirectory(siblingDir);
        var siblingChartPath = Path.Combine(siblingDir, "sibling.dtx");
        await File.WriteAllTextAsync(siblingChartPath, """
            #TITLE: Sibling
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 50
            """);
        var insideChartPath = WriteChart(
            "Songs/SongA/inside.dtx", "Inside A", 30);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE SongA
            #L1FILE inside.dtx
            #L2FILE ../SongB/sibling.dtx
            """);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        var normalizedSibling = SongPathIdentity.Normalize(siblingChartPath);
        // The sibling chart must be discovered exactly once (from direct
        // enumeration of SongB, not from the set.def reference).
        Assert.Contains(normalizedSibling, result.Batch.DiscoveredChartPaths);
        Assert.Single(result.Batch.Candidates, c =>
            SongPathIdentity.CanonicalComparer.Equals(
                c.NormalizedChartPath, normalizedSibling));
        // The inside chart must also be imported.
        Assert.Contains(insideChartPath, result.Batch.DiscoveredChartPaths);
        // No duplicate: one candidate for inside + one for sibling = 2 total.
        Assert.Equal(2, result.Batch.Candidates.Count);
        // The published hierarchy must contain exactly one score node for the
        // sibling chart (no duplicate/misplaced placeholder from the set.def
        // reference).
        var siblingScoreNodes = FlattenScoreNodes(_manager.RootSongs)
            .Where(node => node.DatabaseChart != null &&
                SongPathIdentity.CanonicalComparer.Equals(
                    node.DatabaseChart.FilePath, normalizedSibling))
            .ToArray();
        Assert.Single(siblingScoreNodes);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReferencesAbsolutePathInsideRoot_ShouldRejectEscapedChart()
    {
        // Regression (review item 1, medium): a set.def #LnFILE that uses an
        // absolute path still INSIDE the configured root must be rejected.
        // Path.Combine with an absolute second argument silently overrides the
        // set.def directory, which would create a duplicate placeholder for a
        // chart that is also discovered by direct enumeration of its own
        // directory.
        await _manager.InitializeAsync();
        var setRoot = Path.Combine(_songsRoot, "AbsInnerSet");
        Directory.CreateDirectory(setRoot);
        var absSiblingDir = Path.Combine(_songsRoot, "AbsInnerSibling");
        Directory.CreateDirectory(absSiblingDir);
        var absSiblingChartPath = Path.Combine(absSiblingDir, "abs.dtx");
        await File.WriteAllTextAsync(absSiblingChartPath, """
            #TITLE: Abs Sibling
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 55
            """);
        var insideChartPath = WriteChart(
            "Songs/AbsInnerSet/inside.dtx", "Inside Abs Inner", 28);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            $"""
            #TITLE AbsInnerSet
            #L1FILE inside.dtx
            #L2FILE {absSiblingChartPath}
            """);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        var normalizedAbsSibling = SongPathIdentity.Normalize(absSiblingChartPath);
        // The absolute-path-referenced chart must be discovered exactly once
        // (from direct enumeration, not from the set.def reference).
        Assert.Contains(normalizedAbsSibling, result.Batch.DiscoveredChartPaths);
        Assert.Single(result.Batch.Candidates, c =>
            SongPathIdentity.CanonicalComparer.Equals(
                c.NormalizedChartPath, normalizedAbsSibling));
        Assert.Contains(insideChartPath, result.Batch.DiscoveredChartPaths);
        Assert.Equal(2, result.Batch.Candidates.Count);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReferencesAnotherConfiguredRoot_ShouldRejectCrossRootChart()
    {
        // Regression (review item 1, medium): a set.def #LnFILE that references
        // a chart in a DIFFERENT configured root must be rejected. Even though
        // the target chart is beneath an active root, the reference escapes the
        // set.def's own directory and would create a duplicate/misplaced
        // placeholder.
        await _manager.InitializeAsync();
        var rootA = Path.Combine(_testRoot, "RootA");
        var rootB = Path.Combine(_testRoot, "RootB");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        var setRoot = Path.Combine(rootA, "SetA");
        Directory.CreateDirectory(setRoot);
        var crossDir = Path.Combine(rootB, "SetB");
        Directory.CreateDirectory(crossDir);
        var crossChartPath = Path.Combine(crossDir, "cross.dtx");
        await File.WriteAllTextAsync(crossChartPath, """
            #TITLE: Cross
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 50
            """);
        var insideChartPath = Path.Combine(setRoot, "inside.dtx");
        await File.WriteAllTextAsync(insideChartPath, """
            #TITLE: Inside A
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 30
            """);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE SetA
            #L1FILE inside.dtx
            #L2FILE ../../RootB/SetB/cross.dtx
            """);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { rootA, rootB }, null, CancellationToken.None);

        var normalizedCross = SongPathIdentity.Normalize(crossChartPath);
        // The cross-root chart must be discovered exactly once (from direct
        // enumeration of RootB/SetB, not from the set.def reference in RootA).
        Assert.Contains(normalizedCross, result.Batch.DiscoveredChartPaths);
        Assert.Single(result.Batch.Candidates, c =>
            SongPathIdentity.CanonicalComparer.Equals(
                c.NormalizedChartPath, normalizedCross));
        var normalizedInside = SongPathIdentity.Normalize(insideChartPath);
        Assert.Contains(normalizedInside, result.Batch.DiscoveredChartPaths);
        Assert.Equal(2, result.Batch.Candidates.Count);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReferencesValidChildPath_ShouldAcceptChart()
    {
        // Regression (review item 1, medium): a set.def #LnFILE that references
        // a chart in a subdirectory of the set.def's own directory (e.g.
        // charts/basic.dtx) must be ACCEPTED. The directory-local containment
        // check permits child paths while rejecting ../, absolute, and
        // cross-root references.
        await _manager.InitializeAsync();
        var setRoot = Path.Combine(_songsRoot, "ChildSet");
        Directory.CreateDirectory(setRoot);
        var chartsDir = Path.Combine(setRoot, "charts");
        Directory.CreateDirectory(chartsDir);
        var childChartPath = Path.Combine(chartsDir, "basic.dtx");
        await File.WriteAllTextAsync(childChartPath, """
            #TITLE: Basic
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 40
            """);
        await File.WriteAllTextAsync(
            Path.Combine(setRoot, "set.def"),
            """
            #TITLE ChildSet
            #L1FILE charts/basic.dtx
            """);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        var normalizedChild = SongPathIdentity.Normalize(childChartPath);
        Assert.Contains(normalizedChild, result.Batch.DiscoveredChartPaths);
        Assert.Single(result.Batch.Candidates, c =>
            SongPathIdentity.CanonicalComparer.Equals(
                c.NormalizedChartPath, normalizedChild));
        // The chart must appear in the published hierarchy.
        Assert.Single(FlattenScoreNodes(_manager.RootSongs));
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenChartParseFails_ShouldReturnTrueToRetryDirtyRoot()
    {
        // Regression (review item 2): a recoverable parse failure leaves the
        // chart's old database row preserved (discovered but unupdated). If the
        // root's watermark advances past the file's mtime, the next freshness
        // check sees matching counts and a stale watermark → no rescan → the
        // chart's content is permanently stale. The fix suppresses the
        // watermark advance for roots with recoverable errors so the next
        // check triggers a rescan that retries the failed parse.
        var songsRoot = Path.Combine(_testRoot, "RecoverableErrorSongs");
        Directory.CreateDirectory(songsRoot);
        var chartPath = Path.Combine(songsRoot, "song.dtx");
        await File.WriteAllTextAsync(chartPath, """
            #TITLE: Recoverable
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 50
            """);
        var oldTime = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(chartPath, oldTime);
        File.SetCreationTimeUtc(chartPath, oldTime);
        Directory.SetLastWriteTimeUtc(songsRoot, oldTime);
        Directory.SetCreationTimeUtc(songsRoot, oldTime);

        await _manager.InitializeAsync();
        var canonicalRoot = _manager.RootPolicy
            .Validate(new[] { songsRoot }).CanonicalRoots.Single();

        // First enumeration: succeed so the chart is in the database and the
        // root's per-root watermark is established.
        var firstResult = await _manager.EnumerateAndImportSongsAsync(
            new[] { songsRoot }, null, CancellationToken.None);
        Assert.Single(firstResult.Batch.Candidates);
        var firstWatermark = await new SongCatalogStore(ManagerDatabaseService!)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalRoot);
        Assert.NotNull(firstWatermark);

        // Simulate a transient parse failure on the next enumeration with a
        // fresh library over the same database (HPA-801: parser is a
        // constructor seam, so the dirty pass gets its own instance).
        var dirty = CreateManager(parseSongEntitiesAsync: path =>
            SongPathIdentity.CanonicalComparer.Equals(path, SongPathIdentity.Normalize(chartPath))
                ? Task.FromException<(SongEntity, SongChart)>(
                    new InvalidDataException($"transient parse failure for {path}"))
                : DTXChartParser.ParseSongEntitiesAsync(path));

        // Second enumeration: the parse fails, producing a recoverable error.
        // The chart's old DB row is preserved (discovered but unupdated).
        await dirty.InitializeAsync();
        var secondResult = await dirty.EnumerateAndImportSongsAsync(
            new[] { songsRoot }, null, CancellationToken.None);
        Assert.Contains(secondResult.Batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(chartPath) &&
            !error.IsRootFailure);

        // The root's per-root watermark must NOT have advanced. Without the
        // fix, it would advance to the second enumeration's backed-off start,
        // silently accepting the stale chart content.
        var secondWatermark = await new SongCatalogStore(dirty.Database)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalRoot);
        Assert.Equal(firstWatermark!.Value.ToUniversalTime(),
            secondWatermark!.Value.ToUniversalTime());

        // Set the file's mtime to just after the first watermark so the
        // freshness check detects the change and triggers a retry.
        var editTime = firstWatermark.Value.AddSeconds(10);
        File.SetLastWriteTimeUtc(chartPath, editTime);
        var needsEnumeration = await dirty.NeedsEnumerationAsync(
            new[] { songsRoot });
        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenTabHasNoDrumTrack_ShouldLoadFromCacheUntilTheTabChanges()
    {
        // HPA-693: a guitar-only tab is skipped by the scan. That is a stable
        // outcome, so an unchanged restart must still load from cache, while
        // editing the tab to add drums must still be picked up.
        var songsRoot = Path.Combine(_testRoot, "TabSongs");
        var chartPath = Path.Combine(songsRoot, "Drums", "song.dtx");
        var tabPath = Path.Combine(songsRoot, "Guitar", "guitar.musicxml");
        Directory.CreateDirectory(Path.GetDirectoryName(chartPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(tabPath)!);
        await File.WriteAllTextAsync(chartPath, """
            #TITLE: Drum Song
            #BPM: 120
            #DLEVEL: 50
            #00113: 01
            """);
        await File.WriteAllTextAsync(tabPath, MusicXmlTab(withDrums: false));
        var oldTime = DateTime.UtcNow.AddMinutes(-10);
        foreach (var path in new[] { chartPath, tabPath })
        {
            File.SetLastWriteTimeUtc(path, oldTime);
            File.SetCreationTimeUtc(path, oldTime);
        }
        foreach (var directory in new[] { songsRoot, Path.GetDirectoryName(chartPath)!, Path.GetDirectoryName(tabPath)! })
        {
            Directory.SetLastWriteTimeUtc(directory, oldTime);
            Directory.SetCreationTimeUtc(directory, oldTime);
        }

        await _manager.InitializeAsync();
        var canonicalRoot = _manager.RootPolicy
            .Validate(new[] { songsRoot }).CanonicalRoots.Single();

        var firstScan = await _manager.EnumerateAndImportSongsAsync(
            new[] { songsRoot }, null, CancellationToken.None);
        Assert.Single(firstScan.Batch.Candidates);
        Assert.Empty(firstScan.Batch.Errors);
        Assert.Single(FlattenScoreNodes(_manager.RootSongs));
        Assert.False(await _manager.NeedsEnumerationAsync(new[] { songsRoot }));

        // Editing the tab to add drums is a change the next startup must scan.
        var watermark = await new SongCatalogStore(ManagerDatabaseService!)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalRoot);
        await File.WriteAllTextAsync(tabPath, MusicXmlTab(withDrums: true));
        File.SetLastWriteTimeUtc(tabPath, watermark!.Value.AddSeconds(10));
        Assert.True(await _manager.NeedsEnumerationAsync(new[] { songsRoot }));

        // Back-date the edit so the restart after the rescan sees no newer file.
        File.SetLastWriteTimeUtc(tabPath, oldTime);
        var secondScan = await _manager.EnumerateAndImportSongsAsync(
            new[] { songsRoot }, null, CancellationToken.None);
        Assert.Equal(2, secondScan.Batch.Candidates.Count);
        Assert.Equal(2, FlattenScoreNodes(_manager.RootSongs).Count());
        Assert.False(await _manager.NeedsEnumerationAsync(new[] { songsRoot }));
    }

    /// <summary>
    /// A one-bar MusicXML tab with a guitar part and, optionally, a drum part
    /// holding one kick (midi-unpitched is 1-based, so 37 = GM 36).
    /// </summary>
    private static string MusicXmlTab(bool withDrums)
    {
        const string attributes =
            "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>";
        var drumPartList = withDrums
            ? "<score-part id=\"P2\"><part-name>Drumset</part-name>" +
              "<score-instrument id=\"P2-I37\"><instrument-name>Kick</instrument-name></score-instrument>" +
              "<midi-instrument id=\"P2-I37\"><midi-channel>10</midi-channel><midi-unpitched>37</midi-unpitched></midi-instrument>" +
              "</score-part>"
            : "";
        var drumPart = withDrums
            ? "<part id=\"P2\"><measure number=\"1\">" + attributes + "<clef><sign>percussion</sign></clef></attributes>" +
              "<note><unpitched><display-step>F</display-step><display-octave>4</display-octave></unpitched>" +
              "<duration>4</duration><instrument id=\"P2-I37\"/><type>whole</type></note></measure></part>"
            : "";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><score-partwise version=\"4.0\">" +
            "<work><work-title>Tab Song</work-title></work><part-list>" +
            "<score-part id=\"P1\"><part-name>Guitar</part-name></score-part>" + drumPartList +
            "</part-list><part id=\"P1\"><measure number=\"1\">" + attributes +
            "<clef><sign>G</sign><line>2</line></clef></attributes>" +
            "<note><pitch><step>E</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note>" +
            "</measure></part>" + drumPart + "</score-partwise>";
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenRecoverableErrorInOneRoot_ShouldStillAdvanceCleanRoot()
    {
        // Regression (review item 2): when one root has a recoverable error
        // and another is clean, only the dirty root's watermark is suppressed.
        // The clean root's watermark advances so it does not needlessly rescan.
        var rootA = Path.Combine(_testRoot, "CleanRoot");
        var rootB = Path.Combine(_testRoot, "DirtyRoot");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        var chartA = Path.Combine(rootA, "clean.dtx");
        var chartB = Path.Combine(rootB, "dirty.dtx");
        await File.WriteAllTextAsync(chartA, """
            #TITLE: Clean
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 40
            """);
        await File.WriteAllTextAsync(chartB, """
            #TITLE: Dirty
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 60
            """);
        var oldTime = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(chartA, oldTime);
        File.SetCreationTimeUtc(chartA, oldTime);
        File.SetLastWriteTimeUtc(chartB, oldTime);
        File.SetCreationTimeUtc(chartB, oldTime);
        Directory.SetLastWriteTimeUtc(rootA, oldTime);
        Directory.SetCreationTimeUtc(rootA, oldTime);
        Directory.SetLastWriteTimeUtc(rootB, oldTime);
        Directory.SetCreationTimeUtc(rootB, oldTime);

        await _manager.InitializeAsync();
        var canonicalA = _manager.RootPolicy
            .Validate(new[] { rootA }).CanonicalRoots.Single();
        var canonicalB = _manager.RootPolicy
            .Validate(new[] { rootB }).CanonicalRoots.Single();

        // First enumeration: both charts succeed, both watermarks established.
        await _manager.EnumerateAndImportSongsAsync(
            new[] { rootA, rootB }, null, CancellationToken.None);
        var firstWatermarkA = await new SongCatalogStore(ManagerDatabaseService!)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA);
        var firstWatermarkB = await new SongCatalogStore(ManagerDatabaseService!)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalB);
        Assert.NotNull(firstWatermarkA);
        Assert.NotNull(firstWatermarkB);

        // Second enumeration runs on a fresh library over the same database
        // (HPA-801: parser is a constructor seam); chartB parse fails
        // (recoverable error in rootB).
        var dirty = CreateManager(parseSongEntitiesAsync: path =>
            SongPathIdentity.CanonicalComparer.Equals(path, SongPathIdentity.Normalize(chartB))
                ? Task.FromException<(SongEntity, SongChart)>(
                    new InvalidDataException($"transient parse failure for {path}"))
                : DTXChartParser.ParseSongEntitiesAsync(path));
        await dirty.InitializeAsync();
        await dirty.EnumerateAndImportSongsAsync(
            new[] { rootA, rootB }, null, CancellationToken.None);

        // rootA's watermark advanced (clean), rootB's did not (dirty).
        var secondWatermarkA = await new SongCatalogStore(dirty.Database)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA);
        var secondWatermarkB = await new SongCatalogStore(dirty.Database)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalB);
        Assert.NotEqual(firstWatermarkA!.Value.ToUniversalTime(),
            secondWatermarkA!.Value.ToUniversalTime());
        Assert.Equal(firstWatermarkB!.Value.ToUniversalTime(),
            secondWatermarkB!.Value.ToUniversalTime());

        // Checking only rootA should report no changes needed (clean).
        var needsA = await dirty.NeedsEnumerationAsync(new[] { rootA });
        Assert.False(needsA);

        // Checking only rootB should report changes needed (dirty watermark).
        // Set chartB's mtime to just after the first watermark to ensure the
        // change is detectable.
        File.SetLastWriteTimeUtc(chartB, firstWatermarkB.Value.AddSeconds(10));
        var needsB = await dirty.NeedsEnumerationAsync(new[] { rootB });
        Assert.True(needsB);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenLegacyGlobalWatermarkOnlyAndRootLacksPerRootWatermark_ShouldReturnTrue()
    {
        // Regression (review item 2, migration case): a database created
        // before per-root watermarks only has the global timestamp. When an
        // enumeration gives root A a recoverable parse error while root B
        // succeeds, B receives a per-root watermark and the global timestamp
        // advances, but A does NOT. On the next startup with B unavailable,
        // A has no per-root watermark. The legacy fallback (now removed) would
        // assign B's advanced global timestamp to A, and because A's retained
        // rows and file mtimes are older than that timestamp,
        // NeedsEnumerationAsync would return false — never retrying A's failed
        // parse. A missing per-root watermark must always mean the root needs
        // enumeration.
        var rootA = Path.Combine(_testRoot, "LegacyRootA");
        var rootB = Path.Combine(_testRoot, "LegacyRootB");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        var chartA = Path.Combine(rootA, "song.dtx");
        var chartB = Path.Combine(rootB, "song.dtx");
        await File.WriteAllTextAsync(chartA, """
            #TITLE: Legacy A
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 50
            """);
        await File.WriteAllTextAsync(chartB, """
            #TITLE: Legacy B
            #ARTIST: Fixture Artist
            #BPM: 120
            #DLEVEL: 40
            """);
        var oldTime = DateTime.UtcNow.AddMinutes(-30);
        File.SetLastWriteTimeUtc(chartA, oldTime);
        File.SetCreationTimeUtc(chartA, oldTime);
        File.SetLastWriteTimeUtc(chartB, oldTime);
        File.SetCreationTimeUtc(chartB, oldTime);
        Directory.SetLastWriteTimeUtc(rootA, oldTime);
        Directory.SetCreationTimeUtc(rootA, oldTime);
        Directory.SetLastWriteTimeUtc(rootB, oldTime);
        Directory.SetCreationTimeUtc(rootB, oldTime);

        await _manager.InitializeAsync();

        // First enumeration: both roots succeed so the database holds rows
        // for A and B and both per-root watermarks are established.
        var firstResult = await _manager.EnumerateAndImportSongsAsync(
            new[] { rootA, rootB }, null, CancellationToken.None);
        Assert.Equal(2, firstResult.Batch.Candidates.Count);

        // Simulate a legacy/migration database: delete every per-root
        // watermark so only the global timestamp remains, then age it so the
        // second enumeration's scan-start watermark is newer.
        await using (var context = ManagerDatabaseService!.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM __EnumerationMetadata WHERE Key LIKE {0}",
                SongCatalogStore.LastSuccessfulEnumerationRootKeyPrefix + "%");
        }
        var legacyGlobal = DateTime.UtcNow.AddMinutes(-20);
        await new SongCatalogStore(ManagerDatabaseService!)
            .SetLastSuccessfulEnumerationUtcAsync(legacyGlobal);
        // Confirm the legacy state: global watermark present, no per-root rows.
        Assert.NotNull(await new SongCatalogStore(ManagerDatabaseService!)
            .GetLastSuccessfulEnumerationUtcAsync());
        var canonicalA = _manager.RootPolicy
            .Validate(new[] { rootA }).CanonicalRoots.Single();
        var canonicalB = _manager.RootPolicy
            .Validate(new[] { rootB }).CanonicalRoots.Single();
        Assert.Null(await new SongCatalogStore(ManagerDatabaseService!)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA));
        Assert.Null(await new SongCatalogStore(ManagerDatabaseService!)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalB));

        // Second enumeration: chartA parse fails (recoverable error in A),
        // chartB succeeds. B receives a per-root watermark and the global
        // watermark advances; A does NOT receive a per-root watermark.
        // Second enumeration runs on a fresh library over the same database
        // (HPA-801: parser is a constructor seam); chartA parse fails
        // (recoverable error in A), chartB succeeds.
        var dirty = CreateManager(parseSongEntitiesAsync: path =>
            SongPathIdentity.CanonicalComparer.Equals(path, SongPathIdentity.Normalize(chartA))
                ? Task.FromException<(SongEntity, SongChart)>(
                    new InvalidDataException($"transient parse failure for {path}"))
                : DTXChartParser.ParseSongEntitiesAsync(path));
        await dirty.InitializeAsync();
        var secondResult = await dirty.EnumerateAndImportSongsAsync(
            new[] { rootA, rootB }, null, CancellationToken.None);
        Assert.Contains(secondResult.Batch.Errors, error =>
            error.Path == SongPathIdentity.Normalize(chartA) &&
            !error.IsRootFailure);

        // B has a per-root watermark now; A still does not.
        Assert.Null(await new SongCatalogStore(dirty.Database)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA));
        Assert.NotNull(await new SongCatalogStore(dirty.Database)
            .GetLastSuccessfulEnumerationUtcAsync(canonicalB));
        // The global watermark advanced past A's file mtime.
        var advancedGlobal = await new SongCatalogStore(dirty.Database)
            .GetLastSuccessfulEnumerationUtcAsync();
        Assert.True(advancedGlobal!.Value > oldTime);

        // Make B unavailable (external drive detached / share unmounted).
        Directory.Delete(rootB, recursive: true);

        // The freshness check must return true because A has no per-root
        // watermark. Without the fix, the legacy fallback would assign the
        // advanced global timestamp to A, and A's matching row count plus
        // older file mtimes would produce false — never retrying the failed
        // parse.
        var needsEnumeration = await dirty.NeedsEnumerationAsync(
            new[] { rootA, rootB });
        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenChartParseCancelled_ShouldThrowOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var parseCallCount = 0;
        _manager = CreateManager(parseSongEntitiesAsync: path =>
        {
            parseCallCount++;
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
            return Task.FromResult<(SongEntity, SongChart)>((new SongEntity
            {
                Title = "Cancel Song",
                Artist = "Artist",
                Genre = "Genre"
            }, new SongChart
            {
                FilePath = path,
                DrumLevel = 50,
                HasDrumChart = true,
                DifficultyLevel = 50
            }));
        });
        await _manager.InitializeAsync();
        WriteChart("Songs/Cancel/chart.dtx", "Cancel Song", 50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, cts.Token));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenFileEnumerationCancelled_ShouldThrowOperationCanceled()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/BoxDir/chart.dtx", "Box Song", 50);
        var boxDir = Path.Combine(_songsRoot, "BoxDir");
        File.WriteAllLines(Path.Combine(boxDir, "box.def"),
            ["#TITLE: Box Title", "#GENRE: Test"]);
        using var cts = new CancellationTokenSource();
        // Cancel before the box.def read happens by triggering cancellation
        // during the chart file enumeration phase (before box.def is read).
        var fileCallCount = 0;
        _manager = CreateManager(enumerateFiles: _ =>
        {
            fileCallCount++;
            if (fileCallCount > 1)
                cts.Cancel();
            return new[] { Path.Combine(boxDir, "chart.dtx") };
        });
        await _manager.InitializeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, cts.Token));
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WhenRootIsBlank_ShouldSkipAndRecordRootFailure()
    {
        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { "  " }, null, CancellationToken.None);

        Assert.Empty(batch.Candidates);
        var rootError = Assert.Single(batch.Errors, e => e.IsRootFailure);
        Assert.Contains("blank", rootError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WhenRootDoesNotExist_ShouldSkipAndRecordRootFailure()
    {
        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { Path.Combine(_testRoot, "Nonexistent") },
            null,
            CancellationToken.None);

        Assert.Empty(batch.Candidates);
        var rootError = Assert.Single(batch.Errors, e => e.IsRootFailure);
        Assert.Contains("does not exist", rootError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
        public async Task BuildEnumerationBatchAsync_ShouldUseRootPolicyToDeduplicateActiveRoots()
        {
            var root = Path.Combine(_testRoot, "PolicyRoot");
            Directory.CreateDirectory(root);
            _manager = CreateManager(rootPolicy: new SongRootPolicy(
                SongRootPolicy.CreateComparer(ignoreCase: true)));

            var batch = await _manager.BuildEnumerationBatchAsync(
                new[] { root, root.ToUpperInvariant() },
                null,
                CancellationToken.None);

            Assert.Equal([Path.GetFullPath(root)], batch.ActiveRoots);
            Assert.Contains(batch.Errors, error =>
                error.IsRootFailure &&
                error.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenBatchIsIncomplete_ShouldThrowInvalidOperationException()
    {
        // Inject an incomplete batch directly via the build seam so the import
        // guard (IsComplete == false) is exercised independently of cancellation.
        var incompleteBatch = new SongEnumerationBatch
        {
            ActiveRoots = new[] { _songsRoot },
            DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal),
            Candidates = new List<SongImportCandidate>(),
            RootNodes = new List<SongListNode>(),
            PendingSongs = new List<PendingSongNode>(),
            Errors = new List<SongEnumerationError>(),
            IsComplete = false
        };
        _manager = CreateManager(buildEnumerationBatchAsync: (_, _, _) =>
            Task.FromResult(incompleteBatch));
        await _manager.InitializeAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Contains("incomplete enumeration", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenCompleteBatchHasNoActiveRoots_ShouldNotImportOrPublish()
    {
        await SeedPublishedLibraryAsync();
        var importCalls = 0;
        var publishEvents = 0;
        // The accepted-empty-roots scan runs on a fresh library over the same
        // database (HPA-801: seams are constructor-only), warm-published from
        // the seeded rows so the retained-snapshot assertions stay meaningful.
        var acceptedEmpty = CreateManager(
            importSongsAsync: (_, _, _, _) =>
            {
                importCalls++;
                return Task.FromResult(SongBulkImportResult.Empty);
            },
            buildEnumerationBatchAsync: (_, _, _) =>
                Task.FromResult(new SongEnumerationBatch
                {
                    ActiveRoots = Array.Empty<string>(),
                    DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal),
                    Candidates = new List<SongImportCandidate>(),
                    RootNodes = new List<SongListNode>(),
                    PendingSongs = new List<PendingSongNode>(),
                    Errors = new List<SongEnumerationError>(),
                    IsComplete = true,
                }));
        await acceptedEmpty.InitializeAsync();
        await acceptedEmpty.BuildHierarchyFromDatabaseOnceAsync(new[] { _songsRoot });
        // Subscribe after the warm rebuild so publishEvents counts only the test scan.
        acceptedEmpty.SongLibraryPublished += (_, _) => publishEvents++;
        var before = acceptedEmpty.GetLibrarySnapshot();

        var result = await acceptedEmpty.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        var after = acceptedEmpty.GetLibrarySnapshot();

        Assert.Equal(SongEnumerationOutcome.NoActiveRoots, result.Outcome);
        Assert.Same(SongBulkImportResult.Empty, result.Import);
        Assert.Equal(0, importCalls);
        Assert.Equal(0, publishEvents);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.RootSongs, after.RootSongs);
        Assert.Equal(before.ActiveRoots, after.ActiveRoots);

        // The busy slot must be released on this instance (HPA-801 fix round:
        // the old assertion inspected _manager, which only seeded the database
        // and never scans here, so the NoActiveRoots branch could leak its slot
        // undetected). A second scan must therefore acquire the slot and
        // complete instead of throwing SongEnumerationBusyException.
        var secondScan = await acceptedEmpty.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        Assert.Equal(SongEnumerationOutcome.NoActiveRoots, secondScan.Outcome);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenDatabaseInitializedButFaulted_ShouldReachNoActiveRootsWithoutQuerying()
    {
        // The scan's readiness guard is an in-memory initialization-state check
        // (the pre-rename null-reference throw), not a SQL probe: root
        // validation and traversal happen before any database I/O, so a scan
        // without usable roots must yield NoActiveRoots even when the
        // initialized database faults on every context creation.
        var batchBuilds = 0;
        var manager = new SongLibrary(
            new SongDatabase(
                new DbContextOptionsBuilder<SongDbContext>()
                    .UseSqlite("Data Source=:memory:")
                    .Options,
                () => throw new InvalidOperationException("database unavailable"),
                initialized: true),
            buildEnumerationBatchAsync: (_, _, _) =>
            {
                batchBuilds++;
                return Task.FromResult(CompletedBatchWithNoActiveRoots());
            });

        var result = await manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(SongEnumerationOutcome.NoActiveRoots, result.Outcome);
        Assert.Equal(1, batchBuilds);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenDatabaseNeverInitialized_ShouldThrowReadinessErrorBeforeTraversal()
    {
        // Pre-rename contract: the readiness guard fails with the original
        // "Song database is not initialized." error before the filesystem is
        // touched, releasing the busy slot via the enumeration finally.
        var batchBuilds = 0;
        var manager = new SongLibrary(
            new SongDatabase(_databasePath),
            buildEnumerationBatchAsync: (_, _, _) =>
            {
                batchBuilds++;
                return Task.FromResult(CompletedBatchWithNoActiveRoots());
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Contains(
            "not initialized",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, batchBuilds);
        Assert.Null(ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
            manager, "_enumCancellation"));
    }

    [Fact]
    public async Task Dispose_WhileEnumerationInFlight_ShouldLeaveSourceDisposalToTheEnumerationFinally()
    {
        // Dispose must synchronize cancellation with the enumeration gate and
        // leave source disposal to the enumeration's finally: cancelling a
        // source that EndEnumeration concurrently clears and disposes throws
        // ObjectDisposedException and would skip the remaining BaseGame
        // cleanup during shutdown.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new SongLibrary(
            new SongDatabase(_databasePath),
            importSongsAsync: async (_, _, _, token) =>
            {
                entered.SetResult();
                await release.Task;
                token.ThrowIfCancellationRequested();
                return SongBulkImportResult.Empty;
            },
            buildEnumerationBatchAsync: (_, _, _) =>
                Task.FromResult(CompletedBatchWithActiveRoot(_songsRoot)));
        await manager.InitializeAsync();

        var scan = manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        manager.Dispose();

        // Dispose cancels but does not mutate the slot: the enumeration still
        // owns the source until its finally runs.
        Assert.NotNull(ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
            manager, "_enumCancellation"));

        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.Null(ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
            manager, "_enumCancellation"));
    }

    [Fact]
    public async Task RefreshSongListFromDatabaseAsync_WhenCurrentRootBecomesUnavailable_ShouldPreservePublishedSnapshot()
    {
        await SeedPublishedLibraryAsync();
        var before = _manager.GetLibrarySnapshot();
        var publishEvents = 0;
        _manager.SongLibraryPublished += (_, _) => publishEvents++;
        Directory.Delete(_songsRoot, recursive: true);

        await _manager.RefreshSongListFromDatabaseAsync();

        var after = _manager.GetLibrarySnapshot();
        Assert.Equal(0, publishEvents);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.RootSongs, after.RootSongs);
        Assert.Equal(before.ActiveRoots, after.ActiveRoots);
        Assert.Equal(before.EnumeratedFileCount, after.EnumeratedFileCount);
        Assert.Equal(before.DiscoveredScoreCount, after.DiscoveredScoreCount);
    }

    [Fact]
    public async Task RefreshSongListFromDatabaseAsync_WhenDatabaseNotInitialized_ShouldReturnEarly()
    {
        // Without InitializeDatabaseServiceAsync, DatabaseService is null.
        // Should not throw.
        await _manager.RefreshSongListFromDatabaseAsync();
    }

    [Fact]
    public async Task FinalizePendingNodes_WhenChartPathMissing_ShouldNullScoreAndRemovePlaceholder()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/Finalize/chart.dtx", "Finalize Song", 50);

        // Build the batch without finalizing so the placeholder still has its
        // pre-resolution score slots, then finalize with an empty charts map to
        // exercise the "chart not found" branch for every pending chart path.
        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        var pending = Assert.Single(batch.PendingSongs);
        var placeholder = pending.Placeholder;
        var chartPathCount = pending.OrderedChartPaths.Count;
        Assert.InRange(chartPathCount, 1, placeholder.Scores.Length);
        Assert.Contains(placeholder, batch.RootNodes);

        var emptyCharts = new Dictionary<string, SongChart>();
        _manager.Hierarchy.FinalizePendingNodes(batch, emptyCharts);

        // Each missing chart path must have its placeholder score slot nulled.
        for (var index = 0; index < chartPathCount; index++)
            Assert.Null(placeholder.Scores[index]);

        // With no resolved charts, the placeholder is removed from the batch.
        Assert.DoesNotContain(placeholder, batch.RootNodes);
    }

    [Fact]
    public async Task PublishEnumeration_WhenCalled_ShouldUpdateRootSongsAndCounts()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/Publish/chart.dtx", "Publish Song", 50);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        // PublishEnumeration was called internally; verify state was set.
        Assert.Equal(1, _manager.EnumeratedFileCount);
        Assert.Equal(1, _manager.DiscoveredScoreCount);
        Assert.NotEmpty(_manager.RootSongs);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenCancellationArrivesAfterCommit_ShouldStillFinalizeAndPublish()
    {
        using var cancellation = new CancellationTokenSource();
        // The default importer returns only after SaveChanges/transaction
        // commit. This models a Config deactivation that races immediately
        // afterward: finalization/publication must still complete (HPA-801:
        // the wrapper is a constructor seam on a test-owned instance).
        _manager = CreateManager(importSongsAsync: async (catalog, request, progress, token) =>
        {
            var import = await catalog.ImportSongsAsync(request, progress, token);
            cancellation.Cancel();
            return import;
        });
        await _manager.InitializeAsync();
        WriteChart("Songs/PostCommit/chart.dtx", "Post Commit", 50);
        var published = 0;
        _manager.SongLibraryPublished += (_, _) => published++;

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(SongEnumerationOutcome.ImportedAndPublished, result.Outcome);
        Assert.Equal(1, published);
        Assert.NotEmpty(_manager.RootSongs);
        Assert.Equal(1, _manager.DiscoveredScoreCount);
    }

    [Theory]
    [InlineData(SongEnumerationPostCommitPhase.Finalization)]
    [InlineData(SongEnumerationPostCommitPhase.Publication)]
    public async Task EnumerateAndImportSongsAsync_WhenPostCommitPhaseFails_ShouldThrowPhaseAwareException(
        SongEnumerationPostCommitPhase expectedPhase)
    {
        await SeedPublishedLibraryAsync();
        WriteChart("Songs/PostCommitFailure/new.dtx", "Committed New", 50);
        var failure = new InvalidOperationException("post-commit phase failed");

        // The failing post-commit scan runs on a fresh library over the same
        // database (HPA-801: seams are constructor-only), warm-published from
        // the seeded rows so the retained-snapshot assertions stay meaningful.
        SongLibrary failing;
        if (expectedPhase == SongEnumerationPostCommitPhase.Finalization)
        {
            failing = CreateManager(finalizePendingNodes: (_, _) => throw failure);
        }
        else
        {
            failing = CreateManager(publishEnumeration: _ => throw failure);
        }
        await failing.InitializeAsync();
        await failing.BuildHierarchyFromDatabaseOnceAsync(new[] { _songsRoot });
        var before = failing.GetLibrarySnapshot();

        var exception = await Assert.ThrowsAsync<SongEnumerationPostCommitException>(() =>
            failing.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

        Assert.Equal(expectedPhase, exception.Phase);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal(2, exception.Batch.Candidates.Count);
        Assert.Equal(_seededChartCount + 1, await CountChartsAsync());
        var after = failing.GetLibrarySnapshot();
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.RootSongs, after.RootSongs);
    }

    [Fact]
    public async Task GetRecentlyPlayedNodesAsync_WithMoreThanLimit_ShouldReturnLimitInRecencyOrder()
    {
        await _manager.InitializeAsync();
        // Seed multiple songs with play history.
        for (var i = 0; i < 5; i++)
        {
            WriteChart($"Songs/Recent/{i}/chart.dtx", $"Recent {i}", 50);
        }
        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        // Assign play history deterministically by title so the expected recency
        // ordering does not depend on chart enumeration/return order: "Recent 0"
        // is the most recently played, "Recent 4" the oldest.
        await using var context = ManagerDatabaseService!.CreateContext();
        var songs = await context.Songs
            .Include(s => s.Charts).ThenInclude(c => c.Scores)
            .ToListAsync();
        foreach (var song in songs)
        {
            var titleNumber = int.Parse(
                song.Title.Substring("Recent ".Length),
                CultureInfo.InvariantCulture);
            var lastPlayed = DateTime.UtcNow.AddDays(-titleNumber);
            foreach (var chart in song.Charts)
            {
                var score = chart.Scores.FirstOrDefault();
                if (score != null)
                {
                    score.LastPlayedAt = lastPlayed;
                    score.PlayCount = 1;
                }
                else
                {
                    chart.Scores.Add(new SongScore
                    {
                        Instrument = EInstrumentPart.DRUMS,
                        PlaySpeedPercent = PlaySpeedRange.Default,
                        LastPlayedAt = lastPlayed,
                        PlayCount = 1
                    });
                }
            }
        }
        await context.SaveChangesAsync();

        var recent = await _manager.GetRecentlyPlayedNodesAsync(limit: 3);

        // Exactly the limit, in newest-first recency order.
        Assert.Equal(3, recent.Count);
        Assert.Equal(
            new[] { "Recent 0", "Recent 1", "Recent 2" },
            recent.Select(node => node.Title).ToArray());
    }

    [Fact]
    public async Task CreatePersistenceProgressAdapter_WhenInvoked_ShouldMapAllMilestones()
    {
        await _manager.InitializeAsync();
        WriteChart("Songs/Adapter/chart.dtx", "Adapter Song", 50);

        var messages = new List<string>();
        IProgress<EnumerationProgress>? capturedProgress = null;
        var importCallCount = 0;
        _manager = CreateManager(importSongsAsync: async (catalog, request, progress, token) =>
        {
            importCallCount++;
            // The adapter is passed as progress; report through it.
            progress?.Report(new SongBulkImportProgress(
                SongBulkImportMilestone.PreloadStarted, 0, 1));
            progress?.Report(new SongBulkImportProgress(
                SongBulkImportMilestone.MatchingCompleted, 1, 1));
            progress?.Report(new SongBulkImportProgress(
                SongBulkImportMilestone.MutationsStaged, 1, 1));
            progress?.Report(new SongBulkImportProgress(
                SongBulkImportMilestone.CleanupCompleted, 1, 1));
            progress?.Report(new SongBulkImportProgress(
                SongBulkImportMilestone.SaveStarted, 1, 1));
            progress?.Report(new SongBulkImportProgress(
                SongBulkImportMilestone.Committed, 1, 1));
            return await catalog.ImportSongsAsync(request, progress, token);
        });
        await _manager.InitializeAsync();
        WriteChart("Songs/Adapter/chart.dtx", "Adapter Song", 50);

        var progress = new InlineProgress<EnumerationProgress>(p =>
        {
            if (p.CurrentOperation != null)
                messages.Add(p.CurrentOperation);
        });

        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, progress, CancellationToken.None);

        // Verify that milestone messages were mapped.
        Assert.Contains("Loading existing songs", messages);
        Assert.Contains("Matching charts", messages);
        Assert.Contains("Preparing changes", messages);
        Assert.Contains("Removing stale records", messages);
        Assert.Contains("Saving songs", messages);
        Assert.Contains("Song database committed", messages);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Ignore errors during cleanup (e.g. lingering SQLite file locks)
            }
        }
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
        return SongPathIdentity.Normalize(path);
    }

    /// <summary>
    /// One line per node, depth-first in display order, covering what the song list
    /// shows: kind, title, BOX styling, breadcrumb and every difficulty slot.
    /// </summary>
    private static List<string> DescribeHierarchy(IEnumerable<SongListNode> nodes, int depth = 0)
    {
        var lines = new List<string>();
        foreach (var node in nodes)
        {
            var slots = string.Join(", ", node.Scores
                .Select((score, index) => (score, index))
                .Where(slot => slot.score != null)
                .Select(slot =>
                    $"{slot.index}:{slot.score!.ChartId}/{slot.score.DifficultyLevel}/" +
                    $"{slot.score.DifficultyLabel}/{node.DifficultyLabels[slot.index]}"));
            lines.Add(
                $"{new string(' ', depth * 2)}{node.Type} '{node.Title}' genre='{node.Genre}' " +
                $"skin='{node.SkinPath}' color={node.TextColor.PackedValue:X8} root={node.IsSongRoot} " +
                $"crumb='{node.BreadcrumbPath}' song={node.DatabaseSongId} [{slots}]");
            lines.AddRange(DescribeHierarchy(node.Children, depth + 1));
        }

        return lines;
    }

    /// <summary>
    /// Writes a bundled-songs root holding a "Practice" box.def folder and points the
    /// manager at it, mirroring the shipped DTXMania.Game/BundledSongs layout.
    /// </summary>
    private string PrepareBundledPracticeBox()
    {
        var bundledRoot = Path.Combine(_testRoot, "Bundled");
        WriteChart("Bundled/Practice/pop/pop.dtx", "Practice Song", 15);
        File.WriteAllText(Path.Combine(bundledRoot, "Practice", "box.def"), "#TITLE: Practice");
        return bundledRoot;
    }

    private static void AssertBundledPracticeBox(SongListNode node)
    {
        Assert.Equal(NodeType.Box, node.Type);
        Assert.Equal("Practice", node.Title);
        Assert.False(node.IsSongRoot);
        Assert.Equal("Practice Song", Assert.Single(node.Children).Title);
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

    /// <summary>
    /// Slot occupancy mirror of the removed test-only IsEnumerating accessor:
    /// any non-null cancellation source means an enumeration task is in flight
    /// (including the cancellation wind-down window).
    /// </summary>
    private bool ManagerIsEnumerating =>
        ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(_manager, "_enumCancellation") != null;

    private static SongEnumerationBatch CompletedBatchWithNoActiveRoots() => new()
    {
        ActiveRoots = Array.Empty<string>(),
        DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal),
        Candidates = new List<SongImportCandidate>(),
        RootNodes = new List<SongListNode>(),
        PendingSongs = new List<PendingSongNode>(),
        Errors = new List<SongEnumerationError>(),
        IsComplete = true
    };

    private static SongEnumerationBatch CompletedBatchWithActiveRoot(string root) => new()
    {
        ActiveRoots = new[] { root },
        DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal),
        Candidates = new List<SongImportCandidate>(),
        RootNodes = new List<SongListNode>(),
        PendingSongs = new List<PendingSongNode>(),
        Errors = new List<SongEnumerationError>(),
        IsComplete = true
    };

    /// <summary>
    /// The initialized database service is stored in a private field swapped
    /// under the manager lock; tests read it via the established reflection
    /// seam after the test-only DatabaseService accessor was removed
    /// (HPA-801 Decision 0).
    /// </summary>
    private SongDatabase ManagerDatabaseService => _manager.Database;

    private async Task<int> CountChartsAsync()
    {
        await using var context = ManagerDatabaseService!.CreateContext();
        return await context.SongCharts.CountAsync();
    }

    private async Task<PersistedChartSnapshot[]> ReadPersistedChartSnapshotsAsync()
    {
        var songs = await new SongCatalogStore(ManagerDatabaseService!).GetSongsAsync();
        return songs
            .SelectMany(song => song.Charts.Select(chart =>
                new PersistedChartSnapshot(
                    song.Id,
                    chart.Id,
                    song.Title,
                    song.Artist,
                    song.IsBookmarked,
                    chart.FilePath,
                    chart.DrumLevel,
                    chart.Scores.Count,
                    chart.Scores.Sum(score => score.PlayCount),
                    chart.Scores.Max(score => score.LastPlayedAt))))
            .OrderBy(snapshot => snapshot.ChartId)
            .ToArray();
    }

    private async Task SeedPublishedLibraryAsync()
    {
        WriteChart("Songs/Seed/seed.dtx", "Seed", 40);
        await _manager.InitializeAsync();
        await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None);
        _seededChartCount = await CountChartsAsync();
    }

    private async Task ExecuteDatabaseSqlAsync(string sql)
    {
        await using var context = ManagerDatabaseService!.CreateContext();
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
        bool IsBookmarked,
        string FilePath,
        int DrumLevel,
        int ScoreCount,
        int PlayCount,
        DateTime? LastPlayedAt);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

}
