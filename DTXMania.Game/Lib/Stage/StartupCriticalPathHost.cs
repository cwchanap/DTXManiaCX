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
}
