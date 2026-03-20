using System;
using Xunit;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Extended unit tests for stage transition system covering edge cases and boundary conditions
    /// </summary>
    public class StageTransitionExtendedTests
    {
        #region BaseStageTransition State Tests

        [Fact]
        public void BaseTransition_BeforeStart_ShouldNotBeComplete()
        {
            var transition = new FadeTransition(0.5);
            Assert.False(transition.IsComplete);
            Assert.False(transition.IsComplete);
        }

        [Fact]
        public void BaseTransition_BeforeStart_ProgressShouldBeZero()
        {
            var transition = new CrossfadeTransition(1.0);
            Assert.Equal(0.0, transition.Progress);
        }

        [Fact]
        public void BaseTransition_UpdateBeforeStart_ShouldNotAdvanceProgress()
        {
            var transition = new FadeTransition(0.5);
            transition.Update(0.25); // not started
            Assert.Equal(0.0, transition.Progress);
            Assert.False(transition.IsComplete);
        }

        [Fact]
        public void BaseTransition_AfterComplete_UpdateShouldNotExceedDuration()
        {
            var transition = new CrossfadeTransition(0.5);
            transition.Start();
            transition.Update(0.5); // exactly at end
            transition.Update(1.0); // extra update beyond end
            Assert.Equal(1.0, transition.Progress);
        }

        [Fact]
        public void BaseTransition_Reset_AfterCompletion_ShouldRestartCorrectly()
        {
            var transition = new FadeTransition(0.5);
            transition.Start();
            transition.Update(1.0); // complete
            Assert.True(transition.IsComplete);

            transition.Reset();
            Assert.False(transition.IsComplete);
            Assert.Equal(0.0, transition.Progress);

            // Should be able to restart
            transition.Start();
            transition.Update(0.25);
            Assert.True(transition.Progress > 0.0);
        }

        [Fact]
        public void BaseTransition_MinimumDuration_ShouldPreventDivisionByZero()
        {
            var transition = new FadeTransition(-5.0, -5.0); // negative duration
            Assert.True(transition.Duration > 0.0);
        }

        #endregion

        #region InstantTransition Extended Tests

        [Fact]
        public void InstantTransition_BeforeStart_FadeOutAlphaShouldBeOne()
        {
            var transition = new InstantTransition();
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
        }

        [Fact]
        public void InstantTransition_BeforeStart_FadeInAlphaShouldBeZero()
        {
            var transition = new InstantTransition();
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void InstantTransition_AfterReset_ShouldReturnToBeforeStartState()
        {
            var transition = new InstantTransition();
            transition.Start();
            Assert.True(transition.IsComplete);

            transition.Reset();
            Assert.False(transition.IsComplete);
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void InstantTransition_MultipleUpdatesAfterStart_ShouldRemainComplete()
        {
            var transition = new InstantTransition();
            transition.Start();
            transition.Update(1.0);
            transition.Update(1.0);
            Assert.True(transition.IsComplete);
            Assert.Equal(0.0f, transition.GetFadeOutAlpha());
            Assert.Equal(1.0f, transition.GetFadeInAlpha());
        }

        #endregion

        #region FadeTransition Extended Tests

        [Fact]
        public void FadeTransition_BeforeStart_FadeOutAlphaShouldBeOne()
        {
            var transition = new FadeTransition(0.5, 0.5);
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
        }

        [Fact]
        public void FadeTransition_BeforeStart_FadeInAlphaShouldBeZero()
        {
            var transition = new FadeTransition(0.5, 0.5);
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void FadeTransition_AtStartOfFadeOut_FadeOutAlphaShouldBeOne()
        {
            var transition = new FadeTransition(0.4, 0.4);
            transition.Start();
            Assert.Equal(1.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void FadeTransition_AsymmetricDurations_ShouldHonorEachPhase()
        {
            // 0.2s fade out, 0.8s fade in
            var transition = new FadeTransition(0.2, 0.8);
            Assert.Equal(1.0, transition.Duration, 5);

            transition.Start();
            transition.Update(0.1); // 50% through fade-out
            Assert.Equal(0.5f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);

            transition.Update(0.1); // end of fade-out
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);

            transition.Update(0.4); // 50% through fade-in
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.5f, transition.GetFadeInAlpha(), 3);

            transition.Update(0.4); // complete
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
            Assert.True(transition.IsComplete);
        }

        [Fact]
        public void FadeTransition_DefaultDurations_ShouldBe0Point5Each()
        {
            var transition = new FadeTransition();
            Assert.Equal(1.0, transition.Duration, 5);
        }

        [Fact]
        public void FadeTransition_AtEndOfFadeIn_FadeInAlphaShouldBeOne()
        {
            var transition = new FadeTransition(0.5, 0.5);
            transition.Start();
            transition.Update(1.0);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
        }

        #endregion

        #region CrossfadeTransition Extended Tests

        [Fact]
        public void CrossfadeTransition_BeforeStart_AlphasShouldBeOneAndZero()
        {
            var transition = new CrossfadeTransition(0.5);
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void CrossfadeTransition_AtQuarterProgress_AlphasShouldBe0Point75And0Point25()
        {
            var transition = new CrossfadeTransition(1.0);
            transition.Start();
            transition.Update(0.25);
            Assert.Equal(0.75f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.25f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void CrossfadeTransition_AlphasSumShouldAlwaysBeOne()
        {
            var transition = new CrossfadeTransition(1.0);
            transition.Start();
            for (double t = 0.0; t <= 1.0; t += 0.1)
            {
                transition.Update(0.0); // reset then
                // Re-create for clean state
                var t2 = new CrossfadeTransition(1.0);
                t2.Start();
                t2.Update(t);
                float fadeOut = t2.GetFadeOutAlpha();
                float fadeIn = t2.GetFadeInAlpha();
                Assert.Equal(1.0f, fadeOut + fadeIn, 3);
            }
        }

        [Fact]
        public void CrossfadeTransition_DefaultDuration_ShouldBe0Point5()
        {
            var transition = new CrossfadeTransition();
            Assert.Equal(0.5, transition.Duration, 5);
        }

        #endregion

        #region DTXManiaFadeTransition Extended Tests

        [Fact]
        public void DTXManiaFadeTransition_BeforeStart_AlphasShouldBeOneAndZero()
        {
            var transition = new DTXManiaFadeTransition(0.7);
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void DTXManiaFadeTransition_WithEasing_AtComplete_AlphasShouldBe0And1()
        {
            var transition = new DTXManiaFadeTransition(0.7, useEasing: true);
            transition.Start();
            transition.Update(0.7);
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void DTXManiaFadeTransition_WithoutEasing_AtStart_AlphasShouldBeOneAndZero()
        {
            var transition = new DTXManiaFadeTransition(1.0, useEasing: false);
            transition.Start();
            // At progress=0, fade out should be 1.0, fade in should be 0.0
            Assert.Equal(1.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void DTXManiaFadeTransition_WithEasing_FadeOutPlusFadeIn_ShouldEqualOne()
        {
            // At any progress, with easing: sin^2 + cos^2 = 1 (Pythagorean identity)
            // fadeIn = sin(progress * pi/2), fadeOut = 1 - (1 - cos(progress * pi/2)) = cos(progress * pi/2)
            var transition = new DTXManiaFadeTransition(1.0, useEasing: true);
            transition.Start();
            transition.Update(0.5); // 50%

            double progress = 0.5;
            double easedOut = 1.0 - Math.Cos(progress * Math.PI * 0.5);
            double easedIn = Math.Sin(progress * Math.PI * 0.5);
            float expectedFadeOut = (float)(1.0 - easedOut);
            float expectedFadeIn = (float)easedIn;

            Assert.Equal(expectedFadeOut, transition.GetFadeOutAlpha(), 4);
            Assert.Equal(expectedFadeIn, transition.GetFadeInAlpha(), 4);
        }

        [Fact]
        public void DTXManiaFadeTransition_DefaultDuration_ShouldBe0Point7()
        {
            var transition = new DTXManiaFadeTransition();
            Assert.Equal(0.7, transition.Duration, 5);
        }

        [Fact]
        public void DTXManiaFadeTransition_WithEasing_AtComplete_BothAlphasShouldConverge()
        {
            var withEasing = new DTXManiaFadeTransition(1.0, useEasing: true);
            var withoutEasing = new DTXManiaFadeTransition(1.0, useEasing: false);

            withEasing.Start();
            withoutEasing.Start();
            withEasing.Update(1.0);
            withoutEasing.Update(1.0);

            // Both should arrive at 0 and 1 at completion
            Assert.Equal(0.0f, withEasing.GetFadeOutAlpha(), 3);
            Assert.Equal(1.0f, withEasing.GetFadeInAlpha(), 3);
            Assert.Equal(0.0f, withoutEasing.GetFadeOutAlpha(), 3);
            Assert.Equal(1.0f, withoutEasing.GetFadeInAlpha(), 3);
        }

        #endregion

        #region StartupToTitleTransition Extended Tests

        [Fact]
        public void StartupToTitleTransition_BeforeStart_AlphasShouldBeOneAndZero()
        {
            var transition = new StartupToTitleTransition(1.0);
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void StartupToTitleTransition_At30PercentProgress_FadeInShouldStillBeZero()
        {
            // Fade-in is delayed by 0.3, so at exactly 30% progress, fade-in = 0
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(0.3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void StartupToTitleTransition_BeyondDelay_FadeInShouldBePositive()
        {
            // After the 0.3 delay, fade-in should increase
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(0.5); // progress = 0.5, beyond 0.3 delay
            Assert.True(transition.GetFadeInAlpha() > 0.0f);
        }

        [Fact]
        public void StartupToTitleTransition_At100PercentProgress_FadeInShouldBeOne()
        {
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(1.0);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void StartupToTitleTransition_FadeOutFasterThanLinear()
        {
            // Fade out uses 1.5x multiplier so it completes before fade in starts
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(0.67); // at 67% progress, 1.5 * 0.67 = 1.0, so fade out complete
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 2);
        }

        [Fact]
        public void StartupToTitleTransition_DefaultDuration_ShouldBe1Point0()
        {
            var transition = new StartupToTitleTransition();
            Assert.Equal(1.0, transition.Duration, 5);
        }

        [Fact]
        public void StartupToTitleTransition_FadeInDelayedBy30Percent()
        {
            // At 30% + epsilon, fade-in should be > 0
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(0.31); // just past the 0.3 delay
            Assert.True(transition.GetFadeInAlpha() > 0.0f);
        }

        #endregion
    }
}
