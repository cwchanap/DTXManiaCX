using System.Runtime.InteropServices;
using DTXMania.Automation.Process;
using DTXMania.VideoRecorder.Workflow;

namespace DTXMania.VideoRecorder.Diagnostics;

internal sealed record RecorderPlatformFacts(
    bool IsWindows,
    bool IsMacOS,
    Version OsVersion,
    Architecture ProcessArchitecture);

internal sealed record RecorderPreflightGate(
    string Name,
    bool Passed,
    string Detail);

internal sealed record RecorderPlatformPreflightResult(
    IReadOnlyList<RecorderPreflightGate> Gates,
    string? NativeRuntimeDirectory)
{
    public bool Passed => Gates.All(gate => gate.Passed);
}

/// <summary>
/// Shared record/doctor preflight for the native recorder launch
/// environment. HPA-515 recorder certification requires the bundled
/// osx-arm64 Debug runtime; a working PATH FFmpeg is deliberately not
/// accepted as evidence.
/// </summary>
internal static class RecorderPlatformPreflight
{
    internal const string MacRuntimeRecoveryCommand =
        "dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug";

    internal static RecorderPlatformFacts CaptureFacts() => new(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        CaptureOsVersion(),
        RuntimeInformation.ProcessArchitecture);

    /// <summary>
    /// Evaluates the platform gates. Windows adds no native-runtime,
    /// version, or architecture rejection; macOS must be 13+ on Apple
    /// Silicon with the Mac project and a usable bundled runtime.
    /// </summary>
    internal static RecorderPlatformPreflightResult Evaluate(
        RecorderPlatformFacts facts,
        ResolvedRecorderTarget target)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(target);

        if (facts.IsWindows)
            return new RecorderPlatformPreflightResult(Array.Empty<RecorderPreflightGate>(), null);

        if (!facts.IsMacOS)
        {
            return new RecorderPlatformPreflightResult(
                new[]
                {
                    new RecorderPreflightGate(
                        "Platform",
                        Passed: false,
                        Detail: "dtx-video record is supported on Windows and macOS only.")
                },
                null);
        }

        var projectPath = Path.GetFullPath(target.Target.Path);
        var runtimeDirectory = Path.Combine(
            Path.GetDirectoryName(projectPath) ?? string.Empty,
            "bin", "Debug", "net8.0", "runtimes", "osx-arm64", "MMTools");
        return new RecorderPlatformPreflightResult(
            new[]
            {
                new RecorderPreflightGate(
                    "macOS >= 13",
                    facts.OsVersion.Major >= 13,
                    $"found macOS {facts.OsVersion}"),
                new RecorderPreflightGate(
                    "Apple Silicon (arm64)",
                    facts.ProcessArchitecture == Architecture.Arm64,
                    $"process architecture is {facts.ProcessArchitecture}"),
                new RecorderPreflightGate(
                    "Mac game project",
                    IsMacProject(projectPath),
                    projectPath),
                new RecorderPreflightGate(
                    "Game project exists",
                    File.Exists(projectPath),
                    projectPath),
                new RecorderPreflightGate(
                    "Bundled ffmpeg",
                    IsUsableRuntimeBinary(Path.Combine(runtimeDirectory, "ffmpeg")),
                    DescribeRuntimeBinary(Path.Combine(runtimeDirectory, "ffmpeg"))),
                new RecorderPreflightGate(
                    "Bundled ffprobe",
                    IsUsableRuntimeBinary(Path.Combine(runtimeDirectory, "ffprobe")),
                    DescribeRuntimeBinary(Path.Combine(runtimeDirectory, "ffprobe")))
            },
            runtimeDirectory);
    }

    private static bool IsMacProject(string projectPath)
        => projectPath.Replace('\\', '/')
            .EndsWith(GameProjectPaths.Mac, StringComparison.Ordinal);

    private static bool IsUsableRuntimeBinary(string path)
        => File.Exists(path) && IsExecutable(path);

    private static string DescribeRuntimeBinary(string path)
        => File.Exists(path) && IsExecutable(path)
            ? path
            : $"'{path}' is missing or not executable. Build the bundled Debug runtime first: {MacRuntimeRecoveryCommand}";

    private static bool IsExecutable(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // The host filesystem has no executable bit (e.g. Windows CI
            // evaluating synthetic macOS facts); existence is the strongest
            // check available there.
            return true;
        }

        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    private static Version CaptureOsVersion()
    {
        var version = Environment.OSVersion.Version;
        if (!OperatingSystem.IsMacOS() || version.Major < 20)
            return version;

        // Environment.OSVersion reports the Darwin kernel version on macOS
        // (macOS 13 ships Darwin 22). Convert back to the product major so
        // the "macOS >= 13" gate compares product versions; Darwin 25+
        // (year-based releases) still clears the threshold after conversion.
        return new Version(version.Major - 9, version.Minor);
    }
}
