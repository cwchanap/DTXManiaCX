using System.Collections.Generic;
using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// GPU-opt-in tests for the SongListDisplay borrowed-white-pixel chain (HPA-796):
    /// InitializeEnhancedRendering must lend the display's WhitePixel to its own
    /// DefaultGraphicsGenerator and, via SongBar.InitializeGraphicsGenerator, to the
    /// generator inside every cached SongBar — no SongBar may allocate its own pixel.
    /// </summary>
    [Collection("GraphicsTests")]
    public class SongListDisplayGraphicsTests : IDisposable
    {
        private readonly TestGraphicsDeviceService _graphicsService = new();

        [GraphicsFact]
        public void InitializeEnhancedRendering_ShouldLendSameWhitePixelToEachCachedSongBarGenerator()
        {
            var graphicsDevice = _graphicsService.RequiredDevice;
            using var sharedRT = new RenderTarget2D(graphicsDevice, 512, 512);
            // Stand-in for the stage-owned pixel the display borrows and must pass through.
            using var borrowedPixel = new Texture2D(graphicsDevice, 1, 1);

            using var display = new SongListDisplay { WhitePixel = borrowedPixel };

            // Seed one cached bar: production forwards through the same cache the loop reads.
            var songBar = new SongBar();
            var cache = ReflectionHelpers.GetPrivateField<Dictionary<int, SongBar>>(display, "_songBarCache");
            Assert.NotNull(cache);
            cache![0] = songBar;

            display.InitializeEnhancedRendering(graphicsDevice, new MockResourceManager(graphicsDevice), sharedRT);

            // The display's own generator borrows the same pixel...
            var displayGenerator = ReflectionHelpers.GetPrivateField<DefaultGraphicsGenerator>(display, "_graphicsGenerator");
            Assert.NotNull(displayGenerator);
            Assert.Same(borrowedPixel, ReflectionHelpers.GetPrivateField<Texture2D>(displayGenerator!, "_whitePixel"));

            // ...and the SongBar.InitializeGraphicsGenerator path hands the SAME borrowed
            // instance to the generator inside each cached SongBar (dropping or replacing
            // the forwarded pixel fails here).
            var barGenerator = ReflectionHelpers.GetPrivateField<DefaultGraphicsGenerator>(songBar, "_graphicsGenerator");
            Assert.NotNull(barGenerator);
            Assert.Same(borrowedPixel, ReflectionHelpers.GetPrivateField<Texture2D>(barGenerator!, "_whitePixel"));
        }

        public void Dispose()
        {
            _graphicsService.Dispose();
        }
    }
}
