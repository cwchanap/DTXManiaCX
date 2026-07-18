using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Config item value text colors are themeable: a skin's Theme.ini can override
    /// them via the specific Config.* keys or the generic UI palette tokens, while a
    /// skin without a theme file keeps the NX-tuned defaults (dark text on the NX
    /// itembox's white value cell).
    /// </summary>
    [Trait("Category", "Unit")]
    public class ConfigStageThemeColorTests
    {
        [Fact]
        public void ResolveValueColor_WithEmptyTheme_ShouldKeepNxDefaults()
        {
            Assert.Equal(new Color(24, 24, 32), ConfigStage.ResolveValueColor(selected: false, SkinTheme.Empty));
            Assert.Equal(new Color(168, 52, 0), ConfigStage.ResolveValueColor(selected: true, SkinTheme.Empty));
        }

        [Fact]
        public void ResolveValueColor_WithGenericPaletteTokens_ShouldUseThem()
        {
            // The CX Neon pack ships exactly these tokens; no Config.* keys needed.
            var theme = SkinTheme.Parse(new[]
            {
                "UI.TextPrimary=#F1F5F9",
                "UI.Accent=#22D3EE"
            });

            Assert.Equal(new Color(0xF1, 0xF5, 0xF9), ConfigStage.ResolveValueColor(selected: false, theme));
            Assert.Equal(new Color(0x22, 0xD3, 0xEE), ConfigStage.ResolveValueColor(selected: true, theme));
        }

        [Fact]
        public void ResolveValueColor_WithSpecificConfigKeys_ShouldWinOverPaletteTokens()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "UI.TextPrimary=#F1F5F9",
                "UI.Accent=#22D3EE",
                "Config.ValueText=#AABBCC",
                "Config.SelectedValueText=#DDEEFF"
            });

            Assert.Equal(new Color(0xAA, 0xBB, 0xCC), ConfigStage.ResolveValueColor(selected: false, theme));
            Assert.Equal(new Color(0xDD, 0xEE, 0xFF), ConfigStage.ResolveValueColor(selected: true, theme));
        }

        [Fact]
        public void ResolveItemBarX_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(DTXMania.Game.Lib.UI.Layout.ConfigUILayout.ItemBarRect.X,
                ConfigStage.ResolveItemBarX(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveItemBarX_WithThemedX_ShouldUseThemedValue()
        {
            // CX Neon parks the NX separator strip off-screen: at NX x=400 it
            // crosses the CX menu panel's right border.
            var theme = SkinTheme.Parse(new[] { "Config.ItemBarX=-100" });

            Assert.Equal(-100, ConfigStage.ResolveItemBarX(theme));
        }
    }
}
