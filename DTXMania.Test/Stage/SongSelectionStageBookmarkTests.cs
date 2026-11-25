using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.SongSelection;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    // HPA-802 task 3: the Bookmarks cache and its distinct empty/error flags moved into
    // SongSelectionTabs (covered by SongSelectionTabsTests). These stage tests keep the
    // stage-owned wiring: the stage routes the active tab to the tabs owner's rows and
    // assigns them to the display. The owner is captured through the constructor factory
    // seam (no owner-field reflection) and its cache is filled through the REAL load path
    // via the loader seam, so the wiring assertion needs a genuinely populated row.
    [Trait("Category", "Unit")]
    public class SongSelectionStageBookmarkTests
    {
        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_AssignsTabOwnerRowsToDisplay()
        {
            // A real row loaded through the constructor seam: the display assignment is
            // only meaningfully wired when it projects a non-empty owner cache.
            var row = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Bookmarked Song",
                DatabaseSongId = 42
            };
            var (stage, tabs) = CreateStageWithTabs(
                loadBookmarkedNodesAsync: () => Task.FromResult(new List<SongListNode> { row }));
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            var scope = stage.InstallTestBackgroundScope();

            // The real routing: the stage's tab switch requests the owner refresh, the
            // query runs on the activation scope and applies on the update thread (the
            // drain below plays the game loop's role).
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.Bookmarks);
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        scope.Drain();
                        return tabs.BookmarkNodes != null;
                    },
                    TimeSpan.FromSeconds(5)),
                "the Bookmarks load was never applied on the update thread");

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Single(display.CurrentList);
            Assert.Same(row, display.CurrentList[0]);
            Assert.False(tabs.ShowEmptyBookmarksMessage);
            Assert.False(tabs.BookmarksLoadFailed);
        }
    }
}
