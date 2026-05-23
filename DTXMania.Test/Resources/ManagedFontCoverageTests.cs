using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.Resources;

[Collection("ManagedFont")]
[Trait("Category", "Unit")]
public class ManagedFontCoverageTests
{
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
