using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Canonical range, step, and display formatting for independent pitch adjustment.
    /// Values are persisted as integer semitones.
    /// </summary>
    public static class PitchRange
    {
        public const int Min = -12;
        public const int Max = 12;
        public const int Step = 1;
        public const int Default = 0;

        public static int SnapAndClamp(int semitones)
        {
            var snapped = (long)Math.Round(
                semitones / (double)Step,
                MidpointRounding.AwayFromZero) * Step;
            if (snapped < Min) return Min;
            if (snapped > Max) return Max;
            return (int)snapped;
        }

        public static string Format(int semitones)
        {
            var canonical = SnapAndClamp(semitones);
            var value = canonical > 0
                ? "+" + canonical.ToString(CultureInfo.InvariantCulture)
                : canonical.ToString(CultureInfo.InvariantCulture);
            return value + " st";
        }
    }
}