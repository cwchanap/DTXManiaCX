#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;

namespace DTXMania.Game.Lib.Stage;

internal enum StartupTimingMilestone
{
    ProcessEntry,
    ConfigLoaded,
    LoadContentComplete,
    StartupActivated,
    StartupFirstDraw,
    SummaryAndTitleRequested,
    TitleCompleted
}

internal interface IMonotonicClock
{
    long TimestampFrequency { get; }
    long GetTimestamp();
}

internal interface IUtcMicrosecondClock
{
    long GetUnixMicroseconds();
}

internal sealed class StartupTimingTrace
{
    private readonly IMonotonicClock _clock;
    private readonly IUtcMicrosecondClock _wallClock;
    private readonly long[] _timestamps = new long[Enum.GetValues<StartupTimingMilestone>().Length];
    private readonly bool[] _recorded = new bool[Enum.GetValues<StartupTimingMilestone>().Length];
    private readonly bool _enabled;
    private bool _invalid;
    private bool _emitted;
    private long _entryUnixMicroseconds;
    private long _titleUnixMicroseconds;

    private StartupTimingTrace(
        IMonotonicClock clock,
        IUtcMicrosecondClock wallClock,
        bool enabled,
        StartupCriticalPathTrace? criticalPathTrace = null)
    {
        _clock = clock;
        _wallClock = wallClock;
        _enabled = enabled;
        CriticalPathTrace = criticalPathTrace;
    }

    internal StartupCriticalPathTrace? CriticalPathTrace { get; }

    internal static StartupTimingTrace Disabled { get; } = new(
        new StopwatchMonotonicClock(),
        new UtcMicrosecondClock(),
        enabled: false);

    internal static StartupTimingTrace StartProcess()
    {
        var enableCriticalPath =
            Environment.GetEnvironmentVariable("HPA192_CRITICAL_PATH") == "1";
        var exitAfterCriticalPath =
            enableCriticalPath &&
            Environment.GetEnvironmentVariable("HPA192_EXIT_AFTER_CRITICAL_PATH") == "1";
        return Start(
            new StopwatchMonotonicClock(),
            new UtcMicrosecondClock(),
            enableCriticalPath,
            exitAfterCriticalPath);
    }

    internal static StartupTimingTrace Start(
        IMonotonicClock clock,
        IUtcMicrosecondClock wallClock,
        bool enableCriticalPath = false,
        bool exitAfterCriticalPath = false)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(wallClock);

        var entryTimestamp = clock.GetTimestamp();
        var entryUnixMicroseconds = wallClock.GetUnixMicroseconds();
        var criticalPathTrace = enableCriticalPath
            ? StartupCriticalPathTrace.Start(
                clock,
                wallClock,
                entryTimestamp,
                entryUnixMicroseconds,
                exitAfterCriticalPath)
            : null;
        var trace = new StartupTimingTrace(
            clock,
            wallClock,
            enabled: true,
            criticalPathTrace);
        trace._timestamps[(int)StartupTimingMilestone.ProcessEntry] = entryTimestamp;
        trace._recorded[(int)StartupTimingMilestone.ProcessEntry] = true;
        trace._entryUnixMicroseconds = entryUnixMicroseconds;
        return trace;
    }

    internal void MarkConfigLoaded() => Mark(StartupTimingMilestone.ConfigLoaded);
    internal void MarkLoadContentComplete() => Mark(StartupTimingMilestone.LoadContentComplete);
    internal void MarkStartupActivated() => Mark(StartupTimingMilestone.StartupActivated);
    internal void MarkStartupFirstDraw() => Mark(StartupTimingMilestone.StartupFirstDraw);
    internal void MarkSummaryAndTitleRequested() => Mark(StartupTimingMilestone.SummaryAndTitleRequested);
    internal void MarkTitleCompleted() => Mark(StartupTimingMilestone.TitleCompleted);

    internal void Mark(StartupTimingMilestone milestone)
    {
        if (!_enabled || _invalid || _emitted)
            return;

        var index = (int)milestone;
        if (_recorded[index])
            return;

        if (index == 0 || !_recorded[index - 1])
        {
            _invalid = true;
            return;
        }

        _timestamps[index] = _clock.GetTimestamp();
        _recorded[index] = true;
        if (milestone == StartupTimingMilestone.TitleCompleted)
            _titleUnixMicroseconds = _wallClock.GetUnixMicroseconds();
    }

    internal string? TryFormatCompletedLine()
    {
        if (!_enabled || _invalid || _emitted || !_recorded[(int)StartupTimingMilestone.TitleCompleted])
            return null;

        var frequency = _clock.TimestampFrequency;
        if (frequency <= 0)
        {
            _emitted = true;
            return null;
        }

        long IntervalMilliseconds(StartupTimingMilestone start, StartupTimingMilestone end)
        {
            try
            {
                return checked((_timestamps[(int)end] - _timestamps[(int)start]) * 1_000 / frequency);
            }
            catch (OverflowException)
            {
                return -1;
            }
        }

        _emitted = true;
        return string.Format(
            CultureInfo.InvariantCulture,
            "HPA192_TIMING entry_to_config_ms={0} config_to_load_content_ms={1} " +
            "load_content_to_startup_ms={2} startup_to_first_draw_ms={3} " +
            "startup_to_summary_ms={4} summary_to_title_ms={5} entry_to_title_ms={6} " +
            "entry_unix_us={7} title_unix_us={8}",
            IntervalMilliseconds(StartupTimingMilestone.ProcessEntry, StartupTimingMilestone.ConfigLoaded),
            IntervalMilliseconds(StartupTimingMilestone.ConfigLoaded, StartupTimingMilestone.LoadContentComplete),
            IntervalMilliseconds(StartupTimingMilestone.LoadContentComplete, StartupTimingMilestone.StartupActivated),
            IntervalMilliseconds(StartupTimingMilestone.StartupActivated, StartupTimingMilestone.StartupFirstDraw),
            IntervalMilliseconds(StartupTimingMilestone.StartupActivated, StartupTimingMilestone.SummaryAndTitleRequested),
            IntervalMilliseconds(StartupTimingMilestone.SummaryAndTitleRequested, StartupTimingMilestone.TitleCompleted),
            IntervalMilliseconds(StartupTimingMilestone.ProcessEntry, StartupTimingMilestone.TitleCompleted),
            _entryUnixMicroseconds,
            _titleUnixMicroseconds);
    }

    private sealed class StopwatchMonotonicClock : IMonotonicClock
    {
        public long TimestampFrequency => Stopwatch.Frequency;
        public long GetTimestamp() => Stopwatch.GetTimestamp();
    }

    private sealed class UtcMicrosecondClock : IUtcMicrosecondClock
    {
        public long GetUnixMicroseconds() =>
            (DateTimeOffset.UtcNow.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) / 10;
    }
}
