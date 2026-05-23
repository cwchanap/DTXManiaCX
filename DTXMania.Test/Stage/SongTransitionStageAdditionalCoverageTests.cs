using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class SongTransitionStageAdditionalCoverageTests
{
    [Fact]
    public void HandleInput_WhenInputManagerNull_ShouldReturn()
    {
        var stage = CreateStage();
        ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput"));

        Assert.Null(exception);
    }

    [Fact]
    public void ExecuteInputCommand_Activate_ShouldTransitionToPerformance()
    {
        var stageManager = new Mock<IStageManager>();
        var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
        var stage = CreateStage(game);
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 50, HasDrumChart = true }));

        ReflectionHelpers.InvokePrivateMethod(stage, "ExecuteInputCommand",
            new InputCommand(InputCommandType.Activate, 0.0));

        stageManager.Verify(
            x => x.ChangeStage(
                StageType.Performance,
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Once);
    }

    [Fact]
    public void ExecuteInputCommand_Back_ShouldTransitionToSongSelect()
    {
        var stageManager = new Mock<IStageManager>();
        var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
        var stage = CreateStage(game);
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "ExecuteInputCommand",
            new InputCommand(InputCommandType.Back, 0.0));

        stageManager.Verify(
            x => x.ChangeStage(
                StageType.SongSelect,
                It.Is<IStageTransition>(t => t is InstantTransition)),
            Times.Once);
    }

    [Fact]
    public void ExecuteInputCommand_WhenCannotTransition_ShouldNotTransition()
    {
        var stageManager = new Mock<IStageManager>();
        var game = ReflectionHelpers.CreateGame(totalGameTime: 0.05, lastStageTransitionTime: 0.0);
        var stage = CreateStage(game);
        stage.StageManager = stageManager.Object;

        ReflectionHelpers.InvokePrivateMethod(stage, "ExecuteInputCommand",
            new InputCommand(InputCommandType.Activate, 0.0));

        stageManager.Verify(
            x => x.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void TransitionToPerformance_WhenAlreadyFadingOut_ShouldReturn()
    {
        var stageManager = new Mock<IStageManager>();
        var stage = CreateStage();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.FadeOut);

        ReflectionHelpers.InvokePrivateMethod(stage, "TransitionToPerformance");

        stageManager.Verify(
            x => x.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void PerformTransition_WithoutSelectedSong_ShouldTransitionWithEmptyData()
    {
        var stageManager = new Mock<IStageManager>();
        var stage = CreateStage();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", null);

        ReflectionHelpers.InvokePrivateMethod(stage, "PerformTransition");

        stageManager.Verify(
            x => x.ChangeStage(
                StageType.Performance,
                It.Is<IStageTransition>(t => t is InstantTransition),
                It.Is<Dictionary<string, object>>(d => d.Count == 0)),
            Times.Once);
    }

    [Fact]
    public void PerformTransition_WithParsedChart_ShouldIncludeInSharedData()
    {
        var stageManager = new Mock<IStageManager>();
        var stage = CreateStage();
        var selectedSong = CreateSongNode(new SongChart { DrumLevel = 40, HasDrumChart = true });
        var parsedChart = new ParsedChart("test.dtx");

        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", selectedSong);
        ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 1);
        ReflectionHelpers.SetPrivateField(stage, "_songId", 77);
        ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);
        ReflectionHelpers.SetPrivateField(stage, "_chartLoaded", true);

        ReflectionHelpers.InvokePrivateMethod(stage, "PerformTransition");

        stageManager.Verify(
            x => x.ChangeStage(
                StageType.Performance,
                It.Is<IStageTransition>(t => t is InstantTransition),
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey("parsedChart") &&
                    ReferenceEquals(d["parsedChart"], parsedChart))),
            Times.Once);
    }

    #region Helpers

    private static SongTransitionStage CreateStage(BaseGame? game = null)
    {
        game ??= ReflectionHelpers.CreateGame();
        return new SongTransitionStage(game);
    }

    private static SongListNode CreateSongNode(SongChart chart)
    {
        var song = new SongEntity
        {
            Title = "Test Song",
            Artist = "Test Artist",
            Charts = new List<SongChart> { chart },
        };
        chart.Song = song;
        return new SongListNode
        {
            DatabaseSong = song,
            DatabaseChart = chart,
            Title = song.Title,
        };
    }

    #endregion
}
