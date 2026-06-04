using System.Collections.Generic;
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
        public void SwitchToNextTab_FromRecent_WrapsBackToAllSongs()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
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
