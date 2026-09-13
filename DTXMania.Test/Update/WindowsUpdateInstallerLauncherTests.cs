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
    public void Launch_WhenProcessStarts_ShouldReturnTrue()
    {
        var launcher = new WindowsUpdateInstallerLauncher(_ => new Process());

        Assert.True(launcher.Launch(InstallerPath));
    }

    [Theory]
    [InlineData(740)]  // ERROR_ELEVATION_REQUIRED
    [InlineData(1223)] // ERROR_CANCELLED — the Task-0 UAC-cancel flow
    public void Launch_WhenElevationRefused_ShouldReturnFalseWithoutThrowing(int win32Error)
    {
        var launcher = new WindowsUpdateInstallerLauncher(_ => throw new Win32Exception(win32Error));

        Assert.False(launcher.Launch(InstallerPath));
    }

    [Fact]
    public void Launch_WhenStartReportsNoProcess_ShouldReturnFalse()
    {
        var launcher = new WindowsUpdateInstallerLauncher(_ => null);

        Assert.False(launcher.Launch(InstallerPath));
    }
}
