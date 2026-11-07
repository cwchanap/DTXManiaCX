#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Result;

[Trait("Category", "Unit")]
public class ResultScreenRendererTests
{
    [Fact]
    public void DrawModelText_ShouldPlacePlaybackAndSavePresentationInDedicatedRegion()
    {
        var resources = new Mock<IResourceManager>();
        var smallFont = new Mock<IFont>();
        var renderer = new ResultScreenRenderer(resources.Object, smallFont.Object, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary
            {
                PlaySpeedPercent = 75,
                PitchSemitones = 3
            },
            null,
            0,
            null,
            null,
            new ResultSavePresentation(ResultSaveState.Failed, "database busy"));
        var drawModelText = typeof(ResultScreenRenderer).GetMethod(
            "DrawModelText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        drawModelText!.Invoke(renderer, new object?[] { null, model, 1.0f });

        smallFont.Verify(x => x.DrawString(
            It.IsAny<SpriteBatch>(),
            "PLAY 0.75x · PITCH +3 st",
            ResultScreenRenderer.ResolvePlaybackProfilePosition(SkinTheme.Empty),
            ResultUILayout.PlaybackPresentation.ProfileColor),
            Times.Once);
        smallFont.Verify(x => x.DrawString(
            It.IsAny<SpriteBatch>(),
            "SCORE BUCKET: 0.75x · ALL PITCHES",
            ResultScreenRenderer.ResolvePlaybackScoreBucketPosition(SkinTheme.Empty),
            ResultUILayout.PlaybackPresentation.ScoreBucketColor),
            Times.Once);
        smallFont.Verify(x => x.DrawString(
            It.IsAny<SpriteBatch>(),
            "SCORE SAVE: FAILED · database busy",
            ResultScreenRenderer.ResolvePlaybackSaveStatusPosition(SkinTheme.Empty),
            ResultUILayout.PlaybackPresentation.FailedColor),
            Times.Once);
        smallFont.Verify(x => x.DrawStringWrapped(
            It.IsAny<SpriteBatch>(),
                "PRESS ENTER TO RETRY · BACK TO LEAVE WITHOUT SAVING",
            ResultScreenRenderer.ResolvePlaybackSaveGuidanceBounds(SkinTheme.Empty),
            ResultUILayout.PlaybackPresentation.GuidanceColor,
            TextAlignment.Left),
            Times.Once);
    }

    [Fact]
    public void DrawModelText_WithThemedPlaybackOrigin_ShouldPlaceWholeGroupAtThemedCoordinates()
    {
        var resources = new Mock<IResourceManager>();
        resources
            .Setup(r => r.CurrentTheme)
            .Returns(SkinTheme.Parse(new[] { "Result.PlaybackInfoX=830", "Result.PlaybackInfoY=310" }));
        var smallFont = new Mock<IFont>();
        var renderer = new ResultScreenRenderer(resources.Object, smallFont.Object, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary(),
            null,
            0,
            null,
            null);
        var drawModelText = typeof(ResultScreenRenderer).GetMethod(
            "DrawModelText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        drawModelText!.Invoke(renderer, new object?[] { null, model, 1.0f });

        smallFont.Verify(x => x.DrawString(
            It.IsAny<SpriteBatch>(),
            It.IsAny<string>(),
            new Vector2(830f, 310f),
            ResultUILayout.PlaybackPresentation.ProfileColor),
            Times.Once);
        smallFont.Verify(x => x.DrawString(
            It.IsAny<SpriteBatch>(),
            It.IsAny<string>(),
            new Vector2(830f, 336f),
            ResultUILayout.PlaybackPresentation.ScoreBucketColor),
            Times.Once);
        smallFont.Verify(x => x.DrawString(
            It.IsAny<SpriteBatch>(),
            It.IsAny<string>(),
            new Vector2(830f, 370f),
            ResultUILayout.PlaybackPresentation.NotStartedColor),
            Times.Once);
    }

    [Fact]
    public void Dispose_WithBorrowedPlaybackBackdropTexture_ShouldNotDisposeIt()
    {
        var resources = new Mock<IResourceManager>();
#pragma warning disable SYSLIB0050
        var pixel = (TrackingTexture2D)FormatterServices.GetUninitializedObject(typeof(TrackingTexture2D));
#pragma warning restore SYSLIB0050
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null, null, null, pixel);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = true, TotalNotes = 1 },
            null,
            0,
            null,
            null);

