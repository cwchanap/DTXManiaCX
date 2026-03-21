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

        [Fact]
        public void DTXManiaFadeTransition_DefaultDuration_ShouldBe0Point7()
        {
            var transition = new DTXManiaFadeTransition();
            Assert.Equal(0.7, transition.Duration, 3);
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
