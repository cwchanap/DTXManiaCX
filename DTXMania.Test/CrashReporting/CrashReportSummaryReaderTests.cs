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
    public void Read_WithMoreThanThirtyTwoHeaderLines_ShouldStopAtTheThirtySecondLine()
    {
        // Header (line 1) + CapturedAtUtc (2) + BuildId (3) + 28 padding lines (4..31)
        // places ExceptionType on line 32 (the last line within the 32-line budget) and
        // StageOrMilestone on line 33 (the first line beyond it, which must NOT be parsed).
        // The whole document is ~700 chars, so the 16 KiB budget never binds here — only the
        // 32-line guard does. If that guard were removed or raised, StageOrMilestone would be
        // parsed and the final assertion would fail.
        var padding = new StringBuilder();
        for (var index = 0; index < 28; index++)
        {
            padding.Append("Padding").Append(index.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0'))
                .Append(": value\n");
        }

        var content = HeaderLines(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2.3")
            + padding
            + "ExceptionType: WithinThirtyTwoLineBudget\n"
            + "StageOrMilestone: BeyondThirtyTwoLineBudget\n"
            + "\n--- EXCEPTION ---\n";

        using var file = CrashReportFile.Write(LogicalId + ".txt", content);

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("1.2.3", summary.BuildId);
        Assert.Equal("WithinThirtyTwoLineBudget", summary.ExceptionType);
        Assert.Equal("Unknown", summary.StageOrMilestone);
    }

    [Fact]
    public void Read_WithMoreThanSixteenKibBeforeExceptionMarker_ShouldStopAtTheCharacterBudget()
    {
        // 20 padding lines of ~1011 chars each (~20 KiB) sit between the named fields and the
        // exception marker, so the cumulative char budget (>16 KiB) is exceeded BEFORE the
        // marker is reached. Each padding line is individually well under 16 KiB, so this
        // exercises the cumulative budget across many lines — complementing
        // Read_WithOneSingleLineLargerThanSixteenKib, which exercises a single overlong line.
        // The 32-line guard never binds here (line count stays at ~23). If the char-budget
        // guard were removed, StageOrMilestone would be parsed and the final assertion fail.
        var padding = new StringBuilder();
        for (var index = 0; index < 20; index++)
        {
            padding.Append("Padding").Append(index.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0'))
                .Append(':')
                .Append(new string('p', 1000))
                .Append('\n');
        }

        var content = HeaderLines(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2.3")
            + padding
            + "StageOrMilestone: BeyondSixteenKibBudget\n"
            + "\n--- EXCEPTION ---\n";

        using var file = CrashReportFile.Write(LogicalId + ".txt", content);

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("1.2.3", summary.BuildId);
        Assert.Equal("Unknown", summary.StageOrMilestone);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_WithBlankPath_ShouldThrow(string path)
    {
        Assert.Throws<ArgumentException>(() => new CrashReportSummaryReader().Read(path));
    }

    [Fact]
    public void Read_WhenFileDoesNotExist_ShouldReturnFilenameDerivedDefaults()
    {
        var reader = new CrashReportSummaryReader();

        var summary = reader.Read(Path.Combine(Path.GetTempPath(), "crash-20260806-123456Z-a1b2c3.txt"));

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("Unknown", summary.BuildId);
    }

    [Theory]
    [InlineData("crash-20260806-123456Z-a1b2c3.ack.txt", "crash-20260806-123456Z-a1b2c3")]
    [InlineData("crash-20260806-123456Z-a1b2c3.txt", "crash-20260806-123456Z-a1b2c3")]
    [InlineData("crash-20260806-123456Z-a1b2c3.log", "crash-20260806-123456Z-a1b2c3")]
    [InlineData("report", "report")]
    public void GetLogicalReportId_ShouldStripKnownSuffixesOrReturnFileNameWithoutExtension(
        string fileName, string expected)
    {
        Assert.Equal(expected, CrashReportSummaryReader.GetLogicalReportId(fileName));
    }

    [Fact]
    public void Read_WithFieldMissingColon_ShouldSkipThatLine()
    {
        // A line with no colon (or colon at position 0) should be silently skipped.
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            HeaderLines(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "NoColonHere",
                ": startsWithColon",
                "BuildId: 1.2.3")
            + "\n" + CrashReportTextWriter.ExceptionSection + "\n");

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("1.2.3", summary.BuildId);
    }

    [Fact]
    public void Read_WithFieldValueContainingColon_ShouldSplitOnlyOnFirstColon()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2:3:4"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("1.2:3:4", summary.BuildId);
    }

    [Fact]
    public void Read_WithFieldValueContainingOnlyControlChars_ShouldFallBackToUnknown()
    {
        // After replacing control chars with spaces and trimming, the value is empty -> Unknown.
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header("BuildId: \u0001\u0002\u0003"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("Unknown", summary.BuildId);
    }

    [Fact]
    public void Read_WithDelCharacter_ShouldReplaceWithSpace()
    {
        // 0x7F (DEL) should be replaced with a space.
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            Header("BuildId: a\u007fb"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("a b", summary.BuildId);
    }

    [Fact]
    public void Read_WithNoCapturedAtAndUnparseableFilenameTimestamp_ShouldFallBackToEpoch()
    {
        // A filename that doesn't match the "crash-" prefix pattern: the timestamp fallback
        // fails, so the summary falls back to Unix epoch (0).
        using var file = CrashReportFile.Write(
            "report.txt",
            Header("BuildId: 1.2.3"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(0, summary.CapturedAtUtc.ToUnixTimeSeconds());
        Assert.Equal("1.2.3", summary.BuildId);
    }

    [Fact]
    public void Read_WithFilenameMissingZDesignator_ShouldFallBackToEpoch()
    {
        // The timestamp parser requires a 'z' at position 15; a filename without it fails.
        using var file = CrashReportFile.Write(
            "crash-20260806-123456X-a1b2c3.txt",
            Header("BuildId: 1.2.3"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(0, summary.CapturedAtUtc.ToUnixTimeSeconds());
    }

    [Fact]
    public void Read_WithFilenameHavingWrongStampLength_ShouldFallBackToEpoch()
    {
        // The stamp (before 'z') must be exactly 15 chars with a dash at position 8.
        using var file = CrashReportFile.Write(
            "crash-20260806-12345Z-a1b2c3.txt",
            Header("BuildId: 1.2.3"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(0, summary.CapturedAtUtc.ToUnixTimeSeconds());
    }

    [Fact]
    public void Read_WithFilenameMissingDashInStamp_ShouldFallBackToEpoch()
    {
        // The stamp must have a dash at position 8; without it, parsing fails.
        using var file = CrashReportFile.Write(
            "crash-20260806123456Z-a1b2c3.txt",
            Header("BuildId: 1.2.3"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(0, summary.CapturedAtUtc.ToUnixTimeSeconds());
    }

    [Fact]
    public void Read_WithFilenameHavingInvalidDateDigits_ShouldFallBackToEpoch()
    {
        // The stamp has valid structure but invalid date values (month 13).
        using var file = CrashReportFile.Write(
            "crash-20261306-123456Z-a1b2c3.txt",
            Header("BuildId: 1.2.3"));

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(0, summary.CapturedAtUtc.ToUnixTimeSeconds());
    }

    [Fact]
    public void Read_WithBlankLineBeforeExceptionMarker_ShouldSkipIt()
    {
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            HeaderLines(
                "CapturedAtUtc: " + CapturedAt.ToString("O", CultureInfo.InvariantCulture),
                "BuildId: 1.2.3")
            + "\n\n\n" + CrashReportTextWriter.ExceptionSection + "\n");

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("1.2.3", summary.BuildId);
    }

    [Fact]
    public void Read_WithNoExceptionMarkerAndNoFieldLines_ShouldReturnDefaults()
    {
        // Just the header line followed by EOF (no exception marker, no fields).
        using var file = CrashReportFile.Write(
            LogicalId + ".txt",
            CrashReportTextWriter.Header + "\n");

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal(LogicalId, summary.ReportId);
        Assert.Equal(CapturedAt, summary.CapturedAtUtc);
        Assert.Equal("Unknown", summary.BuildId);
    }

    [Fact]
    public void Read_WithMissingExceptionMarkerAndBodyContainingAllowlistedKey_ShouldStopAtFirstBlankLine()
    {
        // The exception marker is corrupted/missing, so the blank line that the normal writer
        // emits between metadata and the body is the only reliable header boundary. Body
        // content containing an allowlisted key (e.g. another "BuildId: ...") must NOT
        // overwrite the genuine header value and subsequently appear in the title UI or the
        // prefilled GitHub issue.
        var content = CrashReportTextWriter.Header + "\n"
            + "BuildId: legitimate\n"
            + "\n"
            + "BROKEN-EXCEPTION-MARKER\n"
            + "BuildId: body-secret\n";

        using var file = CrashReportFile.Write(LogicalId + ".txt", content);

        var summary = new CrashReportSummaryReader().Read(file.FilePath);

        Assert.Equal("legitimate", summary.BuildId);
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
