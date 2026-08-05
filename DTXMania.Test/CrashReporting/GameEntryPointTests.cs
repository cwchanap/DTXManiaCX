#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class GameEntryPointTests
{
    [Fact]
    public void Run_WhenGameThrows_ShouldCaptureBeforeDisposeAndPreserveOriginalFailure()
    {
        var calls = new List<string>();
        var game = new FakeGameApplication(
            run: () => { calls.Add("run"); throw new InvalidOperationException("fatal"); },
            dispose: () => calls.Add("dispose"));
        var runtime = new FakeCrashRuntime(
            capture: _ => calls.Add("capture"),
            dispose: () => calls.Add("runtime_dispose"));

        var exitCode = GameEntryPoint.Run(
            () => game,
            runtime,
            TextWriter.Null);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            ["run", "capture", "dispose", "runtime_dispose"],
            calls);
    }

    [Fact]
    public void Run_WhenGameConstructionThrows_ShouldCaptureAndDisposeRuntime()
    {
        var calls = new List<string>();
        var runtime = new FakeCrashRuntime(
            capture: _ => calls.Add("capture"),
            dispose: () => calls.Add("runtime_dispose"));

        var exitCode = GameEntryPoint.Run(
            () =>
            {
                calls.Add("construct");
                throw new ArgumentException("construction failure");
            },
            runtime,
            TextWriter.Null);

        Assert.Equal(1, exitCode);
        Assert.Equal(["construct", "capture", "runtime_dispose"], calls);
    }

    [Fact]
    public void Run_WhenCaptureReportsAnInternalFailure_ShouldRecordSecondaryFailure()
    {
        var calls = new List<string>();
        var game = new FakeGameApplication(
            run: () => throw new InvalidOperationException("fatal"),
            dispose: () => calls.Add("dispose"));
        var runtime = new FakeCrashRuntime(
            capture: _ => throw new IOException("capture failed"),
            recordSecondaryFailure: code => calls.Add("secondary:" + code),
            dispose: () => calls.Add("runtime_dispose"));

        var exitCode = GameEntryPoint.Run(() => game, runtime, TextWriter.Null);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            ["secondary:crash_capture_failed", "dispose", "runtime_dispose"],
            calls);
    }

    [Fact]
    public void Run_WhenGameDisposalThrows_ShouldReportSecondaryFailureAndCompleteRuntimeDisposal()
    {
        var calls = new List<string>();
        var game = new FakeGameApplication(
            run: () => calls.Add("run"),
            dispose: () =>
            {
                calls.Add("dispose");
                throw new IOException("game disposal failed");
            });
        var runtime = new FakeCrashRuntime(
            recordSecondaryFailure: code => calls.Add("secondary:" + code),
            dispose: () => calls.Add("runtime_dispose"));

        var exitCode = GameEntryPoint.Run(() => game, runtime, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["run", "dispose", "secondary:game_dispose_failed", "runtime_dispose"],
            calls);
    }

    [Fact]
    public void Run_WhenRuntimeDisposalThrows_ShouldReturnOriginalExitCodeAndWriteSafeFailure()
    {
        var game = new FakeGameApplication(run: () => { }, dispose: () => { });
        var runtime = new FakeCrashRuntime(
            dispose: () => throw new IOException("runtime disposal failed"));
        using var errorWriter = new StringWriter();

        var exitCode = GameEntryPoint.Run(() => game, runtime, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Contains("crash_reporting_secondary_failure code=runtime_dispose_failed", errorWriter.ToString());
    }

    [Fact]
    public void Run_WhenGameCompletes_ShouldReturnZeroAndDisposeInOrder()
    {
        var calls = new List<string>();
        var game = new FakeGameApplication(
            run: () => calls.Add("run"),
            dispose: () => calls.Add("dispose"));
        var runtime = new FakeCrashRuntime(dispose: () => calls.Add("runtime_dispose"));

        var exitCode = GameEntryPoint.Run(() => game, runtime, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal(["run", "dispose", "runtime_dispose"], calls);
    }

    [Fact]
    public void Run_WhenGameThrows_ShouldPassTheOriginalExceptionTypeToCaptureFatal()
    {
        Exception? captured = null;
        var game = new FakeGameApplication(
            run: () => throw new InvalidOperationException("fatal"),
            dispose: () => { });
        var runtime = new FakeCrashRuntime(capture: exception => captured = exception);

        var exitCode = GameEntryPoint.Run(() => game, runtime, TextWriter.Null);

        Assert.Equal(1, exitCode);
        Assert.IsType<InvalidOperationException>(captured);
    }

    [Fact]
    public void Run_WhenFatalGameAndDisposalBothFail_ShouldPreserveOriginalAndReportSecondaryFailure()
    {
        Exception? captured = null;
        string? secondaryFailureCode = null;
        var game = new FakeGameApplication(
            run: () => throw new InvalidOperationException("fatal"),
            dispose: () => throw new IOException("disposal failed"));
        var runtime = new FakeCrashRuntime(
            capture: exception => captured = exception,
            recordSecondaryFailure: code => secondaryFailureCode = code);

        var exitCode = GameEntryPoint.Run(() => game, runtime, TextWriter.Null);

        Assert.Equal(1, exitCode);
        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("game_dispose_failed", secondaryFailureCode);
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

    private sealed class FakeCrashRuntime : ICrashRuntimeLifetime
    {
        private readonly Action<Exception>? _capture;
        private readonly Action<string>? _recordSecondaryFailure;
        private readonly Action _dispose;

        internal FakeCrashRuntime(
            Action<Exception>? capture = null,
            Action<string>? recordSecondaryFailure = null,
            Action? dispose = null)
        {
            _capture = capture;
            _recordSecondaryFailure = recordSecondaryFailure;
            _dispose = dispose ?? (() => { });
        }

        public void CaptureFatal(Exception exception)
        {
            _capture?.Invoke(exception);
        }

        public void RecordSecondaryFailure(string failureCode)
        {
            _recordSecondaryFailure?.Invoke(failureCode);
        }

        public void Dispose()
        {
            _dispose();
        }
    }
}
