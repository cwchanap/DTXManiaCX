using System;
using System.Collections.Generic;
using System.IO;
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
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

#pragma warning disable SYSLIB0050

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class PerformanceStageAdditionalCoverageTests
{
    [Fact]
    public void OnSkillChanged_WithSkillPanelDisplay_ShouldUpdateSkillAndShowMax()
    {
        var stage = CreateStage();
        var panel = CreateSkillPanelDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", panel);
        ReflectionHelpers.SetPrivateField(stage, "_skillMeterDisplay", null);

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
        var stage = CreateStage();
        var meter = CreateSkillMeterDisplay();
        ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", null);
        ReflectionHelpers.SetPrivateField(stage, "_skillMeterDisplay", meter);

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
    public void OnPlayerFailed_NoFailDisabled_ShouldFinalizePerformance()
    {
        var configData = new ConfigData { NoFail = false };
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(configData);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = CreateChartManagerWithSingleNote();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager",
            new JudgementManager(new MockInputManagerCompat(), chartManager));
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());
        ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

        var args = new FailureEventArgs { FinalLife = 0.5f, JudgementType = JudgementType.Miss };
        ReflectionHelpers.InvokePrivateMethod(stage, "OnPlayerFailed", null, args);

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(StageType.Result, It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Once);
    }

    [Fact]
    public void OnPlayerFailed_NoFailEnabled_ShouldNotFinalize()
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

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
        stageManager.Verify(
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
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
            x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void FinalizePerformance_SongComplete_ShouldBuildSummaryWithClearFlag()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(chartManager.TotalNotes));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.True(summary.ClearFlag);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
    }

    [Fact]
    public void FinalizePerformance_PlayerFailed_ShouldBuildSummaryWithoutClearFlag()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = CreateChartManagerWithSingleNote();
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager",
            new JudgementManager(new MockInputManagerCompat(), chartManager));
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.PlayerFailed);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.False(summary.ClearFlag);
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
    }

    [Fact]
    public void FinalizePerformance_WithSkillManager_ShouldCalculateGameSkill()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = CreateChartManagerWithSingleNote();
        var comboManager = new ComboManager();
        var skillManager = new SkillManager(chartManager.TotalNotes, comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager",
            new JudgementManager(new MockInputManagerCompat(), chartManager));
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(chartManager.TotalNotes));
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());
        ReflectionHelpers.SetPrivateField(stage, "_skillManager", skillManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "FinalizePerformance", CompletionReason.SongComplete);

        var summary = ReflectionHelpers.GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(skillManager.CurrentSkill, summary.PlayingSkill);
        var expectedGameSkill = SongScore.CalculateGameSkill(skillManager.CurrentSkill, 0, 0);
        Assert.Equal(expectedGameSkill, summary.GameSkill);
    }

    [Fact]
    public void TransitionToResultStage_ShouldIncludePerformanceSummary()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        Dictionary<string, object>? capturedSharedData = null;
        stageManager
            .Setup(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()))
            .Callback<StageType, IStageTransition, Dictionary<string, object>>((_, _, sd) => capturedSharedData = sd);
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
            x => x.ChangeStage(StageType.Result, It.IsAny<InstantTransition>(), It.IsAny<Dictionary<string, object>>()),
            Times.Once);
        Assert.NotNull(capturedSharedData);
        Assert.Same(summary, capturedSharedData["performanceSummary"]);
        Assert.Same(selectedSong, capturedSharedData["selectedSong"]);
        Assert.Equal(2, capturedSharedData["selectedDifficulty"]);
    }

    [Fact]
    public void CleanupGameplayManagers_ShouldUnsubscribeEvents()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var scoreManager = new ScoreManager(chartManager.TotalNotes);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        var skillManager = new SkillManager(chartManager.TotalNotes, comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_skillManager", skillManager);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", new MockInputManagerCompat());

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupGameplayManagers");

        Assert.Null(ReflectionHelpers.GetPrivateField<JudgementManager>(stage, "_judgementManager"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ScoreManager>(stage, "_scoreManager"));
        Assert.Null(ReflectionHelpers.GetPrivateField<ComboManager>(stage, "_comboManager"));
        Assert.Null(ReflectionHelpers.GetPrivateField<GaugeManager>(stage, "_gaugeManager"));
        Assert.Null(ReflectionHelpers.GetPrivateField<SkillManager>(stage, "_skillManager"));
    }

    [Fact]
    public void CleanupGameplayManagers_ShouldDisposeManagers()
    {
        var stage = CreateStage();
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
        var scoreManager = new ScoreManager(chartManager.TotalNotes);
        var comboManager = new ComboManager();
        var gaugeManager = new GaugeManager();
        var skillManager = new SkillManager(chartManager.TotalNotes, comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
        ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
        ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
        ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
        ReflectionHelpers.SetPrivateField(stage, "_skillManager", skillManager);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", new MockInputManagerCompat());

        ReflectionHelpers.InvokePrivateMethod(stage, "CleanupGameplayManagers");

        Assert.True((bool)typeof(JudgementManager)
            .GetField("_disposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(judgementManager)!);
    }

    [Fact]
    public void LogPerformanceError_WithException_ShouldWriteToConsole()
    {
        var stage = CreateStage();
        var exception = new InvalidOperationException("test error");
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "TestMessage", exception);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("[PerformanceError] TestMessage: test error", output);
        Assert.Contains("[PerformanceError] Stack trace:", output);
    }

    [Fact]
    public void LogPerformanceError_WithoutException_ShouldWriteToConsole()
    {
        var stage = CreateStage();
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "LogPerformanceError", "SimpleMessage", null);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("[PerformanceError] SimpleMessage", output);
        Assert.DoesNotContain("Stack trace:", output);
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
        var timer = (SongTimer)FormatterServices.GetUninitializedObject(typeof(SongTimer));

        ReflectionHelpers.SetPrivateField(stage, "_songTimer", timer);
        ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", null);
        ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
    }

    [Fact]
    public void HandleInput_WhenScrollSpeedIncreasePressed_ShouldAdjustScrollSpeed()
    {
        var configManager = new Mock<IConfigManager>();
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        var inputManager = new ScrollSpeedInputCompat(InputCommandType.IncreaseScrollSpeed);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        configManager.Verify(x => x.AdjustScrollSpeed(It.IsAny<string>(), 1), Times.Once);
    }

    [Fact]
    public void HandleInput_WhenScrollSpeedDecreasePressed_ShouldAdjustScrollSpeed()
    {
        var configManager = new Mock<IConfigManager>();
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        var inputManager = new ScrollSpeedInputCompat(InputCommandType.DecreaseScrollSpeed);
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        configManager.Verify(x => x.AdjustScrollSpeed(It.IsAny<string>(), -1), Times.Once);
    }

    [Fact]
    public void ReturnToSongSelect_ShouldStopTimerAndDeactivateJudgement()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var chartManager = CreateChartManagerWithSingleNote();
        var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager)
        {
            IsActive = true
        };
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);

        ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
        Assert.False(judgementManager.IsActive);
        stageManager.Verify(
            x => x.ChangeStage(StageType.SongSelect, It.Is<IStageTransition>(t => t is DTXManiaFadeTransition), null),
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
    public void ReturnToSongSelect_WhenJudgementManagerNull_ShouldNotThrow()
    {
        var stage = CreateStage();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreateStoppedSongTimer());
        ReflectionHelpers.SetPrivateField(stage, "_judgementManager", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "ReturnToSongSelect"));

        Assert.Null(exception);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_inputPaused"));
    }

    [Fact]
    public void ExtractSharedData_WithNullSharedData_ShouldNotThrow()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_sharedData", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData"));

        Assert.Null(exception);
    }

    [Fact]
    public void ExtractSharedData_WithParsedChart_ShouldSetParsedChart()
    {
        var stage = CreateStage();
        var parsedChart = new ParsedChart("shared-test.dtx") { Bpm = 140.0 };
        var sharedData = new Dictionary<string, object>
        {
            ["selectedSong"] = new SongListNode { Title = "Song" },
            ["selectedDifficulty"] = 2,
            ["songId"] = 42,
            ["parsedChart"] = parsedChart
        };
        ReflectionHelpers.SetPrivateField(stage, "_sharedData", sharedData);

        ReflectionHelpers.InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Same(parsedChart, ReflectionHelpers.GetPrivateField<ParsedChart>(stage, "_parsedChart"));
        Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
        Assert.Equal(42, ReflectionHelpers.GetPrivateField<int>(stage, "_songId"));
    }

    [Fact]
    public void InitializeAutoPlay_WhenConfigAutoPlayTrue_ShouldEnableAutoPlay()
    {
        var configData = new ConfigData { AutoPlay = true };
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(configData);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);
        ReflectionHelpers.SetPrivateField(stage, "_autoPlayNoteIndex", 5);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_autoPlayEnabled"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_autoPlayNoteIndex"));
    }

    [Fact]
    public void InitializeAutoPlay_WhenConfigAutoPlayFalse_ShouldDisableAutoPlay()
    {
        var configData = new ConfigData { AutoPlay = false };
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(configData);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager.Object);
        var stage = CreateStage(game);

        ReflectionHelpers.InvokePrivateMethod(stage, "InitializeAutoPlay");

        Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_autoPlayEnabled"));
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

    private static PerformanceStage CreateStage(BaseGame? game = null)
    {
        var stage = (PerformanceStage)FormatterServices.GetUninitializedObject(typeof(PerformanceStage));
        ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
        return stage;
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
        var timer = (SongTimer)FormatterServices.GetUninitializedObject(typeof(SongTimer));
        ReflectionHelpers.SetPrivateField(timer, "_disposed", true);
        return timer;
    }

    private static SkillPanelDisplay CreateSkillPanelDisplay()
    {
        return (SkillPanelDisplay)FormatterServices.GetUninitializedObject(typeof(SkillPanelDisplay));
    }

    private static SkillMeterDisplay CreateSkillMeterDisplay()
    {
        return (SkillMeterDisplay)FormatterServices.GetUninitializedObject(typeof(SkillMeterDisplay));
    }

    private static NoteRenderer CreateNoteRenderer()
    {
        var renderer = (NoteRenderer)FormatterServices.GetUninitializedObject(typeof(NoteRenderer));
        var whiteTexture = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
        var lanePositions = new Vector2[PerformanceUILayout.LaneCount];
        for (var i = 0; i < lanePositions.Length; i++)
        {
            lanePositions[i] = new Vector2(PerformanceUILayout.GetLaneX(i) - 16f, 0f);
        }

        ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", whiteTexture);
        ReflectionHelpers.SetPrivateField(renderer, "_lanePositions", lanePositions);
        ReflectionHelpers.SetPrivateField(renderer, "_laneColors", System.Linq.Enumerable.Repeat(Color.White, PerformanceUILayout.LaneCount).ToArray());
        ReflectionHelpers.SetPrivateField(renderer, "_laneFlashAlpha", new float[10]);
        ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 0.5);
        ReflectionHelpers.SetPrivateField(renderer, "<EffectiveLookAheadMs>k__BackingField", 1200.0);
        ReflectionHelpers.SetPrivateField(renderer, "_disposed", false);
        return renderer;
    }

    private sealed class ScrollSpeedInputCompat : MockInputManagerCompat
    {
        private readonly InputCommandType _activeCommand;

        public ScrollSpeedInputCompat(InputCommandType activeCommand)
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

#pragma warning restore SYSLIB0050
