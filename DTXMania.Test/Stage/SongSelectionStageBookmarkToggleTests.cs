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
using DTXMania.Game.Lib.Stage.SongSelection;
using Microsoft.Xna.Framework.Input;
using Xunit;
using DTXMania.Test.TestData;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    // HPA-802 task 3: the bookmark write path (pending writes, toggle generations,
    // revert gate, disposal drain) moved into SongSelectionTabs — its durability,
    // retirement, supersession and zero-id contracts are covered by SongSelectionTabsTests
    // over real SQLite with constructor fixtures (including the migrated
    // navigation-crosses-committed-write persistence/retirement half). These stage tests
    // keep the stage-owned input routing (B key, non-song nodes) and the navigation-crosses-
    // committed-write integration: the non-default difficulty and chart identity must
    // survive the update-driven Bookmarks reload, including the reloaded row. The tabs
    // owner is captured through the constructor factory seam (no owner-field reflection);
    // writes are gated through the constructor writer seam (real SetBookmarkAsync runs).
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
            var (stage, tabs) = CreateStageWithTabs();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            tabs.SetActiveTabQuiet(SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            Assert.True(node.DatabaseSong!.IsBookmarked);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");
            Assert.False(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_OnFolderNode_DoesNothing()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var folder = new SongListNode { Type = NodeType.Box, Title = "Folder" };
            var display = DisplayWithSelection(folder);
            AttachCoreUi(stage, display);

            var ex = Record.Exception(() =>
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"));

            Assert.Null(ex);
            Assert.Equal(0, tabs.BookmarkNodes?.Count ?? 0);
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

        // Successful serialized bookmark persistence must survive navigation — that
        // contract (gated write pending across scope retirement, commit lands in the
        // database, reloaded row with its difficulty labels) is asserted on the TYPED
        // TABS FIXTURE (SongSelectionTabsTests.ToggleBookmark_WhenCommitCrossesScopeRetirement_
        // ShouldPersistAndReloadRowWithDifficulty). This stage test keeps the stage-owned
        // integration: the explicitly selected non-default difficulty (ADVANCED) must
        // survive the update-driven Bookmarks reload and still resolve to the advanced
        // chart on the reloaded row. Real DB end to end — no stubbed success, no
        // test-only hooks.
        [Fact]
        public async Task ToggleBookmarkForSelectedSong_WhenNavigationCrossesACommittedWrite_ShouldPersistAndPreserveNondefaultDifficulty()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"bm_persist_nav_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);
            var db = manager.Database;
            var seed = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Song", Artist = "A" };
            var basic = new SongChart { Song = seed, FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            var advanced = new SongChart { Song = seed, FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 55 };
            using (var seedContext = db.CreateContext())
            {
                seedContext.Songs.Add(seed);
                seedContext.SongCharts.AddRange(basic, advanced);
                await seedContext.SaveChangesAsync();
            }
            var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;

            try
            {
                // Gate the REAL write BEFORE toggling: the toggle's write cannot even start
                // until the gate releases, so it is demonstrably still pending when
                // navigation retires the activation scope below. The writer seam wraps the
                // real SetBookmarkAsync against the library argument (the toggle-time
                // capture) — no mock-only substitution.
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task? gatedWrite = null;
                var (stage, tabs) = CreateStageWithTabs(
                    manager,
                    setBookmarkAsync: (lib, id, state) =>
                    {
                        var write = gate.Task.ContinueWith(
                            _ => lib.Queries.SetBookmarkAsync(id, state),
                            TaskScheduler.Default).Unwrap();
                        gatedWrite = write;
                        return write;
                    });

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
                tabs.SetActiveTabQuiet(SongSelectionTab.AllSongs);
                stage.InstallTestBackgroundScope();

                // Wire the stage's REAL selection handler exactly as production OnLoad does
                // (SongSelectionStage wires _songListDisplay.SelectionChanged +=
                // OnSongSelectionChanged): the update-driven Bookmarks reload below raises
                // the display's SelectionChanged event, so the reloaded row must actually
                // reach OnSongSelectionChanged — the stage's selection state is then the
                // callback's product, not stale pre-navigation state.
                display.SelectionChanged += (sender, e) =>
                    InvokePrivateMethod(stage, "OnSongSelectionChanged", sender, e);

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

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"); // optimistic flip to true
                Assert.True(nodeEntity.IsBookmarked);
                // The writer seam runs inside a pool continuation chained behind the prior
                // write; wait for its registration before asserting the gated state.
                Assert.True(
                    SpinWait.SpinUntil(() => gatedWrite != null, TimeSpan.FromSeconds(5)),
                    "the writer seam was never invoked");
                Assert.False(gatedWrite!.IsCompleted, "the write must still be pending before navigation");

                // Retire/rearm while the write is provably pending: the committed write
                // itself is never canceled across the deactivation.
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                stage.Deactivate();
                stage.InstallTestBackgroundScope();
                Assert.False(gatedWrite.IsCompleted, "the write must still be pending across navigation");

                // Release; the real SetBookmarkAsync runs only now — after navigation.
                gate.SetResult(true);
                await gatedWrite.WaitAsync(TimeSpan.FromSeconds(5)); // serialized chain settles across navigation
                Assert.True(gatedWrite.IsCompletedSuccessfully);

                // No rollback ever flipped the node back; the DB persistence itself is
                // asserted on the typed tabs fixture (no duplicate durability test here).
                Assert.True(nodeEntity.IsBookmarked);

                // Reload the Bookmarks tab (real GetBookmarkedNodesAsync; the owner defers
                // it behind settled writes) and drive stage.Update so the update thread
                // applies the load and repopulates the display — the same reconciliation
                // the game loop runs. The explicitly selected non-default difficulty must
                // survive it.
                InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.Bookmarks);
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                Assert.True(
                    SpinWait.SpinUntil(
                        () =>
                        {
                            stage.Update(0.016);
                            return tabs.BookmarkNodes is { Count: 1 }
                                && display.CurrentList.Count == 1;
                        },
                        TimeSpan.FromSeconds(5)),
                    "the post-navigation Bookmarks reload was not applied on the update thread");

                var reloaded = tabs.BookmarkNodes!.Single();
                Assert.Equal(songId, reloaded.DatabaseSongId);
                Assert.False(string.IsNullOrEmpty(reloaded.DifficultyLabels[0]));
                Assert.False(string.IsNullOrEmpty(reloaded.DifficultyLabels[1]));

                // Selection preserved through the REAL callback: the stage itself selects
                // the reloaded row (its _selectedSong is the callback's product, not the
                // pre-navigation node), still at the non-default difficulty, and that
                // difficulty still resolves to the ADVANCED chart (identity by FilePath).
                // SelectedDifficulty is read from the stage's public telemetry surface
                // instead of a private field the reload path never has to touch.
                Assert.Same(reloaded, display.CurrentList[0]);
                Assert.Same(reloaded, GetPrivateField<SongListNode>(stage, "_selectedSong"));
                Assert.Equal(1, display.CurrentDifficulty);
                var telemetry = new DTXMania.Game.Lib.GameTelemetrySnapshot();
                stage.PopulateTelemetry(telemetry);
                Assert.Equal(1, telemetry.SelectedDifficulty);
                Assert.Equal(advanced.FilePath, display.SelectedSong!.GetCurrentDifficultyChart(1)?.FilePath);
            }
            finally
            {
                manager.Dispose();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
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
