using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    public class ScrollSpeedRangeTests
    {
        [Theory]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(400, 400)]
        [InlineData(117, 100)]
        [InlineData(130, 150)]
        [InlineData(124, 100)]
        [InlineData(125, 150)]
        [InlineData(425, 400)]
        [InlineData(0, 50)]
        [InlineData(-50, 50)]
        [InlineData(9999, 400)]
        public void SnapAndClamp_ReturnsExpected(int input, int expected)
        {
            Assert.Equal(expected, ScrollSpeedRange.SnapAndClamp(input));
        }

        [Theory]
        [InlineData(50, "x0.5")]
        [InlineData(100, "x1.0")]
        [InlineData(150, "x1.5")]
        [InlineData(400, "x4.0")]
        public void Format_ReturnsXMultiplier(int percent, string expected)
        {
            Assert.Equal(expected, ScrollSpeedRange.Format(percent));
        }

        [Fact]
        public void Constants_HaveExpectedValues()
        {
            Assert.Equal(50, ScrollSpeedRange.Min);
            Assert.Equal(400, ScrollSpeedRange.Max);
            Assert.Equal(50, ScrollSpeedRange.Step);
            Assert.Equal(100, ScrollSpeedRange.Default);
        }
    }
}
