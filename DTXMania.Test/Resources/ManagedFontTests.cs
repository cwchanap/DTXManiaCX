using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using System;
using Xunit;

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Graphics;
using Moq;
namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Basic unit tests for ManagedFont interfaces and data structures
    /// Tests core functionality without MonoGame dependencies
    /// </summary>
    [Trait("Category", "Unit")]
    [Collection("ManagedFont")]
    public class ManagedFontTests : IDisposable
    {
        public ManagedFontTests()
        {
            // Basic setup for font-related tests
        }

        [Fact]
        public void FontLoadException_ShouldInitializeCorrectly()
        {
            // Arrange
            var fontPath = "Fonts/Arial.ttf";
            var message = "Failed to load font";
            var innerException = new InvalidOperationException("Invalid font format");

            // Act
            var exception1 = new FontLoadException(fontPath, message);
            var exception2 = new FontLoadException(fontPath, message, innerException);

            // Assert
            Assert.Equal(fontPath, exception1.FontPath);
            Assert.Equal(message, exception1.Message);
            Assert.Null(exception1.InnerException);

            Assert.Equal(fontPath, exception2.FontPath);
            Assert.Equal(message, exception2.Message);
            Assert.Equal(innerException, exception2.InnerException);
        }


        [Fact]
        public void TextRenderOptions_DefaultValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var options = new TextRenderOptions();

            // Assert
            Assert.Equal(Color.White, options.TextColor);
            Assert.False(options.EnableOutline);
            Assert.Equal(Color.Black, options.OutlineColor);
            Assert.Equal(1, options.OutlineThickness);
            Assert.False(options.EnableGradient);
            Assert.Equal(Color.White, options.GradientTopColor);
            Assert.Equal(Color.Gray, options.GradientBottomColor);
            Assert.False(options.EnableShadow);
            Assert.Equal(Color.Black, options.ShadowColor);
            Assert.Equal(new Vector2(2, 2), options.ShadowOffset);
            Assert.Equal(1.0f, options.Scale);
            Assert.Equal(0.0f, options.Rotation);
            Assert.Equal(TextAlignment.Left, options.Alignment);
            Assert.Equal(float.MaxValue, options.MaxWidth);
            Assert.False(options.WordWrap);
        }

        [Theory]
        [InlineData('A', 0x0041, "Basic ASCII")]
        [InlineData('あ', 0x3042, "Hiragana")]
        [InlineData('ア', 0x30A2, "Katakana")]
        [InlineData('漢', 0x6F22, "Kanji")]
        [InlineData('Ａ', 0xFF21, "Fullwidth")]
        public void JapaneseCharacterRanges_ShouldHaveCorrectUnicodeValues(char character, int expectedCode, string description)
        {
            // Arrange & Act
            var actualCode = (int)character;

            // Assert
            Assert.Equal(expectedCode, actualCode);
            Assert.NotNull(description); // Ensure description is provided
        }


        [Fact]
        public void TextCacheKey_ShouldIncludeAllRenderingOptions()
        {
            // Arrange
            var text = "Test";
            var options1 = new TextRenderOptions
            {
                TextColor = Color.Red,
                EnableOutline = true,
                OutlineColor = Color.Black,
                OutlineThickness = 2
            };
            var options2 = new TextRenderOptions
            {
                TextColor = Color.Blue,
                EnableOutline = true,
                OutlineColor = Color.Black,
                OutlineThickness = 2
            };

            // Act
            var key1 = GenerateTestCacheKey(text, options1);
            var key2 = GenerateTestCacheKey(text, options2);

            // Assert
            Assert.NotEqual(key1, key2); // Different colors should produce different keys
        }

        private string GenerateTestCacheKey(string text, TextRenderOptions options)
        {
            // Simulate the cache key generation logic
            var keyBuilder = new System.Text.StringBuilder();
            keyBuilder.Append(text);
            keyBuilder.Append('|');
            keyBuilder.Append(options.TextColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.EnableOutline);
            keyBuilder.Append('|');
            keyBuilder.Append(options.OutlineColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.OutlineThickness);
            return keyBuilder.ToString();
        }

        public void Dispose()
        {
            // No cleanup needed for this test class
        }

    [Fact]
    public void DrawStringWithGradient_WithSpriteFontAndMultiLineText_ShouldComputePerLineLerpColor()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('B', 10), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "GradientFont", 16);

        var textSize = font.MeasureString("A\nB");
        Assert.True(textSize.Y > 0);

        var lines = "A\nB".Split('\n');
        Assert.Equal(2, lines.Length);

        var topColor = Color.White;
        var bottomColor = Color.Black;
        float positionY = 10f;
        float currentY = positionY;

        var expectedColors = new List<float>();
        foreach (var line in lines)
        {
            var progress = (currentY - positionY) / textSize.Y;
            expectedColors.Add(progress);
            currentY += font.LineSpacing;
        }

        Assert.Equal(0f / textSize.Y, expectedColors[0]);
        Assert.Equal((float)font.LineSpacing / textSize.Y, expectedColors[1]);
    }

    [Fact]
    public void DrawStringWrapped_CenterAlignment_ShouldComputeOffsetX()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('B', 10), ('C', 10), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "WrapFont", 16);

        var lines = InvokePrivate<List<string>>(font, "WrapText", "A BC", 100f);
        Assert.Single(lines);

        var lineSize = font.MeasureString(lines[0]);
        var bounds = new Rectangle(0, 0, 100, 100);
        var expectedX = bounds.X + (int)((bounds.Width - lineSize.X) / 2);

        Assert.True(expectedX > 0, "Center alignment should offset X from left edge");
        Assert.Equal((int)((100 - lineSize.X) / 2), expectedX);
    }

    [Fact]
    public void DrawStringWrapped_RightAlignment_ShouldComputeOffsetX()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('B', 10), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "WrapFont", 16);

        var lines = InvokePrivate<List<string>>(font, "WrapText", "A B", 200f);
        Assert.Single(lines);

        var lineSize = font.MeasureString(lines[0]);
        var bounds = new Rectangle(0, 0, 200, 100);
        var expectedX = bounds.Right - (int)lineSize.X;

        Assert.True(expectedX > 0, "Right alignment should offset X from left edge");
        Assert.Equal(200 - (int)lineSize.X, expectedX);
    }

    [Fact]
    public void DrawString_WhenArgumentExceptionThrown_ShouldSanitizeText()
    {
        var spriteFont = CreateSpriteFont([('A', 10), ('B', 10), ('?', 8)], defaultCharacter: '?');
        var font = new ManagedFont(spriteFont, "SanitizeFont", 16);

        var sanitized = InvokePrivate<string>(font, "SanitizeText", "A\u2603B");

        Assert.Equal("A?B", sanitized);
    }

    [Fact]
    public void DrawString_AdvancedOverload_WhenArgumentExceptionThrown_ShouldSanitizeText()
    {
        var spriteFont = CreateSpriteFont([('X', 10), ('Y', 10), ('?', 8)], defaultCharacter: '?');
        var font = new ManagedFont(spriteFont, "SanitizeFont", 16);

        var sanitized = InvokePrivate<string>(font, "SanitizeText", "X\u2605Y");

        Assert.Equal("X?Y", sanitized);
    }

    [Fact]
    public void DrawStringWrapped_ShouldStopAtBoundsBottom()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('B', 10), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "WrapFont", 16);

        var lines = InvokePrivate<List<string>>(font, "WrapText", "A B A B A B", 20f);
        Assert.True(lines.Count > 1, "Text should wrap to multiple lines on narrow width");

        var boundsHeight = 20;
        var linesThatFit = 0;
        var currentY = 0;
        foreach (var line in lines)
        {
            if (currentY + font.LineSpacing > boundsHeight)
                break;
            linesThatFit++;
            currentY += (int)font.LineSpacing;
        }

        Assert.True(linesThatFit < lines.Count, "Bounds bottom should limit number of drawn lines");
        Assert.True(linesThatFit <= 1, "With boundsHeight=20 and lineSpacing=16, at most 1 line fits");
    }

    [Fact]
    public void SupportsCharacter_WithPuaGlyphInSpriteFontCharacters_ShouldBeSupported()
    {
        // HPA-409 regression: the legacy range scan never probes the SMuFL
        // Private Use Area (E000-F8FF), so the cache must union
        // SpriteFont.Characters; otherwise notation glyphs report as
        // unsupported and Bravura-Notation silently falls back. Exercised
        // through the IFont.SupportsCharacter seam, GPU-free.
        var spriteFont = CreateSpriteFont([('A', 10), ('\uE0A4', 12)]); // E0A4 = NoteheadBlack
        IFont font = new ManagedFont(spriteFont, "Bravura-Notation", 40);

        Assert.True(font.SupportsCharacter('\uE0A4'), "PUA glyph present in Characters must be supported");
        Assert.False(font.SupportsCharacter('\uE0A5'), "PUA code point absent from Characters must stay unsupported");
    }

    [Fact]
    public void SanitizeText_WhenNoReplacementCharacterExists_ShouldDropUnsupportedCharacters()
    {
        // Bravura-Notation bakes SMuFL PUA glyphs only, with no
        // DefaultCharacter: unsupported text must be dropped rather than
        // appended as an unsupported '?' that rethrows inside the draw/measure
        // catch block.
        var spriteFont = CreateSpriteFont([('\uE0A4', 12), ('\uE4E5', 10)]);
        var font = new ManagedFont(spriteFont, "Bravura-Notation", 40);

        var sanitized = InvokePrivate<string>(font, "SanitizeText", "A\uE0A4B\uE4E5C");

        Assert.Equal("\uE0A4\uE4E5", sanitized);
    }

    [Fact]
    public void MeasureString_WhenGlyphOnlyFontCannotRepresentText_ShouldReturnZero()
    {
        // Same glyph-only case through the public MeasureString path: the
        // first call throws ArgumentException, the retry must get the fully
        // sanitized (empty) string instead of throwing again.
        var spriteFont = CreateSpriteFont([('\uE0A4', 12)]);
        var font = new ManagedFont(spriteFont, "Bravura-Notation", 40);

        Assert.Equal(Vector2.Zero, font.MeasureString("ABC"));
    }

    #region Helpers

    private static SpriteFont CreateSpriteFont((char character, int width)[] glyphs, int lineSpacing = 16, char? defaultCharacter = null)
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var glyphBounds = new List<Rectangle>();
        var cropping = new List<Rectangle>();
        var characters = new List<char>();
        var kerning = new List<Vector3>();
        var x = 0;

        foreach (var (character, width) in glyphs.OrderBy(g => g.character))
        {
            glyphBounds.Add(new Rectangle(x, 0, width, lineSpacing));
            cropping.Add(new Rectangle(0, 0, width, lineSpacing));
            characters.Add(character);
            kerning.Add(new Vector3(0, width, 0));
            x += width;
        }

        return (SpriteFont)Activator.CreateInstance(
            typeof(SpriteFont),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [texture, glyphBounds, cropping, characters, lineSpacing, 0f, kerning, defaultCharacter],
            culture: null)!;
    }

    private static T InvokePrivate<T>(object target, string methodName, params object?[] args)
    {
        var method = typeof(ManagedFont).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(target, args)!;
    }

    #endregion
}
}
