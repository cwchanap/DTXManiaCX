#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// The launch seam the <see cref="CrashReportInbox"/> depends on. Faked in unit tests so the
/// inbox never spins a real process; the concrete <see cref="ExternalLauncher"/> performs the
/// actual platform-branched handoff.
/// </summary>
internal interface IExternalLauncher
{
    CrashReportActionResult LaunchUri(Uri target);

    CrashReportActionResult LaunchFolder(string path);
}

/// <summary>
/// Coarse platform classification used to branch the launcher at runtime rather than at compile
/// time, so both the Windows and macOS command builders live in one shared file that compiles
/// into both game projects and can be exercised from a single test assembly.
/// </summary>
internal enum LauncherPlatform
{
    Windows,
    Mac,
    Unsupported
}

/// <summary>
/// Outcome of a single process-start attempt. <see cref="Started"/> is <c>false</c> when the
/// platform reported a null process (no handler); otherwise <see cref="ExitCode"/> carries the
/// waited-for exit code where one is meaningful (macOS <c>open</c>).
/// </summary>
internal readonly record struct ExternalLaunchAttempt(bool Started, int ExitCode);

internal delegate ExternalLaunchAttempt ExternalLaunchStarter(ProcessStartInfo startInfo);

internal delegate LauncherPlatform LauncherPlatformResolver();

/// <summary>
/// Shared, testable external-handoff launcher for the crash-report inbox. It deliberately lives
/// outside the compile-time <c>Platform/</c> split: the Windows and macOS command builders are
/// pure static methods here, and the concrete launcher branches between them at runtime via
/// <see cref="RuntimeInformation"/>. Supported targets are strictly (a) a validated GitHub issue
/// URI built by <see cref="GitHubCrashIssueBuilder"/> and (b) the internally-resolved crash-report
/// root directory; it is not a general process-execution framework.
/// </summary>
internal sealed class ExternalLauncher : IExternalLauncher
{
    private readonly LauncherPlatformResolver _platform;
    private readonly ExternalLaunchStarter _starter;

    public ExternalLauncher()
        : this(platform: null, starter: null)
    {
    }

    internal ExternalLauncher(LauncherPlatformResolver? platform, ExternalLaunchStarter? starter)
    {
        _platform = platform ?? DefaultPlatform;
        _starter = starter ?? DefaultStart;
    }

    public CrashReportActionResult LaunchUri(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return LaunchCore(target.ToString());
    }

    public CrashReportActionResult LaunchFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LaunchCore(path);
    }

    internal static ProcessStartInfo CreateWindowsStartInfo(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        // FileName is the target URL/path; the shell resolves the handler. No cmd.exe, no
        // powershell, no concatenated command line.
        return new ProcessStartInfo(target)
        {
            UseShellExecute = true
        };
    }

    internal static ProcessStartInfo CreateMacStartInfo(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        // `open -- <target>` is the documented, non-shell way to hand a URL/folder to macOS.
        // ArgumentList quotes each argument literally, so there is no sh -c and no concatenation.
        var info = new ProcessStartInfo("/usr/bin/open")
        {
            UseShellExecute = false
        };
        info.ArgumentList.Add("--");
        info.ArgumentList.Add(target);
        return info;
    }

    private CrashReportActionResult LaunchCore(string target)
    {
        var platform = _platform();
        if (platform == LauncherPlatform.Unsupported)
        {
            return Failure("launch_platform_unsupported");
        }

        var info = platform == LauncherPlatform.Windows
            ? CreateWindowsStartInfo(target)
            : CreateMacStartInfo(target);

        ExternalLaunchAttempt attempt;
        try
        {
            attempt = _starter(info);
        }
        catch (Exception exception) when (IsLaunchException(exception))
        {
            return Failure("launch_start_failed");
        }

        if (!attempt.Started)
        {
            return Failure("launch_process_null");
        }

        // Only macOS `open` is waited on: it returns a meaningful exit code while the handler
        // is left running. Windows UseShellExecute launches the registered handler and must not
        // block on it, so its exit code is not consulted.
        if (platform == LauncherPlatform.Mac && attempt.ExitCode != 0)
        {
            return Failure("launch_nonzero_exit");
        }

        return new CrashReportActionResult(Succeeded: true);
    }

    private static LauncherPlatform DefaultPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return LauncherPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return LauncherPlatform.Mac;
        }

        return LauncherPlatform.Unsupported;
    }

    private static ExternalLaunchAttempt DefaultStart(ProcessStartInfo info)
    {
        var process = Process.Start(info);
        if (process is null)
        {
            return default;
        }

        using (process)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                process.WaitForExit();
                return new ExternalLaunchAttempt(true, process.ExitCode);
            }

            return new ExternalLaunchAttempt(true, 0);
        }
    }

    private static bool IsLaunchException(Exception exception)
    {
        return exception is InvalidOperationException
            or Win32Exception
            or FileNotFoundException
            or DirectoryNotFoundException
            or PlatformNotSupportedException
            or SecurityException;
    }

    private static CrashReportActionResult Failure(string code) => new(Succeeded: false, ErrorCode: code);
}
