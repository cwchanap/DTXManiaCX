using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>Formats player-facing skill values consistently across screens and history.</summary>
    public static class SkillValueFormatter
    {
        /// <summary>Clamps negative values and formats two decimal places with invariant culture.</summary>
        public static string Format(double value)
        {
            return Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
