#nullable enable

using System;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class SongTimerStateTests
    {
        [Fact]
        public void Play_WhenSilentTimer_ShouldUseLogicalGameTimeClock()
        {
            using var timer = new SongTimer();

            Assert.True(timer.Play(At(100)));

            Assert.True(timer.IsPlaying);
            Assert.False(timer.IsPaused);
            Assert.Equal(400.0, timer.GetCurrentMs(At(500)));
        }

        [Fact]
        public void Play_WithConfiguredSpeed_ShouldScaleLogicalTime()
        {
            using var timer = new SongTimer(50);
            Assert.True(timer.Play(At(0)));

            Assert.Equal(500.0, timer.GetCurrentMs(At(1000)));
        }

        [Fact]
        public void Pause_ShouldExposeFrozenLogicalPosition()
        {
            using var timer = new SongTimer(150);
            Assert.True(timer.Play(At(0)));

            timer.Pause(At(200));

            Assert.False(timer.IsPlaying);
            Assert.True(timer.IsPaused);
            Assert.Equal(300.0, timer.GetCurrentMs(At(1000)));
        }

        [Fact]
        public void Pause_ThenResume_ShouldPreserveLogicalPosition()
        {
            using var timer = new SongTimer();
            Assert.True(timer.Play(At(100)));
            timer.Pause(At(600));

            timer.Resume(At(1200));

            Assert.True(timer.IsPlaying);
            Assert.False(timer.IsPaused);
            Assert.Equal(1000.0, timer.GetCurrentMs(At(1700)));
        }

        [Fact]
        public void Stop_ShouldResetLogicalState()
        {
            using var timer = new SongTimer();
            Assert.True(timer.Play(At(0)));

            timer.Stop();

            Assert.False(timer.IsPlaying);
            Assert.False(timer.IsPaused);
            Assert.Equal(0.0, timer.GetCurrentMs(At(500)));
        }

        [Fact]
        public void Update_WhenBackgroundInstanceIsStopped_ShouldKeepLogicalClockRunning()
        {
            var instance = CreateSoundEffectInstance();
            var timer = new SongTimer(instance);
            StartLogicalClock(timer, At(0));

            Assert.Equal(SoundState.Stopped, instance.State);

            timer.Update(At(500));

            Assert.True(timer.IsPlaying);
            Assert.Equal(500.0, timer.GetCurrentMs(At(500)));
        }

        [Theory]
        [InlineData(-2.0f, -1.0f)]
        [InlineData(-0.5f, -0.5f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(2.0f, 1.0f)]
        public void Pitch_SetBeforePlayback_ShouldClampAndAssignToInstance(
            float requested,
            float expected)
        {
            var instance = CreateSoundEffectInstance();
            var timer = new SongTimer(instance);

            timer.Pitch = requested;

            Assert.Equal(expected, instance.Pitch);
            Assert.Equal(expected, timer.Pitch);
        }

        [Fact]
        public void Pitch_WhenSoundInstanceIsAbsent_ShouldUseNeutralDefault()
        {
            using var timer = new SongTimer();

            timer.Pitch = 0.5f;

            Assert.Equal(0f, timer.Pitch);
        }

        [Theory]
        [InlineData(-2.0f, -1.0f)]
        [InlineData(0.75f, 0.75f)]
        [InlineData(2.0f, 1.0f)]
        public void Pan_ShouldClampAndAssignToInstance(float requested, float expected)
        {
            var instance = CreateSoundEffectInstance();
            var timer = new SongTimer(instance);

            timer.Pan = requested;

            Assert.Equal(expected, timer.Pan);
            Assert.Equal(expected, instance.Pan);
        }

        [Fact]
        public void SilentTimer_AudioProperties_ShouldUseSafeDefaults()
        {
            using var timer = new SongTimer();

            timer.Volume = 0.5f;
            timer.Pan = -0.5f;
            timer.IsLooped = true;

            Assert.Equal(0f, timer.Volume);
            Assert.Equal(0f, timer.Pan);
            Assert.False(timer.IsLooped);
        }

        [Fact]
        public void SetPosition_WhenRunning_ShouldUseLogicalMilliseconds()
        {
            using var timer = new SongTimer(50);
            Assert.True(timer.Play(At(0)));

            timer.SetPosition(2000.0, At(100));

            Assert.Equal(2250.0, timer.GetCurrentMs(At(600)));
        }

        [Fact]
        public void DisposedTimer_ShouldRejectPlaybackAndIgnoreStateChanges()
        {
            var timer = new SongTimer();
            timer.Dispose();

            Assert.False(timer.Play(At(0)));
            Assert.Null(Record.Exception(() => timer.Pause(At(100))));
            Assert.Null(Record.Exception(() => timer.Resume(At(100))));
            Assert.Null(Record.Exception(timer.Stop));
            Assert.Null(Record.Exception(() => timer.Update(At(100))));
            Assert.Null(Record.Exception(() => timer.SetPosition(500.0, At(100))));
            Assert.Equal(0.0, timer.GetCurrentMs(At(100)));
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var timer = new SongTimer();
            timer.Dispose();

            Assert.Null(Record.Exception(timer.Dispose));
        }

        private static void StartLogicalClock(SongTimer timer, GameTime gameTime)
        {
            var field = typeof(SongTimer).GetField(
                "_playbackClock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            var clock = Assert.IsType<PlaybackClock>(field!.GetValue(timer));
            clock.Start(gameTime);
        }

        private static GameTime At(double totalMilliseconds)
        {
            return new GameTime(
                TimeSpan.FromMilliseconds(totalMilliseconds),
                TimeSpan.Zero);
        }

        private static SoundEffectInstance CreateSoundEffectInstance()
        {
#pragma warning disable SYSLIB0050
            return (SoundEffectInstance)FormatterServices.GetUninitializedObject(
                typeof(SoundEffectInstance));
#pragma warning restore SYSLIB0050
        }
    }
}
