using System.Reflection;
using DTXMania.Game.Lib.Stage.DrumConfig;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumKitRendererTests
    {
        // The drum pieces are black line-art with transparent interiors. The renderer fills a
        // light "body" behind each pad so the black detail reads on the black stage, and lights
        // that body yellow when the pad is selected/focused/hovered. BodyColorFor is the pure
        // color-selection helper behind that behaviour; the rest of the renderer is graphics.
        private static Color BodyColorFor(bool highlighted)
        {
            var method = typeof(DrumKitRenderer).GetMethod("BodyColorFor",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (Color)method!.Invoke(null, new object[] { highlighted })!;
        }

        [Fact]
        public void BodyColorFor_WhenNotHighlighted_ReturnsLightHead()
        {
            var color = BodyColorFor(false);

            // A near-white head so the black line-art is visible on the black stage.
            Assert.True(color.R > 200 && color.G > 200 && color.B > 200,
                "Unhighlighted pad body should be near-white");
        }

        [Fact]
        public void BodyColorFor_WhenHighlighted_ReturnsSelectionYellow()
        {
            var color = BodyColorFor(true);

            // Matches the yellow selection accent used elsewhere on the stage.
            Assert.Equal(new Color(255, 216, 77), color);
        }

        [Fact]
        public void BodyColorFor_HighlightDiffersFromNormal()
        {
            Assert.NotEqual(BodyColorFor(false), BodyColorFor(true));
        }
    }
}
