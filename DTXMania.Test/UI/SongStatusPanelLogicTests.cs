using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework;
using Moq;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.UI;

public class SongStatusPanelLogicTests
{
    [Fact]
    public void PropertyAccessors_ShouldRoundTripValues()
    {
        var panel = new SongStatusPanel();

        panel.UseStandaloneBPMBackground = true;
        Assert.True(panel.UseStandaloneBPMBackground);

        panel.Font = null;
        Assert.Null(panel.Font);

        panel.SmallFont = null;
        Assert.Null(panel.SmallFont);

        panel.WhitePixel = null;
        Assert.Null(panel.WhitePixel);

        var managedFont = new Mock<IFont>();
        managedFont.SetupGet(x => x.SpriteFont).Returns((Microsoft.Xna.Framework.Graphics.SpriteFont?)null);
        panel.ManagedFont = managedFont.Object;
        Assert.Same(managedFont.Object, panel.ManagedFont);
        Assert.Null(panel.Font);

        var managedSmallFont = new Mock<IFont>();
        managedSmallFont.SetupGet(x => x.SpriteFont).Returns((Microsoft.Xna.Framework.Graphics.SpriteFont?)null);
        panel.ManagedSmallFont = managedSmallFont.Object;
        Assert.Same(managedSmallFont.Object, panel.ManagedSmallFont);
        Assert.Null(panel.SmallFont);

        panel.ManagedFont = null;
        panel.ManagedSmallFont = null;
        Assert.Null(panel.ManagedFont);
        Assert.Null(panel.ManagedSmallFont);
        Assert.Null(panel.Font);
        Assert.Null(panel.SmallFont);
    }

    [Fact]
    public void LayoutPropertyGetters_ShouldReturnPositiveValues()
    {
        var panel = new SongStatusPanel();

        var lineHeight = InvokePrivate<float>(panel, "get_LINE_HEIGHT");
        var sectionSpacing = InvokePrivate<float>(panel, "get_SECTION_SPACING");
        var indent = InvokePrivate<float>(panel, "get_INDENT");

        Assert.True(lineHeight > 0f);
        Assert.True(sectionSpacing >= 0f);
        Assert.True(indent >= 0f);
    }

    [Fact]
    public void InitializeGraphicsGenerator_WithNullRenderTarget_ShouldDisableGenerator()
    {
        var panel = new SongStatusPanel();

        var ex = Record.Exception(() => panel.InitializeGraphicsGenerator(null, null));

        Assert.Null(ex);
        Assert.Null(GetField<object?>(panel, "_graphicsGenerator"));
    }

