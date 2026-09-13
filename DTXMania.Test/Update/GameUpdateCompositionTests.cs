#nullable enable

using DTXMania.Game.Lib.Update;
using Xunit;

namespace DTXMania.Test.Update;

[Trait("Category", "Unit")]
public sealed class GameUpdateCompositionTests
{
    [Theory]
    [InlineData(true, null, true)]      // Windows + empty token -> enabled
    [InlineData(true, "token", false)]  // Windows + token -> disabled (automation/E2E harness)
    [InlineData(false, null, false)]    // non-Windows -> disabled
    public void ShouldEnable_ShouldMatchCompositionPredicate(bool isWindows, string? launchToken, bool expected)
    {
        Assert.Equal(expected, GameUpdateComposition.ShouldEnable(isWindows, launchToken));
    }
}
