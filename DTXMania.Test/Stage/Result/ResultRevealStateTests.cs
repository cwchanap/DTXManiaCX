#nullable enable

using DTXMania.Game.Lib.Stage.Result;
using Xunit;

namespace DTXMania.Test.Stage.Result
{
    [Trait("Category", "Unit")]
    public class ResultRevealStateTests
    {
        [Fact]
        public void NewState_ShouldStartAtZeroProgress()
        {
            var state = new ResultRevealState();

            Assert.Equal(0.0, state.ElapsedSeconds);
            Assert.Equal(0.0f, state.RankProgress);
            Assert.Equal(0.0f, state.PanelProgress);
            Assert.False(state.IsComplete);
        }

        [Fact]
        public void Update_ShouldAdvanceRankBeforePanel()
        {
            var state = new ResultRevealState();

            state.Update(ResultRevealState.RankRevealSeconds / 2.0);

            Assert.InRange(state.RankProgress, 0.49f, 0.51f);
            Assert.Equal(0.0f, state.PanelProgress);
            Assert.False(state.IsComplete);
        }

        [Fact]
        public void Update_AfterRankAndDelay_ShouldAdvancePanel()
        {
            var state = new ResultRevealState();

            state.Update(
                ResultRevealState.RankRevealSeconds
                + ResultRevealState.PanelDelaySeconds
                + ResultRevealState.PanelRevealSeconds / 2.0);

            Assert.Equal(1.0f, state.RankProgress);
            Assert.InRange(state.PanelProgress, 0.49f, 0.51f);
            Assert.False(state.IsComplete);
        }

        [Fact]
        public void Update_PastTotalDuration_ShouldClampAndComplete()
        {
            var state = new ResultRevealState();

            state.Update(ResultRevealState.TotalRevealSeconds + 10.0);

            Assert.Equal(ResultRevealState.TotalRevealSeconds, state.ElapsedSeconds);
            Assert.Equal(1.0f, state.RankProgress);
            Assert.Equal(1.0f, state.PanelProgress);
            Assert.True(state.IsComplete);
        }

        [Fact]
        public void Update_WithNegativeDelta_ShouldNotMoveBackwards()
        {
            var state = new ResultRevealState();

            state.Update(0.25);
            state.Update(-1.0);

            Assert.Equal(0.25, state.ElapsedSeconds);
        }

        [Fact]
        public void Update_WithZeroDelta_ShouldNotAdvance()
        {
            var state = new ResultRevealState();

            state.Update(0.0);

            Assert.Equal(0.0, state.ElapsedSeconds);
            Assert.Equal(0.0f, state.RankProgress);
            Assert.Equal(0.0f, state.PanelProgress);
        }

        [Fact]
        public void Complete_ShouldJumpToEnd()
        {
            var state = new ResultRevealState();

            state.Complete();

            Assert.Equal(ResultRevealState.TotalRevealSeconds, state.ElapsedSeconds);
            Assert.True(state.IsComplete);
            Assert.Equal(1.0f, state.RankProgress);
            Assert.Equal(1.0f, state.PanelProgress);
        }

        [Fact]
        public void Reset_ShouldReturnToBeginning()
        {
            var state = new ResultRevealState();
            state.Complete();

            state.Reset();

            Assert.Equal(0.0, state.ElapsedSeconds);
            Assert.Equal(0.0f, state.RankProgress);
            Assert.Equal(0.0f, state.PanelProgress);
            Assert.False(state.IsComplete);
        }
    }
}
