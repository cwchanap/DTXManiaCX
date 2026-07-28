using System.Collections.Generic;
using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class StartupTimingTraceTests
{
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

        public long GetTimestamp() => _milliseconds.Dequeue();
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
