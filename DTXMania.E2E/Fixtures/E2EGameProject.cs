namespace DTXMania.E2E.Fixtures;

/// <summary>
/// Resolves the platform-selected game project path for out-of-process E2E launches.
/// Honors the <c>DTXMANIA_E2E_GAME_PROJECT</c> override; otherwise selects the Windows
/// or Mac game csproj based on the current operating system so non-Windows runs never
/// attempt to launch the Windows game project.
/// </summary>
public static class E2EGameProject
{
    public const string GameProjectEnvironmentVariable = "DTXMANIA_E2E_GAME_PROJECT";
    public const string WindowsProjectPath = "DTXMania.Game/DTXMania.Game.Windows.csproj";
    public const string MacProjectPath = "DTXMania.Game/DTXMania.Game.Mac.csproj";

    public static string ResolveProjectPath()
    {
        return Environment.GetEnvironmentVariable(GameProjectEnvironmentVariable)
            ?? (OperatingSystem.IsWindows()
                ? WindowsProjectPath
                : MacProjectPath);
    }
}
