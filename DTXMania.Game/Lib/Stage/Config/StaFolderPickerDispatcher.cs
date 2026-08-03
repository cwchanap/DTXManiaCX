#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Stage.Config
{
    /// <summary>
    /// Represents one blocking native picker dialog. Implementations must make
    /// <see cref="Close"/> safe to call from cancellation handling while
    /// <see cref="Show"/> is still blocking on the owned UI thread.
    /// </summary>
    internal interface IStaFolderPickerDialog : IDisposable
    {
        FolderPickerResult Show(string? initialDirectory);

        void Close();
    }

    /// <summary>
    /// Creates and closes dialogs for <see cref="StaFolderPickerDispatcher"/>.
    /// The platform factory owns any required marshalling when a cancellation
    /// requests that its active dialog be closed.
    /// </summary>
    internal interface IStaFolderPickerDialogFactory
    {
        void InitializeDispatcherThread();

        IStaFolderPickerDialog CreateDialog();

        void CloseOnDispatcher(IStaFolderPickerDialog dialog);
    }

    /// <summary>
    /// Serializes blocking native dialogs on one owned thread. This class is
    /// platform-neutral so its cancellation/queue contract can be verified
    /// without opening a real operating-system dialog.
    /// </summary>
    internal sealed class StaFolderPickerDispatcher : IFolderPickerService, IDisposable
    {
        private readonly IStaFolderPickerDialogFactory _dialogFactory;
        private readonly BlockingCollection<PickerRequest> _requests = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Thread _dispatcherThread;
        private readonly object _requestGate = new();
        private readonly object _activeDialogLock = new();
        private PickerRequest? _activeRequest;
        private IStaFolderPickerDialog? _activeDialog;
        private FolderPickerResult? _terminalResult;
        private int _disposed;

        internal StaFolderPickerDispatcher(
            IStaFolderPickerDialogFactory dialogFactory,
            Action<Thread>? configureThread = null)
        {
            _dialogFactory = dialogFactory ?? throw new ArgumentNullException(nameof(dialogFactory));
            _dispatcherThread = new Thread(DispatchRequests)
            {
                IsBackground = true,
                Name = "DTXMania Folder Picker STA",
            };
            configureThread?.Invoke(_dispatcherThread);
            _dispatcherThread.Start();
        }

        internal ApartmentState DispatcherApartmentState => _dispatcherThread.GetApartmentState();

        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(FolderPickerResult.Cancelled());

            var request = new PickerRequest(initialDirectory, cancellationToken, CancelRequest);
            lock (_requestGate)
            {
                if (_terminalResult != null || Volatile.Read(ref _disposed) != 0)
                {
                    request.Complete(_terminalResult ?? FolderPickerResult.Unavailable(
                        "The folder picker is no longer available."));
                }
                else
                {
                    try
                    {
                        _requests.Add(request);
                    }
                    catch (InvalidOperationException)
                    {
                        request.Complete(_terminalResult ?? FolderPickerResult.Unavailable(
                            "The folder picker is no longer available."));
                    }
                }
            }

            return request.Task;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            TransitionToTerminal(FolderPickerResult.Unavailable(
                "The folder picker is shutting down."));
            _shutdown.Cancel();
            StopActiveDialog();

            if (!ReferenceEquals(Thread.CurrentThread, _dispatcherThread) &&
                _dispatcherThread.Join(TimeSpan.FromSeconds(1)))
            {
                _requests.Dispose();
                _shutdown.Dispose();
            }
        }

        private void DispatchRequests()
        {
            FolderPickerResult? terminalResult = null;
            try
            {
                _dialogFactory.InitializeDispatcherThread();

                foreach (var request in _requests.GetConsumingEnumerable(_shutdown.Token))
                {
                    if (request.IsCompleted || request.CancellationToken.IsCancellationRequested)
                    {
                        request.Complete(FolderPickerResult.Cancelled());
                        continue;
                    }

                    ShowRequest(request);
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose requested while the dispatcher was idle.
                terminalResult = FolderPickerResult.Unavailable(
                    "The folder picker is shutting down.");
            }
            catch (Exception exception)
            {
                terminalResult = FolderPickerResult.Unavailable(exception.Message);
            }
            finally
            {
                TransitionToTerminal(terminalResult ?? FolderPickerResult.Unavailable(
                    "The folder picker is shutting down."));
            }
        }

        private void ShowRequest(PickerRequest request)
        {
            IStaFolderPickerDialog? dialog = null;
            try
            {
                dialog = _dialogFactory.CreateDialog();
                lock (_activeDialogLock)
                {
                    if (request.IsCompleted)
                        return;

                    _activeRequest = request;
                    _activeDialog = dialog;
                }

                if (request.IsCompleted)
                    return;

                request.Complete(dialog.Show(request.InitialDirectory));
            }
            catch (Exception exception)
            {
                request.Complete(FolderPickerResult.Failed(exception.Message));
            }
            finally
            {
                lock (_activeDialogLock)
                {
                    if (ReferenceEquals(_activeRequest, request))
                    {
                        _activeRequest = null;
                        _activeDialog = null;
                    }
                }

                if (dialog != null)
                {
                    try
                    {
                        dialog.Dispose();
                    }
                    catch (Exception exception)
                    {
                        request.Complete(FolderPickerResult.Failed(exception.Message));
                    }
                }
            }
        }

        private void CancelRequest(PickerRequest request)
        {
            request.Complete(FolderPickerResult.Cancelled());

            IStaFolderPickerDialog? activeDialog;
            lock (_activeDialogLock)
            {
                activeDialog = ReferenceEquals(_activeRequest, request)
                    ? _activeDialog
                    : null;
            }

            if (activeDialog == null)
                return;

            try
            {
                _dialogFactory.CloseOnDispatcher(activeDialog);
            }
            catch
            {
                // The request is already cancelled. A platform shutdown race
                // must not surface from a CancellationToken callback.
            }
        }

        private void StopActiveDialog()
        {
            PickerRequest? activeRequest;
            IStaFolderPickerDialog? activeDialog;
            lock (_activeDialogLock)
            {
                activeRequest = _activeRequest;
                activeDialog = _activeDialog;
            }

            activeRequest?.Complete(FolderPickerResult.Unavailable(
                "The folder picker is shutting down."));

            if (activeDialog == null)
                return;

            try
            {
                _dialogFactory.CloseOnDispatcher(activeDialog);
            }
            catch
            {
                // Best-effort shutdown. Do not throw from IDisposable.Dispose.
            }
        }

        private void DrainPendingRequests(FolderPickerResult result)
        {
            while (_requests.TryTake(out var request))
                request.Complete(result);
        }

        private void TransitionToTerminal(FolderPickerResult result)
        {
            FolderPickerResult terminalResult;
            lock (_requestGate)
            {
                _terminalResult ??= result;
                terminalResult = _terminalResult;
                _requests.CompleteAdding();
            }

            DrainPendingRequests(terminalResult);
        }

        private sealed class PickerRequest
        {
            private readonly TaskCompletionSource<FolderPickerResult> _completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Action<PickerRequest> _cancelRequest;
            private CancellationTokenRegistration _cancellationRegistration;

            internal PickerRequest(
                string? initialDirectory,
                CancellationToken cancellationToken,
                Action<PickerRequest> cancelRequest)
            {
                InitialDirectory = initialDirectory;
                CancellationToken = cancellationToken;
                _cancelRequest = cancelRequest;
                _cancellationRegistration = cancellationToken.Register(
                    static state =>
                    {
                        var request = (PickerRequest)state!;
                        request._cancelRequest(request);
                    },
                    this);
                if (_completion.Task.IsCompleted)
                    _cancellationRegistration.Dispose();
            }

            internal string? InitialDirectory { get; }

            internal CancellationToken CancellationToken { get; }

            internal Task<FolderPickerResult> Task => _completion.Task;

            internal bool IsCompleted => _completion.Task.IsCompleted;

            internal void Complete(FolderPickerResult result)
            {
                if (_completion.TrySetResult(result))
                    _cancellationRegistration.Dispose();
            }
        }
    }
}
