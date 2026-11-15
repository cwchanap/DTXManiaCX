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
    public class ChamferedBoxTests
    {
        [Fact]
        public void BorderRects_WithOnePixelThickness_ShouldOverlapCornersOnFourEdges()
        {
            var rects = ChamferedBox.BorderRects(new Rectangle(10, 20, 30, 40), 1);

            Assert.Equal(4, rects.Length);
            Assert.Contains(new Rectangle(10, 20, 30, 1), rects);    // top: full width
            Assert.Contains(new Rectangle(10, 59, 30, 1), rects);    // bottom: full width
            Assert.Contains(new Rectangle(10, 20, 1, 40), rects);    // left: full height (overlap)
            Assert.Contains(new Rectangle(39, 20, 1, 40), rects);    // right: full height (overlap)
        }

        [Fact]
        public void BorderRects_WithTwoPixelThickness_ShouldInsetBothHorizontalEdges()
        {
            var rects = ChamferedBox.BorderRects(new Rectangle(0, 0, 50, 30), 2);

            Assert.Equal(4, rects.Length);
            Assert.Contains(new Rectangle(0, 0, 50, 2), rects);      // top
            Assert.Contains(new Rectangle(0, 28, 50, 2), rects);     // bottom
            Assert.Contains(new Rectangle(0, 0, 2, 30), rects);      // left
            Assert.Contains(new Rectangle(48, 0, 2, 30), rects);     // right
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void BorderRects_WithNonPositiveThickness_ShouldClampToOnePixel(int thickness)
        {
            var rects = ChamferedBox.BorderRects(new Rectangle(5, 5, 20, 10), thickness);

            Assert.Contains(new Rectangle(5, 5, 20, 1), rects);
            Assert.Contains(new Rectangle(5, 14, 20, 1), rects);
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
