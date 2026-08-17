using DTXMania.Automation.Process;
using DTXMania.VideoRecorder.Sandbox;

namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// Recorder-local launch policy. Automation deliberately does not know how a
/// disposable recorder sandbox maps to the repository's Windows game project.
/// </summary>
/// <remarks>
/// <b>Repository-root contract:</b> <c>dtx-video</c> must be invoked with the
/// repository checkout as its working directory. <see cref="CreateOptions"/>
/// treats <see cref="Directory.GetCurrentDirectory"/> as the declared root
/// source and walks upward from it to locate the solution, so the Windows game
/// project (<c>DTXMania.Game/DTXMania.Game.Windows.csproj</c>) is always
/// resolved relative to that single declared root.
/// </remarks>
internal sealed record ResolvedRecorderTarget(
    string RepositoryRoot,
    string WorkingDirectory,
    GameLaunchTarget Target);

internal static class RecorderGameLaunchPolicy
{
    /// <summary>
    /// Sandbox-free resolution of the current-platform game launch target
    /// rooted at the repository containing <paramref name="startDirectory"/>.
    /// </summary>
    internal static ResolvedRecorderTarget ResolveTarget(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        var repoRoot = ResolveRepoRoot(startDirectory);
        var projectPath = Path.GetFullPath(Path.Combine(repoRoot, GameProjectPaths.Current));
        return new ResolvedRecorderTarget(
            repoRoot,
            repoRoot,
            GameLaunchTarget.Project(projectPath));
    }

    /// <summary>
    /// Builds the launch options for the resolved target, pinned to the
    /// prebuilt Debug output via no-build.
    /// </summary>
    internal static GameProcessStartOptions CreateOptions(
        RecordingSandbox sandbox,
        ResolvedRecorderTarget target)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(target);
        return new GameProcessStartOptions(
            target.WorkingDirectory,
            target.Target,
            sandbox.AppDataRoot,
            Guid.NewGuid().ToString("N"),
            ProjectRunArguments: new[] { "--no-build", "--configuration", "Debug" });
    }

    /// <summary>
    /// Builds the launch options for the Windows game project rooted at the
    /// repository root declared via the current working directory.
    /// </summary>
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
