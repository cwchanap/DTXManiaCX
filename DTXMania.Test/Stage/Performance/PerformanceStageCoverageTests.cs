using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
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
public class PerformanceStageCoverageTests
{
    [Fact]
    public void OnActivate_WhenConfigManagerNotNull_ShouldSubscribeToScrollSpeedChanged()
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(new ConfigData { AutoPlay = true });
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_sharedData", null);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");
        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

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
        ReflectionHelpers.SetPrivateField(stage, "_sharedData", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");
        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.Null(ReflectionHelpers.GetPrivateField<IConfigManager>(stage, "_subscribedConfigManager"));
    }

    private static void OnScrollSpeedChangedHandler(object? sender, ScrollSpeedChangedEventArgs e) { }

    [Fact]
    public void OnDeactivate_WhenSubscribedConfigManager_ShouldUnsubscribeFlushAndCleanup()
    {
        var configManager = new Mock<IConfigManager>();
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_subscribedConfigManager", configManager.Object);
        ReflectionHelpers.SetPrivateField(stage, "_isLoading", false);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", true);
        ReflectionHelpers.SetPrivateField(stage, "_inputPaused", true);
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 12.0);
        ReflectionHelpers.SetPrivateField(stage, "_stageElapsedTime", 15.0);
        ReflectionHelpers.SetPrivateField(stage, "_readyCountdown", 0.25);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 8);
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("deactivate-test.dtx"));
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());

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
        ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());

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

        configManager.Verify(x => x.AdjustScrollSpeed(It.IsAny<string>(), 1), Times.Once);
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

        configManager.Verify(x => x.AdjustScrollSpeed(It.IsAny<string>(), -1), Times.Once);
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
    public void ReturnToSongSelect_ShouldStopTimerDeactivateJudgementPauseInputAndChangeStage()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote())
        {
            IsActive = true
        };
        var songTimer = CreateStoppedSongTimer();
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", songTimer);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        stageManager.Verify(
            x => x.ChangeStage(
                StageType.SongSelect,
                It.Is<IStageTransition>(t => t is DTXManiaFadeTransition),
                null),
            Times.Once);
    }

    [Fact]
    public void ReturnToSongSelect_WhenSongTimerNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", null);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", null);

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
    public void UpdateGameplay_WhenReadyCountdownExpires_ShouldStartSongAndActivateJudgement()
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
    public void UpdateGameplay_WhenSongPlaying_ShouldAdvanceManagersAndProgress()
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
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("progress-coverage.dtx") { DurationMs = 2000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());

        ReflectionHelpers.InvokePrivateMethod(stage, "UpdateGameplay", 0.016);

        Assert.Equal(1, judgementManager.GetJudgementCount(JudgementType.Miss));
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
    }

    [Fact]
    public void StartSong_WhenNoBgmEvents_ShouldSetVolumeToOne()
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
    public void StartSong_WhenBgmEventsExist_ShouldMuteBackgroundAudio()
    {
        var stage = CreateStage();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote());
        var songTimer = CreateStoppedSongTimer();
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", songTimer);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.016)));
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent> { new() { WavId = "01", TimeMs = 100.0 } });
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
        Assert.True(judgementManager.IsActive);
    }

    [Fact]
    public void StartSong_WhenSongTimerIsNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", null);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "StartSong"));

        Assert.Null(exception);
    }

    [Fact]
    public void FinalizePerformance_ShouldMarkCompletedPauseInputStopTimerCreateSummary()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        Dictionary<string, object>? capturedSharedData = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()))
            .Callback<StageType, IStageTransition, Dictionary<string, object>>((_, _, sharedData) => capturedSharedData = sharedData);
        stage.StageManager = stageManager.Object;

        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var scoreManager = new ScoreManager(chartManager.TotalNotes);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.True(summary.ClearFlag);
        Assert.Equal(chartManager.TotalNotes, summary.TotalNotes);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public void FinalizePerformance_WhenPlayerFailed_ShouldSetClearFlagFalse()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.PlayerFailed);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
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
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("check-complete.dtx") { DurationMs = 1000.0 });
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote()));
        ReflectionHelpers.SetPrivateField(stage, "_totalTime", 10.0);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_chartEndReachedRealTimeSeconds",
            7.0);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        var summary = Assert.IsType<PerformanceSummary>(capturedSharedData!["performanceSummary"]);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.True(summary.ClearFlag);
        stageManager.Verify(x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
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
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", new ParsedChart("check-failed.dtx") { DurationMs = 5000.0 });
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
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void CheckStageCompletion_WhenParsedChartNull_ShouldNotTransition()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "CheckStageCompletion", 5000.0);

        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
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
    public void FinalizePerformance_WhenAllManagersNull_ShouldBuildZeroedSummary()
    {
        var stage = CreateStage();

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(0, summary.Score);
        Assert.Equal(0, summary.MaxCombo);
        Assert.Equal(0, summary.PerfectCount);
        Assert.Equal(0, summary.GreatCount);
        Assert.Equal(0, summary.GoodCount);
        Assert.Equal(0, summary.PoorCount);
        Assert.Equal(0, summary.MissCount);
        Assert.Equal(0, summary.TotalNotes);
        Assert.Equal(0.0f, summary.FinalLife);
        Assert.True(summary.ClearFlag);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
    }

    [Fact]
    public void FinalizePerformance_ShouldStopSongTimerAndDeactivateJudgementManager()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), CreateChartManagerWithSingleNote())
        {
            IsActive = true
        };
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", CreateChartManagerWithSingleNote());

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        Assert.False(judgementManager.IsActive);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
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

    /// <summary>
    /// Test-only <see cref="PerformanceStage"/> that overrides <see cref="PerformanceStage.CreateSpriteBatch"/>
    /// to return null, so the constructor can be exercised without a live GraphicsDevice.
    /// </summary>
    private sealed class HeadlessPerformanceStage : PerformanceStage
    {
        public HeadlessPerformanceStage(IStageGame game) : base(game) { }

        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
    }

    private static PerformanceStage CreateStage(BaseGame? game = null)
    {
#pragma warning disable SYSLIB0050
        var stage = (PerformanceStage)FormatterServices.GetUninitializedObject(typeof(PerformanceStage));
#pragma warning restore SYSLIB0050
        ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
        return stage;
    }

    private static IConfigManager CreateConfigManager(ConfigData configData)
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(configData);
        return configManager.Object;
    }

    private static ChartManager CreateChartManagerWithSingleNote()
    {
        var parsedChart = new ParsedChart("coverage-test.dtx")
        {
            Bpm = 120.0
        };
        parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        parsedChart.FinalizeChart();
        return new ChartManager(parsedChart);
    }

    private static SongTimer CreateStoppedSongTimer()
    {
        return new SongTimer();
    }

    private static SongTimer CreatePlayingSongTimer()
    {
        var timer = new SongTimer();
        timer.Play(new GameTime(TimeSpan.Zero, TimeSpan.Zero));
        return timer;
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
}
