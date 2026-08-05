#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportArchiveWriterTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WriteEmergencyText_ShouldProduceVersionedHeaderAndSummaryFields()
    {
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();
        var document = CreateArchiveDocument();

        writer.WriteEmergencyText(destination, document);
        var text = Encoding.UTF8.GetString(destination.ToArray());

        Assert.StartsWith("DTXMANIACX-CRASH-REPORT 1\n", text, StringComparison.Ordinal);
        Assert.Contains("ReportId: " + document.Summary.ReportId, text, StringComparison.Ordinal);
        Assert.Contains("BuildId: " + document.Summary.BuildId, text, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem: " + document.Summary.OperatingSystem, text, StringComparison.Ordinal);
        Assert.Contains("ProcessArchitecture: " + document.Summary.ProcessArchitecture, text, StringComparison.Ordinal);
        Assert.Contains("StageOrMilestone: " + document.Summary.StageOrMilestone, text, StringComparison.Ordinal);
        Assert.Contains("ExceptionType: " + document.Summary.ExceptionType, text, StringComparison.Ordinal);
        Assert.Contains("---", text, StringComparison.Ordinal);
        Assert.Contains("IncludedLogRecords:", text, StringComparison.Ordinal);
        Assert.Contains("IncludedBreadcrumbs:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEmergencyText_WithNullExceptionTextEnding_ShouldAppendNewline()
    {
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();
        var document = CreateArchiveDocument();

        writer.WriteEmergencyText(destination, document);
        var text = Encoding.UTF8.GetString(destination.ToArray());

        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteZip_WithNullDestination_ShouldThrow()
    {
        var writer = new CrashReportArchiveWriter();

        Assert.Throws<ArgumentNullException>(
            () => writer.WriteZip(null!, CreateArchiveDocument()));
    }

    [Fact]
    public void WriteZip_WithNullDocument_ShouldThrow()
    {
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        Assert.Throws<ArgumentNullException>(
            () => writer.WriteZip(destination, null!));
    }

    [Fact]
    public void WriteEmergencyText_WithNullDestination_ShouldThrow()
    {
        var writer = new CrashReportArchiveWriter();

        Assert.Throws<ArgumentNullException>(
            () => writer.WriteEmergencyText(null!, CreateArchiveDocument()));
    }

    [Fact]
    public void WriteEmergencyText_WithNullDocument_ShouldThrow()
    {
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        Assert.Throws<ArgumentNullException>(
            () => writer.WriteEmergencyText(destination, null!));
    }

    [Fact]
    public void WriteZip_ShouldTruncateLogsExceedingMaximumCount()
    {
        var writer = new CrashReportArchiveWriter();
        var logs = new List<CrashLogRecord>();
        for (var i = 0; i < 600; i++)
        {
            logs.Add(new CrashLogRecord(
                CapturedAt.AddSeconds(i),
                LogLevel.Information,
                new EventId(5100, "crash_safe_stage"),
                "Crash-safe stage changed to {Stage}",
                new Dictionary<string, object?> { ["Stage"] = StageType.Title },
                ExceptionType: null));
        }

        var document = CreateArchiveDocument() with { Logs = logs };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("logs.ndjson")!.Open(), Encoding.UTF8);
        var logLines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(logLines.Length <= 500);
    }

    [Fact]
    public void WriteZip_ShouldTruncateBreadcrumbsExceedingMaximumCount()
    {
        var writer = new CrashReportArchiveWriter();
        var breadcrumbs = new List<CrashBreadcrumb>();
        for (var i = 0; i < 150; i++)
        {
            breadcrumbs.Add(new CrashBreadcrumb(
                CapturedAt.AddSeconds(i),
                "stage_transition_completed",
                new Dictionary<string, object?> { ["Stage"] = StageType.Title }));
        }

        var document = CreateArchiveDocument() with { Breadcrumbs = breadcrumbs };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("breadcrumbs.json")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        Assert.True(json.RootElement.GetArrayLength() <= 100);
    }

    [Fact]
    public void WriteZip_ShouldReportTruncationFlagsWhenDataIsTruncated()
    {
        var writer = new CrashReportArchiveWriter();
        var logs = new List<CrashLogRecord>();
        for (var i = 0; i < 600; i++)
        {
            logs.Add(new CrashLogRecord(
                CapturedAt,
                LogLevel.Information,
                new EventId(5100, "crash_safe_stage"),
                "Crash-safe stage changed to {Stage}",
                new Dictionary<string, object?> { ["Stage"] = StageType.Title },
                ExceptionType: null));
        }

        var document = CreateArchiveDocument() with { Logs = logs };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("report.json")!.Open(), Encoding.UTF8);
        using var report = JsonDocument.Parse(reader.ReadToEnd());

        Assert.True(report.RootElement.GetProperty("truncation").GetProperty("logs").GetBoolean());
    }

    [Fact]
    public void WriteZip_ShouldRedactUnknownBreadcrumbEventNames()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Breadcrumbs =
            [
                new CrashBreadcrumb(
                    CapturedAt,
                    "caller_controlled_event_name",
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title })
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("breadcrumbs.json")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        Assert.Equal("unknown_event", json.RootElement[0].GetProperty("eventName").GetString());
    }

    [Fact]
    public void WriteZip_ShouldPersistReasonPropertyAsEnumString()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Logs =
            [
                new CrashLogRecord(
                    CapturedAt,
                    LogLevel.Information,
                    new EventId(5111, "stage_transition_rejected"),
                    "Stage transition rejected: {Reason}",
                    new Dictionary<string, object?>
                    {
                        ["Reason"] = StageTransitionRejectionReason.AlreadyTransitioning
                    },
                    ExceptionType: null)
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("logs.ndjson")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadLine()!);

        Assert.Equal(
            "AlreadyTransitioning",
            json.RootElement.GetProperty("properties").GetProperty("Reason").GetString());
    }

    [Fact]
    public void WriteZip_ShouldOmitWidthBelowMinimumReportedDimension()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Logs =
            [
                new CrashLogRecord(
                    CapturedAt,
                    LogLevel.Information,
                    new EventId(5107, "graphics_settings_changed"),
                    "Graphics settings updated: {Width}x{Height}, fullscreen={Fullscreen}, vsync={VSync}",
                    new Dictionary<string, object?>
                    {
                        ["Width"] = 100,
                        ["Height"] = 1080,
                        ["Fullscreen"] = true,
                        ["VSync"] = false
                    },
                    ExceptionType: null)
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("logs.ndjson")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadLine()!);

        var properties = json.RootElement.GetProperty("properties");
        Assert.False(properties.TryGetProperty("Width", out _));
        Assert.True(properties.TryGetProperty("Height", out _));
    }

    [Fact]
    public void WriteZip_ShouldOmitHeightBelowMinimumReportedDimension()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Logs =
            [
                new CrashLogRecord(
                    CapturedAt,
                    LogLevel.Information,
                    new EventId(5107, "graphics_settings_changed"),
                    "Graphics settings updated: {Width}x{Height}, fullscreen={Fullscreen}, vsync={VSync}",
                    new Dictionary<string, object?>
                    {
                        ["Width"] = 1920,
                        ["Height"] = 50,
                        ["Fullscreen"] = true,
                        ["VSync"] = false
                    },
                    ExceptionType: null)
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("logs.ndjson")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadLine()!);

        var properties = json.RootElement.GetProperty("properties");
        Assert.False(properties.TryGetProperty("Height", out _));
        Assert.True(properties.TryGetProperty("Width", out _));
    }

    [Fact]
    public void WriteZip_ShouldRetainEnabledBooleanProperty()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Breadcrumbs =
            [
                new CrashBreadcrumb(
                    CapturedAt,
                    "configuration_opened",
                    new Dictionary<string, object?> { ["Enabled"] = true })
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("breadcrumbs.json")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        Assert.True(json.RootElement[0].GetProperty("properties").GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void WriteZip_ShouldRetainMidiDeviceCountInRange()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Breadcrumbs =
            [
                new CrashBreadcrumb(
                    CapturedAt,
                    "midi_device_count_changed",
                    new Dictionary<string, object?> { ["MidiDeviceCount"] = 3 })
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("breadcrumbs.json")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        Assert.Equal(3, json.RootElement[0].GetProperty("properties").GetProperty("MidiDeviceCount").GetInt32());
    }

    [Fact]
    public void WriteZip_ShouldOmitMidiDeviceCountOutOfRange()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Breadcrumbs =
            [
                new CrashBreadcrumb(
                    CapturedAt,
                    "midi_device_count_changed",
                    new Dictionary<string, object?> { ["MidiDeviceCount"] = -1 })
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("breadcrumbs.json")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        Assert.False(json.RootElement[0].GetProperty("properties").TryGetProperty("MidiDeviceCount", out _));
    }

    [Fact]
    public void WriteZip_ShouldRetainMilestoneProperty()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Breadcrumbs =
            [
                new CrashBreadcrumb(
                    CapturedAt,
                    "initialization_milestone_reached",
                    new Dictionary<string, object?>
                    {
                        ["Milestone"] = StartupCriticalPathMilestone.StartupActivation
                    })
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("breadcrumbs.json")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        Assert.Equal(
            "StartupActivation",
            json.RootElement[0].GetProperty("properties").GetProperty("Milestone").GetString());
    }

    [Fact]
    public void WriteZip_ShouldPersistContextFailureCodeForAudioUnavailable()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Audio,
                    CrashContextStatus.Unavailable,
                    new Dictionary<string, object?>(),
                    CrashContextPublisher.AudioDeviceSummaryUnavailable)
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("report.json")!.Open(), Encoding.UTF8);
        using var report = JsonDocument.Parse(reader.ReadToEnd());

        var audio = Assert.Single(
            report.RootElement.GetProperty("contextStatuses").EnumerateArray(),
            c => c.GetProperty("kind").GetString() == "Audio");

        Assert.Equal(
            CrashContextPublisher.AudioDeviceSummaryUnavailable,
            audio.GetProperty("failureCode").GetString());
    }

    [Fact]
    public void WriteZip_ShouldRedactNonCanonicalAudioFailureCode()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Audio,
                    CrashContextStatus.Unavailable,
                    new Dictionary<string, object?>(),
                    FailureCode: "secret_audio_failure")
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("report.json")!.Open(), Encoding.UTF8);
        using var report = JsonDocument.Parse(reader.ReadToEnd());

        var audio = Assert.Single(
            report.RootElement.GetProperty("contextStatuses").EnumerateArray(),
            c => c.GetProperty("kind").GetString() == "Audio");

        Assert.Equal("[REDACTED]", audio.GetProperty("failureCode").GetString());
    }

    [Fact]
    public void WriteZip_ShouldOmitUnclassifiedLogProperties()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Logs =
            [
                new CrashLogRecord(
                    CapturedAt,
                    LogLevel.Information,
                    new EventId(9999, "unknown_event"),
                    "some unknown template",
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title },
                    ExceptionType: null)
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("logs.ndjson")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadLine()!);

        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", json.RootElement.GetProperty("messageTemplate").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("properties").EnumerateObject().Count());
        Assert.Null(json.RootElement.GetProperty("eventName").GetString());
    }

    [Fact]
    public void WriteZip_ShouldPersistPreviousStageAndTargetStageProperties()
    {
        var writer = new CrashReportArchiveWriter();
        var document = CreateArchiveDocument() with
        {
            Logs =
            [
                new CrashLogRecord(
                    CapturedAt,
                    LogLevel.Information,
                    new EventId(5103, "stage_transition_requested"),
                    "Stage transition requested: {PreviousStage} -> {TargetStage}",
                    new Dictionary<string, object?>
                    {
                        ["PreviousStage"] = StageType.Title,
                        ["TargetStage"] = StageType.SongSelect
                    },
                    ExceptionType: null)
            ]
        };
        using var destination = new MemoryStream();
        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("logs.ndjson")!.Open(), Encoding.UTF8);
        using var json = JsonDocument.Parse(reader.ReadLine()!);

        var properties = json.RootElement.GetProperty("properties");
        Assert.Equal("Title", properties.GetProperty("PreviousStage").GetString());
        Assert.Equal("SongSelect", properties.GetProperty("TargetStage").GetString());
    }

    [Fact]
    public void WriteZip_ShouldIncludeReadmeEntry()
    {
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();
        writer.WriteZip(destination, CreateArchiveDocument());
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("README.txt")!.Open(), Encoding.UTF8);
        var readme = reader.ReadToEnd();

        Assert.Contains("DTXManiaCX", readme, StringComparison.Ordinal);
        Assert.Contains("not uploaded automatically", readme, StringComparison.Ordinal);
    }

    private static CrashReportDocument CreateArchiveDocument(Exception? exception = null)
    {
        var summary = new CrashReportSummary(
            "crash-20260802-120000Z-a1b2c3",
            CapturedAt,
            "1.2.3",
            "Test OS",
            "X64",
            "Title",
            typeof(InvalidOperationException).FullName!,
            CrashReportFormat.ZipBundle,
            "crash-20260802-120000Z-a1b2c3.zip");

        return new CrashReportDocument(
            summary,
            exception ?? new InvalidOperationException("Secret Song Name"),
            [
                new CrashLogRecord(
                    CapturedAt,
                    LogLevel.Information,
                    new EventId(5100, "crash_safe_stage"),
                    "Crash-safe stage changed to {Stage}",
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title },
                    ExceptionType: null)
            ],
            [
                new CrashBreadcrumb(
                    CapturedAt,
                    "stage_transition_completed",
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title })
            ],
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title })
            ],
            []);
    }
}
