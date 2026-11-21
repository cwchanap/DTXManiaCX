using System;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class LanesPlayfieldLifecycleTests
{
    [Fact]
    public void ConstructionFailure_ShouldReleaseAlreadyConstructedComponents()
    {
        var lanePixel = new Mock<ITexture>();
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.CreateTextureFromColor(Color.White)).Returns(lanePixel.Object);

        Assert.Throws<ArgumentNullException>(() =>
            new LanesPlayfield(null!, resources.Object, null, null, null, new ConfigData()));

        lanePixel.Verify(t => t.RemoveReference(), Times.Once);
    }
}
