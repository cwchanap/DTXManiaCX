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
    [InlineData(5, 0, 720, 0, 35)]
    [InlineData(0, 0, 720, 540, 37)]
    [InlineData(0, 1, 720, 577, 37)]
    [InlineData(3, 0, 720, 128, 32)]
    [InlineData(8, 0, 720, 70, 29)]
    [InlineData(9, 0, 720, 360, 37)]
    [InlineData(2, 1, 718, 690, 28)]
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

    [Fact]
    public void GetChipFragmentSource_WhenMappedColumnStartsBeyondSheetWidth_ShouldReturnEmpty()
    {
        var source = GetChipFragmentSource(lane: 0, side: 0, sheetWidth: 500);

        Assert.Equal(Rectangle.Empty, source);
    }

    [Fact]
    public void GetChipFragmentSource_WhenSideSpecificFragmentStartsBeyondSheetWidth_ShouldReturnEmpty()
    {
        var source = GetChipFragmentSource(lane: 0, side: 1, sheetWidth: 560);

        Assert.Equal(Rectangle.Empty, source);
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
        var manager = CreateManager(perLaneFallbackAvailable: true);

        manager.Spawn(3, judgementType);

        Assert.Equal(shouldSpawn ? 2 : 0, manager.ActivePrimarySparkCountForTesting);
    }

    [Fact]
    public void Spawn_WhenSameLaneAlreadyActive_ShouldRestartPrimarySpark()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);
        manager.Spawn(2, JudgementType.Perfect);
        manager.Update(0.09);

        manager.Spawn(2, JudgementType.Great);

        Assert.Equal(2, manager.ActivePrimarySparksForTesting.Count);
        Assert.All(manager.ActivePrimarySparksForTesting, spark =>
        {
            Assert.Equal(0, spark.FrameIndex);
            Assert.Equal(JudgementType.Great, spark.JudgementType);
        });
    }

    [Fact]
    public void Spawn_WithNxDefaultAssets_ShouldCreateStarsAndChipFragments()
    {
        var manager = CreateManager(
            perLaneFallbackAvailable: true,
            starsAvailable: true,
            chipTextureAvailable: true,
            waveAvailable: true);

        manager.Spawn(0, JudgementType.Perfect);

        Assert.Equal(2, manager.ActivePrimarySparkCountForTesting);
        Assert.True(manager.ActiveParticleCountForTesting > 0);
    }

    [Fact]
    public void Spawn_WhenChipTextureTooNarrowForFragment_ShouldSkipChipFragments()
    {
        var manager = CreateManager(
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
    public void Constructor_WithPerLaneFireAvailable_ShouldUsePerLaneNxDefaultSpark()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);

        manager.Spawn(0, JudgementType.Perfect);

        Assert.Equal(2, manager.ActivePrimarySparksForTesting.Count);
    }

    [Fact]
    public void Update_AfterNxDefaultStaticFireDuration_ShouldExpireSpark()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);

        manager.Update(0.22);

        Assert.Equal(0, manager.ActivePrimarySparkCountForTesting);
    }

    [Fact]
    public void Dispose_ShouldReleaseLoadedTextures()
    {
        var texture = CreateTexture(width: 128, height: 128);
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.GetDrumChipFireLanePath(0))).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.GetDrumChipFireLanePath(0))).Returns(texture.Object);

        var manager = new NxAttackEffectManager(resourceManager.Object);

        manager.Dispose();

        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Dispose_WhenCalledTwice_ShouldReleaseLoadedTexturesOnce()
    {
        var texture = CreateTexture(width: 128, height: 128);
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.GetDrumChipFireLanePath(0))).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.GetDrumChipFireLanePath(0))).Returns(texture.Object);

        var manager = new NxAttackEffectManager(resourceManager.Object);

        manager.Dispose();
        manager.Dispose();

        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Draw_PrimarySpark_ShouldDrawTwoPerLaneFireSprites()
    {
        var fireTexture = CreateTexture(width: 128, height: 128);
        var fireDraws = new List<(Rectangle Destination, Rectangle? Source)>();
        fireTexture.Setup(x => x.Draw(
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
                    fireDraws.Add((destination, source)));

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.GetDrumChipFireLanePath(0))).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.GetDrumChipFireLanePath(0))).Returns(fireTexture.Object);
        var manager = new NxAttackEffectManager(resourceManager.Object, random: new Random(0));
        manager.Spawn(0, JudgementType.Perfect);
        var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

        manager.Draw(spriteBatch);

        Assert.Equal(2, fireDraws.Count);
        Assert.All(fireDraws, draw =>
        {
            Assert.Null(draw.Source);
            Assert.True(draw.Destination.Width > PerformanceUILayout.NxAttackEffectAssets.PrimarySparkDrawSize.X);
            Assert.True(draw.Destination.Height > PerformanceUILayout.NxAttackEffectAssets.PrimarySparkDrawSize.Y);
        });
    }

    [Fact]
    public void Draw_DefaultNxParticles_ShouldUseCenteredRotationOrigins()
    {
        var starTexture = CreateTexture(width: 40, height: 20);
        var chipTexture = CreateTexture(width: 718, height: 776);
        var waveTexture = CreateTexture(width: 80, height: 40);
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
            Assert.Equal(new Vector2(20f, 10f), draw.Origin);
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
            Assert.Equal(new Vector2(40f, 20f), draw.Origin);
        }
    }

    [Fact]
    public void Draw_DefaultNxParticles_EffectiveRenderedCenterShouldLandAtEffectOrigin()
    {
        // Geometry-level check: compute the effective on-screen center of each drawn
        // sprite from the actual (destination, source, origin) args using MonoGame's
        // destRect-overload formula, and assert it lands at the intended effect origin.
        //
        // MonoGame's destRect Draw maps the source's origin texel to
        //   dest.Location + origin * (dest.Size / sourceSize)
        // so the sprite's effective center is
        //   dest.Location - origin * (dest.Size / sourceSize) + dest.Size / 2
        // (origin is in source-texel units). This is independent of whether the
        // production code uses a centered-dest or top-left-dest convention, so it
        // catches centering regressions that an args-only assertion would miss.
        var starTexture = CreateTexture(width: 40, height: 20);
        var chipTexture = CreateTexture(width: 718, height: 776);
        var waveTexture = CreateTexture(width: 80, height: 40);
        var starTextureSize = new Vector2(40, 20);
        var waveTextureSize = new Vector2(80, 40);
        var draws = new List<(Rectangle Destination, Rectangle? Source, Vector2 Origin, Vector2 TextureSize)>();

        SetupTextureDrawCapture(starTexture, draws, starTextureSize);
        SetupTextureDrawCapture(chipTexture, draws, new Vector2(718, 776));
        SetupTextureDrawCapture(waveTexture, draws, waveTextureSize);

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
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

        Assert.NotEmpty(draws);

        var expectedCenter = new Vector2(
            PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(0).X,
            PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(0).Y);

        foreach (var draw in draws)
        {
            Assert.True(draw.Destination.Width > 0, "destination width must be positive");
            Assert.True(draw.Destination.Height > 0, "destination height must be positive");

            var sourceSize = draw.Source.HasValue
                ? new Vector2(draw.Source.Value.Width, draw.Source.Value.Height)
                : draw.TextureSize;
            Assert.True(sourceSize.X > 0 && sourceSize.Y > 0, "source size must be positive");

            var scaleFactor = new Vector2(
                draw.Destination.Width / sourceSize.X,
                draw.Destination.Height / sourceSize.Y);
            var renderedCenter = new Vector2(draw.Destination.X, draw.Destination.Y)
                - draw.Origin * scaleFactor
                + new Vector2(draw.Destination.Width / 2f, draw.Destination.Height / 2f);

            // Allow 1px tolerance for the int rounding in RotationDrawDestination.
            Assert.InRange(renderedCenter.X, expectedCenter.X - 1f, expectedCenter.X + 1f);
            Assert.InRange(renderedCenter.Y, expectedCenter.Y - 1f, expectedCenter.Y + 1f);
        }
    }

    private static void SetupTextureDrawCapture(
        Mock<ITexture> texture,
        List<(Rectangle Destination, Rectangle? Source, Vector2 Origin, Vector2 TextureSize)> draws,
        Vector2 textureSize)
    {
        texture.Setup(x => x.Draw(
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
                    draws.Add((destination, source, origin, textureSize)));
    }

    private static NxAttackEffectManager CreateManager(
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

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(99)]
    public void Spawn_WhenLaneOutOfRange_ShouldNotSpawnAnything(int lane)
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);

        manager.Spawn(lane, JudgementType.Perfect);

        Assert.Equal(0, manager.ActivePrimarySparkCountForTesting);
        Assert.Equal(0, manager.ActiveParticleCountForTesting);
    }

    [Fact]
    public void Spawn_WhenDisposed_ShouldNotSpawnAnything()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);
        manager.Dispose();

        manager.Spawn(0, JudgementType.Perfect);

        Assert.Equal(0, manager.ActivePrimarySparkCountForTesting);
        Assert.Equal(0, manager.ActiveParticleCountForTesting);
    }

    [Fact]
    public void Update_WhenDisposed_ShouldNotThrow()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);
        manager.Dispose();

        var exception = Record.Exception(() => manager.Update(0.1));
        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WhenDisposed_ShouldNotThrow()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);
        manager.Dispose();
        var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

        var exception = Record.Exception(() => manager.Draw(spriteBatch));
        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WithNullSpriteBatch_ShouldNotThrow()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);

        var exception = Record.Exception(() => manager.Draw(null));
        Assert.Null(exception);
    }

    [Fact]
    public void ClearAll_ShouldRemoveAllSparksAndParticles()
    {
        var manager = CreateManager(
            perLaneFallbackAvailable: true,
            starsAvailable: true,
            chipTextureAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);
        manager.Spawn(3, JudgementType.Great);
        Assert.True(manager.ActivePrimarySparkCountForTesting > 0);
        Assert.True(manager.ActiveParticleCountForTesting > 0);

        manager.ClearAll();

        Assert.Equal(0, manager.ActivePrimarySparkCountForTesting);
        Assert.Equal(0, manager.ActiveParticleCountForTesting);
    }

    [Fact]
    public void Draw_WaveParticle_ShouldDrawWaveTextureWithCenteredRotation()
    {
        // Inject a zero-delay wave directly to cover the immediate DrawParticle branch.
        var waveTexture = CreateTexture(width: 80, height: 40);
        var waveDraws = new List<(Rectangle Destination, Vector2 Origin)>();
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
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipWave)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipWave)).Returns(waveTexture.Object);
        var manager = new NxAttackEffectManager(resourceManager.Object, random: new Random(0));

        // Inject a wave particle with zero delay so it draws immediately.
        var particlesField = typeof(NxAttackEffectManager).GetField(
            "_particles", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(particlesField);
        var particles = (System.Collections.IList)particlesField!.GetValue(manager)!;
        var origin = PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(0);
        var waveParticle = NxAttackEffectManager.ParticleInstance.CreateWave(
            0, origin, delaySeconds: 0.0, durationSeconds: 0.42);
        particles.Add(waveParticle);

        var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));
        manager.Draw(spriteBatch);

        Assert.NotEmpty(waveDraws);
        var draw = waveDraws[0];
        Assert.Equal(new Vector2(40f, 20f), draw.Origin);
    }

    [Fact]
    public void Draw_WaveParticle_WithDelay_ShouldSkipDrawing()
    {
        // Particles with DelaySeconds > 0 should not be drawn.
        var waveTexture = CreateTexture(width: 80, height: 40);
        var waveDraws = new List<Rectangle>();
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
                    waveDraws.Add(destination));

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipWave)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipWave)).Returns(waveTexture.Object);
        var manager = new NxAttackEffectManager(resourceManager.Object, random: new Random(0));

        var particlesField = typeof(NxAttackEffectManager).GetField(
            "_particles", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(particlesField);
        var particles = (System.Collections.IList)particlesField!.GetValue(manager)!;
        var origin = PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(0);
        var waveParticle = NxAttackEffectManager.ParticleInstance.CreateWave(
            0, origin, delaySeconds: 0.5, durationSeconds: 0.42);
        particles.Add(waveParticle);

        var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));
        manager.Draw(spriteBatch);

        Assert.Empty(waveDraws);
    }

    [Fact]
    public void Update_WaveParticle_WithDelay_ShouldDecrementDelayBeforeActivating()
    {
        // Wave particles start with a delay; Update should decrement the delay
        // until it reaches 0, then begin the normal lifecycle.
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        var manager = new NxAttackEffectManager(resourceManager.Object, random: new Random(0));

        var particlesField = typeof(NxAttackEffectManager).GetField(
            "_particles", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(particlesField);
        var particles = (System.Collections.IList)particlesField!.GetValue(manager)!;
        var origin = PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(0);
        var waveParticle = NxAttackEffectManager.ParticleInstance.CreateWave(
            0, origin, delaySeconds: 0.3, durationSeconds: 0.42);
        particles.Add(waveParticle);

        // First update: delay decreases from 0.3 to 0.2, particle not expired.
        manager.Update(0.1);
        Assert.Equal(1, manager.ActiveParticleCountForTesting);

        // Second update: delay decreases from 0.2 to 0 (returns early), particle not expired.
        manager.Update(0.5);
        Assert.Equal(1, manager.ActiveParticleCountForTesting);

        // Third update: delay is now 0, so the normal lifecycle begins.
        // 0.5s elapsed >= 0.42s duration, particle expires.
        manager.Update(0.5);
        Assert.Equal(0, manager.ActiveParticleCountForTesting);
    }

    [Fact]
    public void Constructor_WhenTextureLoadThrows_ShouldNotPropagateException()
    {
        // LoadOptionalTexture catches exceptions and returns null.
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.GetDrumChipFireLanePath(0))).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.GetDrumChipFireLanePath(0)))
            .Throws(new InvalidOperationException("load error"));

        var exception = Record.Exception(() => new NxAttackEffectManager(resourceManager.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void Update_WithNegativeDeltaTime_ShouldClampToZero()
    {
        var manager = CreateManager(perLaneFallbackAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);
        var initialCount = manager.ActivePrimarySparkCountForTesting;

        manager.Update(-0.5);

        // Negative delta is clamped to 0, so sparks should not advance or expire.
        Assert.Equal(initialCount, manager.ActivePrimarySparkCountForTesting);
    }

    [Fact]
    public void PrimarySparkInstance_GetNxStaticFirePosition_ShouldTravelFromOrigin()
    {
        // At frame 0, the spark should be at its origin position.
        var origin = new Vector2(100, 200);
        var spark = new NxAttackEffectManager.PrimarySparkInstance(
            0, JudgementType.Perfect, origin, 0f);

        var position = spark.GetNxStaticFirePosition();

        Assert.Equal(origin, position);
    }

    [Fact]
    public void PrimarySparkInstance_GetNxStaticFireScale_ShouldReturnPositiveScale()
    {
        var spark = new NxAttackEffectManager.PrimarySparkInstance(
            0, JudgementType.Perfect, Vector2.Zero, 0f);

        var scale = spark.GetNxStaticFireScale();

        Assert.True(scale > 0f);
    }

    [Fact]
    public void PrimarySparkInstance_Update_ShouldAdvanceFrameIndex()
    {
        var spark = new NxAttackEffectManager.PrimarySparkInstance(
            0, JudgementType.Perfect, Vector2.Zero, 0f);

        spark.Update(0.006, frameDurationSeconds: 0.003, frameCount: 71);

        Assert.Equal(2, spark.FrameIndex);
        Assert.False(spark.IsExpired);
    }

    [Fact]
    public void PrimarySparkInstance_Update_WhenFrameExceedsCount_ShouldExpire()
    {
        var spark = new NxAttackEffectManager.PrimarySparkInstance(
            0, JudgementType.Perfect, Vector2.Zero, 0f);

        spark.Update(1.0, frameDurationSeconds: 0.003, frameCount: 5);

        Assert.Equal(4, spark.FrameIndex);
        Assert.True(spark.IsExpired);
    }
}
