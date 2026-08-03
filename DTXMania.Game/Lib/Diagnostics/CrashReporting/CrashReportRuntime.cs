#nullable enable

using System;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Threading;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

public sealed class CrashReportRuntime : ICrashRuntimeLifetime
{
    private const int LogBufferCapacity = 500;
    private const int BreadcrumbBufferCapacity = 100;
    private const string BootstrapFailureCode = "bootstrap_failure";
    private const string CaptureFailureCode = "crash_reporting_capture_failed";

    private readonly ILoggerFactory _loggerFactory;
    private readonly CrashLogBufferProvider? _crashLogBufferProvider;
    private readonly CrashBreadcrumbBuffer? _crashBreadcrumbBuffer;
    private readonly CrashContextSnapshotStore? _crashContextSnapshotStore;
    private readonly CrashReportStore? _crashReportStore;
    private readonly TextWriter _errorWriter;
    private readonly IGameCrashDiagnostics _gameDiagnostics;
    private int _disposed;

    internal CrashReportRuntime(
        ILoggerFactory loggerFactory,
        CrashLogBufferProvider? crashLogBufferProvider,
        CrashBreadcrumbBuffer? crashBreadcrumbBuffer,
        CrashContextSnapshotStore? crashContextSnapshotStore,
        CrashReportStore? crashReportStore,
        TextWriter errorWriter)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _crashLogBufferProvider = crashLogBufferProvider;
        _crashBreadcrumbBuffer = crashBreadcrumbBuffer;
        _crashContextSnapshotStore = crashContextSnapshotStore;
        _crashReportStore = crashReportStore;
        _errorWriter = errorWriter ?? throw new ArgumentNullException(nameof(errorWriter));
        _gameDiagnostics = new GameCrashDiagnostics(
            _loggerFactory,
            _crashBreadcrumbBuffer,
            _crashContextSnapshotStore);
        IsCaptureEnabled = _crashLogBufferProvider is not null
            && _crashBreadcrumbBuffer is not null
            && _crashContextSnapshotStore is not null
            && _crashReportStore is not null;
    }

    public bool IsCaptureEnabled { get; }

    public IGameCrashDiagnostics GameDiagnostics => _gameDiagnostics;

    internal CrashLogBufferProvider? CrashLogBufferProvider => _crashLogBufferProvider;

    internal static CrashReportRuntime CreateBestEffort(
        StartupTimingTrace startupTrace,
        TextWriter? errorWriter = null)
    {
        var writer = errorWriter ?? Console.Error;
        return CreateBestEffort(
            startupTrace,
            writer,
            () => CreateDefaultStore(writer));
    }

    internal static CrashReportRuntime CreateBestEffort(
        StartupTimingTrace startupTrace,
        TextWriter? errorWriter,
        Func<CrashReportStore> storeFactory)
    {
        ArgumentNullException.ThrowIfNull(startupTrace);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var writer = errorWriter ?? Console.Error;
        ILoggerFactory? loggerFactory = null;
        var processStartedUtc = DateTimeOffset.UtcNow;

        try
        {
            var logBufferProvider = new CrashLogBufferProvider(
                CrashLogFieldPolicy.Default,
                TimeProvider.System,
                LogBufferCapacity);
            var breadcrumbBuffer = new CrashBreadcrumbBuffer(
                TimeProvider.System,
                BreadcrumbBufferCapacity);
            var contextSnapshotStore = new CrashContextSnapshotStore();
            var reportStore = storeFactory();
            loggerFactory = CreateLoggerFactory(logBufferProvider);

            var runtime = new CrashReportRuntime(
                loggerFactory,
                logBufferProvider,
                breadcrumbBuffer,
                contextSnapshotStore,
                reportStore,
                writer);
            CrashContextPublisher.PublishProcessAndApplication(
                runtime.GameDiagnostics,
                processStartedUtc);
            runtime.GameDiagnostics.Breadcrumbs.Record("process_started");
            return runtime;
        }
        catch (Exception exception) when (IsExpectedBootstrapException(exception))
        {
            DisposePartialFactory(loggerFactory);
            WriteSafeError(writer, "crash_reporting_disabled code=" + BootstrapFailureCode);

            var runtime = new CrashReportRuntime(
                CreateConsoleLoggerFactory(),
                crashLogBufferProvider: null,
                crashBreadcrumbBuffer: null,
                crashContextSnapshotStore: null,
                crashReportStore: null,
                writer);
            CrashContextPublisher.PublishProcessAndApplication(
                runtime.GameDiagnostics,
                processStartedUtc);
            return runtime;
        }
    }

    public void CaptureFatal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!IsCaptureEnabled)
        {
            return;
        }

        try
        {
            var captureData = new CrashCaptureData(
                exception,
                _crashLogBufferProvider!.Snapshot(),
                _crashBreadcrumbBuffer!.Snapshot(),
                _crashContextSnapshotStore!.Snapshot(),
                _crashContextSnapshotStore.SensitivePathSnapshot());
            var result = _crashReportStore!.Capture(captureData);

            if (result.Report is null)
            {
                WriteSafeError(_errorWriter, CaptureFailureCode);
            }
        }
        catch (Exception captureException) when (IsExpectedCaptureException(captureException))
        {
            WriteSafeError(_errorWriter, CaptureFailureCode);
        }
    }

    public void RecordSecondaryFailure(string failureCode, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        WriteSafeError(
            _errorWriter,
            "crash_reporting_secondary_failure code=" + NormalizeSecondaryFailureCode(failureCode));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _loggerFactory.Dispose();
    }

    private static CrashReportStore CreateDefaultStore(TextWriter errorWriter)
    {
        return new CrashReportStore(
            AppPaths.GetCrashReportsRoot(),
            new CrashReportArchiveWriter(),
            TimeProvider.System,
            errorWriter);
    }

    private static ILoggerFactory CreateLoggerFactory(CrashLogBufferProvider crashLogBufferProvider)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddProvider(crashLogBufferProvider);
        });
    }

    private static ILoggerFactory CreateConsoleLoggerFactory()
    {
        return LoggerFactory.Create(builder => builder.AddConsole());
    }

    private static bool IsExpectedBootstrapException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or PathTooLongException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException;
    }

    private static bool IsExpectedCaptureException(Exception exception)
    {
        return IsExpectedBootstrapException(exception)
            || exception is InvalidDataException or JsonException;
    }

    private static void DisposePartialFactory(ILoggerFactory? loggerFactory)
    {
        if (loggerFactory is null)
        {
            return;
        }

        try
        {
            loggerFactory.Dispose();
        }
        catch (Exception exception) when (IsExpectedBootstrapException(exception))
        {
        }
    }

    private static void WriteSafeError(TextWriter writer, string message)
    {
        try
        {
            writer.WriteLine(message);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private static string NormalizeSecondaryFailureCode(string? failureCode)
    {
        return failureCode switch
        {
            "crash_capture_failed" => "crash_capture_failed",
            "game_dispose_failed" => "game_dispose_failed",
            "runtime_dispose_failed" => "runtime_dispose_failed",
            _ => "secondary_failure"
        };
    }

    private sealed class GameCrashDiagnostics : IGameCrashDiagnostics
    {
        internal GameCrashDiagnostics(
            ILoggerFactory loggerFactory,
            CrashBreadcrumbBuffer? breadcrumbs,
            CrashContextSnapshotStore? contexts)
        {
            LoggerFactory = loggerFactory;
            Breadcrumbs = breadcrumbs is null
                ? EmptyCrashBreadcrumbSink.Instance
                : breadcrumbs;
            Contexts = contexts is null
                ? EmptyCrashContextSink.Instance
                : contexts;
            SensitiveData = contexts is null
                ? EmptyCrashSensitiveDataSink.Instance
                : contexts;
            Inbox = EmptyCrashReportInbox.Instance;
        }

        public ILoggerFactory LoggerFactory { get; }

        public ICrashBreadcrumbSink Breadcrumbs { get; }

        public ICrashContextSink Contexts { get; }

        public ICrashSensitiveDataSink SensitiveData { get; }

        public ICrashReportInbox Inbox { get; }
    }
}
