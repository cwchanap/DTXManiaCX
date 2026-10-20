using System.Globalization;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public class ChartLevelFormatterTests
{
    [Theory]
    [InlineData(815, 0, "8.15")]
    [InlineData(815, 50, "8.15")] // Hundredths encoding ignores the separate decimal field.
    [InlineData(850, 0, "8.50")]
    [InlineData(100, 0, "1.00")]
    [InlineData(78, 33, "8.13")]
    [InlineData(80, 50, "8.50")]
    [InlineData(38, 0, "3.80")]
    [InlineData(0, 0, "--")]
    [InlineData(-1, 0, "--")]
    public void Format_SupportsBothDtxLevelEncodings(int level, int levelDec, string expected)
    {
        Assert.Equal(expected, ChartLevelFormatter.Format(level, levelDec));
    }

    [Fact]
    public void Format_UsesInvariantDecimalSeparator()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("8.15", ChartLevelFormatter.Format(815));
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }
}
