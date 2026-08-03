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
            @"Windows C:\\Users\\alice\\Desktop\\secret.dtx; Unix /home/bob/Music/secret.dtx; relative Users/carol/Documents";

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
}
