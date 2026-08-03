#nullable enable

using System;
using System.Collections.Generic;
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

    [Fact]
    public async Task StaDispatcher_WhenCancellationOccursWhileDialogIsOpen_ShouldCloseItAndServeTheNextRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var firstDialog = new BlockingFolderPickerDialog();
        var secondDialog = new ImmediateFolderPickerDialog(FolderPickerResult.Selected("/tmp/next-songs"));
        var factory = new QueuedFolderPickerDialogFactory(firstDialog, secondDialog);
        using var picker = new StaFolderPickerDispatcher(factory);

        var firstRequest = picker.PickFolderAsync(null, cancellation.Token);
        await firstDialog.Opened.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();

        var firstResult = await firstRequest.WaitAsync(TimeSpan.FromSeconds(1));
        var secondResult = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Cancelled, firstResult.Status);
        Assert.Equal(FolderPickerStatus.Selected, secondResult.Status);
        Assert.Equal("/tmp/next-songs", secondResult.Path);
        Assert.Equal(1, factory.CloseRequests);
        Assert.Equal(1, firstDialog.CloseRequests);
    }

    private sealed class QueuedFolderPickerDialogFactory : IStaFolderPickerDialogFactory
    {
        private readonly Queue<IStaFolderPickerDialog> _dialogs;

        internal QueuedFolderPickerDialogFactory(params IStaFolderPickerDialog[] dialogs)
        {
            _dialogs = new Queue<IStaFolderPickerDialog>(dialogs);
        }

        internal int CloseRequests { get; private set; }

        public void InitializeDispatcherThread()
        {
        }

        public IStaFolderPickerDialog CreateDialog() => _dialogs.Dequeue();

        public void CloseOnDispatcher(IStaFolderPickerDialog dialog)
        {
            CloseRequests++;
            dialog.Close();
        }
    }

    private sealed class BlockingFolderPickerDialog : IStaFolderPickerDialog
    {
        private readonly ManualResetEventSlim _closed = new(false);

        internal TaskCompletionSource<bool> Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int CloseRequests { get; private set; }

        public FolderPickerResult Show(string? initialDirectory)
        {
            Opened.TrySetResult(true);
            if (!_closed.Wait(TimeSpan.FromSeconds(3)))
                throw new TimeoutException("The test dialog was not closed after cancellation.");

            return FolderPickerResult.Cancelled();
        }

        public void Close()
        {
            CloseRequests++;
            _closed.Set();
        }

        public void Dispose() => _closed.Dispose();
    }

    private sealed class ImmediateFolderPickerDialog : IStaFolderPickerDialog
    {
        private readonly FolderPickerResult _result;

        internal ImmediateFolderPickerDialog(FolderPickerResult result)
        {
            _result = result;
        }

        public FolderPickerResult Show(string? initialDirectory) => _result;

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
