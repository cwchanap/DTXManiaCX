using DTXMania.Automation.Process;
using DTXMania.VideoRecorder.Sandbox;

namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// Recorder-local launch policy. Automation deliberately does not know how a
/// disposable recorder sandbox maps to the repository's game projects.
/// </summary>
/// <remarks>
/// <see cref="ResolveTarget"/> walks upward from the declared start directory
/// to locate the solution, so <c>dtx-video</c> may be invoked from the
/// repository root or any nested directory within it. The current-platform
/// game project is always resolved relative to the located solution root.
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
