using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.UI;

[Trait("Category", "UI")]
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
        var skillPointPanel = new Mock<ITexture>().Object;
        var skillIcon = new Mock<ITexture>().Object;

        rm.Setup(x => x.LoadTexture(TexturePath.SongStatusPanel)).Returns(status);
        rm.Setup(x => x.LoadTexture(TexturePath.BpmBackground)).Returns(bpm);
        rm.Setup(x => x.LoadTexture(TexturePath.DifficultyPanel)).Returns(difficultyPanel);
        rm.Setup(x => x.LoadTexture(TexturePath.DifficultyFrame)).Returns(difficultyFrame);
        rm.Setup(x => x.LoadTexture(TexturePath.GraphPanelDrums)).Returns(graphDrums);
        rm.Setup(x => x.LoadTexture(TexturePath.GraphPanelGuitarBass)).Returns(graphGb);
        rm.Setup(x => x.LoadTexture(TexturePath.SkillPointPanel)).Returns(skillPointPanel);
        rm.Setup(x => x.LoadTexture(TexturePath.SkillIcon)).Returns(skillIcon);

        panel.InitializeAuthenticGraphics(rm.Object);

        Assert.Same(status, GetField<ITexture>(panel, "_statusPanelTexture"));
        Assert.Same(bpm, GetField<ITexture>(panel, "_bpmBackgroundTexture"));
        Assert.Same(difficultyPanel, GetField<ITexture>(panel, "_difficultyPanelTexture"));
        Assert.Same(difficultyFrame, GetField<ITexture>(panel, "_difficultyFrameTexture"));
        Assert.Same(graphDrums, GetField<ITexture>(panel, "_graphPanelDrumsTexture"));
        Assert.Same(graphGb, GetField<ITexture>(panel, "_graphPanelGuitarBassTexture"));
        Assert.Same(skillPointPanel, GetField<ITexture>(panel, "_skillPointPanelTexture"));
        Assert.Same(skillIcon, GetField<ITexture>(panel, "_skillIconTexture"));
    }

    [Fact]
    public void Dispose_ShouldReleaseManagedTexturesWithRemoveReference()
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
    public void GetCurrentScore_ShouldReturnNullWhenSongOrScoresAreMissing()
    {
        var panel = new SongStatusPanel();
        var nodeWithoutScores = new SongListNode { Type = NodeType.Score, Scores = null };

        Assert.Null(InvokePrivate<SongScore?>(panel, "GetCurrentScore", null!, 0));
        Assert.Null(InvokePrivate<SongScore?>(panel, "GetCurrentScore", nodeWithoutScores, 0));
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
        Assert.Contains(levels, x => x.InstrumentColumn == 1 && x.InstrumentName == "GUITAR");
        Assert.Contains(levels, x => x.InstrumentColumn == 2 && x.InstrumentName == "BASS");

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
    public void SkillTextureLoaders_WhenResourceManagerIsNull_ShouldKeepSkillTexturesNull()
    {
        var panel = new SongStatusPanel();

        SetField(panel, "_resourceManager", null);
        InvokePrivate<object?>(panel, "LoadSkillPointPanelTexture");
        InvokePrivate<object?>(panel, "LoadSkillIconTexture");

        Assert.Null(GetField<ITexture?>(panel, "_skillPointPanelTexture"));
        Assert.Null(GetField<ITexture?>(panel, "_skillIconTexture"));
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

    [Theory]
    [InlineData(0, 128, 0, 128)]
    [InlineData(1, 255, 255, 0)]
    [InlineData(2, 128, 0, 128)]
    [InlineData(3, 255, 0, 0)]
    [InlineData(4, 0, 0, 255)]
    [InlineData(5, 255, 165, 0)]
    [InlineData(6, 0, 0, 255)]
    [InlineData(7, 0, 128, 0)]
    [InlineData(8, 0, 255, 255)]
    public void GetDrumLaneColor_MapsCommonNamedLanes(int lane, byte r, byte g, byte b)
    {
        var panel = new SongStatusPanel();

        var color = InvokePrivate<Color>(panel, "GetDrumLaneColor", lane);

        Assert.Equal(new Color(r, g, b), color);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(1, 0, 128, 0)]
    [InlineData(2, 0, 0, 255)]
    [InlineData(3, 255, 255, 0)]
    [InlineData(4, 128, 0, 128)]
    [InlineData(5, 255, 165, 0)]
    public void GetGuitarBassLaneColor_ShouldMapAllNamedLanes(int lane, byte r, byte g, byte b)
    {
        var panel = new SongStatusPanel();

        var color = InvokePrivate<Color>(panel, "GetGuitarBassLaneColor", lane);

        Assert.Equal(new Color(r, g, b), color);
    }

    [Fact]
    public void GetInstrumentFromDifficulty_ShouldAlwaysReturnDrums()
    {
        var panel = new SongStatusPanel();

        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 0));
        Assert.Equal("DRUMS", InvokePrivate<string>(panel, "GetInstrumentFromDifficulty", 4));
    }

    [Fact]
    public void Draw_WhenScoreSongUsesManagedFontAndTextures_ShouldRenderAuthenticSections()
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            Size = new Vector2(640, 480),
            ManagedFont = font.Object,
            ManagedSmallFont = font.Object,
            WhitePixel = null
        };
        var bpmTexture = CreateTexture(width: 187, height: 67);
        var difficultyPanelTexture = CreateTexture(width: 240, height: 321);
        var difficultyFrameTexture = CreateTexture(width: 80, height: 60);
        var graphTexture = CreateTexture(width: 240, height: 321);
        var skillPointTexture = CreateTexture(width: 187, height: 64);
        var skillIconTexture = CreateTexture(width: 350, height: 53);

        SetField(panel, "_bpmBackgroundTexture", bpmTexture.Object);
        SetField(panel, "_difficultyPanelTexture", difficultyPanelTexture.Object);
        SetField(panel, "_difficultyFrameTexture", difficultyFrameTexture.Object);
        SetField(panel, "_graphPanelDrumsTexture", graphTexture.Object);
        SetField(panel, "_skillPointPanelTexture", skillPointTexture.Object);
        SetField(panel, "_skillIconTexture", skillIconTexture.Object);

        panel.UpdateSongInfo(CreateScoreNodeForDraw(), 0);
        panel.Activate();

        panel.Draw(null!, 0);

        bpmTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        difficultyPanelTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        difficultyFrameTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        graphTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Once);
        skillPointTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        skillIconTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Rectangle?>()), Times.AtLeastOnce);
        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.AtLeast(8));
    }

    [Fact]
    public void Draw_WhenSelectedNodeIsNonScore_ShouldRenderSimplifiedInfo()
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            Size = new Vector2(320, 240),
            ManagedFont = font.Object,
            WhitePixel = null
        };

        panel.UpdateSongInfo(new SongListNode { Type = NodeType.Box, Title = "Folder" }, 0);
        panel.Activate();

        panel.Draw(null!, 0);

        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), "📁 FOLDER", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), "Folder", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
    }

    [Fact]
    public void Draw_WhenSongMissing_ShouldRenderNoSongMessageWithManagedFont()
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            Size = new Vector2(320, 240),
            ManagedFont = font.Object,
            WhitePixel = null
        };
        panel.Activate();

        panel.Draw(null!, 0);

        font.Verify(x => x.MeasureString("No song selected"), Times.Once);
        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), "No song selected", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
    }

    [Fact]
    public void DrawNotesCounter_WhenChartHasNoNotes_ShouldRenderNoNotesMessage()
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            ManagedFont = font.Object
        };
        var chart = new SongChart();

        InvokePrivate<object?>(panel, "DrawNotesCounter", null!, chart, new Vector2(100, 200));

        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), "No notes", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
    }

    [Fact]
    public void DrawNotesCounter_WhenChartIsNull_ShouldReturnWithoutDrawing()
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            ManagedFont = font.Object
        };

        var ex = Record.Exception(() => InvokePrivate<object?>(panel, "DrawNotesCounter", null!, null!, new Vector2(100, 200)));

        Assert.Null(ex);
        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Never);
    }

    [Fact]
    public void DrawNotesCounter_WhenCurrentInstrumentHasNoNotes_ShouldRenderTotalOnlyText()
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            ManagedFont = font.Object
        };
        var chart = new SongChart
        {
            GuitarNoteCount = 123,
            HasGuitarChart = true,
            GuitarLevel = 40
        };

        InvokePrivate<object?>(panel, "DrawNotesCounter", null!, chart, new Vector2(100, 200));

        font.Verify(x => x.MeasureString("123 notes"), Times.Once);
        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), "123 notes", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(NodeType.Score, "♪ SONG")]
    [InlineData(NodeType.BackBox, "⬅ BACK")]
    [InlineData((NodeType)(-1), "UNKNOWN")]
    public void DrawSongTypeInfo_ShouldRenderAdditionalTypeLabels(NodeType nodeType, string expectedLabel)
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            ManagedFont = font.Object
        };
        float x = 10f;
        float y = 20f;

        InvokePrivate<object?>(
            panel,
            "DrawSongTypeInfo",
            null!,
            x,
            y,
            300f,
            new SongListNode { Type = nodeType, Title = "Node" },
            0);

        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), expectedLabel, It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
    }

    [Fact]
    public void DrawNoSongMessage_WhenNoFontsAvailable_ShouldNotThrow()
    {
        var panel = new SongStatusPanel();

        InvokePrivate<object?>(panel, "DrawNoSongMessage", null!, new Rectangle(0, 0, 320, 240));

        Assert.Null(panel.ManagedFont);
        Assert.Null(panel.Font);
        Assert.Null(panel.SmallFont);
    }

    [Fact]
    public void DrawSongTypeInfo_WhenTitleIsLongAndNodeIsRandom_ShouldTruncateAndUseRandomLabel()
    {
        var font = CreateManagedFont();
        var panel = new SongStatusPanel
        {
            ManagedFont = font.Object
        };
        float x = 10f;
        float y = 20f;
        var longTitle = new string('A', 35);

        InvokePrivate<object?>(
            panel,
            "DrawSongTypeInfo",
            null!,
            x,
            y,
            300f,
            new SongListNode { Type = NodeType.Random, Title = longTitle },
            0);

        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), "🎲 RANDOM", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
        font.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), $"{new string('A', 27)}...", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Exactly(2));
    }

    [Fact]
    public void DrawBPMBackground_WhenTextureDisposed_ShouldClearCachedTexture()
    {
        var panel = new SongStatusPanel();
        var bpmTexture = new Mock<ITexture>();
        bpmTexture
            .Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()))
            .Throws(new ObjectDisposedException("bpm"));
        SetField(panel, "_bpmBackgroundTexture", bpmTexture.Object);

        InvokePrivate<object?>(panel, "DrawBPMBackground", (SpriteBatch)null!);

        Assert.Null(GetField<ITexture?>(panel, "_bpmBackgroundTexture"));
    }

    [Fact]
    public void DrawGraphPanelBackground_WhenTextureDisposed_ShouldClearDrumGraphTexture()
    {
        var panel = new SongStatusPanel();
        var graphTexture = new Mock<ITexture>();
        graphTexture.SetupGet(x => x.Width).Returns(220);
        graphTexture.SetupGet(x => x.Height).Returns(180);
        graphTexture
            .Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()))
            .Throws(new ObjectDisposedException("graph"));
        SetField(panel, "_graphPanelDrumsTexture", graphTexture.Object);

        InvokePrivate<object?>(panel, "DrawGraphPanelBackground", null!, new Vector2(1, 2), new Vector2(220, 180));

        Assert.Null(GetField<ITexture?>(panel, "_graphPanelDrumsTexture"));
    }

    [Fact]
    public void DrawBPMBackground_WhenStandaloneEnabled_ShouldDrawAtStandaloneOrigin()
    {
        var panel = new SongStatusPanel { UseStandaloneBPMBackground = true };
        var bpmTexture = new Mock<ITexture>();
        Vector2? drawnPosition = null;
        bpmTexture
            .Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()))
            .Callback<Microsoft.Xna.Framework.Graphics.SpriteBatch, Vector2>((_, position) => drawnPosition = position);
        SetField(panel, "_bpmBackgroundTexture", bpmTexture.Object);

        InvokePrivate<object?>(panel, "DrawBPMBackground", (SpriteBatch)null!);

        Assert.Equal(new Vector2(490, 385), drawnPosition);
    }

    private static Mock<IFont> CreateManagedFont()
    {
        var font = new Mock<IFont>();
        font.SetupGet(x => x.SpriteFont).Returns((Microsoft.Xna.Framework.Graphics.SpriteFont?)null);
        font.Setup(x => x.MeasureString(It.IsAny<string>())).Returns((string text) => new Vector2(text.Length * 8, 16));
        font.Setup(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()));
        return font;
    }

    private static Mock<ITexture> CreateTexture(int width = 64, int height = 64)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(width);
        texture.SetupGet(x => x.Height).Returns(height);
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()));
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Rectangle?>()));
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()));
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<float>(), It.IsAny<Vector2>()));
        return texture;
    }

    private static SongListNode CreateScoreNodeForDraw()
    {
        var chart = new SongChart
        {
            FilePath = "song.dtx",
            Duration = 125,
            Bpm = 180,
            DifficultyLevel = 3,
            DrumLevel = 68,
            HasDrumChart = true,
            DrumNoteCount = 321,
            Scores = new List<SongScore>
            {
                new()
                {
                    Instrument = EInstrumentPart.DRUMS,
                    PlayCount = 7,
                    BestRank = 95,
                    FullCombo = true
                }
            }
        };
        var song = new SongEntity
        {
            Title = "Draw Song",
            Charts = new List<SongChart> { chart }
        };
        chart.Song = song;

        return new SongListNode
        {
            Type = NodeType.Score,
            Title = "Draw Song",
            DatabaseSong = song,
            DatabaseChart = chart,
            Scores =
            [
                new SongScore
                {
                    Instrument = EInstrumentPart.DRUMS,
                    PlayCount = 5,
                    HighSkill = 88.88,
                    BestRank = 95,
                    FullCombo = true
                },
                null,
                null,
                null,
                null
            ]
        };
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
