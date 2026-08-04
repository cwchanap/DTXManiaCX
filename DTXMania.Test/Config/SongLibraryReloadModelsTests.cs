#nullable enable

using DTXMania.Game.Lib.Stage.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class SongLibraryReloadModelsTests
{
    [Fact]
    public void SongLibraryReloadResult_ShouldClassifyRetentionAndRestartRequirements()
    {
        // SongLibraryReloadOutcome is internal, so it cannot be exposed on a public
        // Theory parameter. The loop preserves per-outcome diagnostic clarity.
        var cases = new[]
        {
            (SongLibraryReloadOutcome.Busy, true, false),
            (SongLibraryReloadOutcome.NoActiveRoots, true, false),
            (SongLibraryReloadOutcome.Cancelled, true, false),
            (SongLibraryReloadOutcome.Failed, true, false),
            (SongLibraryReloadOutcome.Published, false, false),
            (SongLibraryReloadOutcome.PartialSuccessRestartRequired, false, true),
        };

        foreach (var (outcome, expectedRetainsSnapshot, expectedRequiresRestart) in cases)
        {
            var result = new SongLibraryReloadResult(
                outcome,
                UnavailableRootCount: 0,
                EnumeratedFileCount: 0,
                DiscoveredScoreCount: 0);

            Assert.True(
                expectedRetainsSnapshot == result.RetainsCurrentSnapshot,
                $"outcome={outcome}: expected RetainsCurrentSnapshot={expectedRetainsSnapshot}, actual={result.RetainsCurrentSnapshot}");
            Assert.True(
                expectedRequiresRestart == result.RequiresRestart,
                $"outcome={outcome}: expected RequiresRestart={expectedRequiresRestart}, actual={result.RequiresRestart}");
        }
    }

    [Fact]
    public void SongLibraryReloadResult_ShouldCarryFailureMessageWhenProvided()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Failed,
            UnavailableRootCount: 1,
            EnumeratedFileCount: 2,
            DiscoveredScoreCount: 3,
            "boom");

        Assert.Equal(SongLibraryReloadOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.UnavailableRootCount);
        Assert.Equal(2, result.EnumeratedFileCount);
        Assert.Equal(3, result.DiscoveredScoreCount);
        Assert.Equal("boom", result.FailureMessage);
    }

    [Fact]
    public void SongLibraryReloadProgress_ShouldRoundTripAllFields()
    {
        var progress = new SongLibraryReloadProgress(
            CurrentOperation: "Parsing",
            ProcessedCount: 7,
            DiscoveredSongs: 4,
            CurrentFile: "/songs/x.dtx",
            CurrentDirectory: "/songs");

        Assert.Equal("Parsing", progress.CurrentOperation);
        Assert.Equal(7, progress.ProcessedCount);
        Assert.Equal(4, progress.DiscoveredSongs);
        Assert.Equal("/songs/x.dtx", progress.CurrentFile);
        Assert.Equal("/songs", progress.CurrentDirectory);
    }
}
