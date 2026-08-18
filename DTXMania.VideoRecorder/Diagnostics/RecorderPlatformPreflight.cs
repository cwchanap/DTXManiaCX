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
/// <c>dotnet run --no-build --configuration Debug</c> on every platform,
/// preflight certifies the exact Debug output artifact that launch will
/// execute. The Mac project is a net8.0 <c>WinExe</c> with the SDK default
/// <c>UseAppHost=true</c>, so <c>dotnet run --no-build</c> executes the
/// native apphost (<c>&lt;AssemblyName&gt;</c>, no extension on macOS) — not
/// the managed DLL. The Windows project is a net8.0-windows <c>WinExe</c>
/// with the same SDK default, so its apphost is
/// <c>&lt;AssemblyName&gt;.exe</c>. The "Debug output" gate therefore
/// certifies the apphost itself (existence + executable bit on macOS;
/// existence on Windows, which has no executable bit), since a
/// missing/non-executable apphost would pass a DLL-only gate and then fail
/// at process launch after the sandbox is already created. The Debug output
/// directory is resolved from the project's <c>&lt;TargetFramework&gt;</c>
/// rather than by enumerating <c>bin/Debug</c> framework directories (a
/// stale previous TFM could otherwise satisfy preflight while the current
/// project has no runnable Debug output).
/// </summary>
internal static class RecorderPlatformPreflight
{
    internal const string MacRuntimeRecoveryCommand =
        "dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug";

    internal const string WindowsRuntimeRecoveryCommand =
        "dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Debug";

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
    /// resolved project is missing on either platform. Windows certifies the
    /// Debug output apphost (<c>&lt;AssemblyName&gt;.exe</c>) in the exact
    /// <c>bin/Debug/&lt;TargetFramework&gt;</c> directory
    /// <c>--no-build</c> will launch, with no native-runtime, version, or
    /// architecture rejection; macOS must be 13+ on Apple Silicon with the
    /// Mac project and a usable bundled runtime in the exact Debug output
    /// directory <c>--no-build</c> will launch.
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
        {
            return new RecorderPlatformPreflightResult(
                commonGates.Concat(EvaluateWindowsGates(projectPath)).ToArray(),
                null);
        }

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
        var debugOutputResolution = ResolveDebugOutputDirectory(projectPath, projectDirectory);
        var debugOutputDirectory = debugOutputResolution.Directory;
        var runtimeDirectory = Path.Combine(debugOutputDirectory, "runtimes", "osx-arm64", "MMTools");
        // dotnet run --no-build on a net8.0 WinExe with UseAppHost=true (the
        // SDK default for executable projects) executes the native apphost,
        // not the managed DLL. The apphost is named after the assembly with
        // no extension on macOS. Certify the apphost itself so a
        // missing/non-executable apphost cannot pass preflight and then fail
        // at process launch after the sandbox is created.
        var apphost = Path.Combine(debugOutputDirectory, ApphostName(projectPath));

