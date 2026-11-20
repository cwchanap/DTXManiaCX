using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using DTXMania.Test.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class PerformanceStageDeterministicTests
{
    [Fact]
    public void ExtractSharedData_WhenPayloadMissing_ShouldLeaveStageDataUnchanged()
    {
        var stage = CreateStage();

        ReflectionHelpers.SetPrivateField(stage, "_transitionPayload", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Null(ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
    }

    [Fact]
    public void ExtractSharedData_WhenSongPlayRequestExists_ShouldAssignAllFields()
    {
        var stage = CreateStage();
        var selectedSong = new SongListNode { Title = "Song" };
        var parsedChart = new ParsedChart("test.dtx");
        ReflectionHelpers.SetPrivateField(stage, "_transitionPayload", new SongPlayRequest(selectedSong, 3, parsedChart));

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Same(selectedSong, ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong"));
        Assert.Equal(3, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
        Assert.Same(parsedChart, ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
    }

    [Fact]
    public void PopulateTelemetry_WhenManagersExist_ShouldExposePerformanceState()
    {
        using var stage = CreateStageWithAutoPlay(Enumerable.Range(0, PerformanceUILayout.LaneCount).ToArray());
        var chart = new ParsedChart("telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        var chartManager = BindChart(stage, chart);
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var judgementManager = CreateActiveJudgement(stage);
        var selectedSong = new SongListNode { Title = "E2E AutoPlay Smoke" };

        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", selectedSong);
        ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 0);
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 1.5);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal("E2E AutoPlay Smoke", telemetry.SelectedSongTitle);
        Assert.Equal(0, telemetry.SelectedDifficulty);
        Assert.True(telemetry.AutoPlayEnabled);
        Assert.True(telemetry.PerformanceReady);
        Assert.False(telemetry.StageCompleted);
        Assert.Equal(1, telemetry.TotalNotes);
        Assert.Equal(0, telemetry.Score);
        Assert.Equal(0, telemetry.CurrentCombo);
        Assert.Equal(0, telemetry.MaxCombo);
        Assert.Equal(GaugeManager.StartingLife, telemetry.Gauge);
    }

    [Fact]
    public void PopulateTelemetry_WhenAutoPlayIsPartial_ShouldReportAutoPlayDisabled()
    {
        using var stage = CreateStageWithAutoPlay(0, 1);
        var chart = new ParsedChart("partial-autoplay-telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        BindChart(stage, chart);
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.False(telemetry.AutoPlayEnabled);
    }

    [Fact]
    public void PopulateTelemetry_WhenNotationStripReady_ShouldReportTabStyle()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), telemetry.NoteDisplayStyle);
    }

    [Fact]
    public void PopulateTelemetry_WhenNotationStripMissing_ShouldReportLanesStyle()
    {
        var stage = CreateStage();

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerMissing_ShouldReportPerformanceNotReady()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chart = new ParsedChart("telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        BindChart(stage, chart);

        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.5);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.False(telemetry.PerformanceReady);
    }

    [Fact]
    public void PopulateTelemetry_WhenAudioPreparationInProgress_ShouldExposeFrozenProfileAndMetrics()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(75, -3));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_audioPreparationProgress",
            new AudioPreparationProgress(
                CompletedCount: 3,
                TotalCount: 5,
                CurrentRole: "chip",
                CacheHitCount: 2,
                Elapsed: TimeSpan.FromSeconds(1),
                DecodedByteEstimate: 4096));

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal(75, telemetry.PlaySpeedPercent);
        Assert.Equal(-3, telemetry.PitchSemitones);
        Assert.True(telemetry.PlaybackProfileFrozen);
        Assert.Equal(3, telemetry.AudioPreparationCompleted);
        Assert.Equal(5, telemetry.AudioPreparationTotal);
        Assert.Equal(2, telemetry.AudioPreparationCacheHits);
        Assert.Equal(4096L, telemetry.PreparedAudioBytes);
    }

    [Fact]
    public void PopulateTelemetry_WhenAudioPreparationHasNotReported_ShouldExposeZeroMetrics()
    {
        var stage = CreateStage();

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal(0, telemetry.AudioPreparationCompleted);
        Assert.Equal(0, telemetry.AudioPreparationTotal);
        Assert.Equal(0, telemetry.AudioPreparationCacheHits);
        Assert.Equal(0L, telemetry.PreparedAudioBytes);
    }

    [Fact]
    public void PopulateTelemetry_WhenChartManagerMissing_ShouldReportPerformanceNotReady()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.0);
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.False(telemetry.PerformanceReady);
    }

    private static Task<PreparedGameplayAudioSet?> StartGameplayInitialization(PerformanceStage stage)
    {
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");
        return ReflectionHelpers.InvokePrivateMethod<Task<PreparedGameplayAudioSet?>>(
            stage, "StartGameplayInitialization")!;
    }

    private static PreparedGameplayAudioSet WaitForQueuedAudioSet(StageBackgroundWorkScope scope)
    {
        var gate = ReflectionHelpers.GetPrivateField<object>(scope, "_gate")!;
        var queue = ReflectionHelpers.GetPrivateField<System.Collections.IList>(scope, "_queue")!;
        PreparedGameplayAudioSet? captured = null;
        var queued = SpinWait.SpinUntil(
            () =>
            {
                lock (gate)
                {
                    foreach (var item in queue)
                    {
                        var drop = item?.GetType().GetProperty("Drop")?.GetValue(item) as Action;
                        var target = drop?.Target;
                        if (target == null)
                            continue;

                        foreach (var field in target.GetType().GetFields(
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic))
                        {
                            if (field.GetValue(target) is PreparedGameplayAudioSet audioSet)
                            {
                                captured = audioSet;
                                return true;
                            }
                        }
                    }
                }

                return false;
            },
            TimeSpan.FromSeconds(5));
        Assert.True(queued, "the prepared audio set was not queued on the scope in time");
        return captured!;
    }

    private static void RunUpdateLoopUntil(PerformanceStage stage, Func<bool> condition)
    {
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
        var settled = SpinWait.SpinUntil(
            () =>
            {
                stage.Update(0.016);
                return condition();
            },
            TimeSpan.FromSeconds(5));
        Assert.True(settled, "the gameplay initialization terminal was not applied within the timeout");
    }

    /// <summary>
    /// Drains the scope directly (the update-thread apply step) instead of driving
    /// stage.Update, for cases whose post-apply OnUpdate would touch components the
    /// reflection-created stage cannot safely initialize.
    /// </summary>
    private static void DrainScopeUntil(PerformanceStage stage, Func<bool> condition)
    {
        var settled = SpinWait.SpinUntil(
            () =>
            {
                stage.BackgroundWork?.Drain();
                return condition();
            },
            TimeSpan.FromSeconds(5));
        Assert.True(settled, "the gameplay initialization terminal was not applied within the timeout");
    }

    [Fact]
    public void StartGameplayInitialization_WhenChartHasNoBackgroundAudio_ShouldCreateSpeedAwareSilentSongTimer()
    {
        var stage = CreateInspectableStage();
        stage.InstallTestBackgroundScope();
        var chart = new ParsedChart("silent-clock.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();

        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", new MockInputManagerCompat());
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(50, 0));

        StartGameplayInitialization(stage);
        RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

        var timer = stage.AudioSession?.Timer;
        Assert.NotNull(timer);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

        var startTime = new GameTime(TimeSpan.FromMilliseconds(1000), TimeSpan.Zero);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", startTime);
        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.True(timer!.IsPlaying);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.Equal(250.0, timer.GetCurrentMs(new GameTime(TimeSpan.FromMilliseconds(1500), TimeSpan.Zero)));
    }

    [Fact]
    public void StartGameplayInitialization_WhenAudioBindingThrowsAfterPreparation_ShouldFailLoadingAndKeepStageOwnedAudioForCleanup()
    {
        // Preparation succeeds on the worker; binding then throws (null _resourceManager
        // makes the session's AudioLoader construction throw ArgumentNullException on
        // the update thread AFTER the prepared set's ownership transferred to the
        // session). The catch must call FailLoading with "Audio binding failed" and
        // the original exception, and CleanupComponents disposes the session-owned set
        // once.
        var directory = CreateTempDirectory();
        try
        {
            var stage = CreateInspectableStage();
            stage.InstallTestBackgroundScope();
            var logger = new Mock<ILogger<PerformanceStage>>();
            ReflectionHelpers.SetPrivateField(stage, "_logger", logger.Object);
            var chart = new ParsedChart("binding-fail.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            // A real loadable WAV makes the prepared set's disposal observable.
            chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(
                directory,
                "binding-fail-main.wav");

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_noteRenderer",
                ReflectionHelpers.CreateUninitialized<NoteRenderer>());
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

            StartGameplayInitialization(stage);
            DrainScopeUntil(stage, () => !ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Contains(
                "Audio binding failed",
                ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
            logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains("Audio binding failed", StringComparison.Ordinal)),
                    It.Is<Exception>(failure => failure is ArgumentNullException),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            // Ownership was transferred before the throw, so the stage owns the set;
            // it must still be live until CleanupComponents disposes it.
            var preparedSet = stage.AudioSession?.PreparedSet;
            Assert.NotNull(preparedSet);
            var mainBackground = preparedSet!.MainBackground;
            Assert.NotNull(mainBackground);
            Assert.False(mainBackground!.IsDisposed);

            ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

            Assert.True(mainBackground.IsDisposed);
            Assert.Null(stage.AudioSession?.PreparedSet);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenChartSetupThrowsBeforeAudioBinding_ShouldFailWithChartSetupErrorAndDisposePreparedAudio()
    {
        // Zero BPM makes NoteRenderer.SetBpm throw during ApplyParsedChart (before
        // audio binding). That failure must be logged as "Chart setup failed" rather
        // than "Audio binding failed", and ApplyPreparedAudio must not retain the audio.
        var directory = CreateTempDirectory();
        try
        {
            var stage = CreateInspectableStage();
            stage.InstallTestBackgroundScope();
            var logger = new Mock<ILogger<PerformanceStage>>();
            ReflectionHelpers.SetPrivateField(stage, "_logger", logger.Object);
            var chart = new ParsedChart("bad-bpm.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.Bpm = 0.0;
            chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(
                directory,
                "bad-bpm-main.wav");

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_noteRenderer",
                ReflectionHelpers.CreateUninitialized<NoteRenderer>());
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

            // Awaiting the worker task IS the audio terminal: the chart post and the
            // terminal WorkItem are both queued by the time the task completes, so a
            // single drain applies them FIFO — the chart-setup failure, then the
            // early-returned audio apply whose finally disposes the prepared set.
            var run = StartGameplayInitialization(stage);
            Assert.True(
                run.Wait(TimeSpan.FromSeconds(5)),
                "the audio preparation worker did not complete in time");
            var preparedSet = run.Result;
            Assert.NotNull(preparedSet);
            var mainBackground = preparedSet!.MainBackground;
            Assert.NotNull(mainBackground);

            stage.BackgroundWork?.Drain();

            Assert.True(
                mainBackground!.IsDisposed,
                "the prepared set was not disposed after the chart-setup failure");
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Contains(
                "Chart setup failed",
                ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
            Assert.Null(stage.AudioSession?.PreparedSet);
            logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains("Chart setup failed", StringComparison.Ordinal)),
                    It.Is<Exception>(failure => failure is ArgumentException),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenAudioPreparationFails_ShouldFailLoadingAndLogOriginalException()
    {
        // A worker fault reaches the scope's onError callback on the update thread,
        // which surfaces the failure via FailLoading. The original exception instance
        // must reach the error log (identity preserved), not just its message.
        var logger = new StageTestLogger();
        var game = CreateHeadlessGame(logger, new ConfigData { PlaySpeedPercent = 50, PitchSemitones = 3 });
        using var stage = new ThrowingAudioVariantStage(game);
        var chart = new ParsedChart("variant-failure.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => logger.Errors.Any(e => e.Message.Contains("Audio preparation failed")));

        // Chart-first ordering holds even on dependency failure: the worker
        // published the chart (ChartManager is only created by ApplyParsedChart)
        // before the throwing factory's fault reached the error path — and the
        // failed audio set is never bound.
        Assert.NotNull(stage.Rules?.ChartManager);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.False(telemetry.PerformanceReady);
        Assert.Equal(1, telemetry.TotalNotes);
        Assert.Null(stage.AudioSession?.PreparedSet);
        var error = Assert.Single(logger.Errors, entry => entry.Message.Contains("Audio preparation failed"));
        Assert.Same(stage.Failure, error.Exception);
    }

    [Fact]
    public void StartGameplayInitialization_WhenFactoryResolutionThrows_ShouldPublishChartBeforeFailing()
    {
        var logger = new StageTestLogger();
        var game = CreateHeadlessGame(logger, new ConfigData { PlaySpeedPercent = 50, PitchSemitones = 3 });
        using var stage = new SyncThrowingAudioVariantStage(game);
        var chart = new ParsedChart("variant-resolution-failure.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 0 });
        GameTelemetrySnapshot? atError = null;
        GameplayAudioSession? sessionAtError = null;
        logger.OnError = (message, _) =>
        {
            if (message.Contains("Audio preparation failed", StringComparison.Ordinal))
            {
                atError = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(atError);
                sessionAtError = stage.AudioSession;
            }
        };

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => atError != null);

        Assert.Equal(1, atError!.TotalNotes);
        Assert.False(atError.PerformanceReady);
        Assert.Null(sessionAtError);
        Assert.Null(stage.AudioSession);
        var error = Assert.Single(logger.Errors, entry => entry.Message.Contains("Audio preparation failed"));
        Assert.Same(stage.Failure, error.Exception);
    }

    [Fact]
    public void StartGameplayInitialization_WhenResultQueuedBeforeDeactivation_ShouldDropItAndDisposeThePreparedSetExactlyOnce()
    {
        // The worker finished and its terminal sat in the scope queue when the stage
        // deactivated: BaseStage.Deactivate retires the scope synchronously BEFORE
        // OnDeactivate's teardown, so the stale never-applied result is dropped via
        // onDropped — its prepared audio set is disposed there, never applied.
        var directory = CreateTempDirectory();
        try
        {
            var stage = CreateInspectableStage();
            stage.InstallTestBackgroundScope();
            var chart = new ParsedChart("late-drop.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            // A real loadable WAV makes the prepared set's disposal observable even
            // though the result is never applied to the stage.
            chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, "late-drop-main.wav");

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

            var scope = stage.BackgroundWork!;
            StartGameplayInitialization(stage);

            // Wait until the worker queues its terminal WorkItem on the scope.
            var preparedSet = WaitForQueuedAudioSet(scope);
            var mainBackground = preparedSet.MainBackground;
            Assert.NotNull(mainBackground);
            Assert.False(mainBackground!.IsDisposed);
            // The result is queued but never applied.
            Assert.Null(stage.AudioSession?.PreparedSet);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            stage.Deactivate();

            Assert.True(
                SpinWait.SpinUntil(() => mainBackground.IsDisposed, TimeSpan.FromSeconds(5)),
                "the dropped prepared set was not disposed");
            Assert.Null(stage.AudioSession?.PreparedSet);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenPreparationIsCanceledMidFlight_ShouldLeaveLoadingStateUntouched()
    {
        // Retirement cancels the scope token while the worker is parked inside the
        // audio preparation. The canceled preparation must neither fail the stage nor
        // leave a half-owned audio set behind. A canceled operation ends silently
        // (the scope contract pins that an OCE queues nothing and post-retirement
        // terminals drop — StageBackgroundWorkScopeTests), so the test asserts the
        // untouched state while the operation is still fully alive and parked
        // (deterministic), waits for the worker task — the canceled chain's actual
        // terminal — to observe the full unwind, and only then starts the fresh
        // activation, so the final unchanged-state assertions cannot run ahead of it.
        //
        // The park deliberately sits in the DEFAULT profile (its up-front
        // load-failure report), with an existing scheduled source keeping a
        // loop-entry cancellation check in play after the park. The variant path is
        // unusable here: its cache awaits the underlying processor through a
        // WaitAsync'd shared operation, so retirement detaches the waiter and lets
        // the cache caller, preparation and worker unwind while the processor stays
        // parked — the worker task then completes without witnessing the underlying
        // unwind. In this arrangement nothing runs detached from the awaited chain,
        // so the task's completion genuinely establishes that every continuation
        // finished unwinding.
        var directory = CreateTempDirectory();
        try
        {
            var logger = new BlockingLoadFailureLogger(gates: 1);
            var stage = CreateInspectableStage();
            ReflectionHelpers.SetPrivateField(stage, "_logger", logger);
            stage.InstallTestBackgroundScope();
            var chart = new ParsedChart("canceled-prep.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            // A missing main source parks the worker in the up-front load-failure
            // report; the existing scheduled source keeps a loop-entry cancellation
            // check in play after the park, so the released unwind is a cancellation.
            chart.BackgroundAudioPath = Path.Combine(directory, "canceled-missing-main.wav");
            chart.AddBGMEvent(new BGMEvent
            {
                AudioFilePath = CreatePlaceholderSource(directory, "canceled-bgm.wav")
            });

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

            var canceledRun = StartGameplayInitialization(stage);
            Assert.True(
                SpinWait.SpinUntil(() => logger.ParkedCount >= 1, TimeSpan.FromSeconds(5)),
                "the worker never parked inside its load-failure report");

            // Retirement cancels the scope token synchronously before OnDeactivate while
            // the worker is parked in test code: from this moment the operation's
            // fate (silent cancellation) is deterministic.
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            stage.Deactivate();

            // The operation is still fully alive and parked: the loading state must
            // already be untouched — the strongest, race-free form of the invariant.
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
            Assert.Null(stage.AudioSession?.PreparedSet);

            // Awaiting the released worker's task observes the full unwind — the
            // canceled chain's real terminal — before the replacement run starts.
            logger.ReleaseNext();
            Assert.True(
                canceledRun.Wait(TimeSpan.FromSeconds(5)),
                "the canceled worker did not unwind in time");
            Assert.Null(canceledRun.Result);

            // Re-establish the fields CleanupComponents nulled, then run a fresh
            // activation to its own observed terminal and ready state.
            var freshChart = new ParsedChart("canceled-fresh.dtx");
            freshChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            freshChart.FinalizeChart();
            freshChart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, "canceled-fresh-main.wav");
            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", freshChart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            // Default profile so the fresh run does not touch the gated processor.
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

            stage.InstallTestBackgroundScope();
            StartGameplayInitialization(stage);
            RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

            // Final unchanged-state assertions, after both terminals: the canceled
            // operation provably unwound before the fresh run started, and the
            // fresh run applied only its own audio.
            var freshSet = stage.AudioSession?.PreparedSet;
            Assert.NotNull(freshSet);
            Assert.NotNull(freshSet!.MainBackground);
            Assert.False(freshSet.MainBackground!.IsDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
            // Both operations are terminally complete, so nothing can land anymore;
            // a few extra update frames plus a re-assert keep that property pinned.
            for (var i = 0; i < 8; i++)
            {
                stage.Update(0.016);
            }
            Assert.Same(freshSet, stage.AudioSession?.PreparedSet);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_ShouldPublishAudioProgressThroughTheScopeAndApplyItBeforeReady()
    {
        // Audio preparation progress flows through a Latest reporter: the mid-flight
        // report applies on the update-thread drain before the terminal does, and the
        // final report lands with the ready state.
        var directory = CreateTempDirectory();
        try
        {
            var stage = CreateGatedVariantStage();
            stage.InstallTestBackgroundScope();
            var chart = new ParsedChart("progress-prep.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "progress-main.wav");

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(50, 0));

            StartGameplayInitialization(stage);
            Assert.True(
                SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)),
                "the worker never reached the variant processor");

            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            AudioPreparationProgress? midFlight = null;
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        stage.Update(0.016);
                        midFlight = ReflectionHelpers.GetPrivateField<AudioPreparationProgress>(stage, "_audioPreparationProgress");
                        return midFlight != null;
                    },
                    TimeSpan.FromSeconds(5)),
                "the mid-flight progress report was never applied");
            Assert.Equal(0, midFlight!.CompletedCount);
            Assert.Equal(1, midFlight.TotalCount);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

            stage.ReleaseProcessor.TrySetResult();
            RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

            var finalProgress = ReflectionHelpers.GetPrivateField<AudioPreparationProgress>(stage, "_audioPreparationProgress");
            Assert.NotNull(finalProgress);
            Assert.Equal(1, finalProgress!.CompletedCount);
            Assert.Equal(1, finalProgress.TotalCount);
            var preparedSet = stage.AudioSession?.PreparedSet;
            Assert.NotNull(preparedSet);
            var timer = stage.AudioSession?.Timer;
            Assert.NotNull(timer);
            Assert.Equal(preparedSet!.RuntimePitch, timer!.Pitch);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenWorkerReportsSoundLoadFailure_ShouldLogThroughTheLoggerResolvedBeforeRun()
    {
        // The worker must not resolve BaseStage.Logger: the property lazily assigns
        // _logger and reads _game.LoggerFactory, which would mutate stage state from
        // the worker thread. The logger is resolved on the update thread before Run
        // and captured into the worker, so every ILoggerFactory.CreateLogger call
        // must happen on the calling (update) thread and the worker's sound-load
        // failure report must flow through that captured logger.
        var directory = CreateTempDirectory();
        try
        {
            var game = ReflectionHelpers.CreateGame();
            var factory = new CapturingLoggerFactory();
            ReflectionHelpers.SetPrivateField(game, "_loggerFactory", factory);
            var stage = CreateStage(game);
            // Leave _logger unset (null): resolving it is exactly what the test
            // monitors.
            ReflectionHelpers.SetPrivateField(stage, "_logger", null);
            stage.InstallTestBackgroundScope();
            var chart = new ParsedChart("worker-log.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            // A referenced-but-missing source makes the default-profile preparation
            // report a load failure through the worker's logging callback.
            chart.BackgroundAudioPath = Path.Combine(directory, "missing-main.wav");

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

            StartGameplayInitialization(stage);

            Assert.True(
                SpinWait.SpinUntil(
                    () => factory.Errors.Any(entry =>
                        entry.Message.Contains("Gameplay audio load failed", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(5)),
                "the worker's sound-load failure was never logged");
            Assert.Equal(1, factory.CreationThreadIds.Count);
            Assert.Equal(
                Environment.CurrentManagedThreadId,
                factory.CreationThreadIds.Single());
            var (_, exception) = factory.Errors.Single(entry =>
                entry.Message.Contains("Gameplay audio load failed", StringComparison.Ordinal));
            Assert.IsType<FileNotFoundException>(exception);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenASupersededPreparationCompletesAfterRetirement_ShouldDropItAndLeaveTheFreshRunUntouched()
    {
        // Retirement can land after a preparation's final cancellation check: the
        // checks sit at each loop iteration's entry, never atomically with the
        // return, so a worker parked inside the default-profile load-failure report
        // — which runs after its iteration's check — still completes successfully
        // after the fresh activation retired the stale scope. That late success
        // must take the retired scope's drop-direct path (onDropped disposes its
        // audio set, exactly once per result) and never touch the fresh run.
        //
        // Both properties are observed directly, not inferred: the stage method
        // returns the worker task, so waiting on the stale task IS the stale
        // operation's terminal (no drain loop can run ahead of an unscheduled
        // worker), and the task's payload hands the test the dropped set whose
        // real loaded main background must be disposed. A zero-BPM landmine on
        // the stale chart (a real NoteRenderer whose SetBpm(0) throws) remains as
        // a tripwire: ANY regression that applies the stale result — whenever it
        // lands — fails the stage with a sticky load error the assertions catch.
        var directory = CreateTempDirectory();
        try
        {
            var logger = new BlockingLoadFailureLogger(gates: 2);
            var stage = CreateInspectableStage();
            ReflectionHelpers.SetPrivateField(stage, "_logger", logger);
            stage.InstallTestBackgroundScope();

            var staleChart = new ParsedChart("late-success.dtx");
            staleChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            staleChart.FinalizeChart();
            staleChart.Bpm = 0.0;
            // A real loadable main plus an existing-but-unloadable scheduled BGM:
            // the sources are visited main-first (the scope collects them into a
            // set in main, then scheduled order), so the corrupt BGM's in-loop
            // load-failure report parks the worker in the FINAL loop iteration —
            // after the last cancellation check — and the prepared set owns the
            // real main background whose disposal the drop must produce. (If the
            // order ever changed, the released worker would hit the next
            // iteration's cancellation check and complete with no result — the
            // assertions below fail loudly, not flakily.)
            staleChart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, "late-success-main.wav");
            staleChart.AddBGMEvent(new BGMEvent
            {
                AudioFilePath = CreatePlaceholderSource(directory, "late-success-bgm.wav")
            });

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", staleChart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", ReflectionHelpers.CreateUninitialized<NoteRenderer>());
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

            var staleRun = StartGameplayInitialization(stage);
            Assert.True(
                SpinWait.SpinUntil(() => logger.ParkedCount >= 1, TimeSpan.FromSeconds(5)),
                "the stale worker never parked inside its load-failure report");

            // Mid-flight (worker parked): the stage is untouched.
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
            Assert.Null(stage.AudioSession?.PreparedSet);

            // The fresh activation retires the stale scope (exactly what Deactivate
            // does) and starts its own run: a real WAV main plus a corrupt
            // scheduled-BGM source whose load-failure report parks the fresh
            // worker, making the fresh run observably mid-flight while the stale
            // completion is still held. Whether the fresh loads run before or after
            // the park, the fresh result carries the same live main background.
            var freshChart = new ParsedChart("late-success-fresh.dtx");
            freshChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            freshChart.AddBGMEvent(new BGMEvent
            {
                AudioFilePath = CreatePlaceholderSource(directory, "late-success-fresh-bgm.wav")
            });
            freshChart.FinalizeChart();
            freshChart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, "late-success-fresh-main.wav");
            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", freshChart);

            stage.InstallTestBackgroundScope();
            StartGameplayInitialization(stage);
            Assert.True(
                SpinWait.SpinUntil(() => logger.ParkedCount >= 2, TimeSpan.FromSeconds(5)),
                "the fresh worker never parked mid-run");

            // Release the stale park only: the superseded preparation runs to its
            // return on the retired scope and drops directly. The worker task is
            // the stale operation's terminal, and its payload is the dropped set —
            // its disposal is observed, not inferred.
            logger.ReleaseNext();
            Assert.True(
                staleRun.Wait(TimeSpan.FromSeconds(5)),
                "the stale worker did not complete after its park was released");
            var staleSet = staleRun.Result;
            Assert.NotNull(staleSet);
            Assert.NotNull(staleSet!.MainBackground);
            Assert.True(
                staleSet.MainBackground!.IsDisposed,
                "the retired scope did not dispose the superseded audio set");

            Assert.Null(stage.AudioSession?.PreparedSet);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));

            // Now release the fresh park and drive the fresh run to its observed
            // terminal and ready state.
            logger.ReleaseNext();
            DrainScopeUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

            // Post-terminal assertions: the fresh run applied only its own live
            // audio and the late success never landed anywhere. Both operations
            // have observed terminals, so nothing can land anymore; a few extra
            // drains plus a re-assert keep that property pinned.
            var freshSet = stage.AudioSession?.PreparedSet;
            Assert.NotNull(freshSet);
            Assert.NotNull(freshSet!.MainBackground);
            Assert.False(freshSet.MainBackground!.IsDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
            Assert.Same(freshChart, ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
            for (var i = 0; i < 8; i++)
            {
                stage.BackgroundWork?.Drain();
            }
            Assert.Same(freshSet, stage.AudioSession?.PreparedSet);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    #region Final-review regressions: scroll speed apply and pad feedback during preparation

    [Fact]
    public void StartGameplayInitialization_WhenScrollSpeedChangesDuringPreparation_ShouldApplyCurrentConfigSpeedNotTheSnapshot()
    {
        // PageUp/PageDown stay active while loading (HandleInput runs every update),
        // so the success callback must read the config at apply time on the update
        // thread. Applying the activation snapshot would snap the renderer back to
        // the stale value and diverge from the live config and the toast.
        var directory = CreateTempDirectory();
        try
        {
            var config = new ConfigData { ScrollSpeed = 100 };
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
            var stage = CreateGatedVariantStage(game);
            stage.InstallTestBackgroundScope();
            var chart = new ParsedChart("scroll-change.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "scroll-change-main.wav");

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", CreateNoteRenderer());
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(50, 0));

            StartGameplayInitialization(stage);
            Assert.True(
                SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)),
                "the worker never reached the variant processor");

            // The user changes the scroll speed while preparation is still in flight.
            config.ScrollSpeed = 200;

            stage.ReleaseProcessor.TrySetResult();
            RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

            var renderer = ReflectionHelpers.GetPrivateField<NoteRenderer>(stage, "_noteRenderer");
            Assert.NotNull(renderer);
            // 200% → 2.0× the base pixels/ms. The stale 100% snapshot would leave 1.0×.
            Assert.Equal(
                PerformanceUILayout.NoteBaseScrollPixelsPerMs * 2.0,
                renderer!.ScrollPixelsPerMs,
                8);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WireUpInputEventHandlers_WhenCalledRepeatedly_ShouldSubscribeExactlyOnce()
    {
        var stage = CreateStage();
        var inputManager = new MockInputManagerCompat();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        // Installed at InitializeComponents (before preparation) and again by the
        // gameplay-manager init; the second call must not double-subscribe.
        ReflectionHelpers.InvokePrivateMethod(stage, "WireUpInputEventHandlers");
        ReflectionHelpers.InvokePrivateMethod(stage, "WireUpInputEventHandlers");

        Assert.Equal(1, CountPadFeedbackSubscribers(stage, inputManager));
    }

    [Fact]
    public void OnLaneHitForPadFeedback_DuringLoading_ShouldLightThePad()
    {
        // Manual hits during loading/countdown must light the pad: the pad-feedback
        // handler is wired before preparation starts, and OnLaneHitForPadFeedback
        // deliberately supports the loading state (no song timer yet).
        var stage = CreateStage();
        var inputManager = new MockInputManagerCompat();
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", true);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);

        ReflectionHelpers.InvokePrivateMethod(stage, "WireUpInputEventHandlers");
        inputManager.TriggerLaneHit(3);

        Assert.Equal(PadState.Pressed, PadStateOf(padRenderer, 3));
    }

    [Fact]
    public void StartGameplayInitialization_WhenPreparationFails_ShouldKeepPadFeedbackWired()
    {
        // Immediate visual feedback is independent of successful preparation: the
        // handler installed at component initialization must survive FailLoading so
        // hits still light pads on the failure screen, with no double subscription.
        var stage = CreateThrowingAudioVariantStage();
        stage.InstallTestBackgroundScope();
        var inputManager = new MockInputManagerCompat();
        var padRenderer = CreatePadRenderer();
        var chart = new ParsedChart("failed-prep-pads.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();

        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
        // Non-default profile: the update thread resolves the overridden
        // processor factory, whose worker-side invocation then throws the
        // stage's sentinel failure — after the worker publishes the chart.
        ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(50, 3));

        ReflectionHelpers.InvokePrivateMethod(stage, "WireUpInputEventHandlers");
        StartGameplayInitialization(stage);
        DrainScopeUntil(stage, () => !ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));

        Assert.Contains(
            "Audio preparation failed",
            ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
        inputManager.TriggerLaneHit(3);
        Assert.Equal(PadState.Pressed, PadStateOf(padRenderer, 3));
        Assert.Equal(1, CountPadFeedbackSubscribers(stage, inputManager));
    }

    [Fact]
    public void CleanupGameplayManagers_ShouldUnsubscribePadFeedback()
    {
        // The early install is balanced by the deactivate-time cleanup: no leaked
        // subscription survives teardown.
        var stage = CreateStage();
        var inputManager = new MockInputManagerCompat();
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "WireUpInputEventHandlers");
        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupGameplayManagers");

        Assert.Equal(0, CountPadFeedbackSubscribers(stage, inputManager));
        inputManager.TriggerLaneHit(3);
        Assert.Equal(PadState.Idle, PadStateOf(padRenderer, 3));
    }

    #endregion

    [Fact]
    public void PlayMetronomeClick_WhenBeatSoundPersistentlyFails_ShouldReportOncePerRun()
    {
        // A metronome sound whose playback throws on every beat must not fill the
        // error log: only the first failure per affected sound is reported.
        var logger = new Mock<ILogger<PerformanceStage>>();
        var failure = new InvalidOperationException("Simulated metronome playback failure");
        var beatSound = new Mock<ISound>();
        beatSound.SetupGet(sound => sound.SoundEffect).Throws(failure);
        var session = CreateMetronomeSession(logger.Object, beatSound: beatSound.Object);

        var marker = new BeatMarker { TimeMs = 100.0, IsMeasureStart = false };
        session.PlayMetronomeClick(marker);
        session.PlayMetronomeClick(marker);
        session.PlayMetronomeClick(marker);

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Metronome click playback failed", StringComparison.Ordinal)),
                failure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void PlayMetronomeClick_WhenEachSoundFails_ShouldReportOncePerAffectedSound()
    {
        var logger = new Mock<ILogger<PerformanceStage>>();
        var beatFailure = new InvalidOperationException("Simulated beat failure");
        var accentFailure = new InvalidOperationException("Simulated accent failure");
        var beatSound = new Mock<ISound>();
        beatSound.SetupGet(sound => sound.SoundEffect).Throws(beatFailure);
        var accentSound = new Mock<ISound>();
        accentSound.SetupGet(sound => sound.SoundEffect).Throws(accentFailure);
        var session = CreateMetronomeSession(
            logger.Object,
            beatSound: beatSound.Object,
            accentSound: accentSound.Object);

        session.PlayMetronomeClick(new BeatMarker { TimeMs = 100.0, IsMeasureStart = false });
        session.PlayMetronomeClick(new BeatMarker { TimeMs = 200.0, IsMeasureStart = true });
        session.PlayMetronomeClick(new BeatMarker { TimeMs = 300.0, IsMeasureStart = false });
        session.PlayMetronomeClick(new BeatMarker { TimeMs = 400.0, IsMeasureStart = true });

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Metronome click playback failed", StringComparison.Ordinal)),
                beatFailure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Metronome click playback failed", StringComparison.Ordinal)),
                accentFailure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void PlayMetronomeClick_WhenNewSessionStarts_ShouldReportNextFailureAgain()
    {
        var logger = new Mock<ILogger<PerformanceStage>>();
        var failure = new InvalidOperationException("Simulated metronome playback failure");
        var beatSound = new Mock<ISound>();
        beatSound.SetupGet(sound => sound.SoundEffect).Throws(failure);
        var marker = new BeatMarker { TimeMs = 100.0, IsMeasureStart = false };

        var firstSession = CreateMetronomeSession(logger.Object, beatSound: beatSound.Object);
        firstSession.PlayMetronomeClick(marker);
        firstSession.PlayMetronomeClick(marker);

        // A new activation creates a fresh session: suppression is scoped to the
        // session, so a persistently broken sound is reported once per run
        // instead of once per process lifetime.
        var secondSession = CreateMetronomeSession(logger.Object, beatSound: beatSound.Object);
        secondSession.PlayMetronomeClick(marker);

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Metronome click playback failed", StringComparison.Ordinal)),
                failure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public void LoadPerformanceUIAssets_WhenResourceManagerReturnsNull_ShouldLeaveAllTexturesNull()
    {
        // Each per-asset load is best-effort via TryLoadTexture; a missing skin must not
        // abort the whole load. This exercises every TryLoadTexture call site in the method.
        var stage = CreateStage();
        var resourceManager = new Mock<IResourceManager>();
        ITexture missingTexture = null;
        resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out missingTexture)).Returns(false);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "LoadPerformanceUIAssets"));

        Assert.Null(exception);
        Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_backgroundTexture"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_shutterTexture"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_skillPanelTexture"));
        // Every asset path should have been attempted exactly once, through the
        // optional-probe API so the misses stay below Warning.
        resourceManager.Verify(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny), Times.AtLeastOnce);
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadSkillPanelTexture_WhenThemedOverrideIs1x1Fallback_ShouldDisposeFallbackAndRetryWithDefault()
    {
        // Regression test for a GPU texture leak: when a custom/partial skin
        // omits Performance.SkillPanelTexture (or points it at a missing file),
        // ResourceManager.LoadTexture returns a fresh uncached 1x1 fallback.
        // RemoveReference() does not dispose (by design — disposal is reserved
        // for EvictSkinDependentCache / CollectUnusedResources, neither of
        // which can see uncached fallbacks), so the previous implementation
        // leaked one GPU Texture2D per Performance-stage activation. The fix
        // is to Dispose() the fallback before retrying with the NX default.
        var stage = CreateStage();

        const string themedPath = "Graphics/7_SkillPanel_perf.png";
        var theme = SkinTheme.Parse(new[] { $"Performance.SkillPanelTexture={themedPath}" });

        var fallbackTexture = new Mock<ITexture>();
        fallbackTexture.SetupGet(x => x.Width).Returns(1);
        fallbackTexture.SetupGet(x => x.Height).Returns(1);
        fallbackTexture.Setup(x => x.RemoveReference());
        fallbackTexture.Setup(x => x.Dispose());

        var defaultTexture = new Mock<ITexture>();
        defaultTexture.SetupGet(x => x.Width).Returns(180);
        defaultTexture.SetupGet(x => x.Height).Returns(96);

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.SetupGet(x => x.CurrentTheme).Returns(theme);
        var themedResult = fallbackTexture.Object;
        var defaultResult = defaultTexture.Object;
        resourceManager.Setup(x => x.TryLoadTexture(themedPath, out themedResult)).Returns(false);
        resourceManager.Setup(x => x.TryLoadTexture(TexturePath.SkillPanel, out defaultResult)).Returns(true);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var result = (ITexture?)ReflectionHelpers.InvokePrivateMethod(stage, "LoadSkillPanelTexture", theme);

        Assert.Same(defaultTexture.Object, result);
        // The fallback must be disposed (not merely RemoveReference'd) so the
        // underlying GPU Texture2D is released. RemoveReference alone leaves
        // the fallback alive with no owner, leaking one texture per activation.
        fallbackTexture.Verify(x => x.Dispose(), Times.Once);
        fallbackTexture.Verify(x => x.RemoveReference(), Times.Never);
        resourceManager.Verify(x => x.TryLoadTexture(TexturePath.SkillPanel, out It.Ref<ITexture>.IsAny), Times.Once);
    }

    [Fact]
    public void LoadSkillPanelTexture_WhenThemedOverrideEqualsDefaultPath_ShouldReturnNullWithoutDisposing()
    {
        // When the resolved override path is already the NX default and that
        // load fails (1x1 fallback), there is nothing left to retry. The
        // method returns null and must still dispose the fallback (the leak
        // applies equally to this branch — the fallback is uncached either
        // way) but must not attempt a redundant second load of the same path.
        var stage = CreateStage();

        // Themeless skin resolves to TexturePath.SkillPanel (the default).
        var theme = SkinTheme.Empty;

        var fallbackTexture = new Mock<ITexture>();
        fallbackTexture.SetupGet(x => x.Width).Returns(1);
        fallbackTexture.SetupGet(x => x.Height).Returns(1);
        fallbackTexture.Setup(x => x.RemoveReference());
        fallbackTexture.Setup(x => x.Dispose());

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.SetupGet(x => x.CurrentTheme).Returns(theme);
        var fallbackResult = fallbackTexture.Object;
        resourceManager.Setup(x => x.TryLoadTexture(TexturePath.SkillPanel, out fallbackResult)).Returns(false);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var result = (ITexture?)ReflectionHelpers.InvokePrivateMethod(stage, "LoadSkillPanelTexture", theme);

        Assert.Null(result);
        // Single load attempt (no retry on the same default path).
        resourceManager.Verify(x => x.TryLoadTexture(TexturePath.SkillPanel, out It.Ref<ITexture>.IsAny), Times.Once);
        // Fallback is still disposed — the leak fix applies to both branches.
        fallbackTexture.Verify(x => x.Dispose(), Times.Once);
        fallbackTexture.Verify(x => x.RemoveReference(), Times.Never);
    }

    [Fact]
    public void LoadSkillPanelTexture_WhenDefaultAssetAlsoIs1x1Fallback_ShouldDisposeAndReturnNull()
    {
        // Regression test for a second GPU texture leak: when both the themed
        // override and the NX default asset resolve to CreateFallbackTexture's
        // 1x1 fallback (e.g. a broken installation missing both files), the
        // previous implementation returned the second fallback without
        // validating it. Like the first fallback, it is uncached and has
        // AddReference called, but CleanupPerformanceUIAssets only calls
        // RemoveReference (which does not dispose), so it leaked one GPU
        // Texture2D per activation. The fix validates the second result too,
        // disposes it, and returns null so the panel is simply not drawn.
        var stage = CreateStage();

        const string themedPath = "Graphics/7_SkillPanel_perf.png";
        var theme = SkinTheme.Parse(new[] { $"Performance.SkillPanelTexture={themedPath}" });

        var themedFallback = new Mock<ITexture>();
        themedFallback.SetupGet(x => x.Width).Returns(1);
        themedFallback.SetupGet(x => x.Height).Returns(1);
        themedFallback.Setup(x => x.RemoveReference());
        themedFallback.Setup(x => x.Dispose());

        var defaultFallback = new Mock<ITexture>();
        defaultFallback.SetupGet(x => x.Width).Returns(1);
        defaultFallback.SetupGet(x => x.Height).Returns(1);
        defaultFallback.Setup(x => x.RemoveReference());
        defaultFallback.Setup(x => x.Dispose());

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.SetupGet(x => x.CurrentTheme).Returns(theme);
        var themedResult = themedFallback.Object;
        var defaultResult = defaultFallback.Object;
        resourceManager.Setup(x => x.TryLoadTexture(themedPath, out themedResult)).Returns(false);
        resourceManager.Setup(x => x.TryLoadTexture(TexturePath.SkillPanel, out defaultResult)).Returns(false);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var result = (ITexture?)ReflectionHelpers.InvokePrivateMethod(stage, "LoadSkillPanelTexture", theme);

        Assert.Null(result);
        // Both fallbacks must be disposed — neither is cached, so neither will
        // be reached by EvictSkinDependentCache or CollectUnusedResources.
        themedFallback.Verify(x => x.Dispose(), Times.Once);
        defaultFallback.Verify(x => x.Dispose(), Times.Once);
        themedFallback.Verify(x => x.RemoveReference(), Times.Never);
        defaultFallback.Verify(x => x.RemoveReference(), Times.Never);
    }

    [Fact]
    public void FreezeRunConfiguration_WhenConfigChanges_ShouldKeepFrozenVisualGates()
    {
        var config = new ConfigData
        {
            AutoAddGauge = false,
            DamageLevel = GaugeDamageLevel.High,
            Risky = 3,
            NoFail = true,
            LaneDisplayMode = DrumsLaneDisplayMode.AllOff,
            NoteDisplayStyle = DrumsNoteDisplayStyle.Tab,
            ShowJudgementLine = false,
            EnableLaneFlush = true,
            ShowCombo = false,
            ShowHitTimingFeedback = true
        };
        config.AutoPlayLanes.UnionWith(new[] { 1, 8 });
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);

        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        // A later config edit must not change the policy captured for this run.
        config.AutoPlayLanes.Clear();
        config.AutoPlayLanes.Add(4);
        config.AutoAddGauge = true;
        config.DamageLevel = GaugeDamageLevel.Low;
        config.Risky = 0;
        config.NoFail = false;
        config.LaneDisplayMode = DrumsLaneDisplayMode.AllOn;
        config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
        config.ShowJudgementLine = true;
        config.EnableLaneFlush = false;
        config.ShowCombo = true;
        config.ShowHitTimingFeedback = false;

        var visualGates = ReflectionHelpers.GetPrivateField<object>(stage, "_visualGates");
        Assert.NotNull(visualGates);
        var gateType = visualGates!.GetType();
        Assert.True((bool)gateType.GetProperty("HideLaneBackground")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("HideMeasureLines")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("HideJudgementLine")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("HideCombo")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("EnableLaneFlush")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("ShowHitTimingFeedback")!.GetValue(visualGates)!);
        // The requested note display style is frozen too: live config edits must not
        // change the style this run was started with.
        Assert.Equal(
            DrumsNoteDisplayStyle.Tab,
            (DrumsNoteDisplayStyle)gateType.GetProperty("RequestedNoteDisplayStyle")!.GetValue(visualGates)!);
    }

    [Fact]
    public void VisualGates_BeforeFreeze_ShouldDefaultRequestedNoteDisplayStyleToLanes()
    {
        // default(PerformanceVisualGates) must mean Lanes so runs started without a
        // frozen configuration keep the classic lane presentation.
        var stage = CreateStage();

        var visualGates = ReflectionHelpers.GetPrivateField<object>(stage, "_visualGates");
        var value = visualGates!.GetType().GetProperty("RequestedNoteDisplayStyle")!.GetValue(visualGates);

        Assert.Equal(DrumsNoteDisplayStyle.Lanes, (DrumsNoteDisplayStyle)value!);
    }

    #region HPA-411 Tab mode integration

    [Fact]
    public void FreezeRunConfiguration_WhenTabRequested_ShouldFreezeStyleWhileEffectiveModeStaysLanesUntilStripPublish()
    {
        var stage = CreateStageWithConfig(new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab });

        var visualGates = ReflectionHelpers.GetPrivateField<object>(stage, "_visualGates");
        var requested = visualGates!.GetType().GetProperty("RequestedNoteDisplayStyle")!.GetValue(visualGates);

        // Requested Tab is frozen, but the effective mode is the strip reference alone:
        // until the one-shot construction publishes it, the run stays in Lanes.
        Assert.Equal(DrumsNoteDisplayStyle.Tab, (DrumsNoteDisplayStyle)requested!);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_notationStrip"));
    }

    [Fact]
    public void StartGameplayInitialization_WhenLanesRequested_ShouldKeepLanesWithoutNotation()
    {
        var stage = CreateStageWithConfig(new ConfigData());
        stage.InstallTestBackgroundScope();
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", CreateFinalizedTabChart());
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
        ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

        StartGameplayInitialization(stage);
        RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_notationStrip"));
    }

    [Fact]
    public void StartGameplayInitialization_WhenTabRequestedAndFontsReady_ShouldPublishNotationStripOnApply()
    {
        var stage = CreateStageWithConfig(new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab });
        stage.InstallTestBackgroundScope();
        var chart = CreateFinalizedTabChart();
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", CreateNotationResourceManager(completeBravura: true).Object);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
        ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

        StartGameplayInitialization(stage);
        RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        var strip = ReflectionHelpers.GetPrivateField<NotationStripRenderer>(stage, "_notationStrip");
        Assert.NotNull(strip);
        Assert.True(strip!.IsReady);
        // No GraphicsDevice in headless coverage: the HUD attempt reports NotAttempted
        // and the strip stays published.
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_tabHudDisplay"));
    }

    [Fact]
    public void StartGameplayInitialization_WhenTabRequestedAndAudioStillPreparing_ShouldPublishChartAndNotationStripBeforeReady()
    {
        // Tab playfield (chart, ChartManager and NotationStripRenderer) must publish
        // ahead of audio preparation so LOADING renders over Tab instead of Lanes.
        var directory = CreateTempDirectory();
        try
        {
            var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, PlaySpeedPercent = 50 };
            var resources = CreateNotationResourceManager(completeBravura: true);
            var logger = new StageTestLogger();
            using var input = new MockInputManagerCompat();
            var game = CreateHeadlessGame(logger, config, input, resources);
            using var stage = new GatedVariantStage(game);
            var chart = CreateFinalizedTabChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "tab-early-main.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
            Assert.True(
                SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)),
                "the worker never reached the variant processor");
            Assert.Null(stage.Rules?.ChartManager);
            Assert.Null(stage.Rules?.JudgementManager);

            // While audio preparation is still parked, draining publishes the chart
            // and Tab notation strip so the Tab playfield renders behind LOADING.
            var telemetry = new GameTelemetrySnapshot();
            UpdateUntil(stage, () =>
            {
                stage.PopulateTelemetry(telemetry);
                return telemetry.NoteDisplayStyle == nameof(DrumsNoteDisplayStyle.Tab);
            });
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.False(telemetry.PerformanceReady);
            Assert.NotNull(stage.Rules?.ChartManager);
            var judgement = Assert.IsType<JudgementManager>(stage.Rules?.JudgementManager);
            Assert.False(judgement.IsActive);
            Assert.NotNull(judgement.GetNoteRuntimeData(chart.Notes[0].Id));
            Assert.Null(stage.AudioSession);

            // Release the audio gate (it returns empty bytes -> CreatePreparedSound throws
            // NoAudioHardwareException or succeeds cleanly; either way the stage exits loading
            // and never hangs).
            stage.ReleaseProcessor.TrySetResult();
            UpdateUntil(stage, () => stage.AudioSession != null ||
                logger.Errors.Any(e => e.Message.Contains("Audio preparation failed")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenAudioPreparationThrowsOperationCanceledWithoutRetirement_ShouldFailLoading()
    {
        // A non-retirement OperationCanceledException (e.g. a killed decoder/ffmpeg
        // process) must route to onError so _isLoading clears and the error screen shows,
        // instead of hanging on LOADING forever.
        var directory = CreateTempDirectory();
        try
        {
            var stage = CreateCancelingAudioVariantStage();
            stage.InstallTestBackgroundScope();
            var chart = new ParsedChart("canceled-decoder.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "canceled-decoder-main.wav");

            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(50, 0));

            StartGameplayInitialization(stage);
            DrainScopeUntil(stage, () => !ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
            Assert.Contains(
                "Audio preparation failed",
                ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenNotationBuildFails_ShouldStayInLanesAndRemainReady()
    {
        var stage = CreateStageWithConfig(new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab });
        stage.InstallTestBackgroundScope();
        var chart = new ParsedChart("tab-invalid-channel.dtx");
        chart.MeasureLines.Add(new MeasureLine { Bar = 0, TimeMs = 0 });
        chart.MeasureLines.Add(new MeasureLine { Bar = 1, TimeMs = 2000 });
        chart.Notes.Add(new Note { Channel = 0x99, Bar = 0, Tick = 0 });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
        ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

        StartGameplayInitialization(stage);
        RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

        // The worker-local build failure is caught and logged: gameplay continues in
        // Lanes with a fully ready stage.
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.Null(ReflectionHelpers.GetPrivateField<string>(stage, "_loadErrorMessage"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_notationStrip"));
    }

    [Fact]
    public void TryConstructNotationStrip_WhenFontsAreReady_ShouldPublishStrip()
    {
        var stage = CreateTabStage(CreateNotationResourceManager(completeBravura: true).Object);

        ReflectionHelpers.InvokePrivateMethod(stage, "TryConstructNotationStrip", BuildNotationChart(), BuildMeasureLines());

        var strip = ReflectionHelpers.GetPrivateField<NotationStripRenderer>(stage, "_notationStrip");
        Assert.NotNull(strip);
        Assert.True(strip!.IsReady);
    }

    [Fact]
    public void TryConstructNotationStrip_WhenBravuraIsIncomplete_ShouldFallBackToLanesForTheSong()
    {
        var stage = CreateTabStage(CreateNotationResourceManager(completeBravura: false).Object);

        ReflectionHelpers.InvokePrivateMethod(stage, "TryConstructNotationStrip", BuildNotationChart(), BuildMeasureLines());

        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_notationStrip"));
    }

    [Fact]
    public void TryConstructNotationStrip_WhenTabHudCannotAcquireResources_ShouldRollStripBackToLanes()
    {
        var stage = CreateTabStage(CreateNotationResourceManager(completeBravura: true).Object);
        // A stub SpriteBatch returns a non-null but uninitialized GraphicsDevice,
        // so HUD construction is attempted and its font acquisition fails
        // deterministically — no GPU needed.
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteBatch",
            CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        ReflectionHelpers.InvokePrivateMethod(stage, "TryConstructNotationStrip", BuildNotationChart(), BuildMeasureLines());

        // The whole Tab decision rolls back to consistent Lanes — a strip kept
        // without its HUD would draw the Lanes widgets over the strip's hit
        // marker and key labels.
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_notationStrip"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_tabHudDisplay"));
    }

    [Fact]
    public void TryConstructNotationStrip_WhenNoGraphicsDevice_ShouldKeepPublishedStrip()
    {
        var stage = CreateTabStage(CreateNotationResourceManager(completeBravura: true).Object);

        // No SpriteBatch at all (plain headless path): the HUD cannot even be
        // attempted, which must not unpublish the strip.
        ReflectionHelpers.InvokePrivateMethod(stage, "TryConstructNotationStrip", BuildNotationChart(), BuildMeasureLines());

        Assert.NotNull(ReflectionHelpers.GetPrivateField<NotationStripRenderer>(stage, "_notationStrip"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_tabHudDisplay"));
    }

    [Fact]
    public void CleanupComponents_WhenTabWasActive_ShouldClearNotationAndDisposeStrip()
    {
        // The font's RemoveReference fires inside the renderer's Dispose: sampling the
        // stage fields there proves the effective-mode reference is cleared BEFORE the
        // renderer is disposed, not just by the time cleanup returns.
        PerformanceStage? stage = null;
        var clearedBeforeDispose = new List<bool>();
        var resourceManager = CreateNotationResourceManager(
            completeBravura: true,
            onFontRemoveReference: () => clearedBeforeDispose.Add(
                stage != null &&
                ReflectionHelpers.GetPrivateField<object>(stage, "_notationStrip") == null));
        stage = CreateTabStage(resourceManager.Object);
        ReflectionHelpers.InvokePrivateMethod(stage, "TryConstructNotationStrip", BuildNotationChart(), BuildMeasureLines());
        var strip = ReflectionHelpers.GetPrivateField<NotationStripRenderer>(stage, "_notationStrip");
        Assert.NotNull(strip);

        // Plant a real Tab HUD display so the cleanup assertion below can actually
        // fail: an unset field is null before AND after cleanup.
        ReflectionHelpers.SetPrivateField(
            stage,
            "_tabHudDisplay",
            new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a"));
        Assert.NotNull(ReflectionHelpers.GetPrivateField<object>(stage, "_tabHudDisplay"));

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

        Assert.NotEmpty(clearedBeforeDispose);
        Assert.All(clearedBeforeDispose, Assert.True);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_notationStrip"));
        // HPA-509: the Tab HUD display (fonts + white primitive) is disposed and
        // released by the same cleanup path used on stop, game-over and fallback.
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_tabHudDisplay"));
        Assert.False(strip!.IsReady);
    }

    private static PerformanceStage CreateStageWithConfig(ConfigData config)
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");
        return stage;
    }

    [Fact]
    public void CleanupComponents_ShouldDisposeChildrenBeforeReleasingCachedWhitePixel()
    {
        // HPA-796: the stage owns the cached white pixel; the Tab HUD and the judgement
        // line renderer only borrow its raw texture. RemoveReference must therefore fire
        // strictly after both children were disposed and cleared — disposal itself, not
        // just field nulling, is probed at release time.
        PerformanceStage? stage = null;
        var tabHud = new TrackingTabHudDisplay(CreateUninitialized<GraphicsDevice>());
        var borrowedPixelStub = CreateUninitialized<Texture2D>();
        GC.SuppressFinalize(borrowedPixelStub);
        var judgementLine = new TrackingJudgementLineRenderer(
            CreateUninitialized<GraphicsDevice>(),
            borrowedPixelStub);
        var childrenReleasedBeforePixel = new List<bool>();
        var whitePixel = new ProbingWhitePixelResource(() => childrenReleasedBeforePixel.Add(
            stage != null &&
            tabHud.WasDisposed &&
            judgementLine.WasDisposed &&
            ReflectionHelpers.GetPrivateField<object>(stage, "_tabHudDisplay") == null &&
            ReflectionHelpers.GetPrivateField<object>(stage, "_judgementLineRenderer") == null));

        stage = CreateTabStage(CreateNotationResourceManager(completeBravura: true).Object);
        ReflectionHelpers.InvokePrivateMethod(stage, "TryConstructNotationStrip", BuildNotationChart(), BuildMeasureLines());
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);
        ReflectionHelpers.SetPrivateField(stage, "_judgementLineRenderer", judgementLine);
        ReflectionHelpers.SetPrivateField(stage, "_fallbackWhiteTextureResource", whitePixel);
        ReflectionHelpers.SetPrivateField(stage, "_fallbackWhiteTexture", whitePixel.Texture);

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

        Assert.NotEmpty(childrenReleasedBeforePixel);
        Assert.All(childrenReleasedBeforePixel, Assert.True);
        Assert.Equal(1, whitePixel.RemoveReferenceCount);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_fallbackWhiteTextureResource"));
        Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_fallbackWhiteTexture"));
    }

    /// <summary>TabHudDisplay double that records its own disposal without a GPU.</summary>
    private sealed class TrackingTabHudDisplay : TabHudDisplay
    {
        public TrackingTabHudDisplay(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, null, "t", "a")
        {
            GC.SuppressFinalize(this);
        }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>JudgementLineRenderer double that records its own disposal without a GPU.</summary>
    private sealed class TrackingJudgementLineRenderer : JudgementLineRenderer
    {
        public TrackingJudgementLineRenderer(GraphicsDevice graphicsDevice, Texture2D? whiteTexture)
            : base(graphicsDevice, whiteTexture!)
        {
            GC.SuppressFinalize(this);
        }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ProbingWhitePixelResource : ITexture
    {
        private readonly Action _onRemoveReference;

        public ProbingWhitePixelResource(Action onRemoveReference)
        {
            _onRemoveReference = onRemoveReference;
        }

        public Texture2D Texture => null!;

        public string SourcePath => "__Color|white";

        public int Width => 1;

        public int Height => 1;

        public Vector2 Size => Vector2.One;

        public bool IsDisposed { get; private set; }

        public int ReferenceCount => 0;

        public long MemoryUsage => 0;

        public int Transparency { get; set; } = 255;

        public Vector3 ScaleRatio { get; set; } = Vector3.One;

        public float ZAxisRotation { get; set; }

        public bool AdditiveBlending { get; set; }

        public int RemoveReferenceCount { get; private set; }

        public void AddReference()
        {
        }

        public void RemoveReference()
        {
            RemoveReferenceCount++;
            _onRemoveReference();
        }

        public ITexture Clone() => this;

        public Color[] GetColorData() => Array.Empty<Color>();

        public void SetColorData(Color[] colorData)
        {
        }

        public void SaveToFile(string filePath)
        {
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position)
        {
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle)
        {
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
        {
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position, Vector2 scale, float rotation, Vector2 origin)
        {
        }

        public void Dispose() => IsDisposed = true;
    }

    private static PerformanceStage CreateTabStage(IResourceManager resourceManager)
    {
        var stage = CreateStageWithConfig(new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab });
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", CreateFinalizedTabChart());
        return stage;
    }

    private static NotationChart BuildNotationChart() => new(Array.Empty<NotationMeasure>());

    private static IReadOnlyList<MeasureLine> BuildMeasureLines() =>
        new List<MeasureLine> { new MeasureLine { Bar = 0, TimeMs = 0 } };

    private static ParsedChart CreateFinalizedTabChart()
    {
        var chart = new ParsedChart("tab-mode.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();
        return chart;
    }

    private static Mock<IResourceManager> CreateNotationResourceManager(
        bool completeBravura,
        Action? onFontRemoveReference = null)
    {
        var font = new Mock<IFont>();
        font.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(completeBravura);
        font.Setup(f => f.LineSpacing).Returns(74f);
        if (onFontRemoveReference != null)
            font.Setup(f => f.RemoveReference()).Callback(onFontRemoveReference);
        var resourceManager = new Mock<IResourceManager>();
        resourceManager
            .Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(font.Object);
        return resourceManager;
    }

    #endregion

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "DTXMania_PerformanceStageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreatePlaceholderSource(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1 });
        return Path.GetFullPath(path);
    }

    private static GatedVariantStage CreateGatedVariantStage(BaseGame? game = null)
    {
#pragma warning disable SYSLIB0050
        var stage = (GatedVariantStage)FormatterServices.GetUninitializedObject(typeof(GatedVariantStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
        ReflectionHelpers.SetPrivateField(stage, "_logger", NullLogger<PerformanceStage>.Instance);
        // All gates must exist before any work starts: FormatterServices skips
        // field initializers, and lazily initializing the gates from both the test
        // thread and the worker could hand out two different instances.
        stage.InitializeGates();
        return stage;
    }

    /// <summary>
    /// Stage whose variant processor parks on a gate so tests can deterministically
    /// hold the worker mid-preparation (the progress coalescing case). Gates are
    /// created up front by <see cref="InitializeGates"/>, before any worker runs.
    /// The mid-flight cancellation case deliberately does NOT use this gate: the
    /// variant cache awaits the underlying operation through a WaitAsync'd shared
    /// task, so retirement detaches the waiter and the parked processor's unwind
    /// is invisible to the worker task — that test parks in the default profile
    /// instead, whose chain has no detached operation.
    /// </summary>
    private sealed class GatedVariantStage : PerformanceStage
    {
        private TaskCompletionSource? _processorEntered;
        private TaskCompletionSource? _releaseProcessor;

        public GatedVariantStage(IStageGame game)
            : base(game)
        {
            InitializeGates();
        }

        /// <summary>
        /// Creates both gates before any work starts. FormatterServices skips
        /// field initializers, so uninitialized instances would otherwise lazily
        /// ??= the gates from whichever thread touches them first — and the test
        /// thread and the worker could each get a different instance.
        /// </summary>
        public void InitializeGates()
        {
            _processorEntered ??= NewGate();
            _releaseProcessor ??= NewGate();
        }

        public TaskCompletionSource ProcessorEntered => _processorEntered!;

        public TaskCompletionSource ReleaseProcessor => _releaseProcessor!;

        private static TaskCompletionSource NewGate() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The gates are snapshot inputs captured when the stage resolves the
        // factory (update thread); the worker-side processor never touches
        // live stage state.
        protected override Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
        {
            var entered = ProcessorEntered;
            var release = ReleaseProcessor;
            return () => new GatedProcessor(entered, release);
        }

        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory()
        {
            var cacheRoot = Path.Combine(
                Path.GetTempPath(),
                "DTXMania_PerformanceStage_TestCache_" + Guid.NewGuid().ToString("N"));
            return () => new PlaybackAudioVariantCache(cacheRoot);
        }

        private sealed class GatedProcessor : IAudioVariantProcessor
        {
            private readonly TaskCompletionSource _entered;
            private readonly TaskCompletionSource _release;

            public GatedProcessor(TaskCompletionSource entered, TaskCompletionSource release)
            {
                _entered = entered;
                _release = release;
            }

            public async Task<PreparedAudioArtifact> PrepareAsync(
                string sourcePath,
                PlaybackModifiers modifiers,
                CancellationToken cancellationToken)
            {
                _entered.TrySetResult();
                await _release.Task.ConfigureAwait(false);
                // The cancellation check runs after the release so the worker stays
                // provably inside the preparation chain while the test holds the
                // gate (progress ordering below). This gate must not be used to
                // stage a canceled operation: retirement detaches the cache's
                // WaitAsync waiter from the still-parked processor, so the worker
                // task would complete without witnessing this continuation (the
                // cancellation test parks in the default profile instead).
                cancellationToken.ThrowIfCancellationRequested();
                return new PreparedAudioArtifact(44100, 1, new byte[8]);
            }
        }
    }

    /// <summary>
    /// Logger that parks the calling (worker) thread on each error-level entry for
    /// which a release gate is pending, so a test can hold a preparation mid-flight
    /// at its load-failure report — the callback runs after the loop iteration's
    /// cancellation check, so no check remains between the park and the return.
    /// Reports arriving after all gates are consumed are only recorded.
    /// </summary>
    private sealed class BlockingLoadFailureLogger : ILogger<PerformanceStage>
    {
        private readonly ConcurrentQueue<(string Message, Exception? Exception)> _errors = new();
        private readonly ConcurrentQueue<TaskCompletionSource> _freeGates = new();
        private readonly ConcurrentQueue<TaskCompletionSource> _parkedGates = new();
        private int _parkedCount;

        public BlockingLoadFailureLogger(int gates)
        {
            for (var i = 0; i < gates; i++)
            {
                _freeGates.Enqueue(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }
        }

        public IReadOnlyCollection<(string Message, Exception? Exception)> Errors => _errors.ToArray();

        public int ParkedCount => Volatile.Read(ref _parkedCount);

        public void ReleaseNext()
        {
            // Parks hand their gate to _parkedGates in arrival order, so this
            // releases the oldest park first.
            Assert.True(
                _parkedGates.TryDequeue(out var gate),
                "no parked worker to release");
            gate!.TrySetResult();
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
            {
                return;
            }

            _errors.Enqueue((formatter(state, exception), exception));
            if (_freeGates.TryDequeue(out var gate))
            {
                _parkedGates.Enqueue(gate);
                Interlocked.Increment(ref _parkedCount);
                // Park the worker synchronously: Log must not return before the
                // test releases the gate, or the preparation would run past its
                // park point and the ordering would be lost.
                if (!gate.Task.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("the park gate was never released");
                }
            }
        }
    }

    /// <summary>
    /// Records which thread created each logger and every warning/error logged
    /// through it, so tests can prove log resolutions and reports happen on the
    /// expected thread.
    /// </summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<int> _creationThreadIds = new();
        private readonly ConcurrentQueue<(string Message, Exception? Exception)> _errors = new();

        public IReadOnlyCollection<int> CreationThreadIds => _creationThreadIds.ToArray();

        public IReadOnlyCollection<(string Message, Exception? Exception)> Errors => _errors.ToArray();

        public ILogger CreateLogger(string categoryName)
        {
            _creationThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            return new CapturingLogger(_errors);
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<(string Message, Exception? Exception)> _errors;

        public CapturingLogger(ConcurrentQueue<(string Message, Exception? Exception)> errors) => _errors = errors;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            _errors.Enqueue((formatter(state, exception), exception));
        }
    }

    [Fact]
    public void StartGameplayInitialization_WithVisualGateSettingsInConfig_ShouldFreezeVisualGates()
    {
        // Direct initialization must capture the configured visual gates the same way
        // OnActivate does; without the freeze before the scope work the gates stay
        // at their all-false defaults and hidden lanes/lines/combo render anyway.
        var config = new ConfigData
        {
            LaneDisplayMode = DrumsLaneDisplayMode.AllOff,
            ShowJudgementLine = false,
            ShowCombo = false,
            EnableLaneFlush = true
        };
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        stage.InstallTestBackgroundScope();
        var chart = new ParsedChart("silent-clock.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();

        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", new MockInputManagerCompat());
        ReflectionHelpers.SetPrivateField(stage, "_playbackModifiers", new PlaybackModifiers(100, 0));

        StartGameplayInitialization(stage);
        RunUpdateLoopUntil(stage, () => ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

        var visualGates = ReflectionHelpers.GetPrivateField<object>(stage, "_visualGates");
        Assert.NotNull(visualGates);
        var gateType = visualGates!.GetType();
        Assert.True((bool)gateType.GetProperty("HideLaneBackground")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("HideMeasureLines")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("HideJudgementLine")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("HideCombo")!.GetValue(visualGates)!);
        Assert.True((bool)gateType.GetProperty("EnableLaneFlush")!.GetValue(visualGates)!);
    }

    [Fact]
    public void OnScoreChanged_WhenDisplayExists_ShouldPropagateCurrentScore()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var scoreDisplay = CreateScoreDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_scoreDisplay", scoreDisplay);

        stage.Rules!.ScoreManager!.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Perfect));
        Assert.Equal(1000000, scoreDisplay.Score);
        ReflectionHelpers.InvokePrivateMethod(stage, "OnScoreChanged", null, new ScoreChangedEventArgs { CurrentScore = 123456 });

        Assert.Equal(123456, scoreDisplay.Score);
    }

    [Fact]
    public void OnComboChanged_WhenDisplayExists_ShouldPropagateCurrentCombo()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var comboDisplay = CreateComboDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_comboDisplay", comboDisplay);

        stage.Rules!.ComboManager!.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Perfect));
        Assert.Equal(1, comboDisplay.Combo);
        ReflectionHelpers.InvokePrivateMethod(stage, "OnComboChanged", null, new ComboChangedEventArgs { CurrentCombo = 87 });

        Assert.Equal(87, comboDisplay.Combo);
    }

    [Theory]
    [InlineData(75f, 0.75f, false)]
    [InlineData(15f, 0.15f, true)]
    public void OnGaugeChanged_ShouldNormalizeGaugeAndUpdateDangerState(float life, float expectedGauge, bool expectedDanger)
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);

        stage.Rules!.GaugeManager!.Reset(life);
        Assert.Equal(expectedGauge, ReflectionHelpers.GetPrivateField<float>(stage, "_currentGaugeValue"));
        Assert.Equal(expectedDanger, ReflectionHelpers.GetPrivateField<bool>(stage, "_isDanger"));

        ReflectionHelpers.InvokePrivateMethod(stage, "OnGaugeChanged", null, new GaugeChangedEventArgs { CurrentLife = life });

        Assert.Equal(expectedGauge, ReflectionHelpers.GetPrivateField<float>(stage, "_currentGaugeValue"));
        Assert.Equal(expectedDanger, ReflectionHelpers.GetPrivateField<bool>(stage, "_isDanger"));
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WhenPadRendererExists_ShouldMarkLanePressed()
    {
        var stage = CreateStage();
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, new LaneHitEventArgs(4, new ButtonState("Key.Z", true)));

        var padVisuals = ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals");
        Assert.NotNull(padVisuals);
        Assert.Equal(PadState.Pressed, padVisuals![4].State);
        Assert.Equal(0.0, padVisuals[4].TimePressed);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WithMixedAutoPlay_ShouldSuppressOnlyAutomatedLane()
    {
        using var stage = CreateStageWithAutoPlay(1);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnLaneHitForPadFeedback",
            null,
            new LaneHitEventArgs(0, new ButtonState("Manual", true)));
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnLaneHitForPadFeedback",
            null,
            new LaneHitEventArgs(1, new ButtonState("Automated", true)));

        var padVisuals = ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals");
        Assert.Equal(PadState.Pressed, padVisuals![0].State);
        Assert.Equal(PadState.Idle, padVisuals[1].State);
    }

    [Theory]
    [InlineData(-100.0, 0.0f)]
    [InlineData(500.0, 0.25f)]
    [InlineData(2500.0, 1.0f)]
    public void UpdateSongProgress_WhenChartDurationExists_ShouldClampProgress(double currentSongTimeMs, float expectedProgress)
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("duration-test.dtx") { DurationMs = 2000.0 };
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSongProgress", currentSongTimeMs);

        Assert.Equal(expectedProgress, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"));
    }

    [Fact]
    public void UpdateSongProgress_WhenChartMissingOrDurationNonPositive_ShouldLeaveProgressUnchanged()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_currentProgressValue", 0.42f);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("zero-duration.dtx") { DurationMs = 0.0 });

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSongProgress", 500.0);

        Assert.Equal(0.42f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"));

        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", null);
        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSongProgress", 800.0);

        Assert.Equal(0.42f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"));
    }

    [Fact]
    public void InitializeGameplayManagers_WhenDependenciesExist_ShouldCreateManagersAndInitializeState()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var inputManager = new MockInputManagerCompat();
        BindChart(stage, CreateSingleNoteChart());
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayManagers");

        var judgementManager = stage.Rules!.JudgementManager;
        Assert.NotNull(judgementManager);
        Assert.False(judgementManager!.IsActive);
        Assert.IsType<ScoreManager>(stage.Rules.ScoreManager);
        Assert.IsType<ComboManager>(stage.Rules.ComboManager);
        Assert.IsType<GaugeManager>(stage.Rules.GaugeManager);
        Assert.IsType<SkillManager>(stage.Rules.SkillManager);
        Assert.Equal(0.5f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentGaugeValue"));
        Assert.Equal(0.0f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isPaused"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isDanger"));
    }

    [Fact]
    public void InitializeGameplayManagers_WithFrozenAutoPlayLanes_ShouldFilterOnlyAutomatedPhysicalInput()
    {
        using var stage = CreateStageWithAutoPlay(1);
        var inputManager = new MockInputManagerCompat();
        var chartManager = BindChart(stage, BuildChart(new[]
        {
            new Note(laneIndex: 0, bar: 0, tick: 96, channel: 0x11, value: "01"),
            new Note(laneIndex: 1, bar: 0, tick: 96, channel: 0x12, value: "01")
        }));
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayManagers");

        var judgementManager = stage.Rules!.JudgementManager;
        Assert.NotNull(judgementManager);
        judgementManager.IsActive = true;
        JudgementEvent? captured = null;
        judgementManager.JudgementMade += (_, judgement) => captured = judgement;

        inputManager.TriggerLaneHit(1);
        judgementManager.Update(1000.0);
        Assert.Null(captured);
        Assert.Equal(NoteStatus.Pending, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[1].Id)!.Status);

        inputManager.TriggerLaneHit(0);
        judgementManager.Update(1000.0);

        Assert.NotNull(captured);
        Assert.Equal(0, captured!.Lane);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void CleanupGameplayManagers_WhenManagersInitialized_ShouldClearAllManagerReferences()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var inputManager = new MockInputManagerCompat();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
        BindChart(stage, CreateSingleNoteChart());
        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayManagers");

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupGameplayManagers");

        Assert.Null(stage.Rules!.JudgementManager);
        Assert.Null(stage.Rules.ScoreManager);
        Assert.Null(stage.Rules.ComboManager);
        Assert.Null(stage.Rules.GaugeManager);
        Assert.Null(stage.Rules.SkillManager);
    }

    [Fact]
    public void ChartPublication_InitializesGameplayAndIgnoresCountdownInput()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var chart = new ParsedChart("countdown-input.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        input.ModularInputManager.KeyBindings.BindButton("Test.Countdown", 0);

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(1, telemetry.TotalNotes);
        Assert.Equal(0, telemetry.Score);
        Assert.Equal(0, telemetry.CurrentCombo);
        Assert.Equal(GaugeManager.StartingLife, telemetry.Gauge);

        Assert.True(input.ModularInputManager.InjectButton("Test.Countdown", true));
        input.ModularInputManager.Update();
        Assert.True(input.ModularInputManager.InjectButton("Test.Countdown", false));
        input.ModularInputManager.Update();
        stage.Update(1.1);
        stage.Update(0.1);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(0, telemetry.PerfectCount);
        Assert.Equal(0, telemetry.Score);

        Assert.True(input.ModularInputManager.InjectButton("Test.Countdown", true));
        input.ModularInputManager.Update();
        stage.Update(0);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(1, telemetry.PerfectCount);
        Assert.Equal(1000000, telemetry.Score);
        Assert.Equal(1, telemetry.CurrentCombo);
        Assert.Equal(52f, telemetry.Gauge);
        stage.Deactivate();
    }

    [Fact]
    public void ChartPublication_FiltersPhysicalInputOnlyForFrozenAutomatedLanes()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = new ParsedChart("mixed-input.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 950 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 1000 });
        input.ModularInputManager.KeyBindings.BindButton("Test.Manual", 0);
        input.ModularInputManager.KeyBindings.BindButton("Test.Auto", 1);

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        config.AutoPlayLanes.Clear();
        UpdateUntil(stage, () => stage.AudioSession != null);
        var score = Assert.IsType<ScoreManager>(stage.Rules!.ScoreManager);
        var combo = Assert.IsType<ComboManager>(stage.Rules.ComboManager);
        var gauge = Assert.IsType<GaugeManager>(stage.Rules.GaugeManager);
        var skill = Assert.IsType<SkillManager>(stage.Rules.SkillManager);
        var order = new List<string>();
        var managerCallbacks = new List<(string Manager, int Score, int Combo, float Gauge)>();
        score.ScoreChanged += (_, _) =>
        {
            order.Add("score");
            var snapshot = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(snapshot);
            managerCallbacks.Add(("score", score.CurrentScore, combo.CurrentCombo, snapshot.Gauge ?? 0));
        };
        combo.ComboChanged += (_, _) =>
        {
            order.Add("combo");
            var snapshot = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(snapshot);
            managerCallbacks.Add(("combo", score.CurrentScore, combo.CurrentCombo, snapshot.Gauge ?? 0));
        };
        gauge.GaugeChanged += (_, _) => order.Add("gauge");
        skill.SkillChanged += (_, _) => order.Add("skill");
        var callbacks = new List<(int Lane, int? Score, int? Combo)>();
        stage.Rules!.JudgementManager!.JudgementMade += (_, judgement) =>
        {
            var snapshot = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(snapshot);
            callbacks.Add((judgement.Lane, snapshot.Score, snapshot.CurrentCombo));
        };
        stage.Update(1.1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Auto", true));
        Assert.True(input.ModularInputManager.InjectButton("Test.Manual", true));
        input.ModularInputManager.Update();
        stage.Update(0.95);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(1, telemetry.PerfectCount);
        Assert.Equal(0, telemetry.GreatCount);
        Assert.Equal(500000, telemetry.Score);
        Assert.Equal(1, telemetry.CurrentCombo);

        stage.Update(0.06);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(2, telemetry.PerfectCount);
        Assert.Equal(1000000, telemetry.Score);
        Assert.Equal(2, telemetry.CurrentCombo);
        Assert.Equal(
            new (int Lane, int? Score, int? Combo)[] { (0, 500000, 1), (1, 1000000, 2) },
            callbacks);
        Assert.Equal(
            new (string Manager, int Score, int Combo, float Gauge)[]
            {
                ("score", 500000, 0, 50f), ("combo", 500000, 1, 50f),
                ("score", 1000000, 1, 52f), ("combo", 1000000, 2, 52f)
            },
            managerCallbacks);
        Assert.Equal(new[] { "score", "combo", "gauge", "skill", "score", "combo", "gauge", "skill" }, order);
        stage.Deactivate();
    }

    [Fact]
    public void Deactivation_ClearsGameplayStateAndReactivationStartsFresh()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var firstChart = new ParsedChart("first-lifetime.dtx") { DurationMs = 10000 };
        firstChart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        var secondChart = new ParsedChart("second-lifetime.dtx") { DurationMs = 10000 };
        secondChart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        secondChart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 1000 });
        input.ModularInputManager.KeyBindings.BindButton("Test.Lifetime", 0);

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "First" }, 0, firstChart));
        var firstRules = Assert.IsType<GameplayRules>(stage.Rules);
        Assert.Null(firstRules.ChartManager);
        Assert.Null(firstRules.JudgementManager);
        Assert.Null(firstRules.ScoreManager);
        Assert.Null(firstRules.ComboManager);
        Assert.Null(firstRules.GaugeManager);
        Assert.Null(firstRules.SkillManager);
        UpdateUntil(stage, () => stage.AudioSession != null);
        var firstManager = Assert.IsType<ChartManager>(firstRules.ChartManager);
        var firstJudgement = Assert.IsType<JudgementManager>(firstRules.JudgementManager);
        var firstScore = Assert.IsType<ScoreManager>(firstRules.ScoreManager);
        var firstCombo = Assert.IsType<ComboManager>(firstRules.ComboManager);
        var firstGauge = Assert.IsType<GaugeManager>(firstRules.GaugeManager);
        var firstSkill = Assert.IsType<SkillManager>(firstRules.SkillManager);
        Assert.False(firstJudgement.IsActive);
        Assert.Equal(1, firstManager.TotalNotes);
        stage.Update(1.1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Lifetime", true));
        input.ModularInputManager.Update();
        stage.Update(0.1);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(1000000, telemetry.Score);
        Assert.Equal(1, telemetry.CurrentCombo);
        Assert.Equal(52f, telemetry.Gauge);
        stage.Deactivate();
        Assert.Null(firstRules.ChartManager);
        Assert.Null(firstRules.JudgementManager);
        Assert.Null(firstRules.ScoreManager);
        Assert.Null(firstRules.ComboManager);
        Assert.Null(firstRules.GaugeManager);
        Assert.Null(firstRules.SkillManager);
        Assert.False(firstJudgement.IsActive);
        Assert.Null(firstJudgement.GetNoteRuntimeData(firstChart.Notes[0].Id));
        firstScore.Reset();
        firstCombo.Reset();
        firstGauge.Reset();
        firstSkill.Reset();
        Assert.Equal(1000000, firstScore.CurrentScore);
        Assert.Equal(1, firstCombo.CurrentCombo);
        Assert.Equal(52f, firstGauge.CurrentLife);
        Assert.Equal(1, firstSkill.PerfectCount);

        stage.PopulateTelemetry(telemetry);
        Assert.False(telemetry.PerformanceReady);
        Assert.Equal(0, telemetry.TotalNotes);
        Assert.Equal(0, telemetry.Score);
        Assert.Equal(0, telemetry.CurrentCombo);
        Assert.Equal(0, telemetry.PerfectCount);
        Assert.Equal(0f, telemetry.Gauge);

        Assert.True(input.ModularInputManager.InjectButton("Test.Lifetime", false));
        input.ModularInputManager.Update();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Second" }, 0, secondChart));
        var secondRules = Assert.IsType<GameplayRules>(stage.Rules);
        Assert.NotSame(firstRules, secondRules);
        Assert.Null(secondRules.ChartManager);
        Assert.Null(secondRules.JudgementManager);
        Assert.Null(secondRules.ScoreManager);
        Assert.Null(secondRules.ComboManager);
        Assert.Null(secondRules.GaugeManager);
        Assert.Null(secondRules.SkillManager);
        UpdateUntil(stage, () => stage.AudioSession != null);
        Assert.NotSame(firstManager, secondRules.ChartManager);
        Assert.NotSame(firstJudgement, secondRules.JudgementManager);
        Assert.NotSame(firstScore, Assert.IsType<ScoreManager>(secondRules.ScoreManager));
        Assert.NotSame(firstCombo, Assert.IsType<ComboManager>(secondRules.ComboManager));
        Assert.NotSame(firstGauge, Assert.IsType<GaugeManager>(secondRules.GaugeManager));
        Assert.NotSame(firstSkill, Assert.IsType<SkillManager>(secondRules.SkillManager));
        Assert.Equal(2, secondRules.SkillManager!.TotalNotes);
        Assert.False(Assert.IsType<JudgementManager>(secondRules.JudgementManager).IsActive);
        Assert.Equal(2, secondRules.ChartManager!.TotalNotes);
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(2, telemetry.TotalNotes);
        Assert.Equal(0, telemetry.Score);
        Assert.Equal(0, telemetry.PerfectCount);
        Assert.Equal(GaugeManager.StartingLife, telemetry.Gauge);
        stage.Update(1.1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Lifetime", true));
        input.ModularInputManager.Update();
        stage.Update(0.1);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(500000, telemetry.Score);
        Assert.Equal(1, telemetry.PerfectCount);
        Assert.Equal(1, telemetry.CurrentCombo);
        stage.Deactivate();
    }

    [Fact]
    public void ProcessBGMEvents_WhenCurrentTimeIsBeforeEventTime_ShouldNotTriggerEarly()
    {
        var session = CreateBgmSession(
            ("01", 1000.0, null),
            ("02", 1300.0, null));

        session.Advance(980.0);

        var remainingEvents = session.ScheduledBgmEvents.ToList();
        Assert.Collection(remainingEvents,
            first => Assert.Equal("01", first.WavId),
            second => Assert.Equal("02", second.WavId));
    }

    [Fact]
    public void ProcessBGMEvents_WhenCurrentTimeReachesEventTime_ShouldTriggerAndRemoveElapsedEvents()
    {
        var session = CreateBgmSession(
            ("01", 1000.0, null),
            ("02", 1300.0, null));

        session.Advance(1000.0);

        var remainingEvents = session.ScheduledBgmEvents.ToList();
        Assert.Single(remainingEvents);
        Assert.Equal("02", remainingEvents[0].WavId);
    }

    [Fact]
    public void ProcessBGMEvents_WhenSeveralEventsAreDue_ShouldTriggerChronologically()
    {
        var played = new List<string>();
        var sounds = new Dictionary<string, ISound>();
        foreach (var wavId in new[] { "01", "02", "03" })
        {
            var capturedId = wavId;
            var sound = new Mock<ISound>();
            sound.Setup(s => s.Play(
                    It.IsAny<float>(),
                    It.IsAny<float>(),
                    It.IsAny<float>()))
                .Callback(() => played.Add(capturedId))
                .Returns((SoundEffectInstance)null!);
            sounds[wavId] = sound.Object;
        }

        var session = CreateBgmSession(
            ("03", 900.0, sounds["03"]),
            ("01", 100.0, sounds["01"]),
            ("02", 500.0, sounds["02"]),
            ("future", 1200.0, null));

        session.Advance(1000.0);

        Assert.Equal(new[] { "01", "02", "03" }, played);
        var remaining = session.ScheduledBgmEvents.ToList();
        Assert.Single(remaining);
        Assert.Equal("future", remaining[0].WavId);
    }

    [Theory]
    [InlineData(50, 875.0)]
    [InlineData(100, 750.0)]
    [InlineData(150, 625.0)]
    public void GetPlayerJudgementTimeMs_WhenAudioLatencyOffsetConfigured_ShouldScaleRealOffset(
        int playSpeedPercent,
        double expectedLogicalTimeMs)
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager),
            CreateConfigManager(new ConfigData { AudioLatencyOffsetMs = 250 }));
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(playSpeedPercent, 0));

        Assert.Equal(
            expectedLogicalTimeMs,
            ReflectionHelpers.InvokePrivateMethod<double>(
                stage,
                "GetPlayerJudgementTimeMs",
                1000.0));
        Assert.Equal(0.0, ReflectionHelpers.InvokePrivateMethod<double>(stage, "GetPlayerJudgementTimeMs", 100.0));
    }

    [Fact]
    public void GetPlayerJudgementTimeMs_WhenDefaultOffsetIsZero_ShouldReturnRawTime()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(
            game,
            nameof(BaseGame.ConfigManager),
            CreateConfigManager(new ConfigData()));
        var stage = CreateStage(game);

        Assert.Equal(
            1000.0,
            ReflectionHelpers.InvokePrivateMethod<double>(
                stage,
                "GetPlayerJudgementTimeMs",
                1000.0));
    }

    [Fact]
    public void UpdateGameplayManagers_WhenAutoPlayDisabled_ShouldStillAdvanceMissDetection()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1301.0, 1301.0);

        Assert.Equal(1, judgementManager.GetJudgementCount(JudgementType.Miss));
    }

    [Fact]
    public void UpdateGameplayManagers_MissScanningUsesCompensatedClock_NotRawLogicalTime()
    {
        // Note at 1000ms, hit window ±200ms. Raw logical time 1301ms is past
        // the raw miss deadline (1200ms), but the compensated clock (1000ms)
        // is still within the hit window. Miss scanning must use the
        // compensated clock so the player keeps the full reaction window
        // after the note becomes audible under output latency.
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "UpdateGameplayManagers",
            1301.0,
            1000.0);

        Assert.Equal(
            0,
            judgementManager.GetJudgementCount(JudgementType.Miss));
    }

    [Fact]
    public void UpdateGameplayManagers_WhenAutoPlayEnabled_ShouldTriggerAutoHitBeforeMissDetection()
    {
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 1000.0);
        judgementManager.Update(1000.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
    }

    [Fact]
    public void UpdateGameplayManagers_WhenJudgementClockIsLatencyAdjusted_ShouldAutoPlayUseRawSongClock()
    {
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 750.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
    }

    [Fact]
    public void InitializeMetronome_WhenDisabled_ShouldNotLoadClickSoundsOrCreatePlayer()
    {
        var resourceManager = new Mock<IResourceManager>();
        var session = CreateSession(resourceManager: resourceManager.Object, metronomeEnabled: false);

        session.Bind(CreateMetronomeChart(), CreateEmptyPreparedSet());

        resourceManager.Verify(x => x.TryLoadSound(It.IsAny<string>(), out It.Ref<ISound>.IsAny), Times.Never);
        Assert.Null(session.Metronome);
    }

    [Fact]
    public void InitializeMetronome_WhenEnabled_ShouldLoadBothSoundsAndUseFinalizedMarkers()
    {
        var beatSound = new Mock<ISound>();
        var accentSound = new Mock<ISound>();
        var resourceManager = new Mock<IResourceManager>();
        var beatResult = beatSound.Object;
        var accentResult = accentSound.Object;
        resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeBeat, out beatResult)).Returns(true);
        resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeAccent, out accentResult)).Returns(true);
        var clicks = new List<BeatMarker>();
        var session = CreateSession(
            resourceManager: resourceManager.Object,
            metronomeEnabled: true,
            playClick: clicks.Add);

        session.Bind(CreateMetronomeChart(
            new BeatMarker { TimeMs = 0.0, IsMeasureStart = true },
            new BeatMarker { TimeMs = 100.0, IsMeasureStart = false }), CreateEmptyPreparedSet());
        session.UpdateMetronome(0.0);

        resourceManager.Verify(x => x.TryLoadSound(SoundPath.MetronomeBeat, out It.Ref<ISound>.IsAny), Times.Once);
        resourceManager.Verify(x => x.TryLoadSound(SoundPath.MetronomeAccent, out It.Ref<ISound>.IsAny), Times.Once);
        Assert.NotNull(session.Metronome);
        var click = Assert.Single(clicks);
        Assert.Equal(0.0, click.TimeMs);
        Assert.True(click.IsMeasureStart);
    }

    [Fact]
    public void UpdateGameplayManagers_ShouldAdvanceMetronomeWithRawLogicalTime()
    {
        var stage = CreateInitializedMetronomeStage(100);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 100.0, 0.0);

        var click = Assert.Single(stage.MetronomeClicks);
        Assert.Equal(100.0, click.TimeMs);
    }

    [Theory]
    [InlineData(50, 150.0, true)]
    [InlineData(50, 151.0, false)]
    [InlineData(100, 200.0, true)]
    [InlineData(100, 201.0, false)]
    [InlineData(200, 300.0, true)]
    [InlineData(200, 301.0, false)]
    public void InitializeMetronome_ShouldScaleStaleToleranceByFrozenPlaySpeed(
        int playSpeedPercent,
        double rawLogicalTimeMs,
        bool expectedClick)
    {
        var stage = CreateInitializedMetronomeStage(
            playSpeedPercent,
            new BeatMarker { TimeMs = 100.0, IsMeasureStart = false });

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "UpdateGameplayManagers",
            rawLogicalTimeMs,
            0.0);

        Assert.Equal(expectedClick, stage.MetronomeClicks.Count == 1);
    }

    [Fact]
    public void CleanupComponents_ShouldClearMetronomeAndReleaseClickSoundReferences()
    {
        var stage = CreateStage();
        var beatSound = new Mock<ISound>();
        var accentSound = new Mock<ISound>();
        var resourceManager = new Mock<IResourceManager>();
        var beatResult = beatSound.Object;
        var accentResult = accentSound.Object;
        resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeBeat, out beatResult)).Returns(true);
        resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeAccent, out accentResult)).Returns(true);
        var session = InstallBoundSession(
            stage,
            chart: CreateMetronomeChart(new BeatMarker { TimeMs = 0.0 }),
            metronomeEnabled: true,
            resourceManager: resourceManager.Object);
        Assert.NotNull(session.Metronome);

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

        Assert.Null(stage.AudioSession);
        beatSound.Verify(x => x.RemoveReference(), Times.Once);
        accentSound.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void InitializeMetronome_AfterCleanup_ShouldCreateFreshPlayerStartingAtMarkerZero()
    {
        var sound = new Mock<ISound>();
        var resourceManager = new Mock<IResourceManager>();
        var soundResult = sound.Object;
        resourceManager.Setup(x => x.TryLoadSound(It.IsAny<string>(), out soundResult)).Returns(true);
        var clicks = new List<BeatMarker>();

        var firstSession = CreateSession(
            resourceManager: resourceManager.Object,
            metronomeEnabled: true,
            playClick: clicks.Add);
        firstSession.Bind(CreateMetronomeChart(
            new BeatMarker { TimeMs = 0.0, IsMeasureStart = true }), CreateEmptyPreparedSet());
        var firstPlayer = firstSession.Metronome;
        firstSession.Dispose();

        // The new activation binds a fresh session whose player restarts at
        // marker zero instead of inheriting the previous run's position.
        var secondSession = CreateSession(
            resourceManager: resourceManager.Object,
            metronomeEnabled: true,
            playClick: clicks.Add);
        secondSession.Bind(CreateMetronomeChart(
            new BeatMarker { TimeMs = 0.0, IsMeasureStart = true }), CreateEmptyPreparedSet());
        var secondPlayer = secondSession.Metronome;
        secondSession.UpdateMetronome(0.0);

        Assert.NotNull(firstPlayer);
        Assert.NotNull(secondPlayer);
        Assert.NotSame(firstPlayer, secondPlayer);
        var click = Assert.Single(clicks);
        Assert.Equal(0.0, click.TimeMs);
    }

    [Fact]
    public void InitializeMetronome_WhenClickResourcesAreMissing_ShouldLeaveGameplayUsable()
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.TryLoadSound(It.IsAny<string>(), out It.Ref<ISound>.IsAny))
            .Throws(new FileNotFoundException("click asset missing"));
        var clicks = new List<BeatMarker>();
        var session = CreateSession(
            resourceManager: resourceManager.Object,
            metronomeEnabled: true,
            playClick: clicks.Add);

        var exception = Record.Exception(() =>
        {
            session.Bind(CreateMetronomeChart(
                new BeatMarker { TimeMs = 0.0, IsMeasureStart = true }), CreateEmptyPreparedSet());
            session.UpdateMetronome(0.0);
        });

        Assert.Null(exception);
        Assert.NotNull(session.Metronome);
        Assert.Single(clicks);
    }

    [Fact]
    public void ProcessAutoPlay_WhenNextNoteIsInFuture_ShouldLeaveIndexUnchanged()
    {
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 900.0);

        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
    }

    [Fact]
    public void ProcessAutoPlay_WhenNoteIsSlightlyEarly_ShouldNotTriggerUntilScheduledTime()
    {
        // Note is at 1000ms. Calling ProcessAutoPlay at 955ms (45ms early) should NOT trigger.
        // Previously, a ±50ms window allowed early triggering which caused audible timing issues.
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        // 45ms before note time - should NOT trigger
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 955.0);

        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);

        // At exactly note time - should trigger
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1000.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
    }

    [Fact]
    public void ProcessAutoPlay_WhenNoteIsLateDueToFrameHitch_ShouldStillAutoHit()
    {
        // Autoplay should never skip a pending note, even if the frame arrived
        // late due to a GC pause, frame hitch, or low FPS. A note at 1000ms
        // processed at 1100ms (100ms late) must still be auto-hit.
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        // 100ms past note time — well beyond any reasonable frame budget
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1100.0);
        judgementManager.Update(1100.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void ProcessAutoPlay_FrameHitchScenario_NoteAt1000ms_Frames999Then1051_ShouldAutoHit()
    {
        // Exact regression scenario from review: frame at 999ms (note still in future),
        // then frame at 1051ms (51ms past note). The old code would skip because
        // 51ms > 50ms autoPlayWindowMs. Autoplay must always hit pending past-due notes.
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        // Frame 1: 999ms — note at 1000ms is in the future, should NOT trigger
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 999.0);
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);

        // Frame 2: 1051ms — note is 51ms late. Must still auto-hit, not skip.
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1051.0);
        judgementManager.Update(1051.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void ProcessAutoPlay_WhenNextNoteIsWithinWindow_ShouldQueueHitAndPressPad()
    {
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1000.0);
        judgementManager.Update(1000.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void ProcessAutoPlay_WhenNoteWasAlreadyResolved_ShouldAdvanceIndexWithoutTriggeringPadPress()
    {
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        judgementManager.EnqueueLaneHit(0, "PreResolved");
        judgementManager.Update(1000.0);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1000.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void ProcessAutoPlay_WhenDueManualNotePrecedesAutomatedNote_ShouldAdvanceCursorAndResolveAutomatedNote()
    {
        using var stage = CreateStageWithAutoPlay(1);
        var chartManager = BindChart(stage, BuildChart(new[]
        {
            new Note(laneIndex: 0, bar: 0, tick: 96, channel: 0x11, value: "01"),
            new Note(laneIndex: 1, bar: 0, tick: 192, channel: 0x12, value: "01")
        }));
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 2000.0);

        Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(NoteStatus.Pending, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[1].Id)!.Status);
        Assert.Equal(
            JudgementType.Perfect,
            judgementManager.GetNoteRuntimeData(chartManager.AllNotes[1].Id)!.JudgementEvent!.Type);
        var padVisuals = ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals");
        Assert.Equal(PadState.Idle, padVisuals![0].State);
        Assert.Equal(PadState.Pressed, padVisuals[1].State);
    }

    [Fact]
    public void UpdateGameplayManagers_WithAutoPlayDisabled_DoesNotProcessAutoPlay()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 1000.0);

        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.NotEqual(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void UpdateGameplayManagers_WithAutoPlayEnabled_AdvancesAutoPlay()
    {
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 1000.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
    }

    [Fact]
    public void ProcessAutoPlay_NoteInWindow_PlaysChipForNote()
    {
        using var stage = CreateStageWithAutoPlay(3);
        var chartManager = BindChart(stage, BuildChart(new[]
        {
            new Note(laneIndex: 3, bar: 0, tick: 10, channel: 0x12, value: "07"),
        }));

        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(
            new Dictionary<string, ISound> { ["07"] = soundMock.Object });

        InstallBoundSession(stage, preparedSet: chipSet);

        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = true;

        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 0);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 110.0);

        soundMock.Verify(s => s.Play(), Times.Once);
    }

    [Fact]
    public void ProcessAutoPlay_NoteInWindow_NoChipCache_DoesNotThrow()
    {
        using var stage = CreateStageWithAutoPlay(3);
        var chartManager = BindChart(stage, BuildChart(new[]
        {
            new Note(laneIndex: 3, bar: 0, tick: 10, channel: 0x12, value: "07"),
        }));
        InstallBoundSession(stage);

        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = true;

        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 0);

        var ex = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 110.0));

        Assert.Null(ex);
    }

    [Fact]
    public void ProcessAutoPlay_NoteFarPastHitWindow_StillResolvedAsHit()
    {
        // Regression test: Previously, ProcessAutoPlay used EnqueueLaneHit which
        // went through FindNearestUnhitNote with a 200ms hit detection window.
        // If a note was >200ms past its time (GC pause, frame hitch), the hit
        // was enqueued but couldn't be matched → note got marked Missed instead.
        // With ResolveAutoHit, autoplay directly resolves by note ID, bypassing
        // the window entirely. A note at 1000ms processed at 1500ms (400ms late)
        // must still be auto-hit with Perfect timing.
        using var stage = CreateStageWithAutoPlay(0);
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        // 500ms past note time — far beyond the 200ms hit detection window.
        // With the old EnqueueLaneHit flow, this note would be Missed.
        // With ResolveAutoHit, it's deterministically resolved as Hit.
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1500.0);

        // No need for judgementManager.Update — ResolveAutoHit is immediate
        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);

        // Verify it got Perfect timing
        var noteData = judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!;
        Assert.Equal(JudgementType.Perfect, noteData.JudgementEvent!.Type);
        Assert.Equal(0.0, noteData.JudgementEvent.DeltaMs);
    }

    [Fact]
    public void ProcessAutoPlay_MultipleNotesFarPastHitWindow_AllResolvedAsHit()
    {
        // Multiple notes all >200ms past their time — all must be resolved.
        // This simulates a long GC pause where the song jumps forward significantly.
        using var stage = CreateStageWithAutoPlay(0, 1, 2);
        var chartManager = BindChart(stage, BuildChart(new[]
        {
            new Note(laneIndex: 0, bar: 0, tick: 10, channel: 0x11, value: "01"),
            new Note(laneIndex: 1, bar: 0, tick: 19, channel: 0x12, value: "01"),
            new Note(laneIndex: 2, bar: 0, tick: 29, channel: 0x13, value: "01"),
        }));
        var judgementManager = CreateActiveJudgement(stage);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        // Jump to 1000ms — all notes are 700-900ms past their time, well beyond 200ms window
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1000.0);

        Assert.Equal(3, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[1].Id)!.Status);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[2].Id)!.Status);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WhenAutoPlayEnabled_NoPadOrChip()
    {
        using var stage = CreateStageWithAutoPlay(0);

        // Seed a hittable note on lane 0 so PlayChipForNote would have something to play
        var note = new Note(laneIndex: 0, bar: 0, tick: 96, channel: 0x12, value: "07");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));

        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = true;

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["07"] = soundMock.Object });

        // Set timer to playing state at 1010ms (within note's hit window)
        // so without the autoplay guard, PlayChipForNote WOULD be called
        InstallBoundSession(stage, preparedSet: chipSet, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(1010.0), TimeSpan.FromSeconds(0.016)));

        var args = new LaneHitEventArgs(0, new ButtonState("Player", true, 1.0f));
        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

        // Pad must remain Idle (Pressed would mean TriggerPadPress fired)
        var pads = ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals");
        Assert.Equal(PadState.Idle, pads[0].State);
        // Sound must NOT play — autoplay guard prevents it even though a
        // hittable note exists and the timer is playing
        soundMock.Verify(s => s.Play(), Times.Never);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_AutoPlayOff_PlaysChipForNearestNote()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());

        var note = new Note(laneIndex: 3, bar: 0, tick: 96, channel: 0x12, value: "07");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));

        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = true;

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["07"] = soundMock.Object });

        // Drive _songTimer.GetCurrentMs(_currentGameTime) -> 1010ms (10ms past the note)
        InstallBoundSession(stage, preparedSet: chipSet, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(1010.0), TimeSpan.FromSeconds(0.016)));

        var args = new LaneHitEventArgs(3, new ButtonState("Player", true, 1.0f));
        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

        soundMock.Verify(s => s.Play(), Times.Once);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_AutoPlayOff_NoNoteInWindow_NoChip()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());

        // Note exists but is far away — outside the ±200ms window
        var note = new Note(laneIndex: 3, bar: 2, tick: 96, channel: 0x12, value: "07");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));

        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = true;

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["07"] = soundMock.Object });

        InstallBoundSession(stage, preparedSet: chipSet, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(1000.0), TimeSpan.FromSeconds(0.016)));

        var args = new LaneHitEventArgs(3, new ButtonState("Player", true, 1.0f));
        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

        soundMock.Verify(s => s.Play(), Times.Never);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WithLatencyOffset_UsesCompensatedTimeForChipLookup()
    {
        // Verify P2 fix: chip feedback must use the same compensated clock as
        // JudgementManager so that a note within the Poor window at the
        // compensated time is found even when the raw clock is offset.
        const int latencyOffsetMs = 200;
        using var stage = CreateStageWithPolicy(new ConfigData { AudioLatencyOffsetMs = latencyOffsetMs });

        // Note at 1000ms on lane 3. With 200ms latency offset, raw clock 1200ms
        // should map to compensated 1000ms — within the Poor window (150ms).
        var note = new Note(laneIndex: 3, bar: 0, tick: 96, channel: 0x12, value: "07");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));

        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = true;

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["07"] = soundMock.Object });

        // Raw clock at 1200ms. Without compensation, distance to note = 200ms > PoorWindow (150ms) → no chip.
        // With compensation, compensated = 1000ms, distance = 0ms → chip plays.
        InstallBoundSession(stage, preparedSet: chipSet, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(1200.0), TimeSpan.FromSeconds(0.016)));

        var args = new LaneHitEventArgs(3, new ButtonState("Player", true, 1.0f));
        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

        // Chip sound should play because compensated time (1000ms) matches note exactly
        soundMock.Verify(s => s.Play(), Times.Once);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_SongNotPlaying_VisualPadButNoChipSound()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());

        var note = new Note(laneIndex: 3, bar: 0, tick: 1, channel: 0x12, value: "07");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));

        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = true;

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var soundMock = new Mock<ISound>();
        var chipSet = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["07"] = soundMock.Object });
        // Bound but never started: IsPlaying stays false while the chip cache
        // holds the real mock, so a broken suppression guard would call Play.
        InstallBoundSession(stage, preparedSet: chipSet);

        // currentTimeMs would be within 200ms of the note at 10ms, but chip
        // playback must be suppressed because the song isn't playing.
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(10.0), TimeSpan.FromSeconds(0.016)));

        var args = new LaneHitEventArgs(3, new ButtonState("Player", true, 1.0f));
        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

        // Pad visual feedback should still fire (pad is pressed)
        var pads = ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals");
        Assert.Equal(PadState.Pressed, pads[3].State);

        // Chip sound must NOT play when song timer is not running
        soundMock.Verify(s => s.Play(), Times.Never);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_SongNotPlaying_ShouldNotRecordTelemetry()
    {
        // Telemetry (_lastLaneHit*) must only be recorded while the chart is actively playing.
        // A hit during loading/countdown/after-stop must not populate the last-hit fields, which
        // would otherwise leak into GameApi state for the next consumer.
        var stage = CreateStage();
        // No _songTimer set -> IsPlaying is false.

        var args = new LaneHitEventArgs(4, new ButtonState("Key.Z", true, 1.0f));
        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

        Assert.Null(ReflectionHelpers.GetPrivateField<PerformanceStage.LastLaneHit?>(stage, "_lastLaneHit"));
    }

    [Fact]
    public void CleanupComponents_ShouldClearLastLaneHitTelemetry()
    {
        // On reactivation, a stage must not inherit the previous song's last-hit telemetry.
        // CleanupComponents resets the cached fields so PopulateTelemetry only reports fresh hits.
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_lastLaneHit", new PerformanceStage.LastLaneHit(5, "MIDI.36", 1234.0));
        // CreateStage() uses GetUninitializedObject, so the field-initialized collections that
        // CleanupComponents iterates must be provided explicitly. All other cleanup paths are
        // null-conditional and tolerate the uninitialized state.

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

        Assert.Null(ReflectionHelpers.GetPrivateField<PerformanceStage.LastLaneHit?>(stage, "_lastLaneHit"));
    }

    [Fact]
    public void PopulateTelemetry_LastHitRecorded_ShouldDeconstructConsistently()
    {
        // Guards the torn-read fix: the three LastLaneHit* telemetry values must all come
        // from the same immutable record snapshot, never a mix of two different hits.
        // A single record reference is read once (atomic) and then deconstructed.
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_lastLaneHit", new PerformanceStage.LastLaneHit(7, "MIDI.42", 5678.5));

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(7, telemetry.LastLaneHitLane);
        Assert.Equal("MIDI.42", telemetry.LastLaneHitButtonId);
        Assert.Equal(5678.5, telemetry.LastLaneHitSongTimeMs);
    }

    [Fact]
    public void PopulateTelemetry_NoHitRecorded_ShouldReportNullLastHit()
    {
        var stage = CreateStage();
        // _lastLaneHit is null by default (no hit recorded).

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Null(telemetry.LastLaneHitLane);
        Assert.Null(telemetry.LastLaneHitButtonId);
        Assert.Null(telemetry.LastLaneHitSongTimeMs);
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerPlaying_ShouldReportCurrentSongTimeMs()
    {
        // Exercises the PopulateTelemetry snapshot path where songTimerPlaying is true
        // and currentGameTime is not null, so GetCurrentMs is called.
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chart = new ParsedChart("playing-telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        BindChart(stage, chart);
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(2000), TimeSpan.Zero));
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        // SongTimer is playing with _startTime=Zero, so GetCurrentMs = 2000 - 0 = 2000.
        // Telemetry reports the raw clock: latency compensation happens in
        // PerformanceStage.GetPlayerJudgementTimeMs (player judgement only),
        // never inside SongTimer.GetCurrentMs.
        Assert.True(telemetry.CurrentSongTimeMs > 0.0);
        Assert.True(telemetry.PerformanceReady);
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerPaused_ShouldReportFrozenLogicalTime()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chart = new ParsedChart("paused-telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        BindChart(stage, chart);
        var session = InstallBoundSession(
            stage,
            modifiers: new PlaybackModifiers(150, 0),
            start: true);
        session.Timer!.Pause(new GameTime(
            TimeSpan.FromMilliseconds(1000),
            TimeSpan.Zero));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(5000), TimeSpan.Zero));
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(150, -4));

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(1500.0, telemetry.CurrentSongTimeMs);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(150, telemetry.PlaySpeedPercent);
        Assert.Equal(-4, telemetry.PitchSemitones);
        Assert.True(telemetry.PlaybackProfileFrozen);
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerPlayingButCurrentGameTimeNull_ShouldReportZeroSongTime()
    {
        // Exercises the branch where songTimerPlaying is true but currentGameTime is null,
        // so CurrentSongTimeMs falls back to 0.0.
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chart = new ParsedChart("null-gametime.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        BindChart(stage, chart);
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", null);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(0.0, telemetry.CurrentSongTimeMs);
        Assert.True(telemetry.PerformanceReady);
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerNotPlaying_ShouldReportZeroSongTimeAndNotReady()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chart = new ParsedChart("stopped-telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        BindChart(stage, chart);
        // Stopped timer: _isPlaying = false
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(2000), TimeSpan.Zero));
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(0.0, telemetry.CurrentSongTimeMs);
        // _isReady is false and songTimerPlaying is false, so PerformanceReady is false.
        Assert.False(telemetry.PerformanceReady);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WhenSongPlaying_ShouldRecordLastLaneHitTelemetry()
    {
        // Exercises the _lastLaneHit = new LastLaneHit(...) line in OnLaneHitForPadFeedback
        // when the song timer is actively playing.
        var stage = CreateStage();
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(1500), TimeSpan.Zero));
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", null);

        var args = new LaneHitEventArgs(3, new ButtonState("Key.L", true, 1.0f));
        ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

        var lastHit = ReflectionHelpers.GetPrivateField<PerformanceStage.LastLaneHit?>(stage, "_lastLaneHit");
        Assert.NotNull(lastHit);
        Assert.Equal(3, lastHit!.Lane);
        Assert.Equal("Key.L", lastHit.ButtonId);
        Assert.True(lastHit.SongTimeMs > 0.0);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_ShouldFrameSampleLogicalClockAtFrozenSpeed()
    {
        var stage = CreateStage();
        InstallBoundSession(
            stage,
            modifiers: new PlaybackModifiers(150, 0),
            start: true);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(1000), TimeSpan.Zero));
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", null);

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnLaneHitForPadFeedback",
            null,
            new LaneHitEventArgs(
                3,
                new ButtonState("Key.L", true, 1.0f)));

        var lastHit =
            ReflectionHelpers.GetPrivateField<PerformanceStage.LastLaneHit?>(
                stage,
                "_lastLaneHit");
        Assert.NotNull(lastHit);
        Assert.Equal(1500.0, lastHit!.SongTimeMs);
    }

    [Fact]
    public void OnPlayerFailed_WhenNoFailDisabled_ShouldFinalizePerformanceAndTransitionToResult()
    {
        using var stage = CreateStageWithPolicy(new ConfigData { NoFail = false });
        var stageManager = new Mock<IStageManager>();
        StageTransitionPayload? capturedPayload = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => capturedPayload = payload);
        stage.StageManager = stageManager.Object;
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        CreateActiveJudgement(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, new FailureEventArgs { FinalLife = 0.0f, JudgementType = JudgementType.Miss });

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        var summary = Assert.IsType<PerformanceResult>(capturedPayload!).Summary;
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
        Assert.Equal(1, summary.TotalNotes);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    [Fact]
    public void CheckStageCompletion_WhenSongEndBufferPassed_ShouldFinalizeAsSongComplete()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var stageManager = new Mock<IStageManager>();
        StageTransitionPayload? capturedPayload = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => capturedPayload = payload);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("complete-test.dtx") { DurationMs = 1000.0 });
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        CreateActiveJudgement(stage);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 10.0);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_chartEndReachedRealTimeSeconds",
            7.0);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        var summary = Assert.IsType<PerformanceResult>(capturedPayload!).Summary;
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.True(summary.ClearFlag);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    [Fact]
    public void CheckStageCompletion_WhenSongEndsAndGaugeHasFailed_ShouldPreferSongComplete()
    {
        using var stage = CreateStageWithPolicy(new ConfigData { NoFail = false });
        var stageManager = new Mock<IStageManager>();
        StageTransitionPayload? capturedPayload = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => capturedPayload = payload);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("complete-failed-test.dtx") { DurationMs = 1000.0 });
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();

        stage.Rules.InitializeGaugeAndSkill();
        var gaugeManager = stage.Rules.GaugeManager!;
        gaugeManager.Reset(2.0f);
        gaugeManager.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Miss));
        CreateActiveJudgement(stage);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 10.0);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_chartEndReachedRealTimeSeconds",
            7.0);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        var summary = Assert.IsType<PerformanceResult>(capturedPayload!).Summary;
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.True(summary.ClearFlag);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    public void CheckStageCompletion_PostChartBuffer_ShouldRemainThreeRealSeconds(
        int playSpeedPercent)
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(playSpeedPercent, 0));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_parsedChart",
            new ParsedChart("real-buffer.dtx") { DurationMs = 1000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 10.0);

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "CheckStageCompletion",
            1000.0);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 12.999);
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "CheckStageCompletion",
            5000.0);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(
            stage,
            "_stageCompleted"));

        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 13.0);
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "CheckStageCompletion",
            5000.0);

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(
            stage,
            "_stageCompleted"));
    }

    [Fact]
    public void CheckStageCompletion_WhenLogicalTimeReturnsBeforeChartEnd_ShouldResetRealBuffer()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(
            stage,
            "_parsedChart",
            new ParsedChart("reset-buffer.dtx") { DurationMs = 1000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 10.0);
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "CheckStageCompletion",
            1000.0);

        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 12.0);
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "CheckStageCompletion",
            900.0);

        Assert.Null(ReflectionHelpers.GetPrivateField<double?>(
            stage,
            "_chartEndReachedRealTimeSeconds"));

        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 20.0);
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "CheckStageCompletion",
            1000.0);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 22.5);
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "CheckStageCompletion",
            5000.0);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(
            stage,
            "_stageCompleted"));
    }

    [Fact]
    public void CleanupComponents_WhenAssetsAndSoundsExist_ShouldResetStateAndReleaseResources()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var backgroundTexture = CreateTextureMock();
        var shutterTexture = CreateTextureMock();
        var laneBgTexture = CreateTextureMock();
        var laneDividerTexture = CreateTextureMock();
        var laneFlashTexture = CreateTextureMock();
        var judgementLineTexture = CreateTextureMock();
        var gaugeBaseTexture = CreateTextureMock();
        var gaugeFillTexture = CreateTextureMock();
        var progressBaseTexture = CreateTextureMock();
        var progressFillTexture = CreateTextureMock();
        var comboDigitsTexture = CreateTextureMock();
        var scoreDigitsTexture = CreateTextureMock();
        var judgeStringsTexture = CreateTextureMock();
        var lagNumbersTexture = CreateTextureMock();
        var hitTimingFeedbackDisplay = HitTimingFeedbackDisplay.CreateForTesting(lagNumbersTexture.Object);
        var pauseOverlayTexture = CreateTextureMock();
        var dangerOverlayTexture = CreateTextureMock();
        var skillPanelTexture = CreateTextureMock();
        var bgmSound = CreateSoundMock();
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);
        ReflectionHelpers.SetPrivateField(stage, "_inputPaused", true);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 12.0);
        ReflectionHelpers.SetPrivateField(stage, "_stageElapsedTime", 15.0);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.25);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 8);
        ReflectionHelpers.SetPrivateField(stage, "_backgroundTexture", backgroundTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_shutterTexture", shutterTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_laneBgTexture", laneBgTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_laneDividerTexture", laneDividerTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_laneFlashTexture", laneFlashTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_judgementLineTexture", judgementLineTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeBaseTexture", gaugeBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeFillTexture", gaugeFillTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_progressBaseTexture", progressBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_progressFillTexture", progressFillTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_comboDigitsTexture", comboDigitsTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_scoreDigitsTexture", scoreDigitsTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_judgeStringsTexture", judgeStringsTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_hitTimingFeedbackDisplay", hitTimingFeedbackDisplay);
        ReflectionHelpers.SetPrivateField(stage, "_pauseOverlayTexture", pauseOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_dangerOverlayTexture", dangerOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelTexture", skillPanelTexture.Object);
        var session = CreateBgmSession(("01", 1000.0, bgmSound.Object));
        ReflectionHelpers.SetPrivateField(stage, "_audioSession", session);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("cleanup-test.dtx"));
        BindChart(stage, CreateSingleNoteChart());

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_totalTime"));
        Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_stageElapsedTime"));
        Assert.Equal(1.0, ReflectionHelpers.GetPrivateField<double>(stage, "_readyCountdown"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
        Assert.Null(stage.Rules?.ChartManager);
        backgroundTexture.Verify(x => x.RemoveReference(), Times.Once);
        shutterTexture.Verify(x => x.RemoveReference(), Times.Once);
        laneBgTexture.Verify(x => x.RemoveReference(), Times.Once);
        laneDividerTexture.Verify(x => x.RemoveReference(), Times.Once);
        laneFlashTexture.Verify(x => x.RemoveReference(), Times.Once);
        judgementLineTexture.Verify(x => x.RemoveReference(), Times.Once);
        gaugeBaseTexture.Verify(x => x.RemoveReference(), Times.Once);
        gaugeFillTexture.Verify(x => x.RemoveReference(), Times.Once);
        progressBaseTexture.Verify(x => x.RemoveReference(), Times.Once);
        progressFillTexture.Verify(x => x.RemoveReference(), Times.Once);
        comboDigitsTexture.Verify(x => x.RemoveReference(), Times.Once);
        scoreDigitsTexture.Verify(x => x.RemoveReference(), Times.Once);
        judgeStringsTexture.Verify(x => x.RemoveReference(), Times.Once);
        lagNumbersTexture.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Null(ReflectionHelpers.GetPrivateField<HitTimingFeedbackDisplay>(stage, "_hitTimingFeedbackDisplay"));
        pauseOverlayTexture.Verify(x => x.RemoveReference(), Times.Once);
        dangerOverlayTexture.Verify(x => x.RemoveReference(), Times.Once);
        skillPanelTexture.Verify(x => x.RemoveReference(), Times.Once);
        bgmSound.Verify(x => x.Dispose(), Times.Once);
        Assert.Empty(session.ScheduledBgmEvents);
        Assert.Null(stage.AudioSession);
    }

    [AudioFact]
    public void ReturnToSongSelect_ShouldPauseInputStopTimerAndChangeStage()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var stageManager = new Mock<IStageManager>();
        using var bgm = PlayingSoundInstance.Create();
        using var chip = PlayingSoundInstance.Create();
        var bgmInstance = bgm.Instance;
        var chipInstance = chip.Instance;
        var session = InstallSessionWithLiveAudio(stage, bgmInstance, chipInstance);
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);

        Assert.True(session.IsPlaying);
        Assert.Equal(SoundState.Playing, bgmInstance.State);
        Assert.Equal(SoundState.Playing, chipInstance.State);
        // Capture instance state at the moment the transition is requested:
        // Back must stop tracked audio before the fade begins, not just by
        // the time the method returns.
        var bgmStoppedAtTransition = false;
        var chipStoppedAtTransition = false;
        stageManager
            .Setup(x => x.ChangeStage(StageType.SongSelect, It.IsAny<IStageTransition>()))
            .Callback(() =>
            {
                bgmStoppedAtTransition = bgmInstance.State == SoundState.Stopped;
                chipStoppedAtTransition = chipInstance.State == SoundState.Stopped;
            });
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect");

        Assert.True(bgmStoppedAtTransition);
        Assert.True(chipStoppedAtTransition);
        Assert.False(bgmInstance.IsDisposed);
        Assert.False(chipInstance.IsDisposed);
        Assert.False(session.IsPlaying);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        stageManager.Verify(
            x => x.ChangeStage(
                StageType.SongSelect,
                It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition)),
            Times.Once);
    }

    [Fact]
    public void ReturnToSongSelect_WhenStageManagerAndJudgementManagerMissing_ShouldStillPauseInput()
    {
        var stage = CreateStage();

        stage.StageManager = null;

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect"));

        Assert.Null(exception);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
    }

    [Fact]
    public void HandleInput_WhenBackActionTriggeredAndTransitionAllowed_ShouldMarkTransitionAndReturnToSongSelect()
    {
        var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
        var inputManager = new BackActionInputManagerCompat(backTriggered: true);
        var stageGame = CreateHeadlessGame(new StageTestLogger(), new ConfigData(), inputManager);
        Mock.Get(stageGame).Setup(g => g.CanPerformStageTransition()).Returns(game.CanPerformStageTransition);
        Mock.Get(stageGame).Setup(g => g.MarkStageTransition()).Callback(game.MarkStageTransition);
        using var stage = new HeadlessPerformanceStage(stageGame);
        stage.Activate();
        var stageManager = new Mock<IStageManager>();
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage, inputManager);
        InstallBoundSession(stage);
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        Assert.Equal(2.0, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        stageManager.Verify(
            x => x.ChangeStage(
                StageType.SongSelect,
                It.IsAny<IStageTransition>()),
            Times.Once);
    }

    [Fact]
    public void HandleInput_WhenBackActionTriggeredButDebounceBlocks_ShouldNotChangeStage()
    {
        var game = ReflectionHelpers.CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();

        ReflectionHelpers.SetPrivateField(stage, "_inputManager", new BackActionInputManagerCompat(backTriggered: true));
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        // No transition may be issued on any ChangeStage overload while debounce blocks the exit.
        stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>()), Times.Never);
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
            Times.Never);
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload?>()),
            Times.Never);
    }

    [Fact]
    public void OnPlayerFailed_WhenGaugeRaisesFailedEvent_ShouldFinalizeWithoutReadingCurrentConfig()
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(new ConfigData());
        using var stage = CreateStageWithConfigManager(configManager.Object);
        configManager.SetupGet(x => x.Config)
            .Throws(new InvalidOperationException("current config must not be read"));
        var stageManager = new Mock<IStageManager>();
        StageTransitionPayload? capturedPayload = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => capturedPayload = payload);
        stage.StageManager = stageManager.Object;
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var gaugeManager = stage.Rules.GaugeManager!;
        gaugeManager.Reset(2.0f);
        CreateActiveJudgement(stage);
        ReflectionHelpers.InvokePrivateMethod(stage, "WireUpEventHandlers");

        gaugeManager.ProcessJudgement(
            new JudgementEvent(noteRef: 1, lane: 0, deltaMs: 0.0, type: JudgementType.Miss));

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        var summary = Assert.IsType<PerformanceResult>(capturedPayload!).Summary;
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Once);
    }

    [Fact]
    public void OnPlayerFailed_WhenStageAlreadyCompleted_ShouldNotTransitionAgain()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, new FailureEventArgs { FinalLife = 0.0f, JudgementType = JudgementType.Miss });

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void UpdateGameplay_WhenStillLoading_ShouldReturnWithoutMutatingReadyCountdown()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.5);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.Equal(0.5, ReflectionHelpers.GetPrivateField<double>(stage, "_readyCountdown"));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    public void UpdateGameplay_WhenReadyCountdownStillActive_ShouldUseRealTimeAtEveryPlaySpeed(
        int playSpeedPercent)
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        CreateActiveJudgement(stage);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.75);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(playSpeedPercent, 0));

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.Equal(0.5, ReflectionHelpers.GetPrivateField<double>(stage, "_readyCountdown"), 3);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void UpdateGameplay_WhenReadyCountdownExpires_ShouldStartSongAndActivateJudgementManager()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = false;
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.1);
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.016)));

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
    }

    [Fact]
    public void UpdateGameplay_WhenSongIsPlaying_ShouldAdvanceManagersAndProgress()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromMilliseconds(1301.0), TimeSpan.FromSeconds(0.016)));
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("progress-test.dtx") { DurationMs = 2000.0 });

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.016);

        Assert.Equal(1, judgementManager.GetJudgementCount(JudgementType.Miss));
        Assert.Equal(1301.0 / 2000.0, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"), 3);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
    }

    [Fact]
    public void UpdateComponents_WhenAllOptionalComponentsMissing_ShouldNotThrow()
    {
        var stage = CreateStage();

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "UpdateComponents", 0.016));

        Assert.Null(exception);
    }

    [Fact]
    public void UpdateComponents_WhenVisualComponentsExist_ShouldAdvanceComboAnimationWithoutAffectingStableState()
    {
        var stage = CreateStage();
        var scoreDisplay = CreateScoreDisplay();
        var comboDisplay = CreateComboDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_backgroundRenderer", CreateUninitialized<BackgroundRenderer>());
        ReflectionHelpers.SetPrivateField(stage, "_laneBackgroundRenderer", CreateUninitialized<LaneBackgroundRenderer>());
        ReflectionHelpers.SetPrivateField(stage, "_judgementLineRenderer", CreateUninitialized<JudgementLineRenderer>());
        ReflectionHelpers.SetPrivateField(stage, "_scoreDisplay", scoreDisplay);
        ReflectionHelpers.SetPrivateField(stage, "_comboDisplay", comboDisplay);
        scoreDisplay.Score = 123456;
        ReflectionHelpers.SetPrivateField(comboDisplay, "_scale", 1.0f);
        ReflectionHelpers.SetPrivateField(comboDisplay, "_targetScale", 1.5f);
        ReflectionHelpers.SetPrivateField(comboDisplay, "_scaleVelocity", 0.0f);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateComponents", 0.1);

        Assert.Equal(123456, scoreDisplay.Score);
        Assert.True(ReflectionHelpers.GetPrivateField<float>(comboDisplay, "_scale") > 1.0f);
        Assert.True(ReflectionHelpers.GetPrivateField<float>(comboDisplay, "_targetScale") < 1.5f);
        Assert.True(ReflectionHelpers.GetPrivateField<float>(comboDisplay, "_scaleVelocity") > 0.0f);
    }

    [Fact]
    public void DrawBackground_WhenBackgroundTextureExists_ShouldDrawUILayoutBackgroundBounds()
    {
        var stage = CreateStage();
        var backgroundTexture = CreateTextureMock(width: 512, height: 256);

        ReflectionHelpers.SetPrivateField(stage, "_backgroundTexture", backgroundTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawBackground");

        backgroundTexture.Verify(
            texture => texture.Draw(
                null!,
                PerformanceUILayout.Background.Bounds,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                1.0f),
            Times.Once);
    }

    [Fact]
    public void DrawBackground_WhenBackgroundTextureMissing_ShouldUseViewportSizedFallbackPath()
    {
        var stage = CreateStage();
        var spriteBatch = CreateSpriteBatchStub(new Viewport(0, 0, 1920, 1080));

        ReflectionHelpers.SetPrivateField(stage, "_backgroundTexture", null);
        ReflectionHelpers.SetPrivateField(stage, "_backgroundRenderer", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawBackground"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawLaneBackgrounds_WhenLaneTextureExists_ShouldDrawEachVisibleLaneStrip()
    {
        var stage = CreateStage();
        var laneTexture = CreateTextureMock(width: 512, height: 512);
        var expectedCalls = PerformanceUILayout.LaneStrips.SourceRects
            .Take(Math.Min(PerformanceUILayout.LaneCount, PerformanceUILayout.LaneStrips.SourceRects.Length))
            .Select((sourceRect, index) => new
            {
                SourceRect = sourceRect,
                DestinationRect = PerformanceUILayout.LaneStrips.GetDestinationRect(index)
            })
            .ToList();
        var actualCalls = new List<(Rectangle DestinationRect, Rectangle? SourceRect, float Depth)>();

        laneTexture
            .Setup(texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Callback<SpriteBatch, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float>(
                (_, destinationRect, sourceRect, _, _, _, _, depth) =>
                {
                    actualCalls.Add((destinationRect, sourceRect, depth));
                });

        ReflectionHelpers.SetPrivateField(stage, "_laneBgTexture", laneTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawLaneBackgrounds");

        Assert.Equal(expectedCalls.Count, actualCalls.Count);
        for (var i = 0; i < expectedCalls.Count; i++)
        {
            Assert.Equal(expectedCalls[i].DestinationRect, actualCalls[i].DestinationRect);
            Assert.Equal(expectedCalls[i].SourceRect, actualCalls[i].SourceRect);
            Assert.Equal(0.8f, actualCalls[i].Depth);
        }
    }

    [Fact]
    public void DrawLaneBackgrounds_WhenGateHidesLaneBackground_ShouldSkipTextureDraw()
    {
        var config = new ConfigData { LaneDisplayMode = DrumsLaneDisplayMode.LaneOff };
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        var laneTexture = CreateTextureMock(width: 512, height: 512);

        ReflectionHelpers.SetPrivateField(stage, "_laneBgTexture", laneTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawLaneBackgrounds");

        laneTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void DrawLaneBackgrounds_WhenLaneTextureMissing_ShouldUseFallbackRendererPathWithoutThrowing()
    {
        var stage = CreateStage();

        ReflectionHelpers.SetPrivateField(stage, "_laneBgTexture", null);
        ReflectionHelpers.SetPrivateField(stage, "_laneBackgroundRenderer", CreateUninitialized<LaneBackgroundRenderer>());
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawLaneBackgrounds"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawJudgementLine_WhenTextureExists_ShouldDrawNxSolidHitBarBounds()
    {
        var stage = CreateStage();
        var judgementLineTexture = CreateTextureMock(width: 8, height: 8);
        var expectedRectangle = new Rectangle(295, 561, 558, 6);
        var expectedSource = new Rectangle(0, 0, 8, 6);

        ReflectionHelpers.SetPrivateField(stage, "_judgementLineTexture", judgementLineTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawJudgementLine");

        judgementLineTexture.Verify(
            texture => texture.Draw(
                null!,
                expectedRectangle,
                expectedSource,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.6f),
            Times.Once);
    }

    [Fact]
    public void DrawJudgementLine_WhenGateHidesJudgementLine_ShouldSkipTextureDraw()
    {
        var config = new ConfigData { ShowJudgementLine = false };
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        var judgementLineTexture = CreateTextureMock(width: 8, height: 8);

        ReflectionHelpers.SetPrivateField(stage, "_judgementLineTexture", judgementLineTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawJudgementLine");

        judgementLineTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void DrawJudgementLine_WhenTextureMissing_ShouldUseFallbackRendererPathWithoutThrowing()
    {
        var stage = CreateStage();

        ReflectionHelpers.SetPrivateField(stage, "_judgementLineTexture", null);
        ReflectionHelpers.SetPrivateField(stage, "_judgementLineRenderer", CreateUninitialized<JudgementLineRenderer>());
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawJudgementLine"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawGaugeElements_WhenGaugeTexturesExist_ShouldDrawFrameAndScaledFill()
    {
        var stage = CreateStage();
        var gaugeBaseTexture = CreateTextureMock(width: 120, height: 24);
        var gaugeFillTexture = CreateTextureMock(width: 200, height: 8);
        Rectangle? actualDestinationRect = null;
        Rectangle? actualSourceRect = null;
        float? actualDepth = null;

        gaugeFillTexture
            .Setup(texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Callback<SpriteBatch, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float>(
                (_, destinationRect, sourceRect, _, _, _, _, depth) =>
                {
                    actualDestinationRect = destinationRect;
                    actualSourceRect = sourceRect;
                    actualDepth = depth;
                });

        ReflectionHelpers.SetPrivateField(stage, "_gaugeBaseTexture", gaugeBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeFillTexture", gaugeFillTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentGaugeValue", 0.6f);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawGaugeElements");

        var framePosition = PerformanceUILayout.Gauge.FramePosition;
        gaugeBaseTexture.Verify(
            texture => texture.Draw(
                null!,
                new Rectangle((int)framePosition.X, (int)framePosition.Y, 120, 24),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.19f),
            Times.Once);

        var expectedFillWidth = 120;
        var fillOrigin = PerformanceUILayout.Gauge.FillOrigin;
        Assert.Equal(new Rectangle((int)fillOrigin.X, (int)fillOrigin.Y, expectedFillWidth, PerformanceUILayout.Gauge.FillHeight), actualDestinationRect);
        Assert.Equal(new Rectangle(0, 0, expectedFillWidth, PerformanceUILayout.Gauge.FillHeight), actualSourceRect);
        Assert.Equal(0.18f, actualDepth);
    }

    [Fact]
    public void DrawGaugeElements_WhenGaugeValueIsZero_ShouldOnlyDrawFrame()
    {
        var stage = CreateStage();
        var gaugeBaseTexture = CreateTextureMock(width: 120, height: 24);
        var gaugeFillTexture = CreateTextureMock(width: 200, height: 8);

        ReflectionHelpers.SetPrivateField(stage, "_gaugeBaseTexture", gaugeBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeFillTexture", gaugeFillTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentGaugeValue", 0.0f);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawGaugeElements");

        gaugeBaseTexture.Verify(
            texture => texture.Draw(
                null!,
                It.IsAny<Rectangle>(),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.19f),
            Times.Once);
        gaugeFillTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void DrawProgressBar_WhenProgressValuePositive_ShouldDrawFrameAndFallbackFill()
    {
        var stage = CreateStage();
        var progressBaseTexture = CreateTextureMock(width: 60, height: 540);
        Rectangle? actualFillRect = null;
        Color? actualFillColor = null;
        float? actualFillDepth = null;

        ReflectionHelpers.SetPrivateField(stage, "_progressBaseTexture", progressBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentProgressValue", 0.25f);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((rectangle, color, depth) =>
            {
                actualFillRect = rectangle;
                actualFillColor = color;
                actualFillDepth = depth;
            }));
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawProgressBar");

        progressBaseTexture.Verify(
            texture => texture.Draw(
                null!,
                new Rectangle(
                    PerformanceUILayout.Progress.FrameBounds.X,
                    PerformanceUILayout.Progress.FrameBounds.Y,
                    60,
                    540),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.2f),
            Times.Once);

        var barRect = PerformanceUILayout.Progress.BarBounds;
        var expectedFillHeight = (int)(barRect.Height * 0.25f);
        Assert.Equal(new Rectangle(barRect.X, barRect.Bottom - expectedFillHeight, barRect.Width, expectedFillHeight), actualFillRect);
        Assert.Equal(Color.LightBlue, actualFillColor);
        Assert.Equal(0.2f, actualFillDepth);
    }

    [Fact]
    public void DrawProgressBar_WhenProgressValueIsZero_ShouldNotDrawFallbackFill()
    {
        var stage = CreateStage();
        var progressBaseTexture = CreateTextureMock(width: 60, height: 540);
        var fallbackInvoked = false;

        ReflectionHelpers.SetPrivateField(stage, "_progressBaseTexture", progressBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentProgressValue", 0.0f);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((_, _, _) => fallbackInvoked = true));
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawProgressBar");

        progressBaseTexture.Verify(
            texture => texture.Draw(
                null!,
                It.IsAny<Rectangle>(),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.2f),
            Times.Once);
        Assert.False(fallbackInvoked);
    }

    [Fact]
    public void DrawProgressBar_WhenFallbackDrawerAndTextureAreMissing_ShouldSkipFallbackFillWithoutThrowing()
    {
        var stage = CreateStage();
        var progressBaseTexture = CreateTextureMock(width: 60, height: 540);

        ReflectionHelpers.SetPrivateField(stage, "_progressBaseTexture", progressBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentProgressValue", 0.25f);
        ReflectionHelpers.SetPrivateField(stage, "_fallbackRectangleDrawer", null);
        ReflectionHelpers.SetPrivateField(stage, "_fallbackWhiteTexture", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawProgressBar"));

        Assert.Null(exception);
        progressBaseTexture.Verify(
            texture => texture.Draw(
                null!,
                It.IsAny<Rectangle>(),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.2f),
            Times.Once);
    }

    [Fact]
    public void DrawSkillPanel_WhenSkillPanelTextureExists_ShouldDrawAtUILayoutPanelPosition()
    {
        var stage = CreateStage();
        var skillPanelTexture = CreateTextureMock(width: 180, height: 96);
        var panelPosition = PerformanceUILayout.SkillPanel.PanelPosition;

        ReflectionHelpers.SetPrivateField(stage, "_skillPanelTexture", skillPanelTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawSkillPanel");

        skillPanelTexture.Verify(
            texture => texture.Draw(
                null!,
                new Rectangle((int)panelPosition.X, (int)panelPosition.Y, 180, 96),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.2f),
            Times.Once);
    }

    [Fact]
    public void DrawSkillPanel_WhenTextureMissing_ShouldNotThrow()
    {
        var stage = CreateStage();

        ReflectionHelpers.SetPrivateField(stage, "_skillPanelTexture", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawSkillPanel"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawShutters_WhenStageIsTransitioning_ShouldDrawAtUILayoutStartPosition()
    {
        var stage = CreateStage();
        var shutterTexture = CreateTextureMock(width: 96, height: 512);
        var shutterPosition = PerformanceUILayout.Shutter.StartPosition;

        ReflectionHelpers.SetPrivateField(stage, "_shutterTexture", shutterTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.FadeOut);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawShutters");

        shutterTexture.Verify(
            texture => texture.Draw(
                null!,
                new Rectangle((int)shutterPosition.X, (int)shutterPosition.Y, 96, 512),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.2f),
            Times.Once);
    }

    [Fact]
    public void DrawShutters_WhenStageIsInNormalPhase_ShouldSkipTextureDraw()
    {
        var stage = CreateStage();
        var shutterTexture = CreateTextureMock(width: 96, height: 512);

        ReflectionHelpers.SetPrivateField(stage, "_shutterTexture", shutterTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawShutters");

        shutterTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void DrawShutters_WhenTextureMissingDuringTransition_ShouldNotThrow()
    {
        var stage = CreateStage();

        ReflectionHelpers.SetPrivateField(stage, "_shutterTexture", null);
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.FadeIn);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawShutters"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawOverlays_WhenPausedAndDangerActive_ShouldDrawPauseAndPulseDangerTint()
    {
        var stage = CreateStage();
        var pauseOverlayTexture = CreateTextureMock(width: 1280, height: 720);
        var dangerOverlayTexture = CreateTextureMock(width: 1280, height: 720);
        Color? actualDangerColor = null;
        var totalTime = 0.5;
        var expectedDangerAlpha = 0.3f + 0.2f * (float)Math.Sin(totalTime * 4.0);

        dangerOverlayTexture
            .Setup(texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Callback<SpriteBatch, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float>(
                (_, _, _, color, _, _, _, _) => actualDangerColor = color);

        ReflectionHelpers.SetPrivateField(stage, "_pauseOverlayTexture", pauseOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_dangerOverlayTexture", dangerOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_isPaused", true);
        ReflectionHelpers.SetPrivateField(stage, "_isDanger", true);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", totalTime);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawOverlays");

        pauseOverlayTexture.Verify(
            texture => texture.Draw(
                null!,
                new Rectangle(0, 0, 1280, 720),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.05f),
            Times.Once);
        dangerOverlayTexture.Verify(
            texture => texture.Draw(
                null!,
                new Rectangle(0, 0, 1280, 720),
                null,
                It.IsAny<Color>(),
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.05f),
            Times.Once);
        Assert.Equal(Color.White * expectedDangerAlpha, actualDangerColor);
    }

    [Fact]
    public void DrawOverlays_WhenFlagsDisabled_ShouldNotDrawOverlayTextures()
    {
        var stage = CreateStage();
        var pauseOverlayTexture = CreateTextureMock(width: 1280, height: 720);
        var dangerOverlayTexture = CreateTextureMock(width: 1280, height: 720);

        ReflectionHelpers.SetPrivateField(stage, "_pauseOverlayTexture", pauseOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_dangerOverlayTexture", dangerOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_isPaused", false);
        ReflectionHelpers.SetPrivateField(stage, "_isDanger", false);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawOverlays");

        pauseOverlayTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()),
            Times.Never);
        dangerOverlayTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void DrawUIElements_WhenAssetsConfigured_ShouldDrawDeterministicUILayoutPaths()
    {
        var stage = CreateStage();
        var shutterTexture = CreateTextureMock(width: 96, height: 512);
        var skillPanelTexture = CreateTextureMock(width: 180, height: 96);
        var gaugeBaseTexture = CreateTextureMock(width: 120, height: 24);
        var gaugeFillTexture = CreateTextureMock(width: 200, height: 8);
        var progressBaseTexture = CreateTextureMock(width: 60, height: 540);
        var pauseOverlayTexture = CreateTextureMock(width: 1280, height: 720);
        var dangerOverlayTexture = CreateTextureMock(width: 1280, height: 720);
        var fallbackInvocationCount = 0;

        ReflectionHelpers.SetPrivateField(stage, "_shutterTexture", shutterTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelTexture", skillPanelTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeBaseTexture", gaugeBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeFillTexture", gaugeFillTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_progressBaseTexture", progressBaseTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_pauseOverlayTexture", pauseOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_dangerOverlayTexture", dangerOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.FadeIn);
        ReflectionHelpers.SetPrivateField(stage, "_currentGaugeValue", 0.5f);
        ReflectionHelpers.SetPrivateField(stage, "_currentProgressValue", 0.4f);
        ReflectionHelpers.SetPrivateField(stage, "_isPaused", true);
        ReflectionHelpers.SetPrivateField(stage, "_isDanger", true);
        ReflectionHelpers.SetPrivateField(stage, "_fallbackRectangleDrawer", (Action<Rectangle, Color, float>)((_, _, _) => fallbackInvocationCount++));
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawUIElements");

        shutterTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                0.2f),
            Times.Once);
        skillPanelTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                0.2f),
            Times.Once);
        gaugeBaseTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                0.19f),
            Times.Once);
        gaugeFillTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                0.18f),
            Times.Once);
        progressBaseTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                0.2f),
            Times.Once);
        pauseOverlayTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                0.05f),
            Times.Once);
        dangerOverlayTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                0.05f),
            Times.Once);
        Assert.Equal(1, fallbackInvocationCount);
    }

    [Fact]
    public void DrawUIElements_WhenDisplaysAndUiManagerExist_ShouldDrawOptionalUiBranchesWithoutThrowing()
    {
        var stage = CreateStage();
        var scoreDisplay = CreateScoreDisplay();
        var comboDisplay = CreateComboDisplay();
        var uiManager = new UIManager();
        var rootContainer = new TrackingUIContainer();
        uiManager.AddRootContainer(rootContainer);

        ReflectionHelpers.SetPrivateField(stage, "_scoreDisplay", scoreDisplay);
        ReflectionHelpers.SetPrivateField(stage, "_comboDisplay", comboDisplay);
        ReflectionHelpers.SetPrivateField(stage, "_uiManager", uiManager);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawUIElements"));

        Assert.Null(exception);
        Assert.Equal(1, rootContainer.DrawCount);
    }

    [Fact]
    public void DrawUIElements_WhenComboGateHidesCombo_ShouldKeepComboStateButSkipDisplayDraw()
    {
        var config = new ConfigData { ShowCombo = false };
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        var comboDisplay = CreateComboDisplay();
        var comboTexture = CreateTextureMock();
        var spriteBatch = CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720));

        ReflectionHelpers.SetPrivateField(comboDisplay, "_comboTexture", comboTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_comboDisplay", comboDisplay);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnComboChanged",
            null,
            new ComboChangedEventArgs { CurrentCombo = 87 });

        Assert.Equal(87, comboDisplay.Combo);

        // Confirm the existing ComboDisplay texture seam is observable before
        // checking that PerformanceStage suppresses only its draw call.
        comboDisplay.Draw(spriteBatch);
        comboTexture.Verify(
            texture => texture.Draw(
                spriteBatch,
                It.IsAny<Vector2>(),
                It.IsAny<Rectangle?>()),
            Times.AtLeastOnce);
        comboTexture.Invocations.Clear();

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawUIElements");

        comboTexture.Verify(
            texture => texture.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Vector2>(),
                It.IsAny<Rectangle?>()),
            Times.Never);
    }

    [Fact]
    public void DrawFallbackRectangle_WhenDrawerMissingAndWhiteTextureExists_ShouldUseTexturePath()
    {
        var stage = CreateInspectableStage();
        Texture2D fallbackWhiteTexture;
#pragma warning disable SYSLIB0050
        fallbackWhiteTexture = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050

        ReflectionHelpers.SetPrivateField(stage, "_fallbackRectangleDrawer", null);
        ReflectionHelpers.SetPrivateField(stage, "_fallbackWhiteTexture", fallbackWhiteTexture);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "DrawFallbackRectangle",
            new Rectangle(12, 34, 56, 78),
            Color.LightBlue,
            0.2f);

        Assert.Same(fallbackWhiteTexture, stage.DrawFallbackTextureArgument);
        Assert.Equal(new Rectangle(12, 34, 56, 78), stage.DrawFallbackTextureRectangle);
        Assert.Equal(Color.LightBlue, stage.DrawFallbackTextureColor);
        Assert.Equal(0.2f, stage.DrawFallbackTextureDepth);
    }

    [Fact]
    public void DrawNotes_WhenRendererIsReadyAndActiveNoteIsOffscreen_ShouldCompleteWithoutThrowing()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var noteRenderer = CreateNoteRenderer();
        var parsedChart = new ParsedChart("draw-notes-active-note.dtx") { Bpm = 120.0 };
        parsedChart.AddNote(new Note(
            laneIndex: 0,
            bar: 2,
            tick: 0,
            channel: 0x13,
            value: "01"));
        parsedChart.FinalizeChart();

        Assert.True(noteRenderer.IsReady);

        ReflectionHelpers.SetPrivateField(noteRenderer, "<EffectiveLookAheadMs>k__BackingField", 3000.0);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        BindChart(stage, parsedChart);
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNotes"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawMeasureLines_WhenGateHidesMeasureLines_ShouldNotReachConcreteRendererPath()
    {
        var disabledStage = CreateMeasureLineStage(DrumsLaneDisplayMode.LineOff);
        var disabledException = Record.Exception(
            () => ReflectionHelpers.InvokePrivateMethod(disabledStage, "DrawMeasureLines"));

        Assert.Null(disabledException);

        // This intentionally malformed chart manager makes the existing concrete
        // NoteRenderer path observable: an enabled draw enumerates its measure-line
        // source and reaches the null collection. The disabled gate must return
        // before that path is touched.
        var enabledStage = CreateMeasureLineStage(DrumsLaneDisplayMode.AllOn);
        var enabledException = Record.Exception(
            () => ReflectionHelpers.InvokePrivateMethod(enabledStage, "DrawMeasureLines"));

        Assert.NotNull(enabledException);
    }

    [Fact]
    public void DrawMeasureLines_WhenRequiredCollaboratorIsMissing_ShouldNotThrow()
    {
        var stage = CreateStage();

        var exception = Record.Exception(
            () => ReflectionHelpers.InvokePrivateMethod(stage, "DrawMeasureLines"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawMeasureLines_WhenTimerIsPlaying_ShouldQueryAndCullSafely()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var renderer = CreateNoteRenderer();
        var parsedChart = new ParsedChart("draw-measure-lines.dtx") { Bpm = 120.0 };
        parsedChart.AddNote(new Note(0, 1, 0, 0x11, "01"));
        parsedChart.FinalizeChart();

        ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 5.0);
        ReflectionHelpers.SetPrivateField(
            renderer,
            "<EffectiveLookAheadMs>k__BackingField",
            3000.0);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", renderer);
        BindChart(stage, parsedChart);
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_currentGameTime",
            new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteBatch",
            CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        var exception = Record.Exception(
            () => ReflectionHelpers.InvokePrivateMethod(stage, "DrawMeasureLines"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawMeasureLines_WhenTimerIsPaused_ShouldUseFrozenLogicalTime()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var renderer = CreateNoteRenderer();
        var parsedChart = new ParsedChart("draw-paused-measure-lines.dtx")
        {
            Bpm = 120.0
        };
        parsedChart.AddNote(new Note(0, 1, 0, 0x11, "01"));
        parsedChart.FinalizeChart();
        var session = InstallBoundSession(
            stage,
            modifiers: new PlaybackModifiers(150, 0),
            start: true);
        session.Timer!.Pause(new GameTime(TimeSpan.FromMilliseconds(1000), TimeSpan.Zero));

        ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 5.0);
        ReflectionHelpers.SetPrivateField(
            renderer,
            "<EffectiveLookAheadMs>k__BackingField",
            3000.0);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", renderer);
        BindChart(stage, parsedChart);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(5000), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteBatch",
            CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        var exception = Record.Exception(
            () => ReflectionHelpers.InvokePrivateMethod(stage, "DrawMeasureLines"));

        Assert.Null(exception);
        Assert.Equal(
            1500.0,
            session.Timer!.GetCurrentMs(new GameTime(
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.Zero)));
    }

    [Fact]
    public void DrawNoteOverlays_WhenRendererIsReadyAndActiveNoteIsOffscreen_ShouldCompleteWithoutThrowing()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var noteRenderer = CreateNoteRenderer();
        var parsedChart = new ParsedChart("draw-note-overlays-active-note.dtx") { Bpm = 120.0 };
        parsedChart.AddNote(new Note(
            laneIndex: 0,
            bar: 2,
            tick: 0,
            channel: 0x13,
            value: "01"));
        parsedChart.FinalizeChart();

        Assert.True(noteRenderer.IsReady);

        ReflectionHelpers.SetPrivateField(noteRenderer, "<EffectiveLookAheadMs>k__BackingField", 3000.0);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        BindChart(stage, parsedChart);
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNoteOverlays"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNotesAndOverlays_WhenSongTimerPaused_ShouldUseCachedLogicalTime()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var noteRenderer = CreateNoteRenderer();
        var parsedChart = new ParsedChart("draw-paused-note.dtx") { Bpm = 120.0 };
        parsedChart.AddNote(new Note
        {
            LaneIndex = 0,
            Bar = 2,
            Tick = 0,
            Channel = 0x13,
            Value = "01"
        });
        parsedChart.FinalizeChart();
        var session = InstallBoundSession(
            stage,
            modifiers: new PlaybackModifiers(150, 0),
            start: true);
        session.Timer!.Pause(new GameTime(
            TimeSpan.FromMilliseconds(1000),
            TimeSpan.Zero));

        ReflectionHelpers.SetPrivateField(
            noteRenderer,
            "<EffectiveLookAheadMs>k__BackingField",
            3000.0);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        BindChart(stage, parsedChart);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_currentGameTime",
            new GameTime(
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteBatch",
            CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        var notesException = Record.Exception(
            () => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNotes"));
        var overlaysException = Record.Exception(
            () => ReflectionHelpers.InvokePrivateMethod(
                stage,
                "DrawNoteOverlays"));

        Assert.Null(notesException);
        Assert.Null(overlaysException);
        Assert.Equal(
            1500.0,
            session.Timer!.GetCurrentMs(new GameTime(
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.Zero)));
    }

    [Fact]
    public void DrawGameplayState_WhenLoading_ShouldUseFallbackRectangleDrawerForCenteredText()
    {
        var stage = CreateStage();
        Rectangle? actualRectangle = null;
        Color? actualColor = null;
        float? actualDepth = null;

        ReflectionHelpers.SetPrivateField(stage, "_isLoading", true);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((rectangle, color, depth) =>
            {
                actualRectangle = rectangle;
                actualColor = color;
                actualDepth = depth;
            }));

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawGameplayState");

        Assert.Equal(new Rectangle(580, 350, 120, 20), actualRectangle);
        Assert.Equal(Color.White, actualColor);
        Assert.Equal(0.1f, actualDepth);
    }

    [Fact]
    public void DrawGameplayState_WhenReadyCountdownActive_ShouldUseFallbackRectangleDrawerForPulsingReadyText()
    {
        var stage = CreateStage();
        Rectangle? actualRectangle = null;
        Color? actualColor = null;
        float? actualDepth = null;

        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.5);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 0.125);
        ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((rectangle, color, depth) =>
            {
                actualRectangle = rectangle;
                actualColor = color;
                actualDepth = depth;
            }));

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawGameplayState");

        Assert.Equal(new Rectangle(592, 350, 96, 20), actualRectangle);
        Assert.Equal(Color.Yellow, actualColor);
        Assert.Equal(0.1f, actualDepth);
    }

    [Fact]
    public void TriggerBGMEvent_WhenPlayReturnsNull_ShouldAttemptPlaybackWithoutThrowing()
    {
        var sound = CreateSoundMock();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns((Microsoft.Xna.Framework.Audio.SoundEffectInstance)null!);
        var session = CreateBgmSession(("01", 0.0, sound.Object));

        var exception = Record.Exception(() => session.Advance(0.0));

        Assert.Null(exception);
        // No per-WAV overrides on the chart → defaults to full volume, centered, no pitch shift.
        sound.Verify(x => x.Play(1.0f, 0.0f, 0.0f), Times.Once);
    }

    [Fact]
    public void TriggerBGMEvent_ShouldHonorChartVolumeAndPan()
    {
        var sound = CreateSoundMock();
        var session = CreateBgmSession(
            chart =>
            {
                chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 50 });
                chart.SetWavPans(new Dictionary<string, int> { ["01"] = -100 });
            },
            ("01", 0.0, sound.Object));

        session.Advance(0.0);

        sound.Verify(x => x.Play(0.5f, 0.0f, -1.0f), Times.Once);
    }

    [Fact]
    public void TriggerBGMEvent_WhenSoundIsMissing_ShouldNotThrow()
    {
        var session = CreateBgmSession(("missing", 0.0, null));

        var exception = Record.Exception(() => session.Advance(0.0));

        Assert.Null(exception);
    }

    [Fact]
    public void TriggerBGMEvent_WhenPlayThrows_ShouldSwallowPlaybackFailure()
    {
        var sound = CreateSoundMock();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Throws(new InvalidOperationException("boom"));
        var session = CreateBgmSession(("01", 0.0, sound.Object));

        var exception = Record.Exception(() => session.Advance(0.0));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(false, 2, JudgementType.Perfect, -1, false)]
    [InlineData(true, 2, JudgementType.Perfect, -1, true)]
    [InlineData(true, 2, JudgementType.Great, -1, true)]
    [InlineData(true, 2, JudgementType.Good, -1, true)]
    [InlineData(true, 2, JudgementType.Poor, -1, true)]
    [InlineData(true, 2, JudgementType.Miss, -1, false)]
    [InlineData(true, 2, JudgementType.Perfect, 2, false)]
    [InlineData(true, 3, JudgementType.Great, 2, true)]
    public void OnJudgementMade_ShouldRouteOnlyEligibleManualHitsToTimingDisplay(
        bool showHitTimingFeedback,
        int lane,
        JudgementType judgementType,
        int autoPlayLane,
        bool expectedTimingDisplay)
    {
        var config = new ConfigData { ShowHitTimingFeedback = showHitTimingFeedback };
        if (autoPlayLane >= 0)
            config.AutoPlayLanes.Add(autoPlayLane);

        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        using var timingDisplay = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        ReflectionHelpers.SetPrivateField(stage, "_hitTimingFeedbackDisplay", timingDisplay);

        var deltaMs = judgementType == JudgementType.Miss ? 75.0 : -12.5;
        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnJudgementMade",
            null!,
            new JudgementEvent(noteRef: 1, lane: lane, deltaMs: deltaMs, type: judgementType));

        Assert.Equal(expectedTimingDisplay ? 1 : 0, timingDisplay.ActiveLaneCountForTesting);
    }

    [Fact]
    public void OnJudgementMade_WhenTimingDisplayMissing_ShouldNotThrowOnEligibleManualHit()
    {
        // When the display was never initialized (e.g. a reflection-created stage used in
        // tests), an eligible manual hit must not crash on the null Spawn target.
        var config = new ConfigData { ShowHitTimingFeedback = true };
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");
        ReflectionHelpers.SetPrivateField(stage, "_hitTimingFeedbackDisplay", null!);

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(
                stage,
                "OnJudgementMade",
                null!,
                new JudgementEvent(noteRef: 1, lane: 2, deltaMs: -12.5, type: JudgementType.Great)));

        Assert.Null(exception);
    }

    [Fact]
    public void UpdateGameplay_WhenSongIsPlaying_ShouldAdvanceTimingFeedbackDisplay()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_currentGameTime",
            new GameTime(TimeSpan.FromMilliseconds(1301.0), TimeSpan.FromSeconds(0.016)));
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("timing-feedback.dtx") { DurationMs = 2000.0 });

        using var timingDisplay = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        timingDisplay.Spawn(new JudgementEvent(1, 2, -12.5, JudgementType.Great));
        ReflectionHelpers.SetPrivateField(stage, "_hitTimingFeedbackDisplay", timingDisplay);
        Assert.Equal(1, timingDisplay.ActiveLaneCountForTesting);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.016);

        // The display's Update must have been invoked, advancing elapsed time and fading
        // the active state instead of leaving it pinned at full alpha.
        var state = timingDisplay.ActiveStatesForTesting[2];
        Assert.NotNull(state);
        Assert.True(state!.ElapsedSeconds > 0.0);
        Assert.True(state.Alpha < 1f);
    }

    [Fact]
    public void OnJudgementMade_WhenJudgementIsHitAndLaneFlushIsEnabled_ShouldTriggerVisualFeedbackAndLaneFlash()
    {
        var config = new ConfigData { EnableLaneFlush = true };
        using var stage = CreateStageWithPolicy(config);
        BindChart(stage, BuildChart(Enumerable.Range(0, 10).Select(i => new Note { LaneIndex = 0, TimeMs = i * 100 })));
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var scoreManager = stage.Rules.ScoreManager!;
        var comboManager = stage.Rules.ComboManager!;
        var gaugeManager = stage.Rules.GaugeManager!;
        var attackManager = CreateNxAttackEffectManager();
        var noteRenderer = CreateNoteRenderer();
        var popupManager = CreateSpriteJudgementTextPopupManager();
        var padRenderer = CreatePadRenderer();
        var skillPanelDisplay = CreateSkillPanelDisplayState();
        ReflectionHelpers.SetPrivateField(stage, "_nxAttackEffectManager", attackManager);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_spriteJudgementTextPopupManager", popupManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", skillPanelDisplay);

        var judgement = new JudgementEvent(noteRef: 1, lane: 2, deltaMs: 0.0, type: JudgementType.Great);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null, judgement);

        Assert.Equal(scoreManager.CalculateScoreForJudgement(JudgementType.Great), scoreManager.CurrentScore);
        Assert.Equal(1, comboManager.CurrentCombo);
        Assert.Equal(51.5f, gaugeManager.CurrentLife);
