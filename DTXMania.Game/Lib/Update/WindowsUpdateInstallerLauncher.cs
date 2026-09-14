#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Process-start seam faked in unit tests so the update service never spins a real
/// installer. Returns the started process's first-exit observation — a task that
/// completes with the process exit code — or <c>null</c> when process creation
/// produced no process. Only the exit code is a terminal signal: the Inno
/// bootstrapper starts unelevated (the .iss uses <c>PrivilegesRequired=lowest</c>
/// with the dialog override, so the manifest is <c>asInvoker</c>) and may sit on
/// an unanswered internal UAC prompt for an unbounded time, so liveness alone can
/// never distinguish "install in progress" from "elevation still undecided".
/// A nonzero exit is a refused/cancelled elevation whenever it happens; a zero
/// exit is a committed elevated respawn or a finished no-elevation install; a
/// still-running process is simply undecided.
/// </summary>
internal delegate Task<int>? WindowsInstallerProcessStarter(ProcessStartInfo startInfo);

/// <summary>
/// Starts the verified Inno installer on Windows using the Task-0-proven baseline
/// contract. Never a shell and never a bounded wait that pretends to settle the
/// elevation outcome: <see cref="Launch"/> returns the started process's
/// first-exit observation, and the caller maps that terminal signal —
/// nonzero → retryable <see cref="GameUpdateState.Failed"/>, zero →
/// <see cref="GameUpdateState.InstallerCommitted"/>. A refused start (a synchronous
/// exception such as <see cref="Win32Exception"/>, or no process) returns
/// <c>null</c> so the service publishes a retryable <see cref="GameUpdateState.Failed"/>
/// and the running game stays alive.
/// </summary>
internal sealed class WindowsUpdateInstallerLauncher
{
    /// <summary>The exact installer argument contract proven manually in Task 0.</summary>
    internal const string BaselineArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /AUTOUPDATE";

    private readonly WindowsInstallerProcessStarter _starter;

    public WindowsUpdateInstallerLauncher()
        : this(starter: null)
    {
    }

    internal WindowsUpdateInstallerLauncher(WindowsInstallerProcessStarter? starter)
    {
        _starter = starter ?? CreateDefaultStarter();
    }

    /// <summary>
    /// Starts the installer at <paramref name="installerPath"/> and returns a task
    /// that completes with the started process's first exit code. Never blocks
    /// waiting on the process; the task may legitimately stay incomplete for the
    /// whole duration of a no-elevation install, during which Inno's
    /// /CLOSEAPPLICATIONS owns closing the game. Returns <c>null</c> when the start
    /// itself was refused — a synchronous start exception or no process produced.
    /// </summary>
    public Task<int>? Launch(string installerPath)
    {
        try
        {
            return _starter(CreateStartInfo(installerPath));
        }
        catch (Exception exception) when (IsLaunchException(exception))
        {
            return null;
        }
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

    internal static WindowsInstallerProcessStarter CreateDefaultStarter()
    {
        return info =>
        {
            var process = Process.Start(info);
            return process is null ? null : ObserveFirstExitAsync(process);
        };
    }

    /// <summary>
    /// Completes with the process's first exit code, then disposes the handle.
    /// Deliberately not cancelled or timed out: a pending UAC prompt has no
    /// deadline, and a refused elevation surfaces as the nonzero exit whenever
    /// the player answers it.
    /// </summary>
    private static async Task<int> ObserveFirstExitAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }

    internal static bool IsLaunchException(Exception exception) =>
        exception is Win32Exception
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException
            or PlatformNotSupportedException
            or SecurityException;
}
