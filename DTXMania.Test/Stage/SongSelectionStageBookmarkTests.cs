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
    public class SongSelectionStageBookmarkTests
    {
        private static SongListNode ScoreNode(string title) => new SongListNode
        {
            Type = NodeType.Score,
            Title = title
        };

        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_ShowsCachedNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes",
                new List<SongListNode> { ScoreNode("B1"), ScoreNode("B2") });

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Equal("B1", display.CurrentList[0].Title);
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyBookmarksMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_WhenEmpty_SetsEmptyFlag()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode>());

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Empty(display.CurrentList);
            Assert.True(GetPrivateField<bool>(stage, "_showEmptyBookmarksMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_WhenLoadFailed_DoesNotShowEmptyMessage()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode>());
            SetPrivateField(stage, "_bookmarksLoadFailed", true);

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.False(GetPrivateField<bool>(stage, "_showEmptyBookmarksMessage"));
            Assert.True(GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_WhenNodeListIsNull_ShowsEmptyAndSetsFlag()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            // _bookmarkNodes deliberately left null (never loaded).

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Empty(display.CurrentList);
            Assert.True(GetPrivateField<bool>(stage, "_showEmptyBookmarksMessage"));
        }
    }
}
