using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    public class ComboDisplayGlyphTests
    {
        [Fact]
        public void CalculateDrumComboGlyphs_ZeroCombo_ShouldReturnEmptyList()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs(0);
            Assert.Empty(result);
        }

        [Fact]
        public void CalculateDrumComboGlyphs_NegativeCombo_ShouldReturnEmptyList()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs(-1);
            Assert.Empty(result);
        }

        [Fact]
        public void CalculateDrumComboGlyphs_SingleDigit_ShouldReturnTwoGlyphs()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs(5);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void CalculateDrumComboGlyphs_TwoDigits_ShouldReturnThreeGlyphs()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs(42);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void CalculateDrumComboGlyphs_ThreeDigits_ShouldUseDefaultTexturePath()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs(123, new Vector2(500, 300));
            for (int i = 1; i < result.Count; i++)
            {
                Assert.Equal(TexturePath.ComboDisplay, result[i].TexturePath);
            }
        }

        [Fact]
        public void CalculateDrumComboGlyphs_FourDigits_ShouldUseAltTexturePath()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs(1000, new Vector2(500, 300));
            Assert.True(result.Count >= 5);
            for (int i = 1; i < result.Count; i++)
            {
                Assert.Equal(TexturePath.ComboDisplayAlt, result[i].TexturePath);
            }
        }

        [Fact]
        public void CalculateDrumComboGlyphs_LargeCombo_ShouldLimitToTenDigits()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs((int)int.MaxValue, new Vector2(500, 300));
            Assert.Equal(11, result.Count);
        }

        [Fact]
        public void CalculateDrumComboGlyphs_LabelGlyph_ShouldUseComboDisplayTexture()
        {
            var result = ComboDisplay.CalculateDrumComboGlyphs(5);
            Assert.Equal(TexturePath.ComboDisplay, result[0].TexturePath);
        }

        [Fact]
        public void CalculateDrumComboGlyphs_WithCustomPosition_ShouldOffsetGlyphs()
        {
            var centerA = new Vector2(500, 300);
            var centerB = new Vector2(800, 600);
            var resultA = ComboDisplay.CalculateDrumComboGlyphs(42, centerA);
            var resultB = ComboDisplay.CalculateDrumComboGlyphs(42, centerB);
            Assert.NotEqual(resultA[0].Position, resultB[0].Position);
        }

        [Fact]
        public void CalculateDrumComboGlyphs_SingleDigitOverload_ShouldUseBasePosition()
        {
            var oneArg = ComboDisplay.CalculateDrumComboGlyphs(7);
            var twoArg = ComboDisplay.CalculateDrumComboGlyphs(7, new Vector2(1245, 60));
            Assert.Equal(twoArg.Count, oneArg.Count);
            for (int i = 0; i < oneArg.Count; i++)
            {
                Assert.Equal(twoArg[i].Position, oneArg[i].Position);
                Assert.Equal(twoArg[i].SourceRectangle, oneArg[i].SourceRectangle);
                Assert.Equal(twoArg[i].TexturePath, oneArg[i].TexturePath);
            }
        }
    }
}
