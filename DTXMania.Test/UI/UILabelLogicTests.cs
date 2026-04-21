using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class UILabelLogicTests
{
    [Fact]
    public void OutlineThickness_ShouldClampAtZero()
    {
        var label = new UILabel();

        label.OutlineThickness = -5;

        Assert.Equal(0, label.OutlineThickness);
    }

    [Fact]
    public void Text_WhenSetToNull_ShouldBecomeEmptyString()
    {
        var label = new UILabel("before");

        label.Text = null!;

        Assert.Equal(string.Empty, label.Text);
    }

    [Fact]
    public void Font_WhenAssignedWithExistingText_ShouldUpdateSizeFromMeasuredText()
    {
        var label = new UILabel("AB");
        var font = CreateSpriteFont([('A', 12), ('B', 18)], lineSpacing: 24);

        label.Font = font;

        Assert.Same(font, label.Font);
        Assert.Equal(new Vector2(30, 24), label.Size);
    }

    [Fact]
    public void Text_WhenUpdatedWithAssignedFont_ShouldRecalculateSize()
    {
        var label = new UILabel("A")
        {
            Font = CreateSpriteFont([('A', 12), ('B', 18)], lineSpacing: 24)
        };

        label.Text = "AB";

        Assert.Equal("AB", label.Text);
        Assert.Equal(new Vector2(30, 24), label.Size);
    }

    [Theory]
    [InlineData(TextAlignment.Left, TextAlignment.Top, 10f, 20f)]
    [InlineData(TextAlignment.Center, TextAlignment.Center, 45f, 55f)]
    [InlineData(TextAlignment.Right, TextAlignment.Bottom, 80f, 90f)]
    public void CalculateTextPosition_ShouldHonorAlignment(TextAlignment horizontal, TextAlignment vertical, float expectedX, float expectedY)
    {
        var label = new UILabel
        {
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical
        };

        var position = ReflectionHelpers.InvokePrivateMethod<Vector2>(
            label,
            "CalculateTextPosition",
            new Rectangle(10, 20, 100, 80),
            new Vector2(30, 10));

        Assert.Equal(new Vector2(expectedX, expectedY), position);
    }

    [Fact]
    public void OnDraw_WhenLabelIsInvisible_ShouldReturnWithoutUsingSpriteBatch()
    {
        var label = new UILabel("hidden")
        {
            Visible = false
        };
        ReflectionHelpers.SetPrivateField(
            label,
            "_font",
            ReflectionHelpers.CreateUninitialized<Microsoft.Xna.Framework.Graphics.SpriteFont>());

        ReflectionHelpers.InvokePrivateMethod(label, "OnDraw", null!, 0d);
    }

    [Fact]
    public void OnDraw_WhenFontIsMissing_ShouldReturnWithoutUsingSpriteBatch()
    {
        var label = new UILabel("text");

        ReflectionHelpers.InvokePrivateMethod(label, "OnDraw", null!, 0d);
    }

    [Fact]
    public void OnDraw_WhenTextIsEmpty_ShouldReturnWithoutUsingSpriteBatch()
    {
        var label = new UILabel
        {
            Font = ReflectionHelpers.CreateUninitialized<Microsoft.Xna.Framework.Graphics.SpriteFont>()
        };

        ReflectionHelpers.InvokePrivateMethod(label, "OnDraw", null!, 0d);
    }

    [Fact]
    public void OnDraw_WhenOutlineAndShadowAreEnabled_ShouldDrawOutlineShadowAndMainText()
    {
        var label = new TrackingUILabel("AB")
        {
            Font = CreateSpriteFont([('A', 12), ('B', 18)], lineSpacing: 24),
            HasOutline = true,
            OutlineThickness = 1,
            OutlineColor = Color.Black,
            HasShadow = true,
            ShadowOffset = new Vector2(2, 3),
            ShadowColor = Color.Gray,
            TextColor = Color.White
        };
        label.Position = new Vector2(10, 20);

        label.InvokeDraw();

        Assert.Equal(10, label.DrawCalls.Count);
        Assert.Equal(8, label.DrawCalls.Count(call => call.Color == Color.Black));
        Assert.Contains(label.DrawCalls, call => call.Color == Color.Black && call.Position == new Vector2(9, 19));
        Assert.Contains(label.DrawCalls, call => call.Color == Color.Gray && call.Position == new Vector2(12, 23));
        Assert.Contains(label.DrawCalls, call => call.Color == Color.White && call.Position == new Vector2(10, 20));
    }

    [Fact]
    public void DrawOutline_WhenFontIsMissing_ShouldReturnWithoutUsingSpriteBatch()
    {
        var label = new UILabel("text");

        ReflectionHelpers.InvokePrivateMethod(label, "DrawOutline", null!, Vector2.Zero);
    }

    [Fact]
    public void DrawOutline_WhenTextIsEmpty_ShouldReturnWithoutUsingSpriteBatch()
    {
        var label = new UILabel
        {
            Font = ReflectionHelpers.CreateUninitialized<Microsoft.Xna.Framework.Graphics.SpriteFont>()
        };

        ReflectionHelpers.InvokePrivateMethod(label, "DrawOutline", null!, Vector2.Zero);
    }

    private static SpriteFont CreateSpriteFont((char character, int width)[] glyphs, int lineSpacing = 16, char? defaultCharacter = null)
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var glyphBounds = new List<Rectangle>();
        var cropping = new List<Rectangle>();
        var characters = new List<char>();
        var kerning = new List<Vector3>();
        var x = 0;

        foreach (var (character, width) in glyphs.OrderBy(glyph => glyph.character))
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

    private sealed class TrackingUILabel : UILabel
    {
        public TrackingUILabel(string text)
            : base(text)
        {
        }

        public List<(string Text, Vector2 Position, Color Color)> DrawCalls { get; } = [];

        public void InvokeDraw()
        {
            ReflectionHelpers.InvokePrivateMethod(this, "OnDraw", null!, 0d);
        }

        protected override void DrawText(SpriteBatch spriteBatch, SpriteFont font, string text, Vector2 position, Color color)
        {
            DrawCalls.Add((text, position, color));
        }
    }
}
