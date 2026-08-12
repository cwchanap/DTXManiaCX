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
    public void PrepareVideoChart_UsesResolvedChartPreviewAndLeavesPreviewStopped()
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
    public void PrepareVideoChart_WhenReplacingPlayingInteractivePreview_StopsOldInstanceBeforePublishingPrepared()
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
    public void PrepareVideoChart_WhenDeclarationFileOrLoadIsInvalid_ClearsPreparation()
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
    public void PrepareVideoChart_WhenRequestedPreviewLoadFails_RestoresPrimaryDelayedPreviewWithoutPreparation()
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
    public void PreparedPreview_DoesNotAutoStartAfterNormalDelay()
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
    public void StartPreparedPreview_WhenAlreadyPlaying_IsIdempotent()
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
    public void StartPreparedPreview_WhenInstanceCannotBeCreated_MarksPreviewFailed()
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
    public void UpdatePreparedPreview_WhenPlaying_AccumulatesOnlyActualPlaybackAndFailsOnStop()
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
    public void CancelPreparedChart_IsIdempotentAndReleasesPreviewExactlyOnce()
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
    public void CancelPreparedChart_WhenNoPreparationPreservesInteractivePreview()
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
    public void PrepareVideoChart_WhenInvalidAndNoPreparationPreservesInteractivePreview()
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
    public void Deactivate_CleansPreparedPreviewExactlyOnceAcrossRepeatedCalls()
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
    public void OnSongSelectionChanged_WhenLeavingPreparedRow_ClearsPreparation()
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
    public void OnSongSelectionChanged_WhenProjectingPreparedSelection_PreservesPreparation()
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
    public void OnDifficultyChanged_WhenLeavingPreparedDifficulty_ClearsAndLoadsNormalPreview()
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
    public void ActivatePreparedChart_WhenTransitionBlocked_PreservesPreparation()
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
    public void ActivatePreparedChart_WhenStageManagerIsTransitioning_PreservesPreparation()
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
    public void ActivatePreparedChart_WhenEligible_CleansPreparationAndUsesSongTransition()
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
    public void PopulateTelemetry_ReportsPreparedIdentityStateAndElapsedWithoutAbsolutePath()
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
