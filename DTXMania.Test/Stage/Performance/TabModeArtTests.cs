using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class TabModeArtTests
    {
        private static Mock<ITexture> Texture(int width, int height)
        {
            var texture = new Mock<ITexture>();
            texture.SetupGet(t => t.Width).Returns(width);
            texture.SetupGet(t => t.Height).Returns(height);
            return texture;
        }

        private static SkinTheme ThemeWithAllArt() => SkinTheme.Parse(new[]
        {
            "Tab.StripTexture=Graphics/7_Tab_strip.png",
            "Tab.PanelTexture=Graphics/7_Tab_panel.png",
            "Tab.HitLineTexture=Graphics/7_Tab_hitline.png",
        });

        [Fact]
        public void Load_WhenThemeNamesNoTabArt_ShouldLoadNothing()
        {
            var resourceManager = new Mock<IResourceManager>(MockBehavior.Strict);

            var art = TabModeArt.Load(resourceManager.Object, SkinTheme.Empty);

            Assert.Null(art.Strip);
            Assert.Null(art.Panel);
            Assert.Null(art.HitLine);
        }

        [Fact]
        public void Load_WhenThemeNamesValidTextures_ShouldHoldEachSlot()
        {
            var strip = Texture(1280, 290);
            var panel = Texture(128, 128);
            var hitLine = Texture(64, 290);
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_strip.png")).Returns(strip.Object);
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_panel.png")).Returns(panel.Object);
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_hitline.png")).Returns(hitLine.Object);

            var art = TabModeArt.Load(resourceManager.Object, ThemeWithAllArt());

            Assert.Same(strip.Object, art.Strip);
            Assert.Same(panel.Object, art.Panel);
            Assert.Same(hitLine.Object, art.HitLine);
        }

        [Fact]
        public void Load_WhenTextureIsOnePixelFallback_ShouldDisposeItAndLeaveSlotEmpty()
        {
            var fallback = Texture(1, 1);
            var panel = Texture(128, 128);
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_strip.png")).Returns(fallback.Object);
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_panel.png")).Returns(panel.Object);
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_hitline.png")).Throws(new InvalidOperationException("missing"));

            var art = TabModeArt.Load(resourceManager.Object, ThemeWithAllArt());

            Assert.Null(art.Strip);
            fallback.Verify(t => t.Dispose(), Times.Once);
            Assert.Same(panel.Object, art.Panel);
            Assert.Null(art.HitLine);
        }

        [Fact]
        public void Release_ShouldRemoveEveryHeldReference()
        {
            var strip = Texture(1280, 290);
            var panel = Texture(128, 128);
            var hitLine = Texture(64, 290);
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_strip.png")).Returns(strip.Object);
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_panel.png")).Returns(panel.Object);
            resourceManager.Setup(r => r.LoadTexture("Graphics/7_Tab_hitline.png")).Returns(hitLine.Object);
            var art = TabModeArt.Load(resourceManager.Object, ThemeWithAllArt());

            art.Release();

            strip.Verify(t => t.RemoveReference(), Times.Once);
            panel.Verify(t => t.RemoveReference(), Times.Once);
            hitLine.Verify(t => t.RemoveReference(), Times.Once);
        }

        [Fact]
        public void GetNineSlice_ShouldKeepCornersAndStretchEdgesAndCenter()
        {
            var sources = new Rectangle[9];
            var destinations = new Rectangle[9];

            TabModeArt.GetNineSlice(new Rectangle(16, 12, 864, 70), 256, 256, 24, sources, destinations);

            // Quarter-size source corners: 64 px of a 256 px plate.
            Assert.Equal(new Rectangle(0, 0, 64, 64), sources[0]);
            Assert.Equal(new Rectangle(64, 64, 128, 128), sources[4]);
            Assert.Equal(new Rectangle(192, 192, 64, 64), sources[8]);

            Assert.Equal(new Rectangle(16, 12, 24, 24), destinations[0]);
            Assert.Equal(new Rectangle(40, 12, 816, 24), destinations[1]);
            Assert.Equal(new Rectangle(40, 36, 816, 22), destinations[4]);
            Assert.Equal(new Rectangle(856, 58, 24, 24), destinations[8]);
        }

        [Fact]
        public void GetNineSlice_WhenRectIsShorterThanTwoCorners_ShouldClampCornerToHalfHeight()
        {
            var sources = new Rectangle[9];
            var destinations = new Rectangle[9];

            TabModeArt.GetNineSlice(new Rectangle(195, 258, 1069, 24), 256, 256, 24, sources, destinations);

            Assert.Equal(new Rectangle(195, 258, 12, 12), destinations[0]);
            Assert.Equal(0, destinations[4].Height);
            Assert.Equal(new Rectangle(1252, 270, 12, 12), destinations[8]);
        }
    }
}
