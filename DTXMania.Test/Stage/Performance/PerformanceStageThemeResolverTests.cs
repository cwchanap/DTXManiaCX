using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Theme-driven overrides on the performance stage. The NX skill-panel art
    /// bakes its labels for the result-screen layout; skins whose panel art
    /// differs per screen point "Performance.SkillPanelTexture" at a
    /// performance-specific sheet. "Performance.StateFontFamily/Size" style the
    /// centered LOADING.../READY... text. Themeless (NX) skins keep NX values.
    /// </summary>
    [Trait("Category", "Unit")]
    public class PerformanceStageThemeResolverTests
    {
        [Fact]
        public void ResolveSkillPanelTexturePath_WithEmptyTheme_ShouldKeepNxPath()
        {
            Assert.Equal(TexturePath.SkillPanel,
                PerformanceStage.ResolveSkillPanelTexturePath(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveSkillPanelTexturePath_WithThemedPath_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(
                new[] { "Performance.SkillPanelTexture=Graphics/7_SkillPanel_perf.png" });

            Assert.Equal("Graphics/7_SkillPanel_perf.png",
                PerformanceStage.ResolveSkillPanelTexturePath(theme));
        }

        [Fact]
        public void ResolveSkillPanelTexturePath_WithEmptyThemedValue_ShouldFallBackToNxPath()
        {
            // A malformed `Performance.SkillPanelTexture=` line yields an empty
            // string from SkinTheme.GetString; coerce it to the NX default so
            // the loader does not pass an empty path to LoadTexture (which
            // throws) and leave the panel blank.
            var theme = SkinTheme.Parse(
                new[] { "Performance.SkillPanelTexture=" });

            Assert.Equal(TexturePath.SkillPanel,
                PerformanceStage.ResolveSkillPanelTexturePath(theme));
        }

        [Fact]
        public void ResolveStateFontFamily_WithEmptyTheme_ShouldBeEmpty()
        {
            Assert.Equal(string.Empty,
                PerformanceStage.ResolveStateFontFamily(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveStateFontFamilyAndSize_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(
                new[] { "Performance.StateFontFamily=Orbitron", "Performance.StateFontSize=32" });

            Assert.Equal("Orbitron", PerformanceStage.ResolveStateFontFamily(theme));
            Assert.Equal(32, PerformanceStage.ResolveStateFontSize(theme));
        }

        [Fact]
        public void ResolveStateFontSize_WithEmptyTheme_ShouldKeepNxSize()
        {
            Assert.Equal(24, PerformanceStage.ResolveStateFontSize(SkinTheme.Empty));
        }
    }
}
