#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class SongLibraryReloadServiceAdditionalTests
{
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
    public async Task ReloadAsync_WhenPublishedWithDiscoveredScores_ShouldReportScoreCount()
    {
        var service = new SongLibraryReloadService(
            (_, _, _) =>
            {
                var batch = new SongEnumerationBatch
                {
                    ActiveRoots = new[] { "/songs" },
                    DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "/songs/a.dtx",
                    },
                    Candidates = new List<SongImportCandidate>(capacity: 2),
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
        Assert.Equal(1, result.EnumeratedFileCount);
        Assert.Equal(0, result.DiscoveredScoreCount);
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
