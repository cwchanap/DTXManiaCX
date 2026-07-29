using System.Collections.Generic;
using System.Linq;
using System.Text;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;

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
        public long TimestampFrequency => 1_000;
        public long Timestamp { get; set; }
        public virtual long GetTimestamp() => Timestamp;
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

        public void WaitUntilBlocked() => _blocked.Wait();

        public void ReleaseBlockedTimestamp() => _release.Set();

        public override long GetTimestamp()
        {
            if (_blockedTimestamp is not { } timestamp)
                return base.GetTimestamp();

            _blockedTimestamp = null;
            _blocked.Set();
            _release.Wait();
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
}
