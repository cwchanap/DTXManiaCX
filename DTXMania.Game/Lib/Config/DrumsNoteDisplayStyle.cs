using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Config
{
    public enum DrumsNoteDisplayStyle
    {
        Lanes = 0,
        Tab = 1,
    }

    public static class DrumsNoteDisplayStyleExtensions
    {
        private static readonly (DrumsNoteDisplayStyle Style, string Label)[] OptionTable =
        {
            (DrumsNoteDisplayStyle.Lanes, "LANES"),
            (DrumsNoteDisplayStyle.Tab, "TAB"),
        };

        public static IReadOnlyList<(DrumsNoteDisplayStyle Style, string Label)> Options => OptionTable;

        public static IReadOnlyList<string> Labels { get; } =
            OptionTable.Select(option => option.Label).ToArray();

        public static string ToLabel(this DrumsNoteDisplayStyle style) =>
            OptionTable.First(option => option.Style == style).Label;

        public static DrumsNoteDisplayStyle FromLabel(string label) =>
            OptionTable.First(option => string.Equals(option.Label, label, StringComparison.OrdinalIgnoreCase)).Style;
    }
}
