using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.SongSelection;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    // HPA-802 task 3: tab caches, flat-tab loads and the empty/error flags moved into
    // SongSelectionTabs (covered by SongSelectionTabsTests with constructor fixtures).
    // These stage tests keep the stage-owned wiring: input/overlay policy drives
    // SwitchToTab, and the stage projects the tabs owner's rows onto SongListDisplay.
    // No owner-private-field access: tab state is asserted through the owner's public
    // API reached like the intake tests reach _intake.
    [Trait("Category", "Unit")]
    public class SongSelectionStageTabTests
    {
        private static SongListNode ScoreNode(string title) => new SongListNode
        {
            Type = NodeType.Score,
            Title = title
        };

        // Tests that need the tabs owner build the stage through the constructor factory
        // seam and KEEP the instance — no owner-field reflection (HPA-802 review).

        [Fact]
        public void RefreshSongListForActiveTab_OnAllSongsTab_UsesBrowseList()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { ScoreNode("A1") });
            // Ensure no filter view is active so PopulateSongList() path runs.
            SetPrivateField(stage, "_filteredView", null);

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Equal("A1", display.CurrentList[0].Title);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnFlatTab_SuppressesGenericEmptyListMessage()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);
            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.False(display.ShowEmptyListMessage);

            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.AllSongs);
            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.True(display.ShowEmptyListMessage);
        }

        [Fact]
        public void SwitchToNextTab_TogglesActiveTabAndRequestsRepopulate()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.RecentPlays, tabs.ActiveTab);
            Assert.True(tabs.TabListNeedsRefresh);
        }

        [Fact]
        public void SwitchToNextTab_FromRecent_GoesToBookmarks()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.Bookmarks, tabs.ActiveTab);
            Assert.True(tabs.TabListNeedsRefresh);
        }

        [Fact]
        public void OpenSearchFilterModal_OnRecentTab_DoesNotOpen()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);

            // No modal is attached; method must early-return without throwing.
            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            // Confirm the tab is unchanged (method early-returned).
            Assert.Equal(SongSelectionTab.RecentPlays, tabs.ActiveTab);
        }

        [Fact]
        public void HandlePadLaneHit_OnFloorTom_SwitchesTab()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "HandlePadLaneHit", 7); // Floor Tom (FT)

            Assert.Equal(SongSelectionTab.RecentPlays, tabs.ActiveTab);
        }

        [Fact]
        public void HandlePadLaneHit_OnLowTom_DoesNotSwitchTab()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "HandlePadLaneHit", 6); // Low Tom (LT) is now "down"

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
        }

        [Fact]
        public void DetectTabSwitchKey_WhenTabPressed_SwitchesToNextTab()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_inputManager", new TabKeyDetectInputManager());

            InvokePrivateMethod(stage, "DetectTabSwitchKey");

            Assert.Equal(SongSelectionTab.RecentPlays, tabs.ActiveTab);
        }

        [Fact]
        public void DetectTabSwitchKey_WhenTabNotPressed_KeepsCurrentTab()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_inputManager", new NoKeyPressInputManager());

            InvokePrivateMethod(stage, "DetectTabSwitchKey");

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
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
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object);
            SetPrivateField(stage, "_searchFilterModal", modal);

            SetPrivateField(stage, "_elapsedTime", 1.0);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7);
            Assert.Equal(SongSelectionTab.Bookmarks, tabs.ActiveTab);
            Assert.False(modal.IsOpen);

            SetPrivateField(stage, "_elapsedTime", 1.3);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7);

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void HandlePadLaneHit_OnFloorTomTwiceEndingOnAllSongs_OpensSearchInPlace()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.Bookmarks);
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object);
            SetPrivateField(stage, "_searchFilterModal", modal);

            SetPrivateField(stage, "_elapsedTime", 1.0);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7); // wraps to All Songs
            SetPrivateField(stage, "_elapsedTime", 1.2);
            InvokePrivateMethod(stage, "HandlePadLaneHit", 7);

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void OnPadLaneHit_WhenModalOpen_DoesNotSwitchTab()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            SetPrivateField(stage, "_searchFilterModal", modal);

            var args = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("MIDI.43", true));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
        }

        [Fact]
        public void OnPadLaneHit_WhenModalClosed_SwitchesTabOnFloorTom()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var args = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("MIDI.43", true));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.RecentPlays, tabs.ActiveTab);
        }

        [Fact]
        public void OnPadLaneHit_FromKeyboardDrumKey_DoesNotSwitchTab()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);

            var args = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("Key.F", true));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, args);

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
        }

        [Fact]
        public void OnPadLaneHit_OnPadRelease_ShouldDoNothing()
        {
            var (stage, tabs) = CreateStageWithTabs();
            AttachCoreUi(stage);

            var release = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("MIDI.43", false));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, release);

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
        }

        [Fact]
        public void OnUpdate_WhenTabListNeedsRefresh_ResetsFlagAndPopulatesList()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            // No background scope is installed, so the tab switch itself cannot start a
            // load; the refresh request below stands in for a queued completion.
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);
            tabs.RequestAsyncTabListRefresh();
            SetPrivateField(stage, "_uiManager", null); // Avoid NRE in _uiManager.Update

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            // The owner projects an empty list and the consumption clears the flag exactly
            // once. No Recent load completed (there is no scope in this synthetic setup), so
            // the empty-results message stays hidden rather than masquerading as a successful load.
            Assert.False(tabs.TabListNeedsRefresh);
            Assert.Empty(display.CurrentList);
            Assert.False(tabs.ShowEmptyRecentMessage);
        }

        [Fact]
        public void OnUpdate_WhenTabListDoesNotNeedRefresh_LeavesDisplayUntouched()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { ScoreNode("Existing") }
            };
            AttachCoreUi(stage, display);
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);
            tabs.RequestAsyncTabListRefresh();
            var consumed = tabs.BeginTabListRefreshConsumption();
            tabs.CompleteTabListRefreshConsumption(consumed); // consumed with no racing request
            SetPrivateField(stage, "_uiManager", null);

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            // The refresh flag stays false and the display is untouched.
            Assert.False(tabs.TabListNeedsRefresh);
            Assert.Single(display.CurrentList);
            Assert.Equal("Existing", display.CurrentList[0].Title);
        }

        // Regression guard for the stale-refresh-flag bug: Activate must clear any
        // pending refresh left over from a prior activation so the first OnUpdate
        // doesn't repopulate the All Songs list and reset the user's selection.
        [Fact]
        public void StaleTabListNeedsRefresh_OnReactivation_DoesNotResetAllSongsSelection()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { ScoreNode("A1"), ScoreNode("A2"), ScoreNode("A3") }
            };
            AttachCoreUi(stage, display);
            // Simulate prior session: user was on All Songs, then tab-switched (which
            // flags a refresh), then backed out before OnUpdate.
            display.SelectedIndex = 2;
            InvokePrivateMethod(stage, "SwitchToNextTab");
            Assert.True(tabs.TabListNeedsRefresh);

            // Simulate re-Activate: the owner resets tab state (tab back to AllSongs,
            // refresh protocol cleared) without touching the durable write state.
            tabs.ResetForActivation();
            SetPrivateField(stage, "_uiManager", null);

            // Re-attach the display list that the prior session had at index 2.
            display.CurrentList = new List<SongListNode> { ScoreNode("A1"), ScoreNode("A2"), ScoreNode("A3") };
            display.SelectedIndex = 2;

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            // Without the reset, the pending refresh would have repopulated the list
            // and reset the user's selection. With it, the selection is preserved.
            Assert.False(tabs.TabListNeedsRefresh);
            Assert.Equal(2, display.SelectedIndex);
        }

        // Regression guard for the cache-warming bug: a recent-plays completion must not
        // flag a refresh when the active tab is All Songs, otherwise the user's position
        // in the All Songs list gets reset to index 0 once the background load completes.
        [Fact]
        public void OnUpdate_OnAllSongsTab_WhenRefreshFlagFalse_PreservesSelection()
        {
            var (stage, tabs) = CreateStageWithTabs();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { ScoreNode("S1"), ScoreNode("S2"), ScoreNode("S3") }
            };
            display.SelectedIndex = 2;
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { ScoreNode("S1"), ScoreNode("S2"), ScoreNode("S3") });
            SetPrivateField(stage, "_filteredView", null);
            // Refresh flag stays false: the steady state after a recent-plays completion
            // on the All Songs tab (the completion must NOT request a refresh).
            SetPrivateField(stage, "_uiManager", null);

            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            Assert.False(tabs.TabListNeedsRefresh);
            Assert.Equal(2, display.SelectedIndex);
            Assert.Equal("S3", display.CurrentList[display.SelectedIndex].Title);
        }

        // --- Back command on Recent tab tests ---

        [Fact]
        public void ExecuteInputCommand_BackOnRecent_WithActiveFilter_DoesNotResetFilter()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);
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
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);
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
            var filterView = new List<FilteredSongResult> { new(ScoreNode("F1"), "") };
            SetPrivateField(stage, "_filteredView", filterView);

            InvokePrivateMethod<bool>(stage, "ExecuteInputCommand",
                new InputCommand(InputCommandType.Back, 0));

            // On All Songs tab, Back should clear the filter as before.
            Assert.Null(GetPrivateField<IReadOnlyList<FilteredSongResult>>(stage, "_filteredView"));
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
