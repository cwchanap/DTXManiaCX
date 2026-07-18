using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// The song list scrollbar indicator is a whitePixel-drawn square; its color
    /// resolves through the skin theme so CX Neon shows an accent-colored marker
    /// instead of a stark white box, while themeless (NX) skins keep white.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongListDisplayScrollbarThemeTests
    {
        [Fact]
        public void ResolveScrollbarIndicatorColor_WithEmptyTheme_ShouldStayWhite()
        {
            Assert.Equal(Color.White,
                SongListDisplay.ResolveScrollbarIndicatorColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveScrollbarIndicatorColor_WithAccentToken_ShouldUseIt()
        {
            var theme = SkinTheme.Parse(new[] { "UI.Accent=#22D3EE" });
            Assert.Equal(new Color(0x22, 0xD3, 0xEE),
                SongListDisplay.ResolveScrollbarIndicatorColor(theme));
        }

        [Fact]
        public void ResolveScrollbarIndicatorColor_SpecificKey_ShouldWinOverAccent()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "UI.Accent=#22D3EE",
                "SongSelect.ScrollbarIndicator=#E879F9"
            });
            Assert.Equal(new Color(0xE8, 0x79, 0xF9),
                SongListDisplay.ResolveScrollbarIndicatorColor(theme));
        }
    }
}
