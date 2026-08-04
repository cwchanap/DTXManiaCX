#nullable enable

using System;
using System.IO;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal interface IGameApplication : IDisposable
{
    void Run();
}

internal interface ICrashRuntimeLifetime : IDisposable
{
    void CaptureFatal(Exception exception);
    void RecordSecondaryFailure(string failureCode, Exception exception);
}

internal static class GameEntryPoint
{
    private const string CaptureFailureCode = "crash_capture_failed";
    private const string GameDisposeFailureCode = "game_dispose_failed";
    private const string RuntimeDisposeFailureCode = "runtime_dispose_failed";

    internal static int Run(
        Func<IGameApplication> createGame,
        ICrashRuntimeLifetime crashRuntime,
        TextWriter errorWriter)
    {
        ArgumentNullException.ThrowIfNull(createGame);
        ArgumentNullException.ThrowIfNull(crashRuntime);
        ArgumentNullException.ThrowIfNull(errorWriter);

        IGameApplication? game = null;
        var exitCode = 0;

        try
        {
            game = createGame();
            game.Run();
        }
        catch (Exception exception)
        {
            exitCode = 1;
            CaptureFatal(crashRuntime, exception, errorWriter);
        }
        finally
        {
            if (game is not null)
            {
                try
                {
#if DEBUG
                    DebugCrashInjection.WriteTeardownCheckpoint("game.dispose.before");
#endif
                    game.Dispose();
#if DEBUG
                    DebugCrashInjection.WriteTeardownCheckpoint("game.dispose.after");
#endif
                }
                catch (Exception exception)
                {
                    RecordSecondaryFailure(
                        crashRuntime,
                        GameDisposeFailureCode,
                        exception,
                        errorWriter);
                }
            }

            try
            {
                crashRuntime.Dispose();
            }
            catch (Exception)
            {
                WriteSafeFailure(errorWriter, RuntimeDisposeFailureCode);
            }
        }

        return exitCode;
    }

    private static void CaptureFatal(
        ICrashRuntimeLifetime crashRuntime,
        Exception originalException,
        TextWriter errorWriter)
    {
        try
        {
            crashRuntime.CaptureFatal(originalException);
        }
        catch (Exception exception)
        {
            RecordSecondaryFailure(
                crashRuntime,
                CaptureFailureCode,
                exception,
                errorWriter);
        }
    }

    private static void RecordSecondaryFailure(
        ICrashRuntimeLifetime crashRuntime,
        string failureCode,
        Exception exception,
        TextWriter errorWriter)
    {
        try
        {
            crashRuntime.RecordSecondaryFailure(failureCode, exception);
        }
        catch (Exception)
        {
            WriteSafeFailure(errorWriter, failureCode);
        }
    }

    private static void WriteSafeFailure(TextWriter errorWriter, string failureCode)
    {
        try
        {
            errorWriter.WriteLine("crash_reporting_secondary_failure code=" + failureCode);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }
}
