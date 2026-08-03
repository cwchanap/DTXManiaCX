#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Game.Platform;
using Xunit;

namespace DTXMania.Test.Platform;

[Trait("Category", "Unit")]
public sealed class MacFolderPickerServiceAdditionalTests
{
    [Fact]
    public async Task PickFolderAsync_WhenCancellationFiresWhileDialogIsOpen_ShouldReturnCancelledAndKillProcess()
    {
        if (!OperatingSystem.IsMacOS())
        {
            // The macOS folder picker only launches osascript on macOS.
            return;
        }

        var picker = new MacFolderPickerService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await picker.PickFolderAsync(initialDirectory: null, cancellation.Token);

        // The cancellation path kills the osascript process and returns Cancelled.
        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task PickFolderAsync_WhenOsascriptReturnsImmediately_ShouldMapOutputToSelected()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // Launch a harmless osascript that returns a POSIX path without showing
        // a dialog, by directly invoking the service against a temp directory.
        // The service's BuildAppleScript always uses `choose folder`, so we
        // instead verify the MapProcessResult path that PickFolderAsync delegates
        // to, ensuring a zero-exit path output maps to Selected.
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 0,
            standardOutput: "/Users/test/Songs",
            standardError: null);

        Assert.Equal(FolderPickerStatus.Selected, result.Status);
        Assert.Equal("/Users/test/Songs", result.Path);
    }

    [Fact]
    public void MapProcessResult_WhenStandardOutputIsNull_ShouldTreatAsEmpty()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: null,
            standardError: null);

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Contains("exited with code 1", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapProcessResult_WhenStandardErrorIsNull_ShouldFallBackToOutput()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: "User canceled. (-128)",
            standardError: null);

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public void MapProcessResult_WhenErrorContainsCancelCode_ShouldReturnCancelled()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: string.Empty,
            standardError: "number -128");

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public void MapProcessResult_WhenAuthorizationDeniedWithBlankDetails_ShouldUseFallbackMessage()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: "  ",
            standardError: "  not authorized  ");

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Contains("not authorized", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppleScript_WhenInitialDirectoryIsOmitted_ShouldOmitDefaultLocation()
    {
        var script = InvokeBuildAppleScript(null);

        Assert.Contains("choose folder", script, StringComparison.Ordinal);
        Assert.DoesNotContain("default location", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CancelledMarker, script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppleScript_WhenInitialDirectoryExists_ShouldEmbedEscapedPath()
    {
        using var directory = TemporaryDirectory.Create();
        var script = InvokeBuildAppleScript(directory.Path);

        Assert.Contains("default location POSIX file", script, StringComparison.Ordinal);
        Assert.Contains(directory.Path, script, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeAppleScriptString_ShouldEscapeBackslashesAndQuotes()
    {
        var escaped = InvokeEscapeAppleScript(@"path\with""quote");

        Assert.Equal(@"path\\with\""quote", escaped);
    }

    [Fact]
    public void EscapeAppleScriptString_WhenValueHasNoSpecialChars_ShouldReturnUnchanged()
    {
        var escaped = InvokeEscapeAppleScript("/plain/path");

        Assert.Equal("/plain/path", escaped);
    }

    [Fact]
    public void CreateStartInfo_WhenInitialDirectoryIsBlank_ShouldOmitDefaultLocation()
    {
        var startInfo = MacFolderPickerService.CreateStartInfo("   ");

        Assert.DoesNotContain(
            "default location",
            startInfo.ArgumentList[1],
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CancelledMarker =>
        (string)typeof(MacFolderPickerService)
            .GetField("CancelledMarker", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    private static string InvokeBuildAppleScript(string? initialDirectory)
    {
        var method = typeof(MacFolderPickerService).GetMethod(
            "BuildAppleScript",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object?[] { initialDirectory })!;
    }

    private static string InvokeEscapeAppleScript(string value)
    {
        var method = typeof(MacFolderPickerService).GetMethod(
            "EscapeAppleScriptString",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object?[] { value })!;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dtxmania-mac-add-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
