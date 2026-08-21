using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Config
{
    public static class RiskyRange
    {
        public const int Min = 0;
        public const int Max = 10;
        public const int Step = 1;
        public const int Default = 0;

        public static int Clamp(int value) => Math.Clamp(value, Min, Max);

        public static string Format(int value) =>
            value == Default ? "Off" : value.ToString(CultureInfo.InvariantCulture);
    }
}
