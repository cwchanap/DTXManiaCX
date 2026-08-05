#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportTextWriterTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_ShouldProduceVersionedHeaderAndSummaryFields()
    {
        var document = CreateDocument();

        var text = Write(document);

        Assert.StartsWith(CrashReportTextWriter.Header + "\n", text, StringComparison.Ordinal);
        Assert.Contains("ReportId: " + document.Summary.ReportId, text, StringComparison.Ordinal);
        Assert.Contains("BuildId: " + document.Summary.BuildId, text, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem: " + document.Summary.OperatingSystem, text, StringComparison.Ordinal);
        Assert.Contains("ProcessArchitecture: " + document.Summary.ProcessArchitecture, text, StringComparison.Ordinal);
        Assert.Contains("StageOrMilestone: " + document.Summary.StageOrMilestone, text, StringComparison.Ordinal);
        Assert.Contains("ExceptionType: " + document.Summary.ExceptionType, text, StringComparison.Ordinal);
        Assert.Contains("RuntimeVersion: ", text, StringComparison.Ordinal);
        Assert.Contains("CapturedAtUtc: " + CapturedAt.ToString("O"), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ShouldEmitEverySection()
    {
        var text = Write(CreateDocument());

        Assert.Contains(CrashReportTextWriter.ExceptionSection, text, StringComparison.Ordinal);
        Assert.Contains(CrashReportTextWriter.ContextSection, text, StringComparison.Ordinal);
        Assert.Contains(CrashReportTextWriter.BreadcrumbSection, text, StringComparison.Ordinal);
        Assert.Contains(CrashReportTextWriter.LogSection, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ShouldPreserveExceptionTypeAndMessage()
    {
        var document = CreateDocument(
            exception: new InvalidOperationException("chart channel 0xZZ is not supported"));

        var text = Write(document);

        Assert.Contains(typeof(InvalidOperationException).FullName!, text, StringComparison.Ordinal);
        Assert.Contains("Message: chart channel 0xZZ is not supported", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ShouldRenderContextKindStatusAndFields()
    {
        var document = CreateDocument(context:
        [
            new CrashContextSnapshot(
                CrashContextKind.Graphics,
                CrashContextStatus.Available,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Width"] = 1280,
                    ["Fullscreen"] = false
                }),
            new CrashContextSnapshot(
                CrashContextKind.Audio,
                CrashContextStatus.Unavailable,
                new Dictionary<string, object?>(StringComparer.Ordinal),
                "audio_device_summary_unavailable")
        ]);

        var text = Write(document);

        Assert.Contains("Graphics [Available]", text, StringComparison.Ordinal);
        Assert.Contains("  Width: 1280", text, StringComparison.Ordinal);
        Assert.Contains("  Fullscreen: False", text, StringComparison.Ordinal);
        Assert.Contains("Audio [Unavailable] audio_device_summary_unavailable", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ShouldRenderBreadcrumbsWithProperties()
    {
        var document = CreateDocument(breadcrumbs:
        [
            new CrashBreadcrumb(
                CapturedAt,
                "stage_transition_completed",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["PreviousStage"] = StageType.Startup,
                    ["TargetStage"] = StageType.Title
                })
        ]);

        var text = Write(document);

        Assert.Contains(
            CapturedAt.ToString("O") + " stage_transition_completed PreviousStage=Startup TargetStage=Title",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ShouldRenderLogRecordsWithEventIdentityAndProperties()
    {
        var document = CreateDocument(logs:
        [
            new CrashLogRecord(
                CapturedAt,
                LogLevel.Warning,
                new EventId(5112, "graphics_device_lost"),
                "Graphics device lost",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["Width"] = 1280 },
                typeof(InvalidOperationException).FullName)
        ]);

        var text = Write(document);

        Assert.Contains(
            "Warning [5112 graphics_device_lost] Graphics device lost exception="
                + typeof(InvalidOperationException).FullName
                + " Width=1280",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ShouldScrubRegisteredSensitivePaths()
    {
        var songRoot = Path.Combine(Path.GetTempPath(), "dtx-writer", "Some Album");
        var document = CreateDocument(
            exception: new FileNotFoundException($"Missing {Path.Combine(songRoot, "chart.dtx")}"),
            sensitivePaths: [songRoot]);

        var text = Write(document);

        Assert.DoesNotContain("Some Album", text, StringComparison.Ordinal);
        Assert.Contains("[PATH]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ShouldKeepMultiLineValuesOnASingleLine()
    {
        var document = CreateDocument(breadcrumbs:
        [
            new CrashBreadcrumb(
                CapturedAt,
                "graphics_settings_changed",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["Reason"] = "a\nb" })
        ]);

        var text = Write(document);

        Assert.Contains("Reason=a b", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithMoreThan100Breadcrumbs_ShouldKeepNewestAndFlagTruncation()
    {
        var breadcrumbs = Enumerable.Range(0, 150)
            .Select(index => new CrashBreadcrumb(
                CapturedAt.AddSeconds(index),
                "process_started",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["Count"] = index }))
            .ToArray();

        var text = Write(CreateDocument(breadcrumbs: breadcrumbs));

        Assert.Contains("breadcrumbs=True", text, StringComparison.Ordinal);
        Assert.Contains("Count=149", text, StringComparison.Ordinal);
        Assert.DoesNotContain(CapturedAt.AddSeconds(49).ToString("O"), text, StringComparison.Ordinal);
        Assert.Contains(CapturedAt.AddSeconds(50).ToString("O"), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithMoreThan500Logs_ShouldKeepNewestAndFlagTruncation()
    {
        var logs = Enumerable.Range(0, 600)
            .Select(index => new CrashLogRecord(
                CapturedAt.AddSeconds(index),
                LogLevel.Information,
                CrashLogEvents.GraphicsDeviceReset.EventId,
                CrashLogEvents.GraphicsDeviceReset.MessageTemplate,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["Count"] = index },
                null))
            .ToArray();

        var text = Write(CreateDocument(logs: logs));

        Assert.Contains("logs=True", text, StringComparison.Ordinal);
        Assert.Contains("Count=599", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithDeeplyNestedException_ShouldFlagExceptionTruncation()
    {
        Exception deepest = new InvalidOperationException("level 0");
        for (var i = 1; i < 12; i++)
        {
            deepest = new InvalidOperationException($"level {i}", deepest);
        }

        var text = Write(CreateDocument(exception: deepest));

        Assert.Contains("exception=True", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithNothingBuffered_ShouldStillProduceAReadableReport()
    {
        var text = Write(CreateDocument(logs: [], breadcrumbs: [], context: []));

        Assert.StartsWith(CrashReportTextWriter.Header, text, StringComparison.Ordinal);
        Assert.Contains("Truncated: exception=False logs=False breadcrumbs=False", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithNullArguments_ShouldThrow()
    {
        var writer = new CrashReportTextWriter();
        using var destination = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => writer.Write(null!, CreateDocument()));
        Assert.Throws<ArgumentNullException>(() => writer.Write(destination, null!));
    }

    [Fact]
    public void Write_ShouldLeaveDestinationStreamOpen()
    {
        using var destination = new MemoryStream();

        new CrashReportTextWriter().Write(destination, CreateDocument());

        Assert.True(destination.CanWrite);
    }

    private static string Write(CrashReportDocument document)
    {
        using var destination = new MemoryStream();
        new CrashReportTextWriter().Write(destination, document);
        return Encoding.UTF8.GetString(destination.ToArray());
    }

    private static CrashReportDocument CreateDocument(
        Exception? exception = null,
        IReadOnlyList<CrashLogRecord>? logs = null,
        IReadOnlyList<CrashBreadcrumb>? breadcrumbs = null,
        IReadOnlyList<CrashContextSnapshot>? context = null,
        IReadOnlyList<string>? sensitivePaths = null)
    {
        return new CrashReportDocument(
            new CrashReportSummary(
                "crash-20260802-120000Z-a1b2c3",
                CapturedAt,
                "1.2.3",
                "macOS 15.0",
                "Arm64",
                "SongSelect",
                typeof(InvalidOperationException).FullName!,
                "crash-20260802-120000Z-a1b2c3.txt"),
            exception ?? new InvalidOperationException("boom"),
            logs ?? [],
            breadcrumbs ?? [],
            context ?? [],
            sensitivePaths ?? []);
    }
}
