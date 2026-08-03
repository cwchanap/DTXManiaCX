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
            long version,
            IReadOnlyList<SongListNode> rootSongs,
            IReadOnlyList<string> activeRoots,
            int enumeratedFileCount,
            int discoveredScoreCount)
        {
            ArgumentNullException.ThrowIfNull(rootSongs);
            ArgumentNullException.ThrowIfNull(activeRoots);

            Version = version;
            RootSongs = Array.AsReadOnly(rootSongs.ToArray());
            ActiveRoots = Array.AsReadOnly(activeRoots.ToArray());
            EnumeratedFileCount = enumeratedFileCount;
            DiscoveredScoreCount = discoveredScoreCount;
        }

        public long Version { get; }
        public IReadOnlyList<SongListNode> RootSongs { get; }
        public IReadOnlyList<string> ActiveRoots { get; }
        public int EnumeratedFileCount { get; }
        public int DiscoveredScoreCount { get; }

        public void Deconstruct(
            out long version,
            out IReadOnlyList<SongListNode> rootSongs,
            out IReadOnlyList<string> activeRoots,
            out int enumeratedFileCount,
            out int discoveredScoreCount)
        {
            version = Version;
            rootSongs = RootSongs;
            activeRoots = ActiveRoots;
            enumeratedFileCount = EnumeratedFileCount;
            discoveredScoreCount = DiscoveredScoreCount;
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