#if DEBUG
        Assert.Equal(1, attackManager.SpawnCallCountForTesting);
        Assert.Equal(2, attackManager.LastSpawnLaneForTesting);
        Assert.Equal(JudgementType.Great, attackManager.LastSpawnJudgementTypeForTesting);
#endif
        Assert.Equal(1.0f, ReflectionHelpers.GetPrivateField<float[]>(noteRenderer, "_laneFlashAlpha")[2]);
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[2].State);
        Assert.Equal(1, popupManager.ActivePopupCount);
        Assert.Equal(1, skillPanelDisplay.GreatCount);
        Assert.Equal(1, skillPanelDisplay.ProcessedJudgementCount);
        Assert.Equal(1, skillPanelDisplay.MaxCombo);
    }

    [Fact]
    public void OnJudgementMade_WhenJudgementIsHitAndLaneFlushIsDisabled_ShouldNotTriggerLaneFlash()
    {
        var config = new ConfigData { EnableLaneFlush = false };
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(config));
        var stage = CreateStage(game);
        var noteRenderer = CreateNoteRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnJudgementMade",
            null,
            new JudgementEvent(noteRef: 1, lane: 2, deltaMs: 0.0, type: JudgementType.Great));

        Assert.Equal(0.0f, ReflectionHelpers.GetPrivateField<float[]>(noteRenderer, "_laneFlashAlpha")[2]);
    }

    [Fact]
    public void OnJudgementMade_WhenJudgementIsMissAndLaneFlushIsEnabled_ShouldNotTriggerHitVisualFeedback()
    {
        var config = new ConfigData { EnableLaneFlush = true };
        using var stage = CreateStageWithPolicy(config);
        BindChart(stage, BuildChart(Enumerable.Range(0, 10).Select(i => new Note { LaneIndex = 0, TimeMs = i * 100 })));
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var scoreManager = stage.Rules.ScoreManager!;
        var comboManager = stage.Rules.ComboManager!;
        var gaugeManager = stage.Rules.GaugeManager!;
        var attackManager = CreateNxAttackEffectManager();
        var noteRenderer = CreateNoteRenderer();
        var popupManager = CreateSpriteJudgementTextPopupManager();
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_nxAttackEffectManager", attackManager);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_spriteJudgementTextPopupManager", popupManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var judgement = new JudgementEvent(noteRef: 1, lane: 4, deltaMs: 75.0, type: JudgementType.Miss);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null, judgement);

        Assert.Equal(0, scoreManager.CurrentScore);
        Assert.Equal(0, comboManager.CurrentCombo);
        Assert.Equal(47.0f, gaugeManager.CurrentLife);
