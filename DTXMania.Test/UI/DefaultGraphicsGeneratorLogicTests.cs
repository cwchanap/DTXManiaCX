using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class DefaultGraphicsGeneratorLogicTests
{
    private sealed record DrawCall(Rectangle Destination, Color Color);

    private sealed class TestableDefaultGraphicsGenerator : DefaultGraphicsGenerator
    {
        public List<Color> ClearColors { get; } = new();
        public List<DrawCall> DrawCalls { get; } = new();
        public int BeginCount { get; private set; }
        public int EndCount { get; private set; }
        public int SetRenderTargetCount { get; private set; }
        public int RestoreRenderTargetsCount { get; private set; }
        public Func<string, ITexture>? CreateGeneratedTextureHandler { get; set; }
        public List<(string SourcePath, int Width, int Height)> CreateGeneratedTextureCalls { get; } = new();

        public TestableDefaultGraphicsGenerator()
            : base(
                (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice)),
                (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D)))
        {
        }

        protected override void ClearGraphics(Color color) => ClearColors.Add(color);

        protected override void BeginSpriteBatch() => BeginCount++;

        protected override void EndSpriteBatch() => EndCount++;

        protected override void DrawSolidRectangle(Rectangle destination, Color color)
            => DrawCalls.Add(new DrawCall(destination, color));

        protected override RenderTargetBinding[] SetRenderTarget()
        {
            SetRenderTargetCount++;
            return Array.Empty<RenderTargetBinding>();
        }

        protected override void RestoreRenderTargets(RenderTargetBinding[] previousTargets)
        {
            RestoreRenderTargetsCount++;
        }

        protected override ITexture CreateGeneratedTexture(string sourcePath, int width, int height)
        {
            CreateGeneratedTextureCalls.Add((sourcePath, width, height));
            return CreateGeneratedTextureHandler?.Invoke(sourcePath)
                ?? throw new InvalidOperationException("CreateGeneratedTextureHandler was not configured.");
        }
    }

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

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    public void GenerateMethods_WhenDimensionsAreNonPositive_ShouldThrowArgumentOutOfRangeException(int width, int height)
    {
        var generator = CreateTestableGenerator();

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GenerateSongBarBackground(width, height, isSelected: false, isCenter: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GenerateBarTypeBackground(width, height, BarType.Score, isSelected: false, isCenter: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GeneratePanelBackground(width, height, withBorder: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GenerateBPMBackground(width, height, withLabels: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GenerateButton(width, height, isPressed: false));
    }

    [Fact]
    public void GenerateSongBarBackground_WhenCenterNotCached_ShouldDrawGradientCenterBordersAndCacheTexture()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_SongBar_4x3");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateSongBarBackground(4, 3, isSelected: false, isCenter: true);

        Assert.Same(texture.Object, result);
        Assert.Equal([Color.Transparent], generator.ClearColors);
        Assert.Equal(1, generator.BeginCount);
        Assert.Equal(1, generator.EndCount);
        Assert.Equal(1, generator.SetRenderTargetCount);
        Assert.Equal(1, generator.RestoreRenderTargetsCount);
        Assert.Equal("Generated_SongBar_4x3", result.SourcePath);
        Assert.Equal(5, generator.DrawCalls.Count);
        Assert.Equal(new Rectangle(0, 0, 4, 2), generator.DrawCalls[3].Destination);
        Assert.Equal(Color.Yellow, generator.DrawCalls[3].Color);
        Assert.Equal(new Rectangle(0, 1, 4, 2), generator.DrawCalls[4].Destination);
        Assert.Equal(Color.Yellow, generator.DrawCalls[4].Color);
        Assert.Same(texture.Object, GetTextureCache(generator)["SongBar_4x3_False_True"]);
        Assert.Single(generator.CreateGeneratedTextureCalls);
        Assert.Equal(("Generated_SongBar_4x3", 4, 3), generator.CreateGeneratedTextureCalls[0]);
    }

    [Fact]
    public void GenerateSongBarBackground_WhenSelectedNotCached_ShouldDrawWhiteBorders()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_SongBar_5x2");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateSongBarBackground(5, 2, isSelected: true, isCenter: false);

        Assert.Same(texture.Object, result);
        Assert.Equal(4, generator.DrawCalls.Count);
        Assert.Equal(new Rectangle(0, 0, 5, 1), generator.DrawCalls[2].Destination);
        Assert.Equal(Color.White, generator.DrawCalls[2].Color);
        Assert.Equal(new Rectangle(0, 1, 5, 1), generator.DrawCalls[3].Destination);
        Assert.Equal(Color.White, generator.DrawCalls[3].Color);
    }

    [Fact]
    public void GenerateClearLamp_WhenNotCleared_ShouldUseGrayBorderAndCacheTexture()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_ClearLamp_3_False");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateClearLamp(3, hasCleared: false);

        Assert.Same(texture.Object, result);
        Assert.Equal("Generated_ClearLamp_3_False", result.SourcePath);
        Assert.Equal(1, generator.SetRenderTargetCount);
        Assert.Equal(1, generator.RestoreRenderTargetsCount);
        Assert.Equal(DTXManiaVisualTheme.Layout.ClearLampHeight + 4, generator.DrawCalls.Count);
        Assert.All(generator.DrawCalls[^4..], drawCall => Assert.Equal(Color.Gray, drawCall.Color));
        Assert.Same(texture.Object, GetTextureCache(generator)["ClearLamp_3_False"]);
        Assert.Single(generator.CreateGeneratedTextureCalls);
        Assert.Equal(DTXManiaVisualTheme.Layout.ClearLampWidth, generator.CreateGeneratedTextureCalls[0].Width);
        Assert.Equal(DTXManiaVisualTheme.Layout.ClearLampHeight, generator.CreateGeneratedTextureCalls[0].Height);
    }

    [Fact]
    public void GenerateEnhancedClearLamp_WhenNotPlayed_ShouldSkipBorder()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_EnhancedClearLamp_1_NotPlayed");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateEnhancedClearLamp(1, ClearStatus.NotPlayed);

        Assert.Same(texture.Object, result);
        Assert.Equal("Generated_EnhancedClearLamp_1_NotPlayed", result.SourcePath);
        Assert.Equal(24, generator.DrawCalls.Count);
        Assert.Same(texture.Object, GetTextureCache(generator)["EnhancedClearLamp_1_NotPlayed"]);
        Assert.Single(generator.CreateGeneratedTextureCalls);
        Assert.Equal(("Generated_EnhancedClearLamp_1_NotPlayed", 8, 24), generator.CreateGeneratedTextureCalls[0]);
    }

    [Fact]
    public void GenerateEnhancedClearLamp_WhenStatusUnknown_ShouldUseDefaultBranchAndDrawBorder()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_EnhancedClearLamp_9_999");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateEnhancedClearLamp(9, (ClearStatus)999);

        Assert.Same(texture.Object, result);
        Assert.Equal("Generated_EnhancedClearLamp_9_999", result.SourcePath);
        Assert.Equal(28, generator.DrawCalls.Count);
        Assert.Equal(Color.White * 0.8f, generator.DrawCalls[^1].Color);
    }

    [Fact]
    public void GenerateBarTypeBackground_WhenBoxSelected_ShouldDrawBorderAndFolderIndicator()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_BarType_Box_10x8");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateBarTypeBackground(10, 8, BarType.Box, isSelected: true, isCenter: false);

        Assert.Same(texture.Object, result);
        Assert.Equal("Generated_BarType_Box_10x8", result.SourcePath);
        Assert.Equal(13, generator.DrawCalls.Count);
        Assert.Equal(Color.Cyan * 0.7f, generator.DrawCalls[^1].Color);
        Assert.Equal(new Rectangle(2, 2, 4, 4), generator.DrawCalls[^1].Destination);
        Assert.Single(generator.CreateGeneratedTextureCalls);
        Assert.Equal(("Generated_BarType_Box_10x8", 10, 8), generator.CreateGeneratedTextureCalls[0]);
    }

    [Fact]
    public void GenerateBarTypeBackground_WhenOtherCentered_ShouldDrawCenterBorderAndSpecialIndicator()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_BarType_Other_12x8");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateBarTypeBackground(12, 8, BarType.Other, isSelected: false, isCenter: true);

        Assert.Same(texture.Object, result);
        Assert.Equal("Generated_BarType_Other_12x8", result.SourcePath);
        Assert.Equal(13, generator.DrawCalls.Count);
        Assert.Equal(Color.Magenta * 0.8f, generator.DrawCalls[^1].Color);
        Assert.Equal(new Rectangle(4, 2, 4, 4), generator.DrawCalls[^1].Destination);
        Assert.Equal(Color.Yellow, generator.DrawCalls[8].Color);
    }

    [Fact]
    public void GeneratePanelBackground_WhenBorderDisabled_ShouldOnlyDrawBackgroundAndCacheTexture()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_Panel_20x8");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GeneratePanelBackground(20, 8, withBorder: false);

        Assert.Same(texture.Object, result);
        Assert.Equal("Generated_Panel_20x8", result.SourcePath);
        Assert.Single(generator.DrawCalls);
        Assert.Equal(new Rectangle(0, 0, 20, 8), generator.DrawCalls[0].Destination);
        Assert.Single(generator.CreateGeneratedTextureCalls);
        Assert.Equal(("Generated_Panel_20x8", 20, 8), generator.CreateGeneratedTextureCalls[0]);
    }

        [Fact]
        public void GenerateBPMBackground_WhenLabelsEnabled_ShouldDrawPlaceholderAreas()
        {
            var generator = CreateTestableGenerator();
            var texture = CreateTextureMock("Generated_BPMBackground_8x8");
            generator.CreateGeneratedTextureHandler = _ => texture.Object;

            var result = generator.GenerateBPMBackground(8, 8, withLabels: true);

            Assert.Same(texture.Object, result);
            Assert.Equal("Generated_BPMBackground_8x8", result.SourcePath);
            Assert.Equal(14, generator.DrawCalls.Count);
            Assert.Equal(new Rectangle(5, 5, 0, 0), generator.DrawCalls[^2].Destination);
            Assert.Equal(new Rectangle(5, 4, 0, 0), generator.DrawCalls[^1].Destination);
            Assert.Single(generator.CreateGeneratedTextureCalls);
            Assert.Equal(("Generated_BPMBackground_8x8", 8, 8), generator.CreateGeneratedTextureCalls[0]);
        }

    [Fact]
    public void GenerateButton_WhenPressed_ShouldDrawGradientAndBorder()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_Button_6x3");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        var result = generator.GenerateButton(6, 3, isPressed: true);

        Assert.Same(texture.Object, result);
        Assert.Equal("Generated_Button_6x3", result.SourcePath);
        Assert.Equal(7, generator.DrawCalls.Count);
        Assert.Equal(Color.White, generator.DrawCalls[^1].Color);
    }

    [Theory]
    [InlineData(187, 67, "BPMBackground")]
    [InlineData(800, 48, "SongBar")]
    [InlineData(640, 96, "BarType")]
    [InlineData(200, 80, "Panel")]
    [InlineData(7, 41, "ClearLamp")]
    public void CreateGeneratedTexture_ShouldPassRequestedDimensions_NotRenderTargetSize(int width, int height, string method)
    {
        // Verifies the fix for the bug where CreateGeneratedTexture copied the
        // full 1024x1024 render target instead of cropping to requested size.
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock($"Generated_{method}_{width}x{height}");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        switch (method)
        {
            case "SongBar":
                generator.GenerateSongBarBackground(width, height);
                break;
            case "BarType":
                generator.GenerateBarTypeBackground(width, height, BarType.Score);
                break;
            case "Panel":
                generator.GeneratePanelBackground(width, height);
                break;
            case "BPMBackground":
                generator.GenerateBPMBackground(width, height);
                break;
            case "ClearLamp":
                // ClearLamp uses fixed dimensions from layout, not caller-specified
                generator.GenerateClearLamp(0);
                return;
        }

        Assert.Single(generator.CreateGeneratedTextureCalls);
        Assert.Equal(width, generator.CreateGeneratedTextureCalls[0].Width);
        Assert.Equal(height, generator.CreateGeneratedTextureCalls[0].Height);
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

    [Fact]
    public void PreGenerateCommonTextures_WhenPanelDimensionsProvided_ShouldCachePanelBackground()
    {
        var generator = CreateTestableGenerator();
        var texture = CreateTextureMock("Generated_Panel_580x320");
        generator.CreateGeneratedTextureHandler = _ => texture.Object;

        generator.PreGenerateCommonTextures(panelWidth: 580, panelHeight: 320, barWidth: 0, barHeight: 0);

        // Only panel background should be generated (bar dimensions are 0)
        Assert.Single(generator.CreateGeneratedTextureCalls);
        Assert.Equal(("Generated_Panel_580x320", 580, 320), generator.CreateGeneratedTextureCalls[0]);
        Assert.Equal(1, generator.SetRenderTargetCount);
        Assert.Equal(1, generator.RestoreRenderTargetsCount);

        // Verify cached so subsequent draw-time calls don't trigger additional SetRenderTarget
        var countBefore = generator.SetRenderTargetCount;
        var cachedResult = generator.GeneratePanelBackground(580, 320, true);
        Assert.Same(texture.Object, cachedResult);
        Assert.Equal(countBefore, generator.SetRenderTargetCount);
    }

    [Fact]
    public void PreGenerateCommonTextures_WhenBarDimensionsProvided_ShouldCacheAllBarTypesAndStates()
    {
        var generator = CreateTestableGenerator();
        var textureIdx = 0;
        generator.CreateGeneratedTextureHandler = _ =>
        {
            var t = CreateTextureMock($"Generated_{textureIdx++}");
            return t.Object;
        };

        generator.PreGenerateCommonTextures(panelWidth: 0, panelHeight: 0, barWidth: 510, barHeight: 48);

        // 3 BarType values x 4 (isSelected, isCenter) states = 12 bar textures, no panel
        Assert.Equal(12, generator.CreateGeneratedTextureCalls.Count);
        Assert.All(generator.CreateGeneratedTextureCalls, call =>
        {
            Assert.Equal(510, call.Width);
            Assert.Equal(48, call.Height);
        });
        Assert.Equal(12, generator.SetRenderTargetCount);

        // Verify subsequent draw-time calls hit cache
        var countBefore = generator.SetRenderTargetCount;
        var cachedResult = generator.GenerateBarTypeBackground(510, 48, BarType.Score, false, false);
        Assert.NotNull(cachedResult);
        Assert.Equal(countBefore, generator.SetRenderTargetCount);
    }

    [Fact]
    public void PreGenerateCommonTextures_WhenBothDimensionsProvided_ShouldGenerateAllTextures()
    {
        var generator = CreateTestableGenerator();
        var textureIdx = 0;
        generator.CreateGeneratedTextureHandler = _ =>
        {
            var t = CreateTextureMock($"Generated_{textureIdx++}");
            return t.Object;
        };

        generator.PreGenerateCommonTextures(panelWidth: 580, panelHeight: 320, barWidth: 510, barHeight: 48);

        // 1 panel + 3 BarType x 4 (isSelected, isCenter) states = 13 total
        Assert.Equal(13, generator.CreateGeneratedTextureCalls.Count);
        Assert.Equal(13, generator.SetRenderTargetCount);
    }

    [Fact]
    public void PreGenerateCommonTextures_WhenAllDimensionsZero_ShouldNotGenerateAnything()
    {
        var generator = CreateTestableGenerator();
        generator.CreateGeneratedTextureHandler = _ => CreateTextureMock().Object;

        generator.PreGenerateCommonTextures(panelWidth: 0, panelHeight: 0, barWidth: 0, barHeight: 0);

        Assert.Empty(generator.CreateGeneratedTextureCalls);
        Assert.Equal(0, generator.SetRenderTargetCount);
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

    private static TestableDefaultGraphicsGenerator CreateTestableGenerator()
    {
        var generator = (TestableDefaultGraphicsGenerator)RuntimeHelpers.GetUninitializedObject(typeof(TestableDefaultGraphicsGenerator));
        ReflectionHelpers.SetPrivateField(generator, "_graphicsDevice", (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice)));
        ReflectionHelpers.SetPrivateField(generator, "_generatedTextures", new Dictionary<string, ITexture>());
        ReflectionHelpers.SetPrivateField(generator, "_renderTarget", (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D)));
        ReflectionHelpers.SetPrivateField(generator, "_spriteBatch", null);
        ReflectionHelpers.SetPrivateField(generator, "_whitePixel", null);
        ReflectionHelpers.SetPrivateField(generator, "_disposed", false);
        SetAutoProperty(generator, "ClearColors", new List<Color>());
        SetAutoProperty(generator, "DrawCalls", new List<DrawCall>());
        SetAutoProperty(generator, "CreateGeneratedTextureCalls", new List<(string, int, int)>());
        return generator;
    }

    private static Dictionary<string, ITexture> GetTextureCache(DefaultGraphicsGenerator generator)
    {
        return ReflectionHelpers.GetPrivateField<Dictionary<string, ITexture>>(generator, "_generatedTextures")!;
    }

    private static Mock<ITexture> CreateTextureMock(string sourcePath = "generated")
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.SourcePath).Returns(sourcePath);
        return texture;
    }

    private static void SetAutoProperty(object target, string propertyName, object value)
    {
        var field = target.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
