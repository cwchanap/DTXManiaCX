#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using DTXMania.Game.Lib.Song;

namespace DTXMania.Game.Lib.Stage;

internal enum StartupCriticalPathMilestone
{
    LoadContentComplete,
    StartupConstructBegin,
    StartupConstructEnd,
    StartupActivateBegin,
    StartupActivation,
    StartupActivateEnd,
    LoadContentReturn,
    BaseInitializeReturn,
    InputManagerBegin,
    InputManagerEnd,
    SavedBindingsBegin,
    SavedBindingsEnd,
    GraphicsInitializeBegin,
    GraphicsInitializeEnd,
    RenderTargetBegin,
    RenderTargetEnd,
    InitializeComplete,
    StartupFirstUpdateBegin,
    StartupFirstUpdateEnd,
    StartupFirstDrawBegin,
    StartupFirstDrawEnd,
    DatabaseInvoke,
    DatabaseTaskReturn,
    DatabaseTerminal,
    DatabaseObserved,
    EnumerationInvoke,
    EnumerationTaskReturn,
    EnumerationTerminal,
    EnumerationObserved,
    SummaryRequest,
    TitleConstructBegin,
    TitleConstructEnd,
    TransitionStart,
    TransitionComplete,
    StartupDeactivateBegin,
    StartupDeactivateEnd,
    TitleActivateBegin,
    TitleActivateEnd,
    TitleFirstUpdateBegin,
    TitleFirstUpdateEnd,
    TitleStageDrawBegin,
    TitleStageDrawEnd,
    TitleBackbufferBlitBegin,
    TitleBackbufferBlitEnd
}

internal enum StartupCriticalPathAggregate
{
    DatabaseServiceSetup,
    DatabaseCorruptionProbe,
    DatabaseInvalidRecovery,
    DatabaseEnsureCreated,
    DatabaseEncodingPragmas,
    DatabaseVersionWork,
    DatabaseSchemaEnsures,
    TitleGpuSetup,
    TitleBackground,
    TitleMenu,
    TitleFont,
    TitleCursorSound,
    TitleDecideSound,
    TitleGameStartSound,
    TitleGameStartFallback
}

internal sealed class StartupCriticalPathTrace : IStartupSongLoadTimingObserver
{
    private const long MaximumUtcMicroseconds = 4_102_444_800_000_000;
    private const long MaximumMilliseconds = 300_000;
    private const long MaximumCounter = 100_000;
    private static readonly string[] MilestoneNames =
        Enum.GetNames<StartupCriticalPathMilestone>();
    private static readonly string[] AggregateNames =
        Enum.GetNames<StartupCriticalPathAggregate>();

    internal static readonly string[] SuccessFieldNames =
    {
        "outcome",
        "error",
        "entry_unix_us",
        "title_backbuffer_unix_us",
        "entry_to_title_backbuffer_ms",
        "load_content_complete_from_entry_ms",
        "startup_construct_begin_from_entry_ms",
        "startup_construct_end_from_entry_ms",
        "startup_activate_begin_from_entry_ms",
        "startup_activation_from_entry_ms",
        "startup_activate_end_from_entry_ms",
        "load_content_return_from_entry_ms",
        "base_initialize_return_from_entry_ms",
        "input_manager_begin_from_entry_ms",
        "input_manager_end_from_entry_ms",
        "saved_bindings_begin_from_entry_ms",
        "saved_bindings_end_from_entry_ms",
        "graphics_initialize_begin_from_entry_ms",
        "graphics_initialize_end_from_entry_ms",
        "render_target_begin_from_entry_ms",
        "render_target_end_from_entry_ms",
        "initialize_complete_from_entry_ms",
        "post_load_unattributed_ms",
        "startup_first_update_begin_from_entry_ms",
        "startup_first_update_end_from_entry_ms",
        "startup_first_draw_begin_from_entry_ms",
        "startup_first_draw_end_from_entry_ms",
        "startup_updates_before_first_draw",
        "startup_game_time_before_first_draw_ms",
        "startup_draws_before_transition",
        "db_invoke_from_entry_ms",
        "db_task_return_from_entry_ms",
        "db_terminal_from_entry_ms",
        "db_observed_from_entry_ms",
        "db_task_returned_terminal",
        "enumeration_invoke_from_entry_ms",
        "enumeration_task_return_from_entry_ms",
        "enumeration_terminal_from_entry_ms",
        "enumeration_observed_from_entry_ms",
        "enumeration_task_returned_terminal",
        "enumeration_unattributed_ms",
        "db_service_setup_ms",
        "db_corruption_probe_ms",
        "db_invalid_recovery_count",
        "db_invalid_recovery_ms",
        "db_ensure_created_count",
        "db_ensure_created_ms",
        "db_encoding_pragmas_ms",
        "db_version_work_ms",
        "db_schema_ensures_ms",
        "db_init_unattributed_ms",
        "summary_request_from_entry_ms",
        "title_construct_begin_from_entry_ms",
        "title_construct_end_from_entry_ms",
        "transition_start_from_entry_ms",
        "transition_complete_from_entry_ms",
        "transition_update_count",
        "transition_game_time_ms",
        "startup_deactivate_begin_from_entry_ms",
        "startup_deactivate_end_from_entry_ms",
        "title_activate_begin_from_entry_ms",
        "title_activate_end_from_entry_ms",
        "title_first_update_begin_from_entry_ms",
        "title_first_update_end_from_entry_ms",
        "title_stage_draw_begin_from_entry_ms",
        "title_stage_draw_end_from_entry_ms",
        "title_backbuffer_blit_begin_from_entry_ms",
        "title_backbuffer_blit_end_from_entry_ms",
        "summary_to_title_unattributed_ms",
        "title_gpu_setup_ms",
        "title_background_ms",
        "title_menu_ms",
        "title_font_ms",
        "title_cursor_sound_ms",
        "title_decide_sound_ms",
        "title_game_start_sound_ms",
        "title_game_start_fallback_ran",
        "title_game_start_fallback_ms",
        "title_sound_load_count",
        "title_activation_unattributed_ms",
        "title_backbuffer_published"
    };

