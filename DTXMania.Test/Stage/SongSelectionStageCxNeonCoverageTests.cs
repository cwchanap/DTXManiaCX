using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageCxNeonCoverageTests
    {
        [Fact]
        public void DrawTabBar_WithOnlyThemedTabFont_ShouldUseThemeColorsAndMeasureEveryLabel()
        {
            var stage = CreateUninitializedStage();
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
            var stage = CreateUninitializedStage();
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

        private static SongSelectionStage CreateUninitializedStage()
        {
#pragma warning disable SYSLIB0050
            return (SongSelectionStage)FormatterServices.GetUninitializedObject(typeof(SongSelectionStage));
#pragma warning restore SYSLIB0050
        }
    }
}
