#nullable enable

using System;
using System.Runtime.InteropServices;
using DTXMania.Game.Lib.Stage.Config;

namespace DTXMania.Game.Platform
{
    internal static class FolderPickerServiceFactory
    {
        internal static IFolderPickerService Create() => Create(GetActiveWindow());

        internal static IFolderPickerService Create(IntPtr ownerWindow) =>
            new WindowsFolderPickerService(ownerWindow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
    }
}
