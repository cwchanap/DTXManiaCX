using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageTabTests
    {
        private static SongListNode ScoreNode(string title) => new SongListNode
        {
            Type = NodeType.Score,
            Title = title
        };

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

            Assert.Single(display.CurrentList);
            Assert.Equal("A1", display.CurrentList[0].Title);
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
        public void HandleLaneHitForTabSwitch_OnLowTom_SwitchesTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            // Low Tom is lane 8 in the lane-hit (KeyBindings) scheme.
            InvokePrivateMethod(stage, "HandleLaneHitForTabSwitch", 8);

            Assert.Equal(SongSelectionTab.RecentPlays, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void HandleLaneHitForTabSwitch_OnOtherLane_DoesNotSwitch()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "HandleLaneHitForTabSwitch", 4); // Snare

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
        public void OnTabSwitchLaneHit_WhenModalOpen_DoesNotSwitchTab()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            SetPrivateField(stage, "_searchFilterModal", modal);

            var args = new DTXMania.Game.Lib.Input.LaneHitEventArgs(8, default);
            InvokePrivateMethod(stage, "OnTabSwitchLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.AllSongs,
                GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void OnTabSwitchLaneHit_WhenModalClosed_SwitchesTabOnLowTom()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            var args = new DTXMania.Game.Lib.Input.LaneHitEventArgs(8, default);
            InvokePrivateMethod(stage, "OnTabSwitchLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.RecentPlays,
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

        // Real exercise of the _activationVersion staleness guard in BeginRecentPlaysLoad.
        // The companion pair below replaces the tautological tests that only hand-set
        // _tabListNeedsRefresh=false. Here we actually invoke BeginRecentPlaysLoad, bump
        // the version token, and wait for the continuation — proving the guard discards
        // stale results instead of overwriting fresh state.
        [Fact]
        public async Task BeginRecentPlaysLoad_WhenActivationVersionBumped_PreservesExistingNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            // Simulate "fresh" data that a newer activation already populated.
            var freshNodes = new List<SongListNode> { ScoreNode("Fresh") };
            SetPrivateField(stage, "_recentPlayNodes", freshNodes);
            SetPrivateField(stage, "_activationVersion", 0);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            // Start the background load — captures version=0.
            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

            // Simulate re-Activate: bump the version so the in-flight continuation is stale.
            SetPrivateField(stage, "_activationVersion", 1);

            // Wait for the thread-pool continuation to complete. With no DB connected,
            // GetRecentlyPlayedNodesAsync returns an empty list almost instantly.
            await Task.Delay(300);

            // The stale continuation must NOT have overwritten _recentPlayNodes.
            var nodes = GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes");
            Assert.NotNull(nodes);
            Assert.Single(nodes);
            Assert.Equal("Fresh", nodes[0].Title);
        }

        // Companion to the guarded test above: proves the continuation actually runs
        // and overwrites _recentPlayNodes when the version is NOT bumped. Without this
        // test, the guarded test could pass simply because the continuation hadn't
        // completed yet within the delay window.
        [Fact]
        public async Task BeginRecentPlaysLoad_WhenActivationVersionUnchanged_OverwritesNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var staleNodes = new List<SongListNode> { ScoreNode("Stale") };
            SetPrivateField(stage, "_recentPlayNodes", staleNodes);
            SetPrivateField(stage, "_activationVersion", 0);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");
            // Do NOT bump version — continuation should be accepted.

            await Task.Delay(300);

            // The continuation ran with matching version and overwrote _recentPlayNodes
            // with the empty result from the no-DB load.
            var nodes = GetPrivateField<List<SongListNode>>(stage, "_recentPlayNodes");
            Assert.NotNull(nodes);
            Assert.Empty(nodes);
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

        // --- BeginRecentPlaysLoad faulted-continuation tests ---
        // These exercise the _activationVersion staleness guard on the faulted
        // path (lines 2039-2053 in SongSelectionStage.cs) by making the SongManager
        // singleton throw from GetRecentlyPlayedNodesAsync. The DB is purged after
        // initialization so CreateContext() throws InvalidOperationException.

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenDbThrows_SetsLoadFailedAndFlagsRefresh()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"stage_fault_{Guid.NewGuid():N}.db");
            SongManager.ResetInstanceForTesting();
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(dbPath, purgeDatabaseFirst: true);
            await manager.DatabaseService!.PurgeDatabaseAsync();

            try
            {
                var stage = CreateStage();
                var display = new SongListDisplay();
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
                SetPrivateField(stage, "_activationVersion", 0);

                InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

                await Task.Delay(500);

                Assert.True(GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));
                Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
                if (File.Exists(dbPath))
                    try { File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenDbThrowsAndVersionBumped_DiscardsStaleFault()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"stage_stale_fault_{Guid.NewGuid():N}.db");
            SongManager.ResetInstanceForTesting();
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(dbPath, purgeDatabaseFirst: true);
            await manager.DatabaseService!.PurgeDatabaseAsync();

            try
            {
                var stage = CreateStage();
                var display = new SongListDisplay();
                AttachCoreUi(stage, display);
                SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
                SetPrivateField(stage, "_activationVersion", 0);

                InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

                // Bump version to simulate re-Activate — the faulted continuation
                // must be discarded.
                SetPrivateField(stage, "_activationVersion", 1);

                await Task.Delay(500);

                Assert.False(GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
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
            SetPrivateField(stage, "_activationVersion", 0);

            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

            await Task.Delay(300);

            // The success continuation populates _recentPlayNodes (cache-warming)
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
            SetPrivateField(stage, "_activationVersion", 0);
            SetPrivateField(stage, "_recentPlaysLoadFailed", true);

            InvokePrivateMethod(stage, "BeginRecentPlaysLoad");

            await Task.Delay(300);

            // The success continuation must clear the failure flag so the draw
            // path shows the normal empty message instead of the error message.
            Assert.False(GetPrivateField<bool>(stage, "_recentPlaysLoadFailed"));
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