    [Fact]
    public void InitializeAuthenticGraphics_WithThrowingResourceManager_ShouldNotThrow()
    {
        var panel = new SongStatusPanel();
        var rm = new Mock<IResourceManager>();

        rm.Setup(x => x.LoadTexture(It.IsAny<string>())).Throws(new Exception("load-failed"));

        var ex = Record.Exception(() => panel.InitializeAuthenticGraphics(rm.Object));

        Assert.Null(ex);
        Assert.Same(rm.Object, GetField<IResourceManager>(panel, "_resourceManager"));
        rm.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public void InitializeAuthenticGraphics_WithValidTextures_ShouldAssignLoadedTextures()
    {
        var panel = new SongStatusPanel();
        var rm = new Mock<IResourceManager>();

        var status = new Mock<ITexture>().Object;
        var bpm = new Mock<ITexture>().Object;
        var difficultyPanel = new Mock<ITexture>().Object;
        var difficultyFrame = new Mock<ITexture>().Object;
        var graphDrums = new Mock<ITexture>().Object;
        var graphGb = new Mock<ITexture>().Object;

        rm.Setup(x => x.LoadTexture(TexturePath.SongStatusPanel)).Returns(status);
        rm.Setup(x => x.LoadTexture(TexturePath.BpmBackground)).Returns(bpm);
        rm.Setup(x => x.LoadTexture(TexturePath.DifficultyPanel)).Returns(difficultyPanel);
        rm.Setup(x => x.LoadTexture(TexturePath.DifficultyFrame)).Returns(difficultyFrame);
        rm.Setup(x => x.LoadTexture(TexturePath.GraphPanelDrums)).Returns(graphDrums);
        rm.Setup(x => x.LoadTexture(TexturePath.GraphPanelGuitarBass)).Returns(graphGb);

        panel.InitializeAuthenticGraphics(rm.Object);

        Assert.Same(status, GetField<ITexture>(panel, "_statusPanelTexture"));
        Assert.Same(bpm, GetField<ITexture>(panel, "_bpmBackgroundTexture"));
        Assert.Same(difficultyPanel, GetField<ITexture>(panel, "_difficultyPanelTexture"));
        Assert.Same(difficultyFrame, GetField<ITexture>(panel, "_difficultyFrameTexture"));
        Assert.Same(graphDrums, GetField<ITexture>(panel, "_graphPanelDrumsTexture"));
        Assert.Same(graphGb, GetField<ITexture>(panel, "_graphPanelGuitarBassTexture"));
    }

    [Fact]
    public void FormatDuration_ShouldFormatShortAndLongDurations()
    {
        var panel = new SongStatusPanel();

        var shortText = InvokePrivate<string>(panel, "FormatDuration", 125.0);
        var longText = InvokePrivate<string>(panel, "FormatDuration", 3725.0);

        Assert.Equal("2:05", shortText);
        Assert.Equal("1:02:05", longText);
    }

    [Fact]
    public void GetCurrentScore_ShouldReturnNullForInvalidDifficulty()
    {
        var panel = new SongStatusPanel();
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Scores = [new SongScore { PlayCount = 2 }, null, null, null, null]
        };

        var inRange = InvokePrivate<SongScore?>(panel, "GetCurrentScore", node, 0);
        var negative = InvokePrivate<SongScore?>(panel, "GetCurrentScore", node, -1);
        var outOfRange = InvokePrivate<SongScore?>(panel, "GetCurrentScore", node, 8);

        Assert.NotNull(inRange);
        Assert.Null(negative);
        Assert.Null(outOfRange);
    }

    [Fact]
    public void GetCurrentDifficultyChart_ShouldUseFallbackAndSortedSelection()
    {
        var panel = new SongStatusPanel();

        var fallbackChart = new SongChart { FilePath = "fallback.dtx", DrumLevel = 40, HasDrumChart = true };
        var noChartsSong = new SongEntity { Title = "No charts", Charts = new List<SongChart>() };
        var fallbackNode = new SongListNode { Type = NodeType.Score, DatabaseSong = noChartsSong, DatabaseChart = fallbackChart };

        var fallback = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", fallbackNode, 0);
        Assert.Same(fallbackChart, fallback);

        var c1 = new SongChart { FilePath = "1.dtx", DrumLevel = 20, HasDrumChart = true };
        var c2 = new SongChart { FilePath = "2.dtx", DrumLevel = 60, HasDrumChart = true };
        var c3 = new SongChart { FilePath = "3.dtx", DrumLevel = 40, HasDrumChart = true };
        var song = new SongEntity { Title = "Multi", Charts = new List<SongChart> { c1, c2, c3 } };
        var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song };

        panel.UpdateSongInfo(node, 0);
        var selected0 = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", node, 0);

        panel.UpdateSongInfo(node, 2);
        var selected2 = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", node, 2);

        panel.UpdateSongInfo(node, 9);
        var selected9 = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", node, 9);

