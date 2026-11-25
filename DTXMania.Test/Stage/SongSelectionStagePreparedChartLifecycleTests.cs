using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.SongSelection;
using Microsoft.Xna.Framework.Audio;
using Moq;
using Xunit;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;
[Trait("Category", "Unit")]
public sealed class SongSelectionStagePreparedChartLifecycleTests
{
    // HPA-802 review round 1: backward difficulty cycling (status-panel Left) must
    // route through the same prepared cleanup as OnDifficultyChanged — the manual
    // path used to change the difficulty while the prepared preview kept playing.
    [Fact]
    public void CycleDifficulty_WhenBackwardCyclingLeavesPreparedDifficulty_ShouldClearPreparationAndRestoreOrdinaryPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-cycle-back");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            node.Scores = new[]
            {
                node.Scores![0],
                new SongScore { ChartId = node.DatabaseChart!.Id + 1, Instrument = EInstrumentPart.DRUMS }
            };
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            var soundObj = sound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());
            SetPrivateField(stage, "_selectedSong", node);
            SetPrivateField(stage, "_currentDifficulty", 0);

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());

            InvokePrivateMethod(stage, "CycleDifficulty", -1);

            Assert.Equal(1, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());
            sound.Verify(x => x.RemoveReference(), Times.Once);

            // The ordinary delayed preview of the (still selected) song is restored.
            PreviewControllerOf(stage).Update(1.5d);
            sound.Verify(x => x.CreateInstance(), Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 review round 1 companion: without a preparation, backward difficulty
    // cycling must not disturb the ordinary interactive preview.
    [Fact]
    public void CycleDifficulty_WhenBackwardCyclingWithoutPreparation_ShouldKeepOrdinaryPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-cycle-back-ordinary");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("song", chartPath, previewFile: "preview.wav");
            node.Scores = new[]
            {
                node.Scores![0],
                new SongScore { ChartId = node.DatabaseChart!.Id + 1, Instrument = EInstrumentPart.DRUMS }
            };
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            var soundObj = sound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            var stage = CreateStage();
            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());
            SetPrivateField(stage, "_selectedSong", node);
            SetPrivateField(stage, "_currentDifficulty", 0);
            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(node, 0, true));

            InvokePrivateMethod(stage, "CycleDifficulty", -1);

            Assert.Equal(1, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());
            sound.Verify(x => x.RemoveReference(), Times.Never);

            // The armed ordinary delay survived the difficulty change.
            PreviewControllerOf(stage).Update(1.5d);
            sound.Verify(x => x.CreateInstance(), Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 review round 1: the stage must assert the two-way pairing between its
    // prepared record and the controller's playback state at stable operation
    // boundaries — a record exists exactly when the state is not None.
    [Fact]
    public void StageOperation_WhenRecordSurvivesWithoutPreparedState_ShouldFailFast()
    {
        var stage = CreateStage();
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "hpa802-invariant-record.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var exception = Assert.Throws<TargetInvocationException>(
            () => InvokePrivateMethod(stage, "OnUpdate", 0.016));
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("inconsistent", inner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StageOperation_WhenPreparedStateSurvivesWithoutRecord_ShouldFailFast()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-invariant-state");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var soundObj = new Mock<ISound>().Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);

            // Drop only the stage-side identity, leaving the controller Prepared.
            SetPrivateField(stage, "_preparedChartSelection", null);

            var exception = Assert.Throws<TargetInvocationException>(
                () => InvokeCommand(stage, "CancelPreparedChart"));
            var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("inconsistent", inner.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 review round 2: every exit of OnSongSelectionChanged is a stable
    // boundary — the scroll-incomplete early return must fail fast on a broken
    // pairing too, not only the scroll-complete path.
    [Fact]
    public void StageOperation_WhenScrollIncompleteExitsWithBrokenPairing_ShouldFailFast()
    {
        var stage = CreateStage();
        AttachCoreUi(stage);
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "hpa802-invariant-scroll.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var exception = Assert.Throws<TargetInvocationException>(
            () => InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(node, 0, false)));
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("inconsistent", inner.Message, StringComparison.Ordinal);
    }

    // HPA-802 review round 2: the invariant must stay enforced on stable failure
    // exits of PrepareVideoChart — and because validation precedes teardown, a
    // request rejected before teardown leaves an injected inconsistency intact
    // for that boundary to catch.
    [Fact]
    public void PrepareVideoChart_WhenPairingBrokenAndRequestInvalid_ShouldFailFastAtStableFailureExit()
    {
        var stage = CreateStage();
        AttachCoreUi(stage);
        var node = CreateNode("prepared", Path.Combine(Path.GetTempPath(), "hpa802-invariant-invalid.dtx"));
        SetPrivateField(stage, "_preparedChartSelection", MakePreparedSelection(node, 0));

        var exception = Assert.Throws<TargetInvocationException>(
            () => InvokeCommand(stage, "PrepareVideoChart", "not-in-library.dtx"));
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("inconsistent", inner.Message, StringComparison.Ordinal);
    }

    // HPA-802 review round 1: the unexpected-stop boundary must keep the pairing
    // legal (record + Failed) — the stage identity survives the stop.
    [Fact]
    public void StageOperation_WhenPlaybackStopsUnexpectedly_ShouldKeepFailedPairedWithIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-invariant-failed");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var preparedInstance = new Mock<ISoundInstance>();
            preparedInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            var stage = CreatePreparedStage(
                root,
                node,
                resourceManager.Object,
                sound => ReferenceEquals(sound, preparedSound.Object) ? preparedInstance.Object : null);

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            Assert.True(InvokeCommand(stage, "StartPreparedPreview").Success);

            // The instance stops outside the stage's control; OnUpdate must keep the
            // Failed state paired with the record instead of failing the invariant.
            preparedInstance.SetupGet(x => x.State).Returns(SoundState.Stopped);
            InvokePrivateMethod(stage, "OnUpdate", 0.016);

            Assert.Equal("Failed", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.Equal("chart:901", GetTelemetryProperty<string>(telemetry, "PreparedChartIdentity"));
            Assert.Equal("Failed", GetTelemetryProperty<string>(telemetry, "PreparedPreviewState"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 review round 1: the removal regressions must drive the real
    // publication/reconciliation path (ReconcileLibrarySnapshot), not the cleanup
    // helper directly, so they fail if reconciliation stops routing removals
    // through the prepared cleanup.
    [Fact]
    public void ReconcileLibrarySnapshot_WhenPreparedChartIsRemoved_ShouldClearIdentityAndPlaybackTogether()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-reconcile-removal");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var preparedInstance = new Mock<ISoundInstance>();
            preparedInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            var stage = CreatePreparedStage(
                root,
                node,
                resourceManager.Object,
                sound => ReferenceEquals(sound, preparedSound.Object) ? preparedInstance.Object : null);

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            Assert.True(InvokeCommand(stage, "StartPreparedPreview").Success);
            Assert.Equal("Playing", PreviewControllerOf(stage).PreparedState.ToString());

            // A publication replaces the tree without the prepared chart.
            var replacement = Score("Replacement", 902, Path.Combine(root, "replacement.dtx"));
            var snapshot = new SongLibrarySnapshot(
                version: 2,
                rootSongs: new[] { replacement },
                activeRoots: new[] { Path.GetFullPath(root) },
                enumeratedFileCount: 1,
                discoveredScoreCount: 1);

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", snapshot);

            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.Equal(0d, PreviewControllerOf(stage).PlayedMs);
            preparedSound.Verify(x => x.RemoveReference(), Times.Once);
            preparedInstance.Verify(x => x.Stop(), Times.Once);
            preparedInstance.Verify(x => x.Dispose(), Times.Once);

            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.Null(GetTelemetryProperty<string>(telemetry, "PreparedChartIdentity"));
            Assert.Equal("None", GetTelemetryProperty<string>(telemetry, "PreparedPreviewState"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 regression companion: after reconciliation removed the prepared chart,
    // a subsequent ordinary selection must still arm the ordinary delayed preview
    // (and it must start after the delay — no stale prepared state blocks it).
    [Fact]
    public void ReconcileLibrarySnapshot_AfterPreparedChartRemoved_ThenOrdinarySelection_ShouldRestartDelayedPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-reconcile-ordinary");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var ordinaryPath = Path.Combine(root, "ordinary.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(ordinaryPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);

            var replacement = Score("Replacement", 902, Path.Combine(root, "replacement.dtx"));
            var snapshot = new SongLibrarySnapshot(
                version: 2,
                rootSongs: new[] { replacement },
                activeRoots: new[] { Path.GetFullPath(root) },
                enumeratedFileCount: 1,
                discoveredScoreCount: 1);
            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", snapshot);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());

            // The next ordinary selection loads and starts its delayed preview.
            var ordinaryPreviewPath = Path.Combine(root, "ordinary.wav");
            File.WriteAllText(ordinaryPreviewPath, "preview");
            var ordinarySound = new Mock<ISound>();
            var ordinarySoundObj = ordinarySound.Object;
            resourceManager.Setup(x => x.TryLoadSound(ordinaryPreviewPath, out ordinarySoundObj)).Returns(true);
            var ordinaryNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "ordinary",
                DatabaseChart = new SongChart
                {
                    FilePath = ordinaryPath,
                    PreviewFile = "ordinary.wav",
                    HasDrumChart = true,
                    DrumLevel = 5
                }
            };
            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(ordinaryNode, 0, true));

            resourceManager.Verify(
                x => x.TryLoadSound(ordinaryPreviewPath, out It.Ref<ISound>.IsAny),
                Times.Once);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());

            PreviewControllerOf(stage).Update(1.5d);
            ordinarySound.Verify(x => x.CreateInstance(), Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
    }

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
            var preparedSoundObj = loadedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(preparedPreview, out preparedSoundObj)).Returns(true);
            var createCalls = 0;
            var stage = CreatePreparedStage(root, node, resourceManager.Object, _ =>
            {
                createCalls++;
                return null;
            });

            var result = InvokeCommand(stage, "PrepareVideoChart", preparedPath);

            Assert.True(result.Success);
            resourceManager.Verify(x => x.TryLoadSound(preparedPreview, out It.Ref<ISound>.IsAny), Times.Once);
            resourceManager.Verify(x => x.TryLoadSound(primaryPreview, out It.Ref<ISound>.IsAny), Times.Never);
            Assert.True(PreviewControllerOf(stage).HasLoadedSound);
            Assert.Equal(0, createCalls);
            Assert.Equal(0d, PreviewControllerOf(stage).PlayedMs);
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
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
        var interactivePath = Path.Combine(root, "interactive.dtx");
        var interactivePreviewPath = Path.Combine(root, "interactive.wav");
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "prepared.wav");
        File.WriteAllText(interactivePath, "chart");
        File.WriteAllText(interactivePreviewPath, "preview");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var preparedNode = CreateNode("prepared", chartPath, previewFile: "prepared.wav");
            var interactiveNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "interactive",
                DatabaseChart = new SongChart
                {
                    FilePath = interactivePath,
                    PreviewFile = "interactive.wav",
                    HasDrumChart = true,
                    DrumLevel = 5
                }
            };
            var resourceManager = new Mock<IResourceManager>();
            var interactiveSound = new Mock<ISound>();
            var interactiveSoundObj = interactiveSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(interactivePreviewPath, out interactiveSoundObj)).Returns(true);
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var interactiveInstance = new Mock<ISoundInstance>();
            interactiveInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            var stage = CreatePreparedStage(
                root,
                preparedNode,
                resourceManager.Object,
                sound => ReferenceEquals(sound, interactiveSound.Object) ? interactiveInstance.Object : null);
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());
            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(interactiveNode, 0, true));

            // The interactive delayed preview starts and plays.
            PreviewControllerOf(stage).Update(1.5d);
            interactiveInstance.Verify(x => x.Play(), Times.Once);

            var result = InvokeCommand(stage, "PrepareVideoChart", chartPath);

            Assert.True(result.Success);
            interactiveInstance.Verify(x => x.Stop(), Times.Once);
            interactiveInstance.Verify(x => x.Dispose(), Times.Once);
            interactiveSound.Verify(x => x.RemoveReference(), Times.Once);
            preparedSound.Verify(x => x.RemoveReference(), Times.Never);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 review round 1: re-preparing while a prepared preview is playing must
    // dispose the old instance before republishing — the controller is single-voice.
    [Fact]
    public void PrepareVideoChart_WhenReplacingPlayingPreparedPreview_ShouldDisposeOldInstanceAndRepublish()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-replace-prepared");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var preparedInstance = new Mock<ISoundInstance>();
            preparedInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            var stage = CreatePreparedStage(
                root,
                node,
                resourceManager.Object,
                sound => ReferenceEquals(sound, preparedSound.Object) ? preparedInstance.Object : null);

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            Assert.True(InvokeCommand(stage, "StartPreparedPreview").Success);
            Assert.Equal("Playing", PreviewControllerOf(stage).PreparedState.ToString());

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);

            preparedInstance.Verify(x => x.Stop(), Times.Once);
            preparedInstance.Verify(x => x.Dispose(), Times.Once);
            preparedSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.Equal(0d, PreviewControllerOf(stage).PlayedMs);
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
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out It.Ref<ISound>.IsAny)).Throws(new InvalidDataException("bad preview"));

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
            Assert.Equal("None", PreviewControllerOf(loadFailureStage).PreparedState.ToString());
            Assert.Null(GetPrivateField<object>(loadFailureStage, "_preparedChartSelection"));
            Assert.False(PreviewControllerOf(loadFailureStage).HasLoadedSound);
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
            resourceManager.Setup(x => x.TryLoadSound(requestedPreviewPath, out It.Ref<ISound>.IsAny))
                .Throws(new InvalidDataException("requested preview is invalid"));
            var primarySoundObj = primarySound.Object;
            resourceManager.Setup(x => x.TryLoadSound(primaryPreviewPath, out primarySoundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);

            var result = InvokeCommand(stage, "PrepareVideoChart", requestedPath);

            Assert.False(result.Success);
            resourceManager.Verify(x => x.TryLoadSound(requestedPreviewPath, out It.Ref<ISound>.IsAny), Times.Once);
            resourceManager.Verify(x => x.TryLoadSound(primaryPreviewPath, out It.Ref<ISound>.IsAny), Times.Once);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.True(PreviewControllerOf(stage).HasLoadedSound);

            // The restored primary preview arms its ordinary delayed start.
            PreviewControllerOf(stage).Update(1.5d);
            primarySound.Verify(x => x.CreateInstance(), Times.Once);
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
            var soundObj = sound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            var createCalls = 0;
            var stage = CreatePreparedStage(
                root,
                CreateNode("song", chartPath, previewFile: "preview.wav"),
                resourceManager.Object,
                _ =>
                {
                    createCalls++;
                    return null;
                });

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            PreviewControllerOf(stage).Update(10d);

            Assert.Equal(0, createCalls);
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StartPreparedPreview_WhenAlreadyPlaying_ShouldBeIdempotent()
    {
        var (stage, instance) = CreateReallyPreparedStageWithPlayingInstance(
            Path.Combine(Path.GetTempPath(), "hpa802-start-idempotent"),
            out var root,
            out var preparedSound);

        try
        {
            Assert.Equal("Playing", PreviewControllerOf(stage).PreparedState.ToString());

            var result = InvokeCommand(stage, "StartPreparedPreview");

            Assert.True(result.Success);
            Assert.Null(result.Error);
            preparedSound.Verify(x => x.CreateInstance(), Times.Never);
            instance.Verify(x => x.Play(), Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StartPreparedPreview_WhenInstanceCannotBeCreated_ShouldMarkPreviewFailed()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-start-null-instance");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var soundObj = new Mock<ISound>().Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            // No seam override: the default test seam cannot create an instance.
            var stage = CreatePreparedStage(root, node, resourceManager.Object);

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);

            var result = InvokeCommand(stage, "StartPreparedPreview");

            Assert.False(result.Success);
            Assert.Equal("Failed", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void UpdatePreparedPreview_WhenPlaying_ShouldAccumulateOnlyActualPlaybackAndFailOnStop()
    {
        var (stage, instance) = CreateReallyPreparedStageWithPlayingInstance(
            Path.Combine(Path.GetTempPath(), "hpa802-update-playing"),
            out var root,
            out _);

        try
        {
            PreviewControllerOf(stage).Update(0.5d);
            Assert.Equal(500d, PreviewControllerOf(stage).PlayedMs);

            instance.SetupGet(x => x.State).Returns(SoundState.Stopped);
            PreviewControllerOf(stage).Update(1d);

            Assert.Equal(500d, PreviewControllerOf(stage).PlayedMs);
            Assert.Equal("Failed", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 review round 2: cancellation is exercised while the prepared preview
    // is actually playing so the instance stop/dispose and the release are verified
    // on real objects, as in the pre-extraction suite.
    [Fact]
    public void CancelPreparedChart_ShouldBeIdempotentAndReleasePreviewExactlyOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-cancel-idempotent");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            var soundObj = sound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            var preparedInstance = new Mock<ISoundInstance>();
            preparedInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            var stage = CreatePreparedStage(
                root,
                node,
                resourceManager.Object,
                loadedSound => ReferenceEquals(loadedSound, sound.Object) ? preparedInstance.Object : null);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            Assert.True(InvokeCommand(stage, "StartPreparedPreview").Success);
            Assert.Equal("Playing", PreviewControllerOf(stage).PreparedState.ToString());

            Assert.True(InvokeCommand(stage, "CancelPreparedChart").Success);
            Assert.True(InvokeCommand(stage, "CancelPreparedChart").Success);

            preparedInstance.Verify(x => x.Stop(), Times.Once);
            preparedInstance.Verify(x => x.Dispose(), Times.Once);
            sound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.Equal(0d, PreviewControllerOf(stage).PlayedMs);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // HPA-802 review round 2: the ordinary preview is actually playing when the
    // (preparation-less) cancellation arrives — an already-playing instance must
    // be left untouched, as the pre-extraction suite verified.
    [Fact]
    public void CancelPreparedChart_WhenNoPreparation_ShouldPreserveInteractivePreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-cancel-ordinary");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var sound = new Mock<ISound>();
            var playingInstance = new Mock<ISoundInstance>();
            playingInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            var stage = CreateStageWithArmedInteractivePreview(
                root,
                chartPath,
                previewPath,
                sound,
                loadedSound => ReferenceEquals(loadedSound, sound.Object) ? playingInstance.Object : null);

            // The armed ordinary delay actually starts the preview first.
            PreviewControllerOf(stage).Update(1.5d);
            playingInstance.Verify(x => x.Play(), Times.Once);

            Assert.True(InvokeCommand(stage, "CancelPreparedChart").Success);

            // The already-playing ordinary instance is untouched: no stop, no
            // dispose, no release.
            sound.Verify(x => x.RemoveReference(), Times.Never);
            playingInstance.Verify(x => x.Stop(), Times.Never);
            playingInstance.Verify(x => x.Dispose(), Times.Never);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareVideoChart_WhenInvalidAndNoPreparation_ShouldPreserveInteractivePreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-invalid-ordinary");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var sound = new Mock<ISound>();
            var stage = CreateStageWithArmedInteractivePreview(root, chartPath, previewPath, sound);

            var result = InvokeCommand(stage, "PrepareVideoChart", " ");

            Assert.False(result.Success);
            sound.Verify(x => x.RemoveReference(), Times.Never);
            PreviewControllerOf(stage).Update(1.5d);
            sound.Verify(x => x.CreateInstance(), Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PrepareVideoChart_WhenInvalidAndPreparationExists_ShouldPreserveExistingPreparation()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-invalid-prepared");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var preparedInstance = new Mock<ISoundInstance>();
            preparedInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            var stage = CreatePreparedStage(
                root,
                node,
                resourceManager.Object,
                sound => ReferenceEquals(sound, preparedSound.Object) ? preparedInstance.Object : null);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            Assert.True(InvokeCommand(stage, "StartPreparedPreview").Success);
            PreviewControllerOf(stage).Update(0.25d);

            var result = InvokeCommand(stage, "PrepareVideoChart", " ");

            Assert.False(result.Success);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("Playing", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.Equal(250d, PreviewControllerOf(stage).PlayedMs);
            preparedSound.Verify(x => x.RemoveReference(), Times.Never);
            preparedInstance.Verify(x => x.Stop(), Times.Never);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Deactivate_ShouldCleanPreparedPreviewExactlyOnceAcrossRepeatedCalls()
    {
        var (stage, instance) = CreateReallyPreparedStageWithPlayingInstance(
            Path.Combine(Path.GetTempPath(), "hpa802-deactivate-prepared"),
            out var root,
            out _);

        try
        {
            stage.Deactivate();
            stage.Deactivate();

            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());
            instance.Verify(x => x.Stop(), Times.Once);
            instance.Verify(x => x.Dispose(), Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void OnSongSelectionChanged_WhenLeavingPreparedRow_ShouldClearPreparation()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-leave-row");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var other = CreateNode("other", Path.Combine(root, "other.dtx"));
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            AttachCoreUi(stage, display: new SongListDisplay());

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(other, 0, true));

            preparedSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void OnSongSelectionChanged_WhenProjectingPreparedSelection_ShouldPreservePreparation()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-projecting-row");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            AttachCoreUi(stage, display: new SongListDisplay());
            SetPrivateField(stage, "_isProjectingPreparedSelection", true);

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(node, 0, true));

            preparedSound.Verify(x => x.RemoveReference(), Times.Never);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
        }
        finally
        {
            TryDelete(root);
        }
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
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());

            InvokePrivateMethod(stage, "OnDifficultyChanged", stage, new DifficultyChangedEventArgs(node, 1));

            preparedSound.Verify(x => x.RemoveReference(), Times.Once);
            resourceManager.Verify(x => x.TryLoadSound(previewPath, out It.Ref<ISound>.IsAny), Times.Exactly(2));
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("None", PreviewControllerOf(stage).PreparedState.ToString());

            // The ordinary delayed preview for the song is restored.
            PreviewControllerOf(stage).Update(1.5d);
            preparedSound.Verify(x => x.CreateInstance(), Times.Once);
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
        var stage = CreateReallyPreparedStage(game, Path.Combine(Path.GetTempPath(), "hpa802-activate-blocked"), out var root);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        try
        {
            var result = InvokeCommand(stage, "ActivatePreparedChart");

            Assert.False(result.Success);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Never);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ActivatePreparedChart_WhenStageManagerIsTransitioning_ShouldPreservePreparation()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateReallyPreparedStage(game, Path.Combine(Path.GetTempPath(), "hpa802-activate-transitioning"), out var root);
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.IsTransitioning).Returns(true);
        stage.StageManager = stageManager.Object;

        try
        {
            var result = InvokeCommand(stage, "ActivatePreparedChart");

            Assert.False(result.Success);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
            stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Never);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ActivatePreparedChart_WhenEligible_ShouldCleanPreparationAndUseSongTransition()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateReallyPreparedStage(game, Path.Combine(Path.GetTempPath(), "hpa802-activate-eligible"), out var root);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        // The projection selects the prepared row on the display (stage _selectedSong stays null).
        var preparedNode = GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!.SelectedSong!;

        try
        {
            var result = InvokeCommand(stage, "ActivatePreparedChart");

            Assert.True(result.Success);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    It.Is<SongPlayRequest>(request =>
                        ReferenceEquals(request.Song, preparedNode)
                        && request.Difficulty == 0)),
                Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StartPreparedPreview_WhenPlayingButInstanceStopped_ShouldMarkFailed()
    {
        var (stage, instance) = CreateReallyPreparedStageWithPlayingInstance(
            Path.Combine(Path.GetTempPath(), "hpa802-start-stopped"),
            out var root,
            out _);

        try
        {
            instance.SetupGet(x => x.State).Returns(SoundState.Stopped);

            var result = InvokeCommand(stage, "StartPreparedPreview");

            Assert.False(result.Success);
            Assert.Equal("The prepared preview stopped unexpectedly.", result.Error);
            Assert.Equal("Failed", PreviewControllerOf(stage).PreparedState.ToString());
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StartPreparedPreview_WhenStateIsFailed_ShouldReturnNotReady()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-start-failed");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(
                root,
                node,
                resourceManager.Object,
                _ => throw new InvalidOperationException("device lost"));
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            Assert.False(InvokeCommand(stage, "StartPreparedPreview").Success);
            Assert.Equal("Failed", PreviewControllerOf(stage).PreparedState.ToString());

            var result = InvokeCommand(stage, "StartPreparedPreview");

            Assert.False(result.Success);
            Assert.Equal("The prepared chart preview is not ready to play.", result.Error);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StartPreparedPreview_WhenPreparedSelectionIsNull_ShouldReturnNoPreview()
    {
        var stage = CreateStage();

        var result = InvokeCommand(stage, "StartPreparedPreview");

        Assert.False(result.Success);
        Assert.Equal("No prepared chart preview is available.", result.Error);
    }

    [Fact]
    public void StartPreparedPreview_WhenCreateInstanceThrows_ShouldMarkFailed()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-start-throw");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(
                root,
                node,
                resourceManager.Object,
                _ => throw new InvalidOperationException("device lost"));

            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);

            var result = InvokeCommand(stage, "StartPreparedPreview");

            Assert.False(result.Success);
            Assert.Equal("The prepared chart preview could not be started.", result.Error);
            Assert.Equal("Failed", PreviewControllerOf(stage).PreparedState.ToString());
        }
        finally
        {
            TryDelete(root);
        }
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
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void ActivatePreparedChart_WhenSelectedSongDiffersFromPreparedNode_ShouldReturnNoLongerSelected()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateReallyPreparedStage(game, Path.Combine(Path.GetTempPath(), "hpa802-activate-node"), out var root);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var otherNode = CreateNode("other", Path.Combine(root, "other-node.dtx"));
        SetPrivateField(stage, "_selectedSong", otherNode);

        try
        {
            var result = InvokeCommand(stage, "ActivatePreparedChart");

            Assert.False(result.Success);
            Assert.Equal("The prepared chart is no longer selected.", result.Error);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
                Times.Never);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ActivatePreparedChart_WhenDifficultyDiffersFromPrepared_ShouldReturnNoLongerSelected()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateReallyPreparedStage(game, Path.Combine(Path.GetTempPath(), "hpa802-activate-diff"), out var root);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        SetPrivateField(stage, "_currentDifficulty", 3);

        try
        {
            var result = InvokeCommand(stage, "ActivatePreparedChart");

            Assert.False(result.Success);
            Assert.Equal("The prepared chart is no longer selected.", result.Error);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ActivatePreparedChart_WhenStageManagerIsNull_ShouldReturnTransitionUnavailable()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateReallyPreparedStage(game, Path.Combine(Path.GetTempPath(), "hpa802-activate-no-sm"), out var root);
        stage.StageManager = null;

        try
        {
            var result = InvokeCommand(stage, "ActivatePreparedChart");

            Assert.False(result.Success);
            Assert.Equal("The song transition manager is unavailable.", result.Error);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ActivatePreparedChart_WhenSelectedSongFromDisplayMatches_ShouldSucceed()
    {
        var game = CreateGame(totalGameTime: 2d, lastStageTransitionTime: 0d);
        var stage = CreateReallyPreparedStage(game, Path.Combine(Path.GetTempPath(), "hpa802-activate-display"), out var root);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        SetPrivateField(stage, "_selectedSong", null);

        try
        {
            var result = InvokeCommand(stage, "ActivatePreparedChart");

            Assert.True(result.Success);
            Assert.Null(GetPrivateField<object>(stage, "_preparedChartSelection"));
            stageManager.Verify(
                x => x.ChangeStage(StageType.SongTransition, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
                Times.Once);
        }
        finally
        {
            TryDelete(root);
        }
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
    public void OnSongSelectionChanged_WhenStayingOnPreparedRow_ShouldPreservePreparationAndNotLoadPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-stay-row");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            AttachCoreUi(stage, display: new SongListDisplay());
            resourceManager.Invocations.Clear();

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(node, 0, true));

            resourceManager.Verify(
                x => x.TryLoadSound(It.IsAny<string>(), out It.Ref<ISound>.IsAny),
                Times.Never);
            preparedSound.Verify(x => x.RemoveReference(), Times.Never);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void OnDifficultyChanged_WhenStayingOnPreparedDifficulty_ShouldPreservePreparation()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa802-stay-difficulty");
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");

        try
        {
            var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
            var resourceManager = new Mock<IResourceManager>();
            var preparedSound = new Mock<ISound>();
            var preparedSoundObj = preparedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
            var stage = CreatePreparedStage(root, node, resourceManager.Object);
            Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());

            InvokePrivateMethod(stage, "OnDifficultyChanged", stage, new DifficultyChangedEventArgs(node, 0));

            preparedSound.Verify(x => x.RemoveReference(), Times.Never);
            resourceManager.Verify(
                x => x.TryLoadSound(It.IsAny<string>(), out It.Ref<ISound>.IsAny),
                Times.Once);
            Assert.NotNull(GetPrivateField<object>(stage, "_preparedChartSelection"));
            Assert.Equal("Prepared", PreviewControllerOf(stage).PreparedState.ToString());
        }
        finally
        {
            TryDelete(root);
        }
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
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
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
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
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
            var soundObj = sound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            var stage = CreateStage();
            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(node, 0, true));

            // With no prepared selection, the normal path should load the preview sound
            resourceManager.Verify(x => x.TryLoadSound(previewPath, out It.Ref<ISound>.IsAny), Times.Once);
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
        var (stage, _) = CreateReallyPreparedStageWithPlayingInstance(
            Path.Combine(Path.GetTempPath(), "hpa802-telemetry-playing"),
            out var root,
            out _);

        try
        {
            PreviewControllerOf(stage).Update(1.2345d);
            var telemetry = new GameTelemetrySnapshot();

            stage.PopulateTelemetry(telemetry);

            Assert.Equal("chart:901", GetTelemetryProperty<string>(telemetry, "PreparedChartIdentity"));
            Assert.Equal("Playing", GetTelemetryProperty<string>(telemetry, "PreparedPreviewState"));
            Assert.Equal(1234.5d, GetTelemetryProperty<double?>(telemetry, "PreparedPreviewElapsedMs"));
            Assert.DoesNotContain(root, GetTelemetryProperty<string>(telemetry, "PreparedChartIdentity") ?? "", StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static SongSelectionStage CreateReallyPreparedStage(BaseGame? game, string root, out string rootPath)
    {
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");
        var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
        var resourceManager = new Mock<IResourceManager>();
        var soundObj = new Mock<ISound>().Object;
        resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
        var stage = CreatePreparedStage(root, node, resourceManager.Object, game: game);
        Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
        rootPath = root;
        return stage;
    }

    // Prepared + explicitly started with a fake playing instance (seam-supplied),
    // used by tests that must observe the real playing → stopped/failed lifecycle.
    private static (SongSelectionStage Stage, Mock<ISoundInstance> Instance)
        CreateReallyPreparedStageWithPlayingInstance(
            string root,
            out string rootPath,
            out Mock<ISound> preparedSound)
    {
        Directory.CreateDirectory(root);
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");
        var node = CreateNode("prepared", chartPath, previewFile: "preview.wav");
        var resourceManager = new Mock<IResourceManager>();
        preparedSound = new Mock<ISound>();
        var sound = preparedSound; // local copy: out parameters cannot be captured in lambdas
        var preparedSoundObj = sound.Object;
        resourceManager.Setup(x => x.TryLoadSound(previewPath, out preparedSoundObj)).Returns(true);
        var preparedInstance = new Mock<ISoundInstance>();
        preparedInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
        var stage = CreatePreparedStage(
            root,
            node,
            resourceManager.Object,
            loadedSound => ReferenceEquals(loadedSound, sound.Object) ? preparedInstance.Object : null);
        Assert.True(InvokeCommand(stage, "PrepareVideoChart", chartPath).Success);
        Assert.True(InvokeCommand(stage, "StartPreparedPreview").Success);
        rootPath = root;
        return (stage, preparedInstance);
    }

    private static SongSelectionStage CreateStageWithArmedInteractivePreview(
        string root,
        string chartPath,
        string previewPath,
        Mock<ISound> sound,
        Func<ISound?, ISoundInstance?>? createPreviewInstance = null)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(chartPath, "chart");
        File.WriteAllText(previewPath, "preview");
        var resourceManager = new Mock<IResourceManager>();
        var soundObj = sound.Object;
        resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
        var stage = CreateStage(null, createPreviewInstance);
        SetPrivateField(stage, "_resourceManager", resourceManager.Object);
        AttachCoreUi(stage, display: new SongListDisplay(), statusPanel: new SongStatusPanel());
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Title = "song",
            DatabaseChart = new SongChart
            {
                FilePath = chartPath,
                PreviewFile = "preview.wav",
                HasDrumChart = true,
                DrumLevel = 5
            }
        };
        InvokePrivateMethod(
            stage,
            "OnSongSelectionChanged",
            GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
            new SongSelectionChangedEventArgs(node, 0, true));
        return stage;
    }

    private static SongSelectionStage CreatePreparedStage(
        string root,
        SongListNode node,
        IResourceManager resourceManager,
        Func<ISound?, ISoundInstance?>? createPreviewInstance = null,
        BaseGame? game = null)
    {
        var stage = CreateStage(game ?? CreateGame(totalGameTime: 2d), createPreviewInstance);
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

    private static SongSelectionPreviewController PreviewControllerOf(SongSelectionStage stage) =>
        GetPrivateField<SongSelectionPreviewController>(stage, "_previewController")!;

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
