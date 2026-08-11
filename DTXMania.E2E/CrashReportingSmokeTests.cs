using System.Text;
using DTXMania.Automation.Process;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class CrashReportingSmokeTests
{
    private const string ControlledCrashMessage = "DTXMANIA_E2E_CONTROLLED_CRASH";

    // Spelled out rather than referenced from CrashReportTextWriter: this is a black-box check
    // that the on-disk format is what an external reader expects, so it should fail if the
    // writer's markers change.
    private const string ReportHeader = "DTXMANIACX-CRASH-REPORT 2";
    private const string ExceptionSection = "--- EXCEPTION ---";
    private const string ContextSection = "--- CONTEXT ---";

    [Theory(Timeout = 180_000)]
    [InlineData("update")]
    [InlineData("draw")]
    public async Task ControlledCallbackCrash_ShouldReachProgramBoundaryExactlyOnce(
        string injectionPoint)
    {
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var runRoot = Path.Combine(
            Path.GetTempPath(),
            "dtx-crash-e2e-" + Guid.NewGuid().ToString("N"));
        var fixture = E2EFixtureBuilder.Build(
            runRoot,
            repoRoot,
            apiPort);
        await using var process = new GameProcessDriver();

        try
        {
            process.Start(E2EGameLaunch.CreateOptions(
                fixture,
                extraEnvironment: new Dictionary<string, string?>
                {
                    ["DTXMANIA_E2E_CRASH_INJECTION"] = injectionPoint
                }));

            var exitCode = await process.WaitForExitAsync(
                TimeSpan.FromSeconds(120),
                CancellationToken.None);
            Assert.NotEqual(0, exitCode);

            var reportRoot = Path.Combine(fixture.AppDataRoot, "CrashReports");
            var reports = Directory.EnumerateFiles(reportRoot, "crash-*").ToArray();
            var reportPath = Assert.Single(reports);
            Assert.EndsWith(".txt", reportPath, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFiles(reportRoot, "*.tmp"));

            var header = ReadCrashHeader(reportPath);
            Assert.Equal(typeof(InvalidOperationException).FullName, header["ExceptionType"]);
            Assert.Equal(
                1,
                ReadExceptionSection(reportPath)
                    .Split(ControlledCrashMessage, StringSplitOptions.None)
                    .Length - 1);
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(
                fixture,
                $"crash-{injectionPoint}-stdout.log",
                process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(
                fixture,
                $"crash-{injectionPoint}-stderr.log",
                process.StandardError);
            CopyCrashReports(fixture);
        }
    }

    private static Dictionary<string, string> ReadCrashHeader(string reportPath)
    {
        using var reader = new StreamReader(reportPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (!string.Equals(reader.ReadLine(), ReportHeader, StringComparison.Ordinal))
            throw new InvalidDataException("Crash report has an unrecognized header.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line && line.Length > 0)
        {
            var separatorIndex = line.IndexOf(": ", StringComparison.Ordinal);
            if (separatorIndex <= 0)
                throw new InvalidDataException("Crash report has a malformed header field: " + line);

            fields[line[..separatorIndex]] = line[(separatorIndex + 2)..];
        }

        return fields;
    }

    /// <summary>
    /// Returns the exception section only, so the assertion on how many times the controlled
    /// crash message appears is not confused by the log or breadcrumb sections.
    /// </summary>
    private static string ReadExceptionSection(string reportPath)
    {
        var text = File.ReadAllText(reportPath);
        var start = text.IndexOf(ExceptionSection, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidDataException("Crash report did not contain an exception section.");

        start += ExceptionSection.Length;
        var end = text.IndexOf(ContextSection, start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static void CopyCrashReports(E2EFixture fixture)
    {
        var reportRoot = Path.Combine(fixture.AppDataRoot, "CrashReports");
        if (!Directory.Exists(reportRoot))
            return;

        Directory.CreateDirectory(fixture.ArtifactRoot);
        foreach (var reportPath in Directory.EnumerateFiles(reportRoot, "crash-*"))
        {
            File.Copy(
                reportPath,
                Path.Combine(fixture.ArtifactRoot, Path.GetFileName(reportPath)),
                overwrite: true);
        }
    }

}
