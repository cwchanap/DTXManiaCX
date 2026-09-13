#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Outcome of a single installer-start attempt. <see cref="Started"/> is <c>false</c> when
/// process creation reported no process. When the process exited inside the bounded
/// elevation-decision window, <see cref="StillRunning"/> is <c>false</c> and
/// <see cref="ExitCode"/> carries the bootstrapper's internal UAC result: 0 means it
/// respawned elevated (accepted) or finished; a nonzero code means the elevation was
/// cancelled or failed. When the process was still running at the deadline,
/// <see cref="StillRunning"/> is <c>true</c> — no internal elevation was needed, so a
/// real install is in progress and the launch is committed.
/// </summary>
internal readonly record struct InstallerLaunchAttempt(bool Started, int ExitCode, bool StillRunning = false);

/// <summary>
/// Process-start seam faked in unit tests so the update service never spins a real
/// installer. The default implementation starts the process AND observes it through the
/// bounded elevation-decision window, because <see cref="Process.Start"/> returning a
/// process does not prove the installer committed: the Inno bootstrapper starts
/// unelevated (the .iss uses <c>PrivilegesRequired=lowest</c> with the dialog override,
/// so the manifest is <c>asInvoker</c>) and re-launches itself elevated via UAC from
/// inside the started process when it reuses a previous all-users install.
/// </summary>
internal delegate InstallerLaunchAttempt WindowsInstallerProcessStarter(ProcessStartInfo startInfo);

/// <summary>
/// Starts the verified Inno installer on Windows using the Task-0-proven baseline
/// contract. Never a shell, never a wait for install completion, and a committed start
/// is required before the service may publish <see cref="GameUpdateState.InstallerLaunched"/>:
/// process creation alone is not commitment, because the bootstrapper's internal
/// elevation prompt resolves after <see cref="Process.Start"/> has already returned.
/// Every refused start — a synchronous start exception (e.g. <see cref="Win32Exception"/>)
/// or a quick nonzero bootstrapper exit — maps to <c>false</c> so the service publishes a
/// retryable <see cref="GameUpdateState.Failed"/> and the running game stays alive.
/// </summary>
internal sealed class WindowsUpdateInstallerLauncher
{
    /// <summary>The exact installer argument contract proven manually in Task 0.</summary>
    internal const string BaselineArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /AUTOUPDATE";

    /// <summary>
    /// The bounded window in which the started process is observed for an early exit —
    /// the only signal that resolves the bootstrapper's internal elevation handoff.
    /// A current-user install needs no elevation, so the bootstrapper IS the installer
    /// and stays running past the window; an all-users install respawns elevated (or
    /// dies on UAC refusal) quickly, inside it. 60s generously covers a distracted
    /// player's UAC response while remaining far shorter than a real install.
    /// </summary>
    internal static readonly TimeSpan DefaultElevationDecisionTimeout = TimeSpan.FromSeconds(60);

    private readonly WindowsInstallerProcessStarter _starter;

    public WindowsUpdateInstallerLauncher()
        : this(starter: null, elevationDecisionTimeout: null)
    {
    }

    internal WindowsUpdateInstallerLauncher(
        WindowsInstallerProcessStarter? starter,
        TimeSpan? elevationDecisionTimeout = null)
    {
        _starter = starter ?? CreateDefaultStarter(elevationDecisionTimeout ?? DefaultElevationDecisionTimeout);
    }

    /// <summary>
    /// Starts the installer at <paramref name="installerPath"/> and observes the bounded
    /// elevation-decision window. Returns <c>true</c> only when the launch is committed:
    /// the process survived the window (an in-progress install needing no elevation), or
    /// it exited inside the window with code 0 (the bootstrapper successfully respawned
    /// elevated). A refused start, a null process, or a nonzero early exit returns
    /// <c>false</c>; the caller must never wait for install completion.
    /// </summary>
    public bool Launch(string installerPath)
    {
        InstallerLaunchAttempt attempt;
        try
        {
            attempt = _starter(CreateStartInfo(installerPath));
        }
        catch (Exception exception) when (IsLaunchException(exception))
        {
            return false;
        }

        if (!attempt.Started)
        {
            return false;
        }

        if (attempt.StillRunning)
        {
            return true;
        }

        return attempt.ExitCode == 0;
    }

    internal static ProcessStartInfo CreateStartInfo(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        // FileName is the installer exe itself; UseShellExecute=false means
        // CreateProcess runs it directly — no cmd.exe, no handler resolution.
        var info = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = false
        };
        foreach (var argument in BaselineArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }

    internal static WindowsInstallerProcessStarter CreateDefaultStarter(TimeSpan elevationDecisionTimeout)
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
                var timeoutMilliseconds = (int)Math.Min(elevationDecisionTimeout.TotalMilliseconds, int.MaxValue);
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    // Still running: the in-progress install itself. Deliberately NOT
                    // killed (unlike a stalled handoff) — it is the committed update.
                    return new InstallerLaunchAttempt(Started: true, ExitCode: 0, StillRunning: true);
                }

                return new InstallerLaunchAttempt(Started: true, process.ExitCode);
            }
        };
    }

    internal static bool IsLaunchException(Exception exception) =>
        exception is Win32Exception
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException
            or PlatformNotSupportedException
            or SecurityException;
}
