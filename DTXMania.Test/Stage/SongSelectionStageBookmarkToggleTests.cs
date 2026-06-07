using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework.Input;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageBookmarkToggleTests
    {
        private static SongListNode BookmarkableNode(string title, bool bookmarked = false)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Id = 42,
                Title = title,
                IsBookmarked = bookmarked
            };
            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                DatabaseSong = song
            };
        }

        private static SongListDisplay DisplayWithSelection(SongListNode node)
        {
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { node }
            };
            // CurrentList setter resets SelectedIndex to 0 -> SelectedSong == node.
            return display;
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_OnScoreNode_FlipsBookmarkFlag()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            Assert.True(node.DatabaseSong!.IsBookmarked);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");
            Assert.False(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_RegistersPendingSerializedWrite()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            // The per-song serialization dictionary must track an in-flight write so rapid
            // toggles apply in order and the last user intent wins in the database.
            var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, System.Threading.Tasks.Task>>(stage, "_pendingBookmarkWrites");
            Assert.NotNull(pending);
            Assert.True(pending!.ContainsKey(node.DatabaseSong!.Id));
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_OnFolderNode_DoesNothing()
        {
            var stage = CreateStage();
            var folder = new SongListNode { Type = NodeType.Box, Title = "Folder" };
            var display = DisplayWithSelection(folder);
            AttachCoreUi(stage, display);

            var ex = Record.Exception(() =>
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"));

            Assert.Null(ex);
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_OnBookmarksTab_Unbookmark_RemovesNodeAndFlagsRefresh()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: true);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode> { node });

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            Assert.False(node.DatabaseSong!.IsBookmarked);
            var remaining = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.Empty(remaining);
            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        [Fact]
        public void HandleLaneHitForBookmark_OnFloorTom_TogglesBookmark()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "HandleLaneHitForBookmark", 1); // Floor Tom = lane 1

            Assert.True(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void HandleLaneHitForBookmark_OnOtherLane_DoesNotToggle()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "HandleLaneHitForBookmark", 4); // Snare

            Assert.False(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void DetectBookmarkKey_WhenBPressed_TogglesBookmark()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_inputManager", new BookmarkKeyInputManager());

            InvokePrivateMethod(stage, "DetectBookmarkKey");

            Assert.True(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void DetectBookmarkKey_WhenInputManagerNull_DoesNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "DetectBookmarkKey"));
            Assert.Null(ex);
        }

        // Cross-tab reconciliation: a toggle must propagate the new flag to every in-memory
        // representation of the song (browse tree, Recent list, Bookmarks list), each of which
        // holds a distinct entity instance. Dropping any of the three Apply calls would leave
        // one surface's star out of sync.
        [Fact]
        public void ToggleBookmarkForSelectedSong_ReconcilesFlagAcrossAllTabs()
        {
            var stage = CreateStage();
            var selectedSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 42, Title = "S", IsBookmarked = false };
            var rootSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 42, Title = "S", IsBookmarked = false };
            var recentSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 42, Title = "S", IsBookmarked = false };
            var bookmarkSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 42, Title = "S", IsBookmarked = false };

            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode>
                {
                    new() { Type = NodeType.Score, Title = "S", DatabaseSong = selectedSong }
                }
            };
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_recentPlayNodes",
                new List<SongListNode> { new() { Type = NodeType.Score, DatabaseSong = recentSong } });
            SetPrivateField(stage, "_bookmarkNodes",
                new List<SongListNode> { new() { Type = NodeType.Score, DatabaseSong = bookmarkSong } });

            var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
            var savedRoots = roots.ToList();
            roots.Clear();
            roots.Add(new SongListNode { Type = NodeType.Score, Title = "S", DatabaseSong = rootSong });

            try
            {
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

                Assert.True(selectedSong.IsBookmarked);   // direct flip on the selected node
                Assert.True(rootSong.IsBookmarked);        // reconciled via RootSongs
                Assert.True(recentSong.IsBookmarked);      // reconciled via _recentPlayNodes
                Assert.True(bookmarkSong.IsBookmarked);    // reconciled via _bookmarkNodes
            }
            finally
            {
                roots.Clear();
                roots.AddRange(savedRoots);
            }
        }

        // Guards the "&& !newState" condition: bookmarking ON while on the Bookmarks tab must
        // flip the flag but must NOT remove the row (only an un-bookmark removes it).
        [Fact]
        public void ToggleBookmarkForSelectedSong_OnBookmarksTab_BookmarkOn_DoesNotRemoveRow()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode> { node });

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            Assert.True(node.DatabaseSong!.IsBookmarked);
            var remaining = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.Single(remaining); // row NOT removed because newState == true
            Assert.False(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        // Rapid double-toggle must serialize so the last user intent lands in the DB. Without
        // the per-song chaining in _pendingBookmarkWrites, two fire-and-forget writes could
        // complete out of order and leave the DB divergent from the in-memory flag.
        [Fact]
        public async Task ToggleBookmarkForSelectedSong_RapidDoubleToggle_LastIntentWinsInDb()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"bm_order_{Guid.NewGuid():N}.db");
            SongManager.ResetInstanceForTesting();
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(dbPath, purgeDatabaseFirst: true);
            var db = manager.DatabaseService!;
            var seed = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Song", Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await db.AddSongAsync(seed, chart);
            var songId = (await db.GetSongsAsync()).Single(s => s.Title == "Song").Id;

            try
            {
                var stage = CreateStage();
                var nodeEntity = new DTXMania.Game.Lib.Song.Entities.Song { Id = songId, Title = "Song", IsBookmarked = false };
                var node = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = nodeEntity };
                var display = new SongListDisplay { CurrentList = new List<SongListNode> { node } };
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // ON
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // OFF

                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
                await pending[songId]; // await the serialized chain so both writes settle

                var persisted = (await db.GetSongsAsync()).Single(s => s.Id == songId);
                Assert.False(persisted.IsBookmarked); // last intent (OFF) won
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        // The optimistic flip must roll back when the persist faults, so the star does not
        // silently diverge from the DB. Here the DB is purged after seeding so the write
        // faults; after draining the revert queue the in-memory flag must return to its
        // pre-toggle value.
        [Fact]
        public async Task ToggleBookmarkForSelectedSong_WhenPersistFaults_RevertsInMemoryFlag()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"bm_revert_{Guid.NewGuid():N}.db");
            SongManager.ResetInstanceForTesting();
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(dbPath, purgeDatabaseFirst: true);
            var db = manager.DatabaseService!;
            var seed = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Song", Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await db.AddSongAsync(seed, chart);
            var songId = (await db.GetSongsAsync()).Single(s => s.Title == "Song").Id;
            await db.PurgeDatabaseAsync(); // make the bookmark write fault

            try
            {
                var stage = CreateStage();
                var nodeEntity = new DTXMania.Game.Lib.Song.Entities.Song { Id = songId, Title = "Song", IsBookmarked = false };
                var node = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = nodeEntity };
                var display = new SongListDisplay { CurrentList = new List<SongListNode> { node } };
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
                // Mirror real All-Songs behavior: the selected node's entity lives in RootSongs,
                // so the revert (which applies by song Id across RootSongs) reaches it.
                var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
                roots.Clear();
                roots.Add(node);

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // optimistic flip to true
                Assert.True(nodeEntity.IsBookmarked);

                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
                try { await pending[songId]; } catch { /* expected persist fault */ }

                // The fault continuation enqueues a revert on the thread pool; poll for it.
                var queue = GetPrivateField<System.Collections.Concurrent.ConcurrentQueue<(int, bool, int)>>(stage, "_pendingBookmarkReverts");
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (queue.IsEmpty && DateTime.UtcNow < deadline)
                    await Task.Delay(20);
                Assert.False(queue.IsEmpty); // the fault enqueued a rollback

                InvokePrivateMethod(stage, "ProcessPendingBookmarkReverts");

                Assert.False(nodeEntity.IsBookmarked); // reverted to pre-toggle state
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        // Stale-revert guard: a revert from an older, superseded toggle must be skipped so it
        // cannot override a newer toggle's in-memory flag.
        [Fact]
        public void ProcessPendingBookmarkReverts_WhenSupersededByNewerToggle_SkipsStaleRevert()
        {
            var stage = CreateStage();
            var recentSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 7, IsBookmarked = true };
            SetPrivateField(stage, "_recentPlayNodes",
                new List<SongListNode> { new() { Type = NodeType.Score, DatabaseSong = recentSong } });

            var queue = GetPrivateField<System.Collections.Concurrent.ConcurrentQueue<(int, bool, int)>>(stage, "_pendingBookmarkReverts");
            queue.Enqueue((7, false, toggleVersion: 1)); // stale: a newer toggle bumped to 2
            var versions = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, int>>(stage, "_bookmarkToggleVersion");
            versions[7] = 2;

            InvokePrivateMethod(stage, "ProcessPendingBookmarkReverts");

            Assert.True(recentSong.IsBookmarked); // newer toggle's flag preserved
        }

        // Reports the B key as pressed exactly once.
        private sealed class BookmarkKeyInputManager : DTXMania.Game.Lib.Input.InputManager
        {
            private bool _consumed;
            public override bool IsKeyPressed(int keyCode)
            {
                if (keyCode == (int)Keys.B && !_consumed)
                {
                    _consumed = true;
                    return true;
                }
                return false;
            }
        }
    }
}
