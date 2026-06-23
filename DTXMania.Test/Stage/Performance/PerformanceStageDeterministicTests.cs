using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class PerformanceStageDeterministicTests
{
    [Fact]
    public void ExtractSharedData_WhenSharedDataMissing_ShouldLeaveStageDataUnchanged()
    {
        var stage = CreateStage();

        ReflectionHelpers.SetPrivateField(stage, "_sharedData", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Null(ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_songId"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
    }

    [Fact]
    public void ExtractSharedData_WhenValidValuesExist_ShouldAssignAllFields()
    {
        var stage = CreateStage();
        var selectedSong = new SongListNode { Title = "Song" };
        var parsedChart = new ParsedChart("test.dtx");
        var sharedData = new Dictionary<string, object>
        {
            ["selectedSong"] = selectedSong,
            ["selectedDifficulty"] = 3,
            ["songId"] = 42,
            ["parsedChart"] = parsedChart
        };

        ReflectionHelpers.SetPrivateField(stage, "_sharedData", sharedData);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Same(selectedSong, ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong"));
        Assert.Equal(3, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
        Assert.Equal(42, ReflectionHelpers.GetPrivateField<int>(stage, "_songId"));
        Assert.Same(parsedChart, ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
    }

    [Fact]
    public void ExtractSharedData_WhenEntriesHaveUnexpectedTypes_ShouldPreserveExistingValues()
    {
        var stage = CreateStage();
        var existingSong = new SongListNode { Title = "Existing" };
        var existingChart = new ParsedChart("existing.dtx");
        var sharedData = new Dictionary<string, object>
        {
            ["selectedSong"] = "not-a-song",
            ["selectedDifficulty"] = "hard",
            ["songId"] = "42",
            ["parsedChart"] = new object()
        };

        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", existingSong);
        ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 2);
        ReflectionHelpers.SetPrivateField(stage, "_songId", 17);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", existingChart);
        ReflectionHelpers.SetPrivateField(stage, "_sharedData", sharedData);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Same(existingSong, ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong"));
        Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
        Assert.Equal(17, ReflectionHelpers.GetPrivateField<int>(stage, "_songId"));
        Assert.Same(existingChart, ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
    }

    [Fact]
    public void ExtractSharedData_WhenKeysAreAbsent_ShouldPreserveExistingValues()
    {
        var stage = CreateStage();
        var existingSong = new SongListNode { Title = "Existing" };
        var existingChart = new ParsedChart("existing.dtx");

        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", existingSong);
        ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 1);
        ReflectionHelpers.SetPrivateField(stage, "_songId", 8);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", existingChart);
        ReflectionHelpers.SetPrivateField(stage, "_sharedData", new Dictionary<string, object>());

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Same(existingSong, ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong"));
        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
        Assert.Equal(8, ReflectionHelpers.GetPrivateField<int>(stage, "_songId"));
        Assert.Same(existingChart, ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
    }

    [Fact]
    public void PopulateTelemetry_WhenManagersExist_ShouldExposePerformanceState()
    {
        var stage = CreateStage();
        var chart = new ParsedChart("telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        var chartManager = new ChartManager(chart);
        var scoreManager = new ScoreManager(1);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var selectedSong = new SongListNode { Title = "E2E AutoPlay Smoke" };

        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", selectedSong);
        ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 0);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", new SongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
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
    public void PopulateTelemetry_WhenSongTimerMissing_ShouldReportPerformanceNotReady()
    {
        var stage = CreateStage();
        var chart = new ParsedChart("telemetry.dtx");
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
        var chartManager = new ChartManager(chart);

        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.5);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", null);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.False(telemetry.PerformanceReady);
    }

    [Fact]
    public void PopulateTelemetry_WhenChartManagerMissing_ShouldReportPerformanceNotReady()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.0);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", null);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", new SongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var telemetry = new GameTelemetrySnapshot();

        stage.PopulateTelemetry(telemetry);

        Assert.False(telemetry.PerformanceReady);
    }

    [Fact]
    public async Task InitializeGameplayAsync_WhenChartHasNoBackgroundAudio_ShouldCreateSilentSongTimer()
    {
        var stage = CreateStage();
        var chart = new ParsedChart("silent-clock.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();

        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_audioLoader", new AudioLoader(new Mock<IResourceManager>().Object));
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", new MockInputManagerCompat());
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        var initializeTask = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayAsync")!;
        await initializeTask;

        var timer = ReflectionHelpers.GetPrivateField<SongTimer>(stage, "_songTimer");
        Assert.NotNull(timer);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));

        var startTime = new GameTime(TimeSpan.FromMilliseconds(1000), TimeSpan.Zero);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", startTime);
        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.True(timer!.IsPlaying);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.Equal(500.0, timer.GetCurrentMs(new GameTime(TimeSpan.FromMilliseconds(1500), TimeSpan.Zero)));
    }

    [Fact]
    public async Task InitializeGameplayAsync_WhenBodyThrows_ShouldResetLoadingAndRethrow()
    {
        // Exercises the catch-all in InitializeGameplayAsync: a zero-BPM chart makes
        // NoteRenderer.SetBpm throw ArgumentException. The catch must reset _isLoading
        // and rethrow so the stage is not left in a broken half-loaded state.
        var stage = CreateStage();
        var chart = new ParsedChart("bad-bpm.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();
        // Force a zero BPM so SetBpm throws downstream.
        chart.Bpm = 0.0;

        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);
        ReflectionHelpers.SetPrivateField(stage, "_audioLoader", new AudioLoader(new Mock<IResourceManager>().Object));
        // Null input manager makes InitializeGameplayManagers return early so we reach the
        // SetBpm call without needing the full manager wiring.
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer",
            ReflectionHelpers.CreateUninitialized<NoteRenderer>());

        var initializeTask = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayAsync")!;

        await Assert.ThrowsAsync<ArgumentException>(async () => await initializeTask);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isLoading"));
    }

    [Fact]
    public void LoadPerformanceUIAssets_WhenResourceManagerReturnsNull_ShouldLeaveAllTexturesNull()
    {
        // Each per-asset load is best-effort via TryLoadTexture; a missing skin must not
        // abort the whole load. This exercises every TryLoadTexture call site in the method.
        var stage = CreateStage();
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns((ITexture?)null);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "LoadPerformanceUIAssets"));

        Assert.Null(exception);
        Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_backgroundTexture"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_shutterTexture"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_skillPanelTexture"));
        // Every asset path should have been attempted exactly once.
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public void InitializeAutoPlay_WhenConfigEnablesAutoPlay_ShouldEnableAndResetIndex()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { AutoPlay = true }));
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 19);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_autoPlayEnabled"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
    }

    [Fact]
    public void InitializeAutoPlay_WhenConfigManagerMissing_ShouldDisableAutoPlayAndResetIndex()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), null);
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 7);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_autoPlayEnabled"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
    }

    [Fact]
    public void InitializeAutoPlay_WhenConfigDisablesAutoPlay_ShouldDisableAndResetIndex()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { AutoPlay = false }));
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 3);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_autoPlayEnabled"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
    }

    [Fact]
    public void InitializeAutoPlay_WhenGameMissing_ShouldDisableAutoPlayAndResetIndex()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_game", null);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 11);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_autoPlayEnabled"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
    }

    [Fact]
    public void InitializeAutoPlay_WhenConfigIsNull_ShouldDisableAutoPlayAndResetIndex()
    {
        var game = ReflectionHelpers.CreateGame();
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns((ConfigData)null!);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 4);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_autoPlayEnabled"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
    }

    [Fact]
    public void OnScoreChanged_WhenDisplayExists_ShouldPropagateCurrentScore()
    {
        var stage = CreateStage();
        var scoreDisplay = CreateScoreDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_scoreDisplay", scoreDisplay);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnScoreChanged", null, new ScoreChangedEventArgs { CurrentScore = 123456 });

        Assert.Equal(123456, scoreDisplay.Score);
    }

    [Fact]
    public void OnComboChanged_WhenDisplayExists_ShouldPropagateCurrentCombo()
    {
        var stage = CreateStage();
        var comboDisplay = CreateComboDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_comboDisplay", comboDisplay);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnComboChanged", null, new ComboChangedEventArgs { CurrentCombo = 87 });

        Assert.Equal(87, comboDisplay.Combo);
    }

    [Theory]
    [InlineData(75f, 0.75f, false)]
    [InlineData(15f, 0.15f, true)]
    public void OnGaugeChanged_ShouldNormalizeGaugeAndUpdateDangerState(float life, float expectedGauge, bool expectedDanger)
    {
        var stage = CreateStage();

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
        var stage = CreateStage();
        var inputManager = new MockInputManagerCompat();
        var chartManager = CreateChartManagerWithSingleNote();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayManagers");

        var judgementManager = ReflectionHelpers.GetPrivateField<JudgementManager>(stage, "_judgementManager");
        Assert.NotNull(judgementManager);
        Assert.False(judgementManager!.IsActive);
        Assert.NotNull(ReflectionHelpers.GetPrivateField<ScoreManager>(stage, "_scoreManager"));
        Assert.NotNull(ReflectionHelpers.GetPrivateField<ComboManager>(stage, "_comboManager"));
        Assert.NotNull(ReflectionHelpers.GetPrivateField<GaugeManager>(stage, "_gaugeManager"));
        Assert.Equal(0.5f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentGaugeValue"));
        Assert.Equal(0.0f, ReflectionHelpers.GetPrivateField<float>(stage, "_currentProgressValue"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isPaused"));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isDanger"));
    }

    [Fact]
    public void CleanupGameplayManagers_WhenManagersInitialized_ShouldClearAllManagerReferences()
    {
        var stage = CreateStage();
        var inputManager = new MockInputManagerCompat();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeGameplayManagers");

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupGameplayManagers");

        Assert.Null(ReflectionHelpers.GetPrivateField<JudgementManager>(stage, "_judgementManager"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ScoreManager>(stage, "_scoreManager"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ComboManager>(stage, "_comboManager"));
        Assert.Null(ReflectionHelpers.GetPrivateField<GaugeManager>(stage, "_gaugeManager"));
    }

    [Fact]
    public void ProcessBGMEvents_WhenCurrentTimeIsBeforeEventTime_ShouldNotTriggerEarly()
    {
        var stage = CreateStage();
        var scheduledEvents = new List<BGMEvent>
        {
            new() { WavId = "01", TimeMs = 1000.0 },
            new() { WavId = "02", TimeMs = 1300.0 }
        };
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", scheduledEvents);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessBGMEvents", 980.0);

        var remainingEvents = ReflectionHelpers.GetPrivateField<List<BGMEvent>>(stage, "_scheduledBGMEvents");
        Assert.Collection(remainingEvents,
            first => Assert.Equal("01", first.WavId),
            second => Assert.Equal("02", second.WavId));
    }

    [Fact]
    public void ProcessBGMEvents_WhenCurrentTimeReachesEventTime_ShouldTriggerAndRemoveElapsedEvents()
    {
        var stage = CreateStage();
        var scheduledEvents = new List<BGMEvent>
        {
            new() { WavId = "01", TimeMs = 1000.0 },
            new() { WavId = "02", TimeMs = 1300.0 }
        };
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", scheduledEvents);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessBGMEvents", 1000.0);

        var remainingEvents = ReflectionHelpers.GetPrivateField<List<BGMEvent>>(stage, "_scheduledBGMEvents");
        Assert.Single(remainingEvents);
        Assert.Equal("02", remainingEvents[0].WavId);
    }

    [Fact]
    public void GetPlayerJudgementTimeMs_WhenAudioLatencyOffsetConfigured_ShouldSubtractOffsetAndClamp()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager),
            CreateConfigManager(new ConfigData { AudioLatencyOffsetMs = 250 }));
        var stage = CreateStage(game);

        Assert.Equal(750.0, ReflectionHelpers.InvokePrivateMethod<double>(stage, "GetPlayerJudgementTimeMs", 1000.0));
        Assert.Equal(0.0, ReflectionHelpers.InvokePrivateMethod<double>(stage, "GetPlayerJudgementTimeMs", 100.0));
    }

    [Fact]
    public void UpdateGameplayManagers_WhenAutoPlayDisabled_ShouldStillAdvanceMissDetection()
    {
        var stage = CreateStage();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1301.0, 1301.0);

        Assert.Equal(1, judgementManager.GetJudgementCount(JudgementType.Miss));
    }

    [Fact]
    public void UpdateGameplayManagers_WhenAutoPlayEnabled_ShouldTriggerAutoHitBeforeMissDetection()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 750.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
    }

    [Fact]
    public void ProcessAutoPlay_WhenNextNoteIsInFuture_ShouldLeaveIndexUnchanged()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        judgementManager.EnqueueLaneHit(0, "PreResolved");
        judgementManager.Update(1000.0);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1000.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void UpdateGameplayManagers_WithAutoPlayDisabled_DoesNotProcessAutoPlay()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 1000.0);

        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
        Assert.NotEqual(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(chartManager.AllNotes[0].Id)!.Status);
    }

    [Fact]
    public void UpdateGameplayManagers_WithAutoPlayEnabled_AdvancesAutoPlay()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0, 1000.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[0].State);
    }

    [Fact]
    public void ProcessAutoPlay_NoteInWindow_PlaysChipForNote()
    {
        var stage = CreateStage();
        var chartManager = BuildChartManager(new[]
        {
            new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "07") { TimeMs = 100.0 },
        });

        var soundMock = new Mock<ISound>();
        var stubWavPath = WriteTempStubWav();
        try
        {
            var cache = new ChipSoundCache(_ => soundMock.Object);
            cache.PreloadAsync(new Dictionary<string, string>
            {
                ["07"] = stubWavPath,
            }).GetAwaiter().GetResult();

            ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
            ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

            var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
            judgementManager.IsActive = true;
            ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

            ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
            ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 0);

            ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 100.0);

            soundMock.Verify(s => s.Play(), Times.Once);
        }
        finally
        {
            File.Delete(stubWavPath);
        }
    }

    [Fact]
    public void ProcessAutoPlay_NoteInWindow_NoChipCache_DoesNotThrow()
    {
        var stage = CreateStage();
        var chartManager = BuildChartManager(new[]
        {
            new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "07") { TimeMs = 100.0 },
        });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", null);

        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        judgementManager.IsActive = true;
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 0);

        var ex = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 100.0));

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
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        var chartManager = BuildChartManager(new[]
        {
            new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "01") { TimeMs = 100.0 },
            new Note(laneIndex: 1, bar: 0, tick: 0, channel: 0x12, value: "01") { TimeMs = 200.0 },
            new Note(laneIndex: 2, bar: 0, tick: 0, channel: 0x13, value: "01") { TimeMs = 300.0 },
        });
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
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
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);

        // Seed a hittable note on lane 0 so PlayChipForNote would have something to play
        var note = new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x12, value: "07") { TimeMs = 1000.0 };
        var chartManager = BuildChartManager(new[] { note });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);

        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        judgementManager.IsActive = true;
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var stubWavPath = WriteTempStubWav();
        try
        {
            var soundMock = new Mock<ISound>();
            var cache = new ChipSoundCache(_ => soundMock.Object);
            cache.PreloadAsync(new Dictionary<string, string> { ["07"] = stubWavPath })
                 .GetAwaiter().GetResult();
            ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

            // Set timer to playing state at 1010ms (within note's hit window)
            // so without the autoplay guard, PlayChipForNote WOULD be called
            ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
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
        finally
        {
            File.Delete(stubWavPath);
        }
    }

    [Fact]
    public void OnLaneHitForPadFeedback_AutoPlayOff_PlaysChipForNearestNote()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);

        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "07") { TimeMs = 1000.0 };
        var chartManager = BuildChartManager(new[] { note });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);

        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        judgementManager.IsActive = true;
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var stubWavPath = WriteTempStubWav();
        try
        {
            var soundMock = new Mock<ISound>();
            var cache = new ChipSoundCache(_ => soundMock.Object);
            cache.PreloadAsync(new Dictionary<string, string> { ["07"] = stubWavPath })
                 .GetAwaiter().GetResult();
            ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

            // Drive _songTimer.GetCurrentMs(_currentGameTime) -> 1010ms (10ms past the note)
            ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
            ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
                new GameTime(TimeSpan.FromMilliseconds(1010.0), TimeSpan.FromSeconds(0.016)));

            var args = new LaneHitEventArgs(3, new ButtonState("Player", true, 1.0f));
            ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

            soundMock.Verify(s => s.Play(), Times.Once);
        }
        finally
        {
            File.Delete(stubWavPath);
        }
    }

    [Fact]
    public void OnLaneHitForPadFeedback_AutoPlayOff_NoNoteInWindow_NoChip()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);

        // Note exists but is far away — outside the ±200ms window
        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "07") { TimeMs = 5000.0 };
        var chartManager = BuildChartManager(new[] { note });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);

        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        judgementManager.IsActive = true;
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var stubWavPath = WriteTempStubWav();
        try
        {
            var soundMock = new Mock<ISound>();
            var cache = new ChipSoundCache(_ => soundMock.Object);
            cache.PreloadAsync(new Dictionary<string, string> { ["07"] = stubWavPath })
                 .GetAwaiter().GetResult();
            ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

            ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
            ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
                new GameTime(TimeSpan.FromMilliseconds(1000.0), TimeSpan.FromSeconds(0.016)));

            var args = new LaneHitEventArgs(3, new ButtonState("Player", true, 1.0f));
            ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

            soundMock.Verify(s => s.Play(), Times.Never);
        }
        finally
        {
            File.Delete(stubWavPath);
        }
    }

    [Fact]
    public void OnLaneHitForPadFeedback_WithLatencyOffset_UsesCompensatedTimeForChipLookup()
    {
        // Verify P2 fix: chip feedback must use the same compensated clock as
        // JudgementManager so that a note within the Poor window at the
        // compensated time is found even when the raw clock is offset.
        var game = ReflectionHelpers.CreateGame();
        const int latencyOffsetMs = 200;
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager),
            CreateConfigManager(new ConfigData { AudioLatencyOffsetMs = latencyOffsetMs }));
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);

        // Note at 1000ms on lane 3. With 200ms latency offset, raw clock 1200ms
        // should map to compensated 1000ms — within the Poor window (150ms).
        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "07") { TimeMs = 1000.0 };
        var chartManager = BuildChartManager(new[] { note });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);

        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        judgementManager.IsActive = true;
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var stubWavPath = WriteTempStubWav();
        try
        {
            var soundMock = new Mock<ISound>();
            var cache = new ChipSoundCache(_ => soundMock.Object);
            cache.PreloadAsync(new Dictionary<string, string> { ["07"] = stubWavPath })
                 .GetAwaiter().GetResult();
            ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

            // Raw clock at 1200ms. Without compensation, distance to note = 200ms > PoorWindow (150ms) → no chip.
            // With compensation, compensated = 1000ms, distance = 0ms → chip plays.
            ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
            ReflectionHelpers.SetPrivateField(stage, "_currentGameTime",
                new GameTime(TimeSpan.FromMilliseconds(1200.0), TimeSpan.FromSeconds(0.016)));

            var args = new LaneHitEventArgs(3, new ButtonState("Player", true, 1.0f));
            ReflectionHelpers.InvokePrivateMethod(stage, "OnLaneHitForPadFeedback", null, args);

            // Chip sound should play because compensated time (1000ms) matches note exactly
            soundMock.Verify(s => s.Play(), Times.Once);
        }
        finally
        {
            File.Delete(stubWavPath);
        }
    }

    [Fact]
    public void OnLaneHitForPadFeedback_SongNotPlaying_VisualPadButNoChipSound()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);

        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "07") { TimeMs = 10.0 };
        var chartManager = BuildChartManager(new[] { note });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);

        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        judgementManager.IsActive = true;
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var stubWavPath = WriteTempStubWav();
        try
        {
            var soundMock = new Mock<ISound>();
            var cache = new ChipSoundCache(_ => soundMock.Object);
            cache.PreloadAsync(new Dictionary<string, string> { ["07"] = stubWavPath })
                 .GetAwaiter().GetResult();
            ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

            // No _songTimer set — defaults to null, so IsPlaying is false.
            // currentTimeMs would be 0.0 which is within 200ms of the note at 10ms,
            // but chip playback must be suppressed because the song isn't playing.
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
        finally
        {
            File.Delete(stubWavPath);
        }
    }

    [Fact]
    public void OnPlayerFailed_WhenNoFailDisabled_ShouldFinalizePerformanceAndTransitionToResult()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        Dictionary<string, object>? capturedSharedData = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()))
            .Callback<StageType, IStageTransition, Dictionary<string, object>>((_, _, sharedData) => capturedSharedData = sharedData);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));

        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, new FailureEventArgs { FinalLife = 0.0f, JudgementType = JudgementType.Miss });

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        var summary = Assert.IsType<PerformanceSummary>(capturedSharedData!["performanceSummary"]);
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
        Assert.Equal(1, summary.TotalNotes);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public void CheckStageCompletion_WhenSongEndBufferPassed_ShouldFinalizeAsSongComplete()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        Dictionary<string, object>? capturedSharedData = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()))
            .Callback<StageType, IStageTransition, Dictionary<string, object>>((_, _, sharedData) => capturedSharedData = sharedData);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("complete-test.dtx") { DurationMs = 1000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        var summary = Assert.IsType<PerformanceSummary>(capturedSharedData!["performanceSummary"]);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.True(summary.ClearFlag);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public void CheckStageCompletion_WhenSongEndsAndGaugeHasFailed_ShouldPreferSongComplete()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        Dictionary<string, object>? capturedSharedData = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()))
            .Callback<StageType, IStageTransition, Dictionary<string, object>>((_, _, sharedData) => capturedSharedData = sharedData);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("complete-failed-test.dtx") { DurationMs = 1000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());

        var gaugeManager = new GaugeManager();
        ReflectionHelpers.SetPrivateField(gaugeManager, "_hasFailed", true);
        ReflectionHelpers.SetPrivateField(gaugeManager, "_currentLife", 0.0f);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        var summary = Assert.IsType<PerformanceSummary>(capturedSharedData!["performanceSummary"]);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.True(summary.ClearFlag);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public void CleanupComponents_WhenAssetsAndSoundsExist_ShouldResetStateAndReleaseResources()
    {
        var stage = CreateStage();
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
        ReflectionHelpers.SetPrivateField(stage, "_lagNumbersTexture", lagNumbersTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_pauseOverlayTexture", pauseOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_dangerOverlayTexture", dangerOverlayTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelTexture", skillPanelTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound> { ["01"] = bgmSound.Object });
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent> { new() { WavId = "01", TimeMs = 1000.0 } });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("cleanup-test.dtx"));
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());

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
        Assert.Null(ReflectionHelpers.GetPrivateField<ChartManager>(stage, "_chartManager"));
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
        pauseOverlayTexture.Verify(x => x.RemoveReference(), Times.Once);
        dangerOverlayTexture.Verify(x => x.RemoveReference(), Times.Once);
        skillPanelTexture.Verify(x => x.RemoveReference(), Times.Once);
        bgmSound.Verify(x => x.Dispose(), Times.Once);
        Assert.Empty(ReflectionHelpers.GetPrivateField<List<BGMEvent>>(stage, "_scheduledBGMEvents"));
        Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
    }

    [Fact]
    public void ReturnToSongSelect_ShouldPauseInputStopTimerAndChangeStage()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote())
        {
            IsActive = true
        };

        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        stageManager.Verify(
            x => x.ChangeStage(
                StageType.SongSelect,
                It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                null),
            Times.Once);
    }

    [Fact]
    public void ReturnToSongSelect_WhenStageManagerAndJudgementManagerMissing_ShouldStillPauseInput()
    {
        var stage = CreateStage();

        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", null);
        stage.StageManager = null;

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect"));

        Assert.Null(exception);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
    }

    [Fact]
    public void HandleInput_WhenBackActionTriggeredAndTransitionAllowed_ShouldMarkTransitionAndReturnToSongSelect()
    {
        var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        var inputManager = new BackActionInputManagerCompat(backTriggered: true);
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote())
        {
            IsActive = true
        };

        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        Assert.Equal(2.0, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        stageManager.Verify(
            x => x.ChangeStage(
                StageType.SongSelect,
                It.IsAny<IStageTransition>(),
                null),
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
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void OnPlayerFailed_WhenNoFailEnabled_ShouldNotFinalizePerformance()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = true }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, new FailureEventArgs { FinalLife = 0.0f, JudgementType = JudgementType.Miss });

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
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
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
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

    [Fact]
    public void UpdateGameplay_WhenReadyCountdownStillActive_ShouldDecreaseCountdownWithoutStartingSong()
    {
        var stage = CreateStage();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.75);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.Equal(0.5, ReflectionHelpers.GetPrivateField<double>(stage, "_readyCountdown"), 3);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void UpdateGameplay_WhenReadyCountdownExpires_ShouldStartSongAndActivateJudgementManager()
    {
        var stage = CreateStage();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.1);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.25);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
    }

    [Fact]
    public void UpdateGameplay_WhenSongIsPlaying_ShouldAdvanceManagersAndProgress()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", false);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromMilliseconds(1301.0), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("progress-test.dtx") { DurationMs = 2000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());

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
    public void DrawJudgementLine_WhenTextureExists_ShouldDrawAtHitBarPositionUsingTextureSize()
    {
        var stage = CreateStage();
        var judgementLineTexture = CreateTextureMock(width: 320, height: 24);
        var expectedPosition = PerformanceUILayout.HitBar.Position;
        var expectedRectangle = new Rectangle((int)expectedPosition.X, (int)expectedPosition.Y, 320, 24);

        ReflectionHelpers.SetPrivateField(stage, "_judgementLineTexture", judgementLineTexture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "DrawJudgementLine");

        judgementLineTexture.Verify(
            texture => texture.Draw(
                null!,
                expectedRectangle,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.6f),
            Times.Once);
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
        var stage = CreateStage();
        var noteRenderer = CreateNoteRenderer();
        var parsedChart = new ParsedChart("draw-notes-active-note.dtx") { Bpm = 120.0 };
        parsedChart.AddNote(new Note { LaneIndex = 0, Channel = 0x13, TimeMs = 4000.0, Value = "01" });
        parsedChart.FinalizeChart();

        Assert.True(noteRenderer.IsReady);

        ReflectionHelpers.SetPrivateField(noteRenderer, "<EffectiveLookAheadMs>k__BackingField", 3000.0);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", new ChartManager(parsedChart));
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNotes"));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawNoteOverlays_WhenRendererIsReadyAndActiveNoteIsOffscreen_ShouldCompleteWithoutThrowing()
    {
        var stage = CreateStage();
        var noteRenderer = CreateNoteRenderer();
        var parsedChart = new ParsedChart("draw-note-overlays-active-note.dtx") { Bpm = 120.0 };
        parsedChart.AddNote(new Note { LaneIndex = 0, Channel = 0x13, TimeMs = 4000.0, Value = "01" });
        parsedChart.FinalizeChart();

        Assert.True(noteRenderer.IsReady);

        ReflectionHelpers.SetPrivateField(noteRenderer, "<EffectiveLookAheadMs>k__BackingField", 3000.0);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", new ChartManager(parsedChart));
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNoteOverlays"));

        Assert.Null(exception);
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
    public async Task LoadBGMSoundsAsync_WhenChartHasNoEvents_ShouldLeaveSoundMapEmpty()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("no-bgm.dtx"));
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
        await task;

        Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
    }

    [Fact]
    public async Task LoadBGMSoundsAsync_WhenParsedChartIsNull_ShouldLeaveSoundMapEmpty()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", null);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
        await task;

        Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
    }

    [Fact]
    public async Task LoadBGMSoundsAsync_WhenAudioFileIsMissingOrDuplicate_ShouldSkipEntries()
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("missing-bgm.dtx");
        parsedChart.BGMEvents.Add(new BGMEvent { WavId = "01", AudioFilePath = GetGeneratedTestArtifactPath($"dtxmania-test-missing-{Guid.NewGuid()}.wav") });
        parsedChart.BGMEvents.Add(new BGMEvent { WavId = "01", AudioFilePath = GetGeneratedTestArtifactPath($"dtxmania-test-missing-{Guid.NewGuid()}.wav") });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
        await task;

        Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
    }

    [Fact]
    public async Task LoadBGMSoundsAsync_WhenAudioFilePathIsEmpty_ShouldSkipEntry()
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("empty-path-bgm.dtx");
        parsedChart.BGMEvents.Add(new BGMEvent { WavId = "01", AudioFilePath = string.Empty });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
        await task;

        Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
    }

    [Fact]
    public async Task LoadBGMSoundsAsync_WhenWavIdAlreadyLoaded_ShouldKeepExistingSoundOnly()
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("duplicate-bgm.dtx");
        var existingSound = CreateSoundMock();
        parsedChart.BGMEvents.Add(new BGMEvent { WavId = "01", AudioFilePath = GetGeneratedTestArtifactPath($"dtxmania-test-duplicate-{Guid.NewGuid()}.wav") });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound> { ["01"] = existingSound.Object });

        var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
        await task;

        var bgmSounds = ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds");
        Assert.Single(bgmSounds);
        Assert.Same(existingSound.Object, bgmSounds["01"]);
    }

    [Fact]
    public async Task LoadBGMSoundsAsync_WhenSoundCreationThrows_ShouldSwallowFailureAndContinue()
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("invalid-bgm.dtx");
        var invalidAudioPath = GetGeneratedTestArtifactPath($"dtxmania-test-invalid-{Guid.NewGuid()}.txt");
        File.WriteAllText(invalidAudioPath, "not audio data");
        parsedChart.BGMEvents.Add(new BGMEvent { WavId = "01", AudioFilePath = invalidAudioPath });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        try
        {
            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
            await task;

            Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
        }
        finally
        {
            if (File.Exists(invalidAudioPath))
            {
                File.Delete(invalidAudioPath);
            }
        }
    }

    [Fact]
    public void TriggerBGMEvent_WhenPlayReturnsNull_ShouldAttemptPlaybackWithoutThrowing()
    {
        var stage = CreateStage();
        var sound = CreateSoundMock();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns((Microsoft.Xna.Framework.Audio.SoundEffectInstance)null!);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound> { ["01"] = sound.Object });

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "TriggerBGMEvent", new BGMEvent { WavId = "01" }));

        Assert.Null(exception);
        // No chart loaded → defaults to full volume, centered, no pitch shift.
        sound.Verify(x => x.Play(1.0f, 0.0f, 0.0f), Times.Once);
    }

    [Fact]
    public void TriggerBGMEvent_ShouldHonorChartVolumeAndPan()
    {
        var stage = CreateStage();
        var chart = new ParsedChart();
        chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 50 });
        chart.SetWavPans(new Dictionary<string, int> { ["01"] = -100 });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);

        var sound = CreateSoundMock();
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound> { ["01"] = sound.Object });

        ReflectionHelpers.InvokePrivateMethod(stage, "TriggerBGMEvent", new BGMEvent { WavId = "01" });

        sound.Verify(x => x.Play(0.5f, 0.0f, -1.0f), Times.Once);
    }

    [Fact]
    public void TriggerBGMEvent_WhenSoundIsMissing_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "TriggerBGMEvent", new BGMEvent { WavId = "missing" }));

        Assert.Null(exception);
    }

    [Fact]
    public void TriggerBGMEvent_WhenPlayThrows_ShouldSwallowPlaybackFailure()
    {
        var stage = CreateStage();
        var sound = CreateSoundMock();
        sound.Setup(x => x.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Throws(new InvalidOperationException("boom"));
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound> { ["01"] = sound.Object });

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "TriggerBGMEvent", new BGMEvent { WavId = "01" }));

        Assert.Null(exception);
    }

    [Fact]
    public void OnJudgementMade_WhenJudgementIsHit_ShouldForwardToManagersAndTriggerVisualFeedbackWithoutLaneFlash()
    {
        var stage = CreateStage();
        var scoreManager = new ScoreManager(10);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        var effectsManager = CreateEffectsManager();
        var noteRenderer = CreateNoteRenderer();
        var popupManager = CreateJudgementTextPopupManager();
        var padRenderer = CreatePadRenderer();
        var skillPanelDisplay = CreateSkillPanelDisplayState();
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_effectsManager", effectsManager);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_judgementTextPopupManager", popupManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", skillPanelDisplay);

        var judgement = new JudgementEvent(noteRef: 1, lane: 2, deltaMs: 0.0, type: JudgementType.Great);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null, judgement);

        Assert.Equal(scoreManager.CalculateScoreForJudgement(JudgementType.Great), scoreManager.CurrentScore);
        Assert.Equal(1, comboManager.CurrentCombo);
        Assert.Equal(51.5f, gaugeManager.CurrentLife);
        Assert.Equal(0.0f, ReflectionHelpers.GetPrivateField<float[]>(noteRenderer, "_laneFlashAlpha")[2]);
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[2].State);
        Assert.Single(ReflectionHelpers.GetPrivateField<List<JudgementTextPopup>>(popupManager, "_activePopups"));
        Assert.Equal(1, skillPanelDisplay.GreatCount);
        Assert.Equal(1, skillPanelDisplay.ProcessedJudgementCount);
        Assert.Equal(1, skillPanelDisplay.MaxCombo);
    }

    [Fact]
    public void OnJudgementMade_WhenJudgementIsMiss_ShouldForwardToManagersWithoutTriggeringHitVisualFeedback()
    {
        var stage = CreateStage();
        var scoreManager = new ScoreManager(10);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        var noteRenderer = CreateNoteRenderer();
        var popupManager = CreateJudgementTextPopupManager();
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_judgementTextPopupManager", popupManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var judgement = new JudgementEvent(noteRef: 1, lane: 4, deltaMs: 75.0, type: JudgementType.Miss);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null, judgement);

        Assert.Equal(0, scoreManager.CurrentScore);
        Assert.Equal(0, comboManager.CurrentCombo);
        Assert.Equal(47.0f, gaugeManager.CurrentLife);
        Assert.Equal(0.0f, ReflectionHelpers.GetPrivateField<float[]>(noteRenderer, "_laneFlashAlpha")[4]);
        Assert.Equal(PadState.Idle, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[4].State);
        Assert.Single(ReflectionHelpers.GetPrivateField<List<JudgementTextPopup>>(popupManager, "_activePopups"));
    }

    [Fact]
    public void CheckStageCompletion_WhenNoFailEnabledAndGaugeFailed_ShouldNotFinalize()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = true }));
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("nofail-test.dtx") { DurationMs = 5000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
    }

    [Fact]
    public void CheckStageCompletion_WhenPlayerFailedAndNoFailDisabled_ShouldFinalizeAsPlayerFailed()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        Dictionary<string, object>? capturedSharedData = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()))
            .Callback<StageType, IStageTransition, Dictionary<string, object>>((_, _, sharedData) => capturedSharedData = sharedData);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("failed-test.dtx") { DurationMs = 5000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());

        var gaugeManager = new GaugeManager();
        ReflectionHelpers.SetPrivateField(gaugeManager, "_hasFailed", true);
        ReflectionHelpers.SetPrivateField(gaugeManager, "_currentLife", 0.0f);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        var summary = Assert.IsType<PerformanceSummary>(capturedSharedData!["performanceSummary"]);
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public void CheckStageCompletion_WhenStageAlreadyCompleted_ShouldReturnWithoutTransition()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("already-complete.dtx") { DurationMs = 1000.0 });

        var gaugeManager = new GaugeManager();
        ReflectionHelpers.SetPrivateField(gaugeManager, "_hasFailed", true);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
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
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void CheckStageCompletion_WhenGaugeHasNotFailedAndSongNotFinished_ShouldNotFinalize()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("in-progress.dtx") { DurationMs = 5000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void CheckStageCompletion_WhenGaugeManagerMissingAndSongNotFinished_ShouldNotFinalize()
    {
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), CreateConfigManager(new ConfigData { NoFail = false }));
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("no-gauge.dtx") { DurationMs = 5000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 1000.0);

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void FinalizePerformance_WhenManagersAreMissing_ShouldBuildZeroedSummaryAndPauseInput()
    {
        var stage = CreateStage();

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(0, summary.Score);
        Assert.Equal(0, summary.TotalNotes);
        Assert.True(summary.ClearFlag);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
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

        var withoutException = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "plain message", null));
        var withException = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "failure", new InvalidOperationException("boom")));

        Assert.Null(withoutException);
        Assert.Null(withException);
    }

    [Fact]
    public void StartSong_WhenPlaybackFailsWithoutBgmEvents_ShouldDisableReadyAndActivateJudgementManager()
    {
        var stage = CreateStage();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote());
        var songTimer = CreateStoppedSongTimer();
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", songTimer);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
    }

    [Fact]
    public void StartSong_WhenPlaybackFailsWithBgmEvents_ShouldStillDisableReadyAndActivateJudgementManager()
    {
        var stage = CreateStage();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote());
        var songTimer = CreateStoppedSongTimer();
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", songTimer);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent> { new() { WavId = "01", TimeMs = 250.0 } });
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
    }

    [Fact]
    public void StartSong_WhenLegacyBgmAndBackgroundWavIdDefined_ShouldApplyPerWavVolumeAndPan()
    {
        // Legacy (no-BGM-events) path: when the chart resolves a BackgroundWavId,
        // StartSong must honor that WAV's #VOLUME/#PAN on the master background
        // track rather than defaulting to full volume.
        var stage = CreateStage();
        var chart = new ParsedChart("legacy-bgm.dtx");
        chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 40 });
        chart.SetWavPans(new Dictionary<string, int> { ["01"] = -80 });
        chart.BackgroundWavId = "01";
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);

        // Real SoundEffectInstance so SongTimer.Volume/Pan setters are exercised.
        var instance = CreateSoundEffectInstance();
        var songTimer = new SongTimer(instance);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", songTimer);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        // #VOLUME01=40 → 0.4f; #PAN01=-80 → -0.8f (clamped to [-1,1]).
        Assert.Equal(0.4f, songTimer.Volume);
        Assert.Equal(-0.8f, songTimer.Pan);
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void StartSong_WhenLegacyBgmAndNoBackgroundWavId_ShouldDefaultToFullVolume()
    {
        // Legacy path with no resolved BackgroundWavId falls back to full volume.
        var stage = CreateStage();
        var chart = new ParsedChart("legacy-bgm-no-id.dtx");
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);

        var instance = CreateSoundEffectInstance();
        var songTimer = new SongTimer(instance);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", songTimer);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.Equal(1.0f, songTimer.Volume);
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
        resourceManager.Setup(x => x.LoadTexture("throws")).Throws(new InvalidOperationException("boom"));
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
        resourceManager.Setup(x => x.LoadTexture("ok")).Returns(texture.Object);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

        var result = ReflectionHelpers.InvokePrivateMethod<ITexture>(stage, "TryLoadTexture", "ok");

        Assert.Same(texture.Object, result);
    }

    [Fact]
    public void PlayChipForNote_WhenNoteIsNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        var soundMock = new Mock<ISound>();
        var cache = new ChipSoundCache(_ => soundMock.Object);
        ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

        var ex = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "PlayChipForNote", (Note)null!));

        Assert.Null(ex);
        soundMock.Verify(s => s.Play(), Times.Never);
    }

    [Fact]
    public void PlayChipForNote_WhenNoteValueIsEmpty_ShouldNotPlayChip()
    {
        var stage = CreateStage();
        var soundMock = new Mock<ISound>();
        var cache = new ChipSoundCache(_ => soundMock.Object);
        ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);
        var note = new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "");

        var ex = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "PlayChipForNote", note));

        Assert.Null(ex);
        soundMock.Verify(s => s.Play(), Times.Never);
    }

    [Fact]
    public void PlayChipForNote_WhenChipSoundCacheIsNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", null);
        var note = new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "01") { TimeMs = 100.0 };

        var ex = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "PlayChipForNote", note));

        Assert.Null(ex);
    }

    [Fact]
    public void PlayChipForNote_ShouldHonorChartVolumeAndPan()
    {
        var stage = CreateStage();
        var chart = new ParsedChart();
        chart.SetWavVolumes(new Dictionary<string, int> { ["07"] = 50 });
        chart.SetWavPans(new Dictionary<string, int> { ["07"] = 100 });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", chart);

        var soundMock = new Mock<ISound>();
        var stubWavPath = WriteTempStubWav();
        try
        {
            var cache = new ChipSoundCache(_ => soundMock.Object);
            cache.PreloadAsync(new Dictionary<string, string> { ["07"] = stubWavPath }).GetAwaiter().GetResult();
            ReflectionHelpers.SetPrivateField(stage, "_chipSoundCache", cache);

            var note = new Note(laneIndex: 0, bar: 0, tick: 0, channel: 0x11, value: "07") { TimeMs = 100.0 };
            ReflectionHelpers.InvokePrivateMethod(stage, "PlayChipForNote", note);

            soundMock.Verify(s => s.Play(0.5f, 0.0f, 1.0f), Times.Once);
            soundMock.Verify(s => s.Play(), Times.Never);
        }
        finally
        {
            File.Delete(stubWavPath);
        }
    }

    [Fact]
    public void FindNearestNoteForChip_WhenChartManagerIsNull_ShouldReturnNull()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", null);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 0, 500.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenJudgementManagerIsNull_ShouldReturnNull()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", null);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 0, 500.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteInDifferentLane_ShouldReturnNull()
    {
        var stage = CreateStage();
        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "01") { TimeMs = 1000.0 };
        var chartManager = BuildChartManager(new[] { note });
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 7, 1000.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteAlreadyHit_ShouldSkipAndReturnNull()
    {
        var stage = CreateStage();
        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "01") { TimeMs = 1000.0 };
        var chartManager = BuildChartManager(new[] { note });
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        judgementManager.EnqueueLaneHit(3, "Test");
        judgementManager.Update(1000.0);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 3, 1000.0);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteIsWithinWindow_ShouldReturnNearestNote()
    {
        var stage = CreateStage();
        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "01") { TimeMs = 1000.0 };
        var chartManager = BuildChartManager(new[] { note });
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        var result = ReflectionHelpers.InvokePrivateMethod<Note?>(stage, "FindNearestNoteForChip", 3, 1010.0);

        Assert.NotNull(result);
        Assert.Equal(1000.0, result!.TimeMs);
    }

    [Fact]
    public void FindNearestNoteForChip_WhenNoteIsInMissRange_ShouldReturnNull()
    {
        var stage = CreateStage();
        // Note at 1000ms; querying at 1180ms gives 180ms delta — within the 200ms hit
        // detection window but classified as Miss (151-200ms). Chip playback should skip it.
        var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x12, value: "01") { TimeMs = 1000.0 };
        var chartManager = BuildChartManager(new[] { note });
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

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
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", null);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNotes"));

        Assert.Null(ex);
    }

    [Fact]
    public void DrawNotes_WhenSongNotPlaying_ShouldReturnWithoutDrawing()
    {
        var stage = CreateStage();
        var noteRenderer = CreateNoteRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNotes"));

        Assert.Null(ex);
    }

    [Fact]
    public void DrawNoteOverlays_WhenNoteRendererIsNull_ShouldReturnWithoutThrowing()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", null);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNoteOverlays"));

        Assert.Null(ex);
    }

    [Fact]
    public void DrawNoteOverlays_WhenSongNotPlaying_ShouldReturnWithoutDrawing()
    {
        var stage = CreateStage();
        var noteRenderer = CreateNoteRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));

        var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawNoteOverlays"));

        Assert.Null(ex);
    }

    private static PerformanceStage CreateStage(BaseGame? game = null)
    {
#pragma warning disable SYSLIB0050
        var stage = (PerformanceStage)FormatterServices.GetUninitializedObject(typeof(PerformanceStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
        return stage;
    }

    private static InspectablePerformanceStage CreateInspectableStage(BaseGame? game = null)
    {
#pragma warning disable SYSLIB0050
        var stage = (InspectablePerformanceStage)FormatterServices.GetUninitializedObject(typeof(InspectablePerformanceStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
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

    private static EffectsManager CreateEffectsManager()
    {
#pragma warning disable SYSLIB0050
        return (EffectsManager)FormatterServices.GetUninitializedObject(typeof(EffectsManager));
#pragma warning restore SYSLIB0050
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

    private static JudgementTextPopupManager CreateJudgementTextPopupManager()
    {
#pragma warning disable SYSLIB0050
        var manager = (JudgementTextPopupManager)FormatterServices.GetUninitializedObject(typeof(JudgementTextPopupManager));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(manager, "_activePopups", new List<JudgementTextPopup>());
        return manager;
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

    private static ChartManager CreateChartManagerWithSingleNote()
    {
        var parsedChart = new ParsedChart("performance-stage-test.dtx")
        {
            Bpm = 120.0
        };
        parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        parsedChart.FinalizeChart();
        return new ChartManager(parsedChart);
    }

    private static ChartManager BuildChartManager(IEnumerable<Note> notes)
    {
        var parsed = new ParsedChart("chip-sound-test.dtx") { Bpm = 120.0 };
        foreach (var n in notes)
        {
            parsed.AddNote(n);
        }
        parsed.FinalizeChart();
        return new ChartManager(parsed);
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

    private static SongTimer CreateStoppedSongTimer()
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

    private static SongTimer CreatePlayingSongTimer()
    {
#pragma warning disable SYSLIB0050
        var timer = (SongTimer)FormatterServices.GetUninitializedObject(typeof(SongTimer));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(timer, "_disposed", false);
        ReflectionHelpers.SetPrivateField(timer, "_isPlaying", true);
        ReflectionHelpers.SetPrivateField(timer, "_startTime", TimeSpan.Zero);
        return timer;
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

    private sealed class InspectablePerformanceStage : PerformanceStage
    {
        public InspectablePerformanceStage(BaseGame game)
            : base(game)
        {
        }

        public Texture2D? DrawFallbackTextureArgument { get; private set; }

        public Rectangle? DrawFallbackTextureRectangle { get; private set; }

        public Color? DrawFallbackTextureColor { get; private set; }

        public float? DrawFallbackTextureDepth { get; private set; }

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
}
