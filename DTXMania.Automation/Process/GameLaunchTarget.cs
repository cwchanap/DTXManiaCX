namespace DTXMania.Automation.Process;

public enum GameLaunchKind
{
    Project,
    Executable
}

public sealed record GameLaunchTarget(GameLaunchKind Kind, string Path)
{
    public static GameLaunchTarget Project(string? projectPathOverride = null)
    {
        return new(GameLaunchKind.Project, string.IsNullOrWhiteSpace(projectPathOverride)
            ? GameProjectPaths.Current
            : projectPathOverride);
    }

    public static GameLaunchTarget Executable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        return new(GameLaunchKind.Executable, executablePath);
    }
}
