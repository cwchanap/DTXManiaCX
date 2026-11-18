using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
using Microsoft.Extensions.Logging;
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

        // Drives the stage's background-work scope until the condition holds: workers only
        // queue their results; a drain on the test thread applies them (as stage.Update does).
        private static async Task DrainUntilAsync(
            SongSelectionStage stage, Func<bool> condition, int timeoutMs = 10000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                stage.BackgroundWork?.Drain();
                if (condition())
                    return;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        "The expected background-work result was not applied within the timeout.");
                await Task.Delay(10);
            }
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

        [Fact]
        public void ToggleBookmark_WhenAppliedSnapshotExists_ShouldReconcileAppliedSongAndLeaveManagerViewBystanderUnchanged()
        {
            var stage = CreateStage();
            var selectedSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 42, Title = "Selected", IsBookmarked = false };
            var appliedSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 42, Title = "Applied", IsBookmarked = false };
            // A manager-view bystander sharing the same song id must NOT be reconciled
            // when an applied snapshot owns the canonical view for this toggle.
            var bystanderSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 42, Title = "Bystander", IsBookmarked = false };
            var selectedNode = new SongListNode { Type = NodeType.Score, Title = "Selected", DatabaseSongId = 42, DatabaseSong = selectedSong };
            var appliedNode = new SongListNode { Type = NodeType.Score, Title = "Applied", DatabaseSongId = 42, DatabaseSong = appliedSong };
            var bystanderNode = new SongListNode { Type = NodeType.Score, Title = "Bystander", DatabaseSongId = 42, DatabaseSong = bystanderSong };
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { selectedNode } };
            var snapshot = new SongLibrarySnapshot(
                version: 101,
                rootSongs: new[] { appliedNode },
                activeRoots: new[] { "/library/active" },
                enumeratedFileCount: 1,
                discoveredScoreCount: 1);

            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_appliedLibrarySnapshot", snapshot);

            var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
            var savedRoots = roots.ToList();
            roots.Clear();
            roots.Add(bystanderNode);

            try
            {
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

                Assert.True(appliedSong.IsBookmarked);     // reconciled via the applied snapshot
                Assert.False(bystanderSong.IsBookmarked);  // manager-view bystander untouched
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
                stage.InstallTestBackgroundScope();
                // Mirror real All-Songs behavior: the selected node's entity lives in RootSongs,
                // so the revert (which applies by song Id across RootSongs) reaches it.
                var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
                roots.Clear();
                roots.Add(node);

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // optimistic flip to true
                Assert.True(nodeEntity.IsBookmarked);

                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
                try { await pending[songId]; } catch { /* expected persist fault */ }

                // The fault continuation posts the rollback into the stage scope; a drain
                // applies it on the update thread.
                await DrainUntilAsync(stage, () => !nodeEntity.IsBookmarked);

                Assert.False(nodeEntity.IsBookmarked); // reverted to pre-toggle state
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        // A committed bookmark persistence task may outlive navigation; only the stale UI
        // rollback is dropped (the stage's scope retired on Deactivate), never the write itself.
        // On a FAILED write there is no committed intent to preserve: the shared SongManager
        // entity must revert to the pre-toggle state it matches in the database, while the
        // per-activation UI lists stay untouched (their rollback was dropped with the scope).
        [Fact]
        public async Task ToggleBookmarkForSelectedSong_WhenStageDeactivatesBeforeFault_RevertsSharedEntityButDropsUiRollback()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"bm_revert_drop_{Guid.NewGuid():N}.db");
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
                stage.InstallTestBackgroundScope();
                var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
                roots.Clear();
                roots.Add(node);
                // A distinct entity in a per-activation UI list: the dropped rollback must
                // never reach it (only the shared entity reverts on the worker).
                var recentEntity = new DTXMania.Game.Lib.Song.Entities.Song { Id = songId, Title = "Recent", IsBookmarked = false };
                SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode>
                {
                    new() { Type = NodeType.Score, Title = "Recent", DatabaseSong = recentEntity }
                });

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // optimistic flip to true
                Assert.True(nodeEntity.IsBookmarked);
                Assert.True(recentEntity.IsBookmarked);

                // Navigate away before the persist fault lands: Deactivate retires the
                // scope before tearing down, so the stale UI rollback cannot be applied.
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                stage.Deactivate();

                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
                try { await pending[songId]; } catch { /* expected persist fault */ }

                // The shared entity reverts on the worker (version-guarded), so reentry
                // starts DB-consistent instead of keeping the state the DB rejected.
                Assert.True(
                    SpinWait.SpinUntil(() => !nodeEntity.IsBookmarked, TimeSpan.FromSeconds(5)),
                    "the shared SongManager entity was not reverted after the failed persist");

                // The per-activation UI list keeps its optimistic value: its rollback was
                // dropped with the retired scope, exactly as the design allows.
                Assert.True(recentEntity.IsBookmarked);
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        // Critical regression: a persist fault from activation A must post its rollback
        // into A's (retired) scope — never into a later activation B. Reading
        // BackgroundWork at fault time instead of capturing the originating scope at
        // toggle time lets the rollback land in B, where the per-song generation check
        // still matches (no intervening toggle), mutating B's lists and starting another
        // bookmark load.
        [Fact]
        public async Task ToggleBookmarkForSelectedSong_WhenFaultLandsAfterReactivation_ShouldNotMutateTheNewActivation()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"bm_revert_react_{Guid.NewGuid():N}.db");
            SongManager.ResetInstanceForTesting();
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(dbPath, purgeDatabaseFirst: true);
            var db = manager.DatabaseService!;
            var seed = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Song", Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await db.AddSongAsync(seed, chart);
            var songId = (await db.GetSongsAsync()).Single(s => s.Title == "Song").Id;
            await db.PurgeDatabaseAsync(); // make the bookmark write fault once it runs

            try
            {
                var stage = CreateStage();
                var nodeEntity = new DTXMania.Game.Lib.Song.Entities.Song { Id = songId, Title = "Song", IsBookmarked = false };
                var node = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = nodeEntity };
                var display = new SongListDisplay { CurrentList = new List<SongListNode> { node } };
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
                var bookmarkLoads = 0;
                SetPrivateField(stage, "_loadBookmarkedNodesAsync",
                    new Func<Task<List<SongListNode>>>(() =>
                    {
                        Interlocked.Increment(ref bookmarkLoads);
                        return Task.FromResult(new List<SongListNode>());
                    }));
                stage.InstallTestBackgroundScope();
                var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
                roots.Clear();
                roots.Add(node);
                // A distinct entity in B's per-activation UI list: the stale rollback must
                // never reach it, while the shared entity itself is reverted on the worker.
                var recentEntity = new DTXMania.Game.Lib.Song.Entities.Song { Id = songId, Title = "Recent", IsBookmarked = false };
                SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode>
                {
                    new() { Type = NodeType.Score, Title = "Recent", DatabaseSong = recentEntity }
                });

                // The fault continuation posts the rollback into the origin scope FIRST and
                // logs the "Bookmark save failed" warning immediately after, on the same
                // thread. Observing the warning therefore proves the posting attempt already
                // completed — so if a broken implementation posted into the NEW activation
                // instead, the rollback is queued before the drains below and cannot slip
                // past them. A retired scope drops the post silently, so the log spy is the
                // only implementation-independent observable of the hand-off.
                var faultLog = new FaultContinuationLogSpy();
                SetPrivateField(stage, "_logger", faultLog);

                // Gate the toggle's write behind a pre-seeded in-flight write for the same
                // song (the rapid-toggle serialization path) so the fault lands
                // deterministically AFTER the re-activation below.
                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending[songId] = gate.Task;

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // optimistic flip to true
                Assert.True(nodeEntity.IsBookmarked);

                // Navigate away and come back: the new activation installs a fresh scope
                // while A's write is still queued.
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                stage.Deactivate();
                stage.InstallTestBackgroundScope();

                // Open the gate; the write runs against the purged DB and faults.
                gate.SetException(new InvalidOperationException("forced fault for the reactivation test"));
                var write = pending[songId];
                try { await write; } catch { /* expected persist fault */ }

                // Barrier: prove the fault continuation reached its rollback posting point
                // (faultLog above) before the final drain/assertions. Awaiting the write
                // alone does not order the separately scheduled continuation, so without
                // this a broken implementation could pass by simply not having attempted
                // the stale rollback yet.
                Assert.True(
                    SpinWait.SpinUntil(
                        () => Volatile.Read(ref faultLog.Warnings) >= 1,
                        TimeSpan.FromSeconds(5)),
                    "the fault continuation never reached its rollback posting point");

                // Drain B exactly as the game loop would. The stale UI rollback must never
                // apply here: it was posted into A's retired scope and dropped there, so B's
                // per-activation lists are untouched. The shared entity, however, is reverted
                // on the worker (version-guarded): a failed write leaves no committed intent,
                // so reentry starts DB-consistent instead of keeping the rejected state.
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    stage.BackgroundWork?.Drain();
                    Assert.True(
                        recentEntity.IsBookmarked,
                        "the stale UI rollback leaked into the new activation's lists");
                    Assert.Equal(0, Interlocked.CompareExchange(ref bookmarkLoads, 0, 0));
                    await Task.Delay(10);
                }
                Assert.True(
                    SpinWait.SpinUntil(() => !nodeEntity.IsBookmarked, TimeSpan.FromSeconds(5)),
                    "the shared entity was not reverted after the failed persist");
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        // Successful serialized bookmark persistence must survive navigation: the write is
        // gated before the toggle, so it demonstrably has not started when the activation
        // that toggled it retires; releasing the gate afterwards proves the real commit
        // lands in the database across navigation. The explicitly selected non-default
        // difficulty (ADVANCED) must survive the update-driven Bookmarks reload and still
        // resolve to the advanced chart. Real DB end to end — no stubbed success, no
        // test-only hooks.
        [Fact]
        public async Task ToggleBookmarkForSelectedSong_WhenNavigationCrossesACommittedWrite_ShouldPersistAndPreserveNondefaultDifficulty()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"bm_persist_nav_{Guid.NewGuid():N}.db");
            SongManager.ResetInstanceForTesting();
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(dbPath, purgeDatabaseFirst: true);
            var db = manager.DatabaseService!;
            var seed = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Song", Artist = "A" };
            var basic = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            var advanced = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 55 };
            await db.AddSongAsync(seed, basic);
            await db.AddSongAsync(seed, advanced); // second chart joins the same song
            var songId = (await db.GetSongsAsync()).Single(s => s.Title == "Song").Id;

            try
            {
                var stage = CreateStage();
                var nodeEntity = new DTXMania.Game.Lib.Song.Entities.Song { Id = songId, Title = "Song", IsBookmarked = false };
                var node = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = nodeEntity };
                // Mirror the published difficulty-slot layout (slot 0 = BASIC, slot 1 =
                // ADVANCED) so the non-default difficulty below is genuinely selectable.
                node.Scores[0] = new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 30 };
                node.Scores[1] = new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 55 };
                node.DifficultyLabels[0] = "BASIC";
                node.DifficultyLabels[1] = "ADVANCED";
                var display = new SongListDisplay { CurrentList = new List<SongListNode> { node } };
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
                stage.InstallTestBackgroundScope();

                // Explicitly select the NON-DEFAULT difficulty (ADVANCED) through the
                // status-panel difficulty path (the real input route): slots [0,1] wrap
                // 0 -> 1. The write below must not disturb this selection and the
                // post-navigation reconciliation must preserve it.
                SetPrivateField(stage, "_selectedSong", node);
                SetPrivateField(stage, "_isInStatusPanel", true);
                InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveLeft, 0.0));
                SetPrivateField(stage, "_isInStatusPanel", false);
                Assert.Equal(1, display.CurrentDifficulty);
                Assert.Equal(1, GetPrivateField<int>(stage, "_currentDifficulty"));

                // Gate the serialized write BEFORE toggling: the toggle's write chains
                // behind the pre-seeded gate (the per-song serialization path), so it is
                // demonstrably still pending — it cannot even start — when navigation
                // retires the activation scope below.
                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending[songId] = gate.Task;

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // optimistic flip to true
                Assert.True(nodeEntity.IsBookmarked);
                Assert.False(pending[songId].IsCompleted, "the write must still be pending before navigation");

                // Retire/rearm while the write is provably pending: the committed write
                // itself is never canceled across the deactivation.
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                stage.Deactivate();
                stage.InstallTestBackgroundScope();
                Assert.False(pending[songId].IsCompleted, "the write must still be pending across navigation");

                // Release; the real SetBookmarkAsync runs only now — after navigation.
                gate.SetResult(true);
                await pending[songId]; // serialized chain settles across navigation
                Assert.True(pending[songId].IsCompletedSuccessfully);

                // The committed write landed in the database.
                var persisted = (await db.GetSongsAsync()).Single(s => s.Id == songId);
                Assert.True(persisted.IsBookmarked);
                Assert.True(nodeEntity.IsBookmarked); // and no rollback ever flipped the node back

                // Reload the Bookmarks tab (real GetBookmarkedNodesAsync; chained after the
                // settled write) and drive stage.Update so the update thread applies the
                // load and repopulates the display — the same reconciliation the game loop
                // runs. The explicitly selected non-default difficulty must survive it.
                InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.Bookmarks);
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                Assert.True(
                    SpinWait.SpinUntil(
                        () =>
                        {
                            stage.Update(0.016);
                            return GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes") is { Count: 1 }
                                && display.CurrentList.Count == 1;
                        },
                        TimeSpan.FromSeconds(5)),
                    "the post-navigation Bookmarks reload was not applied on the update thread");

                var reloaded = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes")!.Single();
                Assert.Equal(songId, reloaded.DatabaseSongId);
                Assert.False(string.IsNullOrEmpty(reloaded.DifficultyLabels[0]));
                Assert.False(string.IsNullOrEmpty(reloaded.DifficultyLabels[1]));

                // Selection preserved: still the reloaded row, still the non-default
                // difficulty, and that difficulty still resolves to the ADVANCED chart
                // (identity by FilePath).
                Assert.Same(reloaded, display.CurrentList[0]);
                Assert.Equal(1, display.CurrentDifficulty);
                Assert.Equal(1, GetPrivateField<int>(stage, "_currentDifficulty"));
                Assert.Equal(advanced.FilePath, display.SelectedSong!.GetCurrentDifficultyChart(1)?.FilePath);
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        // SET.def duplicate regression: when the persisted id (DatabaseSongId) is set but the
        // parsed SongEntity.Id was never stamped (AddSongAsync returned the existing id), the
        // toggle must key persistence/reconciliation on the persisted id — not 0. Otherwise
        // SetBookmarkAsync(0,...) is a no-op and the per-song write chain collides on key 0.
        [Fact]
        public void ToggleBookmarkForSelectedSong_WhenDatabaseSongIdSetButEntityIdZero_UsesPersistedId()
        {
            var stage = CreateStage();
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Id = 0, Title = "S", IsBookmarked = false };
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = "S",
                DatabaseSong = song,
                DatabaseSongId = 77
            };
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
            // Must key on the persisted id (77), not the zero entity id.
            Assert.True(pending.ContainsKey(77));
            Assert.False(pending.ContainsKey(0));
        }

        // SET.def duplicate reconciliation: a browse-tree node whose DatabaseSong.Id is 0 but
        // whose DatabaseSongId matches the toggled song must receive the new flag via the
        // reconciler (otherwise the star marker never refreshes in the All Songs list).
        [Fact]
        public void ToggleBookmarkForSelectedSong_WhenZeroEntityIdNodeInRoots_ReconcilesByPersistedId()
        {
            var stage = CreateStage();
            var selectedSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 0, Title = "S", IsBookmarked = false };
            var rootNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "S",
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 0, Title = "S", IsBookmarked = false },
                DatabaseSongId = 77
            };
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode>
                {
                    new() { Type = NodeType.Score, Title = "S", DatabaseSong = selectedSong, DatabaseSongId = 77 }
                }
            };
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
            var savedRoots = roots.ToList();
            roots.Clear();
            roots.Add(rootNode);

            try
            {
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

                Assert.True(selectedSong.IsBookmarked);   // direct flip on the selected node
                Assert.True(rootNode.DatabaseSong!.IsBookmarked); // reconciled via persisted id
            }
            finally
            {
                roots.Clear();
                roots.AddRange(savedRoots);
            }
        }

        // Zero-id guard regression: when the selected node has neither a persisted id
        // (DatabaseSongId is null) nor a stamped entity id (DatabaseSong.Id == 0) — e.g. an
        // unpersisted fallback node — the toggle must NOT key persistence/reconciliation on
        // the sentinel 0. Doing so would (a) queue a silent no-op DB write and (b) flip
        // IsBookmarked on every OTHER unrelated zero-id node via BookmarkStateReconciler.
        // The selected node's own optimistic flip (done before the id resolution) is allowed
        // to stand so its star marker refreshes, but the shared path must be skipped.
        [Fact]
        public void ToggleBookmarkForSelectedSong_WhenNoPersistedIdAndZeroEntityId_SkipsSharedPath()
        {
            var stage = CreateStage();
            // Selected node: no persisted id, entity id 0.
            var selectedSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 0, Title = "S", IsBookmarked = false };
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = "S",
                DatabaseSong = selectedSong,
                DatabaseSongId = null
            };
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            // An unrelated zero-id node in RootSongs that must NOT be touched.
            var bystanderSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 0, Title = "Other", IsBookmarked = false };
            var bystander = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Other",
                DatabaseSong = bystanderSong,
                DatabaseSongId = null
            };
            var roots = GetPrivateField<List<SongListNode>>(SongManager.Instance, "_rootSongs");
            var savedRoots = roots.ToList();
            roots.Clear();
            roots.Add(bystander);

            try
            {
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

                // The selected node's optimistic flip is allowed to stand (visual feedback).
                Assert.True(selectedSong.IsBookmarked);
                // The bystander zero-id node must be untouched.
                Assert.False(bystanderSong.IsBookmarked);
                // No persistence queued under the sentinel key 0.
                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
                Assert.False(pending.ContainsKey(0));
            }
            finally
            {
                roots.Clear();
                roots.AddRange(savedRoots);
            }
        }

        // Comment 2 regression: switching to the Bookmarks tab must wait for any in-flight
        // bookmark writes before querying the DB, otherwise a just-bookmarked song won't
        // appear (the optimistic reconciler only updates nodes already in _bookmarkNodes).
        // Here an incomplete pending write defers the load; settling it triggers the reload.
        [Fact]
        public async Task SwitchToNextTab_ToBookmarks_ChainsLoadAfterPendingWrites()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays); // -> Bookmarks
            stage.InstallTestBackgroundScope();

            // Gate the bookmark loader so the test does not depend on the SongManager
            // singleton's database state or on real DB I/O latency.
            var loadStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseLoad = new TaskCompletionSource<List<SongListNode>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetPrivateField(stage, "_loadBookmarkedNodesAsync",
                new Func<Task<List<SongListNode>>>(() =>
                {
                    loadStarted.TrySetResult(true);
                    return releaseLoad.Task;
                }));

            // Stale bookmark list from before the toggle.
            SetPrivateField(stage, "_bookmarkNodes",
                new List<SongListNode> { new() { Type = NodeType.Score, Title = "Stale" } });

            // In-flight bookmark write that has not yet committed.
            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(stage, "_pendingBookmarkWrites");
            pending[42] = tcs.Task;

            InvokePrivateMethod(stage, "SwitchToNextTab");

            // Load is deferred until the pending write settles; stale list remains and the
            // loader has not been invoked.
            var nodes = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.Single(nodes);
            Assert.False(loadStarted.Task.IsCompleted);

            // Settle the write. Its worker continuation only posts the load request into the
            // stage scope, so the stage-owned cache stays unchanged until a drain consumes it.
            tcs.SetResult(true);
            await DrainUntilAsync(stage, () => loadStarted.Task.IsCompleted);
            Assert.True(loadStarted.Task.IsCompleted);
            nodes = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.Single(nodes);

            // The deferred load runs on the worker; releasing it queues the completion which
            // the next drain applies.
            releaseLoad.SetResult(new List<SongListNode>());
            await DrainUntilAsync(stage, () => GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes")!.Count == 0);

            nodes = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.Empty(nodes);
        }

        // Stale-revert guard: a revert from an older, superseded toggle must be skipped so it
        // cannot override a newer toggle's in-memory flag.
        [Fact]
        public void ApplyBookmarkRevert_WhenSupersededByNewerToggle_SkipsStaleRevert()
        {
            var stage = CreateStage();
            var recentSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = 7, IsBookmarked = true };
            SetPrivateField(stage, "_recentPlayNodes",
                new List<SongListNode> { new() { Type = NodeType.Score, DatabaseSong = recentSong } });

            var versions = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, int>>(stage, "_bookmarkToggleVersion");
            versions[7] = 2;

            // The posted rollback carries the toggle generation that triggered it (1);
            // a newer toggle bumped the generation to 2, so this revert is stale.
            InvokePrivateMethod(stage, "ApplyBookmarkRevert", 7, false, 1);

            Assert.True(recentSong.IsBookmarked); // newer toggle's flag preserved
        }

        // Counts warning-level log calls on the stage logger. The bookmark fault
        // continuation posts the rollback first and logs the "Bookmark save failed"
        // warning immediately after on the same thread, so observing the warning proves
        // the rollback posting attempt already happened.
        private sealed class FaultContinuationLogSpy : ILogger
        {
            public int Warnings;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    Interlocked.Increment(ref Warnings);
            }
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
