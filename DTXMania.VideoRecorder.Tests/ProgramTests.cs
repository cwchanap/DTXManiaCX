using System.Runtime.InteropServices;
using DTXMania.Automation.Process;
using DTXMania.VideoRecorder;
using DTXMania.VideoRecorder.Configuration;
using DTXMania.VideoRecorder.Diagnostics;
using DTXMania.VideoRecorder.Workflow;

namespace DTXMania.VideoRecorder.Tests;

public sealed class ProgramTests
{
    [Fact]
    public async Task RunRecordAsync_FailedPreflight_RejectsBeforeSandboxDiagnosticsOrObs()
    {
        var sourceRoot = CreateSandboxSourceRoot();
        var outputDirectory = Path.Combine(sourceRoot, "out");
        try
        {
            var command = new RecorderCommand(
                RecorderVerb.Record,
                Path.Combine(sourceRoot, "song.dtx"),
                outputDirectory);
            var environment = new RecorderEnvironment(
                new Uri("ws://127.0.0.1:4455"),
                string.Empty,
                Path.Combine(sourceRoot, "raw"),
                sourceRoot);
            var target = new ResolvedRecorderTarget(
                sourceRoot,
                sourceRoot,
                GameLaunchTarget.Project(Path.Combine(sourceRoot, "DTXMania.Game", "DTXMania.Game.Mac.csproj")));
            var preflight = new RecorderPlatformPreflightResult(
                new[]
                {
                    new RecorderPreflightGate(
                        "Bundled ffmpeg",
                        Passed: false,
                        Detail: "'/missing/ffmpeg' not found. Build the bundled runtime: "
                            + RecorderPlatformPreflight.MacRuntimeRecoveryCommand)
                },
                MacRuntimeDirectory: null);

            // No OBS server is required for this call to complete: completing
            // without one is exactly the invariant under test.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Program.RunRecordAsync(command, environment, target, preflight));

            Assert.Contains("Recorder platform preflight failed", exception.Message);
            Assert.Contains("Bundled ffmpeg", exception.Message);
            Assert.Contains(RecorderPlatformPreflight.MacRuntimeRecoveryCommand, exception.Message);
            Assert.Empty(FindSandboxesSourcedFrom(sourceRoot));
            Assert.False(Directory.Exists(Path.Combine(outputDirectory, "diagnostics")));
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void ReportPlatformPreflight_MacFacts_ReportsMacGatesAndGuidanceWithoutWindowsGate()
    {
        var repo = CreateFakeMacRepo();
        try
        {
            var facts = new RecorderPlatformFacts(
                IsWindows: false,
                IsMacOS: true,
                OsVersion: new Version(14, 0),
                ProcessArchitecture: Architecture.Arm64);
            var preflight = RecorderPlatformPreflight.Evaluate(
                facts,
                new ResolvedRecorderTarget(
                    repo,
                    repo,
                    GameLaunchTarget.Project(Path.Combine(repo, "DTXMania.Game", "DTXMania.Game.Mac.csproj"))));
            Assert.True(preflight.Passed);

            using var writer = new StringWriter();
            var passed = Program.ReportPlatformPreflight(writer, facts, preflight);

            Assert.True(passed);
            var output = writer.ToString();
            Assert.Contains("macOS >= 13: passed", output);
            Assert.Contains("Bundled ffmpeg: passed", output);
            Assert.DoesNotContain("Windows: ", output);
            Assert.DoesNotContain("Windows-only", output);
            Assert.Contains("ScreenCaptureKit application/window capture scoped to CX", output);
            Assert.Contains("Desktop Audio disabled", output);
            Assert.Contains("Microphone disabled", output);
            Assert.Contains("Screen Recording permission granted manually", output);
            Assert.Contains("Raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR", output);
            Assert.DoesNotContain("CX window/program capture configured", output);
        }
        finally
        {
            Delete(repo);
        }
    }

    [Fact]
    public void ReportPlatformPreflight_UnsupportedPlatform_DoesNotEmitWindowsOrMacPrerequisites()
    {
        var repo = CreateFakeRepo(GameProjectPaths.Mac);
        try
        {
            var facts = new RecorderPlatformFacts(
                IsWindows: false,
                IsMacOS: false,
                OsVersion: new Version(0, 0),
                ProcessArchitecture: Architecture.X64);
            var preflight = RecorderPlatformPreflight.Evaluate(
                facts,
                new ResolvedRecorderTarget(
                    repo,
                    repo,
                    GameLaunchTarget.Project(Path.Combine(repo, GameProjectPaths.Mac))));

            Assert.False(preflight.Passed);
            Assert.Null(preflight.MacRuntimeDirectory);

            using var writer = new StringWriter();
            var passed = Program.ReportPlatformPreflight(writer, facts, preflight);

            Assert.False(passed);
            var output = writer.ToString();
            Assert.Contains("Windows and macOS only", output);
            Assert.DoesNotContain("CX window/program capture configured", output);
            Assert.DoesNotContain("ScreenCaptureKit application/window capture scoped to CX", output);
            Assert.Contains("Manual OBS prerequisites are unavailable on this platform", output);
        }
        finally
        {
            Delete(repo);
        }
    }

    /// <summary>
    /// Identifies recorder sandboxes created from <paramref name="sourceRoot"/>
    /// by the marker paths its copied Config.ini embeds. Unique per source root,
    /// so parallel tests creating their own sandboxes never match here.
    /// </summary>
    private static string[] FindSandboxesSourcedFrom(string sourceRoot)
    {
        var sandboxHome = Path.Combine(Path.GetTempPath(), "DTXManiaCX-video");
        if (!Directory.Exists(sandboxHome))
            return Array.Empty<string>();

        return Directory.GetDirectories(sandboxHome)
            .Where(runRoot => SandboxConfigReferences(runRoot, sourceRoot))
            .ToArray();
    }

    private static bool SandboxConfigReferences(string runRoot, string sourceRoot)
    {
        var configPath = Path.Combine(runRoot, "appdata", "Config.ini");
        try
        {
            return File.Exists(configPath) &&
                File.ReadAllText(configPath).Contains(sourceRoot, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string CreateFakeMacRepo()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dtx-video-program-tests-repo",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DTXMania.sln"), string.Empty);
        var projectPath = Path.Combine(root, "DTXMania.Game", "DTXMania.Game.Mac.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">"
                + "<PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>"
                + "</Project>");
        var debugOutputDirectory = Path.Combine(
            root,
            "DTXMania.Game",
            "bin",
            "Debug",
            "net8.0");
        Directory.CreateDirectory(debugOutputDirectory);
        // dotnet run --no-build on a net8.0 WinExe with UseAppHost=true
        // executes the native apphost (no extension on macOS), not the DLL.
        // Stage the apphost with the executable bit so the "Debug output"
        // gate passes, matching a real Debug build's output.
        var apphostPath = Path.Combine(debugOutputDirectory, "DTXMania.Game.Mac");
        File.WriteAllText(apphostPath, "stub");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                apphostPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var runtimeDirectory = Path.Combine(debugOutputDirectory, "runtimes", "osx-arm64", "MMTools");
        Directory.CreateDirectory(runtimeDirectory);
        foreach (var name in new[] { "ffmpeg", "ffprobe" })
        {
            var path = Path.Combine(runtimeDirectory, name);
            File.WriteAllText(path, "stub");
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        return root;
    }

    private static string CreateFakeRepo(string relativeProjectPath)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dtx-video-program-tests-repo",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DTXMania.sln"), string.Empty);
        var projectPath = Path.Combine(
            new[] { root }.Concat(relativeProjectPath.Split('/')).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return root;
    }

    private static string CreateSandboxSourceRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dtx-video-program-tests-source",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        TestSourceConfigDatabase.Create(root, TestSourceConfigDatabase.BuildValidRows(root));
        return root;
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
