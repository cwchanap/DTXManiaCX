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
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public sealed class SongSelectionStageIdentityTests
{
    [Fact]
    public void GetStableBoxPath_WhenNodeIsBoxWithValidPath_ShouldReturnNormalizedPath()
    {
        var node = Box("BOX", "/library/one/BOX");

        var result = InvokeStatic<string?>("GetStableBoxPath", node);

        Assert.Equal(SongPathIdentity.Normalize("/library/one/BOX"), result);
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

        Assert.Equal($"box:{SongPathIdentity.Normalize("/library/one/BOX")}", result);
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

        Assert.Equal($"chart:{SongPathIdentity.Normalize("/library/song.dtx")}", result);
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

    private static T InvokeStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(SongSelectionStage).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }
}
