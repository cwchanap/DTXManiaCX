using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Stage-level tests for the background work scope lifecycle (HPA-799 Task 1):
    /// one scope per Activate, retire before OnDeactivate, drain before stage
    /// logic/input, and mid-drain deactivation safety. Driven via stage.Update on
    /// a real activated stage, matching the brief's required test surface.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BaseStageBackgroundWorkTests
    {
        #region Helpers

        private static void SpinUntilTrue(Func<bool> condition, string message)
        {
            Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), message);
        }

        private static int PendingQueueCount(StageBackgroundWorkScope scope)
        {
            var field = typeof(StageBackgroundWorkScope).GetField("_queue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            var queue = Assert.IsAssignableFrom<System.Collections.ICollection>(field!.GetValue(scope));
            return queue.Count;
        }

        private static TestStage CreateStage(string? backgroundPath = null)
        {
            return new TestStage(
                ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object),
                StageType.Title,
                backgroundPath);
        }

        private static TaskCompletionSource<bool> NewGate()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        #endregion

        #region Drain before stage logic

        [Fact]
        public void Update_ShouldDrainPostedWorkBeforeStageLogic()
        {
            var stage = CreateStage();
            var order = new List<string>();
            stage.Order = order;

            stage.Activate();
            Assert.NotNull(stage.ActivatedScope);
            stage.ActivatedScope!.Post(() => order.Add("work"));

            stage.Update(0.016);

            // The drained callback runs before OnFirstUpdate/OnUpdate — i.e. before
            // any stage logic or input handling for the frame.
            Assert.Equal(new[] { "work", "first", "update" }, order);
        }

        [Fact]
        public void Update_ShouldApplyAtMost64ActionsPerFrame()
        {
            // The 64-callback frame budget is a design contract, so the test pins the
            // literal rather than deriving it from the implementation constant: a
            // budget change must consciously update this test.
            const int budget = 64;
            var stage = CreateStage();
            var order = new List<string>();
            stage.Order = order;

            stage.Activate();
            var scope = stage.ActivatedScope!;
            for (var i = 0; i < budget + 6; i++)
            {
                scope.Post(() => order.Add("w"));
            }

            stage.Update(0.016);

            Assert.Equal(budget, order.Count(entry => entry == "w"));
            Assert.Equal(1, order.Count(entry => entry == "first"));
            Assert.Equal(1, order.Count(entry => entry == "update"));

            stage.Update(0.016);

            Assert.Equal(budget + 6, order.Count(entry => entry == "w"));
            Assert.Equal(2, order.Count(entry => entry == "update"));
        }

        #endregion

        #region Scope lifecycle across activation

        [Fact]
        public void Activate_ShouldCreateFreshScopeAndRetireBeforeOnDeactivate()
        {
            var stage = CreateStage();

            stage.Activate();

            var first = stage.ActivatedScope;
            Assert.NotNull(first);
            Assert.False(first!.Token.IsCancellationRequested);

            stage.Deactivate();

            Assert.True(first.Token.IsCancellationRequested);
            Assert.True(stage.ScopeRetiredBeforeOnDeactivate);
            Assert.True(stage.BackgroundWorkWasNullAtDeactivate);

            stage.Activate();

            var second = stage.ActivatedScope;
            Assert.NotNull(second);
            Assert.NotSame(first, second);
            Assert.False(second!.Token.IsCancellationRequested);
        }

        [Fact]
        public void Deactivate_ShouldDropQueuedResultSynchronouslyWithoutSubsequentUpdate()
        {
            var stage = CreateStage();
            stage.Activate();
            var scope = stage.ActivatedScope!;
            var applied = new List<string>();
            var dropped = new List<string>();
            var gate = NewGate();
            scope.Run<string>(
                _ => { gate.Task.Wait(TimeSpan.FromSeconds(5)); return "payload"; },
                applied.Add,
                onDropped: dropped.Add);
            gate.SetResult(true);
            SpinUntilTrue(() => PendingQueueCount(scope) == 1, "result was not enqueued");

            stage.Deactivate();

            Assert.Equal(new[] { "payload" }, dropped);
            Assert.Empty(applied);

            stage.Update(0.016);

            Assert.Empty(applied);
            Assert.Equal(0, stage.UpdateCalls);
        }

        [Fact]
        public void Dispose_WhenActive_ShouldRetireScopeSynchronously()
        {
            var stage = CreateStage();
            stage.Activate();
            var scope = stage.ActivatedScope!;
            var dropped = new List<int>();
            var report = scope.Latest<int>(_ => { }, dropped.Add);
            report(9);

            stage.Dispose();

            Assert.Equal(new[] { 9 }, dropped);
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        #endregion

        #region Mid-drain deactivation safety

        [Fact]
        public void Update_WhenDrainedCallbackChangesStageInstantly_ShouldDropRemainingWorkAndStopStageLogic()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();
            // A real StageManager completes an InstantTransition synchronously,
            // deactivating the previous stage inside ChangeStage. Mimic exactly that.
            stageManager
                .Setup(m => m.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()))
                .Callback(() => stage.Deactivate());
            stage.StageManager = stageManager.Object;

            var order = new List<string>();
            var dropped = new ConcurrentQueue<string>();
            var droppedSignal = NewGate();
            stage.Order = order;

            stage.Activate();
            var scope = stage.ActivatedScope!;
            scope.Post(() =>
            {
                order.Add("transition");
                stage.ForwardChangeStage(StageType.Result);
            });
            scope.Post(() => order.Add("second"));
            scope.Run<string>(
                _ => "owned",
                _ => order.Add("owned-applied"),
                onDropped: v =>
                {
                    dropped.Enqueue(v);
                    droppedSignal.TrySetResult(true);
                });
            scope.Post(() => order.Add("third"));

            // Wait for the owned result to be enqueued so this test exercises the
            // synchronous removal of an already-queued owned item, not the
            // completion-after-retirement direct-drop path.
            SpinUntilTrue(() => PendingQueueCount(scope) == 4, "owned result was not enqueued");

            stage.Update(0.016);
            Assert.True(droppedSignal.Task.Wait(TimeSpan.FromSeconds(5)), "owned result was not dropped");

            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Equal(new[] { "transition" }, order);
            Assert.Equal(new[] { "owned" }, dropped);
            Assert.Equal(0, stage.FirstUpdateCalls);
            Assert.Equal(0, stage.UpdateCalls);
            stageManager.Verify(
                m => m.ChangeStage(StageType.Result, It.Is<IStageTransition>(t => t is InstantTransition)),
                Times.Once);
        }

        [Fact]
        public void Update_WhenFirstUpdateDeactivatesStage_ShouldNotContinueStageLogic()
        {
            var stage = CreateStage();
            stage.Activate();
            stage.FirstUpdateAction = () => stage.Deactivate();

            stage.Update(0.016);

            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Equal(1, stage.FirstUpdateCalls);
            Assert.Equal(0, stage.UpdateCalls);
        }

        #endregion

        #region Headless test seam

        [Fact]
        public void InstallTestBackgroundScope_ShouldProvideDrainingScopeWithoutActivation()
        {
            var stage = CreateStage();
            Assert.Null(stage.ActivatedScope);

            var scope = stage.InstallTestBackgroundScope();
            var applied = new List<int>();
            scope.Post(() => applied.Add(1));

            scope.Drain();

            Assert.Equal(new[] { 1 }, applied);

            // Re-installing retires the previous scope synchronously.
            var second = stage.InstallTestBackgroundScope();
            Assert.NotSame(scope, second);
            Assert.True(scope.Token.IsCancellationRequested);
        }

        [Fact]
        public void Activate_AfterInstallTestBackgroundScope_ShouldRetireInstalledScopeAndCreateFresh()
        {
            var stage = CreateStage();
            var installed = stage.InstallTestBackgroundScope();

            stage.Activate();

            var auto = stage.ActivatedScope;
            Assert.NotNull(auto);
            Assert.NotSame(installed, auto);
            Assert.True(installed.Token.IsCancellationRequested);
            Assert.False(auto!.Token.IsCancellationRequested);
        }

        #endregion

        #region BaseGame queue isolation

        [Fact]
        public void StageScopeDrain_ShouldLeaveBaseGameApiQueueUntouched()
        {
            // Parameterless CreateGame initializes _mainThreadActions.
            var game = ReflectionHelpers.CreateGame();
            var stage = new TestStage(game, StageType.Title, null);
            stage.Activate();
            var gameQueue = ReflectionHelpers.GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions");
            Assert.NotNull(gameQueue);
            var before = gameQueue!.Count;

            stage.ActivatedScope!.Post(() => { });
            stage.Update(0.016);

            Assert.Equal(before, gameQueue.Count);
            Assert.Equal(0, gameQueue.Count);
        }

        [Fact]
        public void Update_WhenPostedCallbackThrows_ShouldLogErrorAndContinueStageUpdate()
        {
            var game = ReflectionHelpers.CreateGame();
            var logger = new Mock<ILogger>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            ReflectionHelpers.SetPrivateField(game, "_loggerFactory", loggerFactory.Object);

            var stage = new TestStage(game, StageType.Title, null);
            stage.Activate();
            var boom = new InvalidOperationException("posted failure");
            stage.ActivatedScope!.Post(() => throw boom);

            var updateEx = Record.Exception(() => stage.Update(0.016));

            Assert.Null(updateEx);
            Assert.Equal(1, stage.UpdateCalls);
            logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    boom,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        /// <summary>
        /// Minimal headless-friendly stage capturing the scope lifecycle for asserts.
        /// </summary>
        private sealed class TestStage : BaseStage
        {
            private readonly StageType _type;
            private readonly string? _backgroundPath;

            public TestStage(IStageGame game, StageType type, string? backgroundPath)
                : base(game)
            {
                _type = type;
                _backgroundPath = backgroundPath;
            }

            public override StageType Type => _type;

            public List<string> Order { get; set; } = new();
            public Action? FirstUpdateAction { get; set; }
            public StageBackgroundWorkScope? ActivatedScope { get; private set; }
            public bool ScopeRetiredBeforeOnDeactivate { get; private set; }
            public bool BackgroundWorkWasNullAtDeactivate { get; private set; }
            public int FirstUpdateCalls { get; private set; }
            public int UpdateCalls { get; private set; }

            public void ForwardChangeStage(StageType stageType, IStageTransition? transition = null)
            {
                ChangeStage(stageType, transition);
            }

            protected override string? GetBackgroundTexturePath()
            {
                return _backgroundPath;
            }

            protected override void OnActivate()
            {
                ActivatedScope = BackgroundWork;
            }

            protected override void OnDeactivate()
            {
                // Runs after RetireBackgroundWork: the scope must already be gone.
                BackgroundWorkWasNullAtDeactivate = BackgroundWork == null;
                ScopeRetiredBeforeOnDeactivate = ActivatedScope == null || ActivatedScope.Token.IsCancellationRequested;
            }

            protected override void OnFirstUpdate(double deltaTime)
            {
                FirstUpdateCalls++;
                Order.Add("first");
                FirstUpdateAction?.Invoke();
            }

            protected override void OnUpdate(double deltaTime)
            {
                UpdateCalls++;
                Order.Add("update");
            }
        }
    }
}
