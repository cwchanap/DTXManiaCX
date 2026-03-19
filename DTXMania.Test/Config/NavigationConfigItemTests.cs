using DTXMania.Game.Lib.Config;

namespace DTXMania.Test.Config;

[Trait("Category", "Config")]
public class NavigationConfigItemTests
{
    [Fact]
    public void NavigationConfigItem_GetDisplayText_ShouldShowArrowPrefix()
    {
        var item = new NavigationConfigItem("Drum Key Mapping", () => { });
        Assert.Equal("> Drum Key Mapping", item.GetDisplayText());
    }

    [Theory]
    [InlineData("Toggle")]
    [InlineData("Previous")]
    [InlineData("Next")]
    public void NavigationConfigItem_ActionMethod_ShouldInvokeAction(string method)
    {
        bool invoked = false;
        var item = new NavigationConfigItem("Test", () => invoked = true);
        switch (method)
        {
            case "Toggle":   item.ToggleValue();   break;
            case "Previous": item.PreviousValue(); break;
            case "Next":     item.NextValue();     break;
        }
        Assert.True(invoked);
    }

    [Fact]
    public void NavigationConfigItem_Constructor_NullAction_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationConfigItem("Test", null!));
    }
}
