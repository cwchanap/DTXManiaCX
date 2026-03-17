using System;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for BGMEvent (Background Music Event)
    /// </summary>
    public class BGMEventTests
    {
        #region Constructor Tests

        [Fact]
        public void DefaultConstructor_ShouldSetDefaultValues()
        {
            var evt = new BGMEvent();
            Assert.Equal(0, evt.Bar);
            Assert.Equal(0, evt.Tick);
            Assert.Equal(0.0, evt.TimeMs);
            Assert.Equal("", evt.WavId);
            Assert.Equal("", evt.AudioFilePath);
        }

        [Fact]
        public void ParameterizedConstructor_ShouldSetProperties()
        {
            var evt = new BGMEvent(bar: 4, tick: 96, wavId: "01");
            Assert.Equal(4, evt.Bar);
            Assert.Equal(96, evt.Tick);
            Assert.Equal("01", evt.WavId);
        }

        #endregion

        #region CalculateTimeMs Tests

        [Fact]
        public void CalculateTimeMs_AtBpm120_Bar0_Tick0_ShouldBeZero()
        {
            var evt = new BGMEvent(0, 0, "01");
            evt.CalculateTimeMs(120.0);
            Assert.Equal(0.0, evt.TimeMs, precision: 3);
        }

        [Fact]
        public void CalculateTimeMs_AtBpm120_Bar1_Tick0_ShouldBe2000Ms()
        {
            // 1 bar at 120BPM: (1*192+0)/192 * (60000/120) * 4 = 1 * 500 * 4 = 2000ms
            var evt = new BGMEvent(1, 0, "01");
            evt.CalculateTimeMs(120.0);
            Assert.Equal(2000.0, evt.TimeMs, precision: 3);
        }

        [Fact]
        public void CalculateTimeMs_AtBpm120_Bar0_HalfMeasure_ShouldBe1000Ms()
        {
            // Tick 96 = half bar: (0*192+96)/192 * (60000/120) * 4 = 0.5 * 500 * 4 = 1000ms
            var evt = new BGMEvent(0, 96, "01");
            evt.CalculateTimeMs(120.0);
            Assert.Equal(1000.0, evt.TimeMs, precision: 3);
        }

        [Fact]
        public void CalculateTimeMs_AtBpm60_Bar1_ShouldBe4000Ms()
        {
            // 1 bar at 60BPM: 1 * (60000/60) * 4 = 1 * 1000 * 4 = 4000ms
            var evt = new BGMEvent(1, 0, "01");
            evt.CalculateTimeMs(60.0);
            Assert.Equal(4000.0, evt.TimeMs, precision: 3);
        }

        [Fact]
        public void CalculateTimeMs_AtBpm240_Bar1_ShouldBe1000Ms()
        {
            // 1 bar at 240BPM: 1 * (60000/240) * 4 = 1 * 250 * 4 = 1000ms
            var evt = new BGMEvent(1, 0, "01");
            evt.CalculateTimeMs(240.0);
            Assert.Equal(1000.0, evt.TimeMs, precision: 3);
        }

        [Fact]
        public void CalculateTimeMs_ZeroBpm_ShouldThrowArgumentException()
        {
            var evt = new BGMEvent(1, 0, "01");
            Assert.Throws<ArgumentException>(() => evt.CalculateTimeMs(0.0));
        }

        [Fact]
        public void CalculateTimeMs_NegativeBpm_ShouldThrowArgumentException()
        {
            var evt = new BGMEvent(1, 0, "01");
            Assert.Throws<ArgumentException>(() => evt.CalculateTimeMs(-120.0));
        }

        [Fact]
        public void CalculateTimeMs_Bar5_Tick48_AtBpm120()
        {
            // 5 bars + 48 ticks = (5*192 + 48)/192 = 5.25 measures
            // 5.25 * 500 * 4 = 10500ms
            var evt = new BGMEvent(5, 48, "01");
            evt.CalculateTimeMs(120.0);
            Assert.Equal(10500.0, evt.TimeMs, precision: 3);
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_ShouldIncludeWavId()
        {
            var evt = new BGMEvent(2, 0, "05");
            evt.TimeMs = 4000.0;
            evt.AudioFilePath = "/music/kick.wav";
            var result = evt.ToString();

            Assert.Contains("05", result);
            Assert.Contains("2", result);
        }

        [Fact]
        public void ToString_WithEmptyAudioFilePath_ShouldNotThrow()
        {
            var evt = new BGMEvent(0, 0, "01");
            var result = evt.ToString();
            Assert.NotNull(result);
        }

        [Fact]
        public void ToString_WithFullPath_ShouldShowFilename()
        {
            var evt = new BGMEvent(1, 0, "03");
            evt.AudioFilePath = "/music/folder/bgm.wav";
            var result = evt.ToString();
            Assert.Contains("bgm.wav", result);
        }

        #endregion

        #region Property Tests

        [Fact]
        public void Properties_SetAndGet_ShouldWork()
        {
            var evt = new BGMEvent();
            evt.Bar = 10;
            evt.Tick = 48;
            evt.TimeMs = 5000.0;
            evt.WavId = "FF";
            evt.AudioFilePath = "/test/sound.ogg";

            Assert.Equal(10, evt.Bar);
            Assert.Equal(48, evt.Tick);
            Assert.Equal(5000.0, evt.TimeMs);
            Assert.Equal("FF", evt.WavId);
            Assert.Equal("/test/sound.ogg", evt.AudioFilePath);
        }

        #endregion
    }
}
