using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Process;
using DTXMania.E2E.Support;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class CrashReportingSmokeTests
{
    private const string ControlledCrashMessage = "DTXMANIA_E2E_CONTROLLED_CRASH";

    [Theory(Timeout = 180_000)]
    [InlineData("update")]
    [InlineData("draw")]
    public async Task ControlledCallbackCrash_ShouldReachProgramBoundaryExactlyOnce(
        string injectionPoint)
    {
        var repoRoot = FindRepoRoot();
        var runRoot = Path.Combine(
            Path.GetTempPath(),
            "dtx-crash-e2e-" + Guid.NewGuid().ToString("N"));
        var fixture = E2EFixtureBuilder.Build(
            runRoot,
            repoRoot,
            GetAvailablePort());
        await using var process = new GameProcessDriver();

        try
        {
            process.Start(
                repoRoot,
                "DTXMania.Game/DTXMania.Game.Windows.csproj",
                fixture,
                environmentOverrides: new Dictionary<string, string?>
                {
                    ["DTXMANIA_E2E_CRASH_INJECTION"] = injectionPoint
                });

            var exitCode = await process.WaitForExitAsync(
                TimeSpan.FromSeconds(120),
                CancellationToken.None);
            Assert.NotEqual(0, exitCode);

            var reportRoot = Path.Combine(fixture.AppDataRoot, "CrashReports");
            var reports = Directory.EnumerateFiles(reportRoot, "crash-*")
                .Where(path => Path.GetExtension(path) is ".zip" or ".txt")
                .ToArray();
            var reportPath = Assert.Single(reports);
            Assert.Empty(Directory.EnumerateFiles(reportRoot, "*.tmp"));

            var summary = ReadCrashSummary(reportPath);
            Assert.Equal("InvalidOperationException", summary.ExceptionType);
            Assert.Equal(
                1,
                ReadPrimaryExceptionText(reportPath)
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

    private static CrashReportSummary ReadCrashSummary(string reportPath)
    {
        if (string.Equals(Path.GetExtension(reportPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(reportPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry("report.json")
                ?? throw new InvalidDataException("Crash ZIP does not contain report.json.");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            return new CrashReportSummary(
                root.GetProperty("reportId").GetString()!,
                root.GetProperty("capturedAtUtc").GetDateTimeOffset(),
                root.GetProperty("buildId").GetString()!,
                root.GetProperty("operatingSystem").GetString()!,
                root.GetProperty("processArchitecture").GetString()!,
                root.GetProperty("stageOrMilestone").GetString()!,
                root.GetProperty("exceptionType").GetString()!,
                CrashReportFormat.ZipBundle,
                Path.GetFileName(reportPath));
        }

        using var emergencyReader = new StreamReader(reportPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (!string.Equals(emergencyReader.ReadLine(), "DTXMANIACX-CRASH-REPORT 1", StringComparison.Ordinal))
            throw new InvalidDataException("Crash emergency report has an unrecognized header.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string? line;
        while ((line = emergencyReader.ReadLine()) is not null && !string.Equals(line, "---", StringComparison.Ordinal))
        {
            var separatorIndex = line.IndexOf(": ", StringComparison.Ordinal);
            if (separatorIndex <= 0)
                throw new InvalidDataException("Crash emergency report has a malformed header field.");

            fields[line[..separatorIndex]] = line[(separatorIndex + 2)..];
        }

        if (line is null)
            throw new InvalidDataException("Crash emergency report did not terminate its header.");

        return new CrashReportSummary(
            fields["ReportId"],
            DateTimeOffset.Parse(fields["CapturedAtUtc"], CultureInfo.InvariantCulture),
            fields["BuildId"],
            fields["OperatingSystem"],
            fields["ProcessArchitecture"],
            fields["StageOrMilestone"],
            fields["ExceptionType"],
            CrashReportFormat.EmergencyText,
            Path.GetFileName(reportPath));
    }

    private static string ReadPrimaryExceptionText(string reportPath)
    {
        if (string.Equals(Path.GetExtension(reportPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(reportPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry("exception.txt")
                ?? throw new InvalidDataException("Crash ZIP does not contain exception.txt.");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        using var emergencyReader = new StreamReader(reportPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (emergencyReader.ReadLine() is { } line)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
                return emergencyReader.ReadToEnd();
        }

        throw new InvalidDataException("Crash emergency report did not contain an exception body.");
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

    private static int GetAvailablePort()
    {
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var chosen = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            try
            {
                var verificationListener = new TcpListener(IPAddress.Loopback, chosen);
                verificationListener.Start();
                verificationListener.Stop();
                return chosen;
            }
            catch (SocketException)
            {
            }
        }

        using var fallback = new TcpListener(IPAddress.Loopback, 0);
        fallback.Start();
        return ((IPEndPoint)fallback.LocalEndpoint).Port;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DTXMania.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from current directory.");
    }
}
