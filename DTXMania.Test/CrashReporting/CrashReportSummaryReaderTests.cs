#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportSummaryReaderTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 8, 6, 12, 34, 56, TimeSpan.Zero);

    private const string LogicalId = "crash-20260806-123456Z-a1b2c3";

    [Fact]
    public void Read_WithValidSchemaV2Header_ShouldPopulateEverySummaryField()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header(
                "ReportId: " + LogicalId,
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2.3",
                "OperatingSystem: macOS 15.0",
                "RuntimeVersion: .NET 8.0.0",
                "ProcessArchitecture: Arm64",
                "StageOrMilestone: SongSelect",
                "ExceptionType: System.InvalidOperationException",
                "Truncated: exception=False logs=False breadcrumbs=False"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("1.2.3", summary.BuildId);
        Assert.Equal("macOS 15.0", summary.OperatingSystem);
        Assert.Equal("Arm64", summary.ProcessArchitecture);
        Assert.Equal("SongSelect", summary.StageOrMilestone);
        Assert.Equal("System.InvalidOperationException", summary.ExceptionType);
        Assert.Equal(LogicalId + ".txt", summary.FileName);
    }

    [Fact]
    public void Read_WithMissingCorruptHeader_ShouldFallBackToFilenameDerivedValues()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            "NOT-A-CRASH-REPORT\nBuildId: should-be-ignored\n\n--- EXCEPTION ---\nbody");

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("Unknown", summary.BuildId);
        Assert.Equal("Unknown", summary.OperatingSystem);
        Assert.Equal("Unknown", summary.ProcessArchitecture);
        Assert.Equal("Unknown", summary.StageOrMilestone);
        Assert.Equal("Unknown", summary.ExceptionType);
    }

    [Fact]
    public void Read_WhenHeaderReportIdDiffersFromFilename_ShouldUseTheFilenameDerivedId()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header("ReportId: crash-20990101-000000Z-zzzzzz"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
    }

    [Fact]
    public void Read_WithMoreThanThirtyTwoHeaderLines_ShouldStopBoundedlyAndKeepParsedFields()
    {
        var padding = new StringBuilder();
        for (var index = 0; index < 48; index++)
        {
            padding.Append("Padding").Append(index.ToString(CultureInfo.InvariantCulture))
                .Append(": value\n");
        }

        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2.3")
            + padding
            + "\n--- EXCEPTION ---\n");

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("1.2.3", summary.BuildId);
    }

    [Fact]
    public void Read_WithMoreThanSixteenKibBeforeExceptionMarker_ShouldStopBoundedly()
    {
        var padding = new StringBuilder();
        for (var index = 0; index < 1000; index++)
        {
            padding.Append("Padding")
                .Append(index.ToString(CultureInfo.InvariantCulture).PadLeft(4, '0'))
                .Append(':')
                .Append(new string('p', 24))
                .Append('\n');
        }

        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2.3")
            + padding
            + "\n--- EXCEPTION ---\n");

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("1.2.3", summary.BuildId);
    }

    [Fact]
    public void Read_WithOneSingleLineLargerThanSixteenKib_ShouldStopWithoutThrowing()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: " + new string('y', 20_000),
                "OperatingSystem: never-reached"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        // The overlong BuildId line never completes within the 16 KiB budget, so it is
        // discarded rather than allocated in full.
        Assert.Equal("Unknown", summary.BuildId);
        Assert.Equal("Unknown", summary.OperatingSystem);
    }

    [Fact]
    public void Read_WithTenThousandCharacterFieldValue_ShouldCapTheValueTo256Characters()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header("BuildId: " + new string('z', 10_000)));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(CrashReportSummaryReader.MaximumFieldValueLength, summary.BuildId.Length);
        Assert.Equal(new string('z', CrashReportSummaryReader.MaximumFieldValueLength), summary.BuildId);
    }

    [Fact]
    public void Read_WithEmbeddedAsciiControls_ShouldReplaceThemWithSpaces()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header(
                "BuildId: a\tb\u0000c",
                "OperatingSystem: mac\rOS"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("a b c", summary.BuildId);
        Assert.Equal("mac OS", summary.OperatingSystem);
    }

    [Fact]
    public void Read_WithEmptyFieldValues_ShouldFallBackToUnknown()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header("BuildId: ", "OperatingSystem:"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("Unknown", summary.BuildId);
        Assert.Equal("Unknown", summary.OperatingSystem);
    }

    [Fact]
    public void Read_WithHugeExceptionBody_ShouldStopAtTheExceptionMarkerWithoutReadingIt()
    {
        var builder = new StringBuilder()
            .Append(HeaderLines(
                "ReportId: " + LogicalId,
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2.3"))
            .Append('\n')
            .Append(CrashReportTextWriter.ExceptionSection)
            .Append('\n')
            .Append(new string('E', 1024 * 1024));

        using var file = CrashReportFile.Write(LogicalId + ".txt", builder.ToString());

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("1.2.3", summary.BuildId);
    }

    [Fact]
    public void Read_WithUnparseableCapturedAt_ShouldFallBackToFilenameTimestamp()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header("CapturedAtUtc: not-a-date", "BuildId: 1.2.3"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("1.2.3", summary.BuildId);
    }

    private static string Header(params string[] fields)
    {
        return HeaderLines(fields) + "\n" + CrashReportTextWriter.ExceptionSection + "\n";
    }

    private static string HeaderLines(params string[] fields)
    {
        var builder = new StringBuilder()
            .Append(CrashReportTextWriter.Header)
            .Append('\n');
        foreach (var field in fields)
        {
            builder.Append(field).Append('\n');
        }
        return builder.ToString();
    }

    private sealed class CrashReportFile : IDisposable
    {
        internal string DirectoryPath { get; }
        internal string FilePath { get; }

        private CrashReportFile(string directoryPath, string filePath)
        {
            DirectoryPath = directoryPath;
            FilePath = filePath;
        }

        internal static CrashReportFile Write(string fileName, string content)
        {
            var directoryPath = Path.Combine(
                Path.GetTempPath(),
                "dtx-crash-reader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);
            var filePath = Path.Combine(directoryPath, fileName);
            File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new CrashReportFile(directoryPath, filePath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
