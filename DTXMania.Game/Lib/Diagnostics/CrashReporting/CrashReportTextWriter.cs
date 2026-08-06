#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed record CrashReportDocument(
    CrashReportSummary Summary,
    Exception Exception,
    IReadOnlyList<CrashLogRecord> Logs,
    IReadOnlyList<CrashBreadcrumb> Breadcrumbs,
    IReadOnlyList<CrashContextSnapshot> Context,
    IReadOnlyList<string> SensitivePaths,
    IReadOnlyList<string> SensitiveSecrets);

internal interface ICrashReportArtifactWriter
{
    void Write(Stream destination, CrashReportDocument document);
}

/// <summary>
/// Serializes a crash report as a single plain-text file.
///
/// This writer does not filter values. Everything reaching it has already been normalized
/// against the allowlist in <see cref="CrashLogFieldPolicy"/> when it entered the in-memory
/// buffers, so the only work left here is layout plus size limits.
/// </summary>
internal sealed class CrashReportTextWriter : ICrashReportArtifactWriter
{
    internal const string Header = "DTXMANIACX-CRASH-REPORT 2";
    internal const string ExceptionSection = "--- EXCEPTION ---";
    internal const string ContextSection = "--- CONTEXT ---";
    internal const string BreadcrumbSection = "--- BREADCRUMBS ---";
    internal const string LogSection = "--- LOGS ---";

    private const int MaximumLogRecords = 500;
    private const int MaximumLogCharacters = 512 * 1024;
    private const int MaximumBreadcrumbs = 100;

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public void Write(Stream destination, CrashReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(document);

        var sanitizer = new CrashReportSanitizer(document.SensitivePaths, document.SensitiveSecrets);
        var exceptionText = sanitizer.SanitizeExceptionChain(document.Exception, out var exceptionTruncated);
        var logs = SelectLogs(document.Logs, sanitizer, out var logsTruncated);
        var breadcrumbs = SelectBreadcrumbs(document.Breadcrumbs, out var breadcrumbsTruncated);

        using var writer = new StreamWriter(destination, Utf8WithoutBom, bufferSize: 4096, leaveOpen: true)
        {
            NewLine = "\n"
        };

        var summary = document.Summary;
        writer.WriteLine(Header);
        writer.WriteLine("ReportId: " + summary.ReportId);
        writer.WriteLine("CapturedAtUtc: " + summary.CapturedAtUtc.ToUniversalTime().ToString("O"));
        writer.WriteLine("BuildId: " + summary.BuildId);
        writer.WriteLine("OperatingSystem: " + sanitizer.SanitizeMetadata(summary.OperatingSystem));
        writer.WriteLine("RuntimeVersion: "
            + sanitizer.SanitizeMetadata(RuntimeInformation.FrameworkDescription));
        writer.WriteLine("ProcessArchitecture: " + summary.ProcessArchitecture);
        writer.WriteLine("StageOrMilestone: " + summary.StageOrMilestone);
        writer.WriteLine("ExceptionType: " + summary.ExceptionType);
        writer.WriteLine("Truncated: exception=" + exceptionTruncated
            + " logs=" + logsTruncated
            + " breadcrumbs=" + breadcrumbsTruncated);

        writer.WriteLine();
        writer.WriteLine(ExceptionSection);
        writer.Write(exceptionText);
        if (!exceptionText.EndsWith('\n'))
        {
            writer.WriteLine();
        }

        writer.WriteLine();
        writer.WriteLine(ContextSection);
        foreach (var snapshot in document.Context)
        {
            writer.WriteLine(snapshot.Kind + " [" + snapshot.Status + "]"
                + (snapshot.FailureCode is null ? string.Empty : " " + snapshot.FailureCode));
            foreach (var field in snapshot.Fields)
            {
                writer.WriteLine("  " + field.Key + ": " + FormatValue(field.Value));
            }
        }

        writer.WriteLine();
        writer.WriteLine(BreadcrumbSection);
        foreach (var breadcrumb in breadcrumbs)
        {
            writer.WriteLine(
                breadcrumb.TimestampUtc.ToUniversalTime().ToString("O")
                + " " + breadcrumb.EventName
                + FormatProperties(breadcrumb.Properties));
        }

        writer.WriteLine();
        writer.WriteLine(LogSection);
        foreach (var record in logs)
        {
            writer.WriteLine(FormatLogRecord(record, sanitizer));
        }
    }

    private static string FormatLogRecord(CrashLogRecord record, CrashReportSanitizer sanitizer)
    {
        var builder = new StringBuilder()
            .Append(record.TimestampUtc.ToUniversalTime().ToString("O"))
            .Append(' ')
            .Append(record.LogLevel)
            .Append(" [")
            .Append(record.EventId.Id.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrEmpty(record.EventId.Name))
        {
            builder.Append(' ').Append(record.EventId.Name);
        }

        builder.Append(']');

        // Render the originating logger category when present so unclassified records still
        // identify their subsystem (graphics, input, JSON-RPC, …) instead of being anonymous.
        // The category is caller-controlled and never allowlisted, so it must be scrubbed
        // with the same sanitizer as exception messages — otherwise a registered secret (or
        // URI credentials) passed as a logger category would leak verbatim into the report.
        if (!string.IsNullOrEmpty(record.Category))
        {
            builder.Append(" [").Append(sanitizer.Scrub(record.Category)).Append(']');
        }

        builder.Append(' ').Append(record.MessageTemplate);

        if (record.ExceptionType is not null)
        {
            builder.Append(" exception=").Append(record.ExceptionType);
        }

        return builder.Append(FormatProperties(record.Properties)).ToString();
    }

    private static string FormatProperties(IReadOnlyDictionary<string, object?> properties)
    {
        if (properties.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var property in properties)
        {
            builder.Append(' ')
                .Append(property.Key)
                .Append('=')
                .Append(FormatValue(property.Value));
        }

        return builder.ToString();
    }

    private static string FormatValue(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O"),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O"),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        // Values are allowlisted scalars, but keep the line format intact regardless.
        return text.ReplaceLineEndings(" ");
    }

    private static IReadOnlyList<CrashLogRecord> SelectLogs(
        IReadOnlyList<CrashLogRecord> records,
        CrashReportSanitizer sanitizer,
        out bool truncated)
    {
        var selected = new List<CrashLogRecord>();
        var charactersUsed = 0;

        for (var index = records.Count - 1; index >= 0 && selected.Count < MaximumLogRecords; index--)
        {
            var length = FormatLogRecord(records[index], sanitizer).Length + 1;
            if (charactersUsed + length > MaximumLogCharacters)
            {
                break;
            }

            selected.Add(records[index]);
            charactersUsed += length;
        }

        selected.Reverse();
        truncated = selected.Count < records.Count;
        return selected;
    }

    private static IReadOnlyList<CrashBreadcrumb> SelectBreadcrumbs(
        IReadOnlyList<CrashBreadcrumb> breadcrumbs,
        out bool truncated)
    {
        var firstIncluded = Math.Max(0, breadcrumbs.Count - MaximumBreadcrumbs);
        truncated = firstIncluded > 0;

        var selected = new List<CrashBreadcrumb>(breadcrumbs.Count - firstIncluded);
        for (var index = firstIncluded; index < breadcrumbs.Count; index++)
        {
            selected.Add(breadcrumbs[index]);
        }

        return selected;
    }
}
