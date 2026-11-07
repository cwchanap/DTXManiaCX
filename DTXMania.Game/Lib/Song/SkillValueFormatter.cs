using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Song
{
    public static class SkillValueFormatter
    {
        public static string Format(double value)
        {
            return Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
