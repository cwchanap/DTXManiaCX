#nullable enable
using System;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Synchronous <see cref="IProgress{T}"/> that invokes the callback inline on the
    /// reporting thread. Unlike <see cref="Progress{T}"/> it never captures a
    /// SynchronizationContext, so stages hand each report straight to a
    /// <see cref="StageBackgroundWorkScope"/> Latest reporter (at most one status
    /// update queued per frame) or apply it in place, and stale queued progress can
    /// never overwrite a terminal status.
    /// </summary>
    internal sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        public void Report(T value) => _report(value);
    }
}
