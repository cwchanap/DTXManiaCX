using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class NxAttackEffectManagerTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 3, 450, 750)]
    [InlineData(9, 11, 1650, 1350)]
    public void GetCombinedSparkSource_ShouldUseLaneRowsAndFrameColumns(int lane, int frame, int x, int y)
    {
        var source = NxAttackEffectManager.GetCombinedSparkSource(lane, frame);

        Assert.Equal(new Rectangle(x, y, 150, 150), source);
    }

    [Theory]
    [InlineData(5, 0, 720, 0, 35)]
    [InlineData(0, 0, 720, 540, 37)]
    [InlineData(0, 1, 720, 577, 37)]
    [InlineData(3, 0, 720, 128, 32)]
    [InlineData(8, 0, 720, 70, 29)]
    [InlineData(9, 0, 720, 360, 37)]
    [InlineData(2, 1, 718, 689, 29)]
    public void GetChipFragmentSource_ShouldUseDrumChipSpriteSheetColumns(
        int lane,
        int side,
        int sheetWidth,
        int expectedX,
        int expectedWidth)
    {
        var source = GetChipFragmentSource(lane, side, sheetWidth);

        Assert.Equal(new Rectangle(expectedX, 640, expectedWidth, 64), source);
    }

    [Theory]
    [InlineData(703, true)]
    [InlineData(704, false)]
    public void GetChipFragmentSource_ShouldValidateSheetHeight(int sheetHeight, bool shouldBeEmpty)
    {
        var source = GetChipFragmentSource(lane: 2, side: 1, sheetWidth: 718, sheetHeight);

        Assert.Equal(shouldBeEmpty, source == Rectangle.Empty);
    }

    [Theory]
    [InlineData(JudgementType.Perfect, true)]
    [InlineData(JudgementType.Great, true)]
    [InlineData(JudgementType.Good, true)]
    [InlineData(JudgementType.Poor, true)]
    [InlineData(JudgementType.Miss, false)]
    public void Spawn_ShouldCreatePrimarySparkOnlyForHitJudgements(JudgementType judgementType, bool shouldSpawn)
    {
        var manager = CreateManager(combinedAvailable: true);

        manager.Spawn(3, judgementType);

        Assert.Equal(shouldSpawn ? 1 : 0, manager.ActivePrimarySparkCountForTesting);
    }

    [Fact]
    public void Spawn_WhenSameLaneAlreadyActive_ShouldRestartPrimarySpark()
    {
        var manager = CreateManager(combinedAvailable: true);
        manager.Spawn(2, JudgementType.Perfect);
        manager.Update(0.09);

        manager.Spawn(2, JudgementType.Great);

        var spark = Assert.Single(manager.ActivePrimarySparksForTesting.Values);
        Assert.Equal(0, spark.FrameIndex);
        Assert.Equal(JudgementType.Great, spark.JudgementType);
    }

    [Fact]
    public void Spawn_WithCombinedSheet_ShouldCreateSecondaryParticles()
    {
        var manager = CreateManager(combinedAvailable: true, starsAvailable: true, chipTextureAvailable: true, waveAvailable: true);

        manager.Spawn(0, JudgementType.Perfect);

        Assert.Equal(1, manager.ActivePrimarySparkCountForTesting);
        Assert.True(manager.ActiveParticleCountForTesting > 0);
    }

    [Fact]
    public void Spawn_WhenChipTextureTooNarrowForFragment_ShouldSkipChipFragments()
    {
        var manager = CreateManager(
            combinedAvailable: false,
            chipTextureAvailable: true,
            chipTextureWidth: 7,
            settings: new NxAttackEffectSettings
            {
                ChipFragmentCount = 2
            });

        manager.Spawn(2, JudgementType.Perfect);

        Assert.Equal(0, manager.ActiveParticleCountForTesting);
    }

    [Fact]
    public void Spawn_WhenChipTextureTooShortForFragment_ShouldSkipChipFragments()
    {
        var manager = CreateManager(
            combinedAvailable: false,
            chipTextureAvailable: true,
            chipTextureWidth: 718,
            chipTextureHeight: 703,
            settings: new NxAttackEffectSettings
            {
                ChipFragmentCount = 2
            });

        manager.Spawn(2, JudgementType.Perfect);

        Assert.Equal(0, manager.ActiveParticleCountForTesting);
    }

    [Fact]
    public void Constructor_WhenCombinedSheetMissing_ShouldUsePerLaneFallbackTexture()
    {
        var manager = CreateManager(combinedAvailable: false, perLaneFallbackAvailable: true);

        manager.Spawn(0, JudgementType.Perfect);

        var spark = Assert.Single(manager.ActivePrimarySparksForTesting.Values);
        Assert.False(spark.UsesCombinedSheet);
    }

    [Fact]
    public void Update_AfterPrimarySparkDuration_ShouldExpireSpark()
    {
        var manager = CreateManager(combinedAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);

        manager.Update(1.0);

        Assert.Equal(0, manager.ActivePrimarySparkCountForTesting);
    }

    [Fact]
    public void Dispose_ShouldReleaseLoadedTextures()
    {
        var texture = CreateTexture(width: 1800, height: 1650);
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipFireCombined)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipFireCombined)).Returns(texture.Object);

        var manager = new NxAttackEffectManager(resourceManager.Object);

        manager.Dispose();

        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Constructor_WhenCombinedSheetInvalid_ShouldReleaseItOnceAndUseFallback()
    {
        var invalidCombinedTexture = CreateTexture(width: 1799, height: 1499);
        var fallbackTexture = CreateTexture(width: 128, height: 128);
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipFireCombined)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipFireCombined)).Returns(invalidCombinedTexture.Object);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.GetDrumChipFireLanePath(0))).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.GetDrumChipFireLanePath(0))).Returns(fallbackTexture.Object);

        var manager = new NxAttackEffectManager(resourceManager.Object);

        invalidCombinedTexture.Verify(x => x.RemoveReference(), Times.Once);

        manager.Spawn(0, JudgementType.Perfect);
        var spark = Assert.Single(manager.ActivePrimarySparksForTesting.Values);
        Assert.False(spark.UsesCombinedSheet);

        manager.Dispose();

        invalidCombinedTexture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Dispose_WhenCalledTwice_ShouldReleaseLoadedTexturesOnce()
    {
        var texture = CreateTexture(width: 1800, height: 1650);
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipFireCombined)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipFireCombined)).Returns(texture.Object);

        var manager = new NxAttackEffectManager(resourceManager.Object);

        manager.Dispose();
        manager.Dispose();

        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Draw_Particles_ShouldUseCenteredRotationOrigins()
    {
        var combinedTexture = CreateTexture(width: 1800, height: 1650);
        var starTexture = CreateTexture(width: 32, height: 32);
        var chipTexture = CreateTexture(width: 718, height: 776);
        var waveTexture = CreateTexture(width: 64, height: 64);
        var starDraws = new List<(Rectangle Destination, Vector2 Origin)>();
        var chipDraws = new List<(Rectangle Destination, Rectangle? Source, Vector2 Origin)>();
        var waveDraws = new List<(Rectangle Destination, Vector2 Origin)>();
        var originPosition = PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(0);
        var expectedDestinationOrigin = new Point(
            (int)MathF.Round(originPosition.X),
            (int)MathF.Round(originPosition.Y));

        starTexture.Setup(x => x.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Callback<SpriteBatch, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float>(
                (spriteBatch, destination, source, color, rotation, origin, effects, layerDepth) =>
                    starDraws.Add((destination, origin)));
        chipTexture.Setup(x => x.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Callback<SpriteBatch, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float>(
                (spriteBatch, destination, source, color, rotation, origin, effects, layerDepth) =>
                    chipDraws.Add((destination, source, origin)));
        waveTexture.Setup(x => x.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Callback<SpriteBatch, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float>(
                (spriteBatch, destination, source, color, rotation, origin, effects, layerDepth) =>
                    waveDraws.Add((destination, origin)));

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipFireCombined)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipFireCombined)).Returns(combinedTexture.Object);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.GetDrumChipStarLanePath(0))).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.GetDrumChipStarLanePath(0))).Returns(starTexture.Object);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.DrumChips)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.DrumChips)).Returns(chipTexture.Object);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipWave)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipWave)).Returns(waveTexture.Object);
        var manager = new NxAttackEffectManager(resourceManager.Object, random: new Random(0));
        manager.Spawn(0, JudgementType.Perfect);
        var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

        manager.Draw(spriteBatch);

        Assert.NotEmpty(starDraws);
        Assert.NotEmpty(chipDraws);
        Assert.NotEmpty(waveDraws);

        foreach (var draw in starDraws)
        {
            Assert.Equal(expectedDestinationOrigin.X, draw.Destination.X);
            Assert.Equal(expectedDestinationOrigin.Y, draw.Destination.Y);
            Assert.Equal(PerformanceUILayout.NxAttackEffectAssets.StarDrawSize / 2f, draw.Origin);
        }

        foreach (var draw in chipDraws)
        {
            Assert.Equal(expectedDestinationOrigin.X, draw.Destination.X);
            Assert.Equal(expectedDestinationOrigin.Y, draw.Destination.Y);
            Assert.NotNull(draw.Source);
            Assert.Equal(
                new Vector2(draw.Source.Value.Width / 2f, draw.Source.Value.Height / 2f),
                draw.Origin);
        }

        foreach (var draw in waveDraws)
        {
            Assert.Equal(expectedDestinationOrigin.X, draw.Destination.X);
            Assert.Equal(expectedDestinationOrigin.Y, draw.Destination.Y);
            Assert.Equal(PerformanceUILayout.NxAttackEffectAssets.WaveDrawSize / 2f, draw.Origin);
        }
    }

    private static NxAttackEffectManager CreateManager(
        bool combinedAvailable,
        bool perLaneFallbackAvailable = false,
        bool starsAvailable = false,
        bool chipTextureAvailable = false,
        bool waveAvailable = false,
        int chipTextureWidth = 718,
        int chipTextureHeight = 776,
        NxAttackEffectSettings? settings = null)
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);

        if (combinedAvailable)
        {
            resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipFireCombined)).Returns(true);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipFireCombined))
                .Returns(CreateTexture(width: 1800, height: 1650).Object);
        }

        if (perLaneFallbackAvailable)
        {
            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                var path = TexturePath.GetDrumChipFireLanePath(lane);
                resourceManager.Setup(x => x.ResourceExists(path)).Returns(true);
                resourceManager.Setup(x => x.LoadTexture(path)).Returns(CreateTexture(width: 128, height: 128).Object);
            }
        }

        if (starsAvailable)
        {
            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                var path = TexturePath.GetDrumChipStarLanePath(lane);
                resourceManager.Setup(x => x.ResourceExists(path)).Returns(true);
                resourceManager.Setup(x => x.LoadTexture(path)).Returns(CreateTexture(width: 32, height: 32).Object);
            }
        }

        if (chipTextureAvailable)
        {
            resourceManager.Setup(x => x.ResourceExists(TexturePath.DrumChips)).Returns(true);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.DrumChips)).Returns(CreateTexture(width: chipTextureWidth, height: chipTextureHeight).Object);
        }

        if (waveAvailable)
        {
            resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipWave)).Returns(true);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipWave)).Returns(CreateTexture(width: 64, height: 64).Object);
        }

        return new NxAttackEffectManager(resourceManager.Object, settings, random: new Random(0));
    }

    private static Mock<ITexture> CreateTexture(int width, int height)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(width);
        texture.SetupGet(x => x.Height).Returns(height);
        return texture;
    }

    private static Rectangle GetChipFragmentSource(int lane, int side, int sheetWidth = 720, int sheetHeight = 704)
    {
        var method = typeof(NxAttackEffectManager).GetMethod(
            "GetChipFragmentSource",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(int), typeof(int), typeof(int), typeof(int) },
            null);

        Assert.NotNull(method);
        return (Rectangle)method!.Invoke(null, new object[] { lane, side, sheetWidth, sheetHeight })!;
    }
}
