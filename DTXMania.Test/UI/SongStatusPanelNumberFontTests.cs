using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
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

        private static Color InvokeDrumLaneColor(int lane)
        {
            var panel = new SongStatusPanel();
            var method = typeof(SongStatusPanel).GetMethod(
                "GetDrumLaneColor", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (Color)method!.Invoke(panel, new object[] { lane })!;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(9)]
        public void GetDrumLaneColor_MatchesAuthenticGameplayLaneColor(int lane)
        {
            // The distribution bars must be colored by lane type using the same palette as the
            // performance stage (regression guard against the old ad-hoc color table).
            Assert.Equal(PerformanceUILayout.GetLaneColor(lane), InvokeDrumLaneColor(lane));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10)]
        public void GetDrumLaneColor_OutOfRange_ReturnsWhite(int lane)
        {
            Assert.Equal(Color.White, InvokeDrumLaneColor(lane));
        }

        [Fact]
        public void DrawBpmNumberBitmap_WithScaledThemeAndDoubleResolutionSheet_ShouldScaleSourceAndDestination()
        {
            var panel = new SongStatusPanel();
            var texture = new Mock<ITexture>();
            texture.SetupGet(value => value.Height).Returns(40);

            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(
                SkinTheme.Parse(new[] { "SongSelect.StatusNumberScale=1.5" }));

            ReflectionHelpers.SetPrivateField(panel, "_bpmNumberTexture", texture.Object);
            ReflectionHelpers.SetPrivateField(panel, "_resourceManager", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(
                panel,
                "DrawBpmNumberBitmap",
                (SpriteBatch)null!,
                10f,
                20f,
                "1:A2");

            VerifyBitmapDraw(texture, new Rectangle(10, 20, 18, 30), new Rectangle(24, 0, 24, 40));
            VerifyBitmapDraw(texture, new Rectangle(28, 20, 9, 30), new Rectangle(246, 0, 12, 40));
            // The unsupported 'A' advances by one digit cell but does not draw.
            VerifyBitmapDraw(texture, new Rectangle(55, 20, 18, 30), new Rectangle(48, 0, 24, 40));

            texture.Verify(value => value.Draw(
                    It.IsAny<SpriteBatch>(),
                    It.IsAny<Rectangle>(),
                    It.IsAny<Rectangle?>(),
                    It.IsAny<Color>(),
                    It.IsAny<float>(),
                    It.IsAny<Vector2>(),
                    It.IsAny<SpriteEffects>(),
                    It.IsAny<float>()),
                Times.Exactly(3));
        }

        private static void VerifyBitmapDraw(
            Mock<ITexture> texture,
            Rectangle destination,
            Rectangle source)
        {
            texture.Verify(value => value.Draw(
                    It.IsAny<SpriteBatch>(),
                    destination,
                    It.Is<Rectangle?>(candidate => candidate.HasValue && candidate.Value == source),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    0f),
                Times.Once);
        }
    }
}
