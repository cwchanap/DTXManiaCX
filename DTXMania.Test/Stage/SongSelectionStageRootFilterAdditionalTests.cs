using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using Moq;
using Xunit;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public sealed class SongSelectionStageRootFilterAdditionalTests
{
    [Fact]
    public void FilterNodesForActiveRoots_WhenNodesAreNull_ShouldReturnEmptyList()
    {
        var stage = CreateStage();

        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForActiveRoots", null, new[] { "/songs" });

        Assert.Empty(result);
    }

    [Fact]
    public void FilterNodesForActiveRoots_WhenActiveRootsAreNull_ShouldReturnAllNodes()
    {
        var stage = CreateStage();
        var song = Score("Test", 1, "/songs/test.dtx");

        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForActiveRoots", new[] { song }, null);

        Assert.Single(result);
        Assert.Same(song, result[0]);
    }

    [Fact]
    public void FilterNodesForActiveRoots_WhenActiveRootsAreEmpty_ShouldReturnEmptyList()
    {
        var stage = CreateStage();
        var song = Score("Test", 1, "/songs/test.dtx");

        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForActiveRoots", new[] { song }, Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void FilterNodesForActiveRoots_WhenRootsDoNotNormalize_ShouldReturnEmptyList()
    {
        var stage = CreateStage();
        var song = Score("Test", 1, "/songs/test.dtx");

        // A NUL character makes SongPathIdentity.TryNormalize fail, so no roots
        // normalize and the filtered list is empty.
        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForActiveRoots", new[] { song }, new[] { "/bad\0root" });

        Assert.Empty(result);
    }

    [Fact]
    public void FilterNodesForActiveRoots_WhenSongIsUnderActiveRoot_ShouldIncludeIt()
    {
        var stage = CreateStage();
        var song = Score("Test", 1, "/library/active/test.dtx");

        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForActiveRoots", new[] { song }, new[] { "/library/active" });

        Assert.Single(result);
        Assert.Same(song, result[0]);
    }

    [Fact]
    public void FilterNodesForActiveRoots_WhenSongIsOutsideActiveRoot_ShouldExcludeIt()
    {
        var stage = CreateStage();
        var active = Score("Active", 1, "/library/active/active.dtx");
        var inactive = Score("Inactive", 2, "/library/inactive/inactive.dtx");

        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForActiveRoots", new[] { active, inactive }, new[] { "/library/active" });

        Assert.Single(result);
        Assert.Same(active, result[0]);
    }

    [Fact]
    public void FilterNodesForAppliedLibrary_WhenNoSnapshotIsApplied_ShouldReturnRootFilteredNodes()
    {
        var stage = CreateStage();
        var song = Score("Test", 1, "/songs/test.dtx");

        // Without an applied snapshot, GetAppliedActiveRoots returns null, so
        // FilterNodesForActiveRoots returns all nodes.
        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForAppliedLibrary", new object[] { new[] { song } });

        Assert.Single(result);
        Assert.Same(song, result[0]);
    }

    [Fact]
    public void FilterNodesForAppliedLibrary_WhenSnapshotHasNoScoreIdentities_ShouldReturnEmptyList()
    {
        var stage = CreateStage();
        var song = Score("Test", 1, "/library/active/test.dtx");
        // Apply a snapshot with only boxes (no score nodes) so the identity set is empty.
        var box = Box("Empty", "/library/active/Empty");
        InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
            Snapshot(30, new[] { box }, new[] { "/library/active" }));

        var result = InvokePrivateMethod<List<SongListNode>>(
            stage, "FilterNodesForAppliedLibrary", new object[] { new[] { song } });

        Assert.Empty(result);
    }

    [Fact]
    public void GetAppliedActiveRoots_WhenNoSnapshotIsApplied_ShouldReturnNull()
    {
        var stage = CreateStage();

        var roots = InvokePrivateMethod<IReadOnlyList<string>?>(
            stage, "GetAppliedActiveRoots");

        Assert.Null(roots);
    }

    [Fact]
    public void GetAppliedActiveRoots_WhenSnapshotIsApplied_ShouldReturnSnapshotRoots()
    {
        var stage = CreateStage();
        InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
            Snapshot(31, Array.Empty<SongListNode>(), new[] { "/library/one", "/library/two" }));

        var roots = InvokePrivateMethod<IReadOnlyList<string>?>(
            stage, "GetAppliedActiveRoots");

        Assert.Equal(new[] { "/library/one", "/library/two" }, roots);
    }

    [Fact]
    public void ClearRemovedSelectionPresentation_ShouldClearSelectionAndPreview()
    {
        var stage = CreateStage();
        var preview = new PreviewImagePanel();
        var statusPanel = new SongStatusPanel { Visible = true };
        AttachCoreUi(stage, previewPanel: preview, statusPanel: statusPanel);
        var song = Score("Selected", 1, "/songs/selected.dtx");
        SetPrivateField(stage, "_selectedSong", song);
        SetPrivateField(stage, "_isInStatusPanel", true);

        InvokePrivateMethod(stage, "ClearRemovedSelectionPresentation");

        Assert.Null(GetPrivateField<SongListNode>(stage, "_selectedSong"));
        Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
        Assert.False(statusPanel.Visible);
    }

    [Fact]
    public void OnSongLibraryPublishedForActivation_WhenPublicationIsInactive_ShouldIgnoreEvent()
    {
        var stage = CreateStage();
        SetPrivateField(stage, "_libraryPublicationActive", 0);
        SetPrivateField(stage, "_pendingLibraryPublicationVersion", 0L);

        InvokePrivateMethod(stage, "OnSongLibraryPublishedForActivation",
            1, new SongLibraryPublishedEventArgs(Snapshot(40, Array.Empty<SongListNode>(), Array.Empty<string>())));

        Assert.Equal(0L, GetPrivateField<long>(stage, "_pendingLibraryPublicationVersion"));
    }

    [Fact]
    public void OnSongLibraryPublishedForActivation_WhenActivationVersionDiffers_ShouldIgnoreEvent()
    {
        var stage = CreateStage();
        SetPrivateField(stage, "_libraryPublicationActive", 1);
        SetPrivateField(stage, "_activationVersion", 5);
        SetPrivateField(stage, "_pendingLibraryPublicationVersion", 0L);

        // A handler captured for activation version 4 must not update version 5's state.
        InvokePrivateMethod(stage, "OnSongLibraryPublishedForActivation",
            4, new SongLibraryPublishedEventArgs(Snapshot(41, Array.Empty<SongListNode>(), Array.Empty<string>())));

        Assert.Equal(0L, GetPrivateField<long>(stage, "_pendingLibraryPublicationVersion"));
    }

    [Fact]
    public void OnSongLibraryPublishedForActivation_WhenPublishedVersionIsOlder_ShouldNotOverwritePendingVersion()
    {
        var stage = CreateStage();
        SetPrivateField(stage, "_libraryPublicationActive", 1);
        SetPrivateField(stage, "_activationVersion", 1);
        SetPrivateField(stage, "_pendingLibraryPublicationVersion", 50L);

        InvokePrivateMethod(stage, "OnSongLibraryPublishedForActivation",
            1, new SongLibraryPublishedEventArgs(Snapshot(45, Array.Empty<SongListNode>(), Array.Empty<string>())));

        Assert.Equal(50L, GetPrivateField<long>(stage, "_pendingLibraryPublicationVersion"));
    }

    [Fact]
    public void GetPublishedScoreIdentities_WhenTreeContainsScores_ShouldCollectSongAndChartIdentities()
    {
        var song = Score("Test", 7, "/library/active/test.dtx");
        var box = Box("Group", "/library/active/Group", song);

        var identities = InvokeStatic<HashSet<string>>(
            "GetPublishedScoreIdentities", new object[] { new[] { box } });

        Assert.Contains("song:7", identities);
        Assert.Contains($"chart:{NormalizePath("/library/active/test.dtx")}", identities);
    }

    [Fact]
    public void GetScoreIdentityCandidates_WhenNodeIsNotAScore_ShouldYieldNoIdentities()
    {
        var box = Box("Folder", "/library/active/Folder");

        var candidates = InvokeStatic<IEnumerable<string>>(
            "GetScoreIdentityCandidates", new object[] { box });

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetScoreIdentityCandidates_WhenScoreHasNoDatabaseId_ShouldUseChartPathIdentity()
    {
        var chart = new SongChart { FilePath = "/library/active/anon.dtx" };
        var song = new SongEntity { Id = 0, Title = "Anon", Charts = new List<SongChart> { chart } };
        chart.Song = song;
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Title = "Anon",
            DatabaseSong = song,
            DatabaseChart = chart,
        };

        var candidates = InvokeStatic<IEnumerable<string>>(
            "GetScoreIdentityCandidates", new object[] { node }).ToList();

        // The chart path is yielded from both DatabaseChart and the song's Charts
        // collection, producing two identical chart: identities.
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.StartsWith("chart:", c));
    }

    [Fact]
    public void ApplyLibrarySnapshotCore_WhenSnapshotIsNull_ShouldThrowArgumentNullException()
    {
        var stage = CreateStage();

        Assert.Throws<TargetInvocationException>(() =>
            InvokePrivateMethod(stage, "ApplyLibrarySnapshotCore", new object?[] { null, true }));
    }

    [Fact]
    public void ReconcileLibrarySnapshot_WhenSnapshotIsNull_ShouldThrowArgumentNullException()
    {
        var stage = CreateStage();

        Assert.Throws<TargetInvocationException>(() =>
            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", new object?[] { null }));
    }

    [Fact]
    public void ResolveLibraryEmptyState_WhenRootsContainScores_ShouldReturnHasSongs()
    {
        var stage = CreateStage();
        var song = Score("Test", 1, "/library/active/test.dtx");
        var snapshot = Snapshot(50, new[] { song }, new[] { "/library/active" });

        var result = InvokePrivateMethod<object>(stage, "ResolveLibraryEmptyState", snapshot);

        Assert.Equal("HasSongs", result!.ToString());
    }

    #region Helpers

    private static SongListNode Box(string title, string directoryPath, params SongListNode[] children)
    {
        var box = new SongListNode
        {
            Type = NodeType.Box,
            Title = title,
            DirectoryPath = directoryPath,
            Children = children.ToList(),
        };
        foreach (var child in box.Children)
            child.Parent = box;
        return box;
    }

    private static SongListNode Score(string title, int songId, string chartPath)
    {
        var chart = new SongChart { FilePath = chartPath };
        var song = new SongEntity { Id = songId, Title = title, Charts = new List<SongChart> { chart } };
        chart.Song = song;
        chart.SongId = songId;
        return new SongListNode
        {
            Type = NodeType.Score,
            Title = title,
            DatabaseSongId = songId,
            DatabaseSong = song,
            DatabaseChart = chart,
        };
    }

    private static SongLibrarySnapshot Snapshot(
        long version,
        IReadOnlyList<SongListNode> roots,
        IReadOnlyList<string> activeRoots) =>
        new(version, roots, activeRoots, enumeratedFileCount: roots.Count, discoveredScoreCount: roots.Count);

    private static string NormalizePath(string path)
    {
        return SongPathIdentity.TryNormalize(path, out var normalized) ? normalized : path;
    }

    private static T InvokeStatic<T>(string name, params object[] args)
    {
        var method = typeof(SongSelectionStage).GetMethod(
            name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    #endregion
}
