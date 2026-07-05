using System;
using System.Diagnostics;

namespace DTXMania.Test.Helpers;

/// <summary>
/// Converts <see cref="Debug.Assert(bool)"/> / <see cref="Debug.Fail(string)"/> failures into
/// thrown exceptions so assertions surface as test failures instead of modal dialogs (the
/// default <see cref="DefaultTraceListener"/> shows a dialog that hangs headless test runs).
/// Install via <see cref="Install"/> in a <c>using</c>-scope; the original listener set is
/// restored on <see cref="IDisposable.Dispose"/>.
/// </summary>
internal sealed class ThrowingTraceListener : TraceListener
{
    public override void Fail(string? message) => throw new DebugAssertFailedException(message);
    public override void Fail(string? message, string? detailMessage)
        => throw new DebugAssertFailedException(
            detailMessage is null ? message : $"{message}: {detailMessage}");
    public override void Write(string? message) { }
    public override void WriteLine(string? message) { }

    /// <summary>
    /// Replaces all trace listeners with a <see cref="ThrowingTraceListener"/> for the
    /// duration of the returned scope. Restores the previous listener set on dispose.
    /// </summary>
    public static IDisposable Install()
    {
        var original = new TraceListener[Trace.Listeners.Count];
        Trace.Listeners.CopyTo(original, 0);
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new ThrowingTraceListener());
        return new Scope(original);
    }

    private sealed class Scope : IDisposable
    {
        private readonly TraceListener[] _original;

        public Scope(TraceListener[] original) => _original = original;

        public void Dispose()
        {
            Trace.Listeners.Clear();
            foreach (var listener in _original)
                Trace.Listeners.Add(listener);
        }
    }
}

/// <summary>
/// Thrown by <see cref="ThrowingTraceListener"/> when a <see cref="Debug.Assert(bool)"/> or
/// <see cref="Debug.Fail(string)"/> fires while the throwing listener is installed.
/// </summary>
internal sealed class DebugAssertFailedException : Exception
{
    public DebugAssertFailedException(string? message) : base(message) { }
}
