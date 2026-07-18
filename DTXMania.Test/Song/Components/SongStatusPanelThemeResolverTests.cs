using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song.Components
{
    /// <summary>
    /// "SongSelect.StatusNumberScale" enlarges the bitmap-font numbers on the
    /// song-select status panel (BPM, song length, total notes). NX skins keep
    /// scale 1.0 so their art renders pixel-identically.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongStatusPanelThemeResolverTests
    {
        [Fact]
        public void ResolveStatusNumberScale_WithEmptyTheme_ShouldBeOne()
        {
            Assert.Equal(1f, SongStatusPanel.ResolveStatusNumberScale(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveStatusNumberScale_WithThemedScale_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "SongSelect.StatusNumberScale=1.3" });

            Assert.Equal(1.3f, SongStatusPanel.ResolveStatusNumberScale(theme), 3);
        }
    }
}
