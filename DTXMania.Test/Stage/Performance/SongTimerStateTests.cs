#nullable enable

using System;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class SongTimerStateTests
    {
        // ---------------------------------------------------------------
        // Factory helper – bypasses constructor to avoid SoundEffectInstance
        // ---------------------------------------------------------------
        private static SongTimer CreateTimer(bool isPlaying = false, bool disposed = false)
        {
#pragma warning disable SYSLIB0050
            var timer = (SongTimer)FormatterServices.GetUninitializedObject(typeof(SongTimer));
#pragma warning restore SYSLIB0050
            ReflectionHelpers.SetPrivateField(timer, "_isPlaying", isPlaying);
            ReflectionHelpers.SetPrivateField(timer, "_disposed", disposed);
            ReflectionHelpers.SetPrivateField(timer, "_systemStartTime", DateTime.UtcNow - TimeSpan.FromMilliseconds(250));
            ReflectionHelpers.SetPrivateField(timer, "_startTime", TimeSpan.FromMilliseconds(100));
            // _soundInstance left null intentionally – tests the null-guard paths
            return timer;
        }

        // ---------------------------------------------------------------
        // IsPlaying
        // Note: the soundState == SoundState.Playing branch inside IsPlaying is
        // not reachable through the null _soundInstance reflection seam used here.
        // The tests below intentionally cover the muted/null branch only.
        // ---------------------------------------------------------------

        [Fact]
        public void IsPlaying_WhenInternalFlagTrueAndSoundInstanceNull_ShouldReturnTrue()
        {
            // When the backing sound is absent, the timer should still report the stored playing state.
            var timer = CreateTimer(isPlaying: true);
            Assert.True(timer.IsPlaying);
        }

        [Fact]
        public void IsPlaying_WhenInternalFlagFalse_ShouldReturnFalse()
        {
            var timer = CreateTimer(isPlaying: false);
            Assert.False(timer.IsPlaying);
        }

        // ---------------------------------------------------------------
        // GetCurrentMs(GameTime)
        // ---------------------------------------------------------------

        [Fact]
        public void GetCurrentMs_GameTime_WhenNotPlaying_ShouldReturnZero()
        {
            var timer = CreateTimer(isPlaying: false);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.Zero);
            Assert.Equal(0.0, timer.GetCurrentMs(gameTime));
        }

        [Fact]
        public void GetCurrentMs_GameTime_WhenDisposed_ShouldReturnZero()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.Zero);
            Assert.Equal(0.0, timer.GetCurrentMs(gameTime));
        }

        [Fact]
        public void GetCurrentMs_GameTime_WhenPlaying_ShouldReturnElapsedMs()
        {
            // _startTime = 100 ms, totalGameTime = 500 ms → elapsed = 400 ms
            var timer = CreateTimer(isPlaying: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.Zero);
            Assert.Equal(400.0, timer.GetCurrentMs(gameTime));
        }

        // ---------------------------------------------------------------
        // GetCurrentMs() – system-clock variant
        // ---------------------------------------------------------------

        [Fact]
        public void GetCurrentMs_WhenNotPlaying_ShouldReturnZero()
        {
            var timer = CreateTimer(isPlaying: false);
            Assert.Equal(0.0, timer.GetCurrentMs());
        }

        [Fact]
        public void GetCurrentMs_WhenDisposed_ShouldReturnZero()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            Assert.Equal(0.0, timer.GetCurrentMs());
        }

        [Fact]
        public void GetCurrentMs_WhenPlaying_ShouldReturnPositiveElapsed()
        {
            // _systemStartTime is 250 ms in the past → result should be ≥ 250 ms
            var timer = CreateTimer(isPlaying: true);
            var ms = timer.GetCurrentMs();
            Assert.InRange(ms, 200.0, 10_000.0);
        }

        // ---------------------------------------------------------------
        // Volume & IsLooped – null-guard defaults
        // ---------------------------------------------------------------

        [Fact]
        public void Volume_Get_WhenSoundInstanceNull_ShouldReturnZero()
        {
            var timer = CreateTimer();
            Assert.Equal(0f, timer.Volume);
        }

        [Fact]
        public void Volume_Set_WhenSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer();
            // Must not throw
            var ex = Record.Exception(() => timer.Volume = 0.5f);
            Assert.Null(ex);
        }

        [Fact]
        public void Pan_Get_WhenSoundInstanceNull_ShouldReturnCentered()
        {
            var timer = CreateTimer();
            Assert.Equal(0f, timer.Pan);
        }

        [Fact]
        public void Pan_Set_WhenSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer();
            var ex = Record.Exception(() => timer.Pan = -0.5f);
            Assert.Null(ex);
        }

        [Fact]
        public void IsLooped_Get_WhenSoundInstanceNull_ShouldReturnFalse()
        {
            var timer = CreateTimer();
            Assert.False(timer.IsLooped);
        }

        [Fact]
        public void IsLooped_Set_WhenSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer();
            var ex = Record.Exception(() => timer.IsLooped = true);
            Assert.Null(ex);
        }

        // ---------------------------------------------------------------
        // Guard clauses – disposed state
        // ---------------------------------------------------------------

        [Fact]
        public void Pause_WhenDisposed_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Pause(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Stop_WhenDisposed_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var ex = Record.Exception(() => timer.Stop());
            Assert.Null(ex);
        }

        [Fact]
        public void Update_WhenDisposed_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Update(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void SetPosition_WhenDisposed_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.SetPosition(500.0, gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Resume_WhenDisposed_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: false, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Resume(gameTime));
            Assert.Null(ex);
        }

        // ---------------------------------------------------------------
        // Guard clauses – null _soundInstance (not disposed)
        // ---------------------------------------------------------------

        [Fact]
        public void Pause_WhenSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Pause(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Stop_WhenSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true);
            var ex = Record.Exception(() => timer.Stop());
            Assert.Null(ex);
        }

        [Fact]
        public void Resume_WhenSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: false);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Resume(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void SetPosition_WhenSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.SetPosition(250.0, gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Update_WhenNotDisposed_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: false);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Update(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Update_WhenPlayingAndSoundInstanceNull_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);

            var ex = Record.Exception(() => timer.Update(gameTime));

            Assert.Null(ex);
        }

        // ---------------------------------------------------------------
        // Dispose – idempotency
        // ---------------------------------------------------------------

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var timer = CreateTimer(isPlaying: false);
            // First Dispose() releases state; second Dispose() should be a no-op.
            timer.Dispose();
            var ex = Record.Exception(() => timer.Dispose());
            Assert.Null(ex);
        }

        // ---------------------------------------------------------------
        // IsFinished – null _soundInstance
        // ---------------------------------------------------------------

        [Fact]
        public void IsFinished_WhenSoundInstanceNull_ShouldReturnFalse()
        {
            // _soundInstance?.State == SoundState.Stopped → null == Stopped → false
            var timer = CreateTimer(isPlaying: true);
            Assert.False(timer.IsFinished);
        }

        // ---------------------------------------------------------------
        // Play – silent timer / disposed guard paths
        // ---------------------------------------------------------------

        [Fact]
        public void Play_WhenSilentTimer_ShouldUseGameTimeClock()
        {
            var timer = new SongTimer();
            var start = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);

            Assert.True(timer.Play(start));

            Assert.True(timer.IsPlaying);
            Assert.Equal(400.0, timer.GetCurrentMs(new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.Zero)));
        }

        [Fact]
        public void Play_WhenDisposed_ShouldReturnFalse()
        {
            var timer = CreateTimer(isPlaying: false, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            Assert.False(timer.Play(gameTime));
        }

        // ---------------------------------------------------------------
        // Pause / Resume – elapsed position preservation
        // ---------------------------------------------------------------

        [Fact]
        public void Pause_ThenResume_ShouldPreserveElapsedPosition()
        {
            // Use a real SongTimer (silent) for deterministic GameTime-based testing.
            // Both Pause(GameTime) and Resume(GameTime) now use the GameTime clock,
            // so the elapsed position is preserved exactly.
            var timer = new SongTimer();
            var start = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            Assert.True(timer.Play(start));

            // Elapsed at gameTime 600 ms → 500 ms
            var beforePause = new GameTime(TimeSpan.FromMilliseconds(600), TimeSpan.Zero);
            var elapsedBefore = timer.GetCurrentMs(beforePause);
            Assert.Equal(500.0, elapsedBefore);

            // Pause using GameTime — caches 500 ms from the GameTime clock
            timer.Pause(beforePause);
            Assert.False(timer.IsPlaying);

            // Resume at gameTime 1200 ms — elapsed should continue from 500 ms
            var atResume = new GameTime(TimeSpan.FromMilliseconds(1200), TimeSpan.Zero);
            timer.Resume(atResume);

            // After resume: elapsed = 1700 - (1200 - 500) = 1000 ms
            var afterResume = new GameTime(TimeSpan.FromMilliseconds(1700), TimeSpan.Zero);
            var elapsedAfter = timer.GetCurrentMs(afterResume);
            Assert.Equal(1000.0, elapsedAfter);
        }

        [Fact]
        public void Pause_ShouldCacheElapsedBeforeClearingIsPlaying()
        {
            var timer = new SongTimer();
            var start = new GameTime(TimeSpan.FromMilliseconds(0), TimeSpan.Zero);
            Assert.True(timer.Play(start));

            // Pause caches the elapsed position from the GameTime clock
            var atPause = new GameTime(TimeSpan.FromMilliseconds(250), TimeSpan.Zero);
            timer.Pause(atPause);

            // _cachedElapsedMs should be exactly 250 (GameTime-based, no jitter)
            var cachedMs = ReflectionHelpers.GetPrivateField<double>(timer, "_cachedElapsedMs");
            Assert.Equal(250.0, cachedMs);
        }
    }
}
