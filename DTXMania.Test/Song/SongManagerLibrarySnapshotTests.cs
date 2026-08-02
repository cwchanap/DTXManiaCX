#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public sealed class SongManagerLibrarySnapshotTests : IDisposable
{
    private readonly SongManager _manager;

    public SongManagerLibrarySnapshotTests()
    {
        SongManager.ResetInstanceForTesting();
        _manager = SongManager.Instance;
    }

    [Fact]
    public void GetLibrarySnapshot_ShouldNotAdvancePublicationVersionOrExposeLiveRootList()
    {
        var firstNode = CreateScoreNode("First");
        _manager.PublishEnumeration(CreateBatch(["/roots/one"], [firstNode]));

        var firstSnapshot = _manager.GetLibrarySnapshot();
        var secondSnapshot = _manager.GetLibrarySnapshot();
        var rootSongsView = _manager.RootSongs;

        Assert.Equal(firstSnapshot.Version, secondSnapshot.Version);
        Assert.NotSame(firstSnapshot.RootSongs, secondSnapshot.RootSongs);
        Assert.NotSame(firstSnapshot.RootSongs, rootSongsView);
        var mutableRoots = Assert.IsAssignableFrom<IList<SongListNode>>(
            rootSongsView);
        Assert.Throws<NotSupportedException>(() =>
            mutableRoots.Add(CreateScoreNode("Mutated")));

        _manager.PublishEnumeration(CreateBatch(["/roots/two"], [CreateScoreNode("Second")]));

        Assert.Equal([firstNode], firstSnapshot.RootSongs);
        Assert.Equal([firstNode], rootSongsView);
        Assert.Equal(["/roots/one"], firstSnapshot.ActiveRoots);
    }

    [Fact]
    public void PublishEmptyLibrary_ShouldPublishOneNewVersionWithEmptyHierarchyAndRoots()
    {
        _manager.PublishEnumeration(CreateBatch(
            ["/roots/one"],
            [CreateScoreNode("First")]));
        var before = _manager.GetLibrarySnapshot();
        SongLibrarySnapshot? published = null;
        _manager.SongLibraryPublished += (_, args) => published = args.Snapshot;

        _manager.PublishEmptyLibrary();

        var after = _manager.GetLibrarySnapshot();
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Empty(after.RootSongs);
        Assert.Empty(after.ActiveRoots);
        Assert.Equal(0, after.EnumeratedFileCount);
        Assert.Equal(0, after.DiscoveredScoreCount);
        Assert.NotNull(published);
        Assert.Equal(after.Version, published!.Version);
        Assert.Empty(published.RootSongs);
        Assert.Empty(published.ActiveRoots);
    }

    [Fact]
    public void PublishEnumeration_ShouldRaiseOneCoherentVersionedSnapshot()
    {
        var rootNode = CreateScoreNode("Published");
        SongLibrarySnapshot? published = null;
        var eventCount = 0;
        _manager.SongLibraryPublished += (_, args) =>
        {
            eventCount++;
            published = args.Snapshot;
        };

        _manager.PublishEnumeration(CreateBatch(
            ["/roots/one", "/roots/two"],
            [rootNode]));

        var current = _manager.GetLibrarySnapshot();
        Assert.Equal(1, eventCount);
        Assert.NotNull(published);
        Assert.Equal(current.Version, published!.Version);
        Assert.Equal([rootNode], published.RootSongs);
        Assert.Equal(["/roots/one", "/roots/two"], published.ActiveRoots);
        Assert.Equal(0, published.EnumeratedFileCount);
        Assert.Equal(0, published.DiscoveredScoreCount);
    }

    [Fact]
    public void SongLibrarySnapshot_ShouldCopyAndFreezeConstructorCollections()
    {
        var rootNode = CreateScoreNode("Stable");
        var roots = new List<SongListNode> { rootNode };
        var activeRoots = new List<string> { "/roots/one" };

        var snapshot = new SongLibrarySnapshot(
            Version: 42,
            RootSongs: roots,
            ActiveRoots: activeRoots,
            EnumeratedFileCount: 3,
            DiscoveredScoreCount: 2);
        roots.Clear();
        activeRoots[0] = "/roots/mutated";

        Assert.Equal([rootNode], snapshot.RootSongs);
        Assert.Equal(["/roots/one"], snapshot.ActiveRoots);
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<string>>(snapshot.ActiveRoots)
                .Add("/roots/extra"));
    }

    [Fact]
    public void SetCurrentSearchPaths_ShouldKeepPublishedSnapshotCoherentUntilFullPublication()
    {
        _manager.RootPolicy = new SongRootPolicy(
            SongRootPolicy.CreateComparer(ignoreCase: true));
        var rootNode = CreateScoreNode("Published");
        _manager.PublishEnumeration(CreateBatch(["/roots/published"], [rootNode]));
        var before = _manager.GetLibrarySnapshot();

        _manager.SetCurrentSearchPaths(["/roots/pending", "/ROOTS/PENDING"]);

        var after = _manager.GetLibrarySnapshot();

        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.RootSongs, after.RootSongs);
        Assert.Equal(before.ActiveRoots, after.ActiveRoots);
        Assert.Equal(before.EnumeratedFileCount, after.EnumeratedFileCount);
        Assert.Equal(before.DiscoveredScoreCount, after.DiscoveredScoreCount);
    }

    [Fact]
    public void SetCurrentSearchPaths_WithEmptyInput_ShouldPublishEmptyLibrary()
    {
        _manager.PublishEnumeration(CreateBatch(
            ["/roots/published"],
            [CreateScoreNode("Published")]));
        var before = _manager.GetLibrarySnapshot();
        SongLibrarySnapshot? published = null;
        _manager.SongLibraryPublished += (_, args) => published = args.Snapshot;

        _manager.SetCurrentSearchPaths(Array.Empty<string>());

        var after = _manager.GetLibrarySnapshot();
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Empty(after.RootSongs);
        Assert.Empty(after.ActiveRoots);
        Assert.Equal(0, after.EnumeratedFileCount);
        Assert.Equal(0, after.DiscoveredScoreCount);
        Assert.NotNull(published);
        Assert.Equal(after.Version, published!.Version);
    }

    [Fact]
    public async Task SnapshotIteration_ShouldRemainStableWhileAnotherThreadPublishes()
    {
        var firstNode = CreateScoreNode("First");
        _manager.PublishEnumeration(CreateBatch(["/roots/one"], [firstNode]));
        var stableSnapshot = _manager.GetLibrarySnapshot();

        using var iterationStarted = new ManualResetEventSlim(false);
        using var releaseIteration = new ManualResetEventSlim(false);
        var iterationTask = Task.Run(() =>
        {
            var titles = new List<string>();
            foreach (var node in stableSnapshot.RootSongs)
            {
                iterationStarted.Set();
                releaseIteration.Wait();
                titles.Add(node.Title);
            }

            return titles.ToArray();
        });
        Assert.True(iterationStarted.Wait(TimeSpan.FromSeconds(1)));
        _manager.PublishEnumeration(CreateBatch(
            ["/roots/two"],
            [CreateScoreNode("Second")]));
        releaseIteration.Set();
        var enumeratedTitles = await iterationTask;

        Assert.Equal(["First"], enumeratedTitles);
        Assert.Equal([firstNode], stableSnapshot.RootSongs);
        Assert.Equal(["/roots/one"], stableSnapshot.ActiveRoots);
    }

    public void Dispose()
    {
        SongManager.ResetInstanceForTesting();
    }

    private static SongEnumerationBatch CreateBatch(
        IReadOnlyList<string> activeRoots,
        IReadOnlyList<SongListNode> rootNodes)
    {
        return new SongEnumerationBatch
        {
            ActiveRoots = activeRoots,
            DiscoveredChartPaths = new HashSet<string>(StringComparer.Ordinal),
            Candidates = new List<SongImportCandidate>(),
            RootNodes = rootNodes.ToList(),
            PendingSongs = new List<PendingSongNode>(),
            Errors = new List<SongEnumerationError>(),
            DiscoveryAndParsingDuration = TimeSpan.Zero,
            IsComplete = true,
        };
    }

    private static SongListNode CreateScoreNode(string title) => new()
    {
        Type = NodeType.Score,
        Title = title,
    };
}
