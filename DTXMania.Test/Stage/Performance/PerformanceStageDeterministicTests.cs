using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
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
    public void ProcessBGMEvents_WhenCurrentTimeReachesTolerance_ShouldTriggerAndRemoveElapsedEvents()
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
        Assert.Single(remainingEvents);
        Assert.Equal("02", remainingEvents[0].WavId);
    }

    [Fact]
    public void UpdateGameplayManagers_WhenAutoPlayDisabled_ShouldStillAdvanceMissDetection()
    {
        var stage = CreateStage();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", false);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1301.0);

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

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplayManagers", 1000.0);
        judgementManager.Update(1000.0);

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
    public void ProcessAutoPlay_WhenNextNoteIsTooFarInPast_ShouldSkipNote()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "ProcessAutoPlay", 1100.0);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
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
    public async Task LoadBGMSoundsAsync_WhenAudioFileIsMissingOrDuplicate_ShouldSkipEntries()
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("missing-bgm.dtx");
        parsedChart.BGMEvents.Add(new BGMEvent { WavId = "01", AudioFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dtxmania-test-missing-{Guid.NewGuid()}.wav") });
        parsedChart.BGMEvents.Add(new BGMEvent { WavId = "01", AudioFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dtxmania-test-missing-{Guid.NewGuid()}.wav") });
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

        var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
        await task;

        Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
    }

    [Fact]
    public void TriggerBGMEvent_WhenCreateInstanceReturnsNull_ShouldAttemptPlaybackWithoutThrowing()
    {
        var stage = CreateStage();
        var sound = CreateSoundMock();
        sound.Setup(x => x.CreateInstance()).Returns((Microsoft.Xna.Framework.Audio.SoundEffectInstance)null!);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound> { ["01"] = sound.Object });

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "TriggerBGMEvent", new BGMEvent { WavId = "01" }));

        Assert.Null(exception);
        sound.Verify(x => x.CreateInstance(), Times.Once);
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
    public void TriggerBGMEvent_WhenCreateInstanceThrows_ShouldSwallowPlaybackFailure()
    {
        var stage = CreateStage();
        var sound = CreateSoundMock();
        sound.Setup(x => x.CreateInstance()).Throws(new InvalidOperationException("boom"));
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound> { ["01"] = sound.Object });

        var exception = Record.Exception(() =>
            ReflectionHelpers.InvokePrivateMethod(stage, "TriggerBGMEvent", new BGMEvent { WavId = "01" }));

        Assert.Null(exception);
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
    public void InitializeReadyFont_WhenSpriteBatchIsMissing_ShouldLeaveFontNull()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);
        ReflectionHelpers.SetPrivateField(stage, "_resourceManager", new Mock<IResourceManager>().Object);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "InitializeReadyFont"));

        Assert.Null(exception);
        Assert.Null(ReflectionHelpers.GetPrivateField<BitmapFont>(stage, "_readyFont"));
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

    private static PerformanceStage CreateStage(BaseGame? game = null)
    {
#pragma warning disable SYSLIB0050
        var stage = (PerformanceStage)FormatterServices.GetUninitializedObject(typeof(PerformanceStage));
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

    private static Mock<ITexture> CreateTextureMock()
    {
        var texture = new Mock<ITexture>();
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