    private readonly object _sync = new();
    private readonly IMonotonicClock _clock;
    private readonly IUtcMicrosecondClock _wallClock;
    private readonly long _entryTimestamp;
    private readonly long _entryUnixMicroseconds;
    private readonly long[] _timestamps =
        new long[Enum.GetValues<StartupCriticalPathMilestone>().Length];
    private readonly bool[] _recorded =
        new bool[Enum.GetValues<StartupCriticalPathMilestone>().Length];
    private readonly bool[] _firstObservationBegun =
        new bool[Enum.GetValues<StartupCriticalPathMilestone>().Length];
    private readonly bool[] _firstObservationEnded =
        new bool[Enum.GetValues<StartupCriticalPathMilestone>().Length];
    private readonly long[] _aggregateBeginTimestamps =
        new long[Enum.GetValues<StartupCriticalPathAggregate>().Length];
    private readonly long[] _aggregateTimestampTicks =
        new long[Enum.GetValues<StartupCriticalPathAggregate>().Length];
    private readonly long[] _aggregateCounts =
        new long[Enum.GetValues<StartupCriticalPathAggregate>().Length];
    private readonly bool[] _aggregateActive =
        new bool[Enum.GetValues<StartupCriticalPathAggregate>().Length];

    private TerminalOutcome _terminalOutcome;
    private string? _terminalErrorRaw = "none";
    private string? _lastMilestoneRaw = "unknown";
    private bool _publicationAttempted;
    private int _terminalClosed;
    private long _titleBackbufferUnixMicroseconds;
    private bool _titleBackbufferWallClockPending;
    private long _startupUpdateCount;
    private long _startupGameTimeTicks;
    private long _startupDrawCount;
    private long _transitionUpdateCount;
    private long _transitionGameTimeTicks;
    private long _titleSoundLoadCount;
    private bool _titleGameStartFallbackRan;
    private bool _databaseTaskReturnedRecorded;
    private bool _databaseTaskReturnedTerminal;
    private bool _enumerationTaskReturnedRecorded;
    private bool _enumerationTaskReturnedTerminal;
    private bool _enumerationResultRecorded;
    private long _enumerationDiscoveryTicks;
    private long _enumerationPersistenceTicks;
    private long _enumerationCleanupTicks;
    private long _enumerationHierarchyTicks;
    private bool _titleCompletionLookupRecorded;
    private bool _titleCompletionLookupCacheHit;

    private StartupCriticalPathTrace(
        IMonotonicClock clock,
        IUtcMicrosecondClock wallClock,
        long entryTimestamp,
        long entryUnixMicroseconds,
        bool exitAfterPublication)
    {
        _clock = clock;
        _wallClock = wallClock;
        _entryTimestamp = entryTimestamp;
        _entryUnixMicroseconds = entryUnixMicroseconds;
        ExitAfterPublication = exitAfterPublication;
    }

    internal bool ExitAfterPublication { get; }