#if DEBUG
        Assert.Equal(0, attackManager.SpawnCallCountForTesting);
        Assert.Null(attackManager.LastSpawnLaneForTesting);
        Assert.Null(attackManager.LastSpawnJudgementTypeForTesting);
#endif
        Assert.Equal(0.0f, ReflectionHelpers.GetPrivateField<float[]>(noteRenderer, "_laneFlashAlpha")[4]);
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[4].State);
        Assert.Equal(1, popupManager.ActivePopupCount);
    }

    [Fact]
    public void OnJudgementMade_WhenAutoPlayAndAutoAddGaugeAreDisabled_ShouldSkipOnlyGaugeForwarding()
    {
        var config = new ConfigData { AutoAddGauge = false };
        config.AutoPlayLanes.Add(2);
        using var stage = CreateStageWithPolicy(config);
        BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var scoreManager = stage.Rules.ScoreManager!;
        var comboManager = stage.Rules.ComboManager!;
        var gaugeManager = stage.Rules.GaugeManager!;

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnJudgementMade",
            null,
            new JudgementEvent(noteRef: 1, lane: 2, deltaMs: 0.0, type: JudgementType.Great));

        Assert.Equal(scoreManager.CalculateScoreForJudgement(JudgementType.Great), scoreManager.CurrentScore);
        Assert.Equal(1, comboManager.CurrentCombo);
        Assert.Equal(GaugeManager.StartingLife, gaugeManager.CurrentLife);
        Assert.Equal(1, stage.Rules.SkillManager!.GreatCount);
    }

    [Fact]
    public void OnJudgementMade_WhenManualPlayAndAutoAddGaugeAreDisabled_ShouldStillUpdateGauge()
    {
        using var stage = CreateStageWithPolicy(new ConfigData { AutoAddGauge = false });
        BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var gaugeManager = stage.Rules.GaugeManager!;

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnJudgementMade",
            null,
            new JudgementEvent(noteRef: 1, lane: 2, deltaMs: 0.0, type: JudgementType.Great));

        Assert.Equal(51.5f, gaugeManager.CurrentLife);
    }

    [Fact]
    public void CheckStageCompletion_WhenGaugeHasFailed_ShouldFinalizeWithoutReadingCurrentConfig()
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(new ConfigData());
        using var stage = CreateStageWithConfigManager(configManager.Object);
        configManager.SetupGet(x => x.Config)
            .Throws(new InvalidOperationException("current config must not be read"));
        var stageManager = new Mock<IStageManager>();
        StageTransitionPayload? capturedPayload = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => capturedPayload = payload);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("nofail-test.dtx") { DurationMs = 5000.0 });
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();

        stage.Rules.InitializeGaugeAndSkill();
        var gaugeManager = stage.Rules.GaugeManager!;
        gaugeManager.Reset(2.0f);
        gaugeManager.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Miss));
        CreateActiveJudgement(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        var summary = Assert.IsType<PerformanceResult>(capturedPayload!).Summary;
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
        stageManager.Verify(
            x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Once);
    }

    [Fact]
    public void CheckStageCompletion_WhenPlayerFailedAndNoFailDisabled_ShouldFinalizeAsPlayerFailed()
    {
        using var stage = CreateStageWithPolicy(new ConfigData { NoFail = false });
        var stageManager = new Mock<IStageManager>();
        StageTransitionPayload? capturedPayload = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => capturedPayload = payload);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("failed-test.dtx") { DurationMs = 5000.0 });
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();

        stage.Rules.InitializeGaugeAndSkill();
        var gaugeManager = stage.Rules.GaugeManager!;
        gaugeManager.Reset(2.0f);
        gaugeManager.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Miss));
        CreateActiveJudgement(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        var summary = Assert.IsType<PerformanceResult>(capturedPayload!).Summary;
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    [Fact]
    public void CheckStageCompletion_WhenStageAlreadyCompleted_ShouldReturnWithoutTransition()
    {
        using var stage = CreateStageWithPolicy(new ConfigData { NoFail = false });
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("already-complete.dtx") { DurationMs = 1000.0 });

        BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var gaugeManager = stage.Rules.GaugeManager!;
        gaugeManager.Reset(2.0f);
        gaugeManager.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Miss));

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void CheckStageCompletion_WhenParsedChartMissing_ShouldReturnWithoutTransition()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void CheckStageCompletion_WhenGaugeHasNotFailedAndSongNotFinished_ShouldNotFinalize()
    {
        using var stage = CreateStageWithPolicy(new ConfigData { NoFail = false });
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("in-progress.dtx") { DurationMs = 5000.0 });
        BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void CheckStageCompletion_WhenGaugeManagerMissingAndSongNotFinished_ShouldNotFinalize()
    {
        using var stage = CreateStageWithPolicy(new ConfigData { NoFail = false });
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("no-gauge.dtx") { DurationMs = 5000.0 });
        Assert.Null(stage.Rules!.GaugeManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void FinalizePerformance_WhenManagersAreMissing_ShouldBuildZeroedSummaryAndPauseInput()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(75, -4));

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(0, summary.Score);
        Assert.Equal(0, summary.TotalNotes);
        Assert.True(summary.ClearFlag);
        Assert.NotEqual(Guid.Empty, summary.RunId);
        Assert.Equal(75, summary.PlaySpeedPercent);
        Assert.Equal(-4, summary.PitchSemitones);
        Assert.True(summary.IsSavable);
        Assert.False(summary.UsedAutoPlay);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
    }

    [Fact]
    public void FinalizePerformance_ShouldUseFrozenProfileInsteadOfMutableConfig()
    {
        var config = new ConfigData
        {
            PlaySpeedPercent = 150,
            PitchSemitones = 12
        };
        config.AutoPlayLanes.Add(2);
        using var stage = CreateStageWithPolicy(config);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(75, -4));

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "FinalizePerformance",
            CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(
            stage,
            "_performanceSummary");
        Assert.Equal(75, summary.PlaySpeedPercent);
        Assert.Equal(-4, summary.PitchSemitones);
        Assert.NotEqual(Guid.Empty, summary.RunId);
        Assert.True(summary.UsedAutoPlay);
        Assert.False(summary.IsSavable);
    }

    [Fact]
    public void FinalizePerformance_WhenPlayerQuits_ShouldNotMarkRunClearOrSavable()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(100, 0));

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "FinalizePerformance",
            CompletionReason.PlayerQuit);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(
            stage,
            "_performanceSummary");
        Assert.NotEqual(Guid.Empty, summary.RunId);
        Assert.False(summary.ClearFlag);
        Assert.False(summary.IsSavable);
    }

    [Fact]
    public void FailLoading_ShouldNotCreateSavablePerformanceSummary()
    {
        var stage = CreateStage();

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "FailLoading",
            "decode failed",
            null);

        Assert.Null(ReflectionHelpers.GetPrivateField<PerformanceSummary?>(
            stage,
            "_performanceSummary"));
    }

    [Fact]
    public void TransitionToResultStage_WhenStageManagerMissing_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_performanceSummary", new PerformanceSummary());

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "TransitionToResultStage"));

        Assert.Null(exception);
    }

    [Fact]
    public void LogPerformanceError_WithAndWithoutException_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_logger", NullLogger<PerformanceStage>.Instance);

        var withoutException = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "plain message", null));
        var withException = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "failure", new InvalidOperationException("boom")));

        Assert.Null(withoutException);
        Assert.Null(withException);
    }

    [Fact]
    public void StartSong_WhenSessionCannotStartPlayback_ShouldDisableReadyAndActivateJudgementManager()
    {
        var logger = new StageTestLogger();
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(logger, new ConfigData(), input));
        var chart = new ParsedChart("start-failure.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 0 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var session = stage.AudioSession!;
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);

        // Disposing the real bound timer makes its Play return false at countdown.
        Assert.NotNull(session.Timer);
        session.Timer!.Dispose();
        stage.Update(1.0);

        stage.PopulateTelemetry(telemetry);
        Assert.False(session.IsPlaying);
        Assert.False(telemetry.PerformanceReady);
        Assert.Single(logger.Errors, entry =>
            entry.Message == "PerformanceStage: Failed to start song playback - audio may be unavailable");

        // A hit queued after failure must survive once a test-supplied clock runs.
        input.ModularInputManager.KeyBindings.BindButton("Test.Lane0", 0);
        Assert.True(input.ModularInputManager.InjectButton("Test.Lane0", true));
        input.ModularInputManager.Update();
        session.Dispose();
        session.Bind(chart, CreateEmptyPreparedSet());
        Assert.True(session.Start(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.Zero), chart));
        stage.Update(0);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(1, telemetry.PerfectCount);
    }

    [Fact]
    public void StartSong_WhenLegacyBgmAndBackgroundWavIdDefined_ShouldApplyPerWavVolumeAndPan()
    {
        // Legacy (no-BGM-events) path: when the stage's parsed chart resolves a
        // BackgroundWavId, StartSong must honor that WAV's #VOLUME/#PAN on the
        // master background track rather than defaulting to full volume. The
        // session is deliberately bound to a different empty chart, so the
        // applied values can only come through the _parsedChart the stage
        // hands to session.Start.
        var stage = CreateStage();
        var chart = new ParsedChart("legacy-bgm.dtx");
        chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 40 });
        chart.SetWavPans(new Dictionary<string, int> { ["01"] = -80 });
        chart.BackgroundWavId = "01";
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);

        // Real SoundEffectInstance so SongTimer.Volume/Pan setters are exercised.
        var instance = CreateSoundEffectInstance();
        var session = InstallBoundSession(
            stage,
            preparedSet: CreatePreparedSetWithMainBackground(instance));
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        // #VOLUME01=40 → 0.4f; #PAN01=-80 → -0.8f (clamped to [-1,1]).
        Assert.Equal(0.4f, session.Timer!.Volume);
        Assert.Equal(-0.8f, session.Timer!.Pan);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void InitializeReadyFont_WhenSpriteBatchIsMissing_ShouldLeaveFontNull()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "InitializeReadyFont"));

        Assert.Null(exception);
        Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_readyFont"));
    }

    [Fact]
    public void TryLoadTexture_WhenResourceManagerUnavailableOrLoadFails_ShouldReturnNull()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", null);

        var missingManagerResult = ReflectionHelpers.InvokePrivateMethod<ITexture>(stage, "TryLoadTexture", "missing");
        Assert.Null(missingManagerResult);

        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.TryLoadTexture("throws", out It.Ref<ITexture>.IsAny)).Throws(new InvalidOperationException("boom"));
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var loadFailureResult = ReflectionHelpers.InvokePrivateMethod<ITexture>(stage, "TryLoadTexture", "throws");
        Assert.Null(loadFailureResult);
    }

    [Fact]
    public void TryLoadTexture_WhenLoadSucceeds_ShouldReturnTexture()
    {
        var stage = CreateStage();
        var texture = CreateTextureMock();
        var resourceManager = new Mock<IResourceManager>();
        var textureResult = texture.Object;
        resourceManager.Setup(x => x.TryLoadTexture("ok", out textureResult)).Returns(true);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var result = ReflectionHelpers.InvokePrivateMethod<ITexture>(stage, "TryLoadTexture", "ok");

        Assert.Same(texture.Object, result);
        // The probe keeps a missing optional asset out of the Warning path.
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenChartManagerIsNull_ShouldReturnNull()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 0, 500.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenJudgementManagerIsNull_ShouldReturnNull()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 0, 500.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteInDifferentLane_ShouldReturnNull()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var note = new Note(laneIndex: 3, bar: 0, tick: 96, channel: 0x12, value: "01");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));
        var judgementManager = CreateActiveJudgement(stage);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 7, 1000.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteAlreadyHit_ShouldSkipAndReturnNull()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var note = new Note(laneIndex: 3, bar: 0, tick: 96, channel: 0x12, value: "01");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));
        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.EnqueueLaneHit(3, "Test");
        judgementManager.Update(1000.0);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 3, 1000.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteIsWithinWindow_ShouldReturnNearestNote()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var note = new Note(laneIndex: 3, bar: 0, tick: 96, channel: 0x12, value: "01");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));
        var judgementManager = CreateActiveJudgement(stage);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 3, 1010.0);

        Assert.NotNull(result);
        Assert.Equal(1000.0, result!.TimeMs);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteIsInMissRange_ShouldReturnNull()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        // Note at 1000ms; querying at 1180ms gives 180ms delta — within the 200ms hit
        // detection window but classified as Miss (151-200ms). Chip playback should skip it.
        var note = new Note(laneIndex: 3, bar: 0, tick: 96, channel: 0x12, value: "01");
        var chartManager = BindChart(stage, BuildChart(new[] { note }));
        var judgementManager = CreateActiveJudgement(stage);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 3, 1180.0);

        Assert.Null(result);
    }

    [Fact]
    public void DrawCenteredText_WhenTextIsEmpty_ShouldReturnWithoutDrawing()
    {
        var stage = CreateStage();
        var fallbackInvoked = false;
        ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((_, _, _) => fallbackInvoked = true));

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawCenteredText", "", Color.White);

        Assert.False(fallbackInvoked);
    }

    [Fact]
    public void DrawPads_WhenPadRendererIsNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", null);

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawPads"));

        Assert.Null(ex);
    }

    [Fact]
    public void DrawNotes_WhenNoteRendererIsNull_ShouldReturnWithoutThrowing()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", null);
        BindChart(stage, CreateSingleNoteChart());
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNotes"));

        Assert.Null(ex);
    }

    [Fact]
    public void DrawNotes_WhenSongNotPlaying_ShouldReturnWithoutDrawing()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var noteRenderer = CreateNoteRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        BindChart(stage, CreateSingleNoteChart());
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNotes"));

        Assert.Null(ex);
    }

    [Fact]
    public void DrawNoteOverlays_WhenNoteRendererIsNull_ShouldReturnWithoutThrowing()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", null);
        BindChart(stage, CreateSingleNoteChart());
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNoteOverlays"));

        Assert.Null(ex);
    }

    [Fact]
    public void DrawNoteOverlays_WhenSongNotPlaying_ShouldReturnWithoutDrawing()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var noteRenderer = CreateNoteRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        BindChart(stage, CreateSingleNoteChart());
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNoteOverlays"));

        Assert.Null(ex);
    }

    #region HPA-11 Chart Background Video

    [Fact]
    public void ProcessVideoEvents_WhenChartHasNoVideoEvents_ShouldNotStartPlayer()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart());

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 10_000.0);

        Assert.Empty(player.StartedPaths);
        Assert.Empty(player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenBeforeFirstEvent_ShouldNotStartPlayer()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(5000.0, "movie.avi")));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 4999.0);

        Assert.Empty(player.StartedPaths);
        Assert.Empty(player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenEventBecomesDue_ShouldStartOnceAndForwardRelativeMediaTime()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 1000.0);

        Assert.Equal(new[] { "movie.avi" }, player.StartedPaths);
        Assert.Equal(new[] { 500.0 }, player.UpdateValues);
        Assert.Equal(500.0, ReflectionHelpers.GetPrivateField<ChartVideoEvent>(stage, "_activeVideoEvent")!.TimeMs);
    }

    [Fact]
    public void ProcessVideoEvents_WhenSeveralEventsAreDue_ShouldStartOnlyLastDueEvent()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart(
            VideoEvent(100.0, "first.avi", "01"),
            VideoEvent(300.0, "second.avi", "02"),
            VideoEvent(700.0, "third.avi", "03")));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 800.0);

        Assert.Equal(new[] { "third.avi" }, player.StartedPaths);
        Assert.Single(player.UpdateValues);
        Assert.Equal(100.0, player.UpdateValues[0]);
    }

    [Fact]
    public void ProcessVideoEvents_WhenLaterUpdateArrives_ShouldForwardExactLogicalMediaTimeWithoutRescaling()
    {
        // The stage runs frozen at 50% play speed (set by CreateVideoStage); the
        // forwarded media time must stay raw logical chart ms, never rescaled.
        var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 1000.0);
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 1500.0);

        Assert.Single(player.StartedPaths);
        Assert.Equal(new[] { 500.0, 1000.0 }, player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenUnresolvedEventIsDue_ShouldLeaveFallbackAndAllowLaterValidEvent()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart(
            VideoEvent(500.0, "", "01"),
            VideoEvent(700.0, "movie.avi", "02")));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 600.0);

        Assert.Empty(player.StartedPaths);
        Assert.Null(ReflectionHelpers.GetPrivateField<ChartVideoEvent>(stage, "_activeVideoEvent"));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 800.0);

        Assert.Equal(new[] { "movie.avi" }, player.StartedPaths);
        Assert.Equal(new[] { 100.0 }, player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenUnresolvedEventSupersedesActiveEvent_ShouldStopActiveAndClear()
    {
        // A valid event starts the video, then a later unresolved event (empty
        // path) becomes due: the active generation must be stopped and the
        // active event cleared so the static background reappears.
        var stage = CreateVideoStage(out var player, CreateVideoChart(
            VideoEvent(500.0, "movie.avi", "01"),
            VideoEvent(900.0, "", "02")));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 600.0);
        Assert.Equal(new[] { "movie.avi" }, player.StartedPaths);
        Assert.NotNull(ReflectionHelpers.GetPrivateField<ChartVideoEvent>(stage, "_activeVideoEvent"));

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 1000.0);

        Assert.Equal(1, player.StopCount);
        Assert.Null(ReflectionHelpers.GetPrivateField<ChartVideoEvent>(stage, "_activeVideoEvent"));
        // No further Update calls after the active event is cleared.
        Assert.Equal(new[] { 100.0 }, player.UpdateValues);
    }

    [Fact]
    public void StopGameplayAudioInstances_WhenVideoIsActive_ShouldStopPlayer()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 1000.0);

        ReflectionHelpers.InvokePrivateMethod(stage, "StopGameplayAudioInstances");

        Assert.Equal(1, player.StopCount);
    }

    [Fact]
    public void CleanupComponents_WhenVideoPlayerExists_ShouldDisposeAndClearPlayer()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

        Assert.Equal(1, player.DisposeCount);
        Assert.Null(ReflectionHelpers.GetPrivateField<IChartVideoPlayer>(stage, "_chartVideoPlayer"));
    }

    [Fact]
    public void ResolveChartVideoDrawLayout_ShouldPinBackgroundBoundsAndDepth()
    {
        var stage = CreateStage();

        var (bounds, depth) = ReflectionHelpers.InvokePrivateMethod<(Rectangle Bounds, float Depth)>(
            stage,
            "ResolveChartVideoDrawLayout");

        Assert.Equal(PerformanceUILayout.Background.Bounds, bounds);
        Assert.Equal(0.95f, depth);
    }

    [Fact]
    public void DrawBackground_WhenVideoPlayerDraws_ShouldConsumePinnedLayout()
    {
        var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteBatch",
            CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));
        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessVideoEvents", 1000.0);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawBackground");

        var draw = Assert.Single(player.DrawCalls);
        Assert.Equal(PerformanceUILayout.Background.Bounds, draw.Bounds);
        Assert.Equal(0.95f, draw.Depth);
    }

    #endregion

    private static PerformanceStage CreateVideoStage(out RecordingChartVideoPlayer player, ParsedChart chart)
    {
        var stage = CreateStage();
        player = new RecordingChartVideoPlayer();
        ReflectionHelpers.SetPrivateField(stage, "_chartVideoPlayer", player);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_playbackModifiers",
            new PlaybackModifiers(50, 0));
        return stage;
    }

    private static ParsedChart CreateVideoChart(params ChartVideoEvent[] events)
    {
        var chart = new ParsedChart("video-schedule.dtx") { Bpm = 120.0 };
        foreach (var videoEvent in events)
        {
            chart.AddVideoEvent(videoEvent);
        }

        return chart;
    }

    private static ChartVideoEvent VideoEvent(double timeMs, string videoFilePath, string videoId = "01")
    {
        return new ChartVideoEvent(0, 0, videoId) { TimeMs = timeMs, VideoFilePath = videoFilePath };
    }

    private sealed record RecordedVideoDraw(Rectangle Bounds, float Depth);

    private sealed class RecordingChartVideoPlayer : IChartVideoPlayer
    {
        public List<string> StartedPaths { get; } = new();
        public List<double> UpdateValues { get; } = new();
        public List<RecordedVideoDraw> DrawCalls { get; } = new();
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Start(string videoFilePath) => StartedPaths.Add(videoFilePath);

        public void Update(double mediaTimeMs) => UpdateValues.Add(mediaTimeMs);

        public void Draw(SpriteBatch spriteBatch, Rectangle destinationBounds, float layerDepth) =>
            DrawCalls.Add(new RecordedVideoDraw(destinationBounds, layerDepth));

        public void Stop() => StopCount++;

        public void Dispose() => DisposeCount++;
    }

    private static HeadlessPerformanceStage CreateStageWithAutoPlay(params int[] lanes)
    {
        var config = new ConfigData();
        config.AutoPlayLanes.UnionWith(lanes);
        return CreateStageWithPolicy(config);
    }

    private static HeadlessPerformanceStage CreateStageWithPolicy(ConfigData config)
    {
        var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config));
        stage.Activate();
        return stage;
    }

    private static HeadlessPerformanceStage CreateStageWithConfigManager(IConfigManager configManager)
    {
        var game = new Mock<IStageGame>();
        game.SetupGet(g => g.ConfigManager).Returns(configManager);
        game.SetupGet(g => g.ResourceManager).Returns(new Mock<IResourceManager>().Object);
        var stage = new HeadlessPerformanceStage(game.Object);
        stage.Activate();
        return stage;
    }

    private static ChartManager BindChart(PerformanceStage stage, ParsedChart chart)
    {
        Assert.NotNull(stage.Rules);
        stage.Rules.BindChart(chart);
        return Assert.IsType<ChartManager>(stage.Rules.ChartManager);
    }

    private static JudgementManager CreateActiveJudgement(PerformanceStage stage, MockInputManagerCompat? input = null)
    {
        Assert.NotNull(stage.Rules);
        stage.Rules.InitializeJudgement(input ?? new MockInputManagerCompat());
        var manager = Assert.IsType<JudgementManager>(stage.Rules.JudgementManager);
        manager.IsActive = true;
        return manager;
    }

    #region HPA-509 Tab/Lanes spawn gating (OnJudgementMade)

    private static object BuildVisualGates(bool showHitTimingFeedback, DrumsNoteDisplayStyle style)
    {
        var gatesType = typeof(PerformanceStage).GetNestedType(
            "PerformanceVisualGates", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(gatesType);
        return Activator.CreateInstance(
            gatesType!,
            false, false, false, false, false,
            showHitTimingFeedback,
            style)!;
    }

    [Fact]
    public void OnJudgementMade_WhenTabEffective_ShouldSkipBothLegacySpawnsAndFeedTabHud()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());
        ReflectionHelpers.SetPrivateField(stage, "_visualGates", BuildVisualGates(true, DrumsNoteDisplayStyle.Tab));
        var spritePopups = new List<SpriteJudgementTextPopup>();
        var fontPopups = new List<JudgementTextPopup>();
        var fontManager = JudgementTextPopupManager.CreateForTesting(
            CreateUninitialized<GraphicsDevice>(), new Mock<IResourceManager>().Object, activePopups: fontPopups);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteJudgementTextPopupManager",
            SpriteJudgementTextPopupManager.CreateForTesting(new MutableTexture(), fontManager.SpawnPopup, spritePopups));
        var timingDisplay = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        ReflectionHelpers.SetPrivateField(stage, "_hitTimingFeedbackDisplay", timingDisplay);
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        tabHud.MarkResourcesReadyForTesting();
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(1, 3, -12, JudgementType.Perfect));

        // Both legacy transients must stay empty in effective Tab — the draws are
        // untouched, so gating the spawns empties all three legacy paths.
        Assert.Empty(spritePopups);
        Assert.Empty(fontPopups);
        Assert.Equal(0, timingDisplay.ActiveLaneCountForTesting);

        // The Tab HUD slot is fed instead: popup word + per-hit fast/slow line
        // (predicate passes) + a lane-colored key-cap flash on the judged lane.
        Assert.Equal("PERFECT", tabHud.PopupForTesting!.Text);
        Assert.Equal("-12", tabHud.PopupTimingForTesting.Text);
        Assert.False(tabHud.PopupTimingForTesting.IsSlow);
        Assert.Equal(1f, tabHud.FlashTimersForTesting[3]);
        Assert.Equal(PerformanceUILayout.GetLaneColor(3), tabHud.FlashColorsForTesting[3]);
    }

    [Fact]
    public void OnJudgementMade_WhenTabMiss_ShouldFeedDangerFlashWithoutTimingLine()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());
        ReflectionHelpers.SetPrivateField(stage, "_visualGates", BuildVisualGates(true, DrumsNoteDisplayStyle.Tab));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteJudgementTextPopupManager",
            SpriteJudgementTextPopupManager.CreateForTesting(new MutableTexture()));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_hitTimingFeedbackDisplay",
            HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture()));
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        tabHud.MarkResourcesReadyForTesting();
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(1, 5, 0, JudgementType.Miss));

        // Miss: no timing line (not a hit), danger lane flash — instrument identity
        // survives even though the newest popup replaces it.
        Assert.Equal("MISS", tabHud.PopupForTesting!.Text);
        Assert.Null(tabHud.PopupTimingForTesting.Text);
        Assert.Equal(1f, tabHud.FlashTimersForTesting[5]);
        Assert.Equal(tabHud.Palette.Danger, tabHud.FlashColorsForTesting[5]);
    }

    [Fact]
    public void OnJudgementMade_WhenTabAndTimingFeedbackHidden_ShouldOmitPerHitTimingLineOnly()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());
        ReflectionHelpers.SetPrivateField(stage, "_visualGates", BuildVisualGates(false, DrumsNoteDisplayStyle.Tab));
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        tabHud.MarkResourcesReadyForTesting();
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(1, 3, -12, JudgementType.Perfect));

        // The popup still appears, but the per-hit fast/slow line obeys the same
        // opt-in predicate as the Lanes timing display. Stat tiles stay ungated.
        Assert.Equal("PERFECT", tabHud.PopupForTesting!.Text);
        Assert.Null(tabHud.PopupTimingForTesting.Text);
    }

    [Fact]
    public void OnJudgementMade_WhenTabHudNotReady_ShouldFallBackToLegacySpawns()
    {
        // Strip published but HUD resources dead: the judgement routing falls back
        // to the Lanes popup path so the player still sees feedback — the strip is
        // not left with a silent HUD (HPA-509 review).
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());
        ReflectionHelpers.SetPrivateField(stage, "_visualGates", BuildVisualGates(true, DrumsNoteDisplayStyle.Tab));
        var spritePopups = new List<SpriteJudgementTextPopup>();
        var fontPopups = new List<JudgementTextPopup>();
        var fontManager = JudgementTextPopupManager.CreateForTesting(
            CreateUninitialized<GraphicsDevice>(), new Mock<IResourceManager>().Object, activePopups: fontPopups);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteJudgementTextPopupManager",
            SpriteJudgementTextPopupManager.CreateForTesting(new MutableTexture(), fontManager.SpawnPopup, spritePopups));
        var timingDisplay = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        ReflectionHelpers.SetPrivateField(stage, "_hitTimingFeedbackDisplay", timingDisplay);
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        // Deliberately NOT marked ready — the same state a real font-load failure leaves.
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(1, 3, -12, JudgementType.Perfect));

        Assert.Single(spritePopups);
        Assert.Equal(1, timingDisplay.ActiveLaneCountForTesting);
        Assert.Null(tabHud.PopupForTesting);
    }

    [Fact]
    public void OnJudgementMade_WhenTabPartialAutoPlayLane_ShouldKeepPlayerPopupAndTiming()
    {
        // Partial autoplay: an automated lane's judgement must not replace the
        // player's popup — its word carries no timing and would wipe the player's
        // fast/slow line within milliseconds of a chord (HPA-509 review). The
        // automated lane still flashes so the strip stays alive.
        using var stage = CreateStageWithAutoPlay(3);
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());
        ReflectionHelpers.SetPrivateField(stage, "_visualGates", BuildVisualGates(true, DrumsNoteDisplayStyle.Tab));
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        tabHud.MarkResourcesReadyForTesting();
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(1, 5, -12, JudgementType.Perfect));
        var playerPopup = tabHud.PopupForTesting;
        Assert.NotNull(playerPopup);

        // Autoplay resolves its own lane during the same chord.
        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(0, 3, 0, JudgementType.Perfect));

        Assert.Same(playerPopup, tabHud.PopupForTesting);
        Assert.Equal("-12", tabHud.PopupTimingForTesting.Text);
        Assert.False(tabHud.PopupTimingForTesting.IsSlow);
        Assert.Equal(1f, tabHud.FlashTimersForTesting[3]);
    }

    [Fact]
    public void OnJudgementMade_WhenTabFullAutoPlay_ShouldFeedPopupFromAutomatedLanes()
    {
        // Full autoplay keeps the popup feed — with no player there is nothing to
        // protect, and the strip mirrors what the Lanes HUD would show.
        using var stage = CreateStageWithAutoPlay(Enumerable.Range(0, PerformanceUILayout.LaneCount).ToArray());
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());
        ReflectionHelpers.SetPrivateField(stage, "_visualGates", BuildVisualGates(true, DrumsNoteDisplayStyle.Tab));
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        tabHud.MarkResourcesReadyForTesting();
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(0, 3, 0, JudgementType.Perfect));

        Assert.Equal("PERFECT", tabHud.PopupForTesting!.Text);
        Assert.Equal(1f, tabHud.FlashTimersForTesting[3]);
    }

    [Fact]
    public void BuildTabHudFrameState_WhenShowComboDisabled_ShouldGateTheTabCombo()
    {
        // ShowCombo=false hides the Lanes combo draw; the Tab HUD combo must obey
        // the same frozen gate instead of drawing unconditionally (final review).
        var stage = CreateStageWithConfig(new ConfigData { ShowCombo = false });

        var state = ReflectionHelpers.InvokePrivateMethod<TabHudFrameState>(
            stage, "BuildTabHudFrameState");

        Assert.True(state.HideCombo);
    }

    [Fact]
    public void OnJudgementMade_WhenLanesEffective_ShouldKeepBothLegacySpawns()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_visualGates", BuildVisualGates(true, DrumsNoteDisplayStyle.Lanes));
        var spritePopups = new List<SpriteJudgementTextPopup>();
        var fontPopups = new List<JudgementTextPopup>();
        var fontManager = JudgementTextPopupManager.CreateForTesting(
            CreateUninitialized<GraphicsDevice>(), new Mock<IResourceManager>().Object, activePopups: fontPopups);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteJudgementTextPopupManager",
            SpriteJudgementTextPopupManager.CreateForTesting(new MutableTexture(), fontManager.SpawnPopup, spritePopups));
        var timingDisplay = HitTimingFeedbackDisplay.CreateForTesting(new MutableTexture());
        ReflectionHelpers.SetPrivateField(stage, "_hitTimingFeedbackDisplay", timingDisplay);
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(1, 3, -12, JudgementType.Perfect));
        ReflectionHelpers.InvokePrivateMethod(
            stage, "OnJudgementMade", null, new JudgementEvent(2, 3, 0, JudgementType.Miss));

        // Lanes keeps the exact legacy behavior: every judgement spawns a sprite
        // popup, hits also spawn the timing run; the Tab slot is never fed.
        Assert.Equal(2, spritePopups.Count);
        Assert.Empty(fontPopups); // valid sprite texture → no font fallback
        Assert.Equal(1, timingDisplay.ActiveLaneCountForTesting); // Miss never spawns timing
        Assert.Null(tabHud.PopupForTesting);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WhenNotationStripActive_ShouldFlashTabHudLane()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_notationStrip", CreateReadyNotationStrip());
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnLaneHitForPadFeedback",
            stage,
            new LaneHitEventArgs(3, new ButtonState("Key.S", true)));

        Assert.Equal(1f, tabHud.FlashTimersForTesting[3]);
        Assert.Equal(PerformanceUILayout.GetLaneColor(3), tabHud.FlashColorsForTesting[3]);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WhenNotationStripMissing_ShouldNotFlashTabHudLane()
    {
        var stage = CreateStage();
        using var tabHud = new TabHudDisplay(CreateUninitialized<GraphicsDevice>(), null, "t", "a");
        ReflectionHelpers.SetPrivateField(stage, "_tabHudDisplay", tabHud);

        ReflectionHelpers.InvokePrivateMethod(
            stage,
            "OnLaneHitForPadFeedback",
            stage,
            new LaneHitEventArgs(3, new ButtonState("Key.S", true)));

        Assert.Equal(0f, tabHud.FlashTimersForTesting[3]);
    }

    #endregion

    /// <summary>
    /// A notation strip whose fonts pass the coverage check via mocks, so it mirrors
    /// the IsReady-retained renderer the stage keeps while Tab is effective.
    /// </summary>
    private static NotationStripRenderer CreateReadyNotationStrip()
    {
        var font = new Mock<IFont>();
        font.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>())).Returns(font.Object);
        return new NotationStripRenderer(
            resources.Object,
            new NotationChart(Array.Empty<NotationMeasure>()),
            new List<MeasureLine> { new MeasureLine { Bar = 0, TimeMs = 0 } });
    }

    private static PerformanceStage CreateStage(BaseGame? game = null)
    {
#pragma warning disable SYSLIB0050
        var stage = (PerformanceStage)FormatterServices.GetUninitializedObject(typeof(PerformanceStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
        ReflectionHelpers.SetPrivateField(stage, "_logger", NullLogger<PerformanceStage>.Instance);
        return stage;
    }

    private static PerformanceStage CreateMeasureLineStage(DrumsLaneDisplayMode laneDisplayMode)
    {
        var config = new ConfigData { LaneDisplayMode = laneDisplayMode };
        var stage = CreateStageWithPolicy(config);
        var renderer = CreateNoteRenderer();
        var chartManager = BindChart(stage, CreateSingleNoteChart());

        ReflectionHelpers.SetPrivateField(chartManager, "_measureLines", null);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", renderer);
        InstallBoundSession(stage, start: true);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_currentGameTime",
            new GameTime(TimeSpan.Zero, TimeSpan.Zero));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_spriteBatch",
            CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));
        return stage;
    }

    private static ThrowingAudioVariantStage CreateThrowingAudioVariantStage()
    {
#pragma warning disable SYSLIB0050
        var stage = (ThrowingAudioVariantStage)FormatterServices.GetUninitializedObject(typeof(ThrowingAudioVariantStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", ReflectionHelpers.CreateGame());
        ReflectionHelpers.SetPrivateField(stage, "_logger", NullLogger<PerformanceStage>.Instance);
        return stage;
    }

    private static CancelingAudioVariantStage CreateCancelingAudioVariantStage()
    {
#pragma warning disable SYSLIB0050
        var stage = (CancelingAudioVariantStage)FormatterServices.GetUninitializedObject(typeof(CancelingAudioVariantStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", ReflectionHelpers.CreateGame());
        ReflectionHelpers.SetPrivateField(stage, "_logger", NullLogger<PerformanceStage>.Instance);
        return stage;
    }

    private static InspectablePerformanceStage CreateInspectableStage(BaseGame? game = null)
    {
#pragma warning disable SYSLIB0050
        var stage = (InspectablePerformanceStage)FormatterServices.GetUninitializedObject(typeof(InspectablePerformanceStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
        ReflectionHelpers.SetPrivateField(stage, "_logger", NullLogger<PerformanceStage>.Instance);
        return stage;
    }

    private static ScoreDisplay CreateScoreDisplay()
    {
#pragma warning disable SYSLIB0050
        var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(display, "_currentScore", 0);
        ReflectionHelpers.SetPrivateField(display, "_scoreText", "0000000");
        return display;
    }

    private static ComboDisplay CreateComboDisplay()
    {
#pragma warning disable SYSLIB0050
        var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(display, "_currentCombo", 0);
        ReflectionHelpers.SetPrivateField(display, "_comboText", "0");
        ReflectionHelpers.SetPrivateField(display, "_visible", false);
        ReflectionHelpers.SetPrivateField(display, "_targetScale", 1.0f);
        return display;
    }

    private static SkillPanelDisplay CreateSkillPanelDisplayState()
    {
#pragma warning disable SYSLIB0050
        return (SkillPanelDisplay)FormatterServices.GetUninitializedObject(typeof(SkillPanelDisplay));
#pragma warning restore SYSLIB0050
    }

    /// <summary>
    /// Counts the stage's own handlers subscribed to the ModularInputManager's
    /// OnLaneHit event via its compiler-generated backing field (same reflection
    /// trick MockInputManagerCompat.TriggerLaneHit uses to raise it). The manager
    /// carries unrelated internal subscribers, so entries are filtered by target.
    /// </summary>
    private static int CountPadFeedbackSubscribers(
        object stage,
        MockInputManagerCompat inputManager)
    {
        var field = typeof(ModularInputManager).GetField(
            "OnLaneHit",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var handler = (EventHandler<LaneHitEventArgs>?)field?.GetValue(inputManager.ModularInputManager);
        return handler?.GetInvocationList().Count(d => ReferenceEquals(d.Target, stage)) ?? 0;
    }

    private static PadState PadStateOf(PadRenderer padRenderer, int lane)
    {
        var visuals = ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals");
        Assert.NotNull(visuals);
        return visuals![lane].State;
    }

    private static PadRenderer CreatePadRenderer()
    {
#pragma warning disable SYSLIB0050
        var renderer = (PadRenderer)FormatterServices.GetUninitializedObject(typeof(PadRenderer));
#pragma warning restore SYSLIB0050
        var padVisuals = new PadVisual[10];
        for (var i = 0; i < padVisuals.Length; i++)
        {
            padVisuals[i] = new PadVisual();
        }

        ReflectionHelpers.SetPrivateField(renderer, "_padVisuals", padVisuals);
        return renderer;
    }

    private static NoteRenderer CreateNoteRenderer()
    {
#pragma warning disable SYSLIB0050
        var renderer = (NoteRenderer)FormatterServices.GetUninitializedObject(typeof(NoteRenderer));
        var whiteTexture = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
        var lanePositions = new Vector2[PerformanceUILayout.LaneCount];
        for (var i = 0; i < lanePositions.Length; i++)
        {
            lanePositions[i] = new Vector2(PerformanceUILayout.GetLaneX(i) - 16f, 0f);
        }

        ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", whiteTexture);
        ReflectionHelpers.SetPrivateField(renderer, "_lanePositions", lanePositions);
        ReflectionHelpers.SetPrivateField(renderer, "_laneColors", Enumerable.Repeat(Color.White, PerformanceUILayout.LaneCount).ToArray());
        ReflectionHelpers.SetPrivateField(renderer, "_laneFlashAlpha", new float[10]);
        ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 0.5);
        ReflectionHelpers.SetPrivateField(renderer, "<EffectiveLookAheadMs>k__BackingField", 1200.0);
        ReflectionHelpers.SetPrivateField(renderer, "_disposed", false);
        return renderer;
    }

    private static SpriteJudgementTextPopupManager CreateSpriteJudgementTextPopupManager()
    {
        return SpriteJudgementTextPopupManager.CreateForTesting(CreateSpriteJudgementTexture());
    }

    private static NxAttackEffectManager CreateNxAttackEffectManager()
    {
        var manager = new NxAttackEffectManager(new Mock<IResourceManager>().Object);
#if DEBUG
        manager.SuppressSpawnForTesting = true;
#endif
        return manager;
    }

    private static ITexture CreateSpriteJudgementTexture()
    {
        return new MutableTexture();
    }

    private static string GetGeneratedTestArtifactPath(string fileName)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "GeneratedTestData");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static IConfigManager CreateConfigManager(ConfigData configData)
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(configData);
        return configManager.Object;
    }

    private static ParsedChart CreateSingleNoteChart()
    {
        var parsedChart = new ParsedChart("performance-stage-test.dtx")
        {
            Bpm = 120.0
        };
        parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        parsedChart.FinalizeChart();
        return parsedChart;
    }

    private static ParsedChart CreateMetronomeChart(params BeatMarker[] markers)
    {
        var chart = new ParsedChart("metronome-stage-test.dtx");
        chart.BeatMarkers.AddRange(markers);
        return chart;
    }

    private static InspectablePerformanceStage CreateInitializedMetronomeStage(
        int playSpeedPercent,
        params BeatMarker[] markers)
    {
        var stage = CreateInspectableStage();
        var sound = new Mock<ISound>();
        var resourceManager = new Mock<IResourceManager>();
        var soundResult = sound.Object;
        resourceManager.Setup(x => x.TryLoadSound(It.IsAny<string>(), out soundResult)).Returns(true);
        InstallBoundSession(
            stage,
            chart: CreateMetronomeChart(
                markers.Length == 0
                    ? new[] { new BeatMarker { TimeMs = 100.0, IsMeasureStart = false } }
                    : markers),
            modifiers: new PlaybackModifiers(playSpeedPercent, 0),
            metronomeEnabled: true,
            resourceManager: resourceManager.Object,
            playClick: stage.RecordMetronomeClick);
        return stage;
    }

    private static ParsedChart BuildChart(IEnumerable<Note> notes)
    {
        var parsed = new ParsedChart("chip-sound-test.dtx") { Bpm = 120.0 };
        foreach (var n in notes)
        {
            parsed.AddNote(n);
        }
        parsed.FinalizeChart();
        return parsed;
    }

    private static string WriteTempStubWav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stub-{Guid.NewGuid()}.wav");
        File.WriteAllBytes(path, new byte[] { 0 });
        return path;
    }

    private static SpriteBatch CreateSpriteBatchStub(Viewport viewport)
    {
#pragma warning disable SYSLIB0050
        var spriteBatch = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
        var graphicsDevice = (GraphicsDevice)FormatterServices.GetUninitializedObject(typeof(GraphicsDevice));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(graphicsDevice, "_viewport", viewport);
        ReflectionHelpers.SetPrivateField(graphicsDevice, "_resourcesLock", new object());
        ReflectionHelpers.SetPrivateField(graphicsDevice, "_resources", new List<WeakReference>());
        ReflectionHelpers.SetProperty(spriteBatch, nameof(SpriteBatch.GraphicsDevice), graphicsDevice);
        return spriteBatch;
    }

    private static Mock<ITexture> CreateTextureMock(int width = 1, int height = 1)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(width);
        texture.SetupGet(x => x.Height).Returns(height);
        texture.Setup(x => x.RemoveReference());
        return texture;
    }

    private static Mock<ISound> CreateSoundMock()
    {
        var sound = new Mock<ISound>();
        sound.Setup(x => x.Dispose());
        return sound;
    }

    private static ParsedChart CreateBoundChart()
    {
        return new ParsedChart("bound-session.dtx") { Bpm = 120.0 };
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
            preparedSoundFactory: (_, path) => new Mock<ISound>().Object)
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

    /// <summary>
    /// Prepared set whose main background creates the supplied real
    /// SoundEffectInstance, letting SongTimer's Volume/Pan setters be observed.
    /// </summary>
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

    /// <summary>
    /// Session bound to a metronome-enabled chart whose beat/accent click
    /// sounds come from the supplied mocks via the resource manager.
    /// </summary>
    private static GameplayAudioSession CreateMetronomeSession(
        ILogger logger,
        ISound? beatSound = null,
        ISound? accentSound = null)
    {
        var resourceManager = new Mock<IResourceManager>();
        var beatResult = beatSound;
        var accentResult = accentSound;
        if (beatSound != null)
            resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeBeat, out beatResult)).Returns(true);
        if (accentSound != null)
            resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeAccent, out accentResult)).Returns(true);
        var session = CreateSession(
            resourceManager: resourceManager.Object,
            metronomeEnabled: true,
            logger: logger);
        session.Bind(
            CreateMetronomeChart(new BeatMarker { TimeMs = 0.0, IsMeasureStart = true }),
            CreateEmptyPreparedSet());
        return session;
    }

    private static GameplayAudioSession CreateBoundSession(
        ParsedChart? chart = null,
        PreparedGameplayAudioSet? preparedSet = null,
        PlaybackModifiers? modifiers = null,
        bool metronomeEnabled = false,
        IResourceManager? resourceManager = null,
        ILogger? logger = null,
        Action<BeatMarker>? playClick = null,
        bool start = false)
    {
        var session = CreateSession(resourceManager, modifiers, metronomeEnabled, logger, playClick);
        var boundChart = chart ?? CreateBoundChart();
        session.Bind(boundChart, preparedSet ?? CreateEmptyPreparedSet());
        if (start)
        {
            session.Start(new GameTime(TimeSpan.Zero, TimeSpan.Zero), boundChart);
        }
        return session;
    }

    /// <summary>
    /// Session bound to a chart carrying the supplied scheduled BGM events.
    /// Each event resolves its WAV id through the prepared set's scheduled-BGM
    /// map keyed by real stub-file paths, mirroring the production wiring.
    /// </summary>
    private static GameplayAudioSession CreateBgmSession(
        params (string WavId, double TimeMs, ISound? Sound)[] events)
        => CreateBgmSession(null, events);

    private static GameplayAudioSession CreateBgmSession(
        Action<ParsedChart>? configureChart,
        params (string WavId, double TimeMs, ISound? Sound)[] events)
    {
        var chart = new ParsedChart("bgm-session.dtx") { Bpm = 120.0 };
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
        configureChart?.Invoke(chart);
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
        var session = CreateSession();
        session.Bind(chart, preparedSet);
        return session;
    }

    private static GameplayAudioSession InstallBoundSession(
        PerformanceStage stage,
        ParsedChart? chart = null,
        PreparedGameplayAudioSet? preparedSet = null,
        PlaybackModifiers? modifiers = null,
        bool metronomeEnabled = false,
        IResourceManager? resourceManager = null,
        ILogger? logger = null,
        Action<BeatMarker>? playClick = null,
        bool start = false)
    {
        var session = CreateBoundSession(
            chart, preparedSet, modifiers, metronomeEnabled, resourceManager, logger, playClick, start);
        ReflectionHelpers.SetPrivateField(stage, "_audioSession", session);
        return session;
    }

    /// <summary>
    /// Already-disposed timer used by the lifecycle cases moved from the
    /// retired performance-stage bucket: finalization and return-to-song-select
    /// must tolerate a timer whose Stop/Dispose paths were already taken.
    /// </summary>
    private static SongTimer CreateDisposedSongTimer()
    {
#pragma warning disable SYSLIB0050
        var timer = (SongTimer)FormatterServices.GetUninitializedObject(typeof(SongTimer));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(timer, "_disposed", true);
        return timer;
    }

    /// <summary>
    /// Creates a real SoundEffectInstance without invoking its constructor
    /// (which requires a native audio device). Pan/Volume backing fields are
    /// usable on the uninitialized instance, letting StartSong's per-WAV
    /// volume/pan assignment be exercised without a graphics device.
    /// Play() throws on this instance, but SongTimer.Play swallows that and
    /// StartSong continues to the volume/pan assignment regardless.
    /// </summary>
    private static SoundEffectInstance CreateSoundEffectInstance()
    {
#pragma warning disable SYSLIB0050
        return (SoundEffectInstance)FormatterServices.GetUninitializedObject(typeof(SoundEffectInstance));
#pragma warning restore SYSLIB0050
    }

    /// <summary>
    /// Installs a bound, started session that already owns one live scheduled-BGM
    /// instance and one live chip instance, so transition tests can verify the
    /// stop reaches tracked playback rather than only the silent timer.
    /// </summary>
    private static GameplayAudioSession InstallSessionWithLiveAudio(
        PerformanceStage stage,
        SoundEffectInstance bgmInstance,
        SoundEffectInstance chipInstance)
    {
        var bgmSound = new Mock<ISound>();
        bgmSound
            .Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(bgmInstance);
        var chipSound = new Mock<ISound>();
        chipSound.Setup(x => x.Play()).Returns(chipInstance);

        var bgmPath = Path.GetFullPath(WriteTempStubWav());
        var chipPath = Path.GetFullPath(WriteTempStubWav());
        var soundsByPath = new Dictionary<string, ISound>(StringComparer.Ordinal)
        {
            [bgmPath] = bgmSound.Object,
            [chipPath] = chipSound.Object,
        };
        var chart = new ParsedChart("live-audio.dtx") { Bpm = 120.0 };
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

        var session = InstallBoundSession(stage, chart: chart, preparedSet: preparedSet, start: true);

        // Fire the scheduled BGM event (the live instance survives reaping
        // because it reports Playing) and play one chip so both tracking lists
        // hold a real playing instance.
        session.Advance(150.0);
        session.PlayChip(new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "07") { TimeMs = 120.0 });
        return session;
    }

    private static T CreateUninitialized<T>() where T : class
    {
#pragma warning disable SYSLIB0050
        return (T)FormatterServices.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
    }

    private sealed class TrackingUIContainer : UIContainer
    {
        public int DrawCount { get; private set; }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            DrawCount++;
            base.OnDraw(spriteBatch, deltaTime);
        }
    }

    private sealed class ThrowingAudioVariantStage : PerformanceStage
    {
        public ThrowingAudioVariantStage(IStageGame game)
            : base(game)
        {
        }

        // Lazily initialized so instances created via FormatterServices
        // (which skips property initializers) still expose a real sentinel.
        private InvalidOperationException? _failure;

        public InvalidOperationException Failure =>
            _failure ??= new InvalidOperationException(
                "Simulated variant processor failure",
                new InvalidDataException("inner chain"));

        // The sentinel is captured when the stage resolves the factory (update
        // thread) so the worker-side invocation throws it without touching
        // stage state — and still after chart publication.
        protected override Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
        {
            var failure = Failure;
            return () => throw failure;
        }
    }

    private sealed class SyncThrowingAudioVariantStage : PerformanceStage
    {
        public SyncThrowingAudioVariantStage(IStageGame game)
            : base(game)
        {
        }

        public InvalidOperationException Failure { get; } =
            new InvalidOperationException(
                "Simulated variant factory resolution failure",
                new InvalidDataException("inner chain"));

        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;

        // Resolution itself throws on the update thread; the stage captures it
        // and defers it into a throwing factory, so the worker still publishes
        // the chart before the fault reaches onError.
        protected override Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
            => throw Failure;
    }

    private sealed class CancelingAudioVariantStage : PerformanceStage
    {
        public CancelingAudioVariantStage(BaseGame game)
            : base(game)
        {
        }

        protected override Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
            => () => new CancelingProcessor();

        // The cache path is a snapshot input, so the worker-side construction
        // touches only the captured string — never live stage state.
        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory()
        {
            var cacheRoot = Path.Combine(
                Path.GetTempPath(),
                "DTXMania_PerformanceStage_CancelCache_" + Guid.NewGuid().ToString("N"));
            return () => new PlaybackAudioVariantCache(cacheRoot);
        }

        private sealed class CancelingProcessor : IAudioVariantProcessor
        {
            public Task<PreparedAudioArtifact> PrepareAsync(
                string sourcePath,
                PlaybackModifiers modifiers,
                CancellationToken cancellationToken)
                => Task.FromException<PreparedAudioArtifact>(
                    new OperationCanceledException("Simulated external decoder cancellation"));
        }
    }

    private sealed class InspectablePerformanceStage : PerformanceStage
    {
        private List<BeatMarker>? _metronomeClicks;

        public InspectablePerformanceStage(BaseGame game)
            : base(game)
        {
        }

        public IReadOnlyList<BeatMarker> MetronomeClicks =>
            _metronomeClicks ??= new List<BeatMarker>();

        protected override void PlayMetronomeClick(BeatMarker marker) => RecordMetronomeClick(marker);

        public void RecordMetronomeClick(BeatMarker marker)
        {
            (_metronomeClicks ??= new List<BeatMarker>()).Add(marker);
        }

        public Texture2D? DrawFallbackTextureArgument { get; private set; }

        public Rectangle? DrawFallbackTextureRectangle { get; private set; }

        public Color? DrawFallbackTextureColor { get; private set; }

        public float? DrawFallbackTextureDepth { get; private set; }

        // The cache path is a snapshot input, so the worker-side construction
        // touches only the captured string — never live stage state.
        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory()
        {
            var cacheRoot = Path.Combine(
                Path.GetTempPath(),
                "DTXMania_PerformanceStage_TestCache_" + Guid.NewGuid().ToString("N"));
            return () => new PlaybackAudioVariantCache(cacheRoot);
        }

        internal override void DrawFallbackTexture(Texture2D texture, Rectangle destinationRectangle, Color color, float layerDepth)
        {
            DrawFallbackTextureArgument = texture;
            DrawFallbackTextureRectangle = destinationRectangle;
            DrawFallbackTextureColor = color;
            DrawFallbackTextureDepth = layerDepth;
        }
    }

    private sealed class BackActionInputManagerCompat : MockInputManagerCompat
    {
        private readonly bool _backTriggered;

        public BackActionInputManagerCompat(bool backTriggered)
        {
            _backTriggered = backTriggered;
        }

        public override bool IsBackActionTriggered()
        {
            return _backTriggered;
        }
    }

    [Fact]
    public void OnActivate_WhenConfigManagerNotNull_ShouldSubscribeToScrollSpeedChanged()
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(new ConfigData());
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_transitionPayload", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        var configManagerField = game.ConfigManager;
        Assert.NotNull(configManagerField);

        ReflectionHelpers.SetPrivateField(stage, "_subscribedConfigManager", configManagerField);
        configManagerField.ScrollSpeedChanged += OnScrollSpeedChangedHandler;

        Assert.Same(configManagerField, ReflectionHelpers.GetPrivateField<IConfigManager>(stage, "_subscribedConfigManager"));
    }

    [Fact]
    public void OnActivate_WhenConfigManagerIsNull_ShouldNotSubscribe()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), null);
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_transitionPayload", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");
        ReflectionHelpers.InvokePrivateMethod(stage, "FreezeRunConfiguration");

        Assert.Null(ReflectionHelpers.GetPrivateField<IConfigManager>(stage, "_subscribedConfigManager"));
    }

    private static void OnScrollSpeedChangedHandler(object? sender, ScrollSpeedChangedEventArgs e) { }

    [Fact]
    public void OnDeactivate_WhenSubscribedConfigManager_ShouldUnsubscribeFlushAndCleanup()
    {
        var configManager = new Mock<IConfigManager>();
        using var stage = CreateStageWithPolicy(new ConfigData());
        ReflectionHelpers.SetPrivateField(stage, "_subscribedConfigManager", configManager.Object);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);
        ReflectionHelpers.SetPrivateField(stage, "_inputPaused", true);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 12.0);
        ReflectionHelpers.SetPrivateField(stage, "_stageElapsedTime", 15.0);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.25);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 8);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("deactivate-test.dtx"));
        BindChart(stage, CreateSingleNoteChart());

        ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

        configManager.Verify(x => x.FlushPendingSave(), Times.Once);
        Assert.Null(ReflectionHelpers.GetPrivateField<IConfigManager>(stage, "_subscribedConfigManager"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_totalTime"));
        Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_stageElapsedTime"));
        Assert.Equal(1.0, ReflectionHelpers.GetPrivateField<double>(stage, "_readyCountdown"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
    }

    [Fact]
    public void OnDeactivate_WhenNoSubscribedConfigManager_ShouldStillCleanupComponents()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_subscribedConfigManager", null);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate"));

        Assert.Null(exception);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void HandleInput_WhenIncreaseScrollSpeedPressed_ShouldCallAdjustScrollSpeed()
    {
        var configManager = new Mock<IConfigManager>();
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        var inputManager = new ScrollSpeedInputManagerCompat(InputCommandType.IncreaseScrollSpeed);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        configManager.Verify(x => x.AdjustScrollSpeed(1), Times.Once);
    }

    [Fact]
    public void HandleInput_WhenDecreaseScrollSpeedPressed_ShouldCallAdjustScrollSpeedNegative()
    {
        var configManager = new Mock<IConfigManager>();
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        var inputManager = new ScrollSpeedInputManagerCompat(InputCommandType.DecreaseScrollSpeed);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        configManager.Verify(x => x.AdjustScrollSpeed(-1), Times.Once);
    }

    [Fact]
    public void HandleInput_WhenNoConfigManager_ShouldNotThrowOnScrollSpeed()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), null);
        var stage = CreateStage(game);
        var inputManager = new ScrollSpeedInputManagerCompat(InputCommandType.IncreaseScrollSpeed);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput"));

        Assert.Null(exception);
    }

    [Fact]
    public void OnScrollSpeedChanged_ShouldSetScrollSpeedOnNoteRendererAndShowIndicator()
    {
        var stage = CreateStage();
        var noteRenderer = CreateNoteRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        var indicator = new ScrollSpeedIndicator(null);
        ReflectionHelpers.SetPrivateField(stage, "_scrollSpeedIndicator", indicator);

        var args = new ScrollSpeedChangedEventArgs(50, 75);
        ReflectionHelpers.InvokePrivateMethod(stage, "OnScrollSpeedChanged", null, args);

        Assert.Equal("Scroll Speed " + ScrollSpeedRange.Format(75), indicator.Text);
    }

    [Fact]
    public void OnScrollSpeedChanged_WhenNoteRendererNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", null);
        ReflectionHelpers.SetPrivateField(stage, "_scrollSpeedIndicator", new ScrollSpeedIndicator(null));

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "OnScrollSpeedChanged", null, new ScrollSpeedChangedEventArgs(50, 75)));

        Assert.Null(exception);
    }

    [Fact]
    public void OnScrollSpeedChanged_WhenIndicatorNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", null);
        ReflectionHelpers.SetPrivateField(stage, "_scrollSpeedIndicator", null);

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "OnScrollSpeedChanged", null, new ScrollSpeedChangedEventArgs(50, 75)));

        Assert.Null(exception);
    }

    [Fact]
    public void ReturnToSongSelect_WhenSongTimerNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect"));

        Assert.Null(exception);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
    }

    [Fact]
    public void UpdateGameplay_WhenLoading_ShouldReturnEarlyWithoutMutatingState()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.75);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.Equal(0.75, ReflectionHelpers.GetPrivateField<double>(stage, "_readyCountdown"));
    }

    [Fact]
    public void UpdateGameplay_WhenReadyCountdownActive_ShouldDecreaseCountdown()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        CreateActiveJudgement(stage);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.75);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.Equal(0.5, ReflectionHelpers.GetPrivateField<double>(stage, "_readyCountdown"), 3);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void UpdateGameplay_WhenReadyCountdownExpires_ShouldStartSongAndActivateJudgement()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = false;
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.1);
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.016)));

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
    }

    [Fact]
    public void StartSong_WhenNoBgmEvents_ShouldSetVolumeToOne()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = false;
        // A real main background gives the session a timer whose Volume setter
        // is observable; seed a non-default volume so the assignment is proven.
        var session = InstallBoundSession(
            stage,
            preparedSet: CreatePreparedSetWithMainBackground(CreateSoundEffectInstance()));
        session.Timer!.Volume = 0.3f;
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_chartEndReachedRealTimeSeconds", 12.5);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.Equal(1.0f, session.Timer.Volume);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_chartEndReachedRealTimeSeconds"));
    }

    [Fact]
    public void StartSong_WhenBgmEventsExist_ShouldMuteBackgroundAudio()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        judgementManager.IsActive = false;
        var bgmChart = new ParsedChart("startsong-mute.dtx") { Bpm = 120.0 };
        bgmChart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100.0 });
        var session = InstallBoundSession(
            stage,
            chart: bgmChart,
            preparedSet: CreatePreparedSetWithMainBackground(CreateSoundEffectInstance()));
        // Seed an audible volume: the scheduled-BGM path must mute it back to 0.
        session.Timer!.Volume = 0.7f;
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.Equal(0.0f, session.Timer.Volume);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
    }

    [Fact]
    public void StartSong_WhenSongTimerIsNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "StartSong"));

        Assert.Null(exception);
    }

    [AudioFact]
    public void FinalizePerformance_ShouldMarkCompletedPauseInputStopTimerCreateSummary()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var stageManager = new Mock<IStageManager>();
        using var bgm = PlayingSoundInstance.Create();
        using var chip = PlayingSoundInstance.Create();
        var bgmInstance = bgm.Instance;
        var chipInstance = chip.Instance;
        var session = InstallSessionWithLiveAudio(stage, bgmInstance, chipInstance);
        StageTransitionPayload? capturedPayload = null;
        var bgmStoppedAtTransition = false;
        var chipStoppedAtTransition = false;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) =>
            {
                capturedPayload = payload;
                // Result transitions are instant: tracked BGM/chip audio must
                // already be stopped when ChangeStage fires, not merely
                // disposed later in CleanupComponents.
                bgmStoppedAtTransition = bgmInstance.State == SoundState.Stopped;
                chipStoppedAtTransition = chipInstance.State == SoundState.Stopped;
            });
        stage.StageManager = stageManager.Object;

        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        Assert.True(session.IsPlaying);
        Assert.Equal(SoundState.Playing, bgmInstance.State);
        Assert.Equal(SoundState.Playing, chipInstance.State);

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        Assert.True(bgmStoppedAtTransition);
        Assert.True(chipStoppedAtTransition);
        Assert.False(bgmInstance.IsDisposed);
        Assert.False(chipInstance.IsDisposed);
        Assert.False(session.IsPlaying);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.True(summary.ClearFlag);
        Assert.Equal(chartManager.TotalNotes, summary.TotalNotes);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    [Fact]
    public void FinalizePerformance_WhenPlayerFailed_ShouldSetClearFlagFalse()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        CreateActiveJudgement(stage);
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.PlayerFailed);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
    }

    [Fact]
    public void CheckStageCompletion_WhenStageAlreadyCompleted_ShouldNotTransitionAgain()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("already-done.dtx") { DurationMs = 1000.0 });

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void DrawGameplayState_WhenLoading_ShouldDrawLoadingText()
    {
        var stage = CreateStage();
        Rectangle? actualRectangle = null;
        Color? actualColor = null;
        float? actualDepth = null;

        ReflectionHelpers.SetPrivateField(stage, "_isLoading", true);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((rectangle, color, depth) =>
            {
                actualRectangle = rectangle;
                actualColor = color;
                actualDepth = depth;
            }));

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawGameplayState");

        Assert.NotNull(actualRectangle);
        Assert.Equal(Color.White, actualColor);
        Assert.Equal(0.1f, actualDepth);
    }

    [Fact]
    public void DrawGameplayState_WhenReadyWithCountdown_ShouldDrawPulsingReadyText()
    {
        var stage = CreateStage();
        Rectangle? actualRectangle = null;
        Color? actualColor = null;
        float? actualDepth = null;

        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.5);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 0.125);
        ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((rectangle, color, depth) =>
            {
                actualRectangle = rectangle;
                actualColor = color;
                actualDepth = depth;
            }));

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawGameplayState");

        Assert.NotNull(actualRectangle);
        Assert.Equal(0.1f, actualDepth);
    }

    [Fact]
    public void DrawGameplayState_WhenNotLoadingAndNotReady_ShouldNotDraw()
    {
        var stage = CreateStage();
        var fallbackInvoked = false;

        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((_, _, _) => fallbackInvoked = true));

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawGameplayState");

        Assert.False(fallbackInvoked);
    }

    [Fact]
    public void Constructor_WithIStageGame_ShouldInitializeCoreSystems()
    {
        // Covers the refactored constructor signature (IStageGame instead of BaseGame).
        // Uses a test subclass that overrides CreateSpriteBatch to avoid constructing a real
        // SpriteBatch (whose GraphicsResource finalizers crash on an uninitialized GraphicsDevice).
        var mockGame = new Mock<IStageGame>();
        mockGame.SetupGet(g => g.GraphicsDevice).Returns((GraphicsDevice?)null);

        var stage = new HeadlessPerformanceStage(mockGame.Object);

        Assert.Null(ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
        Assert.NotNull(ReflectionHelpers.GetPrivateField<UIManager>(stage, "_uiManager"));
    }

    [Fact]
    public void PlayMetronomeClick_WithMeasureStartMarker_ShouldSelectAccentSound()
    {
        var accentSound = new Mock<ISound>();
        var beatSound = new Mock<ISound>();
        var session = CreateMetronomeSession(
            NullLogger<PerformanceStage>.Instance,
            beatSound: beatSound.Object,
            accentSound: accentSound.Object);

        var exception = Record.Exception(() =>
            session.PlayMetronomeClick(
                new BeatMarker { TimeMs = 0.0, IsMeasureStart = true }));

        Assert.Null(exception);
        accentSound.VerifyGet(s => s.SoundEffect, Times.Once);
        beatSound.VerifyGet(s => s.SoundEffect, Times.Never);
    }

    [Fact]
    public void PlayMetronomeClick_WithBeatMarker_ShouldSelectBeatSound()
    {
        var accentSound = new Mock<ISound>();
        var beatSound = new Mock<ISound>();
        var session = CreateMetronomeSession(
            NullLogger<PerformanceStage>.Instance,
            beatSound: beatSound.Object,
            accentSound: accentSound.Object);

        var exception = Record.Exception(() =>
            session.PlayMetronomeClick(
                new BeatMarker { TimeMs = 500.0, IsMeasureStart = false }));

        Assert.Null(exception);
        beatSound.VerifyGet(s => s.SoundEffect, Times.Once);
        accentSound.VerifyGet(s => s.SoundEffect, Times.Never);
    }

    [Fact]
    public void PlayMetronomeClick_WhenSoundEffectAccessThrows_ShouldSwallowException()
    {
        var beatSound = new Mock<ISound>();
        beatSound.SetupGet(s => s.SoundEffect)
            .Throws(new InvalidOperationException("sound device lost"));
        var session = CreateMetronomeSession(
            NullLogger<PerformanceStage>.Instance,
            beatSound: beatSound.Object);

        var exception = Record.Exception(() =>
            session.PlayMetronomeClick(
                new BeatMarker { TimeMs = 500.0, IsMeasureStart = false }));

        Assert.Null(exception);
        beatSound.VerifyGet(s => s.SoundEffect, Times.AtLeastOnce);
    }

    [Fact]
    public void OnActivate_WhenMetronomeEnabled_ShouldLoadMetronomeSounds()
    {
        var resources = new Mock<IResourceManager>();
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData { Metronome = true }, resources: resources);
        using var stage = new HeadlessPerformanceStage(game);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, new ParsedChart("metronome.dtx")));
        UpdateUntil(stage, () => stage.AudioSession != null);

        resources.Verify(r => r.TryLoadSound(SoundPath.MetronomeBeat, out It.Ref<ISound>.IsAny), Times.Once);
        resources.Verify(r => r.TryLoadSound(SoundPath.MetronomeAccent, out It.Ref<ISound>.IsAny), Times.Once);
    }

    [Fact]
    public void Activation_FreezesFullAutoPlayBeforeChartPublication()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.UnionWith(Enumerable.Range(0, PerformanceUILayout.LaneCount));
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = new ParsedChart("before-chart-publication.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);
        Assert.False(telemetry.AutoPlayEnabled);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, chart));
        config.AutoPlayLanes.Clear();
        stage.PopulateTelemetry(telemetry);

        Assert.True(telemetry.AutoPlayEnabled);
        Assert.False(telemetry.PerformanceReady);
        Assert.Equal(0, telemetry.TotalNotes);
        Assert.Null(stage.AudioSession);
        stage.Deactivate();
    }

    [Fact]
    public void Activation_FrozenLanePolicyWorksBeforeAndAfterChartPublication()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = new ParsedChart("loading-lanes.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 100 });
        input.ModularInputManager.KeyBindings.BindButton("Test.Auto", 0);
        input.ModularInputManager.KeyBindings.BindButton("Test.Manual", 1);

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, chart));
        config.AutoPlayLanes.Clear();
        config.AutoPlayLanes.Add(1);
        var loading = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(loading);
        Assert.False(loading.PerformanceReady);
        Assert.Equal(0, loading.TotalNotes);
        Assert.Null(stage.AudioSession);

        Assert.True(input.ModularInputManager.InjectButton("Test.Auto", true));
        input.ModularInputManager.Update();
        Assert.True(input.ModularInputManager.InjectButton("Test.Manual", true));
        input.ModularInputManager.Update();
        stage.PopulateTelemetry(loading);
        Assert.Null(loading.LastLaneHitLane);
        Assert.True(input.ModularInputManager.InjectButton("Test.Auto", false));
        Assert.True(input.ModularInputManager.InjectButton("Test.Manual", false));
        input.ModularInputManager.Update();

        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Auto", true));
        input.ModularInputManager.Update();
        var gameplay = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(gameplay);
        Assert.Null(gameplay.LastLaneHitLane);
        Assert.True(input.ModularInputManager.InjectButton("Test.Manual", true));
        input.ModularInputManager.Update();
        stage.PopulateTelemetry(gameplay);
        Assert.Equal(1, gameplay.LastLaneHitLane);
        stage.Update(0.1);
        stage.PopulateTelemetry(gameplay);
        Assert.Equal(2, gameplay.PerfectCount);
        Assert.Equal(1000000, gameplay.Score);
        stage.Deactivate();
    }

    [Fact]
    public void Activation_FreezesAutomatedLanesAndReactivationResetsAutoplayCursor()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var firstChart = new ParsedChart("first-run.dtx") { DurationMs = 10000 };
        firstChart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        var secondChart = new ParsedChart("second-run.dtx") { DurationMs = 10000 };
        secondChart.Notes.Add(new Note { Id = 0, LaneIndex = 1, TimeMs = 100 });

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "First" }, 0, firstChart));
        config.AutoPlayLanes.Clear();
        config.AutoPlayLanes.Add(1);
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(0.1);
        var first = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(first);
        Assert.Equal(1, first.PerfectCount);
        Assert.Equal(1000000, first.Score);
        stage.Deactivate();

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Second" }, 0, secondChart));
        config.AutoPlayLanes.Clear();
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(0.1);
        var second = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(second);
        Assert.Equal(1, second.PerfectCount);
        Assert.Equal(1000000, second.Score);
        stage.Deactivate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Activation_FrozenGaugePolicySurvivesConfigRemovalDuringChartLoading(bool noFail)
    {
        var config = new ConfigData
        {
            AutoAddGauge = false,
            DamageLevel = GaugeDamageLevel.High,
            Risky = 1,
            NoFail = noFail
        };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(manager => manager.Config).Returns(config);
        bool configAvailable = true;
        var game = new Mock<IStageGame>();
        game.SetupGet(g => g.ConfigManager).Returns(() => configAvailable ? configManager.Object : null!);
        game.SetupGet(g => g.InputManager).Returns(input);
        game.SetupGet(g => g.ResourceManager).Returns(new Mock<IResourceManager>().Object);
        using var stage = new HeadlessPerformanceStage(game.Object);
        var chart = new ParsedChart("frozen-policy.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 1000 });

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Frozen" }, 0, chart));
        config.AutoPlayLanes.Clear();
        config.AutoPlayLanes.Add(1);
        config.AutoAddGauge = true;
        config.DamageLevel = GaugeDamageLevel.Low;
        config.Risky = 0;
        config.NoFail = !noFail;
        configAvailable = false;
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(0.1);
        var afterAutoHit = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(afterAutoHit);
        Assert.Equal(1, afterAutoHit.PerfectCount);
        Assert.Equal(50f, afterAutoHit.Gauge);

        stage.Update(1.2);
        var afterMiss = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(afterMiss);
        Assert.Equal(1, afterMiss.PerfectCount);
        Assert.Equal(1, afterMiss.MissCount);
        Assert.Equal(45.5f, afterMiss.Gauge);
        Assert.Equal(!noFail, afterMiss.HasFailed);
        Assert.Equal(!noFail, afterMiss.StageCompleted);
        stage.Deactivate();
    }

    [Fact]
    public void RiskyFailure_SummaryIncludesFailingMissAndEarlierAutoHit()
    {
        var config = new ConfigData { Risky = 1 };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var manager = new Mock<IStageManager>();
        PerformanceResult? result = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => result = (PerformanceResult)payload);
        stage.StageManager = manager.Object;
        var chart = new ParsedChart("failure-summary.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 1000 });

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(0.1);
        stage.Update(1.2);

        Assert.NotNull(result);
        Assert.Equal(CompletionReason.PlayerFailed, result.Summary.CompletionReason);
        Assert.False(result.Summary.ClearFlag);
        Assert.True(result.Summary.UsedAutoPlay);
        Assert.Equal(500000, result.Summary.Score);
        Assert.Equal(1, result.Summary.MaxCombo);
        Assert.Equal(1, result.Summary.PerfectCount);
        Assert.Equal(1, result.Summary.MissCount);
        Assert.Equal(2, result.Summary.TotalNotes);
        Assert.Equal(SongScore.CalculatePlayingSkill(2, 1, 0, 1), result.Summary.PlayingSkill);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
        stage.Deactivate();
    }

    [Fact]
    public void RiskyFailure_PoorHitIsCountedInReentrantResultAndTelemetry()
    {
        var config = new ConfigData { Risky = 1 };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var manager = new Mock<IStageManager>();
        PerformanceResult? result = null;
        GameTelemetrySnapshot? telemetryAtTransition = null;
        int? skillPoorAtTransition = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) =>
            {
                result = (PerformanceResult)payload;
                telemetryAtTransition = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(telemetryAtTransition);
                skillPoorAtTransition = stage.Rules!.SkillManager!.PoorCount;
            });
        stage.StageManager = manager.Object;
        var chart = new ParsedChart("poor-failure.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 1000 });

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(0.1);
        input.ModularInputManager.KeyBindings.BindButton("Test.Lane1", 1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Lane1", true));
        input.ModularInputManager.Update();
        stage.Update(1.02);

        Assert.NotNull(result);
        Assert.Equal(CompletionReason.PlayerFailed, result.Summary.CompletionReason);
        Assert.Equal(500000, result.Summary.Score);
        Assert.Equal(1, result.Summary.MaxCombo);
        Assert.Equal(1, result.Summary.PerfectCount);
        Assert.Equal(1, result.Summary.PoorCount);
        Assert.Equal(0, result.Summary.MissCount);
        Assert.Equal(SongScore.CalculatePlayingSkill(2, 1, 0, 1), result.Summary.PlayingSkill);
        Assert.NotNull(telemetryAtTransition);
        Assert.Equal(1, telemetryAtTransition.PerfectCount);
        Assert.Equal(1, telemetryAtTransition.PoorCount);
        Assert.Equal(0, telemetryAtTransition.MissCount);
        Assert.Equal(0, telemetryAtTransition.CurrentCombo);
        Assert.True(telemetryAtTransition.HasFailed);
        Assert.Equal(0, skillPoorAtTransition);
        Assert.Equal(1, stage.Rules!.SkillManager!.PoorCount);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
        stage.Deactivate();
    }

    [Fact]
    public void RiskyFailure_LateManualMissIsAbsentFromResultAndTelemetryMissCounts()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData { Risky = 1 }, input));
        var manager = new Mock<IStageManager>();
        PerformanceResult? result = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => result = (PerformanceResult)payload);
        stage.StageManager = manager.Object;
        var chart = new ParsedChart("late-manual-miss.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 1000 });

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        input.ModularInputManager.KeyBindings.BindButton("Test.Lane0", 0);
        Assert.True(input.ModularInputManager.InjectButton("Test.Lane0", true));
        input.ModularInputManager.Update();
        stage.Update(1.16);

        Assert.NotNull(result);
        Assert.Equal(CompletionReason.PlayerFailed, result.Summary.CompletionReason);
        Assert.Equal(0, result.Summary.MissCount);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.StageCompleted);
        Assert.True(telemetry.HasFailed);
        Assert.Equal(0, telemetry.MissCount);
        stage.Deactivate();
    }

    /// <summary>
    /// Test-only <see cref="PerformanceStage"/> that overrides <see cref="PerformanceStage.CreateSpriteBatch"/>
    /// to return null, so the constructor can be exercised without a live GraphicsDevice.
    /// </summary>
    private sealed class HeadlessPerformanceStage : PerformanceStage
    {
        public HeadlessPerformanceStage(IStageGame game) : base(game) { }

        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
        protected override IChartVideoPlayer CreateChartVideoPlayer() =>
            throw new InvalidOperationException("Headless activation must not create a video player.");
    }

    private static IStageGame CreateHeadlessGame(
        ILogger logger, ConfigData config, MockInputManagerCompat? input = null,
        Mock<IResourceManager>? resources = null)
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(manager => manager.Config).Returns(config);
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(factory => factory.CreateLogger(It.IsAny<string>())).Returns(logger);
        var game = new Mock<IStageGame>();
        game.SetupGet(g => g.ConfigManager).Returns(configManager.Object);
        game.SetupGet(g => g.ResourceManager).Returns((resources ?? new Mock<IResourceManager>()).Object);
        game.SetupGet(g => g.InputManager).Returns(input!);
        game.SetupGet(g => g.LoggerFactory).Returns(loggerFactory.Object);
        return game.Object;
    }

    private static void UpdateUntil(PerformanceStage stage, Func<bool> condition)
    {
        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.Update(0);
            return condition();
        }, TimeSpan.FromSeconds(5)), "stage did not reach the expected state");
    }

    private sealed class StageTestLogger : ILogger
    {
        public ConcurrentQueue<(string Message, Exception? Exception)> Errors { get; } = new();
        public Action<string, Exception?>? OnError { get; set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Error)
                return;
            var message = formatter(state, exception);
            Errors.Enqueue((message, exception));
            OnError?.Invoke(message, exception);
        }
    }

    private sealed class ScrollSpeedInputManagerCompat : MockInputManagerCompat
    {
        private readonly InputCommandType _activeCommand;

        public ScrollSpeedInputManagerCompat(InputCommandType activeCommand)
        {
            _activeCommand = activeCommand;
        }

        public override bool IsCommandPressed(InputCommandType command)
        {
            return command == _activeCommand;
        }

        public override bool IsBackActionTriggered()
        {
            return false;
        }
    }

    [Fact]
    public void OnSkillChanged_WithSkillPanelDisplay_ShouldUpdateSkillAndShowMax()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var panel = CreateSkillPanelDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", panel);
        ReflectionHelpers.SetPrivateField(stage, "_skillMeterDisplay", null);

        var hit = new JudgementEvent(0, 0, 0, JudgementType.Perfect);
        stage.Rules!.ComboManager!.ProcessJudgement(hit);
        stage.Rules.SkillManager!.ProcessJudgement(hit);
        Assert.Equal(100.0, panel.Skill);
        Assert.True(panel.ShowMax);

        var args = new SkillChangedEventArgs
        {
            CurrentSkill = 85.5,
            IsMax = true,
            PreviousSkill = 80.0
        };
        ReflectionHelpers.InvokePrivateMethod(stage, "OnSkillChanged", null, args);

        Assert.Equal(85.5, panel.Skill);
        Assert.True(panel.ShowMax);
    }

    [Fact]
    public void OnSkillChanged_WithSkillMeterDisplay_ShouldUpdateSkill()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var meter = CreateSkillMeterDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", null);
        ReflectionHelpers.SetPrivateField(stage, "_skillMeterDisplay", meter);

        var hit = new JudgementEvent(0, 0, 0, JudgementType.Perfect);
        stage.Rules!.ComboManager!.ProcessJudgement(hit);
        stage.Rules.SkillManager!.ProcessJudgement(hit);
        Assert.Equal(100.0, meter.Skill);

        var args = new SkillChangedEventArgs
        {
            CurrentSkill = 72.3,
            IsMax = false,
            PreviousSkill = 70.0
        };
        ReflectionHelpers.InvokePrivateMethod(stage, "OnSkillChanged", null, args);

        Assert.Equal(72.3, meter.Skill);
    }

    [Fact]
    public void OnSkillChanged_WithNullDisplays_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", null);
        ReflectionHelpers.SetPrivateField(stage, "_skillMeterDisplay", null);

        var args = new SkillChangedEventArgs
        {
            CurrentSkill = 50.0,
            IsMax = false,
            PreviousSkill = 45.0
        };
        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "OnSkillChanged", null, args));

        Assert.Null(exception);
    }

    [Fact]
    public void OnJudgementMade_WithHit_ShouldSpawnNxAttackAndSpriteJudgementText()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var attackManager = CreateNxAttackEffectManager();
        var spriteTextManager = SpriteJudgementTextPopupManager.CreateForTesting(CreateTexture(width: 448, height: 256));
        var gaugeManager = stage.Rules.GaugeManager!;
        var skillManager = stage.Rules.SkillManager!;
        var callbacks = new List<string>();
        stage.Rules.ScoreManager!.ScoreChanged += (_, _) => callbacks.Add("score");
        stage.Rules.ComboManager!.ComboChanged += (_, _) => callbacks.Add("combo");
        gaugeManager.GaugeChanged += (_, _) => callbacks.Add("gauge");
        skillManager.SkillChanged += (_, _) => callbacks.Add("skill");

        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", null);
        ReflectionHelpers.SetPrivateField(stage, "_nxAttackEffectManager", attackManager);
        ReflectionHelpers.SetPrivateField(stage, "_spriteJudgementTextPopupManager", spriteTextManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null,
            new JudgementEvent(0, 0, 0.0, JudgementType.Perfect));

