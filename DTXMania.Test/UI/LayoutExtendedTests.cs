using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Extended tests for PerformanceUILayout inner classes
    /// </summary>
    public class PerformanceUILayoutExtendedTests
    {
        #region Animation Constants

        [Fact]
        public void Animation_StandardFrameDuration_ShouldBeApprox60Fps()
        {
            Assert.InRange(PerformanceUILayout.Animation.StandardFrameDuration, 0.016, 0.017);
        }

        [Fact]
        public void Animation_FlashDecayRate_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Animation.FlashDecayRate > 0f);
        }

        #endregion

        #region Effects Constants

        [Fact]
        public void Effects_FrameWidth_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Effects.FrameWidth > 0);
        }

        [Fact]
        public void Effects_DefaultAlpha_ShouldBeBetweenZeroAndOne()
        {
            Assert.InRange(PerformanceUILayout.Effects.DefaultAlpha, 0f, 1f);
        }

        #endregion

        #region Typography Constants

        [Fact]
        public void Typography_ComboFontSize_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Typography.ComboFontSize > 0);
        }

        [Fact]
        public void Typography_ScoreFontSize_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Typography.ScoreFontSize > 0);
        }

        #endregion

        #region Visual Constants

        [Fact]
        public void Visual_StandardShadowColor_ShouldHaveAlpha()
        {
            Assert.True(PerformanceUILayout.Visual.StandardShadowColor.A > 0);
        }

        [Fact]
        public void Visual_StandardFrameThickness_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Visual.StandardFrameThickness > 0);
        }

        #endregion

        #region Timing Constants

        [Fact]
        public void Timing_BaseLookAheadMs_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Timing.BaseLookAheadMs > 0.0);
        }

        [Fact]
        public void Timing_JudgementTextFadeDuration_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Timing.JudgementTextFadeDuration > 0f);
        }

        #endregion

        #region GaugeDisplay Constants

        [Fact]
        public void GaugeDisplay_HighLifeThreshold_ShouldBeBetweenMediumAndOne()
        {
            Assert.True(PerformanceUILayout.GaugeDisplay.HighLifeThreshold > PerformanceUILayout.GaugeDisplay.MediumLifeThreshold);
            Assert.True(PerformanceUILayout.GaugeDisplay.HighLifeThreshold <= 1.0f);
        }

        [Fact]
        public void GaugeDisplay_LowLifeThreshold_ShouldBeLessThanMedium()
        {
            Assert.True(PerformanceUILayout.GaugeDisplay.LowLifeThreshold < PerformanceUILayout.GaugeDisplay.MediumLifeThreshold);
        }

        [Fact]
        public void GaugeDisplay_DefaultValue_ShouldBeBetweenZeroAndOne()
        {
            Assert.InRange(PerformanceUILayout.GaugeDisplay.DefaultValue, 0f, 1f);
        }

        #endregion

        #region GaugeSettings Tests

        [Fact]
        public void GaugeSettings_StartingLife_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.GaugeSettings.StartingLife > 0f);
        }

        [Fact]
        public void GaugeSettings_DangerThreshold_ShouldBeLessThanStartingLife()
        {
            Assert.True(PerformanceUILayout.GaugeSettings.DangerThreshold < PerformanceUILayout.GaugeSettings.StartingLife);
        }

        [Fact]
        public void GaugeSettings_LifeAdjustment_JustShouldBePositive()
        {
            Assert.True(PerformanceUILayout.GaugeSettings.LifeAdjustment.Just > 0f);
        }

        [Fact]
        public void GaugeSettings_LifeAdjustment_MissShouldBeNegative()
        {
            Assert.True(PerformanceUILayout.GaugeSettings.LifeAdjustment.Miss < 0f);
        }

        [Fact]
        public void GaugeSettings_LifeAdjustment_JustGreaterThanGreat()
        {
            Assert.True(PerformanceUILayout.GaugeSettings.LifeAdjustment.Just > PerformanceUILayout.GaugeSettings.LifeAdjustment.Great);
        }

        #endregion

        #region PooledEffects Constants

        [Fact]
        public void PooledEffects_MaxPoolSize_ShouldBeGreaterThanInitial()
        {
            Assert.True(PerformanceUILayout.PooledEffects.MaxPoolSize > PerformanceUILayout.PooledEffects.InitialPoolSize);
        }

        #endregion

        #region FullCombo.GetCenteredPosition Tests

        [Fact]
        public void FullCombo_GetCenteredPosition_200x100Texture_ShouldCenter()
        {
            var pos = PerformanceUILayout.FullCombo.GetCenteredPosition(200, 100);
            // Expected: ((1280-200)/2, (720-100)/2) = (540, 310)
            Assert.Equal(540, pos.X);
            Assert.Equal(310, pos.Y);
        }

        [Fact]
        public void FullCombo_GetCenteredPosition_FullScreen_ShouldReturnOrigin()
        {
            var pos = PerformanceUILayout.FullCombo.GetCenteredPosition(1280, 720);
            Assert.Equal(0, pos.X);
            Assert.Equal(0, pos.Y);
        }

        #endregion

        #region ScoreDisplay Constants

        [Fact]
        public void ScoreDisplay_MaxScore_ShouldBe9999999()
        {
            Assert.Equal(9999999, PerformanceUILayout.ScoreDisplay.MaxScore);
        }

        #endregion

        #region Notes Constants

        [Fact]
        public void Notes_DropGracePeriod_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Notes.DropGracePeriod > 0);
        }

        [Fact]
        public void Notes_DefaultSize_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Notes.DefaultSize.X > 0);
            Assert.True(PerformanceUILayout.Notes.DefaultSize.Y > 0);
        }

        #endregion
    }

    /// <summary>
    /// Extended tests for SongTransitionUILayout inner classes
    /// </summary>
    public class SongTransitionUILayoutExtendedTests
    {
        [Fact]
        public void Difficulty_Position_ShouldMatchXY()
        {
            var pos = SongTransitionUILayout.Difficulty.Position;
            Assert.Equal(SongTransitionUILayout.Difficulty.X, pos.X);
            Assert.Equal(SongTransitionUILayout.Difficulty.Y, pos.Y);
        }

        [Fact]
        public void Difficulty_Size_ShouldMatchWidthHeight()
        {
            var size = SongTransitionUILayout.Difficulty.Size;
            Assert.Equal(SongTransitionUILayout.Difficulty.Width, size.X);
            Assert.Equal(SongTransitionUILayout.Difficulty.Height, size.Y);
        }

        [Fact]
        public void Difficulty_TextColor_ShouldBeYellow()
        {
            var color = SongTransitionUILayout.Difficulty.TextColor;
            Assert.True(color.R > 0 && color.G > 0); // Yellow = R+G
        }

        [Fact]
        public void DifficultySprite_GetSpriteIndex_Basic_ShouldReturnBasicIndex()
        {
            Assert.Equal(SongTransitionUILayout.DifficultySprite.BasicIndex,
                SongTransitionUILayout.DifficultySprite.GetSpriteIndex(0));
        }

        [Fact]
        public void DifficultySprite_GetSpriteIndex_Advanced_ShouldReturnAdvancedIndex()
        {
            Assert.Equal(SongTransitionUILayout.DifficultySprite.AdvancedIndex,
                SongTransitionUILayout.DifficultySprite.GetSpriteIndex(1));
        }

        [Fact]
        public void DifficultySprite_GetSpriteIndex_Extreme_ShouldReturnExtremeIndex()
        {
            Assert.Equal(SongTransitionUILayout.DifficultySprite.ExtremeIndex,
                SongTransitionUILayout.DifficultySprite.GetSpriteIndex(2));
        }

        [Fact]
        public void DifficultySprite_GetSpriteIndex_Master_ShouldReturnMasterIndex()
        {
            Assert.Equal(SongTransitionUILayout.DifficultySprite.MasterIndex,
                SongTransitionUILayout.DifficultySprite.GetSpriteIndex(3));
        }

        [Fact]
        public void DifficultySprite_GetSpriteIndex_Real_ShouldReturnRealIndex()
        {
            Assert.Equal(SongTransitionUILayout.DifficultySprite.RealIndex,
                SongTransitionUILayout.DifficultySprite.GetSpriteIndex(4));
        }

        [Fact]
        public void DifficultySprite_GetSpriteIndex_Unknown_ShouldReturnBasicIndex()
        {
            Assert.Equal(SongTransitionUILayout.DifficultySprite.BasicIndex,
                SongTransitionUILayout.DifficultySprite.GetSpriteIndex(99));
        }

        [Fact]
        public void DifficultySprite_Position_ShouldBeAccessible()
        {
            var pos = SongTransitionUILayout.DifficultySprite.Position;
            Assert.True(pos.X >= 0);
        }

        [Fact]
        public void DifficultySprite_SpriteSize_ShouldBePositive()
        {
            var size = SongTransitionUILayout.DifficultySprite.SpriteSize;
            Assert.True(size.X > 0);
            Assert.True(size.Y > 0);
        }

        [Fact]
        public void DifficultySprite_BackgroundSize_ShouldBePositive()
        {
            var size = SongTransitionUILayout.DifficultySprite.BackgroundSize;
            Assert.True(size.X > 0);
            Assert.True(size.Y > 0);
        }

        [Fact]
        public void DifficultyLevelNumber_Position_ShouldMatchXY()
        {
            var pos = SongTransitionUILayout.DifficultyLevelNumber.Position;
            Assert.Equal(SongTransitionUILayout.DifficultyLevelNumber.X, pos.X);
            Assert.Equal(SongTransitionUILayout.DifficultyLevelNumber.Y, pos.Y);
        }

        [Fact]
        public void DifficultyLevelNumber_TextColor_ShouldBeAccessible()
        {
            var color = SongTransitionUILayout.DifficultyLevelNumber.TextColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        [Fact]
        public void PreviewImage_Position_ShouldMatchXY()
        {
            var pos = SongTransitionUILayout.PreviewImage.Position;
            Assert.Equal(SongTransitionUILayout.PreviewImage.X, pos.X);
            Assert.Equal(SongTransitionUILayout.PreviewImage.Y, pos.Y);
        }

        [Fact]
        public void PreviewImage_Size_ShouldMatchWidthHeight()
        {
            var size = SongTransitionUILayout.PreviewImage.Size;
            Assert.Equal(SongTransitionUILayout.PreviewImage.Width, size.X);
            Assert.Equal(SongTransitionUILayout.PreviewImage.Height, size.Y);
        }

        [Fact]
        public void PreviewImage_Origin_ShouldBeCenter()
        {
            var origin = SongTransitionUILayout.PreviewImage.Origin;
            Assert.Equal(SongTransitionUILayout.PreviewImage.Width / 2f, origin.X);
            Assert.Equal(SongTransitionUILayout.PreviewImage.Height / 2f, origin.Y);
        }

        [Fact]
        public void PreviewImage_TintColor_ShouldBeAccessible()
        {
            var color = SongTransitionUILayout.PreviewImage.TintColor;
            Assert.Equal(Color.White, color);
        }

        [Fact]
        public void Timing_AutoTransitionDelay_ShouldBePositive()
        {
            Assert.True(SongTransitionUILayout.Timing.AutoTransitionDelay > 0);
        }

        [Fact]
        public void Timing_FadeInDuration_ShouldBePositive()
        {
            Assert.True(SongTransitionUILayout.Timing.FadeInDuration > 0);
        }

        [Fact]
        public void Timing_FadeOutDuration_ShouldBePositive()
        {
            Assert.True(SongTransitionUILayout.Timing.FadeOutDuration > 0);
        }

        [Fact]
        public void Background_GradientTopColor_ShouldBeAccessible()
        {
            var color = SongTransitionUILayout.Background.GradientTopColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        [Fact]
        public void Background_GradientBottomColor_ShouldBeAccessible()
        {
            var color = SongTransitionUILayout.Background.GradientBottomColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        [Fact]
        public void Background_DefaultBackgroundPath_ShouldNotBeEmpty()
        {
            var path = SongTransitionUILayout.Background.DefaultBackgroundPath;
            Assert.False(string.IsNullOrEmpty(path));
        }

        [Fact]
        public void Background_GradientLineSpacing_ShouldBePositive()
        {
            Assert.True(SongTransitionUILayout.Background.GradientLineSpacing > 0);
        }
    }
}
