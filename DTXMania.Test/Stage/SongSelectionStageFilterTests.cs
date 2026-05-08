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
    }
}
