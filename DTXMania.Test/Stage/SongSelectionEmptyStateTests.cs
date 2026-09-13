using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public sealed class SongSelectionEmptyStateTests
    {
        [Theory]
        [InlineData(
            (int)SongSelectionStage.SongLibraryEmptyState.NoActiveRoots,
            "Song folder missing or unreadable - fix it in CONFIG > Song Folders")]
        [InlineData(
            (int)SongSelectionStage.SongLibraryEmptyState.NoSupportedCharts,
            "No charts here - add DTX files or change CONFIG > Song Folders")]
        public void ResolveLibraryEmptyMessage_EmptyState_ShouldExplainCorrectRecovery(
            int stateValue,
            string expected)
        {
            var state = (SongSelectionStage.SongLibraryEmptyState)stateValue;
            Assert.Equal(expected, SongSelectionStage.ResolveLibraryEmptyMessage(state));
        }

        [Fact]
        public void ResolveLibraryEmptyMessage_HasSongs_ShouldReturnEmpty()
        {
            Assert.Equal(string.Empty, SongSelectionStage.ResolveLibraryEmptyMessage(
                SongSelectionStage.SongLibraryEmptyState.HasSongs));
        }

        [Theory]
        [InlineData((int)SongSelectionStage.SongLibraryEmptyState.NoActiveRoots)]
        [InlineData((int)SongSelectionStage.SongLibraryEmptyState.NoSupportedCharts)]
        public void ResolveLibraryEmptyMessage_RecoveryCopy_ShouldUseSpriteFontSafeAscii(
            int stateValue)
        {
            var state = (SongSelectionStage.SongLibraryEmptyState)stateValue;
            var message = SongSelectionStage.ResolveLibraryEmptyMessage(state);

            Assert.NotEmpty(message);
            Assert.All(message, character => Assert.InRange((int)character, 0x20, 0x7e));
        }

        [Fact]
        public void LibraryEmptyMessageLayout_ShouldUseRemainingSongBarWidth()
        {
            Assert.Equal(
                SongSelectionUILayout.SongBars.BarWidth - SongSelectionUILayout.SongBars.EmptyMessageOffsetX,
                SongSelectionUILayout.SongBars.EmptyMessageMaxWidth);
            Assert.True(SongSelectionUILayout.SongBars.EmptyMessageMaxWidth > 0);
        }
    }
}
