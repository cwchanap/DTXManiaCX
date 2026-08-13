using DTXMania.Automation.Process;
using DTXMania.VideoRecorder.Sandbox;

namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// Recorder-local launch policy. Automation deliberately does not know how a
/// disposable recorder sandbox maps to the repository's Windows game project.
/// </summary>
internal static class RecorderGameLaunchPolicy
{
    public static GameProcessStartOptions CreateOptions(RecordingSandbox sandbox)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        var repoRoot = ResolveRepoRoot(Directory.GetCurrentDirectory());
        var projectPath = Path.Combine(
            repoRoot,
            "DTXMania.Game",
            "DTXMania.Game.Windows.csproj");
        return new GameProcessStartOptions(
            repoRoot,
            GameLaunchTarget.Project(projectPath),
            sandbox.AppDataRoot,
            Guid.NewGuid().ToString("N"));
    }

    internal static string ResolveRepoRoot(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        var candidate = Path.GetFullPath(startDirectory);
        while (true)
        {
            if (File.Exists(Path.Combine(candidate, "DTXMania.sln"))
                || File.Exists(Path.Combine(candidate, "DTXMania.slnx")))
            {
                return candidate;
            }

            var parent = Directory.GetParent(candidate);
            if (parent is null)
            {
                throw new InvalidOperationException(
                    "Could not locate the DTXMania repository root from the current directory.");
            }

            candidate = parent.FullName;
        }
    }
}
