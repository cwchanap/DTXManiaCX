#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Config;
using Xunit;

using System.Reflection;
namespace DTXMania.Test.Config;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public sealed class SongLibraryReloadServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(), "HPA-191-ReloadService", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public SongLibraryReloadServiceTests()
    {
        _databasePath = Path.Combine(_testRoot, "songs.db");
        SongManager.ResetInstanceForTesting();
    }

    [Fact]
    public async Task ReloadAsync_WhenEnumerationIsBusy_ShouldReturnBusyWithoutPublishing()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => throw new SongEnumerationBusyException());

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.Busy, result.Outcome);
        Assert.True(result.RetainsCurrentSnapshot);
    }

    [Fact]
    public async Task ReloadAsync_WhenNoActiveRoots_ShouldRetainCurrentSnapshot()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => Task.FromResult(CreateResult(
                SongEnumerationOutcome.NoActiveRoots,
                new[] { new SongEnumerationError("/missing", "unavailable", true) })));

        var result = await service.ReloadAsync(
            new[] { "/missing" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.NoActiveRoots, result.Outcome);
        Assert.True(result.RetainsCurrentSnapshot);
        Assert.Equal(1, result.UnavailableRootCount);
    }

    [Fact]
    public async Task ReloadAsync_WhenBatchContainsRootFailures_ShouldUseBatchFailureCount()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => Task.FromResult(CreateResult(
                SongEnumerationOutcome.ImportedAndPublished,
                new[]
                {
                    new SongEnumerationError("/missing-a", "unavailable", true),
                    new SongEnumerationError("/chart", "parse failure", false),
                    new SongEnumerationError("/missing-b", "unavailable", true),
                })));

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.Published, result.Outcome);
        Assert.False(result.RetainsCurrentSnapshot);
        Assert.Equal(2, result.UnavailableRootCount);
    }

    [Fact]
    public async Task ReloadAsync_WhenCancelledBeforeCommit_ShouldRetainCurrentSnapshot()
    {
        var service = new SongLibraryReloadService(
            (_, _, token) => Task.FromCanceled<SongEnumerationResult>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, cancellation.Token);

        Assert.Equal(SongLibraryReloadOutcome.Cancelled, result.Outcome);
        Assert.True(result.RetainsCurrentSnapshot);
    }

    [Fact]
    public async Task ReloadAsync_WhenDefaultEnumerationFinalizationFailsAfterCommit_ShouldRequireRestart()
    {
        var songRoot = Path.Combine(_testRoot, "Songs");
        Directory.CreateDirectory(songRoot);
        File.WriteAllLines(Path.Combine(songRoot, "committed.dtx"), new[]
        {
            "#TITLE: Committed Reload",
            "#ARTIST: Fixture Artist",
            "#BPM: 120",
            "#DLEVEL: 50"
        });
        var manager = SongManager.Instance;
        Assert.True(await manager.InitializeDatabaseServiceAsync(_databasePath));
        manager.FinalizePendingNodesCore = (_, _) =>
            throw new InvalidOperationException("finalization failed after commit");
        var service = new SongLibraryReloadService();

        var result = await service.ReloadAsync(
            new[] { songRoot }, progress: null, CancellationToken.None);

        Assert.Equal(
            SongLibraryReloadOutcome.PartialSuccessRestartRequired,
            result.Outcome);
        Assert.True(result.RequiresRestart);
        Assert.False(result.RetainsCurrentSnapshot);
        Assert.Contains("finalization failed after commit", result.FailureMessage);
        // The default service scans the bundled practice songs after the configured roots.
        var titles = (await manager.DatabaseService!.GetSongsAsync())
            .Select(song => song.Title)
            .ToList();
        Assert.Contains("Committed Reload", titles);
        Assert.Contains("Pop 8-Beat", titles);
    }

    [Fact]
    public async Task ReloadAsync_WhenEnumerationThrowsUnexpectedException_ShouldReturnFailedRetainingSnapshot()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => throw new InvalidOperationException("unexpected boom"));

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.Failed, result.Outcome);
        Assert.True(result.RetainsCurrentSnapshot);
        Assert.False(result.RequiresRestart);
        Assert.Contains("unexpected boom", result.FailureMessage);
    }

    [Fact]
    public async Task ReloadAsync_WhenProgressIsReported_ShouldForwardAdaptedProgressValues()
    {
        var reported = new List<SongLibraryReloadProgress>();
        var progress = new SynchronousProgress<SongLibraryReloadProgress>(reported.Add);
        var service = new SongLibraryReloadService(
            (roots, progress, token) =>
            {
                progress?.Report(new EnumerationProgress
                {
                    CurrentOperation = "Scanning",
                    ProcessedCount = 4,
                    DiscoveredSongs = 2,
                    CurrentFile = "/songs/a.dtx",
                    CurrentDirectory = "/songs",
                });
                progress?.Report(new EnumerationProgress
                {
                    CurrentOperation = "Importing",
                    ProcessedCount = 5,
                    DiscoveredSongs = 3,
                    CurrentFile = "/songs/b.dtx",
                    CurrentDirectory = "/songs/sub",
                });
                return Task.FromResult(CreateResult(
                    SongEnumerationOutcome.ImportedAndPublished,
                    Array.Empty<SongEnumerationError>()));
            });

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.Published, result.Outcome);
        Assert.Equal(2, reported.Count);
        Assert.Equal("Scanning", reported[0].CurrentOperation);
        Assert.Equal(4, reported[0].ProcessedCount);
        Assert.Equal(2, reported[0].DiscoveredSongs);
        Assert.Equal("/songs/a.dtx", reported[0].CurrentFile);
        Assert.Equal("/songs", reported[0].CurrentDirectory);
        Assert.Equal("Importing", reported[1].CurrentOperation);
        Assert.Equal("/songs/sub", reported[1].CurrentDirectory);
    }

    [Fact]
    public async Task ReloadAsync_WhenPublished_ShouldReportEnumeratedFileCount()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) =>
            {
                var batch = new SongEnumerationBatch
                {
                    ActiveRoots = new[] { "/songs" },
                    DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "/songs/a.dtx", "/songs/b.dtx", "/songs/c.dtx",
                    },
                    Candidates = new List<SongImportCandidate>(),
                    RootNodes = new List<SongListNode>(),
                    PendingSongs = new List<PendingSongNode>(),
                    Errors = new List<SongEnumerationError>(),
                    IsComplete = true,
                };
                return Task.FromResult(new SongEnumerationResult(
                    SongEnumerationOutcome.ImportedAndPublished,
                    batch,
                    SongBulkImportResult.Empty));
            });

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.Published, result.Outcome);
        Assert.Equal(3, result.EnumeratedFileCount);
        Assert.Equal(0, result.DiscoveredScoreCount);
        Assert.Equal(0, result.UnavailableRootCount);
    }

    public void Dispose()
    {
        SongManager.ResetInstanceForTesting();
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch (IOException)
            {
                // SQLite can briefly retain a sidecar file after disposal.
            }
        }
    }

    private static SongEnumerationResult CreateResult(
        SongEnumerationOutcome outcome,
        IReadOnlyList<SongEnumerationError> errors)
    {
        return new SongEnumerationResult(
            outcome,
            new SongEnumerationBatch
            {
                ActiveRoots = outcome == SongEnumerationOutcome.NoActiveRoots
                    ? Array.Empty<string>()
                    : new[] { "/songs" },
                DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal),
                Candidates = new List<SongImportCandidate>(),
                RootNodes = new List<SongListNode>(),
                PendingSongs = new List<PendingSongNode>(),
                Errors = errors.ToList(),
                IsComplete = true,
            },
            SongBulkImportResult.Empty);
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> that invokes the callback synchronously on
    /// the reporting thread, unlike <see cref="Progress{T}"/> which posts to a
    /// captured <see cref="SynchronizationContext"/>. This guarantees the
    /// recorded values are visible before assertions run.
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        public SynchronousProgress(Action<T> callback) =>
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        public void Report(T value) => _callback(value);
    }

    [Fact]
    public async Task ReloadAsync_WhenOutcomeIsUnknown_ShouldReturnFailedWithUnknownOutcomeMessage()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => Task.FromResult(CreateResult(
                (SongEnumerationOutcome)999,
                Array.Empty<SongEnumerationError>())));

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.Failed, result.Outcome);
        Assert.Contains("unknown outcome", result.FailureMessage, StringComparison.Ordinal);
        Assert.True(result.RetainsCurrentSnapshot);
    }

    [Fact]
    public async Task ReloadAsync_WhenConfiguredRootsIsNull_ShouldThrowArgumentNullException()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => Task.FromResult(CreateResult(
                SongEnumerationOutcome.ImportedAndPublished,
                Array.Empty<SongEnumerationError>())));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ReloadAsync(null!, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task ReloadAsync_WhenEnumerationReturnsNoActiveRootsWithoutRootFailures_ShouldReportZeroUnavailable()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => Task.FromResult(CreateResult(
                SongEnumerationOutcome.NoActiveRoots,
                Array.Empty<SongEnumerationError>())));

        var result = await service.ReloadAsync(
            new[] { "/missing" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.NoActiveRoots, result.Outcome);
        Assert.Equal(0, result.UnavailableRootCount);
        Assert.True(result.RetainsCurrentSnapshot);
    }

    [Fact]
    public async Task ReloadAsync_WhenProgressAdapterReceivesNullValue_ShouldThrowArgumentNullException()
    {
        // The ReloadProgressAdapter guards against a null progress value. Drive it
        // through the public ReloadAsync path by having the enumeration callback
        // report null, which the adapter must reject rather than silently swallow.
        IProgress<EnumerationProgress>? capturedProgress = null;
        var service = new SongLibraryReloadService(
            (roots, progress, token) =>
            {
                capturedProgress = progress;
                return Task.FromResult(CreateResult(
                    SongEnumerationOutcome.ImportedAndPublished,
                    Array.Empty<SongEnumerationError>()));
            });

        var reported = new List<SongLibraryReloadProgress>();
        var outerProgress = new Progress<SongLibraryReloadProgress>(reported.Add);
        await service.ReloadAsync(new[] { "/songs" }, outerProgress, CancellationToken.None);

        Assert.NotNull(capturedProgress);
        Assert.Throws<ArgumentNullException>(() => capturedProgress!.Report(null!));
    }

    [Fact]
    public void CountUnavailableRoots_WhenBatchIsNull_ShouldThrowArgumentNullException()
    {
        // CountUnavailableRoots is a private static helper; exercise it via reflection
        // so the null-guard branch is covered.
        var method = typeof(SongLibraryReloadService).GetMethod(
            "CountUnavailableRoots",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var thrown = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, new object?[] { null }));
        Assert.IsType<ArgumentNullException>(thrown.InnerException);
    }

    [Fact]
    public async Task ReloadAsync_WhenPublishedWithCandidates_ShouldReportDiscoveredScoreCount()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) =>
            {
                var batch = new SongEnumerationBatch
                {
                    ActiveRoots = new[] { "/songs" },
                    DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "/songs/a.dtx", "/songs/b.dtx",
                    },
                    Candidates = new List<SongImportCandidate>
                    {
                        CreateCandidate("/songs/a.dtx", 0),
                        CreateCandidate("/songs/b.dtx", 1),
                    },
                    RootNodes = new List<SongListNode>(),
                    PendingSongs = new List<PendingSongNode>(),
                    Errors = new List<SongEnumerationError>(),
                    IsComplete = true,
                };
                return Task.FromResult(new SongEnumerationResult(
                    SongEnumerationOutcome.ImportedAndPublished,
                    batch,
                    SongBulkImportResult.Empty));
            });

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, CancellationToken.None);

        Assert.Equal(SongLibraryReloadOutcome.Published, result.Outcome);
        Assert.Equal(2, result.EnumeratedFileCount);
        Assert.Equal(2, result.DiscoveredScoreCount);
    }

    private static SongImportCandidate CreateCandidate(string chartPath, int groupOrder)
    {
        var song = new global::DTXMania.Game.Lib.Song.Entities.Song { Title = $"Song {groupOrder}" };
        var chart = new global::DTXMania.Game.Lib.Song.Entities.SongChart
        {
            Song = song,
            FilePath = chartPath,
        };
        song.Charts.Add(chart);
        return new SongImportCandidate(song, chart, chartPath, $"group-{groupOrder}", groupOrder);
    }
}
