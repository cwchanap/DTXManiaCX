using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for BGMEvent (Background Music Event)
    /// </summary>
    [Trait("Category", "Song")]
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

        #region Authored Position Tests

        [Fact]
        public void AuthoredPosition_ShouldResolveWhenContainingChartIsFinalized()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var evt = new BGMEvent(5, 48, "01");

            chart.AddBGMEvent(evt);
            chart.FinalizeChart();

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
