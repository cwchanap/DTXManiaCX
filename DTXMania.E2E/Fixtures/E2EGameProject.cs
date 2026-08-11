using DTXMania.Automation.Process;

namespace DTXMania.E2E.Fixtures;

/// <summary>
/// Resolves the game project target for out-of-process E2E launches.
/// The environment override is intentionally owned here; platform defaults are provided by
/// <see cref="GameProjectPaths.Current"/> in the reusable Automation project.
/// </summary>
public static class E2EGameProject
{
    public const string GameProjectEnvironmentVariable = "DTXMANIA_E2E_GAME_PROJECT";

    public static GameLaunchTarget ResolveLaunchTarget()
    {
        var overridePath = Environment.GetEnvironmentVariable(GameProjectEnvironmentVariable);
        return GameLaunchTarget.Project(overridePath);
    }
}
