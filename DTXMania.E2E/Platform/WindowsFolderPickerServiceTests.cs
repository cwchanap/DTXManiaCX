using System.Reflection;
using System.Runtime.InteropServices;
using DTXMania.Game.Platform;

namespace DTXMania.E2E;

[Trait("Category", "E2E-Support")]
public sealed class WindowsFolderPickerServiceTests
{
    [Fact]
    public void FolderPicker_DoesNotUseLegacyShBrowseForFolder()
    {
        var pickerTypes = new[] { typeof(WindowsFolderPickerService) }
            .Concat(typeof(WindowsFolderPickerService).GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic));

        var pinvokeMethods = pickerTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<DllImportAttribute>() is not null)
            .ToArray();

        Assert.DoesNotContain(
            pinvokeMethods,
            method => string.Equals(
                method.Name,
                "SHBrowseForFolder",
                StringComparison.Ordinal));
    }
}