    internal static StartupCriticalPathTrace Start(
        IMonotonicClock clock,
        IUtcMicrosecondClock wallClock,
        long entryTimestamp,
        long entryUnixMicroseconds,
        bool exitAfterPublication)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(wallClock);
        return new StartupCriticalPathTrace(
            clock,
            wallClock,
            entryTimestamp,
            entryUnixMicroseconds,
            exitAfterPublication);
    }

    internal void RecordExactlyOnce(StartupCriticalPathMilestone milestone)
    {
        if (!TryCaptureTimestamp(out var timestamp))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;

            var index = (int)milestone;
            if (_recorded[index])
            {
                FailLocked(
                    "duplicate_milestone",
                    MilestoneNames[index],
                    cancellation: false);
                return;
            }

            RecordMilestoneLocked(milestone, timestamp);
        }
    }

    internal void RecordFirstObservationBegin(
        StartupCriticalPathMilestone begin,
        StartupCriticalPathMilestone end)
    {
        if (!TryCaptureTimestamp(out var timestamp))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;

            var beginIndex = (int)begin;
            if (_firstObservationBegun[beginIndex])
                return;

            _firstObservationBegun[beginIndex] = true;
            RecordMilestoneLocked(begin, timestamp);
            if (begin == end)
            {
                _firstObservationEnded[beginIndex] = true;
            }
        }
    }

    internal void RecordFirstObservationEnd(
        StartupCriticalPathMilestone begin,
        StartupCriticalPathMilestone end)
    {
        if (!TryCaptureTimestamp(out var timestamp))
            return;

        var captureWallTimestamp =
            end == StartupCriticalPathMilestone.TitleBackbufferBlitEnd;
        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;

            var beginIndex = (int)begin;
            var endIndex = (int)end;
            if (begin == end)
                return;
            if (_firstObservationEnded[endIndex])
                return;
            if (!_firstObservationBegun[beginIndex])
            {
                FailLocked(
                    "unmatched_first_observation",
                    MilestoneNames[endIndex],
                    cancellation: false);
                return;
            }

            _firstObservationEnded[endIndex] = true;
            RecordMilestoneLocked(end, timestamp);
            if (captureWallTimestamp)
                _titleBackbufferWallClockPending = true;
        }

        if (!captureWallTimestamp)
            return;

        long wallTimestamp;
        try
        {
            wallTimestamp = _wallClock.GetUnixMicroseconds();
        }
        catch
        {
            Fail("wall_clock_failure", MilestoneNames[(int)end]);
            return;
        }

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open ||
                !_titleBackbufferWallClockPending)
            {
                return;
            }

            _titleBackbufferUnixMicroseconds = wallTimestamp;
            _titleBackbufferWallClockPending = false;
        }
    }

    internal void BeginAggregate(StartupCriticalPathAggregate aggregate)
    {
        if (!TryCaptureTimestamp(out var timestamp))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;

            var index = (int)aggregate;
            if (_aggregateActive[index])
            {
                FailLocked(
                    "nested_aggregate",
                    AggregateNames[index],
                    cancellation: false);
                return;
            }
            if (_aggregateCounts[index] >= MaximumAggregateCount(aggregate))
            {
                FailLocked(
                    "aggregate_count_exceeded",
                    AggregateNames[index],
                    cancellation: false);
                return;
            }

            _aggregateActive[index] = true;
            _aggregateBeginTimestamps[index] = timestamp;
            _aggregateCounts[index]++;
            SetLastMilestoneLocked(AggregateNames[index]);
        }
    }

    internal void EndAggregate(StartupCriticalPathAggregate aggregate)
    {
        if (!TryCaptureTimestamp(out var timestamp))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;

            var index = (int)aggregate;
            if (!_aggregateActive[index])
            {
                FailLocked(
                    "unmatched_aggregate",
                    AggregateNames[index],
                    cancellation: false);
                return;
            }

            try
            {
                var elapsed = checked(timestamp - _aggregateBeginTimestamps[index]);
                if (elapsed < 0)
                {
                    FailLocked(
                        "negative_aggregate",
                        AggregateNames[index],
                        cancellation: false);
                    return;
                }

                _aggregateTimestampTicks[index] =
                    checked(_aggregateTimestampTicks[index] + elapsed);
                _aggregateActive[index] = false;
                SetLastMilestoneLocked(AggregateNames[index]);
            }
            catch (OverflowException)
            {
                FailLocked(
                    "aggregate_overflow",
                    AggregateNames[index],
                    cancellation: false);
            }
        }
    }

    internal void IncrementStartupUpdate(double elapsedSeconds)
    {
        if (!TryElapsedTimeTicks(elapsedSeconds, out var ticks))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open ||
                _recorded[(int)StartupCriticalPathMilestone.StartupFirstDrawBegin])
            {
                return;
            }

            IncrementCounterLocked(
                ref _startupUpdateCount,
                ref _startupGameTimeTicks,
                ticks,
                "startup_update");
        }
    }

    internal void IncrementCompletedStartupDraw()
    {
        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open ||
                _recorded[(int)StartupCriticalPathMilestone.SummaryRequest])
            {
                return;
            }

            try
            {
                _startupDrawCount = checked(_startupDrawCount + 1);
            }
            catch (OverflowException)
            {
                FailLocked("counter_overflow", "startup_draw", cancellation: false);
            }
        }
    }

    internal void IncrementTransitionUpdate(double elapsedSeconds)
    {
        if (!TryElapsedTimeTicks(elapsedSeconds, out var ticks))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open ||
                _recorded[(int)StartupCriticalPathMilestone.TransitionComplete])
            {
                return;
            }

            IncrementCounterLocked(
                ref _transitionUpdateCount,
                ref _transitionGameTimeTicks,
                ticks,
                "transition_update");
        }
    }

    internal void IncrementTitleSoundLoad()
    {
        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open ||
                _recorded[(int)StartupCriticalPathMilestone.TitleActivateEnd])
            {
                return;
            }

            try
            {
                _titleSoundLoadCount = checked(_titleSoundLoadCount + 1);
            }
            catch (OverflowException)
            {
                FailLocked("counter_overflow", "title_sound_load", cancellation: false);
            }
        }
    }

    internal void MarkTitleGameStartFallbackRan()
    {
        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open ||
                _recorded[(int)StartupCriticalPathMilestone.TitleActivateEnd])
            {
                return;
            }
            if (_titleGameStartFallbackRan)
            {
                FailLocked(
                    "duplicate_flag",
                    "title_game_start_fallback",
                    cancellation: false);
                return;
            }

            _titleGameStartFallbackRan = true;
        }
    }

    internal void RecordDatabaseTaskReturned(bool wasTerminal)
    {
        if (!TryCaptureTimestamp(out var timestamp))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;
            if (_databaseTaskReturnedRecorded)
            {
                var milestone =
                    StartupCriticalPathMilestone.DatabaseTaskReturn;
                FailLocked(
                    "duplicate_milestone",
                    MilestoneNames[(int)milestone],
                    cancellation: false);
                return;
            }

            _databaseTaskReturnedRecorded = true;
            _databaseTaskReturnedTerminal = wasTerminal;
            RecordMilestoneLocked(
                StartupCriticalPathMilestone.DatabaseTaskReturn,
                timestamp);
        }
    }

    internal void RecordEnumerationTaskReturned(bool wasTerminal)
    {
        if (!TryCaptureTimestamp(out var timestamp))
            return;

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;
            if (_enumerationTaskReturnedRecorded)
            {
                var milestone =
                    StartupCriticalPathMilestone.EnumerationTaskReturn;
                FailLocked(
                    "duplicate_milestone",
                    MilestoneNames[(int)milestone],
                    cancellation: false);
                return;
            }

            _enumerationTaskReturnedRecorded = true;
            _enumerationTaskReturnedTerminal = wasTerminal;
            RecordMilestoneLocked(
                StartupCriticalPathMilestone.EnumerationTaskReturn,
                timestamp);
        }
    }

    internal void RecordEnumerationResult(SongEnumerationResult result)
    {
        if (result is null)
        {
            Fail("missing_enumeration_result", "enumeration_result");
            return;
        }

        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;
            if (_enumerationResultRecorded)
            {
                FailLocked(
                    "duplicate_enumeration_result",
                    "enumeration_result",
                    cancellation: false);
                return;
            }

            _enumerationResultRecorded = true;
            _enumerationDiscoveryTicks = result.Batch.DiscoveryAndParsingDuration.Ticks;
            _enumerationPersistenceTicks = result.Import.PersistenceDuration.Ticks;
            _enumerationCleanupTicks = result.Import.CleanupDuration.Ticks;
            _enumerationHierarchyTicks = result.HierarchyDuration.Ticks;
            SetLastMilestoneLocked("enumeration_result");
        }
    }

    internal void RecordTitleCompletionLookup(bool cacheHit)
    {
        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;
            if (_titleCompletionLookupRecorded)
            {
                FailLocked(
                    "duplicate_title_completion_lookup",
                    "title_completion_lookup",
                    cancellation: false);
                return;
            }

            _titleCompletionLookupRecorded = true;
            _titleCompletionLookupCacheHit = cacheHit;
            SetLastMilestoneLocked("title_completion_lookup");
            if (!cacheHit)
            {
                FailLocked(
                    "title_completion_cache_miss",
                    "title_completion_lookup",
                    cancellation: false);
            }
        }
    }

    internal void Fail(string error, string lastMilestone, bool cancellation = false)
    {
        lock (_sync)
        {
            if (_terminalOutcome != TerminalOutcome.Open)
                return;

            FailLocked(error, lastMilestone, cancellation);
        }
    }

    void IStartupSongLoadTimingObserver.BeginDatabaseSpan(
        StartupDatabaseTimingSpan span) =>
        BeginAggregate(MapDatabaseAggregate(span));

    void IStartupSongLoadTimingObserver.EndDatabaseSpan(
        StartupDatabaseTimingSpan span) =>
        EndAggregate(MapDatabaseAggregate(span));

    void IStartupSongLoadTimingObserver.RecordUnexpectedTableExistsPath() =>
        Fail(
            "unexpected_table_exists_path",
            "unexpected_table_exists_path");

    void IStartupSongLoadTimingObserver.RecordEnumerationTerminal(
        SongEnumerationResult? result,
        StartupOperationOutcome outcome)
    {
        RecordExactlyOnce(StartupCriticalPathMilestone.EnumerationTerminal);

        switch (outcome)
        {
            case StartupOperationOutcome.Success:
                RecordEnumerationResult(result!);
                break;

            case StartupOperationOutcome.Failure:
                Fail(
                    "enumeration_failure",
                    nameof(StartupCriticalPathMilestone.EnumerationTerminal));
                break;

            case StartupOperationOutcome.Cancellation:
                Fail(
                    "enumeration_cancellation",
                    nameof(StartupCriticalPathMilestone.EnumerationTerminal),
                    cancellation: true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    internal bool TryPublishTerminal(TextWriter writer)
    {
        if (writer is null)
            return false;

        Snapshot snapshot;
        lock (_sync)
        {
            if (_publicationAttempted)
                return false;
            if (_terminalOutcome == TerminalOutcome.Open &&
                (!_recorded[(int)StartupCriticalPathMilestone.TitleBackbufferBlitEnd] ||
                 _titleBackbufferWallClockPending))
            {
                return false;
            }

            if (_terminalOutcome == TerminalOutcome.Open)
                _terminalOutcome = TerminalOutcome.ReservedSuccess;

            _publicationAttempted = true;
            Volatile.Write(ref _terminalClosed, 1);
            snapshot = CreateSnapshotLocked();
        }

        string line;
        if (snapshot.Outcome is TerminalOutcome.Failure or TerminalOutcome.Cancellation)
        {
            line = FormatFailureLine(
                snapshot.Outcome,
                snapshot.ErrorRaw,
                snapshot.LastMilestoneRaw);
        }
        else if (!TryFormatSuccessLine(snapshot, out line, out var validationError))
        {
            line = FormatFailureLine(
                TerminalOutcome.Failure,
                validationError,
                snapshot.LastMilestoneRaw);
        }

        try
        {
            writer.WriteLine(line);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryFormatSuccessLine(
        Snapshot snapshot,
        out string line,
        out string validationError)
    {
        line = string.Empty;
        validationError = "schema_validation_failed";

        try
        {
            if (!TryGetStableTimestampFrequency(out var timestampFrequency))
            {
                validationError = "clock_failure";
                return false;
            }
            if (!InRange(snapshot.EntryUnixMicroseconds, MaximumUtcMicroseconds) ||
                !InRange(snapshot.TitleBackbufferUnixMicroseconds, MaximumUtcMicroseconds))
            {
                return false;
            }
            if (Array.Exists(snapshot.AggregateActive, active => active))
            {
                validationError = "aggregate_still_open";
                return false;
            }

            var origins = new long[snapshot.Timestamps.Length];
            for (var index = 0; index < origins.Length; index++)
            {
                if (!snapshot.Recorded[index])
                {
                    validationError = "missing_milestone";
                    return false;
                }

                origins[index] = MillisecondsFromEntry(
                    snapshot.Timestamps[index],
                    timestampFrequency);
                if (!InRange(origins[index], MaximumMilliseconds))
                {
                    validationError = "origin_out_of_bounds";
                    return false;
                }
            }

            if (!ValidateMilestoneOrder(snapshot))
            {
                validationError = "invalid_milestone_order";
                return false;
            }

            var durations = new long[snapshot.AggregateTimestampTicks.Length];
            for (var index = 0; index < durations.Length; index++)
            {
                durations[index] = TimestampTicksToMilliseconds(
                    snapshot.AggregateTimestampTicks[index],
                    timestampFrequency);
                if (!InRange(durations[index], MaximumMilliseconds) ||
                    !InRange(snapshot.AggregateCounts[index], MaximumCounter))
                {
                    validationError = "aggregate_out_of_bounds";
                    return false;
                }
            }

            var startupGameTime = TimeSpanTicksToMilliseconds(snapshot.StartupGameTimeTicks);
            var transitionGameTime =
                TimeSpanTicksToMilliseconds(snapshot.TransitionGameTimeTicks);
            var enumerationDiscovery =
                TimeSpanTicksToMilliseconds(snapshot.EnumerationDiscoveryTicks);
            var enumerationPersistence =
                TimeSpanTicksToMilliseconds(snapshot.EnumerationPersistenceTicks);
            var enumerationCleanup =
                TimeSpanTicksToMilliseconds(snapshot.EnumerationCleanupTicks);
            var enumerationHierarchy =
                TimeSpanTicksToMilliseconds(snapshot.EnumerationHierarchyTicks);

            if (snapshot.EnumerationDiscoveryTicks < 0 ||
                snapshot.EnumerationPersistenceTicks < 0 ||
                snapshot.EnumerationCleanupTicks < 0 ||
                snapshot.EnumerationHierarchyTicks < 0 ||
                !InRange(snapshot.StartupUpdateCount, MaximumCounter) ||
                !InRange(snapshot.StartupDrawCount, MaximumCounter) ||
                !InRange(snapshot.TransitionUpdateCount, MaximumCounter) ||
                !InRange(snapshot.TitleSoundLoadCount, MaximumCounter) ||
                !InRange(startupGameTime, MaximumMilliseconds) ||
                !InRange(transitionGameTime, MaximumMilliseconds) ||
                !InRange(enumerationDiscovery, MaximumMilliseconds) ||
                !InRange(enumerationPersistence, MaximumMilliseconds) ||
                !InRange(enumerationCleanup, MaximumMilliseconds) ||
                !InRange(enumerationHierarchy, MaximumMilliseconds))
            {
                validationError = "counter_out_of_bounds";
                return false;
            }

            if (!ValidateAggregateCounts(snapshot))
            {
                validationError = "invalid_aggregate_count";
                return false;
            }

            var invalidRecoveryCount =
                snapshot.AggregateCounts[(int)StartupCriticalPathAggregate.DatabaseInvalidRecovery];
            var ensureCreatedCount =
                snapshot.AggregateCounts[(int)StartupCriticalPathAggregate.DatabaseEnsureCreated];
            if (invalidRecoveryCount > 2 ||
                ensureCreatedCount is < 1 or > 2 ||
                !snapshot.DatabaseTaskReturnedRecorded ||
                !snapshot.EnumerationTaskReturnedRecorded ||
                !snapshot.EnumerationResultRecorded ||
                !snapshot.TitleCompletionLookupRecorded ||
                !snapshot.TitleCompletionLookupCacheHit)
            {
                validationError = "invalid_count_or_flag";
                return false;
            }

            var fallbackRan = snapshot.TitleGameStartFallbackRan ? 1L : 0L;
            if (snapshot.TitleSoundLoadCount != 3 + fallbackRan ||
                snapshot.AggregateCounts[
                    (int)StartupCriticalPathAggregate.TitleGameStartFallback] != fallbackRan)
            {
                validationError = "invalid_title_sound_count";
                return false;
            }

            var postLoadUnattributed =
                Origin(origins, StartupCriticalPathMilestone.InitializeComplete) -
                Origin(origins, StartupCriticalPathMilestone.LoadContentComplete) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.StartupConstructBegin,
                    StartupCriticalPathMilestone.StartupConstructEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.StartupActivateBegin,
                    StartupCriticalPathMilestone.StartupActivateEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.LoadContentReturn,
                    StartupCriticalPathMilestone.BaseInitializeReturn) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.InputManagerBegin,
                    StartupCriticalPathMilestone.InputManagerEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.SavedBindingsBegin,
                    StartupCriticalPathMilestone.SavedBindingsEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.GraphicsInitializeBegin,
                    StartupCriticalPathMilestone.GraphicsInitializeEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.RenderTargetBegin,
                    StartupCriticalPathMilestone.RenderTargetEnd);

            var dbInitUnattributed =
                Origin(origins, StartupCriticalPathMilestone.DatabaseTerminal) -
                Origin(origins, StartupCriticalPathMilestone.DatabaseInvoke) -
                Duration(durations, StartupCriticalPathAggregate.DatabaseServiceSetup) -
                Duration(durations, StartupCriticalPathAggregate.DatabaseCorruptionProbe) -
                Duration(durations, StartupCriticalPathAggregate.DatabaseInvalidRecovery) -
                Duration(durations, StartupCriticalPathAggregate.DatabaseEnsureCreated) -
                Duration(durations, StartupCriticalPathAggregate.DatabaseEncodingPragmas) -
                Duration(durations, StartupCriticalPathAggregate.DatabaseVersionWork) -
                Duration(durations, StartupCriticalPathAggregate.DatabaseSchemaEnsures);

            var enumerationUnattributed =
                Origin(origins, StartupCriticalPathMilestone.EnumerationTerminal) -
                Origin(origins, StartupCriticalPathMilestone.EnumerationInvoke) -
                enumerationDiscovery -
                enumerationPersistence -
                enumerationCleanup -
                enumerationHierarchy;

            var titleActivationUnattributed =
                Origin(origins, StartupCriticalPathMilestone.TitleActivateEnd) -
                Origin(origins, StartupCriticalPathMilestone.TitleActivateBegin) -
                Duration(durations, StartupCriticalPathAggregate.TitleGpuSetup) -
                Duration(durations, StartupCriticalPathAggregate.TitleBackground) -
                Duration(durations, StartupCriticalPathAggregate.TitleMenu) -
                Duration(durations, StartupCriticalPathAggregate.TitleFont) -
                Duration(durations, StartupCriticalPathAggregate.TitleCursorSound) -
                Duration(durations, StartupCriticalPathAggregate.TitleDecideSound) -
                Duration(durations, StartupCriticalPathAggregate.TitleGameStartSound) -
                Duration(durations, StartupCriticalPathAggregate.TitleGameStartFallback);

            var summaryToTitleUnattributed =
                Origin(origins, StartupCriticalPathMilestone.TitleBackbufferBlitEnd) -
                Origin(origins, StartupCriticalPathMilestone.SummaryRequest) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.TitleConstructBegin,
                    StartupCriticalPathMilestone.TitleConstructEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.TransitionStart,
                    StartupCriticalPathMilestone.TransitionComplete) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.StartupDeactivateBegin,
                    StartupCriticalPathMilestone.StartupDeactivateEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.TitleActivateBegin,
                    StartupCriticalPathMilestone.TitleActivateEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.TitleFirstUpdateBegin,
                    StartupCriticalPathMilestone.TitleFirstUpdateEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.TitleStageDrawBegin,
                    StartupCriticalPathMilestone.TitleStageDrawEnd) -
                OriginDuration(
                    origins,
                    StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
                    StartupCriticalPathMilestone.TitleBackbufferBlitEnd);

            if (postLoadUnattributed < 0 ||
                dbInitUnattributed < 0 ||
                enumerationUnattributed < 0 ||
                titleActivationUnattributed < 0 ||
                summaryToTitleUnattributed < 0)
            {
                validationError = "negative_residual";
                return false;
            }

            line = FormatSuccessLine(
                snapshot,
                origins,
                durations,
                startupGameTime,
                transitionGameTime,
                postLoadUnattributed,
                enumerationUnattributed,
                dbInitUnattributed,
                summaryToTitleUnattributed,
                titleActivationUnattributed);
            return true;
        }
        catch (OverflowException)
        {
            validationError = "clock_overflow";
            return false;
        }
        catch
        {
            validationError = "validation_failure";
            return false;
        }
    }

    private static bool ValidateMilestoneOrder(Snapshot snapshot)
    {
        bool Ordered(params StartupCriticalPathMilestone[] milestones)
        {
            for (var index = 1; index < milestones.Length; index++)
            {
                if (snapshot.Timestamps[(int)milestones[index - 1]] >
                    snapshot.Timestamps[(int)milestones[index]])
                {
                    return false;
                }
            }

            return true;
        }

        if (!Ordered(
                StartupCriticalPathMilestone.LoadContentComplete,
                StartupCriticalPathMilestone.StartupConstructBegin,
                StartupCriticalPathMilestone.StartupConstructEnd,
                StartupCriticalPathMilestone.StartupActivateBegin,
                StartupCriticalPathMilestone.StartupActivation,
                StartupCriticalPathMilestone.StartupActivateEnd,
                StartupCriticalPathMilestone.LoadContentReturn,
                StartupCriticalPathMilestone.BaseInitializeReturn,
                StartupCriticalPathMilestone.InputManagerBegin,
                StartupCriticalPathMilestone.InputManagerEnd,
                StartupCriticalPathMilestone.SavedBindingsBegin,
                StartupCriticalPathMilestone.SavedBindingsEnd,
                StartupCriticalPathMilestone.GraphicsInitializeBegin,
                StartupCriticalPathMilestone.GraphicsInitializeEnd,
                StartupCriticalPathMilestone.RenderTargetBegin,
                StartupCriticalPathMilestone.RenderTargetEnd,
                StartupCriticalPathMilestone.InitializeComplete,
                StartupCriticalPathMilestone.StartupFirstUpdateBegin,
                StartupCriticalPathMilestone.StartupFirstUpdateEnd,
                StartupCriticalPathMilestone.StartupFirstDrawBegin,
                StartupCriticalPathMilestone.StartupFirstDrawEnd) ||
            !Ordered(
                StartupCriticalPathMilestone.DatabaseInvoke,
                StartupCriticalPathMilestone.DatabaseTaskReturn,
                StartupCriticalPathMilestone.DatabaseObserved) ||
            !Ordered(
                StartupCriticalPathMilestone.DatabaseInvoke,
                StartupCriticalPathMilestone.DatabaseTerminal,
                StartupCriticalPathMilestone.DatabaseObserved) ||
            !Ordered(
                StartupCriticalPathMilestone.EnumerationInvoke,
                StartupCriticalPathMilestone.EnumerationTaskReturn,
                StartupCriticalPathMilestone.EnumerationObserved) ||
            !Ordered(
                StartupCriticalPathMilestone.EnumerationInvoke,
                StartupCriticalPathMilestone.EnumerationTerminal,
                StartupCriticalPathMilestone.EnumerationObserved) ||
            !Ordered(
                StartupCriticalPathMilestone.SummaryRequest,
                StartupCriticalPathMilestone.TitleConstructBegin,
                StartupCriticalPathMilestone.TitleConstructEnd,
                StartupCriticalPathMilestone.TransitionStart,
                StartupCriticalPathMilestone.TransitionComplete,
                StartupCriticalPathMilestone.StartupDeactivateBegin,
                StartupCriticalPathMilestone.StartupDeactivateEnd,
                StartupCriticalPathMilestone.TitleActivateBegin,
                StartupCriticalPathMilestone.TitleActivateEnd,
                StartupCriticalPathMilestone.TitleFirstUpdateBegin,
                StartupCriticalPathMilestone.TitleFirstUpdateEnd,
                StartupCriticalPathMilestone.TitleStageDrawBegin,
                StartupCriticalPathMilestone.TitleStageDrawEnd,
                StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
                StartupCriticalPathMilestone.TitleBackbufferBlitEnd))
        {
            return false;
        }

        var dbTaskReturn =
            snapshot.Timestamps[(int)StartupCriticalPathMilestone.DatabaseTaskReturn];
        var dbTerminal =
            snapshot.Timestamps[(int)StartupCriticalPathMilestone.DatabaseTerminal];
        if ((!snapshot.DatabaseTaskReturnedTerminal && dbTaskReturn > dbTerminal) ||
            (snapshot.DatabaseTaskReturnedTerminal && dbTerminal > dbTaskReturn))
        {
            return false;
        }

        var enumerationTaskReturn =
            snapshot.Timestamps[(int)StartupCriticalPathMilestone.EnumerationTaskReturn];
        var enumerationTerminal =
            snapshot.Timestamps[(int)StartupCriticalPathMilestone.EnumerationTerminal];
        if ((!snapshot.EnumerationTaskReturnedTerminal &&
             enumerationTaskReturn > enumerationTerminal) ||
            (snapshot.EnumerationTaskReturnedTerminal &&
             enumerationTerminal > enumerationTaskReturn))
        {
            return false;
        }

        return snapshot.Timestamps[(int)StartupCriticalPathMilestone.DatabaseObserved] <=
                   snapshot.Timestamps[(int)StartupCriticalPathMilestone.SummaryRequest] &&
               snapshot.Timestamps[(int)StartupCriticalPathMilestone.EnumerationObserved] <=
                   snapshot.Timestamps[(int)StartupCriticalPathMilestone.SummaryRequest];
    }

    private static bool ValidateAggregateCounts(Snapshot snapshot)
    {
        long Count(StartupCriticalPathAggregate aggregate) =>
            snapshot.AggregateCounts[(int)aggregate];

        return Count(StartupCriticalPathAggregate.DatabaseServiceSetup) == 1 &&
               Count(StartupCriticalPathAggregate.DatabaseCorruptionProbe) is >= 1 and <= 3 &&
               Count(StartupCriticalPathAggregate.DatabaseInvalidRecovery) is >= 0 and <= 2 &&
               Count(StartupCriticalPathAggregate.DatabaseEnsureCreated) is >= 1 and <= 2 &&
               Count(StartupCriticalPathAggregate.DatabaseEncodingPragmas) == 1 &&
               Count(StartupCriticalPathAggregate.DatabaseVersionWork) == 1 &&
               Count(StartupCriticalPathAggregate.DatabaseSchemaEnsures) == 1 &&
               Count(StartupCriticalPathAggregate.TitleGpuSetup) == 1 &&
               Count(StartupCriticalPathAggregate.TitleBackground) == 1 &&
               Count(StartupCriticalPathAggregate.TitleMenu) == 1 &&
               Count(StartupCriticalPathAggregate.TitleFont) == 1 &&
               Count(StartupCriticalPathAggregate.TitleCursorSound) == 1 &&
               Count(StartupCriticalPathAggregate.TitleDecideSound) == 1 &&
               Count(StartupCriticalPathAggregate.TitleGameStartSound) == 1;
    }

    private string FormatSuccessLine(
        Snapshot snapshot,
        long[] origins,
        long[] durations,
        long startupGameTime,
        long transitionGameTime,
        long postLoadUnattributed,
        long enumerationUnattributed,
        long dbInitUnattributed,
        long summaryToTitleUnattributed,
        long titleActivationUnattributed)
    {
        var builder = new StringBuilder("HPA192_CRITICAL_PATH");
        AppendField(builder, "outcome", "success");
        AppendField(builder, "error", "none");
        AppendField(builder, "entry_unix_us", snapshot.EntryUnixMicroseconds);
        AppendField(
            builder,
            "title_backbuffer_unix_us",
            snapshot.TitleBackbufferUnixMicroseconds);
        AppendField(
            builder,
            "entry_to_title_backbuffer_ms",
            Origin(origins, StartupCriticalPathMilestone.TitleBackbufferBlitEnd));
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.LoadContentComplete, "load_content_complete_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupConstructBegin, "startup_construct_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupConstructEnd, "startup_construct_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupActivateBegin, "startup_activate_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupActivation, "startup_activation_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupActivateEnd, "startup_activate_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.LoadContentReturn, "load_content_return_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.BaseInitializeReturn, "base_initialize_return_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.InputManagerBegin, "input_manager_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.InputManagerEnd, "input_manager_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.SavedBindingsBegin, "saved_bindings_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.SavedBindingsEnd, "saved_bindings_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.GraphicsInitializeBegin, "graphics_initialize_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.GraphicsInitializeEnd, "graphics_initialize_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.RenderTargetBegin, "render_target_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.RenderTargetEnd, "render_target_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.InitializeComplete, "initialize_complete_from_entry_ms");
        AppendField(builder, "post_load_unattributed_ms", postLoadUnattributed);
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupFirstUpdateBegin, "startup_first_update_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupFirstUpdateEnd, "startup_first_update_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupFirstDrawBegin, "startup_first_draw_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupFirstDrawEnd, "startup_first_draw_end_from_entry_ms");
        AppendField(builder, "startup_updates_before_first_draw", snapshot.StartupUpdateCount);
        AppendField(builder, "startup_game_time_before_first_draw_ms", startupGameTime);
        AppendField(builder, "startup_draws_before_transition", snapshot.StartupDrawCount);
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.DatabaseInvoke, "db_invoke_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.DatabaseTaskReturn, "db_task_return_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.DatabaseTerminal, "db_terminal_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.DatabaseObserved, "db_observed_from_entry_ms");
        AppendField(builder, "db_task_returned_terminal", snapshot.DatabaseTaskReturnedTerminal ? 1 : 0);
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.EnumerationInvoke, "enumeration_invoke_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.EnumerationTaskReturn, "enumeration_task_return_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.EnumerationTerminal, "enumeration_terminal_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.EnumerationObserved, "enumeration_observed_from_entry_ms");
        AppendField(builder, "enumeration_task_returned_terminal", snapshot.EnumerationTaskReturnedTerminal ? 1 : 0);
        AppendField(builder, "enumeration_unattributed_ms", enumerationUnattributed);
        AppendDuration(builder, durations, StartupCriticalPathAggregate.DatabaseServiceSetup, "db_service_setup_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.DatabaseCorruptionProbe, "db_corruption_probe_ms");
        AppendField(builder, "db_invalid_recovery_count", snapshot.AggregateCounts[(int)StartupCriticalPathAggregate.DatabaseInvalidRecovery]);
        AppendDuration(builder, durations, StartupCriticalPathAggregate.DatabaseInvalidRecovery, "db_invalid_recovery_ms");
        AppendField(builder, "db_ensure_created_count", snapshot.AggregateCounts[(int)StartupCriticalPathAggregate.DatabaseEnsureCreated]);
        AppendDuration(builder, durations, StartupCriticalPathAggregate.DatabaseEnsureCreated, "db_ensure_created_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.DatabaseEncodingPragmas, "db_encoding_pragmas_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.DatabaseVersionWork, "db_version_work_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.DatabaseSchemaEnsures, "db_schema_ensures_ms");
        AppendField(builder, "db_init_unattributed_ms", dbInitUnattributed);
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.SummaryRequest, "summary_request_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleConstructBegin, "title_construct_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleConstructEnd, "title_construct_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TransitionStart, "transition_start_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TransitionComplete, "transition_complete_from_entry_ms");
        AppendField(builder, "transition_update_count", snapshot.TransitionUpdateCount);
        AppendField(builder, "transition_game_time_ms", transitionGameTime);
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupDeactivateBegin, "startup_deactivate_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.StartupDeactivateEnd, "startup_deactivate_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleActivateBegin, "title_activate_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleActivateEnd, "title_activate_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleFirstUpdateBegin, "title_first_update_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleFirstUpdateEnd, "title_first_update_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleStageDrawBegin, "title_stage_draw_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleStageDrawEnd, "title_stage_draw_end_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleBackbufferBlitBegin, "title_backbuffer_blit_begin_from_entry_ms");
        AppendOrigin(builder, origins, StartupCriticalPathMilestone.TitleBackbufferBlitEnd, "title_backbuffer_blit_end_from_entry_ms");
        AppendField(builder, "summary_to_title_unattributed_ms", summaryToTitleUnattributed);
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleGpuSetup, "title_gpu_setup_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleBackground, "title_background_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleMenu, "title_menu_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleFont, "title_font_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleCursorSound, "title_cursor_sound_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleDecideSound, "title_decide_sound_ms");
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleGameStartSound, "title_game_start_sound_ms");
        AppendField(builder, "title_game_start_fallback_ran", snapshot.TitleGameStartFallbackRan ? 1 : 0);
        AppendDuration(builder, durations, StartupCriticalPathAggregate.TitleGameStartFallback, "title_game_start_fallback_ms");
        AppendField(builder, "title_sound_load_count", snapshot.TitleSoundLoadCount);
        AppendField(builder, "title_activation_unattributed_ms", titleActivationUnattributed);
        AppendField(builder, "title_backbuffer_published", 1);
        return builder.ToString();
    }

    private Snapshot CreateSnapshotLocked()
    {
        return new Snapshot(
            _terminalOutcome,
            _terminalErrorRaw,
            _lastMilestoneRaw,
            _entryUnixMicroseconds,
            _titleBackbufferUnixMicroseconds,
            (long[])_timestamps.Clone(),
            (bool[])_recorded.Clone(),
            (long[])_aggregateTimestampTicks.Clone(),
            (long[])_aggregateCounts.Clone(),
            (bool[])_aggregateActive.Clone(),
            _startupUpdateCount,
            _startupGameTimeTicks,
            _startupDrawCount,
            _transitionUpdateCount,
            _transitionGameTimeTicks,
            _titleSoundLoadCount,
            _titleGameStartFallbackRan,
            _databaseTaskReturnedRecorded,
            _databaseTaskReturnedTerminal,
            _enumerationTaskReturnedRecorded,
            _enumerationTaskReturnedTerminal,
            _enumerationResultRecorded,
            _enumerationDiscoveryTicks,
            _enumerationPersistenceTicks,
            _enumerationCleanupTicks,
            _enumerationHierarchyTicks,
            _titleCompletionLookupRecorded,
            _titleCompletionLookupCacheHit);
    }

    private void RecordMilestoneLocked(
        StartupCriticalPathMilestone milestone,
        long timestamp)
    {
        var index = (int)milestone;
        _timestamps[index] = timestamp;
        _recorded[index] = true;
        SetLastMilestoneLocked(MilestoneNames[index]);
    }

    private bool TryCaptureTimestamp(out long timestamp)
    {
        timestamp = 0;
        if (Volatile.Read(ref _terminalClosed) != 0)
            return false;

        try
        {
            timestamp = _clock.GetTimestamp();
            return true;
        }
        catch
        {
            Fail("clock_failure", "clock");
            return false;
        }
    }

    private bool TryElapsedTimeTicks(double elapsedSeconds, out long ticks)
    {
        ticks = 0;
        if (Volatile.Read(ref _terminalClosed) != 0)
            return false;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            Fail("invalid_elapsed_time", "elapsed_time");
            return false;
        }

        try
        {
            ticks = TimeSpan.FromSeconds(elapsedSeconds).Ticks;
            return true;
        }
        catch (OverflowException)
        {
            Fail("elapsed_time_overflow", "elapsed_time");
            return false;
        }
    }

    private void IncrementCounterLocked(
        ref long count,
        ref long elapsedTicks,
        long additionalTicks,
        string name)
    {
        try
        {
            count = checked(count + 1);
            elapsedTicks = checked(elapsedTicks + additionalTicks);
        }
        catch (OverflowException)
        {
            FailLocked("counter_overflow", name, cancellation: false);
        }
    }

    private void FailLocked(string error, string lastMilestone, bool cancellation)
    {
        _terminalOutcome =
            cancellation ? TerminalOutcome.Cancellation : TerminalOutcome.Failure;
        _terminalErrorRaw = error;
        SetLastMilestoneLocked(lastMilestone);
        Volatile.Write(ref _terminalClosed, 1);
    }

    private void SetLastMilestoneLocked(string value) =>
        _lastMilestoneRaw = value;

    private static bool IsSafeTokenCharacter(char character) =>
        character is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or
            '.' or '_' or '-';

    private bool TryGetStableTimestampFrequency(out long timestampFrequency)
    {
        timestampFrequency = 0;
        try
        {
            var firstRead = _clock.TimestampFrequency;
            var secondRead = _clock.TimestampFrequency;
            if (firstRead <= 0 || secondRead != firstRead)
                return false;

            timestampFrequency = firstRead;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private long MillisecondsFromEntry(long timestamp, long timestampFrequency)
    {
        return checked(
            (timestamp - _entryTimestamp) *
            1_000 /
            timestampFrequency);
    }

    private static long TimestampTicksToMilliseconds(
        long ticks,
        long timestampFrequency)
    {
        return checked(ticks * 1_000 / timestampFrequency);
    }

    private static long TimeSpanTicksToMilliseconds(long ticks)
    {
        return ticks / TimeSpan.TicksPerMillisecond;
    }

    private static long Origin(long[] origins, StartupCriticalPathMilestone milestone) =>
        origins[(int)milestone];

    private static long Duration(
        long[] durations,
        StartupCriticalPathAggregate aggregate) =>
        durations[(int)aggregate];

    private static long OriginDuration(
        long[] origins,
        StartupCriticalPathMilestone begin,
        StartupCriticalPathMilestone end) =>
        Origin(origins, end) - Origin(origins, begin);

    private static bool InRange(long value, long maximum) =>
        value >= 0 && value <= maximum;

    private static long MaximumAggregateCount(StartupCriticalPathAggregate aggregate)
    {
        return aggregate switch
        {
            StartupCriticalPathAggregate.DatabaseCorruptionProbe => 3,
            StartupCriticalPathAggregate.DatabaseInvalidRecovery => 2,
            StartupCriticalPathAggregate.DatabaseEnsureCreated => 2,
            _ => 1
        };
    }

    private static StartupCriticalPathAggregate MapDatabaseAggregate(
        StartupDatabaseTimingSpan span)
    {
        return span switch
        {
            StartupDatabaseTimingSpan.ServiceSetup =>
                StartupCriticalPathAggregate.DatabaseServiceSetup,
            StartupDatabaseTimingSpan.CorruptionProbe =>
                StartupCriticalPathAggregate.DatabaseCorruptionProbe,
            StartupDatabaseTimingSpan.InvalidRecovery =>
                StartupCriticalPathAggregate.DatabaseInvalidRecovery,
            StartupDatabaseTimingSpan.EnsureCreated =>
                StartupCriticalPathAggregate.DatabaseEnsureCreated,
            StartupDatabaseTimingSpan.EncodingPragmas =>
                StartupCriticalPathAggregate.DatabaseEncodingPragmas,
            StartupDatabaseTimingSpan.VersionWork =>
                StartupCriticalPathAggregate.DatabaseVersionWork,
            StartupDatabaseTimingSpan.SchemaEnsures =>
                StartupCriticalPathAggregate.DatabaseSchemaEnsures,
            _ => throw new ArgumentOutOfRangeException(nameof(span))
        };
    }

    private static string FormatFailureLine(
        TerminalOutcome outcome,
        string? error,
        string? lastMilestone)
    {
        var outcomeToken =
            outcome == TerminalOutcome.Cancellation ? "cancellation" : "failure";
        return string.Format(
            CultureInfo.InvariantCulture,
            "HPA192_CRITICAL_PATH_FAILURE outcome={0} error={1} last_milestone={2}",
            outcomeToken,
            SafeToken(error),
            SafeToken(lastMilestone));
    }

    private static string SafeToken(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "unknown";

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (IsSafeTokenCharacter(character))
                builder.Append(character);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static void AppendOrigin(
        StringBuilder builder,
        long[] origins,
        StartupCriticalPathMilestone milestone,
        string name) =>
        AppendField(builder, name, Origin(origins, milestone));

    private static void AppendDuration(
        StringBuilder builder,
        long[] durations,
        StartupCriticalPathAggregate aggregate,
        string name) =>
        AppendField(builder, name, Duration(durations, aggregate));

    private static void AppendField(StringBuilder builder, string name, long value)
    {
        builder.Append(' ');
        builder.Append(name);
        builder.Append('=');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendField(StringBuilder builder, string name, string value)
    {
        builder.Append(' ');
        builder.Append(name);
        builder.Append('=');
        builder.Append(value);
    }

    private enum TerminalOutcome
    {
        Open,
        ReservedSuccess,
        Failure,
        Cancellation
    }

    private sealed record Snapshot(
        TerminalOutcome Outcome,
        string? ErrorRaw,
        string? LastMilestoneRaw,
        long EntryUnixMicroseconds,
        long TitleBackbufferUnixMicroseconds,
        long[] Timestamps,
        bool[] Recorded,
        long[] AggregateTimestampTicks,
        long[] AggregateCounts,
        bool[] AggregateActive,
        long StartupUpdateCount,
        long StartupGameTimeTicks,
        long StartupDrawCount,
        long TransitionUpdateCount,
        long TransitionGameTimeTicks,
        long TitleSoundLoadCount,
        bool TitleGameStartFallbackRan,
        bool DatabaseTaskReturnedRecorded,
        bool DatabaseTaskReturnedTerminal,
        bool EnumerationTaskReturnedRecorded,
        bool EnumerationTaskReturnedTerminal,
        bool EnumerationResultRecorded,
        long EnumerationDiscoveryTicks,
        long EnumerationPersistenceTicks,
        long EnumerationCleanupTicks,
        long EnumerationHierarchyTicks,
        bool TitleCompletionLookupRecorded,
        bool TitleCompletionLookupCacheHit);
}