#if DEBUG
        Assert.Equal(1, attackManager.SpawnCallCountForTesting);
#endif
        Assert.Equal(1, spriteTextManager.ActivePopupCount);
        Assert.Equal(new[] { "score", "combo", "gauge", "skill" }, callbacks);
        Assert.Equal(SongScore.CalculatePlayingSkill(1, 1, 0, 1), skillManager.CurrentSkill);
    }

    [Fact]
    public void OnJudgementMade_WithMiss_ShouldSkipNxAttackAndShowSpriteJudgementText()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        var attackManager = CreateNxAttackEffectManager();
        var spriteTextManager = SpriteJudgementTextPopupManager.CreateForTesting(CreateTexture(width: 448, height: 256));

        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", null);
        ReflectionHelpers.SetPrivateField(stage, "_nxAttackEffectManager", attackManager);
        ReflectionHelpers.SetPrivateField(stage, "_spriteJudgementTextPopupManager", spriteTextManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null,
            new JudgementEvent(0, 0, 200.0, JudgementType.Miss));

#if DEBUG
        Assert.Equal(0, attackManager.SpawnCallCountForTesting);
#endif
        Assert.Equal(1, spriteTextManager.ActivePopupCount);
    }

    [Fact]
    public void OnPlayerFailed_NoFailDisabled_ShouldFinalizePerformance()
    {
        var configData = new ConfigData { NoFail = false };
        using var stage = CreateStageWithPolicy(configData);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        CreateActiveJudgement(stage);
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var args = new FailureEventArgs { FinalLife = 0.5f, JudgementType = JudgementType.Miss };
        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, args);

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Once);
    }

    [Fact]
    public void OnPlayerFailed_NoFailEnabled_ShouldFinalizeWithoutReadingConfig()
    {
        var configData = new ConfigData { NoFail = true };
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(configData);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var args = new FailureEventArgs { FinalLife = 0.5f, JudgementType = JudgementType.Miss };
        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, args);

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Once);
        configManager.VerifyGet(x => x.Config, Times.Never);
    }

    [Fact]
    public void OnPlayerFailed_AlreadyCompleted_ShouldNotFinalizeAgain()
    {
        var configData = new ConfigData { NoFail = false };
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(configData);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);

        var args = new FailureEventArgs { FinalLife = 0.5f, JudgementType = JudgementType.Miss };
        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, args);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Never);
    }

    [Fact]
    public void FinalizePerformance_SongComplete_ShouldBuildSummaryWithClearFlag()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        InstallBoundSession(stage).Dispose();

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.True(summary.ClearFlag);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
    }

    [Fact]
    public void FinalizePerformance_WithSkillManager_ShouldCalculateGameSkill()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        stage.Rules!.InitializeScoreAndCombo();
        var comboManager = stage.Rules.ComboManager!;
        stage.Rules.InitializeGaugeAndSkill();
        var skillManager = stage.Rules.SkillManager!;
        CreateActiveJudgement(stage);
        var hit = new JudgementEvent(0, 0, 0, JudgementType.Perfect);
        comboManager.ProcessJudgement(hit);
        skillManager.ProcessJudgement(hit);

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(100.0, skillManager.CurrentSkill);
        Assert.Equal(skillManager.CurrentSkill, summary.PlayingSkill);
        var expectedGameSkill = SongScore.CalculateGameSkill(skillManager.CurrentSkill, 0, 0);
        Assert.Equal(expectedGameSkill, summary.GameSkill);
    }

    [Theory]
    [InlineData(false, 52.0f)]
    [InlineData(true, 54.0f)]
    public void UpdateGameplayManagers_MixedAutoPlay_ShouldApplyGaugeByFrozenLanePolicy(
        bool autoAddGauge,
        float expectedFinalLife)
    {
        var config = new ConfigData { AutoAddGauge = autoAddGauge };
        config.AutoPlayLanes.Add(1);
        using var stage = CreateStageWithPolicy(config);
        config.AutoAddGauge = !autoAddGauge;
        var inputManager = new MockInputManagerCompat();
        var chartManager = BindChart(stage, CreateMixedChart());
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayManagers");

        var judgementManager = stage.Rules!.JudgementManager;
        var gaugeManager = stage.Rules.GaugeManager;
        Assert.NotNull(judgementManager);
        Assert.NotNull(gaugeManager);
        judgementManager!.IsActive = true;

        inputManager.TriggerLaneHit(0);
        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 1000.0);
        Assert.Equal(52.0f, gaugeManager!.CurrentLife);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 2000.0, 2000.0);

        Assert.Equal(expectedFinalLife, gaugeManager.CurrentLife);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[1].Id)!.Status);
    }

    [Fact]
    public void TransitionToResultStage_ShouldIncludePerformanceSummary()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        StageTransitionPayload? capturedPayload = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => capturedPayload = payload);
        stage.StageManager = stageManager.Object;
        var summary = new PerformanceSummary
        {
            Score = 950000,
            MaxCombo = 200,
            ClearFlag = true,
            CompletionReason = CompletionReason.SongComplete
        };
        ReflectionHelpers.SetPrivateField(stage, "_performanceSummary", summary);
        var selectedSong = new SongListNode { Title = "TestSong" };
        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", selectedSong);
        ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 2);

        ReflectionHelpers.InvokePrivateMethod(stage, "TransitionToResultStage");

        stageManager.Verify(
            x => x.ChangeStage(StageType.Result, It.IsAny<InstantTransition>(), It.IsAny<StageTransitionPayload>()),
            Times.Once);
        Assert.NotNull(capturedPayload);
        var result = Assert.IsType<PerformanceResult>(capturedPayload);
        Assert.Same(summary, result.Summary);
        Assert.Same(selectedSong, result.Song);
        Assert.Equal(2, result.Difficulty);
    }

    [Fact]
    public void CleanupGameplayManagers_ShouldUnsubscribeEvents()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var rules = stage.Rules!;
        var score = Assert.IsType<ScoreManager>(rules.ScoreManager);
        var gauge = Assert.IsType<GaugeManager>(rules.GaugeManager);
        var skill = Assert.IsType<SkillManager>(rules.SkillManager);
        EventHandler<ScoreChangedEventArgs> observer = (_, _) => { };
        EventHandler<GaugeChangedEventArgs> gaugeObserver = (_, _) => { };
        EventHandler<FailureEventArgs> failureObserver = (_, _) => { };
        score.ScoreChanged += observer;
        gauge.GaugeChanged += gaugeObserver;
        gauge.Failed += failureObserver;
        var subscribers = ReflectionHelpers.GetPrivateField<EventHandler<ScoreChangedEventArgs>>(score, "ScoreChanged");
        Assert.Contains(subscribers!.GetInvocationList(), callback => callback.Target == stage);
        Assert.Contains(ReflectionHelpers.GetPrivateField<EventHandler<GaugeChangedEventArgs>>(gauge, "GaugeChanged")!.GetInvocationList(), callback => callback.Target == stage);
        Assert.Contains(ReflectionHelpers.GetPrivateField<EventHandler<FailureEventArgs>>(gauge, "Failed")!.GetInvocationList(), callback => callback.Target == stage);
        Assert.Contains(ReflectionHelpers.GetPrivateField<EventHandler<SkillChangedEventArgs>>(skill, "SkillChanged")!.GetInvocationList(), callback => callback.Target == stage);

        stage.Deactivate();

        Assert.Equal(observer, ReflectionHelpers.GetPrivateField<EventHandler<ScoreChangedEventArgs>>(score, "ScoreChanged"));
        Assert.Equal(gaugeObserver, ReflectionHelpers.GetPrivateField<EventHandler<GaugeChangedEventArgs>>(gauge, "GaugeChanged"));
        Assert.Equal(failureObserver, ReflectionHelpers.GetPrivateField<EventHandler<FailureEventArgs>>(gauge, "Failed"));
        Assert.Null(ReflectionHelpers.GetPrivateField<EventHandler<SkillChangedEventArgs>>(skill, "SkillChanged"));
        Assert.Null(rules.JudgementManager);
        Assert.Null(rules.ScoreManager);
        Assert.Null(rules.ComboManager);
        Assert.Null(rules.GaugeManager);
        Assert.Null(rules.SkillManager);
    }

    [Fact]
    public void CleanupGameplayManagers_ShouldDisposeManagers()
    {
        using var stage = CreateStageWithPolicy(new ConfigData());
        var chartManager = BindChart(stage, CreateSingleNoteChart());
        var judgementManager = CreateActiveJudgement(stage);
        stage.Rules!.InitializeScoreAndCombo();
        stage.Rules.InitializeGaugeAndSkill();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", new MockInputManagerCompat());

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupGameplayManagers");

        Assert.False(judgementManager.IsActive);
        Assert.Null(judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id));
    }

    [Fact]
    public void LogPerformanceError_WithException_ShouldLogErrorWithOriginalException()
    {
        var stage = CreateStage();
        var logger = new Mock<ILogger<PerformanceStage>>();
        ReflectionHelpers.SetPrivateField(stage, "_logger", logger.Object);

        var exception = new InvalidOperationException("test error");
        ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "TestMessage", exception);

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("TestMessage", StringComparison.Ordinal)),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogPerformanceError_WithoutException_ShouldLogErrorWithoutException()
    {
        var stage = CreateStage();
        var logger = new Mock<ILogger<PerformanceStage>>();
        ReflectionHelpers.SetPrivateField(stage, "_logger", logger.Object);

        ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "SimpleMessage", null);

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("SimpleMessage", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void DrawCenteredText_WithReadyFont_ShouldMeasureAndDrawAtCenteredPosition()
    {
        var stage = CreateStage();
        var spriteBatch = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
        GC.SuppressFinalize(spriteBatch);
        var font = new Mock<IFont>();
        font.Setup(f => f.MeasureString("READY")).Returns(new Vector2(100, 20));

        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
        ReflectionHelpers.SetPrivateField(stage, "_readyFont", font.Object);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawCenteredText", "READY", Color.White);

        font.Verify(f => f.MeasureString("READY"), Times.Once);
        font.Verify(f => f.DrawString(
            spriteBatch,
            "READY",
            new Vector2((PerformanceUILayout.ScreenWidth / 2) - 50, (PerformanceUILayout.ScreenHeight / 2) - 10),
            Color.White,
            0f,
            Vector2.Zero,
            Vector2.One,
            SpriteEffects.None,
            0.1f), Times.Once);
    }

    [Fact]
    public void DrawCenteredText_WithoutReadyFont_ShouldDrawFallbackRectangle()
    {
        var stage = CreateStage();
        Rectangle? capturedRect = null;
        Color? capturedColor = null;
        float? capturedDepth = null;

        ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
        ReflectionHelpers.SetPrivateField(stage, "_fallbackRectangleDrawer",
            (Action<Rectangle, Color, float>)((rect, color, depth) =>
            {
                capturedRect = rect;
                capturedColor = color;
                capturedDepth = depth;
            }));

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawCenteredText", "READY", Color.Cyan);

        Assert.Equal(new Rectangle(610, 350, 60, 20), capturedRect);
        Assert.Equal(Color.Cyan, capturedColor);
        Assert.Equal(0.1f, capturedDepth);
    }

    [Fact]
    public void StartSong_WhenCurrentGameTimeIsNull_ShouldLeaveReadyStateUnchanged()
    {
        var stage = CreateStage();
        InstallBoundSession(stage);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", null);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void ExtractSharedData_WithParsedChart_ShouldSetParsedChart()
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("shared-test.dtx") { Bpm = 140.0 };
        ReflectionHelpers.SetPrivateField(stage, "_transitionPayload", new SongPlayRequest(new SongListNode { Title = "Song" }, 2, parsedChart));

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Same(parsedChart, ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
        Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
    }

    [Fact]
    public void OnScrollSpeedChanged_ShouldSetScrollSpeedAndShowIndicator()
    {
        var stage = CreateStage();
        var noteRenderer = CreateNoteRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        var indicator = new ScrollSpeedIndicator(null);
        ReflectionHelpers.SetPrivateField(stage, "_scrollSpeedIndicator", indicator);

        var args = new ScrollSpeedChangedEventArgs(50, 75);
        ReflectionHelpers.InvokePrivateMethod(stage, "OnScrollSpeedChanged", null, args);

        Assert.Equal("Scroll Speed " + ScrollSpeedRange.Format(75), indicator.Text);
    }

    [Fact]
    public void UpdateSongProgress_WithValidChart_ShouldClampProgress()
    {
        var stage = CreateStage();
        var chart = new ParsedChart("progress.dtx") { DurationMs = 2000.0 };
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_currentProgressValue", 0.0f);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSongProgress", 5000.0);

        Assert.Equal(1.0f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"));
    }

    [Fact]
    public void UpdateSongProgress_WithNegativeTime_ShouldClampToZero()
    {
        var stage = CreateStage();
        var chart = new ParsedChart("progress-neg.dtx") { DurationMs = 2000.0 };
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_currentProgressValue", 0.5f);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSongProgress", -500.0);

        Assert.Equal(0.0f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"));
    }



    private static ParsedChart CreateMixedChart()
    {
        var parsedChart = new ParsedChart("mixed-autoplay-gauge.dtx")
        {
            Bpm = 120.0
        };
        parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        parsedChart.AddNote(new Note(1, 0, 192, 0x12, "01"));
        parsedChart.FinalizeChart();
        return parsedChart;
    }


    private static SkillPanelDisplay CreateSkillPanelDisplay()
    {
        return (SkillPanelDisplay)FormatterServices.GetUninitializedObject(typeof(SkillPanelDisplay));
    }

    private static SkillMeterDisplay CreateSkillMeterDisplay()
    {
        return (SkillMeterDisplay)FormatterServices.GetUninitializedObject(typeof(SkillMeterDisplay));
    }

    private static ITexture CreateTexture(int width, int height)
    {
        return new MutableTexture
        {
            Width = width,
            Height = height
        };
    }
}
