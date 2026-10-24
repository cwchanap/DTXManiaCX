using System.Collections.Generic;

namespace DTXMania.Game.Lib.Config
{
    public enum DrumsNoteDisplayStyle
    {
        Lanes = 0,
        Tab = 1,
    }

    public static class DrumsNoteDisplayStyleExtensions
    {
        private static readonly EnumOptionTable<DrumsNoteDisplayStyle> OptionTable = new(
            (DrumsNoteDisplayStyle.Lanes, "LANES"),
            (DrumsNoteDisplayStyle.Tab, "TAB"));

        public static IReadOnlyList<(DrumsNoteDisplayStyle Style, string Label)> Options =>
            OptionTable.Options;

        public static IReadOnlyList<string> Labels => OptionTable.Labels;

        public static string ToLabel(this DrumsNoteDisplayStyle style) =>
            OptionTable.ToLabel(style);

        public static DrumsNoteDisplayStyle FromLabel(string label) =>
            OptionTable.FromLabel(label);
    }
}
