using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public class StartupSongLoadTimingObserverTests
{
    [Fact]
    public void TryBeginDatabaseSpan_WhenObserverThrows_ShouldNotPropagate()
    {
        IStartupSongLoadTimingObserver observer = new ThrowingObserver();

        var exception = Record.Exception(() =>
            observer.TryBeginDatabaseSpan(StartupDatabaseTimingSpan.EnsureCreated));

        Assert.Null(exception);
    }

    [Fact]
    public void TryEndDatabaseSpan_WhenObserverThrows_ShouldNotPropagate()
    {
        IStartupSongLoadTimingObserver observer = new ThrowingObserver();

        var exception = Record.Exception(() =>
            observer.TryEndDatabaseSpan(StartupDatabaseTimingSpan.EnsureCreated));

        Assert.Null(exception);
    }

    [Fact]
    public void TryRecordEnumerationTerminal_WhenObserverThrows_ShouldNotPropagate()
    {
        IStartupSongLoadTimingObserver observer = new ThrowingObserver();

        var exception = Record.Exception(() =>
            observer.TryRecordEnumerationTerminal(
                result: null,
                StartupOperationOutcome.Failure));

        Assert.Null(exception);
    }

    [Fact]
    public void TryRecordUnexpectedTableExistsPath_WhenObserverThrows_ShouldNotPropagate()
    {
        IStartupSongLoadTimingObserver observer = new ThrowingObserver();

        var exception = Record.Exception(
            observer.TryRecordUnexpectedTableExistsPath);

        Assert.Null(exception);
    }

    [Fact]
    public void Extensions_WhenObserverIsNull_ShouldBeNoOps()
    {
        IStartupSongLoadTimingObserver? observer = null;

        var exception = Record.Exception(() =>
        {
            observer.TryBeginDatabaseSpan(StartupDatabaseTimingSpan.CorruptionProbe);
            observer.TryEndDatabaseSpan(StartupDatabaseTimingSpan.CorruptionProbe);
            observer.TryRecordUnexpectedTableExistsPath();
            observer.TryRecordEnumerationTerminal(
                result: null,
                StartupOperationOutcome.Cancellation);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void CriticalTrace_WhenDatabaseSpansComplete_ShouldMapApprovedAggregates()
    {
        var fixture = CreateTraceFixture();
        CompleteValidTrace(
            fixture,
            databaseSpansViaObserver: true,
            invalidRecoverySpans: 1);
        using var writer = new StringWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Contains("db_service_setup_ms=1", writer.ToString());
        Assert.Contains("db_corruption_probe_ms=1", writer.ToString());
        Assert.Contains("db_invalid_recovery_count=1", writer.ToString());
        Assert.Contains("db_invalid_recovery_ms=1", writer.ToString());
        Assert.Contains("db_ensure_created_count=1", writer.ToString());
        Assert.Contains("db_ensure_created_ms=1", writer.ToString());
        Assert.Contains("db_encoding_pragmas_ms=1", writer.ToString());
        Assert.Contains("db_version_work_ms=1", writer.ToString());
        Assert.Contains("db_schema_ensures_ms=1", writer.ToString());
    }

    [Fact]
    public void CriticalTrace_WhenEnumerationSucceeds_ShouldRecordResultAndTerminalTimestamp()
    {
        var fixture = CreateTraceFixture();
        CompleteValidTrace(fixture, enumerationTerminalViaObserver: true);
        using var writer = new StringWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.StartsWith(
            "HPA192_CRITICAL_PATH outcome=success ",
            writer.ToString());
        Assert.Contains(
            "enumeration_terminal_from_entry_ms=70",
            writer.ToString());
        Assert.Contains("enumeration_unattributed_ms=4", writer.ToString());
    }

    [Fact]
    public void CriticalTrace_WhenEnumerationFails_ShouldRecordTerminalFailure()
    {
        var fixture = CreateTraceFixture();
        At(fixture, 70, () =>
            ((IStartupSongLoadTimingObserver)fixture.Trace)
                .RecordEnumerationTerminal(
                    result: null,
                    StartupOperationOutcome.Failure));
        using var writer = new StringWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            "HPA192_CRITICAL_PATH_FAILURE outcome=failure " +
            "error=enumeration_failure last_milestone=EnumerationTerminal" +
            Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void CriticalTrace_WhenEnumerationCancels_ShouldRecordTerminalCancellation()
    {
        var fixture = CreateTraceFixture();
        At(fixture, 70, () =>
            ((IStartupSongLoadTimingObserver)fixture.Trace)
                .RecordEnumerationTerminal(
                    result: null,
                    StartupOperationOutcome.Cancellation));
        using var writer = new StringWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            "HPA192_CRITICAL_PATH_FAILURE outcome=cancellation " +
            "error=enumeration_cancellation last_milestone=EnumerationTerminal" +
            Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void CriticalTrace_WhenEnumerationSuccessHasNoResult_ShouldRecordFailure()
    {
        var fixture = CreateTraceFixture();
        At(fixture, 70, () =>
            ((IStartupSongLoadTimingObserver)fixture.Trace)
                .RecordEnumerationTerminal(
                    result: null,
                    StartupOperationOutcome.Success));
        using var writer = new StringWriter();

        Assert.True(fixture.Trace.TryPublishTerminal(writer));
        Assert.Equal(
            "HPA192_CRITICAL_PATH_FAILURE outcome=failure " +
            "error=missing_enumeration_result last_milestone=enumeration_result" +
            Environment.NewLine,
            writer.ToString());
    }

    private static TraceFixture CreateTraceFixture()
    {
        var clock = new ManualMonotonicClock();
        var trace = StartupCriticalPathTrace.Start(
            clock,
            new FixedUtcMicrosecondClock(),
            entryTimestamp: 0,
            entryUnixMicroseconds: 1_000_000,
            exitAfterPublication: false);
        return new TraceFixture(trace, clock);
    }

    private static void CompleteValidTrace(
        TraceFixture fixture,
        bool databaseSpansViaObserver = false,
        bool enumerationTerminalViaObserver = false,
        int invalidRecoverySpans = 0)
    {
        void ExactlyOnce(
            StartupCriticalPathMilestone milestone,
            long timestamp) =>
            At(
                fixture,
                timestamp,
                () => fixture.Trace.RecordExactlyOnce(milestone));

        void FirstPair(
            StartupCriticalPathMilestone begin,
            StartupCriticalPathMilestone end,
            long beginTimestamp,
            long endTimestamp)
        {
            At(
                fixture,
                beginTimestamp,
                () => fixture.Trace.RecordFirstObservationBegin(begin, end));
            At(
                fixture,
                endTimestamp,
                () => fixture.Trace.RecordFirstObservationEnd(begin, end));
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

        FirstPair(
            StartupCriticalPathMilestone.StartupFirstUpdateBegin,
            StartupCriticalPathMilestone.StartupFirstUpdateEnd,
            31,
            32);
        fixture.Trace.IncrementStartupUpdate(0.005);
        FirstPair(
            StartupCriticalPathMilestone.StartupFirstDrawBegin,
            StartupCriticalPathMilestone.StartupFirstDrawEnd,
            33,
            34);
        fixture.Trace.IncrementCompletedStartupDraw();

        ExactlyOnce(StartupCriticalPathMilestone.DatabaseInvoke, 40);
        if (databaseSpansViaObserver)
        {
            ObserverSpan(fixture, StartupDatabaseTimingSpan.ServiceSetup, 40, 41);
            ObserverSpan(fixture, StartupDatabaseTimingSpan.CorruptionProbe, 41, 42);
            for (var index = 0; index < invalidRecoverySpans; index++)
            {
                ObserverSpan(
                    fixture,
                    StartupDatabaseTimingSpan.InvalidRecovery,
                    42,
                    43);
            }
            ObserverSpan(fixture, StartupDatabaseTimingSpan.EnsureCreated, 42, 43);
            ObserverSpan(fixture, StartupDatabaseTimingSpan.EncodingPragmas, 43, 44);
            ObserverSpan(fixture, StartupDatabaseTimingSpan.VersionWork, 44, 45);
            ObserverSpan(fixture, StartupDatabaseTimingSpan.SchemaEnsures, 45, 46);
        }
        else
        {
            Aggregate(
                fixture,
                StartupCriticalPathAggregate.DatabaseServiceSetup,
                40,
                41);
            Aggregate(
                fixture,
                StartupCriticalPathAggregate.DatabaseCorruptionProbe,
                41,
                42);
            Aggregate(
                fixture,
                StartupCriticalPathAggregate.DatabaseEnsureCreated,
                42,
                43);
            Aggregate(
                fixture,
                StartupCriticalPathAggregate.DatabaseEncodingPragmas,
                43,
                44);
            Aggregate(
                fixture,
                StartupCriticalPathAggregate.DatabaseVersionWork,
                44,
                45);
            Aggregate(
                fixture,
                StartupCriticalPathAggregate.DatabaseSchemaEnsures,
                45,
                46);
        }
        At(
            fixture,
            41,
            () => fixture.Trace.RecordDatabaseTaskReturned(wasTerminal: false));
        ExactlyOnce(StartupCriticalPathMilestone.DatabaseTerminal, 50);
        FirstPair(
            StartupCriticalPathMilestone.DatabaseObserved,
            StartupCriticalPathMilestone.DatabaseObserved,
            51,
            51);

        ExactlyOnce(StartupCriticalPathMilestone.EnumerationInvoke, 60);
        At(
            fixture,
            61,
            () => fixture.Trace.RecordEnumerationTaskReturned(wasTerminal: false));
        if (enumerationTerminalViaObserver)
        {
            At(
                fixture,
                70,
                () => ((IStartupSongLoadTimingObserver)fixture.Trace)
                    .RecordEnumerationTerminal(
                        CreateEnumerationResult(),
                        StartupOperationOutcome.Success));
        }
        else
        {
            fixture.Trace.RecordEnumerationResult(CreateEnumerationResult());
            ExactlyOnce(StartupCriticalPathMilestone.EnumerationTerminal, 70);
        }
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
        Aggregate(
            fixture,
            StartupCriticalPathAggregate.TitleGameStartSound,
            98,
            99);
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
        return new SongEnumerationResult(
            batch,
            import,
            TimeSpan.FromMilliseconds(1));
    }

    private static void ObserverSpan(
        TraceFixture fixture,
        StartupDatabaseTimingSpan span,
        long begin,
        long end)
    {
        var observer = (IStartupSongLoadTimingObserver)fixture.Trace;
        At(fixture, begin, () => observer.BeginDatabaseSpan(span));
        At(fixture, end, () => observer.EndDatabaseSpan(span));
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

    private static void At(
        TraceFixture fixture,
        long timestamp,
        Action action)
    {
        fixture.Clock.Timestamp = timestamp;
        action();
    }

    private sealed record TraceFixture(
        StartupCriticalPathTrace Trace,
        ManualMonotonicClock Clock);

    private sealed class ManualMonotonicClock : IMonotonicClock
    {
        public long TimestampFrequency => 1_000;
        public long Timestamp { get; set; }
        public long GetTimestamp() => Timestamp;
    }

    private sealed class FixedUtcMicrosecondClock : IUtcMicrosecondClock
    {
        public long GetUnixMicroseconds() => 2_000_000;
    }

    private sealed class ThrowingObserver : IStartupSongLoadTimingObserver
    {
        public void BeginDatabaseSpan(StartupDatabaseTimingSpan span) =>
            throw new InvalidOperationException("observer begin failure");

        public void EndDatabaseSpan(StartupDatabaseTimingSpan span) =>
            throw new InvalidOperationException("observer end failure");

        public void RecordUnexpectedTableExistsPath() =>
            throw new InvalidOperationException("observer table-exists failure");

        public void RecordEnumerationTerminal(
            SongEnumerationResult? result,
            StartupOperationOutcome outcome) =>
            throw new InvalidOperationException("observer terminal failure");
    }
}
