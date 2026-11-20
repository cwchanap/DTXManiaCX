using System;
using System.Reflection;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class BackgroundRendererLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextureLoad_DisposeDuringAcquisitionReleasesReturnedTexture(bool retire)
    {
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        var value = texture.Object;
        var renderer = new BackgroundRenderer(resources.Object);
        resources.Setup(x => x.TryLoadTexture(TexturePath.PerformanceBackground, out value))
            .Callback(() => { if (retire) renderer.Dispose(); }).Returns(true);

        await renderer.LoadBackgroundAsync();

        Assert.Equal(!retire, renderer.IsReady);
        renderer.Dispose();
        renderer.Dispose();
        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public async Task TextureLoad_ThrowAfterSettingOutReleasesPartialTextureAndReportsFailure()
    {
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        var value = texture.Object;
        resources.Setup(x => x.TryLoadTexture(TexturePath.PerformanceBackground, out value))
            .Throws(new InvalidOperationException("load"));
        var renderer = new BackgroundRenderer(resources.Object);

        await renderer.LoadBackgroundAsync();

        Assert.True(renderer.LoadingFailed);
        Assert.False(renderer.IsReady);
        texture.Verify(x => x.RemoveReference(), Times.Once);
        renderer.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackPixel_DisposeDuringAcquisitionOrTextureGetterReleasesOnce(bool duringGetter)
    {
        var resources = new Mock<IResourceManager>();
        var pixel = new Mock<ITexture>();
        var renderer = new BackgroundRenderer(resources.Object);
        resources.Setup(x => x.CreateTextureFromColor(Color.White)).Returns(() =>
        {
            if (!duringGetter) renderer.Dispose();
            return pixel.Object;
        });
        pixel.SetupGet(x => x.Texture).Returns(() =>
        {
            if (duringGetter) renderer.Dispose();
            return null!;
        });

        DrawFallbackWithoutGpu(renderer);
        renderer.Dispose();

        pixel.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void FallbackPixel_ThrowingTextureGetterPreservesOriginalFailureAndReleasesPixel()
    {
        var resources = new Mock<IResourceManager>();
        var pixel = new Mock<ITexture>();
        var failure = new InvalidOperationException("pixel getter");
        resources.Setup(x => x.CreateTextureFromColor(Color.White)).Returns(pixel.Object);
        pixel.SetupGet(x => x.Texture).Throws(failure);
        pixel.Setup(x => x.RemoveReference()).Throws(new InvalidOperationException("pixel release"));
        var renderer = new BackgroundRenderer(resources.Object);

        var caught = Assert.Throws<TargetInvocationException>(() => DrawFallbackWithoutGpu(renderer));

        Assert.Same(failure, caught.InnerException);
        pixel.Verify(x => x.RemoveReference(), Times.Once);
        renderer.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_ReentrantOrThrowingFirstReleaseStillReleasesEachResourceOnce(bool throws)
    {
        var resources = new Mock<IResourceManager>();
        var background = new Mock<ITexture>();
        var pixel = new Mock<ITexture>();
        var value = background.Object;
        var renderer = new BackgroundRenderer(resources.Object);
        resources.Setup(x => x.TryLoadTexture(TexturePath.PerformanceBackground, out value)).Returns(true);
        resources.Setup(x => x.CreateTextureFromColor(Color.White)).Returns(pixel.Object);
        var failure = new InvalidOperationException("background release");
        int calls = 0;
        background.Setup(x => x.RemoveReference()).Callback(() =>
        {
            if (++calls == 1) renderer.Dispose();
            if (throws) throw failure;
        });
        pixel.Setup(x => x.RemoveReference()).Throws(new InvalidOperationException("pixel release"));
        await renderer.LoadBackgroundAsync();
        DrawFallbackWithoutGpu(renderer);

        var caught = Assert.Throws<InvalidOperationException>(() => renderer.Dispose());

        if (throws) Assert.Same(failure, caught);
        Assert.Equal(1, calls);
        pixel.Verify(x => x.RemoveReference(), Times.Once);
        renderer.Dispose();
    }

    private static void DrawFallbackWithoutGpu(BackgroundRenderer renderer)
    {
        var method = typeof(BackgroundRenderer).GetMethod("DrawFallbackBackground",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(SpriteBatch), typeof(Rectangle), typeof(float) }, null)!;
        method.Invoke(renderer, new object?[] { null, Rectangle.Empty, 1f });
    }
}
