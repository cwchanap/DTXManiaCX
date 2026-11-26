#nullable enable

using System;
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.SongSelection;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework.Audio;
using Moq;
using Xunit;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage;

// HPA-802: constructor-built tests for the extracted preview controller. The
// controller owns only audio facts (managed sound/instance references, the
// ordinary delayed start, stopped prepared playback, playback state and elapsed
// time); no songs, browse nodes or UI. The stage keeps selection facts and
// recorder-facing APIs.
//
// HPA-802 review round 1: no test reaches into the controller's private fields.
// MonoGame's SoundEffectInstance cannot be constructed headlessly, so tests use
// the internal instance-creation delegate seam (production keeps the
// two-argument constructor) to drive the real load/delay/start/stop paths with
// fake ISound/ISoundInstance mocks.
[Trait("Category", "Unit")]
public sealed class SongSelectionPreviewControllerTests
{
    private static SongSelectionPreviewController CreateController(
        Mock<IResourceManager>? resourceManager = null,
        Mock<ILogger>? logger = null,
        Func<ISound?, ISoundInstance?>? createInstance = null)
    {
        var rm = resourceManager;
        return new SongSelectionPreviewController(
            rm == null ? (Func<IResourceManager?>)(() => null) : () => rm.Object,
            logger?.Object ?? NullLogger.Instance,
            createInstance ?? (_ => null));
    }

    private static Mock<ISound> SetupSuccessfulLoad(Mock<IResourceManager> resourceManager, string path)
    {
        var sound = new Mock<ISound>();
        var soundObj = sound.Object;
        resourceManager.Setup(x => x.TryLoadSound(path, out soundObj)).Returns(true);
        return sound;
    }

    /// <summary>
    /// Drives the real preparation flow: LoadStopped publishes Prepared, then the
    /// explicit start runs the full production start path against the fake
    /// instance supplied by the seam. A non-null <paramref name="startFailure"/>
    /// makes the seam throw, exercising the real failed-start path. The loaded
    /// sound mock is returned so release behavior can be verified directly.
    /// </summary>
    private static (SongSelectionPreviewController Controller, Mock<ISoundInstance> Instance, Mock<ISound> Sound)
        CreatePlayingPreparation(
            Mock<IResourceManager>? resourceManager = null,
            Mock<ILogger>? logger = null,
            string path = "/songs/prepared.ogg",
            SoundState initialState = SoundState.Playing,
            Exception? startFailure = null)
    {
        var rm = resourceManager ?? new Mock<IResourceManager>();
        var sound = SetupSuccessfulLoad(rm, path);
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(initialState);
        var controller = CreateController(
            rm,
            logger,
            loadedSound =>
            {
                if (startFailure != null)
                    throw startFailure;
                return ReferenceEquals(loadedSound, sound.Object) ? instance.Object : null;
            });

        Assert.True(controller.LoadStopped(path));
        var start = controller.StartPrepared();
        if (startFailure == null)
        {
            Assert.True(start.Success);
            Assert.Equal(SongSelectionPreviewState.Playing, controller.PreparedState);
        }
        return (controller, instance, sound);
    }

    #region Ordinary delayed start (LoadDelayed + Update)

    [Fact]
    public void LoadDelayed_WhenLoadSucceeds_ShouldHoldSoundAndArmDelayWithoutStartingEarly()
    {
        var resourceManager = new Mock<IResourceManager>();
        var sound = SetupSuccessfulLoad(resourceManager, "/songs/preview.ogg");
        var instance = new Mock<ISoundInstance>();
        var createCalls = 0;
        var controller = CreateController(resourceManager, createInstance: loadedSound =>
        {
            createCalls++;
            return ReferenceEquals(loadedSound, sound.Object) ? instance.Object : null;
        });

        var result = controller.LoadDelayed("/songs/preview.ogg");

        Assert.True(result);
        Assert.True(controller.HasLoadedSound);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);

