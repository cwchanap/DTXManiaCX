#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Extensions.Logging;

namespace DTXMania.Test.CrashReporting;

/// <summary>
/// Uses the existing AppPaths collection because these scenarios temporarily replace the
/// process-wide app-data override.
/// </summary>
[Collection("AppPaths")]
[Trait("Category", "Integration")]
public sealed class CrashReportIntegrationTests
{
    [Fact]
    public void Run_WhenFactoryThrowsBeforeGameConstruction_ShouldWriteOneReport()
    {
        RunWithTemporaryAppData(appData =>
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(TextWriter.Null);
            var songPath = Path.Combine(appData.Path, "Some Song.dtx");
            runtime.GameDiagnostics.SensitiveData.RegisterPath(songPath);

            var exitCode = GameEntryPoint.Run(
                () => throw new InvalidOperationException("song=" + songPath),
                runtime,
                TextWriter.Null);

            Assert.Equal(1, exitCode);
            var reportPath = Assert.Single(EnumerateCompletedReportPaths());
            var allText = File.ReadAllText(reportPath);

            Assert.Contains("[PATH]", allText, StringComparison.Ordinal);
            Assert.DoesNotContain(songPath, allText, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFiles(AppPaths.GetCrashReportsRoot(), "*.tmp"));
        });
    }

    [Fact]
    public void CaptureFatal_ShouldPreserveTheExceptionMessageAndType()
    {
        RunWithTemporaryAppData(_ =>
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(TextWriter.Null);

            runtime.CaptureFatal(new InvalidOperationException(
                "chart channel 0xZZ is not supported",
                new ArgumentOutOfRangeException("laneIndex")));

            var allText = File.ReadAllText(Assert.Single(EnumerateCompletedReportPaths()));

            Assert.Contains("Message: chart channel 0xZZ is not supported", allText, StringComparison.Ordinal);
            Assert.Contains(typeof(InvalidOperationException).FullName!, allText, StringComparison.Ordinal);
            Assert.Contains(typeof(ArgumentOutOfRangeException).FullName!, allText, StringComparison.Ordinal);
            Assert.Contains("laneIndex", allText, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Structured diagnostics are allowlisted as they enter the in-memory buffers, so values the
    /// game never explicitly approved cannot reach a report even when a subsystem publishes them.
    /// </summary>
    [Fact]
    public void CaptureFatal_ShouldDropStructuredValuesThatAreNotAllowlisted()
    {
        RunWithTemporaryAppData(appData =>
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(TextWriter.Null);
            const string apiKey = "api-key-should-not-persist";
            const string midiStableId = "MIDI-STABLE-ID-DO-NOT-PERSIST";
            const string renderedLog = "RENDERED-LOG-STRING-DO-NOT-PERSIST";
            var skinPath = Path.Combine(appData.Path, "Skins", "PrivateSkin");

            runtime.GameDiagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
                CrashContextKind.Graphics,
                CrashContextStatus.Unavailable,
                new Dictionary<string, object?>
                {
                    ["GameApiKey"] = apiKey,
                    ["SkinPath"] = skinPath,
                    ["MidiStableId"] = midiStableId
                },
                FailureCode: "graphics_context_collection_failed"));
            runtime.GameDiagnostics.Breadcrumbs.Record(
                CrashBreadcrumbEvents.MidiDeviceCountChanged,
                new Dictionary<string, object?>
                {
                    ["Status"] = midiStableId,
                    ["MidiStableId"] = midiStableId
                });

            // An interpolated (non-structured) log message cannot be classified, so it is dropped.
            runtime.GameDiagnostics.LoggerFactory
                .CreateLogger("integration")
                .LogInformation($"Rendered crash log: {renderedLog}");

            runtime.CaptureFatal(new InvalidOperationException("capture"));

            var allText = File.ReadAllText(Assert.Single(EnumerateCompletedReportPaths()));

            Assert.Contains("Graphics [Unavailable] [REDACTED]", allText, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, allText, StringComparison.Ordinal);
            Assert.DoesNotContain("GameApiKey", allText, StringComparison.Ordinal);
            Assert.DoesNotContain(skinPath, allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(midiStableId, allText, StringComparison.Ordinal);
            Assert.DoesNotContain(renderedLog, allText, StringComparison.Ordinal);
            Assert.Contains("[UNCLASSIFIED MESSAGE OMITTED]", allText, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(AppPaths.GetCrashReportsRoot(), "*.tmp"));
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
                    errorWriter,
                    storeFactory: () => throw new UnauthorizedAccessException("denied"));
                var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("integration");
                var runCalls = 0;

                logger.LogInformation("console fallback log survives");
                var exitCode = GameEntryPoint.Run(
                    () => new FakeGameApplication(run: () => runCalls++, dispose: () => { }),
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
            using var runtime = CrashReportRuntime.CreateBestEffort(errorWriter);
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

            var header = CrashReportTestReader.ReadHeader(Assert.Single(EnumerateCompletedReportPaths()));

            Assert.Equal(typeof(InvalidOperationException).FullName, header["ExceptionType"]);
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
            var reportIds = new List<string>();

            using (var runtime = CreateEnabledRuntime(reportRoot, new CrashReportTextWriter(), clock))
            {
                for (var index = 0; index < 6; index++)
                {
                    runtime.CaptureFatal(new InvalidOperationException("capture " + index));
                    reportIds.Add(CrashReportTestReader
                        .ReadHeader(EnumerateCompletedReportPaths()[^1])["ReportId"]);
                    clock.Advance(TimeSpan.FromSeconds(1));
                }
            }

            var remaining = EnumerateCompletedReportPaths()
                .Select(Path.GetFileNameWithoutExtension)
                .ToArray();

            Assert.Equal(5, remaining.Length);
            Assert.DoesNotContain(reportIds[0], remaining);
            Assert.Contains(reportIds[5], remaining);
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
                using var runtime = CrashReportRuntime.CreateBestEffort(TextWriter.Null);
                runtime.CaptureFatal(new InvalidOperationException("failure artifact verification"));

                var preservedPath = appData.PreserveFailureArtifact(verificationRoot);

                Assert.NotNull(preservedPath);
                Assert.True(File.Exists(preservedPath));
                Assert.Single(TemporaryAppDataRoot.EnumerateFailureArtifacts(verificationRoot));
                Assert.DoesNotContain(
                    appData.Path,
                    File.ReadAllText(preservedPath!),
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
            .Where(path => Path.GetExtension(path) is CrashReportStore.ReportExtension)
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

        public void Run() => _run();

        public void Dispose() => _dispose();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        internal ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
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
    /// copied only when the test's registered sensitive values are absent from it; otherwise a
    /// fixed, content-free diagnostic is retained instead.
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
            var destinationPath = System.IO.Path.Combine(
                artifactRoot,
                FailureArtifactFileName + CrashReportStore.ReportExtension);

            if (sourcePath is not null && IsSafeToCopy(sourcePath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                return destinationPath;
            }

            File.WriteAllText(
                destinationPath,
                "DTXMANIACX-CRASH-TEST-ARTIFACT 1\n"
                + "Status: report_withheld_for_privacy\n"
                + "---\n"
                + "The original completed report was not copied because no report was available "
                + "or it did not pass the test's sensitive-value safety check.\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return destinationPath;
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
            .Where(path => System.IO.Path.GetExtension(path) is CrashReportStore.ReportExtension)
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
            .Where(path => System.IO.Path.GetExtension(path) is CrashReportStore.ReportExtension)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool IsSafeToCopy(string sourcePath)
    {
        var allText = File.ReadAllText(sourcePath);
        return _sensitiveValues.All(value =>
            !allText.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class CrashReportTestReader
{
    /// <summary>
    /// Reads the leading <c>Key: value</c> header block of a crash report, stopping at the
    /// blank line that precedes the first section.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ReadHeader(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        Assert.Equal(CrashReportTextWriter.Header, reader.ReadLine());

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line && line.Length > 0)
        {
            var separatorIndex = line.IndexOf(": ", StringComparison.Ordinal);
            Assert.True(separatorIndex > 0, "Malformed crash report header line: " + line);
            fields[line[..separatorIndex]] = line[(separatorIndex + 2)..];
        }

        return fields;
    }
}
