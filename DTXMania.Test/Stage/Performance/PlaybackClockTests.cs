#nullable enable

using System;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class PlaybackClockTests
    {
        [Theory]
        [InlineData(50, 500.0)]
        [InlineData(100, 1000.0)]
        [InlineData(150, 1500.0)]
        public void GetLogicalTimeMs_AfterOneSecond_ShouldScaleByPlaySpeed(
            int playSpeedPercent,
            double expectedLogicalMs)
        {
            var clock = new PlaybackClock(playSpeedPercent);
            clock.Start(At(250));

            Assert.Equal(expectedLogicalMs, clock.GetLogicalTimeMs(At(1250)));
        }

        [Fact]
        public void Pause_ThenResume_ShouldFreezeAndContinueWithoutJumping()
        {
            var clock = new PlaybackClock(150);
            clock.Start(At(100));
            clock.Pause(At(500));

            Assert.False(clock.IsRunning);
            Assert.True(clock.IsPaused);
            Assert.Equal(600.0, clock.GetLogicalTimeMs(At(1000)));

            clock.Resume(At(1200));

            Assert.True(clock.IsRunning);
            Assert.False(clock.IsPaused);
            Assert.Equal(1050.0, clock.GetLogicalTimeMs(At(1500)));
        }

        [Fact]
        public void Stop_ShouldResetStateAndLogicalPosition()
        {
            var clock = new PlaybackClock(100);
            clock.Start(At(0));
            Assert.Equal(500.0, clock.GetLogicalTimeMs(At(500)));

            clock.Stop();

            Assert.False(clock.IsRunning);
            Assert.False(clock.IsPaused);
            Assert.Equal(0.0, clock.GetLogicalTimeMs(At(1000)));
        }

        [Fact]
        public void SetLogicalPosition_WhileRunning_ShouldUseLogicalMilliseconds()
        {
            var clock = new PlaybackClock(50);
            clock.Start(At(0));

            clock.SetLogicalPosition(2000.0, At(100));

            Assert.Equal(2250.0, clock.GetLogicalTimeMs(At(600)));
        }

        [Fact]
        public void SetLogicalPosition_WhilePaused_ShouldReplaceFrozenPosition()
        {
            var clock = new PlaybackClock(100);
            clock.Start(At(0));
            clock.Pause(At(100));

            clock.SetLogicalPosition(750.0, At(500));

            Assert.Equal(750.0, clock.GetLogicalTimeMs(At(1000)));
            Assert.True(clock.IsPaused);
        }

        [Fact]
        public void GetLogicalTimeMs_WhenGameTimeMovesBackward_ShouldNotMoveBackward()
        {
            var clock = new PlaybackClock(100);
            clock.Start(At(100));

            Assert.Equal(500.0, clock.GetLogicalTimeMs(At(600)));
            Assert.Equal(500.0, clock.GetLogicalTimeMs(At(400)));
            Assert.Equal(600.0, clock.GetLogicalTimeMs(At(700)));
        }

        [Fact]
        public void GetLogicalTimeMs_WhenGameTimePredatesStart_ShouldRemainAtStartPosition()
        {
            var clock = new PlaybackClock(100);
            clock.Start(At(100), logicalPositionMs: 250.0);

            Assert.Equal(250.0, clock.GetLogicalTimeMs(At(-100)));
            Assert.Equal(250.0, clock.GetLogicalTimeMs(At(100)));
        }

        [Fact]
        public void Constructor_WhenSpeedIsNotPositive_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(0));
        }

        private static GameTime At(double totalMilliseconds)
        {
            return new GameTime(
                TimeSpan.FromMilliseconds(totalMilliseconds),
                TimeSpan.Zero);
        }
    }
}