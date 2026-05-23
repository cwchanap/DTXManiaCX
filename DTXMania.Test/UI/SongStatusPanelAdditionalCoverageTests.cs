using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class SongStatusPanelAdditionalCoverageTests
{
    [Fact]
    public void UpdateSongInfo_ShouldUpdateCurrentSongAndDifficulty()
    {
        var panel = new SongStatusPanel();
        var song = new SongListNode { Type = NodeType.Score, Title = "TestSong" };

        panel.UpdateSongInfo(song, 3);

        Assert.Same(song, GetField<SongListNode>(panel, "_currentSong"));
        Assert.Equal(3, GetField<int>(panel, "_currentDifficulty"));
    }

    [Fact]
    public void UpdateSongInfo_WithNull_ShouldClearCurrentSong()
    {
        var panel = new SongStatusPanel();
        var song = new SongListNode { Type = NodeType.Score, Title = "TestSong" };
        panel.UpdateSongInfo(song, 2);

        panel.UpdateSongInfo(null, 0);

        Assert.Null(GetField<SongListNode>(panel, "_currentSong"));
        Assert.Equal(0, GetField<int>(panel, "_currentDifficulty"));
    }

    [Fact]
    public void Dispose_ShouldReleaseAllManagedTextures()
    {
        var panel = new SongStatusPanel();

        var status = new Mock<ITexture>();
        var bpm = new Mock<ITexture>();
        var difficultyPanel = new Mock<ITexture>();
        var difficultyFrame = new Mock<ITexture>();
        var graphDrums = new Mock<ITexture>();
        var graphGb = new Mock<ITexture>();
        var skillPointPanel = new Mock<ITexture>();
        var skillIcon = new Mock<ITexture>();

        SetField(panel, "_statusPanelTexture", status.Object);
        SetField(panel, "_bpmBackgroundTexture", bpm.Object);
        SetField(panel, "_difficultyPanelTexture", difficultyPanel.Object);
        SetField(panel, "_difficultyFrameTexture", difficultyFrame.Object);
        SetField(panel, "_graphPanelDrumsTexture", graphDrums.Object);
        SetField(panel, "_graphPanelGuitarBassTexture", graphGb.Object);
        SetField(panel, "_skillPointPanelTexture", skillPointPanel.Object);
        SetField(panel, "_skillIconTexture", skillIcon.Object);

        panel.Dispose();

        status.Verify(x => x.RemoveReference(), Times.Once);
        bpm.Verify(x => x.RemoveReference(), Times.Once);
        difficultyPanel.Verify(x => x.RemoveReference(), Times.Once);
        difficultyFrame.Verify(x => x.RemoveReference(), Times.Once);
        graphDrums.Verify(x => x.RemoveReference(), Times.Once);
        graphGb.Verify(x => x.RemoveReference(), Times.Once);
        skillPointPanel.Verify(x => x.RemoveReference(), Times.Once);
        skillIcon.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void InitializeAuthenticGraphics_WithNullResourceManager_ShouldNotThrow()
    {
        var panel = new SongStatusPanel();

        var exception = Record.Exception(() => panel.InitializeAuthenticGraphics(null!));

        Assert.Null(exception);
    }

    [Fact]
    public void FolderHint_SetShouldUpdateProperty()
    {
        var panel = new SongStatusPanel();

        panel.FolderHint = "Rock > Metal";
        Assert.Equal("Rock > Metal", panel.FolderHint);

        panel.FolderHint = "";
        Assert.Equal("", panel.FolderHint);
    }

    [Fact]
    public void UseStandaloneBPMBackground_SetShouldUpdateProperty()
    {
        var panel = new SongStatusPanel();

        panel.UseStandaloneBPMBackground = true;
        Assert.True(panel.UseStandaloneBPMBackground);

        panel.UseStandaloneBPMBackground = false;
        Assert.False(panel.UseStandaloneBPMBackground);
    }

    [Fact]
    public void GetBpmBackgroundOrigin_WhenStandalone_ShouldUseStandalonePosition()
    {
        var panel = new SongStatusPanel { UseStandaloneBPMBackground = true };

        var origin = InvokePrivate<Vector2>(panel, "GetBpmBackgroundOrigin");

        Assert.Equal(new Vector2(490, 385), origin);
    }

    [Fact]
    public void GetBpmBackgroundOrigin_WhenNotStandalone_ShouldUseLayoutPosition()
    {
        var panel = new SongStatusPanel { UseStandaloneBPMBackground = false };

        var origin = InvokePrivate<Vector2>(panel, "GetBpmBackgroundOrigin");

        Assert.Equal(SongSelectionUILayout.BPMSection.Position, origin);
    }

    [Fact]
    public void GetInstrumentFromDifficulty_ShouldReturnCorrectInstrument()
    {
        var panel = new SongStatusPanel();

        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 0));
        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 1));
        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 2));
        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 3));
        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 4));
    }

    [Fact]
    public void Dispose_WhenTexturesNull_ShouldNotThrow()
    {
        var panel = new SongStatusPanel();
        SetField(panel, "_statusPanelTexture", null);
        SetField(panel, "_bpmBackgroundTexture", null);
        SetField(panel, "_difficultyPanelTexture", null);
        SetField(panel, "_difficultyFrameTexture", null);
        SetField(panel, "_graphPanelDrumsTexture", null);
        SetField(panel, "_graphPanelGuitarBassTexture", null);
        SetField(panel, "_skillPointPanelTexture", null);
        SetField(panel, "_skillIconTexture", null);

        var exception = Record.Exception(() => panel.Dispose());

        Assert.Null(exception);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(target, args)!;
    }

    private static T GetField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(target)!;
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
