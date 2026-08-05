#nullable enable

using System;
using System.IO;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportSanitizerTests
{
    [Fact]
    public void SanitizeStackTrace_ShouldRemoveHomeAndSourcePaths()
    {
        var home = Path.Combine(Path.GetTempPath(), "Users", "alice");
        var sanitizer = new CrashReportSanitizer(
            [home, Path.Combine(home, "Library", "Application Support", "DTXManiaCX")]);

        var input =
            $"at Example.Run() in {Path.Combine(home, "src", "Game.cs")}:line 42";

        var result = sanitizer.SanitizeStackTrace(input);

        Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Game.cs", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[SOURCE]", result);
    }

    [Fact]
    public void SanitizeExceptionMessage_ShouldOmitArbitraryContent()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(
            "[EXCEPTION MESSAGE OMITTED]",
            sanitizer.SanitizeExceptionMessage("Failed to load Secret Song Name"));
    }

    [Theory]
    [InlineData("DTXMANIA_E2E_CONTROLLED_CRASH ")]
    [InlineData("DTXMANIA_E2E_CONTROLLED_CRASH-extra")]
    [InlineData("dtxmania_e2e_controlled_crash")]
    public void SanitizeExceptionMessage_WithNearControlledMarker_ShouldOmitContent(string message)
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(CrashReportSanitizer.ExceptionMessageOmitted, sanitizer.SanitizeExceptionMessage(message));
    }

#if DEBUG
    [Fact]
    public void SanitizeExceptionChain_WithControlledDebugMarker_ShouldPreserveOnlyFixedMessage()
    {
        var sanitizer = new CrashReportSanitizer([]);

        var result = sanitizer.SanitizeExceptionChain(
            new InvalidOperationException(DebugCrashInjection.ControlledCrashMessage));

        Assert.Contains(
            "Message: " + DebugCrashInjection.ControlledCrashMessage,
            result,
            StringComparison.Ordinal);
    }
