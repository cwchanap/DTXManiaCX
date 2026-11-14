using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

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
        public void DrawStringWithGradient_ForMultiLineText_ShouldInterpolateLineColorsByLineSpacingOverTextHeight()
        {
            var spriteFont = CreateSpriteFont(
                [('A', 10), ('B', 10), (' ', 8)],
                lineSpacing: 16);
            var font = new RecordingManagedFont(spriteFont, "GradientFont", 16);
            var textSize = font.MeasureString("A\nB");
            Assert.True(textSize.Y > 0);

            var topColor = new Color(0, 100, 200, 255);
            var bottomColor = new Color(200, 0, 100, 255);
            var startPosition = new Vector2(12f, 10f);

            font.DrawStringWithGradient((SpriteBatch)null!, "A\nB", startPosition, topColor, bottomColor);

            Assert.Equal(2, font.DrawCalls.Count);
            Assert.Equal("A", font.DrawCalls[0].Text);
            Assert.Equal(startPosition, font.DrawCalls[0].Position);
            Assert.Equal(Color.Lerp(topColor, bottomColor, 0f), font.DrawCalls[0].Color);

            Assert.Equal("B", font.DrawCalls[1].Text);
            Assert.Equal(new Vector2(startPosition.X, startPosition.Y + font.LineSpacing), font.DrawCalls[1].Position);
            Assert.Equal(Color.Lerp(topColor, bottomColor, (float)font.LineSpacing / textSize.Y), font.DrawCalls[1].Color);
        }

        [Fact]
        public void DrawStringWrapped_WithCenterAlignment_ShouldOffsetByHalfTheFreeWidth()
        {
            var spriteFont = CreateSpriteFont(
                [('A', 10), ('B', 10), ('C', 10), (' ', 8)],
                lineSpacing: 16);
            var font = new RecordingManagedFont(spriteFont, "WrapFont", 16);
            var bounds = new Rectangle(10, 20, 100, 100);

            font.DrawStringWrapped((SpriteBatch)null!, "A BC", bounds, Color.White, TextAlignment.Center);

            var call = Assert.Single(font.DrawCalls);
            var lineSize = font.MeasureString("A BC");
            var expectedX = bounds.X + (int)((bounds.Width - lineSize.X) / 2);
            Assert.Equal("A BC", call.Text);
            Assert.True(expectedX > bounds.X, "Center alignment should offset X from bounds.X");
            Assert.Equal(new Vector2(expectedX, bounds.Y), call.Position);
            Assert.Equal(Color.White, call.Color);
        }

        [Fact]
        public void DrawStringWrapped_WithRightAlignment_ShouldAlignToBoundsRight()
        {
            var spriteFont = CreateSpriteFont(
                [('A', 10), ('B', 10), (' ', 8)],
                lineSpacing: 16);
            var font = new RecordingManagedFont(spriteFont, "WrapFont", 16);
            var bounds = new Rectangle(10, 25, 200, 100);

            font.DrawStringWrapped((SpriteBatch)null!, "A B", bounds, Color.Cyan, TextAlignment.Right);

            var call = Assert.Single(font.DrawCalls);
            var lineSize = font.MeasureString("A B");
            var expectedX = bounds.Right - (int)lineSize.X;
            Assert.Equal("A B", call.Text);
            Assert.True(expectedX > bounds.X, "Right alignment should offset X from bounds.X");
            Assert.Equal(new Vector2(expectedX, bounds.Y), call.Position);
            Assert.Equal(Color.Cyan, call.Color);
        }

        [Theory]
        [InlineData("A\u2603B", "A?B")]
        [InlineData("X\u2605Y", "X?Y")]
        public void SanitizeText_WithUnsupportedCharacters_ShouldSubstituteDefaultCharacter(string input, string expected)
        {
            var spriteFont = CreateSpriteFont(
                [('A', 10), ('B', 10), ('X', 10), ('Y', 10), ('?', 8)],
                defaultCharacter: '?');
            var font = new ManagedFont(spriteFont, "SanitizeFont", 16);

            var sanitized = InvokePrivate<string>(font, "SanitizeText", input);

            Assert.Equal(expected, sanitized);
        }

        [Fact]
        public void DrawStringWrapped_WhenNextLineWouldExceedBoundsBottom_ShouldStopDrawing()
        {
            var spriteFont = CreateSpriteFont(
                [('A', 10), ('B', 10), (' ', 8)],
                lineSpacing: 16);
            var font = new RecordingManagedFont(spriteFont, "WrapFont", 16);
            var wrappedLines = InvokePrivate<List<string>>(font, "WrapText", "A B A B A B", 20f);
            Assert.True(wrappedLines.Count > 1, "Text should wrap to multiple lines on narrow width");

            var bounds = new Rectangle(5, 10, 20, 20);
            font.DrawStringWrapped((SpriteBatch)null!, "A B A B A B", bounds, Color.Yellow);

            var call = Assert.Single(font.DrawCalls);
            Assert.Equal(wrappedLines[0], call.Text);
            Assert.Equal(new Vector2(bounds.X, bounds.Y), call.Position);
            Assert.Equal(Color.Yellow, call.Color);
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

        private sealed class RecordingManagedFont : ManagedFont
        {
            public RecordingManagedFont(SpriteFont spriteFont, string sourcePath, int size)
                : base(spriteFont, sourcePath, size)
            {
            }

            public List<(string Text, Vector2 Position, Color Color)> DrawCalls { get; } = new();

            public override void DrawString(SpriteBatch spriteBatch, string text, Vector2 position, Color color)
            {
                DrawCalls.Add((text, position, color));
            }
        }

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
                cropping.Add(new Rectangle(x, 0, width, lineSpacing));
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
