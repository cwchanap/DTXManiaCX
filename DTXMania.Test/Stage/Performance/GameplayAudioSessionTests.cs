using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
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
    /// on the uninitialized instance; Play/Stop are no-ops on it.
    /// </summary>
    private static SoundEffectInstance CreateSoundEffectInstance()
    {
#pragma warning disable SYSLIB0050
        return (SoundEffectInstance)FormatterServices.GetUninitializedObject(typeof(SoundEffectInstance));
#pragma warning restore SYSLIB0050
    }
}
