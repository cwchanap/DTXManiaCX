#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed record CrashReportDocument(
    CrashReportSummary Summary,
    Exception Exception,
    IReadOnlyList<CrashLogRecord> Logs,
    IReadOnlyList<CrashBreadcrumb> Breadcrumbs,
    IReadOnlyList<CrashContextSnapshot> Context,
    IReadOnlyList<string> SensitivePaths);

internal interface ICrashReportArtifactWriter
{
    void WriteZip(Stream destination, CrashReportDocument document);
    void WriteEmergencyText(Stream destination, CrashReportDocument document);
}

internal sealed class CrashReportArchiveWriter : ICrashReportArtifactWriter
{
    private const int SchemaVersion = 1;
    private const int MaximumLogRecords = 500;
    private const int MaximumLogBytes = 512 * 1024;
    private const int MaximumBreadcrumbs = 100;
    private const int MinimumReportedWidth = 320;
    private const int MinimumReportedHeight = 200;
    private const int MaximumReportedDimension = 16_384;
    private const int MaximumReportedCount = 100_000;
    private const int MaximumBufferMilliseconds = 60_000;
    private const int MaximumMultiSampleCount = 64;

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private static readonly HashSet<string> KnownBreadcrumbEvents = new(StringComparer.Ordinal)
    {
        "process_started",
        "initialization_milestone_reached",
        "stage_transition_requested",
        "stage_transition_started",
        "stage_transition_completed",
        "stage_transition_rejected",
        "configuration_opened",
        "configuration_closed",
        "graphics_settings_changed",
        "graphics_device_lost",
        "graphics_device_reset",
        "audio_device_attached",
        "audio_device_detached",
        "audio_device_selected",
        "midi_device_attached",
        "midi_device_detached",
        "midi_device_selected",
        "midi_device_count_changed",
        "song_selection_entered",
        "gameplay_entered",
        "exit_requested"
    };
    private static readonly IReadOnlyDictionary<string, object?> EmptyProperties =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public void WriteZip(Stream destination, CrashReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(document);

        var sanitized = Sanitize(document);
        using var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true,
            entryNameEncoding: Utf8WithoutBom);

