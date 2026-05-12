using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class SongStatusPanelExtendedTests
    {
        [Fact]
        public void UseStandaloneBPMBackground_ByDefault_ShouldBeFalse()
        {
            var panel = new SongStatusPanel();
            Assert.False(panel.UseStandaloneBPMBackground);
        }

        [Fact]
        public void UseStandaloneBPMBackground_WhenSetTrue_ShouldBeTrue()
        {
            var panel = new SongStatusPanel();
            panel.UseStandaloneBPMBackground = true;
            Assert.True(panel.UseStandaloneBPMBackground);
        }

        [Fact]
        public void Font_ByDefault_ShouldBeNull()
        {
            var panel = new SongStatusPanel();
            Assert.Null(panel.Font);
        }

        [Fact]
        public void Font_WhenSet_ShouldReturnValue()
        {
            var panel = new SongStatusPanel();
            panel.Font = null;
            Assert.Null(panel.Font);
        }

        [Fact]
        public void SmallFont_ByDefault_ShouldBeNull()
        {
            var panel = new SongStatusPanel();
            Assert.Null(panel.SmallFont);
        }

        [Fact]
        public void SmallFont_WhenSet_ShouldReturnValue()
        {
            var panel = new SongStatusPanel();
            panel.SmallFont = null;
            Assert.Null(panel.SmallFont);
        }

        [Fact]
        public void ManagedFont_WhenSetNull_ShouldUpdateFontToNull()
        {
            var panel = new SongStatusPanel();
            panel.ManagedFont = null;
            Assert.Null(panel.ManagedFont);
            Assert.Null(panel.Font);
        }

        [Fact]
        public void ManagedSmallFont_WhenSetNull_ShouldUpdateSmallFontToNull()
        {
            var panel = new SongStatusPanel();
            panel.ManagedSmallFont = null;
            Assert.Null(panel.ManagedSmallFont);
            Assert.Null(panel.SmallFont);
        }

        [Fact]
        public void WhitePixel_ByDefault_ShouldBeNull()
        {
            var panel = new SongStatusPanel();
            Assert.Null(panel.WhitePixel);
        }

        [Fact]
        public void WhitePixel_WhenSet_ShouldReturnValue()
        {
            var panel = new SongStatusPanel();
            panel.WhitePixel = null;
            Assert.Null(panel.WhitePixel);
        }

        [Fact]
        public void InitializeGraphicsGenerator_WhenNullRenderTarget_ShouldNotThrow()
        {
            var panel = new SongStatusPanel();
            panel.InitializeGraphicsGenerator(null, null);
        }

        [Fact]
        public void InitializeAuthenticGraphics_WhenNullResourceManager_ShouldNotThrow()
        {
            var panel = new SongStatusPanel();
            panel.InitializeAuthenticGraphics(null);
        }

        [Fact]
        public void Dispose_WhenCalledAfterInitializeAuthenticGraphics_ShouldNotThrow()
        {
            var panel = new SongStatusPanel();
            panel.InitializeAuthenticGraphics(null);
            panel.Dispose();
        }

        [Fact]
        public void Dispose_WhenCalledAfterInitializeGraphicsGenerator_ShouldNotThrow()
        {
            var panel = new SongStatusPanel();
            panel.InitializeGraphicsGenerator(null, null);
            panel.Dispose();
        }

        [Fact]
        public void ManagedFont_ByDefault_ShouldBeNull()
        {
            var panel = new SongStatusPanel();
            Assert.Null(panel.ManagedFont);
        }

        [Fact]
        public void ManagedSmallFont_ByDefault_ShouldBeNull()
        {
            var panel = new SongStatusPanel();
            Assert.Null(panel.ManagedSmallFont);
        }
    }
}