        renderer.Load(model);
        renderer.Dispose();

        // The pixel is borrowed from ResultStage: loading and disposing the
        // renderer must not dispose it (the stage still owns its lifetime).
        Assert.False(pixel.WasDisposed);
    }

    private sealed class TrackingTexture2D : Texture2D
    {
        public TrackingTexture2D() : base(null!, 1, 1)
        {
        }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
        }
    }

    [Theory]
    [InlineData(ResultSaveState.NotStarted)]
    [InlineData(ResultSaveState.Saving)]
    [InlineData(ResultSaveState.Saved)]
    [InlineData(ResultSaveState.Failed)]
    public void ResolveSaveStatusColor_ShouldReturnConfiguredColor(ResultSaveState state)
    {
        var color = ResultScreenRenderer.ResolveSaveStatusColor(state);

        Assert.NotEqual(default, color);
    }

    [Fact]
    public void CreateViewportTransform_ExactNXSize_ShouldUseIdentityScale()
    {
        var matrix = ResultScreenRenderer.CreateViewportTransform(new Viewport(0, 0, 1280, 720));

        Assert.Equal(1.0f, matrix.M11, 3);
        Assert.Equal(1.0f, matrix.M22, 3);
        Assert.Equal(0.0f, matrix.M41, 3);
        Assert.Equal(0.0f, matrix.M42, 3);
    }

    [Fact]
    public void CreateViewportTransform_FourByThreeViewport_ShouldLetterboxVertically()
    {
        var matrix = ResultScreenRenderer.CreateViewportTransform(new Viewport(0, 0, 1024, 768));

        Assert.Equal(0.8f, matrix.M11, 3);
        Assert.Equal(0.8f, matrix.M22, 3);
        Assert.Equal(0.0f, matrix.M41, 3);
        Assert.Equal(96.0f, matrix.M42, 3);
    }

    [Fact]
    public void CreateViewportTransform_UltrawideViewport_ShouldPillarboxHorizontally()
    {
        var matrix = ResultScreenRenderer.CreateViewportTransform(new Viewport(0, 0, 1920, 720));

        Assert.Equal(1.0f, matrix.M11, 3);
        Assert.Equal(1.0f, matrix.M22, 3);
        Assert.Equal(320.0f, matrix.M41, 3);
        Assert.Equal(0.0f, matrix.M42, 3);
    }

    [Fact]
    public void Load_ExcellentSSModel_ShouldLoadNXStructuralPaths()
    {
        var resources = new Mock<IResourceManager>();
        var loadedPaths = new List<string>();
        var texture = new Mock<ITexture>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources
            .Setup(r => r.LoadTexture(It.IsAny<string>()))
            .Callback<string>(loadedPaths.Add)
            .Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                CompletionReason = CompletionReason.SongComplete,
                ClearFlag = true,
                PerfectCount = 100,
                TotalNotes = 100,
                PlayingSkill = 100.0,
                Score = 1
            },
            null,
            0,
            null,
            new SongScore { PlayCount = 1, BestScore = 0, HighSkill = 0.0 });

        renderer.Load(model);

        Assert.Contains(TexturePath.ResultBackground, loadedPaths);
        Assert.Contains(TexturePath.ResultBackgroundRankSS, loadedPaths);
        Assert.Contains(TexturePath.ResultRankSS, loadedPaths);
        Assert.Contains(TexturePath.ResultPlateExcellent, loadedPaths);
        Assert.Contains(TexturePath.ResultJacketPanel, loadedPaths);
        Assert.Contains(TexturePath.ResultSkillPanel, loadedPaths);
        Assert.Contains(TexturePath.ResultDefaultPreview, loadedPaths);
        Assert.Contains(TexturePath.ResultNewRecord, loadedPaths);
    }

    [Fact]
    public void Load_FailedPlate_ShouldNotLoadClearPlateTextures()
    {
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = false, MissCount = 1, TotalNotes = 1 },
            null,
            0,
            null,
            null);

        renderer.Load(model);

        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateExcellent), Times.Never);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateFullCombo), Times.Never);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateStageCleared), Times.Never);
    }

    [Fact]
    public void Load_MissingOptionalAssets_ShouldNotThrowOrLoadMissingPaths()
    {
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        resources
            .Setup(r => r.ResourceExists(It.IsAny<string>()))
            .Returns<string>(path => path != TexturePath.ResultBackgroundRankS
                && path != TexturePath.ResultNewRecord);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary
            {
                ClearFlag = true,
                PerfectCount = 80,
                GreatCount = 20,
                TotalNotes = 100,
                PlayingSkill = 90.0,
                Score = 1
            },
            null,
            0,
            null,
            new SongScore { PlayCount = 1, BestScore = 0, HighSkill = 0.0 });

        var exception = CaptureException(() => renderer.Load(model));

        Assert.Null(exception);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultBackgroundRankS), Times.Never);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultNewRecord), Times.Never);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultRankS), Times.Once);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateFullCombo), Times.Once);
    }

    [Fact]
    public void Load_FRankModel_ShouldLoadEBackgroundButNoRankBadge()
    {
        var resources = new Mock<IResourceManager>();
        var loadedPaths = new List<string>();
        var texture = new Mock<ITexture>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources
            .Setup(r => r.LoadTexture(It.IsAny<string>()))
            .Callback<string>(loadedPaths.Add)
            .Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary
            {
                ClearFlag = true,
                TotalNotes = 100,
                PlayingSkill = 30.0,
                Score = 1
            },
            null,
            0,
            null,
            null);

        renderer.Load(model);

        Assert.Equal(ResultRank.F, model.Rank);
        Assert.DoesNotContain(loadedPaths, path => path.Contains("8_rank"));
        resources.Verify(r => r.LoadTexture(TexturePath.ResultRankE), Times.Never);
        Assert.Contains(TexturePath.ResultBackgroundRankE, loadedPaths);
    }

    [Fact]
    public void RankPathHelpers_FOrUnknownRank_ShouldNotReturnEBadgePath()
    {
        var getRankTexturePath = typeof(ResultScreenRenderer).GetMethod(
            "GetRankTexturePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var getRankBackgroundPath = typeof(ResultScreenRenderer).GetMethod(
            "GetRankBackgroundPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.Null(getRankTexturePath!.Invoke(null, new object[] { ResultRank.F }));
        Assert.Null(getRankTexturePath.Invoke(null, new object[] { (ResultRank)99 }));
        Assert.Equal(
            TexturePath.ResultBackgroundRankE,
            getRankBackgroundPath!.Invoke(null, new object[] { ResultRank.F }));
        Assert.Null(getRankBackgroundPath.Invoke(null, new object[] { (ResultRank)99 }));
    }

    [Fact]
    public void Load_LoadTextureFailure_ShouldNotThrow()
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources
            .Setup(r => r.LoadTexture(TexturePath.ResultBackgroundRankA))
            .Throws(new InvalidOperationException("missing optional rank background"));
        resources
            .Setup(r => r.LoadTexture(It.Is<string>(path => path != TexturePath.ResultBackgroundRankA)))
            .Returns(new Mock<ITexture>().Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary
            {
                ClearFlag = true,
                PerfectCount = 73,
                GreatCount = 27,
                TotalNotes = 100,
                PlayingSkill = 80.0
            },
            null,
            0,
            null,
            null);

        var exception = CaptureException(() => renderer.Load(model));

        Assert.Null(exception);
    }

    [Fact]
    public void Load_MissingPreview_ShouldLoadDefaultPreviewFallback()
    {
        var chartDirectory = Path.Combine(Path.GetTempPath(), "dtx-result-renderer");
        var previewPath = Path.Combine(chartDirectory, "custom.png");
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        resources
            .Setup(r => r.ResourceExists(It.IsAny<string>()))
            .Returns<string>(path => path != previewPath);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = true, TotalNotes = 10 },
            null,
            0,
            new SongChart
            {
                FilePath = Path.Combine(chartDirectory, "chart.dtx"),
                PreviewImage = "custom.png"
            },
            null);

        renderer.Load(model);

        resources.Verify(r => r.LoadTexture(previewPath), Times.Never);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultDefaultPreview), Times.Once);
    }

    [Fact]
    public void Load_ReloadAndDispose_ShouldReleaseLoadedTextures()
    {
        var resources = new Mock<IResourceManager>();
        var firstTexture = new Mock<ITexture>(MockBehavior.Strict);
        var secondTexture = new Mock<ITexture>(MockBehavior.Strict);
        firstTexture.Setup(t => t.RemoveReference());
        secondTexture.Setup(t => t.RemoveReference());
        var loadCount = 0;
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources
            .Setup(r => r.LoadTexture(It.IsAny<string>()))
            .Returns(() => loadCount++ == 0 ? firstTexture.Object : secondTexture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = true, PerfectCount = 10, TotalNotes = 10 },
            null,
            0,
            null,
            null);

        renderer.Load(model);
        renderer.Load(model);
        renderer.Dispose();

        firstTexture.Verify(t => t.RemoveReference(), Times.Once);
        secondTexture.Verify(t => t.RemoveReference(), Times.AtLeastOnce);
    }

    [Fact]
    public void ApplyPanelTransparency_ShouldScaleAndRestoreTransparency()
    {
        // Use distinct mock textures to simulate production behavior where
        // each texture slot holds a different object.
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);

        // Track per-path textures so we can verify the panel-specific ones
        var pathToTexture = new Dictionary<string, Mock<ITexture>>();
        resources
            .Setup(r => r.LoadTexture(It.IsAny<string>()))
            .Returns<string>(path =>
            {
                var tex = new Mock<ITexture>();
                tex.SetupProperty(t => t.Transparency, 255);
                pathToTexture[path] = tex;
                return tex.Object;
            });

        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary
            {
                ClearFlag = true,
                PerfectCount = 100,
                TotalNotes = 100,
                PlayingSkill = 100.0,
                Score = 1
            },
            null,
            0,
            null,
            new SongScore { PlayCount = 1, BestScore = 0, HighSkill = 0.0 });
        renderer.Load(model);

        var applyMethod = typeof(ResultScreenRenderer).GetMethod(
            "ApplyPanelTransparency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var restoreMethod = typeof(ResultScreenRenderer).GetMethod(
            "RestorePanelTransparency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Apply 50% alpha
        applyMethod!.Invoke(renderer, new object[] { 128 });

        // Panel textures should have transparency scaled to ~128
        var panelPaths = new[]
        {
            TexturePath.ResultPlateExcellent, // SS → Excellent plate
            TexturePath.ResultJacketPanel,
            TexturePath.ResultDefaultPreview,
            TexturePath.ResultSkillPanel,
            TexturePath.ResultNewRecord,
        };
        foreach (var path in panelPaths)
        {
            if (pathToTexture.TryGetValue(path, out var tex))
            {
                Assert.InRange(tex.Object.Transparency, 120, 136);
            }
        }

        restoreMethod!.Invoke(renderer, null);

        // All panel textures should be restored to 255
        foreach (var path in panelPaths)
        {
            if (pathToTexture.TryGetValue(path, out var tex))
            {
                Assert.Equal(255, tex.Object.Transparency);
            }
        }

        renderer.Dispose();
    }

    [Fact]
    public void ApplyPanelTransparency_ZeroAlpha_ShouldSetZeroTransparency()
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);

        var pathToTexture = new Dictionary<string, Mock<ITexture>>();
        resources
            .Setup(r => r.LoadTexture(It.IsAny<string>()))
            .Returns<string>(path =>
            {
                var tex = new Mock<ITexture>();
                tex.SetupProperty(t => t.Transparency, 255);
                pathToTexture[path] = tex;
                return tex.Object;
            });

        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = true, TotalNotes = 10 },
            null,
            0,
            null,
            null);
        renderer.Load(model);

        var applyMethod = typeof(ResultScreenRenderer).GetMethod(
            "ApplyPanelTransparency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var restoreMethod = typeof(ResultScreenRenderer).GetMethod(
            "RestorePanelTransparency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Apply zero alpha
        applyMethod!.Invoke(renderer, new object[] { 0 });

        // Plate texture (FullCombo) should be at transparency 0
        Assert.True(pathToTexture.ContainsKey(TexturePath.ResultPlateFullCombo),
            $"Expected plate path not found. Actual paths: {string.Join(", ", pathToTexture.Keys)}");
        Assert.Equal(0, pathToTexture[TexturePath.ResultPlateFullCombo].Object.Transparency);

        restoreMethod!.Invoke(renderer, null);

        // Should be restored to 255
        Assert.Equal(255, pathToTexture[TexturePath.ResultPlateFullCombo].Object.Transparency);

        renderer.Dispose();
    }

    [Fact]
    public void RestorePanelTransparency_WithoutApply_ShouldNotThrow()
    {
        var resources = new Mock<IResourceManager>();
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);

        var restoreMethod = typeof(ResultScreenRenderer).GetMethod(
            "RestorePanelTransparency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var exception = CaptureException(() => restoreMethod!.Invoke(renderer, null));

        Assert.Null(exception);
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}