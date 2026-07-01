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
    [InlineData(0, 148)]
    [InlineData(1, 180)]
    [InlineData(2, 212)]
    public void MenuCursorRect_ShouldStepBy32(int index, int expectedY)
    {
        Assert.Equal(new Rectangle(250, expectedY, 170, 32), ConfigUILayout.MenuCursorRect(index));
    }

    [Fact]
    public void MenuLabelCenterX_ShouldBePanelCenter()
    {
        Assert.Equal(335, ConfigUILayout.MenuLabelCenterX);
    }

    [Fact]
    public void RowTopY_ShouldCenterSelectedItemWhenSettled()
    {
        // p == selected index -> that row sits at the focus row.
        Assert.Equal(189, ConfigUILayout.RowTopY(3, 3.0));
        Assert.Equal(189, ConfigUILayout.RowTopY(0, 0.0));
    }

    [Fact]
    public void RowTopY_ShouldStepByStride()
    {
        Assert.Equal(189 + 67, ConfigUILayout.RowTopY(4, 3.0));
        Assert.Equal(189 - 67, ConfigUILayout.RowTopY(2, 3.0));
    }

    [Theory]
    [InlineData(189, true)]    // focus row
    [InlineData(690, false)]   // top at footer edge -> below band
    [InlineData(-213, false)]  // far above -> hidden behind header
    [InlineData(60, true)]     // top above header bottom but box still intrudes band
    public void IsRowVisible_ShouldGateOnVisibleBand(int rowTopY, bool expected)
    {
        Assert.Equal(expected, ConfigUILayout.IsRowVisible(rowTopY));
    }

    [Fact]
    public void ItemBoxAndTextPositions_ShouldMatchNx()
    {
        Assert.Equal(new Rectangle(420, 189, 538, 80), ConfigUILayout.ItemBoxRect(189, ConfigUILayout.ItemBoxNormalWidth));
        Assert.Equal(new Rectangle(420, 189, 438, 80), ConfigUILayout.ItemBoxRect(189, ConfigUILayout.ItemBoxOtherWidth));
        Assert.Equal(new Vector2(440, 213), ConfigUILayout.ItemNamePos(189));
        Assert.Equal(new Vector2(680, 213), ConfigUILayout.ItemValuePos(189));
        Assert.Equal(new Rectangle(413, 193, 497, 68), ConfigUILayout.ItemCursorRect);
    }

    [Fact]
    public void DescriptionTitleAndBody_ShouldSitOnCorrectArtCells()
    {
        // Title on the white upper region; body on the black lower region.
        Assert.Equal(new Vector2(818, 300), ConfigUILayout.DescriptionTitlePos);
        Assert.Equal(new Vector2(818, 448), ConfigUILayout.DescriptionBodyPos);
        Assert.Equal(248, ConfigUILayout.DescriptionWrapWidth);
        Assert.True(ConfigUILayout.DescriptionBodyPos.Y > ConfigUILayout.DescriptionTitlePos.Y);
    }
}
