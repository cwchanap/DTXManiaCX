#nullable enable

using System;
using DTXMania.Game.Lib.Stage.Config;

namespace DTXMania.Game.Platform
{
    internal static class FolderPickerServiceFactory
    {
        internal static IFolderPickerService Create() => new MacFolderPickerService();

        internal static IFolderPickerService Create(IntPtr _) => new MacFolderPickerService();
    }
}
