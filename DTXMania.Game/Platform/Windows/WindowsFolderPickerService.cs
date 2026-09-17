#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
            : this(IntPtr.Zero)
        {
        }

        internal WindowsFolderPickerService(IntPtr ownerWindow)
            : this(new WindowsFolderPickerDialogFactory(ownerWindow))
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
            private readonly IntPtr _ownerWindow;

            internal WindowsFolderPickerDialogFactory(IntPtr ownerWindow)
            {
                _ownerWindow = ownerWindow;
            }

            public void InitializeDispatcherThread()
            {
            }

            public IStaFolderPickerDialog CreateDialog() =>
                new WindowsFolderPickerDialog(_ownerWindow);

            public void CloseOnDispatcher(IStaFolderPickerDialog dialog) => dialog.Close();
        }

        /// <summary>
        /// Modern Windows Common Item Dialog configured for folder selection.
        /// It keeps normal Explorer navigation (including other drives) while
        /// remaining closeable through the existing dispatcher contract.
        /// </summary>
        private sealed class WindowsFolderPickerDialog : IStaFolderPickerDialog
        {
            private const uint FosPickFolders = 0x00000020;
            private const uint FosForceFileSystem = 0x00000040;
            private const uint FosPathMustExist = 0x00000800;
            private const uint SigdnFileSystemPath = 0x80058000;
            private const int ErrorCancelled = unchecked((int)0x800704C7);

            private static readonly Guid FileOpenDialogClassId =
                new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
            private static readonly Guid ShellItemInterfaceId =
                new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

            private readonly object _dialogLock = new();
            private readonly IntPtr _ownerWindow;
            // Created on the dispatcher STA thread; BeginInvoke marshals close
            // requests onto that thread's message pump.
            private readonly Control _staInvoker;
            private IFileOpenDialog? _dialog;
            private bool _closeRequested;

            internal WindowsFolderPickerDialog(IntPtr ownerWindow)
            {
                _ownerWindow = ownerWindow;
                _staInvoker = new Control();
                _ = _staInvoker.Handle;
            }

            public FolderPickerResult Show(string? initialDirectory)
            {
                IFileOpenDialog? dialog = null;
                IShellItem? defaultFolder = null;
                IShellItem? selectedItem = null;
                IntPtr selectedPath = IntPtr.Zero;

                try
                {
                    var dialogType = Type.GetTypeFromCLSID(FileOpenDialogClassId, throwOnError: true)
                        ?? throw new InvalidOperationException(
                            "The Windows folder picker could not be created.");
                    dialog = (IFileOpenDialog)(Activator.CreateInstance(dialogType)
                        ?? throw new InvalidOperationException(
                            "The Windows folder picker could not be created."));

                    ThrowIfFailed(dialog.GetOptions(out var options));
                    ThrowIfFailed(dialog.SetOptions(
                        options | FosPickFolders | FosForceFileSystem | FosPathMustExist));
                    ThrowIfFailed(dialog.SetTitle("Choose song folder"));

                    if (!string.IsNullOrWhiteSpace(initialDirectory) &&
                        Directory.Exists(initialDirectory))
                    {
                        var shellItemId = ShellItemInterfaceId;
                        var createResult = SHCreateItemFromParsingName(
                            initialDirectory,
                            IntPtr.Zero,
                            ref shellItemId,
                            out defaultFolder);
                        if (createResult >= 0)
                        {
                            // The configured root is only a navigation hint. If
                            // Windows rejects a stale/reparse-point default, keep
                            // the picker usable so the player can choose a new root.
                            _ = dialog.SetDefaultFolder(defaultFolder);
                        }
                    }

                    lock (_dialogLock)
                    {
                        _dialog = dialog;
                        if (_closeRequested)
                            return FolderPickerResult.Cancelled();
                    }

                    var showResult = dialog.Show(_ownerWindow);

                    lock (_dialogLock)
                    {
                        if (ReferenceEquals(_dialog, dialog))
                            _dialog = null;
                    }

                    if (showResult == ErrorCancelled)
                        return FolderPickerResult.Cancelled();
                    ThrowIfFailed(showResult);

                    ThrowIfFailed(dialog.GetResult(out selectedItem));
                    ThrowIfFailed(selectedItem.GetDisplayName(
                        SigdnFileSystemPath,
                        out selectedPath));

                    var path = Marshal.PtrToStringUni(selectedPath);
                    return !string.IsNullOrWhiteSpace(path)
                        ? FolderPickerResult.Selected(path)
                        : FolderPickerResult.Failed(
                            "The selected folder path could not be extracted from the picker result.");
                }
                catch (COMException exception) when (exception.HResult == ErrorCancelled)
                {
                    return FolderPickerResult.Cancelled();
                }
                catch (Exception exception)
                {
                    return FolderPickerResult.Failed(exception.Message);
                }
                finally
                {
                    lock (_dialogLock)
                    {
                        if (ReferenceEquals(_dialog, dialog))
                            _dialog = null;
                    }

                    // The RCW is only ever used on this STA thread: Close()
                    // posts to _staInvoker instead of calling into the dialog
                    // object, so nothing races this release.
                    ReleaseComObject(dialog);

                    if (selectedPath != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(selectedPath);
                    ReleaseComObject(selectedItem);
                    ReleaseComObject(defaultFolder);
                }
            }

            public void Close()
            {
                lock (_dialogLock)
                {
                    _closeRequested = true;
                }

                // IFileOpenDialog is STA-bound: calling into it here would be
                // marshaled to the dispatcher thread and can outlive its
                // message pump once Show has returned. Post the request
                // instead so the STA services it only while it is pumping.
                try
                {
                    _ = _staInvoker.BeginInvoke(new Action(CloseOnDispatcherThread));
                }
                catch (InvalidOperationException)
                {
                    // The dispatcher thread or the invoker handle is gone. The
                    // request has already been completed as cancelled by the
                    // dispatcher.
                }
            }

            private void CloseOnDispatcherThread()
            {
                IFileOpenDialog? dialog;
                lock (_dialogLock)
                {
                    dialog = _dialog;
                }

                if (dialog == null)
                    return;

                try
                {
                    _ = dialog.Close(ErrorCancelled);
                }
                catch (COMException)
                {
                    // Best effort. The request has already been completed as
                    // cancelled by the dispatcher.
                }
            }

            public void Dispose()
            {
                Close();
                _staInvoker.Dispose();
            }

            private static void ThrowIfFailed(int hresult)
            {
                if (hresult < 0)
                    Marshal.ThrowExceptionForHR(hresult);
            }

            private static void ReleaseComObject(object? value)
            {
                if (value != null && Marshal.IsComObject(value))
                    _ = Marshal.ReleaseComObject(value);
            }

            [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
            private static extern int SHCreateItemFromParsingName(
                [MarshalAs(UnmanagedType.LPWStr)] string path,
                IntPtr bindingContext,
                ref Guid interfaceId,
                [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

            [ComImport]
            [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IFileOpenDialog
            {
                [PreserveSig]
                int Show(IntPtr parent);

                [PreserveSig]
                int SetFileTypes(uint fileTypeCount, IntPtr filterSpec);

                [PreserveSig]
                int SetFileTypeIndex(uint fileTypeIndex);

                [PreserveSig]
                int GetFileTypeIndex(out uint fileTypeIndex);

                [PreserveSig]
                int Advise(IntPtr events, out uint cookie);

                [PreserveSig]
                int Unadvise(uint cookie);

                [PreserveSig]
                int SetOptions(uint options);

                [PreserveSig]
                int GetOptions(out uint options);

                [PreserveSig]
                int SetDefaultFolder(IShellItem shellItem);

                [PreserveSig]
                int SetFolder(IShellItem shellItem);

                [PreserveSig]
                int GetFolder(out IShellItem shellItem);

                [PreserveSig]
                int GetCurrentSelection(out IShellItem shellItem);

                [PreserveSig]
                int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);

                [PreserveSig]
                int GetFileName(out IntPtr fileName);

                [PreserveSig]
                int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

                [PreserveSig]
                int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

                [PreserveSig]
                int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

                [PreserveSig]
                int GetResult(out IShellItem shellItem);

                [PreserveSig]
                int AddPlace(IShellItem shellItem, uint alignment);

                [PreserveSig]
                int SetDefaultExtension(
                    [MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);

                [PreserveSig]
                int Close(int hresult);

                [PreserveSig]
                int SetClientGuid(ref Guid clientGuid);

                [PreserveSig]
                int ClearClientData();

                [PreserveSig]
                int SetFilter(IntPtr filter);

                [PreserveSig]
                int GetResults(out IntPtr shellItems);

                [PreserveSig]
                int GetSelectedItems(out IntPtr shellItems);
            }

            [ComImport]
            [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IShellItem
            {
                [PreserveSig]
                int BindToHandler(
                    IntPtr bindingContext,
                    ref Guid handlerId,
                    ref Guid interfaceId,
                    out IntPtr result);

                [PreserveSig]
                int GetParent(out IShellItem parent);

                [PreserveSig]
                int GetDisplayName(uint displayNameType, out IntPtr displayName);

                [PreserveSig]
                int GetAttributes(uint attributeMask, out uint attributes);

                [PreserveSig]
                int Compare(IShellItem shellItem, uint hint, out int order);
            }
        }
    }
}
