using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    [Trait("Category", "UI")]
    public class SongSelectionUILayoutTests
    {
        [Fact]
        public void StatusPanel_Position_ShouldMatchConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.StatusPanel.X, SongSelectionUILayout.StatusPanel.Y),
                SongSelectionUILayout.StatusPanel.Position);
            Assert.Equal(new Vector2(SongSelectionUILayout.StatusPanel.Width, SongSelectionUILayout.StatusPanel.Height),
                SongSelectionUILayout.StatusPanel.Size);
        }

        [Fact]
        public void SearchFilterModal_Bounds_ShouldMatchConstants()
        {
            var bounds = SongSelectionUILayout.SearchFilterModal.Bounds;
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Height, bounds.Height);
        }

        [Fact]
        public void DifficultyGrid_GetCellPosition_ShouldMatchComputedFormula()
        {
            // GetCellPosition uses: X = BaseX + PanelBodyWidth + CellWidth*(instrument-3), Y = BaseY + ((4-difficultyLevel)*CellHeight) - 2
            var pos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(0, 0);
            var expectedX = SongSelectionUILayout.DifficultyGrid.BaseX + SongSelectionUILayout.DifficultyGrid.PanelBodyWidth
                            + SongSelectionUILayout.DifficultyGrid.CellWidth * (0 - 3);
            var expectedY = SongSelectionUILayout.DifficultyGrid.BaseY + ((4 - 0) * SongSelectionUILayout.DifficultyGrid.CellHeight) - 2;
            Assert.Equal(new Vector2(expectedX, expectedY), pos);
        }

        [Fact]
        public void DifficultyGrid_GetCellContentPosition_ShouldHave20pxOffset()
        {
            var cell = SongSelectionUILayout.DifficultyGrid.GetCellPosition(2, 1);
            var content = SongSelectionUILayout.DifficultyGrid.GetCellContentPosition(2, 1);
            Assert.Equal(20, content.Y - cell.Y);
        }

        [Fact]
        public void BPMSection_Position_ShouldMatchConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.BPMSection.X, SongSelectionUILayout.BPMSection.Y),
                SongSelectionUILayout.BPMSection.Position);
        }

        [Fact]
        public void SkillPointSection_Position_ShouldMatchConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.SkillPointSection.X, SongSelectionUILayout.SkillPointSection.Y),
                SongSelectionUILayout.SkillPointSection.Position);
        }

        [Fact]
        public void PlayHistoryPanel_Bounds_ShouldMatchNXTexturePlacement()
        {
            Assert.Equal(new Vector2(700, 570), SongSelectionUILayout.PlayHistoryPanel.Position);
            Assert.Equal(new Vector2(458, 151), SongSelectionUILayout.PlayHistoryPanel.Size);
            Assert.Equal(new Rectangle(700, 570, 458, 151), SongSelectionUILayout.PlayHistoryPanel.Bounds);
            Assert.Equal(18, SongSelectionUILayout.PlayHistoryPanel.TextOffsetX);
            Assert.Equal(32, SongSelectionUILayout.PlayHistoryPanel.TextOffsetY);
            Assert.Equal(18, SongSelectionUILayout.PlayHistoryPanel.RowSpacing);
            Assert.Equal(5, SongSelectionUILayout.PlayHistoryPanel.MaxRows);
        }

        [Fact]
        public void GraphPanel_BasePosition_ShouldMatchConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.GraphPanel.BaseX, SongSelectionUILayout.GraphPanel.BaseY),
                SongSelectionUILayout.GraphPanel.BasePosition);
        }

        [Fact]
        public void SongBars_SelectedBarPosition_ShouldMatchConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.SongBars.SelectedBarX, SongSelectionUILayout.SongBars.SelectedBarY),
                SongSelectionUILayout.SongBars.SelectedBarPosition);
        }

        [Fact]
        public void NoteDistributionBars_Drums_ShouldHave10Lanes()
        {
            Assert.Equal(10, SongSelectionUILayout.NoteDistributionBars.Drums.LaneCount);
        }

        [Fact]
        public void NoteDistributionBars_GuitarBass_ShouldHave6Lanes()
        {
            Assert.Equal(6, SongSelectionUILayout.NoteDistributionBars.GuitarBass.LaneCount);
        }

        [Fact]
        public void Timing_FadeInDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.FadeInDuration > 0);
        }

        [Fact]
        public void Audio_PreviewSoundVolume_ShouldBeInRange()
        {
            Assert.InRange(SongSelectionUILayout.Audio.PreviewSoundVolume, 0f, 1f);
        }

        [Fact]
        public void ScrollSpeedLabel_Position_ShouldBeValid()
        {
            Assert.True(SongSelectionUILayout.ScrollSpeedLabelX >= 0);
            Assert.True(SongSelectionUILayout.ScrollSpeedLabelY >= 0);
        }

        [Fact]
        public void FolderHintOverlay_Offsets_ShouldBeNonNegative()
        {
            Assert.True(SongSelectionUILayout.FolderHintOverlay.OffsetX >= 0);
            Assert.True(SongSelectionUILayout.FolderHintOverlay.OffsetY >= 0);
        }

        [Fact]
        public void CommentBar_Position_ShouldBeValid()
        {
            Assert.True(SongSelectionUILayout.CommentBar.X >= 0);
            Assert.True(SongSelectionUILayout.CommentBar.Y >= 0);
        }

        [Fact]
        public void ItemCounter_BasePosition_ShouldBeValid()
        {
            Assert.True(SongSelectionUILayout.ItemCounter.BaseX > 0);
            Assert.True(SongSelectionUILayout.ItemCounter.BaseY > 0);
        }

        [Fact]
        public void Scrollbar_Position_ShouldBeValid()
        {
            Assert.True(SongSelectionUILayout.Scrollbar.X > 0);
            Assert.True(SongSelectionUILayout.Scrollbar.Height > 0);
        }

        [Fact]
        public void SongListDisplay_ShouldBeFullScreen()
        {
            Assert.Equal(1280, SongSelectionUILayout.SongListDisplay.Width);
            Assert.Equal(720, SongSelectionUILayout.SongListDisplay.Height);
        }

        [Fact]
        public void Background_DefaultFontSize_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Background.DefaultFontSize > 0);
        }

        [Fact]
        public void Spacing_BorderThickness_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Spacing.BorderThickness > 0);
        }

        [Fact]
        public void SongBars_GetBarPosition_WhenCenterIndex_ShouldReturnSelectedPosition()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(SongSelectionUILayout.SongBars.CenterIndex);
            Assert.Equal(SongSelectionUILayout.SongBars.SelectedBarPosition, pos);
        }

        [Fact]
        public void SongBars_GetBarPosition_WhenInvalidIndex_ShouldReturnZeroY()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(-1);
            Assert.Equal(0, pos.Y);
        }

        [Fact]
        public void PreviewImagePanel_WithStatusPanel_ShouldHaveValidBounds()
        {
            Assert.True(SongSelectionUILayout.PreviewImagePanel.WithStatusPanel.Size > 0);
        }

        [Fact]
        public void UILabels_Title_Position_ShouldBeValid()
        {
            Assert.True(SongSelectionUILayout.UILabels.Title.X >= 0);
            Assert.True(SongSelectionUILayout.UILabels.Title.Y >= 0);
        }

        [Fact]
        public void SearchFilterModal_ButtonWidthAndHeight_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.SearchFilterModal.ButtonWidth > 0);
            Assert.True(SongSelectionUILayout.SearchFilterModal.ButtonHeight > 0);
        }

        [Fact]
        public void SongBars_GetBarY_WhenValidIndex_ShouldReturnCoordinateY()
        {
            Assert.Equal(SongSelectionUILayout.SongBars.BarCoordinates[0].Y,
                SongSelectionUILayout.SongBars.GetBarY(0));
            Assert.Equal(SongSelectionUILayout.SongBars.BarCoordinates[3].Y,
                SongSelectionUILayout.SongBars.GetBarY(3));
        }

        [Fact]
        public void SongBars_GetBarPosition_WhenNonCenterIndex_ShouldReturnUnselectedPosition()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(0);
            Assert.Equal(SongSelectionUILayout.SongBars.UnselectedBarX, pos.X);
            Assert.Equal(SongSelectionUILayout.SongBars.GetBarY(0), pos.Y);
        }

        [Fact]
        public void SongBars_BarSize_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.SongBars.BarSize;
            Assert.Equal(SongSelectionUILayout.SongBars.BarWidth, size.X);
            Assert.Equal(SongSelectionUILayout.SongBars.BarHeight, size.Y);
        }

        [Fact]
        public void SongBars_UnselectedBarPosition_ShouldHaveCorrectX()
        {
            Assert.Equal(SongSelectionUILayout.SongBars.UnselectedBarX,
                SongSelectionUILayout.SongBars.UnselectedBarPosition.X);
        }

        [Fact]
        public void NoteDistributionBars_Drums_GetBarPosition_ShouldCalculateCorrectly()
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.Drums.GetBarPosition(0);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartX, pos.X);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartY, pos.Y);

            var pos3 = SongSelectionUILayout.NoteDistributionBars.Drums.GetBarPosition(3);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartX + 3 * (SongSelectionUILayout.NoteDistributionBars.Drums.BarWidth + SongSelectionUILayout.NoteDistributionBars.Drums.BarSpacing), pos3.X);
        }

        [Fact]
        public void NoteDistributionBars_GuitarBass_GetBarPosition_ShouldCalculateCorrectly()
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.GuitarBass.GetBarPosition(0);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartX, pos.X);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartY, pos.Y);

            var pos2 = SongSelectionUILayout.NoteDistributionBars.GuitarBass.GetBarPosition(2);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartX + 2 * (SongSelectionUILayout.NoteDistributionBars.GuitarBass.BarWidth + SongSelectionUILayout.NoteDistributionBars.GuitarBass.BarSpacing), pos2.X);
        }

        [Fact]
        public void PreviewImagePanel_WithoutStatusPanel_Size_ShouldMatchConstant()
        {
            Assert.Equal(368, SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Size);
        }

        [Fact]
        public void PreviewImagePanel_WithoutStatusPanel_Bounds_ShouldMatchConstants()
        {
            var bounds = SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Bounds;
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Size, bounds.Width);
            Assert.Equal(SongSelectionUILayout.PreviewImagePanel.WithoutStatusPanel.Size, bounds.Height);
        }

        [Fact]
        public void StatusPanel_Bounds_ShouldMatchConstants()
        {
            var bounds = SongSelectionUILayout.StatusPanel.Bounds;
            Assert.Equal(SongSelectionUILayout.StatusPanel.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.StatusPanel.Height, bounds.Height);
        }

        [Fact]
        public void StatusPanel_Size_ShouldMatchNXStatusPanelTexture()
        {
            Assert.Equal(561, SongSelectionUILayout.StatusPanel.Width);
            Assert.Equal(342, SongSelectionUILayout.StatusPanel.Height);
        }

        [Fact]
        public void BPMSection_Bounds_ShouldMatchConstants()
        {
            var bounds = SongSelectionUILayout.BPMSection.Bounds;
            Assert.Equal(SongSelectionUILayout.BPMSection.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.BPMSection.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.BPMSection.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.BPMSection.Height, bounds.Height);
        }

        [Fact]
        public void BPMSection_Size_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.BPMSection.Size;
            Assert.Equal(SongSelectionUILayout.BPMSection.Width, size.X);
            Assert.Equal(SongSelectionUILayout.BPMSection.Height, size.Y);
        }

        [Fact]
        public void BPMSection_LengthTextPosition_ShouldBeWithinBounds()
        {
            var pos = SongSelectionUILayout.BPMSection.LengthTextPosition;
            Assert.True(pos.X >= SongSelectionUILayout.BPMSection.X);
            Assert.True(pos.Y >= SongSelectionUILayout.BPMSection.Y);
        }

        [Fact]
        public void BPMSection_BPMTextPosition_ShouldBeWithinBounds()
        {
            var pos = SongSelectionUILayout.BPMSection.BPMTextPosition;
            Assert.True(pos.X >= SongSelectionUILayout.BPMSection.X);
            Assert.True(pos.Y >= SongSelectionUILayout.BPMSection.Y);
        }

        [Fact]
        public void SkillPointSection_Bounds_ShouldMatchConstants()
        {
            var bounds = SongSelectionUILayout.SkillPointSection.Bounds;
            Assert.Equal(SongSelectionUILayout.SkillPointSection.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.SkillPointSection.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.SkillPointSection.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.SkillPointSection.Height, bounds.Height);
        }

        [Fact]
        public void SkillPointSection_Size_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.SkillPointSection.Size;
            Assert.Equal(SongSelectionUILayout.SkillPointSection.Width, size.X);
            Assert.Equal(SongSelectionUILayout.SkillPointSection.Height, size.Y);
        }

        [Fact]
        public void SkillPointSection_ValuePosition_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.SkillPointSection.ValuePosition;
            Assert.Equal(SongSelectionUILayout.SkillPointSection.ValueX, pos.X);
            Assert.Equal(SongSelectionUILayout.SkillPointSection.ValueY, pos.Y);
        }

        [Fact]
        public void GraphPanel_Size_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.GraphPanel.Size;
            Assert.Equal(SongSelectionUILayout.GraphPanel.Width, size.X);
            Assert.Equal(SongSelectionUILayout.GraphPanel.Height, size.Y);
        }

        [Fact]
        public void GraphPanel_Bounds_ShouldMatchConstants()
        {
            var bounds = SongSelectionUILayout.GraphPanel.Bounds;
            Assert.Equal(SongSelectionUILayout.GraphPanel.BaseX, bounds.X);
            Assert.Equal(SongSelectionUILayout.GraphPanel.BaseY, bounds.Y);
            Assert.Equal(SongSelectionUILayout.GraphPanel.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.GraphPanel.Height, bounds.Height);
        }

        [Fact]
        public void GraphPanel_Size_ShouldMatchNXDrumsGraphPanelTexture()
        {
            Assert.Equal(110, SongSelectionUILayout.GraphPanel.Width);
            Assert.Equal(321, SongSelectionUILayout.GraphPanel.Height);
        }

        [Fact]
        public void GraphPanel_NotesCounterPosition_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.GraphPanel.NotesCounterPosition;
            Assert.Equal(SongSelectionUILayout.GraphPanel.NotesCounterX, pos.X);
            Assert.Equal(SongSelectionUILayout.GraphPanel.NotesCounterY, pos.Y);
        }

        [Fact]
        public void GraphPanel_ProgressBarPosition_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.GraphPanel.ProgressBarPosition;
            Assert.Equal(SongSelectionUILayout.GraphPanel.ProgressBarX, pos.X);
            Assert.Equal(SongSelectionUILayout.GraphPanel.ProgressBarY, pos.Y);
        }

        [Fact]
        public void GraphPanel_ProgressBarSize_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.GraphPanel.ProgressBarSize;
            Assert.Equal(SongSelectionUILayout.GraphPanel.ProgressBarWidth, size.X);
            Assert.Equal(SongSelectionUILayout.GraphPanel.ProgressBarHeight, size.Y);
        }

        [Fact]
        public void SongListDisplay_Position_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.SongListDisplay.Position;
            Assert.Equal(SongSelectionUILayout.SongListDisplay.X, pos.X);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Y, pos.Y);
        }

        [Fact]
        public void SongListDisplay_Size_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.SongListDisplay.Size;
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Width, size.X);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Height, size.Y);
        }

        [Fact]
        public void SongListDisplay_Bounds_ShouldMatchConstants()
        {
            var bounds = SongSelectionUILayout.SongListDisplay.Bounds;
            Assert.Equal(SongSelectionUILayout.SongListDisplay.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Height, bounds.Height);
        }

        [Fact]
        public void SearchFilterModal_Position_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.SearchFilterModal.Position;
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.X, pos.X);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Y, pos.Y);
        }

        [Fact]
        public void SearchFilterModal_Size_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.SearchFilterModal.Size;
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Width, size.X);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Height, size.Y);
        }

        [Fact]
        public void ItemCounter_BasePosition_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.ItemCounter.BasePosition;
            Assert.Equal(SongSelectionUILayout.ItemCounter.BaseX, pos.X);
            Assert.Equal(SongSelectionUILayout.ItemCounter.BaseY, pos.Y);
        }

        [Fact]
        public void Scrollbar_Position_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.Scrollbar.Position;
            Assert.Equal(SongSelectionUILayout.Scrollbar.X, pos.X);
            Assert.Equal(SongSelectionUILayout.Scrollbar.Y, pos.Y);
        }

        [Fact]
        public void Scrollbar_IndicatorSizeVector_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.Scrollbar.IndicatorSizeVector;
            Assert.Equal(SongSelectionUILayout.Scrollbar.IndicatorSize, size.X);
            Assert.Equal(SongSelectionUILayout.Scrollbar.IndicatorSize, size.Y);
        }

        [Fact]
        public void Background_MainPanelAlpha_ShouldBeInRange()
        {
            Assert.InRange(SongSelectionUILayout.Background.MainPanelAlpha, 0f, 1f);
        }

        [Fact]
        public void Timing_NavigationDebounceSeconds_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.NavigationDebounceSeconds > 0);
        }

        [Fact]
        public void Timing_TaskTimeoutMilliseconds_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.TaskTimeoutMilliseconds > 0);
        }

        [Fact]
        public void Timing_TransitionDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Timing.TransitionDuration > 0);
        }

        [Fact]
        public void Audio_NavigationSoundVolume_ShouldBeInRange()
        {
            Assert.InRange(SongSelectionUILayout.Audio.NavigationSoundVolume, 0f, 1f);
        }

        [Fact]
        public void Audio_GameStartSoundVolume_ShouldBeInRange()
        {
            Assert.InRange(SongSelectionUILayout.Audio.GameStartSoundVolume, 0f, 1f);
        }

        [Fact]
        public void Audio_BgmFadeOutDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Audio.BgmFadeOutDuration > 0);
        }

        [Fact]
        public void Audio_BgmFadeInDuration_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Audio.BgmFadeInDuration > 0);
        }

        [Fact]
        public void Audio_PreviewPlayDelaySeconds_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds > 0);
        }

        [Fact]
        public void Audio_BgmMinVolume_ShouldBeLessThanMax()
        {
            Assert.True(SongSelectionUILayout.Audio.BgmMinVolume < SongSelectionUILayout.Audio.BgmMaxVolume);
        }

        [Fact]
        public void Audio_BgmFadeRange_ShouldMatchDifference()
        {
            Assert.Equal(SongSelectionUILayout.Audio.BgmMaxVolume - SongSelectionUILayout.Audio.BgmMinVolume,
                SongSelectionUILayout.Audio.BgmFadeRange);
        }

        [Fact]
        public void Spacing_CellPadding_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Spacing.CellPadding > 0);
        }

        [Fact]
        public void Spacing_SectionSpacing_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Spacing.SectionSpacing > 0);
        }

        [Fact]
        public void Spacing_LabelValueSpacing_ShouldBePositive()
        {
            Assert.True(SongSelectionUILayout.Spacing.LabelValueSpacing > 0);
        }

        [Fact]
        public void NoteDistributionBars_Drums_StartPosition_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.Drums.StartPosition;
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartX, pos.X);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.Drums.StartY, pos.Y);
        }

        [Fact]
        public void NoteDistributionBars_GuitarBass_StartPosition_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartPosition;
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartX, pos.X);
            Assert.Equal(SongSelectionUILayout.NoteDistributionBars.GuitarBass.StartY, pos.Y);
        }

        [Fact]
        public void DifficultyGrid_BasePosition_ShouldMatchConstants()
        {
            var pos = SongSelectionUILayout.DifficultyGrid.BasePosition;
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.BaseX, pos.X);
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.BaseY, pos.Y);
        }

        [Fact]
        public void DifficultyGrid_CellSize_ShouldMatchConstants()
        {
            var size = SongSelectionUILayout.DifficultyGrid.CellSize;
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.CellWidth, size.X);
            Assert.Equal(SongSelectionUILayout.DifficultyGrid.CellHeight, size.Y);
        }

        [Fact]
        public void Tabs_Constants_ShouldBeValid()
        {
            Assert.True(SongSelectionUILayout.Tabs.X >= 0);
            Assert.True(SongSelectionUILayout.Tabs.Y >= 0);
            Assert.True(SongSelectionUILayout.Tabs.Spacing > 0);
            // Referencing the static fields forces their initializers to run,
            // covering the two static Color fields reported as uncovered.
            Assert.Equal(Color.White, SongSelectionUILayout.Tabs.ActiveColor);
            Assert.Equal(new Color(150, 150, 150), SongSelectionUILayout.Tabs.InactiveColor);
        }
    }
}
