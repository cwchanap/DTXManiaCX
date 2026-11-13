using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

using DTXMania.Game.Lib.Song;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Graphics;
using Moq;
namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Tab-bar and status-message colors on song select resolve through the skin
    /// theme (specific key → generic token → NX default) so CX Neon shows accent
    /// tabs and slate status text while themeless (NX) skins stay byte-identical.
    /// </summary>
    [Collection("SongManager")]
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

        [Fact]
        public void ResolveTabFontFamily_WithEmptyTheme_ShouldBeEmpty()
        {
            // Empty = use the shared UI font, exactly as NX does.
            Assert.Equal(string.Empty,
                SongSelectionStage.ResolveTabFontFamily(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTabFontFamilyAndSize_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(
                new[] { "SongSelect.TabFontFamily=Orbitron", "SongSelect.TabFontSize=18" });

            Assert.Equal("Orbitron", SongSelectionStage.ResolveTabFontFamily(theme));
            Assert.Equal(18, SongSelectionStage.ResolveTabFontSize(theme));
        }

        [Fact]
        public void ResolveTabFontSize_WithEmptyTheme_ShouldKeepSharedUiFontSize()
        {
            Assert.Equal(DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.Background.DefaultFontSize,
                SongSelectionStage.ResolveTabFontSize(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveHistoryFont_WithEmptyTheme_ShouldKeepSharedUiFontAndNxScale()
        {
            Assert.Equal(string.Empty, SongSelectionStage.ResolveHistoryFontFamily(SkinTheme.Empty));
            Assert.Equal(DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.Background.DefaultFontSize,
                SongSelectionStage.ResolveHistoryFontSize(SkinTheme.Empty));
            Assert.Equal(DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.PlayHistoryPanel.FontScale,
                SongSelectionStage.ResolveHistoryTextScale(SkinTheme.Empty), 3);
        }

        [Fact]
        public void ResolveHistoryFont_WithThemedValues_ShouldUseThemedValues()
        {
            // The recent-plays rows are tabular, so skins can hand them a
            // monospace face at its native size instead of the scaled UI serif.
            var theme = SkinTheme.Parse(new[]
            {
                "SongSelect.HistoryFontFamily=ShareTechMono",
                "SongSelect.HistoryFontSize=14",
                "SongSelect.HistoryTextScale=1.0"
            });

            Assert.Equal("ShareTechMono", SongSelectionStage.ResolveHistoryFontFamily(theme));
            Assert.Equal(14, SongSelectionStage.ResolveHistoryFontSize(theme));
            Assert.Equal(1.0f, SongSelectionStage.ResolveHistoryTextScale(theme), 3);
        }

        [Fact]
        public void DrawTabBar_WithOnlyThemedTabFont_ShouldUseThemeColorsAndMeasureEveryLabel()
        {
            var stage = SongSelectionStageTestFactory.CreateStage();
            var tabFont = new Mock<IFont>();
            tabFont.Setup(value => value.MeasureString(It.IsAny<string>()))
                .Returns(new Vector2(40, 16));
            var resourceManager = new Mock<IResourceManager>();
            var activeColor = new Color(12, 34, 56);
            var inactiveColor = new Color(78, 90, 123);
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(new[]
            {
                "SongSelect.TabActive=#0C2238",
                "SongSelect.TabInactive=#4E5A7B"
            }));

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_font", null);
            ReflectionHelpers.SetPrivateField(stage, "_tabFont", tabFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);
            ReflectionHelpers.SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawTabBar");

            tabFont.Verify(value => value.DrawString(
                It.IsAny<SpriteBatch>(), "All Songs", It.IsAny<Vector2>(), inactiveColor), Times.Once);
            tabFont.Verify(value => value.DrawString(
                It.IsAny<SpriteBatch>(), "Recent", It.IsAny<Vector2>(), activeColor), Times.Once);
            tabFont.Verify(value => value.DrawString(
                It.IsAny<SpriteBatch>(), "Bookmarks", It.IsAny<Vector2>(), inactiveColor), Times.Once);
            tabFont.Verify(value => value.MeasureString(It.IsAny<string>()), Times.Exactly(3));
        }

        [Fact]
        public void ThemeResolvers_WithOverrides_ShouldReturnConfiguredTypographyAndStatusValues()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "SongSelect.TabFontFamily=Orbitron",
                "SongSelect.TabFontSize=27",
                "SongSelect.HistoryFontFamily=ShareTechMono",
                "SongSelect.HistoryFontSize=18",
                "SongSelect.HistoryTextScale=1.25",
                "SongSelect.StatusText=#AABBCC",
                "UI.Accent=#123456"
            });

            Assert.Equal("Orbitron", SongSelectionStage.ResolveTabFontFamily(theme));
            Assert.Equal(27, SongSelectionStage.ResolveTabFontSize(theme));
            Assert.Equal("ShareTechMono", SongSelectionStage.ResolveHistoryFontFamily(theme));
            Assert.Equal(18, SongSelectionStage.ResolveHistoryFontSize(theme));
            Assert.Equal(1.25f, SongSelectionStage.ResolveHistoryTextScale(theme));
            Assert.Equal(new Color(0xAA, 0xBB, 0xCC), SongSelectionStage.ResolveStatusTextColor(theme));
            Assert.Equal(new Color(0x12, 0x34, 0x56), SongSelectionStage.ResolveTabColor(true, theme));
        }

        [Fact]
        public void Deactivate_WithAllDisplayFontsLoaded_ShouldReleaseAndClearEachReference()
        {
            var stage = SongSelectionStageTestFactory.CreateStage();
            var bodyFont = new Mock<IFont>();
            var tabFont = new Mock<IFont>();
            var historyFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_font", bodyFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_tabFont", tabFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_historyFont", historyFont.Object);

            stage.Deactivate();

            bodyFont.Verify(value => value.RemoveReference(), Times.Once);
            tabFont.Verify(value => value.RemoveReference(), Times.Once);
            historyFont.Verify(value => value.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_tabFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_historyFont"));
        }

    }
}
