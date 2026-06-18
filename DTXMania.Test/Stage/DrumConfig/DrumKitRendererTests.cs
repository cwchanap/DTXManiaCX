using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumKitRendererTests
    {
        // The renderer fits each piece (a photorealistic 3D render, or a fallback filled disc when
        // the skin lacks the art) into its zone and draws a yellow glow behind a piece that is
        // selected/focused/hovered. BodyColorFor is the pure color-selection helper behind the
        // fallback-disc tint; the rest of the renderer is graphics.
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

        // ---- FitCentered (pure static) ----

        private static Rectangle FitCentered(Rectangle bounds, int srcWidth, int srcHeight)
        {
            var method = typeof(DrumKitRenderer).GetMethod("FitCentered",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (Rectangle)method!.Invoke(null, new object[] { bounds, srcWidth, srcHeight })!;
        }

        [Fact]
        public void FitCentered_TallerSource_LetterboxesHorizontallyAndCenters()
        {
            var bounds = new Rectangle(0, 0, 100, 100);

            var rect = FitCentered(bounds, srcWidth: 50, srcHeight: 100);

            // scale = min(100/50, 100/100) = 1 -> w=50, h=100, centered on (50,50).
            Assert.Equal(new Rectangle(25, 0, 50, 100), rect);
        }

        [Fact]
        public void FitCentered_WiderSource_LetterboxesVerticallyAndCenters()
        {
            var bounds = new Rectangle(0, 0, 100, 100);

            var rect = FitCentered(bounds, srcWidth: 200, srcHeight: 100);

            // scale = min(100/200, 100/100) = 0.5 -> w=100, h=50, centered on (50,50).
            Assert.Equal(new Rectangle(0, 25, 100, 50), rect);
        }

        [Fact]
        public void FitCentered_NonPositiveSource_ReturnsBoundsUnchanged()
        {
            var bounds = new Rectangle(10, 20, 100, 80);

            // Degenerate source dimensions must short-circuit to the bounds (no div-by-zero).
            Assert.Equal(bounds, FitCentered(bounds, srcWidth: 0, srcHeight: 100));
            Assert.Equal(bounds, FitCentered(bounds, srcWidth: 100, srcHeight: -5));
        }

        [Fact]
        public void FitCentered_NeverExceedsBounds()
        {
            var bounds = new Rectangle(0, 0, 64, 64);

            // The fitted rect must lie entirely within the bounds for any aspect.
            foreach (var (w, h) in new[] { (32, 64), (64, 32), (128, 128), (200, 50) })
            {
                var rect = FitCentered(bounds, w, h);
                Assert.True(bounds.Contains(rect));
                Assert.InRange(rect.Width, 1, bounds.Width);
                Assert.InRange(rect.Height, 1, bounds.Height);
            }
        }

        // ---- ShapeTexture (instance switch) ----

        private static DrumKitRenderer NewRendererWithShapes(
            out ITexture cymbal, out ITexture drum, out ITexture kick,
            out ITexture pedal, out ITexture hihat)
        {
            var renderer = ReflectionHelpers.CreateUninitialized<DrumKitRenderer>();
            cymbal = new Mock<ITexture>().Object;
            drum = new Mock<ITexture>().Object;
            kick = new Mock<ITexture>().Object;
            pedal = new Mock<ITexture>().Object;
            hihat = new Mock<ITexture>().Object;
            ReflectionHelpers.SetPrivateField(renderer, "_cymbal", cymbal);
            ReflectionHelpers.SetPrivateField(renderer, "_drum", drum);
            ReflectionHelpers.SetPrivateField(renderer, "_kick", kick);
            ReflectionHelpers.SetPrivateField(renderer, "_pedal", pedal);
            ReflectionHelpers.SetPrivateField(renderer, "_hihat", hihat);
            return renderer;
        }

        private static ITexture? ShapeTexture(DrumKitRenderer renderer, DrumZoneShape shape)
        {
            var method = typeof(DrumKitRenderer).GetMethod("ShapeTexture",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (ITexture?)method!.Invoke(renderer, new object[] { shape });
        }

        [Fact]
        public void ShapeTexture_MapsEachShapeToItsTexture()
        {
            var renderer = NewRendererWithShapes(out var cymbal, out var drum, out var kick,
                out var pedal, out var hihat);

            Assert.Same(cymbal, ShapeTexture(renderer, DrumZoneShape.Cymbal));
            Assert.Same(drum, ShapeTexture(renderer, DrumZoneShape.Drum));
            Assert.Same(kick, ShapeTexture(renderer, DrumZoneShape.Kick));
            Assert.Same(pedal, ShapeTexture(renderer, DrumZoneShape.Pedal));
            Assert.Same(hihat, ShapeTexture(renderer, DrumZoneShape.HiHatPedal));
        }

        [Fact]
        public void ShapeTexture_UnknownShape_ReturnsNull()
        {
            var renderer = NewRendererWithShapes(out _, out _, out _, out _, out _);

            Assert.Null(ShapeTexture(renderer, (DrumZoneShape)999));
        }

        // ---- TryLoad (static, best-effort texture load) ----

        private static ITexture? TryLoad(IResourceManager? resourceManager, string path)
        {
            var method = typeof(DrumKitRenderer).GetMethod("TryLoad",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (ITexture?)method!.Invoke(null, new object?[] { resourceManager, path });
        }

        [Fact]
        public void TryLoad_WithNullResourceManager_ReturnsNull()
        {
            Assert.Null(TryLoad(null, TexturePath.DrumPadDrum));
        }

        [Fact]
        public void TryLoad_WhenResourceManagerThrows_ReturnsNull()
        {
            var resources = new Mock<IResourceManager>();
            resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Throws(new System.IO.IOException());

            // Missing/invalid art is non-fatal: the zone falls back to a plain disc.
            Assert.Null(TryLoad(resources.Object, TexturePath.DrumPadDrum));
        }

        [Fact]
        public void TryLoad_WhenResourceManagerReturnsTexture_ReturnsIt()
        {
            var resources = new Mock<IResourceManager>();
            var texture = new Mock<ITexture>().Object;
            resources.Setup(r => r.LoadTexture(TexturePath.DrumPadCymbal)).Returns(texture);

            Assert.Same(texture, TryLoad(resources.Object, TexturePath.DrumPadCymbal));
        }

        // ---- LaneHighlights (the bundled highlight-lane draw parameter) ----

        [Fact]
        public void LaneHighlights_Default_HighlightsNothing()
        {
            var highlights = new LaneHighlights();

            // All three default to -1 (none), so no real lane (0..9) is highlighted.
            for (int lane = 0; lane < 10; lane++)
                Assert.False(highlights.IsHighlighted(lane));
        }

        [Fact]
        public void LaneHighlights_EachNamedProperty_DrivesHighlight()
        {
            // Named init properties — the point of the struct is that each lane is set by name,
            // not by position, so they cannot be transposed at the call site.
            Assert.True(new LaneHighlights { SelectedLane = 4 }.IsHighlighted(4));
            Assert.True(new LaneHighlights { FocusedLane = 7 }.IsHighlighted(7));
            Assert.True(new LaneHighlights { HoveredLane = 0 }.IsHighlighted(0));
        }

        [Fact]
        public void LaneHighlights_MultipleSet_AllHighlight()
        {
            var highlights = new LaneHighlights { SelectedLane = 2, FocusedLane = 5, HoveredLane = 8 };

            Assert.True(highlights.IsHighlighted(2));
            Assert.True(highlights.IsHighlighted(5));
            Assert.True(highlights.IsHighlighted(8));
            // A lane matching none of the three is not highlighted.
            Assert.False(highlights.IsHighlighted(1));
        }
    }
}
