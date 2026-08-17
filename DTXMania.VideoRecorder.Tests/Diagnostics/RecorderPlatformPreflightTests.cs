using System.Runtime.InteropServices;
using DTXMania.Automation.Process;
using DTXMania.VideoRecorder.Diagnostics;
using DTXMania.VideoRecorder.Workflow;

namespace DTXMania.VideoRecorder.Tests.Diagnostics;

public sealed class RecorderPlatformPreflightTests
{
    [Fact]
    public void Evaluate_WindowsFacts_AddNoPlatformOrRuntimeGates()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Windows);
        try
        {
            var result = RecorderPlatformPreflight.Evaluate(
                Facts(isWindows: true, isMacOS: false, new Version(10, 0, 22621), Architecture.X64),
                CreateTarget(repo, GameProjectPaths.Windows));

            Assert.True(result.Passed);
            Assert.Empty(result.Gates);
            Assert.Null(result.NativeRuntimeDirectory);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs12Arm64_FailsVersionGate()
    {
        var repo = CreateMacRuntimeRepo();
        try
        {
            var result = EvaluateMac(repo, new Version(12, 0), Architecture.Arm64);

            var gate = FailedGate(result, "macOS >= 13");
            Assert.Contains("macOS 12", gate.Detail);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13X64_FailsArchitectureGate()
    {
        var repo = CreateMacRuntimeRepo();
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.X64);

            var gate = FailedGate(result, "Apple Silicon (arm64)");
            Assert.Contains("X64", gate.Detail);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithWrongProject_FailsTargetGate()
    {
        var repo = CreateMacRuntimeRepo();
        WriteProject(repo, GameProjectPaths.Windows);
        try
        {
            var result = RecorderPlatformPreflight.Evaluate(
                Facts(isWindows: false, isMacOS: true, new Version(13, 0), Architecture.Arm64),
                CreateTarget(repo, GameProjectPaths.Windows));

            Assert.False(result.Passed);
            var gate = FailedGate(result, "Mac game project");
            Assert.Contains("DTXMania.Game.Windows.csproj", gate.Detail);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithoutFfmpeg_FailsRuntimeGateWithRecoveryCommand()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            var gate = FailedGate(result, "Bundled ffmpeg");
            Assert.Contains(RecorderPlatformPreflight.MacRuntimeRecoveryCommand, gate.Detail);
            Assert.All(result.Gates.Where(g => g.Name == "Bundled ffprobe"), g => Assert.True(g.Passed));
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithoutFfprobe_FailsRuntimeGateWithRecoveryCommand()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            var gate = FailedGate(result, "Bundled ffprobe");
            Assert.Contains(RecorderPlatformPreflight.MacRuntimeRecoveryCommand, gate.Detail);
            Assert.All(result.Gates.Where(g => g.Name == "Bundled ffmpeg"), g => Assert.True(g.Passed));
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithNonExecutablePair_FailsRuntimeGates()
    {
        if (!UnixFileModeSupported)
            return; // exec bit not representable on this host; pass-case covers the fallback

        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: false);
        WriteRuntimeBinary(repo, "ffprobe", executable: false);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            Assert.False(result.Passed);
            Assert.Contains(RecorderPlatformPreflight.MacRuntimeRecoveryCommand,
                FailedGate(result, "Bundled ffmpeg").Detail);
            Assert.Contains(RecorderPlatformPreflight.MacRuntimeRecoveryCommand,
                FailedGate(result, "Bundled ffprobe").Detail);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithExecutablePair_Passes()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            Assert.True(result.Passed);
            Assert.All(result.Gates, gate => Assert.True(gate.Passed));
            Assert.Equal(RuntimeDirectory(repo), result.NativeRuntimeDirectory);
        }
        finally
        {
            Delete(repo);
        }
    }

    private static RecorderPlatformPreflightResult EvaluateMac(
        string repo,
        Version osVersion,
        Architecture architecture)
        => RecorderPlatformPreflight.Evaluate(
            Facts(isWindows: false, isMacOS: true, osVersion, architecture),
            CreateTarget(repo, GameProjectPaths.Mac));

    private static RecorderPlatformFacts Facts(
        bool isWindows,
        bool isMacOS,
        Version osVersion,
        Architecture architecture)
        => new(isWindows, isMacOS, osVersion, architecture);

    private static ResolvedRecorderTarget CreateTarget(string repo, string relativeProjectPath)
        => new(repo, repo, GameLaunchTarget.Project(Resolve(repo, relativeProjectPath)));

    private static string CreateFakeRepo(string relativeProjectPath)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dtx-video-preflight-repo",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DTXMania.sln"), string.Empty);
        WriteProject(root, relativeProjectPath);
        return root;
    }

    private static string CreateMacRuntimeRepo()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        return repo;
    }

    private static void WriteProject(string root, string relativeProjectPath)
    {
        var projectPath = Resolve(root, relativeProjectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    }

    private static string Resolve(string root, string relativeProjectPath)
        => Path.Combine(new[] { root }.Concat(relativeProjectPath.Split('/')).ToArray());

    private static string RuntimeDirectory(string repo)
        => Path.Combine(repo, "DTXMania.Game", "bin", "Debug", "net8.0", "runtimes", "osx-arm64", "MMTools");

    private static void WriteRuntimeBinary(string repo, string name, bool executable)
    {
        var directory = RuntimeDirectory(repo);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "stub");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                executable
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static bool UnixFileModeSupported =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private static RecorderPreflightGate FailedGate(RecorderPlatformPreflightResult result, string name)
    {
        var gate = Assert.Single(result.Gates, g => g.Name == name);
        Assert.False(gate.Passed);
        return gate;
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
