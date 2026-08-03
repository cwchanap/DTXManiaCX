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
        Assert.Single(await manager.DatabaseService!.GetSongsAsync());
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

        var progress = new Progress<SongLibraryReloadProgress>(reported.Add);
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
                    DiscoveryAndParsingDuration = TimeSpan.Zero,
                    IsComplete = true,
                };
                return Task.FromResult(new SongEnumerationResult(
                    SongEnumerationOutcome.ImportedAndPublished,
                    batch,
                    SongBulkImportResult.Empty,
                    TimeSpan.Zero));
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
                DiscoveryAndParsingDuration = TimeSpan.Zero,
                IsComplete = true,
            },
            SongBulkImportResult.Empty,
            TimeSpan.Zero);
    }
}
