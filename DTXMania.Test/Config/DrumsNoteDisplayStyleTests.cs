using System.Linq;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class DrumsNoteDisplayStyleTests
{
    [Theory]
    [InlineData(DrumsNoteDisplayStyle.Lanes, "LANES")]
    [InlineData(DrumsNoteDisplayStyle.Tab, "TAB")]
    public void Labels_ShouldRoundTrip(DrumsNoteDisplayStyle style, string label)
    {
        var parsedStyle = DrumsNoteDisplayStyleExtensions.FromLabel(label);
        Assert.Equal(style, parsedStyle);
        Assert.Equal(label, DrumsNoteDisplayStyleExtensions.ToLabel(parsedStyle));
    }

    [Fact]
    public void Options_ShouldPreserveOrderAndExplicitValues()
    {
        Assert.Equal(0, (int)DrumsNoteDisplayStyle.Lanes);
        Assert.Equal(1, (int)DrumsNoteDisplayStyle.Tab);

        Assert.Equal(
            new[]
            {
                DrumsNoteDisplayStyle.Lanes,
                DrumsNoteDisplayStyle.Tab,
            },
            DrumsNoteDisplayStyleExtensions.Options.Select(option => option.Style));
        Assert.Equal(
            new[] { "LANES", "TAB" },
            DrumsNoteDisplayStyleExtensions.Labels);
    }

    [Fact]
    public void ToLabel_WhenStyleIsUndefined_ShouldFallBackToEnumName()
    {
        Assert.Equal("99", DrumsNoteDisplayStyleExtensions.ToLabel((DrumsNoteDisplayStyle)99));
    }
}
