#if DEBUG
using System;
using System.Threading;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal enum DebugCrashInjectionPoint
{
    Update,
    Draw
}

internal static class DebugCrashInjection
{
    private static readonly DebugCrashInjectionPoint? RequestedInjectionPoint =
        Environment.GetEnvironmentVariable("DTXMANIA_E2E_CRASH_INJECTION") switch
        {
            "update" => DebugCrashInjectionPoint.Update,
            "draw" => DebugCrashInjectionPoint.Draw,
            _ => null
        };

    private static int _thrown;

    internal static void ThrowIfRequested(DebugCrashInjectionPoint callback)
    {
        if (RequestedInjectionPoint != callback
            || Interlocked.Exchange(ref _thrown, 1) != 0)
        {
            return;
        }

        throw new InvalidOperationException("DTXMANIA_E2E_CONTROLLED_CRASH");
    }
}
#endif
