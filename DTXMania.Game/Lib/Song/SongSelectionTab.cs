namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Identifies which tab is active in the Song Selection stage.
    /// Ordering here defines the cycle order used by <see cref="SongSelectionTabExtensions.Next"/>.
    /// </summary>
    public enum SongSelectionTab
    {
        AllSongs = 0,
        RecentPlays = 1
    }

    public static class SongSelectionTabExtensions
    {
        /// <summary>Cycles to the next tab, wrapping back to the first.</summary>
        public static SongSelectionTab Next(SongSelectionTab tab)
        {
            return tab switch
            {
                SongSelectionTab.AllSongs => SongSelectionTab.RecentPlays,
                SongSelectionTab.RecentPlays => SongSelectionTab.AllSongs,
                _ => SongSelectionTab.AllSongs
            };
        }

        /// <summary>Human-readable label shown on the tab bar.</summary>
        public static string DisplayLabel(this SongSelectionTab tab)
        {
            return tab switch
            {
                SongSelectionTab.AllSongs => "All Songs",
                SongSelectionTab.RecentPlays => "Recent",
                _ => tab.ToString()
            };
        }
    }
}