        // The armed delay starts only after it elapses.
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds - 0.01);
        Assert.Equal(0, createCalls);
        instance.Verify(x => x.Play(), Times.Never);

        // Once it elapses, the ordinary start assigns preview volume/looping and
        // plays through the instance-creation seam.
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);

        instance.VerifySet(x => x.Volume = SongSelectionUILayout.Audio.PreviewSoundVolume);
        instance.VerifySet(x => x.IsLooped = true);
        instance.Verify(x => x.Play(), Times.Once);
        Assert.Equal(1, createCalls);
    }

    [Fact]
    public void LoadDelayed_WhenReplaced_ShouldReleasePreviousReferenceExactlyOnce()
    {
        var resourceManager = new Mock<IResourceManager>();
        var previous = SetupSuccessfulLoad(resourceManager, "/songs/first.ogg");
        SetupSuccessfulLoad(resourceManager, "/songs/second.ogg");
        var controller = CreateController(resourceManager);

        Assert.True(controller.LoadDelayed("/songs/first.ogg"));
        Assert.True(controller.LoadDelayed("/songs/second.ogg"));

        previous.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void LoadDelayed_WhenLoadReportsFalse_ShouldReleaseFallbackAndPreviousAndNotArm()
    {
        var resourceManager = new Mock<IResourceManager>();
        var previous = SetupSuccessfulLoad(resourceManager, "/songs/first.ogg");
        var fallback = new Mock<ISound>();
        var fallbackObj = fallback.Object;
        resourceManager.Setup(x => x.TryLoadSound("/songs/bad.ogg", out fallbackObj)).Returns(false);
        var createCalls = 0;
        var controller = CreateController(resourceManager, createInstance: _ =>
        {
            createCalls++;
            return null;
        });
        Assert.True(controller.LoadDelayed("/songs/first.ogg"));

        var result = controller.LoadDelayed("/songs/bad.ogg");

        Assert.False(result);
        Assert.False(controller.HasLoadedSound);
        previous.Verify(x => x.RemoveReference(), Times.Once);
        fallback.Verify(x => x.RemoveReference(), Times.Once);

        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public void LoadDelayed_WhenLoadThrows_ShouldReleasePreviousAndReturnFalse()
    {
        var resourceManager = new Mock<IResourceManager>();
        var previous = SetupSuccessfulLoad(resourceManager, "/songs/first.ogg");
        resourceManager
            .Setup(x => x.TryLoadSound("/songs/boom.ogg", out It.Ref<ISound>.IsAny))
            .Throws(new IOException("boom"));
        var controller = CreateController(resourceManager);
        Assert.True(controller.LoadDelayed("/songs/first.ogg"));

        var result = controller.LoadDelayed("/songs/boom.ogg");

        Assert.False(result);
        Assert.False(controller.HasLoadedSound);
        previous.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void LoadDelayed_WhenResourceManagerIsMissing_ShouldReturnFalseWithoutArming()
    {
        var controller = CreateController();

        var result = controller.LoadDelayed("/songs/preview.ogg");

        Assert.False(result);
        Assert.False(controller.HasLoadedSound);
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);
    }

    [Fact]
    public void LoadDelayed_WhenLoadReturnsNullSound_ShouldReturnFalseWithoutArming()
    {
        var resourceManager = new Mock<IResourceManager>();
        ISound nullSound = null!;
        resourceManager.Setup(x => x.TryLoadSound("/songs/null.ogg", out nullSound)).Returns(true);
        var controller = CreateController(resourceManager);

        var result = controller.LoadDelayed("/songs/null.ogg");

        Assert.False(result);
        Assert.False(controller.HasLoadedSound);
    }

    [Fact]
    public void Update_BeforeDelayElapses_ShouldAccumulateWithoutStarting()
    {
        var resourceManager = new Mock<IResourceManager>();
        SetupSuccessfulLoad(resourceManager, "/songs/preview.ogg");
        var createCalls = 0;
        var controller = CreateController(resourceManager, createInstance: _ =>
        {
            createCalls++;
            return null;
        });
        controller.LoadDelayed("/songs/preview.ogg");

        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds - 0.01);

        Assert.Equal(0, createCalls);
    }

    [Fact]
    public void Update_WhenNotArmed_ShouldNotStartOrChangeState()
    {
        var resourceManager = new Mock<IResourceManager>();
        var sound = SetupSuccessfulLoad(resourceManager, "/songs/prepared.ogg");
        var createCalls = 0;
        var controller = CreateController(resourceManager, createInstance: _ =>
        {
            createCalls++;
            return null;
        });
        Assert.True(controller.LoadStopped("/songs/prepared.ogg"));

        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);

        Assert.Equal(0, createCalls);
        sound.Verify(x => x.CreateInstance(), Times.Never);
        Assert.Equal(SongSelectionPreviewState.Prepared, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
    }

    [Fact]
    public void Update_WhenDelayFires_ShouldStartExactlyOnceAndNotRestartAfterwards()
    {
        var resourceManager = new Mock<IResourceManager>();
        var sound = SetupSuccessfulLoad(resourceManager, "/songs/preview.ogg");
        var instance = new Mock<ISoundInstance>();
        instance.SetupGet(x => x.State).Returns(SoundState.Playing);
        var createCalls = 0;
        var controller = CreateController(resourceManager, createInstance: loadedSound =>
        {
            createCalls++;
            return ReferenceEquals(loadedSound, sound.Object) ? instance.Object : null;
        });
        controller.LoadDelayed("/songs/preview.ogg");

        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);

        Assert.Equal(1, createCalls);
        instance.Verify(x => x.Play(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
    }

    [Fact]
    public void Update_WhenCreateInstanceThrows_ShouldLogWarningOnceAndNotRetry()
    {
        var logger = new Mock<ILogger>();
        var failure = new InvalidOperationException("audio device lost");
        var resourceManager = new Mock<IResourceManager>();
        SetupSuccessfulLoad(resourceManager, "/songs/preview.ogg");
        var createCalls = 0;
        var controller = CreateController(
            resourceManager,
            logger,
            _ =>
            {
                createCalls++;
                throw failure;
            });
        controller.LoadDelayed("/songs/preview.ogg");

        // The delay flag is cleared before playback starts, so a second tick
        // must not retry the failed preview or log again.
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Song preview playback failed", StringComparison.Ordinal)),
                It.Is<Exception?>(e => ReferenceEquals(e, failure)),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        Assert.Equal(1, createCalls);
    }

    #endregion

    #region Replacement and mode enforcement (HPA-802 review round 1)

    // The controller is single-voice: LoadStopped while a previous preview is still
    // playing must stop/dispose that instance before publishing the new preparation.
    [Fact]
    public void LoadStopped_WhenReplacingPlayingPreview_ShouldStopAndDisposePreviousInstance()
    {
        var resourceManager = new Mock<IResourceManager>();
        var soundA = SetupSuccessfulLoad(resourceManager, "/songs/a.ogg");
        SetupSuccessfulLoad(resourceManager, "/songs/b.ogg");
        var instanceA = new Mock<ISoundInstance>();
        instanceA.SetupGet(x => x.State).Returns(SoundState.Playing);
        var controller = CreateController(
            resourceManager,
            createInstance: loadedSound => ReferenceEquals(loadedSound, soundA.Object) ? instanceA.Object : null);

        Assert.True(controller.LoadStopped("/songs/a.ogg"));
        var start = controller.StartPrepared();
        Assert.True(start.Success);

        Assert.True(controller.LoadStopped("/songs/b.ogg"));

        instanceA.Verify(x => x.Stop(), Times.Once);
        instanceA.Verify(x => x.Dispose(), Times.Once);
        soundA.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.Prepared, controller.PreparedState);
    }

    // LoadStopped(A) → LoadDelayed(B) must not leave the controller reporting
    // Prepared while the ordinary delayed preview of B starts — replacement resets
    // to ordinary mode and the delay only ever runs in None.
    [Fact]
    public void LoadDelayed_WhenReplacingStoppedPreparation_ShouldResumeOrdinaryModeInsteadOfReportingPrepared()
    {
        var resourceManager = new Mock<IResourceManager>();
        var soundA = SetupSuccessfulLoad(resourceManager, "/songs/a.ogg");
        var soundB = SetupSuccessfulLoad(resourceManager, "/songs/b.ogg");
        var instanceB = new Mock<ISoundInstance>();
        var controller = CreateController(
            resourceManager,
            createInstance: loadedSound => ReferenceEquals(loadedSound, soundB.Object) ? instanceB.Object : null);

        Assert.True(controller.LoadStopped("/songs/a.ogg"));

        Assert.True(controller.LoadDelayed("/songs/b.ogg"));
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);

        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
        instanceB.Verify(x => x.Play(), Times.Once);
        soundA.Verify(x => x.RemoveReference(), Times.Once);
    }

    #endregion

    #region Stopped preparation (LoadStopped)

    [Fact]
    public void LoadStopped_WhenLoadSucceeds_ShouldPublishPreparedStateAndKeepSilent()
    {
        var resourceManager = new Mock<IResourceManager>();
        SetupSuccessfulLoad(resourceManager, "/songs/prepared.ogg");
        var createCalls = 0;
        var controller = CreateController(resourceManager, createInstance: _ =>
        {
            createCalls++;
            return null;
        });

        var result = controller.LoadStopped("/songs/prepared.ogg");

        Assert.True(result);
        Assert.True(controller.HasLoadedSound);
        Assert.Equal(SongSelectionPreviewState.Prepared, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
        Assert.Equal(0, createCalls);

        // Prepared mode never auto-starts after the ordinary delay.
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds * 2);
        Assert.Equal(0, createCalls);
        Assert.Equal(SongSelectionPreviewState.Prepared, controller.PreparedState);
    }

    [Fact]
    public void LoadStopped_WhenLoadFails_ShouldStayInNoneState()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager
            .Setup(x => x.TryLoadSound("/songs/bad.ogg", out It.Ref<ISound>.IsAny))
            .Returns(false);
        var controller = CreateController(resourceManager);

        var result = controller.LoadStopped("/songs/bad.ogg");

        Assert.False(result);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
        Assert.False(controller.HasLoadedSound);
    }

    #endregion

    #region Explicit start (StartPrepared)

    [Fact]
    public void StartPrepared_WhenPrepared_ShouldAssignPreviewVolumeAndLoopAndCountPlaybackFromZero()
    {
        var (controller, instance, _) = CreatePlayingPreparation();

        // The real start path assigned preview volume/looping and played the fake
        // instance; the prepared elapsed time counts actual playback from zero.
        instance.VerifySet(x => x.Volume = SongSelectionUILayout.Audio.PreviewSoundVolume);
        instance.VerifySet(x => x.IsLooped = true);
        instance.Verify(x => x.Play(), Times.Once);
        Assert.Equal(0d, controller.PlayedMs);

        controller.Update(0.5d);
        Assert.Equal(500d, controller.PlayedMs);
    }

    [Fact]
    public void StartPrepared_WhenAlreadyPlaying_ShouldBeIdempotent()
    {
        var (controller, instance, _) = CreatePlayingPreparation();

        var result = controller.StartPrepared();

        Assert.True(result.Success);
        Assert.Null(result.Error);
        instance.Verify(x => x.Play(), Times.Once);
    }

    [Fact]
    public void StartPrepared_WhenPlayingButInstanceStopped_ShouldMarkFailed()
    {
        var (controller, instance, _) = CreatePlayingPreparation();

        instance.SetupGet(x => x.State).Returns(SoundState.Stopped);
        var result = controller.StartPrepared();

        Assert.False(result.Success);
        Assert.Equal("The prepared preview stopped unexpectedly.", result.Error);
        Assert.Equal(SongSelectionPreviewState.Failed, controller.PreparedState);
    }

    [Fact]
    public void StartPrepared_WhenIdle_ShouldReturnNotReadyMessage()
    {
        var controller = CreateController();

        var result = controller.StartPrepared();

        Assert.False(result.Success);
        Assert.Equal("The prepared chart preview is not ready to play.", result.Error);
    }

    [Fact]
    public void StartPrepared_WhenFailed_ShouldReturnNotReadyMessage()
    {
        var (controller, _, _) = CreatePlayingPreparation(startFailure: new InvalidOperationException("device lost"));
        Assert.Equal(SongSelectionPreviewState.Failed, controller.PreparedState);

        var result = controller.StartPrepared();

        Assert.False(result.Success);
        Assert.Equal("The prepared chart preview is not ready to play.", result.Error);
    }

    [Fact]
    public void StartPrepared_WhenInstanceCannotBeCreated_ShouldMarkFailed()
    {
        var resourceManager = new Mock<IResourceManager>();
        var sound = SetupSuccessfulLoad(resourceManager, "/songs/prepared.ogg");
        // The default test seam returns null: the instance cannot be created.
        var controller = CreateController(resourceManager);
        Assert.True(controller.LoadStopped("/songs/prepared.ogg"));

        var result = controller.StartPrepared();

        Assert.False(result.Success);
        Assert.Equal("The prepared chart preview instance could not be created.", result.Error);
        Assert.Equal(SongSelectionPreviewState.Failed, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);

        // Elapsed time never accumulates outside actual playback.
        controller.Update(1d);
        Assert.Equal(0d, controller.PlayedMs);
    }

    [Fact]
    public void StartPrepared_WhenCreateInstanceThrows_ShouldLogWarningOnceAndMarkFailed()
    {
        var logger = new Mock<ILogger>();
        var failure = new InvalidOperationException("device lost");
        var (controller, instance, _) = CreatePlayingPreparation(logger: logger, startFailure: failure);

        // The start attempt above ran the real failed-start path exactly once.
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Prepared chart preview could not be started", StringComparison.Ordinal)),
                It.Is<Exception?>(e => ReferenceEquals(e, failure)),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        Assert.Equal(SongSelectionPreviewState.Failed, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
        instance.Verify(x => x.Dispose(), Times.Never);
    }

    #endregion

    #region Playback state and elapsed time (Update)

    [Fact]
    public void Update_WhenPlaying_ShouldAccumulateOnlyActualPlaybackAndFailOnStop()
    {
        var (controller, instance, _) = CreatePlayingPreparation();

        controller.Update(0.5);

        Assert.Equal(500d, controller.PlayedMs);
        Assert.Equal(SongSelectionPreviewState.Playing, controller.PreparedState);

        instance.SetupGet(x => x.State).Returns(SoundState.Stopped);
        controller.Update(1d);

        Assert.Equal(500d, controller.PlayedMs);
        Assert.Equal(SongSelectionPreviewState.Failed, controller.PreparedState);
    }

    [Fact]
    public void Update_WhenPlayingWithNegativeDelta_ShouldClampToZero()
    {
        var (controller, _, _) = CreatePlayingPreparation();

        controller.Update(-0.5);

        Assert.Equal(0d, controller.PlayedMs);
        Assert.Equal(SongSelectionPreviewState.Playing, controller.PreparedState);
    }

    [Fact]
    public void Update_WhenUnexpectedStop_ShouldNotAutoRestartOrFallBack()
    {
        var (controller, instance, _) = CreatePlayingPreparation();

        instance.SetupGet(x => x.State).Returns(SoundState.Stopped);
        controller.Update(1d);
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds * 2);

        // Failed previews never restart themselves or fall back to the ordinary
        // delayed start.
        instance.Verify(x => x.Play(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.Failed, controller.PreparedState);
    }

    #endregion

    #region Explicit Stop contract

    [Fact]
    public void Stop_WhenPlaying_ShouldStopDisposeReleaseAndReset()
    {
        var (controller, instance, _) = CreatePlayingPreparation();

        controller.Stop();

        instance.Verify(x => x.Stop(), Times.Once);
        instance.Verify(x => x.Dispose(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
        Assert.False(controller.HasLoadedSound);
    }

    [Fact]
    public void Stop_WhenInstanceNotPlaying_ShouldNotCallStopButStillDispose()
    {
        var (controller, instance, _) = CreatePlayingPreparation();
        instance.SetupGet(x => x.State).Returns(SoundState.Stopped);
        controller.Update(1d);
        Assert.Equal(SongSelectionPreviewState.Failed, controller.PreparedState);

        controller.Stop();

        instance.Verify(x => x.Stop(), Times.Never);
        instance.Verify(x => x.Dispose(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
    }

    [Fact]
    public void Stop_WhenInstanceStopThrows_ShouldStillDisposeAndRelease()
    {
        var (controller, instance, sound) = CreatePlayingPreparation();
        instance.Setup(x => x.Stop()).Throws(new Exception("stop error"));

        controller.Stop();

        instance.Verify(x => x.Dispose(), Times.Once);
        sound.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
        Assert.False(controller.HasLoadedSound);
    }

    [Fact]
    public void Stop_WhenInstanceDisposeThrows_ShouldSwallowAndClearInstance()
    {
        var (controller, instance, _) = CreatePlayingPreparation();
        instance.Setup(x => x.Dispose()).Throws(new InvalidOperationException("disposed twice"));

        controller.Stop();
        controller.Stop();

        instance.Verify(x => x.Dispose(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
    }

    [Fact]
    public void Stop_RepeatedCalls_ShouldReleaseExactlyOnce()
    {
        var (controller, instance, sound) = CreatePlayingPreparation();

        controller.Stop();
        controller.Stop();
        controller.Stop();

        instance.Verify(x => x.Stop(), Times.Once);
        instance.Verify(x => x.Dispose(), Times.Once);
        sound.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
    }

    [Fact]
    public void Stop_WhenIdleWithArmedDelay_ShouldClearDelayAndReleaseWithoutStarting()
    {
        var resourceManager = new Mock<IResourceManager>();
        var sound = SetupSuccessfulLoad(resourceManager, "/songs/preview.ogg");
        var createCalls = 0;
        var controller = CreateController(resourceManager, createInstance: _ =>
        {
            createCalls++;
            return null;
        });
        Assert.True(controller.LoadDelayed("/songs/preview.ogg"));

        controller.Stop();

        Assert.Equal(SongSelectionPreviewState.None, controller.PreparedState);
        Assert.Equal(0d, controller.PlayedMs);
        Assert.False(controller.HasLoadedSound);
        sound.Verify(x => x.RemoveReference(), Times.Once);

        // The armed delay was cleared: no delayed start fires afterwards.
        controller.Update(SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);
        Assert.Equal(0, createCalls);
        sound.Verify(x => x.CreateInstance(), Times.Never);
    }

    #endregion

    #region Lazy resource access

    [Fact]
    public void LoadDelayed_ShouldResolveResourceManagerLazilyOnlyWhenLoading()
    {
        var calls = 0;
        var resourceManager = new Mock<IResourceManager>();
        var loaded = new Mock<ISound>().Object;
        resourceManager.Setup(x => x.TryLoadSound("/songs/preview.ogg", out loaded)).Returns(true);
        var controller = new SongSelectionPreviewController(
            () => { calls++; return resourceManager.Object; },
            NullLogger.Instance);

        // Constructor and Update never resolve the accessor.
        controller.Update(1d);
        Assert.Equal(0, calls);

        Assert.True(controller.LoadDelayed("/songs/preview.ogg"));
        Assert.Equal(1, calls);
    }

    // Headless stage: constructed without Activate; the manager is supplied
    // afterwards and must be resolved lazily when a preview load runs.
    [Fact]
    public void Stage_WithoutActivate_ShouldResolveSuppliedManagerLazilyForPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hpa802-headless-stage-{Guid.NewGuid():N}");
        var chartPath = Path.Combine(root, "chart.dtx");
        var previewPath = Path.Combine(root, "preview.wav");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(chartPath, "chart");
            File.WriteAllText(previewPath, "preview");
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            var soundObj = sound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out soundObj)).Returns(true);
            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            var statusPanel = new SongStatusPanel();
            AttachCoreUi(stage, statusPanel: statusPanel);

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!,
                new SongSelectionChangedEventArgs(
                    CreateNode("song", chartPath, previewFile: "preview.wav"), 0, true));

            resourceManager.Verify(x => x.TryLoadSound(previewPath, out It.Ref<ISound>.IsAny), Times.Once);

            // Drive the stage's REAL update loop (OnUpdate forwards the delta to the
            // preview controller): the armed ordinary delay can only fire — creating the
            // instance — while the loaded sound is still held by the stage's controller.
            SetPrivateField(stage, "_currentPhase", DTXMania.Game.Lib.Stage.StagePhase.Normal);
            stage.Update(DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.Audio.PreviewPlayDelaySeconds + 0.1);
            sound.Verify(x => x.CreateInstance(), Times.Once);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static SongListNode CreateNode(string title, string chartPath, string previewFile)
    {
        var chart = new SongChart
        {
            FilePath = chartPath,
            PreviewFile = previewFile,
            HasDrumChart = true,
            DrumLevel = 5
        };
        return new SongListNode
        {
            Type = NodeType.Score,
            Title = title,
            DatabaseChart = chart
        };
    }

    #endregion
}
