using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;

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
            var game = CreateGame(totalGameTime, lastStageTransitionTime);

            Assert.Equal(expected, game.CanPerformStageTransition());
        }

        [Fact]
        public void MarkStageTransition_ShouldCaptureCurrentGameTime()
        {
            var game = CreateGame(totalGameTime: 3.25, lastStageTransitionTime: 0.0);

            game.MarkStageTransition();

            Assert.Equal(3.25, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void QueueMainThreadAction_ShouldEnqueueAction()
        {
            var game = CreateGame();
            var context = (IGameContext)game;
            var queue = GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions");
            var executed = false;

            context.QueueMainThreadAction(() => executed = true);

            Assert.True(queue.TryDequeue(out var action));
            action();
            Assert.True(executed);
        }

        [Fact]
        public void QueueMainThreadAction_WithNullAction_ShouldThrowArgumentNullException()
        {
            var game = CreateGame();
            var context = (IGameContext)game;

            Assert.Throws<ArgumentNullException>(() => context.QueueMainThreadAction(null!));
        }

        [Fact]
        public void CaptureScreenshotAsync_WhenNoPendingRequest_ShouldStorePendingTask()
        {
            var game = CreateGame();
            var context = (IGameContext)game;

            var task = context.CaptureScreenshotAsync();

            Assert.False(task.IsCompleted);
            var pendingScreenshot = GetPrivateField<TaskCompletionSource<byte[]?>>(game, "_pendingScreenshot");
            Assert.NotNull(pendingScreenshot);
            Assert.Same(pendingScreenshot!.Task, task);
        }

        [Fact]
        public async Task CaptureScreenshotAsync_WhenRequestAlreadyPending_ShouldReturnCompletedNullTask()
        {
            var game = CreateGame();
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

        private static BaseGame CreateGame(double totalGameTime = 0.0, double lastStageTransitionTime = 0.0)
        {
#pragma warning disable SYSLIB0050
            var game = (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));
#pragma warning restore SYSLIB0050

            SetPrivateField(game, "_mainThreadActions", new ConcurrentQueue<Action>());
            SetPrivateField(game, "_pendingScreenshot", null);
            SetPrivateField(game, "_totalGameTime", totalGameTime);
            SetPrivateField(game, "_lastStageTransitionTime", lastStageTransitionTime);

            return game;
        }

        private static T? GetPrivateField<T>(object target, string fieldName)
        {
            var field = GetField(target.GetType(), fieldName);
            Assert.NotNull(field);
            return (T?)field!.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = GetField(target.GetType(), fieldName);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        private static FieldInfo? GetField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType!;
            }

            return null;
        }
    }
}
