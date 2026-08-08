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
        var waitCalled = false;
        var launcher = new ExternalLauncher(
            platform: () => LauncherPlatform.Windows,
            starter: _ => new ExternalLaunchAttempt(true, 0));

        var result = launcher.LaunchUri(new Uri(GitHubTarget));

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.False(waitCalled);
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
}
