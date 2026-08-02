using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
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
    public async Task EnumerateAndImportSongsAsync_PublicOverload_ShouldUseNullObserver()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Batch.IsComplete);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSuccessful_ShouldNotifyAfterEndEnumeration()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var sourceWasCleared = false;
        var observer = new RecordingObserver((_, _) =>
        {
            sourceWasCleared =
                ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                    _manager,
                    "_enumCancellation") == null;
        });

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None,
            observer);

        Assert.Equal(1, observer.TerminalCount);
        Assert.Same(result, observer.Result);
        Assert.Equal(
            StartupOperationOutcome.Success,
            observer.Outcome);
        Assert.True(sourceWasCleared);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenFaulted_ShouldNotifyFailureAfterCleanup()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/Failure/chart.dtx", "Failure", 50);
        var expected = new IOException("persistence unavailable");
        _manager.ImportSongsCoreAsync = (_, _, _, _) =>
            Task.FromException<SongBulkImportResult>(expected);
        var sourceWasCleared = false;
        var observer = new RecordingObserver((_, _) =>
        {
            sourceWasCleared =
                ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                    _manager,
                    "_enumCancellation") == null;
        });

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress: null,
                CancellationToken.None,
                observer));

        Assert.Same(expected, actual);
        Assert.Equal(1, observer.TerminalCount);
        Assert.Null(observer.Result);
        Assert.Equal(
            StartupOperationOutcome.Failure,
            observer.Outcome);
        Assert.True(sourceWasCleared);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenCancelled_ShouldNotifyCancellationAfterCleanup()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/Cancelled/chart.dtx", "Cancelled", 50);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<EnumerationProgress>(
            _ => cancellation.Cancel());
        var sourceWasCleared = false;
        var observer = new RecordingObserver((_, _) =>
        {
            sourceWasCleared =
                ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                    _manager,
                    "_enumCancellation") == null;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress,
                cancellation.Token,
                observer));

        Assert.Equal(1, observer.TerminalCount);
        Assert.Null(observer.Result);
        Assert.Equal(
            StartupOperationOutcome.Cancellation,
            observer.Outcome);
        Assert.True(sourceWasCleared);
    }

    [Fact]
    public async Task Enumeration_WhenCancelledButNotTerminated_ShouldKeepSlotOccupiedUntilTermination()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);

        // Park the enumeration inside the batch builder so it holds the slot
        // after cancellation but before EndEnumeration runs.
        var gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _manager.BuildEnumerationBatchCoreAsync = (_, _, token) =>
            ParkUntilReleasedThenCancelAsync(gate, token);

        using var cts = new CancellationTokenSource();
        // Fire enumeration A without awaiting: it acquires the slot then parks.
        var enumerationA = _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, progress: null, cts.Token);

        await SpinUntilAsync(
            () => _manager.IsEnumerating,
            "Enumeration slot was never acquired.");

        // Cancel while A is still executing inside the batch builder.
        cts.Cancel();

        try
        {
            // The slot must remain occupied until A actually terminates, so a
            // concurrent caller cannot start a second enumeration against the
            // shared _rootSongs/database state while A winds down.
            Assert.True(_manager.IsEnumerating);

            // A second enumeration must be rejected while A has not terminated.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
        Assert.False(_manager.IsEnumerating);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WithAlreadyCancelledToken_ShouldFreeSlotForSubsequentEnumeration()
    {
        // Regression: an already-cancelled token used to throw from the
        // pre-try cancellation check, skipping EndEnumeration and leaving
        // _enumCancellation non-null permanently. Every subsequent call was
        // then rejected as "already in progress."
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress: null,
                cts.Token));

        // The slot must be released even though the throw happened before the
        // batch builder ran.
        Assert.False(_manager.IsEnumerating);
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

        Assert.False(_manager.IsEnumerating);
        Assert.Null(
            ReflectionHelpers.GetPrivateField<CancellationTokenSource?>(
                _manager,
                "_enumCancellation"));

        // Initialize the database after the failed attempt and verify the
        // enumeration can be retried successfully.
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
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
    public async Task EnumerateAndImportSongsAsync_WhenSongDiscoveredSubscriberThrows_ShouldNotFailOrSuppressOtherSubscribers()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/A/basic.dtx", title: "Grouped", drumLevel: 20);
        WriteChart("Songs/A/extreme.dtx", title: "Grouped", drumLevel: 80);

        var secondDiscoveredCalls = 0;
        var completedCalls = 0;
        // First subscriber throws; it must not stop the second subscriber or
        // EnumerationCompleted, and must not turn the committed/published
        // import into a reported failure.
        _manager.SongDiscovered += (_, _) => throw new InvalidOperationException("boom");
        _manager.SongDiscovered += (_, _) => secondDiscoveredCalls++;
        _manager.EnumerationCompleted += (_, _) => completedCalls++;
        var observer = new RecordingObserver(onTerminal: null);

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, progress: null, CancellationToken.None, observer);

        Assert.NotNull(result);
        Assert.True(result.Batch.IsComplete);
        Assert.Equal(StartupOperationOutcome.Success, observer.Outcome);
        Assert.Same(result, observer.Result);
        Assert.True(secondDiscoveredCalls > 0,
            "A throwing SongDiscovered subscriber suppressed later subscribers.");
        Assert.Equal(1, completedCalls);
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenObserverThrows_ShouldPreserveResultOrException()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var observer = new ThrowingObserver();

        var result = await _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot },
            progress: null,
            CancellationToken.None,
            observer);

        Assert.True(result.Batch.IsComplete);

        var expected = new IOException("persistence unavailable");
        _manager.ImportSongsCoreAsync = (_, _, _, _) =>
            Task.FromException<SongBulkImportResult>(expected);

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot },
                progress: null,
                CancellationToken.None,
                observer));

        Assert.Same(expected, actual);
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
    public async Task EnumerateSongsOnlyWithPublicationAsync_WithoutDatabase_ShouldReportNoPublication()
    {
        var result = await _manager.EnumerateSongsOnlyWithPublicationAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(0, result.SongCount);
        Assert.False(result.Published);
    }

    [Fact]
    public async Task EnumerateSongsOnlyWithPublicationAsync_WithEmptyRoot_ShouldReportEmptyPublication()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);

        var result = await _manager.EnumerateSongsOnlyWithPublicationAsync(
            new[] { _songsRoot }, null, CancellationToken.None);

        Assert.Equal(0, result.SongCount);
        Assert.True(result.Published);
        Assert.Empty(_manager.RootSongs);
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
    public async Task EnumerateAndImportSongsAsync_WhenSetDefinitionReadFails_ShouldProtectSubtreeChartsAndImportOtherCandidates()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
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

        await using (var context = _manager.DatabaseService!.CreateContext())
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
        var realReader = _manager.ReadAllLinesCoreAsync;
        _manager.ReadAllLinesCoreAsync = (path, encoding, token) =>
            SongPathIdentity.CanonicalComparer.Equals(path, setDefPath)
                ? Task.FromException<string[]>(
                    new IOException("set definition unavailable"))
                : realReader(path, encoding, token);

        var result = await _manager.EnumerateAndImportSongsAsync(
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
    public async Task GetRecentlyPlayedNodesAsync_WhenActiveRecencyRanksAcrossPageBoundary_ShouldOrderGloballyByActiveRecency()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        var activeRoot = Path.Combine(_testRoot, "Active");
        var inactiveRoot = Path.Combine(_testRoot, "Removed");
        Directory.CreateDirectory(activeRoot);
        var now = DateTime.UtcNow;

        await using (var context = _manager.DatabaseService!.CreateContext())
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
        await _manager.BuildSongListFromDatabasePublicAsync(new[] { activeRoot });

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

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReadCancelled_ShouldThrowOperationCanceled()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/SetDef/chart.dtx", "SetDef Song", 50);
        File.WriteAllLines(Path.Combine(_songsRoot, "SetDef", "set.def"),
            ["#TITLE: SetDef Song", "#DLEVEL: 50", "1: chart.dtx"]);
        using var cts = new CancellationTokenSource();
        _manager.ReadAllLinesCoreAsync = (_, _, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Array.Empty<string>());
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, cts.Token));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenSetDefReadThrowsCancellation_ShouldThrowOperationCanceled()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/SetDef2/chart.dtx", "SetDef Song 2", 50);
        File.WriteAllLines(Path.Combine(_songsRoot, "SetDef2", "set.def"),
            ["#TITLE: SetDef Song 2", "#DLEVEL: 50", "1: chart.dtx"]);
        using var cts = new CancellationTokenSource();
        // Cancel during set.def read by throwing OperationCanceledException.
        _manager.ReadAllLinesCoreAsync = (_, _, token) =>
            throw new OperationCanceledException(token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, cts.Token));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenChartParseCancelled_ShouldThrowOperationCanceled()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/Cancel/chart.dtx", "Cancel Song", 50);
        using var cts = new CancellationTokenSource();
        var parseCallCount = 0;
        _manager.ParseSongEntitiesCoreAsync = path =>
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
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, cts.Token));
    }

    [Fact]
    public async Task EnumerateAndImportSongsAsync_WhenFileEnumerationCancelled_ShouldThrowOperationCanceled()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/BoxDir/chart.dtx", "Box Song", 50);
        var boxDir = Path.Combine(_songsRoot, "BoxDir");
        File.WriteAllLines(Path.Combine(boxDir, "box.def"),
            ["#TITLE: Box Title", "#GENRE: Test"]);
        using var cts = new CancellationTokenSource();
        // Cancel before the box.def read happens by triggering cancellation
        // during the chart file enumeration phase (before box.def is read).
        var fileCallCount = 0;
        _manager.EnumerateFilesCore = _ =>
        {
            fileCallCount++;
            if (fileCallCount > 1)
                cts.Cancel();
            return new[] { Path.Combine(boxDir, "chart.dtx") };
        };

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
            _manager.RootPolicy = new SongRootPolicy(
                SongRootPolicy.CreateComparer(ignoreCase: true));

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
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
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
            DiscoveryAndParsingDuration = TimeSpan.Zero,
            IsComplete = false
        };
        _manager.BuildEnumerationBatchCoreAsync = (_, _, _) =>
            Task.FromResult(incompleteBatch);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None));

            Assert.Contains("incomplete enumeration", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WhenCompleteBatchHasNoActiveRoots_ShouldNotImportOrPublish()
        {
            await SeedPublishedLibraryAsync();
            var before = _manager.GetLibrarySnapshot();
            var importCalls = 0;
            var publishEvents = 0;
            _manager.ImportSongsCoreAsync = (_, _, _, _) =>
            {
                importCalls++;
                return Task.FromResult(SongBulkImportResult.Empty);
            };
            _manager.SongLibraryPublished += (_, _) => publishEvents++;
            _manager.BuildEnumerationBatchCoreAsync = (_, _, _) =>
                Task.FromResult(new SongEnumerationBatch
                {
                    ActiveRoots = Array.Empty<string>(),
                    DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal),
                    Candidates = new List<SongImportCandidate>(),
                    RootNodes = new List<SongListNode>(),
                    PendingSongs = new List<PendingSongNode>(),
                    Errors = new List<SongEnumerationError>(),
                    DiscoveryAndParsingDuration = TimeSpan.Zero,
                    IsComplete = true,
                });

            var result = await _manager.EnumerateAndImportSongsAsync(
                new[] { _songsRoot }, null, CancellationToken.None);
            var after = _manager.GetLibrarySnapshot();

            Assert.Equal(SongEnumerationOutcome.NoActiveRoots, result.Outcome);
            Assert.Same(SongBulkImportResult.Empty, result.Import);
            Assert.Equal(TimeSpan.Zero, result.HierarchyDuration);
            Assert.Equal(0, importCalls);
            Assert.Equal(0, publishEvents);
            Assert.Equal(before.Version, after.Version);
            Assert.Equal(before.RootSongs, after.RootSongs);
            Assert.Equal(before.ActiveRoots, after.ActiveRoots);
            Assert.False(_manager.IsEnumerating);
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
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
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
        _manager.FinalizePendingNodes(batch, emptyCharts);

        // Each missing chart path must have its placeholder score slot nulled.
        for (var index = 0; index < chartPathCount; index++)
            Assert.Null(placeholder.Scores[index]);

        // With no resolved charts, the placeholder is removed from the batch.
        Assert.DoesNotContain(placeholder, batch.RootNodes);
    }

    [Fact]
    public async Task PublishEnumeration_WhenCalled_ShouldUpdateRootSongsAndCounts()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
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
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/PostCommit/chart.dtx", "Post Commit", 50);
        using var cancellation = new CancellationTokenSource();
        var realImporter = _manager.ImportSongsCoreAsync;
        _manager.ImportSongsCoreAsync = async (database, request, progress, token) =>
        {
            var import = await realImporter(database, request, progress, token);
            // The default importer returns only after SaveChanges/transaction
            // commit. This models a Config deactivation that races immediately
            // afterward: finalization/publication must still complete.
            cancellation.Cancel();
            return import;
        };
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

    [Fact]
    public async Task GetRecentlyPlayedNodesAsync_WithMoreThanLimit_ShouldReturnLimitInRecencyOrder()
    {
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
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
        await using var context = _manager.DatabaseService!.CreateContext();
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
        await _manager.InitializeDatabaseServiceAsync(_databasePath);
        WriteChart("Songs/Adapter/chart.dtx", "Adapter Song", 50);

        var messages = new List<string>();
        IProgress<EnumerationProgress>? capturedProgress = null;
        var importCallCount = 0;
        var realImporter = _manager.ImportSongsCoreAsync;
        _manager.ImportSongsCoreAsync = async (database, request, progress, token) =>
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
            return await realImporter(database, request, progress, token);
        };

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
        SongManager.ResetInstanceForTesting();
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

    private sealed class RecordingObserver(
        Action<SongEnumerationResult?, StartupOperationOutcome>? onTerminal)
        : IStartupSongLoadTimingObserver
    {
        public int TerminalCount { get; private set; }
        public SongEnumerationResult? Result { get; private set; }
        public StartupOperationOutcome? Outcome { get; private set; }

        public void BeginDatabaseSpan(StartupDatabaseTimingSpan span)
        {
        }

        public void EndDatabaseSpan(StartupDatabaseTimingSpan span)
        {
        }

        public void RecordUnexpectedTableExistsPath()
        {
        }

        public void RecordEnumerationTerminal(
            SongEnumerationResult? result,
            StartupOperationOutcome outcome)
        {
            TerminalCount++;
            Result = result;
            Outcome = outcome;
            onTerminal?.Invoke(result, outcome);
        }
    }

    private sealed class ThrowingObserver : IStartupSongLoadTimingObserver
    {
        public void BeginDatabaseSpan(StartupDatabaseTimingSpan span) =>
            throw new InvalidOperationException("observer begin failure");

        public void EndDatabaseSpan(StartupDatabaseTimingSpan span) =>
            throw new InvalidOperationException("observer end failure");

        public void RecordUnexpectedTableExistsPath() =>
            throw new InvalidOperationException(
                "observer table-exists failure");

        public void RecordEnumerationTerminal(
            SongEnumerationResult? result,
            StartupOperationOutcome outcome) =>
            throw new InvalidOperationException(
                "observer terminal failure");
    }
}
