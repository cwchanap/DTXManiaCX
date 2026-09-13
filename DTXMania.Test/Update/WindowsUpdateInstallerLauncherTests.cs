#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using DTXMania.Game.Lib.Update;
using Xunit;

namespace DTXMania.Test.Update;

[Trait("Category", "Unit")]
public class WindowsUpdateInstallerLauncherTests
{
    private const string InstallerPath = @"C:\temp\DTXMania-Setup-1.2.3.tmp.exe";

    [Fact]
    public void CreateStartInfo_WhenBuilt_ShouldRunInstallerExeDirectlyWithBaselineArguments()
    {
        var info = WindowsUpdateInstallerLauncher.CreateStartInfo(InstallerPath);

        Assert.Equal(InstallerPath, info.FileName);
        // Never a shell: the installer exe itself is the process image.
        Assert.False(info.UseShellExecute);
        Assert.Equal(
            new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/AUTOUPDATE" },
            info.ArgumentList.ToArray());
        // ArgumentList quoting means no concatenated command line was smuggled in.
        Assert.Equal(string.Empty, info.Arguments);
    }

    [Fact]
    public void Launch_WhenProcessStillRunningAtDeadline_ShouldReturnTrue()
    {
        // In-progress install needing no internal elevation (the current-user path):
        // the bootstrapper IS the installer and stays alive past the decision window.
        var launcher = new WindowsUpdateInstallerLauncher(
            _ => new InstallerLaunchAttempt(Started: true, ExitCode: 0, StillRunning: true));

        Assert.True(launcher.Launch(InstallerPath));
    }

    [Fact]
    public void Launch_WhenProcessExitsZeroInsideWindow_ShouldReturnTrue()
    {
        // The unelevated bootstrapper exits 0 once it has respawned itself elevated
        // (UAC accepted) — the elevated copy carries on the all-users install.
        var launcher = new WindowsUpdateInstallerLauncher(
            _ => new InstallerLaunchAttempt(Started: true, ExitCode: 0));

        Assert.True(launcher.Launch(InstallerPath));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1223)]
    public void Launch_WhenProcessExitsNonzeroInsideWindow_ShouldReturnFalse(int exitCode)
    {
        // The Task-0 all-users path: the bootstrapper's internal UAC prompt was
        // cancelled or failed AFTER Process.Start already returned a process. Any
        // nonzero early exit is a refused launch — retryable, never InstallerLaunched,
        // never a game exit. (The real Windows cancel run must observe a nonzero code.)
        var launcher = new WindowsUpdateInstallerLauncher(
            _ => new InstallerLaunchAttempt(Started: true, ExitCode: exitCode));

        Assert.False(launcher.Launch(InstallerPath));
    }

    [Theory]
    [InlineData(740)]  // ERROR_ELEVATION_REQUIRED
    [InlineData(1223)] // ERROR_CANCELLED
    public void Launch_WhenStarterThrows_ShouldReturnFalseWithoutThrowing(int win32Error)
    {
        // Synchronous process-creation failures still map to a refused launch.
        var launcher = new WindowsUpdateInstallerLauncher(_ => throw new Win32Exception(win32Error));

        Assert.False(launcher.Launch(InstallerPath));
    }

    [Fact]
    public void Launch_WhenStartReportsNoProcess_ShouldReturnFalse()
    {
        var launcher = new WindowsUpdateInstallerLauncher(_ => default);

        Assert.False(launcher.Launch(InstallerPath));
    }

    // ---------------------------------------------------------------------------------------------
    // Real-process regression: exercise the actual WaitForExit/ExitCode plumbing in
    // CreateDefaultStarter against a real executable on whichever OS the tests run.
    // The final all-users UAC-cancel behaviour still needs the manual Windows Task-0
    // re-run — CI cannot click a consent prompt — but the early-exit signal the
    // launcher maps is pinned here against a real process.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DefaultStarter_WhenRealProcessExitsNonzero_ShouldReportExitCode()
    {
        var starter = WindowsUpdateInstallerLauncher.CreateDefaultStarter(TimeSpan.FromSeconds(10));

        var attempt = starter(RealProcessStartInfo(exitCommand: "exit 42"));

        Assert.True(attempt.Started);
        Assert.False(attempt.StillRunning);
        Assert.Equal(42, attempt.ExitCode);
    }

    [Fact]
    public void DefaultStarter_WhenRealProcessOutlivesWindow_ShouldReportStillRunning()
    {
        var starter = WindowsUpdateInstallerLauncher.CreateDefaultStarter(TimeSpan.FromMilliseconds(300));

        var attempt = starter(RealProcessStartInfo(exitCommand: null));

        Assert.True(attempt.Started);
        Assert.True(attempt.StillRunning);
        // The real process is left running past the window (it self-terminates shortly
        // after); the starter deliberately does not kill a committed install.
    }

    /// <summary>
    /// A real executable: cmd.exe on Windows, /bin/sh elsewhere. With
    /// <paramref name="exitCommand"/> the process exits immediately with that command's
    /// code; without it the process sleeps a few seconds so the bounded wait expires
    /// while it is still running.
    /// </summary>
    private static ProcessStartInfo RealProcessStartInfo(string? exitCommand)
    {
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false };
            info.ArgumentList.Add("/c");
            if (exitCommand is not null)
            {
                // "exit 42" as one argument -> cmd /c "exit 42".
                info.ArgumentList.Add(exitCommand);
            }
            else
            {
                info.ArgumentList.Add("ping -n 3 127.0.0.1 >nul");
            }
        }
        else
        {
            info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(exitCommand ?? "sleep 2");
        }

        return info;
    }
}
