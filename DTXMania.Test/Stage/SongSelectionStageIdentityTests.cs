#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public sealed class SongSelectionStageIdentityTests
{
    [Fact]
    public void GetStableBoxPath_WhenNodeIsBoxWithValidPath_ShouldReturnNormalizedPath()
    {
        var node = Box("BOX", "/library/one/BOX");

        var result = InvokeStatic<string?>("GetStableBoxPath", node);

        Assert.Equal("/library/one/BOX", result);
    }

    [Fact]
    public void GetStableBoxPath_WhenNodeIsNull_ShouldReturnNull()
    {
        var result = InvokeStatic<string?>("GetStableBoxPath", new object?[] { null });

        Assert.Null(result);
    }

    [Fact]
    public void GetStableBoxPath_WhenNodeIsNotBox_ShouldReturnNull()
    {
        var node = Score("Song", 1, "/library/song.dtx");

        var result = InvokeStatic<string?>("GetStableBoxPath", node);

        Assert.Null(result);
    }

    [Fact]
    public void GetStableBoxPath_WhenBoxPathCannotBeNormalized_ShouldReturnNull()
    {
        var node = Box("BOX", "/bad\0path");

        var result = InvokeStatic<string?>("GetStableBoxPath", node);

        Assert.Null(result);
    }

    [Fact]
    public void GetStableSelectionIdentity_WhenBoxNode_ShouldReturnBoxIdentity()
    {
        var node = Box("BOX", "/library/one/BOX");

        var result = InvokeStatic<string?>("GetStableSelectionIdentity", node);

        Assert.Equal("box:/library/one/BOX", result);
    }

    [Fact]
    public void GetStableSelectionIdentity_WhenScoreWithDatabaseId_ShouldReturnSongIdentity()
    {
        var node = Score("Song", 42, "/library/song.dtx");

        var result = InvokeStatic<string?>("GetStableSelectionIdentity", node);

        Assert.Equal("song:42", result);
    }

    [Fact]
    public void GetStableSelectionIdentity_WhenScoreWithoutDatabaseId_ShouldReturnChartIdentity()
    {
        var chart = new SongChart { FilePath = "/library/song.dtx" };
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Title = "Song",
            DatabaseChart = chart,
        };

        var result = InvokeStatic<string?>("GetStableSelectionIdentity", node);

        Assert.Equal("chart:/library/song.dtx", result);
    }

    [Fact]
    public void GetStableSelectionIdentity_WhenNodeIsNull_ShouldReturnNull()
    {
        var result = InvokeStatic<string?>("GetStableSelectionIdentity", new object?[] { null });

        Assert.Null(result);
    }

    [Fact]
    public void GetStableSelectionIdentity_WhenScoreHasNoIdOrChartPath_ShouldReturnNull()
    {
        var node = new SongListNode { Type = NodeType.Score, Title = "Empty" };

        var result = InvokeStatic<string?>("GetStableSelectionIdentity", node);

        Assert.Null(result);
    }

    [Fact]
    public void GetScoreIdentityCandidates_WhenScoreWithDatabaseId_ShouldYieldSongAndChartIdentities()
    {
        var node = Score("Song", 42, "/library/song.dtx");

        var candidates = InvokeStatic<IEnumerable<string>>("GetScoreIdentityCandidates", node)
            .ToList();

        Assert.Contains("song:42", candidates);
        Assert.Contains("chart:/library/song.dtx", candidates);
    }

    [Fact]
    public void GetScoreIdentityCandidates_WhenScoreWithoutDatabaseId_ShouldYieldOnlyChartIdentity()
    {
        var chart = new SongChart { FilePath = "/library/song.dtx" };
        var node = new SongListNode { Type = NodeType.Score, DatabaseChart = chart };

        var candidates = InvokeStatic<IEnumerable<string>>("GetScoreIdentityCandidates", node)
            .ToList();

        Assert.Single(candidates);
        Assert.Equal("chart:/library/song.dtx", candidates[0]);
    }

    [Fact]
    public void GetScoreIdentityCandidates_WhenNodeIsNotScore_ShouldYieldNothing()
    {
        var node = Box("BOX", "/library/BOX");

        var candidates = InvokeStatic<IEnumerable<string>>("GetScoreIdentityCandidates", node)
            .ToList();

        Assert.Empty(candidates);
    }

    [Fact]
    public void HasPublishedScoreIdentity_WhenIdentityIsPublished_ShouldReturnTrue()
    {
        var node = Score("Song", 42, "/library/song.dtx");
        var identities = new HashSet<string>(StringComparer.Ordinal) { "song:42" };

        var result = InvokeStatic<bool>("HasPublishedScoreIdentity", node, identities);

        Assert.True(result);
    }

    [Fact]
    public void HasPublishedScoreIdentity_WhenIdentityIsNotPublished_ShouldReturnFalse()
    {
        var node = Score("Song", 42, "/library/song.dtx");
        var identities = new HashSet<string>(StringComparer.Ordinal) { "song:99" };

        var result = InvokeStatic<bool>("HasPublishedScoreIdentity", node, identities);

        Assert.False(result);
    }

    [Fact]
    public void GetNodeChartPaths_WhenNodeHasDatabaseChart_ShouldReturnChartPath()
    {
        var chart = new SongChart { FilePath = "/library/song.dtx" };
        var node = new SongListNode { DatabaseChart = chart };

        var paths = InvokeStatic<IEnumerable<string>>("GetNodeChartPaths", node)
            .ToList();

        Assert.Single(paths);
        Assert.Equal("/library/song.dtx", paths[0]);
    }

    [Fact]
    public void GetNodeChartPaths_WhenNodeHasSongWithMultipleCharts_ShouldReturnAllChartPaths()
    {
        var song = new SongEntity
        {
            Charts = new List<SongChart>
            {
                new() { FilePath = "/library/song1.dtx" },
                new() { FilePath = "/library/song2.dtx" },
            },
        };
        var node = new SongListNode { DatabaseSong = song };

        var paths = InvokeStatic<IEnumerable<string>>("GetNodeChartPaths", node)
            .ToList();

        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public void GetNodeChartPaths_WhenNodeHasOnlyDirectoryPath_ShouldReturnDirectoryPath()
    {
        var node = new SongListNode { DirectoryPath = "/library/box" };

        var paths = InvokeStatic<IEnumerable<string>>("GetNodeChartPaths", node)
            .ToList();

        Assert.Single(paths);
        Assert.Equal("/library/box", paths[0]);
    }

    [Fact]
    public void NodeIsUnderAnyActiveRoot_WhenChartUnderRoot_ShouldReturnTrue()
    {
        var node = Score("Song", 1, "/library/songs/song.dtx");
        var roots = new[] { "/library/songs" };

        var result = InvokeStatic<bool>("NodeIsUnderAnyActiveRoot", node, roots);

        Assert.True(result);
    }

    [Fact]
    public void NodeIsUnderAnyActiveRoot_WhenChartOutsideRoots_ShouldReturnFalse()
    {
        var node = Score("Song", 1, "/other/song.dtx");
        var roots = new[] { "/library/songs" };

        var result = InvokeStatic<bool>("NodeIsUnderAnyActiveRoot", node, roots);

        Assert.False(result);
    }

    [Fact]
    public void NodeIsUnderAnyActiveRoot_WhenChartPathCannotBeNormalized_ShouldSkipThatPath()
    {
        var chart = new SongChart { FilePath = "/bad\0path" };
        var node = new SongListNode
        {
            Type = NodeType.Score,
            DatabaseChart = chart,
            DirectoryPath = "/library/songs/fallback",
        };
        var roots = new[] { "/library/songs" };

        var result = InvokeStatic<bool>("NodeIsUnderAnyActiveRoot", node, roots);

        Assert.True(result);
    }

    [Fact]
    public void ContainsPublishedScore_WhenTreeHasScore_ShouldReturnTrue()
    {
        var song = Score("Song", 1, "/song.dtx");
        var box = Box("BOX", "/library/BOX", song);

        var result = InvokeStatic<bool>("ContainsPublishedScore", new object[] { new[] { box } });

        Assert.True(result);
    }

    [Fact]
    public void ContainsPublishedScore_WhenTreeOnlyHasEmptyBoxes_ShouldReturnFalse()
    {
        var box = Box("BOX", "/library/BOX");

        var result = InvokeStatic<bool>("ContainsPublishedScore", new object[] { new[] { box } });

        Assert.False(result);
    }

    [Fact]
    public void ContainsPublishedScore_WhenNodesAreEmpty_ShouldReturnFalse()
    {
        var result = InvokeStatic<bool>(
            "ContainsPublishedScore",
            new object[] { Array.Empty<SongListNode>() });

        Assert.False(result);
    }

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

    private static T InvokeStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(SongSelectionStage).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }
}
