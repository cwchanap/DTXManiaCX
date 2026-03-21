using DTXMania.Game.Lib.Resources;
using System.Linq;
using System.Reflection;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Tests for the TexturePath static class.
    /// Verifies constant values, helper method completeness, and
    /// that every path registered in the helper arrays is non-empty
    /// and follows the "Graphics/" prefix convention.
    /// </summary>
    [Trait("Category", "Unit")]
    public class TexturePathTests
    {
        #region Background Texture Constants (parameterized)

        [Theory]
        [InlineData("Graphics/1_background.jpg", nameof(TexturePath.StartupBackground))]
        [InlineData("Graphics/2_background.jpg", nameof(TexturePath.TitleBackground))]
        [InlineData("Graphics/5_background.jpg", nameof(TexturePath.SongSelectionBackground))]
        [InlineData("Graphics/6_background.jpg", nameof(TexturePath.SongTransitionBackground))]
        [InlineData("Graphics/7_background.jpg", nameof(TexturePath.PerformanceBackground))]
        [InlineData("Graphics/8_background.jpg", nameof(TexturePath.ResultBackground))]
        public void BackgroundTexturePath_ShouldBeCorrect(string expectedPath, string fieldName)
        {
            var field = typeof(TexturePath).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            Assert.Equal(expectedPath, (string?)field!.GetValue(null));
        }

        #endregion

        #region Performance Stage Texture Constants

        [Fact]
        public void PerformanceBackgroundTexture_ShouldAliasPerformanceBackground()
        {
            Assert.Equal(TexturePath.PerformanceBackground, TexturePath.PerformanceBackgroundTexture);
        }

        [Fact]
        public void PerformanceBackgroundVideo_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_background.mp4", TexturePath.PerformanceBackgroundVideo);
        }

        [Fact]
        public void StageFailed_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_stage_failed.jpg", TexturePath.StageFailed);
        }

        [Fact]
        public void FullCombo_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_FullCombo.png", TexturePath.FullCombo);
        }

        [Fact]
        public void Danger_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_Danger.png", TexturePath.Danger);
        }

        [Fact]
        public void DrumChips_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_chips_drums.png", TexturePath.DrumChips);
        }

        [Fact]
        public void HitBar_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/ScreenPlayDrums hit-bar.png", TexturePath.HitBar);
        }

        [Fact]
        public void GaugeFrame_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_Gauge.png", TexturePath.GaugeFrame);
        }

        [Fact]
        public void GaugeFill_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_gauge_bar.png", TexturePath.GaugeFill);
        }

        [Fact]
        public void ComboDisplay_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/ScreenPlayDrums combo.png", TexturePath.ComboDisplay);
        }

        [Fact]
        public void JudgeStrings_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_judge.png", TexturePath.JudgeStrings);
        }

        [Fact]
        public void PadCaps_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_pads.png", TexturePath.PadCaps);
        }

        [Fact]
        public void ScoreNumbers_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_score numbersGD.png", TexturePath.ScoreNumbers);
        }

        [Fact]
        public void LaneStrips_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_Paret.png", TexturePath.LaneStrips);
        }

        [Fact]
        public void HitSparkRed_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/ScreenPlayDrums chip fire_red.png", TexturePath.HitSparkRed);
        }

        [Fact]
        public void HitSparkBlue_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/ScreenPlayDrums chip fire_blue.png", TexturePath.HitSparkBlue);
        }

        [Fact]
        public void HitSparkGreen_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/ScreenPlayDrums chip fire_green.png", TexturePath.HitSparkGreen);
        }

        [Fact]
        public void WailingFire_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_WailingFire.png", TexturePath.WailingFire);
        }

        [Fact]
        public void LagNumbers_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_lag numbers.png", TexturePath.LagNumbers);
        }

        [Fact]
        public void PauseOverlay_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/7_pause_overlay.png", TexturePath.PauseOverlay);
        }

        #endregion

        #region Font Texture Constants

        [Fact]
        public void ConsoleFont_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/Console font 8x16.png", TexturePath.ConsoleFont);
        }

        [Fact]
        public void ConsoleFontSecondary_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/Console font 2 8x16.png", TexturePath.ConsoleFontSecondary);
        }

        [Fact]
        public void LevelNumberFont_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/6_LevelNumber.png", TexturePath.LevelNumberFont);
        }

        [Fact]
        public void DifficultySprite_ShouldBeCorrectPath()
        {
            Assert.Equal("Graphics/6_Difficulty.png", TexturePath.DifficultySprite);
        }

        #endregion

        #region GetAllTexturePaths

        [Fact]
        public void GetAllTexturePaths_ShouldReturnNonEmptyArray()
        {
            var paths = TexturePath.GetAllTexturePaths();
            Assert.NotNull(paths);
            Assert.NotEmpty(paths);
        }

        [Fact]
        public void GetAllTexturePaths_AllEntriesShouldBeNonEmpty()
        {
            var paths = TexturePath.GetAllTexturePaths();
            Assert.All(paths, path => Assert.False(string.IsNullOrWhiteSpace(path),
                $"Expected non-empty path but got: '{path}'"));
        }

        [Fact]
        public void GetAllTexturePaths_AllEntriesShouldStartWithGraphics()
        {
            var paths = TexturePath.GetAllTexturePaths();
            Assert.All(paths, path => Assert.StartsWith("Graphics/", path));
        }

        [Fact]
        public void GetAllTexturePaths_ShouldContainBackgroundTextures()
        {
            var paths = TexturePath.GetAllTexturePaths();
            Assert.Contains(TexturePath.StartupBackground, paths);
            Assert.Contains(TexturePath.TitleBackground, paths);
            Assert.Contains(TexturePath.SongSelectionBackground, paths);
            Assert.Contains(TexturePath.PerformanceBackground, paths);
            Assert.Contains(TexturePath.ResultBackground, paths);
        }

        [Fact]
        public void GetAllTexturePaths_ShouldContainPerformanceTextures()
        {
            var paths = TexturePath.GetAllTexturePaths();
            Assert.Contains(TexturePath.DrumChips, paths);
            Assert.Contains(TexturePath.HitBar, paths);
            Assert.Contains(TexturePath.GaugeFrame, paths);
            Assert.Contains(TexturePath.JudgeStrings, paths);
            Assert.Contains(TexturePath.PadCaps, paths);
        }

        [Fact]
        public void GetAllTexturePaths_ShouldContainFontTextures()
        {
            var paths = TexturePath.GetAllTexturePaths();
            Assert.Contains(TexturePath.ConsoleFont, paths);
            Assert.Contains(TexturePath.ConsoleFontSecondary, paths);
            Assert.Contains(TexturePath.LevelNumberFont, paths);
            Assert.Contains(TexturePath.DifficultySprite, paths);
        }

        [Fact]
        public void GetAllTexturePaths_ShouldNotContainDuplicates()
        {
            var paths = TexturePath.GetAllTexturePaths();
            var uniquePaths = paths.Distinct().ToArray();
            Assert.Equal(paths.Length, uniquePaths.Length);
        }

        #endregion

        #region GetBackgroundTextures

        [Fact]
        public void GetBackgroundTextures_ShouldReturnSixPaths()
        {
            var paths = TexturePath.GetBackgroundTextures();
            Assert.Equal(6, paths.Length);
        }

        [Fact]
        public void GetBackgroundTextures_ShouldContainAllStageBackgrounds()
        {
            var paths = TexturePath.GetBackgroundTextures();
            Assert.Contains(TexturePath.StartupBackground, paths);
            Assert.Contains(TexturePath.TitleBackground, paths);
            Assert.Contains(TexturePath.SongSelectionBackground, paths);
            Assert.Contains(TexturePath.SongTransitionBackground, paths);
            Assert.Contains(TexturePath.PerformanceBackground, paths);
            Assert.Contains(TexturePath.ResultBackground, paths);
        }

        [Fact]
        public void GetBackgroundTextures_AllEntriesShouldBeNonEmpty()
        {
            var paths = TexturePath.GetBackgroundTextures();
            Assert.All(paths, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        }

        #endregion

        #region GetPanelTextures

        [Fact]
        public void GetPanelTextures_ShouldReturnNonEmptyArray()
        {
            var paths = TexturePath.GetPanelTextures();
            Assert.NotEmpty(paths);
        }

        [Fact]
        public void GetPanelTextures_ShouldContainTitleMenu()
        {
            var paths = TexturePath.GetPanelTextures();
            Assert.Contains(TexturePath.TitleMenu, paths);
        }

        [Fact]
        public void GetPanelTextures_ShouldContainSongSelectionPanels()
        {
            var paths = TexturePath.GetPanelTextures();
            Assert.Contains(TexturePath.SongSelectionHeaderPanel, paths);
            Assert.Contains(TexturePath.SongSelectionFooterPanel, paths);
            Assert.Contains(TexturePath.SongStatusPanel, paths);
        }

        [Fact]
        public void GetPanelTextures_AllEntriesShouldStartWithGraphics()
        {
            var paths = TexturePath.GetPanelTextures();
            Assert.All(paths, p => Assert.StartsWith("Graphics/", p));
        }

        #endregion

        #region GetFontTextures

        [Fact]
        public void GetFontTextures_ShouldReturnFourPaths()
        {
            var paths = TexturePath.GetFontTextures();
            Assert.Equal(4, paths.Length);
        }

        [Fact]
        public void GetFontTextures_ShouldContainAllFontPaths()
        {
            var paths = TexturePath.GetFontTextures();
            Assert.Contains(TexturePath.ConsoleFont, paths);
            Assert.Contains(TexturePath.ConsoleFontSecondary, paths);
            Assert.Contains(TexturePath.LevelNumberFont, paths);
            Assert.Contains(TexturePath.DifficultySprite, paths);
        }

        [Fact]
        public void GetFontTextures_AllEntriesShouldBeNonEmpty()
        {
            var paths = TexturePath.GetFontTextures();
            Assert.All(paths, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        }

        #endregion
    }
}
