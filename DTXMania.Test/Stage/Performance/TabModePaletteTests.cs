using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class TabModePaletteTests
    {
        [Fact]
        public void Resolve_WithEmptyTheme_ShouldUseCxNeonDefaults()
        {
            var palette = TabModePalette.Resolve(SkinTheme.Empty);

            Assert.Equal(new Color(0x22, 0xD3, 0xEE), palette.Accent);
            Assert.Equal(new Color(0xDB, 0xEB, 0xF0), palette.TextPrimary);
            Assert.Equal(new Color(0xF7, 0xCA, 0x41), palette.Perfect);
            Assert.Equal(new Color(0xE9, 0x45, 0x3A), palette.Miss);
        }

        [Fact]
        public void Resolve_WithNullTheme_ShouldMatchDefault()
        {
            var palette = TabModePalette.Resolve(null);

            Assert.Equal(TabModePalette.Default.Accent, palette.Accent);
            Assert.Equal(TabModePalette.Default.Danger, palette.Danger);
        }

        [Fact]
        public void Resolve_WithThemedKeys_ShouldUseUiRampAndResultLedgerColors()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "UI.Accent=#FF0000",
                "UI.TextPrimary=#010203",
                "Result.GreatText=#00FF00",
            });

            var palette = TabModePalette.Resolve(theme);

            Assert.Equal(new Color(0xFF, 0x00, 0x00), palette.Accent);
            Assert.Equal(new Color(0x01, 0x02, 0x03), palette.TextPrimary);
            Assert.Equal(new Color(0x00, 0xFF, 0x00), palette.ForJudgement(JudgementType.Great));
        }

        [Theory]
        [InlineData(JudgementType.Perfect)]
        [InlineData(JudgementType.Great)]
        [InlineData(JudgementType.Good)]
        [InlineData(JudgementType.Poor)]
        [InlineData(JudgementType.Miss)]
        public void ForJudgement_ShouldReturnThatTiersLedgerColor(JudgementType type)
        {
            var palette = TabModePalette.Default;
            var expected = type switch
            {
                JudgementType.Perfect => palette.Perfect,
                JudgementType.Great => palette.Great,
                JudgementType.Good => palette.Good,
                JudgementType.Poor => palette.Poor,
                _ => palette.Miss,
            };

            Assert.Equal(expected, palette.ForJudgement(type));
        }

        [Fact]
        public void GaugeSegment_ShouldRunDangerToWarningThenAccentToSuccess()
        {
            var palette = TabModePalette.Default;

            Assert.Equal(palette.Danger, palette.GaugeSegment(0f));
            Assert.Equal(palette.Accent, palette.GaugeSegment(0.25f));
            Assert.Equal(palette.Success, palette.GaugeSegment(1f));
        }
    }
}
