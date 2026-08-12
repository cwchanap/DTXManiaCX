using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework.Audio;
using Moq;
using Xunit;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public sealed class SongSelectionStagePreparedChartLifecycleTests
{
    [Fact]
    public void PrepareVideoChart_ShouldUseResolvedChartPreviewAndLeavePreviewStopped()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-lifecycle-exact");
        Directory.CreateDirectory(root);
        var primaryPath = Path.Combine(root, "primary.dtx");
        var preparedPath = Path.Combine(root, "prepared.dtx");
        var primaryPreview = Path.Combine(root, "primary.wav");
        var preparedPreview = Path.Combine(root, "prepared.wav");
        File.WriteAllText(preparedPath, "chart");
        File.WriteAllText(preparedPreview, "preview");

        try
        {
            var primary = new SongChart { Id = 501, FilePath = primaryPath, PreviewFile = "primary.wav" };
            var prepared = new SongChart { Id = 502, FilePath = preparedPath, PreviewFile = "prepared.wav" };
            var song = new SongEntity { Id = 501, Title = "set", Charts = new List<SongChart> { primary, prepared } };
            primary.Song = song;
            prepared.Song = song;
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = "set",
                DatabaseSong = song,
                DatabaseSongId = song.Id,
                DatabaseChart = primary,
                Scores = new[] { new SongScore { ChartId = prepared.Id, Instrument = EInstrumentPart.DRUMS } }
            };
            var resourceManager = new Mock<IResourceManager>();
            var loadedSound = new Mock<ISound>();
            resourceManager.Setup(x => x.LoadSound(preparedPreview)).Returns(loadedSound.Object);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);

            var result = InvokeCommand(stage, "PrepareVideoChart", preparedPath);

            Assert.True(result.Success);
            resourceManager.Verify(x => x.LoadSound(preparedPreview), Times.Once);
            resourceManager.Verify(x => x.LoadSound(primaryPreview), Times.Never);
            Assert.Same(loadedSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.Equal(0d, GetPrivateField<double>(stage, "_preparedPreviewElapsedMs"));
            Assert.Equal("Prepared", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareVideoChart_WhenReplacingPlayingInteractivePreview_ShouldStopOldInstanceBeforePublishingPrepared()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-lifecycle-replace-interactive");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "prepared.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "prepared.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            resourceManager.Setup(x => x.LoadSound(previewPath)).Returns(preparedSound.Object);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            var interactiveSound = new Mock<ISound>();
            var interactiveInstance = new Mock<ISoundInstance>();
            interactiveInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            SetPrivateField(stage, "_previewSound", interactiveSound.Object);
            SetPrivateField(stage, "_previewSoundInstance", interactiveInstance.Object);

            var result = InvokeCommand(stage, "PrepareVideoChart", chartPath);

            Assert.True(result.Success);
            interactiveInstance.Verify(x => x.Stop(), Times.Once);
            interactiveInstance.Verify(x => x.Dispose(), Times.Once);
            interactiveSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(preparedSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
            Assert.Equal("Prepared", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
            preparedSound.Verify(x => x.CreateInstance(), Times.Never);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareVideoChart_WhenDeclarationFileOrLoadIsInvalid_ShouldClearPreparation()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-lifecycle-failures");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");

        try
        {
            var noDeclaration = CreateNode("none", chartPath, previewFile: "");
            var missingFile = CreateNode("missing", chartPath, previewFile: "missing.wav");
            var loadFailure = CreateNode("load", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadSound(previewPath)).Throws(new InvalidDataException("bad preview"));

            var noDeclarationResult = InvokeCommand(
                CreatePreparedStage(root, noDeclaration, resourceManager.Object),
                "PrepareVideoChart",
                chartPath);
            var missingFileResult = InvokeCommand(
                CreatePreparedStage(root, missingFile, resourceManager.Object),
                "PrepareVideoChart",
                chartPath);

            File.WriteAllText(previewPath, "preview");
            var loadFailureStage = CreatePreparedStage(root, loadFailure, resourceManager.Object);
            var loadFailureResult = InvokeCommand(loadFailureStage, "PrepareVideoChart", chartPath);

            Assert.False(noDeclarationResult.Success);
            Assert.False(missingFileResult.Success);
            Assert.False(loadFailureResult.Success);
            Assert.Equal("None", GetPrivateField<object>(loadFailureStage, "_preparedPreviewState")?.ToString());
            Assert.Null(GetPrivateField<object>(loadFailureStage, "_preparedChartSelection"));
            Assert.Null(GetPrivateField<ISound>(loadFailureStage, "_previewSound"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareVideoChart_WhenRequestedPreviewLoadFails_ShouldRestorePrimaryDelayedPreviewWithoutPreparation()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-lifecycle-fallback-primary");
        Directory.CreateDirectory(root);
        var primaryPath = Path.Combine(root, "primary.dtx");
        var requestedPath = Path.Combine(root, "requested.dtx");
        var primaryPreviewPath = Path.Combine(root, "primary.wav");
        var requestedPreviewPath = Path.Combine(root, "requested.wav");
        File.WriteAllText(primaryPath, "chart");
        File.WriteAllText(requestedPath, "chart");
        File.WriteAllText(primaryPreviewPath, "preview");
        File.WriteAllText(requestedPreviewPath, "preview");

        try
        {
            var primary = new SongChart { Id = 601, FilePath = primaryPath, PreviewFile = "primary.wav" };
            var requested = new SongChart { Id = 602, FilePath = requestedPath, PreviewFile = "requested.wav" };
            var song = new SongEntity { Id = 601, Title = "multi-chart", Charts = new List<SongChart> { primary, requested } };
            primary.Song = song;
            requested.Song = song;
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = song.Title,
                DatabaseSong = song,
                DatabaseSongId = song.Id,
                DatabaseChart = primary,
                Scores = new[] { new SongScore { ChartId = requested.Id, Instrument = EInstrumentPart.DRUMS } }
            };
            var resourceManager = new Mock<IResourceManager>();
            var primarySound = new Mock<ISound>();
            resourceManager.Setup(x => x.LoadSound(requestedPreviewPath))
                .Throws(new InvalidDataException("requested preview is invalid"));
            resourceManager.Setup(x => x.LoadSound(primaryPreviewPath)).Returns(primarySound.Object);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);

            var result = InvokeCommand(stage, "PrepareVideoChart", requestedPath);

            Assert.False(result.Success);
            resourceManager.Verify(x => x.LoadSound(requestedPreviewPath), Times.Once);
            resourceManager.Verify(x => x.LoadSound(primaryPreviewPath), Times.Once);
            Assert.Same(primarySound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PreparedPreview_ShouldNotAutoStartAfterNormalDelay()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-lifecycle-delay");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            resourceManager.Setup(x => x.LoadSound(previewPath)).Returns(sound.Object);
            var stage = CreatePreparedStage(root, CreateNode("song", chartPath, previewFile: "preview.wav"), resourceManager.Object);

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 10d);

            sound.Verify(x => x.CreateInstance(), Times.Never);
            Assert.Equal("Prepared", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StartPreparedPreview_WhenAlreadyPlaying_ShouldBeIdempotent()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPreparedState(stage, "Playing");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-start.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.True(result.Success);
        sound.Verify(x => x.CreateInstance(), Times.Never);
        instance.Verify(x => x.Play(), Times.Never);
    }

    [Fact]
    public void StartPreparedPreview_WhenInstanceCannotBeCreated_ShouldMarkPreviewFailed()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        sound.Setup(x => x.CreateInstance()).Returns((SoundEffectInstance)null!);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPreparedState(stage, "Prepared");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-start-failed.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        sound.Verify(x => x.CreateInstance(), Times.Once);
        Assert.Equal("Failed", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
    }

    [Fact]
    public void UpdatePreparedPreview_WhenPlaying_ShouldAccumulateOnlyActualPlaybackAndFailOnStop()
    {
        var stage = CreateStage();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPreparedState(stage, "Playing");
        SetPrivateField(stage, "_preparedPreviewElapsedMs", 100d);

        InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.5d);

        Assert.Equal(600d, GetPrivateField<double>(stage, "_preparedPreviewElapsedMs"));

        instance.SetupGet(x => x.State).Returns(SoundState.Stopped);
        InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 1d);

        Assert.Equal(600d, GetPrivateField<double>(stage, "_preparedPreviewElapsedMs"));
        Assert.Equal("Failed", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
    }

    [Fact]
    public void CancelPreparedChart_ShouldBeIdempotentAndReleasePreviewExactlyOnce()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPreparedState(stage, "Playing");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-cancel.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPrivateField(stage, "_preparedPreviewElapsedMs", 500d);

        Assert.True(InvokeCommand(stage, "CancelPreparedChart").Success);
        Assert.True(InvokeCommand(stage, "CancelPreparedChart").Success);

        sound.Verify(x => x.RemoveReference(), Times.Once);
        instance.Verify(x => x.Stop(), Times.Once);
        instance.Verify(x => x.Dispose(), Times.Once);
        Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("None", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        Assert.Equal(0d, GetPrivateField<double>(stage, "_preparedPreviewElapsedMs"));
    }

    [Fact]
    public void CancelPreparedChart_WhenNoPreparation_ShouldPreserveInteractivePreview()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPrivateField(stage, "_previewPlayDelay", 1.5d);
        SetPrivateField(stage, "_isPreviewDelayActive", true);
        SetPrivateField(stage, "_isBgmFadingOut", true);
        SetPrivateField(stage, "_isBgmFadingIn", false);

        var result = InvokeCommand(stage, "CancelPreparedChart");

        Assert.True(result.Success);
        Assert.Same(sound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
        Assert.Same(instance.Object, GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
        Assert.Equal(1.5d, GetPrivateField<double>(stage, "_previewPlayDelay"));
        Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
        Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        sound.Verify(x => x.RemoveReference(), Times.Never);
        instance.Verify(x => x.Stop(), Times.Never);
        instance.Verify(x => x.Dispose(), Times.Never);
    }

    [Fact]
    public void PrepareVideoChart_WhenInvalidAndNoPreparation_ShouldPreserveInteractivePreview()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPrivateField(stage, "_previewPlayDelay", 2.5d);
        SetPrivateField(stage, "_isPreviewDelayActive", true);
        SetPrivateField(stage, "_isBgmFadingOut", true);
        SetPrivateField(stage, "_isBgmFadingIn", false);

        var result = InvokeCommand(stage, "PrepareVideoChart", " ");

        Assert.False(result.Success);
        Assert.Same(sound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
        Assert.Same(instance.Object, GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
        Assert.Equal(2.5d, GetPrivateField<double>(stage, "_previewPlayDelay"));
        Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
        Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        sound.Verify(x => x.RemoveReference(), Times.Never);
        instance.Verify(x => x.Stop(), Times.Never);
        instance.Verify(x => x.Dispose(), Times.Never);
    }

    [Fact]
    public void PrepareVideoChart_WhenInvalidAndPreparationExists_ShouldPreserveExistingPreparation()
    {
        var stage = CreateStage();
        var preparedSound = new Mock<ISound>();
        var preparedInstance = new Mock<ISoundInstance>();
        preparedInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSound", preparedSound.Object);
        SetPrivateField(stage, "_previewSoundInstance", preparedInstance.Object);
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-existing.dtx"));
        var existingSelection = MakePreparedSelection(node, 0);
        SetPrivateField(stage, "_preparedChartSelection", existingSelection);
        SetPreparedState(stage, "Playing");
        SetPrivateField(stage, "_preparedPreviewElapsedMs", 750d);

        var result = InvokeCommand(stage, "PrepareVideoChart", " ");

        Assert.False(result.Success);
        Assert.Same(existingSelection, GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("Playing", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        Assert.Same(preparedSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
        Assert.Same(preparedInstance.Object, GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
        Assert.Equal(750d, GetPrivateField<double>(stage, "_preparedPreviewElapsedMs"));
    }

    [Fact]
    public void Deactivate_ShouldCleanPreparedPreviewExactlyOnceAcrossRepeatedCalls()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPreparedState(stage, "Playing");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-deactivate.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        stage.Deactivate();
        stage.Deactivate();

        sound.Verify(x => x.RemoveReference(), Times.Once);
        instance.Verify(x => x.Stop(), Times.Once);
        instance.Verify(x => x.Dispose(), Times.Once);
        Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("None", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
    }

    [Fact]
    public void OnSongSelectionChanged_WhenLeavingPreparedRow_ShouldClearPreparation()
    {
        var stage = CreateStage();
        var prepared = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared.dtx"));
        var other = CreateNode("other", Path.Combine(Path.GetTempPath(), "other.dtx"));
        var sound = new Mock<ISound>();
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(prepared, 0));
        SetPreparedState(stage, "Prepared");
        AttachCoreUi(stage, display: new SongListDisplay());

        InvokePrivateMethod(
            stage,
            "OnSongSelectionChanged",
            GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
            new SongSelectionChangedEventArgs(other, 0, true));

        sound.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("None", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
    }

    [Fact]
    public void OnSongSelectionChanged_WhenProjectingPreparedSelection_ShouldPreservePreparation()
    {
        var stage = CreateStage();
        var prepared = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-projecting.dtx"));
        var sound = new Mock<ISound>();
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(prepared, 0));
        SetPreparedState(stage, "Prepared");
        SetPrivateField(stage, "_isProjectingPreparedSelection", true);
        AttachCoreUi(stage, display: new SongListDisplay());

        InvokePrivateMethod(
            stage,
            "OnSongSelectionChanged",
            GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
            new SongSelectionChangedEventArgs(prepared, 0, true));

        sound.Verify(x => x.RemoveReference(), Times.Never);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("Prepared", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
    }

    [Fact]
    public void OnDifficultyChanged_WhenLeavingPreparedDifficulty_ShouldClearAndLoadNormalPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-lifecycle-difficulty");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadSound(previewPath)).Returns(new Mock<ISound>().Object);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            var preparedSound = new Mock<ISound>();
            SetPrivateField(stage, "_previewSound", preparedSound.Object);
            SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
            SetPreparedState(stage, "Prepared");
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());

            InvokePrivateMethod(stage, "OnDifficultyChanged", stage, new DifficultyChangedEventArgs(node, 1));

            preparedSound.Verify(x => x.RemoveReference(), Times.Once);
            resourceManager.Verify(x => x.LoadSound(previewPath), Times.Once);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
            Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ActivatePreparedChart_WhenTransitionBlocked_ShouldPreservePreparation()
    {
        var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-activation-blocked.dtx"));
        stage.StageManager = stageManager.Object;
        SetPrivateField(stage, "_selectedSong", node);
        SetPrivateField(stage, "_currentDifficulty", 0);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.False(result.Success);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
    }

    [Fact]
    public void ActivatePreparedChart_WhenStageManagerIsTransitioning_ShouldPreservePreparation()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.IsTransitioning).Returns(true);
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-activation-transitioning.dtx"));
        stage.StageManager = stageManager.Object;
        SetPrivateField(stage, "_selectedSong", node);
        SetPrivateField(stage, "_currentDifficulty", 0);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.False(result.Success);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("Prepared", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
    }

    [Fact]
    public void ActivatePreparedChart_WhenEligible_ShouldCleanPreparationAndUseSongTransition()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-activation.dtx"), songId: 902);
        stage.StageManager = stageManager.Object;
        SetPrivateField(stage, "_selectedSong", node);
        SetPrivateField(stage, "_currentDifficulty", 0);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.True(result.Success);
        Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
        stageManager.Verify(
            x => x.ChangeStage(
                StageType.SongTransition,
                It.Is<IStageTransition>(transition => transition is InstantTransition),
                It.Is<Dictionary<string, object>>(data =>
                    ReferenceEquals(data["selectedSong"], node)
                    && (int)data["selectedDifficulty"] == 0
                    && (int)data["songId"] == 902)),
            Times.Once);
    }

    [Fact]
    public void StartPreparedPreview_WhenPlayingButInstanceStopped_ShouldMarkFailed()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Stopped);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPreparedState(stage, "Playing");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-stopped.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        Assert.Equal("The prepared preview stopped unexpectedly.", result.Error);
        Assert.Equal("Failed", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        sound.Verify(x => x.CreateInstance(), Times.Never);
    }

    [Fact]
    public void StartPreparedPreview_WhenStateIsNone_ShouldReturnNotReady()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPreparedState(stage, "None");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-none.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        Assert.Equal("The prepared chart preview is not ready to play.", result.Error);
        sound.Verify(x => x.CreateInstance(), Times.Never);
    }

    [Fact]
    public void StartPreparedPreview_WhenStateIsFailed_ShouldReturnNotReady()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPreparedState(stage, "Failed");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-failed.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        Assert.Equal("The prepared chart preview is not ready to play.", result.Error);
    }

    [Fact]
    public void StartPreparedPreview_WhenPreparedSelectionIsNull_ShouldReturnNoPreview()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        Assert.Equal("No prepared chart preview is available.", result.Error);
    }

    [Fact]
    public void StartPreparedPreview_WhenPreviewSoundIsNull_ShouldReturnNoPreview()
    {
        var stage = CreateStage();
        SetPreparedState(stage, "Prepared");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-no-sound.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        Assert.Equal("No prepared chart preview is available.", result.Error);
    }

    [Fact]
    public void StartPreparedPreview_WhenExistingInstancePresent_ShouldDisposeBeforeCreatingNew()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        sound.Setup(x => x.CreateInstance()).Returns((SoundEffectInstance)null!);
        var existingInstance = new Mock<ISoundInstance>();
        existingInstance.SetupGet(x => x.State).Returns(SoundState.Stopped);
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_previewSoundInstance", existingInstance.Object);
        SetPreparedState(stage, "Prepared");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-dispose-existing.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        existingInstance.Verify(x => x.Dispose(), Times.Once);
        Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
    }

    [Fact]
    public void StartPreparedPreview_WhenCreateInstanceThrows_ShouldMarkFailedAndDispose()
    {
        var stage = CreateStage();
        var sound = new Mock<ISound>();
        sound.Setup(x => x.CreateInstance()).Throws(new InvalidOperationException("device lost"));
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPreparedState(stage, "Prepared");
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-throw.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        Assert.Equal("The prepared chart preview could not be started.", result.Error);
        Assert.Equal("Failed", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
        Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
    }

    [Fact]
    public void ActivatePreparedChart_WhenNoPreparation_ShouldReturnNoChartAvailable()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.False(result.Success);
        Assert.Equal("No prepared chart is available.", result.Error);
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void ActivatePreparedChart_WhenSelectedSongDiffersFromPreparedNode_ShouldReturnNoLongerSelected()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var preparedNode = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-node.dtx"));
        var otherNode = CreateNode("other", Path.Combine(Path.GetTempPath(), "other-node.dtx"));
        SetPrivateField(stage, "_selectedSong", otherNode);
        SetPrivateField(stage, "_currentDifficulty", 0);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(preparedNode, 0));
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.False(result.Success);
        Assert.Equal("The prepared chart is no longer selected.", result.Error);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void ActivatePreparedChart_WhenDifficultyDiffersFromPrepared_ShouldReturnNoLongerSelected()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-diff.dtx"));
        SetPrivateField(stage, "_selectedSong", node);
        SetPrivateField(stage, "_currentDifficulty", 3);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.False(result.Success);
        Assert.Equal("The prepared chart is no longer selected.", result.Error);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
    }

    [Fact]
    public void ActivatePreparedChart_WhenStageManagerIsNull_ShouldReturnTransitionUnavailable()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        stage.StageManager = null;
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-no-sm.dtx"));
        SetPrivateField(stage, "_selectedSong", node);
        SetPrivateField(stage, "_currentDifficulty", 0);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.False(result.Success);
        Assert.Equal("The song transition manager is unavailable.", result.Error);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
    }

    [Fact]
    public void ActivatePreparedChart_WhenSelectedSongFromDisplayMatches_ShouldSucceed()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-display.dtx"), songId: 904);
        var display = new SongListDisplay { CurrentList = new List<SongListNode> { node } };
        display.SetSelection(0, 0);
        SetPrivateField(stage, "_songListDisplay", display);
        SetPrivateField(stage, "_selectedSong", null);
        SetPrivateField(stage, "_currentDifficulty", 0);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPreparedState(stage, "Prepared");

        var result = InvokeCommand(stage, "ActivatePreparedChart");

        Assert.True(result.Success);
        Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
        stageManager.Verify(
            x => x.ChangeStage(StageType.SongTransition, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Once);
    }

    [Fact]
    public void PopulateTelemetry_WhenChartIdIsZero_ShouldReportRelativePathIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-telemetry-relative");
        var chartPath = Path.Combine(root, "sub", "telemetry.dtx");
        var stage = CreateStage();
        var chart = new SongChart
        {
            Id = 0,
            FilePath = chartPath,
            PreviewFile = "preview.wav",
            HasDrumChart = true,
            DrumLevel = 5
        };
        var song = new SongEntity { Id = 0, Title = "telemetry", Charts = new List<SongChart> { chart } };
        chart.Song = song;
        chart.SongId = 0;
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Title = "telemetry",
            DatabaseSongId = 0,
            DatabaseSong = song,
            DatabaseChart = chart,
            Scores = new[] { new SongScore { ChartId = 0, Instrument = EInstrumentPart.DRUMS } }
        };
        SetPrivateField(stage, "_appliedLibrarySnapshot", new SongLibrarySnapshot(
            version: 1,
            rootSongs: new[] { node },
            activeRoots: new[] { Path.GetFullPath(root) },
            enumeratedFileCount: 1,
            discoveredScoreCount: 1));
        var identity = (string)InvokePrivateMethod(stage, "BuildPreparedChartTelemetryIdentity", chart)!;

        Assert.Equal("sub/telemetry.dtx", identity);
        Assert.DoesNotContain(Path.GetFullPath(chartPath), identity, StringComparison.Ordinal);
    }

    [Fact]
    public void PopulateTelemetry_WhenChartIdIsZeroAndNoActiveRootMatch_ShouldReportEmptyIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-telemetry-no-root");
        var otherRoot = Path.Combine(Path.GetTempPath(), "hpa510-telemetry-other-root");
        var chartPath = Path.Combine(otherRoot, "telemetry.dtx");
        var stage = CreateStage();
        var chart = new SongChart
        {
            Id = 0,
            FilePath = chartPath,
            PreviewFile = "preview.wav",
            HasDrumChart = true,
            DrumLevel = 5
        };
        var song = new SongEntity { Id = 0, Title = "telemetry", Charts = new List<SongChart> { chart } };
        chart.Song = song;
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Title = "telemetry",
            DatabaseSong = song,
            DatabaseChart = chart,
        };
        SetPrivateField(stage, "_appliedLibrarySnapshot", new SongLibrarySnapshot(
            version: 1,
            rootSongs: new[] { node },
            activeRoots: new[] { Path.GetFullPath(root) },
            enumeratedFileCount: 1,
            discoveredScoreCount: 1));
        var identity = (string)InvokePrivateMethod(stage, "BuildPreparedChartTelemetryIdentity", chart)!;

        Assert.Equal("", identity);
    }

    [Fact]
    public void PopulateTelemetry_WhenChartIdIsZeroAndNoSnapshot_ShouldReportEmptyIdentity()
    {
        var stage = CreateStage();
        var chart = new SongChart
        {
            Id = 0,
            FilePath = Path.Combine(Path.GetTempPath(), "telemetry.dtx"),
            PreviewFile = "preview.wav",
        };

        var identity = (string)InvokePrivateMethod(stage, "BuildPreparedChartTelemetryIdentity", chart)!;

        Assert.Equal("", identity);
    }

    [Fact]
    public void PrepareVideoChart_WhenPreviewPathContainsInvalidChars_ShouldReturnPreviewNotFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-invalid-chars");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        File.WriteAllText(chartPath, "chart");

        try
        {
            var chart = new SongChart
            {
                Id = 701,
                FilePath = chartPath,
                PreviewFile = "pre\0view.wav",
                HasDrumChart = true,
                DrumLevel = 5
            };
            var song = new SongEntity { Id = 701, Title = "invalid", Charts = new List<SongChart> { chart } };
            chart.Song = song;
            chart.SongId = 701;
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = "invalid",
                DatabaseSongId = 701,
                DatabaseSong = song,
                DatabaseChart = chart,
                Scores = new[] { new SongScore { ChartId = 701, Instrument = EInstrumentPart.DRUMS } }
            };
            var stage = CreatePreparedStage(root, node, new Mock<IResourceManager>().Object);

            var result = InvokeCommand(stage, "PrepareVideoChart", chartPath);

            Assert.False(result.Success);
            Assert.Equal("The requested chart does not declare a preview file.", result.Error);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DisposePreviewInstance_WhenDisposeThrows_ShouldSwallowExceptionAndClearInstance()
    {
        var stage = CreateStage();
        var instance = new Mock<ISoundInstance>();
        instance.Setup(x => x.Dispose()).Throws(new InvalidOperationException("disposed twice"));
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);

        InvokePrivateMethod(stage, "DisposePreviewInstance");

        Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
        instance.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public void DisposePreviewInstance_WhenInstanceIsNull_ShouldBeNoOp()
    {
        var stage = CreateStage();
        SetPrivateField(stage, "_previewSoundInstance", null);

        InvokePrivateMethod(stage, "DisposePreviewInstance");

        Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
    }

    [Fact]
    public void OnSongSelectionChanged_WhenStayingOnPreparedRow_ShouldPreservePreparationAndNotLoadPreview()
    {
        var stage = CreateStage();
        var prepared = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-stay.dtx"));
        var sound = new Mock<ISound>();
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(prepared, 0));
        SetPreparedState(stage, "Prepared");
        AttachCoreUi(stage, display: new SongListDisplay());

        InvokePrivateMethod(
            stage,
            "OnSongSelectionChanged",
            GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
            new SongSelectionChangedEventArgs(prepared, 0, true));

        sound.Verify(x => x.RemoveReference(), Times.Never);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("Prepared", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
    }

    [Fact]
    public void OnDifficultyChanged_WhenStayingOnPreparedDifficulty_ShouldPreservePreparation()
    {
        var stage = CreateStage();
        var prepared = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "prepared-diff-stay.dtx"));
        var sound = new Mock<ISound>();
        SetPrivateField(stage, "_previewSound", sound.Object);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(prepared, 0));
        SetPreparedState(stage, "Prepared");
        AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());

        InvokePrivateMethod(stage, "OnDifficultyChanged", stage, new DifficultyChangedEventArgs(prepared, 0));

        sound.Verify(x => x.RemoveReference(), Times.Never);
        Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        Assert.Equal("Prepared", GetPrivateField<object>(stage, "_preparedPreviewState")?.ToString());
    }

    [Fact]
    public void UpdatePreviewSoundTimers_WhenPlayingWithNegativeDelta_ShouldClampToZero()
    {
        var stage = CreateStage();
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        SetPrivateField(stage, "_previewSoundInstance", instance.Object);
        SetPreparedState(stage, "Playing");
        SetPrivateField(stage, "_preparedPreviewElapsedMs", 100d);

        InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", -0.5d);

        Assert.Equal(100d, GetPrivateField<double>(stage, "_preparedPreviewElapsedMs"));
    }

    [Fact]
    public void SelectSong_WhenSongNodeIsNull_ShouldNotTransition()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        InvokePrivateMethod(stage, "SelectSong", (SongListNode?)null);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void SelectSong_WhenSongNodeIsBox_ShouldNotTransition()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var box = new SongListNode { Type = NodeType.Box, Title = "box" };

        InvokePrivateMethod(stage, "SelectSong", box);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void SelectSong_WhenStageManagerIsNull_ShouldNotTransition()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateStage(game);
        stage.StageManager = null;
        var node = CreateNode("song", Path.Combine(Path.GetTempPath(), "select-no-sm.dtx"));

        var exception = Record.Exception(() => InvokePrivateMethod(stage, "SelectSong", node));

        Assert.Null(exception);
    }

    [Fact]
    public void OnSongSelectionChanged_WhenNoPreparationAndScoreSelected_ShouldStopAndLoadPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-no-prep");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("song", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            resourceManager.Setup(x => x.LoadSound(previewPath)).Returns(sound.Object);
            var stage = CreateStage();
            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            SetPrivateField(stage, "_preparedChartSelection", null);
            SetPreparedState(stage, "None");
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(node, 0, true));

            // With no prepared selection, the normal path should load the preview sound
            resourceManager.Verify(x => x.LoadSound(previewPath), Times.Once);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PopulateTelemetry_ShouldReportPreparedIdentityStateAndElapsedWithoutAbsolutePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa510-telemetry");
        var chartPath = Path.Combine(root, "telemetry.dtx");
        var stage = CreateStage();
        var node = CreateNode("telemetry", chartPath, songId: 903);
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));
        SetPreparedState(stage, "Playing");
        SetPrivateField(stage, "_preparedPreviewElapsedMs", 1234.5d);
        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal("chart:903", GetTelemetryProperty<string>(telemetry, "PreparedChartIdentity"));
        Assert.Equal("Playing", GetTelemetryProperty<string>(telemetry, "PreparedPreviewState"));
        Assert.Equal(1234.5d, GetTelemetryProperty<double?>(telemetry, "PreparedPreviewElapsedMs"));
        Assert.DoesNotContain(Path.GetFullPath(chartPath), GetTelemetryProperty<string>(telemetry, "PreparedChartIdentity") ?? "", StringComparison.Ordinal);
    }

    private static SongSelectionStage CreatePreparedStage(string root, SongListNode node, IResourceManager resourceManager)
    {
        var stage = CreateStage(CreateGame(totalGameTime: 2d));
        AttachCoreUi(stage, display: new SongListDisplay { CurrentList = new List<SongListNode> { node } });
        SetPrivateField(stage, "_resourceManager", resourceManager);
        SetPrivateField(stage, "_currentSongList", new List<SongListNode> { node });
        SetPrivateField(stage, "_appliedLibrarySnapshot", new SongLibrarySnapshot(
            version: 1,
            rootSongs: new[] { node },
            activeRoots: new[] { Path.GetFullPath(root) },
            enumeratedFileCount: 1,
            discoveredScoreCount: 1));
        return stage;
    }

    private static SongListNode CreateNode(string title, string chartPath, string previewFile = "preview.wav", int songId = 901)
    {
        var chart = new SongChart
        {
            Id = songId,
            FilePath = chartPath,
            PreviewFile = previewFile,
            HasDrumChart = true,
            DrumLevel = 5
        };
        var song = new SongEntity { Id = songId, Title = title, Charts = new List<SongChart> { chart } };
        chart.Song = song;
        chart.SongId = songId;
        return new SongListNode
        {
            Type = NodeType.Score,
            Title = title,
            DatabaseSongId = songId,
            DatabaseSong = song,
            DatabaseChart = chart,
            Scores = new[] { new SongScore { ChartId = songId, Instrument = EInstrumentPart.DRUMS } }
        };
    }

    private static object MakePreparedSelection(SongListNode node, int difficulty)
    {
        var field = GetField(typeof(SongSelectionStage), "_preparedChartSelection");
        Assert.NotNull(field);
        var selectionType = field!.FieldType;
        var chart = node.DatabaseChart!;
        return Activator.CreateInstance(selectionType, node, chart, difficulty, $"chart:{chart.Id}")!;
    }

    private static void SetPreparedState(SongSelectionStage stage, string state)
    {
        var field = GetField(typeof(SongSelectionStage), "_preparedPreviewState");
        Assert.NotNull(field);
        SetPrivateField(stage, "_preparedPreviewState", Enum.Parse(field!.FieldType, state));
    }

    private static (bool Success, string? Error) InvokeCommand(SongSelectionStage stage, string methodName, params object[] args)
    {
        var value = InvokePrivateMethod(stage, methodName, args);
        Assert.NotNull(value);
        var success = (bool)value!.GetType().GetField("Item1")!.GetValue(value)!;
        var error = (string?)value.GetType().GetField("Item2")!.GetValue(value);
        return (success, error);
    }

    private static T? GetTelemetryProperty<T>(GameTelemetrySnapshot telemetry, string propertyName)
    {
        return (T?)typeof(GameTelemetrySnapshot).GetProperty(propertyName)?.GetValue(telemetry);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
