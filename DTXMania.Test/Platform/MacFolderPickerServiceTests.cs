#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Game.Platform;
using Xunit;

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
namespace DTXMania.Test.Platform;

[Trait("Category", "Unit")]
public sealed class MacFolderPickerServiceTests
{
    [Fact]
    public void CreateStartInfo_WhenInitialDirectoryIsOmitted_ShouldOmitDefaultLocation()
    {
        var startInfo = MacFolderPickerService.CreateStartInfo(initialDirectory: null);

        Assert.Equal("/usr/bin/osascript", startInfo.FileName);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal("-e", startInfo.ArgumentList[0]);
        var script = startInfo.ArgumentList[1];
        Assert.DoesNotContain("default location", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("choose folder", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStartInfo_WhenInitialDirectoryDoesNotExist_ShouldOmitDefaultLocation()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dtxmania-missing-" + Guid.NewGuid().ToString("N"));

        var startInfo = MacFolderPickerService.CreateStartInfo(missing);

        Assert.DoesNotContain(
            "default location",
            startInfo.ArgumentList[1],
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateStartInfo_WhenInitialDirectoryExists_ShouldEmbedEscapedDefaultLocation()
    {
        using var directory = TemporaryDirectory.Create();

        var startInfo = MacFolderPickerService.CreateStartInfo(directory.Path);

        var script = startInfo.ArgumentList[1];
        var escapedPath = directory.Path
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        Assert.Contains("default location POSIX file", script, StringComparison.Ordinal);
        Assert.Contains(escapedPath, script, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStartInfo_WhenPathContainsSpecialCharacters_ShouldEscapeForAppleScript()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows forbids quotes in directory names. The Windows-safe
            // backslash case is covered by the existing-directory test above,
            // while the macOS job exercises both quote and backslash escaping.
            return;
        }

        // Build a directory whose name contains characters AppleScript must escape
        // (double-quote and backslash). Both are legal in macOS directory names.
        var suffix = "with\"quote\\and-backslash-" + Guid.NewGuid().ToString("N");
        var directoryPath = Path.Combine(Path.GetTempPath(), suffix);
        Directory.CreateDirectory(directoryPath);
        try
        {
            var startInfo = MacFolderPickerService.CreateStartInfo(directoryPath);

            var script = startInfo.ArgumentList[1];
            var escaped = suffix.Replace("\\", "\\\\").Replace("\"", "\\\"");
            Assert.Contains(escaped, script, StringComparison.Ordinal);
            // The raw, unescaped special characters must not appear inside the
            // quoted POSIX file literal.
            Assert.DoesNotContain("\"" + suffix + "\"", script, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public void MapProcessResult_WhenExitCodeIsZeroAndOutputIsAPath_ShouldReturnSelected()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 0,
            standardOutput: "/Users/test/Songs",
            standardError: string.Empty);

        Assert.Equal(FolderPickerStatus.Selected, result.Status);
        Assert.Equal("/Users/test/Songs", result.Path);
        Assert.Null(result.Message);
    }

    [Fact]
    public void MapProcessResult_WhenExitCodeIsZeroAndOutputIsCancellationMarker_ShouldReturnCancelled()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 0,
            standardOutput: "__DTXMANIA_FOLDER_PICKER_CANCELLED__",
            standardError: string.Empty);

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MapProcessResult_WhenExitCodeIsZeroAndOutputIsBlank_ShouldReturnCancelled(string output)
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 0,
            standardOutput: output,
            standardError: string.Empty);

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public void MapProcessResult_WhenErrorContainsUserCancellationCodeInOutput_ShouldReturnCancelled()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: "User canceled. (-128)",
            standardError: string.Empty);

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    [Fact]
    public void MapProcessResult_WhenErrorContainsNotAuthorizedText_ShouldReturnFailedWithDetails()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: string.Empty,
            standardError: "not authorized to send Apple events");

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Equal("not authorized to send Apple events", result.Message);
    }

    [Fact]
    public void MapProcessResult_WhenAuthorizationDeniedErrorCodeReturned_ShouldReturnFailedWithDetails()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: string.Empty,
            standardError: "-1743");

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Equal("-1743", result.Message);
    }

    [Fact]
    public void MapProcessResult_WhenNonZeroExitHasErrorDetails_ShouldReturnFailedWithDetails()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 42,
            standardOutput: "ignored output",
            standardError: "osascript failed");

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Equal("osascript failed", result.Message);
    }

    [Fact]
    public void MapProcessResult_WhenNonZeroExitHasNoDetails_ShouldReturnFailedWithExitCodeMessage()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 7,
            standardOutput: "   ",
            standardError: string.Empty);

        Assert.Equal(FolderPickerStatus.Failed, result.Status);
        Assert.Equal("macOS folder picker exited with code 7.", result.Message);
    }

    [Fact]
    public void MapProcessResult_WhenStandardErrorIsWhitespace_ShouldFallBackToOutputForDetails()
    {
        var result = MacFolderPickerService.MapProcessResult(
            exitCode: 1,
            standardOutput: "User canceled. (-128)",
            standardError: "   ");

        Assert.Equal(FolderPickerStatus.Cancelled, result.Status);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dtxmania-mac-picker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

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
    public void MapProcessResult_WhenZeroExitReturnsPosixPath_ShouldMapToSelected()
    {
        // Verify the MapProcessResult path that PickFolderAsync delegates to,
        // ensuring a zero-exit POSIX path output maps to Selected. This runs on
        // every platform since MapProcessResult is a pure static method.
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
    public void MapProcessResult_WhenAuthorizationDeniedWithTrimmedDetails_ShouldReturnFailedWithDetails()
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
        var escapedPath = directory.Path
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        Assert.Contains("default location POSIX file", script, StringComparison.Ordinal);
        Assert.Contains(escapedPath, script, StringComparison.Ordinal);
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
}
