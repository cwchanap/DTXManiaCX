using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// The selected-song artist name draws at NX-absolute (right edge 1235, y 320),
    /// which lands on the CX Neon selected bar's bottom border. Skins may move and
    /// scale it via Theme.ini layout keys; themeless (NX) skins keep the NX values.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongListDisplayArtistNameThemeTests
    {
        [Fact]
        public void ResolveArtistNamePosition_WithEmptyTheme_ShouldUseNxDefaults()
        {
            var pos = SongListDisplay.ResolveArtistNamePosition(200f, SkinTheme.Empty);

            Assert.Equal(SongSelectionUILayout.SongBars.ArtistNameAbsoluteRightEdge - 200f, pos.X);
            Assert.Equal(SongSelectionUILayout.SongBars.ArtistNameAbsoluteY, pos.Y);
        }

        [Fact]
        public void ResolveArtistNamePosition_WithThemedY_ShouldUseThemedY()
        {
            var theme = SkinTheme.Parse(new[] { "SongSelect.ArtistNameY=306" });

            var pos = SongListDisplay.ResolveArtistNamePosition(200f, theme);

            Assert.Equal(306f, pos.Y);
        }

        [Fact]
        public void ResolveArtistNamePosition_WideText_ShouldClampXToZero()
        {
            var pos = SongListDisplay.ResolveArtistNamePosition(5000f, SkinTheme.Empty);

            Assert.Equal(0f, pos.X);
        }

        [Fact]
        public void ResolveArtistNameScale_WithEmptyTheme_ShouldBeOne()
        {
            Assert.Equal(1f, SongListDisplay.ResolveArtistNameScale(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveArtistNameScale_WithThemeKey_ShouldUseIt()
        {
            var theme = SkinTheme.Parse(new[] { "SongSelect.ArtistNameScale=0.75" });

            Assert.Equal(0.75f, SongListDisplay.ResolveArtistNameScale(theme));
        }
    }
}
