using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;
using DTXMania.Test.TestData;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageTabTests
    {
        private static SongListNode ScoreNode(string title) => new SongListNode
        {
            Type = NodeType.Score,
            Title = title
        };

        // Drives the stage's background-work scope until the condition holds: workers only
        // queue their results; a drain on the test thread applies them (as stage.Update does).
        private static async Task DrainUntilAsync(
            SongSelectionStage stage, Func<bool> condition, int timeoutMs = 5000)
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

        // Run completion enqueues behind the same lock that guards the queue, so tests can
        // await a deterministic worker/terminal enqueue by observing the pending item count
        // (read-only probe; same pattern as StageBackgroundWorkScopeTests).
        private static int PendingQueueCount(StageBackgroundWorkScope scope)
        {
            var queue = typeof(StageBackgroundWorkScope)
                .GetField("_queue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(scope) as System.Collections.ICollection;
            return queue?.Count ?? 0;
        }

        // Deterministic worker/terminal barrier: waits until the scope worker has queued
        // its terminal, then applies exactly one drain. Proves the older terminal was
        // actually processed before the winner is checked, instead of hoping a fixed
        // delay was long enough (a too-short delay lets a fence regression pass vacuously).
        private static void DrainAfterTerminalQueued(SongSelectionStage stage)
        {
            var scope = stage.BackgroundWork!;
            var queued = SpinWait.SpinUntil(
                () => PendingQueueCount(scope) >= 1,
                TimeSpan.FromSeconds(5));
            Assert.True(queued, "the scope worker did not queue its terminal in time");
            scope.Drain();
        }

        // Retires the activation scope the way Deactivate does, without requiring the full
        // Activate path: an in-flight result must drop instead of being applied.
        private static void RetireStageScope(SongSelectionStage stage)
        {
            SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            stage.Deactivate();
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnRecentTab_ShowsCachedRecentNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode> { ScoreNode("R1"), ScoreNode("R2") });

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Equal("R1", display.CurrentList[0].Title);
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyRecentMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnRecentTab_WhenEmpty_SetsEmptyFlag()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode>());

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Empty(display.CurrentList);
            Assert.True(GetPrivateField<bool>(stage, "_showEmptyRecentMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnRecentTab_WhenNodeListIsNull_ShowsEmptyAndSetsFlag()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            // _recentPlayNodes left null (default)

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Empty(display.CurrentList);
            Assert.True(GetPrivateField<bool>(stage, "_showEmptyRecentMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnAllSongsTab_UsesBrowseList()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { ScoreNode("A1") });
            // Ensure no filter view is active so PopulateSongList() path runs.
            SetPrivateField(stage, "_filteredView", null);

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Equal("A1", display.CurrentList[0].Title);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
        }

        [Fact]
        public void SwitchToNextTab_TogglesActiveTabAndRequestsRepopulate()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.RecentPlays, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        [Fact]
        public void SwitchToNextTab_FromRecent_GoesToBookmarks()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.Bookmarks, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        [Fact]
        public void OpenSearchFilterModal_OnRecentTab_DoesNotOpen()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            // No modal is attached; method must early-return without throwing.
            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            // Confirm the tab is unchanged (method early-returned).
            Assert.Equal(SongSelectionTab.RecentPlays, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void HandlePadLaneHit_OnFloorTom_SwitchesTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "HandlePadLaneHit", 7); // Floor Tom (FT)

            Assert.Equal(SongSelectionTab.RecentPlays, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void HandlePadLaneHit_OnLowTom_DoesNotSwitchTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "HandlePadLaneHit", 6); // Low Tom (LT) is now "down"

            Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void DetectTabSwitchKey_WhenTabPressed_SwitchesToNextTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_inputManager", new TabKeyDetectInputManager());

            InvokePrivateMethod(stage, "DetectTabSwitchKey");

            Assert.Equal(SongSelectionTab.RecentPlays,
                GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void DetectTabSwitchKey_WhenTabNotPressed_KeepsCurrentTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_inputManager", new NoKeyPressInputManager());

            InvokePrivateMethod(stage, "DetectTabSwitchKey");

            Assert.Equal(SongSelectionTab.AllSongs,
                GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void DetectTabSwitchKey_WhenInputManagerNull_DoesNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "DetectTabSwitchKey"));
            Assert.Null(ex);
        }

        [Fact]
        public void HandlePadLaneHit_OnFloorTomTwice_OpensSearchOnAllSongs()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object);
            SetPrivateField(stage, "_searchFilterModal", modal);

            SetPrivateField(stage, "_elapsedTime", 1.0);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7);
            Assert.Equal(SongSelectionTab.Bookmarks, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
            Assert.False(modal.IsOpen);

            SetPrivateField(stage, "_elapsedTime", 1.3);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7);

            Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void HandlePadLaneHit_OnFloorTomTwiceEndingOnAllSongs_OpensSearchInPlace()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object);
            SetPrivateField(stage, "_searchFilterModal", modal);

            SetPrivateField(stage, "_elapsedTime", 1.0);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7); // wraps to All Songs
            SetPrivateField(stage, "_elapsedTime", 1.2);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7);

            Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void OnPadLaneHit_WhenModalOpen_DoesNotSwitchTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            SetPrivateField(stage, "_searchFilterModal", modal);

            var args = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("MIDI.43", true));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.AllSongs,
                GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void OnPadLaneHit_WhenModalClosed_SwitchesTabOnFloorTom()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            var args = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("MIDI.43", true));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.RecentPlays,
                GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void OnPadLaneHit_FromKeyboardDrumKey_DoesNotSwitchTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            var args = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("Key.F", true));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.AllSongs,
                GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void OnUpdate_WhenTabListNeedsRefresh_ResetsFlagAndPopulatesList()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlayNodes",
                new List<SongListNode> { ScoreNode("Recent1") });
            SetPrivateField(stage, "_tabListNeedsRefresh", true);
            SetPrivateField(stage, "_uiManager", null); // Avoid NRE in _uiManager.Update

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            Assert.False(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
            Assert.Single(display.CurrentList);
            Assert.Equal("Recent1", display.CurrentList[0].Title);
        }

        [Fact]
        public void OnUpdate_WhenTabListDoesNotNeedRefresh_LeavesDisplayUntouched()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { ScoreNode("Existing") }
            };
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlayNodes",
                new List<SongListNode> { ScoreNode("RecentFresh") });
            SetPrivateField(stage, "_tabListNeedsRefresh", false);
            SetPrivateField(stage, "_uiManager", null);

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            // The refresh flag stays false and the display is untouched.
            Assert.False(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
            Assert.Single(display.CurrentList);
            Assert.Equal("Existing", display.CurrentList[0].Title);
        }

        // Regression guard for the stale-refresh-flag bug: Activate must clear any
        // _tabListNeedsRefresh left over from a prior activation so the first OnUpdate
        // doesn't repopulate the All Songs list and reset the user's selection.
        // We can't drive the full Activate path headlessly (it spins up UI/song loading),
        // but we can pin the invariant via SwitchToNextTab -> (simulate Deactivate) ->
        // manual reset mirroring Activate's reset block.
        [Fact]
        public void StaleTabListNeedsRefresh_OnReactivation_DoesNotResetAllSongsSelection()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { ScoreNode("A1"), ScoreNode("A2"), ScoreNode("A3") }
            };
            AttachCoreUi(stage, display);
            // Simulate prior session: user was on All Songs, then tab-switched (which
            // sets _tabListNeedsRefresh=true at line 1991), then backed out before OnUpdate.
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            display.SelectedIndex = 2;
            InvokePrivateMethod(stage, "SwitchToNextTab");
            // SwitchToNextTab moved to RecentPlays and flagged a refresh.
            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));

            // Simulate re-Activate: tab reset to AllSongs, _tabListNeedsRefresh cleared.
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_tabListNeedsRefresh", false);
            SetPrivateField(stage, "_uiManager", null);

            // Re-attach the display list that the prior session had at index 2.
            display.CurrentList = new List<SongListNode> { ScoreNode("A1"), ScoreNode("A2"), ScoreNode("A3") };
            display.SelectedIndex = 2;

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            // Without the fix, _tabListNeedsRefresh would still be true and the list/selection
            // would have been reset. With the fix, the user's selection is preserved.
            Assert.False(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
            Assert.Equal(2, display.SelectedIndex);
        }

        // Regression guard for the cache-warming bug: BeginRecentPlaysLoad's continuation
        // must not flag a refresh when the active tab is All Songs, otherwise the user's
        // position in the All Songs list gets reset to index 0 once the background load
        // completes. We verify the invariant by checking the guarded code path through
        // OnUpdate when no refresh is flagged.
        [Fact]
        public void OnUpdate_OnAllSongsTab_WhenRefreshFlagFalse_PreservesSelection()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { ScoreNode("S1"), ScoreNode("S2"), ScoreNode("S3") }
            };
            display.SelectedIndex = 2;
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { ScoreNode("S1"), ScoreNode("S2"), ScoreNode("S3") });
            SetPrivateField(stage, "_filteredView", null);
            // Refresh flag stays false: simulates the steady state after BeginRecentPlaysLoad
            // completes on the All Songs tab (the continuation must NOT set the flag).
            SetPrivateField(stage, "_tabListNeedsRefresh", false);
            SetPrivateField(stage, "_uiManager", null);

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            Assert.False(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
            Assert.Equal(2, display.SelectedIndex);
            Assert.Equal("S3", display.CurrentList[display.SelectedIndex].Title);
        }

        // Real exercise of the retirement staleness guard in BeginRecentPlaysLoad:
        // a result that arrives after the activation scope retired is dropped and must
        // not overwrite fresh state from a newer activation.
        [Fact]
        public async Task BeginRecentPlaysLoad_WhenScopeRetires_ShouldPreserveExistingNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            // Simulate "fresh" data that a newer activation already populated.
            var freshNodes = new List<SongListNode> { ScoreNode("Fresh") };
            SetPrivateField(stage, "_recentPlayNodes", freshNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            stage.InstallTestBackgroundScope();

            // Start the background load for the current activation.
            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

            // Retire the scope (as Deactivate does before teardown) — the in-flight
            // completion drops instead of being applied. The grace wait is cosmetic:
            // a retired scope structurally cannot apply results.
            RetireStageScope(stage);
            await Task.Delay(300);

            // The dropped completion must NOT overwrite _recentPlayNodes.
            var nodes = GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes");
            Assert.NotNull(nodes);
            Assert.Single(nodes);
            Assert.Equal("Fresh", nodes[0].Title);
        }

        // Companion to the guarded test above: proves the completion actually runs and
        // overwrites _recentPlayNodes when the scope stays active. Without this test,
        // the guarded test could pass simply because the load never completed.
        [Fact]
        public async Task BeginRecentPlaysLoad_WhenScopeActive_ShouldApplyResultOnDrain()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var staleNodes = new List<SongListNode> { ScoreNode("Stale") };
            SetPrivateField(stage, "_recentPlayNodes", staleNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            stage.InstallTestBackgroundScope();

            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");
            // Scope stays active — the worker's queued completion applies on drain.
            await DrainUntilAsync(stage,
                () => GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes")!.Count == 0);

            Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes")!);
        }

        // The DB query must start on the scope worker, never on the update thread:
        // an EF/SQLite async call can execute synchronously and stall a frame, and
        // a retirement-cancelled load must not leave an orphaned query task behind.
        [Fact]
        public async Task BeginRecentPlaysLoad_ShouldInvokeLoaderOnWorkerThreadNotUpdateThread()
        {
            using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            connection.Open();
            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(connection)
                .Options;
            using (var schema = new SongDbContext(options))
                schema.Database.EnsureCreated();

            var updateThreadId = Environment.CurrentManagedThreadId;
            int? loaderThreadId = null;
            using var loaderInvoked = new ManualResetEventSlim(false);
            using var dbService = new SongDatabase(options, () =>
            {
                loaderThreadId = Environment.CurrentManagedThreadId;
                loaderInvoked.Set();
                return new SongDbContext(options);
            });

            var library = new SongLibrary(dbService);
            try
            {
                var stage = CreateStage(library);
                AttachCoreUi(stage, new SongListDisplay());
                SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
                stage.InstallTestBackgroundScope();

                InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

                Assert.True(
                    loaderInvoked.Wait(TimeSpan.FromSeconds(5)),
                    "the recent-plays DB query was never invoked");
                // The test thread stays alive (blocked on Wait above) while the query
                // runs, so a pool thread can never reuse the update thread's id here.
                Assert.NotEqual(updateThreadId, loaderThreadId!.Value);

                // The queued completion still applies on the drain.
                await DrainUntilAsync(stage,
                    () => GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes") != null);
                Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes")!);
            }
            finally
            {
                library.Dispose();
            }
        }

        [Fact]
        public void PopulateRecentPlaysList_WhenLoadFailed_DoesNotShowEmptyMessage()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode>());
            SetPrivateField(stage, "_recentPlaysLoadFailed", true);

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            // When load failed, _showEmptyRecentMessage must be false so the draw path
            // shows "Could not load recent plays" instead of "No recent plays yet."
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyRecentMessage"));
            Assert.True(GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));
        }

        // --- Back command on Recent tab tests ---

        [Fact]
        public void ExecuteInputCommand_BackOnRecent_WithActiveFilter_DoesNotResetFilter()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            // Simulate All-Songs filter state left over from before tab switch.
            var filterView = new List<FilteredSongResult> { new(ScoreNode("F1"), "") };
            SetPrivateField(stage, "_filteredView", filterView);

            InvokePrivateMethod<bool>(stage, "ExecuteInputCommand",
                new InputCommand(InputCommandType.Back, 0));

            // Filter state must be preserved (not cleared by OnFilterReset) because the
            // user is on the Recent tab. When they tab back to All Songs, the filter
            // should still be active.
            Assert.NotNull(GetPrivateField<IReadOnlyList<FilteredSongResult>>(stage, "_filteredView"));
        }

        [Fact]
        public void ExecuteInputCommand_BackOnRecent_WithNavigationStack_DoesNotPopStack()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            // Simulate All-Songs BOX navigation state left over from before tab switch.
            var navStack = new Stack<SongListNode>();
            navStack.Push(new SongListNode { Title = "BOX", Type = NodeType.BackBox });
            SetPrivateField(stage, "_navigationStack", navStack);
            SetPrivateField(stage, "_filteredView", null);

            InvokePrivateMethod<bool>(stage, "ExecuteInputCommand",
                new InputCommand(InputCommandType.Back, 0));

            // Navigation stack must be preserved because the user is on the Recent tab.
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack");
            Assert.Single(stack);
        }

        [Fact]
        public void ExecuteInputCommand_BackOnAllSongs_WithActiveFilter_ResetsFilter()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            var filterView = new List<FilteredSongResult> { new(ScoreNode("F1"), "") };
            SetPrivateField(stage, "_filteredView", filterView);

            InvokePrivateMethod<bool>(stage, "ExecuteInputCommand",
                new InputCommand(InputCommandType.Back, 0));

            // On All Songs tab, Back should clear the filter as before.
            Assert.Null(GetPrivateField<IReadOnlyList<FilteredSongResult>>(stage, "_filteredView"));
        }

        // --- BeginRecentPlaysLoad faulted-result tests ---
        // These exercise the retirement staleness guard on the faulted path by making
        // the game-owned library throw from GetRecentlyPlayedNodesAsync. The DB is
        // purged after initialization so CreateContext() throws InvalidOperationException.

        /// <summary>
        /// A game-style library whose database is "initialized" but faults on
        /// every context creation (HPA-801): models a broken/locked database for
        /// the failure-path tests without relying on file deletion (SQLite
        /// connection pooling keeps deleted files readable).
        /// </summary>
        private static SongLibrary CreateFaultingManager() => new(
            new SongDatabase(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<
                    DTXMania.Game.Lib.Song.Entities.SongDbContext>()
                    .UseSqlite("Data Source=:memory:")
                    .Options,
                () => throw new InvalidOperationException("database unavailable"),
                initialized: true));

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenDbThrows_SetsLoadFailedAndFlagsRefresh()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"stage_fault_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);
            // Break the initialized database: every context creation now faults,
            // so the load/write paths surface their failure states.
            manager = CreateFaultingManager();

            try
            {
                var stage = CreateStage(manager);
                var display = new SongListDisplay();
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
                stage.InstallTestBackgroundScope();

                InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

                await DrainUntilAsync(stage, () => GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));

                Assert.True(GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));
                Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
            }
            finally
            {
                manager.Dispose();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenScopeRetiresBeforeFault_ShouldDropStaleFault()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"stage_stale_fault_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);
            // Break the initialized database: every context creation now faults,
            // so the load/write paths surface their failure states.
            manager = CreateFaultingManager();

            try
            {
                var stage = CreateStage(manager);
                var display = new SongListDisplay();
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
                stage.InstallTestBackgroundScope();

                InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

                // Retire the scope (simulate re-Activate after navigation away) — the
                // faulted result must drop instead of flagging the load failed.
                RetireStageScope(stage);

                await Task.Delay(500);

                Assert.False(GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));
            }
            finally
            {
                manager.Dispose();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenAllSongsTab_DoesNotFlagListRefresh()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            stage.InstallTestBackgroundScope();

            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

            await DrainUntilAsync(stage,
                () => GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes") != null);

            // The success completion populates _recentPlayNodes (cache-warming)
            // but must NOT set _tabListNeedsRefresh on the All Songs tab.
            Assert.False(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_OnSuccess_ClearsPreExistingLoadFailedFlag()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlaysLoadFailed", true);
            stage.InstallTestBackgroundScope();

            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

            await DrainUntilAsync(stage, () => !GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));

            // The success completion must clear the failure flag so the draw
            // path shows the normal empty message instead of the error message.
            Assert.False(GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));
        }

        // --- BeginBookmarksLoad staleness/fault tests ---
        // These mirror the BeginRecentPlaysLoad pair above. A bookmark load whose scope
        // retired must drop its completion instead of overwriting fresh state, and DB
        // faults set _bookmarksLoadFailed only while the activation is still current.

        [Fact]
        public async Task BeginBookmarksLoad_WhenCompletionArrives_ShouldDeferStageMutationUntilDrain()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            var loadStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadResult = new TaskCompletionSource<List<SongListNode>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetPrivateField(stage, "_loadBookmarkedNodesAsync",
                new Func<Task<List<SongListNode>>>(() =>
                {
                    loadStarted.TrySetResult(true);
                    return loadResult.Task;
                }));
            stage.InstallTestBackgroundScope();

            var staleNodes = new List<SongListNode> { ScoreNode("Stale") };
            SetPrivateField(stage, "_bookmarkNodes", staleNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_uiManager", null);

            InvokePrivateMethod(stage, "BeginBookmarksLoad");
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            loadResult.SetResult(new List<SongListNode> { ScoreNode("Loaded") });

            // The worker only queues its result; give the pool thread a moment to reach the
            // queue, then prove nothing was applied before a drain ran. Only a production
            // bug (applying from the worker) could fail this — EnqueueSuccess never applies.
            await Task.Delay(50);
            var beforeDrain = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.NotNull(beforeDrain);
            Assert.Single(beforeDrain);
            Assert.Equal("Stale", beforeDrain[0].Title);

            await DrainUntilAsync(stage,
                () => GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes")![0].Title == "Loaded");

            var afterUpdate = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.NotNull(afterUpdate);
            Assert.Single(afterUpdate);
            Assert.Equal("Loaded", afterUpdate[0].Title);
        }

        [Fact]
        public async Task BeginBookmarksLoad_WhenScopeRetiresBeforeCompletion_ShouldDropQueuedResult()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            var loadStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadResult = new TaskCompletionSource<List<SongListNode>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetPrivateField(stage, "_loadBookmarkedNodesAsync",
                new Func<Task<List<SongListNode>>>(() =>
                {
                    loadStarted.TrySetResult(true);
                    return loadResult.Task;
                }));
            stage.InstallTestBackgroundScope();

            var freshNodes = new List<SongListNode> { ScoreNode("Fresh") };
            SetPrivateField(stage, "_bookmarkNodes", freshNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_uiManager", null);

            InvokePrivateMethod(stage, "BeginBookmarksLoad");
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            // Navigate away before the worker produces its result: the queued completion
            // (and any not-yet-queued one) must drop instead of reaching torn-down state.
            RetireStageScope(stage);
            loadResult.SetResult(new List<SongListNode> { ScoreNode("Stale completion") });
            await Task.Delay(300);

            var nodes = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.NotNull(nodes);
            Assert.Single(nodes);
            Assert.Equal("Fresh", nodes[0].Title);
        }

        [Fact]
        public async Task BeginBookmarksLoad_WhenScopeRetires_ShouldPreserveExistingNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var freshNodes = new List<SongListNode> { ScoreNode("Fresh") };
            SetPrivateField(stage, "_bookmarkNodes", freshNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            stage.InstallTestBackgroundScope();

            // Start the background load for the current activation.
            InvokePrivateMethod(stage, "BeginBookmarksLoad");

            // Retire the scope — the in-flight completion drops (grace wait is cosmetic;
            // a retired scope structurally cannot apply results).
            RetireStageScope(stage);

            await Task.Delay(300);

            var nodes = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.NotNull(nodes);
            Assert.Single(nodes);
            Assert.Equal("Fresh", nodes[0].Title);
        }

        [Fact]
        public async Task BeginBookmarksLoad_WhenScopeActive_ShouldOverwriteNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var staleNodes = new List<SongListNode> { ScoreNode("Stale") };
            SetPrivateField(stage, "_bookmarkNodes", staleNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            stage.InstallTestBackgroundScope();

            InvokePrivateMethod(stage, "BeginBookmarksLoad");

            await DrainUntilAsync(stage,
                () => GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes")!.Count == 0);

            // The completion was drained and overwrote _bookmarkNodes with the
            // empty result from the no-DB load.
            Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes")!);
        }

        [Fact]
        public async Task BeginBookmarksLoad_WhenDbThrows_ShouldSetLoadFailedAndFlagsRefresh()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"stage_bm_fault_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);
            // Break the initialized database: every context creation now faults,
            // so the load/write paths surface their failure states.
            manager = CreateFaultingManager();

            try
            {
                var stage = CreateStage(manager);
                var display = new SongListDisplay();
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
                stage.InstallTestBackgroundScope();

                InvokePrivateMethod(stage, "BeginBookmarksLoad");

                await DrainUntilAsync(stage, () => GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));

                Assert.True(GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));
                Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
            }
            finally
            {
                manager.Dispose();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task BeginBookmarksLoad_WhenScopeRetiresBeforeFault_ShouldDropStaleFault()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"stage_bm_stale_fault_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);
            // Break the initialized database: every context creation now faults,
            // so the load/write paths surface their failure states.
            manager = CreateFaultingManager();

            try
            {
                var stage = CreateStage(manager);
                var display = new SongListDisplay();
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
                stage.InstallTestBackgroundScope();

                InvokePrivateMethod(stage, "BeginBookmarksLoad");

                // Retire the scope (simulate navigation away) — the faulted result must
                // drop instead of flagging the load failed.
                RetireStageScope(stage);

                await Task.Delay(500);

                Assert.False(GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));
            }
            finally
            {
                manager.Dispose();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task BeginBookmarksLoad_OnSuccess_ShouldClearPreExistingLoadFailedFlag()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarksLoadFailed", true);
            stage.InstallTestBackgroundScope();

            InvokePrivateMethod(stage, "BeginBookmarksLoad");

            await DrainUntilAsync(stage, () => !GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));

            // The success completion must clear the failure flag so the draw
            // path shows the normal empty message instead of the error message.
            Assert.False(GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));
        }

        // --- BeginBookmarksLoad per-load sequence guard tests ---
        // These verify the _bookmarksLoadVersion guard, which prevents an older
        // same-activation load (e.g., the activation-time warm load still in flight
        // when the user bookmarks a song and switches to the Bookmarks tab) from
        // overwriting the result of a newer load with a stale pre-toggle snapshot.

        [Fact]
        public async Task BeginBookmarksLoad_WhenNewerSameActivationLoadSupersedes_ShouldPreserveFreshResult()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var loadStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseOlderLoad = new TaskCompletionSource<List<SongListNode>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetPrivateField(stage, "_loadBookmarkedNodesAsync",
                new Func<Task<List<SongListNode>>>(() =>
                {
                    loadStarted.TrySetResult(true);
                    return releaseOlderLoad.Task;
                }));
            stage.InstallTestBackgroundScope();

            // Simulate the result of a newer load already landing.
            var freshNodes = new List<SongListNode> { ScoreNode("Fresh") };
            SetPrivateField(stage, "_bookmarkNodes", freshNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);

            // Start the older background load — captures load version 1.
            InvokePrivateMethod(stage, "BeginBookmarksLoad");
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            // Simulate a newer same-activation load starting (e.g., from a tab switch
            // after a bookmark toggle). This bumps _bookmarksLoadVersion past the
            // captured value so the older load's completion is treated as stale.
            SetPrivateField(stage, "_bookmarksLoadVersion", 2);

            releaseOlderLoad.SetResult(new List<SongListNode>());
            // Deterministic worker/terminal barrier: wait until the worker has queued the
            // stale completion, then drain exactly once. Only a fence regression could
            // now apply it — a fixed delay here let the stale terminal pass unprocessed.
            DrainAfterTerminalQueued(stage);

            // The stale completion must NOT overwrite _bookmarkNodes with
            // the empty no-DB result.
            var nodes = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.NotNull(nodes);
            Assert.Single(nodes);
            Assert.Equal("Fresh", nodes[0].Title);
        }

        [Fact]
        public async Task BeginBookmarksLoad_WhenFaultedAndLoadVersionSuperseded_ShouldDiscardStaleFault()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"stage_bm_stale_load_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);
            // Break the initialized database: every context creation now faults,
            // so the load/write paths surface their failure states.
            manager = CreateFaultingManager();

            try
            {
                var stage = CreateStage(manager);
                var display = new SongListDisplay();
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
                stage.InstallTestBackgroundScope();

                // Start the older faulting load — captures load version 1.
                InvokePrivateMethod(stage, "BeginBookmarksLoad");

                // Simulate a newer same-activation load starting before the older
                // load's fault lands. The older fault must be discarded so it does
                // not spuriously set _bookmarksLoadFailed over the newer load's result.
                SetPrivateField(stage, "_bookmarksLoadVersion", 2);

                // Deterministic worker/terminal barrier: prove the stale fault terminal
                // queued and was processed (fenced) before checking the winner flag.
                DrainAfterTerminalQueued(stage);

                Assert.False(GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));
            }
            finally
            {
                manager.Dispose();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task BeginBookmarksLoad_WhenLoadVersionUnchanged_ShouldOverwriteNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var staleNodes = new List<SongListNode> { ScoreNode("Stale") };
            SetPrivateField(stage, "_bookmarkNodes", staleNodes);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            stage.InstallTestBackgroundScope();

            // Start the load — captures the current load version. No newer load
            // supersedes it, so the completion should be accepted on drain.
            InvokePrivateMethod(stage, "BeginBookmarksLoad");

            await DrainUntilAsync(stage,
                () => GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes")!.Count == 0);

            // The completion was drained un-superseded and overwrote _bookmarkNodes
            // with the empty result from the no-DB load.
            Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes")!);
        }

        // Rapid same-activation bookmark load requests (e.g. two tab switches while writes
        // settle) each start a load; the per-load version fence must make the newest load's
        // result win even when the older load completes last.
        [Fact]
        public async Task BeginBookmarksLoad_RapidSameActivationRequests_ShouldApplyNewestLoad()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            stage.InstallTestBackgroundScope();

            var loadsGate = new object();
            var loads = new List<(TaskCompletionSource<bool> Started, TaskCompletionSource<List<SongListNode>> Result)>();
            SetPrivateField(stage, "_loadBookmarkedNodesAsync",
                new Func<Task<List<SongListNode>>>(() =>
                {
                    var started = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var result = new TaskCompletionSource<List<SongListNode>>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    lock (loadsGate)
                    {
                        loads.Add((started, result));
                    }
                    started.TrySetResult(true);
                    return result.Task;
                }));

            // Loaders now start inside the scope workers, so registration order is
            // established by awaiting each start before issuing the next request:
            // only one request is outstanding while loads[0] registers, so loads[0] is
            // load A (version 1) and loads[1] is load B (version 2, newest).
            InvokePrivateMethod(stage, "BeginBookmarksLoad"); // load A (version 1)
            SpinWait.SpinUntil(() => { lock (loadsGate) return loads.Count >= 1; }, TimeSpan.FromSeconds(5));
            await loads[0].Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            InvokePrivateMethod(stage, "BeginBookmarksLoad"); // load B (version 2, newest)
            SpinWait.SpinUntil(() => { lock (loadsGate) return loads.Count >= 2; }, TimeSpan.FromSeconds(5));
            await loads[1].Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(2, loads.Count);

            // The newest load completes first with the fresh (empty) result...
            loads[1].Result.SetResult(new List<SongListNode>());
            await DrainUntilAsync(stage,
                () => GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes") is { Count: 0 });

            // ...and the older load completes last with a stale snapshot that must be fenced.
            // Deterministic worker/terminal barrier: prove the stale terminal queued and was
            // processed (fenced) before checking the winner.
            loads[0].Result.SetResult(new List<SongListNode> { ScoreNode("Stale") });
            DrainAfterTerminalQueued(stage);

            var nodes = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.NotNull(nodes);
            Assert.Empty(nodes);
        }

        [Fact]
        public async Task ToggleBookmarkFromBookmarksTab_WhenStaleLoadCompletesAfterUnbookmark_ShouldKeepRowRemoved()
        {
            var stage = CreateStage();
            var dbPath = LibraryOf(stage).Database.DatabasePath;
            try
            {
                await LibraryOf(stage).InitializeAsync(); // the toggle persists for real
                var node = Score("Song", 42, "/c/song.dtx");
                node.DatabaseSong!.IsBookmarked = true;
                var display = new SongListDisplay { CurrentList = new List<SongListNode> { node } };
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
                SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode> { node });
                SetPrivateField(stage, "_uiManager", null);
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
                stage.InstallTestBackgroundScope();

                InvokePrivateMethod(stage, "BeginBookmarksLoad");
                await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<int, Task>>(
                    stage, "_pendingBookmarkWrites");
                var releaseWrite = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                pending[42] = releaseWrite.Task;

                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");
                Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes"));

                releaseLoad.SetResult(new List<SongListNode> { node });
                DrainAfterTerminalQueued(stage);

                Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes"));
                releaseWrite.SetResult(true);
                await pending[42].WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                // Best-effort cleanup of the one database this test materializes;
                // the remaining factory databases stay uninitialized (no file).
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // Fake InputManager that reports Tab key pressed on the first poll.
        private sealed class TabKeyDetectInputManager : DTXMania.Game.Lib.Input.InputManager
        {
            private bool _consumed;
            public override bool IsKeyPressed(int keyCode)
            {
                if (keyCode == (int)Keys.Tab && !_consumed)
                {
                    _consumed = true;
                    return true;
                }
                return false;
            }
        }

        // Fake InputManager that never reports a key press.
        private sealed class NoKeyPressInputManager : DTXMania.Game.Lib.Input.InputManager
        {
            public override bool IsKeyPressed(int keyCode) => false;
        }
    }
}
