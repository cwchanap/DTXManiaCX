#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportStoreTests
{
    [Fact]
    public void WriteZip_ShouldContainExactlyTheRequiredEntries()
    {
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        writer.WriteZip(destination, CreateArchiveDocument());
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        var entryNames = archive.Entries
            .Select(entry => entry.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["README.txt", "breadcrumbs.json", "exception.txt", "logs.ndjson", "report.json"],
            entryNames);
    }

    [Fact]
    public void WriteZip_ShouldWriteSchemaOneAndFormatNeutralSummaryFields()
    {
        var document = CreateArchiveDocument();
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(
            archive.GetEntry("report.json")!.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true);
        using var json = JsonDocument.Parse(reader.ReadToEnd());
        var report = json.RootElement;

        Assert.Equal(1, report.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(document.Summary.ReportId, report.GetProperty("reportId").GetString());
        Assert.Equal(document.Summary.BuildId, report.GetProperty("buildId").GetString());
        Assert.Equal(document.Summary.OperatingSystem, report.GetProperty("operatingSystem").GetString());
        Assert.False(string.IsNullOrWhiteSpace(report.GetProperty("runtimeVersion").GetString()));
        Assert.Equal(document.Summary.ProcessArchitecture, report.GetProperty("processArchitecture").GetString());
        Assert.Equal(document.Summary.StageOrMilestone, report.GetProperty("stageOrMilestone").GetString());
        Assert.Equal(document.Summary.ExceptionType, report.GetProperty("exceptionType").GetString());
    }

    [Fact]
    public void WriteZip_ShouldSanitizeExceptionMessageBeforePersisting()
    {
        var document = CreateArchiveDocument(
            exception: new InvalidOperationException("Secret Song Name"));
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("exception.txt")!.Open(), Encoding.UTF8);
        var exceptionText = reader.ReadToEnd();

        Assert.DoesNotContain("Secret Song Name", exceptionText, StringComparison.Ordinal);
        Assert.Contains("[EXCEPTION MESSAGE OMITTED]", exceptionText);
    }

#if DEBUG
    [Fact]
    public void WriteZip_WithControlledDebugMarker_ShouldPersistFixedMessage()
    {
        var document = CreateArchiveDocument(
            exception: new InvalidOperationException(DebugCrashInjection.ControlledCrashMessage));
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("exception.txt")!.Open(), Encoding.UTF8);
        var exceptionText = reader.ReadToEnd();

        Assert.Equal(
            1,
            exceptionText.Split(DebugCrashInjection.ControlledCrashMessage, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void WriteEmergencyText_WithControlledDebugMarker_ShouldPersistFixedMessage()
    {
        var document = CreateArchiveDocument(
            exception: new InvalidOperationException(DebugCrashInjection.ControlledCrashMessage));
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        writer.WriteEmergencyText(destination, document);

        var emergencyText = Encoding.UTF8.GetString(destination.ToArray());
        Assert.Equal(
            1,
            emergencyText.Split(DebugCrashInjection.ControlledCrashMessage, StringSplitOptions.None).Length - 1);
    }
#endif

    [Fact]
    public void WriteZip_ShouldExcludeHardwareAndMidiLikeValuesAtThePersistenceBoundary()
    {
        var deviceId = Guid.Parse("9bc2520f-5b38-4e6c-a1c4-5f34e0135da3");
        var unsafeProperties = new Dictionary<string, object?>
        {
            ["MidiDeviceCount"] = deviceId,
            ["Count"] = 36,
            ["Status"] = 36,
            ["Width"] = 36,
            ["Height"] = 36,
            ["Stage"] = deviceId,
            ["Fullscreen"] = true
        };
        var capturedAt = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var document = CreateArchiveDocument() with
        {
            Logs =
            [
                new CrashLogRecord(
                    capturedAt,
                    LogLevel.Information,
                    new EventId(5108, "midi_device_count_changed"),
                    "MIDI device count: {MidiDeviceCount}",
                    unsafeProperties,
                    ExceptionType: null)
            ],
            Breadcrumbs =
            [
                new CrashBreadcrumb(capturedAt, "midi_device_selected", unsafeProperties)
            ],
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Input,
                    CrashContextStatus.Available,
                    unsafeProperties)
            ]
        };
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var logsReader = new StreamReader(archive.GetEntry("logs.ndjson")!.Open(), Encoding.UTF8);
        using var logs = JsonDocument.Parse(logsReader.ReadLine()!);
        using var breadcrumbsReader = new StreamReader(archive.GetEntry("breadcrumbs.json")!.Open(), Encoding.UTF8);
        using var breadcrumbs = JsonDocument.Parse(breadcrumbsReader.ReadToEnd());
        using var reportReader = new StreamReader(archive.GetEntry("report.json")!.Open(), Encoding.UTF8);
        using var report = JsonDocument.Parse(reportReader.ReadToEnd());

        AssertPersistedPropertiesContainOnlySafeValues(logs.RootElement.GetProperty("properties"));
        AssertPersistedPropertiesContainOnlySafeValues(breadcrumbs.RootElement[0].GetProperty("properties"));
        Assert.Empty(
            report.RootElement.GetProperty("contextStatuses")[0]
                .GetProperty("fields")
                .EnumerateObject());
    }

    [Fact]
    public void WriteZip_ShouldPersistOnlyTheApprovedTaskFiveContextFields()
    {
        const string secret = "Secret song and API key";
        const string dtxPath = @"C:\Secret\Songs";
        var document = CreateArchiveDocument() with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Process,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["RuntimeFramework"] = ".NET 8.0",
                        ["OperatingSystem"] = "macOS",
                        ["ProcessArchitecture"] = Architecture.Arm64,
                        ["ProcessStartUtc"] = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
                        ["CommandLine"] = secret
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Application,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["ApplicationVersion"] = "1.2.3",
                        ["BuildPath"] = secret
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Startup,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["Milestone"] = StartupCriticalPathMilestone.StartupActivation,
                        ["SongTitle"] = secret
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Configuration,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["ScreenWidth"] = 1920,
                        ["ScreenHeight"] = 1080,
                        ["FullScreen"] = true,
                        ["VSyncWait"] = false,
                        ["BufferSizeMs"] = 80,
                        ["AutoPlay"] = true,
                        ["NoFail"] = true,
                        ["EnableGameApi"] = true,
                        ["KeyBindingCount"] = 4,
                        ["SystemKeyBindingCount"] = 3,
                        ["UnboundDrumLaneCount"] = 1,
                        ["UnboundDrumButtonCount"] = 2,
                        ["MidiVelocityThresholdCount"] = 5,
                        ["GameApiKey"] = secret,
                        ["DTXPath"] = dtxPath
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Graphics,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["Width"] = 1920,
                        ["Height"] = 1080,
                        ["Fullscreen"] = true,
                        ["VSync"] = false,
                        ["BackBufferFormat"] = SurfaceFormat.Color,
                        ["DepthStencilFormat"] = DepthFormat.Depth24,
                        ["MultiSampleCount"] = 4,
                        ["DeviceAvailable"] = true,
                        ["GraphicsSettings"] = secret
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["Stage"] = StageType.Title,
                        ["StageCount"] = 3,
                        ["SharedData"] = secret
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Input,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["MidiDeviceCount"] = 2,
                        ["MidiDeviceName"] = secret
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Audio,
                    CrashContextStatus.Unavailable,
                    new Dictionary<string, object?> { ["DeviceName"] = secret },
                    CrashContextPublisher.AudioDeviceSummaryUnavailable)
            ]
        };
        var writer = new CrashReportArchiveWriter();
        using var destination = new MemoryStream();

        writer.WriteZip(destination, document);
        destination.Position = 0;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("report.json")!.Open(), Encoding.UTF8);
        var reportText = reader.ReadToEnd();
        using var report = JsonDocument.Parse(reportText);
        var contexts = report.RootElement.GetProperty("contextStatuses").EnumerateArray().ToArray();
        var process = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Process");
        var application = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Application");
        var startup = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Startup");
        var configuration = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Configuration");
        var graphics = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Graphics");
        var stage = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Stage");
        var input = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Input");
        var audio = Assert.Single(contexts, item => item.GetProperty("kind").GetString() == "Audio");

        Assert.Equal(
            ["OperatingSystem", "ProcessArchitecture", "ProcessStartUtc", "RuntimeFramework"],
            process.GetProperty("fields").EnumerateObject().Select(item => item.Name).ToArray());
        Assert.Equal(
            ["ApplicationVersion"],
            application.GetProperty("fields").EnumerateObject().Select(item => item.Name).ToArray());
        Assert.Equal(
            ["Milestone"],
            startup.GetProperty("fields").EnumerateObject().Select(item => item.Name).ToArray());
        Assert.Equal(
            [
                "AutoPlay", "BufferSizeMs", "EnableGameApi", "FullScreen", "KeyBindingCount",
                "MidiVelocityThresholdCount", "NoFail", "ScreenHeight", "ScreenWidth",
                "SystemKeyBindingCount", "UnboundDrumButtonCount", "UnboundDrumLaneCount", "VSyncWait"
            ],
            configuration.GetProperty("fields").EnumerateObject().Select(item => item.Name).ToArray());
        Assert.Equal(
            [
                "BackBufferFormat", "DepthStencilFormat", "DeviceAvailable", "Fullscreen", "Height",
                "MultiSampleCount", "VSync", "Width"
            ],
            graphics.GetProperty("fields").EnumerateObject().Select(item => item.Name).ToArray());
        Assert.Equal(
            ["Stage", "StageCount"],
            stage.GetProperty("fields").EnumerateObject().Select(item => item.Name).ToArray());
        Assert.Equal(2, input.GetProperty("fields").GetProperty("MidiDeviceCount").GetInt32());
        Assert.Equal(CrashContextPublisher.AudioDeviceSummaryUnavailable, audio.GetProperty("failureCode").GetString());
        Assert.Empty(audio.GetProperty("fields").EnumerateObject());
        Assert.DoesNotContain(secret, reportText, StringComparison.Ordinal);
        Assert.DoesNotContain(dtxPath, reportText, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_ShouldWriteTemporaryFileInReportDirectoryBeforeFinalization()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();

        var result = store.Capture(fixture.CreateCapture(0));

        var report = Assert.IsType<CrashReportSummary>(result.Report);
        var temporaryPath = Assert.IsType<string>(fixture.ArtifactWriter.LastZipDestinationPath);
        Assert.Equal(fixture.RootPath, Path.GetDirectoryName(temporaryPath));
        Assert.StartsWith($".{report.ReportId}", Path.GetFileName(temporaryPath), StringComparison.Ordinal);
        Assert.EndsWith(".tmp", temporaryPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(fixture.RootPath, report.FileName)));
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Capture_WhenZipWriterThrows_ShouldProduceOneEmergencyTextReport()
    {
        using var fixture = CrashStoreFixture.Create();
        fixture.ArtifactWriter.FailZip = true;
        var store = fixture.CreateStore();

        var result = store.Capture(fixture.CreateCapture(0));

        var report = Assert.IsType<CrashReportSummary>(result.Report);
        Assert.True(result.UsedEmergencyFallback);
        Assert.Null(result.FailureCode);
        Assert.Equal(CrashReportFormat.EmergencyText, report.Format);
        Assert.Equal(1, fixture.ArtifactWriter.ZipWriteCount);
        Assert.Equal(1, fixture.ArtifactWriter.EmergencyWriteCount);
        Assert.Single(Directory.EnumerateFiles(fixture.RootPath, "*.txt", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Capture_WhenStartupMilestonePrecedesActiveStage_ShouldPreferActiveStage()
    {
        using var fixture = CrashStoreFixture.Create();
        var contextStore = new CrashContextSnapshotStore();
        contextStore.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Startup,
            CrashContextStatus.Available,
            new Dictionary<string, object?>
            {
                ["Milestone"] = StartupCriticalPathMilestone.SummaryRequest
            }));
        contextStore.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Stage,
            CrashContextStatus.Available,
            new Dictionary<string, object?>
            {
                ["Stage"] = StageType.Title
            }));

        var capture = fixture.CreateCapture(0) with { Context = contextStore.Snapshot() };

        var report = Assert.IsType<CrashReportSummary>(fixture.CreateStore().Capture(capture).Report);

        Assert.Equal(StageType.Title.ToString(), report.StageOrMilestone);
    }

    [Fact]
    public void Capture_EmergencyText_ShouldUseVersionedHeaderAndBeDiscoverable()
    {
        using var fixture = CrashStoreFixture.Create();
        fixture.ArtifactWriter.FailZip = true;
        var store = fixture.CreateStore();

        var captured = Assert.IsType<CrashReportSummary>(store.Capture(fixture.CreateCapture(0)).Report);
        var emergencyText = File.ReadAllText(Path.Combine(fixture.RootPath, captured.FileName));
        var discovered = Assert.Single(store.DiscoverCompletedReports());

        Assert.StartsWith("DTXMANIACX-CRASH-REPORT 1\n", emergencyText, StringComparison.Ordinal);
        Assert.Equal(CrashReportFormat.EmergencyText, discovered.Format);
        Assert.Equal(captured.ReportId, discovered.ReportId);
        Assert.Equal(captured.CapturedAtUtc, discovered.CapturedAtUtc);
    }

    [Fact]
    public void DiscoverCompletedReports_WhenEmergencyHeaderIsCorrupt_ShouldUseFilenameFallbacks()
    {
        using var fixture = CrashStoreFixture.Create();
        const string reportId = "crash-20260802-120000Z-a1b2c3";
        File.WriteAllText(
            Path.Combine(fixture.RootPath, reportId + ".txt"),
            "corrupt header\n---\nSecret emergency exception body");
        var store = fixture.CreateStore();

        var report = Assert.Single(store.DiscoverCompletedReports());

        Assert.Equal(reportId, report.ReportId);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero), report.CapturedAtUtc);
        Assert.Equal("Unknown", report.BuildId);
        Assert.Equal("Unknown", report.OperatingSystem);
        Assert.Equal("Unknown", report.ProcessArchitecture);
        Assert.Equal("Unknown", report.StageOrMilestone);
        Assert.Equal("Unknown", report.ExceptionType);
        Assert.Equal(CrashReportFormat.EmergencyText, report.Format);
    }

    [Fact]
    public void DiscoverCompletedReports_WhenZipSchemaIsCorrupt_ShouldUseFilenameFallbacks()
    {
        using var fixture = CrashStoreFixture.Create();
        const string reportId = "crash-20260802-120000Z-a1b2c3";
        var path = Path.Combine(fixture.RootPath, reportId + ".zip");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("report.json").Open(), Encoding.UTF8))
        {
            writer.Write("{\"schemaVersion\":1.5}");
        }

        var report = Assert.Single(fixture.CreateStore().DiscoverCompletedReports());

        Assert.Equal(reportId, report.ReportId);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero), report.CapturedAtUtc);
        Assert.Equal("Unknown", report.BuildId);
        Assert.Equal("Unknown", report.OperatingSystem);
        Assert.Equal("Unknown", report.ProcessArchitecture);
        Assert.Equal("Unknown", report.StageOrMilestone);
        Assert.Equal("Unknown", report.ExceptionType);
        Assert.Equal(CrashReportFormat.ZipBundle, report.Format);
    }

    [Fact]
    public void DiscoverCompletedReports_WhenZipMetadataIsNotAnObject_ShouldUseFilenameFallbacks()
    {
        using var fixture = CrashStoreFixture.Create();
        const string reportId = "crash-20260802-120000Z-a1b2c3";
        var path = Path.Combine(fixture.RootPath, reportId + ".zip");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("report.json").Open(), Encoding.UTF8))
        {
            writer.Write("[]");
        }

        var report = Assert.Single(fixture.CreateStore().DiscoverCompletedReports());

        Assert.Equal(reportId, report.ReportId);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero), report.CapturedAtUtc);
        Assert.Equal("Unknown", report.BuildId);
        Assert.Equal("Unknown", report.OperatingSystem);
        Assert.Equal("Unknown", report.ProcessArchitecture);
        Assert.Equal("Unknown", report.StageOrMilestone);
        Assert.Equal("Unknown", report.ExceptionType);
        Assert.Equal(CrashReportFormat.ZipBundle, report.Format);
    }

    [Fact]
    public void DiscoverCompletedReports_WhenEmergencyHeaderDoesNotMatchFileName_ShouldUseFilenameFallbacks()
    {
        using var fixture = CrashStoreFixture.Create();
        const string reportId = "crash-20260802-120000Z-a1b2c3";
        File.WriteAllText(
            Path.Combine(fixture.RootPath, reportId + ".txt"),
            "DTXMANIACX-CRASH-REPORT 1\n"
            + "ReportId: crash-20260802-120001Z-d4e5f6\n"
            + "CapturedAtUtc: 2026-08-02T12:00:01.0000000+00:00\n"
            + "BuildId: Secret Song Build\n"
            + "OperatingSystem: Secret Album OS\n"
            + "ProcessArchitecture: Secret Architecture\n"
            + "StageOrMilestone: SecretStage\n"
            + "ExceptionType: Secret.Exception\n"
            + "---\n");

        var report = Assert.Single(fixture.CreateStore().DiscoverCompletedReports());

        Assert.Equal(reportId, report.ReportId);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero), report.CapturedAtUtc);
        Assert.Equal("Unknown", report.BuildId);
        Assert.Equal("Unknown", report.OperatingSystem);
        Assert.Equal("Unknown", report.ProcessArchitecture);
        Assert.Equal("Unknown", report.StageOrMilestone);
        Assert.Equal("Unknown", report.ExceptionType);
        Assert.Equal(CrashReportFormat.EmergencyText, report.Format);
    }

    [Fact]
    public void Capture_SixMixedReports_ShouldRetainNewestFiveAcrossFormats()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        string? oldestReportId = null;

        for (var index = 0; index < 6; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            fixture.ArtifactWriter.FailZip = index % 2 == 1;
            var report = Assert.IsType<CrashReportSummary>(store.Capture(fixture.CreateCapture(index)).Report);
            oldestReportId ??= report.ReportId;
        }

        var reports = store.DiscoverCompletedReports();

        Assert.Equal(5, reports.Count);
        Assert.DoesNotContain(reports, report => report.ReportId == oldestReportId);
        Assert.Equal(
            reports.OrderByDescending(report => report.CapturedAtUtc)
                .ThenByDescending(report => report.FileName, StringComparer.Ordinal)
                .Select(report => report.ReportId),
            reports.Select(report => report.ReportId));
    }

    [Fact]
    public void Cleanup_ShouldDeleteOnlyTemporaryFilesOlderThanTwentyFourHours()
    {
        using var fixture = CrashStoreFixture.Create();
        var stalePath = Path.Combine(fixture.RootPath, ".crash-20260801-110000Z-a1b2c3.tmp");
        var recentPath = Path.Combine(fixture.RootPath, ".crash-20260802-115900Z-d4e5f6.tmp");
        File.WriteAllText(stalePath, "stale");
        File.WriteAllText(recentPath, "recent");
        File.SetLastWriteTimeUtc(stalePath, fixture.Clock.GetUtcNow().UtcDateTime.Subtract(TimeSpan.FromHours(25)));
        File.SetLastWriteTimeUtc(recentPath, fixture.Clock.GetUtcNow().UtcDateTime.Subtract(TimeSpan.FromHours(23)));
        var store = fixture.CreateStore();

        store.Cleanup();

        Assert.False(File.Exists(stalePath));
        Assert.True(File.Exists(recentPath));
        Assert.Empty(store.DiscoverCompletedReports());
    }

    [Fact]
    public void DiscoverCompletedReports_ShouldNeverExposeFreeFormEmergencyBody()
    {
        using var fixture = CrashStoreFixture.Create();
        const string secret = "Secret Song Title That Must Stay Local";
        const string reportId = "crash-20260802-120000Z-a1b2c3";
        File.WriteAllText(
            Path.Combine(fixture.RootPath, reportId + ".txt"),
            "DTXMANIACX-CRASH-REPORT 1\n"
            + "ReportId: crash-20260802-120000Z-a1b2c3\n"
            + "CapturedAtUtc: 2026-08-02T12:00:00.0000000+00:00\n"
            + "BuildId: 1.2.3\n"
            + "OperatingSystem: Test OS\n"
            + "ProcessArchitecture: X64\n"
            + "StageOrMilestone: Title\n"
            + "ExceptionType: System.InvalidOperationException\n"
            + "---\n"
            + secret);
        var store = fixture.CreateStore();

        var report = Assert.Single(store.DiscoverCompletedReports());
        var exposedSummary = string.Join(
            "|",
            report.ReportId,
            report.BuildId,
            report.OperatingSystem,
            report.ProcessArchitecture,
            report.StageOrMilestone,
            report.ExceptionType,
            report.FileName);

        Assert.DoesNotContain(secret, exposedSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenRootCannotBeCreated_ShouldReturnSafeFailureWithoutThrowing()
    {
        using var fixture = CrashStoreFixture.Create();
        var fileAsRootPath = Path.Combine(fixture.RootPath, "not-a-directory");
        File.WriteAllText(fileAsRootPath, "file");
        var store = new CrashReportStore(
            fileAsRootPath,
            fixture.ArtifactWriter,
            fixture.Clock,
            new StringWriter(CultureInfo.InvariantCulture));
        CrashReportWriteResult? result = null;

        var exception = Record.Exception(() => result = store.Capture(fixture.CreateCapture(0)));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Null(result.Report);
        Assert.False(result.UsedEmergencyFallback);
        Assert.Equal("capture_io_failure", result.FailureCode);
    }

    [Fact]
    public void Capture_WhenBothZipAndEmergencyFail_ShouldReturnFailureWithoutThrowing()
    {
        using var fixture = CrashStoreFixture.Create();
        fixture.ArtifactWriter.FailZip = true;
        fixture.ArtifactWriter.FailEmergency = true;
        var store = fixture.CreateStore();

        var result = store.Capture(fixture.CreateCapture(0));

        Assert.Null(result.Report);
        Assert.False(result.UsedEmergencyFallback);
        Assert.NotNull(result.FailureCode);
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*.zip", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*.txt", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void DiscoverCompletedReports_WhenRootDirectoryDoesNotExist_ShouldReturnEmpty()
    {
        using var fixture = CrashStoreFixture.Create();
        var missingRoot = Path.Combine(fixture.RootPath, "does-not-exist");
        var store = new CrashReportStore(
            missingRoot,
            fixture.ArtifactWriter,
            fixture.Clock,
            new StringWriter(CultureInfo.InvariantCulture));

        Assert.Empty(store.DiscoverCompletedReports());
    }

    [Fact]
    public void Cleanup_WhenRootDirectoryDoesNotExist_ShouldNotThrow()
    {
        using var fixture = CrashStoreFixture.Create();
        var missingRoot = Path.Combine(fixture.RootPath, "does-not-exist");
        var store = new CrashReportStore(
            missingRoot,
            fixture.ArtifactWriter,
            fixture.Clock,
            new StringWriter(CultureInfo.InvariantCulture));

        var exception = Record.Exception(() => store.Cleanup());

        Assert.Null(exception);
    }

    [Fact]
    public void DiscoverCompletedReports_WithValidZipReport_ShouldReturnSanitizedSummary()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var captured = Assert.IsType<CrashReportSummary>(store.Capture(fixture.CreateCapture(0)).Report);

        var discovered = Assert.Single(store.DiscoverCompletedReports());

        Assert.Equal(captured.ReportId, discovered.ReportId);
        Assert.Equal(captured.CapturedAtUtc, discovered.CapturedAtUtc);
        Assert.Equal(captured.BuildId, discovered.BuildId);
        Assert.Equal(captured.ProcessArchitecture, discovered.ProcessArchitecture);
        Assert.Equal(captured.StageOrMilestone, discovered.StageOrMilestone);
        Assert.Equal(captured.ExceptionType, discovered.ExceptionType);
        Assert.Equal(CrashReportFormat.ZipBundle, discovered.Format);
    }

    [Fact]
    public void DiscoverCompletedReports_WithValidEmergencyReport_ShouldReturnSanitizedSummary()
    {
        using var fixture = CrashStoreFixture.Create();
        fixture.ArtifactWriter.FailZip = true;
        var store = fixture.CreateStore();
        var captured = Assert.IsType<CrashReportSummary>(store.Capture(fixture.CreateCapture(0)).Report);

        var discovered = Assert.Single(store.DiscoverCompletedReports());

        Assert.Equal(captured.ReportId, discovered.ReportId);
        Assert.Equal(captured.CapturedAtUtc, discovered.CapturedAtUtc);
        Assert.Equal(CrashReportFormat.EmergencyText, discovered.Format);
    }

    [Fact]
    public void DiscoverCompletedReports_WithNonMatchingFileNames_ShouldBeIgnored()
    {
        using var fixture = CrashStoreFixture.Create();
        File.WriteAllText(Path.Combine(fixture.RootPath, "not-a-crash-report.txt"), "irrelevant");
        File.WriteAllText(Path.Combine(fixture.RootPath, "random-file.zip"), "irrelevant");
        var store = fixture.CreateStore();

        Assert.Empty(store.DiscoverCompletedReports());
    }

    [Fact]
    public void Capture_WithStartupMilestoneOnly_ShouldReportMilestoneAsStageOrMilestone()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Startup,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["Milestone"] = StartupCriticalPathMilestone.StartupActivation
                    })
            ]
        };

        var report = Assert.IsType<CrashReportSummary>(store.Capture(capture).Report);

        Assert.Equal(StartupCriticalPathMilestone.StartupActivation.ToString(), report.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithNoAvailableContext_ShouldReportUnknownStageOrMilestone()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.NotInitialized,
                    new Dictionary<string, object?>())
            ]
        };

        var report = Assert.IsType<CrashReportSummary>(store.Capture(capture).Report);

        Assert.Equal("Unknown", report.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithStartupStageOnly_ShouldReportUnknownStageOrMilestone()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["Stage"] = StageType.Startup
                    })
            ]
        };

        var report = Assert.IsType<CrashReportSummary>(store.Capture(capture).Report);

        Assert.Equal("Unknown", report.StageOrMilestone);
    }

    [Fact]
    public void Constructor_WithNullOrWhiteSpaceRootPath_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Throws<ArgumentException>(() => new CrashReportStore(
            "",
            fixture.ArtifactWriter,
            fixture.Clock,
            new StringWriter(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Constructor_WithNullWriter_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Throws<ArgumentNullException>(() => new CrashReportStore(
            fixture.RootPath,
            null!,
            fixture.Clock,
            new StringWriter(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Throws<ArgumentNullException>(() => new CrashReportStore(
            fixture.RootPath,
            fixture.ArtifactWriter,
            null!,
            new StringWriter(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Constructor_WithNullErrorWriter_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Throws<ArgumentNullException>(() => new CrashReportStore(
            fixture.RootPath,
            fixture.ArtifactWriter,
            fixture.Clock,
            null!));
    }

    [Fact]
    public void Capture_WithNullData_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();

        Assert.Throws<ArgumentNullException>(() => store.Capture(null!));
    }

    private static CrashReportDocument CreateArchiveDocument(Exception? exception = null)
    {
        var capturedAt = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var summary = new CrashReportSummary(
            "crash-20260802-120000Z-a1b2c3",
            capturedAt,
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
                    capturedAt,
                    LogLevel.Information,
                    new EventId(5100, "crash_safe_stage"),
                    "Crash-safe stage changed to {Stage}",
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title },
                    ExceptionType: null)
            ],
            [
                new CrashBreadcrumb(
                    capturedAt,
                    "stage_transition_completed",
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title })
            ],
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Stage"] = StageType.Title })
            ],
            [Path.Combine(Path.GetTempPath(), "Users", "alice")]);
    }

    private static void AssertPersistedPropertiesContainOnlySafeValues(JsonElement properties)
    {
        Assert.True(properties.TryGetProperty("Fullscreen", out var fullscreen));
        Assert.True(fullscreen.GetBoolean());
        Assert.False(properties.TryGetProperty("MidiDeviceCount", out _));
        Assert.False(properties.TryGetProperty("Count", out _));
        Assert.False(properties.TryGetProperty("Status", out _));
        Assert.False(properties.TryGetProperty("Width", out _));
        Assert.False(properties.TryGetProperty("Height", out _));
        Assert.False(properties.TryGetProperty("Stage", out _));
    }

    private sealed class CrashStoreFixture : IDisposable
    {
        private CrashStoreFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "dtx-crash-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            Clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));
            ArtifactWriter = new FakeArtifactWriter();
        }

        internal string RootPath { get; }
        internal ManualTimeProvider Clock { get; }
        internal FakeArtifactWriter ArtifactWriter { get; }

        internal static CrashStoreFixture Create()
        {
            return new CrashStoreFixture();
        }

        internal CrashReportStore CreateStore()
        {
            return new CrashReportStore(
                RootPath,
                ArtifactWriter,
                Clock,
                new StringWriter(CultureInfo.InvariantCulture));
        }

        internal CrashCaptureData CreateCapture(int index)
        {
            return new CrashCaptureData(
                new InvalidOperationException($"Secret Song {index}", new ArgumentException($"Secret Skin {index}")),
                [
                    new CrashLogRecord(
                        Clock.GetUtcNow(),
                        LogLevel.Information,
                        new EventId(5100, "crash_safe_stage"),
                        "Crash-safe stage changed to {Stage}",
                        new Dictionary<string, object?> { ["Stage"] = StageType.Title },
                        ExceptionType: null)
                ],
                [
                    new CrashBreadcrumb(
                        Clock.GetUtcNow(),
                        "stage_transition_completed",
                        new Dictionary<string, object?> { ["Stage"] = StageType.Title })
                ],
                [
                    new CrashContextSnapshot(
                        CrashContextKind.Stage,
                        CrashContextStatus.Available,
                        new Dictionary<string, object?> { ["Stage"] = StageType.Title })
                ],
                [Path.Combine(RootPath, "songs", "Secret Song")]);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class FakeArtifactWriter : ICrashReportArtifactWriter
    {
        private readonly CrashReportArchiveWriter _inner = new();

        internal bool FailZip { get; set; }
        internal bool FailEmergency { get; set; }
        internal int ZipWriteCount { get; private set; }
        internal int EmergencyWriteCount { get; private set; }
        internal string? LastZipDestinationPath { get; private set; }

        public void WriteZip(Stream destination, CrashReportDocument document)
        {
            ZipWriteCount++;
            LastZipDestinationPath = (destination as FileStream)?.Name;

            if (FailZip)
            {
                throw new IOException("Simulated ZIP writer failure.");
            }

            _inner.WriteZip(destination, document);
        }

        public void WriteEmergencyText(Stream destination, CrashReportDocument document)
        {
            EmergencyWriteCount++;

            if (FailEmergency)
            {
                throw new IOException("Simulated emergency writer failure.");
            }

            _inner.WriteEmergencyText(destination, document);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        internal ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        internal void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }
    }
}
