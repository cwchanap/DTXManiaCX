#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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
    public async Task Launch_WhenProcessExitObserved_ShouldReturnTaskCompletingWithExitCode()
    {
        // The first-exit observation is the only terminal signal the launcher reports;
        // Launch itself never decides commitment.
        var launcher = new WindowsUpdateInstallerLauncher(_ => Task.FromResult(0));

        var observation = launcher.Launch(InstallerPath);

        Assert.NotNull(observation);
        Assert.Equal(0, await observation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1223)]
    public async Task Launch_WhenProcessExitsNonzero_ShouldReturnTaskCompletingWithExitCode(int exitCode)
    {
        // The Task-0 all-users path: the bootstrapper's internal UAC prompt was
        // cancelled or failed AFTER Process.Start already returned a process. The
        // launcher surfaces the code unfiltered — the service maps nonzero to a
        // retryable Failed, never InstallerCommitted, never a game exit.
        var launcher = new WindowsUpdateInstallerLauncher(_ => Task.FromResult(exitCode));

        var observation = launcher.Launch(InstallerPath);

        Assert.NotNull(observation);
        Assert.Equal(exitCode, await observation);
    }

    [Fact]
    public void Launch_WhenExitObservationStillPending_ShouldReturnIncompleteTask()
    {
        // A still-running process is simply undecided — an unanswered internal UAC
        // prompt has no deadline, so there is deliberately no timeout that would
        // pretend the handoff resolved.
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new WindowsUpdateInstallerLauncher(_ => gate.Task);

        var observation = launcher.Launch(InstallerPath);

        Assert.NotNull(observation);
        Assert.False(observation.IsCompleted);

        gate.TrySetResult(1); // late refusal still resolves the same observation
        Assert.Equal(1, observation.Result);
    }

    [Theory]
    [InlineData(740)]  // ERROR_ELEVATION_REQUIRED
    [InlineData(1223)] // ERROR_CANCELLED
    public void Launch_WhenStarterThrows_ShouldReturnNullWithoutThrowing(int win32Error)
    {
        // Synchronous process-creation failures still map to a refused launch.
        var launcher = new WindowsUpdateInstallerLauncher(_ => throw new Win32Exception(win32Error));

        Assert.Null(launcher.Launch(InstallerPath));
    }

    [Fact]
    public void Launch_WhenStartReportsNoProcess_ShouldReturnNull()
    {
        var launcher = new WindowsUpdateInstallerLauncher(_ => null);

        Assert.Null(launcher.Launch(InstallerPath));
    }

    // ---------------------------------------------------------------------------------------------
    // Real-process regression: exercise the actual WaitForExitAsync/ExitCode plumbing in
    // CreateDefaultStarter against a real executable on whichever OS the tests run.
    // The final all-users UAC-cancel behaviour still needs the manual Windows Task-0
    // re-run — CI cannot click a consent prompt — but the first-exit signal the
    // launcher surfaces is pinned here against a real process.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task DefaultStarter_WhenRealProcessExitsNonzero_ShouldCompleteWithExitCode()
    {
        var starter = WindowsUpdateInstallerLauncher.CreateDefaultStarter();

        var observation = starter(RealProcessStartInfo(exitCommand: "exit 42"));

        Assert.NotNull(observation);
        Assert.Equal(42, await observation);
    }

    [Fact]
    public async Task DefaultStarter_WhenRealProcessOutlivesEarlyObservation_ShouldStayIncompleteThenCompleteWithCode()
    {
        var starter = WindowsUpdateInstallerLauncher.CreateDefaultStarter();

        var observation = starter(RealProcessStartInfo(exitCommand: null));

        Assert.NotNull(observation);
        Assert.False(observation.IsCompleted); // still running — undecided, not committed
        Assert.Equal(0, await observation);    // exits cleanly a couple of seconds later
    }

    /// <summary>
    /// A real executable: cmd.exe on Windows, /bin/sh elsewhere. With
    /// <paramref name="exitCommand"/> the process exits immediately with that command's
    /// code; without it the process sleeps a few seconds so the observation stays
    /// incomplete while it is running.
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
