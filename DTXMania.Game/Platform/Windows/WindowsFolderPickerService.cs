#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Config;

namespace DTXMania.Game.Platform
{
    /// <summary>
    /// Windows folder picker running all native dialog work on an owned STA
    /// dispatcher thread. Config's update path never needs to assume STA.
    /// </summary>
    internal sealed class WindowsFolderPickerService : IFolderPickerService, IDisposable
    {
        private readonly StaFolderPickerDispatcher _dispatcher;

        public WindowsFolderPickerService()
            : this(new WindowsFolderPickerDialogFactory())
        {
        }

        internal WindowsFolderPickerService(IStaFolderPickerDialogFactory dialogFactory)
        {
            _dispatcher = new StaFolderPickerDispatcher(
                dialogFactory,
                static thread => thread.SetApartmentState(ApartmentState.STA));
        }

        internal ApartmentState DispatcherApartmentState => _dispatcher.DispatcherApartmentState;

        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken) =>
            _dispatcher.PickFolderAsync(initialDirectory, cancellationToken);

        public void Dispose() => _dispatcher.Dispose();

        private sealed class WindowsFolderPickerDialogFactory : IStaFolderPickerDialogFactory
        {
            public void InitializeDispatcherThread()
            {
            }

            public IStaFolderPickerDialog CreateDialog() => new WindowsFolderPickerDialog();

            public void CloseOnDispatcher(IStaFolderPickerDialog dialog)
            {
                // WindowsFolderPickerDialog posts WM_CLOSE to the native window;
                // Windows dispatches that message on the dialog's STA thread.
                dialog.Close();
            }
        }

        /// <summary>
        /// A closeable native SHBrowseForFolder dialog. FolderBrowserDialog does
        /// not expose its modal window handle, so its ShowDialog call cannot be
        /// released deterministically after cancellation. The callback here
        /// captures that native handle and posts WM_CLOSE to its owning STA.
        /// </summary>
        private sealed class WindowsFolderPickerDialog : IStaFolderPickerDialog
        {
            private const uint BifReturnOnlyFileSystemDirectories = 0x0001;
            private const uint BifEditBox = 0x0010;
            private const uint BifNewDialogStyle = 0x0040;
            private const uint BffmInitialized = 1;
            private const uint BffmSetSelectionW = 0x0400 + 103;
            private const uint WmClose = 0x0010;
            private const int MaxPathLength = 260;

            private readonly object _dialogLock = new();
            private IntPtr _dialogHandle;
            private bool _closeRequested;
            private string? _initialDirectory;

            public FolderPickerResult Show(string? initialDirectory)
            {
                try
                {
                    _initialDirectory = !string.IsNullOrWhiteSpace(initialDirectory) &&
                        Directory.Exists(initialDirectory)
                        ? initialDirectory
                        : null;

                    var callback = new BrowseCallback(HandleBrowseCallback);
                    var displayName = Marshal.AllocHGlobal((MaxPathLength + 1) * sizeof(char));
                    var handle = GCHandle.Alloc(this);
                    try
                    {
                        var browseInfo = new BrowseInfo
                        {
                            DisplayName = displayName,
                            Title = "Choose song folder",
                            Flags = BifReturnOnlyFileSystemDirectories |
                                BifEditBox |
                                BifNewDialogStyle,
                            Callback = callback,
                            CallbackData = GCHandle.ToIntPtr(handle),
                        };

                        var itemIdList = SHBrowseForFolder(ref browseInfo);
                        if (itemIdList == IntPtr.Zero)
                            return FolderPickerResult.Cancelled();

                        try
                        {
                            var path = new char[MaxPathLength + 1];
                            return SHGetPathFromIDList(itemIdList, path)
                                ? FolderPickerResult.Selected(new string(path).TrimEnd('\0'))
                                : FolderPickerResult.Failed(
                                    "The selected folder path could not be extracted from the picker result.");
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(itemIdList);
                        }
                    }
                    finally
                    {
                        handle.Free();
                        Marshal.FreeHGlobal(displayName);
                        lock (_dialogLock)
                            _dialogHandle = IntPtr.Zero;
                    }
                }
                catch (Exception exception)
                {
                    return FolderPickerResult.Failed(exception.Message);
                }
            }

            public void Close()
            {
                lock (_dialogLock)
                {
                    _closeRequested = true;
                    PostCloseMessage(_dialogHandle);
                }
            }

            public void Dispose() => Close();

            private int HandleBrowseCallback(
                IntPtr windowHandle,
                uint message,
                IntPtr lParam,
                IntPtr callbackData)
            {
                if (message != BffmInitialized)
                    return 0;

                lock (_dialogLock)
                {
                    _dialogHandle = windowHandle;
                    if (_closeRequested)
                    {
                        PostCloseMessage(windowHandle);
                    }
                    else if (!string.IsNullOrWhiteSpace(_initialDirectory))
                    {
                        var path = Marshal.StringToHGlobalUni(_initialDirectory);
                        try
                        {
                            SendMessage(windowHandle, BffmSetSelectionW, (IntPtr)1, path);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(path);
                        }
                    }
                }

                return 0;
            }

            private static void PostCloseMessage(IntPtr windowHandle)
            {
                if (windowHandle != IntPtr.Zero)
                    PostMessage(windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct BrowseInfo
            {
                internal IntPtr OwnerWindow;
                internal IntPtr RootItemIdList;
                internal IntPtr DisplayName;
                [MarshalAs(UnmanagedType.LPWStr)]
                internal string? Title;
                internal uint Flags;
                internal BrowseCallback? Callback;
                internal IntPtr CallbackData;
                internal int ImageIndex;
            }

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int BrowseCallback(
                IntPtr windowHandle,
                uint message,
                IntPtr lParam,
                IntPtr callbackData);

            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SHGetPathFromIDList(IntPtr itemIdList, [Out] char[] path);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SendMessage(
                IntPtr windowHandle,
                uint message,
                IntPtr wParam,
                IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool PostMessage(
                IntPtr windowHandle,
                uint message,
                IntPtr wParam,
                IntPtr lParam);
        }
    }
}
