using System;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using RD = DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage;

// HPA-796 review follow-up: a timer-driven preview that throws while creating
// or playing its sound instance must surface a single ILogger warning (the
// update loop has no caller to return an error to), keep cleanup, and not retry.
[Trait("Category", "Unit")]
public class SongSelectionStagePreviewWarningTests
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
        loggerFactory.Logger.Invocations.Clear();
        return (stage, loggerFactory.Logger);
    }

    [Fact]
    public void UpdatePreviewSoundTimers_WhenPreviewPlaybackFails_ShouldLogWarningOnceAndNotRetry()
    {
        var (stage, logger) = CreateStageWithCapturingLogger();

        var failure = new InvalidOperationException("audio device lost");
        var previewSound = new Mock<ISound>();
        previewSound.Setup(s => s.CreateInstance()).Throws(failure);

        RD.SetPrivateField(stage, "_previewSound", previewSound.Object);
        RD.SetPrivateField(stage, "_isPreviewDelayActive", true);

        // The delay flag is cleared before playback starts, so a second tick
        // must not retry the failed preview or log again.
        for (var i = 0; i < 2; i++)
        {
            RD.InvokePrivateMethod(
                stage,
                "UpdatePreviewSoundTimers",
                SongSelectionUILayout.Audio.PreviewPlayDelaySeconds);
        }

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Song preview playback failed", StringComparison.Ordinal)),
                It.Is<Exception?>(e => ReferenceEquals(e, failure)),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        previewSound.Verify(s => s.CreateInstance(), Times.Once);
        Assert.Null(RD.GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
        Assert.False(RD.GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
    }
}
