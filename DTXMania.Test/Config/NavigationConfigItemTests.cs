using DTXMania.Game.Lib.Config;

namespace DTXMania.Test.Config;

public class NavigationConfigItemTests
{
    [Fact]
    public void NavigationConfigItem_GetDisplayText_ShouldShowArrowPrefix()
    {
        var item = new NavigationConfigItem("Drum Key Mapping", () => { });
        Assert.Equal("> Drum Key Mapping", item.GetDisplayText());
    }

    [Fact]
    public void NavigationConfigItem_ToggleValue_ShouldInvokeAction()
    {
        bool invoked = false;
        var item = new NavigationConfigItem("Test", () => invoked = true);
        item.ToggleValue();
        Assert.True(invoked);
    }

    [Fact]
    public void NavigationConfigItem_PreviousValue_ShouldInvokeAction()
    {
        bool invoked = false;
        var item = new NavigationConfigItem("Test", () => invoked = true);
        item.PreviousValue();
        Assert.True(invoked);
    }

    [Fact]
    public void NavigationConfigItem_NextValue_ShouldInvokeAction()
    {
        bool invoked = false;
        var item = new NavigationConfigItem("Test", () => invoked = true);
        item.NextValue();
        Assert.True(invoked);
    }

    [Fact]
    public void NavigationConfigItem_Constructor_NullAction_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NavigationConfigItem("Test", null!));
    }
}
