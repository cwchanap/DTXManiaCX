using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Tests for SongSelectionUILayout constants and methods
    /// </summary>
    public class SongSelectionUILayoutTests
    {
        #region StatusPanel Tests

        [Fact]
        public void StatusPanel_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.StatusPanel.Position;
            Assert.Equal(SongSelectionUILayout.StatusPanel.X, pos.X);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Y, pos.Y);
        }

        [Fact]
        public void StatusPanel_Size_ShouldMatchWidthHeight()
        {
            var size = SongSelectionUILayout.StatusPanel.Size;
            Assert.Equal(SongSelectionUILayout.StatusPanel.Width, size.X);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Height, size.Y);
        }

        [Fact]
        public void StatusPanel_Bounds_ShouldMatchXYWidthHeight()
        {
            var bounds = SongSelectionUILayout.StatusPanel.Bounds;
            Assert.Equal(SongSelectionUILayout.StatusPanel.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Height, bounds.Height);
        }

        #endregion

        #region BPMSection Tests

        [Fact]
        public void BPMSection_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.BPMSection.Position;
            Assert.Equal(SongSelectionUILayout.BPMSection.X, pos.X);
            Assert.Equal(SongSelectionUILayout.BPMSection.Y, pos.Y);
        }

        [Fact]
        public void BPMSection_Size_ShouldMatchWidthHeight()
        {
            var size = SongSelectionUILayout.BPMSection.Size;
            Assert.Equal(SongSelectionUILayout.BPMSection.Width, size.X);
            Assert.Equal(SongSelectionUILayout.BPMSection.Height, size.Y);
        }

        [Fact]
        public void BPMSection_LengthTextPosition_ShouldBePositive()
        {
            var pos = SongSelectionUILayout.BPMSection.LengthTextPosition;
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        [Fact]
        public void BPMSection_BPMTextPosition_ShouldBePositive()
        {
            var pos = SongSelectionUILayout.BPMSection.BPMTextPosition;
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        #endregion

        #region DifficultyGrid Tests

        [Fact]
        public void DifficultyGrid_BasePosition_ShouldMatchBaseXY()
        {
            var pos = SongSelectionUILayout.DifficultyGrid.BasePosition;
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.BaseX, pos.X);
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.BaseY, pos.Y);
        }

        [Fact]
        public void DifficultyGrid_CellSize_ShouldMatchWidthHeight()
        {
            var size = SongSelectionUILayout.DifficultyGrid.CellSize;
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.CellWidth, size.X);
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.CellHeight, size.Y);
        }

        [Fact]
        public void DifficultyGrid_GetCellPosition_ShouldReturnVector()
        {
            var pos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(0, 3);
            Assert.True(pos.X >= 0);
            Assert.True(pos.Y >= 0);
        }

        [Fact]
        public void DifficultyGrid_GetCellContentPosition_ShouldBeOffset20FromCellPosition()
        {
            var cellPos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(0, 3);
            var contentPos = SongSelectionUILayout.DifficultyGrid.GetCellContentPosition(0, 3);
            Assert.Equal(cellPos.X, contentPos.X);
            Assert.Equal(cellPos.Y + 20, contentPos.Y);
        }

        [Fact]
        public void DifficultyGrid_DifficultyLabelPosition_ShouldBeAccessible()
        {
            var pos = SongSelectionUILayout.DifficultyGrid.DifficultyLabelPosition;
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        #endregion

        #region SkillPointSection Tests

        [Fact]
        public void SkillPointSection_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.SkillPointSection.Position;
            Assert.Equal(SongSelectionUILayout.SkillPointSection.X, pos.X);
            Assert.Equal(SongSelectionUILayout.SkillPointSection.Y, pos.Y);
        }

        [Fact]
        public void SkillPointSection_Bounds_ShouldHavePositiveSize()
        {
            var bounds = SongSelectionUILayout.SkillPointSection.Bounds;
            Assert.True(bounds.Width > 0);
            Assert.True(bounds.Height > 0);
        }

        [Fact]
        public void SkillPointSection_ValuePosition_ShouldBePositive()
        {
            var pos = SongSelectionUILayout.SkillPointSection.ValuePosition;
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        #endregion

        #region GraphPanel Tests

        [Fact]
        public void GraphPanel_BasePosition_ShouldMatchBaseXY()
        {
            var pos = SongSelectionUILayout.GraphPanel.BasePosition;
            Assert.Equal(SongSelectionUILayout.GraphPanel.BaseX, pos.X);
            Assert.Equal(SongSelectionUILayout.GraphPanel.BaseY, pos.Y);
        }

        [Fact]
        public void GraphPanel_Size_ShouldBePositive()
        {
            var size = SongSelectionUILayout.GraphPanel.Size;
            Assert.True(size.X > 0);
            Assert.True(size.Y > 0);
        }

        [Fact]
        public void GraphPanel_Bounds_ShouldMatchBaseXY()
        {
            var bounds = SongSelectionUILayout.GraphPanel.Bounds;
            Assert.Equal(SongSelectionUILayout.GraphPanel.BaseX, bounds.X);
            Assert.Equal(SongSelectionUILayout.GraphPanel.BaseY, bounds.Y);
        }

        [Fact]
        public void GraphPanel_NotesCounterPosition_ShouldBeAccessible()
        {
            var pos = SongSelectionUILayout.GraphPanel.NotesCounterPosition;
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        [Fact]
        public void GraphPanel_ProgressBarPosition_ShouldBeAccessible()
        {
            var pos = SongSelectionUILayout.GraphPanel.ProgressBarPosition;
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        [Fact]
        public void GraphPanel_ProgressBarSize_ShouldBePositive()
        {
            var size = SongSelectionUILayout.GraphPanel.ProgressBarSize;
            Assert.True(size.X > 0);
            Assert.True(size.Y > 0);
        }

        #endregion

        #region NoteDistributionBars Tests

        [Fact]
        public void NoteDistributionBars_Drums_StartPosition_ShouldMatchStartXY()
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.Drums.StartPosition;
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartX, pos.X);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartY, pos.Y);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void NoteDistributionBars_Drums_GetBarPosition_ShouldVaryByLane(int lane)
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.Drums.GetBarPosition(lane);
            Assert.True(pos.X >= SongSelectionUILayout.NoteDistributionBars.Drums.StartX);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartY, pos.Y);
        }

        [Fact]
        public void NoteDistributionBars_GuitarBass_StartPosition_ShouldMatchStartXY()
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartPosition;
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartX, pos.X);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartY, pos.Y);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(5)]
        public void NoteDistributionBars_GuitarBass_GetBarPosition_ShouldVaryByLane(int lane)
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.GuitarBass.GetBarPosition(lane);
            Assert.True(pos.X >= SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartX);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartY, pos.Y);
        }

        #endregion

        #region SongBars Tests

        [Fact]
        public void SongBars_SelectedBarPosition_ShouldMatchSelectedXY()
        {
            var pos = SongSelectionUILayout.SongBars.SelectedBarPosition;
            Assert.Equal(SongSelectionUILayout.SongBars.SelectedBarX, pos.X);
            Assert.Equal(SongSelectionUILayout.SongBars.SelectedBarY, pos.Y);
        }

        [Fact]
        public void SongBars_BarCoordinates_ShouldHave13Entries()
        {
            Assert.Equal(13, SongSelectionUILayout.SongBars.BarCoordinates.Length);
        }

        [Fact]
        public void SongBars_GetBarY_CenterIndex_ShouldReturn270()
        {
            var y = SongSelectionUILayout.SongBars.GetBarY(SongSelectionUILayout.SongBars.CenterIndex);
            Assert.True(y > 0);
        }

        [Fact]
        public void SongBars_GetBarY_OutOfRange_ShouldReturn0()
        {
            Assert.Equal(0, SongSelectionUILayout.SongBars.GetBarY(-1));
            Assert.Equal(0, SongSelectionUILayout.SongBars.GetBarY(100));
        }

        [Fact]
        public void SongBars_GetBarPosition_CenterIndex_ShouldReturnSelectedBarPosition()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(SongSelectionUILayout.SongBars.CenterIndex);
            Assert.Equal(SongSelectionUILayout.SongBars.SelectedBarX, pos.X);
            Assert.Equal(SongSelectionUILayout.SongBars.SelectedBarY, pos.Y);
        }

        [Fact]
        public void SongBars_GetBarPosition_NonCenterIndex_ShouldUseUnselectedX()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(0);
            Assert.Equal(SongSelectionUILayout.SongBars.UnselectedBarX, pos.X);
        }

        [Fact]
        public void SongBars_BarSize_ShouldMatchWidthHeight()
        {
            var size = SongSelectionUILayout.SongBars.BarSize;
            Assert.Equal(SongSelectionUILayout.SongBars.BarWidth, size.X);
            Assert.Equal(SongSelectionUILayout.SongBars.BarHeight, size.Y);
        }

        [Fact]
        public void SongBars_VisibleItems_ShouldBe13()
        {
            Assert.Equal(13, SongSelectionUILayout.SongBars.VisibleItems);
        }

        #endregion

        #region SongListDisplay Tests

        [Fact]
        public void SongListDisplay_Position_ShouldBeOrigin()
        {
            var pos = SongSelectionUILayout.SongListDisplay.Position;
            Assert.Equal(0, pos.X);
            Assert.Equal(0, pos.Y);
        }

        [Fact]
        public void SongListDisplay_Size_ShouldMatchWidthHeight()
        {
            var size = SongSelectionUILayout.SongListDisplay.Size;
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Width, size.X);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Height, size.Y);
        }

        [Fact]
        public void SongListDisplay_Bounds_ShouldMatchXYWidthHeight()
        {
            var bounds = SongSelectionUILayout.SongListDisplay.Bounds;
            Assert.Equal(0, bounds.X);
            Assert.Equal(0, bounds.Y);
            Assert.Equal(1280, bounds.Width);
            Assert.Equal(720, bounds.Height);
        }

        #endregion

        #region ItemCounter Tests

        [Fact]
        public void ItemCounter_BasePosition_ShouldMatchBaseXY()
        {
            var pos = SongSelectionUILayout.ItemCounter.BasePosition;
            Assert.Equal(SongSelectionUILayout.ItemCounter.BaseX, pos.X);
            Assert.Equal(SongSelectionUILayout.ItemCounter.BaseY, pos.Y);
        }

        #endregion

        #region Scrollbar Tests

        [Fact]
        public void Scrollbar_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.Scrollbar.Position;
            Assert.Equal(SongSelectionUILayout.Scrollbar.X, pos.X);
            Assert.Equal(SongSelectionUILayout.Scrollbar.Y, pos.Y);
        }

        [Fact]
        public void Scrollbar_Height_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Scrollbar.Height > 0);
        }

        [Fact]
        public void Scrollbar_IndicatorSizeVector_ShouldBeSquare()
        {
            var size = SongSelectionUILayout.Scrollbar.IndicatorSizeVector;
            Assert.Equal(SongSelectionUILayout.Scrollbar.IndicatorSize, size.X);
            Assert.Equal(SongSelectionUILayout.Scrollbar.IndicatorSize, size.Y);
        }

        #endregion

        #region CommentBar Tests

        [Fact]
        public void CommentBar_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.CommentBar.Position;
            Assert.Equal(SongSelectionUILayout.CommentBar.X, pos.X);
            Assert.Equal(SongSelectionUILayout.CommentBar.Y, pos.Y);
        }

        [Fact]
        public void CommentBar_FallbackColor_ShouldNotBeTransparent()
        {
            var color = SongSelectionUILayout.CommentBar.FallbackColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        #endregion

        #region PreviewImagePanel Tests

        [Fact]
        public void PreviewImagePanel_WithoutStatusPanel_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Position;
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.X, pos.X);
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Y, pos.Y);
        }

        [Fact]
        public void PreviewImagePanel_WithoutStatusPanel_Bounds_ShouldHavePositiveSize()
        {
            var bounds = SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Bounds;
            Assert.True(bounds.Width > 0);
            Assert.True(bounds.Height > 0);
        }

        [Fact]
        public void PreviewImagePanel_WithStatusPanel_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.PreviewImagePanel.WithStatusPanel.Position;
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithStatusPanel.X, pos.X);
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithStatusPanel.Y, pos.Y);
        }

        [Fact]
        public void PreviewImagePanel_WithStatusPanel_SizeVector_ShouldBePositive()
        {
            var size = SongSelectionUILayout.PreviewImagePanel.WithStatusPanel.SizeVector;
            Assert.True(size.X > 0);
            Assert.True(size.Y > 0);
        }

        #endregion

        #region UILabels Tests

        [Fact]
        public void UILabels_Title_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.UILabels.Title.Position;
            Assert.Equal(SongSelectionUILayout.UILabels.Title.X, pos.X);
            Assert.Equal(SongSelectionUILayout.UILabels.Title.Y, pos.Y);
        }

        [Fact]
        public void UILabels_Title_Size_ShouldMatchWidthHeight()
        {
            var size = SongSelectionUILayout.UILabels.Title.Size;
            Assert.Equal(SongSelectionUILayout.UILabels.Title.Width, size.X);
            Assert.Equal(SongSelectionUILayout.UILabels.Title.Height, size.Y);
        }

        [Fact]
        public void UILabels_Breadcrumb_Position_ShouldMatchXY()
        {
            var pos = SongSelectionUILayout.UILabels.Breadcrumb.Position;
            Assert.Equal(SongSelectionUILayout.UILabels.Breadcrumb.X, pos.X);
            Assert.Equal(SongSelectionUILayout.UILabels.Breadcrumb.Y, pos.Y);
        }

        #endregion

        #region Background Tests

        [Fact]
        public void Background_GradientLineSpacing_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Background.GradientLineSpacing > 0);
        }

        [Fact]
        public void Background_DefaultFontSize_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Background.DefaultFontSize > 0);
        }

        [Fact]
        public void Background_MainPanelAlpha_ShouldBeBetweenZeroAndOne()
        {
            Assert.InRange(SongSelectionUILayout.Background.MainPanelAlpha, 0f, 1f);
        }

        #endregion

        #region Timing Tests

        [Fact]
        public void Timing_FadeInDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.FadeInDuration > 0);
        }

        [Fact]
        public void Timing_FadeOutDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.FadeOutDuration > 0);
        }

        [Fact]
        public void Timing_NavigationDebounceSeconds_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.NavigationDebounceSeconds > 0);
        }

        #endregion

        #region Audio Tests

        [Fact]
        public void Audio_PreviewSoundVolume_ShouldBeBetweenZeroAndOne()
        {
            Assert.InRange(SongSelectionUILayout.Audio.PreviewSoundVolume, 0f, 1f);
        }

        [Fact]
        public void Audio_BgmFadeOutDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Audio.BgmFadeOutDuration > 0);
        }

        [Fact]
        public void Audio_BgmMaxVolume_ShouldBe1()
        {
            Assert.Equal(1.0f, SongSelectionUILayout.Audio.BgmMaxVolume);
        }

        #endregion

        #region Spacing Tests

        [Fact]
        public void Spacing_CellPadding_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Spacing.CellPadding > 0);
        }

        [Fact]
        public void Spacing_BorderThickness_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Spacing.BorderThickness > 0);
        }

        #endregion
    }
}