        var macGates = new List<RecorderPreflightGate>
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
        };

        if (debugOutputResolution.TargetFramework is null)
        {
            // Target-framework resolution failed (project missing, IO error,
            // or no <TargetFramework> element). The net8.0 fallback below is
            // retained only to name a diagnostic location in
            // MacRuntimeDirectory and the gate detail; the runtime/output
            // gates are deliberately not evaluated so a stale
            // bin/Debug/net8.0 directory cannot satisfy preflight when the
            // current project's TFM is unreadable.
            macGates.Add(new RecorderPreflightGate(
                "Target framework",
                Passed: false,
                Detail: $"Could not read <TargetFramework> from '{projectPath.Replace('\\', '/')}'. "
                    + $"Diagnostic fallback Debug output directory is '{debugOutputDirectory.Replace('\\', '/')}'; "
                    + $"build the project so a readable <TargetFramework> is present."));
        }
        else
        {
            macGates.Add(new RecorderPreflightGate(
                "Debug output",
                IsUsableRuntimeBinary(apphost),
                DescribeRuntimeBinary(apphost, MacRuntimeRecoveryCommand)));
            macGates.Add(new RecorderPreflightGate(
                "Bundled ffmpeg",
                IsUsableRuntimeBinary(Path.Combine(runtimeDirectory, "ffmpeg")),
                DescribeRuntimeBinary(Path.Combine(runtimeDirectory, "ffmpeg"), MacRuntimeRecoveryCommand)));
            macGates.Add(new RecorderPreflightGate(
                "Bundled ffprobe",
                IsUsableRuntimeBinary(Path.Combine(runtimeDirectory, "ffprobe")),
                DescribeRuntimeBinary(Path.Combine(runtimeDirectory, "ffprobe"), MacRuntimeRecoveryCommand)));
        }

        return new RecorderPlatformPreflightResult(
            commonGates.Concat(macGates).ToArray(),
            runtimeDirectory);
    }

    private static bool IsMacProject(string projectPath)
        => projectPath.Replace('\\', '/')
            .EndsWith(GameProjectPaths.Mac, StringComparison.Ordinal);

    /// <summary>
    /// Windows-only gates. <c>dotnet run --no-build</c> on a net8.0-windows
    /// <c>WinExe</c> with the SDK default <c>UseAppHost=true</c> executes the
    /// native apphost (<c>&lt;AssemblyName&gt;.exe</c>), not the managed DLL.
    /// The "Debug output" gate certifies that apphost in the TFM-pinned
    /// <c>bin/Debug</c> directory so a clean checkout cannot pass preflight
    /// and then fail at process launch after the sandbox is created — the
    /// same ordering guarantee the Mac gates provide. When the project's
    /// <c>&lt;TargetFramework&gt;</c> is unreadable, a failed "Target
    /// framework" gate replaces the "Debug output" gate so a stale
    /// <c>bin/Debug</c> directory cannot satisfy preflight.
    /// </summary>
    private static IEnumerable<RecorderPreflightGate> EvaluateWindowsGates(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var debugOutputResolution = ResolveDebugOutputDirectory(projectPath, projectDirectory);
        var debugOutputDirectory = debugOutputResolution.Directory;
        var apphost = Path.Combine(debugOutputDirectory, WindowsApphostName(projectPath));

        if (debugOutputResolution.TargetFramework is null)
        {
            // Target-framework resolution failed (project missing, IO error,
            // or no <TargetFramework> element). The fallback directory is
            // retained only to name a diagnostic location in the gate detail;
            // the Debug output gate is deliberately not evaluated so a stale
            // bin/Debug directory cannot satisfy preflight when the current
            // project's TFM is unreadable.
            yield return new RecorderPreflightGate(
                "Target framework",
                Passed: false,
                Detail: $"Could not read <TargetFramework> from '{projectPath.Replace('\\', '/')}'. "
                    + $"Diagnostic fallback Debug output directory is '{debugOutputDirectory.Replace('\\', '/')}'; "
                    + $"build the project so a readable <TargetFramework> is present.");
            yield break;
        }

        yield return new RecorderPreflightGate(
            "Debug output",
            IsUsableRuntimeBinary(apphost),
            DescribeRuntimeBinary(apphost, WindowsRuntimeRecoveryCommand));
    }

    /// <summary>
    /// Derives the native apphost file name that
    /// <c>dotnet run --no-build</c> executes for a net8.0 <c>WinExe</c>
    /// project with the SDK default <c>UseAppHost=true</c>. The apphost is
    /// named after the assembly with no extension on macOS. This matches the
    /// default MSBuild assembly name for a single-target project that does
    /// not override <c>&lt;AssemblyName&gt;</c>.
    /// </summary>
    private static string ApphostName(string projectPath)
        => Path.GetFileNameWithoutExtension(projectPath);

    /// <summary>
    /// Derives the native Windows apphost file name that
    /// <c>dotnet run --no-build</c> executes for a net8.0-windows
    /// <c>WinExe</c> project with the SDK default <c>UseAppHost=true</c>:
    /// <c>&lt;AssemblyName&gt;.exe</c>.
    /// </summary>
    private static string WindowsApphostName(string projectPath)
        => Path.GetFileNameWithoutExtension(projectPath) + ".exe";

    /// <summary>
    /// Resolves the single Debug output directory that
    /// <c>dotnet run --no-build --configuration Debug</c> will launch, by
    /// reading <c>&lt;TargetFramework&gt;</c> from the project file. This
    /// intentionally does not enumerate <c>bin/Debug</c> framework
    /// directories: a stale previous TFM could otherwise satisfy preflight
    /// while the current project has no runnable Debug output. When the
    /// project file or target framework cannot be read,
    /// <see cref="DebugOutputResolution.TargetFramework"/> is null and the
    /// returned <see cref="DebugOutputResolution.Directory"/> falls back to
    /// a best-effort <c>net8.0</c> path so downstream diagnostic strings
    /// (gate detail, <see cref="RecorderPlatformPreflightResult.MacRuntimeDirectory"/>)
    /// still name a location; the caller must add a failed gate in that case
    /// rather than relying on the fallback directory to satisfy runtime/output
    /// gates, since a stale <c>bin/Debug/net8.0</c> directory could otherwise
    /// pass those <see cref="File.Exists"/> checks.
    /// </summary>
    private static DebugOutputResolution ResolveDebugOutputDirectory(
        string projectPath,
        string projectDirectory)
    {
        var targetFramework = ReadTargetFramework(projectPath);
        var binDebug = Path.Combine(projectDirectory, "bin", "Debug");
        return new DebugOutputResolution(
            Path.Combine(binDebug, targetFramework ?? "net8.0"),
            targetFramework);
    }

    private sealed record DebugOutputResolution(
        string Directory,
        string? TargetFramework);

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

    private static string DescribeRuntimeBinary(string path, string recoveryCommand)
        => File.Exists(path) && IsExecutable(path)
            ? path
            : $"'{path}' is missing or not executable. Build the Debug output first: {recoveryCommand}";

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
