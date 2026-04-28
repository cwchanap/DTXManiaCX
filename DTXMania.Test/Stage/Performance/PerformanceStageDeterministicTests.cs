using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
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
    public void ProcessAutoPlay_WhenNoteWasAlreadyResolved_ShouldAdvanceIndexWithoutTriggeringPadPress()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var padRenderer = CreatePadRenderer();
        judgementManager.TestTriggerLaneHit(0, "PreResolved");
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
    public void OnJudgementMade_WhenJudgementIsHit_ShouldForwardToManagersAndTriggerVisualFeedback()
    {
        var stage = CreateStage();
        var scoreManager = new ScoreManager(10);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        var effectsManager = CreateEffectsManager();
        var noteRenderer = CreateNoteRenderer();
        var popupManager = CreateJudgementTextPopupManager();
        var padRenderer = CreatePadRenderer();
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_effectsManager", effectsManager);
        ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", noteRenderer);
        ReflectionHelpers.SetPrivateField(stage, "_judgementTextPopupManager", popupManager);
        ReflectionHelpers.SetPrivateField(stage, "_padRenderer", padRenderer);

        var judgement = new JudgementEvent(noteRef: 1, lane: 2, deltaMs: 0.0, type: JudgementType.Great);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null, judgement);

        Assert.Equal(scoreManager.CalculateScoreForJudgement(JudgementType.Great), scoreManager.CurrentScore);
        Assert.Equal(1, comboManager.CurrentCombo);
        Assert.Equal(51.5f, gaugeManager.CurrentLife);
        Assert.Equal(1.0f, ReflectionHelpers.GetPrivateField<float[]>(noteRenderer, "_laneFlashAlpha")[2]);
        Assert.Equal(PadState.Pressed, ReflectionHelpers.GetPrivateField<PadVisual[]>(padRenderer, "_padVisuals")[2].State);
        Assert.Single(ReflectionHelpers.GetPrivateField<List<JudgementTextPopup>>(popupManager, "_activePopups"));
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
