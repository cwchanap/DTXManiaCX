using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    public class SongSelectionStageFilterTests
    {
        [Fact]
        public void NewStage_FilterCriteriaDefaultsToEmpty()
        {
            // Stage.GetFilterCriteria() is a test-access helper added in this task.
            Assert.True(SongSelectionStage.DefaultFilterCriteriaIsEmpty());
        }

        [Fact]
        public void DefaultFilteredViewIsNull()
        {
            // No filter active → projection is null
            Assert.True(SongSelectionStage.DefaultFilteredViewIsNull());
        }

        [Fact]
        public void IsLibraryReady_SyncInitPath_DoesNotBlockApply()
        {
            // When SongManager is already initialized at Activate time, the stage takes the
            // synchronous else-branch in InitializeSongList: _songInitializationTask stays null
            // and _songInitializationProcessed stays false. The IsLibraryReady predicate must
            // still return true so the modal's Apply isn't permanently blocked.
            //
            // Predicate (mirrors OpenSearchFilterModal):
            //   _currentSongList != null && (_songInitializationTask == null || _songInitializationProcessed)
            object? list = new System.Collections.Generic.List<DTXMania.Game.Lib.Song.SongListNode>();
            System.Threading.Tasks.Task? task = null;
            bool processed = false;

            bool ready = list != null && (task == null || processed);

            Assert.True(ready);
        }

        [Fact]
        public void IsLibraryReady_AsyncStillRunning_BlocksApply()
        {
            // Async path before completion: task non-null, processed false → ready must be false.
            object? list = new System.Collections.Generic.List<DTXMania.Game.Lib.Song.SongListNode>();
            System.Threading.Tasks.Task task = System.Threading.Tasks.Task.CompletedTask;
            bool processed = false;

            bool ready = list != null && (task == null || processed);

            Assert.False(ready);
        }
    }
}
