#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Stage-scoped background work queue (HPA-799). Created fresh by
    /// <see cref="BaseStage"/> on every successful activation, drained at the start
    /// of <see cref="BaseStage.Update"/> before stage logic and input, and retired
    /// synchronously before <see cref="BaseStage.OnDeactivate"/>.
    ///
    /// Public surface is exactly: <see cref="Token"/>, <see cref="Post"/>,
    /// <see cref="Latest{T}"/>, <see cref="Run{T}"/>, <see cref="Drain"/>,
    /// <see cref="Retire"/>.
    ///
    /// - <see cref="Post"/> queues FIFO terminal callbacks; never coalesced.
    /// - <see cref="Latest{T}"/> coalesces per reporter: the newest report replaces
    ///   the pending one (superseded values drop immediately), so at most one apply
    ///   is queued per reporter.
    /// - <see cref="Run{T}"/> executes work on the thread pool; success and fault
    ///   results are queued FIFO for the update thread; cancellation is observed
    ///   silently, so no faulted task is left unobserved.
    /// - <see cref="Retire"/> marks the scope retired, synchronously detaches every
    ///   queued item and invokes its drop callback outside the lock (one failing
    ///   cleanup never skips the rest). Results arriving after retirement drop
    ///   directly. Owned results drop exactly once; user callbacks never run under
    ///   the lock.
    /// </summary>
    internal sealed class StageBackgroundWorkScope
    {
        /// <summary>Maximum callbacks applied per <see cref="Drain"/> call (one frame).</summary>
        public const int MaxActionsPerDrain = 64;

        private readonly object _gate = new();
        private readonly List<WorkItem> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _retired;

        /// <summary>Canceled by <see cref="Retire"/>; observed by worker code.</summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>
        /// Queues a FIFO terminal callback applied on the update thread by
        /// <see cref="Drain"/>. No-op after retirement.
        /// </summary>
        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            lock (_gate)
            {
                if (_retired)
                {
                    return;
                }

                _queue.Add(new WorkItem(action, null));
            }
        }

        /// <summary>
        /// Creates a reporter whose newest report is the only queued apply; calling
        /// the reporter again before a drain replaces the pending value (dropping
        /// the superseded one via <paramref name="onDropped"/>). After retirement,
        /// reports drop directly.
        /// </summary>
        public Action<T> Latest<T>(Action<T> apply, Action<T>? onDropped = null)
        {
            ArgumentNullException.ThrowIfNull(apply);

            var reporter = new LatestReporter<T>(this, apply, onDropped);
            return reporter.Report;
        }

        /// <summary>
        /// Runs <paramref name="work"/> on the thread pool with <see cref="Token"/>.
        /// A successful result queues <paramref name="onSuccess"/>; a fault queues
        /// <paramref name="onError"/>; cancellation queues nothing. Both are FIFO
        /// terminal callbacks applied by <see cref="Drain"/> on the update thread.
        /// If the result arrives after <see cref="Retire"/> (or the scope is already
        /// retired), <paramref name="onDropped"/> disposes the owned result exactly
        /// once instead. Exceptions thrown by the update-thread callbacks are the
        /// owning stage's responsibility (local try/catch/finally).
        /// </summary>
        public void Run<T>(
            Func<CancellationToken, T> work,
            Action<T> onSuccess,
            Action<Exception>? onError = null,
            Action<T>? onDropped = null)
        {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(onSuccess);

            lock (_gate)
            {
                if (_retired)
                {
                    return;
                }
            }

            _ = Task.Run(() =>
            {
                T result;
                try
                {
                    result = work(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected outcome (e.g. retirement) — observed, nothing queued.
                    return;
                }
                catch (Exception ex)
                {
                    EnqueueError(ex, onError);
                    return;
                }

                EnqueueSuccess(result, onSuccess, onDropped);
            });
        }

        /// <summary>
        /// Applies queued items on the calling (update) thread, at most
        /// <see cref="MaxActionsPerDrain"/> callbacks per call. Stops immediately if
        /// a callback retires the scope (e.g. a synchronous InstantTransition
        /// deactivated the stage); the remaining items were already dropped by
        /// <see cref="Retire"/>.
        /// </summary>
        public void Drain()
        {
            for (var applied = 0; applied < MaxActionsPerDrain; applied++)
            {
                WorkItem item;
                lock (_gate)
                {
                    if (_retired || _queue.Count == 0)
                    {
                        return;
                    }

                    item = _queue[0];
                    _queue.RemoveAt(0);
                    item.Detached = true;
                }

                item.Apply?.Invoke();
            }
        }

        /// <summary>
        /// Retires the scope synchronously and idempotently: cancels the worker
        /// token without awaiting, detaches every queued item under the lock and
        /// invokes their drop callbacks outside it (one failing cleanup never skips
        /// the rest), so owned results are released even if the stage never updates
        /// again. Queued items are never applied afterwards.
        /// </summary>
        public void Retire()
        {
            WorkItem[] droppedItems;
            lock (_gate)
            {
                if (_retired)
                {
                    return;
                }

                _retired = true;
                droppedItems = _queue.ToArray();
                _queue.Clear();
                foreach (var item in droppedItems)
                {
                    item.Detached = true;
                }
            }

            // A plain scope CTS carries no timers or linked tokens, so Cancel is
            // enough and the source stays undisposed by design.
            try
            {
                _cts.Cancel();
            }
            catch (AggregateException)
            {
                // Registration callbacks were all invoked; never fail teardown here.
            }

            foreach (var item in droppedItems)
            {
                if (item.Drop == null)
                {
                    continue;
                }

                try
                {
                    item.Drop();
                }
                catch
                {
                    // One failed cleanup must not skip the remaining owned results.
                }
            }
        }

        private void EnqueueSuccess<T>(T result, Action<T> onSuccess, Action<T>? onDropped)
        {
            Action? dropDirect = null;
            lock (_gate)
            {
                if (_retired)
                {
                    if (onDropped != null)
                    {
                        dropDirect = () => onDropped(result);
                    }
                }
                else
                {
                    _queue.Add(new WorkItem(
                        () => onSuccess(result),
                        onDropped == null ? null : () => onDropped(result)));
                }
            }

            try
            {
                dropDirect?.Invoke();
            }
            catch
            {
                // A failing direct drop (result arriving after retirement) runs on
                // the worker thread outside the worker try/catch; isolate it like
                // Retire's per-item drops so the discarded Task.Run task never
                // faults unobserved.
            }
        }

        private void EnqueueError(Exception ex, Action<Exception>? onError)
        {
            if (onError == null)
            {
                return;
            }

            lock (_gate)
            {
                if (!_retired)
                {
                    _queue.Add(new WorkItem(() => onError(ex), null));
                }
            }
        }

        /// <summary>One queued apply with an optional owned-result cleanup.</summary>
        private class WorkItem
        {
            public WorkItem(Action apply, Action? drop)
            {
                Apply = apply;
                Drop = drop;
            }

            protected WorkItem()
            {
            }

            public Action? Apply { get; protected set; }

            public Action? Drop { get; protected set; }

            /// <summary>
            /// True once the item can no longer be applied by a future Drain
            /// (dequeued by Drain or detached by Retire); set under
            /// <see cref="_gate"/> so Latest reporters can re-queue safely.
            /// </summary>
            public bool Detached { get; set; }
        }

        /// <summary>Queued Latest apply carrying the current value in the item itself.</summary>
        private sealed class LatestItem<T> : WorkItem
        {
            public LatestItem(T value, Action<T> apply, Action<T>? onDropped)
            {
                Value = value;
                Apply = () => apply(Value);
                if (onDropped != null)
                {
                    Drop = () => onDropped(Value);
                }
            }

            public T Value;
        }

        /// <summary>Per-reporter coalescing state; <see cref="_pending"/> is guarded by the scope gate.</summary>
        private sealed class LatestReporter<T>
        {
            private readonly StageBackgroundWorkScope _scope;
            private readonly Action<T> _apply;
            private readonly Action<T>? _onDropped;
            private LatestItem<T>? _pending;

            public LatestReporter(StageBackgroundWorkScope scope, Action<T> apply, Action<T>? onDropped)
            {
                _scope = scope;
                _apply = apply;
                _onDropped = onDropped;
            }

            public void Report(T value)
            {
                Action? dropSuperseded = null;
                Action? dropDirect = null;
                lock (_scope._gate)
                {
                    if (_scope._retired)
                    {
                        if (_onDropped != null)
                        {
                            dropDirect = () => _onDropped(value);
                        }
                    }
                    else if (_pending != null && !_pending.Detached)
                    {
                        var superseded = _pending.Value;
                        _pending.Value = value;
                        if (_onDropped != null)
                        {
                            dropSuperseded = () => _onDropped(superseded);
                        }
                    }
                    else
                    {
                        _pending = new LatestItem<T>(value, _apply, _onDropped);
                        _scope._queue.Add(_pending);
                    }
                }

                dropSuperseded?.Invoke();
                dropDirect?.Invoke();
            }
        }
    }
}
