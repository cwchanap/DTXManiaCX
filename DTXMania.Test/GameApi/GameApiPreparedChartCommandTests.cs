using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.Stage;
using DTXMania.Test.TestData;
using Moq;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.GameApi;

[Trait("Category", "Unit")]
public sealed class GameApiPreparedChartCommandTests
{
    [Fact]
    public async Task PrepareVideoChartAsync_WhenStageCommandFails_ShouldPropagateSafeErrorText()
    {
        var stage = SongSelectionStageTestFactory.CreateStage();
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(stage);
        stage.StageManager = stageManager.Object;
        var queuedActions = new List<Action>();
        var context = new Mock<IGameContext>();
        context.SetupGet(game => game.StageManager).Returns(stageManager.Object);
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(context.Object);

        var commandTask = api.PrepareVideoChartAsync(" ");

        Assert.False(commandTask.IsCompleted);
        Assert.Single(queuedActions);
        queuedActions[0]();

        var result = await commandTask;

        Assert.False(result.Success);
        Assert.Equal("A chart path is required.", result.Error);
    }

    [Fact]
    public async Task ActivatePreparedChartAsync_ShouldNotMutateOrCompleteBeforeQueuedActionRuns()
    {
        var fixture = CreateBlockedActivationFixture();
        var queuedActions = new List<Action>();
        fixture.Context
            .Setup(context => context.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(fixture.Context.Object);

        var commandTask = api.ActivatePreparedChartAsync();

        Assert.False(commandTask.IsCompleted);
        Assert.NotNull(ReflectionHelpers.GetPrivateField<object>(fixture.Stage, "_preparedChartSelection"));
        Assert.Single(queuedActions);
        fixture.StageManager.Verify(
            manager => manager.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);

        queuedActions[0]();
        var result = await commandTask;

        Assert.False(result.Success);
        Assert.Equal("The song transition is currently blocked.", result.Error);
        Assert.NotNull(ReflectionHelpers.GetPrivateField<object>(fixture.Stage, "_preparedChartSelection"));
        fixture.StageManager.Verify(
            manager => manager.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public async Task StartPreparedPreviewAsync_WhenStageCommandFails_ShouldPropagateSafeErrorText()
    {
        var stage = SongSelectionStageTestFactory.CreateStage();
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(stage);
        stage.StageManager = stageManager.Object;
        var queuedActions = new List<Action>();
        var context = new Mock<IGameContext>();
        context.SetupGet(game => game.StageManager).Returns(stageManager.Object);
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(context.Object);

        var commandTask = api.StartPreparedPreviewAsync();

        Assert.False(commandTask.IsCompleted);
        Assert.Single(queuedActions);

        queuedActions[0]();
        var result = await commandTask;

        Assert.False(result.Success);
        Assert.Equal("No prepared chart preview is available.", result.Error);
    }

    [Fact]
    public async Task CancelPreparedChartAsync_WhenCurrentStageIsNotSongSelect_ShouldReturnFailure()
    {
        var currentStage = new Mock<IStage>();
        currentStage.SetupGet(stage => stage.Type).Returns(StageType.Title);
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(currentStage.Object);
        var queuedActions = new List<Action>();
        var context = new Mock<IGameContext>();
        context.SetupGet(game => game.StageManager).Returns(stageManager.Object);
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(context.Object);

        var commandTask = api.CancelPreparedChartAsync();

        Assert.False(commandTask.IsCompleted);
        queuedActions[0]();
        var result = await commandTask;

        Assert.False(result.Success);
        Assert.Equal("Prepared chart commands require the Song Select stage.", result.Error);
    }

    private static BlockedActivationFixture CreateBlockedActivationFixture()
    {
        var game = ReflectionHelpers.CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0d);
        var stage = SongSelectionStageTestFactory.CreateStage(game);
        var stageManager = new Mock<IStageManager>();
        var node = CreateNode();
        stage.StageManager = stageManager.Object;
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(stage);
        ReflectionHelpers.SetPrivateField(stage, "_selectedSong", node);
        ReflectionHelpers.SetPrivateField(stage, "_currentDifficulty", 0);
        ReflectionHelpers.SetPrivateField(
            stage,
            "_preparedChartSelection",
            CreatePreparedSelection(stage, node));
        ReflectionHelpers.SetPrivateField(
            stage,
            "_preparedPreviewState",
            Enum.Parse(
                ReflectionHelpers.GetField(typeof(SongSelectionStage), "_preparedPreviewState")!.FieldType,
                "Prepared"));

        var context = new Mock<IGameContext>();
        context.SetupGet(gameContext => gameContext.StageManager).Returns(stageManager.Object);
        return new BlockedActivationFixture(stage, stageManager, context);
    }

    private static object CreatePreparedSelection(SongSelectionStage stage, SongListNode node)
    {
        var field = ReflectionHelpers.GetField(typeof(SongSelectionStage), "_preparedChartSelection");
        var chart = node.DatabaseChart!;
        return Activator.CreateInstance(
            field!.FieldType,
            node,
            chart,
            0,
            $"chart:{chart.Id}")!;
    }

    private static SongListNode CreateNode()
    {
        var chartPath = Path.Combine(Path.GetTempPath(), "hpa510-api-command.dtx");
        var chart = new SongChart
        {
            Id = 901,
            FilePath = chartPath,
            HasDrumChart = true,
            DrumLevel = 5
        };
        var song = new SongEntity { Id = 901, Title = "prepared", Charts = new List<SongChart> { chart } };
        chart.Song = song;
        chart.SongId = song.Id;
        return new SongListNode
        {
            Type = NodeType.Score,
            Title = song.Title,
            DatabaseSongId = song.Id,
            DatabaseSong = song,
            DatabaseChart = chart,
        };
    }

    private sealed record BlockedActivationFixture(
        SongSelectionStage Stage,
        Mock<IStageManager> StageManager,
        Mock<IGameContext> Context);
}
