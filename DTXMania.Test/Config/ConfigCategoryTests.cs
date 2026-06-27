using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Stage.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class ConfigCategoryTests
{
    private static IConfigItem Toggle(string name) =>
        new ToggleConfigItem(name, () => false, _ => { });

    [Fact]
    public void Constructor_ShouldExposeNameDescriptionAndItems()
    {
        var items = new List<IConfigItem> { Toggle("A"), Toggle("B") };

        var category = new ConfigCategory("Drums", "Drum settings.", items);

        Assert.Equal("Drums", category.Name);
        Assert.Equal("Drum settings.", category.Description);
        Assert.Equal(2, category.Items.Count);
        Assert.True(category.HasItems);
        Assert.Same(items[0], category.SelectedItem);
    }

    [Fact]
    public void MoveSelectionDown_ShouldWrapAround()
    {
        var category = new ConfigCategory("Drums", "", new List<IConfigItem> { Toggle("A"), Toggle("B") });

        category.MoveSelectionDown();
        Assert.Equal(1, category.SelectedIndex);

        category.MoveSelectionDown();
        Assert.Equal(0, category.SelectedIndex);
    }

    [Fact]
    public void MoveSelectionUp_FromFirst_ShouldWrapToLast()
    {
        var category = new ConfigCategory("Drums", "", new List<IConfigItem> { Toggle("A"), Toggle("B"), Toggle("C") });

        category.MoveSelectionUp();

        Assert.Equal(2, category.SelectedIndex);
    }

    [Fact]
    public void EmptyCategory_ShouldHaveNoItemsAndNoOpMoves()
    {
        var category = new ConfigCategory("Exit", "Leave.", new List<IConfigItem>());

        Assert.False(category.HasItems);
        Assert.Null(category.SelectedItem);

        category.MoveSelectionDown();
        category.MoveSelectionUp();

        Assert.Equal(0, category.SelectedIndex);
    }

    [Fact]
    public void SelectedIndex_AboveRange_ShouldClampToLastItem()
    {
        // The setter is self-enforcing so an out-of-range value can never drive a phantom cursor
        // row in the draw layer (which maps SelectedIndex straight to a screen rectangle).
        var category = new ConfigCategory("Drums", "", new List<IConfigItem> { Toggle("A"), Toggle("B") });

        category.SelectedIndex = 99;

        Assert.Equal(1, category.SelectedIndex);
    }

    [Fact]
    public void SelectedIndex_BelowRange_ShouldClampToFirstItem()
    {
        var category = new ConfigCategory("Drums", "", new List<IConfigItem> { Toggle("A"), Toggle("B") });

        category.SelectedIndex = -5;

        Assert.Equal(0, category.SelectedIndex);
    }

    [Fact]
    public void SelectedIndex_OnEmptyCategory_ShouldStayAtZero()
    {
        var category = new ConfigCategory("Exit", "", new List<IConfigItem>());

        category.SelectedIndex = 7;

        Assert.Equal(0, category.SelectedIndex);
        Assert.Null(category.SelectedItem);
    }
}
