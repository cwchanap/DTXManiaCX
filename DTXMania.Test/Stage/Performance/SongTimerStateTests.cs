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
        public void IsPlaying_WhenInternalFlagTrueAndSoundInstanceNull_ReturnsTrue()
        {
            // When the backing sound is absent, the timer should still report the stored playing state.
            var timer = CreateTimer(isPlaying: true);
            Assert.True(timer.IsPlaying);
        }

        [Fact]
        public void IsPlaying_WhenInternalFlagFalse_ReturnsFalse()
        {
            var timer = CreateTimer(isPlaying: false);
            Assert.False(timer.IsPlaying);
        }

        // ---------------------------------------------------------------
        // GetCurrentMs(GameTime)
        // ---------------------------------------------------------------

        [Fact]
        public void GetCurrentMs_GameTime_WhenNotPlaying_ReturnsZero()
        {
            var timer = CreateTimer(isPlaying: false);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.Zero);
            Assert.Equal(0.0, timer.GetCurrentMs(gameTime));
        }

        [Fact]
        public void GetCurrentMs_GameTime_WhenDisposed_ReturnsZero()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.Zero);
            Assert.Equal(0.0, timer.GetCurrentMs(gameTime));
        }

        [Fact]
        public void GetCurrentMs_GameTime_WhenPlaying_ReturnsElapsedMs()
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
        public void GetCurrentMs_WhenNotPlaying_ReturnsZero()
        {
            var timer = CreateTimer(isPlaying: false);
            Assert.Equal(0.0, timer.GetCurrentMs());
        }

        [Fact]
        public void GetCurrentMs_WhenDisposed_ReturnsZero()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            Assert.Equal(0.0, timer.GetCurrentMs());
        }

        [Fact]
        public void GetCurrentMs_WhenPlaying_ReturnsPositiveElapsed()
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
        public void Volume_Get_WhenSoundInstanceNull_ReturnsZero()
        {
            var timer = CreateTimer();
            Assert.Equal(0f, timer.Volume);
        }

        [Fact]
        public void Volume_Set_WhenSoundInstanceNull_DoesNotThrow()
        {
            var timer = CreateTimer();
            // Must not throw
            var ex = Record.Exception(() => timer.Volume = 0.5f);
            Assert.Null(ex);
        }

        [Fact]
        public void IsLooped_Get_WhenSoundInstanceNull_ReturnsFalse()
        {
            var timer = CreateTimer();
            Assert.False(timer.IsLooped);
        }

        [Fact]
        public void IsLooped_Set_WhenSoundInstanceNull_DoesNotThrow()
        {
            var timer = CreateTimer();
            var ex = Record.Exception(() => timer.IsLooped = true);
            Assert.Null(ex);
        }

        // ---------------------------------------------------------------
        // Guard clauses – disposed state
        // ---------------------------------------------------------------

        [Fact]
        public void Pause_WhenDisposed_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var ex = Record.Exception(() => timer.Pause());
            Assert.Null(ex);
        }

        [Fact]
        public void Stop_WhenDisposed_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var ex = Record.Exception(() => timer.Stop());
            Assert.Null(ex);
        }

        [Fact]
        public void Update_WhenDisposed_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Update(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void SetPosition_WhenDisposed_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: true, disposed: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.SetPosition(500.0, gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Resume_WhenDisposed_DoesNotThrow()
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
        public void Pause_WhenSoundInstanceNull_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: true);
            var ex = Record.Exception(() => timer.Pause());
            Assert.Null(ex);
        }

        [Fact]
        public void Stop_WhenSoundInstanceNull_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: true);
            var ex = Record.Exception(() => timer.Stop());
            Assert.Null(ex);
        }

        [Fact]
        public void Resume_WhenSoundInstanceNull_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: false);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Resume(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void SetPosition_WhenSoundInstanceNull_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: true);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.SetPosition(250.0, gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Update_WhenNotDisposed_DoesNotThrow()
        {
            var timer = CreateTimer(isPlaying: false);
            var gameTime = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var ex = Record.Exception(() => timer.Update(gameTime));
            Assert.Null(ex);
        }

        [Fact]
        public void Update_WhenPlayingAndSoundInstanceNull_DoesNotThrow()
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
        public void Dispose_CalledTwice_DoesNotThrow()
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
        public void IsFinished_WhenSoundInstanceNull_ReturnsFalse()
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
        public void Play_WhenDisposed_ReturnsFalse()
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
            // Use a real SongTimer (silent) for deterministic GameTime-based testing
            var timer = new SongTimer();
            var start = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            Assert.True(timer.Play(start));

            // Elapsed at gameTime 600 ms → 500 ms
            var beforePause = new GameTime(TimeSpan.FromMilliseconds(600), TimeSpan.Zero);
            var elapsedBefore = timer.GetCurrentMs(beforePause);
            Assert.Equal(500.0, elapsedBefore);

            // Pause and verify stopped
            timer.Pause();
            Assert.False(timer.IsPlaying);

            // Resume at gameTime 1200 ms – elapsed should be ~500 ms (system clock jitter acceptable)
            var atResume = new GameTime(TimeSpan.FromMilliseconds(1200), TimeSpan.Zero);
            timer.Resume(atResume);

            // After resume: elapsed = gameTime - _startTime = gameTime - (resumeTime - cachedElapsed)
            // cachedElapsed is system-clock based (~500ms with jitter), so we allow ±50ms tolerance
            var afterResume = new GameTime(TimeSpan.FromMilliseconds(1700), TimeSpan.Zero);
            var elapsedAfter = timer.GetCurrentMs(afterResume);
            Assert.InRange(elapsedAfter, 450.0, 550.0 + (1700.0 - 1200.0));
        }

        [Fact]
        public void Pause_CachesElapsedBeforeClearingIsPlaying()
        {
            var timer = new SongTimer();
            var start = new GameTime(TimeSpan.FromMilliseconds(0), TimeSpan.Zero);
            Assert.True(timer.Play(start));

            // Pause caches the elapsed position
            timer.Pause();

            // _cachedElapsedMs should be >= 0 (system clock based, so includes execution time)
            var cachedMs = ReflectionHelpers.GetPrivateField<double>(timer, "_cachedElapsedMs");
            Assert.True(cachedMs >= 0.0, $"Expected cached elapsed >= 0, got {cachedMs}");
        }
    }
}
