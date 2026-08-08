#nullable enable

using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// Production <see cref="ICrashReportInbox"/>: orchestrates the crash-report <see cref="CrashReportStore"/>,
/// the deterministic <see cref="GitHubCrashIssueBuilder"/>, and the cross-platform
/// <see cref="ExternalLauncher"/> handoff.
///
/// Every action resolves a report by id at call time. No caller-supplied path ever reaches the
/// launcher: the GitHub target is always the validated, builder-produced URI, and the folder
/// target is always the store's own root. Launch-backed actions follow
/// <c>resolve -> build/validate -> launch -> acknowledge</c>, and only a successful launch
/// acknowledges (never deletes). Raw filesystem/process exceptions are absorbed by the store
/// and launcher respectively; the inbox never re-surfaces them and maps anything unexpected to
/// a stable code.
/// </summary>
internal sealed class CrashReportInbox : ICrashReportInbox
{
    private const string ReportNotFoundCode = "report_not_found";
    private const string UnexpectedFailureCode = "inbox_unexpected_failure";

    private readonly CrashReportStore _store;
    private readonly IExternalLauncher _launcher;

    internal CrashReportInbox(CrashReportStore store, IExternalLauncher launcher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public IReadOnlyList<CrashReportInboxItem> GetReports()
    {
        try
        {
            return _store.DiscoverReports();
        }
        catch (Exception)
        {
            // DiscoverReports never throws in practice (it absorbs filesystem errors), but the
            // inbox must never leak an exception to a UI caller.
            return Array.Empty<CrashReportInboxItem>();
        }
    }

    public CrashReportActionResult OpenGitHubIssue(string reportId)
    {
        var summary = Resolve(reportId);
        if (summary is null)
        {
            return Failure(ReportNotFoundCode);
        }

        return RunOrchestration(() =>
        {
            var uri = GitHubCrashIssueBuilder.BuildIssueUrl(summary);
            var launch = _launcher.LaunchUri(uri);
            return launch.Succeeded ? _store.Acknowledge(reportId) : launch;
        });
    }

    public CrashReportActionResult OpenReportFolder(string reportId)
    {
        var summary = Resolve(reportId);
        if (summary is null)
        {
            return Failure(ReportNotFoundCode);
        }

        return RunOrchestration(() =>
        {
            var launch = _launcher.LaunchFolder(_store.RootPath);
            return launch.Succeeded ? _store.Acknowledge(reportId) : launch;
        });
    }

    public CrashReportActionResult Dismiss(string reportId)
    {
        var summary = Resolve(reportId);
        if (summary is null)
        {
            return Failure(ReportNotFoundCode);
        }

        // Dismiss acknowledges the report WITHOUT launching an external handler or deleting it.
        return RunOrchestration(() => _store.Acknowledge(reportId));
    }

    public CrashReportActionResult Delete(string reportId)
    {
        var summary = Resolve(reportId);
        if (summary is null)
        {
            return Failure(ReportNotFoundCode);
        }

        return RunOrchestration(() => _store.DeleteReport(reportId));
    }

    private CrashReportSummary? Resolve(string reportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        foreach (var item in _store.DiscoverReports())
        {
            if (string.Equals(item.Summary.ReportId, reportId, StringComparison.Ordinal))
            {
                return item.Summary;
            }
        }

        return null;
    }

    private static CrashReportActionResult RunOrchestration(Func<CrashReportActionResult> orchestration)
    {
        try
        {
            return orchestration();
        }
        catch (Exception)
        {
            // The store and launcher absorb their own exceptions; reaching here is an unexpected
            // bug. Map to a stable code rather than propagating a raw message to the UI.
            return Failure(UnexpectedFailureCode);
        }
    }

    private static CrashReportActionResult Failure(string code) => new(Succeeded: false, ErrorCode: code);
}
