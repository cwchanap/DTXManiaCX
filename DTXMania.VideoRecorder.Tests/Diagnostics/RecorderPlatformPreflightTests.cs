using System.Runtime.InteropServices;
using DTXMania.Automation.Process;
using DTXMania.VideoRecorder.Diagnostics;
using DTXMania.VideoRecorder.Workflow;

namespace DTXMania.VideoRecorder.Tests.Diagnostics;

public sealed class RecorderPlatformPreflightTests
{
    [Fact]
    public void Evaluate_WindowsFacts_ReportsCommonProjectGateOnly()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Windows);
        try
        {
            var result = RecorderPlatformPreflight.Evaluate(
                Facts(isWindows: true, isMacOS: false, new Version(10, 0, 22621), Architecture.X64),
                CreateTarget(repo, GameProjectPaths.Windows));

            Assert.True(result.Passed);
            // Windows keeps the common project-existence gate but adds no
            // native-runtime/version/architecture gates.
            var gate = Assert.Single(result.Gates);
            Assert.Equal("Game project exists", gate.Name);
            Assert.True(gate.Passed);
            Assert.Null(result.MacRuntimeDirectory);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_WindowsFactsWithMissingProject_FailsProjectGate()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Windows);
        // Remove the project file so the common existence gate fails even on
        // Windows, where the old early-return skipped all validation.
        File.Delete(Resolve(repo, GameProjectPaths.Windows));
        try
        {
            var result = RecorderPlatformPreflight.Evaluate(
                Facts(isWindows: true, isMacOS: false, new Version(10, 0, 22621), Architecture.X64),
                CreateTarget(repo, GameProjectPaths.Windows));

            Assert.False(result.Passed);
            var gate = Assert.Single(result.Gates);
            Assert.Equal("Game project exists", gate.Name);
            Assert.False(gate.Passed);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_UnsupportedPlatformFacts_FailsPlatformGateOnly()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        try
        {
            var result = RecorderPlatformPreflight.Evaluate(
                Facts(isWindows: false, isMacOS: false, new Version(0, 0), Architecture.X64),
                CreateTarget(repo, GameProjectPaths.Mac));

            Assert.False(result.Passed);
            Assert.Null(result.MacRuntimeDirectory);
            // The common project-existence gate is present (and passes here
            // because the fake repo wrote the project), but the platform gate
            // is the one that rejects an unsupported host.
            var gate = FailedGate(result, "Platform");
            Assert.Contains("Windows and macOS only", gate.Detail);
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
            var ffprobeGate = Assert.Single(result.Gates, g => g.Name == "Bundled ffprobe");
            Assert.True(ffprobeGate.Passed);
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
            var ffmpegGate = Assert.Single(result.Gates, g => g.Name == "Bundled ffmpeg");
            Assert.True(ffmpegGate.Passed);
        }
        finally
        {
            Delete(repo);
        }
    }

    [SkippableFact]
    public void Evaluate_MacOs13Arm64WithNonExecutablePair_FailsRuntimeGates()
    {
        Skip.IfNot(UnixFileModeSupported, "Unix file mode is not representable on this host.");

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
        WriteApphost(repo, GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            Assert.True(result.Passed);
            Assert.All(result.Gates, gate => Assert.True(gate.Passed));
            Assert.Equal(RuntimeDirectory(repo), result.MacRuntimeDirectory);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithManagedDllButNoApphost_FailsDebugOutputGate()
    {
        // Regression: the Mac project is a net8.0 WinExe with the SDK default
        // UseAppHost=true, so dotnet run --no-build executes the native
        // apphost (DTXMania.Game.Mac, no extension), not the managed DLL.
        // A previous version of the "Debug output" gate checked only the
        // managed DLL, so a missing/non-executable apphost would pass
        // preflight, create the sandbox, and then fail at process launch —
        // the exact ordering problem preflight exists to prevent. This test
        // stages the DLL + bundled ffmpeg/ffprobe but NO apphost and asserts
        // preflight fails on the "Debug output" gate.
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        WriteManagedAssembly(repo, GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            Assert.False(result.Passed);
            var debugGate = FailedGate(result, "Debug output");
            Assert.Contains(RecorderPlatformPreflight.MacRuntimeRecoveryCommand, debugGate.Detail);
            // The bundled runtime gates pass, isolating the failure to the
            // apphost-only "Debug output" gate.
            var ffmpegGate = Assert.Single(result.Gates, g => g.Name == "Bundled ffmpeg");
            Assert.True(ffmpegGate.Passed);
            var ffprobeGate = Assert.Single(result.Gates, g => g.Name == "Bundled ffprobe");
            Assert.True(ffprobeGate.Passed);
        }
        finally
        {
            Delete(repo);
        }
    }

    [SkippableFact]
    public void Evaluate_MacOs13Arm64WithNonExecutableApphost_FailsDebugOutputGate()
    {
        // The apphost exists but is not executable. dotnet run --no-build
        // would fail to launch it, so preflight must reject it the same way
        // it rejects a non-executable ffmpeg/ffprobe.
        Skip.IfNot(UnixFileModeSupported, "Unix file mode is not representable on this host.");

        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        WriteManagedAssembly(repo, GameProjectPaths.Mac);
        WriteNonExecutableApphost(repo, GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            Assert.False(result.Passed);
            var debugGate = FailedGate(result, "Debug output");
            Assert.Contains(RecorderPlatformPreflight.MacRuntimeRecoveryCommand, debugGate.Detail);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithStaleTfmRuntimeOnly_FailsDebugOutputGate()
    {
        // Project targets net8.0, but only a stale previous-TFM runtime exists.
        // The old enumerator-based resolution would accept the stale runtime;
        // the TFM-pinned resolver must reject it because the current Debug
        // output directory has no build output.
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        var staleRuntime = Path.Combine(
            repo, "DTXMania.Game", "bin", "Debug", "net7.0", "runtimes", "osx-arm64", "MMTools");
        WriteExecutableFile(Path.Combine(staleRuntime, "ffmpeg"));
        WriteExecutableFile(Path.Combine(staleRuntime, "ffprobe"));
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            Assert.False(result.Passed);
            var debugGate = FailedGate(result, "Debug output");
            Assert.Contains("net8.0", debugGate.Detail);
            // The resolved runtime directory must follow the current TFM, not
            // the stale one, so a stale net7.0 runtime cannot satisfy gates.
            Assert.Contains("net8.0", result.MacRuntimeDirectory!);
            Assert.DoesNotContain("net7.0", result.MacRuntimeDirectory!);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void Evaluate_MacOs13Arm64WithUnreadableTargetFramework_FailsTargetFrameworkGateBeforeRuntimeGates()
    {
        // Project file exists but carries no readable <TargetFramework>, while
        // a stale bin/Debug/net8.0 build output (apphost + bundled
        // ffmpeg/ffprobe) is present. The net8.0 fallback must NOT let the
        // runtime/output gates pass: preflight fails on a dedicated
        // "Target framework" gate, and the runtime/output gates are not
        // evaluated, so a stale net8.0 directory cannot satisfy preflight
        // when the current project's TFM is unreadable.
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        OverwriteProjectWithoutTargetFramework(repo, GameProjectPaths.Mac);
        WriteApphost(repo, GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        try
        {
            var result = EvaluateMac(repo, new Version(13, 0), Architecture.Arm64);

            Assert.False(result.Passed);

            var tfmGate = FailedGate(result, "Target framework");
            Assert.Contains(GameProjectPaths.Mac, tfmGate.Detail);
            Assert.Contains("net8.0", tfmGate.Detail);

            // The runtime/output gates must be absent (not merely failing) so
            // a stale net8.0 directory cannot green-light preflight.
            Assert.DoesNotContain(result.Gates, gate => gate.Name == "Debug output");
            Assert.DoesNotContain(result.Gates, gate => gate.Name == "Bundled ffmpeg");
            Assert.DoesNotContain(result.Gates, gate => gate.Name == "Bundled ffprobe");

            // The fallback directory is still surfaced for diagnostics.
            Assert.Contains("net8.0", result.MacRuntimeDirectory!);
        }
        finally
        {
            Delete(repo);
        }
    }

    private static void OverwriteProjectWithoutTargetFramework(string root, string relativeProjectPath)
    {
        var projectPath = Resolve(root, relativeProjectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        // Project file with no <TargetFramework> element so ReadTargetFramework
        // returns null and the fallback path is exercised.
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">"
                + "<PropertyGroup><AssemblyName>DTXMania.Game.Mac</AssemblyName></PropertyGroup>"
                + "</Project>");
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
        WriteApphost(repo, GameProjectPaths.Mac);
        WriteRuntimeBinary(repo, "ffmpeg", executable: true);
        WriteRuntimeBinary(repo, "ffprobe", executable: true);
        return repo;
    }

    private static void WriteProject(string root, string relativeProjectPath)
    {
        var projectPath = Resolve(root, relativeProjectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        // The fake project carries the same TargetFramework the real Mac
        // project does, so the TFM-pinned Debug output resolver exercises the
        // production path rather than the unreadable-project fallback.
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">"
                + "<PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>"
                + "</Project>");
    }

    private static string Resolve(string root, string relativeProjectPath)
        => Path.Combine(new[] { root }.Concat(relativeProjectPath.Split('/')).ToArray());

    private static string RuntimeDirectory(string repo)
        => Path.Combine(repo, "DTXMania.Game", "bin", "Debug", "net8.0", "runtimes", "osx-arm64", "MMTools");

    private static void WriteApphost(string repo, string relativeProjectPath)
    {
        // dotnet run --no-build on a net8.0 WinExe with UseAppHost=true
        // executes the native apphost (named after the assembly, no extension
        // on macOS), not the managed DLL. Tests must therefore stage the
        // apphost — the artifact the "Debug output" gate certifies.
        var projectPath = Resolve(repo, relativeProjectPath);
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        var debugDir = Path.Combine(
            Path.GetDirectoryName(projectPath)!, "bin", "Debug", "net8.0");
        WriteExecutableFile(Path.Combine(debugDir, assemblyName));
    }

    private static void WriteNonExecutableApphost(string repo, string relativeProjectPath)
    {
        var projectPath = Resolve(repo, relativeProjectPath);
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        var debugDir = Path.Combine(
            Path.GetDirectoryName(projectPath)!, "bin", "Debug", "net8.0");
        var path = Path.Combine(debugDir, assemblyName);
        Directory.CreateDirectory(debugDir);
        File.WriteAllText(path, "stub");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void WriteManagedAssembly(string repo, string relativeProjectPath)
    {
        var projectPath = Resolve(repo, relativeProjectPath);
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        var debugDir = Path.Combine(
            Path.GetDirectoryName(projectPath)!, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(debugDir);
        File.WriteAllText(Path.Combine(debugDir, assemblyName + ".dll"), "stub");
    }

    private static void WriteRuntimeBinary(string repo, string name, bool executable)
    {
        var path = Path.Combine(RuntimeDirectory(repo), name);
        if (executable)
        {
            WriteExecutableFile(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "stub");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void WriteExecutableFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "stub");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
