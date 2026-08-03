#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Game.Platform;
using DTXMania.Test.TestData;

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
        await firstDialog.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

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

    [Fact]
    public async Task StaDispatcher_WhenInitializationFails_ShouldRejectSubsequentRequestsAfterThreadStops()
    {
        var factory = new InitializationFailingFolderPickerDialogFactory();
        using var picker = new StaFolderPickerDispatcher(factory);

        await factory.InitializationAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var dispatcherThread = ReflectionHelpers.GetPrivateField<Thread>(picker, "_dispatcherThread");
        Assert.NotNull(dispatcherThread);
        Assert.True(dispatcherThread!.Join(TimeSpan.FromSeconds(1)));

        var result = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task StaDispatcher_WhenDialogDisposalFails_ShouldContinueServingSubsequentRequests()
    {
        var firstDialog = new DisposeThrowingFolderPickerDialog(
            FolderPickerResult.Selected("/tmp/first-songs"));
        var secondDialog = new ImmediateFolderPickerDialog(
            FolderPickerResult.Selected("/tmp/second-songs"));
        var factory = new QueuedFolderPickerDialogFactory(firstDialog, secondDialog);
        using var picker = new StaFolderPickerDispatcher(factory);

        var firstResult = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));
        await firstDialog.DisposeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondResult = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Selected, firstResult.Status);
        Assert.Equal("/tmp/first-songs", firstResult.Path);
        Assert.Equal(FolderPickerStatus.Selected, secondResult.Status);
        Assert.Equal("/tmp/second-songs", secondResult.Path);
    }

    [Fact]
    public async Task StaDispatcher_WhenAlreadyCancelledBeforeEnqueue_ShouldReturnCancelledImmediately()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new QueuedFolderPickerDialogFactory(
            new ImmediateFolderPickerDialog(FolderPickerResult.Selected("/tmp/songs")));
        using var picker = new StaFolderPickerDispatcher(factory);

        var result = await picker.PickFolderAsync(null, cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task StaDispatcher_WhenRequestArrivesAfterDisposal_ShouldReturnUnavailable()
    {
        var factory = new QueuedFolderPickerDialogFactory(
            new ImmediateFolderPickerDialog(FolderPickerResult.Selected("/tmp/songs")));
        var picker = new StaFolderPickerDispatcher(factory);
        picker.Dispose();

        var result = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task StaDispatcher_WhenCloseOnDispatcherThrows_ShouldStillReportCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var firstDialog = new BlockingFolderPickerDialog();
        var factory = new CloseThrowingFolderPickerDialogFactory(firstDialog);
        using var picker = new StaFolderPickerDispatcher(factory);

        var request = picker.PickFolderAsync(null, cancellation.Token);
        await firstDialog.Opened.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();

        var result = await request.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
        Assert.Equal(1, factory.CloseRequests);
    }

    [Fact]
    public async Task StaDispatcher_WhenCancellationTargetsARequestThatIsNotActive_ShouldCompleteItAsCancelled()
    {
        // Queue two requests against a blocking first dialog. Cancelling the second
        // request while the first is still open must complete the second as Cancelled
        // even though it is not the active dialog (CancelRequest's null-active branch).
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var firstDialog = new BlockingFolderPickerDialog();
        var secondDialog = new ImmediateFolderPickerDialog(FolderPickerResult.Selected("/tmp/second"));
        var factory = new QueuedFolderPickerDialogFactory(firstDialog, secondDialog);
        using var picker = new StaFolderPickerDispatcher(factory);

        var firstRequest = picker.PickFolderAsync(null, firstCancellation.Token);
        await firstDialog.Opened.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var secondRequest = picker.PickFolderAsync(null, secondCancellation.Token);

        secondCancellation.Cancel();
        var secondResult = await secondRequest.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FolderPickerStatus.Cancelled, secondResult.Status);

        // The first request is still open; release and assert it completes.
        firstCancellation.Cancel();
        var firstResult = await firstRequest.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FolderPickerStatus.Cancelled, firstResult.Status);
    }

    [Fact]
    public void StaDispatcher_DisposeCalledTwice_ShouldBeIdempotent()
    {
        var factory = new QueuedFolderPickerDialogFactory(
            new ImmediateFolderPickerDialog(FolderPickerResult.Selected("/tmp/songs")));
        var picker = new StaFolderPickerDispatcher(factory);

        picker.Dispose();
        // A second dispose must not throw and must remain a no-op.
        picker.Dispose();
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

    private sealed class InitializationFailingFolderPickerDialogFactory : IStaFolderPickerDialogFactory
    {
        internal TaskCompletionSource<bool> InitializationAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void InitializeDispatcherThread()
        {
            InitializationAttempted.TrySetResult(true);
            throw new InvalidOperationException("The dispatcher could not initialize.");
        }

        public IStaFolderPickerDialog CreateDialog() =>
            throw new InvalidOperationException("The dispatcher did not initialize.");

        public void CloseOnDispatcher(IStaFolderPickerDialog dialog)
        {
        }
    }

    private sealed class DisposeThrowingFolderPickerDialog : IStaFolderPickerDialog
    {
        private readonly FolderPickerResult _result;

        internal DisposeThrowingFolderPickerDialog(FolderPickerResult result)
        {
            _result = result;
        }

        internal TaskCompletionSource<bool> DisposeAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FolderPickerResult Show(string? initialDirectory) => _result;

        public void Close()
        {
        }

        public void Dispose()
        {
            DisposeAttempted.TrySetResult(true);
            throw new InvalidOperationException("The native dialog could not be disposed.");
        }
    }

    private sealed class CloseThrowingFolderPickerDialogFactory : IStaFolderPickerDialogFactory
    {
        private readonly IStaFolderPickerDialog _dialog;

        internal CloseThrowingFolderPickerDialogFactory(IStaFolderPickerDialog dialog)
        {
            _dialog = dialog;
        }

        internal int CloseRequests { get; private set; }

        public void InitializeDispatcherThread()
        {
        }

        public IStaFolderPickerDialog CreateDialog() => _dialog;

        public void CloseOnDispatcher(IStaFolderPickerDialog dialog)
        {
            CloseRequests++;
            throw new InvalidOperationException("The platform close marshalling failed.");
        }
    }
}
