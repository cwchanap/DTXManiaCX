#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed record CrashCaptureData(
    Exception Exception,
    IReadOnlyList<CrashLogRecord> Logs,
    IReadOnlyList<CrashBreadcrumb> Breadcrumbs,
    IReadOnlyList<CrashContextSnapshot> Context,
    IReadOnlyList<string> SensitivePaths,
    IReadOnlyList<string> SensitiveSecrets);

internal sealed record CrashReportWriteResult(
    CrashReportSummary? Report,
    string? FailureCode);

internal sealed class CrashReportStore
{
    internal const string ReportExtension = ".txt";

    private const int MaximumRetainedReports = 5;
    private static readonly TimeSpan TemporaryFileLifetime = TimeSpan.FromHours(24);

    // One retained-name policy shared by discovery, retention, acknowledgement, and delete.
    // Accepts both the pending "crash-<date>-<time>Z-<hex>.txt" name and its acknowledged
    // "crash-<date>-<time>Z-<hex>.ack.txt" twin. The leading timestamp makes an ordinal
    // sort chronological, which is all retention/discovery ordering needs — no body reads.
    internal static readonly Regex RetainedReportFileNameRegex = new(
        """\Acrash-\d{8}-\d{6}Z-[0-9a-f]{6}(?:\.ack)?\.txt\z""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private readonly string _rootPath;
    private readonly ICrashReportArtifactWriter _writer;
    private readonly TimeProvider _timeProvider;
    private readonly TextWriter _errorWriter;
    private readonly CrashReportSummaryReader _summaryReader = new();

    internal string RootPath => _rootPath;

    internal static bool IsRetainedReport(string fileName)
    {
        return RetainedReportFileNameRegex.IsMatch(Path.GetFileName(fileName));
    }

    internal CrashReportStore(
        string rootPath,
        ICrashReportArtifactWriter writer,
        TimeProvider timeProvider,
        TextWriter errorWriter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _errorWriter = errorWriter ?? throw new ArgumentNullException(nameof(errorWriter));
    }

    internal CrashReportWriteResult Capture(CrashCaptureData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var capturedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var reportId = CreateReportId(capturedAtUtc);

        try
        {
            Directory.CreateDirectory(_rootPath);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return CreateFailure(GetFailureCode(exception));
        }

        var summary = CreateSummary(reportId, capturedAtUtc, data);
        var document = new CrashReportDocument(
            summary,
            data.Exception,
            data.Logs,
            data.Breadcrumbs,
            data.Context,
            data.SensitivePaths,
            data.SensitiveSecrets);

        if (!TryWriteReport(document, out var failureCode))
        {
            return CreateFailure(failureCode ?? "capture_io_failure");
        }

        Cleanup();
        return new CrashReportWriteResult(summary, FailureCode: null);
    }

    internal void Cleanup()
    {
        try
        {
            if (!Directory.Exists(_rootPath))
            {
                return;
            }

            var cutoff = _timeProvider.GetUtcNow().UtcDateTime.Subtract(TemporaryFileLifetime);
            foreach (var path in Directory.EnumerateFiles(_rootPath, "*.tmp", SearchOption.TopDirectoryOnly))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }

            // Latest-five retention operates on LOGICAL report ids: a pending/ack pair for
            // one id counts as a single report, and stale ids have all of their variants deleted.
            // Comparison is case-insensitive to match the case-insensitive retained-name regex.
            var retained = Directory
                .EnumerateFiles(_rootPath, "*", SearchOption.TopDirectoryOnly)
                .Where(static path => RetainedReportFileNameRegex.IsMatch(Path.GetFileName(path)))
                .Select(static path =>
                    new RetainedEntry(path, CrashReportSummaryReader.GetLogicalReportId(Path.GetFileName(path))))
                .ToList();

            if (retained.Count <= MaximumRetainedReports)
            {
                return;
            }

            var staleIds = retained
                .Select(static entry => entry.LogicalId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static id => id, StringComparer.OrdinalIgnoreCase)
                .Skip(MaximumRetainedReports)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in retained)
            {
                if (staleIds.Contains(entry.LogicalId))
                {
                    File.Delete(entry.Path);
                }
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            WriteSafeError("crash_report_cleanup_failed");
        }
    }

    internal IReadOnlyList<CrashReportInboxItem> DiscoverReports()
    {
        var results = new List<CrashReportInboxItem>();

        try
        {
            if (!Directory.Exists(_rootPath))
            {
                return results;
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return results;
        }

        // Keyed case-insensitively so mixed-case variants of one logical id collapse into a
        // single group, matching the case-insensitive retained-name regex.
        var groups = new Dictionary<string, LogicalReportGroup>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_rootPath, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName;
                try
                {
                    fileName = Path.GetFileName(path);
                }
                catch (Exception exception) when (IsExpectedFileSystemException(exception))
                {
                    continue;
                }

                if (!RetainedReportFileNameRegex.IsMatch(fileName))
                {
                    continue;
                }

                var logicalId = CrashReportSummaryReader.GetLogicalReportId(fileName);
                if (string.IsNullOrEmpty(logicalId))
                {
                    continue;
                }

                if (!groups.TryGetValue(logicalId, out var group))
                {
                    group = new LogicalReportGroup(logicalId);
                    groups[logicalId] = group;
                }

                if (fileName.EndsWith(".ack" + ReportExtension, StringComparison.OrdinalIgnoreCase))
                {
                    group.AcknowledgedPath ??= path;
                }
                else
                {
                    group.PendingPath ??= path;
                }
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return results;
        }

        // Order by logical id (filename), newest first — never by reading the report body.
        // Case-insensitive so the ordering agrees with the case-insensitive grouping above.
        foreach (var group in groups.Values.OrderByDescending(static group => group.LogicalId, StringComparer.OrdinalIgnoreCase))
        {
            var physicalPath = group.PendingPath ?? group.AcknowledgedPath;
            if (physicalPath is null)
            {
                continue;
            }

            // Pending wins while both variants exist.
            var isAcknowledged = group.PendingPath is null;

            CrashReportSummary summary;
            try
            {
                summary = _summaryReader.Read(physicalPath);
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                continue;
            }

            results.Add(new CrashReportInboxItem(summary, isAcknowledged));
        }

        return results;
    }

    internal CrashReportActionResult Acknowledge(string reportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);

        try
        {
            // Resolve the ACTUAL physical variants on disk rather than reconstructing a path
            // from the report id: on a case-sensitive volume the on-disk name may not share
            // the caller's casing, so enumerate and match by logical id (case-insensitively,
            // like the retained-name regex). On-disk names are left whatever they are.
            string? pendingPath = null;
            string? acknowledgedPath = null;

            if (Directory.Exists(_rootPath))
            {
                foreach (var path in EnumerateRetainedVariants(reportId))
                {
                    var fileName = Path.GetFileName(path);
                    if (fileName.EndsWith(".ack" + ReportExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        acknowledgedPath ??= path;
                    }
                    else
                    {
                        pendingPath ??= path;
                    }
                }
            }

            // Already-acknowledged (or never captured) reports are an idempotent success.
            if (pendingPath is null)
            {
                return new CrashReportActionResult(Succeeded: true);
            }

            // Derive the ack twin from the pending file's actual on-disk name so its casing is
            // preserved (insert ".ack" before the final extension).
            var pendingName = Path.GetFileName(pendingPath);
            var lastDot = pendingName.LastIndexOf('.');
            var destination = acknowledgedPath
                ?? Path.Combine(
                    _rootPath,
                    lastDot >= 0
                        ? pendingName[..lastDot] + ".ack" + pendingName[lastDot..]
                        : pendingName + ".ack" + ReportExtension);

            File.Move(pendingPath, destination, overwrite: true);
            return new CrashReportActionResult(Succeeded: true);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            WriteSafeError("crash_report_acknowledge_failed");
            return new CrashReportActionResult(Succeeded: false, ErrorCode: "acknowledge_io_failure");
        }
    }

    internal CrashReportActionResult DeleteReport(string reportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);

        try
        {
            if (!Directory.Exists(_rootPath))
            {
                return new CrashReportActionResult(Succeeded: true);
            }

            // Delete every physical variant whose logical id matches, regardless of on-disk
            // casing (a pending/ack pair for one logical id is removed together).
            foreach (var path in EnumerateRetainedVariants(reportId))
            {
                File.Delete(path);
            }

            return new CrashReportActionResult(Succeeded: true);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            WriteSafeError("crash_report_delete_failed");
            return new CrashReportActionResult(Succeeded: false, ErrorCode: "delete_io_failure");
        }
    }

    /// <summary>
    /// Enumerates retained report files whose logical id matches <paramref name="reportId"/>
    /// using case-insensitive comparison, so physical variants are found regardless of on-disk
    /// casing (correct on case-sensitive as well as case-insensitive volumes).
    /// </summary>
    private IEnumerable<string> EnumerateRetainedVariants(string reportId)
    {
        foreach (var path in Directory.EnumerateFiles(_rootPath, "*", SearchOption.TopDirectoryOnly))
        {
            string fileName;
            try
            {
                fileName = Path.GetFileName(path);
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                continue;
            }

            if (!RetainedReportFileNameRegex.IsMatch(fileName))
            {
                continue;
            }

            if (string.Equals(
                    CrashReportSummaryReader.GetLogicalReportId(fileName),
                    reportId,
                    StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private bool TryWriteReport(CrashReportDocument document, out string? failureCode)
    {
        var temporaryPath = Path.Combine(_rootPath, "." + document.Summary.FileName + ".tmp");
        var finalPath = Path.Combine(_rootPath, document.Summary.FileName);
        var temporaryFileCreated = false;
        var movedToFinal = false;

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       options: FileOptions.WriteThrough))
            {
                temporaryFileCreated = true;
                _writer.Write(stream, document);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            movedToFinal = true;
            failureCode = null;
            return true;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            failureCode = GetFailureCode(exception);
            return false;
        }
        finally
        {
            // Delete the temporary file unless it was successfully moved to its final name.
            // The catch above only handles filesystem exceptions; a non-filesystem failure
            // from _writer.Write (e.g. InvalidOperationException) propagates, but the
            // finally block still removes the .tmp file so it cannot linger on disk.
            if (temporaryFileCreated && !movedToFinal)
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private static CrashReportSummary CreateSummary(
        string reportId,
        DateTimeOffset capturedAtUtc,
        CrashCaptureData data)
    {
        return new CrashReportSummary(
            reportId,
            capturedAtUtc,
            GetBuildId(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            GetStageOrMilestone(data.Context),
            data.Exception.GetType().FullName ?? data.Exception.GetType().Name,
            reportId + ReportExtension);
    }

    /// <summary>
    /// Reports the most specific location the crash can be attributed to: the active stage
    /// when one exists, otherwise the furthest startup milestone reached.
    /// </summary>
    private static string GetStageOrMilestone(IReadOnlyList<CrashContextSnapshot> context)
    {
        if (context is null)
        {
            return "Unknown";
        }

        string? startupMilestone = null;
        foreach (var snapshot in context)
        {
            if (snapshot is null
                || snapshot.Status != CrashContextStatus.Available
                || snapshot.Fields is not { } fields)
            {
                continue;
            }

            if (snapshot.Kind == CrashContextKind.Stage
                && fields.TryGetValue("Stage", out var stage)
                && stage is StageType stageValue
                && Enum.IsDefined(stageValue)
                && stageValue != StageType.Startup)
            {
                return stageValue.ToString();
            }

            if (startupMilestone is null
                && snapshot.Kind == CrashContextKind.Startup
                && fields.TryGetValue("Milestone", out var milestone)
                && milestone is StartupCriticalPathMilestone milestoneValue
                && Enum.IsDefined(milestoneValue))
            {
                startupMilestone = milestoneValue.ToString();
            }
        }

        return startupMilestone ?? "Unknown";
    }

    private static string GetBuildId()
    {
        var assembly = typeof(CrashReportStore).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "Unknown"
            : informationalVersion;
    }

    private static string CreateReportId(DateTimeOffset capturedAtUtc)
    {
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        return "crash-"
            + capturedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "Z-"
            + suffix;
    }

    private CrashReportWriteResult CreateFailure(string failureCode)
    {
        WriteSafeError("crash_report_capture_failed:" + failureCode);
        return new CrashReportWriteResult(null, failureCode);
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or SecurityException;
    }

    private static string GetFailureCode(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException or SecurityException => "capture_access_denied",
            ArgumentException or NotSupportedException or PathTooLongException => "capture_path_failure",
            _ => "capture_io_failure"
        };
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
        }
    }

    private void WriteSafeError(string code)
    {
        try
        {
            _errorWriter.WriteLine(code);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private sealed class LogicalReportGroup
    {
        internal string LogicalId { get; }
        internal string? PendingPath { get; set; }
        internal string? AcknowledgedPath { get; set; }

        internal LogicalReportGroup(string logicalId)
        {
            LogicalId = logicalId;
        }
    }

    private readonly record struct RetainedEntry(string Path, string LogicalId);
}
