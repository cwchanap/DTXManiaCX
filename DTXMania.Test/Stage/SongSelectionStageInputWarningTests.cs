using System;
using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using RD = DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage;

// HPA-796 Task 3: the missing-shared-InputManager case in
// SongSelectionStage.AssignInputManager is a real warning and must travel
// through the stage's ILogger (IStageGame.LoggerFactory) instead of
// Trace.TraceWarning. A provided shared InputManager stays silent.
[Trait("Category", "Unit")]
public class SongSelectionStageInputWarningTests
{
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public Mock<ILogger<SongSelectionStage>> Logger { get; } = new();

        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => Logger.Object;
        public void Dispose() { }
    }

    private static (SongSelectionStage Stage, Mock<ILogger<SongSelectionStage>> Logger) CreateStageWithCapturingLogger()
    {
        var loggerFactory = new CapturingLoggerFactory();

        var game = RD.CreateGame();
        RD.SetPrivateField(game, "_loggerFactory", loggerFactory);

        var stage = SongSelectionStageTestFactory.CreateStage(game);
        // The stage constructor already routes game.InputManager (null in this
        // fixture) through AssignInputManager; measure only the act step.
        loggerFactory.Logger.Invocations.Clear();
        return (stage, loggerFactory.Logger);
    }

    [Fact]
    public void AssignInputManager_WithoutSharedInputManager_ShouldLogWarningOnce()
    {
        var (stage, logger) = CreateStageWithCapturingLogger();

        RD.InvokePrivateMethod(stage, "AssignInputManager", new object?[] { null });

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("InputManager", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        Assert.True(RD.GetPrivateField<bool>(stage, "_ownsInputManager"));
    }

    [Fact]
    public void AssignInputManager_WithSharedInputManager_ShouldStaySilent()
    {
        var (stage, logger) = CreateStageWithCapturingLogger();

        using var shared = new InputManager();
        RD.InvokePrivateMethod(stage, "AssignInputManager", new object?[] { shared });

        logger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
        Assert.False(RD.GetPrivateField<bool>(stage, "_ownsInputManager"));
    }
}
