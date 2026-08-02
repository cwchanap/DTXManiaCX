#nullable enable

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DTXMania.Game.Lib.Stage.Config;

namespace DTXMania.Game.Platform
{
    /// <summary>
    /// Windows folder picker running all WinForms dialog work on an owned STA
    /// dispatcher thread. Config's update path never needs to assume STA.
    /// </summary>
    internal sealed class WindowsFolderPickerService : IFolderPickerService, IDisposable
    {
        private readonly BlockingCollection<PickerRequest> _requests = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Thread _dispatcherThread;
        private int _disposed;

        public WindowsFolderPickerService()
        {
            _dispatcherThread = new Thread(DispatchRequests)
            {
                IsBackground = true,
                Name = "DTXMania Folder Picker STA",
            };
            _dispatcherThread.SetApartmentState(ApartmentState.STA);
            _dispatcherThread.Start();
        }

        internal ApartmentState DispatcherApartmentState => _dispatcherThread.GetApartmentState();

        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(FolderPickerResult.Cancelled());

            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.FromResult(FolderPickerResult.Unavailable(
                    "The Windows folder picker is no longer available."));
            }

            var request = new PickerRequest(initialDirectory, cancellationToken);
            try
            {
                _requests.Add(request, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                request.Complete(cancellationToken.IsCancellationRequested
                    ? FolderPickerResult.Cancelled()
                    : FolderPickerResult.Unavailable("The Windows folder picker is shutting down."));
            }
            catch (InvalidOperationException)
            {
                request.Complete(FolderPickerResult.Unavailable(
                    "The Windows folder picker is no longer available."));
            }

            return request.Task;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _requests.CompleteAdding();
            _shutdown.Cancel();
            if (!ReferenceEquals(Thread.CurrentThread, _dispatcherThread) &&
                _dispatcherThread.Join(TimeSpan.FromSeconds(1)))
            {
                _requests.Dispose();
                _shutdown.Dispose();
            }
        }

        private void DispatchRequests()
        {
            try
            {
                foreach (var request in _requests.GetConsumingEnumerable(_shutdown.Token))
                {
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        request.Complete(FolderPickerResult.Cancelled());
                        continue;
                    }

                    request.Complete(ShowDialog(request.InitialDirectory, request.CancellationToken));
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose requested while the dispatcher was idle.
            }
            finally
            {
                while (_requests.TryTake(out var request))
                {
                    request.Complete(FolderPickerResult.Unavailable(
                        "The Windows folder picker is shutting down."));
                }
            }
        }

        private static FolderPickerResult ShowDialog(
            string? initialDirectory,
            CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return FolderPickerResult.Cancelled();

                using var dialog = new FolderBrowserDialog
                {
                    Description = "Choose song folder",
                    UseDescriptionForTitle = true,
                };
                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                    dialog.SelectedPath = initialDirectory;

                var result = dialog.ShowDialog();
                if (cancellationToken.IsCancellationRequested ||
                    result != DialogResult.OK ||
                    string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    return FolderPickerResult.Cancelled();
                }

                return FolderPickerResult.Selected(dialog.SelectedPath);
            }
            catch (Win32Exception exception)
            {
                return FolderPickerResult.Unavailable(exception.Message);
            }
            catch (Exception exception)
            {
                return FolderPickerResult.Failed(exception.Message);
            }
        }

        private sealed class PickerRequest
        {
            private readonly TaskCompletionSource<FolderPickerResult> _completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private CancellationTokenRegistration _cancellationRegistration;

            internal PickerRequest(string? initialDirectory, CancellationToken cancellationToken)
            {
                InitialDirectory = initialDirectory;
                CancellationToken = cancellationToken;
                _cancellationRegistration = cancellationToken.Register(
                    static state => ((PickerRequest)state!).Complete(FolderPickerResult.Cancelled()),
                    this);
                if (_completion.Task.IsCompleted)
                    _cancellationRegistration.Dispose();
            }

            internal string? InitialDirectory { get; }

            internal CancellationToken CancellationToken { get; }

            internal Task<FolderPickerResult> Task => _completion.Task;

            internal void Complete(FolderPickerResult result)
            {
                if (_completion.TrySetResult(result))
                    _cancellationRegistration.Dispose();
            }
        }
    }
}
