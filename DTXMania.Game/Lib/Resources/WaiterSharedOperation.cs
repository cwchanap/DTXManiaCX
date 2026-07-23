#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Shares one cancellable operation across independent callers.
    /// Each caller cancels only its own wait; the operation is canceled when
    /// the last waiter leaves before completion.
    /// </summary>
    internal sealed class WaiterSharedOperation<TResult>
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _operationCancellation = new();
        private readonly Func<CancellationToken, Task<TResult>> _factory;
        private Task<TResult>? _task;
        private int _waiterCount;
        private bool _acceptingWaiters = true;
        private bool _completed;
        private bool _cancellationDisposed;

        public WaiterSharedOperation(
            Func<CancellationToken, Task<TResult>> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public bool TryAddWaiter()
        {
            lock (_sync)
            {
                if (!_acceptingWaiters)
                    return false;

                _waiterCount++;
                return true;
            }
        }

        public Task<TResult> GetTask()
        {
            lock (_sync)
            {
                if (_task != null)
                    return _task;

                try
                {
                    _task = _factory(_operationCancellation.Token)
                        ?? Task.FromException<TResult>(
                            new InvalidOperationException(
                                "The shared operation factory returned no task."));
                }
                catch (Exception ex)
                {
                    _task = Task.FromException<TResult>(ex);
                }

                _ = _task.ContinueWith(
                    static (completedTask, state) =>
                        ((WaiterSharedOperation<TResult>)state!)
                            .OnCompleted(completedTask),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return _task;
            }
        }

        /// <summary>
        /// Releases one waiter. Returns true when the dictionary owner must
        /// remove this operation so a later caller cannot join an abandoned task.
        /// </summary>
        public bool ReleaseWaiter()
        {
            var cancelOperation = false;
            var disposeCancellation = false;

            lock (_sync)
            {
                if (_waiterCount <= 0)
                    throw new InvalidOperationException("No shared-operation waiter to release.");

                _waiterCount--;
                if (_waiterCount != 0)
                    return false;

                _acceptingWaiters = false;
                cancelOperation = !_completed;
                if (_completed && !_cancellationDisposed)
                {
                    _cancellationDisposed = true;
                    disposeCancellation = true;
                }
            }

            if (cancelOperation)
            {
                try
                {
                    _operationCancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Completion won the race and already disposed the source.
                }
            }

            if (disposeCancellation)
                _operationCancellation.Dispose();

            return true;
        }

        private void OnCompleted(Task<TResult> completedTask)
        {
            // Observe failures even when every caller canceled its own wait.
            if (completedTask.IsFaulted)
                _ = completedTask.Exception;

            var disposeCancellation = false;
            lock (_sync)
            {
                _completed = true;
                if (!_acceptingWaiters && !_cancellationDisposed)
                {
                    _cancellationDisposed = true;
                    disposeCancellation = true;
                }
            }

            if (disposeCancellation)
                _operationCancellation.Dispose();
        }
    }
}