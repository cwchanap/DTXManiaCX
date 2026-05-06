using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Range, step, and formatting for the visual scroll-speed setting.
    /// Single source of truth used by ConfigManager, ConfigStage, SongSelectionStage,
    /// and the in-game ScrollSpeedIndicator.
    /// </summary>
    public static class ScrollSpeedRange
    {
        public const int Min = 50;
        public const int Max = 400;
        public const int Step = 50;
        public const int Default = 100;

        /// <summary>
        /// Snaps an arbitrary integer percent to the nearest multiple of Step,
        /// then clamps to [Min, Max].
        /// </summary>
        public static int SnapAndClamp(int percent)
        {
            var snapped = (long)Math.Round(percent / (double)Step, MidpointRounding.AwayFromZero) * Step;
            if (snapped < Min) return Min;
            if (snapped > Max) return Max;
            return (int)snapped;
        }

        /// <summary>
        /// Formats a percent as "x1.0", "x1.5", etc. (one decimal).
        /// </summary>
        public static string Format(int percent)
        {
            var multiplier = percent / 100.0;
            return "x" + multiplier.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
