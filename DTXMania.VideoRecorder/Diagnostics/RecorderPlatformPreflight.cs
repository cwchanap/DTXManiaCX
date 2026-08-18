using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
    string? MacRuntimeDirectory)
{
    public bool Passed => Gates.All(gate => gate.Passed);
}

/// <summary>
/// Shared record/doctor preflight for the native recorder launch
/// environment. HPA-515 recorder certification requires the bundled
/// osx-arm64 Debug runtime; a working PATH FFmpeg is deliberately not
/// accepted as evidence. Because <c>record</c> launches with
/// <c>dotnet run --no-build --configuration Debug</c>, preflight certifies
/// the exact Debug output directory that launch will use, resolved from the
/// project's <c>&lt;TargetFramework&gt;</c> rather than by enumerating
/// <c>bin/Debug</c> framework directories (a stale previous TFM could
/// otherwise satisfy preflight while the current project has no runnable
/// Debug output).
/// </summary>
internal static class RecorderPlatformPreflight
{
    internal const string MacRuntimeRecoveryCommand =
        "dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug";

    private static readonly Regex TargetFrameworkRegex = new(
        @"<TargetFramework>\s*([^<\s]+)\s*</TargetFramework>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static RecorderPlatformFacts CaptureFacts() => new(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        CaptureOsVersion(),
        RuntimeInformation.ProcessArchitecture);

    /// <summary>
    /// Evaluates the platform gates. The "Game project exists" gate is
    /// common to Windows and macOS so doctor cannot report success when the
    /// resolved project is missing on either platform. Windows adds no
    /// native-runtime, version, or architecture rejection; macOS must be 13+
    /// on Apple Silicon with the Mac project and a usable bundled runtime in
    /// the exact Debug output directory <c>--no-build</c> will launch.
    /// </summary>
    internal static RecorderPlatformPreflightResult Evaluate(
        RecorderPlatformFacts facts,
        ResolvedRecorderTarget target)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(target);

        var projectPath = Path.GetFullPath(target.Target.Path);
        var commonGates = new[]
        {
            new RecorderPreflightGate(
                "Game project exists",
                File.Exists(projectPath),
                projectPath),
        };

        if (facts.IsWindows)
            return new RecorderPlatformPreflightResult(commonGates, null);

        if (!facts.IsMacOS)
        {
            return new RecorderPlatformPreflightResult(
                commonGates
                    .Append(new RecorderPreflightGate(
                        "Platform",
                        Passed: false,
                        Detail: "dtx-video record is supported on Windows and macOS only."))
                    .ToArray(),
                null);
        }

        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var debugOutputDirectory = ResolveMacDebugOutputDirectory(projectPath, projectDirectory);
        var runtimeDirectory = Path.Combine(debugOutputDirectory, "runtimes", "osx-arm64", "MMTools");
        var managedAssembly = Path.Combine(debugOutputDirectory, ManagedAssemblyName(projectPath));
        return new RecorderPlatformPreflightResult(
            commonGates
                .Concat(new[]
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
                        "Debug output",
                        File.Exists(managedAssembly),
                        managedAssembly),
                    new RecorderPreflightGate(
                        "Bundled ffmpeg",
                        IsUsableRuntimeBinary(Path.Combine(runtimeDirectory, "ffmpeg")),
                        DescribeRuntimeBinary(Path.Combine(runtimeDirectory, "ffmpeg"))),
                    new RecorderPreflightGate(
                        "Bundled ffprobe",
                        IsUsableRuntimeBinary(Path.Combine(runtimeDirectory, "ffprobe")),
                        DescribeRuntimeBinary(Path.Combine(runtimeDirectory, "ffprobe"))),
                })
                .ToArray(),
            runtimeDirectory);
    }

    private static bool IsMacProject(string projectPath)
        => projectPath.Replace('\\', '/')
            .EndsWith(GameProjectPaths.Mac, StringComparison.Ordinal);

    /// <summary>
    /// Derives the managed assembly file name
    /// (<c>&lt;AssemblyName&gt;.dll</c>) from the resolved project file name,
    /// matching the default MSBuild assembly name for a single-target project
    /// that does not override <c>&lt;AssemblyName&gt;</c>.
    /// </summary>
    private static string ManagedAssemblyName(string projectPath)
        => Path.GetFileNameWithoutExtension(projectPath) + ".dll";

    /// <summary>
    /// Resolves the single Debug output directory that
    /// <c>dotnet run --no-build --configuration Debug</c> will launch, by
    /// reading <c>&lt;TargetFramework&gt;</c> from the project file. This
    /// intentionally does not enumerate <c>bin/Debug</c> framework
    /// directories: a stale previous TFM could otherwise satisfy preflight
    /// while the current project has no runnable Debug output. Falls back to
    /// a best-effort <c>net8.0</c> path when the project file or target
    /// framework cannot be read so the gate diagnostic still names a
    /// location; the <see cref="File.Exists"/> checks will fail in that case.
    /// </summary>
    private static string ResolveMacDebugOutputDirectory(
        string projectPath,
        string projectDirectory)
    {
        var targetFramework = ReadTargetFramework(projectPath);
        var binDebug = Path.Combine(projectDirectory, "bin", "Debug");
        return Path.Combine(binDebug, targetFramework ?? "net8.0");
    }

    private static string? ReadTargetFramework(string projectPath)
    {
        if (!File.Exists(projectPath))
            return null;
        string projectXml;
        try
        {
            projectXml = File.ReadAllText(projectPath);
        }
        catch (IOException)
        {
            return null;
        }
        var match = TargetFrameworkRegex.Match(projectXml);
        return match.Success ? match.Groups[1].Value : null;
    }

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
        // On .NET 5+ (this project targets net8.0), Environment.OSVersion
        // returns the macOS product version directly (e.g. macOS 26 reports
        // 26.5.2), not the Darwin kernel version. The "macOS >= 13" gate
        // therefore compares product versions without any Darwin conversion.
        return Environment.OSVersion.Version;
    }
}
