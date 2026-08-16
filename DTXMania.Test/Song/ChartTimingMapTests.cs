using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Song")]
    public class ChartTimingMapTests
    {
        [Fact]
        public void CalculateTimeMs_BaseBpmOnly_ShouldMatchOldClock()
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 2);

            Assert.Equal(0.0, map.CalculateTimeMs(0, 0), 3);
            Assert.Equal(500.0, map.CalculateTimeMs(0, 48), 3);
            Assert.Equal(1000.0, map.CalculateTimeMs(0, 96), 3);
            Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
            Assert.Equal(5000.0, map.CalculateTimeMs(2, 96), 3);
        }

        [Fact]
        public void CalculateTimeMs_FractionalTick_ShouldResolveAtBaseBpm()
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 0);

            Assert.Equal(1005.2083333333, map.CalculateTimeMs(0, 96.5), 6);
        }

        [Fact]
        public void CalculateTimeMs_FractionalTickAroundTempoAnchor_ShouldChooseContainingAnchor()
        {
            var map = new ChartTimingMap();
            map.SetTempoChange(0, 96, 240.0);
            map.Rebuild(120.0, throughBar: 0);

            Assert.Equal(994.7916666667, map.CalculateTimeMs(0, 95.5), 6);
            Assert.Equal(1000.0, map.CalculateTimeMs(0, 96.0), 6);
            Assert.Equal(1002.6041666667, map.CalculateTimeMs(0, 96.5), 6);
        }

        [Theory]
        [InlineData(-0.5)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void CalculateTimeMs_NegativeOrNonFiniteTick_ShouldThrow(double tick)
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 0);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => map.CalculateTimeMs(0, tick));
        }

        [Fact]
        public void CalculateTimeMs_OversizedFractionalTick_ShouldCarryIntoLaterBar()
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 1);

            Assert.Equal(
                map.CalculateTimeMs(1, 0.5),
                map.CalculateTimeMs(0, 192.5),
                6);
        }

        [Fact]
        public void CalculateTimeMs_FractionalTickBeyondCompiledHorizon_ShouldThrow()
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 1);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => map.CalculateTimeMs(1, 192.5));
        }

        [Fact]
        public void GetMeasureLengthMultiplier_ShouldReadCompiledBarStartAnchor()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 0.75);
            map.Rebuild(120.0, throughBar: 1);

            Assert.Equal(0.75, map.GetMeasureLengthMultiplier(0));
            Assert.Equal(1.0, map.GetMeasureLengthMultiplier(1));
        }

        [Fact]
        public void CalculateTimeMs_ShortMeasure_ShouldShiftFollowingBar()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 0.5);
            map.Rebuild(120.0, 1);

            Assert.Equal(1000.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void CalculateTimeMs_ExtendedMeasure_ShouldShiftFollowingBar()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 1.5);
            map.Rebuild(120.0, 1);

            Assert.Equal(3000.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void CalculateTimeMs_MeasureLengthOnBarOne_ShouldNotAffectBarZero()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(1, 0.5);
            map.Rebuild(120.0, 2);

            Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
            Assert.Equal(3000.0, map.CalculateTimeMs(2, 0), 3);
        }

        [Fact]
        public void CalculateTimeMs_HalfwayTempoChange_ShouldIntegrateSegments()
        {
            var map = new ChartTimingMap();
            map.SetTempoChange(0, 96, 240.0);
            map.Rebuild(120.0, 1);

            Assert.Equal(1000.0, map.CalculateTimeMs(0, 96), 3);
            Assert.Equal(1500.0, map.CalculateTimeMs(1, 0), 3);
            Assert.Equal(2000.0, map.CalculateTimeMs(1, 96), 3);
        }

        [Fact]
        public void CalculateTimeMs_TempoChangeAtTickZero_ShouldApplyImmediately()
        {
            var map = new ChartTimingMap();
            map.SetTempoChange(0, 0, 240.0);
            map.Rebuild(120.0, 1);

            Assert.Equal(500.0, map.CalculateTimeMs(0, 96), 3);
            Assert.Equal(1000.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void SetTempoChange_SamePosition_ShouldUseLastValue()
        {
            var map = new ChartTimingMap();
            map.SetTempoChange(0, 96, 180.0);
            map.SetTempoChange(0, 96, 240.0);
            map.Rebuild(120.0, 1);

            Assert.Equal(1500.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void CalculateTimeMs_MeasureLengthAndTempoChange_ShouldCompose()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 0.5);
            map.SetTempoChange(0, 96, 240.0);
            map.Rebuild(120.0, 1);

            Assert.Equal(500.0, map.CalculateTimeMs(0, 96), 3);
            Assert.Equal(750.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void NormalizePosition_OversizedTick_ShouldFoldIntoLaterBar()
        {
            Assert.Equal((1, 0), ChartTimingMap.NormalizePosition(0, 192));
            Assert.Equal((3, 48), ChartTimingMap.NormalizePosition(2, 240));
            Assert.Equal((5, 0), ChartTimingMap.NormalizePosition(0, 960));
        }

        [Fact]
        public void CalculateTimeMs_OversizedTick_ShouldUseEachCrossedMeasureLength()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 0.5); // 1000 ms
            map.SetMeasureLength(1, 1.5); // 3000 ms
            map.Rebuild(120.0, 2);

            // (0, 384) canonicalizes to (2, 0), so both measures contribute.
            Assert.Equal(4000.0, map.CalculateTimeMs(0, 384), 3);
            Assert.Equal(4000.0, map.CalculateTimeMs(2, 0), 3);
        }

        [Fact]
        public void Rebuild_Repeated_ShouldBeDeterministic()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 0.5);
            map.SetTempoChange(0, 96, 240.0);
            map.Rebuild(120.0, 1);
            var expected = map.CalculateTimeMs(1, 0);

            map.Rebuild(120.0, 1);

            Assert.Equal(expected, map.CalculateTimeMs(1, 0), 3);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-120.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Rebuild_InvalidBaseBpm_ShouldThrow(double bpm)
        {
            var map = new ChartTimingMap();
            Assert.Throws<ArgumentException>(() => map.Rebuild(bpm, 1));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void SetMeasureLength_NonFiniteMultiplier_ShouldIgnore(double multiplier)
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, multiplier);
            map.Rebuild(120.0, 1);

            Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void SetTempoChange_NonFiniteBpm_ShouldIgnore(double bpm)
        {
            var map = new ChartTimingMap();
            map.SetTempoChange(0, 96, bpm);
            map.Rebuild(120.0, 1);

            Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void NormalizePosition_NegativeTick_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ChartTimingMap.NormalizePosition(0, -1));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        public void GetMeasureLengthMultiplier_BarOutsideCompiledRange_ShouldThrow(int bar)
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 1);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => map.GetMeasureLengthMultiplier(bar));
        }

        [Fact]
        public void CalculateTimeMs_NegativeBar_ShouldThrow()
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 1);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => map.CalculateTimeMs(-1, 0));
        }

        [Fact]
        public void CalculateTimeMs_TickOverflowingIntRange_ShouldThrow()
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 1);

            // measureOffset = floor(tick / 192) exceeds int.MaxValue, tripping the
            // overflow guard in NormalizeFractionalPosition before any anchor lookup.
            var overflowingTick = (double)int.MaxValue * ChartTimingMap.TicksPerMeasure + ChartTimingMap.TicksPerMeasure;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => map.CalculateTimeMs(0, overflowingTick));
        }
    }
}
