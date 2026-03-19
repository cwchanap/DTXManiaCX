using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for DTXManiaVisualTheme static helper methods and constants.
    /// </summary>
    [Trait("Category", "Unit")]
    public class DTXManiaVisualThemeTests
    {
        #region GetDifficultyColor Tests

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void GetDifficultyColor_ValidIndex_ShouldReturnArrayColor(int index)
        {
            var result = DTXManiaVisualTheme.GetDifficultyColor(index);
            Assert.Equal(DTXManiaVisualTheme.SongSelection.DifficultyColors[index], result);
        }

        [Fact]
        public void GetDifficultyColor_NegativeIndex_ShouldReturnWhite()
        {
            var result = DTXManiaVisualTheme.GetDifficultyColor(-1);
            Assert.Equal(Color.White, result);
        }

        [Fact]
        public void GetDifficultyColor_IndexBeyondArray_ShouldReturnWhite()
        {
            var result = DTXManiaVisualTheme.GetDifficultyColor(100);
            Assert.Equal(Color.White, result);
        }

        [Fact]
        public void GetDifficultyColor_IndexEqualToArrayLength_ShouldReturnWhite()
        {
            int length = DTXManiaVisualTheme.SongSelection.DifficultyColors.Length;
            var result = DTXManiaVisualTheme.GetDifficultyColor(length);
            Assert.Equal(Color.White, result);
        }

        #endregion

        #region GetNodeTypeColor Tests

        [Fact]
        public void GetNodeTypeColor_Score_ShouldReturnSongTitleTextColor()
        {
            var result = DTXManiaVisualTheme.GetNodeTypeColor(NodeType.Score);
            Assert.Equal(DTXManiaVisualTheme.SongSelection.SongTitleText, result);
        }

        [Fact]
        public void GetNodeTypeColor_Box_ShouldReturnFolderTextColor()
        {
            var result = DTXManiaVisualTheme.GetNodeTypeColor(NodeType.Box);
            Assert.Equal(DTXManiaVisualTheme.SongSelection.FolderText, result);
        }

        [Fact]
        public void GetNodeTypeColor_BackBox_ShouldReturnBackFolderTextColor()
        {
            var result = DTXManiaVisualTheme.GetNodeTypeColor(NodeType.BackBox);
            Assert.Equal(DTXManiaVisualTheme.SongSelection.BackFolderText, result);
        }

        [Fact]
        public void GetNodeTypeColor_Random_ShouldReturnMagenta()
        {
            var result = DTXManiaVisualTheme.GetNodeTypeColor(NodeType.Random);
            Assert.Equal(Color.Magenta, result);
        }

        [Fact]
        public void GetNodeTypeColor_UnknownType_ShouldReturnWhite()
        {
            var result = DTXManiaVisualTheme.GetNodeTypeColor((NodeType)999);
            Assert.Equal(Color.White, result);
        }

        #endregion

        #region LerpColor Tests

        [Fact]
        public void LerpColor_AmountZero_ShouldReturnFirstColor()
        {
            var result = DTXManiaVisualTheme.LerpColor(Color.Red, Color.Blue, 0f);
            Assert.Equal(Color.Red, result);
        }

        [Fact]
        public void LerpColor_AmountOne_ShouldReturnSecondColor()
        {
            var result = DTXManiaVisualTheme.LerpColor(Color.Red, Color.Blue, 1f);
            Assert.Equal(Color.Blue, result);
        }

        [Fact]
        public void LerpColor_AmountNegative_ShouldClampToZero()
        {
            var result = DTXManiaVisualTheme.LerpColor(Color.Red, Color.Blue, -1f);
            // Clamped to 0 means result should equal first color
            Assert.Equal(Color.Red, result);
        }

        [Fact]
        public void LerpColor_AmountGreaterThanOne_ShouldClampToOne()
        {
            var result = DTXManiaVisualTheme.LerpColor(Color.Red, Color.Blue, 2f);
            // Clamped to 1 means result should equal second color
            Assert.Equal(Color.Blue, result);
        }

        [Fact]
        public void LerpColor_AmountHalf_ShouldReturnIntermediateColor()
        {
            var expected = Color.Lerp(Color.Red, Color.Blue, 0.5f);
            var result = DTXManiaVisualTheme.LerpColor(Color.Red, Color.Blue, 0.5f);
            Assert.Equal(expected, result);
        }

        #endregion

        #region ApplySelectionHighlight Tests

        [Fact]
        public void ApplySelectionHighlight_NotSelected_ShouldReturnBaseColor()
        {
            var baseColor = Color.DarkGray;
            var result = DTXManiaVisualTheme.ApplySelectionHighlight(baseColor, isSelected: false, isCenter: false);
            Assert.Equal(baseColor, result);
        }

        [Fact]
        public void ApplySelectionHighlight_IsSelected_ShouldBlendWithSelectedColor()
        {
            var baseColor = Color.DarkGray;
            var expected = DTXManiaVisualTheme.LerpColor(baseColor, DTXManiaVisualTheme.SongSelection.SongBarSelected, 0.5f);
            var result = DTXManiaVisualTheme.ApplySelectionHighlight(baseColor, isSelected: true, isCenter: false);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ApplySelectionHighlight_IsCenter_ShouldBlendWithCenterColor()
        {
            var baseColor = Color.DarkGray;
            var expected = DTXManiaVisualTheme.LerpColor(baseColor, DTXManiaVisualTheme.SongSelection.SongBarCenter, 0.7f);
            var result = DTXManiaVisualTheme.ApplySelectionHighlight(baseColor, isSelected: false, isCenter: true);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ApplySelectionHighlight_IsCenterTakesPrecedenceOverSelected()
        {
            var baseColor = Color.DarkGray;
            // When isCenter is true, the center logic should be applied regardless of isSelected
            var expected = DTXManiaVisualTheme.LerpColor(baseColor, DTXManiaVisualTheme.SongSelection.SongBarCenter, 0.7f);
            var result = DTXManiaVisualTheme.ApplySelectionHighlight(baseColor, isSelected: true, isCenter: true);
            Assert.Equal(expected, result);
        }

        #endregion

        #region EaseOutQuad Tests

        [Fact]
        public void EaseOutQuad_Zero_ShouldReturnZero()
        {
            Assert.Equal(0f, DTXManiaVisualTheme.Animations.EaseOutQuad(0f));
        }

        [Fact]
        public void EaseOutQuad_One_ShouldReturnOne()
        {
            Assert.Equal(1f, DTXManiaVisualTheme.Animations.EaseOutQuad(1f), 5);
        }

        [Fact]
        public void EaseOutQuad_Half_ShouldReturnThreeQuarters()
        {
            // EaseOutQuad(0.5) = 1 - (0.5)^2 = 0.75
            Assert.Equal(0.75f, DTXManiaVisualTheme.Animations.EaseOutQuad(0.5f), 5);
        }

        [Fact]
        public void EaseOutQuad_IsMonotonicallyIncreasing()
        {
            float prev = DTXManiaVisualTheme.Animations.EaseOutQuad(0f);
            for (float t = 0.1f; t <= 1.0f; t += 0.1f)
            {
                float current = DTXManiaVisualTheme.Animations.EaseOutQuad(t);
                Assert.True(current >= prev, $"EaseOutQuad({t}) should be >= EaseOutQuad({t - 0.1f})");
                prev = current;
            }
        }

        #endregion

        #region EaseInOutQuad Tests

        [Fact]
        public void EaseInOutQuad_Zero_ShouldReturnZero()
        {
            Assert.Equal(0f, DTXManiaVisualTheme.Animations.EaseInOutQuad(0f), 5);
        }

        [Fact]
        public void EaseInOutQuad_One_ShouldReturnOne()
        {
            Assert.Equal(1f, DTXManiaVisualTheme.Animations.EaseInOutQuad(1f), 5);
        }

        [Fact]
        public void EaseInOutQuad_Half_ShouldReturnHalf()
        {
            // At t=0.5, EaseInOutQuad should return 0.5 (symmetry point)
            Assert.Equal(0.5f, DTXManiaVisualTheme.Animations.EaseInOutQuad(0.5f), 4);
        }

        [Fact]
        public void EaseInOutQuad_IsMonotonicallyIncreasing()
        {
            float prev = DTXManiaVisualTheme.Animations.EaseInOutQuad(0f);
            for (float t = 0.1f; t <= 1.0f; t += 0.1f)
            {
                float current = DTXManiaVisualTheme.Animations.EaseInOutQuad(t);
                Assert.True(current >= prev, $"EaseInOutQuad({t}) should be >= EaseInOutQuad({t - 0.1f})");
                prev = current;
            }
        }

        #endregion

        #region Layout Constants Tests

        [Fact]
        public void Layout_SongBarHeight_ShouldBePositive()
        {
            Assert.True(DTXManiaVisualTheme.Layout.SongBarHeight > 0);
        }

        [Fact]
        public void Layout_VisibleSongCount_ShouldBePositive()
        {
            Assert.True(DTXManiaVisualTheme.Layout.VisibleSongCount > 0);
        }

        [Fact]
        public void Layout_CenterSongIndex_ShouldBeLessThanVisibleCount()
        {
            Assert.True(DTXManiaVisualTheme.Layout.CenterSongIndex < DTXManiaVisualTheme.Layout.VisibleSongCount);
        }

        [Fact]
        public void Layout_StatusPanelDimensions_ShouldBePositive()
        {
            Assert.True(DTXManiaVisualTheme.Layout.StatusPanelWidth > 0);
            Assert.True(DTXManiaVisualTheme.Layout.StatusPanelHeight > 0);
        }

        [Fact]
        public void Animations_SelectionFadeTime_ShouldBePositive()
        {
            Assert.True(DTXManiaVisualTheme.Animations.SelectionFadeTime.TotalMilliseconds > 0);
        }

        [Fact]
        public void Animations_ScrollAnimationTime_ShouldBePositive()
        {
            Assert.True(DTXManiaVisualTheme.Animations.ScrollAnimationTime.TotalMilliseconds > 0);
        }

        #endregion

        #region DifficultyColors Tests

        [Fact]
        public void SongSelection_DifficultyColors_ShouldHaveFiveEntries()
        {
            Assert.Equal(5, DTXManiaVisualTheme.SongSelection.DifficultyColors.Length);
        }

        [Fact]
        public void FontEffects_DefaultShadowOffset_ShouldBeNonZero()
        {
            var offset = DTXManiaVisualTheme.FontEffects.DefaultShadowOffset;
            Assert.True(offset.X > 0 || offset.Y > 0);
        }

        [Fact]
        public void FontEffects_DefaultOutlineThickness_ShouldBePositive()
        {
            Assert.True(DTXManiaVisualTheme.FontEffects.DefaultOutlineThickness > 0);
        }

        #endregion
    }
}
