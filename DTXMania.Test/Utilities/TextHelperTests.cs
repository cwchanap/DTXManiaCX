using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Utilities
{
    /// <summary>
    /// Unit tests for <see cref="TextHelper.TruncateToWidth(string, float, IFont)"/>. The
    /// <see cref="Microsoft.Xna.Framework.Graphics.SpriteFont"/> overload shares the identical
    /// algorithm body but needs a GraphicsDevice to instantiate; it is covered by the Windows-only
    /// SongListDisplay rendering tests instead of here (Mac test project excludes graphics tests).
    /// </summary>
    [Trait("Category", "Unit")]
    public class TextHelperTests
    {
        [Fact]
        public void TruncateToWidth_WhenTextIsNull_ShouldReturnNull()
        {
            var font = new Mock<IFont>().Object;

            Assert.Null(TextHelper.TruncateToWidth(null!, 100f, font));
        }

        [Fact]
        public void TruncateToWidth_WhenTextIsEmpty_ShouldReturnEmpty()
        {
            var font = new Mock<IFont>().Object;

            Assert.Equal(string.Empty, TextHelper.TruncateToWidth(string.Empty, 100f, font));
        }

        [Fact]
        public void TruncateToWidth_WhenFontIsNull_ShouldReturnTextUnchanged()
        {
            Assert.Equal("hello", TextHelper.TruncateToWidth("hello", 1f, (IFont)null!));
        }

        [Fact]
        public void TruncateToWidth_WhenTextFits_ShouldReturnTextUnchanged()
        {
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>()))
                .Returns<string>(s => new Vector2(s.Length * 5f, 14f));

            var result = TextHelper.TruncateToWidth("short", 100f, font.Object);

            Assert.Equal("short", result);
        }

        [Fact]
        public void TruncateToWidth_WhenTextExceedsWidth_ShouldEllipsizeToFit()
        {
            // Per-character mock font: 8px/char. "longtextthatwontfit" = 19 chars = 152px.
            // With maxWidth=50, the ellipsis "..." (3 chars = 24px) alone fits; each extra char
            // adds 8px. 50/8 = 6.25, so the longest prefix + "..." that fits is 3 chars + "..."
            // = 6 chars = 48px. Binary search must converge on "lon...".
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>()))
                .Returns<string>(s => new Vector2(s.Length * 8f, 14f));

            var result = TextHelper.TruncateToWidth("longtextthatwontfit", 50f, font.Object);

            Assert.EndsWith("...", result);
            Assert.True(font.Object.MeasureString(result).X <= 50f + 0.01f,
                $"truncated result \"{result}\" must fit maxWidth");
        }

        [Fact]
        public void TruncateToWidth_WhenOnlyEllipsisFits_ShouldReturnEllipsis()
        {
            // 8px/char, maxWidth=24 -> exactly the ellipsis width. No prefix char fits.
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>()))
                .Returns<string>(s => new Vector2(s.Length * 8f, 14f));

            var result = TextHelper.TruncateToWidth("longtextthatwontfit", 24f, font.Object);

            Assert.Equal("...", result);
        }

        [Fact]
        public void TruncateToWidth_WhenNothingFits_ShouldReturnEmptyString()
        {
            // 8px/char, maxWidth=16 -> even "..." (24px) doesn't fit. bestFit stays "".
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>()))
                .Returns<string>(s => new Vector2(s.Length * 8f, 14f));

            var result = TextHelper.TruncateToWidth("longtextthatwontfit", 16f, font.Object);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void TruncateToWidth_ShouldReturnLongestPrefixThatFits()
        {
            // 5px/char. "abcdefghij" = 10 chars = 50px. maxWidth=40.
            // "..." = 15px. Each prefix char adds 5px. 40 - 15 = 25 -> 5 chars prefix.
            // "abcde..." = 8 chars = 40px. Exactly fits. Binary search must find it.
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>()))
                .Returns<string>(s => new Vector2(s.Length * 5f, 14f));

            var result = TextHelper.TruncateToWidth("abcdefghij", 40f, font.Object);

            Assert.Equal("abcde...", result);
            Assert.True(font.Object.MeasureString(result).X <= 40f + 0.01f);
        }
    }
}
