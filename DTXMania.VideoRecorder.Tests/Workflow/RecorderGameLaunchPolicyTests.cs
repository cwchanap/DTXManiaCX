using DTXMania.Automation.Process;
using DTXMania.VideoRecorder.Sandbox;
using DTXMania.VideoRecorder.Workflow;
using Microsoft.Data.Sqlite;

namespace DTXMania.VideoRecorder.Tests.Workflow;

public sealed class RecorderGameLaunchPolicyTests
{
    [Fact]
    public void ResolveTarget_FromRepositoryRoot()
    {
        var repo = CreateFakeRepo();
        try
        {
            var resolved = RecorderGameLaunchPolicy.ResolveTarget(repo);

            Assert.Equal(Path.GetFullPath(repo), resolved.RepositoryRoot);
            Assert.Equal(resolved.RepositoryRoot, resolved.WorkingDirectory);
            Assert.Equal(GameLaunchKind.Project, resolved.Target.Kind);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(repo, GameProjectPaths.Current)),
                resolved.Target.Path);
            Assert.True(File.Exists(resolved.Target.Path));
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void ResolveTarget_FromNestedDirectory()
    {
        var repo = CreateFakeRepo();
        var nested = Path.Combine(repo, "DTXMania.Game", "Properties");
        Directory.CreateDirectory(nested);
        try
        {
            var resolved = RecorderGameLaunchPolicy.ResolveTarget(nested);

            Assert.Equal(Path.GetFullPath(repo), resolved.RepositoryRoot);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(repo, GameProjectPaths.Current)),
                resolved.Target.Path);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void CreateOptions_PreservesResolvedTarget()
    {
        var repo = CreateFakeRepo();
        var sourceRoot = CreateSandboxSourceRoot();
        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
            var resolved = RecorderGameLaunchPolicy.ResolveTarget(repo);

            var options = RecorderGameLaunchPolicy.CreateOptions(sandbox, resolved);

            Assert.Equal(resolved.WorkingDirectory, options.WorkingDirectory);
            Assert.Equal(resolved.Target, options.Target);
            Assert.Equal(sandbox.AppDataRoot, options.AppDataRoot);
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
            Delete(sourceRoot);
            Delete(repo);
        }
    }

    [Fact]
    public void CreateOptions_AddsFreshLaunchToken()
    {
        var repo = CreateFakeRepo();
        var sourceRoot = CreateSandboxSourceRoot();
        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
            var resolved = RecorderGameLaunchPolicy.ResolveTarget(repo);

            var first = RecorderGameLaunchPolicy.CreateOptions(sandbox, resolved);
            var second = RecorderGameLaunchPolicy.CreateOptions(sandbox, resolved);

            Assert.False(string.IsNullOrWhiteSpace(first.LaunchToken));
            Assert.False(string.IsNullOrWhiteSpace(second.LaunchToken));
            Assert.NotEqual(first.LaunchToken, second.LaunchToken);
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
            Delete(sourceRoot);
            Delete(repo);
        }
    }

    [Fact]
    public void CreateOptions_UsesNoBuildDebugArguments()
    {
        var repo = CreateFakeRepo();
        var sourceRoot = CreateSandboxSourceRoot();
        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
            var resolved = RecorderGameLaunchPolicy.ResolveTarget(repo);

            var options = RecorderGameLaunchPolicy.CreateOptions(sandbox, resolved);

            Assert.Equal(new[] { "--no-build", "--configuration", "Debug" }, options.ProjectRunArguments);
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
            Delete(sourceRoot);
            Delete(repo);
        }
    }

    private static string CreateFakeRepo()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dtx-video-launch-policy-repo",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DTXMania.sln"), string.Empty);
        var projectPath = Path.Combine(root, GameProjectPaths.Current);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return root;
    }

    private static string CreateSandboxSourceRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dtx-video-launch-policy-source",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        TestSourceConfigDatabase.Create(root, TestSourceConfigDatabase.BuildValidRows(root));
        return root;
    }

    private static void Delete(string path)
    {
        // RecordingSandbox.Create opens a (default-pooled) read-only
        // SqliteConnection to <sourceRoot>/config.db via
        // SourceConfigDatabase.Load. On Windows the pooled native handle
        // keeps the file locked after the managed connection is disposed,
        // so the recursive directory delete below would throw IOException.
        // Clear the pool first to release the inode.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