#endif

    [Fact]
    public void SanitizeStackTrace_ShouldRemoveApiKeyLikeValuesAndUriQueries()
    {
        var sanitizer = new CrashReportSanitizer([]);
        const string apiKey = "dtx_live_abcdefghijklmnopqrstuvwxyz012345";
        var input =
            $"Authorization: Bearer {apiKey}; at Loader.Run() in https://example.test/api/report?api_key={apiKey}&song=SecretSong";

        var result = sanitizer.SanitizeStackTrace(input);

        Assert.DoesNotContain(apiKey, result, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretSong", result, StringComparison.Ordinal);
        Assert.DoesNotContain("?", result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void SanitizeStackTrace_ShouldRemoveWindowsUncPathsAndUriCredentials()
    {
        var sanitizer = new CrashReportSanitizer([]);
        const string credential = "uri-credential-secret";
        const string input =
            @"at Loader.Run() in \\server\share\Secret Album\chart.dtx:line 42; https://alice:uri-credential-secret@example.test/song?token=uri-credential-secret";

        var result = sanitizer.SanitizeStackTrace(input);

        Assert.DoesNotContain("Secret Album", result, StringComparison.Ordinal);
        Assert.DoesNotContain("chart.dtx", result, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", result, StringComparison.Ordinal);
        Assert.DoesNotContain(credential, result, StringComparison.Ordinal);
        Assert.Contains("[SOURCE]", result, StringComparison.Ordinal);
        Assert.Contains("https://[REDACTED]@example.test/song", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeStackTrace_ShouldReplaceRegisteredSongAndSkinRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-sanitizer-roots");
        var songRoot = Path.Combine(root, "songs", "Secret Album");
        var skinRoot = Path.Combine(root, "skins", "Secret Skin");
        var sanitizer = new CrashReportSanitizer([songRoot, skinRoot]);

        var result = sanitizer.SanitizeStackTrace(
            $"Song={Path.Combine(songRoot, "chart.dtx")}; Skin={Path.Combine(skinRoot, "Config.ini")}");

        Assert.DoesNotContain("Secret Album", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret Skin", result, StringComparison.Ordinal);
        Assert.Contains("[PATH]", result);
    }

    [Fact]
    public void SanitizeStackTrace_ShouldReplaceAbsolutePathsAndUsernameSegments()
    {
        var sanitizer = new CrashReportSanitizer([]);
        const string input =
            @"Windows C:\Users\alice\Desktop\secret.dtx; Unix /home/bob/Music/secret.dtx; relative Users/carol/Documents";

        var result = sanitizer.SanitizeStackTrace(input);

        Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bob", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("carol", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.dtx", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PATH]", result);
        Assert.Contains("[USER]", result);
    }

    [Fact]
    public void SanitizeExceptionChain_ShouldOmitNestedExceptionMessages()
    {
        var sanitizer = new CrashReportSanitizer([]);
        var exception = new InvalidOperationException(
            "Top Secret Song",
            new ArgumentException("Inner Secret Skin"));

        var result = sanitizer.SanitizeExceptionChain(exception);

        Assert.Contains(typeof(InvalidOperationException).FullName!, result, StringComparison.Ordinal);
        Assert.Contains(typeof(ArgumentException).FullName!, result, StringComparison.Ordinal);
        Assert.Contains("[EXCEPTION MESSAGE OMITTED]", result);
        Assert.DoesNotContain("Top Secret Song", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Inner Secret Skin", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeStackTrace_WhenSensitivePathNormalizationFails_ShouldReturnRedacted()
    {
        var sanitizer = new CrashReportSanitizer(["invalid\0path"]);

        var result = sanitizer.SanitizeStackTrace("Secret Song Name");

        Assert.Equal("[REDACTED]", result);
    }

    [Fact]
    public void SanitizeStackTrace_WithNull_ShouldReturnEmptyString()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(string.Empty, sanitizer.SanitizeStackTrace(null));
    }

    [Fact]
    public void SanitizeExceptionChain_WithDeepNesting_ShouldTruncateAfterMaximumInnerExceptions()
    {
        var sanitizer = new CrashReportSanitizer([]);
        Exception? deepest = new InvalidOperationException("level 0");
        for (var i = 1; i < 12; i++)
        {
            deepest = new InvalidOperationException($"level {i}", deepest);
        }

        var result = sanitizer.SanitizeExceptionChain(deepest, out var truncated);

        Assert.True(truncated);
        Assert.Contains("[TRUNCATED]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeExceptionChain_WithoutTruncation_ShouldSetTruncatedFalse()
    {
        var sanitizer = new CrashReportSanitizer([]);

        var result = sanitizer.SanitizeExceptionChain(
            new InvalidOperationException("top"),
            out var truncated);

        Assert.False(truncated);
        Assert.DoesNotContain("[TRUNCATED]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeExceptionChain_WithoutStackTrace_ShouldOmitStackTraceSection()
    {
        var sanitizer = new CrashReportSanitizer([]);
        var exception = new InvalidOperationException("no stack trace");

        var result = sanitizer.SanitizeExceptionChain(exception);

        Assert.DoesNotContain("StackTrace:", result, StringComparison.Ordinal);
        Assert.Contains("ExceptionType:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeExceptionType_WithType_ShouldReturnSanitizedFullName()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            sanitizer.SanitizeExceptionType(typeof(InvalidOperationException)));
    }

    [Fact]
    public void SanitizeExceptionType_WithNull_ShouldThrow()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Throws<ArgumentNullException>(() => sanitizer.SanitizeExceptionType(null!));
    }

    [Fact]
    public void SanitizeTypeName_WithNullOrWhitespace_ShouldReturnUnknown()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("Unknown", sanitizer.SanitizeTypeName(null));
        Assert.Equal("Unknown", sanitizer.SanitizeTypeName(""));
        Assert.Equal("Unknown", sanitizer.SanitizeTypeName("   "));
    }

    [Fact]
    public void SanitizeTypeName_WithInvalidCharacters_ShouldReturnRedacted()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(CrashReportSanitizer.RedactedValue, sanitizer.SanitizeTypeName("Invalid Type Name!"));
    }

    [Fact]
    public void SanitizeTypeName_WithTooLongName_ShouldReturnRedacted()
    {
        var sanitizer = new CrashReportSanitizer([]);
        var longName = new string('A', 300);

        Assert.Equal(CrashReportSanitizer.RedactedValue, sanitizer.SanitizeTypeName(longName));
    }

    [Fact]
    public void SanitizeTypeName_WithValidName_ShouldReturnName()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("System.InvalidOperationException", sanitizer.SanitizeTypeName("System.InvalidOperationException"));
    }

    [Fact]
    public void SanitizeMetadata_WithNullOrWhitespace_ShouldReturnFallback()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("Unknown", sanitizer.SanitizeMetadata(null));
        Assert.Equal("Unknown", sanitizer.SanitizeMetadata(""));
        Assert.Equal("N/A", sanitizer.SanitizeMetadata("  ", "N/A"));
    }

    [Fact]
    public void SanitizeMetadata_WithControlCharacters_ShouldReturnFallback()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("Unknown", sanitizer.SanitizeMetadata("line\nbreak"));
        Assert.Equal("Unknown", sanitizer.SanitizeMetadata("null\0char"));
    }

    [Fact]
    public void SanitizeMetadata_WithNormalValue_ShouldReturnLimitedValue()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("macOS", sanitizer.SanitizeMetadata("macOS"));
    }

    [Fact]
    public void SanitizeMetadata_WithTooLongValue_ShouldBeLimited()
    {
        var sanitizer = new CrashReportSanitizer([]);
        var longValue = new string('X', 300);

        var result = sanitizer.SanitizeMetadata(longValue);

        Assert.True(result.Length <= 256);
    }

    [Fact]
    public void SanitizeStableLabel_WithNullOrWhitespace_ShouldReturnFallback()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("Unknown", sanitizer.SanitizeStableLabel(null));
        Assert.Equal("Unknown", sanitizer.SanitizeStableLabel(""));
        Assert.Equal("fallback", sanitizer.SanitizeStableLabel("  ", "fallback"));
    }

    [Fact]
    public void SanitizeStableLabel_WithInvalidCharacters_ShouldReturnFallback()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("Unknown", sanitizer.SanitizeStableLabel("has spaces!"));
    }

    [Fact]
    public void SanitizeStableLabel_WithValidLabel_ShouldReturnLabel()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("crash-20260802-120000Z-a1b2c3", sanitizer.SanitizeStableLabel("crash-20260802-120000Z-a1b2c3"));
    }

    [Fact]
    public void SanitizeExceptionChain_WithNullException_ShouldThrow()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Throws<ArgumentNullException>(() => sanitizer.SanitizeExceptionChain(null!));
    }

    [Fact]
    public void Constructor_WithEmptySensitivePaths_ShouldNotFail()
    {
        var sanitizer = new CrashReportSanitizer(null);

        Assert.Equal("test", sanitizer.SanitizeMetadata("test"));
    }

    [Fact]
    public void Constructor_WithWhitespaceOnlySensitivePath_ShouldSkipIt()
    {
        var sanitizer = new CrashReportSanitizer(["   ", ""]);

        var result = sanitizer.SanitizeStackTrace("some text");

        Assert.DoesNotContain("[REDACTED]", result, StringComparison.Ordinal);
    }
}
