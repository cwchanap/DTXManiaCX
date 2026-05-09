using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    public class SongSelectionStageClampingTests
    {
        private static SongListNode S(string title) =>
            new SongListNode { Type = NodeType.Score, Title = title };

        [Fact]
        public void ClampSelectionIndex_PreviousNodeStillPresent_ReturnsItsIndex()
        {
            var prev = S("B");
            var newList = new List<SongListNode> { S("A"), prev, S("C") };

            int idx = SongSelectionStage.ClampSelectionIndex(prev, newList);

            Assert.Equal(1, idx);
        }

        [Fact]
        public void ClampSelectionIndex_PreviousNodeMissing_ReturnsZero()
        {
            var prev = S("Removed");
            var newList = new List<SongListNode> { S("A"), S("B") };

            int idx = SongSelectionStage.ClampSelectionIndex(prev, newList);

            Assert.Equal(0, idx);
        }

        [Fact]
        public void ClampSelectionIndex_EmptyList_ReturnsZero()
        {
            int idx = SongSelectionStage.ClampSelectionIndex(S("X"), new List<SongListNode>());
            Assert.Equal(0, idx);
        }

        [Fact]
        public void ClampSelectionIndex_NullPrevious_ReturnsZero()
        {
            int idx = SongSelectionStage.ClampSelectionIndex(null, new List<SongListNode> { S("A") });
            Assert.Equal(0, idx);
        }
    }
}
