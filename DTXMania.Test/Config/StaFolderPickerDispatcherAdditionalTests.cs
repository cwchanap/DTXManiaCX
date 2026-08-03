#nullable enable

using System;
using System.Reflection;
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

        // Compare the property against the dispatcher thread's actual apartment
        // state, retrieved via reflection, instead of asserting a tautological
        // membership check.
        var dispatcherThread = (Thread)typeof(StaFolderPickerDispatcher)
            .GetField("_dispatcherThread", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(picker)!;
        Assert.Equal(dispatcherThread.GetApartmentState(), picker.DispatcherApartmentState);
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
        // A preceding blocking request keeps the dispatcher busy so the target
        // request sits in the queue and is cancelled before the dispatcher
        // dequeues it. This deterministically exercises the `request.IsCompleted`
        // branch in DispatchRequests rather than relying on a race between
        // cancellation and dequeue.
        var blockingDialog = new BlockingDialog();
        var factory = new QueuedFactory(blockingDialog);
        using var picker = new StaFolderPickerDispatcher(factory);

        // Start the blocking request and wait for the dispatcher to enter Show().
        var firstRequest = picker.PickFolderAsync(null, CancellationToken.None);
        await blockingDialog.ShowEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Enqueue the target request and cancel it while the dispatcher is busy.
        using var cancellation = new CancellationTokenSource();
        var secondRequest = picker.PickFolderAsync(null, cancellation.Token);
        cancellation.Cancel();

        // Release the blocking dialog so the dispatcher dequeues the target.
        blockingDialog.Release();
        var firstResult = await firstRequest.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FolderPickerStatus.Selected, firstResult.Status);

        var secondResult = await secondRequest.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FolderPickerStatus.Cancelled, secondResult.Status);
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

    private sealed class BlockingDialog : IStaFolderPickerDialog
    {
        private readonly TaskCompletionSource<FolderPickerResult> _release = new();
        public TaskCompletionSource<bool> ShowEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FolderPickerResult Show(string? initialDirectory)
        {
            ShowEntered.TrySetResult(true);
            return _release.Task.GetAwaiter().GetResult();
        }

        public void Release() => _release.TrySetResult(
            FolderPickerResult.Selected("/tmp/songs"));
        public void Close() => _release.TrySetResult(FolderPickerResult.Cancelled());
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
