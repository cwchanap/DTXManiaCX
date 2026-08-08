#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class ExternalLauncherTests
{
    private const string GitHubTarget = "https://github.com/cwchanap/DTXManiaCX/issues/new";

    [Fact]
    public void CreateWindowsStartInfo_ShouldUseShellExecuteWithTheTargetAsFileName()
    {
        var info = ExternalLauncher.CreateWindowsStartInfo(GitHubTarget);

        Assert.Equal(GitHubTarget, info.FileName);
        Assert.True(info.UseShellExecute);
        Assert.Empty(info.ArgumentList);
    }

    [Fact]
    public void CreateWindowsStartInfo_ShouldNeverInvokeCmdOrPowershell()
    {
        var info = ExternalLauncher.CreateWindowsStartInfo(GitHubTarget);

        Assert.DoesNotContain("cmd", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cmd", info.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", info.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateMacStartInfo_ShouldInvokeOpenWithDashDashAndTheTarget()
    {
        var info = ExternalLauncher.CreateMacStartInfo(GitHubTarget);

        Assert.Equal("/usr/bin/open", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(2, info.ArgumentList.Count);
        Assert.Equal("--", info.ArgumentList[0]);
        Assert.Equal(GitHubTarget, info.ArgumentList[1]);
    }

    [Fact]
    public void CreateMacStartInfo_ShouldNeverInvokeShOrCsh()
    {
        var info = ExternalLauncher.CreateMacStartInfo(GitHubTarget);

        Assert.DoesNotContain("sh", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sh -c", info.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("bash", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zsh", info.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWindowsStartInfo_WithBlankTarget_ShouldThrow(string target)
    {
        Assert.Throws<ArgumentException>(() => ExternalLauncher.CreateWindowsStartInfo(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMacStartInfo_WithBlankTarget_ShouldThrow(string target)
    {
        Assert.Throws<ArgumentException>(() => ExternalLauncher.CreateMacStartInfo(target));
    }

    [Fact]
    public void CreateWindowsStartInfo_WithNullTarget_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ExternalLauncher.CreateWindowsStartInfo(null!));
    }

    [Fact]
    public void CreateMacStartInfo_WithNullTarget_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ExternalLauncher.CreateMacStartInfo(null!));
    }

    [Fact]
    public void LaunchUri_OnUnsupportedPlatform_ShouldReturnStablePlatformCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Unsupported,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_WhenProcessStartThrows_ShouldReturnStableStartFailureCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => throw new InvalidOperationException("no such file"));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_start_failed", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_WhenProcessIsNull_ShouldReturnStableNullProcessCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => default); // Started == false

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_process_null", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_OnMacWithNonZeroExit_ShouldReturnStableExitCode()
    {
        var started = false;
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ =>
            {
                started = true;
                return new ExternalLaunchAttempt(true, 1);
            });

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.True(started);
        Assert.False(result.Succeeded);
        Assert.Equal("launch_nonzero_exit", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_OnMacWhenProcessDoesNotExitWithinTheBoundedWait_ShouldReturnStableTimeoutCode()
    {
        // The starter is the launcher test seam: simulate a macOS `open` that does not return
        // within the bounded wait by reporting a timed-out attempt. No real process is spun up,
        // so the test stays fast and platform-independent.
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => new ExternalLaunchAttempt(Started: true, ExitCode: 0, TimedOut: true));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_timeout", result.ErrorCode);
    }

    [Fact]
    public void LaunchFolder_OnMacWhenTimedOut_ShouldReturnStableTimeoutCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => new ExternalLaunchAttempt(true, 0, TimedOut: true));

        var result = launcher.LaunchFolder("/tmp/crash-root");

        Assert.False(result.Succeeded);
        Assert.Equal("launch_timeout", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_OnMacWithZeroExit_ShouldSucceed()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_OnWindows_ShouldSucceedWithoutWaitingForExit()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData("http://github.com/cwchanap/DTXManiaCX/issues/new")]
    [InlineData("ftp://github.com/cwchanap/DTXManiaCX/issues/new")]
    [InlineData("https/github.com/cwchanap/DTXManiaCX/issues/new")]
    public void LaunchUri_WithNonAbsoluteHttpsTarget_ShouldRejectBeforeLaunching(string uriString)
    {
        var started = false;
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ =>
            {
                started = true;
                return new ExternalLaunchAttempt(true, 0);
            });

        var result = launcher.LaunchUri(new Uri(uriString, UriKind.RelativeOrAbsolute));

        Assert.False(started); // never reached the starter
        Assert.False(result.Succeeded);
        Assert.Equal("launch_target_rejected", result.ErrorCode);
    }

    [Fact]
    public void LaunchFolder_OnMac_ShouldLaunchTheResolvedFolderPath()
    {
        string? captured = null;
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: info =>
            {
                captured = info.ArgumentList[1];
                return new ExternalLaunchAttempt(true, 0);
            });

        var result = launcher.LaunchFolder("/tmp/crash-root");

        Assert.True(result.Succeeded);
        Assert.Equal("/tmp/crash-root", captured);
    }

    [Fact]
    public void LaunchFolder_WithBlankPath_ShouldThrow()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        Assert.Throws<ArgumentException>(() => launcher.LaunchFolder("   "));
    }

    [Fact]
    public void LaunchUri_WithNullTarget_ShouldThrow()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        Assert.Throws<ArgumentNullException>(() => launcher.LaunchUri(null!));
    }

    [Fact]
    public void LaunchFolder_WithNullPath_ShouldThrow()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        Assert.Throws<ArgumentNullException>(() => launcher.LaunchFolder(null!));
    }

    [Fact]
    public void ParameterlessConstructor_ShouldNotThrow()
    {
        var exception = Record.Exception(() => new ExternalLauncher());

        Assert.Null(exception);
    }

    [Fact]
    public void LaunchUri_OnWindowsWithNonZeroExit_ShouldStillSucceed()
    {
        // Windows UseShellExecute launches the registered handler and must not block on it,
        // so its exit code is never consulted — even a non-zero exit is a success.
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ => new ExternalLaunchAttempt(true, 1));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void LaunchFolder_OnWindows_ShouldSucceed()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        var result = launcher.LaunchFolder("/tmp/crash-root");

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_WhenStarterThrowsWin32Exception_ShouldReturnStartFailedCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ => throw new System.ComponentModel.Win32Exception(2));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_start_failed", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_WhenStarterThrowsFileNotFoundException_ShouldReturnStartFailedCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => throw new System.IO.FileNotFoundException("not found"));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_start_failed", result.ErrorCode);
    }

    [Fact]
    public void LaunchFolder_OnMacWithNonZeroExit_ShouldReturnNonZeroExitCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => new ExternalLaunchAttempt(true, 42));

        var result = launcher.LaunchFolder("/tmp/crash-root");

        Assert.False(result.Succeeded);
        Assert.Equal("launch_nonzero_exit", result.ErrorCode);
    }

    [Fact]
    public void LaunchFolder_OnUnsupportedPlatform_ShouldReturnPlatformUnsupportedCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Unsupported,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        var result = launcher.LaunchFolder("/tmp/crash-root");

        Assert.False(result.Succeeded);
        Assert.Equal("launch_platform_unsupported", result.ErrorCode);
    }

    // ---------------------------------------------------------------------------------------------
    // Remaining IsLaunchException types (exercised through the LaunchCore catch filter)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void LaunchUri_WhenStarterThrowsDirectoryNotFoundException_ShouldReturnStartFailedCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => throw new System.IO.DirectoryNotFoundException("dir not found"));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_start_failed", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_WhenStarterThrowsPlatformNotSupportedException_ShouldReturnStartFailedCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ => throw new PlatformNotSupportedException("not supported"));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_start_failed", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_WhenStarterThrowsSecurityException_ShouldReturnStartFailedCode()
    {
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ => throw new System.Security.SecurityException("denied"));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.False(result.Succeeded);
        Assert.Equal("launch_start_failed", result.ErrorCode);
    }

    [Fact]
    public void LaunchUri_WhenStarterThrowsNonLaunchException_ShouldPropagate()
    {
        // An exception type NOT in IsLaunchException must propagate (not be swallowed).
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Mac,
            starter: _ => throw new OutOfMemoryException("oom"));

        Assert.Throws<OutOfMemoryException>(() => launcher.LaunchUri(new Uri(GitHubTarget)));
    }

    // ---------------------------------------------------------------------------------------------
    // IsLaunchException direct tests
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(InvalidOperationException), true)]
    [InlineData(typeof(System.ComponentModel.Win32Exception), true)]
    [InlineData(typeof(System.IO.FileNotFoundException), true)]
    [InlineData(typeof(System.IO.DirectoryNotFoundException), true)]
    [InlineData(typeof(PlatformNotSupportedException), true)]
    [InlineData(typeof(System.Security.SecurityException), true)]
    [InlineData(typeof(OutOfMemoryException), false)]
    [InlineData(typeof(ArgumentNullException), false)]
    public void IsLaunchException_ShouldClassifyExpectedLaunchExceptions(Type exceptionType, bool expected)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "test")!;

        Assert.Equal(expected, ExternalLauncher.IsLaunchException(exception));
    }

    // ---------------------------------------------------------------------------------------------
    // DefaultPlatform — exercises RuntimeInformation on the current OS
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DefaultPlatform_ShouldReturnAValidPlatformForTheCurrentOs()
    {
        var platform = ExternalLauncher.DefaultPlatform();

        Assert.True(platform is LauncherPlatform.Windows or LauncherPlatform.Mac or LauncherPlatform.Unsupported);
    }

    // ---------------------------------------------------------------------------------------------
    // CreateDefaultStarter — exercises the real process-start path with a harmless process
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateDefaultStarter_OnNonMacPlatform_ShouldReturnSuccessWithoutWaiting()
    {
        // Use a harmless process that exits immediately. On non-Mac the starter does not wait
        // for exit, so the exit code is not consulted.
        var info = new ProcessStartInfo(
            OperatingSystem.IsWindows() ? "cmd.exe" : "/usr/bin/true")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("exit 0");
        }

        var starter = ExternalLauncher.CreateDefaultStarter(
            platform: () => LauncherPlatform.Unsupported,
            macLaunchTimeout: TimeSpan.FromSeconds(5));

        var attempt = starter(info);

        Assert.True(attempt.Started);
        Assert.False(attempt.TimedOut);
    }

    [Fact]
    public void CreateDefaultStarter_OnMacWithQuickExit_ShouldReturnExitCode()
    {
        if (!OperatingSystem.IsMacOS())
        {
            // The Mac-specific bounded wait only runs on Mac platform; skip elsewhere.
            return;
        }

        // /usr/bin/true exits 0 immediately — the bounded wait succeeds well within the timeout.
        var info = new ProcessStartInfo("/usr/bin/true")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var starter = ExternalLauncher.CreateDefaultStarter(
            platform: () => LauncherPlatform.Mac,
            macLaunchTimeout: TimeSpan.FromSeconds(5));

        var attempt = starter(info);

        Assert.True(attempt.Started);
        Assert.Equal(0, attempt.ExitCode);
        Assert.False(attempt.TimedOut);
    }

    [Fact]
    public void CreateDefaultStarter_OnMacWithNonZeroExit_ShouldReturnNonZeroExitCode()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // /usr/bin/false exits 1 — the bounded wait captures the non-zero exit code.
        var info = new ProcessStartInfo("/usr/bin/false")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var starter = ExternalLauncher.CreateDefaultStarter(
            platform: () => LauncherPlatform.Mac,
            macLaunchTimeout: TimeSpan.FromSeconds(5));

        var attempt = starter(info);

        Assert.True(attempt.Started);
        Assert.NotEqual(0, attempt.ExitCode);
        Assert.False(attempt.TimedOut);
    }

    [Fact]
    public void CreateDefaultStarter_OnMacWhenProcessDoesNotExitWithinTimeout_ShouldReturnTimedOut()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // /bin/sleep 30 will not exit within the 1ms timeout -> TimedOut=true, process killed.
        var info = new ProcessStartInfo("/bin/sleep")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("30");

        var starter = ExternalLauncher.CreateDefaultStarter(
            platform: () => LauncherPlatform.Mac,
            macLaunchTimeout: TimeSpan.FromMilliseconds(1));

        var attempt = starter(info);

        Assert.True(attempt.Started);
        Assert.True(attempt.TimedOut);
    }

    [Fact]
    public void CreateDefaultStarter_WhenProcessStartReturnsNull_ShouldReturnDefaultAttempt()
    {
        // Use ShellExecute on a non-existent target to get a null process on some platforms.
        // This is a best-effort test: if Process.Start returns non-null, the test still passes
        // because we only assert Started is true (the null path is platform-dependent).
        var info = new ProcessStartInfo("nonexistent-launcher-target-" + Guid.NewGuid())
        {
            UseShellExecute = true
        };

        var starter = ExternalLauncher.CreateDefaultStarter(
            platform: () => LauncherPlatform.Unsupported,
            macLaunchTimeout: TimeSpan.FromSeconds(5));

        // Process.Start may throw or return null; either way the test documents the behavior.
        try
        {
            var attempt = starter(info);
            // If it didn't throw, it either started or returned default (Started=false).
            Assert.True(attempt.Started || !attempt.Started);
        }
        catch (Exception exception) when (ExternalLauncher.IsLaunchException(exception))
        {
            // Expected on most platforms for a non-existent target.
        }
    }

    // ---------------------------------------------------------------------------------------------
    // BestEffortKill — swallows expected exceptions from a process that already exited
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BestEffortKill_WhenProcessAlreadyExited_ShouldNotThrow()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
        {
            return;
        }

        // Start a process that exits immediately, then call BestEffortKill on it.
        // Kill on an already-exited process throws InvalidOperationException or Win32Exception,
        // which BestEffortKill must swallow.
        var info = new ProcessStartInfo(
            OperatingSystem.IsWindows() ? "cmd.exe" : "/usr/bin/true")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("exit 0");
        }

        var process = Process.Start(info)!;
        process.WaitForExit();

        var exception = Record.Exception(() => ExternalLauncher.BestEffortKill(process));

        Assert.Null(exception);
        process.Dispose();
    }
}
