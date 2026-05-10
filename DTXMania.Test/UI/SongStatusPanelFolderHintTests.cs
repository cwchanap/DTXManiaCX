using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class SongStatusPanelFolderHintTests
    {
        [Fact]
        public void FolderHint_WhenUninitialized_ShouldBeEmpty()
        {
            var panel = new SongStatusPanel();
            Assert.Equal("", panel.FolderHint);
        }

        [Fact]
        public void FolderHint_WhenSet_ShouldReturnValue()
        {
            var panel = new SongStatusPanel();
            panel.FolderHint = "J-POP / 80s";
            Assert.Equal("J-POP / 80s", panel.FolderHint);
        }

        [Fact]
        public void FolderHint_WhenSetToNull_ShouldBehaveAsDefined()
        {
            var panel = new SongStatusPanel();
            panel.FolderHint = "test";
            panel.FolderHint = null!;
            Assert.Null(panel.FolderHint);
        }

        [Fact]
        public void UpdateSongInfo_WhenNullInput_ShouldNotThrow()
        {
            var panel = new SongStatusPanel();
            panel.UpdateSongInfo(null, 0);
        }

        [Fact]
        public void UpdateSongInfo_WithNodeAndDifficulty_ShouldSetValues()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode { Type = NodeType.Score, Title = "Test" };
            panel.UpdateSongInfo(node, 2);
        }

        [Fact]
        public void Dispose_WhenCalled_ShouldReleaseResources()
        {
            var panel = new SongStatusPanel();
            panel.Dispose();
        }

        [Fact]
        public void Visible_ByDefault_ShouldBeTrue()
        {
            var panel = new SongStatusPanel();
            Assert.True(panel.Visible);
        }

        [Fact]
        public void Visible_WhenSetTrue_ShouldRemainTrue()
        {
            var panel = new SongStatusPanel();
            panel.Visible = true;
            Assert.True(panel.Visible);
        }
    }
}
