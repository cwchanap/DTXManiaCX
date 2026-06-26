using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class ConfigUILayoutTests
{
    [Fact]
    public void Panels_ShouldMatchNxCoordinates()
    {
        Assert.Equal(new Rectangle(0, 0, 1280, 720), ConfigUILayout.BackgroundRect);
        Assert.Equal(new Rectangle(400, 0, 18, 720), ConfigUILayout.ItemBarRect);
        Assert.Equal(new Rectangle(0, 0, 1280, 105), ConfigUILayout.HeaderRect);
        Assert.Equal(new Rectangle(0, 690, 1280, 30), ConfigUILayout.FooterRect);
        Assert.Equal(new Rectangle(245, 140, 180, 172), ConfigUILayout.MenuPanelRect);
        Assert.Equal(new Rectangle(800, 270, 280, 360), ConfigUILayout.DescriptionPanelRect);
    }

    [Theory]
    [InlineData(0, 156)]
    [InlineData(1, 190)]
    [InlineData(2, 224)]
    public void MenuRowY_ShouldStepBy34(int index, int expected)
    {
        Assert.Equal(expected, ConfigUILayout.MenuRowY(index));
    }

    [Fact]
    public void MenuCursorRect_ShouldSitOnSelectedRow()
    {
        Assert.Equal(new Rectangle(250, 153, 150, 30), ConfigUILayout.MenuCursorRect(0));
        Assert.Equal(new Rectangle(250, 221, 150, 30), ConfigUILayout.MenuCursorRect(2));
    }

    [Fact]
    public void ItemRowRect_ShouldStepBy60()
    {
        Assert.Equal(new Rectangle(430, 150, 360, 54), ConfigUILayout.ItemRowRect(0));
        Assert.Equal(new Rectangle(430, 210, 360, 54), ConfigUILayout.ItemRowRect(1));
    }

    [Fact]
    public void ItemCursorRect_ShouldFrameTheRow()
    {
        Assert.Equal(new Rectangle(426, 147, 368, 60), ConfigUILayout.ItemCursorRect(0));
        Assert.Equal(new Rectangle(426, 207, 368, 60), ConfigUILayout.ItemCursorRect(1));
    }

    [Fact]
    public void ItemTextPositions_ShouldOffsetFromRow()
    {
        Assert.Equal(new Vector2(454, 164), ConfigUILayout.ItemNamePos(0));
        Assert.Equal(new Vector2(760, 164), ConfigUILayout.ItemValuePos(0));
        Assert.Equal(new Vector2(454, 224), ConfigUILayout.ItemNamePos(1));
    }

    [Fact]
    public void DescriptionText_ShouldStartInsidePanel()
    {
        Assert.Equal(new Vector2(818, 288), ConfigUILayout.DescriptionTextPos);
        Assert.Equal(248, ConfigUILayout.DescriptionWrapWidth);
    }
}
