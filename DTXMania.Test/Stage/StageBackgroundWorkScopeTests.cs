using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Isolated tests for the stage-local background work scope (HPA-799 Task 1).
    /// These drive the scope directly via Drain, without a BaseStage lifecycle.
    /// </summary>
    [Trait("Category", "Unit")]
    public class StageBackgroundWorkScopeTests
    {
        #region Helpers

        private static void SpinUntilTrue(Func<bool> condition, string message)
        {
            Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), message);
        }

        /// <summary>
        /// Run completion enqueues behind the same lock that guards the queue, so
        /// tests await a deterministic enqueue by observing the pending item count.
        /// Read-only probe; never mutates the queue.
        /// </summary>
        private static int PendingQueueCount(StageBackgroundWorkScope scope)
        {
            var field = typeof(StageBackgroundWorkScope).GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            var queue = Assert.IsAssignableFrom<ICollection>(field!.GetValue(scope));
            return queue.Count;
        }

        #endregion

        #region Token and Retire lifecycle

        [Fact]
        public void Retire_ShouldCancelToken()
        {
            var scope = new StageBackgroundWorkScope();

            Assert.False(scope.Token.IsCancellationRequested);

            scope.Retire();

            Assert.True(scope.Token.IsCancellationRequested);
        }

        [Fact]
        public void Retire_ShouldBeIdempotentAndDropQueuedResultExactlyOnce()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var dropped = new List<int>();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Run<int>(
                _ => { gate.Task.Wait(TimeSpan.FromSeconds(5)); return 5; },
                applied.Add,
                onDropped: dropped.Add);
            gate.SetResult(true);
            SpinUntilTrue(() => PendingQueueCount(scope) == 1, "result was not enqueued");

            scope.Retire();
            scope.Retire();

            Assert.Equal(new[] { 5 }, dropped);
            Assert.Empty(applied);
        }

        [Fact]
        public void Retire_ShouldContinueDroppingWhenOneCleanupCallbackThrows()
        {
            var scope = new StageBackgroundWorkScope();
            var dropped = new List<string>();
            var gateA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gateB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Run<string>(
                _ => { gateA.Task.Wait(TimeSpan.FromSeconds(5)); return "a"; },
                _ => { },
                onDropped: _ => throw new InvalidOperationException("cleanup a failed"));
            scope.Run<string>(
                _ => { gateB.Task.Wait(TimeSpan.FromSeconds(5)); return "b"; },
                _ => { },
                onDropped: dropped.Add);
            gateA.SetResult(true);
            gateB.SetResult(true);
            SpinUntilTrue(() => PendingQueueCount(scope) == 2, "results were not enqueued");

            var ex = Record.Exception(() => scope.Retire());

            Assert.Null(ex);
            Assert.Equal(new[] { "b" }, dropped);
        }

        [Fact]
        public void Drain_AfterRetire_ShouldNotApplyAnything()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var report = scope.Latest<int>(applied.Add);
            report(1);
            scope.Retire();

            scope.Drain();

            Assert.Empty(applied);
        }

        #endregion

        #region Post — FIFO terminal callbacks

        [Fact]
        public void Post_ShouldApplyInFifoOrderOnDrain()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            scope.Post(() => applied.Add(1));
            scope.Post(() => applied.Add(2));
            scope.Post(() => applied.Add(3));

            scope.Drain();

            Assert.Equal(new[] { 1, 2, 3 }, applied);
            Assert.Equal(0, PendingQueueCount(scope));
        }

        [Fact]
        public void Drain_ShouldApplyAtMost64ActionsPerCall()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = 0;
            for (var i = 0; i < StageBackgroundWorkScope.MaxActionsPerDrain + 6; i++)
            {
                scope.Post(() => applied++);
            }

            scope.Drain();

            Assert.Equal(StageBackgroundWorkScope.MaxActionsPerDrain, applied);
            Assert.Equal(6, PendingQueueCount(scope));

            scope.Drain();

            Assert.Equal(StageBackgroundWorkScope.MaxActionsPerDrain + 6, applied);
            Assert.Equal(0, PendingQueueCount(scope));
        }

        [Fact]
        public void Post_AfterRetire_ShouldNotApply()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            scope.Retire();

            scope.Post(() => applied.Add(1));
            scope.Drain();

            Assert.Empty(applied);
        }

        #endregion

        #region Latest — coalesced progress

        [Fact]
        public void Latest_TenThousandReports_ShouldCoalesceToOneQueuedApplyOfNewest()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var report = scope.Latest<int>(applied.Add);

            for (var i = 0; i < 10000; i++)
            {
                report(i);
            }

            Assert.Equal(1, PendingQueueCount(scope));

            scope.Drain();

            Assert.Single(applied);
            Assert.Equal(9999, applied[0]);
        }

        [Fact]
        public void Latest_ShouldQueueFreshApplyAfterEachDrain()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var report = scope.Latest<int>(applied.Add);

            report(1);
            scope.Drain();
            report(2);
            scope.Drain();

            Assert.Equal(new[] { 1, 2 }, applied);
        }

        [Fact]
        public void Latest_SupersededPendingValue_ShouldDropExactlyOnce()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var dropped = new List<int>();
            var report = scope.Latest<int>(applied.Add, dropped.Add);

            report(1);
            report(2);

            Assert.Equal(new[] { 1 }, dropped);
            Assert.Empty(applied);

            scope.Drain();

            Assert.Equal(new[] { 2 }, applied);
            Assert.Equal(new[] { 1 }, dropped);
        }

        [Fact]
        public void Latest_QueuedPendingValue_ShouldDropSynchronouslyOnRetire()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var dropped = new List<int>();
            var report = scope.Latest<int>(applied.Add, dropped.Add);

            report(7);

            scope.Retire();

            Assert.Equal(new[] { 7 }, dropped);

            scope.Drain();

            Assert.Empty(applied);
        }

        [Fact]
        public void Latest_AfterRetire_ShouldDropReportsDirectly()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var dropped = new List<int>();
            var report = scope.Latest<int>(applied.Add, dropped.Add);
            scope.Retire();

            report(3);

            Assert.Equal(new[] { 3 }, dropped);
            scope.Drain();
            Assert.Empty(applied);
        }

        #endregion

        #region Run — worker results

        [Fact]
        public void Run_Success_ShouldApplyResultOnDrainThreadOnly()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            var applyThreads = new List<int>();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            scope.Run<int>(
                _ => { gate.Task.Wait(TimeSpan.FromSeconds(5)); return 42; },
                v =>
                {
                    applied.Add(v);
                    applyThreads.Add(Thread.CurrentThread.ManagedThreadId);
                });

            Assert.Empty(applied);
            gate.SetResult(true);
            SpinUntilTrue(() => PendingQueueCount(scope) == 1, "result was not enqueued");
            Assert.Empty(applied);

            scope.Drain();

            Assert.Equal(new[] { 42 }, applied);
            Assert.Equal(Thread.CurrentThread.ManagedThreadId, applyThreads[0]);
        }

        [Fact]
        public void Run_Fault_ShouldQueueErrorFifoAfterEarlierSuccess()
        {
            var scope = new StageBackgroundWorkScope();
            var events = new List<string>();
            var gate1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            scope.Run<int>(
                _ => { gate1.Task.Wait(TimeSpan.FromSeconds(5)); return 1; },
                v => events.Add($"ok:{v}"));
            scope.Run<int>(
                _ => { gate2.Task.Wait(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("boom"); },
                v => events.Add($"ok:{v}"),
                ex => events.Add($"err:{ex.Message}"));

            gate1.SetResult(true);
            SpinUntilTrue(() => PendingQueueCount(scope) == 1, "success result was not enqueued");
            gate2.SetResult(true);
            SpinUntilTrue(() => PendingQueueCount(scope) == 2, "error result was not enqueued");

            scope.Drain();

            Assert.Equal(new[] { "ok:1", "err:boom" }, events);
        }

        [Fact]
        public void Run_Canceled_ShouldObserveCancellationWithoutQueueingErrorOrSuccess()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<int>();
            Exception? error = null;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var leftWorker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            scope.Run<int>(
                _ =>
                {
                    started.SetResult(true);
                    try
                    {
                        SpinWait.SpinUntil(() => scope.Token.IsCancellationRequested, TimeSpan.FromSeconds(5));
                        throw new OperationCanceledException(scope.Token);
                    }
                    finally
                    {
                        leftWorker.SetResult(true);
                    }
                },
                applied.Add,
                ex => error = ex);

            SpinUntilTrue(() => started.Task.IsCompleted, "worker did not start");
            scope.Retire();
            SpinUntilTrue(() => leftWorker.Task.IsCompleted, "worker did not finish");

            scope.Drain();

            Assert.Null(error);
            Assert.Empty(applied);
            Assert.Equal(0, PendingQueueCount(scope));
        }

        [Fact]
        public void Run_QueuedResultBeforeRetire_ShouldDropSynchronouslyWithoutSubsequentDrain()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<string>();
            var dropped = new List<string>();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            scope.Run<string>(
                _ => { gate.Task.Wait(TimeSpan.FromSeconds(5)); return "payload"; },
                applied.Add,
                onDropped: dropped.Add);
            gate.SetResult(true);
            SpinUntilTrue(() => PendingQueueCount(scope) == 1, "result was not enqueued");

            scope.Retire();

            Assert.Equal(new[] { "payload" }, dropped);
            scope.Drain();
            Assert.Empty(applied);
        }

        [Fact]
        public void Run_ResultCompletingAfterRetire_ShouldDropExactlyOnce()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<string>();
            var dropped = new List<string>();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            scope.Run<string>(
                _ => { gate.Task.Wait(TimeSpan.FromSeconds(5)); return "late"; },
                applied.Add,
                onDropped: dropped.Add);

            scope.Retire();
            gate.SetResult(true);
            SpinUntilTrue(() => dropped.Count == 1, "late result was not dropped");

            Assert.Equal(new[] { "late" }, dropped);
            Assert.Empty(applied);
            scope.Drain();
            Assert.Empty(applied);
            Assert.Equal(1, dropped.Count);
        }

        [Fact]
        public void Run_AfterRetire_ShouldNotStartWork()
        {
            var scope = new StageBackgroundWorkScope();
            var ran = false;
            scope.Retire();

            scope.Run<int>(
                _ => { ran = true; return 1; },
                _ => { });

            Assert.False(ran);
            Assert.Equal(0, PendingQueueCount(scope));
        }

        [Fact]
        public void ConcurrentEnqueueAndRetire_ShouldDropEachResultExactlyOnce()
        {
            var scope = new StageBackgroundWorkScope();
            const int count = 512;
            var applied = new int[count];
            var dropped = new int[count];
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            for (var i = 0; i < count; i++)
            {
                var index = i;
                scope.Run<int>(
                    _ => { release.Task.Wait(TimeSpan.FromSeconds(5)); return index; },
                    v => Interlocked.Increment(ref applied[v]),
                    onDropped: v => Interlocked.Increment(ref dropped[v]));
            }

            var retire = Task.Run(scope.Retire);
            release.SetResult(true);
            Assert.True(retire.Wait(TimeSpan.FromSeconds(5)));
            SpinUntilTrue(
                () => System.Linq.Enumerable.Sum(applied) + System.Linq.Enumerable.Sum(dropped) == count,
                "workers did not finish");

            scope.Drain();

            for (var i = 0; i < count; i++)
            {
                Assert.Equal(0, applied[i]);
                Assert.Equal(1, dropped[i]);
            }
        }

        #endregion

        #region Mid-drain retirement

        [Fact]
        public void Drain_WhenCallbackRetiresScope_ShouldDropRemainingWorkAndStop()
        {
            var scope = new StageBackgroundWorkScope();
            var applied = new List<string>();
            var dropped = new List<string>();

            scope.Post(() =>
            {
                applied.Add("first");
                scope.Retire();
            });
            scope.Run<string>(
                _ => "owned",
                _ => applied.Add("owned-applied"),
                onDropped: dropped.Add);
            scope.Post(() => applied.Add("third"));

            scope.Drain();
            SpinUntilTrue(() => dropped.Count == 1, "owned result was not dropped");

            Assert.Equal(new[] { "first" }, applied);
            Assert.Equal(new[] { "owned" }, dropped);
            Assert.Equal(0, PendingQueueCount(scope));
        }

        #endregion
    }
}
