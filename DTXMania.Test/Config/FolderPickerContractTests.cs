#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Game.Platform;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class FolderPickerContractTests
{
    [Fact]
    public void SelectedResult_ShouldCarryTheSelectedFolderPath()
    {
        var result = FolderPickerResult.Selected("/tmp/songs");

        Assert.Equal(FolderPickerStatus.Selected, result.Status);
        Assert.Equal("/tmp/songs", result.Path);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData(FolderPickerStatus.Cancelled)]
    [InlineData(FolderPickerStatus.Unavailable)]
    [InlineData(FolderPickerStatus.Failed)]
    public void NonSelectedResult_ShouldNotExposeAUsablePath(FolderPickerStatus status)
    {
        var result = new FolderPickerResult(status, message: "test message");

        Assert.Null(result.Path);
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void SelectedResult_WithoutAPath_ShouldBeRejected()
    {
        Assert.Throws<ArgumentException>(() => new FolderPickerResult(FolderPickerStatus.Selected));
    }

    [Fact]
    public void NonSelectedResult_WithAPath_ShouldBeRejected()
    {
        Assert.Throws<ArgumentException>(() => new FolderPickerResult(
            FolderPickerStatus.Cancelled,
            path: "/tmp/not-a-selection"));
    }

    [Fact]
    public async Task MacPicker_WhenCancellationWasRequestedBeforeStart_ShouldReturnCancelledWithoutLaunchingProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var picker = new MacFolderPickerService();

        var result = await picker.PickFolderAsync(null, cancellation.Token);

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public void MacPicker_WhenAppleScriptReportsUserCancellation_ShouldMapToCancelled()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: string.Empty,
            standardError: "User canceled. (-128)");

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public void MacPicker_WhenAppleScriptReportsAuthorizationDenied_ShouldMapToFailed()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: string.Empty,
            standardError: "Not authorized to send Apple events to System Events. (-1743)");

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void PlatformFactory_ShouldProduceTheMacPickerForTheMacBuild()
    {
        Assert.IsType<MacFolderPickerService>(FolderPickerServiceFactory.Create());
    }
}
