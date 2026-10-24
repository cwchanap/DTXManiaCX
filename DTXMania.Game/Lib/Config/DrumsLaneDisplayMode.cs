using System.Collections.Generic;

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
        private static readonly EnumOptionTable<DrumsLaneDisplayMode> OptionTable = new(
            (DrumsLaneDisplayMode.AllOn, "ALL ON"),
            (DrumsLaneDisplayMode.LaneOff, "LANE OFF"),
            (DrumsLaneDisplayMode.LineOff, "LINE OFF"),
            (DrumsLaneDisplayMode.AllOff, "ALL OFF"));

        public static IReadOnlyList<(DrumsLaneDisplayMode Mode, string Label)> Options =>
            OptionTable.Options;

        public static IReadOnlyList<string> Labels => OptionTable.Labels;

        public static bool ShowsLaneBackground(this DrumsLaneDisplayMode mode) =>
            mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LineOff;

        public static bool ShowsMeasureLines(this DrumsLaneDisplayMode mode) =>
            mode is DrumsLaneDisplayMode.AllOn or DrumsLaneDisplayMode.LaneOff;

        public static string ToLabel(this DrumsLaneDisplayMode mode) =>
            OptionTable.ToLabel(mode);

        public static DrumsLaneDisplayMode FromLabel(string label) =>
            OptionTable.FromLabel(label);
    }
}
