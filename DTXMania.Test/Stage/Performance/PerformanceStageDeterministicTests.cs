using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class PerformanceStageDeterministicTests
{
    [Fact]
    public async Task HeadlessPerformanceStage_NonDefaultProfile_UsesIsolatedActualCacheFactory()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 75 }));
        using var other = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 75 }));
        Assert.NotEqual(stage.CacheRoot, other.CacheRoot);
        Directory.CreateDirectory(stage.CacheRoot);
        Directory.CreateDirectory(other.CacheRoot);
        var orphan = Path.Combine(stage.CacheRoot, "orphan.tmp-test");
        var otherSentinel = Path.Combine(other.CacheRoot, "orphan.tmp-test");
        File.WriteAllText(orphan, "owned");
        File.WriteAllText(otherSentinel, "other fixture");

        try
        {
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Isolated" }, 0,
                new ParsedChart("isolated.dtx")));
            Assert.NotNull(await stage.LastPreparationTask!.WaitAsync(TimeSpan.FromSeconds(5)));
            var cache = await stage.CacheCreated.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Path.GetFullPath(stage.CacheRoot), cache.CacheRoot);
            Assert.False(File.Exists(orphan));
            Assert.Equal("other fixture", File.ReadAllText(otherSentinel));
        }
        finally
        {
            stage.Dispose();
            other.Dispose();
            await stage.CacheCleanup!.WaitAsync(TimeSpan.FromSeconds(5));
            await other.CacheCleanup!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(Directory.Exists(stage.CacheRoot));
        Assert.False(Directory.Exists(other.CacheRoot));
    }

    [Fact]
    public async Task CacheCleanup_WhenStageCanceledDuringCacheOperation_LeavesRootForLateWrite()
    {
        var directory = CreateTempDirectory();
        using var stage = new GatedVariantStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 50 }));
        Task<PreparedAudioArtifact>? lateWaiter = null;
        try
        {
            var source = CreatePlaceholderSource(directory, "late-cache-source.wav");
            var chart = new ParsedChart("late-cache.dtx") { BackgroundAudioPath = source };
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Late cache" }, 0, chart));
            await stage.ProcessorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cache = await stage.CacheCreated.WaitAsync(TimeSpan.FromSeconds(5));
            var key = await AudioVariantKey.CreateAsync(source, new PlaybackModifiers(50, 0), CancellationToken.None);
            var duplicateFactoryCalled = false;
            lateWaiter = cache.GetOrCreateAsync(key, (_, _) =>
            {
                duplicateFactoryCalled = true;
                throw new InvalidOperationException("The late waiter should join the existing operation.");
            }, CancellationToken.None);
            Assert.False(lateWaiter.IsCompleted);

            stage.Deactivate();
            Assert.Null(Assert.Single(await stage.Workers.WaitAsync(TimeSpan.FromSeconds(5))));
            stage.Dispose();
            await stage.CacheCleanup!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Directory.Exists(stage.CacheRoot));
            Assert.False(lateWaiter.IsCompleted);

            stage.ReleaseProcessor.TrySetResult();
            var artifact = await lateWaiter.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(duplicateFactoryCalled);
            Assert.Equal(8, artifact.PcmByteLength);
            Assert.True(File.Exists(cache.GetArtifactPath(key)));
        }
        finally
        {
            stage.ReleaseProcessor.TrySetResult();
            stage.ReleaseAndDeleteSources(directory);
            stage.Dispose();
            await stage.CacheCleanup!.WaitAsync(TimeSpan.FromSeconds(5));
            // A joined waiter is the observable terminal for the detached cache write.
            if (lateWaiter != null)
            {
                await lateWaiter.WaitAsync(TimeSpan.FromSeconds(5));
                if (Directory.Exists(stage.CacheRoot))
                    Directory.Delete(stage.CacheRoot, recursive: true);
            }
        }
        Assert.False(Directory.Exists(stage.CacheRoot));
    }

    [Fact]
    public void ExtractSharedData_WhenPayloadMissing_ShouldLeaveStageDataUnchanged()
    {
        var logger = new StageTestLogger();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(logger, new ConfigData()));
        stage.Activate();

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Null(telemetry.SelectedSongTitle);
        Assert.Equal(0, telemetry.SelectedDifficulty);
        Assert.False(telemetry.PerformanceReady);
        Assert.Null(stage.Rules?.ChartManager);
        Assert.Single(logger.Errors, e => e.Message.Contains("No song was selected"));

        stage.Deactivate();
        var request = new SongPlayRequest(new SongListNode { Title = "Prior" }, 3, CreateSingleNoteChart());
        stage.Activate(request);
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Deactivate();
        stage.Activate();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal("Prior", telemetry.SelectedSongTitle);
        Assert.Equal(3, telemetry.SelectedDifficulty);
        Assert.False(telemetry.PerformanceReady);
        Assert.Single(logger.Errors, e => e.Message.Contains("The selected chart path is unavailable"));
    }

    [Fact]
    public void ExtractSharedData_WhenSongPlayRequestExists_ShouldAssignAllFields()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        var selectedSong = new SongListNode { Title = "Song" };
        var parsedChart = new ParsedChart("test.dtx");
        var note = new Note { Id = 1, LaneIndex = 0, TimeMs = 100 };
        parsedChart.Notes.Add(note);
        stage.Activate(new SongPlayRequest(selectedSong, 3, parsedChart));
        UpdateUntil(stage, () => stage.AudioSession != null);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal("Song", telemetry.SelectedSongTitle);
        Assert.Equal(3, telemetry.SelectedDifficulty);
        Assert.Same(note, Assert.Single(stage.Rules!.ChartManager!.AllNotes));
        Assert.True(telemetry.PerformanceReady);
    }

    [Fact]
    public void PopulateTelemetry_WhenManagersExist_ShouldExposePerformanceState()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.UnionWith(Enumerable.Range(0, PerformanceUILayout.LaneCount));
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = new ParsedChart("telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "E2E AutoPlay Smoke" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);

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
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_WhenAutoPlayIsPartial_ShouldReportAutoPlayDisabled()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.UnionWith(new[] { 0, 1 });
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = new ParsedChart("partial-autoplay-telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Partial" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.False(telemetry.AutoPlayEnabled);
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_WhenNotationStripReady_ShouldReportTabStyle()
    {
        using var input = new MockInputManagerCompat();
        var resources = CreateNotationResourceManager(completeBravura: true);
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab }, input, resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, CreateFinalizedTabChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), telemetry.NoteDisplayStyle);
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_WhenNotationStripMissing_ShouldReportLanesStyle()
    {
        using var input = new MockInputManagerCompat();
        var resources = CreateNotationResourceManager(completeBravura: false);
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab }, input, resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fallback" }, 0, CreateFinalizedTabChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        stage.Deactivate();
    }

    [Fact]
    public async Task PopulateTelemetry_WhenSongTimerMissing_ShouldReportPerformanceNotReady()
    {
        var directory = CreateTempDirectory();
        using var stage = new GatedVariantStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 50 }));
        try
        {
            var chart = new ParsedChart("telemetry.dtx");
            chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "timer.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, chart));
            await stage.ProcessorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var telemetry = new GameTelemetrySnapshot();
            UpdateUntil(stage, () =>
            {
                stage.PopulateTelemetry(telemetry);
                return telemetry.TotalNotes == 1;
            });
            Assert.Null(stage.AudioSession);
            Assert.False(telemetry.PerformanceReady);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public async Task PopulateTelemetry_WhenAudioPreparationInProgress_ShouldExposeFrozenProfileAndMetrics()
    {
        var directory = CreateTempDirectory();
        var config = new ConfigData { PlaySpeedPercent = 75, PitchSemitones = -3 };
        using var stage = new GatedVariantStage(CreateHeadlessGame(new StageTestLogger(), config));
        try
        {
            var chart = new ParsedChart("progress-telemetry.dtx");
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "audio.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Progress" }, 0, chart));
            await stage.ProcessorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var telemetry = new GameTelemetrySnapshot();
            UpdateUntil(stage, () =>
            {
                stage.PopulateTelemetry(telemetry);
                return telemetry.AudioPreparationTotal == 1;
            });
            config.PlaySpeedPercent = 100;
            config.PitchSemitones = 0;
            Assert.Equal(75, telemetry.PlaySpeedPercent);
            Assert.Equal(-3, telemetry.PitchSemitones);
            Assert.True(telemetry.PlaybackProfileFrozen);
            Assert.Equal(0, telemetry.AudioPreparationCompleted);
            Assert.Equal(1, telemetry.AudioPreparationTotal);
            Assert.Equal(0, telemetry.AudioPreparationCacheHits);
            Assert.Equal(0L, telemetry.PreparedAudioBytes);
            stage.ReleaseProcessor.TrySetResult();
            UpdateUntil(stage, () => stage.AudioSession != null);
            stage.PopulateTelemetry(telemetry);
            Assert.Equal(1, telemetry.AudioPreparationCompleted);
            Assert.Equal(1, telemetry.AudioPreparationTotal);
            Assert.Equal(0, telemetry.AudioPreparationCacheHits);
            Assert.Equal(8L, telemetry.PreparedAudioBytes);
            Assert.Equal(75, telemetry.PlaySpeedPercent);
            Assert.Equal(-3, telemetry.PitchSemitones);
            stage.Deactivate();

            config.PlaySpeedPercent = 75;
            config.PitchSemitones = -3;
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Cached" }, 0, chart));
            UpdateUntil(stage, () => stage.AudioSession != null);
            stage.PopulateTelemetry(telemetry);
            Assert.Equal(1, telemetry.AudioPreparationCompleted);
            Assert.Equal(1, telemetry.AudioPreparationCacheHits);
            Assert.Equal(8L, telemetry.PreparedAudioBytes);
            stage.Deactivate();
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void PopulateTelemetry_WhenAudioPreparationHasNotReported_ShouldExposeZeroMetrics()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, new ParsedChart("initial.dtx")));

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.Equal(0, telemetry.AudioPreparationCompleted);
        Assert.Equal(0, telemetry.AudioPreparationTotal);
        Assert.Equal(0, telemetry.AudioPreparationCacheHits);
        Assert.Equal(0L, telemetry.PreparedAudioBytes);
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_WhenChartManagerMissing_ShouldReportPerformanceNotReady()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, new ParsedChart("loading.dtx")));

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.False(telemetry.PerformanceReady);
        stage.Deactivate();
    }

    [Fact]
    public void StartGameplayInitialization_WhenChartHasNoBackgroundAudio_ShouldCreateSpeedAwareSilentSongTimer()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { PlaySpeedPercent = 50 };
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = CreateSingleNoteChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Silent" }, 0, chart));
        config.PlaySpeedPercent = 100;
        UpdateUntil(stage, () => stage.AudioSession != null);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);
        var session = stage.AudioSession!;
        Assert.NotNull(session.Timer);
        stage.Update(1.0);
        Assert.True(session.IsPlaying);
        Assert.True(stage.Rules!.JudgementManager!.IsActive);
        stage.Update(0.5);
        Assert.Equal(250.0, session.GetCurrentMs(new GameTime(TimeSpan.FromSeconds(1.5), TimeSpan.Zero)));
    }

    [Fact]
    public void StartGameplayInitialization_WhenAudioBindingThrowsAfterPreparation_ShouldFailLoadingAndKeepStageOwnedAudioForCleanup()
    {
        var directory = CreateTempDirectory();
        var logger = new StageTestLogger();
        var game = CreateHeadlessGame(logger, new ConfigData());
        Mock.Get(game).SetupGet(g => g.ResourceManager).Returns((IResourceManager)null!);
        using var stage = new HeadlessPerformanceStage(game);
        try
        {
            var chart = new ParsedChart("binding-fail.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            // A real loadable WAV makes the prepared set's disposal observable.
            chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(
                directory,
                "binding-fail-main.wav");

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Binding" }, 0, chart));
            UpdateUntil(stage, () => logger.Errors.Any(e => e.Message.Contains("Audio binding failed")));
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.False(telemetry.PerformanceReady);
            var error = Assert.Single(logger.Errors, e => e.Message.Contains("Audio binding failed"));
            Assert.IsType<ArgumentNullException>(error.Exception);
            var preparedSet = stage.AudioSession?.PreparedSet;
            Assert.NotNull(preparedSet);
            var mainBackground = preparedSet!.MainBackground;
            Assert.NotNull(mainBackground);
            Assert.False(mainBackground!.IsDisposed);

            stage.Deactivate();

            Assert.True(mainBackground.IsDisposed);
            Assert.Null(stage.AudioSession);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public async Task StartGameplayInitialization_WhenChartSetupThrowsBeforeAudioBinding_ShouldFailWithChartSetupErrorAndDisposePreparedAudio()
    {
        // A playfield publication failure precedes audio binding; the queued real
        // audio must be disposed rather than retained by a failed activation.
        var directory = CreateTempDirectory();
        using var input = new MockInputManagerCompat();
        var logger = new StageTestLogger();
        var playfield = new Mock<IPerformancePlayfield>();
        playfield.Setup(p => p.OnChartPublished(0, It.IsAny<int>()))
            .Throws(new ArgumentException("invalid BPM"));
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(logger, new ConfigData(), input))
        {
            Playfield = playfield.Object
        };
        try
        {
            var chart = new ParsedChart("bad-bpm.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.Bpm = 0.0;
            chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(
                directory,
                "bad-bpm-main.wav");

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Bad BPM" }, 0, chart));
            var task = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
            var preparedSet = Assert.IsType<PreparedGameplayAudioSet>(await task.WaitAsync(TimeSpan.FromSeconds(5)));
            var mainBackground = preparedSet.MainBackground;
            Assert.NotNull(mainBackground);
            Assert.False(mainBackground!.IsDisposed);
            stage.Update(0);

            Assert.True(
                mainBackground.IsDisposed,
                "the prepared set was not disposed after the chart-setup failure");
            Assert.Null(stage.AudioSession?.PreparedSet);
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.False(telemetry.PerformanceReady);
            Assert.Single(logger.Errors, error => error.Message.Contains("Chart setup failed")
                && error.Exception is ArgumentException);
            playfield.Verify(p => p.OnChartPublished(0, It.IsAny<int>()), Times.Once);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
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
        var manager = new Mock<IStageManager>();
        stage.StageManager = manager.Object;
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
        Assert.False(telemetry.PerformanceReady);
        Assert.False(telemetry.StageCompleted);
        Assert.Equal(1, telemetry.TotalNotes);
        Assert.Null(stage.AudioSession?.PreparedSet);
        stage.Update(10);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Never);
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
    public async Task StartGameplayInitialization_WhenResultQueuedBeforeDeactivation_ShouldDropItAndDisposeThePreparedSetExactlyOnce()
    {
        // The worker finished and its terminal sat in the scope queue when the stage
        // deactivated: BaseStage.Deactivate retires the scope synchronously BEFORE
        // OnDeactivate's teardown, so the stale never-applied result is dropped via
        // onDropped — its prepared audio set is disposed there, never applied.
        var directory = CreateTempDirectory();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        try
        {
            var chart = new ParsedChart("late-drop.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            // A real loadable WAV makes the prepared set's disposal observable even
            // though the result is never applied to the stage.
            chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, "late-drop-main.wav");

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Queued" }, 0, chart));
            var task = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
            var preparedSet = Assert.IsType<PreparedGameplayAudioSet>(await task.WaitAsync(TimeSpan.FromSeconds(5)));
            var mainBackground = preparedSet.MainBackground;
            Assert.NotNull(mainBackground);
            Assert.False(mainBackground!.IsDisposed);
            // The result is queued but never applied.
            Assert.Null(stage.AudioSession);

            stage.Deactivate();

            Assert.True(
                SpinWait.SpinUntil(() => mainBackground.IsDisposed, TimeSpan.FromSeconds(5)),
                "the dropped prepared set was not disposed");
            Assert.Null(stage.AudioSession);
            stage.Deactivate();
            Assert.True(mainBackground.IsDisposed);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public async Task StartGameplayInitialization_WhenPreparationIsCanceledMidFlight_ShouldLeaveLoadingStateUntouched()
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
        var logger = new BlockingLoadFailureLogger(gates: 1);
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(logger, new ConfigData()));
        Task<PreparedGameplayAudioSet?>? canceledRun = null;
        try
        {
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

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Canceled" }, 0, chart));
            canceledRun = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
            Assert.True(
                SpinWait.SpinUntil(() => logger.ParkedCount >= 1, TimeSpan.FromSeconds(5)),
                "the worker never parked inside its load-failure report");

            // Retirement cancels the scope token synchronously before OnDeactivate while
            // the worker is parked in test code: from this moment the operation's
            // fate (silent cancellation) is deterministic.
            stage.Deactivate();

            // The operation is still fully alive and parked: the loading state must
            // already be untouched — the strongest, race-free form of the invariant.
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.False(telemetry.PerformanceReady);
            Assert.Null(stage.AudioSession);
            Assert.False(canceledRun.IsCompleted);

            // Awaiting the released worker's task observes the full unwind — the
            // canceled chain's real terminal — before the replacement run starts.
            logger.ReleaseNext();
            Assert.Null(await canceledRun.WaitAsync(TimeSpan.FromSeconds(5)));

            var freshChart = new ParsedChart("canceled-fresh.dtx");
            freshChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            freshChart.FinalizeChart();
            freshChart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, "canceled-fresh-main.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fresh" }, 0, freshChart));
            var freshRun = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
            Assert.NotSame(canceledRun, freshRun);
            var completedFreshSet = Assert.IsType<PreparedGameplayAudioSet>(
                await freshRun.WaitAsync(TimeSpan.FromSeconds(5)));
            UpdateUntil(stage, () => stage.AudioSession != null);

            // Final unchanged-state assertions, after both terminals: the canceled
            // operation provably unwound before the fresh run started, and the
            // fresh run applied only its own audio.
            var freshSet = stage.AudioSession?.PreparedSet;
            Assert.NotNull(freshSet);
            Assert.Same(completedFreshSet, freshSet);
            Assert.NotNull(freshSet!.MainBackground);
            Assert.False(freshSet.MainBackground!.IsDisposed);
            Assert.DoesNotContain(logger.Errors, e => e.Message.Contains("Audio preparation failed"));
            // Both operations are terminally complete, so nothing can land anymore;
            // a few extra update frames plus a re-assert keep that property pinned.
            for (var i = 0; i < 8; i++)
            {
                stage.Update(0.016);
            }
            Assert.Same(freshSet, stage.AudioSession?.PreparedSet);
            stage.PopulateTelemetry(telemetry);
            Assert.True(telemetry.PerformanceReady);
            Assert.Equal("Fresh", telemetry.SelectedSongTitle);
            Assert.DoesNotContain(logger.Errors, e => e.Message.Contains("Audio preparation failed"));
            stage.Deactivate();
            Assert.True(freshSet.MainBackground.IsDisposed);
        }
        finally
        {
            logger.ReleaseAll();
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void StartGameplayInitialization_ShouldPublishAudioProgressThroughTheScopeAndApplyItBeforeReady()
    {
        // Audio preparation progress flows through a Latest reporter: the mid-flight
        // report applies on the update-thread drain before the terminal does, and the
        // final report lands with the ready state.
        var directory = CreateTempDirectory();
        using var stage = new GatedVariantStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 50 }));
        try
        {
            var chart = new ParsedChart("progress-prep.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "progress-main.wav");

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Progress" }, 0, chart));
            Assert.True(
                SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)),
                "the worker never reached the variant processor");

            var telemetry = new GameTelemetrySnapshot();
            UpdateUntil(stage, () =>
            {
                stage.PopulateTelemetry(telemetry);
                return telemetry.AudioPreparationTotal == 1;
            });
            Assert.Equal(0, telemetry.AudioPreparationCompleted);
            Assert.False(telemetry.PerformanceReady);
            Assert.NotNull(stage.Rules?.ChartManager);
            Assert.Null(stage.AudioSession);

            stage.ReleaseProcessor.TrySetResult();
            UpdateUntil(stage, () => stage.AudioSession != null);
            stage.PopulateTelemetry(telemetry);
            Assert.Equal(1, telemetry.AudioPreparationCompleted);
            Assert.Equal(1, telemetry.AudioPreparationTotal);
            Assert.True(telemetry.PerformanceReady);
            var preparedSet = stage.AudioSession?.PreparedSet;
            Assert.NotNull(preparedSet);
            var timer = stage.AudioSession?.Timer;
            Assert.NotNull(timer);
            Assert.Equal(preparedSet!.RuntimePitch, timer!.Pitch);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
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
        var factory = new CapturingLoggerFactory();
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData());
        Mock.Get(game).SetupGet(g => g.LoggerFactory).Returns(factory);
        using var stage = new HeadlessPerformanceStage(game);
        try
        {
            var chart = new ParsedChart("worker-log.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            // A referenced-but-missing source makes the default-profile preparation
            // report a load failure through the worker's logging callback.
            chart.BackgroundAudioPath = Path.Combine(directory, "missing-main.wav");

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Logging" }, 0, chart));

            Assert.True(
                SpinWait.SpinUntil(
                    () => factory.Errors.Any(entry =>
                        entry.Message.Contains("Gameplay audio load failed", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(5)),
                "the worker's sound-load failure was never logged");
            Assert.Equal(
                Environment.CurrentManagedThreadId,
                Assert.Single(factory.CreationThreadIds));
            var (_, exception) = factory.Errors.Single(entry =>
                entry.Message.Contains("Gameplay audio load failed", StringComparison.Ordinal));
            Assert.IsType<FileNotFoundException>(exception);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void Activation_WhenRetiredAudioFinishesLate_ShouldDropRealWavAndPreserveFreshRun()
    {
        var directory = CreateTempDirectory();
        var logger = new BlockingLoadFailureLogger(gates: 2);
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        playfield.Setup(p => p.OnChartPublished(0, It.IsAny<int>()))
            .Throws(new ArgumentException("stale chart must never be applied"));
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(logger, new ConfigData(), input))
        {
            Playfield = playfield.Object
        };
        try
        {
            ParsedChart Chart(string name, double bpm)
            {
                var chart = new ParsedChart(name + ".dtx");
                chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
                chart.AddBGMEvent(new BGMEvent { AudioFilePath = CreatePlaceholderSource(directory, name + "-bad.wav") });
                chart.FinalizeChart();
                chart.Bpm = bpm;
                chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, name + "-main.wav");
                return chart;
            }
            var staleChart = Chart("stale", 0);
            var freshChart = Chart("fresh", 120);
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Stale" }, 0, staleChart));
            var staleTask = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
            Assert.True(SpinWait.SpinUntil(() => logger.ParkedCount >= 1, TimeSpan.FromSeconds(5)));
            stage.Deactivate();
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fresh" }, 0, freshChart));
            var freshTask = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
            Assert.NotSame(staleTask, freshTask);
            Assert.True(SpinWait.SpinUntil(() => logger.ParkedCount >= 2, TimeSpan.FromSeconds(5)));
            UpdateUntil(stage, () => stage.Rules?.ChartManager != null);
            Assert.False(freshTask.IsCompleted);
            Assert.Null(stage.AudioSession);
            Assert.Same(freshChart.Notes[0], Assert.Single(stage.Rules!.ChartManager!.AllNotes));

            logger.ReleaseNext();
            Assert.True(staleTask.Wait(TimeSpan.FromSeconds(5)));
            var staleSet = Assert.IsType<PreparedGameplayAudioSet>(staleTask.Result);
            Assert.NotNull(staleSet.MainBackground);
            Assert.True(staleSet.MainBackground!.IsDisposed);
            Assert.False(freshTask.IsCompleted);
            Assert.Null(stage.AudioSession);
            playfield.Verify(p => p.OnChartPublished(0, It.IsAny<int>()), Times.Never);
            Assert.DoesNotContain(logger.Errors, e => e.Message.Contains("Chart setup failed"));

            logger.ReleaseNext();
            Assert.True(freshTask.Wait(TimeSpan.FromSeconds(5)));
            var freshSet = Assert.IsType<PreparedGameplayAudioSet>(freshTask.Result);
            Assert.NotNull(freshSet.MainBackground);
            Assert.False(freshSet.MainBackground!.IsDisposed);
            UpdateUntil(stage, () => stage.AudioSession != null);
            Assert.Same(freshSet, stage.AudioSession!.PreparedSet);
            Assert.Same(freshChart.Notes[0], Assert.Single(stage.Rules!.ChartManager!.AllNotes));
            for (var i = 0; i < 8; i++) stage.Update(0);
            Assert.Same(freshSet, stage.AudioSession.PreparedSet);
            Assert.DoesNotContain(logger.Errors, e => e.Message.Contains("Chart setup failed"));
            stage.Deactivate();
            Assert.True(freshSet.MainBackground.IsDisposed);
        }
        finally
        {
            logger.ReleaseAll();
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    #region Final-review regressions: scroll speed apply and pad feedback during preparation

    [Fact]
    public void PlayfieldUpdate_ShouldContinueDuringLoadingAndCountdownButGateGameplayEffects()
    {
        var directory = CreateTempDirectory();
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new GatedVariantStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 50 }, input))
        {
            Playfield = playfield.Object
        };
        try
        {
            var chart = new ParsedChart("visual-update.dtx") { DurationMs = 10000 };
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "visual-update.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Visual Update" }, 0, chart));
            Assert.True(SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)));

            stage.Update(0.1);
            playfield.Verify(p => p.Update(0.1, false), Times.Once);

            stage.ReleaseProcessor.TrySetResult();
            UpdateUntil(stage, () => stage.AudioSession != null);
            playfield.Invocations.Clear();
            stage.Update(0.3);
            stage.Update(0.8);
            stage.Update(0.1);

            playfield.Verify(p => p.Update(0.3, false), Times.Once);
            playfield.Verify(p => p.Update(0.8, false), Times.Once);
            playfield.Verify(p => p.Update(0.1, true), Times.Once);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenScrollSpeedChangesDuringPreparation_ShouldApplyCurrentConfigSpeedNotTheSnapshot()
    {
        var directory = CreateTempDirectory();
        var config = new ConfigData { ScrollSpeed = 100, PlaySpeedPercent = 50 };
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new GatedVariantStage(CreateHeadlessGame(new StageTestLogger(), config, input))
        {
            Playfield = playfield.Object
        };
        try
        {
            var chart = new ParsedChart("scroll-change.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "scroll-change-main.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Scroll" }, 0, chart));
            Assert.True(
                SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)),
                "the worker never reached the variant processor");
            config.ScrollSpeed = 200;
            stage.Update(0);
            playfield.Verify(x => x.OnChartPublished(chart.Bpm, 200), Times.Once);
            Assert.Null(stage.AudioSession);
            stage.ReleaseProcessor.TrySetResult();
            UpdateUntil(stage, () => stage.AudioSession != null);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void WireUpInputEventHandlers_WhenCalledRepeatedly_ShouldSubscribeExactlyOnce()
    {
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input))
        {
            Playfield = playfield.Object
        };
        input.ModularInputManager.KeyBindings.BindButton("Test.Pad", 3);
        var request = new SongPlayRequest(new SongListNode { Title = "Pad" }, 0, CreateSingleNoteChart());
        stage.Activate(request);
        Assert.True(input.ModularInputManager.InjectButton("Test.Pad", true));
        input.ModularInputManager.Update();
        playfield.Verify(p => p.OnLaneInput(3), Times.Once);
        Assert.True(input.ModularInputManager.InjectButton("Test.Pad", false));
        input.ModularInputManager.Update();
        UpdateUntil(stage, () => stage.AudioSession != null);
        playfield.Invocations.Clear();
        Assert.True(input.ModularInputManager.InjectButton("Test.Pad", true));
        input.ModularInputManager.Update();
        playfield.Verify(p => p.OnLaneInput(3), Times.Once);
        Assert.True(input.ModularInputManager.InjectButton("Test.Pad", false));
        input.ModularInputManager.Update();
        stage.Deactivate();
        stage.Activate(request);
        playfield.Invocations.Clear();
        Assert.True(input.ModularInputManager.InjectButton("Test.Pad", true));
        input.ModularInputManager.Update();
        playfield.Verify(p => p.OnLaneInput(3), Times.Once);
    }

    [Fact]
    public void StartGameplayInitialization_WhenPreparationFails_ShouldKeepPadFeedbackWired()
    {
        var logger = new StageTestLogger();
        using var inputManager = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        var config = new ConfigData { PlaySpeedPercent = 50, PitchSemitones = 3 };
        using var stage = new ThrowingAudioVariantStage(CreateHeadlessGame(logger, config, inputManager))
        {
            Playfield = playfield.Object
        };
        var chart = new ParsedChart("failed-prep-pads.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();
        inputManager.ModularInputManager.KeyBindings.BindButton("Test.Pad", 3);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Failure" }, 0, chart));
        UpdateUntil(stage, () => logger.Errors.Any(e => e.Message.Contains("Audio preparation failed")));
        Assert.True(inputManager.ModularInputManager.InjectButton("Test.Pad", true));
        inputManager.ModularInputManager.Update();
        playfield.Verify(x => x.OnLaneInput(3), Times.Once);
    }

    #endregion

    [Fact]
    public void LoadPerformanceUIAssets_WhenResourceManagerReturnsNull_ShouldLeaveAllTexturesNull()
    {
        // Each per-asset load is best-effort via TryLoadTexture; a missing skin must not
        // abort the whole load. This exercises every TryLoadTexture call site in the method.
        var resourceManager = new Mock<IResourceManager>();
        ITexture missingTexture = null;
        resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out missingTexture)).Returns(false);
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resourceManager));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Missing" }, 0, new ParsedChart("missing.dtx")));
        stage.LoadPerformanceUIAssets();
        stage.DrawBasePass(new Rectangle(0, 0, 1920, 1080));
        stage.DrawUiPass(null);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Assert.Single(stage.FallbackBackgrounds));
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
        var result = LanesPlayfield.LoadSkillPanelTexture(resourceManager.Object, theme);

        Assert.Same(defaultTexture.Object, result);
        // The fallback must be disposed (not merely RemoveReference'd) so the
        // underlying GPU Texture2D is released. RemoveReference alone leaves
        // the fallback alive with no owner, leaking one texture per activation.
        fallbackTexture.Verify(x => x.Dispose(), Times.Once);
        fallbackTexture.Verify(x => x.RemoveReference(), Times.Never);
        resourceManager.Verify(x => x.TryLoadTexture(TexturePath.SkillPanel, out It.Ref<ITexture>.IsAny), Times.Once);
    }

    [Fact]
    public void LoadSkillPanelTexture_WhenThemedOverrideEqualsDefaultPath_ShouldDisposeFallbackAndReturnNull()
    {
        // When the resolved override path is already the NX default and that
        // load fails (1x1 fallback), there is nothing left to retry. The
        // method returns null and must still dispose the fallback (the leak
        // applies equally to this branch — the fallback is uncached either
        // way) but must not attempt a redundant second load of the same path.
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
        var result = LanesPlayfield.LoadSkillPanelTexture(resourceManager.Object, theme);

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
        var result = LanesPlayfield.LoadSkillPanelTexture(resourceManager.Object, theme);

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
        using var input = new MockInputManagerCompat();
        var font = new Mock<IFont>();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), config, input, CreateNotationResourceManager(true)))
        {
            MakeHud = () => TabPlayfieldTests.CreateHud(font)
        };
        var chart = CreateTabFeedbackChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Frozen" }, 0, chart));

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

        UpdateUntil(stage, () => stage.AudioSession != null);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), telemetry.NoteDisplayStyle);
        var manager = stage.Rules!.JudgementManager!;
        manager.IsActive = true;
        manager.EnqueueLaneHit(5);
        manager.Update(chart.Notes.First(n => n.LaneIndex == 5).TimeMs - 12);
        Assert.Equal("-12", stage.LastHud!.PopupTimingForTesting.Text);
        stage.Update(0);
        Assert.Equal(1, stage.CurrentFrame.Combo);
        stage.DrawPlayfieldHud(null!);
        font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "COMBO", It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void VisualGates_BeforeFreeze_ShouldDefaultRequestedNoteDisplayStyleToLanes()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab }));
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
    }

    #region HPA-411 Tab mode integration

    [Fact]
    public void FreezeRunConfiguration_WhenTabRequested_ShouldFreezeStyleWhileEffectiveModeStaysLanesUntilStripPublish()
    {
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), config, input, CreateNotationResourceManager(true)));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, CreateFinalizedTabChart()));
        config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), telemetry.NoteDisplayStyle);
    }

    [Fact]
    public void StartGameplayInitialization_WhenLanesRequested_ShouldKeepLanesWithoutNotation()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData(), input, CreateNotationResourceManager(true)));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Lanes" }, 0, CreateFinalizedTabChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
    }

    [Fact]
    public void StartGameplayInitialization_WhenTabRequestedAndAudioStillPreparing_ShouldPublishChartAndNotationStripBeforeReady()
    {
        // Tab playfield (chart, ChartManager and NotationStripRenderer) must publish
        // ahead of audio preparation so LOADING renders over Tab instead of Lanes.
        var directory = CreateTempDirectory();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, PlaySpeedPercent = 50 };
        var resources = CreateNotationResourceManager(completeBravura: true);
        var logger = new StageTestLogger();
        using var input = new MockInputManagerCompat();
        var game = CreateHeadlessGame(logger, config, input, resources);
        using var stage = new GatedVariantStage(game);
        try
        {
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
            Assert.False(stage.ProcessorCompleted.Task.IsCompleted);
            Assert.DoesNotContain(logger.Errors, e => e.Message.Contains("failed", StringComparison.OrdinalIgnoreCase));
            Assert.False(telemetry.PerformanceReady);
            Assert.NotNull(stage.Rules?.ChartManager);
            var judgement = Assert.IsType<JudgementManager>(stage.Rules?.JudgementManager);
            Assert.False(judgement.IsActive);
            Assert.NotNull(judgement.GetNoteRuntimeData(chart.Notes[0].Id));
            Assert.Null(stage.AudioSession);
            input.ModularInputManager.KeyBindings.BindButton("Test.LoadingTab", 3);
            Assert.True(input.ModularInputManager.InjectButton("Test.LoadingTab", true));
            input.ModularInputManager.Update();
            Assert.Equal(1f, stage.LastHud!.FlashTimersForTesting[3]);
            Assert.Equal(PerformanceUILayout.GetLaneColor(3), stage.LastHud.FlashColorsForTesting[3]);

            // Release the audio gate (it returns empty bytes -> CreatePreparedSound throws
            // NoAudioHardwareException or succeeds cleanly; either way the stage exits loading
            // and never hangs).
            stage.ReleaseProcessor.TrySetResult();
            UpdateUntil(stage, () => stage.AudioSession != null ||
                logger.Errors.Any(e => e.Message.Contains("Audio preparation failed")));
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenTabAudioIsPreparing_ShouldForwardCurrentAndLiveScrollSpeed()
    {
        var directory = CreateTempDirectory();
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, PlaySpeedPercent = 50 };
        var game = CreateHeadlessGame(new StageTestLogger(), config, input);
        var manager = Mock.Get(game.ConfigManager);
        var lanes = new Mock<IPerformancePlayfield>();
        var tab = new Mock<IPerformancePlayfield>();
        tab.SetupGet(t => t.Mode).Returns(DrumsNoteDisplayStyle.Tab);
        using var stage = new GatedVariantStage(game)
        {
            Playfield = lanes.Object,
            TabFactory = () => tab.Object
        };
        try
        {
            var chart = CreateFinalizedTabChart();
            chart.Bpm = 150;
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "tab-scroll.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Scroll" }, 0, chart));
            Assert.True(SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)));
            config.ScrollSpeed = 200;
            manager.Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(100, 200));
            lanes.Verify(f => f.OnScrollSpeedChanged(200), Times.Once);
            UpdateUntil(stage, () =>
            {
                var telemetry = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(telemetry);
                return telemetry.NoteDisplayStyle == nameof(DrumsNoteDisplayStyle.Tab);
            });
            Assert.False(stage.ProcessorCompleted.Task.IsCompleted);
            Assert.Null(stage.AudioSession);
            tab.Verify(f => f.OnChartPublished(150, 200), Times.Once);
            manager.Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(200, 225));
            tab.Verify(f => f.OnScrollSpeedChanged(225), Times.Once);
            lanes.Verify(f => f.OnScrollSpeedChanged(225), Times.Never);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenAudioPreparationThrowsOperationCanceledWithoutRetirement_ShouldFailLoading()
    {
        // A non-retirement OperationCanceledException (e.g. a killed decoder/ffmpeg
        // process) must route to onError so _isLoading clears and the error screen shows,
        // instead of hanging on LOADING forever.
        var directory = CreateTempDirectory();
        var logger = new StageTestLogger();
        using var stage = new CancelingAudioVariantStage(CreateHeadlessGame(
            logger, new ConfigData { PlaySpeedPercent = 50 }));
        try
        {
            var chart = new ParsedChart("canceled-decoder.dtx");
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
            chart.FinalizeChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "canceled-decoder-main.wav");

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Canceled decoder" }, 0, chart));
            UpdateUntil(stage, () => logger.Errors.Any(e => e.Message.Contains("Audio preparation failed")));

            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.False(telemetry.PerformanceReady);
            Assert.NotNull(stage.Rules?.ChartManager);
            Assert.Null(stage.AudioSession);
            var error = Assert.Single(logger.Errors, e => e.Message.Contains("Audio preparation failed"));
            Assert.IsAssignableFrom<OperationCanceledException>(error.Exception);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void StartGameplayInitialization_WhenNotationBuildFails_ShouldStayInLanesAndRemainReady()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab }, input));
        var chart = new ParsedChart("tab-invalid-channel.dtx");
        chart.MeasureLines.Add(new MeasureLine { Bar = 0, TimeMs = 0 });
        chart.MeasureLines.Add(new MeasureLine { Bar = 1, TimeMs = 2000 });
        chart.Notes.Add(new Note { Channel = 0x99, Bar = 0, Tick = 0 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Bad notation" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
    }

    [Fact]
    public void TryConstructNotationStrip_WhenBravuraIsIncomplete_ShouldFallBackToLanesForTheSong()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab },
            input, CreateNotationResourceManager(false)));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Bad font" }, 0, CreateFinalizedTabChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
    }

    [Fact]
    public void TryConstructNotationStrip_WhenTabHudCannotAcquireResources_ShouldRollStripBackToLanes()
    {
        using var hud = TabPlayfieldTests.CreateUnreadyHud();
        var resources = TabPlayfieldTests.Resources();
        Assert.Null(TabPlayfieldTests.Create(resources, hud));
        Assert.False(hud.IsReady);
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
        return Directory.CreateTempSubdirectory("DTXMania_PerformanceStageTests_").FullName;
    }

    private static string CreatePlaceholderSource(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1 });
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Stage whose variant processor parks on a gate so tests can deterministically
    /// hold the worker mid-preparation (the progress coalescing case).
    /// The loading-cancellation test parks in the default profile instead: the
    /// variant cache can detach its underlying operation from the stage worker.
    /// The cache-cleanup regression uses this gate and joins that operation
    /// explicitly to observe its actual terminal.
    /// </summary>
    private sealed class GatedVariantStage : PerformanceStage
    {
        private readonly IsolatedStageAudioCache _cache = new();

        public GatedVariantStage(IStageGame game)
            : base(game)
        { }

        public TaskCompletionSource ProcessorEntered { get; } = NewGate();
        public TaskCompletionSource ReleaseProcessor { get; } = NewGate();
        public TaskCompletionSource ProcessorCompleted { get; } = NewGate();
        public IPerformancePlayfield? Playfield { get; set; }
        public Func<IPerformancePlayfield?>? TabFactory { get; set; }
        public TabHudDisplay? LastHud { get; private set; }
        public string CacheRoot => _cache.Root;
        public Task<PlaybackAudioVariantCache> CacheCreated => _cache.Created;
        public Task? CacheCleanup => _cache.Cleanup;
        public Task<PreparedGameplayAudioSet?[]> Workers => _cache.Workers;
        protected override IPerformancePlayfield? CreateLanesPlayfield(GraphicsDevice? device, Texture2D? whitePixel) => Playfield;
        protected override IPerformancePlayfield? CreateTabPlayfield(Func<IPerformancePlayfield?> create) =>
            TabFactory == null ? create() : TabFactory();
        internal override TabHudDisplay? CreateTabHud(GraphicsDevice? device, SongChart? chart,
            string? title, string? artist, Texture2D? whitePixel, SkinPalette palette, TabModeArt art) =>
            LastHud = CreateReadyHeadlessTabHud();

        private static TaskCompletionSource NewGate() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The gates are snapshot inputs captured when the stage resolves the
        // factory (update thread); the worker-side processor never touches
        // live stage state.
        protected override Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
        {
            var entered = ProcessorEntered;
            var release = ReleaseProcessor;
            var completed = ProcessorCompleted;
            return () => new GatedProcessor(entered, release, completed);
        }

        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory()
            => _cache.Factory();

        internal override Task<PreparedGameplayAudioSet?> StartGameplayInitialization() =>
            _cache.Track(base.StartGameplayInitialization());

        public void ReleaseAndDeleteSources(string directory)
        {
            ReleaseProcessor.TrySetResult();
            Deactivate();
            // The gated processor does not read the source after its cache key is computed.
            _cache.Workers.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Directory.Delete(directory, recursive: true);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _cache.Release();
        }

        private sealed class GatedProcessor : IAudioVariantProcessor
        {
            private readonly TaskCompletionSource _entered;
            private readonly TaskCompletionSource _release;
            private readonly TaskCompletionSource _completed;

            public GatedProcessor(TaskCompletionSource entered, TaskCompletionSource release, TaskCompletionSource completed)
            {
                _entered = entered;
                _release = release;
                _completed = completed;
            }

            public async Task<PreparedAudioArtifact> PrepareAsync(
                string sourcePath,
                PlaybackModifiers modifiers,
                CancellationToken cancellationToken)
            {
                _entered.TrySetResult();
                await _release.Task.ConfigureAwait(false);
                // Retirement can detach the worker from this continuation; the
                // cache-cleanup regression separately awaits the shared operation.
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new PreparedAudioArtifact(44100, 1, new byte[8]);
                }
                finally
                {
                    _completed.TrySetResult();
                }
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

        public void ReleaseAll()
        {
            while (_parkedGates.TryDequeue(out var gate))
                gate.TrySetResult();
            while (_freeGates.TryDequeue(out var gate))
                gate.TrySetResult();
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
    public void StartGameplayInitialization_WithHiddenCombo_ShouldFreezeTabComboGate()
    {
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, ShowCombo = false };
        using var input = new MockInputManagerCompat();
        var font = new Mock<IFont>();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), config, input, CreateNotationResourceManager(true)))
        {
            MakeHud = () => TabPlayfieldTests.CreateHud(font)
        };
        var chart = CreateFinalizedTabChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Combo" }, 0, chart));
        config.ShowCombo = true;
        UpdateUntil(stage, () => stage.AudioSession != null);
        var manager = stage.Rules!.JudgementManager!;
        manager.IsActive = true;
        Assert.True(manager.ResolveAutoHit(chart.Notes[0].Id));
        stage.Update(0);
        Assert.Equal(1, stage.CurrentFrame.Combo);
        stage.DrawPlayfieldHud(null!);
        font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "COMBO", It.IsAny<Vector2>(), It.IsAny<Color>(),
            It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()),
            Times.Never);
    }

    [Fact]
    public void Judgement_ShouldForwardScoreComboAndSkillInTheSameFrame()
    {
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input))
        {
            Playfield = playfield.Object
        };
        var chart = CreateSingleNoteChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Rules!.JudgementManager!.IsActive = true;
        Assert.True(stage.Rules.JudgementManager.ResolveAutoHit(chart.Notes[0].Id));
        playfield.Verify(x => x.OnJudgement(
            It.IsAny<JudgementEvent>(),
            It.Is<PerformanceFrameState>(frame => frame.Score == 1000000 && frame.Combo == 1
                && frame.Achievement == 100.0),
            false), Times.Once);
    }

    [Theory]
    [InlineData(75f, 0.75f, false)]
    [InlineData(15f, 0.15f, true)]
    public void GaugeChanges_ArePublishedToFrameAndTelemetry(float life, float normalized, bool danger)
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Gauge" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Rules!.GaugeManager!.Reset(life);
        stage.Update(0);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(life, telemetry.Gauge);
        Assert.Equal(life, stage.CurrentFrame.Gauge);
        Assert.Equal(normalized, stage.CurrentFrame.Life);
        Assert.Equal(danger, stage.CurrentFrame.IsDanger);
    }

    [Theory]
    [InlineData(0.0, 0.0f)]
    [InlineData(0.5, 0.25f)]
    [InlineData(2.5, 1.0f)]
    public void UpdateSongProgress_WhenChartDurationExists_ShouldClampProgress(double elapsedSeconds, float expectedProgress)
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var parsedChart = new ParsedChart("duration-test.dtx") { DurationMs = 2000.0 };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Progress" }, 0, parsedChart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        stage.Update(elapsedSeconds);
        Assert.Equal(expectedProgress, stage.CurrentFrame.Progress);
    }

    [Fact]
    public void UpdateSongProgress_ZeroDurationAndAfterDeactivationRemainZero()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Zero" }, 0,
            new ParsedChart("zero-duration.dtx") { DurationMs = 0 }));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        stage.Update(0.5);
        Assert.Equal(0f, stage.CurrentFrame.Progress);
        stage.Deactivate();
        Assert.Equal(0f, stage.CurrentFrame.Progress);
    }

    [Theory]
    [InlineData(DrumsNoteDisplayStyle.Tab)]
    [InlineData(DrumsNoteDisplayStyle.Lanes)]
    public void Frame_BackFadeRetainsProgressWhileClockAndTelemetryReset(DrumsNoteDisplayStyle style)
    {
        using var input = new SwitchableBackInputManager();
        var game = CreateHeadlessGame(new StageTestLogger(),
            new ConfigData { NoteDisplayStyle = style }, input, CreateNotationResourceManager(true));
        Mock.Get(game).Setup(g => g.CanPerformStageTransition()).Returns(true);
        using var stage = new HeadlessPerformanceStage(game);
        var manager = new Mock<IStageManager>();
        stage.StageManager = manager.Object;
        manager.Setup(m => m.ChangeStage(StageType.SongSelect, It.IsAny<IStageTransition>()))
            .Callback<StageType, IStageTransition>((_, transition) => stage.OnTransitionOut(transition));
        var chart = CreateFinalizedTabChart();
        chart.DurationMs = 10000;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Back" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(style.ToString(), telemetry.NoteDisplayStyle);
        stage.Update(1.1);
        stage.Update(2.5);
        var before = stage.CurrentFrame;
        Assert.Equal(0.25f, before.Progress);
        Assert.Equal(2500, before.ElapsedMs);

        input.BackTriggered = true;
        stage.Update(0.1);

        stage.PopulateTelemetry(telemetry);
        Assert.Equal(StagePhase.FadeOut, stage.CurrentPhase);
        Assert.False(stage.AudioSession!.IsPlaying);
        Assert.Equal(0, telemetry.CurrentSongTimeMs);
        Assert.Equal(0, stage.CurrentFrame.ElapsedMs);
        Assert.Equal(10000, stage.CurrentFrame.TotalMs);
        Assert.Equal(before.Progress, stage.CurrentFrame.Progress);

        input.BackTriggered = false;
        stage.Update(0.2);
        Assert.Equal(before.Progress, stage.CurrentFrame.Progress);
        Assert.Equal(0, stage.CurrentFrame.ElapsedMs);
        stage.Deactivate();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(0, telemetry.Gauge);
        Assert.Equal(0.5f, stage.CurrentFrame.Life);
        Assert.Equal(0f, stage.CurrentFrame.Gauge);
        Assert.Equal(0f, stage.CurrentFrame.Progress);
        Assert.Equal(0, stage.CurrentFrame.TotalMs);

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Next" }, 0, chart));
        Assert.Equal(0f, stage.CurrentFrame.Progress);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(0, telemetry.Gauge);
        UpdateUntil(stage, () => stage.AudioSession != null);
        Assert.Equal(0f, stage.CurrentFrame.Progress);
        stage.Deactivate();
    }

    [Fact]
    public void Frame_AndTelemetrySeeSameCompletedPublicationDuringJudgement()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var chart = new ParsedChart("frame-publication.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        input.ModularInputManager.KeyBindings.BindButton("Test.Frame", 0);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Frame" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        PerformanceFrameState during = default;
        var duringTelemetry = new GameTelemetrySnapshot();
        stage.Rules!.ScoreManager!.ScoreChanged += (_, _) =>
        {
            during = stage.CurrentFrame;
            stage.PopulateTelemetry(duringTelemetry);
        };

        Assert.True(input.ModularInputManager.InjectButton("Test.Frame", true));
        input.ModularInputManager.Update();
        stage.Update(0.1);

        Assert.Equal(0, duringTelemetry.Score);
        Assert.Equal(duringTelemetry.Score, during.Score);
        Assert.Equal(duringTelemetry.CurrentCombo, during.Combo);
        var after = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(after);
        Assert.Equal(1000000, after.Score);
        Assert.Equal(after.Score, stage.CurrentFrame.Score);
        Assert.Equal(after.CurrentCombo, stage.CurrentFrame.Combo);
        stage.Deactivate();
    }

    [Fact]
    public async Task Telemetry_ConcurrentPollDuringJudgementSeesLastCompletePublication()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var chart = new ParsedChart("telemetry-atomic.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        input.ModularInputManager.KeyBindings.BindButton("Test.Atomic", 0);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Atomic" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Atomic", true));
        input.ModularInputManager.Update();

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        stage.Rules!.ScoreManager!.ScoreChanged += (_, _) =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        };
        var update = Task.Run(() => stage.Update(0.1));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            for (int i = 0; i < 1000; i++)
            {
                var snapshot = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(snapshot);
                Assert.Equal(0, snapshot.Score);
                Assert.Equal(0, snapshot.PerfectCount);
                Assert.Equal(0, snapshot.CurrentCombo);
            }
        }
        finally
        {
            release.Set();
            await update.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var completed = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(completed);
        Assert.Equal(1000000, completed.Score);
        Assert.Equal(1, completed.PerfectCount);
        Assert.Equal(1, completed.CurrentCombo);
        stage.Deactivate();
    }

    [Theory]
    [InlineData(0)] // Poor hit
    [InlineData(1)] // timeout Miss
    [InlineData(2)] // late manual Miss (Hit status)
    public async Task Telemetry_FailurePublishesStatusBeforeReentrantTransitionAndReactivation(int failureKind)
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData { Risky = 1 }, input));
        var chart = new ParsedChart("failure-snapshot.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 1000 });
        var nextChart = new ParsedChart("new-snapshot.dtx") { DurationMs = 10000 };
        nextChart.Notes.Add(new Note { Id = 0, LaneIndex = 2, TimeMs = 1000 });
        input.ModularInputManager.KeyBindings.BindButton("Test.First", 0);
        input.ModularInputManager.KeyBindings.BindButton("Test.Fail", 1);
        var manager = new Mock<IStageManager>();
        stage.StageManager = manager.Object;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Failed" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        Assert.True(input.ModularInputManager.InjectButton("Test.First", true));
        input.ModularInputManager.Update();
        stage.Update(0.1);
        var judgement = stage.Rules!.JudgementManager!;
        var preEventSkill = stage.Rules.SkillManager!.CurrentSkill;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        GameTelemetrySnapshot? atTransition = null;
        PerformanceFrameState frameAtTransition = default;
        PerformanceSummary? summary = null;
        int[]? scanned = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(),
                It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) =>
            {
                atTransition = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(atTransition);
                frameAtTransition = stage.CurrentFrame;
                summary = Assert.IsType<PerformanceResult>(payload).Summary;
                scanned = Enum.GetValues<JudgementType>().Select(judgement.GetJudgementCount).ToArray();
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                stage.Deactivate();
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "Next" }, 0, nextChart));
            });
        if (failureKind != 1)
        {
            Assert.True(input.ModularInputManager.InjectButton("Test.Fail", true));
            input.ModularInputManager.Update();
        }
        var update = Task.Run(() => stage.Update(failureKind switch
        {
            0 => 1.02,
            1 => 1.101,
            _ => 1.06
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotNull(summary);
            Assert.NotNull(atTransition);
            Assert.Equal(preEventSkill, summary!.PlayingSkill);
            Assert.True(atTransition!.StageCompleted);
            Assert.True(atTransition.HasFailed);
            Assert.False(atTransition.PerformanceReady);
            Assert.Equal(1, atTransition.PerfectCount);
            Assert.Equal(failureKind == 0 ? 1 : 0, atTransition.PoorCount);
            Assert.Equal(failureKind == 1 ? 1 : 0, atTransition.MissCount);
            Assert.Equal(preEventSkill, frameAtTransition.Achievement);
            Assert.Equal(0, frameAtTransition.PoorCount);
            Assert.Equal(0, frameAtTransition.MissCount);
            Assert.Equal(atTransition.Score, frameAtTransition.Score);
            Assert.Equal(atTransition.CurrentCombo, frameAtTransition.Combo);
            Assert.Equal(atTransition.MaxCombo, frameAtTransition.MaxCombo);
            Assert.Equal(atTransition.Gauge, frameAtTransition.Gauge);
            Assert.Equal(atTransition.HasFailed, frameAtTransition.HasFailed);
            Assert.Equal(atTransition.TotalNotes, frameAtTransition.TotalNotes);
            Assert.Equal(atTransition.PerfectCount, frameAtTransition.StatusPerfectCount);
            Assert.Equal(atTransition.GreatCount, frameAtTransition.StatusGreatCount);
            Assert.Equal(atTransition.GoodCount, frameAtTransition.StatusGoodCount);
            Assert.Equal(atTransition.PoorCount, frameAtTransition.StatusPoorCount);
            Assert.Equal(atTransition.MissCount, frameAtTransition.StatusMissCount);
            Assert.Equal(scanned, new[] { summary.PerfectCount, summary.GreatCount,
                summary.GoodCount, summary.PoorCount, summary.MissCount });
            for (int i = 0; i < 1000; i++)
            {
                var poll = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(poll);
                Assert.True(poll.StageCompleted);
                Assert.Equal(atTransition.Score, poll.Score);
                Assert.Equal(atTransition.PoorCount, poll.PoorCount);
                Assert.Equal(atTransition.MissCount, poll.MissCount);
            }
        }
        finally
        {
            release.Set();
            await update.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var next = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(next);
        Assert.Equal("Next", next.SelectedSongTitle);
        Assert.False(next.StageCompleted);
        Assert.Equal(0, next.TotalJudgements);
        Assert.Null(judgement.GetNoteRuntimeData(chart.Notes[1].Id));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.PopulateTelemetry(next);
        Assert.True(next.PerformanceReady);
        Assert.Equal(1, next.TotalNotes);
        Assert.Equal(0, next.Score);
        stage.Deactivate();
    }

    [Fact]
    public async Task Telemetry_ConcurrentPollingAcrossAudioCancellationHitsAndReactivation()
    {
        var directory = CreateTempDirectory();
        var config = new ConfigData { PlaySpeedPercent = 50 };
        using var input = new MockInputManagerCompat();
        using var stage = new GatedVariantStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        try
        {
            input.ModularInputManager.KeyBindings.BindButton("Test.Left", 0);
            input.ModularInputManager.KeyBindings.BindButton("Test.Right", 1);
            var first = new ParsedChart("gated-snapshot.dtx") { DurationMs = 100000 };
            first.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 90000 });
            first.BackgroundAudioPath = CreatePlaceholderSource(directory, "gated.wav");
            var second = new ParsedChart("live-snapshot.dtx") { DurationMs = 100000 };
            second.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 90000 });
            using var stop = new CancellationTokenSource();
            var progressSeen = 0;
            var cleanupSeen = 0;
            var hitSeen = 0;
            var poll = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var snapshot = new GameTelemetrySnapshot();
                    stage.PopulateTelemetry(snapshot);
                    if (snapshot.SelectedSongTitle == "Gated" && snapshot.TotalNotes == 1 &&
                        snapshot.AudioPreparationTotal == 1 && !snapshot.PerformanceReady.GetValueOrDefault())
                        Interlocked.Exchange(ref progressSeen, 1);
                    if (snapshot.SelectedSongTitle == "Live" && snapshot.TotalNotes == 0 &&
                        !snapshot.PerformanceReady.GetValueOrDefault() && snapshot.LastLaneHitLane == null)
                        Interlocked.Exchange(ref cleanupSeen, 1);
                    if (snapshot.LastLaneHitLane is int lane)
                    {
                        Assert.Equal(lane == 0 ? "Test.Left" : "Test.Right", snapshot.LastLaneHitButtonId);
                        Assert.NotNull(snapshot.LastLaneHitSongTimeMs);
                        var time = (int)Math.Round(snapshot.LastLaneHitSongTimeMs!.Value);
                        Assert.Equal(lane == 0 ? 100 : 0, time % 200);
                        Interlocked.Exchange(ref hitSeen, time);
                    }
                }
            });
            try
            {
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "Gated" }, 0, first));
                await stage.ProcessorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                UpdateUntil(stage, () => Volatile.Read(ref progressSeen) == 1);
                stage.Deactivate();
                config.PlaySpeedPercent = 100;
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "Live" }, 0, second));
                stage.ReleaseProcessor.TrySetResult();
                await stage.ProcessorCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                UpdateUntil(stage, () => stage.AudioSession != null);
                stage.Update(1.1);
                for (int i = 1; i <= 20; i++)
                {
                    stage.Update(0.1);
                    string button = i % 2 == 1 ? "Test.Left" : "Test.Right";
                    Assert.True(input.ModularInputManager.InjectButton(button, true));
                    input.ModularInputManager.Update();
                    Assert.True(input.ModularInputManager.InjectButton(button, false));
                    input.ModularInputManager.Update();
                    stage.Update(0);
                    int expectedTime = i * 100;
                    Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref hitSeen) == expectedTime,
                        TimeSpan.FromSeconds(5)), $"poller did not observe hit {i}");
                }
                stage.Deactivate();
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref cleanupSeen) == 1, TimeSpan.FromSeconds(5)));
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fresh" }, 0, second));
                UpdateUntil(stage, () => stage.AudioSession != null);
                var fresh = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(fresh);
                Assert.Equal("Fresh", fresh.SelectedSongTitle);
                Assert.Equal(100, fresh.PlaySpeedPercent);
                Assert.Equal(0, fresh.AudioPreparationTotal);
                Assert.Equal(0L, fresh.PreparedAudioBytes);
                Assert.Equal(0, fresh.TotalJudgements);
                Assert.Null(fresh.LastLaneHitLane);
                stage.Deactivate();
            }
            finally
            {
                stage.ReleaseProcessor.TrySetResult();
                stop.Cancel();
                await poll.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public async Task Telemetry_ConcurrentPollWhileTabResourcesAreBeingDisposed()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var releases = 0;
        var resources = CreateNotationResourceManager(true, () =>
        {
            if (Interlocked.Increment(ref releases) == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
        });
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input, resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, CreateFinalizedTabChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var deactivation = Task.Run(stage.Deactivate);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            for (int i = 0; i < 1000; i++)
            {
                var snapshot = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(snapshot);
                Assert.Equal("Tab", snapshot.SelectedSongTitle);
                Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), snapshot.NoteDisplayStyle);
                Assert.True(snapshot.PerformanceReady);
                Assert.Equal(1, snapshot.TotalNotes);
            }
        }
        finally
        {
            release.Set();
            await deactivation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var cleaned = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(cleaned);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), cleaned.NoteDisplayStyle);
        Assert.False(cleaned.PerformanceReady);
        Assert.Equal(0, cleaned.TotalNotes);
    }

    [Fact]
    public void Telemetry_StatusCountsMatchChartScansAndExcludeLateManualMiss()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData { NoFail = true }, input));
        var chart = new ParsedChart("status-telemetry.dtx") { DurationMs = 10000 };
        for (int i = 0; i < 6; i++)
        {
            chart.Notes.Add(new Note { Id = i, LaneIndex = i, TimeMs = 1000 * (i + 1) });
            input.ModularInputManager.KeyBindings.BindButton($"Test.Status{i}", i);
        }
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Statuses" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        var judgement = stage.Rules!.JudgementManager!;
        var telemetry = new GameTelemetrySnapshot();
        void CheckCounts()
        {
            stage.PopulateTelemetry(telemetry);
            int?[] counts = { telemetry.PerfectCount, telemetry.GreatCount, telemetry.GoodCount,
                telemetry.PoorCount, telemetry.MissCount };
            foreach (var type in Enum.GetValues<JudgementType>())
                Assert.Equal(judgement.GetJudgementCount(type), counts[(int)type]);
            var frame = stage.CurrentFrame;
            Assert.Equal(new[] { frame.StatusPerfectCount, frame.StatusGreatCount, frame.StatusGoodCount,
                frame.StatusPoorCount, frame.StatusMissCount }, counts.Select(count => count!.Value));
            Assert.Equal(frame.Score, telemetry.Score);
            Assert.Equal(frame.Combo, telemetry.CurrentCombo);
            Assert.Equal(frame.MaxCombo, telemetry.MaxCombo);
            Assert.Equal(frame.Gauge, telemetry.Gauge);
            Assert.Equal(frame.HasFailed, telemetry.HasFailed);
            Assert.Equal(frame.TotalNotes, telemetry.TotalNotes);
        }
        double previousTime = 0;
        double[] hitTimes = { 1000, 2040, 3080, 4120, 5160 };
        for (int lane = 0; lane < hitTimes.Length; lane++)
        {
            Assert.True(input.ModularInputManager.InjectButton($"Test.Status{lane}", true));
            input.ModularInputManager.Update();
            stage.Update((hitTimes[lane] - previousTime) / 1000.0);
            previousTime = hitTimes[lane];
            CheckCounts();
        }
        stage.Update((6201 - previousTime) / 1000.0);
        CheckCounts();
        Assert.Equal(1, telemetry.PerfectCount);
        Assert.Equal(1, telemetry.GreatCount);
        Assert.Equal(1, telemetry.GoodCount);
        Assert.Equal(1, telemetry.PoorCount);
        Assert.Equal(1, telemetry.MissCount);
        Assert.Equal(2, stage.Rules.BuildFrameState(6201).MissCount);
        Assert.Equal(2, stage.CurrentFrame.MissCount);
        Assert.Equal(1, stage.CurrentFrame.StatusMissCount);
        Assert.Equal(NoteStatus.Hit, judgement.GetNoteRuntimeData(chart.Notes[4].Id)!.Status);
        Assert.Equal(NoteStatus.Missed, judgement.GetNoteRuntimeData(chart.Notes[5].Id)!.Status);
        stage.Deactivate();
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
            new (int Lane, int? Score, int? Combo)[] { (0, 0, 0), (1, 500000, 1) },
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
        Assert.Equal(0.5f, stage.CurrentFrame.Life);
        Assert.Equal(0f, stage.CurrentFrame.Progress);
        Assert.False(stage.CurrentFrame.IsDanger);
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

    [Fact]
    public void UpdateGameplayManagers_ShouldAdvanceMetronomeWithRawLogicalTime()
    {
        var config = new ConfigData { Metronome = true, AudioLatencyOffsetMs = 250 };
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config));
        var clicks = new List<BeatMarker>();
        stage.OnMetronomeClick = clicks.Add;
        var chart = CreateMetronomeChart(new BeatMarker { TimeMs = 100.0 });
        chart.DurationMs = 10000;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Metronome" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.0);
        stage.Update(0.1);

        var click = Assert.Single(clicks);
        Assert.Equal(100.0, click.TimeMs);
    }

    [Theory]
    [InlineData(50, 150.0, true)]
    [InlineData(50, 151.0, false)]
    [InlineData(100, 200.0, true)]
    [InlineData(100, 201.0, false)]
    [InlineData(150, 250.0, true)]
    [InlineData(150, 251.0, false)]
    public void InitializeMetronome_ShouldScaleStaleToleranceByFrozenPlaySpeed(
        int playSpeedPercent,
        double rawLogicalTimeMs,
        bool expectedClick)
    {
        var config = new ConfigData { Metronome = true, PlaySpeedPercent = playSpeedPercent, AudioLatencyOffsetMs = 250 };
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config));
        var clicks = new List<BeatMarker>();
        stage.OnMetronomeClick = clicks.Add;
        var chart = CreateMetronomeChart(new BeatMarker { TimeMs = 100.0, IsMeasureStart = false });
        chart.DurationMs = 10000;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tolerance" }, 0, chart));
        config.PlaySpeedPercent = playSpeedPercent == 100 ? 200 : 100;
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.0);
        stage.Update(rawLogicalTimeMs / (10.0 * playSpeedPercent));

        Assert.Equal(expectedClick ? 1 : 0, clicks.Count);
    }

    [Fact]
    public void CleanupComponents_ShouldClearMetronomeAndReleaseClickSoundReferences()
    {
        var beatSound = new Mock<ISound>();
        var accentSound = new Mock<ISound>();
        var resourceManager = new Mock<IResourceManager>();
        var beatResult = beatSound.Object;
        var accentResult = accentSound.Object;
        resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeBeat, out beatResult)).Returns(true);
        resourceManager.Setup(x => x.TryLoadSound(SoundPath.MetronomeAccent, out accentResult)).Returns(true);
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { Metronome = true }, resources: resourceManager));
        var chart = CreateMetronomeChart(new BeatMarker { TimeMs = 0.0 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Metronome" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var session = stage.AudioSession!;
        Assert.NotNull(session.Metronome);

        stage.Deactivate();

        Assert.Null(stage.AudioSession);
        Assert.Null(session.Metronome);
        beatSound.Verify(x => x.RemoveReference(), Times.Once);
        accentSound.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public async Task NaturalAudioPreparation_BindsRealSoundsAndSchedulesBgmBeforeAutoplayJudgement()
    {
        var directory = CreateTempDirectory();
        var config = new ConfigData();
        config.AutoPlayLanes.Add(3);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        try
        {
            var bgmPath = AudioTestUtils.CreateTestWavFile(directory, "natural-bgm.wav", 10);
            var chipPath = AudioTestUtils.CreateTestWavFile(directory, "natural-chip.wav", 10);
            var unusedPath = AudioTestUtils.CreateTestWavFile(directory, "natural-unused.wav", 10);
            var chart = new ParsedChart("natural-audio.dtx") { DurationMs = 10000 };
            chart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100, AudioFilePath = bgmPath });
            chart.Notes.Add(new Note { LaneIndex = 3, TimeMs = 100, Value = "07" });
            chart.SetWavDefinitions(new Dictionary<string, string> { ["07"] = chipPath, ["08"] = unusedPath });
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Natural audio" }, 0, chart));
            var prepared = Assert.IsType<PreparedGameplayAudioSet>(
                await stage.LastPreparationTask!.WaitAsync(TimeSpan.FromSeconds(5)));
            UpdateUntil(stage, () => stage.AudioSession != null);
            var session = stage.AudioSession!;
            Assert.Same(prepared, session.PreparedSet);
            var chip = Assert.IsType<ManagedSound>(prepared.ChipSoundsByWavId["07"]);
            var bgm = Assert.IsType<ManagedSound>(prepared.ScheduledBgmBySourcePath[Path.GetFullPath(bgmPath)]);
            Assert.False(prepared.ChipSoundsByWavId.ContainsKey("08"));
            Assert.Equal(Path.GetFullPath(chipPath), Path.GetFullPath(chip.SourcePath));
            Assert.False(chip.IsDisposed);
            Assert.False(bgm.IsDisposed);
            Assert.Single(session.ScheduledBgmEvents);
            Assert.Empty(session.ActiveBgmInstances);
            var events = new List<string>();
            stage.Rules!.JudgementManager!.JudgementMade += (_, e) =>
            {
                Assert.Equal(JudgementType.Perfect, e.Type);
                Assert.Equal(SoundState.Playing, Assert.Single(session.ActiveBgmInstances).State);
                Assert.Equal(1000000, stage.Rules.ScoreManager!.CurrentScore);
                events.Add("judgement");
            };

            stage.Update(1);
            Assert.True(session.IsPlaying);
            Assert.Empty(session.ActiveBgmInstances);
            stage.Update(0.1);

            Assert.Equal(new[] { "judgement" }, events);
            Assert.Empty(session.ScheduledBgmEvents);
            Assert.Equal(1000000, stage.CurrentFrame.Score);
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.Equal(1000000, telemetry.Score);
            var bgmInstance = Assert.Single(session.ActiveBgmInstances);
            stage.Deactivate();
            Assert.True(bgmInstance.IsDisposed);
            Assert.True(chip.IsDisposed);
            Assert.True(bgm.IsDisposed);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public async Task AutoPlay_JudgementPrecedesChipFeedback()
    {
        var directory = CreateTempDirectory();
        var config = new ConfigData();
        config.AutoPlayLanes.Add(3);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        try
        {
            var wav = AudioTestUtils.CreateTestWavFile(directory, "auto.wav", 5);
            var chart = new ParsedChart("auto-feedback.dtx") { DurationMs = 10000 };
            chart.Notes.Add(new Note { LaneIndex = 3, TimeMs = 100, Value = "07" });
            chart.SetWavDefinitions(new Dictionary<string, string> { ["07"] = wav });
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
            UpdateUntil(stage, () => stage.AudioSession != null);
            var sound = (await RebindObservedAudio(stage, chart))[Path.GetFullPath(wav)];
            var order = new List<string>();
            stage.Rules!.JudgementManager!.JudgementMade += (_, _) => order.Add("judgement");
            sound.OnPlay = () => order.Add("chip");
            stage.Update(1.1);
            stage.Update(0.1);
            Assert.Equal(new[] { "judgement", "chip" }, order);
            Assert.Single(sound.Instances);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void GameplayFrame_AutoplayThenMetronomeThenCompensatedManualJudgement()
    {
        var config = new ConfigData { Metronome = true, AudioLatencyOffsetMs = 250 };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = new ParsedChart("frame-order.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 750 });
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        chart.BeatMarkers.Add(new BeatMarker { TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        var order = new List<string>();
        stage.OnMetronomeClick = _ => order.Add("metronome");
        stage.Rules!.JudgementManager!.JudgementMade += (_, e) =>
            order.Add($"{e.Lane}:{e.Type}:{e.DeltaMs}");
        input.ModularInputManager.KeyBindings.BindButton("Test.Manual", 1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Manual", true));
        input.ModularInputManager.Update();

        stage.Update(1.0);

        Assert.Equal(new[] { "0:Perfect:0", "metronome", "1:Perfect:0" }, order);
    }

    [Fact]
    public async Task GameplayFrame_LiveLatencyAndFrozenSpeedApplyToManualChipHitAndMiss()
    {
        var directory = CreateTempDirectory();
        var config = new ConfigData();
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        try
        {
            var wav = AudioTestUtils.CreateTestWavFile(directory, "manual.wav", 5);
            var chart = new ParsedChart("latency-frame.dtx") { DurationMs = 10000 };
            var manual = new Note { LaneIndex = 1, TimeMs = 1000, Value = "07" };
            var missed = new Note { LaneIndex = 2, TimeMs = 1000 };
            var automatic = new Note { LaneIndex = 0, TimeMs = 1250 };
            chart.Notes.AddRange(new[] { manual, missed, automatic });
            chart.SetWavDefinitions(new Dictionary<string, string> { ["07"] = wav });
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
            config.AudioLatencyOffsetMs = 250;
            config.PlaySpeedPercent = 150;
            UpdateUntil(stage, () => stage.AudioSession != null);
            var sound = (await RebindObservedAudio(stage, chart))[Path.GetFullPath(wav)];
            stage.Update(1.1);
            stage.Update(1.25);
            var judgement = stage.Rules!.JudgementManager!;
            Assert.Equal(NoteStatus.Hit, judgement.GetNoteRuntimeData(automatic.Id)!.Status);
            Assert.Equal(NoteStatus.Pending, judgement.GetNoteRuntimeData(manual.Id)!.Status);
            Assert.Equal(NoteStatus.Pending, judgement.GetNoteRuntimeData(missed.Id)!.Status);
            input.ModularInputManager.KeyBindings.BindButton("Test.Manual", 1);
            Assert.True(input.ModularInputManager.InjectButton("Test.Manual", true));
            input.ModularInputManager.Update();
            Assert.Single(sound.Instances);
            stage.Update(0);
            Assert.Equal(JudgementType.Perfect, judgement.GetNoteRuntimeData(manual.Id)!.JudgementEvent!.Type);
            stage.Update(0.201);
            Assert.Equal(NoteStatus.Missed, judgement.GetNoteRuntimeData(missed.Id)!.Status);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void AutoPlay_ReentrantDeactivationCleansUpAndNextActivationStartsFresh()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var first = new ParsedChart("auto-teardown.dtx") { DurationMs = 10000 };
        first.Notes.Add(new Note { LaneIndex = 0, TimeMs = 100 });
        first.Notes.Add(new Note { LaneIndex = 0, TimeMs = 200 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "First" }, 0, first));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var oldRules = stage.Rules!;
        var count = 0;
        oldRules.JudgementManager!.JudgementMade += (_, _) =>
        {
            count++;
            stage.Deactivate();
        };
        stage.Update(1.1);
        stage.Update(0.5);
        Assert.Equal(1, count);
        Assert.Null(oldRules.JudgementManager);
        Assert.Null(oldRules.ChartManager);
        Assert.Null(stage.AudioSession);

        config.AutoPlayLanes.Clear();
        config.AutoPlayLanes.Add(1);
        var second = new ParsedChart("auto-reactivate.dtx") { DurationMs = 10000 };
        second.Notes.Add(new Note { LaneIndex = 1, TimeMs = 100 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Second" }, 0, second));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(0.1);
        Assert.NotSame(oldRules, stage.Rules);
        Assert.Equal(NoteStatus.Hit, stage.Rules!.JudgementManager!.GetNoteRuntimeData(second.Notes[0].Id)!.Status);
    }

    [Theory]
    [InlineData("manual", 990, 1000, 0, true)]
    [InlineData("far", 1000, 3000, 0, false)]
    [InlineData("latency", 1200, 1000, 200, true)]
    [InlineData("auto", 990, 1000, 0, false)]
    [InlineData("countdown", 0, 0, 0, false)]
    [InlineData("paused", 1000, 1000, 0, false)]
    public async Task ManualInput_UsesRealChipPlaybackOnlyForPlayingManualLaneAndCompensatedWindow(
        string scenario, int rawMs, int noteMs, int latencyMs, bool expectedChip)
    {
        var directory = CreateTempDirectory();
        var config = new ConfigData { AudioLatencyOffsetMs = latencyMs };
        if (scenario == "auto")
            config.AutoPlayLanes.Add(3);
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input))
        {
            Playfield = playfield.Object
        };
        try
        {
            var wav = AudioTestUtils.CreateTestWavFile(directory, "chip.wav", 5);
            var fartherWav = AudioTestUtils.CreateTestWavFile(directory, "farther.wav", 5);
            var chart = new ParsedChart("chip.dtx") { DurationMs = 10000 };
            var note = new Note { LaneIndex = 3, TimeMs = noteMs, Value = "07" };
            chart.Notes.Add(note);
            chart.Notes.Add(new Note { LaneIndex = 3, TimeMs = noteMs + 120, Value = "08" });
            chart.SetWavDefinitions(new Dictionary<string, string> { ["07"] = wav, ["08"] = fartherWav });
            input.ModularInputManager.KeyBindings.BindButton("Test.Chip", 3);
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Chip" }, 0, chart));
            UpdateUntil(stage, () => stage.AudioSession != null);
            var observed = await RebindObservedAudio(stage, chart);
            var sound = observed[Path.GetFullPath(wav)];
            var order = new List<string>();
            playfield.Setup(p => p.OnLaneInput(3)).Callback(() => order.Add("pad"));
            sound.OnPlay = () => order.Add("chip");
            stage.Rules!.JudgementManager!.JudgementMade += (_, _) => order.Add("judgement");

            if (scenario != "countdown")
            {
                stage.Update(1);
                stage.Update(rawMs / 1000.0);
            }
            if (scenario == "paused")
                stage.AudioSession!.Timer!.Pause(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.Zero));
            Assert.True(input.ModularInputManager.InjectButton("Test.Chip", true));
            input.ModularInputManager.Update();

            Assert.Equal(expectedChip ? 1 : 0, sound.Instances.Count);
            Assert.Empty(observed[Path.GetFullPath(fartherWav)].Instances);
            Assert.Equal(scenario == "auto" ? Array.Empty<string>() :
                expectedChip ? new[] { "pad", "chip" } : new[] { "pad" }, order);
            stage.Update(0);
            if (expectedChip)
            {
                Assert.Equal("judgement", order[^1]);
                Assert.Equal(NoteStatus.Hit, stage.Rules.JudgementManager.GetNoteRuntimeData(note.Id)!.Status);
            }
            else if (scenario == "auto")
            {
                stage.Update(0.02);
                Assert.Equal(new[] { "judgement", "chip" }, order);
                Assert.Single(sound.Instances);
            }
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Theory]
    [InlineData(100, 1000.0)]
    [InlineData(150, 1500.0)]
    public void LastHit_UsesFrozenRawClockOnlyWhilePlayingAndResetsOnDeactivation(int speed, double rawMs)
    {
        var config = new ConfigData { PlaySpeedPercent = speed, AudioLatencyOffsetMs = 200 };
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input))
        {
            Playfield = playfield.Object
        };
        var chart = new ParsedChart("hit-lifetime.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 3, TimeMs = 9000 });
        var request = new SongPlayRequest(new SongListNode { Title = "Hit" }, 0, chart);
        input.ModularInputManager.KeyBindings.BindButton("Key.L", 3);
        stage.Activate(request);
        config.PlaySpeedPercent = speed == 100 ? 150 : 100;
        UpdateUntil(stage, () => stage.AudioSession != null);
        Assert.True(input.ModularInputManager.InjectButton("Key.L", true));
        input.ModularInputManager.Update();
        stage.Update(0);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Null(telemetry.LastLaneHitLane);
        Assert.Null(telemetry.LastLaneHitButtonId);
        Assert.Null(telemetry.LastLaneHitSongTimeMs);
        playfield.Verify(p => p.OnLaneInput(3), Times.Once);
        Assert.True(input.ModularInputManager.InjectButton("Key.L", false));
        input.ModularInputManager.Update();
        stage.Update(1);
        stage.Update(1);
        Assert.True(input.ModularInputManager.InjectButton("Key.L", true));
        input.ModularInputManager.Update();
        stage.Update(0);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(3, telemetry.LastLaneHitLane);
        Assert.Equal("Key.L", telemetry.LastLaneHitButtonId);
        Assert.Equal(rawMs, telemetry.LastLaneHitSongTimeMs);
        stage.AudioSession!.StopAll();
        Assert.True(input.ModularInputManager.InjectButton("Key.L", false));
        input.ModularInputManager.Update();
        Assert.True(input.ModularInputManager.InjectButton("Key.L", true));
        input.ModularInputManager.Update();
        stage.Update(0);
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(rawMs, telemetry.LastLaneHitSongTimeMs);
        stage.Deactivate();
        stage.PopulateTelemetry(telemetry);
        Assert.Null(telemetry.LastLaneHitLane);
        Assert.Null(telemetry.LastLaneHitButtonId);
        Assert.Null(telemetry.LastLaneHitSongTimeMs);
        stage.Activate(request);
        stage.PopulateTelemetry(telemetry);
        Assert.Null(telemetry.LastLaneHitLane);
        Assert.Null(telemetry.LastLaneHitButtonId);
        Assert.Null(telemetry.LastLaneHitSongTimeMs);
    }

    [Fact]
    public void PopulateTelemetry_LastHitRecorded_ShouldDeconstructConsistently()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var chart = new ParsedChart("last-hit.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 7, TimeMs = 9000 });
        input.ModularInputManager.KeyBindings.BindButton("MIDI.42", 7);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Hit" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(1.5);
        Assert.True(input.ModularInputManager.InjectButton("MIDI.42", true));
        input.ModularInputManager.Update();
        stage.Update(0);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(7, telemetry.LastLaneHitLane);
        Assert.Equal("MIDI.42", telemetry.LastLaneHitButtonId);
        Assert.Equal(1500, telemetry.LastLaneHitSongTimeMs);
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_NoHitRecorded_ShouldReportNullLastHit()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Null(telemetry.LastLaneHitLane);
        Assert.Null(telemetry.LastLaneHitButtonId);
        Assert.Null(telemetry.LastLaneHitSongTimeMs);
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerPlaying_ShouldReportCurrentSongTimeMs()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        var chart = new ParsedChart("playing-telemetry.dtx") { DurationMs = 10000 };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Playing" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(2);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(2000.0, telemetry.CurrentSongTimeMs);
        Assert.True(telemetry.PerformanceReady);
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerPaused_ShouldReportFrozenLogicalTime()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 150, PitchSemitones = -4 }));
        var chart = new ParsedChart("paused-telemetry.dtx") { DurationMs = 10000 };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Paused" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(1);
        var session = stage.AudioSession!;
        session.Timer!.Pause(new GameTime(
            TimeSpan.FromMilliseconds(2100),
            TimeSpan.Zero));
        stage.Update(3);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(1500.0, telemetry.CurrentSongTimeMs);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(150, telemetry.PlaySpeedPercent);
        Assert.Equal(-4, telemetry.PitchSemitones);
        Assert.True(telemetry.PlaybackProfileFrozen);
        Assert.Equal(1500, stage.CurrentFrame.ElapsedMs);
        Assert.Equal(0.15f, stage.CurrentFrame.Progress);
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_WhenAudioBindsBeforeFirstUpdate_ShouldReportZeroSongTime()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        var chart = new ParsedChart("null-gametime.dtx");
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "First frame" }, 0, chart));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.BackgroundWork!.Drain();
            return stage.AudioSession != null;
        }, TimeSpan.FromSeconds(5)));
        Assert.False(stage.AudioSession!.IsPlaying);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(0.0, telemetry.CurrentSongTimeMs);
        Assert.True(telemetry.PerformanceReady);
        stage.Deactivate();
    }

    [Fact]
    public void PopulateTelemetry_WhenSongTimerNotPlaying_ShouldReportZeroSongTimeAndNotReady()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        var chart = new ParsedChart("stopped-telemetry.dtx") { DurationMs = 10000 };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Stopped" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.AudioSession!.StopAll();
        stage.Update(0);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);

        Assert.Equal(0.0, telemetry.CurrentSongTimeMs);
        // _isReady is false and songTimerPlaying is false, so PerformanceReady is false.
        Assert.False(telemetry.PerformanceReady);
        stage.Deactivate();
    }

    [Fact]
    public void Deactivation_WithScheduledAudio_ShouldDisposeAndResetForReactivation()
    {
        var directory = CreateTempDirectory();
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        try
        {
            var path = AudioTestUtils.CreateTestWavFile(directory, "cleanup.wav");
            var chart = new ParsedChart("cleanup.dtx") { DurationMs = 10000, BackgroundAudioPath = path };
            chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
            chart.AddBGMEvent(new BGMEvent { AudioFilePath = path, TimeMs = 2000 });
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Cleanup" }, 0, chart));
            UpdateUntil(stage, () => stage.AudioSession != null);
            var rules = stage.Rules!;
            var session = stage.AudioSession!;
            var main = session.PreparedSet!.MainBackground!;
            Assert.Single(session.ScheduledBgmEvents);
            Assert.False(main.IsDisposed);
            Assert.True(rules.TryComplete());
            stage.Update(0.5);

            stage.Deactivate();

            Assert.True(main.IsDisposed);
            Assert.Empty(session.ScheduledBgmEvents);
            Assert.Null(stage.AudioSession);
            Assert.Null(rules.ChartManager);
            Assert.False(rules.IsCompleted);

            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Again" }, 0, chart));
            UpdateUntil(stage, () => stage.AudioSession != null);
            stage.Update(0.9);
            Assert.False(stage.AudioSession!.IsPlaying);
            stage.Update(0.2);
            Assert.True(stage.AudioSession.IsPlaying);
            stage.Deactivate();
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public async Task ReturnToSongSelect_ShouldPauseInputStopTimerAndChangeStage()
    {
        var directory = CreateTempDirectory();
        using var input = new SwitchableBackInputManager();
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input);
        Mock.Get(game).Setup(g => g.CanPerformStageTransition()).Returns(true);
        using var stage = new HeadlessPerformanceStage(game);
        try
        {
            var bgmPath = AudioTestUtils.CreateTestWavFile(directory, "bgm.wav", 10);
            var chipPath = AudioTestUtils.CreateTestWavFile(directory, "chip.wav", 10);
            var chart = new ParsedChart("back-audio.dtx") { DurationMs = 10000 };
            chart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100, AudioFilePath = bgmPath });
            chart.Notes.Add(new Note { LaneIndex = 3, TimeMs = 1000, Value = "07" });
            chart.SetWavDefinitions(new Dictionary<string, string> { ["07"] = chipPath });
            input.ModularInputManager.KeyBindings.BindButton("Test.BackChip", 3);
            var manager = new Mock<IStageManager>();
            stage.StageManager = manager.Object;
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Back" }, 0, chart));
            UpdateUntil(stage, () => stage.AudioSession != null);
            var sounds = await RebindObservedAudio(stage, chart);
            var session = stage.AudioSession!;
            var judgement = stage.Rules!.JudgementManager!;
            stage.Update(1);
            stage.Update(0.9);
            var bgmInstance = Assert.Single(session.ActiveBgmInstances);
            Assert.True(input.ModularInputManager.InjectButton("Test.BackChip", true));
            input.ModularInputManager.Update();
            var chipInstance = Assert.Single(sounds[Path.GetFullPath(chipPath)].Instances);
            Assert.Equal(SoundState.Playing, bgmInstance.State);
            Assert.Equal(SoundState.Playing, chipInstance.State);
            manager.Setup(x => x.ChangeStage(StageType.SongSelect, It.IsAny<IStageTransition>()))
                .Callback(() =>
                {
                    Assert.Equal(SoundState.Stopped, bgmInstance.State);
                    Assert.Equal(SoundState.Stopped, chipInstance.State);
                    Assert.False(session.IsPlaying);
                    Assert.False(judgement.IsActive);
                    Assert.False(bgmInstance.IsDisposed);
                    Assert.False(chipInstance.IsDisposed);
                });
            input.BackTriggered = true;
            stage.Update(0);
            manager.Verify(x => x.ChangeStage(StageType.SongSelect,
                It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition)), Times.Once);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Back_WhenLoadingFailedWithoutSessionOrJudgement_IsSafeWithOrWithoutStageManager(bool hasManager)
    {
        using var input = new SwitchableBackInputManager { BackTriggered = true };
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input);
        Mock.Get(game).Setup(g => g.CanPerformStageTransition()).Returns(true);
        using var stage = new HeadlessPerformanceStage(game);
        var manager = new Mock<IStageManager>();
        if (hasManager)
            stage.StageManager = manager.Object;
        stage.Activate();
        Assert.Null(stage.AudioSession);
        Assert.Null(stage.Rules!.JudgementManager);
        stage.Update(0);
        Mock.Get(game).Verify(g => g.MarkStageTransition(), Times.Once);
        manager.Verify(m => m.ChangeStage(StageType.SongSelect, It.IsAny<IStageTransition>()),
            hasManager ? Times.Once() : Times.Never());
    }

    [Fact]
    public void HandleInput_BackHonorsDebounceThenStopsBeforeTransition()
    {
        using var input = new SwitchableBackInputManager();
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input);
        var allowTransition = false;
        Mock.Get(game).Setup(g => g.CanPerformStageTransition()).Returns(() => allowTransition);
        using var stage = new HeadlessPerformanceStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chart = new ParsedChart("back.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 5000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Back" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        var session = stage.AudioSession!;
        var judgement = stage.Rules!.JudgementManager!;
        Assert.True(session.IsPlaying);
        Assert.True(judgement.IsActive);
        input.BackTriggered = true;
        stage.Update(0.1);
        Assert.True(session.IsPlaying);
        Assert.True(judgement.IsActive);
        Mock.Get(game).Verify(g => g.MarkStageTransition(), Times.Never);
        stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>()), Times.Never);
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
            Times.Never);
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload?>()),
            Times.Never);
        allowTransition = true;
        stageManager.Setup(x => x.ChangeStage(StageType.SongSelect, It.IsAny<IStageTransition>()))
            .Callback(() =>
            {
                Mock.Get(game).Verify(g => g.MarkStageTransition(), Times.Once);
                Assert.False(session.IsPlaying);
                Assert.False(judgement.IsActive);
            });
        stage.Update(0.1);
        stageManager.Verify(x => x.ChangeStage(StageType.SongSelect,
            It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition)), Times.Once);
        Mock.Get(game).Verify(g => g.MarkStageTransition(), Times.Once);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    public void Countdown_StartsSongAfterOneRealSecondAtEveryFrozenSpeed(
        int playSpeedPercent)
    {
        var config = new ConfigData { PlaySpeedPercent = playSpeedPercent };
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Countdown" }, 0, CreateSingleNoteChart()));
        config.PlaySpeedPercent = playSpeedPercent == 100 ? 150 : 100;
        UpdateUntil(stage, () => stage.AudioSession != null);
        var session = stage.AudioSession!;
        var judgement = stage.Rules!.JudgementManager!;
        stage.Update(0.75);
        Assert.False(session.IsPlaying);
        Assert.False(judgement.IsActive);
        Assert.Equal(0, judgement.GetJudgementCount(JudgementType.Miss));
        stage.Update(0.24);
        Assert.False(session.IsPlaying);
        Assert.False(judgement.IsActive);
        stage.Update(0.02);
        Assert.True(session.IsPlaying);
        Assert.True(judgement.IsActive);
        Assert.Equal(0, judgement.GetJudgementCount(JudgementType.Miss));
        stage.Update(0.1);
        Assert.Equal(playSpeedPercent, stage.CurrentFrame.ElapsedMs, 5);
    }

    [Fact]
    public void UpdateGameplay_WhenSongIsPlaying_ShouldAdvanceManagersAndProgress()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var chart = new ParsedChart("progress-test.dtx") { DurationMs = 2000.0 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Progress" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var judgementManager = stage.Rules!.JudgementManager!;
        stage.Update(1.0);
        stage.Update(1.301);
        Assert.Equal(1, judgementManager.GetJudgementCount(JudgementType.Miss));
        Assert.Equal(1301.0 / 2000.0, stage.CurrentFrame.Progress, 3);
        Assert.False(stage.Rules!.IsCompleted);
    }

    [Fact]
    public void UpdateComponents_WhenAllOptionalComponentsMissing_ShouldNotThrow()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "No playfield" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.0);
        stage.Update(0.016);
        Assert.True(stage.AudioSession!.IsPlaying);
    }

    [Fact]
    public void DrawBackground_WhenBackgroundTextureExists_ShouldDrawUILayoutBackgroundBounds()
    {
        var backgroundTexture = CreateTextureMock(width: 512, height: 256);
        var resources = new Mock<IResourceManager>();
        var texture = backgroundTexture.Object;
        resources.Setup(r => r.TryLoadTexture(TexturePath.PerformanceBackgroundTexture, out texture)).Returns(true);
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Background" }, 0, new ParsedChart("background.dtx")));
        stage.LoadPerformanceUIAssets();
        stage.DrawBasePass(new Rectangle(0, 0, 1920, 1080));
        Assert.Empty(stage.FallbackBackgrounds);

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
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fallback" }, 0, new ParsedChart("fallback.dtx")));
        stage.LoadPerformanceUIAssets();
        stage.DrawBasePass(new Rectangle(0, 0, 1920, 1080));
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Assert.Single(stage.FallbackBackgrounds));
    }

    [Fact]
    public void DrawLaneBackgrounds_WhenLaneTextureExists_ShouldDrawEachVisibleLaneStrip()
    {
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

        LanesPlayfield.DrawLaneBackgrounds(null!, laneTexture.Object, null, hidden: false);

        Assert.Equal(expectedCalls.Count, actualCalls.Count);
        for (var i = 0; i < expectedCalls.Count; i++)
        {
            Assert.Equal(expectedCalls[i].DestinationRect, actualCalls[i].DestinationRect);
            Assert.Equal(expectedCalls[i].SourceRect, actualCalls[i].SourceRect);
            Assert.Equal(0.8f, actualCalls[i].Depth);
        }
    }

    [Fact]
    public void DrawLaneBackgrounds_WhenLaneTextureMissing_ShouldUseFallbackRendererPathWithoutThrowing()
    {
        using var fallback = new LaneBackgroundRenderer(new Mock<IResourceManager>().Object);
        LanesPlayfield.DrawLaneBackgrounds(null!, null, fallback, hidden: false);
    }

    [Fact]
    public void DrawJudgementLine_WhenTextureExists_ShouldDrawNxSolidHitBarBounds()
    {
        var judgementLineTexture = CreateTextureMock(width: 8, height: 8);
        var expectedRectangle = new Rectangle(295, 561, 558, 6);
        var expectedSource = new Rectangle(0, 0, 8, 6);

        LanesPlayfield.DrawJudgementLine(null!, judgementLineTexture.Object, null, hidden: false);

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
        var judgementLineTexture = CreateTextureMock(width: 8, height: 8);
        LanesPlayfield.DrawJudgementLine(null!, judgementLineTexture.Object, null, hidden: true);

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
    public void DrawJudgementLine_WhenTextureAndFallbackMissing_ShouldNotThrow()
    {
        LanesPlayfield.DrawJudgementLine(null!, null, null, hidden: false);
    }

    [Fact]
    public void DrawGaugeElements_WhenGaugeTexturesExist_ShouldDrawFrameAndScaledFill()
    {
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

        LanesPlayfield.DrawGauge(null!, gaugeBaseTexture.Object, gaugeFillTexture.Object, 0.6f);

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
        var gaugeBaseTexture = CreateTextureMock(width: 120, height: 24);
        var gaugeFillTexture = CreateTextureMock(width: 200, height: 8);

        LanesPlayfield.DrawGauge(null!, gaugeBaseTexture.Object, gaugeFillTexture.Object, 0.0f);

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
        var progressBaseTexture = CreateTextureMock(width: 60, height: 540);
        LanesPlayfield.DrawProgressFrame(null!, progressBaseTexture.Object);
        var fill = LanesPlayfield.ProgressFill(0.25f);

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
        Assert.NotNull(fill);
        Assert.Equal(new Rectangle(barRect.X, barRect.Bottom - expectedFillHeight, barRect.Width, expectedFillHeight), fill.Value.Bounds);
        Assert.Equal(Color.LightBlue, fill.Value.Color);
        Assert.Equal(0.2f, fill.Value.Depth);
    }

    [Fact]
    public void DrawProgressBar_WhenProgressValueIsZero_ShouldNotDrawFallbackFill()
    {
        var progressBaseTexture = CreateTextureMock(width: 60, height: 540);
        LanesPlayfield.DrawProgressFrame(null!, progressBaseTexture.Object);

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
        Assert.Null(LanesPlayfield.ProgressFill(0));
        Assert.Null(LanesPlayfield.ProgressFill(float.NaN));
    }

    [Fact]
    public void DrawProgressBar_WhenFallbackDrawerAndTextureAreMissing_ShouldSkipFallbackFillWithoutThrowing()
    {
        var progressBaseTexture = CreateTextureMock(width: 60, height: 540);
        LanesPlayfield.DrawProgressFrame(null!, progressBaseTexture.Object);
        var fill = LanesPlayfield.ProgressFill(0.25f)!.Value;
        LanesPlayfield.DrawProgressFill(null!, null, fill.Bounds, fill.Color, fill.Depth);
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
        var skillPanelTexture = CreateTextureMock(width: 180, height: 96);
        var panelPosition = PerformanceUILayout.SkillPanel.PanelPosition;

        LanesPlayfield.DrawSkillPanel(null!, skillPanelTexture.Object);

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
        LanesPlayfield.DrawSkillPanel(null!, null);
    }

    [Fact]
    public void DrawShutters_WhenStageIsTransitioning_ShouldDrawAtUILayoutStartPosition()
    {
        var shutterTexture = CreateTextureMock(width: 96, height: 512);
        var shutterPosition = PerformanceUILayout.Shutter.StartPosition;
        using var stage = CreateVisualStage(TexturePath.Shutter, shutterTexture.Object);
        stage.OnTransitionOut(new InstantTransition());
        stage.DrawUiPass(null);

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
        var shutterTexture = CreateTextureMock(width: 96, height: 512);
        using var stage = CreateVisualStage(TexturePath.Shutter, shutterTexture.Object);
        stage.OnTransitionComplete();
        stage.DrawUiPass(null);

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
        using var stage = CreateVisualStage(TexturePath.Shutter, null);
        Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
        stage.DrawUiPass(null);
    }

    [Fact]
    public void DrawOverlays_WhenDangerActive_ShouldPulseDangerTint()
    {
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

        using var input = new MockInputManagerCompat();
        using var stage = CreateVisualStage(TexturePath.Danger, dangerOverlayTexture.Object, input);
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Rules!.GaugeManager!.Reset(15);
        stage.Update(totalTime);
        stage.DrawUiPass(null);

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
    public void DrawOverlays_WhenDangerDisabled_ShouldNotDrawDangerTexture()
    {
        var dangerOverlayTexture = CreateTextureMock(width: 1280, height: 720);
        using var stage = CreateVisualStage(TexturePath.Danger, dangerOverlayTexture.Object);
        stage.DrawUiPass(null);

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
    public void DrawFallbackRectangle_WhenDrawerMissingAndWhiteTextureExists_ShouldUseTexturePath()
    {
        var pixel = new Mock<ITexture>();
        using var stage = new InspectablePerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData()), pixel.Object);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, new ParsedChart("loading.dtx")));
        stage.DrawUiPass(null);
        Assert.Same(pixel.Object, stage.DrawnPixel);
        Assert.Equal((new Rectangle(580, 350, 120, 20), Color.White, 0.1f), Assert.Single(stage.Draws));
        stage.Deactivate();
        pixel.Verify(p => p.RemoveReference(), Times.Once);
    }

    [Fact]
    public void DrawGameplayState_WhenLoading_ShouldUseFallbackRectangleDrawerForCenteredText()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, new ParsedChart("loading.dtx")));
        stage.DrawUiPass(null);
        Assert.Equal((new Rectangle(580, 350, 120, 20), Color.White, 0.1f),
            Assert.Single(stage.FallbackRectangles));
    }

    [Fact]
    public void DrawGameplayState_WhenReadyCountdownActive_ShouldUseFallbackRectangleDrawerForPulsingReadyText()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Ready" }, 0, new ParsedChart("ready.dtx")));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(0.125);
        stage.DrawUiPass(null);
        Assert.Equal((new Rectangle(592, 350, 96, 20), Color.Yellow, 0.1f),
            Assert.Single(stage.FallbackRectangles));
        stage.Update(0.25);
        stage.DrawUiPass(null);
        Assert.Equal((new Rectangle(592, 350, 96, 20), Color.Yellow * 0f, 0.1f),
            stage.FallbackRectangles[1]);
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

        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input))
        {
            Playfield = playfield.Object
        };
        var chart = new ParsedChart("timing-gate.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 1, LaneIndex = lane, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Timing gate" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var manager = stage.Rules!.JudgementManager!;
        manager.IsActive = true;
        manager.EnqueueLaneHit(lane, "Test.Manual");
        var deltaMs = judgementType switch
        {
            JudgementType.Perfect => 0,
            JudgementType.Great => 40,
            JudgementType.Good => 80,
            JudgementType.Poor => 120,
            _ => 160
        };
        manager.Update(1000 + deltaMs);

        playfield.Verify(x => x.OnJudgement(
            It.Is<JudgementEvent>(e => e.Lane == lane && e.Type == judgementType),
            It.IsAny<PerformanceFrameState>(), expectedTimingDisplay), Times.Once);
    }

    [Fact]
    public void JudgementAndAutoplay_ShouldUpdateRulesThenPlayfieldAndPlayChipAfterFeedback()
    {
        var config = new ConfigData { EnableLaneFlush = true };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var playfield = new Mock<IPerformancePlayfield>();
        var order = new List<string>();
        playfield.Setup(p => p.OnJudgement(It.IsAny<JudgementEvent>(), It.IsAny<PerformanceFrameState>(), It.IsAny<bool>()))
            .Callback<JudgementEvent, PerformanceFrameState, bool>((judgement, frame, _) =>
            {
                if (judgement.Type == JudgementType.Great)
                {
                    order.Add("visual");
                    Assert.Equal(1, frame.SlowCount);
                    Assert.Equal(1, frame.Combo);
                }
            });
        bool autoFeedback = false;
        playfield.Setup(p => p.OnAutoHit(0)).Callback(() => autoFeedback = true);
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input))
        {
            Playfield = playfield.Object
        };
        var chart = new ParsedChart("visual-hit.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 2, TimeMs = 1000 });
        foreach (var i in Enumerable.Range(0, 9))
            chart.Notes.Add(new Note { LaneIndex = i == 0 ? 3 : 0, TimeMs = 3000 + i * 100 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var scoreManager = stage.Rules!.ScoreManager!;
        var comboManager = stage.Rules.ComboManager!;
        var gaugeManager = stage.Rules.GaugeManager!;
        var skillManager = stage.Rules.SkillManager!;
        var judgementManager = stage.Rules.JudgementManager!;
        scoreManager.ScoreChanged += (_, _) => order.Add("score");
        comboManager.ComboChanged += (_, _) => order.Add("combo");
        gaugeManager.GaugeChanged += (_, _) => order.Add("gauge");
        EventHandler<SkillChangedEventArgs> skillObserver = (_, _) =>
        {
            order.Add("skill");
            Assert.Equal(0, stage.Rules.BuildFrameState(1040).SlowCount);
        };
        skillManager.SkillChanged += skillObserver;

        judgementManager.IsActive = true;
        judgementManager.EnqueueLaneHit(2, "Test.Manual");
        judgementManager.Update(1040);

        Assert.Equal(new[] { "score", "combo", "gauge", "skill", "visual" }, order);
        Assert.Equal(scoreManager.CalculateScoreForJudgement(JudgementType.Great), scoreManager.CurrentScore);
        Assert.Equal(1, comboManager.CurrentCombo);
        Assert.Equal(51.5f, gaugeManager.CurrentLife);
        Assert.Equal(1, stage.Rules.BuildFrameState(1040).GreatCount);
        Assert.Equal(1, stage.Rules.BuildFrameState(1040).SlowCount);
        Assert.Equal(1, stage.Rules.BuildFrameState(1040).MaxCombo);
        skillManager.SkillChanged -= skillObserver;

        judgementManager.EnqueueLaneHit(3, "Test.Manual");
        judgementManager.Update(3160, 1040);
        Assert.Equal(1, stage.Rules.BuildFrameState(3160).MissCount);
        Assert.Equal(1, skillManager.MissCount);
        Assert.Equal(0, judgementManager.GetJudgementCount(JudgementType.Miss));
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chart.Notes[1].Id)!.Status);

        chart.Notes[2].Value = "07";
        bool feedbackWhenChipPlayed = false;
        var chip = new Mock<ISound>();
        chip.Setup(s => s.Play()).Callback(() => feedbackWhenChipPlayed = autoFeedback);
        var prepared = CreatePreparedSetWithChips(new Dictionary<string, ISound> { ["07"] = chip.Object });
        var session = stage.AudioSession!;
        session.Dispose();
        session.Bind(chart, prepared);
        stage.Update(1.1);
        stage.Update(3.1);
        Assert.True(autoFeedback);
        Assert.True(feedbackWhenChipPlayed);
        chip.Verify(s => s.Play(), Times.Once);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chart.Notes[2].Id)!.Status);
    }

    [Fact]
    public async Task LogPerformanceError_WithAndWithoutException_ShouldNotThrow()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            NullLogger<PerformanceStage>.Instance, new ConfigData()));
        stage.Activate();
        stage.Deactivate();
        var chart = CreateSingleNoteChart();
        chart.BackgroundAudioPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-missing.wav");
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Missing audio" }, 0, chart));
        var task = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
        Assert.NotNull(await task.WaitAsync(TimeSpan.FromSeconds(5)));
        UpdateUntil(stage, () => stage.AudioSession != null);
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
        var chart = new ParsedChart("legacy-bgm.dtx");
        chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 40 });
        chart.SetWavPans(new Dictionary<string, int> { ["01"] = -80 });
        chart.BackgroundWavId = "01";
        AssertStartSongAudio(chart, 0.4f, -0.8f);
    }

    [Fact]
    public void InitializeReadyFont_WhenSpriteBatchIsMissing_ShouldLeaveFontNull()
    {
        var resources = new Mock<IResourceManager>();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "No GPU" }, 0, new ParsedChart("headless.dtx")));
        resources.Verify(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        stage.DrawUiPass(null);
        Assert.Single(stage.FallbackRectangles);
    }

    [Fact]
    public void TryLoadTexture_WhenResourceManagerUnavailableOrLoadFails_ShouldReturnNull()
    {
        var missingGame = CreateHeadlessGame(new StageTestLogger(), new ConfigData());
        Mock.Get(missingGame).SetupGet(g => g.ResourceManager).Returns((IResourceManager)null!);
        using var missing = new HeadlessPerformanceStage(missingGame);
        missing.Activate(new SongPlayRequest(new SongListNode { Title = "No resources" }, 0,
            new ParsedChart("no-resources.dtx")));
        missing.LoadPerformanceUIAssets();
        missing.DrawBasePass(new Rectangle(0, 0, 1920, 1080));
        Assert.Single(missing.FallbackBackgrounds);

        var resources = new Mock<IResourceManager>();
        resources.Setup(x => x.TryLoadTexture(TexturePath.PerformanceBackgroundTexture, out It.Ref<ITexture>.IsAny))
            .Throws(new InvalidOperationException("boom"));
        var shutter = CreateTextureMock(96, 512);
        var value = shutter.Object;
        resources.Setup(x => x.TryLoadTexture(TexturePath.Shutter, out value)).Returns(true);
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Failure" }, 0, new ParsedChart("failure.dtx")));
        stage.LoadPerformanceUIAssets();
        stage.DrawBasePass(new Rectangle(0, 0, 1920, 1080));
        stage.DrawUiPass(null);
        Assert.Single(stage.FallbackBackgrounds);
        shutter.Verify(t => t.Draw(null!, It.IsAny<Rectangle>(), null, Color.White, 0f, Vector2.Zero,
            SpriteEffects.None, 0.2f), Times.Once);
    }

    [Fact]
    public void TryLoadTexture_WhenLoadSucceeds_ShouldReturnTexture()
    {
        var texture = CreateTextureMock();
        var resourceManager = new Mock<IResourceManager>();
        var textureResult = texture.Object;
        resourceManager.Setup(x => x.TryLoadTexture(TexturePath.PerformanceBackgroundTexture, out textureResult)).Returns(true);
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resourceManager));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Assets" }, 0, new ParsedChart("assets.dtx")));
        stage.LoadPerformanceUIAssets();
        stage.DrawBasePass(new Rectangle(0, 0, 1280, 720));
        texture.Verify(t => t.Draw(null!, PerformanceUILayout.Background.Bounds, null, Color.White, 0f,
            Vector2.Zero, SpriteEffects.None, 1f), Times.Once);
        // The probe keeps a missing optional asset out of the Warning path.
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WithoutPlayfield_DrawPassesAndJudgement_ShouldRemainSafe()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData { ShowHitTimingFeedback = true }, input));
        var chart = new ParsedChart("no-playfield.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 2, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "No playfield" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);

        stage.DrawPlayfieldBase(null!);
        stage.DrawPlayfieldOverlay(null!);
        stage.DrawPlayfieldHud(null!);
        stage.DrawPlayfieldAdditive(null!);
        var judgement = stage.Rules!.JudgementManager!;
        judgement.IsActive = true;
        judgement.EnqueueLaneHit(2, "Test.Manual");
        judgement.Update(1000);
        Assert.Equal(1, stage.Rules.ComboManager!.CurrentCombo);
    }

    #region HPA-11 Chart Background Video

    [Fact]
    public void ProcessVideoEvents_WhenChartHasNoVideoEvents_ShouldNotStartPlayer()
    {
        using var stage = CreateVideoStage(out var player, CreateVideoChart());
        AdvanceVideo(stage, 10_000);

        Assert.Empty(player.StartedPaths);
        Assert.Empty(player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenBeforeFirstEvent_ShouldNotStartPlayer()
    {
        using var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(5000.0, "movie.avi")));
        AdvanceVideo(stage, 4999);

        Assert.Empty(player.StartedPaths);
        Assert.Empty(player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenEventBecomesDue_ShouldStartOnceAndForwardRelativeMediaTime()
    {
        using var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));
        AdvanceVideo(stage, 1000);

        Assert.Equal(new[] { "movie.avi" }, player.StartedPaths);
        Assert.Equal(new[] { 500.0 }, player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenSeveralEventsAreDue_ShouldStartOnlyLastDueEvent()
    {
        using var stage = CreateVideoStage(out var player, CreateVideoChart(
            VideoEvent(100.0, "first.avi", "01"),
            VideoEvent(300.0, "second.avi", "02"),
            VideoEvent(700.0, "third.avi", "03")));

        AdvanceVideo(stage, 800);

        Assert.Equal(new[] { "third.avi" }, player.StartedPaths);
        Assert.Single(player.UpdateValues);
        Assert.Equal(100.0, player.UpdateValues[0]);
    }

    [Fact]
    public void ProcessVideoEvents_WhenLaterUpdateArrives_ShouldForwardExactLogicalMediaTimeWithoutRescaling()
    {
        // The stage runs frozen at 50% play speed (set by CreateVideoStage); the
        // forwarded media time must stay raw logical chart ms, never rescaled.
        using var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));
        AdvanceVideo(stage, 1000);
        AdvanceVideo(stage, 500);

        Assert.Single(player.StartedPaths);
        Assert.Equal(new[] { 500.0, 1000.0 }, player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenUnresolvedEventIsDue_ShouldLeaveFallbackAndAllowLaterValidEvent()
    {
        using var stage = CreateVideoStage(out var player, CreateVideoChart(
            VideoEvent(500.0, "", "01"),
            VideoEvent(700.0, "movie.avi", "02")));

        AdvanceVideo(stage, 600);

        Assert.Empty(player.StartedPaths);
        Assert.Empty(player.UpdateValues);

        AdvanceVideo(stage, 200);

        Assert.Equal(new[] { "movie.avi" }, player.StartedPaths);
        Assert.Equal(new[] { 100.0 }, player.UpdateValues);
    }

    [Fact]
    public void ProcessVideoEvents_WhenUnresolvedEventSupersedesActiveEvent_ShouldStopActiveAndClear()
    {
        // A valid event starts the video, then a later unresolved event (empty
        // path) becomes due: the active generation must be stopped and the
        // active event cleared so the static background reappears.
        using var stage = CreateVideoStage(out var player, CreateVideoChart(
            VideoEvent(500.0, "movie.avi", "01"),
            VideoEvent(900.0, "", "02")));

        AdvanceVideo(stage, 600);
        Assert.Equal(new[] { "movie.avi" }, player.StartedPaths);

        AdvanceVideo(stage, 400);

        Assert.Equal(1, player.StopCount);
        // No further Update calls after the active event is cleared.
        Assert.Equal(new[] { 100.0 }, player.UpdateValues);
    }

    [Fact]
    public void StopGameplayAudioInstances_WhenVideoIsActive_ShouldStopPlayer()
    {
        using var input = new SwitchableBackInputManager();
        using var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")), input);
        AdvanceVideo(stage, 1000);
        input.BackTriggered = true;
        stage.Update(0);

        Assert.Equal(1, player.StopCount);
    }

    [Fact]
    public void CleanupComponents_WhenVideoPlayerExists_ShouldDisposeAndClearPlayer()
    {
        using var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));
        stage.Deactivate();

        Assert.Equal(1, player.DisposeCount);
        stage.Deactivate();
        Assert.Equal(1, player.DisposeCount);
    }

    [Fact]
    public void VideoPlayer_ReactivationResetsScheduleAndDisposesEachGenerationOwner()
    {
        var chart = CreateVideoChart(VideoEvent(500, "movie.avi"));
        using var stage = CreateVideoStage(out var first, chart);
        AdvanceVideo(stage, 1000);
        stage.Deactivate();
        Assert.Equal(1, first.DisposeCount);
        var second = new RecordingChartVideoPlayer();
        stage.VideoPlayer = second;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Again" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        AdvanceVideo(stage, 1000);
        Assert.Equal(new[] { "movie.avi" }, second.StartedPaths);
        Assert.Equal(new[] { 500d }, second.UpdateValues);
        Assert.Equal(new[] { 500d }, first.UpdateValues);
        stage.Deactivate();
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task VideoPlayer_MissingFileLeavesStaticBackgroundAndIsDisposed()
    {
        var logs = new ConcurrentQueue<string>();
        var player = new FfmpegChartVideoPlayer(work => Task.Run(work), null, logs.Enqueue);
        var background = CreateTextureMock(1280, 720);
        var resources = new Mock<IResourceManager>();
        var texture = background.Object;
        resources.Setup(r => r.TryLoadTexture(TexturePath.PerformanceBackgroundTexture, out texture)).Returns(true);
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resources))
            { VideoPlayer = player };
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-missing.avi");
        var chart = CreateVideoChart(VideoEvent(100, missing));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Missing video" }, 0, chart));
        stage.LoadPerformanceUIAssets();
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        stage.DrawBasePass(new Rectangle(0, 0, 1280, 720));
        stage.Update(0.2);
        Assert.NotNull(player.GenerationTask);
        await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(logs);
        Assert.False(player.HasCurrentFrame);
        stage.DrawBasePass(new Rectangle(0, 0, 1280, 720));
        background.Verify(t => t.Draw(null!, PerformanceUILayout.Background.Bounds, null, Color.White, 0f,
            Vector2.Zero, SpriteEffects.None, 1f), Times.Exactly(2));
        stage.Deactivate();
        Assert.Throws<ObjectDisposedException>(() => player.Start(missing));
    }

    [Fact]
    public void ResolveChartVideoDrawLayout_ShouldPinBackgroundBoundsAndDepth()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        var (bounds, depth) = stage.ResolveChartVideoDrawLayout();

        Assert.Equal(PerformanceUILayout.Background.Bounds, bounds);
        Assert.Equal(0.95f, depth);
        Assert.True(depth > PerformanceUILayout.NotationStrip.DepthBackground);
        Assert.Equal(255, SkinPalette.Default.StripBackground.A);
    }

    [Fact]
    public void DrawBackground_WhenVideoPlayerDraws_ShouldConsumePinnedLayout()
    {
        using var stage = CreateVideoStage(out var player, CreateVideoChart(VideoEvent(500.0, "movie.avi")));
        AdvanceVideo(stage, 1000);
        stage.DrawBasePass(new Rectangle(0, 0, 1280, 720));

        var draw = Assert.Single(player.DrawCalls);
        Assert.Equal(PerformanceUILayout.Background.Bounds, draw.Bounds);
        Assert.Equal(0.95f, draw.Depth);
    }

    [Fact]
    public void BasePass_DrawsStaticBackgroundThenVideoBeforePlayfield()
    {
        var order = new List<string>();
        var background = CreateTextureMock(1280, 720);
        background.Setup(t => t.Draw(null!, PerformanceUILayout.Background.Bounds, null, Color.White, 0f,
            Vector2.Zero, SpriteEffects.None, 1f)).Callback(() => order.Add("background"));
        var resources = new Mock<IResourceManager>();
        var texture = background.Object;
        resources.Setup(r => r.TryLoadTexture(TexturePath.PerformanceBackgroundTexture, out texture)).Returns(true);
        var player = new RecordingChartVideoPlayer { OnDraw = () => order.Add("video") };
        var playfield = new Mock<IPerformancePlayfield>();
        playfield.Setup(p => p.DrawBase(null!, It.IsAny<ChartManager?>(), It.IsAny<bool>(), It.IsAny<double>()))
            .Callback(() => order.Add("playfield"));
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resources))
            { VideoPlayer = player, Playfield = playfield.Object };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Order" }, 0, CreateVideoChart()));
        stage.LoadPerformanceUIAssets();
        stage.DrawBasePass(new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(new[] { "background", "video", "playfield" }, order);
        Assert.Equal((PerformanceUILayout.Background.Bounds, 0.95f),
            (Assert.Single(player.DrawCalls).Bounds, player.DrawCalls[0].Depth));
    }

    #endregion

    private static HeadlessPerformanceStage CreateVideoStage(out RecordingChartVideoPlayer player, ParsedChart chart,
        MockInputManagerCompat? input = null)
    {
        player = new RecordingChartVideoPlayer();
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData { PlaySpeedPercent = 50 }, input);
        Mock.Get(game).Setup(g => g.CanPerformStageTransition()).Returns(true);
        var stage = new HeadlessPerformanceStage(game)
            { VideoPlayer = player };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Video" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        return stage;
    }

    private static void AdvanceVideo(PerformanceStage stage, double logicalMs) => stage.Update(logicalMs / 500);

    private static ParsedChart CreateVideoChart(params ChartVideoEvent[] events)
    {
        var chart = new ParsedChart("video-schedule.dtx") { Bpm = 120.0, DurationMs = 60000 };
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
        public Action? OnDraw { get; init; }

        public void Start(string videoFilePath) => StartedPaths.Add(videoFilePath);

        public void Update(double mediaTimeMs) => UpdateValues.Add(mediaTimeMs);

        public void Draw(SpriteBatch spriteBatch, Rectangle destinationBounds, float layerDepth)
        {
            DrawCalls.Add(new RecordedVideoDraw(destinationBounds, layerDepth));
            OnDraw?.Invoke();
        }

        public void Stop() => StopCount++;

        public void Dispose() => DisposeCount++;
    }

    #region HPA-509 Tab/Lanes spawn gating (OnJudgementMade)

    private static ParsedChart CreateTabFeedbackChart()
    {
        var chart = new ParsedChart("tab-feedback.dtx");
        chart.AddNote(new Note(5, 0, 96, 0x13, "01"));
        chart.AddNote(new Note(3, 0, 96, 0x12, "01"));
        chart.FinalizeChart();
        return chart;
    }

    [Fact]
    public void OnJudgementMade_WhenTabAndTimingFeedbackHidden_ShouldOmitPerHitTimingLineOnly()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, ShowHitTimingFeedback = false };
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), config, input, CreateNotationResourceManager(true)));
        var chart = CreateTabFeedbackChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var tabHud = stage.LastHud!;
        var manager = stage.Rules!.JudgementManager!;
        manager.IsActive = true;
        manager.EnqueueLaneHit(3);
        manager.Update(chart.Notes.First(n => n.LaneIndex == 3).TimeMs - 12);

        // The popup still appears, but the per-hit fast/slow line obeys the same
        // opt-in predicate as the Lanes timing display. Stat tiles stay ungated.
        Assert.Equal("PERFECT", tabHud.PopupForTesting!.Text);
        Assert.Null(tabHud.PopupTimingForTesting.Text);
    }

    [Fact]
    public void OnJudgementMade_WhenTabPartialAutoPlayLane_ShouldKeepPlayerPopupAndTiming()
    {
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, ShowHitTimingFeedback = true };
        config.AutoPlayLanes.Add(3);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), config, input, CreateNotationResourceManager(true)));
        var chart = CreateTabFeedbackChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var tabHud = stage.LastHud!;
        var manager = stage.Rules!.JudgementManager!;
        manager.IsActive = true;
        manager.EnqueueLaneHit(5);
        manager.Update(chart.Notes.First(n => n.LaneIndex == 5).TimeMs - 12);
        var playerPopup = tabHud.PopupForTesting;
        Assert.NotNull(playerPopup);

        Assert.True(manager.ResolveAutoHit(chart.Notes.First(n => n.LaneIndex == 3).Id));

        Assert.Same(playerPopup, tabHud.PopupForTesting);
        Assert.Equal("-12", tabHud.PopupTimingForTesting.Text);
        Assert.False(tabHud.PopupTimingForTesting.IsSlow);
        Assert.Equal(1f, tabHud.FlashTimersForTesting[3]);
    }

    [Fact]
    public void OnJudgementMade_WhenTabFullAutoPlay_ShouldFeedPopupFromAutomatedLanes()
    {
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, ShowHitTimingFeedback = true };
        config.AutoPlayLanes.UnionWith(Enumerable.Range(0, PerformanceUILayout.LaneCount));
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), config, input, CreateNotationResourceManager(true)));
        var chart = CreateTabFeedbackChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var tabHud = stage.LastHud!;
        var manager = stage.Rules!.JudgementManager!;
        manager.IsActive = true;
        Assert.True(manager.ResolveAutoHit(chart.Notes.First(n => n.LaneIndex == 3).Id));

        Assert.Equal("PERFECT", tabHud.PopupForTesting!.Text);
        Assert.Null(tabHud.PopupTimingForTesting.Text);
        Assert.Equal(1f, tabHud.FlashTimersForTesting[3]);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WhenNotationStripActive_ShouldFlashTabHudLane()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab },
            input, CreateNotationResourceManager(true)));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, CreateTabFeedbackChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var tabHud = stage.LastHud!;
        input.ModularInputManager.KeyBindings.BindButton("Test.Tab", 3);
        Assert.True(input.ModularInputManager.InjectButton("Test.Tab", true));
        input.ModularInputManager.Update();

        Assert.Equal(1f, tabHud.FlashTimersForTesting[3]);
        Assert.Equal(PerformanceUILayout.GetLaneColor(3), tabHud.FlashColorsForTesting[3]);
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WhenNotationStripMissing_ShouldNotFlashTabHudLane()
    {
        using var input = new MockInputManagerCompat();
        using var tabHud = TabPlayfieldTests.CreateUnreadyHud();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab },
            input, CreateNotationResourceManager(true))) { MakeHud = () => tabHud };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fallback" }, 0, CreateTabFeedbackChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        input.ModularInputManager.KeyBindings.BindButton("Test.Tab", 3);
        Assert.True(input.ModularInputManager.InjectButton("Test.Tab", true));
        input.ModularInputManager.Update();

        Assert.Equal(0f, tabHud.FlashTimersForTesting[3]);
    }

    #endregion

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

    private static Mock<ITexture> CreateTextureMock(int width = 1, int height = 1)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(width);
        texture.SetupGet(x => x.Height).Returns(height);
        texture.Setup(x => x.RemoveReference());
        return texture;
    }

    private static HeadlessPerformanceStage CreateVisualStage(string path, ITexture? texture,
        MockInputManagerCompat? input = null)
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.TryLoadTexture(path, out texture!)).Returns(texture != null);
        var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input, resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Visual" }, 0,
            new ParsedChart("visual.dtx") { DurationMs = 10000 }));
        stage.LoadPerformanceUIAssets();
        return stage;
    }

    private static Mock<ISound> CreateSoundMock()
    {
        var sound = new Mock<ISound>();
        sound.Setup(x => x.Dispose());
        return sound;
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

    internal static PreparedGameplayAudioSet CreatePreparedSetWithChips(
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

    // Playback instances are not exposed by the natural chip cache; wrap real
    // sounds before countdown to observe ordering, separately testing the natural bind.
    private static async Task<Dictionary<string, ObservedSound>> RebindObservedAudio(
        PerformanceStage stage, ParsedChart chart)
    {
        var sounds = new Dictionary<string, ObservedSound>(StringComparer.Ordinal);
        var prepared = await PreparedGameplayAudioSet.PrepareAsync(
            chart.BackgroundAudioPath,
            chart.BGMEvents.Select(e => e.AudioFilePath),
            chart.WavDefinitions,
            new PlaybackModifiers(100, 0),
            processor: null,
            cache: null,
            progress: null,
            cancellationToken: CancellationToken.None,
            defaultSoundLoader: path =>
            {
                var sound = new ObservedSound(new ManagedSound(path, path));
                sounds.Add(Path.GetFullPath(path), sound);
                return sound;
            },
            preparedSoundFactory: (_, _) => throw new InvalidOperationException("Default profile expected"));
        var session = stage.AudioSession!;
        session.Dispose();
        session.Bind(chart, prepared);
        return sounds;
    }

    private sealed class ObservedSound : ISound
    {
        private readonly ISound _sound;

        public ObservedSound(ISound sound) => _sound = sound;
        public SoundEffect SoundEffect => _sound.SoundEffect;
        public string SourcePath => _sound.SourcePath;
        public TimeSpan Duration => _sound.Duration;
        public bool IsDisposed => _sound.IsDisposed;
        public int ReferenceCount => _sound.ReferenceCount;
        public List<SoundEffectInstance> Instances { get; } = new();
        public Action? OnPlay { get; set; }
        public void AddReference() => _sound.AddReference();
        public void RemoveReference() => _sound.RemoveReference();
        public SoundEffectInstance CreateInstance() => _sound.CreateInstance();
        public SoundEffectInstance Play() => Record(_sound.Play());
        public SoundEffectInstance Play(float volume) => Record(_sound.Play(volume));
        public SoundEffectInstance Play(float volume, float pitch, float pan) => Record(_sound.Play(volume, pitch, pan));
        public void Dispose() => _sound.Dispose();

        private SoundEffectInstance Record(SoundEffectInstance instance)
        {
            Instances.Add(instance);
            OnPlay?.Invoke();
            return instance;
        }
    }

    private static (ParsedChart Chart, PreparedGameplayAudioSet PreparedSet) PrepareLiveAudio(
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

        return (chart, preparedSet);
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

        public InvalidOperationException Failure { get; } =
            new InvalidOperationException(
                "Simulated variant processor failure",
                new InvalidDataException("inner chain"));
        public IPerformancePlayfield? Playfield { get; set; }
        protected override IPerformancePlayfield? CreateLanesPlayfield(GraphicsDevice? device, Texture2D? whitePixel) => Playfield;

        // The sentinel is captured when the stage resolves the factory (update
        // thread) so the worker-side invocation throws it without touching
        // stage state — and still after chart publication.
        protected override Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
        {
            var failure = Failure;
            return () => throw failure;
        }

        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory() =>
            () => throw new InvalidOperationException("The throwing processor must not construct a cache.");
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

        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory() =>
            () => throw new InvalidOperationException("Factory resolution must fail before constructing a cache.");
    }

    private sealed class CancelingAudioVariantStage : PerformanceStage
    {
        private readonly IsolatedStageAudioCache _cache = new();
        public CancelingAudioVariantStage(IStageGame game)
            : base(game)
        {
        }

        protected override Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
            => () => new CancelingProcessor();

        // The cache path is a snapshot input, so the worker-side construction
        // touches only the captured string — never live stage state.
        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory()
            => _cache.Factory();

        internal override Task<PreparedGameplayAudioSet?> StartGameplayInitialization() =>
            _cache.Track(base.StartGameplayInitialization());

        public void ReleaseAndDeleteSources(string directory)
        {
            Deactivate();
            // The canceling processor never reads its source.
            _cache.Workers.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Directory.Delete(directory, recursive: true);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _cache.Release();
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
        private readonly ITexture _pixel;
        public InspectablePerformanceStage(IStageGame game, ITexture pixel) : base(game) => _pixel = pixel;
        public List<(Rectangle, Color, float)> Draws { get; } = new();
        public ITexture? DrawnPixel { get; private set; }

        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory()
            => () => throw new InvalidOperationException("Default-profile test does not use a cache.");
        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
        protected override ITexture? CreateFallbackWhiteTexture() => _pixel;
        internal override void DrawFallbackTexture(ITexture texture, Rectangle rectangle, Color color, float depth)
        {
            DrawnPixel = texture;
            Draws.Add((rectangle, color, depth));
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

    private sealed class SwitchableBackInputManager : MockInputManagerCompat
    {
        public bool BackTriggered { get; set; }
        public override bool IsBackActionTriggered() => BackTriggered;
    }

    [Fact]
    public void OnActivate_WhenConfigManagerNotNull_ShouldSubscribeToScrollSpeedChanged()
    {
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData());
        var manager = Mock.Get(game.ConfigManager);
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessPerformanceStage(game) { Playfield = playfield.Object };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        manager.Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(100, 200));
        playfield.Verify(p => p.OnScrollSpeedChanged(200), Times.Once);
        stage.Deactivate();
        manager.Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(200, 225));
        playfield.Verify(p => p.OnScrollSpeedChanged(225), Times.Never);
    }

    [Fact]
    public void Deactivate_WhenNeverActivated_ShouldBeSafeWithoutSubscription()
    {
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData());
        using var stage = new HeadlessPerformanceStage(game);
        stage.Deactivate();
        Mock.Get(game.ConfigManager).Verify(m => m.FlushPendingSave(), Times.Never);
        Assert.Null(stage.AudioSession);
    }

    [Fact]
    public void OnDeactivate_WhenSubscribedConfigManager_ShouldUnsubscribeFlushAndCleanup()
    {
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData());
        var manager = Mock.Get(game.ConfigManager);
        var playfield = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessPerformanceStage(game) { Playfield = playfield.Object };
        var request = new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart());
        stage.Activate(request);
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(0.75);
        var oldSession = stage.AudioSession!;
        stage.Deactivate();

        manager.Verify(x => x.FlushPendingSave(), Times.Once);
        Assert.Null(stage.AudioSession);
        Assert.Null(oldSession.Timer);
        Assert.Null(stage.Rules!.ChartManager);
        Assert.False(stage.Rules.IsCompleted);
        manager.Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(100, 200));
        playfield.Verify(p => p.OnScrollSpeedChanged(200), Times.Never);

        stage.Activate(request);
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(0.75);
        Assert.False(stage.AudioSession!.IsPlaying);
        stage.Update(0.25);
        Assert.True(stage.AudioSession.IsPlaying);
        manager.Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(200, 225));
        playfield.Verify(p => p.OnScrollSpeedChanged(225), Times.Once);
        stage.Deactivate();
        manager.Verify(x => x.FlushPendingSave(), Times.Exactly(2));
    }

    [Fact]
    public void Deactivate_WhenAlreadyDeactivated_ShouldNotFlushAgain()
    {
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData());
        using var stage = new HeadlessPerformanceStage(game);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, CreateSingleNoteChart()));
        stage.Deactivate();
        stage.Deactivate();
        Mock.Get(game.ConfigManager).Verify(m => m.FlushPendingSave(), Times.Once);
        Assert.Null(stage.AudioSession);
    }

    [Theory]
    [InlineData(InputCommandType.IncreaseScrollSpeed, 1)]
    [InlineData(InputCommandType.DecreaseScrollSpeed, -1)]
    public void HandleInput_ScrollSpeedHotkeysWorkDuringCountdown(InputCommandType command, int adjustment)
    {
        using var input = new ScrollSpeedInputManagerCompat(command);
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input);
        using var stage = new HeadlessPerformanceStage(game);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Scroll" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        Assert.False(stage.AudioSession!.IsPlaying);
        var config = Mock.Get(game.ConfigManager);
        config.Invocations.Clear();
        stage.Update(0.1);
        config.Verify(x => x.AdjustScrollSpeed(adjustment), Times.Once);
        config.Verify(x => x.AdjustScrollSpeed(-adjustment), Times.Never);
    }

    [Fact]
    public void HandleInput_WhenNoConfigManager_ShouldNotThrowOnScrollSpeed()
    {
        using var input = new ScrollSpeedInputManagerCompat(InputCommandType.IncreaseScrollSpeed);
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input);
        using var stage = new HeadlessPerformanceStage(game);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Scroll" }, 0, CreateSingleNoteChart()));
        Mock.Get(game).SetupGet(g => g.ConfigManager).Returns((IConfigManager)null!);
        stage.Update(0.1);
    }

    [Fact]
    public void OnScrollSpeedChanged_ShouldShowIndicator()
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>())).Returns(new Mock<IFont>().Object);
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resources);
        using var stage = new HeadlessPerformanceStage(game);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Scroll" }, 0, CreateSingleNoteChart()));
        var indicator = stage.InitializeFonts();
        Mock.Get(game.ConfigManager).Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(50, 75));
        Assert.Equal("Scroll Speed " + ScrollSpeedRange.Format(75), indicator.Text);
        stage.Deactivate();
        Mock.Get(game.ConfigManager).Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(75, 100));
        Assert.Equal("Scroll Speed " + ScrollSpeedRange.Format(75), indicator.Text);
    }

    [Fact]
    public void OnScrollSpeedChanged_WhenPlayfieldNull_ShouldNotThrow()
    {
        var game = CreateHeadlessGame(new StageTestLogger(), new ConfigData());
        using var stage = new HeadlessPerformanceStage(game);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Scroll" }, 0, CreateSingleNoteChart()));
        Mock.Get(game.ConfigManager).Raise(m => m.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(50, 75));
        stage.Deactivate();
    }

    [Fact]
    public void StartSong_WhenNoBgmEvents_ShouldSetVolumeToOne()
    {
        AssertStartSongAudio(new ParsedChart("startsong-default.dtx"), 1.0f, 0.0f);
    }

    [Fact]
    public void StartSong_WhenBgmEventsExist_ShouldMuteBackgroundAudio()
    {
        var bgmChart = new ParsedChart("startsong-mute.dtx") { Bpm = 120.0 };
        bgmChart.AddBGMEvent(new BGMEvent { WavId = "01", TimeMs = 100.0 });
        AssertStartSongAudio(bgmChart, 0.0f, 0.0f);
    }

    private static void AssertStartSongAudio(ParsedChart chart, float volume, float pan)
    {
        var directory = CreateTempDirectory();
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        try
        {
            chart.DurationMs = 10000;
            chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 5000 });
            chart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory, "start-main.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Start" }, 0, chart));
            UpdateUntil(stage, () => stage.AudioSession != null);
            var session = stage.AudioSession!;
            Assert.NotNull(session.PreparedSet!.MainBackground);
            session.Timer!.Volume = 0.3f;
            Assert.Equal(0.3f, session.Timer.Volume);
            Assert.False(stage.Rules!.JudgementManager!.IsActive);

            stage.Update(1.0);

            Assert.True(session.IsPlaying);
            Assert.Equal(volume, session.Timer.Volume);
            Assert.Equal(pan, session.Timer.Pan);
            Assert.True(stage.Rules.JudgementManager.IsActive);
            stage.Deactivate();
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void StartSong_WhileAudioIsPreparing_DoesNotStartOrConsumeCountdown()
    {
        var directory = CreateTempDirectory();
        using var input = new MockInputManagerCompat();
        using var stage = new GatedVariantStage(CreateHeadlessGame(
            new StageTestLogger(), new ConfigData { PlaySpeedPercent = 50 }, input));
        try
        {
            var chart = CreateSingleNoteChart();
            chart.BackgroundAudioPath = CreatePlaceholderSource(directory, "start-loading.wav");
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, chart));
            Assert.True(SpinWait.SpinUntil(() => stage.ProcessorEntered.Task.IsCompleted, TimeSpan.FromSeconds(5)));
            UpdateUntil(stage, () => stage.Rules?.JudgementManager != null);
            stage.Update(2.0);
            Assert.Null(stage.AudioSession);
            Assert.False(stage.Rules!.JudgementManager!.IsActive);

            stage.ReleaseProcessor.TrySetResult();
            UpdateUntil(stage, () => stage.AudioSession != null);
            stage.Update(0.75);
            Assert.False(stage.AudioSession!.IsPlaying);
            stage.Update(0.25);
            Assert.True(stage.AudioSession.IsPlaying);
            Assert.True(stage.Rules.JudgementManager.IsActive);
        }
        finally
        {
            stage.ReleaseAndDeleteSources(directory);
        }
    }

    [Fact]
    public void DrawGameplayState_WhenNotLoadingAndNotReady_ShouldNotDraw()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Playing" }, 0, new ParsedChart("playing.dtx")));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        stage.DrawUiPass(null);
        Assert.Empty(stage.FallbackRectangles);
    }

    [Fact]
    public void Constructor_WithIStageGame_ShouldInitializeCoreSystems()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        Assert.Equal(StageType.Performance, stage.Type);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Constructor" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1);
        Assert.True(stage.AudioSession!.IsPlaying);
        Assert.True(stage.Rules!.JudgementManager!.IsActive);
        stage.Deactivate();
        Assert.False(stage.IsActive);
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
        Assert.Null(gameplay.LastLaneHitLane);
        stage.Update(0.1);
        stage.PopulateTelemetry(gameplay);
        Assert.Equal(1, gameplay.LastLaneHitLane);
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
        PerformanceFrameState frameAtTransition = default;
        int? skillPoorAtTransition = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) =>
            {
                result = (PerformanceResult)payload;
                telemetryAtTransition = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(telemetryAtTransition);
                skillPoorAtTransition = stage.Rules!.SkillManager!.PoorCount;
                frameAtTransition = stage.Rules.BuildFrameState(1120);
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
        Assert.Equal(0, frameAtTransition.PoorCount);
        Assert.Equal(0, frameAtTransition.SlowCount);
        Assert.Equal(1, stage.Rules!.SkillManager!.PoorCount);
        Assert.Equal(1, stage.Rules.BuildFrameState(1120).PoorCount);
        Assert.Equal(1, stage.Rules.BuildFrameState(1120).SlowCount);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
        stage.Deactivate();
    }

    [Fact]
    public void RiskyFailure_SynchronousDeactivationStopsFanoutAfterGauge()
    {
        var config = new ConfigData { Risky = 1 };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var manager = new Mock<IStageManager>();
        PerformanceResult? result = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) =>
            {
                result = (PerformanceResult)payload;
                stage.Deactivate();
            });
        stage.StageManager = manager.Object;
        var chart = new ParsedChart("failure-teardown.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var rules = stage.Rules!;
        var judgement = rules.JudgementManager!;
        var skill = rules.SkillManager!;
        var gauge = rules.GaugeManager!;
        judgement.IsActive = true;
        Assert.True(judgement.ResolveAutoHit(chart.Notes[0].Id));
        int gaugeNotifications = 0;
        gauge.GaugeChanged += (_, _) => gaugeNotifications++;

        judgement.EnqueueLaneHit(1, "Test.Manual");
        judgement.Update(1120, 0);

        Assert.NotNull(result);
        Assert.Equal(CompletionReason.PlayerFailed, result.Summary.CompletionReason);
        Assert.Equal(500000, result.Summary.Score);
        Assert.Equal(1, result.Summary.MaxCombo);
        Assert.Equal(1, result.Summary.PoorCount);
        Assert.Equal(SongScore.CalculatePlayingSkill(2, 1, 0, 1), result.Summary.PlayingSkill);
        Assert.Equal(0, skill.PoorCount);
        Assert.Equal(1, gaugeNotifications);
        Assert.Null(rules.JudgementManager);
        Assert.Null(rules.ScoreManager);
        Assert.Null(rules.ComboManager);
        Assert.Null(rules.GaugeManager);
        Assert.Null(rules.SkillManager);
        Assert.Equal(0, rules.BuildFrameState(1120).SlowCount);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
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
        Assert.Equal(1, stage.Rules!.BuildFrameState(1160).MissCount);
        stage.Deactivate();
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    public void SongCompletion_UsesRealTimeBufferAndFrozenRunMetadata(int speed)
    {
        var config = new ConfigData { PlaySpeedPercent = speed, PitchSemitones = -4 };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var firstChart = new SongChart { DrumLevel = 20, DrumLevelDec = 7, HasDrumChart = true };
        var selectedChart = new SongChart { DrumLevel = 78, DrumLevelDec = 33, HasDrumChart = true };
        var song = new SongListNode
        {
            Title = "Completion",
            DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Charts = new List<SongChart> { firstChart, selectedChart }
            }
        };
        var chart = new ParsedChart("completion.dtx") { DurationMs = 1000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 100 });
        var manager = new Mock<IStageManager>();
        PerformanceResult? result = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, transition, payload) =>
            {
                Assert.IsType<InstantTransition>(transition);
                result = Assert.IsType<PerformanceResult>(payload);
                Assert.False(stage.AudioSession!.IsPlaying);
                Assert.False(stage.Rules!.JudgementManager!.IsActive);
                stage.Update(0);
            });
        stage.StageManager = manager.Object;
        stage.Activate(new SongPlayRequest(song, 1, chart));
        config.PlaySpeedPercent = speed == 100 ? 150 : 100;
        config.PitchSemitones = 12;
        config.AutoPlayLanes.Clear();
        UpdateUntil(stage, () => stage.AudioSession != null);
        Assert.Null(stage.Rules!.CheckCompletion(1000, chart.DurationMs, -100));
        stage.Update(1.1);
        stage.Update(2.0);
        Assert.Null(result);
        stage.Update(2.999);
        Assert.Null(result);
        stage.Update(0.002);

        Assert.NotNull(result);
        Assert.Same(song, result.Song);
        Assert.Equal(1, result.Difficulty);
        Assert.Equal(CompletionReason.SongComplete, result.Summary.CompletionReason);
        Assert.True(result.Summary.ClearFlag);
        Assert.True(result.Summary.UsedAutoPlay);
        Assert.False(result.Summary.IsSavable);
        Assert.Equal(speed, result.Summary.PlaySpeedPercent);
        Assert.Equal(-4, result.Summary.PitchSemitones);
        Assert.Equal(78, result.Summary.ChartLevel);
        Assert.Equal(33, result.Summary.ChartLevelDec);
        Assert.Equal(1000000, result.Summary.Score);
        Assert.Equal(1, result.Summary.PerfectCount);
        Assert.Equal(SongScore.CalculateGameSkill(result.Summary.PlayingSkill, 78, 33), result.Summary.GameSkill);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    [AudioFact]
    public void SongCompletion_StopsLiveBgmAndChipBeforeResultTransition() => AssertAudioStoppedAtResult(false);

    [AudioFact]
    public void GaugeFailure_StopsLiveBgmAndChipBeforeResultTransition() => AssertAudioStoppedAtResult(true);

    [Fact]
    public void GaugeFailure_DoesNotReadMutableConfigDuringSynchronousFinalization()
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.Config).Returns(new ConfigData { Risky = 1 });
        using var input = new MockInputManagerCompat();
        var game = new Mock<IStageGame>();
        game.SetupGet(g => g.ConfigManager).Returns(config.Object);
        game.SetupGet(g => g.InputManager).Returns(input);
        game.SetupGet(g => g.ResourceManager).Returns(new Mock<IResourceManager>().Object);
        using var stage = new HeadlessPerformanceStage(game.Object);
        var chart = new ParsedChart("fail-without-config.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        var manager = new Mock<IStageManager>();
        PerformanceResult? result = null;
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) => result = (PerformanceResult)payload);
        stage.StageManager = manager.Object;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Frozen" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        config.SetupGet(c => c.Config).Throws(new InvalidOperationException("live config was read"));

        stage.Rules!.JudgementManager!.Update(1201);

        Assert.NotNull(result);
        Assert.Equal(CompletionReason.PlayerFailed, result.Summary.CompletionReason);
        Assert.Equal(1, result.Summary.MissCount);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    [Fact]
    public void SongCompletion_WithoutStageManagerStillStopsAndCompletes()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "No manager" }, 0,
            new ParsedChart("no-manager.dtx") { DurationMs = 1000 }));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        stage.Update(1);
        stage.Update(3.001);
        Assert.True(stage.Rules!.IsCompleted);
        Assert.False(stage.AudioSession!.IsPlaying);
        Assert.False(stage.Rules.JudgementManager!.IsActive);
    }

    private static void AssertAudioStoppedAtResult(bool fail)
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData { Risky = 1 }, input));
        using var bgm = PlayingSoundInstance.Create();
        using var chip = PlayingSoundInstance.Create();
        var manager = new Mock<IStageManager>();
        PerformanceResult? result = null;
        var chart = new ParsedChart("audio-result.dtx") { DurationMs = 1000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 100 });
        manager.Setup(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()))
            .Callback<StageType, IStageTransition, StageTransitionPayload>((_, _, payload) =>
            {
                result = Assert.IsType<PerformanceResult>(payload);
                Assert.Equal(SoundState.Stopped, bgm.Instance.State);
                Assert.Equal(SoundState.Stopped, chip.Instance.State);
                Assert.False(stage.AudioSession!.IsPlaying);
                Assert.False(stage.Rules!.JudgementManager!.IsActive);
                var telemetry = new GameTelemetrySnapshot();
                stage.PopulateTelemetry(telemetry);
                Assert.True(telemetry.StageCompleted);
            });
        stage.StageManager = manager.Object;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Audio" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var session = stage.AudioSession!;
        var (audioChart, prepared) = PrepareLiveAudio(bgm.Instance, chip.Instance);
        session.Dispose();
        session.Bind(audioChart, prepared);
        stage.Update(1.1);
        session.Advance(150);
        session.PlayChip(new Note { Value = "07" });
        Assert.Equal(SoundState.Playing, bgm.Instance.State);
        Assert.Equal(SoundState.Playing, chip.Instance.State);

        if (fail)
            stage.Update(0.301);
        else
        {
            Assert.True(stage.Rules!.JudgementManager!.ResolveAutoHit(chart.Notes[0].Id));
            stage.Update(1);
            stage.Update(3.001);
        }

        Assert.NotNull(result);
        Assert.Equal(fail ? CompletionReason.PlayerFailed : CompletionReason.SongComplete, result.Summary.CompletionReason);
        Assert.Equal(!fail, result.Summary.ClearFlag);
        Assert.False(bgm.Instance.IsDisposed);
        Assert.False(chip.Instance.IsDisposed);
        manager.Verify(m => m.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<StageTransitionPayload>()), Times.Once);
    }

    /// <summary>
    /// Test-only <see cref="PerformanceStage"/> that overrides <see cref="PerformanceStage.CreateSpriteBatch"/>
    /// to return null, so the constructor can be exercised without a live GraphicsDevice.
    /// </summary>
    private sealed class HeadlessPerformanceStage : PerformanceStage
    {
        private readonly IsolatedStageAudioCache _cache = new();
        public HeadlessPerformanceStage(IStageGame game) : base(game) { }

        public string CacheRoot => _cache.Root;
        public Task? CacheCleanup => _cache.Cleanup;
        public Task<PlaybackAudioVariantCache> CacheCreated => _cache.Created;
        public Action<BeatMarker>? OnMetronomeClick { get; set; }
        public IPerformancePlayfield? Playfield { get; set; }
        public Func<TabHudDisplay>? MakeHud { get; set; }
        public IChartVideoPlayer? VideoPlayer { get; set; }
        public List<(Rectangle Bounds, Color Color, float Depth)> FallbackRectangles { get; } = new();
        public List<Rectangle> FallbackBackgrounds { get; } = new();
        public TabHudDisplay? LastHud { get; private set; }
        public Task<PreparedGameplayAudioSet?>? LastPreparationTask { get; private set; }
        public ScrollSpeedIndicator InitializeFonts() => InitializeReadyFont();
        internal override Task<PreparedGameplayAudioSet?> StartGameplayInitialization() =>
            LastPreparationTask = _cache.Track(base.StartGameplayInitialization());
        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory() => _cache.Factory();
        public void ReleaseAndDeleteSources(string directory)
        {
            Deactivate();
            // These source-owning cases use the default profile, which has no detached cache operation.
            _cache.Workers.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Directory.Delete(directory, recursive: true);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _cache.Release();
        }
        protected override void PlayMetronomeClick(BeatMarker marker)
        {
            if (OnMetronomeClick != null)
                OnMetronomeClick(marker);
            else
                base.PlayMetronomeClick(marker);
        }
        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
        protected override IPerformancePlayfield? CreateLanesPlayfield(GraphicsDevice? device, Texture2D? whitePixel) => Playfield;
        protected override IPerformancePlayfield? CreateTabPlayfield(Func<IPerformancePlayfield?> create) => create();
        internal override TabHudDisplay? CreateTabHud(GraphicsDevice? device, SongChart? chart,
            string? title, string? artist, Texture2D? whitePixel, SkinPalette palette, TabModeArt art) =>
            LastHud = MakeHud?.Invoke() ?? CreateReadyHeadlessTabHud();
        protected override IChartVideoPlayer? CreateChartVideoPlayerIfAvailable() => VideoPlayer;
        internal override void DrawFallbackRectangle(Rectangle bounds, Color color, float depth) =>
            FallbackRectangles.Add((bounds, color, depth));
        internal override void DrawFallbackBackground(Rectangle viewport) => FallbackBackgrounds.Add(viewport);
    }

    private static TabHudDisplay CreateReadyHeadlessTabHud()
    {
        return TabPlayfieldTests.CreateHud();
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
    public void Judgement_WithNoPlayfield_ShouldStillUpdateSkill()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var chart = CreateSingleNoteChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Rules!.JudgementManager!.IsActive = true;
        Assert.True(stage.Rules.JudgementManager.ResolveAutoHit(chart.Notes[0].Id));
        Assert.Equal(100.0, stage.Rules.SkillManager!.CurrentSkill);
    }

    [Theory]
    [InlineData(false, 52.0f)]
    [InlineData(true, 54.0f)]
    public void StageUpdate_MixedAutoPlay_ShouldApplyGaugeByFrozenLanePolicy(
        bool autoAddGauge,
        float expectedFinalLife)
    {
        var config = new ConfigData { AutoAddGauge = autoAddGauge };
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        input.ModularInputManager.KeyBindings.BindButton("Test.MixedManual", 0);
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), config, input));
        var chart = CreateMixedChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Mixed" }, 0, chart));
        config.AutoAddGauge = !autoAddGauge;
        UpdateUntil(stage, () => stage.AudioSession != null);
        var judgementManager = stage.Rules!.JudgementManager!;
        var gaugeManager = stage.Rules.GaugeManager!;
        stage.Update(1.1);
        Assert.True(input.ModularInputManager.InjectButton("Test.MixedManual", true));
        input.ModularInputManager.Update();
        stage.Update(1.0);
        Assert.Equal(52.0f, gaugeManager.CurrentLife);

        stage.Update(1.0);

        Assert.Equal(expectedFinalLife, gaugeManager.CurrentLife);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chart.Notes[0].Id)!.Status);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chart.Notes[1].Id)!.Status);
        stage.Deactivate();
    }

    [Fact]
    public void CleanupGameplayManagers_UnsubscribesStageAndRetainsThirdPartyObservers()
    {
        using var input = new MockInputManagerCompat();
        var (stageRef, rulesRef, judgement, score, gauge, observers) = CreateRetiredStageWithManagers(input);
        for (var i = 0; i < 3 && (stageRef.IsAlive || rulesRef.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(stageRef.IsAlive);
        Assert.False(rulesRef.IsAlive);
        Assert.All(observers, observer => Assert.True(observer.IsAlive));
        GC.KeepAlive(input);
        GC.KeepAlive(judgement);
        GC.KeepAlive(score);
        GC.KeepAlive(gauge);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Stage, WeakReference Rules, JudgementManager Judgement, ScoreManager Score,
        GaugeManager Gauge, WeakReference[] Observers)
        CreateRetiredStageWithManagers(MockInputManagerCompat input)
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData(), input));
        var chart = CreateSingleNoteChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var rules = stage.Rules!;
        var judgement = Assert.IsType<JudgementManager>(rules.JudgementManager);
        var score = Assert.IsType<ScoreManager>(rules.ScoreManager);
        var gauge = Assert.IsType<GaugeManager>(rules.GaugeManager);
        var judgementObserver = new ManagerObserver();
        var scoreObserver = new ManagerObserver();
        var gaugeObserver = new ManagerObserver();
        var failureObserver = new ManagerObserver();
        judgement.JudgementMade += judgementObserver.OnJudgement;
        score.ScoreChanged += scoreObserver.OnScore;
        gauge.GaugeChanged += gaugeObserver.OnGauge;
        gauge.Failed += failureObserver.OnFailure;
        judgement.IsActive = true;
        Assert.True(judgement.ResolveAutoHit(chart.Notes[0].Id));
        Assert.Equal(1, judgementObserver.Calls);
        Assert.Equal(1, scoreObserver.Calls);
        Assert.NotNull(scoreObserver.LastScore);
        Assert.Equal(0, scoreObserver.LastScore.PreviousScore);
        Assert.Equal(ScoreManager.MaxScore, scoreObserver.LastScore.CurrentScore);
        Assert.Equal(ScoreManager.MaxScore, scoreObserver.LastScore.ScoreAdded);
        Assert.Equal(JudgementType.Perfect, scoreObserver.LastScore.JudgementType);
        Assert.Equal(1, gaugeObserver.Calls);
        stage.Deactivate();
        Assert.Null(rules.JudgementManager);
        Assert.Null(rules.ScoreManager);
        Assert.Null(rules.ComboManager);
        Assert.Null(rules.GaugeManager);
        Assert.Null(rules.SkillManager);
        // Disposed managers suppress events, so retention is observed through
        // independently subscribed targets while those managers remain alive.
        return (new WeakReference(stage), new WeakReference(rules), judgement, score, gauge,
            new[] { new WeakReference(judgementObserver), new WeakReference(scoreObserver),
                new WeakReference(gaugeObserver), new WeakReference(failureObserver) });
    }

    private sealed class ManagerObserver
    {
        public int Calls { get; private set; }
        public ScoreChangedEventArgs? LastScore { get; private set; }
        public void OnJudgement(object? sender, JudgementEvent e) => Calls++;
        public void OnScore(object? sender, ScoreChangedEventArgs e)
        {
            Calls++;
            LastScore = e;
        }
        public void OnGauge(object? sender, GaugeChangedEventArgs e) => Calls++;
        public void OnFailure(object? sender, FailureEventArgs e) => Calls++;
    }

    [Fact]
    public void LogPerformanceError_WithException_ShouldLogErrorWithOriginalException()
    {
        var logger = new StageTestLogger();
        using var stage = new ThrowingAudioVariantStage(CreateHeadlessGame(
            logger, new ConfigData { PlaySpeedPercent = 50 }));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Failure" }, 0, CreateSingleNoteChart()));
        UpdateUntil(stage, () => logger.Errors.Any(e => e.Message.Contains("Audio preparation failed")));
        var error = Assert.Single(logger.Errors, e => e.Message.Contains("Audio preparation failed"));
        Assert.Same(stage.Failure, error.Exception);
    }

    [Fact]
    public void LogPerformanceError_WithoutException_ShouldLogErrorWithoutException()
    {
        var logger = new StageTestLogger();
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(logger, new ConfigData()));
        stage.Activate();
        var error = Assert.Single(logger.Errors, e => e.Message.Contains("No song was selected"));
        Assert.Null(error.Exception);
    }

    [Fact]
    public void DrawCenteredText_WithReadyFont_ShouldMeasureAndDrawAtCenteredPosition()
    {
        var font = new Mock<IFont>();
        font.Setup(f => f.MeasureString("READY...")).Returns(new Vector2(100, 20));
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.LoadFont("NotoSerifJP", 24)).Returns(font.Object);
        resources.Setup(r => r.LoadFont("NotoSerifJP", 14)).Returns(new Mock<IFont>().Object);
        using var stage = new HeadlessPerformanceStage(
            CreateHeadlessGame(new StageTestLogger(), new ConfigData(), resources: resources));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Ready" }, 0, new ParsedChart("ready.dtx")));
        stage.InitializeFonts();
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(0.125);
        stage.DrawUiPass(null);

        font.Verify(f => f.MeasureString("READY..."), Times.Once);
        font.Verify(f => f.DrawString(
            null!,
            "READY...",
            new Vector2((PerformanceUILayout.ScreenWidth / 2) - 50, (PerformanceUILayout.ScreenHeight / 2) - 10),
            Color.Yellow,
            0f,
            Vector2.Zero,
            Vector2.One,
            SpriteEffects.None,
            0.1f), Times.Once);
    }

    [Fact]
    public async Task StartSong_BeforeFirstUpdate_DoesNotBindOrStart()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "First frame" }, 0, CreateSingleNoteChart()));
        var task = Assert.IsAssignableFrom<Task<PreparedGameplayAudioSet?>>(stage.LastPreparationTask);
        Assert.NotNull(await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(stage.AudioSession);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.False(telemetry.PerformanceReady);
        stage.Update(0);
        Assert.NotNull(stage.AudioSession);
        Assert.False(stage.AudioSession!.IsPlaying);
        stage.Update(1.0);
        Assert.True(stage.AudioSession.IsPlaying);
    }

    [Fact]
    public void ExtractSharedData_WithParsedChart_ShouldSetParsedChart()
    {
        using var stage = new HeadlessPerformanceStage(CreateHeadlessGame(new StageTestLogger(), new ConfigData()));
        var parsedChart = new ParsedChart("shared-test.dtx") { Bpm = 140.0 };
        var playfield = new Mock<IPerformancePlayfield>();
        stage.Playfield = playfield.Object;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 2, parsedChart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        playfield.Verify(p => p.OnChartPublished(140.0, It.IsAny<int>()), Times.Once);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(2, telemetry.SelectedDifficulty);
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


    private static ITexture CreateTexture(int width, int height)
    {
        return new MutableTexture
        {
            Width = width,
            Height = height
        };
    }
}
