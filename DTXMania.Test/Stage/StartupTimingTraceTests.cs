using System.Collections.Generic;
using System.IO;
using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Stage;

[CollectionDefinition("StartupCriticalPathEnvironment", DisableParallelization = true)]
public sealed class StartupCriticalPathEnvironmentCollection;

[Trait("Category", "Unit")]
[Collection("StartupCriticalPathEnvironment")]
public class StartupTimingTraceTests
{
    private const string CriticalPathVariable = "HPA192_CRITICAL_PATH";
    private const string ExitAfterCriticalPathVariable = "HPA192_EXIT_AFTER_CRITICAL_PATH";

    [Fact]
    public void StartProcess_WhenCriticalPathFlagMissing_ShouldLeaveCompanionDisabled()
    {
        var previousCriticalPath = Environment.GetEnvironmentVariable(CriticalPathVariable);
        var previousExit = Environment.GetEnvironmentVariable(ExitAfterCriticalPathVariable);

        try
        {
            Environment.SetEnvironmentVariable(CriticalPathVariable, null);
            Environment.SetEnvironmentVariable(ExitAfterCriticalPathVariable, "1");

            var trace = StartupTimingTrace.StartProcess();

            Assert.Null(trace.CriticalPathTrace);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CriticalPathVariable, previousCriticalPath);
            Environment.SetEnvironmentVariable(ExitAfterCriticalPathVariable, previousExit);
        }
    }

    [Fact]
    public void StartProcess_WhenCriticalPathFlagIsOne_ShouldShareEntryClocksWithCompanion()
    {
        var previousCriticalPath = Environment.GetEnvironmentVariable(CriticalPathVariable);
        var previousExit = Environment.GetEnvironmentVariable(ExitAfterCriticalPathVariable);

        try
        {
            Environment.SetEnvironmentVariable(CriticalPathVariable, "1");
            Environment.SetEnvironmentVariable(ExitAfterCriticalPathVariable, "1");
            var processTrace = StartupTimingTrace.StartProcess();
            var clock = new FakeMonotonicClock(123);
            var wallClock = new FakeUtcMicrosecondClock(456);

            var deterministicTrace = StartupTimingTrace.Start(
                clock,
                wallClock,
                enableCriticalPath: true,
                exitAfterCriticalPath: true);

            Assert.NotNull(processTrace.CriticalPathTrace);
            Assert.True(processTrace.CriticalPathTrace.ExitAfterPublication);
            Assert.NotNull(deterministicTrace.CriticalPathTrace);
            Assert.Equal(1, clock.CallCount);
            Assert.Equal(1, wallClock.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CriticalPathVariable, previousCriticalPath);
            Environment.SetEnvironmentVariable(ExitAfterCriticalPathVariable, previousExit);
        }
    }

    [Fact]
    public void Format_WhenCriticalPathEnabled_ShouldKeepCompatibilityLineByteForByte()
    {
        var trace = StartupTimingTrace.Start(
            new FakeMonotonicClock(0, 100, 400, 500, 520, 900, 1900),
            new FakeUtcMicrosecondClock(10_000_000, 11_900_000),
            enableCriticalPath: true);

        trace.MarkConfigLoaded();
        trace.MarkLoadContentComplete();
        trace.MarkStartupActivated();
        trace.MarkStartupFirstDraw();
        trace.MarkSummaryAndTitleRequested();
        trace.MarkTitleCompleted();

        Assert.Equal(
            "HPA192_TIMING entry_to_config_ms=100 " +
            "config_to_load_content_ms=300 " +
            "load_content_to_startup_ms=100 " +
            "startup_to_first_draw_ms=20 " +
            "startup_to_summary_ms=400 " +
            "summary_to_title_ms=1000 " +
            "entry_to_title_ms=1900 " +
            "entry_unix_us=10000000 " +
            "title_unix_us=11900000",
            trace.TryFormatCompletedLine());
    }

    [Fact]
    public void Disabled_WhenExitFlagIsOne_ShouldPublishNeitherCompanionPrefix()
    {
        var previousCriticalPath = Environment.GetEnvironmentVariable(CriticalPathVariable);
        var previousExit = Environment.GetEnvironmentVariable(ExitAfterCriticalPathVariable);

        try
        {
            Environment.SetEnvironmentVariable(CriticalPathVariable, null);
            Environment.SetEnvironmentVariable(ExitAfterCriticalPathVariable, "1");
            using var writer = new StringWriter();

            var trace = StartupTimingTrace.Disabled;
            var published = trace.CriticalPathTrace?.TryPublishTerminal(writer) ?? false;

            Assert.False(published);
            Assert.DoesNotContain("HPA192_CRITICAL_PATH", writer.ToString());
            Assert.DoesNotContain("HPA192_CRITICAL_PATH_FAILURE", writer.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CriticalPathVariable, previousCriticalPath);
            Environment.SetEnvironmentVariable(ExitAfterCriticalPathVariable, previousExit);
        }
    }

    [Fact]
    public void Format_WhenAllMilestonesRecorded_ShouldEmitExactIntervalsOnce()
    {
        var clock = new FakeMonotonicClock(0, 100, 400, 500, 520, 900, 1900);
        var wallClock = new FakeUtcMicrosecondClock(10_000_000, 11_900_000);
        var trace = StartupTimingTrace.Start(clock, wallClock);

        trace.MarkConfigLoaded();
        trace.MarkLoadContentComplete();
        trace.MarkStartupActivated();
        trace.MarkStartupFirstDraw();
        trace.MarkSummaryAndTitleRequested();
        trace.MarkTitleCompleted();

        Assert.Equal(
            "HPA192_TIMING entry_to_config_ms=100 " +
            "config_to_load_content_ms=300 " +
            "load_content_to_startup_ms=100 " +
            "startup_to_first_draw_ms=20 " +
            "startup_to_summary_ms=400 " +
            "summary_to_title_ms=1000 " +
            "entry_to_title_ms=1900 " +
            "entry_unix_us=10000000 " +
            "title_unix_us=11900000",
            trace.TryFormatCompletedLine());
        Assert.Null(trace.TryFormatCompletedLine());
    }

    [Fact]
    public void Markers_WhenRepeatedOrOutOfOrder_ShouldNotPublishInvalidTimeline()
    {
        var duplicateClock = new FakeMonotonicClock(0, 100, 400, 500, 520, 900, 1900);
        var duplicateWallClock = new FakeUtcMicrosecondClock(10_000_000, 11_900_000);
        var duplicateTrace = StartupTimingTrace.Start(duplicateClock, duplicateWallClock);

        duplicateTrace.MarkConfigLoaded();
        duplicateTrace.MarkConfigLoaded();
        duplicateTrace.MarkLoadContentComplete();
        duplicateTrace.MarkStartupActivated();
        duplicateTrace.MarkStartupFirstDraw();
        duplicateTrace.MarkSummaryAndTitleRequested();
        duplicateTrace.MarkTitleCompleted();

        Assert.NotNull(duplicateTrace.TryFormatCompletedLine());

        var outOfOrderTrace = StartupTimingTrace.Start(
            new FakeMonotonicClock(0, 100, 400),
            new FakeUtcMicrosecondClock(10_000_000, 11_900_000));
        outOfOrderTrace.MarkLoadContentComplete();
        outOfOrderTrace.MarkConfigLoaded();

        Assert.Null(outOfOrderTrace.TryFormatCompletedLine());
    }

    [Fact]
    public void Start_WhenProcessEntryCaptured_ShouldUseSingleMonotonicOriginAndAdjacentUtcAnchors()
    {
        var clock = new FakeMonotonicClock(0, 100, 400, 500, 520, 900, 1900);
        var wallClock = new FakeUtcMicrosecondClock(10_000_000, 11_900_000);
        var trace = StartupTimingTrace.Start(clock, wallClock);

        trace.Mark(StartupTimingMilestone.ConfigLoaded);
        trace.Mark(StartupTimingMilestone.LoadContentComplete);
        trace.Mark(StartupTimingMilestone.StartupActivated);
        trace.Mark(StartupTimingMilestone.StartupFirstDraw);
        trace.Mark(StartupTimingMilestone.SummaryAndTitleRequested);
        trace.Mark(StartupTimingMilestone.TitleCompleted);

        var line = trace.TryFormatCompletedLine();

        Assert.Contains("entry_to_title_ms=1900", line);
        Assert.Contains("entry_unix_us=10000000", line);
        Assert.Contains("title_unix_us=11900000", line);
        Assert.Equal(2, wallClock.CallCount);
    }

    [Fact]
    public void Format_WhenTimelineIsIncomplete_ShouldNotEmit()
    {
        var trace = StartupTimingTrace.Start(
            new FakeMonotonicClock(0, 100),
            new FakeUtcMicrosecondClock(10_000_000, 11_900_000));

        trace.MarkConfigLoaded();

        Assert.Null(trace.TryFormatCompletedLine());
    }

    [Fact]
    public void LifecycleReports_WhenBaseGameReceivesStartupEvents_ShouldForwardToTimingTrace()
    {
        var trace = StartupTimingTrace.Start(
            new FakeMonotonicClock(0, 100, 400, 500, 520, 900, 1900),
            new FakeUtcMicrosecondClock(10_000_000, 11_900_000));
        var game = ReflectionHelpers.CreateUninitialized<BaseGame>();
        ReflectionHelpers.SetPrivateField(game, "_startupTimingTrace", trace);
        trace.MarkConfigLoaded();
        trace.MarkLoadContentComplete();

        game.ReportStartupActivated();
        game.ReportStartupFrameRendered();
        game.ReportStartupSummaryAndTitleRequested();
        trace.MarkTitleCompleted();

        Assert.NotNull(trace.TryFormatCompletedLine());
    }

    private sealed class FakeMonotonicClock : IMonotonicClock
    {
        private readonly Queue<long> _milliseconds;

        public FakeMonotonicClock(params long[] milliseconds)
        {
            _milliseconds = new Queue<long>(milliseconds);
        }

        public long TimestampFrequency => 1_000;

        public int CallCount { get; private set; }

        public long GetTimestamp()
        {
            CallCount++;
            return _milliseconds.Dequeue();
        }
    }

    private sealed class FakeUtcMicrosecondClock : IUtcMicrosecondClock
    {
        private readonly Queue<long> _microseconds;

        public FakeUtcMicrosecondClock(params long[] microseconds)
        {
            _microseconds = new Queue<long>(microseconds);
        }

        public int CallCount { get; private set; }

        public long GetUnixMicroseconds()
        {
            CallCount++;
            return _microseconds.Dequeue();
        }
    }
}
