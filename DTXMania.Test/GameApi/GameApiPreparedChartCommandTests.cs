using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
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

    [Fact]
    public async Task         Dispose_ShouldCompletePendingPreparedCommandAndLeaveQueuedDelegateAsNoOp()
    {
        var stage = SongSelectionStageTestFactory.CreateStage();
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(stage);
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

        api.Dispose();

        var result = await commandTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(result.Success);
        Assert.Equal("The prepared chart command was canceled.", result.Error);

        queuedActions[0]();
        Assert.Equal("The prepared chart command was canceled.", result.Error);
        stageManager.Verify(
            manager => manager.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public async Task         PreparedCommand_WhenRequestCancellationIsSignaled_ShouldCompleteAndLeaveQueuedDelegateAsNoOp()
    {
        var stage = SongSelectionStageTestFactory.CreateStage();
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(stage);
        var queuedActions = new List<Action>();
        var context = new Mock<IGameContext>();
        context.SetupGet(game => game.StageManager).Returns(stageManager.Object);
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(context.Object);

        using var requestCancellation = new CancellationTokenSource();
        var commandTask = api.StartPreparedPreviewAsync(requestCancellation.Token);

        requestCancellation.Cancel();

        var result = await commandTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(result.Success);
        Assert.Equal("The prepared chart command was canceled.", result.Error);

        queuedActions[0]();
        stageManager.Verify(
            manager => manager.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public async Task PreparedCommand_WhenAlreadyDisposed_ShouldReturnCanceledWithoutQueuing()
    {
        var context = new Mock<IGameContext>();
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        var api = new GameApiImplementation(context.Object);

        api.Dispose();

        var result = await api.CancelPreparedChartAsync();

        Assert.False(result.Success);
        Assert.Equal("The prepared chart command was canceled.", result.Error);
        context.Verify(game => game.QueueMainThreadAction(It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public async Task PreparedCommand_WithPreCanceledToken_ShouldReturnCanceledWithoutExecutingCommand()
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

        using var preCanceled = new CancellationTokenSource();
        preCanceled.Cancel();

        var commandTask = api.StartPreparedPreviewAsync(preCanceled.Token);

        var result = await commandTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(result.Success);
        Assert.Equal("The prepared chart command was canceled.", result.Error);
        // The queued action may or may not be present, but if it is, it must be a no-op.
        foreach (var action in queuedActions)
            action();
        stageManager.Verify(
            manager => manager.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public async Task PreparedCommand_WhenQueueMainThreadActionThrows_ShouldReturnFailure()
    {
        var stage = SongSelectionStageTestFactory.CreateStage();
        var stageManager = new Mock<IStageManager>();
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(stage);
        stage.StageManager = stageManager.Object;
        var context = new Mock<IGameContext>();
        context.SetupGet(game => game.StageManager).Returns(stageManager.Object);
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Throws(new InvalidOperationException("queue unavailable"));
        var api = new GameApiImplementation(context.Object);

        var result = await api.CancelPreparedChartAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Equal("The prepared chart command could not be completed.", result.Error);
    }

    [Fact]
    public async Task CancelPreparedChartAsync_WhenStageIsSongSelect_ShouldReturnSuccess()
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

        var commandTask = api.CancelPreparedChartAsync();
        Assert.Single(queuedActions);
        queuedActions[0]();

        var result = await commandTask;
        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task PreparedCommand_WhenStageManagerIsNull_ShouldReturnRequiresSongSelectStage()
    {
        var queuedActions = new List<Action>();
        var context = new Mock<IGameContext>();
        context.SetupGet(game => game.StageManager).Returns((IStageManager?)null);
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(context.Object);

        var commandTask = api.CancelPreparedChartAsync();
        Assert.Single(queuedActions);
        queuedActions[0]();

        var result = await commandTask;
        Assert.False(result.Success);
        Assert.Equal("Prepared chart commands require the Song Select stage.", result.Error);
    }

    [Fact]
    public async Task Dispose_CalledTwice_ShouldBeIdempotent()
    {
        var context = new Mock<IGameContext>();
        var api = new GameApiImplementation(context.Object);

        api.Dispose();
        api.Dispose();

        var result = await api.CancelPreparedChartAsync();
        Assert.False(result.Success);
        Assert.Equal("The prepared chart command was canceled.", result.Error);
    }

    [Fact]
    public async Task PreparedCommand_WhenCommandThrowsOnUpdateThread_ShouldReturnSafeError()
    {
        // QueuePreparedChartCommandAsync wraps the update-thread command in a
        // try/catch so an unexpected exception surfaces as a safe error result
        // instead of propagating to the caller. The previous fixture relied on
        // BuildPreparedChartTelemetryIdentity throwing on a null DatabaseChart,
        // but that path is fully defensive (Try-pattern + catch-all) and never
        // actually threw — the command succeeded and the loose assertion hid it.
        // We now force the throw deterministically by having StageManager.CurrentStage
        // raise on access, which is the first dereference inside the inner try.
        var stageManager = new Mock<IStageManager>();
        stageManager
            .SetupGet(manager => manager.CurrentStage)
            .Throws(new InvalidOperationException("Stage access failed on the update thread."));
        var queuedActions = new List<Action>();
        var context = new Mock<IGameContext>();
        context.SetupGet(game => game.StageManager).Returns(stageManager.Object);
        context
            .Setup(game => game.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(context.Object);

        var commandTask = api.PrepareVideoChartAsync(
            Path.Combine(Path.GetTempPath(), "hpa510-throw.dtx"));
        Assert.Single(queuedActions);
        queuedActions[0]();

        var result = await commandTask;
        Assert.False(result.Success);
        Assert.Equal("The prepared chart command could not be completed.", result.Error);
    }

    [Fact]
    public async Task ActivatePreparedChartAsync_WhenRequestCancelsMidExecution_ShouldLetExecutionOwnCompletion()
    {
        // Reproduces the race in [P2]: cancellation fires after TryBeginExecution
        // succeeds but before the command finishes. The execution path must own
        // completion — the caller should see the command's real result, not
        // "canceled", and the stage transition must have happened.
        var game = ReflectionHelpers.CreateGame(totalGameTime: 1.0, lastStageTransitionTime: 0d);
        var stage = SongSelectionStageTestFactory.CreateStage(game);
        var node = CreateNode();
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        stageManager.SetupGet(manager => manager.CurrentStage).Returns(stage);
        stageManager.SetupGet(manager => manager.IsTransitioning).Returns(false);
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

        var enteredChangeStage = new ManualResetEventSlim(initialState: false);
        var releaseChangeStage = new ManualResetEventSlim(initialState: false);
        stageManager.Setup(
                manager => manager.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()))
            .Callback<StageType, IStageTransition, Dictionary<string, object>>((_, _, _) =>
            {
                enteredChangeStage.Set();
                releaseChangeStage.Wait(TimeSpan.FromSeconds(5));
            });

        var queuedActions = new List<Action>();
        var context = new Mock<IGameContext>();
        context.SetupGet(gameContext => gameContext.StageManager).Returns(stageManager.Object);
        context
            .Setup(gameContext => gameContext.QueueMainThreadAction(It.IsAny<Action>()))
            .Callback<Action>(queuedActions.Add);
        var api = new GameApiImplementation(context.Object);

        using var requestCancellation = new CancellationTokenSource();
        var commandTask = api.ActivatePreparedChartAsync(requestCancellation.Token);
        Assert.Single(queuedActions);

        // Run the queued action on a background thread so we can cancel while
        // the command is blocked inside StageManager.ChangeStage.
        var executionTask = Task.Run(queuedActions[0]);

        Assert.True(enteredChangeStage.Wait(TimeSpan.FromSeconds(5)),
            "ChangeStage was not entered within the timeout.");

        // Cancellation fires while the command is executing (state == Executing).
        requestCancellation.Cancel();

        // Let the command finish. The execution path should own completion.
        releaseChangeStage.Set();
        var result = await commandTask.WaitAsync(TimeSpan.FromSeconds(5));

        await executionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Success);
        Assert.Null(result.Error);
        stageManager.Verify(
            manager => manager.ChangeStage(
                It.Is<StageType>(t => t == StageType.SongTransition),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Once);
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
