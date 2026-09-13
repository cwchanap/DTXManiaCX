#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Process-start seam faked in unit tests so the update service never spins a
/// real installer; a null return means the start reported no process.
/// </summary>
internal delegate Process? WindowsInstallerProcessStarter(ProcessStartInfo startInfo);

/// <summary>
/// Starts the verified Inno installer on Windows using the Task-0-proven
/// baseline contract. Never a shell, never a wait on the installer process,
/// never a game exit: every start failure (including elevation refusal
/// surfacing as <see cref="Win32Exception"/>) maps to <c>false</c> so the
/// service can publish a retryable <see cref="GameUpdateState.Failed"/>.
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
        _starter = starter ?? DefaultStarter;
    }

    /// <summary>
    /// Starts the installer at <paramref name="installerPath"/>. Returns <c>true</c>
    /// only when the process was actually created; the caller must not wait for it.
    /// </summary>
    public bool Launch(string installerPath)
    {
        try
        {
            using var process = _starter(CreateStartInfo(installerPath));
            return process is not null;
        }
        catch (Exception exception) when (IsLaunchException(exception))
        {
            return false;
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

    private static Process? DefaultStarter(ProcessStartInfo startInfo) => Process.Start(startInfo);

    internal static bool IsLaunchException(Exception exception) =>
        exception is Win32Exception
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException
            or PlatformNotSupportedException
            or SecurityException;
}
