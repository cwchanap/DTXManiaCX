#nullable enable

using System;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class GitHubCrashIssueBuilderTests
{
    private const string ExpectedTarget = "https://github.com/cwchanap/DTXManiaCX/issues/new";

    [Fact]
    public void BuildIssueUrl_ShouldTargetTheExactNewIssueEndpointBase()
    {
        var summary = CreateSummary();

        var uri = GitHubCrashIssueBuilder.BuildIssueUrl(summary);

        Assert.Equal(ExpectedTarget, uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("github.com", uri.Host);
        Assert.True(uri.IsDefaultPort);
    }

    [Fact]
    public void BuildIssueUrl_ShouldAlwaysProduceATargetAllowedUri()
    {
        var summary = CreateSummary();

        var uri = GitHubCrashIssueBuilder.BuildIssueUrl(summary);

        Assert.True(GitHubCrashIssueBuilder.IsTargetAllowed(uri));
    }

    [Fact]
    public void BuildIssueUrl_ShouldEscapeEveryDynamicValue()
    {
        // Each value contains characters that, if not escaped, would break or reshape the URL:
        // '&' and '=' split query pairs, '#' starts a fragment, '+' decodes to space, and
        // spaces/newlines are illegal verbatim.
        var summary = CreateSummary() with
        {
            ReportId = "crash-20260807-010203Z-a1b2c3",
            BuildId = "1.2&3=beta",
            OperatingSystem = "Mac OS X #13",
            ProcessArchitecture = "Arm64+",
            StageOrMilestone = "Song&Select",
            ExceptionType = "System.Invalid#OperationException"
        };

        var uri = GitHubCrashIssueBuilder.BuildIssueUrl(summary);
        var raw = uri.AbsoluteUri;

        // None of the dangerous raw characters survive into the serialized URL.
        Assert.DoesNotContain("1.2&3=beta", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Mac OS X #13", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Song&Select", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Invalid#OperationException", raw, StringComparison.Ordinal);

        // The escaped encodings are present.
        Assert.Contains(Uri.EscapeDataString("1.2&3=beta"), raw, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Mac OS X #13"), raw, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Song&Select"), raw, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("System.Invalid#OperationException"), raw, StringComparison.Ordinal);

        // And the values round-trip through the standard unescape.
        Assert.Contains("Mac OS X #13", Uri.UnescapeDataString(uri.Query), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildIssueUrl_ShouldIncludeAllAllowedFieldsAndAttachmentGuidance()
    {
        var summary = CreateSummary() with
        {
            ReportId = "crash-20260807-010203Z-a1b2c3",
            BuildId = "9.9-beta",
            OperatingSystem = "Darwin 23",
            ProcessArchitecture = "Arm64",
            StageOrMilestone = "Title",
            ExceptionType = "System.NullReferenceException"
        };

        var uri = GitHubCrashIssueBuilder.BuildIssueUrl(summary);
        var decoded = Uri.UnescapeDataString(uri.Query);

        Assert.Contains(summary.ReportId, decoded, StringComparison.Ordinal);
        Assert.Contains(summary.BuildId, decoded, StringComparison.Ordinal);
        Assert.Contains(summary.OperatingSystem, decoded, StringComparison.Ordinal);
        Assert.Contains(summary.ProcessArchitecture, decoded, StringComparison.Ordinal);
        Assert.Contains(summary.StageOrMilestone, decoded, StringComparison.Ordinal);
        Assert.Contains(summary.ExceptionType, decoded, StringComparison.Ordinal);
        // Schema-v2 identifier is mandated by the inbox contract.
        Assert.Contains("schema v2", decoded, StringComparison.OrdinalIgnoreCase);
        // Manual .txt attachment instructions must be present.
        Assert.Contains(".txt", decoded, StringComparison.Ordinal);
        Assert.Contains(summary.FileName, decoded, StringComparison.Ordinal);

        // The allow-list is closed: the capture timestamp is NOT permitted in the body (it is
        // already encoded in the report id). Guard the boundary so it cannot slip back in.
        Assert.DoesNotContain("Captured", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildIssueUrl_WithNullSummary_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => GitHubCrashIssueBuilder.BuildIssueUrl(null!));
    }

    [Fact]
    public void IsTargetAllowed_WithTheCanonicalTarget_ShouldAccept()
    {
        Assert.True(GitHubCrashIssueBuilder.IsTargetAllowed(new Uri(ExpectedTarget)));
    }

    [Theory]
    [InlineData("http://github.com/cwchanap/DTXManiaCX/issues/new", false)]
    [InlineData("https://gitlab.com/cwchanap/DTXManiaCX/issues/new", false)]
    [InlineData("https://github.com/cwchanap/DTXManiaCX/issues", false)]
    [InlineData("https://github.com/cwchanap/DTXManiaCX/issues/new/edit", false)]
    [InlineData("https://github.com/cwchanap/DTXManiaCX/pulls", false)]
    [InlineData("https://github.com/other/DTXManiaCX/issues/new", false)]
    [InlineData("https://github.com/cwchanap/OtherRepo/issues/new", false)]
    [InlineData("https://github.com:8443/cwchanap/DTXManiaCX/issues/new", false)]
    public void IsTargetAllowed_WithOffTargetUri_ShouldReject(string uriString, bool expected)
    {
        Assert.Equal(expected, GitHubCrashIssueBuilder.IsTargetAllowed(new Uri(uriString)));
    }

    [Fact]
    public void IsTargetAllowed_WithNull_ShouldReject()
    {
        Assert.False(GitHubCrashIssueBuilder.IsTargetAllowed(null));
    }

    private static CrashReportSummary CreateSummary()
    {
        return new CrashReportSummary(
            "crash-20260807-010203Z-a1b2c3",
            new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero),
            "1.0.0",
            "Darwin 23.4.0",
            "Arm64",
            "Title",
            "System.InvalidOperationException",
            "crash-20260807-010203Z-a1b2c3.txt");
    }
}
