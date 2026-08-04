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
    internal const string ControlledCrashMessage = "DTXMANIA_E2E_CONTROLLED_CRASH";
    private const string TeardownCheckpointPrefix = "DTXMANIA_E2E_CRASH_TEARDOWN";

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

        throw new InvalidOperationException(ControlledCrashMessage);
    }

    internal static void WriteTeardownCheckpoint(string checkpoint)
    {
        if (RequestedInjectionPoint is null || Volatile.Read(ref _thrown) == 0)
        {
            return;
        }

        try
        {
            Console.Error.WriteLine(TeardownCheckpointPrefix + " " + checkpoint);
            Console.Error.Flush();
        }
        catch (Exception)
        {
        }
    }
}
#endif
