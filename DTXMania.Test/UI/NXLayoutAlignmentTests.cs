using Xunit;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using System.Collections.Generic;
using System.Linq;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Tests for NX-authentic layout alignment changes.
    /// Covers bar positioning, sizing, texture paths, column order,
    /// title text rendering constants, and scrollbar/counter features.
    /// </summary>
    public class NXLayoutAlignmentTests
    {
        #region Bar Position Constants (Step 1)

        [Fact]
        public void SelectedBarX_ShouldMatchNXValue()
        {
            Assert.Equal(665, SongSelectionUILayout.SongBars.SelectedBarX);
        }

        [Fact]
        public void UnselectedBarX_ShouldMatchNXValue()
        {
            Assert.Equal(673, SongSelectionUILayout.SongBars.UnselectedBarX);
        }

        [Fact]
        public void SelectedBarY_ShouldBe269()
        {
            Assert.Equal(269, SongSelectionUILayout.SongBars.SelectedBarY);
        }

        [Fact]
        public void SelectedBarPosition_ShouldCombineXY()
        {
            var pos = SongSelectionUILayout.SongBars.SelectedBarPosition;
            Assert.Equal(665, (int)pos.X);
            Assert.Equal(269, (int)pos.Y);
        }

        [Fact]
        public void GetBarPosition_CenterIndex_ShouldReturnSelectedPosition()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(SongSelectionUILayout.SongBars.CenterIndex);
            Assert.Equal(665, (int)pos.X);
            Assert.Equal(269, (int)pos.Y);
        }

        [Fact]
        public void GetBarPosition_NonCenterIndex_ShouldUseUnselectedX()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(0);
            Assert.Equal(673, (int)pos.X);
        }

        [Fact]
        public void CenterIndex_ShouldBe5()
        {
            Assert.Equal(5, SongSelectionUILayout.SongBars.CenterIndex);
        }

        [Fact]
        public void VisibleItems_ShouldBe13()
        {
            Assert.Equal(13, SongSelectionUILayout.SongBars.VisibleItems);
        }

        [Fact]
        public void BarCoordinates_ShouldHave13Entries()
        {
            Assert.Equal(13, SongSelectionUILayout.SongBars.BarCoordinates.Length);
        }

        [Fact]
        public void GetBarY_OutOfRange_ShouldReturnZero()
        {
            Assert.Equal(0, SongSelectionUILayout.SongBars.GetBarY(-1));
            Assert.Equal(0, SongSelectionUILayout.SongBars.GetBarY(13));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(12)]
        public void GetBarY_ValidIndex_ShouldReturnCoordinateY(int index)
        {
            var expectedY = SongSelectionUILayout.SongBars.BarCoordinates[index].Y;
            Assert.Equal(expectedY, SongSelectionUILayout.SongBars.GetBarY(index));
        }

        #endregion

        #region Bar Height Constants (Step 2)

        [Fact]
        public void BarHeight_ShouldBe48()
        {
            Assert.Equal(48, SongSelectionUILayout.SongBars.BarHeight);
        }

        [Fact]
        public void SongBarHeight_InTheme_ShouldBe48()
        {
            Assert.Equal(48, DTXManiaVisualTheme.Layout.SongBarHeight);
        }

        [Fact]
        public void BarSize_ShouldUseBarHeightConstant()
        {
            var barSize = SongSelectionUILayout.SongBars.BarSize;
            Assert.Equal(SongSelectionUILayout.SongBars.BarWidth, (int)barSize.X);
            Assert.Equal(SongSelectionUILayout.SongBars.BarHeight, (int)barSize.Y);
        }

        [Fact]
        public void ClearLampHeight_ShouldBe44()
        {
            Assert.Equal(44, SongSelectionUILayout.SongBars.ClearLampHeight);
        }

        #endregion

        #region Preview Image Size (Step 5)

        [Fact]
        public void PreviewImageSize_InLayout_ShouldBe44()
        {
            Assert.Equal(44, SongSelectionUILayout.SongBars.PreviewImageSize);
        }

        [Fact]
        public void PreviewImageSize_InTheme_ShouldBe44()
        {
            Assert.Equal(44, DTXManiaVisualTheme.Layout.PreviewImageSize);
        }

        [Fact]
        public void PreviewImageSize_ShouldBeConsistentBetweenLayoutAndTheme()
        {
            Assert.Equal(
                SongSelectionUILayout.SongBars.PreviewImageSize,
                DTXManiaVisualTheme.Layout.PreviewImageSize);
        }

        #endregion

        #region Texture Path Constants (Step 3)

        [Fact]
        public void BarScore_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_bar score.png", TexturePath.BarScore);
        }

        [Fact]
        public void BarScoreSelected_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_bar score selected.png", TexturePath.BarScoreSelected);
        }

        [Fact]
        public void BarBox_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_bar box.png", TexturePath.BarBox);
        }

        [Fact]
        public void BarBoxSelected_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_bar box selected.png", TexturePath.BarBoxSelected);
        }

        [Fact]
        public void BarOther_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_bar other.png", TexturePath.BarOther);
        }

        [Fact]
        public void BarOtherSelected_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_bar other selected.png", TexturePath.BarOtherSelected);
        }

        [Fact]
        public void PreimagePanel_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_preimage panel.png", TexturePath.PreimagePanel);
        }

        [Fact]
        public void Scrollbar_TexturePath_ShouldBeCorrect()
        {
            Assert.Equal("Graphics/5_scrollbar.png", TexturePath.Scrollbar);
        }

        [Fact]
        public void GetAllTexturePaths_ShouldContainBarTextures()
        {
            var allPaths = TexturePath.GetAllTexturePaths();
            Assert.Contains(TexturePath.BarScore, allPaths);
            Assert.Contains(TexturePath.BarScoreSelected, allPaths);
            Assert.Contains(TexturePath.BarBox, allPaths);
            Assert.Contains(TexturePath.BarBoxSelected, allPaths);
            Assert.Contains(TexturePath.BarOther, allPaths);
            Assert.Contains(TexturePath.BarOtherSelected, allPaths);
            Assert.Contains(TexturePath.PreimagePanel, allPaths);
            Assert.Contains(TexturePath.Scrollbar, allPaths);
        }

        [Fact]
        public void GetPanelTextures_ShouldContainBarTextures()
        {
            var panelPaths = TexturePath.GetPanelTextures();
            Assert.Contains(TexturePath.BarScore, panelPaths);
            Assert.Contains(TexturePath.BarScoreSelected, panelPaths);
            Assert.Contains(TexturePath.PreimagePanel, panelPaths);
            Assert.Contains(TexturePath.Scrollbar, panelPaths);
        }

        #endregion

        #region Title Text Rendering Constants (Step 8)

        [Fact]
        public void TitleTextureWidth_ShouldBe1020_ForTwoXRender()
        {
            Assert.Equal(1020, SongSelectionUILayout.SongBars.TitleTextureWidth);
        }

        [Fact]
        public void TitleTextureHeight_ShouldBe76_ForTwoXRender()
        {
            Assert.Equal(76, SongSelectionUILayout.SongBars.TitleTextureHeight);
        }

        [Fact]
        public void TitleDisplayWidth_ShouldBe510()
        {
            Assert.Equal(510, SongSelectionUILayout.SongBars.TitleDisplayWidth);
        }

        [Fact]
        public void TitleDisplayHeight_ShouldBe38()
        {
            Assert.Equal(38, SongSelectionUILayout.SongBars.TitleDisplayHeight);
        }

        [Fact]
        public void TitleRenderScale_ShouldBe2()
        {
            Assert.Equal(2.0f, SongSelectionUILayout.SongBars.TitleRenderScale);
        }

        [Fact]
        public void TitleDisplayScale_ShouldBe0Point5()
        {
            Assert.Equal(0.5f, SongSelectionUILayout.SongBars.TitleDisplayScale);
        }

        [Fact]
        public void TitleRenderScale_TimesDisplayScale_ShouldEqual1()
        {
            var product = SongSelectionUILayout.SongBars.TitleRenderScale *
                          SongSelectionUILayout.SongBars.TitleDisplayScale;
            Assert.Equal(1.0f, product);
        }

        [Fact]
        public void TitleTextureWidth_ShouldBe_DisplayWidth_TimesRenderScale()
        {
            var expected = (int)(SongSelectionUILayout.SongBars.TitleDisplayWidth *
                                 SongSelectionUILayout.SongBars.TitleRenderScale);
            Assert.Equal(expected, SongSelectionUILayout.SongBars.TitleTextureWidth);
        }

        [Fact]
        public void TitleTextureHeight_ShouldBe_DisplayHeight_TimesRenderScale()
        {
            var expected = (int)(SongSelectionUILayout.SongBars.TitleDisplayHeight *
                                 SongSelectionUILayout.SongBars.TitleRenderScale);
            Assert.Equal(expected, SongSelectionUILayout.SongBars.TitleTextureHeight);
        }

        #endregion

        #region SongBar Size Integration (Steps 1-2)

        [Fact]
        public void SongBar_DefaultSize_ShouldUseNXDimensions()
        {
            var bar = new SongBar();
            Assert.Equal(SongSelectionUILayout.SongBars.BarWidth, (int)bar.Size.X);
            Assert.Equal(DTXManiaVisualTheme.Layout.SongBarHeight, (int)bar.Size.Y);
        }

        [Fact]
        public void SongBar_DefaultSize_HeightShouldBe48()
        {
            var bar = new SongBar();
            Assert.Equal(48, (int)bar.Size.Y);
        }

        [Fact]
        public void SongBar_DefaultSize_WidthShouldBe510()
        {
            var bar = new SongBar();
            Assert.Equal(510, (int)bar.Size.X);
        }

        #endregion

        #region SongListDisplay Item Counter and Scrollbar (Steps 8-10)

        [Fact]
        public void SongListDisplay_WithSongs_ShouldTrackSelectedIndex()
        {
            var display = new SongListDisplay();
            var songs = new List<SongListNode>();
            for (int i = 0; i < 20; i++)
            {
                songs.Add(new SongListNode { Type = NodeType.Score, Title = $"Song {i}" });
            }
            display.CurrentList = songs;

            // Navigate to index 10
            for (int i = 0; i < 10; i++)
                display.MoveNext();

            Assert.Equal(10, display.SelectedIndex);
            Assert.Equal(songs[10], display.SelectedSong);
        }

        [Fact]
        public void SongListDisplay_InfiniteLoop_IndexWrapsCorrectly()
        {
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 0" },
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };
            display.CurrentList = songs;

            // Move forward 5 times (wraps around)
            for (int i = 0; i < 5; i++)
                display.MoveNext();

            // Index = 5, actual = 5 % 3 = 2
            Assert.Equal(5, display.SelectedIndex);
            Assert.Equal(songs[2], display.SelectedSong);
        }

        [Fact]
        public void SongListDisplay_NegativeIndex_WrapsCorrectly()
        {
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 0" },
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };
            display.CurrentList = songs;

            // Move backward 2 times
            display.MovePrevious();
            display.MovePrevious();

            // Index = -2, actual = (-2 % 3 + 3) % 3 = 1
            Assert.Equal(-2, display.SelectedIndex);
            Assert.Equal(songs[1], display.SelectedSong);
        }

        [Fact]
        public void SongListDisplay_ScrollSpeedMultiplier_DefaultIsOne()
        {
            var display = new SongListDisplay();
            Assert.Equal(1.0f, display.ScrollSpeedMultiplier);
        }

        [Fact]
        public void SongListDisplay_ScrollSpeedMultiplier_CanBeSet()
        {
            var display = new SongListDisplay();
            display.ScrollSpeedMultiplier = 2.5f;
            Assert.Equal(2.5f, display.ScrollSpeedMultiplier);
        }

        #endregion

        #region Difficulty Column Order (Step 7)

        [Fact]
        public void SongStatusPanel_Constructor_ShouldInitialize()
        {
            var panel = new SongStatusPanel();
            Assert.NotNull(panel);
            Assert.Equal(new Vector2(580, 320), panel.Size);
        }

        [Fact]
        public void SongStatusPanel_UpdateSongInfo_WithMultiInstrument_ShouldNotThrow()
        {
            // Create a song with drums, guitar, and bass charts
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Multi Instrument Song",
                Artist = "Test"
            };

            var chart = new SongChart
            {
                DrumLevel = 50,
                GuitarLevel = 40,
                BassLevel = 30,
                HasDrumChart = true,
                HasGuitarChart = true,
                HasBassChart = true,
                DifficultyLevel = 1
            };
            song.Charts = new List<SongChart> { chart };

            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Multi Instrument Song",
                DatabaseSong = song,
                DatabaseChart = chart,
                Scores = new SongScore[]
                {
                    new SongScore { Instrument = EInstrumentPart.DRUMS },
                    new SongScore { Instrument = EInstrumentPart.GUITAR },
                    new SongScore { Instrument = EInstrumentPart.BASS }
                }
            };

            var panel = new SongStatusPanel();

            // Should handle all difficulties without throwing
            for (int d = 0; d < 5; d++)
            {
                var ex = Record.Exception(() => panel.UpdateSongInfo(node, d));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void SongStatusPanel_NullSong_ShouldHandleGracefully()
        {
            var panel = new SongStatusPanel();
            var ex = Record.Exception(() => panel.UpdateSongInfo(null, 0));
            Assert.Null(ex);
        }

        [Fact]
        public void SongStatusPanel_NonScoreNode_ShouldHandleGracefully()
        {
            var panel = new SongStatusPanel();
            var boxNode = new SongListNode { Type = NodeType.Box, Title = "Folder" };
            var ex = Record.Exception(() => panel.UpdateSongInfo(boxNode, 0));
            Assert.Null(ex);
        }

        #endregion

        #region PreviewImagePanel (Step 6)

        [Fact]
        public void PreviewImagePanel_Constructor_ShouldInitialize()
        {
            var panel = new PreviewImagePanel();
            Assert.NotNull(panel);
            Assert.True(panel.HasStatusPanel); // Default
        }

        [Fact]
        public void PreviewImagePanel_HasStatusPanel_ShouldAffectSize()
        {
            var panel = new PreviewImagePanel();

            // With status panel
            panel.HasStatusPanel = true;
            Assert.Equal(new Vector2(292, 292), panel.Size);

            // Without status panel
            panel.HasStatusPanel = false;
            Assert.Equal(new Vector2(368, 368), panel.Size);
        }

        [Fact]
        public void PreviewImagePanel_HasStatusPanel_ShouldAffectPosition()
        {
            var panel = new PreviewImagePanel();

            panel.HasStatusPanel = true;
            Assert.Equal(new Vector2(250, 34), panel.Position);

            panel.HasStatusPanel = false;
            Assert.Equal(new Vector2(18, 88), panel.Position);
        }

        [Fact]
        public void PreviewImagePanel_UpdateSelectedSong_WithNull_ShouldNotThrow()
        {
            var panel = new PreviewImagePanel();
            var ex = Record.Exception(() => panel.UpdateSelectedSong(null));
            Assert.Null(ex);
        }

        [Fact]
        public void PreviewImagePanel_UpdateSelectedSong_WithNonScore_ShouldNotThrow()
        {
            var panel = new PreviewImagePanel();
            var boxNode = new SongListNode { Type = NodeType.Box, Title = "Folder" };
            var ex = Record.Exception(() => panel.UpdateSelectedSong(boxNode));
            Assert.Null(ex);
        }

        #endregion

        #region BarType and SongBarInfo (Steps 3-4)

        [Theory]
        [InlineData(BarType.Score)]
        [InlineData(BarType.Box)]
        [InlineData(BarType.Other)]
        public void BarType_AllValues_ShouldBeDefined(BarType barType)
        {
            Assert.True(System.Enum.IsDefined(typeof(BarType), barType));
        }

        [Theory]
        [InlineData(ClearStatus.NotPlayed)]
        [InlineData(ClearStatus.Failed)]
        [InlineData(ClearStatus.Clear)]
        [InlineData(ClearStatus.FullCombo)]
        public void ClearStatus_AllValues_ShouldBeDefined(ClearStatus status)
        {
            Assert.True(System.Enum.IsDefined(typeof(ClearStatus), status));
        }

        [Fact]
        public void SongBarInfo_ShouldBeDisposable()
        {
            var barInfo = new SongBarInfo
            {
                TitleString = "Test",
                BarType = BarType.Score,
                TextColor = Color.White
            };

            // Should not throw on dispose even with null textures
            var ex = Record.Exception(() => barInfo.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void SongBarInfo_AllProperties_ShouldBeSettable()
        {
            var node = new SongListNode { Type = NodeType.Score, Title = "Test" };
            using var barInfo = new SongBarInfo
            {
                SongNode = node,
                BarType = BarType.Box,
                TitleString = "Title",
                TextColor = Color.Cyan,
                DifficultyLevel = 3,
                IsSelected = true
            };

            Assert.Equal(node, barInfo.SongNode);
            Assert.Equal(BarType.Box, barInfo.BarType);
            Assert.Equal("Title", barInfo.TitleString);
            Assert.Equal(Color.Cyan, barInfo.TextColor);
            Assert.Equal(3, barInfo.DifficultyLevel);
            Assert.True(barInfo.IsSelected);
            Assert.Null(barInfo.TitleTexture);
            Assert.Null(barInfo.PreviewImage);
            Assert.Null(barInfo.ClearLamp);
        }

        #endregion

        #region DTXManiaVisualTheme Constants

        [Fact]
        public void Theme_SongBarSpacing_ShouldBe2()
        {
            Assert.Equal(2, DTXManiaVisualTheme.Layout.SongBarSpacing);
        }

        [Fact]
        public void Theme_VisibleSongCount_ShouldBe13()
        {
            Assert.Equal(13, DTXManiaVisualTheme.Layout.VisibleSongCount);
        }

        [Fact]
        public void Theme_ClearLampWidth_ShouldBe8()
        {
            Assert.Equal(8, DTXManiaVisualTheme.Layout.ClearLampWidth);
        }

        [Fact]
        public void Theme_DifficultyColors_ShouldHave5Colors()
        {
            Assert.Equal(5, DTXManiaVisualTheme.SongSelection.DifficultyColors.Length);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Theme_GetDifficultyColor_ValidIndex_ShouldReturnColor(int index)
        {
            var color = DTXManiaVisualTheme.GetDifficultyColor(index);
            Assert.NotEqual(Color.White, color); // All difficulty colors should be distinct from white
        }

        [Fact]
        public void Theme_GetDifficultyColor_InvalidIndex_ShouldReturnWhite()
        {
            Assert.Equal(Color.White, DTXManiaVisualTheme.GetDifficultyColor(-1));
            Assert.Equal(Color.White, DTXManiaVisualTheme.GetDifficultyColor(5));
        }

        [Theory]
        [InlineData(NodeType.Score)]
        [InlineData(NodeType.Box)]
        [InlineData(NodeType.BackBox)]
        [InlineData(NodeType.Random)]
        public void Theme_GetNodeTypeColor_AllTypes_ShouldReturnColor(NodeType type)
        {
            var color = DTXManiaVisualTheme.GetNodeTypeColor(type);
            Assert.IsType<Color>(color);
        }

        [Fact]
        public void Theme_LerpColor_ShouldInterpolate()
        {
            var result = DTXManiaVisualTheme.LerpColor(Color.Black, Color.White, 0.5f);
            // Midpoint should be gray-ish
            Assert.True(result.R > 100 && result.R < 200);
        }

        [Fact]
        public void Theme_LerpColor_ShouldClamp()
        {
            // Amount > 1 should clamp to 1
            var result = DTXManiaVisualTheme.LerpColor(Color.Black, Color.White, 2.0f);
            Assert.Equal(Color.White, result);

            // Amount < 0 should clamp to 0
            result = DTXManiaVisualTheme.LerpColor(Color.Black, Color.White, -1.0f);
            Assert.Equal(Color.Black, result);
        }

        #endregion

        #region Layout Spacing and Animation Constants

        [Fact]
        public void Timing_FadeInDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.FadeInDuration > 0);
        }

        [Fact]
        public void Timing_NavigationDebounceSeconds_ShouldBeSmall()
        {
            Assert.True(SongSelectionUILayout.Timing.NavigationDebounceSeconds < 1.0);
        }

        [Fact]
        public void Audio_PreviewSoundVolume_ShouldBeInRange()
        {
            Assert.InRange(SongSelectionUILayout.Audio.PreviewSoundVolume, 0f, 1f);
        }

        [Fact]
        public void BarWidth_ShouldBe510()
        {
            Assert.Equal(510, SongSelectionUILayout.SongBars.BarWidth);
        }

        [Fact]
        public void CommentBar_Position_ShouldBeDefined()
        {
            var pos = SongSelectionUILayout.CommentBar.Position;
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        [Fact]
        public void DifficultyGrid_CellSize_ShouldBeDefined()
        {
            var cellSize = SongSelectionUILayout.DifficultyGrid.CellSize;
            Assert.True(cellSize.X > 0);
            Assert.True(cellSize.Y > 0);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(4, 0)]
        [InlineData(0, 2)]
        [InlineData(4, 2)]
        public void DifficultyGrid_GetCellPosition_ShouldReturnValidPositions(int diffLevel, int instrument)
        {
            var pos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(diffLevel, instrument);
            // All positions should be positive
            Assert.True(pos.X >= 0);
            Assert.True(pos.Y >= 0);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(4, 2)]
        public void DifficultyGrid_GetCellContentPosition_ShouldBeOffset(int diffLevel, int instrument)
        {
            var cellPos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(diffLevel, instrument);
            var contentPos = SongSelectionUILayout.DifficultyGrid.GetCellContentPosition(diffLevel, instrument);
            // Content position should be 20px below cell position
            Assert.Equal(cellPos.X, contentPos.X);
            Assert.Equal(cellPos.Y + 20, contentPos.Y);
        }

        #endregion

        #region SongListDisplay Events

        [Fact]
        public void SongListDisplay_DifficultyChanged_ShouldFireOnCycle()
        {
            var display = new SongListDisplay();
            var song = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test",
                Scores = new SongScore[]
                {
                    new SongScore { Instrument = EInstrumentPart.DRUMS },
                    new SongScore { Instrument = EInstrumentPart.GUITAR }
                }
            };
            display.CurrentList = new List<SongListNode> { song };

            DifficultyChangedEventArgs args = null;
            display.DifficultyChanged += (s, e) => args = e;

            display.CycleDifficulty();

            Assert.NotNull(args);
            Assert.Equal(1, args.NewDifficulty);
            Assert.Equal(song, args.Song);
        }

        [Fact]
        public void SongListDisplay_SongActivated_ShouldFireOnActivate()
        {
            var display = new SongListDisplay();
            var song = new SongListNode { Type = NodeType.Score, Title = "Test" };
            display.CurrentList = new List<SongListNode> { song };

            SongActivatedEventArgs args = null;
            display.SongActivated += (s, e) => args = e;

            display.ActivateSelected();

            Assert.NotNull(args);
            Assert.Equal(song, args.Song);
            Assert.Equal(0, args.Difficulty);
        }

        [Fact]
        public void SongListDisplay_ActivateSelected_WithNullList_ShouldNotThrow()
        {
            var display = new SongListDisplay();
            display.CurrentList = null;

            var ex = Record.Exception(() => display.ActivateSelected());
            Assert.Null(ex);
        }

        [Fact]
        public void SongListDisplay_MoveNext_WithEmptyList_ShouldNotThrow()
        {
            var display = new SongListDisplay();
            display.CurrentList = new List<SongListNode>();

            var ex = Record.Exception(() => display.MoveNext());
            Assert.Null(ex);
        }

        [Fact]
        public void SongListDisplay_MovePrevious_WithEmptyList_ShouldNotThrow()
        {
            var display = new SongListDisplay();
            display.CurrentList = new List<SongListNode>();

            var ex = Record.Exception(() => display.MovePrevious());
            Assert.Null(ex);
        }

        [Fact]
        public void SongListDisplay_CycleDifficulty_WithNoScores_ShouldNotThrow()
        {
            var display = new SongListDisplay();
            var song = new SongListNode { Type = NodeType.Score, Title = "Test" };
            display.CurrentList = new List<SongListNode> { song };

            var ex = Record.Exception(() => display.CycleDifficulty());
            Assert.Null(ex);
        }

        [Fact]
        public void SongListDisplay_RefreshDisplay_ShouldNotThrow()
        {
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" }
            };
            display.CurrentList = songs;

            var ex = Record.Exception(() => display.RefreshDisplay());
            Assert.Null(ex);
        }

        [Fact]
        public void SongListDisplay_SetEnhancedRendering_ShouldNotThrow()
        {
            var display = new SongListDisplay();
            var ex = Record.Exception(() => display.SetEnhancedRendering(false));
            Assert.Null(ex);
            ex = Record.Exception(() => display.SetEnhancedRendering(true));
            Assert.Null(ex);
        }

        [Fact]
        public void SongListDisplay_InvalidateVisuals_ShouldNotThrow()
        {
            var display = new SongListDisplay();
            var ex = Record.Exception(() => display.InvalidateVisuals());
            Assert.Null(ex);
        }

        #endregion

        #region SongSelectionChangedEventArgs

        [Fact]
        public void SongSelectionChangedEventArgs_ShouldStoreProperties()
        {
            var song = new SongListNode { Type = NodeType.Score, Title = "Test" };
            var args = new SongSelectionChangedEventArgs(song, 2, true);

            Assert.Equal(song, args.SelectedSong);
            Assert.Equal(2, args.CurrentDifficulty);
            Assert.True(args.IsScrollComplete);
        }

        [Fact]
        public void DifficultyChangedEventArgs_ShouldStoreProperties()
        {
            var song = new SongListNode { Type = NodeType.Score, Title = "Test" };
            var args = new DifficultyChangedEventArgs(song, 3);

            Assert.Equal(song, args.Song);
            Assert.Equal(3, args.NewDifficulty);
        }

        [Fact]
        public void SongActivatedEventArgs_ShouldStoreProperties()
        {
            var song = new SongListNode { Type = NodeType.Score, Title = "Test" };
            var args = new SongActivatedEventArgs(song, 1);

            Assert.Equal(song, args.Song);
            Assert.Equal(1, args.Difficulty);
        }

        #endregion
    }
}
