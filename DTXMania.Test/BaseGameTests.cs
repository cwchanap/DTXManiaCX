using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Test.TestData;

namespace DTXMania.Test
{
    [Trait("Category", "Unit")]
    public class BaseGameTests
    {
        [Theory]
        [InlineData(0.1, 0.0, false)]
        [InlineData(GameConstants.StageTransition.DebounceDelaySeconds, 0.0, true)]
        [InlineData(2.0, 1.0, true)]
        public void CanPerformStageTransition_ShouldRespectDebounceThreshold(
            double totalGameTime,
            double lastStageTransitionTime,
            bool expected)
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime, lastStageTransitionTime);

            Assert.Equal(expected, game.CanPerformStageTransition());
        }

        [Fact]
        public void MarkStageTransition_ShouldCaptureCurrentGameTime()
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime: 3.25, lastStageTransitionTime: 0.0);

            game.MarkStageTransition();

            Assert.Equal(3.25, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void QueueMainThreadAction_ShouldEnqueueAction()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;
            var queue = ReflectionHelpers.GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions");
            var executed = false;

            context.QueueMainThreadAction(() => executed = true);

            Assert.True(queue.TryDequeue(out var action));
            action();
            Assert.True(executed);
        }

        [Fact]
        public void QueueMainThreadAction_WithNullAction_ShouldThrowArgumentNullException()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;

            Assert.Throws<ArgumentNullException>(() => context.QueueMainThreadAction(null!));
        }

        [Fact]
        public void CaptureScreenshotAsync_WhenNoPendingRequest_ShouldStorePendingTask()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;

            var task = context.CaptureScreenshotAsync();

            Assert.False(task.IsCompleted);
            var pendingScreenshot = ReflectionHelpers.GetPrivateField<TaskCompletionSource<byte[]?>>(game, "_pendingScreenshot");
            Assert.NotNull(pendingScreenshot);
            Assert.Same(pendingScreenshot!.Task, task);
        }

        [Fact]
        public async Task CaptureScreenshotAsync_WhenRequestAlreadyPending_ShouldReturnCompletedNullTask()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;

            _ = context.CaptureScreenshotAsync();
            var secondRequest = context.CaptureScreenshotAsync();

            Assert.True(secondRequest.IsCompletedSuccessfully);
            Assert.Null(await secondRequest);
        }

        [Fact]
        public void CaptureRenderTargetAsPng_WhenRenderTargetIsNull_ShouldReturnNull()
        {
            var method = typeof(BaseGame).GetMethod(
                "CaptureRenderTargetAsPng",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var result = (byte[]?)method!.Invoke(null, new object?[] { null });

            Assert.Null(result);
        }
    }
}
