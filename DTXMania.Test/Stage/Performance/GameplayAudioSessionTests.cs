using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

/// <summary>
/// Focused tests for <see cref="GameplayAudioSession"/>, the Task-1 extraction
/// that owns audio preparation binding, the song timer, scheduled BGM events,
/// chip playback and the metronome for one gameplay activation.
/// </summary>
[Trait("Category", "Unit")]
public class GameplayAudioSessionTests
{
    [Fact]
    public void Bind_WithoutBackgroundAudio_CreatesSilentSpeedAwareTimer()
    {
        var session = CreateSession(modifiers: new PlaybackModifiers(50, 0));

        session.Bind(CreateChart(), CreateEmptyPreparedSet());

        Assert.NotNull(session.Timer);
        Assert.False(session.IsPlaying);
    }

    [Fact]
    public void Bind_SchedulesBgmEventsInChronologicalOrder()
    {
        var chart = CreateChart();
        chart.AddBGMEvent(new BGMEvent { WavId = "03", TimeMs = 900.0 });
        chart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100.0 });
        chart.AddBGMEvent(new BGMEvent { WavId = "02", TimeMs = 500.0 });
        var session = CreateSession();

        session.Bind(chart, CreateEmptyPreparedSet());

        Assert.Equal(
            new[] { "01", "02", "03" },
            session.ScheduledBgmEvents.Select(e => e.WavId));
    }

    [Fact]
    public void Start_WithSilentTimer_ReportsPlayingAndAdvancesLogicalClock()
    {
        var session = CreateSession(modifiers: new PlaybackModifiers(150, 0));
        session.Bind(CreateChart(), CreateEmptyPreparedSet());
        var startTime = new GameTime(TimeSpan.FromMilliseconds(1000), TimeSpan.Zero);

        var started = session.Start(startTime, null);

        Assert.True(started);
        Assert.True(session.IsPlaying);
        Assert.Equal(
            750.0,
            session.GetCurrentMs(new GameTime(TimeSpan.FromMilliseconds(1500), TimeSpan.Zero)));
    }

    [Fact]
    public void Start_WhenBgmEventsAreScheduled_MutesTheMainTimer()
    {
        var instance = CreateSoundEffectInstance();
        var session = CreateSession();
        session.Bind(
            CreateChart(chart => chart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100.0 })),
            CreatePreparedSetWithMainBackground(instance));
        // Seed an audible volume: the scheduled-BGM path must mute it back to 0.
        session.Timer!.Volume = 0.7f;

        session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), null);

        Assert.Equal(0.0f, session.Timer!.Volume);
    }

    [Fact]
    public void Start_WithoutBgmEvents_DefaultsToFullVolume()
    {
        var instance = CreateSoundEffectInstance();
        var session = CreateSession();
        session.Bind(CreateChart(), CreatePreparedSetWithMainBackground(instance));

        session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), null);

        Assert.Equal(1.0f, session.Timer!.Volume);
    }

    [Fact]
    public void Start_WithBackgroundWavId_AppliesChartVolumeAndPan()
    {
        var instance = CreateSoundEffectInstance();
        var chart = CreateChart(chart =>
        {
            chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 40 });
            chart.SetWavPans(new Dictionary<string, int> { ["01"] = -80 });
            chart.BackgroundWavId = "01";
        });
        var session = CreateSession();
        session.Bind(chart, CreatePreparedSetWithMainBackground(instance));

        session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), chart);

        Assert.Equal(0.4f, session.Timer!.Volume);
        Assert.Equal(-0.8f, session.Timer!.Pan);
    }

    [Fact]
    public void Start_WithoutTimer_ReturnsFalse()
    {
        // An unbound session has no timer — the stage logs and continues silently.
        var session = CreateSession();

        Assert.False(session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), null));
    }

    [Fact]
    public void Start_WhenMainBackgroundPlaybackFails_ReturnsFalseAndRunsSilentClock()
    {
        // Play() throws on the uninitialized instance: SongTimer reports the
        // transport failure but the logical clock still runs on its GameTime
        // fallback so the song can advance and complete without audio.
        var session = CreateSession();
        session.Bind(CreateChart(), CreatePreparedSetWithMainBackground(CreateSoundEffectInstance()));

        var started = session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), null);

        Assert.False(started);
        Assert.True(session.IsPlaying);
    }

    [Fact]
    public void Start_WhenPlaybackFailsWithBgmEvents_StillMutesMainTimer()
    {
        // The BGM mute policy is independent of playback outcome: even when
        // Play() fails, the main-track volume is forced to zero.
        var instance = CreateSoundEffectInstance();
        var session = CreateSession();
        session.Bind(
            CreateChart(chart => chart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100.0 })),
            CreatePreparedSetWithMainBackground(instance));
        // Seed an audible volume: the mute write must be observable, not a default.
        session.Timer!.Volume = 0.7f;

        var started = session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), null);

        Assert.False(started);
        Assert.Equal(0.0f, session.Timer!.Volume);
    }

    [Fact]
    public void Advance_TriggersDueBgmEventsAndReapsStoppedInstances()
    {
        var sound = new Mock<ISound>();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(CreateSoundEffectInstance());
        var session = CreateBgmSession(("01", 100.0, sound.Object));

        session.Advance(150.0);

        sound.Verify(x => x.Play(1.0f, 0.0f, 0.0f), Times.Once);
        Assert.Empty(session.ScheduledBgmEvents);
        // The uninitialized instance reports Stopped, so Advance's reaping
        // pass removes it immediately rather than keeping it until teardown.
        Assert.Empty(session.ActiveBgmInstances);
    }

    [Fact]
    public void Advance_FiresBgmEventAtItsExactBoundaryTime()
    {
        var played = new List<string>();
        var sound = new Mock<ISound>();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Callback(() => played.Add("01"))
            .Returns(CreateSoundEffectInstance());
        var session = CreateBgmSession(("01", 100.0, sound.Object));

        session.Advance(99.0);
        Assert.Empty(played);
        Assert.Single(session.ScheduledBgmEvents);

        // The boundary is inclusive: an event is due at its exact TimeMs.
        session.Advance(100.0);

        Assert.Equal(new[] { "01" }, played);
        Assert.Empty(session.ScheduledBgmEvents);
    }

    [Fact]
    public void Advance_WithOverdueEqualTimeBgmEvents_PreservesDeclarationTieOrder()
    {
        var playedOrder = new List<string>();
        ISound RecordingSound(string wavId)
        {
            var sound = new Mock<ISound>();
            sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
                .Callback(() => playedOrder.Add(wavId))
                .Returns(CreateSoundEffectInstance());
            return sound.Object;
        }
        // Declared in non-alphabetical order so an unstable or reversed
        // consumption would surface as ["01", "02"]. Both events share one
        // TimeMs and become due together after a slow frame.
        var session = CreateBgmSession(
            ("02", 100.0, RecordingSound("02")),
            ("01", 100.0, RecordingSound("01")));

        session.Advance(500.0);

        Assert.Equal(new[] { "02", "01" }, playedOrder);
        Assert.Empty(session.ScheduledBgmEvents);
    }

    [Fact]
    public void Advance_ConsumesTriggeredBgmEvents_TheyDoNotReplay()
    {
        var sound = new Mock<ISound>();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(CreateSoundEffectInstance());
        var session = CreateBgmSession(("01", 100.0, sound.Object));

        session.Advance(150.0);
        session.Advance(1000.0);

        sound.Verify(
            x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
        Assert.Empty(session.ScheduledBgmEvents);
    }

    [Fact]
    public void Advance_WhenBgmPlayFails_LogsWarningOncePerWavId()
    {
        var logger = new Mock<ILogger>();
        var sound = new Mock<ISound>();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Throws(new InvalidOperationException("boom"));
        var session = CreateBgmSession(
            logger.Object,
            ("01", 0.0, sound.Object),
            ("01", 100.0, sound.Object));

        session.Advance(200.0);

        sound.Verify(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Exactly(2));
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void PlayChip_PlaysWavWithChartVolumeAndPan()
    {
        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(
            new Dictionary<string, ISound> { ["07"] = soundMock.Object });
        var chart = CreateChart(chart =>
        {
            chart.SetWavVolumes(new Dictionary<string, int> { ["07"] = 50 });
            chart.SetWavPans(new Dictionary<string, int> { ["07"] = 100 });
        });
        var session = CreateSession();
        session.Bind(chart, chipSet);

        session.PlayChip(new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "07") { TimeMs = 100.0 });

        soundMock.Verify(s => s.Play(0.5f, 0.0f, 1.0f), Times.Once);
    }

    [Fact]
    public void UpdateMetronome_FiresPlayClickForDueMarkers()
    {
        var clicks = new List<BeatMarker>();
        var session = CreateSession(metronomeEnabled: true, playClick: clicks.Add);
        var chart = CreateChart();
        chart.BeatMarkers.Add(new BeatMarker { TimeMs = 100.0, IsMeasureStart = false });
        session.Bind(chart, CreateEmptyPreparedSet());

        session.UpdateMetronome(150.0);

        var click = Assert.Single(clicks);
        Assert.Equal(100.0, click.TimeMs);
    }

    [Theory]
    [InlineData(50, 150.0, true)]
    [InlineData(50, 151.0, false)]
    [InlineData(100, 200.0, true)]
    [InlineData(100, 201.0, false)]
    [InlineData(200, 300.0, true)]
    [InlineData(200, 301.0, false)]
    public void UpdateMetronome_ScalesLateToleranceBySessionSpeed(int speed, double time, bool expectedClick)
    {
        var clicks = new List<BeatMarker>();
        using var session = CreateSession(modifiers: new PlaybackModifiers(speed, 0),
            metronomeEnabled: true, playClick: clicks.Add);
        var chart = CreateChart();
        chart.BeatMarkers.Add(new BeatMarker { TimeMs = 100.0 });
        session.Bind(chart, CreateEmptyPreparedSet());

        session.UpdateMetronome(time);

        if (expectedClick)
            Assert.Equal(100.0, Assert.Single(clicks).TimeMs);
        else
            Assert.Empty(clicks);
    }

    [Fact]
    public void UpdateMetronome_WhenDisabled_IsNoOp()
    {
        var session = CreateSession(metronomeEnabled: false);
        session.Bind(CreateChart(), CreateEmptyPreparedSet());

        Assert.Null(session.Metronome);
        var exception = Record.Exception(() => session.UpdateMetronome(1000.0));
        Assert.Null(exception);
    }

    [Fact]
    public void InitializeMetronome_WhenDisabled_ShouldNotLoadClickSoundsOrCreatePlayer()
    {
        var resources = new Mock<IResourceManager>();
        using var session = CreateSession(resourceManager: resources.Object);
        session.Bind(CreateChart(), CreateEmptyPreparedSet());
        resources.Verify(x => x.TryLoadSound(It.IsAny<string>(), out It.Ref<ISound>.IsAny), Times.Never);
        Assert.Null(session.Metronome);
    }

    [Fact]
    public void InitializeMetronome_WhenEnabled_ShouldLoadBothSoundsAndUseFinalizedMarkers()
    {
        var beat = new Mock<ISound>();
        var accent = new Mock<ISound>();
        var resources = new Mock<IResourceManager>();
        var beatResult = beat.Object;
        var accentResult = accent.Object;
        resources.Setup(x => x.TryLoadSound(SoundPath.MetronomeBeat, out beatResult)).Returns(true);
        resources.Setup(x => x.TryLoadSound(SoundPath.MetronomeAccent, out accentResult)).Returns(true);
        var clicks = new List<BeatMarker>();
        using var session = CreateSession(resourceManager: resources.Object, metronomeEnabled: true, playClick: clicks.Add);
        var chart = CreateChart();
        chart.BeatMarkers.Add(new BeatMarker { TimeMs = 0, IsMeasureStart = true });
        chart.BeatMarkers.Add(new BeatMarker { TimeMs = 100 });
        session.Bind(chart, CreateEmptyPreparedSet());
        session.UpdateMetronome(0);
        resources.Verify(x => x.TryLoadSound(SoundPath.MetronomeBeat, out It.Ref<ISound>.IsAny), Times.Once);
        resources.Verify(x => x.TryLoadSound(SoundPath.MetronomeAccent, out It.Ref<ISound>.IsAny), Times.Once);
        Assert.NotNull(session.Metronome);
        Assert.True(Assert.Single(clicks).IsMeasureStart);
        Assert.Equal(0, clicks[0].TimeMs);
    }

    [Fact]
    public void InitializeMetronome_AfterCleanup_ShouldCreateFreshPlayerStartingAtMarkerZero()
    {
        var sound = new Mock<ISound>();
        var resources = new Mock<IResourceManager>();
        var result = sound.Object;
        resources.Setup(x => x.TryLoadSound(It.IsAny<string>(), out result)).Returns(true);
        var clicks = new List<BeatMarker>();
        var chart = CreateChart();
        chart.BeatMarkers.Add(new BeatMarker { TimeMs = 0, IsMeasureStart = true });
        using var first = CreateSession(resourceManager: resources.Object, metronomeEnabled: true, playClick: clicks.Add);
        first.Bind(chart, CreateEmptyPreparedSet());
        var firstPlayer = first.Metronome;
        first.Dispose();
        using var second = CreateSession(resourceManager: resources.Object, metronomeEnabled: true, playClick: clicks.Add);
        second.Bind(chart, CreateEmptyPreparedSet());
        Assert.NotNull(firstPlayer);
        Assert.NotNull(second.Metronome);
        Assert.NotSame(firstPlayer, second.Metronome);
        second.UpdateMetronome(0);
        Assert.Equal(0, Assert.Single(clicks).TimeMs);
    }

    [Fact]
    public void InitializeMetronome_WhenClickResourcesAreMissing_ShouldLeaveGameplayUsable()
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(x => x.TryLoadSound(It.IsAny<string>(), out It.Ref<ISound>.IsAny))
            .Throws(new FileNotFoundException("click asset missing"));
        var clicks = new List<BeatMarker>();
        using var session = CreateSession(resourceManager: resources.Object, metronomeEnabled: true, playClick: clicks.Add);
        var chart = CreateChart();
        chart.BeatMarkers.Add(new BeatMarker { TimeMs = 0, IsMeasureStart = true });
        session.Bind(chart, CreateEmptyPreparedSet());
        session.UpdateMetronome(0);
        Assert.NotNull(session.Metronome);
        Assert.Single(clicks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayMetronomeClick_SelectsBeatOrAccentSound(bool accentMarker)
    {
        var beat = new Mock<ISound>();
        var accent = new Mock<ISound>();
        using var session = CreateMetronomeSession(NullLogger<PerformanceStage>.Instance, beat.Object, accent.Object);
        session.PlayMetronomeClick(new BeatMarker { IsMeasureStart = accentMarker });
        beat.VerifyGet(s => s.SoundEffect, accentMarker ? Times.Never() : Times.Once());
        accent.VerifyGet(s => s.SoundEffect, accentMarker ? Times.Once() : Times.Never());
    }

    [Fact]
    public void PlayMetronomeClick_WhenEachSoundFails_LogsOriginalOncePerSoundAndAgainForNewSession()
    {
        var logger = new Mock<ILogger<PerformanceStage>>();
        var beatFailure = new InvalidOperationException("beat failed");
        var accentFailure = new InvalidOperationException("accent failed");
        var beat = new Mock<ISound>();
        var accent = new Mock<ISound>();
        beat.SetupGet(s => s.SoundEffect).Throws(beatFailure);
        accent.SetupGet(s => s.SoundEffect).Throws(accentFailure);
        using var first = CreateMetronomeSession(logger.Object, beat.Object, accent.Object);
        var beatMarker = new BeatMarker { IsMeasureStart = false };
        var accentMarker = new BeatMarker { IsMeasureStart = true };
        first.PlayMetronomeClick(beatMarker);
        first.PlayMetronomeClick(beatMarker);
        first.PlayMetronomeClick(accentMarker);
        first.PlayMetronomeClick(accentMarker);
        beat.VerifyGet(s => s.SoundEffect, Times.Exactly(2));
        accent.VerifyGet(s => s.SoundEffect, Times.Exactly(2));
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Metronome click playback failed")),
            beatFailure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Metronome click playback failed")),
            accentFailure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        using var second = CreateMetronomeSession(logger.Object, beat.Object, accent.Object);
        second.PlayMetronomeClick(beatMarker);
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Metronome click playback failed")),
            beatFailure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(2));
    }

    [Fact]
    public void StopAll_StopsTheSongTimer()
    {
        var session = CreateSession();
        session.Bind(CreateChart(), CreateEmptyPreparedSet());
        session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), null);
        Assert.True(session.IsPlaying);

        session.StopAll();

        Assert.False(session.IsPlaying);
        Assert.False(session.IsPaused);
    }

    [AudioFact]
    public void StopAll_StopsLiveBgmAndChipInstancesWithoutDisposing()
    {
        using var bgm = PlayingSoundInstance.Create();
        var bgmInstance = bgm.Instance;
        var bgmSound = new Mock<ISound>();
        bgmSound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(bgmInstance);
        using var chip = PlayingSoundInstance.Create();
        var chipInstance = chip.Instance;
        var chipSound = new Mock<ISound>();
        chipSound.Setup(x => x.Play()).Returns(chipInstance);

        var bgmPath = Path.GetFullPath(WriteTempStubWav());
        var chipPath = Path.GetFullPath(WriteTempStubWav());
        var soundsByPath = new Dictionary<string, ISound>(StringComparer.Ordinal)
        {
            [bgmPath] = bgmSound.Object,
            [chipPath] = chipSound.Object,
        };
        var chart = CreateChart();
        chart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100.0, AudioFilePath = bgmPath });
        var preparedSet = PreparedGameplayAudioSet.PrepareAsync(
            mainBackgroundPath: null,
            scheduledBgmSourcePaths: new[] { bgmPath },
            chipSourcePathsByWavId: new Dictionary<string, string> { ["07"] = chipPath },
            modifiers: new PlaybackModifiers(100, 0),
            processor: null,
            cache: null,
            progress: null,
            cancellationToken: CancellationToken.None,
            defaultSoundLoader: path => soundsByPath[Path.GetFullPath(path)],
            preparedSoundFactory: (_, path) => soundsByPath[Path.GetFullPath(path)])
            .GetAwaiter()
            .GetResult();
        var session = CreateSession();
        session.Bind(chart, preparedSet);
        session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), chart);

        // Track one live instance each: the BGM event fires through Advance
        // (the instance survives reaping because it reports Playing) and the
        // chip plays through the cache's full-volume fast path.
        session.Advance(150.0);
        session.PlayChip(new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "07") { TimeMs = 120.0 });
        Assert.Equal(SoundState.Playing, bgmInstance.State);
        Assert.Equal(SoundState.Playing, chipInstance.State);
        Assert.Same(bgmInstance, Assert.Single(session.ActiveBgmInstances));

        session.StopAll();

        // Stop must be immediate but not terminal: instances report Stopped
        // while remaining undisposed, and the borrowed ISound objects stay
        // alive for the prepared set's own teardown.
        Assert.Equal(SoundState.Stopped, bgmInstance.State);
        Assert.Equal(SoundState.Stopped, chipInstance.State);
        Assert.False(bgmInstance.IsDisposed);
        Assert.False(chipInstance.IsDisposed);
        bgmSound.Verify(x => x.Dispose(), Times.Never);
        chipSound.Verify(x => x.Dispose(), Times.Never);
        Assert.False(session.IsPlaying);
        Assert.False(session.IsPaused);
    }

    [Fact]
    public void PlayChip_WhenNoteValueIsEmpty_NeverPlaysASound()
    {
        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(
            new Dictionary<string, ISound> { ["01"] = soundMock.Object });
        var session = CreateSession();
        session.Bind(CreateChart(), chipSet);

        var exception = Record.Exception(
            () => session.PlayChip(new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "")));

        Assert.Null(exception);
        soundMock.Verify(s => s.Play(), Times.Never);
        soundMock.Verify(
            s => s.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void PlayChip_WhenNoteIsNullOrSessionUnbound_DoesNotThrow()
    {
        var bound = CreateSession();
        bound.Bind(CreateChart(), CreateEmptyPreparedSet());
        var unbound = CreateSession();
        var note = new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "01") { TimeMs = 100.0 };

        Assert.Null(Record.Exception(() => bound.PlayChip(null)));
        Assert.Null(Record.Exception(() => unbound.PlayChip(note)));
    }

    [Fact]
    public void Dispose_DisposesPreparedSetAndClearsState()
    {
        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(
            new Dictionary<string, ISound> { ["01"] = soundMock.Object });
        var session = CreateSession();
        session.Bind(CreateChart(), chipSet);

        session.Dispose();

        soundMock.Verify(x => x.Dispose(), Times.Once);
        Assert.Null(session.PreparedSet);
        Assert.Null(session.Timer);
        Assert.Empty(session.ScheduledBgmEvents);
    }

    [Fact]
    public void Bind_WhenAudioLoaderConstructionFails_DisposesTransferredSetExactlyOnce()
    {
        var sound = new Mock<ISound>();
        var prepared = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["01"] = sound.Object });
        var session = new GameplayAudioSession(null, null, NullLogger<PerformanceStage>.Instance,
            new PlaybackModifiers(100, 0), false);

        Assert.Throws<ArgumentNullException>(() => session.Bind(CreateChart(), prepared));
        Assert.Same(prepared, session.PreparedSet);
        sound.Verify(s => s.Dispose(), Times.Never);
        session.Dispose();
        session.Dispose();
        sound.Verify(s => s.Dispose(), Times.Once);
        Assert.Null(session.PreparedSet);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreparedSet_RetiredQueuedOrLateResult_DisposesOwnedSoundExactlyOnce(bool queued)
    {
        var sound = new Mock<ISound>();
        var prepared = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["01"] = sound.Object });
        var scope = new StageBackgroundWorkScope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = 0;
        var drops = 0;
        var task = scope.RunAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return prepared;
        }, _ => applied++, onDropped: result =>
        {
            drops++;
            result.Dispose();
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (queued)
        {
            release.SetResult();
            Assert.Same(prepared, await task.WaitAsync(TimeSpan.FromSeconds(5)));
            sound.Verify(s => s.Dispose(), Times.Never);
        }
        scope.Retire();
        if (!queued)
        {
            release.SetResult();
            Assert.Same(prepared, await task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        scope.Retire();
        scope.Drain();
        prepared.Dispose();
        Assert.Equal(0, applied);
        Assert.Equal(1, drops);
        sound.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = CreateSession();
        session.Bind(CreateChart(), CreateEmptyPreparedSet());

        session.Dispose();
        var exception = Record.Exception(() => session.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void GetCurrentMs_WithoutTimer_ReturnsZero()
    {
        var session = CreateSession();

        Assert.Equal(0.0, session.GetCurrentMs(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.Zero)));
    }

    [Fact]
    public void IsPaused_ReflectsTimerPause()
    {
        var session = CreateSession();
        session.Bind(CreateChart(), CreateEmptyPreparedSet());
        session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), null);

        Assert.False(session.IsPaused);
        session.Timer!.Pause(new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.Zero));

        Assert.True(session.IsPaused);
        Assert.False(session.IsPlaying);
    }

    private static GameplayAudioSession CreateSession(
        IResourceManager? resourceManager = null,
        PlaybackModifiers? modifiers = null,
        bool metronomeEnabled = false,
        ILogger? logger = null,
        Action<BeatMarker>? playClick = null)
    {
        return new GameplayAudioSession(
            resourceManager ?? new Mock<IResourceManager>().Object,
            null,
            logger ?? NullLogger<PerformanceStage>.Instance,
            modifiers ?? new PlaybackModifiers(100, 0),
            metronomeEnabled,
            playClick);
    }

    private static GameplayAudioSession CreateMetronomeSession(ILogger logger, ISound beat, ISound accent)
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(x => x.TryLoadSound(SoundPath.MetronomeBeat, out beat)).Returns(true);
        resources.Setup(x => x.TryLoadSound(SoundPath.MetronomeAccent, out accent)).Returns(true);
        var session = CreateSession(resourceManager: resources.Object, metronomeEnabled: true, logger: logger);
        session.Bind(CreateChart(), CreateEmptyPreparedSet());
        return session;
    }

    private static ParsedChart CreateChart(Action<ParsedChart>? configure = null)
    {
        var chart = new ParsedChart("session-test.dtx") { Bpm = 120.0 };
        configure?.Invoke(chart);
        return chart;
    }

    private static PreparedGameplayAudioSet CreateEmptyPreparedSet()
    {
        return PreparedGameplayAudioSet.PrepareAsync(
            mainBackgroundPath: null,
            scheduledBgmSourcePaths: Array.Empty<string>(),
            chipSourcePathsByWavId: new Dictionary<string, string>(),
            modifiers: new PlaybackModifiers(100, 0),
            processor: null,
            cache: null,
            progress: null,
            cancellationToken: CancellationToken.None,
            defaultSoundLoader: _ => new Mock<ISound>().Object,
            preparedSoundFactory: (_, _) => new Mock<ISound>().Object)
            .GetAwaiter()
            .GetResult();
    }

    private static PreparedGameplayAudioSet CreatePreparedSetWithChips(
        IReadOnlyDictionary<string, ISound> chipsByWavId)
    {
        var chipPaths = new Dictionary<string, string>();
        var soundsByPath = new Dictionary<string, ISound>(StringComparer.Ordinal);
        foreach (var (wavId, sound) in chipsByWavId)
        {
            var path = Path.GetFullPath(WriteTempStubWav());
            chipPaths[wavId] = path;
            soundsByPath[path] = sound;
        }
        return PreparedGameplayAudioSet.PrepareAsync(
            mainBackgroundPath: null,
            scheduledBgmSourcePaths: Array.Empty<string>(),
            chipSourcePathsByWavId: chipPaths,
            modifiers: new PlaybackModifiers(100, 0),
            processor: null,
            cache: null,
            progress: null,
            cancellationToken: CancellationToken.None,
            defaultSoundLoader: path => soundsByPath[Path.GetFullPath(path)],
            preparedSoundFactory: (_, path) => soundsByPath[Path.GetFullPath(path)])
            .GetAwaiter()
            .GetResult();
    }

    private static PreparedGameplayAudioSet CreatePreparedSetWithMainBackground(
        SoundEffectInstance instance)
    {
        var mainSound = new Mock<ISound>();
        mainSound.Setup(x => x.CreateInstance()).Returns(instance);
        var mainPath = Path.GetFullPath(WriteTempStubWav());
        return PreparedGameplayAudioSet.PrepareAsync(
            mainBackgroundPath: mainPath,
            scheduledBgmSourcePaths: Array.Empty<string>(),
            chipSourcePathsByWavId: new Dictionary<string, string>(),
            modifiers: new PlaybackModifiers(100, 0),
            processor: null,
            cache: null,
            progress: null,
            cancellationToken: CancellationToken.None,
            defaultSoundLoader: _ => mainSound.Object,
            preparedSoundFactory: (_, __) => mainSound.Object)
            .GetAwaiter()
            .GetResult();
    }

    private static GameplayAudioSession CreateBgmSession(
        params (string WavId, double TimeMs, ISound? Sound)[] events)
        => CreateBgmSession(null, events);

    private static GameplayAudioSession CreateBgmSession(
        ILogger? logger,
        params (string WavId, double TimeMs, ISound? Sound)[] events)
    {
        var chart = CreateChart();
        var soundsByPath = new Dictionary<string, ISound>(StringComparer.Ordinal);
        var sourcePaths = new List<string>();
        foreach (var (wavId, timeMs, sound) in events)
        {
            var path = Path.GetFullPath(WriteTempStubWav());
            chart.AddBGMEvent(new BGMEvent { WavId = wavId, TimeMs = timeMs, AudioFilePath = path });
            if (sound != null)
            {
                sourcePaths.Add(path);
                soundsByPath[path] = sound;
            }
        }
        var preparedSet = PreparedGameplayAudioSet.PrepareAsync(
            mainBackgroundPath: null,
            scheduledBgmSourcePaths: sourcePaths,
            chipSourcePathsByWavId: new Dictionary<string, string>(),
            modifiers: new PlaybackModifiers(100, 0),
            processor: null,
            cache: null,
            progress: null,
            cancellationToken: CancellationToken.None,
            defaultSoundLoader: path => soundsByPath[Path.GetFullPath(path)],
            preparedSoundFactory: (_, path) => soundsByPath[Path.GetFullPath(path)])
            .GetAwaiter()
            .GetResult();
        var session = CreateSession(logger: logger);
        session.Bind(chart, preparedSet);
        return session;
    }

    private static string WriteTempStubWav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"session-stub-{Guid.NewGuid()}.wav");
        File.WriteAllBytes(path, new byte[] { 0 });
        return path;
    }

    /// <summary>
    /// Creates a real SoundEffectInstance without invoking its constructor
    /// (which requires a native audio device). Volume/Pan backing fields work
    /// on the uninitialized instance, while Play/Stop throw — which lets
    /// SongTimer.Play report a transport failure.
    /// </summary>
    private static SoundEffectInstance CreateSoundEffectInstance()
    {
#pragma warning disable SYSLIB0050
        return (SoundEffectInstance)FormatterServices.GetUninitializedObject(typeof(SoundEffectInstance));
#pragma warning restore SYSLIB0050
    }

}
