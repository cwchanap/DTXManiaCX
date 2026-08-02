#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class SongLibraryReloadServiceTests
{
    [Fact]
    public async Task ReloadAsync_WhenEnumerationIsBusy_ShouldReturnBusyWithoutPublishing()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => throw new InvalidOperationException(
                "Song enumeration is already in progress."));

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
    public async Task ReloadAsync_WhenPublicationFailsAfterCommit_ShouldRequireRestart()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) => throw new SongLibraryReloadPostCommitPublicationException(
                "publication observer failed"));

        var result = await service.ReloadAsync(
            new[] { "/songs" }, progress: null, CancellationToken.None);

        Assert.Equal(
            SongLibraryReloadOutcome.PartialSuccessRestartRequired,
            result.Outcome);
        Assert.True(result.RequiresRestart);
        Assert.True(result.RetainsCurrentSnapshot);
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
