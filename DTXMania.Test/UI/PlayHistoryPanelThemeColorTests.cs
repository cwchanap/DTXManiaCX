using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Play-history rows draw in NX yellow by default; skins can restyle them via
    /// "SongSelect.HistoryText" (or the generic "UI.TextPrimary" token).
    /// </summary>
    [Trait("Category", "Unit")]
    public class PlayHistoryPanelThemeColorTests
    {
        [Fact]
        public void ResolveHistoryTextColor_WithEmptyTheme_ShouldKeepNxYellow()
        {
            Assert.Equal(DTXManiaVisualTheme.FontEffects.DefaultTextColor,
                PlayHistoryPanel.ResolveHistoryTextColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveHistoryTextColor_WithTextPrimaryToken_ShouldUseIt()
        {
            var theme = SkinTheme.Parse(new[] { "UI.TextPrimary=#F1F5F9" });

            Assert.Equal(new Color(0xF1, 0xF5, 0xF9),
                PlayHistoryPanel.ResolveHistoryTextColor(theme));
        }

        [Fact]
        public void ResolveHistoryTextColor_SpecificKey_ShouldWinOverToken()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "UI.TextPrimary=#F1F5F9",
                "SongSelect.HistoryText=#22D3EE"
            });

            Assert.Equal(new Color(0x22, 0xD3, 0xEE),
                PlayHistoryPanel.ResolveHistoryTextColor(theme));
        }
    }
}