        Assert.Equal("1.dtx", selected0?.FilePath);
        Assert.Equal("2.dtx", selected2?.FilePath);
        Assert.Equal("2.dtx", selected9?.FilePath);
    }

    [Fact]
    public void GetCurrentDifficultyChart_WhenSongIsNull_ShouldReturnNull()
    {
        var panel = new SongStatusPanel();

        var selected = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", null, 0);

        Assert.Null(selected);
    }

    [Fact]
    public void GetCurrentDifficultyChart_WhenDatabaseSongIsNull_ShouldReturnNull()
    {
        var panel = new SongStatusPanel();
        var node = new SongListNode { Type = NodeType.Score, DatabaseSong = null, DatabaseChart = null };

        var selected = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", node, 0);

        Assert.Null(selected);
    }

    [Fact]
    public void GetCurrentDifficultyChart_WhenOnlyOneChart_ShouldReturnThatChart()
    {
        var panel = new SongStatusPanel();
        var onlyChart = new SongChart { FilePath = "single.dtx", DrumLevel = 45, HasDrumChart = true };
        var song = new SongEntity { Title = "Single", Charts = new List<SongChart> { onlyChart } };
        var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song };

        var selected = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", node, 3);

        Assert.Same(onlyChart, selected);
    }

    [Fact]
    public void GetCurrentDifficultyChart_WhenNoMatchingInstrumentChart_ShouldFallbackToFirstChart()
    {
        var panel = new SongStatusPanel();
        var first = new SongChart { FilePath = "guitar1.dtx", HasGuitarChart = true, GuitarLevel = 20 };
        var second = new SongChart { FilePath = "guitar2.dtx", HasGuitarChart = true, GuitarLevel = 50 };
        var song = new SongEntity { Title = "No Drum", Charts = new List<SongChart> { first, second } };
        var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song };

        var selected = InvokePrivate<SongChart?>(panel, "GetCurrentDifficultyChart", node, 1);

        Assert.Same(first, selected);
    }

    [Fact]
    public void GetAvailableChartsWithLevels_ShouldProduceInstrumentColumnsAndDeduplicate()
    {
        var panel = new SongStatusPanel();

        var chartA = new SongChart
        {
            FilePath = "a.dtx",
            DifficultyLevel = 2,
            HasDrumChart = true,
            DrumLevel = 35,
            HasBassChart = true,
            BassLevel = 20,
            HasGuitarChart = true,
            GuitarLevel = 25
        };

        var chartB = new SongChart
        {
            FilePath = "b.dtx",
            DifficultyLevel = 2,
            HasDrumChart = true,
            DrumLevel = 50
        };

        var song = new SongEntity { Title = "Song", Charts = new List<SongChart> { chartA, chartB } };
        var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song, DatabaseChart = chartA };
        panel.UpdateSongInfo(node, 0);

        var levels = InvokePrivate<List<SongStatusPanel.ChartLevelInfo>>(panel, "GetAvailableChartsWithLevels");

        Assert.Contains(levels, x => x.InstrumentColumn == 0 && x.InstrumentName == "DRUMS");
        Assert.Contains(levels, x => x.InstrumentColumn == 1 && x.InstrumentName == "BASS");
        Assert.Contains(levels, x => x.InstrumentColumn == 2 && x.InstrumentName == "GUITAR");

        var duplicateKeyCount = levels.GroupBy(x => new { x.Chart.DifficultyLevel, x.InstrumentColumn }).Max(g => g.Count());
        Assert.Equal(1, duplicateKeyCount);
    }

    [Fact]
    public void GetAvailableChartsWithLevels_WhenCurrentSongMissingCharts_ShouldReturnEmptyList()
    {
        var panel = new SongStatusPanel();

        panel.UpdateSongInfo(null, 0);
        var levelsWithNullSong = InvokePrivate<List<SongStatusPanel.ChartLevelInfo>>(panel, "GetAvailableChartsWithLevels");
        Assert.Empty(levelsWithNullSong);

        var nodeWithoutCharts = new SongListNode
        {
            Type = NodeType.Score,
            DatabaseSong = new SongEntity { Title = "NoCharts", Charts = null }
        };
        panel.UpdateSongInfo(nodeWithoutCharts, 0);

        var levelsWithNullCharts = InvokePrivate<List<SongStatusPanel.ChartLevelInfo>>(panel, "GetAvailableChartsWithLevels");
        Assert.Empty(levelsWithNullCharts);
    }

    [Fact]
    public void IsChartSelected_ShouldMatchCurrentInstrumentAndChart()
    {
        var panel = new SongStatusPanel();

        var chart = new SongChart
        {
            FilePath = "drum.dtx",
            DifficultyLevel = 1,
            HasDrumChart = true,
            DrumLevel = 30
        };

        var song = new SongEntity { Title = "Song", Charts = new List<SongChart> { chart } };
        var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song, DatabaseChart = chart };
        panel.UpdateSongInfo(node, 0);

        var selectedInfo = new SongStatusPanel.ChartLevelInfo
        {
            InstrumentName = "DRUMS",
            InstrumentColumn = 0,
            Chart = chart
        };
        var nonSelectedInfo = new SongStatusPanel.ChartLevelInfo
        {
            InstrumentName = "GUITAR",
            InstrumentColumn = 2,
            Chart = chart
        };

        Assert.True(InvokePrivate<bool>(panel, "IsChartSelected", selectedInfo));
        Assert.False(InvokePrivate<bool>(panel, "IsChartSelected", nonSelectedInfo));
    }

    [Fact]
    public void IsChartSelected_WhenInfoIsNullOrChartDiffers_ShouldReturnFalse()
    {
        var panel = new SongStatusPanel();
        var chartA = new SongChart { FilePath = "a.dtx", HasDrumChart = true, DrumLevel = 30 };
        var chartB = new SongChart { FilePath = "b.dtx", HasDrumChart = true, DrumLevel = 50 };
        var song = new SongEntity { Title = "Song", Charts = new List<SongChart> { chartA, chartB } };
        var node = new SongListNode { Type = NodeType.Score, DatabaseSong = song, DatabaseChart = chartA };
        panel.UpdateSongInfo(node, 0);

        var method = typeof(SongStatusPanel).GetMethod("IsChartSelected", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var nullInfoResult = (bool)method!.Invoke(panel, new object?[] { null })!;
        Assert.False(nullInfoResult);

        var mismatchInfo = new SongStatusPanel.ChartLevelInfo
        {
            InstrumentName = "DRUMS",
            InstrumentColumn = 0,
            Chart = chartB
        };
        Assert.False(InvokePrivate<bool>(panel, "IsChartSelected", mismatchInfo));

        panel.UpdateSongInfo(null, 0);
        var noSongInfo = new SongStatusPanel.ChartLevelInfo
        {
            InstrumentName = "DRUMS",
            InstrumentColumn = 0,
            Chart = chartA
        };
        Assert.False(InvokePrivate<bool>(panel, "IsChartSelected", noSongInfo));
    }

    [Fact]
    public void LoadGraphPanelTextures_WhenResourceManagerIsNull_ShouldKeepGraphTexturesNull()
    {
        var panel = new SongStatusPanel();

        SetField(panel, "_resourceManager", null);
        InvokePrivate<object?>(panel, "LoadGraphPanelTextures");

        Assert.Null(GetField<ITexture?>(panel, "_graphPanelDrumsTexture"));
        Assert.Null(GetField<ITexture?>(panel, "_graphPanelGuitarBassTexture"));
    }

    [Fact]
    public void PrivateTextureLoaders_WhenResourceManagerIsNull_ShouldReturnWithoutAssigning()
    {
        var panel = new SongStatusPanel();

        SetField(panel, "_resourceManager", null);
        InvokePrivate<object?>(panel, "LoadBPMBackgroundTexture");
        InvokePrivate<object?>(panel, "LoadDifficultyPanelTexture");
        InvokePrivate<object?>(panel, "LoadDifficultyFrameTexture");

        Assert.Null(GetField<ITexture?>(panel, "_bpmBackgroundTexture"));
        Assert.Null(GetField<ITexture?>(panel, "_difficultyPanelTexture"));
        Assert.Null(GetField<ITexture?>(panel, "_difficultyFrameTexture"));
    }

    [Fact]
    public void LaneColorHelpers_ShouldReturnExpectedFallbackForUnknownLane()
    {
        var panel = new SongStatusPanel();

        var drumKnown = InvokePrivate<Color>(panel, "GetDrumLaneColor", 5);
        var drumUnknown = InvokePrivate<Color>(panel, "GetDrumLaneColor", 999);
        var gbKnown = InvokePrivate<Color>(panel, "GetGuitarBassLaneColor", 1);
        var gbUnknown = InvokePrivate<Color>(panel, "GetGuitarBassLaneColor", 999);

        Assert.Equal(Color.Orange, drumKnown);
        Assert.Equal(Color.White, drumUnknown);
        Assert.Equal(Color.Green, gbKnown);
        Assert.Equal(Color.White, gbUnknown);
    }

    [Fact]
    public void GetInstrumentFromDifficulty_ShouldAlwaysReturnDrums()
    {
        var panel = new SongStatusPanel();

        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 0));
        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 4));
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
