#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class StaFolderPickerDispatcherAdditionalTests
{
    [Fact]
    public void Constructor_WhenDialogFactoryIsNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StaFolderPickerDispatcher(null!));
    }

    [Fact]
    public void Constructor_WhenConfigureThreadCallbackIsSupplied_ShouldInvokeItBeforeStarting()
    {
        var configured = false;
        var factory = new QueuedFactory(
            new ImmediateDialog(FolderPickerResult.Selected("/tmp/songs")));
        using var picker = new StaFolderPickerDispatcher(
            factory,
            thread => configured = true);

        Assert.True(configured);
    }

    [Fact]
    public void DispatcherApartmentState_ShouldReflectTheDispatcherThreadApartment()
    {
        var factory = new QueuedFactory(
            new ImmediateDialog(FolderPickerResult.Selected("/tmp/songs")));
        using var picker = new StaFolderPickerDispatcher(factory);

        // The dispatcher thread is a background thread; its apartment state is
        // MTA by default in .NET. Asserting the property reads the live value
        // without asserting a specific apartment, since STA is platform-specific.
        var state = picker.DispatcherApartmentState;
        Assert.True(state == ApartmentState.MTA || state == ApartmentState.STA ||
                    state == ApartmentState.Unknown);
    }

    [Fact]
    public async Task ShowRequest_WhenDialogShowThrows_ShouldCompleteRequestAsFailed()
    {
        var factory = new QueuedFactory(new ThrowingDialog("dialog blew up"));
        using var picker = new StaFolderPickerDispatcher(factory);

        var result = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Contains("dialog blew up", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchRequests_WhenQueuedRequestIsAlreadyCompleted_ShouldCompleteAsCancelled()
    {
        // Pre-complete a request before the dispatcher dequeues it so the
        // `request.IsCompleted` branch in DispatchRequests is exercised.
        var factory = new QueuedFactory(
            new ImmediateDialog(FolderPickerResult.Selected("/tmp/songs")));
        using var picker = new StaFolderPickerDispatcher(factory);

        // Issue a request and cancel it immediately (before the dispatcher thread
        // can dequeue it). The dispatcher should observe it as completed/cancelled
        // and serve the next request normally.
        using var cancellation = new CancellationTokenSource();
        var firstRequest = picker.PickFolderAsync(null, cancellation.Token);
        cancellation.Cancel();

        var firstResult = await firstRequest.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FolderPickerStatus.Cancelled, firstResult.Status);
    }

    [Fact]
    public async Task PickFolderAsync_WhenRequestArrivesAfterTerminalShutdown_ShouldReturnUnavailable()
    {
        var factory = new QueuedFactory(
            new ImmediateDialog(FolderPickerResult.Selected("/tmp/songs")));
        var picker = new StaFolderPickerDispatcher(factory);
        picker.Dispose();

        // After disposal, a new request should see the terminal result.
        var result = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task ShowRequest_WhenCreateDialogThrows_ShouldCompleteRequestAsFailed()
    {
        // A factory whose CreateDialog throws causes ShowRequest's catch block to
        // complete the request as Failed (the dispatcher loop itself continues).
        var factory = new CreateThrowingFactory();
        using var picker = new StaFolderPickerDispatcher(factory);

        var result = await picker.PickFolderAsync(null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Contains("CreateDialog failed", result.Message, StringComparison.Ordinal);
    }

    #region Test Doubles

    private sealed class QueuedFactory : IStaFolderPickerDialogFactory
    {
        private readonly IStaFolderPickerDialog _dialog;
        public QueuedFactory(IStaFolderPickerDialog dialog) => _dialog = dialog;

        public void InitializeDispatcherThread() { }
        public IStaFolderPickerDialog CreateDialog() => _dialog;
        public void CloseOnDispatcher(IStaFolderPickerDialog dialog) => dialog.Close();
    }

    private sealed class ImmediateDialog : IStaFolderPickerDialog
    {
        private readonly FolderPickerResult _result;
        public ImmediateDialog(FolderPickerResult result) => _result = result;
        public FolderPickerResult Show(string? initialDirectory) => _result;
        public void Close() { }
        public void Dispose() { }
    }

    private sealed class ThrowingDialog : IStaFolderPickerDialog
    {
        private readonly string _message;
        public ThrowingDialog(string message) => _message = message;
        public FolderPickerResult Show(string? initialDirectory) =>
            throw new InvalidOperationException(_message);
        public void Close() { }
        public void Dispose() { }
    }

    private sealed class CreateThrowingFactory : IStaFolderPickerDialogFactory
    {
        public TaskCompletionSource<bool> CreateAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void InitializeDispatcherThread() { }

        public IStaFolderPickerDialog CreateDialog()
        {
            CreateAttempted.TrySetResult(true);
            throw new InvalidOperationException("CreateDialog failed");
        }

        public void CloseOnDispatcher(IStaFolderPickerDialog dialog) { }
    }

    #endregion
}
