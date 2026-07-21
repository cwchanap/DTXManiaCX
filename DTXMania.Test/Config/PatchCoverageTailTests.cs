using System.Reflection;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Config
{
    [Trait("Category", "Unit")]
    public class PatchCoverageTailTests
    {
        [Fact]
        public void InstructionsPosition_ShouldKeepDescendersInsideTheFrame()
        {
            Assert.Equal(new Vector2(16, 692), ConfigUILayout.InstructionsPos);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NormalizeSkinPathForComparison_WithMissingPath_ShouldReturnEmpty(string? path)
        {
            var method = typeof(ConfigManager).GetMethod(
                "NormalizeSkinPathForComparison",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(null, new object?[] { path });

            Assert.Equal(string.Empty, result);
        }
    }
}
