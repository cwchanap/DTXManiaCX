using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Tab-bar and status-message colors on song select resolve through the skin
    /// theme (specific key → generic token → NX default) so CX Neon shows accent
    /// tabs and slate status text while themeless (NX) skins stay byte-identical.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongSelectionStageThemeColorTests
    {
        [Fact]
        public void ResolveTabColor_ActiveWithEmptyTheme_ShouldKeepNxWhite()
        {
            Assert.Equal(SongSelectionUILayout.Tabs.ActiveColor,
                SongSelectionStage.ResolveTabColor(active: true, SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTabColor_ActiveWithAccentToken_ShouldUseAccent()
        {
            var theme = SkinTheme.Parse(new[] { "UI.Accent=#22D3EE" });

            Assert.Equal(new Color(0x22, 0xD3, 0xEE),
                SongSelectionStage.ResolveTabColor(active: true, theme));
        }

        [Fact]
        public void ResolveTabColor_InactiveWithEmptyTheme_ShouldKeepNxGray()
        {
            Assert.Equal(SongSelectionUILayout.Tabs.InactiveColor,
                SongSelectionStage.ResolveTabColor(active: false, SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTabColor_InactiveWithSpecificKey_ShouldUseIt()
        {
            var theme = SkinTheme.Parse(new[] { "SongSelect.TabInactive=#64748B" });

            Assert.Equal(new Color(0x64, 0x74, 0x8B),
                SongSelectionStage.ResolveTabColor(active: false, theme));
        }

        [Fact]
        public void ResolveStatusTextColor_WithEmptyTheme_ShouldKeepNxLightGray()
        {
            Assert.Equal(Color.LightGray,
                SongSelectionStage.ResolveStatusTextColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveStatusTextColor_WithSpecificKey_ShouldUseIt()
        {
            var theme = SkinTheme.Parse(new[] { "SongSelect.StatusText=#94A3B8" });

            Assert.Equal(new Color(0x94, 0xA3, 0xB8),
                SongSelectionStage.ResolveStatusTextColor(theme));
        }
    }
}
