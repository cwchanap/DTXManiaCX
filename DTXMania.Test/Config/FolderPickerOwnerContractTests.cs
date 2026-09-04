#nullable enable

using System;
using DTXMania.Game.Platform;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class FolderPickerOwnerContractTests
{
    [Fact]
    public void PlatformFactory_ShouldAcceptOwnerWindowHandle()
    {
        var ownerWindow = new IntPtr(0x1234);

        var picker = FolderPickerServiceFactory.Create(ownerWindow);

        Assert.NotNull(picker);
        (picker as IDisposable)?.Dispose();
    }
}
