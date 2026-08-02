#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Immutable-at-publication view of the song library. The lists are copied at
    /// construction so a subsequent publication cannot mutate a reader's view.
    /// </summary>
    public sealed record SongLibrarySnapshot
    {
        public SongLibrarySnapshot(
            long Version,
            IReadOnlyList<SongListNode> RootSongs,
            IReadOnlyList<string> ActiveRoots,
            int EnumeratedFileCount,
            int DiscoveredScoreCount)
        {
            ArgumentNullException.ThrowIfNull(RootSongs);
            ArgumentNullException.ThrowIfNull(ActiveRoots);

            this.Version = Version;
            this.RootSongs = Array.AsReadOnly(RootSongs.ToArray());
            this.ActiveRoots = Array.AsReadOnly(ActiveRoots.ToArray());
            this.EnumeratedFileCount = EnumeratedFileCount;
            this.DiscoveredScoreCount = DiscoveredScoreCount;
        }

        public long Version { get; }
        public IReadOnlyList<SongListNode> RootSongs { get; }
        public IReadOnlyList<string> ActiveRoots { get; }
        public int EnumeratedFileCount { get; }
        public int DiscoveredScoreCount { get; }

        public void Deconstruct(
            out long Version,
            out IReadOnlyList<SongListNode> RootSongs,
            out IReadOnlyList<string> ActiveRoots,
            out int EnumeratedFileCount,
            out int DiscoveredScoreCount)
        {
            Version = this.Version;
            RootSongs = this.RootSongs;
            ActiveRoots = this.ActiveRoots;
            EnumeratedFileCount = this.EnumeratedFileCount;
            DiscoveredScoreCount = this.DiscoveredScoreCount;
        }
    }

    public sealed class SongLibraryPublishedEventArgs : EventArgs
    {
        public SongLibraryPublishedEventArgs(SongLibrarySnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public SongLibrarySnapshot Snapshot { get; }
    }
}
