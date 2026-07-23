using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Canonical range, step, and display formatting for gameplay speed.
    /// Values are persisted as integer percentages.
    /// </summary>
    public static class PlaySpeedRange
    {
        public const int Min = 50;
        public const int Max = 150;
        public const int Step = 5;
        public const int Default = 100;

        public static int SnapAndClamp(int percent)
        {
            var snapped = (long)Math.Round(
                percent / (double)Step,
                MidpointRounding.AwayFromZero) * Step;
            if (snapped < Min) return Min;
            if (snapped > Max) return Max;
            return (int)snapped;
        }

        public static string Format(int percent)
        {
            return (percent / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "x";
        }
    }
}