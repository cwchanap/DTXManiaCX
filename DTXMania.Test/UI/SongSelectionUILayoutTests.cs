using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    public class SongSelectionUILayoutTests
    {
        [Fact]
        public void StatusPanel_PositionMatchesConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.StatusPanel.X, SongSelectionUILayout.StatusPanel.Y),
                SongSelectionUILayout.StatusPanel.Position);
            Assert.Equal(new Vector2(SongSelectionUILayout.StatusPanel.Width, SongSelectionUILayout.StatusPanel.Height),
                SongSelectionUILayout.StatusPanel.Size);
        }

        [Fact]
        public void SearchFilterModal_BoundsMatchConstants()
        {
            var bounds = SongSelectionUILayout.SearchFilterModal.Bounds;
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.X, bounds.X);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Y, bounds.Y);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Width, bounds.Width);
            Assert.Equal(SongSelectionUILayout.SearchFilterModal.Height, bounds.Height);
        }

        [Fact]
        public void DifficultyGrid_GetCellPosition_ReturnsValidVector()
        {
            var pos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(0, 0);
            Assert.True(pos.X > 0);
            Assert.True(pos.Y > 0);
        }

        [Fact]
        public void DifficultyGrid_GetCellContentPosition_Has20pxOffset()
        {
            var cell = SongSelectionUILayout.DifficultyGrid.GetCellPosition(2, 1);
            var content = SongSelectionUILayout.DifficultyGrid.GetCellContentPosition(2, 1);
            Assert.Equal(20, content.Y - cell.Y);
        }

        [Fact]
        public void BPMSection_PositionMatchesConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.BPMSection.X, SongSelectionUILayout.BPMSection.Y),
                SongSelectionUILayout.BPMSection.Position);
        }

        [Fact]
        public void SkillPointSection_PositionMatchesConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.SkillPointSection.X, SongSelectionUILayout.SkillPointSection.Y),
                SongSelectionUILayout.SkillPointSection.Position);
        }

        [Fact]
        public void GraphPanel_BasePositionIsValid()
        {
            Assert.True(SongSelectionUILayout.GraphPanel.BasePosition.X > 0);
            Assert.True(SongSelectionUILayout.GraphPanel.BasePosition.Y > 0);
        }

        [Fact]
        public void SongBars_SelectedBarPosition_MatchesConstants()
        {
            Assert.Equal(new Vector2(SongSelectionUILayout.SongBars.SelectedBarX, SongSelectionUILayout.SongBars.SelectedBarY),
                SongSelectionUILayout.SongBars.SelectedBarPosition);
        }

        [Fact]
        public void NoteDistributionBars_Drums_LaneCount()
        {
            Assert.Equal(10, SongSelectionUILayout.NoteDistributionBars.Drums.LaneCount);
        }

        [Fact]
        public void NoteDistributionBars_GuitarBass_LaneCount()
        {
            Assert.Equal(6, SongSelectionUILayout.NoteDistributionBars.GuitarBass.LaneCount);
        }

        [Fact]
        public void Timing_FadeInDuration_IsPositive()
        {
            Assert.True(SongSelectionUILayout.Timing.FadeInDuration > 0);
        }

        [Fact]
        public void Audio_PreviewSoundVolume_InRange()
        {
            Assert.InRange(SongSelectionUILayout.Audio.PreviewSoundVolume, 0f, 1f);
        }

        [Fact]
        public void ScrollSpeedLabel_PositionIsValid()
        {
            Assert.True(SongSelectionUILayout.ScrollSpeedLabelX >= 0);
            Assert.True(SongSelectionUILayout.ScrollSpeedLabelY >= 0);
        }

        [Fact]
        public void FolderHintOverlay_OffsetsAreNonNegative()
        {
            Assert.True(SongSelectionUILayout.FolderHintOverlay.OffsetX >= 0);
            Assert.True(SongSelectionUILayout.FolderHintOverlay.OffsetY >= 0);
        }

        [Fact]
        public void CommentBar_PositionIsValid()
        {
            Assert.True(SongSelectionUILayout.CommentBar.X >= 0);
            Assert.True(SongSelectionUILayout.CommentBar.Y >= 0);
        }

        [Fact]
        public void ItemCounter_BasePositionIsValid()
        {
            Assert.True(SongSelectionUILayout.ItemCounter.BaseX > 0);
            Assert.True(SongSelectionUILayout.ItemCounter.BaseY > 0);
        }

        [Fact]
        public void Scrollbar_PositionIsValid()
        {
            Assert.True(SongSelectionUILayout.Scrollbar.X > 0);
            Assert.True(SongSelectionUILayout.Scrollbar.Height > 0);
        }

        [Fact]
        public void SongListDisplay_FullScreen()
        {
            Assert.Equal(1280, SongSelectionUILayout.SongListDisplay.Width);
            Assert.Equal(720, SongSelectionUILayout.SongListDisplay.Height);
        }

        [Fact]
        public void Background_DefaultFontSize_IsPositive()
        {
            Assert.True(SongSelectionUILayout.Background.DefaultFontSize > 0);
        }

        [Fact]
        public void Spacing_BorderThickness_IsPositive()
        {
            Assert.True(SongSelectionUILayout.Spacing.BorderThickness > 0);
        }

        [Fact]
        public void SongBars_GetBarPosition_CenterIndex_ReturnsSelectedPosition()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(SongSelectionUILayout.SongBars.CenterIndex);
            Assert.Equal(SongSelectionUILayout.SongBars.SelectedBarPosition, pos);
        }

        [Fact]
        public void SongBars_GetBarPosition_InvalidIndex_ReturnsZeroY()
        {
            var pos = SongSelectionUILayout.SongBars.GetBarPosition(-1);
            Assert.Equal(0, pos.Y);
        }

        [Fact]
        public void PreviewImagePanel_WithStatusPanel_HasValidBounds()
        {
            Assert.True(SongSelectionUILayout.PreviewImagePanel.WithStatusPanel.Size > 0);
        }

        [Fact]
        public void UILabels_Title_PositionIsValid()
        {
            Assert.True(SongSelectionUILayout.UILabels.Title.X >= 0);
            Assert.True(SongSelectionUILayout.UILabels.Title.Y >= 0);
        }

        [Fact]
        public void SearchFilterModal_ButtonWidthAndHeight_ArePositive()
        {
            Assert.True(SongSelectionUILayout.SearchFilterModal.ButtonWidth > 0);
            Assert.True(SongSelectionUILayout.SearchFilterModal.ButtonHeight > 0);
        }
    }
}
