#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;

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
