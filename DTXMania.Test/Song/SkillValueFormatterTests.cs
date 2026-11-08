using System.Globalization;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public class SkillValueFormatterTests
{
    [Theory]
    [InlineData(0.345, "0.35")]
    [InlineData(1.005, "1.01")]
    [InlineData(2.675, "2.68")]
    [InlineData(0.0, "0.00")]
    [InlineData(78.625, "78.63")]
    [InlineData(91.25, "91.25")]
    [InlineData(162.6, "162.60")]
    [InlineData(100.0, "100.00")]
    [InlineData(-5.0, "0.00")]
    public void Format_WhenGivenDecimalValue_ShouldUseCustomTwoDecimalRounding(double value, string expected)
    {
        Assert.Equal(expected, SkillValueFormatter.Format(value));
    }

    [Fact]
    public void Format_WhenCultureUsesCommaSeparator_ShouldUseInvariantDecimalSeparator()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("78.63", SkillValueFormatter.Format(78.625));
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }
}
