using System;
using System.Reflection;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// HPA-796 Task 2: the shared rectangle primitives. ChamferedBox.Fill is public and
    /// ChamferedBox.Border draws four edges with overlapping corners and configurable
    /// thickness/depth — the geometry every migrated caller previously inlined locally.
    /// Headless: asserts the exact rectangles; the SpriteBatch pass itself is covered by
    /// graphics-opt-in device tests in the renderers.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ChamferedBoxTests
    {
        [Fact]
        public void BorderRects_WithOnePixelThickness_ShouldOverlapCornersOnFourEdges()
        {
            var (top, bottom, left, right) = ChamferedBox.BorderRects(new Rectangle(10, 20, 30, 40), 1);

            Assert.Equal(new Rectangle(10, 20, 30, 1), top);    // top: full width
            Assert.Equal(new Rectangle(10, 59, 30, 1), bottom); // bottom: full width
            Assert.Equal(new Rectangle(10, 20, 1, 40), left);   // left: full height (overlap)
            Assert.Equal(new Rectangle(39, 20, 1, 40), right);  // right: full height (overlap)
        }

        [Fact]
        public void BorderRects_WithTwoPixelThickness_ShouldInsetBothHorizontalEdges()
        {
            var (top, bottom, left, right) = ChamferedBox.BorderRects(new Rectangle(0, 0, 50, 30), 2);

            Assert.Equal(new Rectangle(0, 0, 50, 2), top);      // top
            Assert.Equal(new Rectangle(0, 28, 50, 2), bottom);  // bottom
            Assert.Equal(new Rectangle(0, 0, 2, 30), left);     // left
            Assert.Equal(new Rectangle(48, 0, 2, 30), right);   // right
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void BorderRects_WithNonPositiveThickness_ShouldClampToOnePixel(int thickness)
        {
            var (top, bottom, _, _) = ChamferedBox.BorderRects(new Rectangle(5, 5, 20, 10), thickness);

            Assert.Equal(new Rectangle(5, 5, 20, 1), top);
            Assert.Equal(new Rectangle(5, 14, 20, 1), bottom);
        }

        [Fact]
        public void Fill_ShouldBePublicForReuse()
        {
            var method = typeof(ChamferedBox).GetMethod(
                "Fill",
                BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(method);
            Assert.Equal(5, method!.GetParameters().Length);
        }
    }
}
