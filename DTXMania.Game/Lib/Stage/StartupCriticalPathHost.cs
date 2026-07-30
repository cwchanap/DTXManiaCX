#nullable enable

namespace DTXMania.Game.Lib.Stage;

internal interface IStartupCriticalPathHost
{
    StartupCriticalPathTrace? StartupCriticalPathTrace { get; }
}

internal static class StartupCriticalPathHost
{
    internal static StartupCriticalPathTrace? Resolve(IStageGame game) =>
        (game as IStartupCriticalPathHost)?.StartupCriticalPathTrace;

    /// <summary>
    /// Exception-safe resolve: returns null if the game does not implement
    /// <see cref="IStartupCriticalPathHost"/> or if resolution throws.
    /// </summary>
    internal static StartupCriticalPathTrace? TryResolve(IStageGame? game)
    {
        try
        {
            return Resolve(game!);
        }
        catch
        {
            return null;
        }
    }
}
