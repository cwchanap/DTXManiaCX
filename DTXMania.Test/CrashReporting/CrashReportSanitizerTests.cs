#nullable enable

using System;
using System.IO;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportSanitizerTests
{
    [Fact]
    public void Scrub_ShouldReplaceRegisteredSongAndSkinRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-sanitizer-roots");
        var songRoot = Path.Combine(root, "songs", "Some Album");
        var skinRoot = Path.Combine(root, "skins", "Some Skin");
        var sanitizer = new CrashReportSanitizer([songRoot, skinRoot]);

        var result = sanitizer.Scrub(
            $"Song={Path.Combine(songRoot, "chart.dtx")}; Skin={Path.Combine(skinRoot, "Config.ini")}");

        Assert.DoesNotContain("Some Album", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Some Skin", result, StringComparison.Ordinal);
        Assert.Contains("[PATH]", result, StringComparison.Ordinal);
        // The leaf file name still identifies what failed, which is the point of the report.
        Assert.Contains("chart.dtx", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_ShouldReplaceHomeDirectoryAccountName()
    {
        var sanitizer = new CrashReportSanitizer([]);

        var result = sanitizer.Scrub(@"C:\Users\alice\Desktop\chart.dtx and /home/bob/Music/chart.dtx");

        Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bob", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[USER]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_ShouldPreserveOrdinaryText()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(
            "Could not load chart.dtx: unexpected channel 0xZZ",
            sanitizer.Scrub("Could not load chart.dtx: unexpected channel 0xZZ"));
    }

    [Fact]
    public void Scrub_ShouldPreferTheLongestMatchingRoot()
    {
        var parent = Path.Combine(Path.GetTempPath(), "dtx-nested");
        var child = Path.Combine(parent, "songs");
        var sanitizer = new CrashReportSanitizer([parent, child]);

        var result = sanitizer.Scrub(Path.Combine(child, "chart.dtx"));

        Assert.StartsWith("[PATH]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("songs", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_ShouldLimitOverlongText()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(10, sanitizer.Scrub(new string('X', 500), maximumLength: 10).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Scrub_WithNullOrEmpty_ShouldReturnEmptyString(string? value)
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal(string.Empty, sanitizer.Scrub(value));
    }

    [Fact]
    public void Constructor_WithUnusableSensitivePath_ShouldNotThrow()
    {
        var sanitizer = new CrashReportSanitizer(["invalid\0path"]);

        Assert.Equal("plain text", sanitizer.Scrub("plain text"));
    }

    [Fact]
    public void Constructor_WithNullOrBlankSensitivePaths_ShouldSkipThem()
    {
        Assert.Equal("test", new CrashReportSanitizer(null).SanitizeMetadata("test"));
        Assert.Equal("test", new CrashReportSanitizer(["   ", ""]).SanitizeMetadata("test"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeMetadata_WithNullOrWhitespace_ShouldReturnFallback(string? value)
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("Unknown", sanitizer.SanitizeMetadata(value));
        Assert.Equal("N/A", sanitizer.SanitizeMetadata(value, "N/A"));
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("null\0char")]
    public void SanitizeMetadata_WithControlCharacters_ShouldReturnFallback(string value)
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("Unknown", sanitizer.SanitizeMetadata(value));
    }

    [Fact]
    public void SanitizeMetadata_ShouldPassThroughAndLimit()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Equal("macOS 15.0", sanitizer.SanitizeMetadata("macOS 15.0"));
        Assert.True(sanitizer.SanitizeMetadata(new string('X', 300)).Length <= 256);
    }

    [Fact]
    public void SanitizeExceptionChain_ShouldPreserveMessagesAndTypes()
    {
        var sanitizer = new CrashReportSanitizer([]);
        var exception = new InvalidOperationException(
            "Could not load chart",
            new ArgumentException("channel out of range"));

        var result = sanitizer.SanitizeExceptionChain(exception, out var truncated);

        Assert.False(truncated);
        Assert.Contains(typeof(InvalidOperationException).FullName!, result, StringComparison.Ordinal);
        Assert.Contains(typeof(ArgumentException).FullName!, result, StringComparison.Ordinal);
        Assert.Contains("Message: Could not load chart", result, StringComparison.Ordinal);
        Assert.Contains("Message: channel out of range", result, StringComparison.Ordinal);
        Assert.Contains("InnerException:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeExceptionChain_ShouldScrubPathsInsideMessages()
    {
        var songRoot = Path.Combine(Path.GetTempPath(), "dtx-chain", "Some Album");
        var sanitizer = new CrashReportSanitizer([songRoot]);

        var result = sanitizer.SanitizeExceptionChain(
            new FileNotFoundException($"Missing {Path.Combine(songRoot, "chart.dtx")}"),
            out _);

        Assert.DoesNotContain("Some Album", result, StringComparison.Ordinal);
        Assert.Contains("[PATH]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeExceptionChain_WithDeepNesting_ShouldTruncate()
    {
        var sanitizer = new CrashReportSanitizer([]);
        Exception deepest = new InvalidOperationException("level 0");
        for (var i = 1; i < 12; i++)
        {
            deepest = new InvalidOperationException($"level {i}", deepest);
        }

        var result = sanitizer.SanitizeExceptionChain(deepest, out var truncated);

        Assert.True(truncated);
        Assert.Contains("[TRUNCATED]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeExceptionChain_WithoutStackTrace_ShouldOmitStackTraceSection()
    {
        var sanitizer = new CrashReportSanitizer([]);

        var result = sanitizer.SanitizeExceptionChain(
            new InvalidOperationException("no stack trace"),
            out _);

        Assert.DoesNotContain("StackTrace:", result, StringComparison.Ordinal);
        Assert.Contains("ExceptionType:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeExceptionChain_WithThrownException_ShouldIncludeStackTrace()
    {
        var sanitizer = new CrashReportSanitizer([]);

        try
        {
            throw new InvalidOperationException("thrown");
        }
        catch (InvalidOperationException exception)
        {
            var result = sanitizer.SanitizeExceptionChain(exception, out _);

            Assert.Contains("StackTrace:", result, StringComparison.Ordinal);
            Assert.Contains(nameof(SanitizeExceptionChain_WithThrownException_ShouldIncludeStackTrace), result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SanitizeExceptionChain_WithNullException_ShouldThrow()
    {
        var sanitizer = new CrashReportSanitizer([]);

        Assert.Throws<ArgumentNullException>(() => sanitizer.SanitizeExceptionChain(null!, out _));
    }

    [Fact]
    public void Scrub_ShouldRedactRegisteredSecretValues()
    {
        const string apiKey = "super-secret-api-key-123456";
        var sanitizer = new CrashReportSanitizer([], [apiKey]);

        var result = sanitizer.Scrub("Failed to start API with key=" + apiKey + " and more text");

        Assert.DoesNotContain(apiKey, result, StringComparison.Ordinal);
        Assert.Contains(CrashReportSanitizer.RedactedValue, result, StringComparison.Ordinal);
        Assert.Contains("Failed to start API with key=", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_ShouldRedactSecretsInsideExceptionMessages()
    {
        const string apiKey = "super-secret-api-key-123456";
        var sanitizer = new CrashReportSanitizer([], [apiKey]);

        var result = sanitizer.SanitizeExceptionChain(
            new InvalidOperationException("GameApi rejected key " + apiKey),
            out var truncated);

        Assert.False(truncated);
        Assert.DoesNotContain(apiKey, result, StringComparison.Ordinal);
        Assert.Contains(CrashReportSanitizer.RedactedValue, result, StringComparison.Ordinal);
        Assert.Contains("GameApi rejected key", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://alice:hunter2@example.com/path", "https://[REDACTED]@example.com/path")]
    [InlineData("ftp://user:password@example.com", "ftp://[REDACTED]@example.com")]
    [InlineData("wss://token:secret@host/ws", "wss://[REDACTED]@host/ws")]
    public void Scrub_ShouldRedactUriUserInfoCredentials(string input, string expected)
    {
        var sanitizer = new CrashReportSanitizer([]);

        var result = sanitizer.Scrub(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://example.com/path?api_key=abc123&other=keep", "api_key=[REDACTED]", "abc123", "other=keep")]
    [InlineData("https://example.com/?token=secret-value&x=1", "token=[REDACTED]", "secret-value", "x=1")]
    [InlineData("https://example.com/?password=hunter2", "password=[REDACTED]", "hunter2", null)]
    [InlineData("https://example.com/?access_token=eyJ0eXAi&keep=yes", "access_token=[REDACTED]", "eyJ0eXAi", "keep=yes")]
    public void Scrub_ShouldRedactCredentialBearingQueryParameters(
        string input,
        string expectedFragment,
        string leakedSecret,
        string? preservedFragment)
    {
        var sanitizer = new CrashReportSanitizer([]);

        var result = sanitizer.Scrub(input);

        Assert.Contains(expectedFragment, result, StringComparison.Ordinal);
        Assert.DoesNotContain(leakedSecret, result, StringComparison.Ordinal);
        if (preservedFragment is not null)
        {
            Assert.Contains(preservedFragment, result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Scrub_ShouldNotRedactOrdinaryUrlsWithoutCredentials()
    {
        var sanitizer = new CrashReportSanitizer([]);

        var result = sanitizer.Scrub("See https://example.com/docs and http://github.com/cwchanap/DTXManiaCX");

        Assert.Contains("https://example.com/docs", result, StringComparison.Ordinal);
        Assert.Contains("http://github.com/cwchanap/DTXManiaCX", result, StringComparison.Ordinal);
        Assert.DoesNotContain(CrashReportSanitizer.RedactedValue, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_ShouldIgnoreTriviallyShortSecrets()
    {
        // Short values are likely common substrings; the sanitizer must not redact them.
        var sanitizer = new CrashReportSanitizer([], ["abc", "12"]);

        var result = sanitizer.Scrub("alphabet and 12345 should remain visible");

        Assert.Equal("alphabet and 12345 should remain visible", result);
    }

    [Fact]
    public void Constructor_WithNullSecrets_ShouldNotThrow()
    {
        var sanitizer = new CrashReportSanitizer([], null);

        Assert.Equal("plain text", sanitizer.Scrub("plain text"));
    }
}
