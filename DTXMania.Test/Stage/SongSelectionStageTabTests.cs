using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage;
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
    }

}
