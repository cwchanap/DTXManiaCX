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
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.Utilities;
using Microsoft.Extensions.Logging;

namespace DTXMania.Test.CrashReporting;

/// <summary>
/// Uses the existing AppPaths collection because these scenarios temporarily replace the
/// process-wide app-data override.
/// </summary>
[Collection("AppPaths")]
[Trait("Category", "Unit")]
public sealed class CrashReportIntegrationTests
{
    [Fact]
    public void Run_WhenFactoryThrowsBeforeGameConstruction_ShouldWriteOneSanitizedReport()
    {
        RunWithTemporaryAppData(appData =>
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                StartupTimingTrace.Disabled,
                TextWriter.Null);
            var secretSongPath = Path.Combine(appData.Path, "Secret Song.dtx");
            appData.RegisterSensitiveValues("Secret Song", secretSongPath);

            var exitCode = GameEntryPoint.Run(
                () => throw new InvalidOperationException("song=" + secretSongPath),
                runtime,
                TextWriter.Null);

            Assert.Equal(1, exitCode);
            var reportPath = Assert.Single(EnumerateCompletedReportPaths());
            var allText = CrashReportTestReader.ReadAllText(reportPath);

            Assert.DoesNotContain("Secret Song", allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(appData.Path, allText, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFiles(AppPaths.GetCrashReportsRoot(), "*.tmp"));
        });
    }

    [Fact]
    public void CaptureFatal_WhenContextCollectionFails_ShouldPersistOnlySanitizedCachedData()
    {
        RunWithTemporaryAppData(appData =>
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                StartupTimingTrace.Disabled,
                TextWriter.Null);
            const string apiKeyName = "GameApiKey";
            const string apiKey = "api-key-integration-should-not-persist";
            const string username = "CrashIntegrationUsername";
            const string songTitle = "Integration Secret Song";
            const string midiStableId = "MIDI-STABLE-ID-DO-NOT-PERSIST";
            const string renderedLog = "RENDERED-LOG-STRING-DO-NOT-PERSIST";
            var dtxPath = Path.Combine(appData.Path, username, "Songs", songTitle + ".dtx");
            var skinPath = Path.Combine(appData.Path, username, "Skins", "PrivateSkin");
            appData.RegisterSensitiveValues(
                apiKeyName,
                apiKey,
                username,
                dtxPath,
                skinPath,
                songTitle,
                midiStableId,
                renderedLog);

            runtime.GameDiagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
                CrashContextKind.Graphics,
                CrashContextStatus.CollectionFailed,
                new Dictionary<string, object?>
                {
                    [apiKeyName] = apiKey,
                    ["DTXPath"] = dtxPath,
                    ["SkinPath"] = skinPath,
                    ["SongTitle"] = songTitle,
                    ["MidiStableId"] = midiStableId
                },
                FailureCode: "graphics_context_collection_failed"));
            runtime.GameDiagnostics.SensitiveData.RegisterPath(dtxPath);
            runtime.GameDiagnostics.SensitiveData.RegisterPath(skinPath);
            runtime.GameDiagnostics.Breadcrumbs.Record(
                "midi_device_attached",
                new Dictionary<string, object?>
                {
                    ["Status"] = midiStableId,
                    ["MidiStableId"] = midiStableId
                });
            var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("integration");
            logger.LogInformation($"Rendered crash log: {renderedLog}");

            runtime.CaptureFatal(new InvalidOperationException(
                $"{apiKeyName}={apiKey}; user={username}; song={songTitle}; "
                + $"dtx={dtxPath}; skin={skinPath}; midi={midiStableId}"));

            var reportPath = Assert.Single(EnumerateCompletedReportPaths());
            var allText = CrashReportTestReader.ReadAllText(reportPath);
            using var report = CrashReportTestReader.ReadZipReportDocument(reportPath);
            var contexts = report.RootElement.GetProperty("contextStatuses").EnumerateArray().ToArray();
            var graphics = Assert.Single(
                contexts,
                context => context.GetProperty("kind").GetString() == "Graphics");

            Assert.Equal("CollectionFailed", graphics.GetProperty("status").GetString());
            Assert.Empty(graphics.GetProperty("fields").EnumerateObject());
            Assert.Equal("[REDACTED]", graphics.GetProperty("failureCode").GetString());
            Assert.DoesNotContain(apiKeyName, allText, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, allText, StringComparison.Ordinal);
            Assert.DoesNotContain(username, allText, StringComparison.Ordinal);
            Assert.DoesNotContain(dtxPath, allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(skinPath, allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(songTitle, allText, StringComparison.Ordinal);
            Assert.DoesNotContain(midiStableId, allText, StringComparison.Ordinal);
            Assert.DoesNotContain(renderedLog, allText, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(AppPaths.GetCrashReportsRoot(), "*.tmp"));
        });
    }

    [Fact]
    public void CaptureFatal_WhenZipArchiveFails_ShouldDiscoverZipAndEmergencySummaries()
    {
        RunWithTemporaryAppData(appData =>
        {
            var reportRoot = AppPaths.GetCrashReportsRoot();

            using (var emergencyRuntime = CreateEnabledRuntime(
                       reportRoot,
                       new ZipFailingArtifactWriter(),
                       TimeProvider.System))
            {
                emergencyRuntime.CaptureFatal(new InvalidOperationException("emergency capture"));
            }

            using (var zipRuntime = CreateEnabledRuntime(
                       reportRoot,
                       new CrashReportArchiveWriter(),
                       TimeProvider.System))
            {
                zipRuntime.CaptureFatal(new InvalidOperationException("zip capture"));
            }

            var store = new CrashReportStore(
                reportRoot,
                new CrashReportArchiveWriter(),
                TimeProvider.System,
                TextWriter.Null);
            var reports = store.DiscoverCompletedReports();
            var zipSummary = Assert.Single(reports, report => report.Format == CrashReportFormat.ZipBundle);
            var emergencySummary = Assert.Single(
                reports,
                report => report.Format == CrashReportFormat.EmergencyText);
            var paths = EnumerateCompletedReportPaths();

            Assert.Equal(2, reports.Count);
            Assert.Equal(typeof(InvalidOperationException).FullName, zipSummary.ExceptionType);
            Assert.Equal(typeof(InvalidOperationException).FullName, emergencySummary.ExceptionType);
            Assert.Equal(
                CrashReportFormat.ZipBundle,
                CrashReportTestReader.ReadSummary(
                    Path.Combine(reportRoot, zipSummary.FileName)).Format);
            Assert.Equal(
                CrashReportFormat.EmergencyText,
                CrashReportTestReader.ReadSummary(
                    Path.Combine(reportRoot, emergencySummary.FileName)).Format);
            Assert.Equal(2, paths.Count);
            Assert.Empty(Directory.EnumerateFiles(reportRoot, "*.tmp"));
        });
    }

    [Fact]
    public void CreateBestEffort_WhenBootstrapDegrades_ShouldKeepConsoleLoggingAndRunnerFunctional()
    {
        RunWithTemporaryAppData(_ =>
        {
            using var errorWriter = new StringWriter(CultureInfo.InvariantCulture);
            using var consoleWriter = new StringWriter(CultureInfo.InvariantCulture);
            var originalConsoleOut = Console.Out;

            try
            {
                Console.SetOut(consoleWriter);
                using var runtime = CrashReportRuntime.CreateBestEffort(
                    StartupTimingTrace.Disabled,
                    errorWriter,
                    storeFactory: () => throw new UnauthorizedAccessException("denied"));
                var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("integration");
                var runCalls = 0;

                logger.LogInformation("console fallback log survives");
                var exitCode = GameEntryPoint.Run(
                    () => new FakeGameApplication(
                        run: () => runCalls++,
                        dispose: () => { }),
                    runtime,
                    errorWriter);

                Assert.Equal(1, runCalls);
                Assert.Equal(0, exitCode);
                Assert.False(runtime.IsCaptureEnabled);
                Assert.Contains("crash_reporting_disabled code=bootstrap_failure", errorWriter.ToString());
                Assert.Contains("console fallback log survives", consoleWriter.ToString());
                Assert.False(Directory.Exists(AppPaths.GetCrashReportsRoot()));
            }
            finally
            {
                Console.SetOut(originalConsoleOut);
            }
        });
    }

    [Fact]
    public void Run_WhenGameDisposalFails_ShouldPreservePrimaryExceptionInReportAndExitCode()
    {
        RunWithTemporaryAppData(_ =>
        {
            using var errorWriter = new StringWriter(CultureInfo.InvariantCulture);
            using var runtime = CrashReportRuntime.CreateBestEffort(
                StartupTimingTrace.Disabled,
                errorWriter);
            var disposeCalls = 0;
            var game = new FakeGameApplication(
                run: () => throw new InvalidOperationException("primary failure"),
                dispose: () =>
                {
                    disposeCalls++;
                    throw new IOException("secondary disposal failure");
                });

            var exitCode = GameEntryPoint.Run(() => game, runtime, errorWriter);

            Assert.Equal(1, exitCode);
            Assert.Equal(1, disposeCalls);
            Assert.Contains(
                "crash_reporting_secondary_failure code=game_dispose_failed",
                errorWriter.ToString());
            var reportPath = Assert.Single(EnumerateCompletedReportPaths());
            var summary = CrashReportTestReader.ReadSummary(reportPath);

            Assert.Equal(typeof(InvalidOperationException).FullName, summary.ExceptionType);
            Assert.Empty(Directory.EnumerateFiles(AppPaths.GetCrashReportsRoot(), "*.tmp"));
        });
    }

    [Fact]
    public void CaptureFatal_SixTimesThroughRuntime_ShouldRetainExactlyFiveCompletedReports()
    {
        RunWithTemporaryAppData(_ =>
        {
            var reportRoot = AppPaths.GetCrashReportsRoot();
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));
            var firstCaptureAtUtc = clock.GetUtcNow();

            using (var runtime = CreateEnabledRuntime(
                       reportRoot,
                       new CrashReportArchiveWriter(),
                       clock))
            {
                for (var index = 0; index < 6; index++)
                {
                    runtime.CaptureFatal(new InvalidOperationException("capture " + index));
                    clock.Advance(TimeSpan.FromSeconds(1));
                }
            }

            var store = new CrashReportStore(
                reportRoot,
                new CrashReportArchiveWriter(),
                clock,
                TextWriter.Null);
            var reports = store.DiscoverCompletedReports();

            Assert.Equal(5, reports.Count);
            Assert.DoesNotContain(reports, report => report.CapturedAtUtc == firstCaptureAtUtc);
            Assert.All(reports, report => Assert.Equal(CrashReportFormat.ZipBundle, report.Format));
            Assert.Equal(5, EnumerateCompletedReportPaths().Count);
            Assert.Empty(Directory.EnumerateFiles(reportRoot, "*.tmp"));
        });
    }

    [Fact]
    public void PreserveFailureArtifact_WhenReportPassedSanitization_ShouldCopyExactlyOneReport()
    {
        RunWithTemporaryAppData(appData =>
        {
            var verificationRoot = Path.Combine(
                TemporaryAppDataRoot.GetFailureArtifactRoot(),
                "verification");
            Directory.CreateDirectory(verificationRoot);

            try
            {
                using var runtime = CrashReportRuntime.CreateBestEffort(
                    StartupTimingTrace.Disabled,
                    TextWriter.Null);
                runtime.CaptureFatal(new InvalidOperationException("failure artifact verification"));

                var preservedPath = appData.PreserveFailureArtifact(verificationRoot);

                Assert.NotNull(preservedPath);
                Assert.True(File.Exists(preservedPath));
                Assert.Single(TemporaryAppDataRoot.EnumerateFailureArtifacts(verificationRoot));
                Assert.DoesNotContain(
                    appData.Path,
                    CrashReportTestReader.ReadAllText(preservedPath),
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                TemporaryAppDataRoot.DeleteFailureArtifacts(verificationRoot);
            }
        });
    }

    private static void RunWithTemporaryAppData(Action<TemporaryAppDataRoot> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var appData = new TemporaryAppDataRoot();
        var completedSuccessfully = false;
        try
        {
            action(appData);
            completedSuccessfully = true;
        }
        catch
        {
            PreserveFailureArtifactBestEffort(appData);
            throw;
        }
        finally
        {
            if (completedSuccessfully)
            {
                try
                {
                    appData.Dispose();
                }
                catch
                {
                    PreserveFailureArtifactBestEffort(appData);
                    appData.DisposeBestEffort();
                    throw;
                }
            }
            else
            {
                appData.DisposeBestEffort();
            }
        }
    }

    private static void PreserveFailureArtifactBestEffort(TemporaryAppDataRoot appData)
    {
        try
        {
            appData.PreserveFailureArtifact();
        }
        catch
        {
            // The assertion or cleanup error remains the primary test result.
        }
    }

    private static CrashReportRuntime CreateEnabledRuntime(
        string reportRoot,
        ICrashReportArtifactWriter artifactWriter,
        TimeProvider timeProvider)
    {
        var logBufferProvider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            timeProvider,
            capacity: 16);
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logBufferProvider));

        return new CrashReportRuntime(
            loggerFactory,
            logBufferProvider,
            new CrashBreadcrumbBuffer(timeProvider, capacity: 16),
            new CrashContextSnapshotStore(),
            new CrashReportStore(reportRoot, artifactWriter, timeProvider, TextWriter.Null),
            TextWriter.Null);
    }

    private static IReadOnlyList<string> EnumerateCompletedReportPaths()
    {
        var reportRoot = AppPaths.GetCrashReportsRoot();
        if (!Directory.Exists(reportRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(reportRoot, "crash-*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path) is ".zip" or ".txt")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class FakeGameApplication : IGameApplication
    {
        private readonly Action _run;
        private readonly Action _dispose;

        internal FakeGameApplication(Action run, Action dispose)
        {
            _run = run;
            _dispose = dispose;
        }

        public void Run()
        {
            _run();
        }

        public void Dispose()
        {
            _dispose();
        }
    }

    private sealed class ZipFailingArtifactWriter : ICrashReportArtifactWriter
    {
        private readonly CrashReportArchiveWriter _inner = new();

        public void WriteZip(Stream destination, CrashReportDocument document)
        {
            throw new IOException("Simulated ZIP writer failure.");
        }

        public void WriteEmergencyText(Stream destination, CrashReportDocument document)
        {
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

internal sealed class TemporaryAppDataRoot : IDisposable
{
    private const string AppDataRootEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
    private const string FailureArtifactFileName = "latest-crash-report";
    private static readonly object FailureArtifactGate = new();
    private readonly string? _previousAppDataRoot;
    private readonly HashSet<string> _sensitiveValues = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    internal TemporaryAppDataRoot()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dtx-crash-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        _previousAppDataRoot = Environment.GetEnvironmentVariable(AppDataRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppDataRootEnvironmentVariable, Path);
        _sensitiveValues.Add(Path);
    }

    internal string Path { get; }

    internal static string GetFailureArtifactRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "DTXMania.sln")))
            {
                return System.IO.Path.Combine(current.FullName, "TestResults", "CrashReporting");
            }

            current = current.Parent;
        }

        return System.IO.Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "CrashReporting");
    }

    internal void RegisterSensitiveValues(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _sensitiveValues.Add(value);
            }
        }
    }

    /// <summary>
    /// Preserves exactly one completed report for a failing integration scenario. A raw report is
    /// copied only after the test's registered sensitive values are absent from its approved
    /// entries; otherwise a fixed, content-free diagnostic is retained instead.
    /// </summary>
    internal string? PreserveFailureArtifact(string? destinationRoot = null)
    {
        lock (FailureArtifactGate)
        {
            var artifactRoot = destinationRoot ?? GetFailureArtifactRoot();
            Directory.CreateDirectory(artifactRoot);
            DeleteFailureArtifacts(artifactRoot);
            Directory.CreateDirectory(artifactRoot);

            var sourcePath = FindNewestCompletedReportPath();
            if (sourcePath is not null && IsSafeToCopy(sourcePath))
            {
                var destinationPath = System.IO.Path.Combine(
                    artifactRoot,
                    FailureArtifactFileName + System.IO.Path.GetExtension(sourcePath));
                File.Copy(sourcePath, destinationPath, overwrite: true);
                return destinationPath;
            }

            var withheldPath = System.IO.Path.Combine(
                artifactRoot,
                FailureArtifactFileName + ".txt");
            File.WriteAllText(
                withheldPath,
                "DTXMANIACX-CRASH-TEST-ARTIFACT 1\n"
                + "Status: report_withheld_for_privacy\n"
                + "---\n"
                + "The original completed report was not copied because no report was available "
                + "or its approved entries did not pass the test's sensitive-value safety check.\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return withheldPath;
        }
    }

    internal static IReadOnlyList<string> EnumerateFailureArtifacts(string artifactRoot)
    {
        if (!Directory.Exists(artifactRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(
                artifactRoot,
                FailureArtifactFileName + ".*",
                SearchOption.TopDirectoryOnly)
            .Where(path => System.IO.Path.GetExtension(path) is ".zip" or ".txt")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    internal static void DeleteFailureArtifacts(string artifactRoot)
    {
        foreach (var path in EnumerateFailureArtifacts(artifactRoot))
        {
            File.Delete(path);
        }

        if (Directory.Exists(artifactRoot)
            && !Directory.EnumerateFileSystemEntries(artifactRoot).Any())
        {
            Directory.Delete(artifactRoot);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Environment.SetEnvironmentVariable(AppDataRootEnvironmentVariable, _previousAppDataRoot);

        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    internal void DisposeBestEffort()
    {
        try
        {
            Dispose();
        }
        catch
        {
            // Do not mask the assertion that caused failure-artifact preservation.
        }
    }

    private string? FindNewestCompletedReportPath()
    {
        var reportRoot = System.IO.Path.Combine(Path, "CrashReports");
        if (!Directory.Exists(reportRoot))
        {
            return null;
        }

        return Directory.EnumerateFiles(reportRoot, "crash-*", SearchOption.TopDirectoryOnly)
            .Where(path => System.IO.Path.GetExtension(path) is ".zip" or ".txt")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool IsSafeToCopy(string sourcePath)
    {
        var allText = CrashReportTestReader.ReadAllText(sourcePath);
        return _sensitiveValues.All(value =>
            !allText.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class CrashReportTestReader
{
    private static readonly string[] ApprovedZipEntries =
    [
        "report.json",
        "exception.txt",
        "logs.ndjson",
        "breadcrumbs.json",
        "README.txt"
    ];

    internal static string ReadAllText(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllText(path);
        }

        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var builder = new StringBuilder();
        foreach (var entryName in ApprovedZipEntries)
        {
            var entry = archive.GetEntry(entryName);
            if (entry is null)
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            builder.AppendLine(reader.ReadToEnd());
        }

        return builder.ToString();
    }

    internal static JsonDocument ReadZipReportDocument(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(
            archive.GetEntry("report.json")!.Open(),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return JsonDocument.Parse(reader.ReadToEnd());
    }

    internal static CrashReportSummary ReadSummary(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var report = ReadZipReportDocument(path);
            var root = report.RootElement;
            return new CrashReportSummary(
                root.GetProperty("reportId").GetString()!,
                root.GetProperty("capturedAtUtc").GetDateTimeOffset(),
                root.GetProperty("buildId").GetString()!,
                root.GetProperty("operatingSystem").GetString()!,
                root.GetProperty("processArchitecture").GetString()!,
                root.GetProperty("stageOrMilestone").GetString()!,
                root.GetProperty("exceptionType").GetString()!,
                CrashReportFormat.ZipBundle,
                Path.GetFileName(path));
        }

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        Assert.Equal("DTXMANIACX-CRASH-REPORT 1", reader.ReadLine());
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var lineCount = 0; lineCount < 8; lineCount++)
        {
            var line = reader.ReadLine();
            Assert.NotNull(line);
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                break;
            }

            var separatorIndex = line!.IndexOf(": ", StringComparison.Ordinal);
            Assert.True(separatorIndex > 0);
            fields[line[..separatorIndex]] = line[(separatorIndex + 2)..];
        }

        return new CrashReportSummary(
            fields["ReportId"],
            DateTimeOffset.Parse(fields["CapturedAtUtc"], CultureInfo.InvariantCulture),
            fields["BuildId"],
            fields["OperatingSystem"],
            fields["ProcessArchitecture"],
            fields["StageOrMilestone"],
            fields["ExceptionType"],
            CrashReportFormat.EmergencyText,
            Path.GetFileName(path));
    }
}
