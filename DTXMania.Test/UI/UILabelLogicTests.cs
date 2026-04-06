using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;

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
}