        WriteJsonEntry(archive, "report.json", CreateReportDocument(sanitized), IndentedJsonOptions);
        WriteTextEntry(archive, "exception.txt", sanitized.ExceptionText);
        WriteLogsEntry(archive, sanitized.Logs);
        WriteJsonEntry(archive, "breadcrumbs.json", sanitized.Breadcrumbs, IndentedJsonOptions);
        WriteTextEntry(archive, "README.txt", CreateReadme());
    }

    public void WriteEmergencyText(Stream destination, CrashReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(document);

        var sanitized = Sanitize(document);
        using var writer = new StreamWriter(
            destination,
            Utf8WithoutBom,
            bufferSize: 1024,
            leaveOpen: true)
        {
            NewLine = "\n"
        };

        writer.WriteLine("DTXMANIACX-CRASH-REPORT 1");
        writer.WriteLine("ReportId: " + sanitized.Summary.ReportId);
        writer.WriteLine("CapturedAtUtc: " + sanitized.Summary.CapturedAtUtc.ToUniversalTime().ToString("O"));
        writer.WriteLine("BuildId: " + sanitized.Summary.BuildId);
        writer.WriteLine("OperatingSystem: " + sanitized.Summary.OperatingSystem);
        writer.WriteLine("ProcessArchitecture: " + sanitized.Summary.ProcessArchitecture);
        writer.WriteLine("StageOrMilestone: " + sanitized.Summary.StageOrMilestone);
        writer.WriteLine("ExceptionType: " + sanitized.Summary.ExceptionType);
        writer.WriteLine("---");
        writer.Write(sanitized.ExceptionText);

        if (!sanitized.ExceptionText.EndsWith('\n'))
        {
            writer.WriteLine();
        }

        writer.WriteLine("IncludedLogRecords: " + sanitized.Logs.Count);
        writer.WriteLine("IncludedBreadcrumbs: " + sanitized.Breadcrumbs.Count);
    }

    private static SanitizedDocument Sanitize(CrashReportDocument document)
    {
        var sanitizer = new CrashReportSanitizer(document.SensitivePaths);
        var summary = new SanitizedSummary(
            sanitizer.SanitizeStableLabel(document.Summary.ReportId, CrashReportSanitizer.RedactedValue),
            document.Summary.CapturedAtUtc.ToUniversalTime(),
            sanitizer.SanitizeStableLabel(document.Summary.BuildId, CrashReportSanitizer.RedactedValue),
            sanitizer.SanitizeMetadata(document.Summary.OperatingSystem),
            sanitizer.SanitizeMetadata(RuntimeInformation.FrameworkDescription),
            sanitizer.SanitizeStableLabel(document.Summary.ProcessArchitecture),
            sanitizer.SanitizeStableLabel(document.Summary.StageOrMilestone),
            sanitizer.SanitizeTypeName(document.Summary.ExceptionType));
        var logs = SanitizeLogs(document.Logs, sanitizer, out var logsTruncated);
        var breadcrumbs = SanitizeBreadcrumbs(document.Breadcrumbs, out var breadcrumbsTruncated);
        var context = SanitizeContext(document.Context, sanitizer);
        var exceptionText = sanitizer.SanitizeExceptionChain(document.Exception, out var exceptionChainTruncated);

        return new SanitizedDocument(
            summary,
            exceptionText,
            logs,
            breadcrumbs,
            context,
            logsTruncated,
            breadcrumbsTruncated,
            exceptionChainTruncated);
    }

    private static ReportDocument CreateReportDocument(SanitizedDocument document)
    {
        return new ReportDocument(
            SchemaVersion,
            document.Summary.ReportId,
            document.Summary.CapturedAtUtc,
            document.Summary.BuildId,
            document.Summary.OperatingSystem,
            document.Summary.RuntimeVersion,
            document.Summary.ProcessArchitecture,
            document.Summary.StageOrMilestone,
            document.Summary.ExceptionType,
            document.Context,
            new IncludedEntriesDocument(
                ["report.json", "exception.txt", "logs.ndjson", "breadcrumbs.json", "README.txt"],
                document.Logs.Count,
                document.Breadcrumbs.Count,
                document.Context.Count),
            new TruncationDocument(
                document.LogsTruncated,
                document.BreadcrumbsTruncated,
                document.ExceptionChainTruncated));
    }

    private static IReadOnlyList<SerializedLogRecord> SanitizeLogs(
        IReadOnlyList<CrashLogRecord> records,
        CrashReportSanitizer sanitizer,
        out bool truncated)
    {
        var selected = new List<SerializedLogRecord>();
        var bytesUsed = 0;

        for (var index = records.Count - 1; index >= 0 && selected.Count < MaximumLogRecords; index--)
        {
            var record = records[index];
            var safeRecord = SanitizeLogRecord(record, sanitizer);
            var encodedLength = JsonSerializer.SerializeToUtf8Bytes(safeRecord, CompactJsonOptions).Length + 1;

            if (bytesUsed + encodedLength > MaximumLogBytes)
            {
                break;
            }

            selected.Add(safeRecord);
            bytesUsed += encodedLength;
        }

        selected.Reverse();
        truncated = selected.Count < records.Count;
        return selected;
    }

    private static SerializedLogRecord SanitizeLogRecord(
        CrashLogRecord record,
        CrashReportSanitizer sanitizer)
    {
        var isClassified = CrashLogFieldPolicy.Default.TryClassify(
            record.EventId,
            record.MessageTemplate,
            out var eventId,
            out var messageTemplate);

        return new SerializedLogRecord(
            record.TimestampUtc.ToUniversalTime(),
            record.LogLevel.ToString(),
            eventId.Id,
            isClassified ? eventId.Name : null,
            messageTemplate,
            SanitizeProperties(isClassified ? record.Properties : EmptyProperties),
            record.ExceptionType is null ? null : sanitizer.SanitizeTypeName(record.ExceptionType));
    }

    private static IReadOnlyList<SerializedBreadcrumb> SanitizeBreadcrumbs(
        IReadOnlyList<CrashBreadcrumb> breadcrumbs,
        out bool truncated)
    {
        var firstIncluded = Math.Max(0, breadcrumbs.Count - MaximumBreadcrumbs);
        var sanitized = new List<SerializedBreadcrumb>(breadcrumbs.Count - firstIncluded);

        for (var index = firstIncluded; index < breadcrumbs.Count; index++)
        {
            var breadcrumb = breadcrumbs[index];
            var eventName = KnownBreadcrumbEvents.Contains(breadcrumb.EventName)
                ? breadcrumb.EventName
                : "unknown_event";
            sanitized.Add(new SerializedBreadcrumb(
                breadcrumb.TimestampUtc.ToUniversalTime(),
                eventName,
                SanitizeProperties(breadcrumb.Properties)));
        }

        truncated = firstIncluded > 0;
        return sanitized;
    }

    private static IReadOnlyList<SerializedContext> SanitizeContext(
        IReadOnlyList<CrashContextSnapshot> context,
        CrashReportSanitizer sanitizer)
    {
        var sanitized = new List<SerializedContext>(context.Count);
        foreach (var snapshot in context)
        {
            sanitized.Add(new SerializedContext(
                snapshot.Kind.ToString(),
                snapshot.Status.ToString(),
                SanitizeContextProperties(snapshot.Kind, snapshot.Fields, sanitizer),
                SanitizeContextFailureCode(snapshot.Kind, snapshot.FailureCode)));
        }

        return sanitized;
    }

    private static IReadOnlyDictionary<string, object?> SanitizeProperties(
        IReadOnlyDictionary<string, object?> properties)
    {
        var sanitized = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (TrySanitizePersistedProperty(
                property.Key,
                property.Value,
                out var normalizedValue))
            {
                sanitized[property.Key] = normalizedValue;
            }
        }

        return sanitized;
    }

    private static IReadOnlyDictionary<string, object?> SanitizeContextProperties(
        CrashContextKind kind,
        IReadOnlyDictionary<string, object?> properties,
        CrashReportSanitizer sanitizer)
    {
        var sanitized = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (TrySanitizeContextProperty(
                    kind,
                    property.Key,
                    property.Value,
                    sanitizer,
                    out var normalizedValue))
            {
                sanitized[property.Key] = normalizedValue;
            }
        }

        return sanitized;
    }

    private static bool TrySanitizeContextProperty(
        CrashContextKind kind,
        string propertyName,
        object? value,
        CrashReportSanitizer sanitizer,
        out object? sanitizedValue)
    {
        sanitizedValue = null;

        switch (kind)
        {
            case CrashContextKind.Process:
                switch (propertyName)
                {
                    case "RuntimeFramework" or "OperatingSystem" when value is string metadata:
                        sanitizedValue = sanitizer.SanitizeMetadata(
                            metadata,
                            CrashReportSanitizer.RedactedValue);
                        return true;

                    case "ProcessArchitecture" when value is Architecture architecture
                                                   && Enum.IsDefined(architecture):
                        sanitizedValue = architecture.ToString();
                        return true;

                    case "ProcessStartUtc" when value is DateTimeOffset processStartUtc:
                        sanitizedValue = processStartUtc.ToUniversalTime();
                        return true;
                }
                break;

            case CrashContextKind.Application:
                if (propertyName == "ApplicationVersion" && value is string applicationVersion)
                {
                    sanitizedValue = sanitizer.SanitizeStableLabel(
                        applicationVersion,
                        CrashReportSanitizer.RedactedValue);
                    return true;
                }
                break;

            case CrashContextKind.Startup:
                if (propertyName == "Milestone"
                    && value is StartupCriticalPathMilestone milestone
                    && Enum.IsDefined(milestone))
                {
                    sanitizedValue = milestone.ToString();
                    return true;
                }
                break;

            case CrashContextKind.Configuration:
                return TrySanitizeConfigurationContextProperty(
                    propertyName,
                    value,
                    out sanitizedValue);

            case CrashContextKind.Stage:
                if (propertyName == "Stage"
                    && value is StageType stage
                    && Enum.IsDefined(stage))
                {
                    sanitizedValue = stage.ToString();
                    return true;
                }

                if (propertyName == "StageCount" && TrySanitizeCount(value, out sanitizedValue))
                {
                    return true;
                }
                break;

            case CrashContextKind.Graphics:
                return TrySanitizeGraphicsContextProperty(
                    propertyName,
                    value,
                    out sanitizedValue);

            case CrashContextKind.Input:
                return propertyName == "MidiDeviceCount"
                    && TrySanitizeCount(value, out sanitizedValue);

            case CrashContextKind.Audio:
                break;
        }

        return false;
    }

    private static bool TrySanitizeConfigurationContextProperty(
        string propertyName,
        object? value,
        out object? sanitizedValue)
    {
        sanitizedValue = null;

        switch (propertyName)
        {
            case "ScreenWidth" or "ScreenHeight" when TrySanitizeContextDimension(value, out sanitizedValue):
            case "KeyBindingCount" or "SystemKeyBindingCount" or "UnboundDrumLaneCount"
                or "UnboundDrumButtonCount" or "MidiVelocityThresholdCount"
                when TrySanitizeCount(value, out sanitizedValue):
                return true;

            case "BufferSizeMs" when value is int bufferSizeMs
                                     && bufferSizeMs >= 0
                                     && bufferSizeMs <= MaximumBufferMilliseconds:
                sanitizedValue = bufferSizeMs;
                return true;

            case "FullScreen" or "VSyncWait" or "AutoPlay" or "NoFail" or "EnableGameApi"
                when value is bool booleanValue:
                sanitizedValue = booleanValue;
                return true;

            default:
                return false;
        }
    }

    private static bool TrySanitizeGraphicsContextProperty(
        string propertyName,
        object? value,
        out object? sanitizedValue)
    {
        sanitizedValue = null;

        switch (propertyName)
        {
            case "Width" or "Height" when TrySanitizeContextDimension(value, out sanitizedValue):
                return true;

            case "Fullscreen" or "VSync" or "DeviceAvailable" when value is bool booleanValue:
                sanitizedValue = booleanValue;
                return true;

            case "BackBufferFormat" when value is SurfaceFormat backBufferFormat
                                         && Enum.IsDefined(backBufferFormat):
                sanitizedValue = backBufferFormat.ToString();
                return true;

            case "DepthStencilFormat" when value is DepthFormat depthStencilFormat
                                           && Enum.IsDefined(depthStencilFormat):
                sanitizedValue = depthStencilFormat.ToString();
                return true;

            case "MultiSampleCount" when value is int multiSampleCount
                                         && multiSampleCount >= 0
                                         && multiSampleCount <= MaximumMultiSampleCount:
                sanitizedValue = multiSampleCount;
                return true;

            default:
                return false;
        }
    }

    private static string? SanitizeContextFailureCode(CrashContextKind kind, string? failureCode)
    {
        if (failureCode is null)
        {
            return null;
        }

        return kind == CrashContextKind.Audio
               && string.Equals(
                   failureCode,
                   CrashContextPublisher.AudioDeviceSummaryUnavailable,
                   StringComparison.Ordinal)
            ? CrashContextPublisher.AudioDeviceSummaryUnavailable
            : CrashReportSanitizer.RedactedValue;
    }

    private static bool TrySanitizeContextDimension(object? value, out object? sanitizedValue)
    {
        if (value is int dimension && dimension >= 0 && dimension <= MaximumReportedDimension)
        {
            sanitizedValue = dimension;
            return true;
        }

        sanitizedValue = null;
        return false;
    }

    private static bool TrySanitizeCount(object? value, out object? sanitizedValue)
    {
        if (value is int count && count >= 0 && count <= MaximumReportedCount)
        {
            sanitizedValue = count;
            return true;
        }

        sanitizedValue = null;
        return false;
    }

    private static bool TrySanitizePersistedProperty(
        string propertyName,
        object? value,
        out object? sanitizedValue)
    {
        sanitizedValue = null;

        switch (propertyName)
        {
            case "Stage" or "PreviousStage" or "TargetStage"
                when value is StageType stage && Enum.IsDefined(stage):
                sanitizedValue = stage.ToString();
                return true;

            case "Milestone"
                when value is StartupCriticalPathMilestone milestone && Enum.IsDefined(milestone):
                sanitizedValue = milestone.ToString();
                return true;

            case "Fullscreen" or "VSync" or "Enabled" when value is bool booleanValue:
                sanitizedValue = booleanValue;
                return true;

            case "Width"
                when value is int width
                     && width >= MinimumReportedWidth
                     && width <= MaximumReportedDimension:
                sanitizedValue = width;
                return true;

            case "Height"
                when value is int height
                     && height >= MinimumReportedHeight
                     && height <= MaximumReportedDimension:
                sanitizedValue = height;
                return true;

            case "MidiDeviceCount"
                when value is int midiDeviceCount
                     && midiDeviceCount >= 0
                     && midiDeviceCount <= MaximumReportedCount:
                sanitizedValue = midiDeviceCount;
                return true;

            case "Reason"
                when value is StageTransitionRejectionReason reason
                     && Enum.IsDefined(reason):
                sanitizedValue = reason.ToString();
                return true;

            // Generic counters and status values are intentionally omitted. The explicit
            // bounded MIDI device count above is the only MIDI-shaped value permitted at
            // persistence; no hardware name or identifier has a serialization case.
            default:
                return false;
        }
    }

    private static void WriteJsonEntry<T>(
        ZipArchive archive,
        string name,
        T value,
        JsonSerializerOptions options)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions { Indented = options.WriteIndented });
        JsonSerializer.Serialize(writer, value, options);
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8WithoutBom)
        {
            NewLine = "\n"
        };
        writer.Write(content);
    }

    private static void WriteLogsEntry(ZipArchive archive, IReadOnlyList<SerializedLogRecord> records)
    {
        var entry = archive.CreateEntry("logs.ndjson", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8WithoutBom)
        {
            NewLine = "\n"
        };

        foreach (var record in records)
        {
            writer.Write(JsonSerializer.Serialize(record, CompactJsonOptions));
            writer.WriteLine();
        }
    }

    private static string CreateReadme()
    {
        return "This crash report was generated locally by DTXManiaCX.\n"
            + "It is not uploaded automatically.\n"
            + "Inspect its contents before attaching it to a bug report.\n";
    }

    private sealed record SanitizedSummary(
        string ReportId,
        DateTimeOffset CapturedAtUtc,
        string BuildId,
        string OperatingSystem,
        string RuntimeVersion,
        string ProcessArchitecture,
        string StageOrMilestone,
        string ExceptionType);

    private sealed record SanitizedDocument(
        SanitizedSummary Summary,
        string ExceptionText,
        IReadOnlyList<SerializedLogRecord> Logs,
        IReadOnlyList<SerializedBreadcrumb> Breadcrumbs,
        IReadOnlyList<SerializedContext> Context,
        bool LogsTruncated,
        bool BreadcrumbsTruncated,
        bool ExceptionChainTruncated);

    private sealed record ReportDocument(
        int SchemaVersion,
        string ReportId,
        DateTimeOffset CapturedAtUtc,
        string BuildId,
        string OperatingSystem,
        string RuntimeVersion,
        string ProcessArchitecture,
        string StageOrMilestone,
        string ExceptionType,
        IReadOnlyList<SerializedContext> ContextStatuses,
        IncludedEntriesDocument IncludedEntries,
        TruncationDocument Truncation);

    private sealed record IncludedEntriesDocument(
        IReadOnlyList<string> Files,
        int LogRecords,
        int Breadcrumbs,
        int ContextSections);

    private sealed record TruncationDocument(
        bool Logs,
        bool Breadcrumbs,
        bool ExceptionInnerChain);

    private sealed record SerializedLogRecord(
        DateTimeOffset TimestampUtc,
        string LogLevel,
        int EventId,
        string? EventName,
        string MessageTemplate,
        IReadOnlyDictionary<string, object?> Properties,
        string? ExceptionType);

    private sealed record SerializedBreadcrumb(
        DateTimeOffset TimestampUtc,
        string EventName,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed record SerializedContext(
        string Kind,
        string Status,
        IReadOnlyDictionary<string, object?> Fields,
        string? FailureCode);
}
