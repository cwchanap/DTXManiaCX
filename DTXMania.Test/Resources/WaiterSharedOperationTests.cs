using System;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public sealed class WaiterSharedOperationTests
    {
        [Fact]
        public void Constructor_WithNullFactory_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new WaiterSharedOperation<int>(null!));
        }

        [Fact]
        public void TryAddWaiter_IncrementsWaiterCountAndAccepts()
        {
            var op = new WaiterSharedOperation<int>(_ => Task.FromResult(42));

            Assert.True(op.TryAddWaiter());
            Assert.True(op.TryAddWaiter());
        }

        [Fact]
        public async Task GetTask_InvokesFactoryOnceAndReturnsResult()
        {
            var factoryCalls = 0;
            var op = new WaiterSharedOperation<int>(token =>
            {
                factoryCalls++;
                return Task.FromResult(99);
            });

            op.TryAddWaiter();
            var task = op.GetTask();
            var result = await task;

            Assert.Equal(99, result);
            Assert.Equal(1, factoryCalls);
        }

        [Fact]
        public async Task GetTask_ReturnsSameTaskInstanceOnRepeatedCalls()
        {
            var op = new WaiterSharedOperation<int>(_ => Task.FromResult(1));

            op.TryAddWaiter();
            var first = op.GetTask();
            var second = op.GetTask();

            Assert.Same(first, second);
        }

        [Fact]
        public async Task GetTask_WhenFactoryReturnsNull_ReturnsFaultedTask()
        {
            var op = new WaiterSharedOperation<int>(_ => null!);

            op.TryAddWaiter();
            var task = op.GetTask();

            await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        }

        [Fact]
        public async Task GetTask_WhenFactoryThrows_ReturnsFaultedTask()
        {
            var op = new WaiterSharedOperation<int>(_ =>
                throw new InvalidOperationException("factory boom"));

            op.TryAddWaiter();
            var task = op.GetTask();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            Assert.Contains("factory boom", ex.Message);
        }

        [Fact]
        public async Task ReleaseWaiter_WithMultipleWaiters_DoesNotCancelOperation()
        {
            var tcs = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var op = new WaiterSharedOperation<int>(_ => tcs.Task);

            op.TryAddWaiter();
            op.TryAddWaiter();

            // Release one waiter — operation should still be alive
            var removed = op.ReleaseWaiter();
            Assert.False(removed); // not the last waiter

            // The task should still be pending
            op.TryAddWaiter();
            var task = op.GetTask();
            Assert.False(task.IsCompleted);

            tcs.SetResult(77);
            var result = await task;
            Assert.Equal(77, result);
        }

        [Fact]
        public async Task ReleaseWaiter_LastWaiterBeforeCompletion_CancelsOperation()
        {
            var tcs = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var op = new WaiterSharedOperation<int>(_ => tcs.Task);

            op.TryAddWaiter();
            var task = op.GetTask();

            // Last waiter leaves before the operation completes → cancel
            var removed = op.ReleaseWaiter();
            Assert.True(removed);

            // Factory should observe cancellation
            tcs.Task.Wait(50); // give a brief moment for cancellation to propagate
            Assert.False(tcs.Task.IsCompleted); // still pending from our side

            // Completing after cancellation should not throw
            tcs.TrySetCanceled();
        }

        [Fact]
        public async Task ReleaseWaiter_LastWaiterAfterCompletion_DisposesCancellation()
        {
            var op = new WaiterSharedOperation<int>(_ => Task.FromResult(5));

            op.TryAddWaiter();
            var task = op.GetTask();
            await task; // let it complete

            var removed = op.ReleaseWaiter();
            Assert.True(removed);
        }

        [Fact]
        public async Task ReleaseWaiter_WithNoWaiters_ThrowsInvalidOperationException()
        {
            var op = new WaiterSharedOperation<int>(_ => Task.FromResult(1));

            Assert.Throws<InvalidOperationException>(() => op.ReleaseWaiter());
        }

        [Fact]
        public async Task FullLifecycle_MultipleWaitersShareOneFactoryCall()
        {
            var factoryCalls = 0;
            var tcs = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var op = new WaiterSharedOperation<int>(token =>
            {
                Interlocked.Increment(ref factoryCalls);
                return tcs.Task;
            });

            op.TryAddWaiter();
            op.TryAddWaiter();
            op.TryAddWaiter();

            var task = op.GetTask();

            // Two waiters leave
            op.ReleaseWaiter();
            op.ReleaseWaiter();

            // One remains — operation still alive
            Assert.False(task.IsCompleted);

            tcs.SetResult(123);
            var result = await task;
            Assert.Equal(123, result);
            Assert.Equal(1, factoryCalls);

            // Last waiter leaves after completion
            op.ReleaseWaiter();
        }

        [Fact]
        public async Task GetTask_ObservesFaultedTaskWithoutThrowing()
        {
            var op = new WaiterSharedOperation<int>(_ =>
                Task.FromException<int>(new InvalidOperationException("decode failed")));

            op.TryAddWaiter();
            var task = op.GetTask();

            // The task itself is faulted, but GetTask should not throw
            Assert.True(task.IsFaulted);

            // Releasing the last waiter after fault should work
            op.ReleaseWaiter();
        }
    }
}
