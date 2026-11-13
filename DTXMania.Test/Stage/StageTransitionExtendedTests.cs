using System;
using Xunit;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Extended unit tests for stage transition system covering edge cases and boundary conditions
    /// </summary>
    [Trait("Category", "Unit")]
    public class StageTransitionExtendedTests
    {
        #region BaseStageTransition State Tests

        [Fact]
        public void BaseTransition_BeforeStart_ShouldNotBeComplete()
        {
            var transition = new FadeTransition(0.5);
            Assert.False(transition.IsComplete);
            Assert.Equal(0.0, transition.Progress);
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
            // Duration should be clamped to a positive value
            Assert.True(transition.Duration > 0.0);
            // After starting, alpha calculations must not produce NaN or Infinity
            transition.Start();
            transition.Update(0.0);
            float fadeOutAlpha = transition.GetFadeOutAlpha();
            float fadeInAlpha = transition.GetFadeInAlpha();
            Assert.False(float.IsNaN(fadeOutAlpha) || float.IsInfinity(fadeOutAlpha));
            Assert.False(float.IsNaN(fadeInAlpha) || float.IsInfinity(fadeInAlpha));
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
            for (double t = 0.0; t <= 1.0; t += 0.1)
            {
                var transition = new CrossfadeTransition(1.0);
                transition.Start();
                transition.Update(t);
                float fadeOut = transition.GetFadeOutAlpha();
                float fadeIn = transition.GetFadeInAlpha();
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
        public void DTXManiaFadeTransition_WithEasing_AtHalfProgress_ShouldMatchExpectedEasingValues()
        {
            // fadeOut = 1 - (1 - cos(x)) = cos(x), fadeIn = sin(x)
            // Note: cos(x) + sin(x) != 1 in general; each is independently verified here
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

        [Theory]
        [InlineData(0.29, true)]  // before 0.3 delay: fade-in is exactly 0
        [InlineData(0.30, true)]  // at the 0.3 boundary: fade-in is exactly 0
        [InlineData(0.31, false)] // just past delay: fade-in has started
        [InlineData(0.50, false)] // well past delay: fade-in is clearly positive
        public void StartupToTitleTransition_FadeIn_RespectsDelay(double progress, bool expectZero)
        {
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(progress);
            if (expectZero)
                Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);
            else
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


        #endregion

        #region Before-Start – All Transition Types (parameterized)

        [Theory]
        [InlineData(nameof(InstantTransition))]
        [InlineData(nameof(FadeTransition))]
        [InlineData(nameof(CrossfadeTransition))]
        [InlineData(nameof(DTXManiaFadeTransition))]
        [InlineData(nameof(StartupToTitleTransition))]
        public void AnyTransition_BeforeStart_ShouldHaveCorrectAlphas(string typeName)
        {
            // Instantiate directly (not via Activator.CreateInstance) because several
            // transition constructors have only default parameters, not truly parameterless ones.
            IStageTransition transition = typeName switch
            {
                nameof(InstantTransition)        => new InstantTransition(),
                nameof(FadeTransition)           => new FadeTransition(),
                nameof(CrossfadeTransition)      => new CrossfadeTransition(),
                nameof(DTXManiaFadeTransition)   => new DTXManiaFadeTransition(),
                nameof(StartupToTitleTransition) => new StartupToTitleTransition(),
                _                                => throw new System.ArgumentOutOfRangeException(nameof(typeName))
            };
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        #endregion

        #region InstantTransition

        [Fact]
        public void InstantTransition_BeforeStart_IsComplete_ShouldBeFalse()
        {
            var transition = new InstantTransition();
            Assert.False(transition.IsComplete);
        }

        [Fact]
        public void InstantTransition_AfterReset_ShouldRestoreInitialState()
        {
            var transition = new InstantTransition();
            transition.Start();
            Assert.True(transition.IsComplete);

            transition.Reset();
            Assert.False(transition.IsComplete);
            Assert.Equal(0.0, transition.Progress);
        }

        #endregion

        #region FadeTransition

        [Fact]
        public void FadeTransition_BeforeStart_IsComplete_ShouldBeFalse()
        {
            var transition = new FadeTransition();
            Assert.False(transition.IsComplete);
            Assert.Equal(0.0, transition.Progress);
        }

        [Fact]
        public void FadeTransition_AtExactFadeOutBoundary_ShouldTransitionToFadeInPhase()
        {
            // fadeOutDuration = 0.4, fadeInDuration = 0.4
            var transition = new FadeTransition(0.4, 0.4);
            transition.Start();

            // Advance exactly to end of fade-out phase
            transition.Update(0.4);

            // Fade-out should be 0, fade-in should still be 0 at the boundary
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(0.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void FadeTransition_JustIntoFadeInPhase_ShouldStartIncreasing()
        {
            var transition = new FadeTransition(0.4, 0.4);
            transition.Start();
            transition.Update(0.5); // 0.1 s into fade-in phase (0.1 / 0.4 = 0.25)

            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.InRange(transition.GetFadeInAlpha(), 0.2f, 0.3f);
        }

        #endregion

        #region CrossfadeTransition

        [Fact]
        public void CrossfadeTransition_AtStart_AlphasSumToOne()
        {
            var transition = new CrossfadeTransition(1.0);
            transition.Start();
            transition.Update(0.0);

            Assert.Equal(1.0f, transition.GetFadeOutAlpha() + transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void CrossfadeTransition_DuringTransition_AlphasSumToOne()
        {
            var transition = new CrossfadeTransition(1.0);
            transition.Start();
            transition.Update(0.3);

            Assert.Equal(1.0f, transition.GetFadeOutAlpha() + transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void CrossfadeTransition_AfterReset_ReturnsToPrestartState()
        {
            var transition = new CrossfadeTransition(0.5);
            transition.Start();
            transition.Update(0.5);
            Assert.True(transition.IsComplete);

            transition.Reset();

            Assert.False(transition.IsComplete);
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        #endregion

        #region DTXManiaFadeTransition – Completion (parameterized)

        [Theory]
        [InlineData(0.7, true)]
        [InlineData(0.4, false)]
        public void DTXManiaFadeTransition_AtCompletion_Alpha_ShouldBeCorrect(double duration, bool useEasing)
        {
            var transition = new DTXManiaFadeTransition(duration, useEasing: useEasing);
            transition.Start();
            transition.Update(duration);

            Assert.True(transition.IsComplete);
            // FadeOut should reach 0, FadeIn should reach 1 for both easing modes
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
        }

        #endregion

        #region StartupToTitleTransition – Completion (parameterized)

        [Theory]
        [InlineData(1.0, 1.0)]
        public void StartupToTitleTransition_AtCompletion_Alpha_ShouldBeCorrect(double duration, double updateTime)
        {
            var transition = new StartupToTitleTransition(duration);
            transition.Start();
            transition.Update(updateTime);

            Assert.True(transition.IsComplete);
            // progress=1.0 → 1.5*progress clamped to 1.0 → FadeOut = 0
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            // progress=1.0 → delayedProgress = (1.0 - 0.3) / 0.7 = 1.0 → FadeIn = 1
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void StartupToTitleTransition_DefaultDuration_ShouldBe1Second()
        {
            var transition = new StartupToTitleTransition();
            Assert.Equal(1.0, transition.Duration, 3);
        }

        [Fact]
        public void StartupToTitleTransition_EarlyFadeIn_ShouldBeZero()
        {
            // The fade-in is delayed until progress > 0.3
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(0.25); // 25% progress, still in delay window

            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void StartupToTitleTransition_FadeOutProgressesFastInitially()
        {
            // Fade-out uses 1.5x multiplier, so it reaches 0 at ~67% of duration
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(0.67); // At 67%, 1.5 * 0.67 = 1.005 clamped to 1.0 → alpha ≈ 0

            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 2);
        }

        #endregion

        #region BaseStageTransition – Shared Behaviour

        [Fact]
        public void BaseTransition_Update_DoesNotAdvancePastCompletion()
        {
            var transition = new FadeTransition(0.5, 0.5);
            transition.Start();
            transition.Update(10.0); // Way past the end

            Assert.True(transition.IsComplete);
            Assert.Equal(1.0, transition.Progress);
        }

        [Fact]
        public void BaseTransition_Update_BeforeStart_DoesNotAdvance()
        {
            var transition = new FadeTransition(0.5, 0.5);
            // Do not call Start()
            transition.Update(0.5);

            Assert.False(transition.IsComplete);
            Assert.Equal(0.0, transition.Progress);
        }

        [Fact]
        public void BaseTransition_NegativeDuration_IsClampedToPositive()
        {
            // BaseStageTransition clamps the total duration to a minimum positive value.
            // This test only verifies that clamping behaviour, not per-phase alpha correctness.
            var transition = new FadeTransition(-1.0, -1.0);
            Assert.True(transition.Duration > 0.0);
        }

        #endregion
    }
}
