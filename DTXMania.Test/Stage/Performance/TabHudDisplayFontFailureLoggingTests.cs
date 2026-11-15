using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using RD = DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Performance;

// HPA-796 Task 3: TabHudDisplay.AcquireDrawResources reports a captured font
// acquisition failure once at Error through the optional stage-supplied
// logger, then keeps the existing Lanes fallback contract (IsReady stays
// false, display disposed). Direct font creation never goes through
// ResourceManager, so this error is not reported by the resource manager.
[Trait("Category", "Unit")]
[Collection("ManagedFont")]
public class TabHudDisplayFontFailureLoggingTests
{
    [Fact]
    public void FontAcquisitionFailure_ShouldLogErrorOnce_WithOriginalException_AndKeepLanesFallback()
    {
        // The ctor touches ManagedFont's process-global factory; pin it to the
        // pristine state (same seam the sibling ManagedFont-collection tests
        // use) so the uninitialized-GraphicsDevice path is deterministic.
        var state = ManagedFont.CaptureFontFactoryStateForTesting();
        var logger = new Mock<ILogger<TabHudDisplay>>();
        try
        {
            ManagedFont.ResetFontFactoryForTesting();
            using var display = new TabHudDisplay(
                RD.CreateUninitialized<GraphicsDevice>(),
                chart: null,
                title: "t",
                artist: "a",
                logger: logger.Object);

            // Lanes fallback contract: acquisition failed, display not ready.
            Assert.False(display.IsReady);

            logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((s, _) =>
                        s.ToString()!.Contains("failed to acquire fonts", StringComparison.Ordinal)),
                    It.Is<Exception?>(ex => ex != null),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            ManagedFont.RestoreFontFactoryStateForTesting(
                state.ContentManager, state.DefaultFont, state.LoadedFonts);
        }
    }
}
