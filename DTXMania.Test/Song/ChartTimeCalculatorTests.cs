using System;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Song")]
    public class ChartTimeCalculatorTests
    {
        [Theory]
        [InlineData(0, 0, 120.0, 0.0)]
        [InlineData(0, 96, 120.0, 1000.0)]
        [InlineData(1, 0, 120.0, 2000.0)]
        [InlineData(5, 48, 120.0, 10500.0)]
        [InlineData(1, 0, 60.0, 4000.0)]
        [InlineData(1, 0, 240.0, 1000.0)]
        public void CalculateTimeMs_ValidPosition_ShouldMatchCurrentClock(
            int bar,
            int tick,
            double bpm,
            double expectedMs)
        {
            var actualMs = ChartTimeCalculator.CalculateTimeMs(bar, tick, bpm);

            Assert.Equal(expectedMs, actualMs, precision: 3);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-120.0)]
        public void CalculateTimeMs_NonPositiveBpm_ShouldThrowArgumentException(
            double bpm)
        {
            Assert.Throws<ArgumentException>(
                () => ChartTimeCalculator.CalculateTimeMs(1, 0, bpm));
        }
    }
}
