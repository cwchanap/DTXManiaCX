using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Config
{
    public enum DrumsLaneDisplayMode
    {
        AllOn = 0,
        LaneOff = 1,
        LineOff = 2,
        AllOff = 3,
    }

    public static class DrumsLaneDisplayModeExtensions
    {
        private static readonly (DrumsLaneDisplayMode Mode, string Label)[] OptionTable =
        {
            (DrumsLaneDisplayMode.AllOn, "ALL ON"),
            (DrumsLaneDisplayMode.LaneOff, "LANE OFF"),
            (DrumsLaneDisplayMode.LineOff, "LINE OFF"),
            (DrumsLaneDisplayMode.AllOff, "ALL OFF"),
        };

        public static IReadOnlyList<(DrumsLaneDisplayMode Mode, string Label)> Options => OptionTable;

        public static IReadOnlyList<string> Labels { get; } =
            OptionTable.Select(option => option.Label).ToArray();

        public static bool ShowsLaneBackground(this DrumsLaneDisplayMode mode) =>
            mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LineOff;

        public static bool ShowsMeasureLines(this DrumsLaneDisplayMode mode) =>
            mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LaneOff;

        public static string ToLabel(this DrumsLaneDisplayMode mode) =>
            OptionTable.First(option => option.Mode == mode).Label;

        public static DrumsLaneDisplayMode FromLabel(string label) =>
            OptionTable.First(option => string.Equals(option.Label, label, StringComparison.OrdinalIgnoreCase)).Mode;
    }
}
