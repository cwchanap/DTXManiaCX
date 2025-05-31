using System;
using Xunit;
using DTX.Stage;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for stage transition system
    /// Tests all transition types and their behavior
    /// </summary>
    public class StageTransitionTests
    {
        #region InstantTransition Tests

        [Fact]
        public void InstantTransition_ShouldCompleteImmediately()
        {
            // Arrange
            var transition = new InstantTransition();

            // Act
            transition.Start();

            // Assert
            Assert.True(transition.IsComplete);
            Assert.Equal(1.0, transition.Progress, 3);
        }

        [Fact]
        public void InstantTransition_ShouldProvideCorrectAlphaValues()
        {
            // Arrange
            var transition = new InstantTransition();

            // Act
            transition.Start();

            // Assert
            Assert.Equal(0.0f, transition.GetFadeOutAlpha());
            Assert.Equal(1.0f, transition.GetFadeInAlpha());
        }

        #endregion

        #region FadeTransition Tests

        [Fact]
        public void FadeTransition_ShouldHaveCorrectDuration()
        {
            // Arrange
            var transition = new FadeTransition(0.5, 0.3);

            // Assert
            Assert.Equal(0.8, transition.Duration, 3);
        }

        [Fact]
        public void FadeTransition_ShouldFadeOutThenFadeIn()
        {
            // Arrange
            var transition = new FadeTransition(0.4, 0.4);
            transition.Start();

            // Act & Assert - Fade out phase
            transition.Update(0.2); // 50% through fade out
            Assert.Equal(0.5f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);

            transition.Update(0.2); // End of fade out
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);

            // Fade in phase
            transition.Update(0.2); // 50% through fade in
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.5f, transition.GetFadeInAlpha(), 3);

            transition.Update(0.2); // Complete
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
            Assert.True(transition.IsComplete);
        }

        #endregion

        #region CrossfadeTransition Tests

        [Fact]
        public void CrossfadeTransition_ShouldCrossfadeSimultaneously()
        {
            // Arrange
            var transition = new CrossfadeTransition(0.5);
            transition.Start();

            // Act & Assert - 50% progress
            transition.Update(0.25);
            Assert.Equal(0.5f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.5f, transition.GetFadeInAlpha(), 3);

            // Complete
            transition.Update(0.25);
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
            Assert.True(transition.IsComplete);
        }

        #endregion

        #region DTXManiaFadeTransition Tests

        [Fact]
        public void DTXManiaFadeTransition_ShouldUseEasingByDefault()
        {
            // Arrange
            var transition = new DTXManiaFadeTransition(0.7);
            transition.Start();

            // Act
            transition.Update(0.35); // 50% progress

            // Assert - With easing, values should not be exactly 0.5
            var fadeOutAlpha = transition.GetFadeOutAlpha();
            var fadeInAlpha = transition.GetFadeInAlpha();

            Assert.NotEqual(0.5f, fadeOutAlpha);
            Assert.NotEqual(0.5f, fadeInAlpha);

            // With DTXMania easing (sin/cos), the sum can be > 1.0 during transition
            // Just verify both values are in valid range
            Assert.True(fadeOutAlpha >= 0.0f && fadeOutAlpha <= 1.0f);
            Assert.True(fadeInAlpha >= 0.0f && fadeInAlpha <= 1.0f);
        }

        [Fact]
        public void DTXManiaFadeTransition_WithoutEasing_ShouldBehaveLinear()
        {
            // Arrange
            var transition = new DTXManiaFadeTransition(0.4, useEasing: false);
            transition.Start();

            // Act
            transition.Update(0.2); // 50% progress

            // Assert - Without easing, should be linear
            Assert.Equal(0.5f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.5f, transition.GetFadeInAlpha(), 3);
        }

        #endregion

        #region StartupToTitleTransition Tests

        [Fact]
        public void StartupToTitleTransition_ShouldHaveDelayedFadeIn()
        {
            // Arrange
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();

            // Act & Assert - Early in transition
            transition.Update(0.2); // 20% progress
            Assert.True(transition.GetFadeOutAlpha() > 0.0f);
            Assert.Equal(0.0f, transition.GetFadeInAlpha()); // Should still be 0 due to delay

            // Later in transition
            transition.Update(0.5); // 70% progress
            Assert.True(transition.GetFadeInAlpha() > 0.0f); // Should start fading in
        }

        #endregion

        #region Base Transition Tests

        [Fact]
        public void BaseTransition_ShouldNotStartAutomatically()
        {
            // Arrange
            var transition = new FadeTransition();

            // Assert
            Assert.False(transition.IsComplete);
            Assert.Equal(0.0, transition.Progress);
        }

        [Fact]
        public void BaseTransition_ShouldResetCorrectly()
        {
            // Arrange
            var transition = new FadeTransition(0.5);
            transition.Start();
            transition.Update(0.25);

            // Act
            transition.Reset();

            // Assert
            Assert.False(transition.IsComplete);
            Assert.Equal(0.0, transition.Progress);
        }

        [Fact]
        public void BaseTransition_ShouldHandleZeroDuration()
        {
            // Arrange & Act
            var transition = new TestTransition(0.0);

            // Assert - Should have minimum duration to prevent division by zero
            Assert.True(transition.Duration > 0.0);
        }

        [Fact]
        public void BaseTransition_ShouldClampProgressToOne()
        {
            // Arrange
            var transition = new FadeTransition(0.5);
            transition.Start();

            // Act - Update beyond duration
            transition.Update(1.0);

            // Assert
            Assert.Equal(1.0, transition.Progress);
            Assert.True(transition.IsComplete);
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// Test transition for testing base functionality
        /// </summary>
        private class TestTransition : BaseStageTransition
        {
            public TestTransition(double duration) : base(duration) { }

            public override float GetFadeOutAlpha() => 1.0f - (float)Progress;
            public override float GetFadeInAlpha() => (float)Progress;
        }

        #endregion
    }
}
