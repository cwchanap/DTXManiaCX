#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed record CrashCaptureData(
    Exception Exception,
    IReadOnlyList<CrashLogRecord> Logs,
    IReadOnlyList<CrashBreadcrumb> Breadcrumbs,
    IReadOnlyList<CrashContextSnapshot> Context,
    IReadOnlyList<string> SensitivePaths);

internal sealed record CrashReportWriteResult(
    CrashReportSummary? Report,
    bool UsedEmergencyFallback,
    string? FailureCode);

internal sealed class CrashReportStore
{
    private const int MaximumCompletedReports = 5;
    private static readonly TimeSpan TemporaryFileLifetime = TimeSpan.FromHours(24);
    private static readonly Regex ReportFileNameRegex = new(
        """\A(?<id>crash-(?<date>\d{8})-(?<time>\d{6})Z-(?<suffix>[0-9a-f]{6}))\.(?<extension>zip|txt)\z""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private readonly string _rootPath;
    private readonly ICrashReportArtifactWriter _writer;
    private readonly TimeProvider _timeProvider;
    private readonly TextWriter _errorWriter;

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

        var zipSummary = CreateSummary(reportId, capturedAtUtc, CrashReportFormat.ZipBundle, data);
        var zipDocument = CreateDocument(zipSummary, data);
        if (TryWriteArtifact(zipDocument, _writer.WriteZip, out var zipFailureCode))
        {
            Cleanup();
            return new CrashReportWriteResult(zipSummary, UsedEmergencyFallback: false, FailureCode: null);
        }

        var emergencySummary = CreateSummary(reportId, capturedAtUtc, CrashReportFormat.EmergencyText, data);
        var emergencyDocument = CreateDocument(emergencySummary, data);
        if (TryWriteArtifact(emergencyDocument, _writer.WriteEmergencyText, out var emergencyFailureCode))
        {
            Cleanup();
            return new CrashReportWriteResult(emergencySummary, UsedEmergencyFallback: true, FailureCode: null);
        }

        return CreateFailure(emergencyFailureCode ?? zipFailureCode ?? "capture_io_failure");
    }

    internal IReadOnlyList<CrashReportSummary> DiscoverCompletedReports()
    {
        try
        {
            if (!Directory.Exists(_rootPath))
            {
                return Array.Empty<CrashReportSummary>();
            }

            var reports = new List<CrashReportSummary>();
            foreach (var path in Directory.EnumerateFiles(_rootPath, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryReadZipSummary(path, fileName, out var summary))
                    {
                        reports.Add(summary);
                    }
                }
                else if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                         && TryReadEmergencySummary(path, fileName, out var summary))
                {
                    reports.Add(summary);
                }
            }

            return reports
                .OrderByDescending(static report => report.CapturedAtUtc)
                .ThenByDescending(static report => report.FileName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception)
                                          || exception is FormatException or OverflowException)
        {
            WriteSafeError("crash_report_discovery_failed");
            return Array.Empty<CrashReportSummary>();
        }
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

            var reports = DiscoverCompletedReports();
            foreach (var report in reports.Skip(MaximumCompletedReports))
            {
                File.Delete(Path.Combine(_rootPath, report.FileName));
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            WriteSafeError("crash_report_cleanup_failed");
        }
    }

    private bool TryWriteArtifact(
        CrashReportDocument document,
        Action<Stream, CrashReportDocument> writeArtifact,
        out string? failureCode)
    {
        var temporaryPath = Path.Combine(_rootPath, "." + document.Summary.FileName + ".tmp");
        var finalPath = Path.Combine(_rootPath, document.Summary.FileName);
        var temporaryFileCreated = false;

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
                writeArtifact(stream, document);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            failureCode = null;
            return true;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception)
                                          || exception is JsonException)
        {
            if (temporaryFileCreated)
            {
                DeleteTemporaryFile(temporaryPath);
            }

            failureCode = GetFailureCode(exception);
            return false;
        }
    }

    private bool TryReadZipSummary(string path, string fileName, out CrashReportSummary summary)
    {
        summary = default!;
        if (!TryParseReportFileName(fileName, out var fallback))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var reportEntry = archive.GetEntry("report.json");
            if (reportEntry is null)
            {
                summary = CreateUnknownSummary(fallback, CrashReportFormat.ZipBundle, fileName);
                return true;
            }

            using var reportStream = reportEntry.Open();
            using var document = JsonDocument.Parse(reportStream);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var schemaVersionValue)
                || schemaVersionValue != 1
                || !TryGetString(root, "reportId", out var reportId)
                || !TryGetDateTimeOffset(root, "capturedAtUtc", out var capturedAtUtc)
                || !TryGetString(root, "buildId", out var buildId)
                || !TryGetString(root, "operatingSystem", out var operatingSystem)
                || !TryGetString(root, "processArchitecture", out var processArchitecture)
                || !TryGetString(root, "stageOrMilestone", out var stageOrMilestone)
                || !TryGetString(root, "exceptionType", out var exceptionType))
            {
                summary = CreateUnknownSummary(fallback, CrashReportFormat.ZipBundle, fileName);
                return true;
            }

            if (!MatchesFileName(fallback, reportId, capturedAtUtc))
            {
                summary = CreateUnknownSummary(fallback, CrashReportFormat.ZipBundle, fileName);
                return true;
            }

            summary = SanitizeDiscoveredSummary(
                reportId,
                capturedAtUtc,
                buildId,
                operatingSystem,
                processArchitecture,
                stageOrMilestone,
                exceptionType,
                CrashReportFormat.ZipBundle,
                fileName);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or JsonException
                                          or FormatException
                                          or OverflowException)
        {
            summary = CreateUnknownSummary(fallback, CrashReportFormat.ZipBundle, fileName);
            return true;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return false;
        }
    }

    private bool TryReadEmergencySummary(string path, string fileName, out CrashReportSummary summary)
    {
        summary = default!;
        if (!TryParseReportFileName(fileName, out var fallback))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            if (!string.Equals(reader.ReadLine(), "DTXMANIACX-CRASH-REPORT 1", StringComparison.Ordinal))
            {
                summary = CreateUnknownSummary(fallback, CrashReportFormat.EmergencyText, fileName);
                return true;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var reachedDelimiter = false;
            for (var lineCount = 0; lineCount < 16; lineCount++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                if (string.Equals(line, "---", StringComparison.Ordinal))
                {
                    reachedDelimiter = true;
                    break;
                }

                var separatorIndex = line.IndexOf(": ", StringComparison.Ordinal);
                if (separatorIndex > 0)
                {
                    fields[line[..separatorIndex]] = line[(separatorIndex + 2)..];
                }
            }

            if (!reachedDelimiter
                || !fields.TryGetValue("ReportId", out var reportId)
                || !fields.TryGetValue("CapturedAtUtc", out var capturedAtText)
                || !DateTimeOffset.TryParse(
                    capturedAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var capturedAtUtc)
                || !fields.TryGetValue("BuildId", out var buildId)
                || !fields.TryGetValue("OperatingSystem", out var operatingSystem)
                || !fields.TryGetValue("ProcessArchitecture", out var processArchitecture)
                || !fields.TryGetValue("StageOrMilestone", out var stageOrMilestone)
                || !fields.TryGetValue("ExceptionType", out var exceptionType))
            {
                summary = CreateUnknownSummary(fallback, CrashReportFormat.EmergencyText, fileName);
                return true;
            }

            if (!MatchesFileName(fallback, reportId, capturedAtUtc))
            {
                summary = CreateUnknownSummary(fallback, CrashReportFormat.EmergencyText, fileName);
                return true;
            }

            summary = SanitizeDiscoveredSummary(
                reportId,
                capturedAtUtc,
                buildId,
                operatingSystem,
                processArchitecture,
                stageOrMilestone,
                exceptionType,
                CrashReportFormat.EmergencyText,
                fileName);
            return true;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return false;
        }
    }

    private CrashReportSummary CreateSummary(
        string reportId,
        DateTimeOffset capturedAtUtc,
        CrashReportFormat format,
        CrashCaptureData data)
    {
        var extension = format == CrashReportFormat.ZipBundle ? ".zip" : ".txt";
        return new CrashReportSummary(
            reportId,
            capturedAtUtc,
            GetBuildId(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            GetStageOrMilestone(data.Context),
            GetExceptionTypeName(data.Exception),
            format,
            reportId + extension);
    }

    private static CrashReportDocument CreateDocument(CrashReportSummary summary, CrashCaptureData data)
    {
        return new CrashReportDocument(
            summary,
            data.Exception,
            data.Logs,
            data.Breadcrumbs,
            data.Context,
            data.SensitivePaths);
    }

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

    private static string GetExceptionTypeName(Exception exception)
    {
        return exception.GetType().FullName ?? exception.GetType().Name;
    }

    private static string GetBuildId()
    {
        var assembly = typeof(CrashReportStore).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private static string CreateReportId(DateTimeOffset capturedAtUtc)
    {
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        return "crash-" + capturedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z-" + suffix;
    }

    private CrashReportWriteResult CreateFailure(string failureCode)
    {
        WriteSafeError("crash_report_capture_failed:" + failureCode);
        return new CrashReportWriteResult(null, UsedEmergencyFallback: false, failureCode);
    }

    private static CrashReportSummary SanitizeDiscoveredSummary(
        string reportId,
        DateTimeOffset capturedAtUtc,
        string buildId,
        string operatingSystem,
        string processArchitecture,
        string stageOrMilestone,
        string exceptionType,
        CrashReportFormat format,
        string fileName)
    {
        var sanitizer = new CrashReportSanitizer([]);
        return new CrashReportSummary(
            sanitizer.SanitizeStableLabel(reportId, CrashReportSanitizer.RedactedValue),
            capturedAtUtc.ToUniversalTime(),
            sanitizer.SanitizeStableLabel(buildId, CrashReportSanitizer.RedactedValue),
            sanitizer.SanitizeMetadata(operatingSystem),
            sanitizer.SanitizeStableLabel(processArchitecture),
            sanitizer.SanitizeStableLabel(stageOrMilestone),
            sanitizer.SanitizeTypeName(exceptionType),
            format,
            fileName);
    }

    private static CrashReportSummary CreateUnknownSummary(
        ParsedReportFileName file,
        CrashReportFormat format,
        string fileName)
    {
        return new CrashReportSummary(
            file.ReportId,
            file.CapturedAtUtc,
            "Unknown",
            "Unknown",
            "Unknown",
            "Unknown",
            "Unknown",
            format,
            fileName);
    }

    private static bool MatchesFileName(
        ParsedReportFileName file,
        string reportId,
        DateTimeOffset capturedAtUtc)
    {
        return string.Equals(reportId, file.ReportId, StringComparison.Ordinal)
            && string.Equals(
                capturedAtUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
                file.CapturedAtUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private static bool TryParseReportFileName(string fileName, out ParsedReportFileName parsed)
    {
        parsed = default;
        var match = ReportFileNameRegex.Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        var timestampText = match.Groups["date"].Value + "-" + match.Groups["time"].Value + "Z";
        if (!DateTimeOffset.TryParseExact(
                timestampText,
                "yyyyMMdd-HHmmss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var capturedAtUtc))
        {
            return false;
        }

        parsed = new ParsedReportFileName(match.Groups["id"].Value, capturedAtUtc);
        return true;
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetDateTimeOffset(JsonElement root, string name, out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out value);
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or SecurityException
            or InvalidDataException;
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
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private readonly record struct ParsedReportFileName(string ReportId, DateTimeOffset CapturedAtUtc);
}
