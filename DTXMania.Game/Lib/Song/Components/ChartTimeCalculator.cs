using System;

namespace DTXMania.Game.Lib.Song.Components
{
    internal static class ChartTimeCalculator
    {
        private const int TicksPerMeasure = 192;

        internal static double CalculateTimeMs(int bar, int tick, double bpm)
        {
            if (bpm <= 0)
                throw new ArgumentException(
                    "BPM must be greater than 0",
                    nameof(bpm));

            var totalTicks = (bar * TicksPerMeasure) + tick;
            var measures = totalTicks / (double)TicksPerMeasure;
            return measures * (60000.0 / bpm) * 4.0;
        }
    }
}
