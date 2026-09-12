using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public sealed class SongSelectionEmptyStateTests
    {
        [Theory]
        [InlineData(
            (int)SongSelectionStage.SongLibraryEmptyState.NoActiveRoots,
            "No song folders available — add one in CONFIG > Song Folders")]
        [InlineData(
            (int)SongSelectionStage.SongLibraryEmptyState.NoSupportedCharts,
            "No supported charts found — check CONFIG > Song Folders")]
        public void GetLibraryEmptyStateMessage_EmptyState_ShouldExplainRecovery(
            int stateValue,
            string expected)
        {
            var state = (SongSelectionStage.SongLibraryEmptyState)stateValue;
            Assert.Equal(expected, SongSelectionStage.GetLibraryEmptyStateMessage(state));
        }

        [Fact]
        public void GetLibraryEmptyStateMessage_HasSongs_ShouldReturnNull()
        {
            Assert.Null(SongSelectionStage.GetLibraryEmptyStateMessage(
                SongSelectionStage.SongLibraryEmptyState.HasSongs));
        }
    }
}
