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

    // crash-<yyyyMMdd>-<HHmmss>Z-<hex>. The leading timestamp makes an ordinal sort
    // chronological, which is all retention needs — no need to open the files.
    private static readonly Regex ReportFileNameRegex = new(
        """\Acrash-\d{8}-\d{6}Z-[0-9a-f]{6}\.txt\z""",
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

            var staleReports = Directory
                .EnumerateFiles(_rootPath, "*", SearchOption.TopDirectoryOnly)
                .Where(static path => ReportFileNameRegex.IsMatch(Path.GetFileName(path)))
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Skip(MaximumRetainedReports);

            foreach (var path in staleReports)
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            WriteSafeError("crash_report_cleanup_failed");
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
}
