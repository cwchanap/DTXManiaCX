using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
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

    private static NxAttackEffectManager CreateManager(
        bool combinedAvailable,
        bool perLaneFallbackAvailable = false,
        bool starsAvailable = false,
        bool chipTextureAvailable = false,
        bool waveAvailable = false)
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
            resourceManager.Setup(x => x.LoadTexture(TexturePath.DrumChips)).Returns(CreateTexture(width: 718, height: 776).Object);
        }

        if (waveAvailable)
        {
            resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipWave)).Returns(true);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipWave)).Returns(CreateTexture(width: 64, height: 64).Object);
        }

        return new NxAttackEffectManager(resourceManager.Object, random: new Random(0));
    }

    private static Mock<ITexture> CreateTexture(int width, int height)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(width);
        texture.SetupGet(x => x.Height).Returns(height);
        return texture;
    }
}
