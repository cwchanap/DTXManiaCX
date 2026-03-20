using Xunit;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Additional edge-case tests for all stage transition types.
    /// Covers before-Start() states, completion states, and boundary values
    /// that are not exercised by StageTransitionTests.cs.
    /// </summary>
    [Trait("Category", "Unit")]
    public class StageTransitionAdditionalTests
    {
        #region InstantTransition – Before Start

        [Fact]
        public void InstantTransition_BeforeStart_FadeOutAlpha_ShouldBeOne()
        {
            var transition = new InstantTransition();
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
        }

        [Fact]
        public void InstantTransition_BeforeStart_FadeInAlpha_ShouldBeZero()
        {
            var transition = new InstantTransition();
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

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

        #region FadeTransition – Before Start

        [Fact]
        public void FadeTransition_BeforeStart_FadeOutAlpha_ShouldBeOne()
        {
            var transition = new FadeTransition();
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
        }

        [Fact]
        public void FadeTransition_BeforeStart_FadeInAlpha_ShouldBeZero()
        {
            var transition = new FadeTransition();
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

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
            transition.Update(0.5); // 50% into fade-in phase

            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
            // 0.1 / 0.4 = 0.25
            Assert.InRange(transition.GetFadeInAlpha(), 0.2f, 0.3f);
        }

        #endregion

        #region CrossfadeTransition – Before Start

        [Fact]
        public void CrossfadeTransition_BeforeStart_FadeOutAlpha_ShouldBeOne()
        {
            var transition = new CrossfadeTransition();
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
        }

        [Fact]
        public void CrossfadeTransition_BeforeStart_FadeInAlpha_ShouldBeZero()
        {
            var transition = new CrossfadeTransition();
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void CrossfadeTransition_AtStart_AlphasSumToOne()
        {
            var transition = new CrossfadeTransition(1.0);
            transition.Start();
            transition.Update(0.0);

            var sum = transition.GetFadeOutAlpha() + transition.GetFadeInAlpha();
            Assert.Equal(1.0f, sum, 3);
        }

        [Fact]
        public void CrossfadeTransition_DuringTransition_AlphasSumToOne()
        {
            var transition = new CrossfadeTransition(1.0);
            transition.Start();
            transition.Update(0.3);

            var sum = transition.GetFadeOutAlpha() + transition.GetFadeInAlpha();
            Assert.Equal(1.0f, sum, 3);
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

        #region DTXManiaFadeTransition – Before Start & Completion

        [Fact]
        public void DTXManiaFadeTransition_BeforeStart_FadeOutAlpha_ShouldBeOne()
        {
            var transition = new DTXManiaFadeTransition();
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
        }

        [Fact]
        public void DTXManiaFadeTransition_BeforeStart_FadeInAlpha_ShouldBeZero()
        {
            var transition = new DTXManiaFadeTransition();
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void DTXManiaFadeTransition_WithEasing_AtCompletion_FadeOutAlphaShouldBeZero()
        {
            var transition = new DTXManiaFadeTransition(0.7, useEasing: true);
            transition.Start();
            transition.Update(0.7); // advance to completion

            Assert.True(transition.IsComplete);
            // At progress=1.0 with cos easing: progress = 1 - cos(PI/2) = 1 - 0 = 1 → alpha = 0
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
        }

        [Fact]
        public void DTXManiaFadeTransition_WithEasing_AtCompletion_FadeInAlphaShouldBeOne()
        {
            var transition = new DTXManiaFadeTransition(0.7, useEasing: true);
            transition.Start();
            transition.Update(0.7);

            Assert.True(transition.IsComplete);
            // At progress=1.0 with sin easing: sin(PI/2) = 1 → alpha = 1
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void DTXManiaFadeTransition_WithoutEasing_AtCompletion_FadeOutAlphaShouldBeZero()
        {
            var transition = new DTXManiaFadeTransition(0.4, useEasing: false);
            transition.Start();
            transition.Update(0.4);

            Assert.True(transition.IsComplete);
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
        }

        [Fact]
        public void DTXManiaFadeTransition_WithoutEasing_AtCompletion_FadeInAlphaShouldBeOne()
        {
            var transition = new DTXManiaFadeTransition(0.4, useEasing: false);
            transition.Start();
            transition.Update(0.4);

            Assert.True(transition.IsComplete);
            Assert.Equal(1.0f, transition.GetFadeInAlpha(), 3);
        }

        [Fact]
        public void DTXManiaFadeTransition_DefaultDuration_ShouldBe0Point7()
        {
            var transition = new DTXManiaFadeTransition();
            Assert.Equal(0.7, transition.Duration, 3);
        }

        #endregion

        #region StartupToTitleTransition – Before Start & Completion

        [Fact]
        public void StartupToTitleTransition_BeforeStart_FadeOutAlpha_ShouldBeOne()
        {
            var transition = new StartupToTitleTransition();
            Assert.Equal(1.0f, transition.GetFadeOutAlpha());
        }

        [Fact]
        public void StartupToTitleTransition_BeforeStart_FadeInAlpha_ShouldBeZero()
        {
            var transition = new StartupToTitleTransition();
            Assert.Equal(0.0f, transition.GetFadeInAlpha());
        }

        [Fact]
        public void StartupToTitleTransition_AtCompletion_FadeOutAlphaShouldBeZero()
        {
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(1.0);

            Assert.True(transition.IsComplete);
            // progress=1.0 → 1.5*progress clamped to 1.0 → alpha = 0
            Assert.Equal(0.0f, transition.GetFadeOutAlpha(), 3);
        }

        [Fact]
        public void StartupToTitleTransition_AtCompletion_FadeInAlphaShouldBeOne()
        {
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(1.0);

            Assert.True(transition.IsComplete);
            // progress=1.0 → delayedProgress = (1.0 - 0.3) / 0.7 = 1.0 → alpha = 1
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
            // Fade-out uses 1.5x multiplier, so it reaches 0 at 66% of duration
            var transition = new StartupToTitleTransition(1.0);
            transition.Start();
            transition.Update(0.67); // ~67%

            // At 67%, 1.5 * 0.67 = 1.005 clamped to 1.0 → alpha ≈ 0
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
        public void BaseTransition_NegativeDuration_ShouldUseMinimumDuration()
        {
            var transition = new FadeTransition(-1.0, -1.0);
            Assert.True(transition.Duration > 0.0);
        }

        #endregion
    }
}
