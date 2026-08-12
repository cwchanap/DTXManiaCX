using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public sealed class SongSelectionStagePreparedChartTests
{
    [Fact]
    public void ResolvePreparedChart_RootLevelChart_ReturnsExactNodeChartAndDifficulty()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-root");
        var chartPath = Path.Combine(root, "root.dtx");
        var node = CreateNode("same title", chartPath, chartId: 11, instrument: EInstrumentPart.DRUMS);
        var stage = CreateStageWithSnapshot(root, node);

        var resolution = Resolve(stage, chartPath);

        Assert.NotNull(resolution);
        Assert.Same(node, Property<SongListNode>(resolution!, "Node"));
        Assert.Same(node.DatabaseChart, Property<SongChart>(resolution!, "Chart"));
        Assert.Equal(0, Property<int>(resolution!, "DifficultyIndex"));
        Assert.Empty(Property<IReadOnlyList<string>>(resolution!, "AncestorBoxPaths"));
    }

    [Fact]
    public void ResolvePreparedChart_NestedBox_ReturnsAncestorPathsInOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-nested");
        var outerPath = Path.Combine(root, "outer");
        var innerPath = Path.Combine(outerPath, "inner");
        var chartPath = Path.Combine(innerPath, "nested.dtx");
        var node = CreateNode("nested", chartPath, chartId: 12, instrument: EInstrumentPart.DRUMS);
        var inner = Box("same title", innerPath, node);
        var outer = Box("same title", outerPath, inner);
        var stage = CreateStageWithSnapshot(root, outer);

        var resolution = Resolve(stage, chartPath);

        Assert.NotNull(resolution);
        Assert.Equal(
            new[] { Path.GetFullPath(outerPath), Path.GetFullPath(innerPath) },
            Property<IReadOnlyList<string>>(resolution!, "AncestorBoxPaths"));
        Assert.Same(node, Property<SongListNode>(resolution!, "Node"));
    }

    [Fact]
    public void ResolvePreparedChart_WhenRequestedChartIsNotDatabaseChart_UsesSongChartsByPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-set");
        var primaryPath = Path.Combine(root, "primary.dtx");
        var requestedPath = Path.Combine(root, "requested.dtx");
        var primary = Chart(21, primaryPath, drums: 2);
        var requested = Chart(22, requestedPath, drums: 7);
        var song = new SongEntity { Id = 21, Title = "set", Charts = new List<SongChart> { primary, requested } };
        primary.Song = song;
        requested.Song = song;
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Title = "set",
            DatabaseSong = song,
            DatabaseChart = primary,
            Scores = Scores((22, EInstrumentPart.DRUMS))
        };
        var stage = CreateStageWithSnapshot(root, node);

        var resolution = Resolve(stage, requestedPath);

        Assert.NotNull(resolution);
        Assert.Same(requested, Property<SongChart>(resolution!, "Chart"));
        Assert.Equal(0, Property<int>(resolution!, "DifficultyIndex"));
    }

    [Fact]
    public void ResolvePreparedChart_DuplicateTitlesAtDifferentPaths_UsesOnlyRequestedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-duplicates");
        var first = CreateNode("duplicate", Path.Combine(root, "one", "chart.dtx"), 31, EInstrumentPart.DRUMS);
        var second = CreateNode("duplicate", Path.Combine(root, "two", "chart.dtx"), 32, EInstrumentPart.DRUMS);
        var stage = CreateStageWithSnapshot(root, first, second);

        var resolution = Resolve(stage, second.DatabaseChart!.FilePath);

        Assert.NotNull(resolution);
        Assert.Same(second, Property<SongListNode>(resolution!, "Node"));
        Assert.NotSame(first, Property<SongListNode>(resolution!, "Node"));
    }

    [Fact]
    public void ResolvePreparedChart_OutsideActiveRoot_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-active");
        var outsidePath = Path.Combine(Path.GetTempPath(), "hpa510-outside", "chart.dtx");
        var node = CreateNode("outside", outsidePath, 41, EInstrumentPart.DRUMS);
        var stage = CreateStageWithSnapshot(root, node);

        Assert.Null(Resolve(stage, outsidePath));
    }

    [Fact]
    public void ResolvePreparedChart_ActiveRootButUnindexedPath_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-unindexed");
        var requestedPath = Path.Combine(root, "not-indexed.dtx");
        var indexed = CreateNode("indexed", Path.Combine(root, "indexed.dtx"), 51, EInstrumentPart.DRUMS);
        var stage = CreateStageWithSnapshot(root, indexed);

        Assert.Null(Resolve(stage, requestedPath));
    }

    [Fact]
    public void ResolvePreparedChart_OrdinaryMultiInstrumentRowWithSharedChartId_PrefersDrumsSlot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-shared-id");
        var chartPath = Path.Combine(root, "shared.dtx");
        var node = CreateNode(
            "shared",
            chartPath,
            chartId: 61,
            instrument: EInstrumentPart.GUITAR,
            scores: Scores(
                (61, EInstrumentPart.GUITAR),
                (61, EInstrumentPart.DRUMS),
                (61, EInstrumentPart.BASS)));
        var stage = CreateStageWithSnapshot(root, node);

        var resolution = Resolve(stage, chartPath);

        Assert.NotNull(resolution);
        Assert.Equal(1, Property<int>(resolution!, "DifficultyIndex"));
    }

    [Fact]
    public void ResolvePreparedChart_WhenDifficultyCannotBeDisambiguated_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-ambiguous");
        var chartPath = Path.Combine(root, "ambiguous.dtx");
        var node = CreateNode(
            "ambiguous",
            chartPath,
            chartId: 71,
            instrument: EInstrumentPart.GUITAR,
            scores: Scores(
                (71, EInstrumentPart.GUITAR),
                (71, EInstrumentPart.BASS)));
        var stage = CreateStageWithSnapshot(root, node);

        Assert.Null(Resolve(stage, chartPath));
    }

    [Fact]
    public void ResolvePreparedChart_BlankOrRelativePath_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-validation");
        var node = CreateNode("song", Path.Combine(root, "song.dtx"), 81, EInstrumentPart.DRUMS);
        var stage = CreateStageWithSnapshot(root, node);

        Assert.Null(Resolve(stage, " "));
        Assert.Null(Resolve(stage, "relative/song.dtx"));
    }

    [Fact]
    public void ProjectPreparedChartSelection_RebuildsHierarchyOnceAndSelectsResolvedRow()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-projection");
        var boxPath = Path.Combine(root, "box");
        var chartPath = Path.Combine(boxPath, "prepared.dtx");
        var node = CreateNode(
            "prepared",
            chartPath,
            chartId: 91,
            instrument: EInstrumentPart.DRUMS,
            scores: Scores((91, EInstrumentPart.DRUMS), (91, EInstrumentPart.GUITAR)));
        var box = Box("box", boxPath, node);
        var stage = CreateStageWithSnapshot(root, box);
        var display = new SongListDisplay();
        AttachCoreUi(stage, display: display);
        SetPrivateField(stage, "_currentSongList", new List<SongListNode> { box });

        var resolution = Resolve(stage, chartPath);
        Assert.NotNull(resolution);

        var projected = InvokePrivateMethod<bool>(stage, "ProjectPreparedChartSelection", resolution!);

        Assert.True(projected);
        Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        Assert.Null(GetPrivateField<object>(stage, "_filteredView"));
        Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
        Assert.Same(node, display.SelectedSong);
        Assert.Equal(0, display.CurrentDifficulty);
        Assert.False(GetPrivateField<bool>(stage, "_isProjectingPreparedSelection"));
    }

    [Fact]
    public void SetSelection_AppliesRowDifficultyAndRaisesOneSelectionEvent()
    {
        var first = new SongListNode { Type = NodeType.Score, Title = "first" };
        var second = new SongListNode { Type = NodeType.Score, Title = "second" };
        var display = new SongListDisplay { CurrentList = new List<SongListNode> { first, second } };
        var events = 0;
        SongSelectionChangedEventArgs? args = null;
        display.SelectionChanged += (_, eventArgs) =>
        {
            events++;
            args = eventArgs;
        };

        InvokePrivateMethod(display, "SetSelection", 1, 99);

        Assert.Equal(1, events);
        Assert.Same(second, display.SelectedSong);
        Assert.Equal(1, display.SelectedIndex);
        Assert.Equal(4, display.CurrentDifficulty);
        Assert.NotNull(args);
        Assert.Same(second, args!.SelectedSong);
        Assert.Equal(4, args.CurrentDifficulty);
        Assert.True(args.IsScrollComplete);
        Assert.Equal(
            GetPrivateField<int>(display, "_targetScrollCounter"),
            GetPrivateField<int>(display, "_currentScrollCounter"));
    }

    [Fact]
    public void SetSelection_InvalidIndex_DoesNotChangeSelectionOrRaiseEvent()
    {
        var first = new SongListNode { Type = NodeType.Score, Title = "first" };
        var display = new SongListDisplay { CurrentList = new List<SongListNode> { first } };
        var events = 0;
        display.SelectionChanged += (_, _) => events++;

        InvokePrivateMethod(display, "SetSelection", -1, 2);
        InvokePrivateMethod(display, "SetSelection", 1, 2);

        Assert.Equal(0, events);
        Assert.Same(first, display.SelectedSong);
        Assert.Equal(0, display.SelectedIndex);
        Assert.Equal(0, display.CurrentDifficulty);
    }

    private static SongSelectionStage CreateStageWithSnapshot(string activeRoot, params SongListNode[] roots)
    {
        var stage = CreateStage();
        SetPrivateField(stage, "_appliedLibrarySnapshot", new SongLibrarySnapshot(
            version: 1,
            rootSongs: roots,
            activeRoots: new[] { Path.GetFullPath(activeRoot) },
            enumeratedFileCount: roots.Length,
            discoveredScoreCount: roots.Length));
        return stage;
    }

    private static object? Resolve(SongSelectionStage stage, string path) =>
        InvokePrivateMethod(stage, "ResolvePreparedChart", path);

    private static T Property<T>(object value, string name) =>
        (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private static SongListNode CreateNode(
        string title,
        string chartPath,
        int chartId,
        EInstrumentPart instrument,
        SongScore[]? scores = null)
    {
        var chart = Chart(chartId, chartPath, drums: instrument == EInstrumentPart.DRUMS ? 5 : 0);
        var song = new SongEntity { Id = chartId, Title = title, Charts = new List<SongChart> { chart } };
        chart.Song = song;
        chart.SongId = song.Id;
        return new SongListNode
        {
            Type = NodeType.Score,
            Title = title,
            DatabaseSong = song,
            DatabaseSongId = song.Id,
            DatabaseChart = chart,
            Scores = scores ?? Scores((chartId, instrument))
        };
    }

    private static SongChart Chart(int id, string path, int drums) => new()
    {
        Id = id,
        FilePath = path,
        HasDrumChart = drums > 0,
        DrumLevel = drums
    };

    private static SongScore[] Scores(params (int ChartId, EInstrumentPart Instrument)[] values)
    {
        var scores = new SongScore[5];
        for (var index = 0; index < values.Length; index++)
        {
            scores[index] = new SongScore
            {
                ChartId = values[index].ChartId,
                Instrument = values[index].Instrument
            };
        }

        return scores;
    }
}
