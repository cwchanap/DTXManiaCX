using System.Reflection;
using DTXMania.Game.Lib.Song.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Verifies the DTXManiaNX bitmap-number sprite tables used by the song-select status panel.
    /// The source rectangles must stay inside the texture bounds and match NX's CActSelectStatusPanel
    /// layout so numbers render correctly (5_level number.png 250x28, 5_skill number.png 138x20,
    /// 5_bpm font.png 132x20).
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongStatusPanelNumberFontTests
    {
        private static Rectangle? InvokeRect(string methodName, char c)
        {
            var method = typeof(SongStatusPanel).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (Rectangle?)method!.Invoke(null, new object[] { c });
        }

        [Theory]
        [InlineData('0', 0, 20)]
        [InlineData('9', 180, 20)]
        [InlineData('.', 200, 10)]
        [InlineData('-', 210, 20)]
        [InlineData('?', 230, 20)]
        public void GetDifficultyNumberRect_KnownChars_MatchNxLayout(char c, int expectedX, int expectedWidth)
        {
            var rect = InvokeRect("GetDifficultyNumberRect", c);
            Assert.True(rect.HasValue);
            Assert.Equal(expectedX, rect!.Value.X);
            Assert.Equal(expectedWidth, rect.Value.Width);
            Assert.Equal(28, rect.Value.Height);
            Assert.True(rect.Value.Right <= 250, "5_level number.png is 250px wide");
        }

        [Theory]
        [InlineData('0', 0, 12)]
        [InlineData('9', 108, 12)]
        [InlineData('.', 120, 6)]
        [InlineData('%', 126, 12)]
        public void GetAchievementNumberRect_KnownChars_MatchNxLayout(char c, int expectedX, int expectedWidth)
        {
            var rect = InvokeRect("GetAchievementNumberRect", c);
            Assert.True(rect.HasValue);
            Assert.Equal(expectedX, rect!.Value.X);
            Assert.Equal(expectedWidth, rect.Value.Width);
            Assert.Equal(20, rect.Value.Height);
            Assert.True(rect.Value.Right <= 138, "5_skill number.png is 138px wide");
        }

        [Theory]
        [InlineData('0', 0, 12)]
        [InlineData('9', 108, 12)]
        [InlineData(':', 123, 6)]
        public void GetBpmNumberRect_KnownChars_MatchNxLayout(char c, int expectedX, int expectedWidth)
        {
            var rect = InvokeRect("GetBpmNumberRect", c);
            Assert.True(rect.HasValue);
            Assert.Equal(expectedX, rect!.Value.X);
            Assert.Equal(expectedWidth, rect.Value.Width);
            Assert.Equal(20, rect.Value.Height);
            Assert.True(rect.Value.Right <= 132, "5_bpm font.png is 132px wide");
        }

        [Theory]
        [InlineData('A')]
        [InlineData(' ')]
        public void GetRect_UnknownChar_ReturnsNull(char c)
        {
            Assert.False(InvokeRect("GetDifficultyNumberRect", c).HasValue);
            Assert.False(InvokeRect("GetAchievementNumberRect", c).HasValue);
            Assert.False(InvokeRect("GetBpmNumberRect", c).HasValue);
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData("123", 36)]    // 3 digits x 12px
        [InlineData("1:23", 42)]   // digit(12) + colon(6) + digit(12) + digit(12)
        [InlineData(" 90", 36)]    // leading space advances 12px so the value right-aligns
        public void MeasureBpmNumberWidth_ReturnsTotalAdvanceWidth(string text, int expected)
        {
            Assert.Equal(expected, SongStatusPanel.MeasureBpmNumberWidth(text));
        }
    }
}
