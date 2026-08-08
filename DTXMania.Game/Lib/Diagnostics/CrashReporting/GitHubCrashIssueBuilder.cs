#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// Builds the deterministic, validated "new GitHub issue" URL for a captured crash report.
/// Side-effect free and fully deterministic: the destination is fixed and cannot be supplied
/// by the caller. Every dynamic value is URI-escaped before it enters the query string, and
/// the resulting <see cref="Uri"/> is re-checked against the fixed target before it is handed
/// back so a malformed build can never hand an off-target URL to the launcher.
/// </summary>
internal static class GitHubCrashIssueBuilder
{
    internal const string TargetScheme = "https";
    internal const string TargetHost = "github.com";
    internal const string TargetOwner = "cwchanap";
    internal const string TargetRepository = "DTXManiaCX";
    internal const string TargetAbsolutePath = "/" + TargetOwner + "/" + TargetRepository + "/issues/new";

    // The exact base the inbox contract mandates; query parameters are appended on top of it.
    internal const string TargetBaseUrl = TargetScheme + "://" + TargetHost + TargetAbsolutePath;

    internal static Uri BuildIssueUrl(CrashReportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var title = "Crash report: " + summary.ReportId;
        var body = BuildBody(summary);

        // Each value is escaped on its own so a '&'/'='/'#' inside a value can never split or
        // fragment the query. UriBuilder is then given an already-escaped query and a fixed
        // host/path, so it performs no further value substitution.
        var escapedQuery = "title=" + Uri.EscapeDataString(title)
            + "&body=" + Uri.EscapeDataString(body);

        var builder = new UriBuilder
        {
            Scheme = TargetScheme,
            Host = TargetHost,
            Port = -1,
            Path = TargetAbsolutePath,
            Query = escapedQuery
        };

        var uri = builder.Uri;
        if (!IsTargetAllowed(uri))
        {
            // The builder is fully deterministic; reaching this branch means the construction
            // itself regressed. Fail loudly here so the launcher never receives a bad target.
            throw new InvalidOperationException(
                "Constructed GitHub issue URL failed target validation: " + uri);
        }

        return uri;
    }

    /// <summary>
    /// Validates a candidate issue URL against the fixed HTTPS host/path. Used both internally
    /// (to re-check the builder output) and by tests to prove the target is enforced: only the
    /// canonical owner/repo/issues/new path on github.com over HTTPS is accepted.
    /// </summary>
    internal static bool IsTargetAllowed(Uri? uri)
    {
        return uri is not null
            && string.Equals(uri.Scheme, TargetScheme, StringComparison.Ordinal)
            && uri.IsDefaultPort
            && string.Equals(uri.Host, TargetHost, StringComparison.Ordinal)
            && string.Equals(uri.AbsolutePath, TargetAbsolutePath, StringComparison.Ordinal);
    }

    private static string BuildBody(CrashReportSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("A DTXManiaCX crash report (schema v2) was captured.");
        builder.AppendLine("Please review the details below and attach the report file to this issue.");
        builder.AppendLine();
        builder.AppendLine("## Report");
        builder.AppendLine("- Report ID: " + summary.ReportId);
        builder.AppendLine("- Captured (UTC): " + summary.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendLine("- Build ID: " + summary.BuildId);
        builder.AppendLine("- Operating system: " + summary.OperatingSystem);
        builder.AppendLine("- Architecture: " + summary.ProcessArchitecture);
        builder.AppendLine("- Stage / milestone: " + summary.StageOrMilestone);
        builder.AppendLine("- Exception type: " + summary.ExceptionType);
        builder.AppendLine();
        builder.AppendLine("## How to attach the report");
        builder.AppendLine("1. Locate the crash report file named " + summary.FileName + " (a .txt file) on this machine.");
        builder.AppendLine("2. Drag-and-drop or browse to attach it to this issue before submitting.");
        builder.AppendLine();
        builder.AppendLine("## Reproduction");
        builder.AppendLine("(Optional) Describe what you were doing when the crash occurred.");

        return builder.ToString();
    }
}
