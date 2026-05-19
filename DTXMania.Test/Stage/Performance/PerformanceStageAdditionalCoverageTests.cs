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
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
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
}

#pragma warning restore SYSLIB0050
