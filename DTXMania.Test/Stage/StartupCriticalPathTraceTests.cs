using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class StartupCriticalPathTraceTests
{
    private const string ExpectedSuccessLine =
        "HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1000000 " +
        "title_backbuffer_unix_us=2000000 entry_to_title_backbuffer_ms=116 " +
        "load_content_complete_from_entry_ms=10 startup_construct_begin_from_entry_ms=11 " +
        "startup_construct_end_from_entry_ms=12 startup_activate_begin_from_entry_ms=13 " +
        "startup_activation_from_entry_ms=14 startup_activate_end_from_entry_ms=15 " +
        "load_content_return_from_entry_ms=16 base_initialize_return_from_entry_ms=17 " +
        "input_manager_begin_from_entry_ms=18 input_manager_end_from_entry_ms=19 " +
        "saved_bindings_begin_from_entry_ms=20 saved_bindings_end_from_entry_ms=21 " +
        "graphics_initialize_begin_from_entry_ms=22 graphics_initialize_end_from_entry_ms=23 " +
        "render_target_begin_from_entry_ms=24 render_target_end_from_entry_ms=25 " +
        "initialize_complete_from_entry_ms=30 post_load_unattributed_ms=12 " +
        "startup_first_update_begin_from_entry_ms=31 startup_first_update_end_from_entry_ms=32 " +
        "startup_first_draw_begin_from_entry_ms=33 startup_first_draw_end_from_entry_ms=34 " +
        "startup_updates_before_first_draw=1 startup_game_time_before_first_draw_ms=5 " +
        "startup_draws_before_transition=1 db_invoke_from_entry_ms=40 " +
        "db_task_return_from_entry_ms=41 db_terminal_from_entry_ms=50 " +
        "db_observed_from_entry_ms=51 db_task_returned_terminal=0 " +
        "enumeration_invoke_from_entry_ms=60 enumeration_task_return_from_entry_ms=61 " +
        "enumeration_terminal_from_entry_ms=70 enumeration_observed_from_entry_ms=71 " +
        "enumeration_task_returned_terminal=0 enumeration_unattributed_ms=4 " +
        "db_service_setup_ms=1 db_corruption_probe_ms=1 db_invalid_recovery_count=0 " +
        "db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=1 " +
        "db_encoding_pragmas_ms=1 db_version_work_ms=1 db_schema_ensures_ms=1 " +
        "db_init_unattributed_ms=4 summary_request_from_entry_ms=80 " +
        "title_construct_begin_from_entry_ms=81 title_construct_end_from_entry_ms=82 " +
        "transition_start_from_entry_ms=83 transition_complete_from_entry_ms=90 " +
        "transition_update_count=1 transition_game_time_ms=5 " +
        "startup_deactivate_begin_from_entry_ms=90 startup_deactivate_end_from_entry_ms=91 " +
        "title_activate_begin_from_entry_ms=92 title_activate_end_from_entry_ms=110 " +
        "title_first_update_begin_from_entry_ms=111 title_first_update_end_from_entry_ms=112 " +
        "title_stage_draw_begin_from_entry_ms=113 title_stage_draw_end_from_entry_ms=114 " +
        "title_backbuffer_blit_begin_from_entry_ms=115 title_backbuffer_blit_end_from_entry_ms=116 " +
        "summary_to_title_unattributed_ms=6 title_gpu_setup_ms=1 title_background_ms=1 " +
        "title_menu_ms=1 title_font_ms=1 title_cursor_sound_ms=1 title_decide_sound_ms=1 " +
        "title_game_start_sound_ms=1 title_game_start_fallback_ran=0 " +
        "title_game_start_fallback_ms=0 title_sound_load_count=3 " +
        "title_activation_unattributed_ms=11 title_backbuffer_published=1";

    [Fact]
    public void Publish_WhenComplete_ShouldWriteExactEightyOneFieldLineAndFlushOnce()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        using var writer = new TrackingWriter();

        var published = fixture.Trace.TryPublishTerminal(writer);

        Assert.True(published);
        Assert.Equal(ExpectedSuccessLine + "\n", writer.ToString());
        Assert.Equal(1, writer.FlushCount);
        var tokens = ExpectedSuccessLine.Split(' ');
        Assert.Equal(82, tokens.Length);
        var parsedNames = tokens.Skip(1).Select(token => token[..token.IndexOf('=')]).ToArray();
        Assert.Equal(StartupCriticalPathTrace.SuccessFieldNames, parsedNames);
    }

    [Fact]
    public void Publish_WhenLifecycleMilestoneDuplicates_ShouldWriteFailureOnly()
    {
        var fixture = CreateFixture();
        At(fixture, 10, () => fixture.Trace.RecordExactlyOnce(
            StartupCriticalPathMilestone.LoadContentComplete));
        At(fixture, 11, () => fixture.Trace.RecordExactlyOnce(
            StartupCriticalPathMilestone.LoadContentComplete));
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith(
            "HPA192_CRITICAL_PATH_FAILURE outcome=failure error=duplicate_milestone ",
            writer.ToString());
        Assert.DoesNotContain("HPA192_CRITICAL_PATH outcome=", writer.ToString());
    }

    [Fact]
    public void FirstObservation_WhenRepeated_ShouldKeepFirstMatchedPair()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        At(fixture, 200, () => fixture.Trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd));
        At(fixture, 201, () => fixture.Trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd));
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("startup_first_update_begin_from_entry_ms=31", writer.ToString());
        Assert.Contains("startup_first_update_end_from_entry_ms=32", writer.ToString());
        Assert.DoesNotContain("startup_first_update_begin_from_entry_ms=200", writer.ToString());
    }

    [Fact]
    public void FirstObservation_WhenTitleBackbufferEndRepeatsAndUtcThrows_ShouldIgnoreRepeat()
    {
        var clock = new ManualMonotonicClock();
        var trace = StartupCriticalPathTrace.Start(
            clock,
            new ThrowingAfterFirstUtcMicrosecondClock(2_000_000),
            entryTimestamp: 0,
            entryUnixMicroseconds: 1_000_000,
            exitAfterPublication: false);
        var fixture = new TraceFixture(trace, clock);
        CompleteValidTrace(fixture);
        At(fixture, 200, () => fixture.Trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
            StartupCriticalPathMilestone.TitleBackbufferBlitEnd));
        At(fixture, 201, () => fixture.Trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
            StartupCriticalPathMilestone.TitleBackbufferBlitEnd));
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(ExpectedSuccessLine + "\n", writer.ToString());
    }

    [Fact]
    public void Record_WhenUpdatingFixedSizeState_ShouldNotAllocate()
    {
        var warmup = CreateFixture();
        At(warmup, 1, () => warmup.Trace.RecordExactlyOnce(
            StartupCriticalPathMilestone.LoadContentComplete));
        Aggregate(
            warmup,
            StartupCriticalPathAggregate.DatabaseInvalidRecovery,
            begin: 2,
            end: 3);

        var fixture = CreateFixture();
        fixture.Clock.Timestamp = 1;
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();

        fixture.Trace.RecordExactlyOnce(StartupCriticalPathMilestone.LoadContentComplete);
        fixture.Clock.Timestamp = 2;
        fixture.Trace.BeginAggregate(StartupCriticalPathAggregate.DatabaseInvalidRecovery);
        fixture.Clock.Timestamp = 3;
        fixture.Trace.EndAggregate(StartupCriticalPathAggregate.DatabaseInvalidRecovery);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        // Allow a small bounded tolerance for GC bookkeeping noise while still
        // detecting unintended allocations from the hot-path recording.
        Assert.True(allocatedBytes <= 64,
            $"Expected <= 64 bytes allocated, got {allocatedBytes}");
    }

    [Fact]
    public void Aggregate_WhenRepeatedWithinBound_ShouldAccumulateAndCount()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture, invalidRecoverySpans: 2);
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("db_invalid_recovery_count=2", writer.ToString());
        Assert.Contains("db_invalid_recovery_ms=2", writer.ToString());
        Assert.Contains("db_init_unattributed_ms=2", writer.ToString());
    }

    [Fact]
    public void Aggregate_WhenNestedOrOverBound_ShouldInvalidate()
    {
        var nested = CreateFixture();
        At(nested, 1, () => nested.Trace.BeginAggregate(
            StartupCriticalPathAggregate.DatabaseInvalidRecovery));
        At(nested, 2, () => nested.Trace.BeginAggregate(
            StartupCriticalPathAggregate.DatabaseInvalidRecovery));
        using var nestedWriter = new TrackingWriter();

        var overBound = CreateFixture();
        for (var index = 0; index < 3; index++)
        {
            At(overBound, index * 2 + 1, () => overBound.Trace.BeginAggregate(
                StartupCriticalPathAggregate.DatabaseInvalidRecovery));
            At(overBound, index * 2 + 2, () => overBound.Trace.EndAggregate(
                StartupCriticalPathAggregate.DatabaseInvalidRecovery));
        }
        using var overBoundWriter = new TrackingWriter();

        Assert.True(nested.Trace.TryPublishTerminal(nestedWriter));
        Assert.Contains("error=nested_aggregate", nestedWriter.ToString());
        Assert.True(overBound.Trace.TryPublishTerminal(overBoundWriter));
        Assert.Contains("error=aggregate_count_exceeded", overBoundWriter.ToString());
    }

    [Fact]
    public void Counters_WhenWindowsClose_ShouldIgnoreLaterSamples()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);

        fixture.Trace.IncrementStartupUpdate(9);
        fixture.Trace.IncrementCompletedStartupDraw();
        fixture.Trace.IncrementTransitionUpdate(9);
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("startup_updates_before_first_draw=1", writer.ToString());
        Assert.Contains("startup_game_time_before_first_draw_ms=5", writer.ToString());
        Assert.Contains("startup_draws_before_transition=1", writer.ToString());
        Assert.Contains("transition_update_count=1", writer.ToString());
        Assert.Contains("transition_game_time_ms=5", writer.ToString());
    }

    [Fact]
    public void Publish_WhenMilestoneMissingOrOutOfOrder_ShouldWriteFailureOnly()
    {
        var missing = CreateFixture();
        CompleteValidTrace(missing, omitMilestone: StartupCriticalPathMilestone.StartupConstructEnd);
        using var missingWriter = new TrackingWriter();

        var outOfOrder = CreateFixture();
        CompleteValidTrace(
            outOfOrder,
            timestampOverrides: new Dictionary<StartupCriticalPathMilestone, long>
            {
                [StartupCriticalPathMilestone.StartupConstructBegin] = 12,
                [StartupCriticalPathMilestone.StartupConstructEnd] = 11
            });
        using var outOfOrderWriter = new TrackingWriter();

        Assert.True(missing.Trace.TryPublishTerminal(missingWriter));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", missingWriter.ToString());
        Assert.True(outOfOrder.Trace.TryPublishTerminal(outOfOrderWriter));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", outOfOrderWriter.ToString());
    }

    [Fact]
    public void Terminal_WhenFailureWins_ShouldIgnoreLateWorkerCompletion()
    {
        var fixture = CreateFixture();
        fixture.Trace.Fail("database / failed!", "database terminal");
        At(fixture, 50, () => fixture.Trace.RecordExactlyOnce(
            StartupCriticalPathMilestone.DatabaseTerminal));
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            "HPA192_CRITICAL_PATH_FAILURE outcome=failure error=databasefailed " +
            "last_milestone=databaseterminal\n",
            writer.ToString());
    }

    [Fact]
    public void Terminal_WhenCancellationWins_ShouldUseCancellationOutcome()
    {
        var fixture = CreateFixture();
        fixture.Trace.Fail("operation_cancelled", "enumeration", cancellation: true);
        fixture.Trace.Fail("late_failure", "database");
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            "HPA192_CRITICAL_PATH_FAILURE outcome=cancellation error=operation_cancelled " +
            "last_milestone=enumeration\n",
            writer.ToString());
    }

    [Fact]
    public void Terminal_WhenSafeTokensExceedPreviousBuffer_ShouldNotTruncate()
    {
        var error = new string('e', 200);
        var lastMilestone = new string('m', 200);
        var fixture = CreateFixture();
        fixture.Trace.Fail(error, lastMilestone);
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            $"HPA192_CRITICAL_PATH_FAILURE outcome=failure error={error} " +
            $"last_milestone={lastMilestone}\n",
            writer.ToString());
    }

    [Fact]
    public void Terminal_WhenLongTokensContainUnsafeCharacters_ShouldFilterWithoutTruncating()
    {
        var errorPrefix = new string('a', 140);
        var errorSuffix = new string('b', 140);
        var milestonePrefix = new string('c', 140);
        var milestoneSuffix = new string('d', 140);
        var fixture = CreateFixture();
        fixture.Trace.Fail(
            errorPrefix + " /!\n" + ".-_" + errorSuffix,
            milestonePrefix + "\t? " + "_." + milestoneSuffix);
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            "HPA192_CRITICAL_PATH_FAILURE outcome=failure " +
            $"error={errorPrefix}.-_{errorSuffix} " +
            $"last_milestone={milestonePrefix}_.{milestoneSuffix}\n",
            writer.ToString());
    }

    [Fact]
    public void Publish_WhenClockValueOverflowsOrExceedsBound_ShouldWriteFailureOnly()
    {
        var overflow = CreateFixture();
        CompleteValidTrace(
            overflow,
            timestampOverrides: new Dictionary<StartupCriticalPathMilestone, long>
            {
                [StartupCriticalPathMilestone.TitleBackbufferBlitEnd] = long.MaxValue
            });
        using var overflowWriter = new TrackingWriter();

        var overBound = CreateFixture();
        CompleteValidTrace(
            overBound,
            timestampOverrides: new Dictionary<StartupCriticalPathMilestone, long>
            {
                [StartupCriticalPathMilestone.TitleBackbufferBlitEnd] = 300_001
            });
        using var overBoundWriter = new TrackingWriter();

        Assert.True(overflow.Trace.TryPublishTerminal(overflowWriter));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", overflowWriter.ToString());
        Assert.True(overBound.Trace.TryPublishTerminal(overBoundWriter));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", overBoundWriter.ToString());
    }

    [Fact]
    public void Publish_WhenTimestampFrequencyThrows_ShouldWriteFailureOnly()
    {
        var clock = new ThrowingFrequencyClock();
        var fixture = new TraceFixture(
            StartupCriticalPathTrace.Start(
                clock,
                new FakeUtcMicrosecondClock(2_000_000),
                entryTimestamp: 0,
                entryUnixMicroseconds: 1_000_000,
                exitAfterPublication: false),
            clock);
        CompleteValidTrace(fixture);
        using var writer = new TrackingWriter();
        var published = false;

        var exception = Record.Exception(
            () => published = fixture.Trace.TryPublishTerminal(writer));

        Assert.Null(exception);
        Assert.True(published);
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void Publish_WhenTimestampFrequencyChangesToZero_ShouldWriteFailureOnly()
    {
        var clock = new PositiveThenZeroFrequencyClock();
        var fixture = new TraceFixture(
            StartupCriticalPathTrace.Start(
                clock,
                new FakeUtcMicrosecondClock(2_000_000),
                entryTimestamp: 0,
                entryUnixMicroseconds: 1_000_000,
                exitAfterPublication: false),
            clock);
        CompleteValidTrace(fixture);
        using var writer = new TrackingWriter();
        var published = false;

        var exception = Record.Exception(
            () => published = fixture.Trace.TryPublishTerminal(writer));

        Assert.Null(exception);
        Assert.True(published);
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public async Task Publish_WhenConcurrentEventsAcquireLockOutOfTimestampOrder_ShouldValidateSnapshot()
    {
        var clock = new ControlledConcurrentClock();
        var wallClock = new FakeUtcMicrosecondClock(2_000_000);
        var fixture = new TraceFixture(
            StartupCriticalPathTrace.Start(clock, wallClock, 0, 1_000_000, false),
            clock);
        CompleteValidTrace(
            fixture,
            omitMilestone: StartupCriticalPathMilestone.StartupConstructBegin,
            secondOmittedMilestone: StartupCriticalPathMilestone.TitleConstructBegin);

        clock.PrepareBlockedTimestamp(11);
        var earlierTimestamp = Task.Run(() => fixture.Trace.RecordExactlyOnce(
            StartupCriticalPathMilestone.StartupConstructBegin));
        clock.WaitUntilBlocked();
        clock.Timestamp = 81;
        var laterTimestamp = Task.Run(() => fixture.Trace.RecordExactlyOnce(
            StartupCriticalPathMilestone.TitleConstructBegin));
        await laterTimestamp;
        clock.ReleaseBlockedTimestamp();
        await earlierTimestamp;
        using var writer = new TrackingWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(ExpectedSuccessLine + "\n", writer.ToString());
        Assert.True(clock.BlockedCallWasReleased);
    }

    [Fact]
    public void Publish_WhenWriterThrows_ShouldContainTelemetryFailure()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        using var writer = new ThrowingWriter();

        var exception = Record.Exception(() => fixture.Trace.TryPublishTerminal(writer));

        Assert.Null(exception);
        Assert.False(fixture.Trace.TryPublishTerminal(writer));
    }

    [Fact]
    public void Publish_WhenDisabled_ShouldWriteNothing()
    {
        using var writer = new TrackingWriter();

        var published =
            StartupTimingTrace.Disabled.CriticalPathTrace?.TryPublishTerminal(writer) ?? false;

        Assert.False(published);
        Assert.Equal(string.Empty, writer.ToString());
        Assert.Equal(0, writer.FlushCount);
    }

    private static TraceFixture CreateFixture()
    {
        var clock = new ManualMonotonicClock();
        var trace = StartupCriticalPathTrace.Start(
            clock,
            new FakeUtcMicrosecondClock(2_000_000),
            entryTimestamp: 0,
            entryUnixMicroseconds: 1_000_000,
            exitAfterPublication: false);
        return new TraceFixture(trace, clock);
    }

    private static void CompleteValidTrace(
        TraceFixture fixture,
        int invalidRecoverySpans = 0,
        StartupCriticalPathMilestone? omitMilestone = null,
        StartupCriticalPathMilestone? secondOmittedMilestone = null,
        IReadOnlyDictionary<StartupCriticalPathMilestone, long>? timestampOverrides = null)
    {
        void ExactlyOnce(StartupCriticalPathMilestone milestone, long timestamp)
        {
            if (omitMilestone == milestone || secondOmittedMilestone == milestone)
                return;

            At(
                fixture,
                timestampOverrides?.GetValueOrDefault(milestone, timestamp) ?? timestamp,
                () => fixture.Trace.RecordExactlyOnce(milestone));
        }

        void FirstPair(
            StartupCriticalPathMilestone begin,
            StartupCriticalPathMilestone end,
            long beginTimestamp,
            long endTimestamp)
        {
            if (omitMilestone != begin && secondOmittedMilestone != begin)
            {
                At(
                    fixture,
                    timestampOverrides?.GetValueOrDefault(begin, beginTimestamp) ?? beginTimestamp,
                    () => fixture.Trace.RecordFirstObservationBegin(begin, end));
            }

            if (omitMilestone != end && secondOmittedMilestone != end)
            {
                At(
                    fixture,
                    timestampOverrides?.GetValueOrDefault(end, endTimestamp) ?? endTimestamp,
                    () => fixture.Trace.RecordFirstObservationEnd(begin, end));
            }
        }

        ExactlyOnce(StartupCriticalPathMilestone.LoadContentComplete, 10);
        ExactlyOnce(StartupCriticalPathMilestone.StartupConstructBegin, 11);
        ExactlyOnce(StartupCriticalPathMilestone.StartupConstructEnd, 12);
        ExactlyOnce(StartupCriticalPathMilestone.StartupActivateBegin, 13);
        ExactlyOnce(StartupCriticalPathMilestone.StartupActivation, 14);
        ExactlyOnce(StartupCriticalPathMilestone.StartupActivateEnd, 15);
        ExactlyOnce(StartupCriticalPathMilestone.LoadContentReturn, 16);
        ExactlyOnce(StartupCriticalPathMilestone.BaseInitializeReturn, 17);
        ExactlyOnce(StartupCriticalPathMilestone.InputManagerBegin, 18);
        ExactlyOnce(StartupCriticalPathMilestone.InputManagerEnd, 19);
        ExactlyOnce(StartupCriticalPathMilestone.SavedBindingsBegin, 20);
        ExactlyOnce(StartupCriticalPathMilestone.SavedBindingsEnd, 21);
        ExactlyOnce(StartupCriticalPathMilestone.GraphicsInitializeBegin, 22);
        ExactlyOnce(StartupCriticalPathMilestone.GraphicsInitializeEnd, 23);
        ExactlyOnce(StartupCriticalPathMilestone.RenderTargetBegin, 24);
        ExactlyOnce(StartupCriticalPathMilestone.RenderTargetEnd, 25);
        ExactlyOnce(StartupCriticalPathMilestone.InitializeComplete, 30);

        At(fixture, 31, () => fixture.Trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd));
        fixture.Trace.IncrementStartupUpdate(0.005);
        At(fixture, 32, () => fixture.Trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd));
        At(fixture, 33, () => fixture.Trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.StartupFirstDrawBegin,
            StartupCriticalPathMilestone.StartupFirstDrawEnd));
        At(fixture, 34, () => fixture.Trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.StartupFirstDrawBegin,
            StartupCriticalPathMilestone.StartupFirstDrawEnd));
        fixture.Trace.IncrementCompletedStartupDraw();

        ExactlyOnce(StartupCriticalPathMilestone.DatabaseInvoke, 40);
        Aggregate(fixture, StartupCriticalPathAggregate.DatabaseServiceSetup, 40, 41);
        Aggregate(fixture, StartupCriticalPathAggregate.DatabaseCorruptionProbe, 41, 42);
        for (var index = 0; index < invalidRecoverySpans; index++)
            Aggregate(fixture, StartupCriticalPathAggregate.DatabaseInvalidRecovery, 42, 43);
        Aggregate(fixture, StartupCriticalPathAggregate.DatabaseEnsureCreated, 42, 43);
        Aggregate(fixture, StartupCriticalPathAggregate.DatabaseEncodingPragmas, 43, 44);
        Aggregate(fixture, StartupCriticalPathAggregate.DatabaseVersionWork, 44, 45);
        Aggregate(fixture, StartupCriticalPathAggregate.DatabaseSchemaEnsures, 45, 46);
        At(fixture, 41, () => fixture.Trace.RecordDatabaseTaskReturned(wasTerminal: false));
        ExactlyOnce(StartupCriticalPathMilestone.DatabaseTerminal, 50);
        FirstPair(
            StartupCriticalPathMilestone.DatabaseObserved,
            StartupCriticalPathMilestone.DatabaseObserved,
            51,
            51);

        ExactlyOnce(StartupCriticalPathMilestone.EnumerationInvoke, 60);
        At(fixture, 61, () => fixture.Trace.RecordEnumerationTaskReturned(wasTerminal: false));
        fixture.Trace.RecordEnumerationResult(CreateEnumerationResult());
        ExactlyOnce(StartupCriticalPathMilestone.EnumerationTerminal, 70);
        FirstPair(
            StartupCriticalPathMilestone.EnumerationObserved,
            StartupCriticalPathMilestone.EnumerationObserved,
            71,
            71);

        ExactlyOnce(StartupCriticalPathMilestone.SummaryRequest, 80);
        ExactlyOnce(StartupCriticalPathMilestone.TitleConstructBegin, 81);
        ExactlyOnce(StartupCriticalPathMilestone.TitleConstructEnd, 82);
        ExactlyOnce(StartupCriticalPathMilestone.TransitionStart, 83);
        fixture.Trace.IncrementTransitionUpdate(0.005);
        ExactlyOnce(StartupCriticalPathMilestone.TransitionComplete, 90);
        ExactlyOnce(StartupCriticalPathMilestone.StartupDeactivateBegin, 90);
        ExactlyOnce(StartupCriticalPathMilestone.StartupDeactivateEnd, 91);
        fixture.Trace.RecordTitleCompletionLookup(cacheHit: true);
        ExactlyOnce(StartupCriticalPathMilestone.TitleActivateBegin, 92);
        Aggregate(fixture, StartupCriticalPathAggregate.TitleGpuSetup, 92, 93);
        Aggregate(fixture, StartupCriticalPathAggregate.TitleBackground, 93, 94);
        Aggregate(fixture, StartupCriticalPathAggregate.TitleMenu, 94, 95);
        Aggregate(fixture, StartupCriticalPathAggregate.TitleFont, 95, 96);
        fixture.Trace.IncrementTitleSoundLoad();
        Aggregate(fixture, StartupCriticalPathAggregate.TitleCursorSound, 96, 97);
        fixture.Trace.IncrementTitleSoundLoad();
        Aggregate(fixture, StartupCriticalPathAggregate.TitleDecideSound, 97, 98);
        fixture.Trace.IncrementTitleSoundLoad();
        Aggregate(fixture, StartupCriticalPathAggregate.TitleGameStartSound, 98, 99);
        ExactlyOnce(StartupCriticalPathMilestone.TitleActivateEnd, 110);
        FirstPair(
            StartupCriticalPathMilestone.TitleFirstUpdateBegin,
            StartupCriticalPathMilestone.TitleFirstUpdateEnd,
            111,
            112);
        FirstPair(
            StartupCriticalPathMilestone.TitleStageDrawBegin,
            StartupCriticalPathMilestone.TitleStageDrawEnd,
            113,
            114);
        FirstPair(
            StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
            StartupCriticalPathMilestone.TitleBackbufferBlitEnd,
            115,
            116);
    }

    private static SongEnumerationResult CreateEnumerationResult()
    {
        var batch = new SongEnumerationBatch
        {
            ActiveRoots = Array.Empty<string>(),
            DiscoveredChartPaths = new HashSet<string>(),
            Candidates = new List<SongImportCandidate>(),
            RootNodes = new List<SongListNode>(),
            PendingSongs = new List<PendingSongNode>(),
            Errors = new List<SongEnumerationError>(),
            DiscoveryAndParsingDuration = TimeSpan.FromMilliseconds(2),
            IsComplete = true
        };
        var import = new SongBulkImportResult(
            new Dictionary<string, SongChart>(),
            Added: 0,
            Updated: 0,
            Preserved: 0,
            Skipped: 0,
            Conflicts: 0,
            StaleCharts: 0,
            StaleSongs: 0,
            PersistenceDuration: TimeSpan.FromMilliseconds(2),
            CleanupDuration: TimeSpan.FromMilliseconds(1));
        return new SongEnumerationResult(batch, import, TimeSpan.FromMilliseconds(1));
    }

    private static void Aggregate(
        TraceFixture fixture,
        StartupCriticalPathAggregate aggregate,
        long begin,
        long end)
    {
        At(fixture, begin, () => fixture.Trace.BeginAggregate(aggregate));
        At(fixture, end, () => fixture.Trace.EndAggregate(aggregate));
    }

    private static void At(TraceFixture fixture, long timestamp, Action action)
    {
        fixture.Clock.Timestamp = timestamp;
        action();
    }

    private sealed record TraceFixture(
        StartupCriticalPathTrace Trace,
        ManualMonotonicClock Clock);

    private class ManualMonotonicClock : IMonotonicClock
    {
        public virtual long TimestampFrequency => 1_000;
        public long Timestamp { get; set; }
        public virtual long GetTimestamp() => Timestamp;
    }

    private sealed class ThrowingFrequencyClock : ManualMonotonicClock
    {
        public override long TimestampFrequency =>
            throw new InvalidOperationException("frequency failure");
    }

    private sealed class PositiveThenZeroFrequencyClock : ManualMonotonicClock
    {
        private int _frequencyReadCount;

        public override long TimestampFrequency =>
            Interlocked.Increment(ref _frequencyReadCount) == 1 ? 1_000 : 0;
    }

    private sealed class ControlledConcurrentClock : ManualMonotonicClock
    {
        private readonly ManualResetEventSlim _blocked = new();
        private readonly ManualResetEventSlim _release = new();
        private long? _blockedTimestamp;

        public bool BlockedCallWasReleased { get; private set; }

        public void PrepareBlockedTimestamp(long timestamp)
        {
            _blockedTimestamp = timestamp;
        }

        public void WaitUntilBlocked() => _blocked.Wait(TimeSpan.FromSeconds(30));

        public void ReleaseBlockedTimestamp() => _release.Set();

        public override long GetTimestamp()
        {
            if (_blockedTimestamp is not { } timestamp)
                return base.GetTimestamp();

            _blockedTimestamp = null;
            _blocked.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException(
                    "ControlledConcurrentClock: release was not signaled within 30s");
            BlockedCallWasReleased = true;
            return timestamp;
        }
    }

    private sealed class FakeUtcMicrosecondClock : IUtcMicrosecondClock
    {
        private readonly Queue<long> _values;

        public FakeUtcMicrosecondClock(params long[] values)
        {
            _values = new Queue<long>(values);
        }

        public long GetUnixMicroseconds() => _values.Dequeue();
    }

    private sealed class ThrowingAfterFirstUtcMicrosecondClock : IUtcMicrosecondClock
    {
        private readonly long _firstValue;
        private int _callCount;

        public ThrowingAfterFirstUtcMicrosecondClock(long firstValue)
        {
            _firstValue = firstValue;
        }

        public long GetUnixMicroseconds()
        {
            if (Interlocked.Increment(ref _callCount) == 1)
                return _firstValue;

            throw new InvalidOperationException("late UTC read");
        }
    }

    private sealed class TrackingWriter : StringWriter
    {
        public TrackingWriter()
        {
            NewLine = "\n";
        }

        public int FlushCount { get; private set; }

        public override void Flush()
        {
            FlushCount++;
            base.Flush();
        }
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            throw new IOException("writer failure");
        }
    }

    #region Edge-Case Branch Coverage

    private static void SetTerminalOutcome(StartupCriticalPathTrace trace, string name)
    {
        var field = typeof(StartupCriticalPathTrace).GetField(
            "_terminalOutcome",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(trace, Enum.Parse(field.FieldType, name));
    }

    private static long[] AggregateTimestamps(StartupCriticalPathTrace trace) =>
        ReflectionHelpers.GetPrivateField<long[]>(trace, "_aggregateTimestampTicks")!;

    private static long[] AggregateCounts(StartupCriticalPathTrace trace) =>
        ReflectionHelpers.GetPrivateField<long[]>(trace, "_aggregateCounts")!;

    private static bool[] AggregateActive(StartupCriticalPathTrace trace) =>
        ReflectionHelpers.GetPrivateField<bool[]>(trace, "_aggregateActive")!;

    private static long[] Timestamps(StartupCriticalPathTrace trace) =>
        ReflectionHelpers.GetPrivateField<long[]>(trace, "_timestamps")!;

    private static void SetField(StartupCriticalPathTrace trace, string name, object? value)
    {
        var field = typeof(StartupCriticalPathTrace).GetField(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(trace, value);
    }

    [Fact]
    public void RecordExactlyOnce_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.RecordExactlyOnce(StartupCriticalPathMilestone.LoadContentComplete);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void RecordFirstObservationBegin_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void RecordFirstObservationEnd_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void BeginAggregate_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.BeginAggregate(StartupCriticalPathAggregate.DatabaseServiceSetup);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void EndAggregate_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.EndAggregate(StartupCriticalPathAggregate.DatabaseServiceSetup);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void RecordDatabaseTaskReturned_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.RecordDatabaseTaskReturned(wasTerminal: false);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void RecordEnumerationTaskReturned_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.RecordEnumerationTaskReturned(wasTerminal: false);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void RecordTitleCompletionLookup_WhenTerminalOutcomeFailure_ShouldReturnInsideLock()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.RecordTitleCompletionLookup(cacheHit: true);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void RecordFirstObservationEnd_WhenBeginNotRecorded_ShouldFailUnmatchedFirstObservation()
    {
        var fixture = CreateFixture();

        fixture.Trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=unmatched_first_observation", writer.ToString());
    }

    [Fact]
    public void RecordFirstObservationEnd_WhenWallClockThrows_ShouldFailWallClockFailure()
    {
        var clock = new ManualMonotonicClock();
        var trace = StartupCriticalPathTrace.Start(
            clock,
            new AlwaysThrowingUtcMicrosecondClock(),
            entryTimestamp: 0,
            entryUnixMicroseconds: 1_000_000,
            exitAfterPublication: false);

        trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
            StartupCriticalPathMilestone.TitleBackbufferBlitEnd);
        trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
            StartupCriticalPathMilestone.TitleBackbufferBlitEnd);

        using var writer = new TrackingWriter();
        Assert.True(trace.TryPublishTerminal(writer));
        Assert.Contains("error=wall_clock_failure", writer.ToString());
    }

    [Fact]
    public void RecordFirstObservationEnd_WhenTerminalSetDuringWallClockRead_ShouldReturnBeforeStoringWallTimestamp()
    {
        // The wall-clock read happens outside the first lock. If the terminal outcome becomes
        // non-Open between the first lock (which sets the pending flag) and the second lock
        // (which stores the wall timestamp), the second lock must bail out without storing.
        var clock = new ManualMonotonicClock();
        var wallClock = new TraceCorruptingWallClock();
        var trace = StartupCriticalPathTrace.Start(
            clock,
            wallClock,
            entryTimestamp: 0,
            entryUnixMicroseconds: 1_000_000,
            exitAfterPublication: false);
        wallClock.Trace = trace;

        trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
            StartupCriticalPathMilestone.TitleBackbufferBlitEnd);
        trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.TitleBackbufferBlitBegin,
            StartupCriticalPathMilestone.TitleBackbufferBlitEnd);

        using var writer = new TrackingWriter();
        Assert.True(trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
        // The wall timestamp must not have been stored because the terminal outcome was set
        // during the wall-clock read.
        Assert.DoesNotContain("title_backbuffer_unix_us=2000000", writer.ToString());
    }

    [Fact]
    public void EndAggregate_WhenNotBegun_ShouldFailUnmatchedAggregate()
    {
        var fixture = CreateFixture();

        fixture.Trace.EndAggregate(StartupCriticalPathAggregate.DatabaseServiceSetup);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=unmatched_aggregate", writer.ToString());
    }

    [Fact]
    public void EndAggregate_WhenElapsedNegative_ShouldFailNegativeAggregate()
    {
        var fixture = CreateFixture();
        At(fixture, 10, () => fixture.Trace.BeginAggregate(
            StartupCriticalPathAggregate.DatabaseServiceSetup));
        At(fixture, 5, () => fixture.Trace.EndAggregate(
            StartupCriticalPathAggregate.DatabaseServiceSetup));

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=negative_aggregate", writer.ToString());
    }

    [Fact]
    public void EndAggregate_WhenAccumulatedTicksOverflow_ShouldFailAggregateOverflow()
    {
        var fixture = CreateFixture();
        At(fixture, 10, () => fixture.Trace.BeginAggregate(
            StartupCriticalPathAggregate.DatabaseServiceSetup));
        AggregateTimestamps(fixture.Trace)[
            (int)StartupCriticalPathAggregate.DatabaseServiceSetup] = long.MaxValue;
        At(fixture, 11, () => fixture.Trace.EndAggregate(
            StartupCriticalPathAggregate.DatabaseServiceSetup));

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=aggregate_overflow", writer.ToString());
    }

    [Fact]
    public void IncrementStartupUpdate_WhenElapsedNotFinite_ShouldFailInvalidElapsedTime()
    {
        var fixture = CreateFixture();

        fixture.Trace.IncrementStartupUpdate(double.NaN);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_elapsed_time", writer.ToString());
    }

    [Fact]
    public void IncrementStartupUpdate_WhenTerminalClosed_ShouldReturnEarly()
    {
        var fixture = CreateFixture();
        fixture.Trace.Fail("boom", "elapsed_time");

        fixture.Trace.IncrementStartupUpdate(1.0);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=boom", writer.ToString());
    }

    [Fact]
    public void IncrementStartupUpdate_WhenElapsedOverflow_ShouldFailElapsedTimeOverflow()
    {
        var fixture = CreateFixture();

        fixture.Trace.IncrementStartupUpdate(double.MaxValue);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=elapsed_time_overflow", writer.ToString());
    }

    [Fact]
    public void IncrementStartupUpdate_WhenCounterOverflow_ShouldFailCounterOverflow()
    {
        var fixture = CreateFixture();
        SetField(fixture.Trace, "_startupUpdateCount", long.MaxValue);

        fixture.Trace.IncrementStartupUpdate(1.0);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=counter_overflow", writer.ToString());
    }

    [Fact]
    public void IncrementCompletedStartupDraw_WhenCounterOverflow_ShouldFailCounterOverflow()
    {
        var fixture = CreateFixture();
        SetField(fixture.Trace, "_startupDrawCount", long.MaxValue);

        fixture.Trace.IncrementCompletedStartupDraw();

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=counter_overflow", writer.ToString());
    }

    [Fact]
    public void IncrementTransitionUpdate_WhenElapsedNotFinite_ShouldFailInvalidElapsedTime()
    {
        var fixture = CreateFixture();

        fixture.Trace.IncrementTransitionUpdate(double.NaN);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_elapsed_time", writer.ToString());
    }

    [Fact]
    public void IncrementTitleSoundLoad_WhenTerminalOutcomeFailure_ShouldReturnEarly()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.IncrementTitleSoundLoad();

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void IncrementTitleSoundLoad_WhenCounterOverflow_ShouldFailCounterOverflow()
    {
        var fixture = CreateFixture();
        SetField(fixture.Trace, "_titleSoundLoadCount", long.MaxValue);

        fixture.Trace.IncrementTitleSoundLoad();

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=counter_overflow", writer.ToString());
    }

    [Fact]
    public void MarkTitleGameStartFallbackRan_WhenTerminalOutcomeFailure_ShouldReturnEarly()
    {
        var fixture = CreateFixture();
        SetTerminalOutcome(fixture.Trace, "Failure");

        fixture.Trace.MarkTitleGameStartFallbackRan();

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith("HPA192_CRITICAL_PATH_FAILURE ", writer.ToString());
    }

    [Fact]
    public void MarkTitleGameStartFallbackRan_WhenAlreadyRan_ShouldFailDuplicateFlag()
    {
        var fixture = CreateFixture();
        fixture.Trace.MarkTitleGameStartFallbackRan();
        fixture.Trace.MarkTitleGameStartFallbackRan();

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=duplicate_flag", writer.ToString());
    }

    [Fact]
    public void RecordDatabaseTaskReturned_WhenDuplicate_ShouldFailDuplicateMilestone()
    {
        var fixture = CreateFixture();
        fixture.Trace.RecordDatabaseTaskReturned(wasTerminal: false);
        fixture.Trace.RecordDatabaseTaskReturned(wasTerminal: false);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=duplicate_milestone", writer.ToString());
    }

    [Fact]
    public void RecordEnumerationTaskReturned_WhenDuplicate_ShouldFailDuplicateMilestone()
    {
        var fixture = CreateFixture();
        fixture.Trace.RecordEnumerationTaskReturned(wasTerminal: false);
        fixture.Trace.RecordEnumerationTaskReturned(wasTerminal: false);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=duplicate_milestone", writer.ToString());
    }

    [Fact]
    public void RecordEnumerationResult_WhenDuplicate_ShouldFailDuplicateEnumerationResult()
    {
        var fixture = CreateFixture();
        var result = CreateEnumerationResult();
        fixture.Trace.RecordEnumerationResult(result);
        fixture.Trace.RecordEnumerationResult(result);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=duplicate_enumeration_result", writer.ToString());
    }

    [Fact]
    public void RecordEnumerationResult_WhenNull_ShouldFailMissingEnumerationResult()
    {
        var fixture = CreateFixture();

        fixture.Trace.RecordEnumerationResult(null!);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=missing_enumeration_result", writer.ToString());
    }

    [Fact]
    public void RecordTitleCompletionLookup_WhenDuplicate_ShouldFailDuplicateTitleCompletionLookup()
    {
        var fixture = CreateFixture();
        fixture.Trace.RecordTitleCompletionLookup(cacheHit: true);
        fixture.Trace.RecordTitleCompletionLookup(cacheHit: true);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=duplicate_title_completion_lookup", writer.ToString());
    }

    [Fact]
    public void RecordTitleCompletionLookup_WhenCacheMiss_ShouldFailTitleCompletionCacheMiss()
    {
        var fixture = CreateFixture();

        fixture.Trace.RecordTitleCompletionLookup(cacheHit: false);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=title_completion_cache_miss", writer.ToString());
    }

    [Fact]
    public void RecordEnumerationTerminal_WhenOutcomeInvalid_ShouldThrowArgumentOutOfRangeException()
    {
        var fixture = CreateFixture();
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            observer.RecordEnumerationTerminal(
                CreateEnumerationResult(),
                (StartupOperationOutcome)999));
    }

    [Fact]
    public void RecordEnumerationTerminal_WhenFailureOutcome_ShouldFailEnumerationFailure()
    {
        var fixture = CreateFixture();
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;

        observer.RecordEnumerationTerminal(null, StartupOperationOutcome.Failure);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=enumeration_failure", writer.ToString());
    }

    [Fact]
    public void RecordEnumerationTerminal_WhenCancellationOutcome_ShouldFailEnumerationCancellation()
    {
        var fixture = CreateFixture();
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;

        observer.RecordEnumerationTerminal(null, StartupOperationOutcome.Cancellation);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("outcome=cancellation", writer.ToString());
        Assert.Contains("error=enumeration_cancellation", writer.ToString());
    }

    [Fact]
    public void BeginDatabaseSpan_WhenSpanInvalid_ShouldThrowArgumentOutOfRangeException()
    {
        var fixture = CreateFixture();
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            observer.BeginDatabaseSpan((StartupDatabaseTimingSpan)999));
    }

    [Fact]
    public void TryCaptureTimestamp_WhenClockThrows_ShouldFailClockFailure()
    {
        var clock = new ThrowingTimestampClock();
        var trace = StartupCriticalPathTrace.Start(
            clock,
            new FakeUtcMicrosecondClock(2_000_000),
            entryTimestamp: 0,
            entryUnixMicroseconds: 1_000_000,
            exitAfterPublication: false);

        trace.RecordExactlyOnce(StartupCriticalPathMilestone.LoadContentComplete);

        using var writer = new TrackingWriter();
        Assert.True(trace.TryPublishTerminal(writer));
        Assert.Contains("error=clock_failure", writer.ToString());
    }

    [Fact]
    public void TryPublishTerminal_WhenWriterNull_ShouldReturnFalse()
    {
        var fixture = CreateFixture();

        Assert.False(fixture.Trace.TryPublishTerminal(null!));
    }

    [Fact]
    public void Fail_WhenErrorEmpty_ShouldNormalizeToUnknownToken()
    {
        var fixture = CreateFixture();
        fixture.Trace.Fail("", "");

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            "HPA192_CRITICAL_PATH_FAILURE outcome=failure error=unknown last_milestone=unknown\n",
            writer.ToString());
    }

    [Fact]
    public void Publish_WhenTitleBackbufferUnixMicrosecondsOutOfRange_ShouldFailInvalidWallClock()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        SetField(fixture.Trace, "_titleBackbufferUnixMicroseconds", long.MaxValue);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_wall_clock", writer.ToString());
    }

        [Fact]
    public void Publish_WhenAggregateStillOpen_ShouldFailAggregateStillOpen()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        AggregateActive(fixture.Trace)[0] = true;

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=aggregate_still_open", writer.ToString());
    }

    [Fact]
    public void Publish_WhenAggregateDurationOutOfBounds_ShouldFailAggregateOutOfBounds()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        AggregateTimestamps(fixture.Trace)[0] = 300_001;

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=aggregate_out_of_bounds", writer.ToString());
    }

    [Fact]
    public void Publish_WhenStartupUpdateCountOutOfBounds_ShouldFailCounterOutOfBounds()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        SetField(fixture.Trace, "_startupUpdateCount", 100_001);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=counter_out_of_bounds", writer.ToString());
    }

    [Fact]
    public void Publish_WhenAggregateCountInvalid_ShouldFailInvalidAggregateCount()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        AggregateCounts(fixture.Trace)[
            (int)StartupCriticalPathAggregate.DatabaseServiceSetup] = 2;

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_aggregate_count", writer.ToString());
    }

    [Fact]
    public void Publish_WhenTitleCompletionCacheHitFalse_ShouldFailInvalidCountOrFlag()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        SetField(fixture.Trace, "_titleCompletionLookupCacheHit", false);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_count_or_flag", writer.ToString());
    }

    [Fact]
    public void Publish_WhenTitleSoundLoadCountInvalid_ShouldFailInvalidTitleSoundCount()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        SetField(fixture.Trace, "_titleSoundLoadCount", 99);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_title_sound_count", writer.ToString());
    }

    [Fact]
    public void Publish_WhenDbInitResidualNegative_ShouldFailNegativeResidual()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        AggregateTimestamps(fixture.Trace)[
            (int)StartupCriticalPathAggregate.DatabaseServiceSetup] = 50;

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=negative_residual", writer.ToString());
    }

    [Fact]
    public void Publish_WhenAggregateCountsArrayShort_ShouldFailValidationFailure()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        SetField(fixture.Trace, "_aggregateCounts", new long[1]);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=validation_failure", writer.ToString());
    }

    [Fact]
    public void Publish_WhenDatabaseTaskReturnAfterTerminalTimestamp_ShouldFailInvalidMilestoneOrder()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        var timestamps = Timestamps(fixture.Trace);
        timestamps[(int)StartupCriticalPathMilestone.DatabaseTaskReturn] = 50;
        timestamps[(int)StartupCriticalPathMilestone.DatabaseTerminal] = 45;

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_milestone_order", writer.ToString());
    }

    [Fact]
    public void Publish_WhenEnumerationTaskReturnAfterTerminalTimestamp_ShouldFailInvalidMilestoneOrder()
    {
        var fixture = CreateFixture();
        CompleteValidTrace(fixture);
        var timestamps = Timestamps(fixture.Trace);
        timestamps[(int)StartupCriticalPathMilestone.EnumerationTaskReturn] = 70;
        timestamps[(int)StartupCriticalPathMilestone.EnumerationTerminal] = 65;

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=invalid_milestone_order", writer.ToString());
    }

    private sealed class AlwaysThrowingUtcMicrosecondClock : IUtcMicrosecondClock
    {
        public long GetUnixMicroseconds() =>
            throw new InvalidOperationException("wall clock unavailable");
    }

    private sealed class TraceCorruptingWallClock : IUtcMicrosecondClock
    {
        public StartupCriticalPathTrace? Trace { get; set; }

        public long GetUnixMicroseconds()
        {
            if (Trace is { } trace)
                SetTerminalOutcome(trace, "Failure");
            return 2_000_000;
        }
    }

    private sealed class ThrowingTimestampClock : ManualMonotonicClock
    {
        public override long GetTimestamp() =>
            throw new InvalidOperationException("timestamp unavailable");
    }

    [Fact]
    public void RecordFirstObservationBegin_WhenTerminalClosed_ShouldReturnBeforeLock()
    {
        var fixture = CreateFixture();
        fixture.Trace.Fail("boom", "test");

        fixture.Trace.RecordFirstObservationBegin(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=boom", writer.ToString());
    }

    [Fact]
    public void RecordFirstObservationEnd_WhenTerminalClosed_ShouldReturnBeforeLock()
    {
        var fixture = CreateFixture();
        fixture.Trace.Fail("boom", "test");

        fixture.Trace.RecordFirstObservationEnd(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd);

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=boom", writer.ToString());
    }

    [Fact]
    public void RecordFirstObservationEnd_WhenNotTitleBackbufferBlitEnd_ShouldRecordMilestoneAndSkipWallClock()
    {
        var fixture = CreateFixture();
        var begin = StartupCriticalPathMilestone.StartupFirstUpdateBegin;
        var end = StartupCriticalPathMilestone.StartupFirstUpdateEnd;

        At(fixture, 100, () => fixture.Trace.RecordFirstObservationBegin(begin, end));
        At(fixture, 200, () => fixture.Trace.RecordFirstObservationEnd(begin, end));

        // The end milestone is recorded: its timestamp is captured and the
        // ended flag is set.
        var ended = ReflectionHelpers.GetPrivateField<bool[]>(fixture.Trace, "_firstObservationEnded")!;
        Assert.True(ended[(int)end]);
        Assert.Equal(200, Timestamps(fixture.Trace)[(int)end]);

        // Because this is not TitleBackbufferBlitEnd, the wall-clock capture
        // path is skipped: no pending flag is set and no wall timestamp stored.
        Assert.False(ReflectionHelpers.GetPrivateField<bool>(
            fixture.Trace, "_titleBackbufferWallClockPending"));
        Assert.Equal(0, ReflectionHelpers.GetPrivateField<long>(
            fixture.Trace, "_titleBackbufferUnixMicroseconds"));
    }

    [Fact]
    public void BeginDatabaseSpan_ViaObserver_ShouldMapToAggregate()
    {
        var fixture = CreateFixture();
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;

        // Each StartupDatabaseTimingSpan maps 1:1 to a StartupCriticalPathAggregate.
        // Replicating the mapping here verifies the observer routes every span to
        // its correct aggregate (not just any aggregate).
        var expectedAggregate = new Dictionary<StartupDatabaseTimingSpan, StartupCriticalPathAggregate>
        {
            [StartupDatabaseTimingSpan.ServiceSetup] = StartupCriticalPathAggregate.DatabaseServiceSetup,
            [StartupDatabaseTimingSpan.CorruptionProbe] = StartupCriticalPathAggregate.DatabaseCorruptionProbe,
            [StartupDatabaseTimingSpan.InvalidRecovery] = StartupCriticalPathAggregate.DatabaseInvalidRecovery,
            [StartupDatabaseTimingSpan.EnsureCreated] = StartupCriticalPathAggregate.DatabaseEnsureCreated,
            [StartupDatabaseTimingSpan.EncodingPragmas] = StartupCriticalPathAggregate.DatabaseEncodingPragmas,
            [StartupDatabaseTimingSpan.VersionWork] = StartupCriticalPathAggregate.DatabaseVersionWork,
            [StartupDatabaseTimingSpan.SchemaEnsures] = StartupCriticalPathAggregate.DatabaseSchemaEnsures,
        };

        var counts = AggregateCounts(fixture.Trace);
        var ticks = AggregateTimestamps(fixture.Trace);
        var active = AggregateActive(fixture.Trace);
        var beginTimestamps = ReflectionHelpers.GetPrivateField<long[]>(
            fixture.Trace, "_aggregateBeginTimestamps")!;

        var beginTs = 100;
        var endTs = 200;
        foreach (var span in Enum.GetValues<StartupDatabaseTimingSpan>())
        {
            var aggregate = expectedAggregate[span];
            var index = (int)aggregate;

            At(fixture, beginTs, () => observer.BeginDatabaseSpan(span));
            // The aggregate must be active between Begin and End.
            Assert.True(active[index]);
            Assert.Equal(beginTs, beginTimestamps[index]);

            At(fixture, endTs, () => observer.EndDatabaseSpan(span));

            // Each span increments its correct aggregate's count exactly once,
            // records the begin/end timing, and deactivates the span.
            Assert.Equal(1, counts[index]);
            Assert.Equal(endTs - beginTs, ticks[index]);
            Assert.False(active[index]);

            beginTs += 1000;
            endTs += 1000;
        }

        // Every database aggregate was exercised exactly once.
        Assert.All(
            Enum.GetValues<StartupDatabaseTimingSpan>(),
            span => Assert.Equal(1, counts[(int)expectedAggregate[span]]));
    }

    [Fact]
    public void EndDatabaseSpan_ViaObserver_ShouldMapToAggregate()
    {
        var fixture = CreateFixture();
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;
        At(fixture, 1, () => observer.BeginDatabaseSpan(
            StartupDatabaseTimingSpan.ServiceSetup));
        At(fixture, 2, () => observer.EndDatabaseSpan(
            StartupDatabaseTimingSpan.ServiceSetup));
        Assert.Equal(
            1,
            AggregateCounts(fixture.Trace)[
                (int)StartupCriticalPathAggregate.DatabaseServiceSetup]);
    }

    [Fact]
    public void RecordUnexpectedTableExistsPath_ViaObserver_ShouldFail()
    {
        var fixture = CreateFixture();
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;

        observer.RecordUnexpectedTableExistsPath();

        using var writer = new TrackingWriter();
        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("error=unexpected_table_exists_path", writer.ToString());
    }

    #endregion
}
