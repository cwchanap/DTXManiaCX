using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Song;

/// <summary>
/// SET.def/box.def definition tests (HPA-801 Task 4). The parser tests moved
/// here from the SongLibrary reflection suites (SongManagerParsingTests,
/// SongManagerSetDefReadLoggingTests, SongManagerTests and the
/// GetSetDefLabelsByFile region of SongManagerDifficultyLabelTests) once
/// SongDefinitionReader took ownership of definition line reading, encoding
/// fallback, normalization, parsing and metadata (HPA-801 Decision C/E).
/// </summary>
public class SongDefinitionReaderParsingTests : IDisposable
{
    private readonly SongDefinitionReader _reader = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Builds a reader with a mocked logger (and optionally an injected
    /// ReadAllLines seam) so tests can observe the warn-once behaviour.
    /// </summary>
    private static (SongDefinitionReader Reader, Mock<ILogger> Logger) CreateLoggedReader(
        Func<string, Encoding, CancellationToken, Task<string[]>>? readAllLines = null)
    {
        var logger = new Mock<ILogger>();
        return (new SongDefinitionReader(logger.Object, readAllLines), logger);
    }

    #region NormalizeSetDefLine

    [Fact]
    public void NormalizeSetDefLine_WhenNullLineIsProvided_ReturnsEmptyString()
    {
        var result = _reader.NormalizeSetDefLine(null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void NormalizeSetDefLine_WhenHashSeparatedCommandIsUsed_CollapsesToSingleCommand()
    {
        var result = _reader.NormalizeSetDefLine("#  TITLE My Song");

        Assert.Equal("#TITLE My Song", result);
    }

    [Fact]
    public void NormalizeSetDefLine_WhenBomAndNullBytesArePresent_StripsNoiseBeforeReturning()
    {
        var result = _reader.NormalizeSetDefLine("\uFEFF#TITLE My Song\u0000");

        Assert.Equal("#TITLE My Song", result);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("#  TITLE Sample Song", "#TITLE Sample Song")]
    [InlineData("#TITLE  Sample Song", "#TITLE Sample Song")]
    [InlineData("#ABC  Value", "#ABC Value")]
    [InlineData("  plain text  ", "plain text")]
    public void NormalizeSetDefLine_ShouldNormalizeSupportedFormats(string line, string expected)
    {
        var normalized = _reader.NormalizeSetDefLine(line);

        Assert.NotNull(normalized);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizeSetDefLine_WithSpacedCommand_ShouldNotReturnEmpty()
    {
        var result = _reader.NormalizeSetDefLine("# L 5 F I L E hard.dtx");

        Assert.NotNull(result);
        Assert.NotEmpty(result!);
        Assert.Contains("hard.dtx", result);
    }

    [Fact]
    public void NormalizeSetDefLine_WithL1LabelSpacedPattern_ShouldNotReturnEmpty()
    {
        var result = _reader.NormalizeSetDefLine("# L 1 L A B E L Expert");

        Assert.NotNull(result);
        Assert.NotEmpty(result!);
        Assert.Contains("Expert", result);
    }

    #endregion

    #region ReconstructCorruptedLine

    [Fact]
    public void ReconstructCorruptedLine_WhenSpacedKnownCommandIsUsed_RebuildsCommandAndValue()
    {
        var result = _reader.ReconstructCorruptedLine("#T I T L E My Song");

        Assert.Equal("#TITLE My Song", result);
    }

    [Fact]
    public void ReconstructCorruptedLine_WhenCommandCannotBeCompleted_UsesFinalFallback()
    {
        var result = _reader.ReconstructCorruptedLine("#A B C Mystery Value");

        Assert.Equal("#A B C Mystery Value", result);
    }

    #endregion

    #region TryParseColor

    [Theory]
    [MemberData(nameof(TryParseColorCases))]
    public void TryParseColor_WhenInputIsProvided_ReturnsExpectedResult(string colorValue, bool expectedParsed, Color expectedColor)
    {
        var parsed = _reader.TryParseColor(colorValue, out var color);

        Assert.Equal(expectedParsed, parsed);
        Assert.Equal(expectedColor, color);
    }

    [Fact]
    public void TryParseColor_WhenNullIsProvided_ReturnsFalseAndLeavesDefaultColor()
    {
        var parsed = _reader.TryParseColor(null, out var color);

        Assert.False(parsed);
        Assert.Equal(Color.White, color);
    }

    public static IEnumerable<object[]> TryParseColorCases()
    {
        yield return new object[] { "#112233", true, Color.FromArgb(0x11, 0x22, 0x33) };
        yield return new object[] { "Blue", true, Color.Blue };
        yield return new object[] { "#12", false, Color.White };
        yield return new object[] { "#GGGGGG", false, Color.White };
    }

    #endregion

    #region ParseBoxDefinitionAsync

    [Fact]
    public async Task ParseBoxDefinitionAsync_WithMissingFile_ShouldReturnNull()
    {
        var boxDef = await _reader.ParseBoxDefinitionAsync(
            Path.Combine(CreateTempDirectory(), "missing-box.def"),
            CancellationToken.None);

        Assert.Null(boxDef);
    }

    [Fact]
    public async Task ParseBoxDefinitionAsync_WithColorLines_ShouldParseColors()
    {
        var boxDefPath = Path.Combine(CreateTempDirectory(), "box.def");
        await File.WriteAllTextAsync(boxDefPath, """
            #TITLE: Colorful Box
            #BGCOLOR: #FF5733
            #TEXTCOLOR: #33FF57
            """);

        var boxDef = await _reader.ParseBoxDefinitionAsync(boxDefPath, CancellationToken.None);

        Assert.NotNull(boxDef);
        Assert.Equal("Colorful Box", boxDef!.Title);
        Assert.Equal(ColorTranslator.FromHtml("#FF5733"), boxDef.BackgroundColor);
        Assert.Equal(ColorTranslator.FromHtml("#33FF57"), boxDef.TextColor);
    }

    #endregion

    #region ReadSetDefLines (encoding fallback and warn-once logging)

    // HPA-796 Task 3 fix round 1: the SET.def encoding retry loop must keep
    // retrying every encoding, but report a persistent read failure exactly
    // once — after all attempts fail — with the SET.def path and the retained
    // exception, instead of one Warning per encoding attempt.

    [Fact]
    public void ReadSetDefLines_PersistentReadFailure_ShouldWarnOnceWithPathAndRetainedException()
    {
        // A directory path exists but cannot be read as a file, so every
        // encoding attempt fails with UnauthorizedAccessException/IOException.
        var (reader, logger) = CreateLoggedReader();
        var unreadableDir = Directory.CreateTempSubdirectory("setdef-");
        string unreadablePath = unreadableDir.FullName;
        try
        {
            var lines = reader.ReadSetDefLines(unreadablePath);

            Assert.Null(lines);
            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains(unreadablePath, StringComparison.Ordinal)),
                    It.Is<Exception?>(exception => exception != null),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            unreadableDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadSetDefLines_RepeatedReadsOfSameUnreadableFile_ShouldWarnOncePerFile()
    {
        var (reader, logger) = CreateLoggedReader();
        var unreadableDir = Directory.CreateTempSubdirectory("setdef-");
        try
        {
            // The DB hierarchy build and the freshness inventory both read the same file.
            Assert.Null(reader.ReadSetDefLines(unreadableDir.FullName));
            Assert.Null(reader.ReadSetDefLines(unreadableDir.FullName));

            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            unreadableDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadSetDefLines_ReadableFile_ShouldStaySilent()
    {
        var (reader, logger) = CreateLoggedReader();
        var setDefDir = Directory.CreateTempSubdirectory("setdef-");
        string setDefPath = Path.Combine(setDefDir.FullName, "SET.def");
        try
        {
            File.WriteAllLines(setDefPath, new[] { "#TITLE Test" });

            var lines = reader.ReadSetDefLines(setDefPath);

            Assert.NotNull(lines);
            logger.Verify(
                l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
        finally
        {
            setDefDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadSetDefLines_WhenInjectedReadAllLinesFails_ShouldRetryEveryEncodingThenWarnOnce()
    {
        // The injected ReadAllLines seam is the one read seam for the fallback
        // chain (HPA-801 Decision E): every configured encoding is retried, and
        // a persistent failure is still reported exactly once per file.
        var (reader, logger) = CreateLoggedReader((_, _, _) =>
            Task.FromException<string[]>(new IOException("injected read failure")));
        var path = Path.Combine(CreateTempDirectory(), "set.def");

        Assert.Null(reader.ReadSetDefLines(path));
        Assert.Null(reader.ReadSetDefLines(path));

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception?>(exception => exception is IOException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ReadSetDefLines_WhenInjectedReadAllLinesSuppliesLines_ShouldReturnThem()
    {
        // The sync database-load read reads through the same seam; no file on
        // disk is touched when the delegate supplies the lines.
        var reader = new SongDefinitionReader(readAllLines: (_, _, _) =>
            Task.FromResult(new[] { "#TITLE Injected" }));
        var missing = Path.Combine(
            CreateTempDirectory(), "not-on-disk", "set.def");

        Assert.Equal(new[] { "#TITLE Injected" }, reader.ReadSetDefLines(missing));
    }

    #endregion

    #region GetSetDefLabelsByFile

    [Fact]
    public void GetSetDefLabelsByFile_StandardSetDef_ShouldMapFilesToLabels()
    {
        var dir = CreateTempDirectory();
        var content =
            "#TITLE Soukyuu e no shouka\n" +
            "#L1LABEL BASIC\n#L1FILE bas.dtx\n" +
            "#L2LABEL ADVANCED\n#L2FILE adv.dtx\n" +
            "#L3LABEL EXTREME\n#L3FILE ext.dtx\n" +
            "#L4LABEL MASTER\n#L4FILE mas.dtx\n" +
            "#L5LABEL REAL\n#L5FILE real.dtx\n";
        File.WriteAllText(Path.Combine(dir, "set.def"), content, Encoding.UTF8);

        var labels = _reader.GetSetDefLabelsByFile(dir);

        Assert.Equal("BASIC", labels["bas.dtx"]);
        Assert.Equal("ADVANCED", labels["adv.dtx"]);
        Assert.Equal("EXTREME", labels["ext.dtx"]);
        Assert.Equal("MASTER", labels["mas.dtx"]);
        Assert.Equal("REAL", labels["real.dtx"]);
    }

    [Fact]
    public void GetSetDefLabelsByFile_Utf16SpacedFormat_ShouldMapFilesToLabels()
    {
        // DTXMania SET.def files are commonly UTF-16; read as UTF-8 they appear as the
        // spaced "# L 1 L A B E L   B A S I C" form. NormalizeSetDefLine must repair this so
        // the difficulty badge still resolves. This mirrors the real on-disk file that
        // exposed the original bug. The L5 label is non-ASCII (Japanese) to verify the
        // UTF-16 round-trip preserves multibyte characters, not just ASCII tier names.
        var dir = CreateTempDirectory();
        var content =
            "#TITLE Soukyuu\r\n" +
            "#L1LABEL BASIC\r\n#L1FILE bas.dtx\r\n" +
            "#L2LABEL ADVANCED\r\n#L2FILE adv.dtx\r\n" +
            "#L3LABEL EXTREME\r\n#L3FILE ext.dtx\r\n" +
            "#L4LABEL MASTER\r\n#L4FILE mas.dtx\r\n" +
            "#L5LABEL 初級\r\n#L5FILE haj.dtx\r\n";
        File.WriteAllText(Path.Combine(dir, "set.def"), content, Encoding.Unicode);

        var labels = _reader.GetSetDefLabelsByFile(dir);

        Assert.Equal("BASIC", labels["bas.dtx"]);
        Assert.Equal("ADVANCED", labels["adv.dtx"]);
        Assert.Equal("EXTREME", labels["ext.dtx"]);
        Assert.Equal("MASTER", labels["mas.dtx"]);
        Assert.Equal("初級", labels["haj.dtx"]);
    }

    [Fact]
    public void GetSetDefLabelsByFile_NoSetDef_ShouldReturnEmpty()
    {
        var dir = CreateTempDirectory();

        var labels = _reader.GetSetDefLabelsByFile(dir);

        Assert.Empty(labels);
    }

    [Fact]
    public void GetSetDefLabelsByFile_MalformedLCommand_ShouldNotThrow()
    {
        // A command like "#LABEL" starts with 'L' and ends with "LABEL" but is shorter than
        // the 6-char LABEL suffix. The level-token slice length is therefore negative and must
        // be guarded so Substring does not throw ArgumentOutOfRangeException during enumeration.
        var dir = CreateTempDirectory();
        var content =
            "#TITLE Soukyuu\n" +
            "#LABEL StrayLabel\n" +
            "#LFILE OrphanFile\n" +
            "#L1LABEL BASIC\n#L1FILE bas.dtx\n";
        File.WriteAllText(Path.Combine(dir, "set.def"), content, Encoding.UTF8);

        var labels = _reader.GetSetDefLabelsByFile(dir);

        // The malformed lines are skipped; the well-formed L1 pair still resolves.
        Assert.Equal("BASIC", labels["bas.dtx"]);
    }

    [Fact]
    public void GetSetDefLabelsByFile_RelativeSubdirectoryPath_ShouldNormalizeKeyToFileName()
    {
        // A legacy SET.def may reference a chart through a relative path such as
        // "#L1FILE charts/bas.dtx". ResolveDifficultyLabel looks up by
        // Path.GetFileName(chart.FilePath) ("bas.dtx"), so the label map must be keyed by
        // the bare file name rather than the raw relative path, otherwise subdirectory
        // references never match and the badge falls back to "Level N"/DTX.
        var dir = CreateTempDirectory();
        var content =
            "#TITLE Subdir song\n" +
            "#L1LABEL BASIC\n#L1FILE charts/bas.dtx\n" +
            "#L3LABEL EXTREME\n#L3FILE charts/ext.dtx\n";
        File.WriteAllText(Path.Combine(dir, "set.def"), content, Encoding.UTF8);

        var labels = _reader.GetSetDefLabelsByFile(dir);

        Assert.Equal("BASIC", labels["bas.dtx"]);
        Assert.Equal("EXTREME", labels["ext.dtx"]);
        Assert.False(labels.ContainsKey("charts/bas.dtx"));
        Assert.False(labels.ContainsKey("charts/ext.dtx"));
    }

    [Fact]
    public void GetSetDefLabelsByFile_ChartInSubdirectory_ShouldFindSetDefInParentFolder()
    {
        // A legacy SET.def may reference charts in a subdirectory ("#L1FILE charts/bas.dtx").
        // The DB-load path passes the chart's directory (".../song/charts") to this method, so
        // it must walk up to the song-folder root to find set.def. Without the walk-up the
        // label map is empty and the badge falls back to "Level N"/DTX even though SET.def
        // declares the label.
        var songDir = CreateTempDirectory();
        var chartsDir = Path.Combine(songDir, "charts");
        Directory.CreateDirectory(chartsDir);
        var content =
            "#TITLE Subdir song\n" +
            "#L1LABEL BASIC\n#L1FILE charts/bas.dtx\n" +
            "#L3LABEL EXTREME\n#L3FILE charts/ext.dtx\n";
        File.WriteAllText(Path.Combine(songDir, "set.def"), content, Encoding.UTF8);

        // Pass the chart's directory (charts/), not the song root.
        var labels = _reader.GetSetDefLabelsByFile(chartsDir);

        Assert.Equal("BASIC", labels["bas.dtx"]);
        Assert.Equal("EXTREME", labels["ext.dtx"]);
    }

    [Fact]
    public void GetSetDefLabelsByFile_NoSetDefAnywhere_ShouldReturnEmpty()
    {
        var nestedDir = Path.Combine(CreateTempDirectory(), "deep", "charts");
        Directory.CreateDirectory(nestedDir);

        var labels = _reader.GetSetDefLabelsByFile(nestedDir);

        Assert.Empty(labels);
    }

    [Fact]
    public void GetSetDefLabelsByFile_WhenInjectedReadAllLinesFails_ShouldReturnEmptyMap()
    {
        // The warm database-load label recovery must go through the same
        // ReadAllLines seam as the scan path (HPA-801 Decision C: no second
        // seam on the warm path).
        var (reader, _) = CreateLoggedReader((_, _, _) =>
            Task.FromException<string[]>(new IOException("injected read failure")));
        var dir = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(dir, "set.def"),
            "#L1LABEL BASIC\n#L1FILE bas.dtx\n",
            Encoding.UTF8);

        Assert.Empty(reader.GetSetDefLabelsByFile(dir));
    }

    [Fact]
    public void GetSetDefLabelsByFile_WhenInjectedReadAllLinesSuppliesLines_ShouldMapLabels()
    {
        var reader = new SongDefinitionReader(readAllLines: (_, _, _) =>
            Task.FromResult(new[] { "#L1LABEL BASIC", "#L1FILE bas.dtx" }));
        var dir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(dir, "set.def"), "ignored on disk", Encoding.UTF8);

        Assert.Equal("BASIC", reader.GetSetDefLabelsByFile(dir)["bas.dtx"]);
    }

    #endregion
}
