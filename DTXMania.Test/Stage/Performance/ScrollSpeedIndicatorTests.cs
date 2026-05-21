using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class ScrollSpeedIndicatorTests
    {
        private const float Duration = PerformanceUILayout.ScrollSpeedIndicatorDurationSeconds;
        private const float Fade = PerformanceUILayout.ScrollSpeedIndicatorFadeSeconds;

        /// <summary>
        /// ScrollSpeedIndicator is created with null font (font init can fail at runtime).
        /// All logic tests work without a real font because Show/Update/ComputeAlpha
        /// do not touch the font — only Draw does. Draw tests use a Mock<IFont>
        /// for full coverage, while logic tests rely on the null-font fast path.
        /// </summary>
        private static ScrollSpeedIndicator CreateIndicator()
        {
            return new ScrollSpeedIndicator(null);
        }

        #region Show

        [Fact]
        public void Show_ShouldSetTextFromScrollSpeedPercent()
        {
            var indicator = CreateIndicator();
            indicator.Show(200);

            Assert.Equal("Scroll Speed x2.0", indicator.Text);
            Assert.True(indicator.IsVisible);
        }

        [Theory]
        [InlineData(50, "Scroll Speed x0.5")]
        [InlineData(100, "Scroll Speed x1.0")]
        [InlineData(400, "Scroll Speed x4.0")]
        public void Show_WithDifferentSpeeds_ShouldSetExpectedText(int speed, string expectedText)
        {
            var indicator = CreateIndicator();
            indicator.Show(speed);
            Assert.Equal(expectedText, indicator.Text);
        }

        [Fact]
        public void Show_CalledMultipleTimes_ShouldResetTimer()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);

            // Advance most of the duration
            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(1400));
            indicator.Update(gameTime);
            Assert.True(indicator.RemainingSeconds < Duration - 1.0f);

            // Show again — should reset the timer
            indicator.Show(200);

            Assert.Equal(Duration, indicator.RemainingSeconds);
            Assert.True(indicator.IsVisible);
        }

        #endregion

        #region Update

        [Fact]
        public void Update_ShouldCountDownRemainingTime()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);

            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
            indicator.Update(gameTime);

            var expected = Duration - 0.5f;
            Assert.InRange(indicator.RemainingSeconds, expected - 0.01f, expected + 0.01f);
            Assert.True(indicator.IsVisible);
        }

        [Fact]
        public void Update_ShouldClampRemainingToZero()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);

            // Advance past the full duration
            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            indicator.Update(gameTime);

            Assert.Equal(0f, indicator.RemainingSeconds);
            Assert.False(indicator.IsVisible);
        }

        [Fact]
        public void Update_WhenNotShown_ShouldBeNoOp()
        {
            var indicator = CreateIndicator();
            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            indicator.Update(gameTime);

            Assert.Equal(0f, indicator.RemainingSeconds);
            Assert.False(indicator.IsVisible);
        }

        [Fact]
        public void Update_AfterExpired_ShouldContinueAsNoOp()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);

            // Expire the timer
            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            indicator.Update(gameTime);
            Assert.Equal(0f, indicator.RemainingSeconds);

            // Update again — remaining should stay at 0
            indicator.Update(gameTime);
            Assert.Equal(0f, indicator.RemainingSeconds);
        }

        #endregion

        #region Alpha

        [Fact]
        public void Alpha_ShouldBeFullWhenRecentlyShown()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);

            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
            indicator.Update(gameTime);

            Assert.Equal(1.0f, indicator.Alpha);
        }

        [Fact]
        public void Alpha_ShouldFadeNearEndOfDuration()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);

            // Advance to 1300ms: remaining = 200ms, which is in the fade zone (200ms < 300ms fade)
            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(1300));
            indicator.Update(gameTime);

            // Alpha ≈ 0.2 / 0.3 ≈ 0.667
            var expected = (Duration - 1.3f) / Fade;
            Assert.InRange(indicator.Alpha, expected - 0.05f, expected + 0.05f);
            Assert.True(indicator.Alpha < 1.0f);
            Assert.True(indicator.Alpha > 0f);
        }

        [Fact]
        public void Alpha_ShouldBeZeroWhenExpired()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);

            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            indicator.Update(gameTime);

            Assert.Equal(0f, indicator.Alpha);
        }

        [Fact]
        public void Alpha_ShouldBeZeroBeforeFirstShow()
        {
            var indicator = CreateIndicator();
            Assert.Equal(0f, indicator.Alpha);
        }

        #endregion

        #region Integration

        [Fact]
        public void FullLifecycle_ShowUpdateExpire_ShouldTransitionThroughStates()
        {
            var indicator = CreateIndicator();

            // Initially not visible
            Assert.False(indicator.IsVisible);
            Assert.Equal(string.Empty, indicator.Text);

            // Show
            indicator.Show(150);
            Assert.Equal("Scroll Speed x1.5", indicator.Text);
            Assert.True(indicator.IsVisible);
            Assert.Equal(1.0f, indicator.Alpha);

            // Update through visible period (100 frames × 16ms ≈ 1.6s > 1.5s duration)
            var frameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
            for (var i = 0; i < 100; i++)
            {
                indicator.Update(frameTime);
            }

            // Timer should be expired
            Assert.False(indicator.IsVisible);
            Assert.Equal(0f, indicator.RemainingSeconds);

            // Show again
            indicator.Show(300);
            Assert.Equal("Scroll Speed x3.0", indicator.Text);
            Assert.True(indicator.IsVisible);

            // Partial update — still visible
            indicator.Update(frameTime);
            Assert.True(indicator.IsVisible);
        }

        #endregion

        #region Draw

        [Fact]
        public void Draw_WhenNotShown_ShouldNotThrow()
        {
            var indicator = CreateIndicator();
            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

            indicator.Draw(spriteBatch);

            Assert.False(indicator.IsVisible);
        }

        [Fact]
        public void Draw_WhenExpired_ShouldNotThrow()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);
            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            indicator.Update(gameTime);
            Assert.Equal(0f, indicator.RemainingSeconds);

            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

            indicator.Draw(spriteBatch);
        }

        [Fact]
        public void Draw_WithVisibleStateAndNullFont_ShouldReturnEarly()
        {
            var indicator = CreateIndicator();
            indicator.Show(100);
            Assert.True(indicator.IsVisible);

            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

            indicator.Draw(spriteBatch);
        }

        [Fact]
        public void Draw_WithFontAndVisible_ShouldExecuteDrawPath()
        {
            var font = CreateTestFont();
            var indicator = new ScrollSpeedIndicator(font);
            indicator.Show(100);
            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

            indicator.Draw(spriteBatch);

            Assert.True(indicator.IsVisible);
            Assert.Equal(1.0f, indicator.Alpha);
        }

        [Fact]
        public void Draw_WithFontDuringFade_ShouldExecuteDrawPathWithFadedAlpha()
        {
            var font = CreateTestFont();
            var indicator = new ScrollSpeedIndicator(font);
            indicator.Show(100);

            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(1300));
            indicator.Update(gameTime);
            Assert.True(indicator.Alpha < 1.0f);
            Assert.True(indicator.Alpha > 0f);

            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

            indicator.Draw(spriteBatch);
        }

        [Fact]
        public void Draw_WithFontAtFullAlpha_DoesNotThrow()
        {
            var font = CreateTestFont();
            var indicator = new ScrollSpeedIndicator(font);
            indicator.Show(200);

            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

            indicator.Draw(spriteBatch);
        }

        [Fact]
        public void Draw_AfterReShow_ShouldUseNewText()
        {
            var font = CreateTestFont();
            var indicator = new ScrollSpeedIndicator(font);
            indicator.Show(100);

            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();

            indicator.Draw(spriteBatch);

            indicator.Show(400);
            Assert.Equal("Scroll Speed x4.0", indicator.Text);

            indicator.Draw(spriteBatch);
        }

        private static IFont CreateTestFont()
        {
            return new Mock<IFont>().Object;
        }

        #endregion
    }
}
