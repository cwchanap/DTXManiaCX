using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Formats the two DTX chart-level encodings for display.
    /// Levels 100 and above are hundredths-encoded (815 = 8.15).
    /// Smaller levels use tenths plus the separate decimal field (78 + 33 = 8.13).
    /// </summary>
    public static class ChartLevelFormatter
    {
        public static string Format(int level, int levelDec = 0)
        {
            if (level <= 0)
                return "--";

            double actualLevel = level >= 100
                ? level / 100.0
                : (level / 10.0) + (Math.Max(0, levelDec) / 100.0);

            return actualLevel.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
