#nullable enable

using DTXMania.Game.Lib.Stage.Config;

namespace DTXMania.Game.Platform
{
    internal static class FolderPickerServiceFactory
    {
        internal static IFolderPickerService Create() => new MacFolderPickerService();
    }
}
