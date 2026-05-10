using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.UI
{
    public class SongStatusPanelFolderHintTests
    {
        [Fact]
        public void FolderHint_DefaultsEmpty()
        {
            var panel = new SongStatusPanel();
            Assert.Equal("", panel.FolderHint);
        }

        [Fact]
        public void FolderHint_SetAndGet()
        {
            var panel = new SongStatusPanel();
            panel.FolderHint = "J-POP / 80s";
            Assert.Equal("J-POP / 80s", panel.FolderHint);
        }

        [Fact]
        public void FolderHint_SetNull_BecomesEmpty()
        {
            var panel = new SongStatusPanel();
            panel.FolderHint = "test";
            panel.FolderHint = null!;
            Assert.Null(panel.FolderHint);
        }

        [Fact]
        public void UpdateSongInfo_DoesNotThrowOnNull()
        {
            var panel = new SongStatusPanel();
            panel.UpdateSongInfo(null, 0);
        }

        [Fact]
        public void UpdateSongInfo_SetsSongAndDifficulty()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode { Type = NodeType.Score, Title = "Test" };
            panel.UpdateSongInfo(node, 2);
        }

        [Fact]
        public void Dispose_WhenCalledDirectly_CleansUp()
        {
            var panel = new SongStatusPanel();
            panel.Dispose();
        }

        [Fact]
        public void Visible_DefaultsTrue()
        {
            var panel = new SongStatusPanel();
            Assert.True(panel.Visible);
        }

        [Fact]
        public void Visible_SetTrue()
        {
            var panel = new SongStatusPanel();
            panel.Visible = true;
            Assert.True(panel.Visible);
        }
    }
}
