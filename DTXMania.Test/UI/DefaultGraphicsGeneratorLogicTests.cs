using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class DefaultGraphicsGeneratorLogicTests
{
    [Fact]
    public void Constructor_WhenGraphicsDeviceIsNull_ShouldThrowArgumentNullException()
    {
        var renderTarget = (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D));

        Assert.Throws<ArgumentNullException>(() => new DefaultGraphicsGenerator(null!, renderTarget));
    }

    [Fact]
    public void Constructor_WhenRenderTargetIsNull_ShouldThrowArgumentNullException()
    {
        var graphicsDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

        Assert.Throws<ArgumentNullException>(() => new DefaultGraphicsGenerator(graphicsDevice, null!));
    }

    [Fact]
    public void GraphicsDevice_ShouldReturnStoredGraphicsDevice()
    {
        var graphicsDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));
        var generator = CreateGenerator(graphicsDevice);

        Assert.Same(graphicsDevice, generator.GraphicsDevice);
    }

    [Fact]
    public void GenerateSongBarBackground_WhenTextureCached_ShouldReturnCachedTexture()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        GetTextureCache(generator)["SongBar_120x40_True_False"] = texture.Object;

        var result = generator.GenerateSongBarBackground(120, 40, isSelected: true, isCenter: false);

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void GenerateClearLamp_WhenTextureCached_ShouldReturnCachedTexture()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        GetTextureCache(generator)["ClearLamp_3_True"] = texture.Object;

        var result = generator.GenerateClearLamp(3, hasCleared: true);

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void GenerateEnhancedClearLamp_WhenTextureCached_ShouldReturnCachedTexture()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        GetTextureCache(generator)["EnhancedClearLamp_2_FullCombo"] = texture.Object;

        var result = generator.GenerateEnhancedClearLamp(2, ClearStatus.FullCombo);

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void GenerateBarTypeBackground_WhenTextureCached_ShouldReturnCachedTexture()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        GetTextureCache(generator)["BarType_128x36_Box_False_True"] = texture.Object;

        var result = generator.GenerateBarTypeBackground(128, 36, BarType.Box, isSelected: false, isCenter: true);

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void GeneratePanelBackground_WhenTextureCached_ShouldReturnCachedTexture()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        GetTextureCache(generator)["Panel_200x80_True"] = texture.Object;

        var result = generator.GeneratePanelBackground(200, 80, withBorder: true);

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void GenerateBPMBackground_WhenTextureCached_ShouldReturnCachedTexture()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        GetTextureCache(generator)["BPMBackground_187x67_True"] = texture.Object;

        var result = generator.GenerateBPMBackground(187, 67, withLabels: true);

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void GenerateButton_WhenTextureCached_ShouldReturnCachedTexture()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        GetTextureCache(generator)["Button_96x28_True"] = texture.Object;

        var result = generator.GenerateButton(96, 28, isPressed: true);

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void Dispose_WhenTexturesAreCached_ShouldDisposeTexturesClearCacheAndMarkDisposed()
    {
        var generator = CreateGenerator();
        var firstTexture = CreateTextureMock();
        var secondTexture = CreateTextureMock();
        var textures = GetTextureCache(generator);
        textures["first"] = firstTexture.Object;
        textures["second"] = secondTexture.Object;

        generator.Dispose();

        firstTexture.Verify(x => x.Dispose(), Times.Once);
        secondTexture.Verify(x => x.Dispose(), Times.Once);
        Assert.Empty(textures);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(generator, "_disposed"));
    }

    [Fact]
    public void Dispose_WhenAlreadyDisposed_ShouldReturnWithoutTouchingCachedTextures()
    {
        var generator = CreateGenerator();
        var texture = CreateTextureMock();
        var textures = GetTextureCache(generator);
        textures["cached"] = texture.Object;
        ReflectionHelpers.SetPrivateField(generator, "_disposed", true);

        generator.Dispose();

        texture.Verify(x => x.Dispose(), Times.Never);
        Assert.Single(textures);
    }

    private static DefaultGraphicsGenerator CreateGenerator(GraphicsDevice? graphicsDevice = null)
    {
        var generator = (DefaultGraphicsGenerator)RuntimeHelpers.GetUninitializedObject(typeof(DefaultGraphicsGenerator));
        ReflectionHelpers.SetPrivateField(generator, "_graphicsDevice", graphicsDevice ?? (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice)));
        ReflectionHelpers.SetPrivateField(generator, "_generatedTextures", new Dictionary<string, ITexture>());
        ReflectionHelpers.SetPrivateField(generator, "_renderTarget", (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D)));
        ReflectionHelpers.SetPrivateField(generator, "_spriteBatch", null);
        ReflectionHelpers.SetPrivateField(generator, "_whitePixel", null);
        ReflectionHelpers.SetPrivateField(generator, "_disposed", false);
        return generator;
    }

    private static Dictionary<string, ITexture> GetTextureCache(DefaultGraphicsGenerator generator)
    {
        return ReflectionHelpers.GetPrivateField<Dictionary<string, ITexture>>(generator, "_generatedTextures")!;
    }

    private static Mock<ITexture> CreateTextureMock()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.SourcePath).Returns("generated");
        return texture;
    }
}
