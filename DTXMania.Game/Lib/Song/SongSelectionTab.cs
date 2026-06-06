namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Identifies which tab is active in the Song Selection stage.
    /// The cycle order is an explicit map in <see cref="SongSelectionTabExtensions.Next"/>,
    /// not driven by declaration order; add new arms there when extending this enum.
    /// </summary>
    public enum SongSelectionTab
    {
        AllSongs = 0,
        RecentPlays = 1,
        Bookmarks = 2
    }

    public static class SongSelectionTabExtensions
    {
        /// <summary>Cycles to the next tab, wrapping back to the first.</summary>
        public static SongSelectionTab Next(this SongSelectionTab tab)
        {
            return tab switch
            {
                SongSelectionTab.AllSongs => SongSelectionTab.RecentPlays,
                SongSelectionTab.RecentPlays => SongSelectionTab.Bookmarks,
                SongSelectionTab.Bookmarks => SongSelectionTab.AllSongs,
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
                SongSelectionTab.Bookmarks => "Bookmarks",
                _ => tab.ToString()
            };
        }
    }
}
