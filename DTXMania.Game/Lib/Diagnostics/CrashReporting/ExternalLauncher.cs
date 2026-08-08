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
/// waited-for exit code where one is meaningful (macOS <c>open</c>). <see cref="TimedOut"/> is
/// <c>true</c> when the bounded macOS wait elapsed before <c>open</c> exited, so the caller can
/// map it to a stable failure without blocking the game thread any further.
/// </summary>
internal readonly record struct ExternalLaunchAttempt(bool Started, int ExitCode, bool TimedOut = false);

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
    /// <summary>
    /// The bounded wait applied to the macOS <c>open</c> handoff. <c>open</c> normally returns
    /// near-instantly once it has handed the target to LaunchServices; this bound keeps a stalled
    /// invocation from freezing the MonoGame update thread, mapping a timeout to
    /// <c>"launch_timeout"</c> instead of an indefinite block. Injected (via the constructor) so
    /// tests can use a tiny value without sleeping.
    /// </summary>
    internal static readonly TimeSpan DefaultMacLaunchTimeout = TimeSpan.FromSeconds(5);

    private readonly LauncherPlatformResolver _platform;
    private readonly ExternalLaunchStarter _starter;

    public ExternalLauncher()
        : this(platform: null, starter: null, macLaunchTimeout: null)
    {
    }

    internal ExternalLauncher(
        LauncherPlatformResolver? platform,
        ExternalLaunchStarter? starter,
        TimeSpan? macLaunchTimeout = null)
    {
        _platform = platform ?? DefaultPlatform;
        _starter = starter ?? CreateDefaultStarter(_platform, macLaunchTimeout ?? DefaultMacLaunchTimeout);
    }

    public CrashReportActionResult LaunchUri(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        // Defense in depth: the only URI handed to LaunchUri is the builder-produced GitHub
        // issue URL (always absolute HTTPS). Reject anything else before it can reach a shell
        // handler, so a malformed/build-regressed target can never be launched.
        if (!target.IsAbsoluteUri
            || !string.Equals(target.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("launch_target_rejected");
        }

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

        // A bounded macOS wait that did not return in time is a stable, retryable failure: the
        // game thread stays responsive instead of freezing on a stalled LaunchServices/open.
        // Checked before the exit code, which is meaningless after a timeout.
        if (attempt.TimedOut)
        {
            return Failure("launch_timeout");
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

    internal static LauncherPlatform DefaultPlatform()
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

    internal static ExternalLaunchStarter CreateDefaultStarter(
        LauncherPlatformResolver platform,
        TimeSpan macLaunchTimeout)
    {
        return info =>
        {
            var process = Process.Start(info);
            if (process is null)
            {
                return default;
            }

            using (process)
            {
                // Only macOS `open` is waited on: it returns a meaningful exit code while the
                // handler is left running. The wait is BOUNDED so a stalled LaunchServices/`open`
                // invocation cannot freeze the game thread indefinitely; a timeout becomes a
                // stable "launch_timeout" failure rather than an unbounded block. Windows
                // UseShellExecute launches the registered handler and must not block on it, so
                // its exit code is not consulted. The platform is resolved through the SAME
                // resolver LaunchCore uses, so an injected resolver controls both the command
                // construction and the bounded wait (rather than each querying the OS independently).
                if (platform() == LauncherPlatform.Mac)
                {
                    var timeoutMilliseconds = (int)Math.Min(macLaunchTimeout.TotalMilliseconds, int.MaxValue);
                    var exited = process.WaitForExit(timeoutMilliseconds);
                    if (!exited)
                    {
                        BestEffortKill(process);
                        return new ExternalLaunchAttempt(Started: true, ExitCode: 0, TimedOut: true);
                    }

                    return new ExternalLaunchAttempt(true, process.ExitCode);
                }

                return new ExternalLaunchAttempt(true, 0);
            }
        };
    }

    internal static void BestEffortKill(Process process)
    {
        // Best-effort cleanup of a timed-out process so it cannot linger; the launch has already
        // been reported as a timeout failure. Swallow the expected failures (process exited
        // between the timeout and the kill, or kill is not supported on the platform).
        try
        {
            process.Kill();
        }
        catch (Exception exception) when (IsLaunchException(exception))
        {
        }
    }

    internal static bool IsLaunchException(Exception exception)
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
