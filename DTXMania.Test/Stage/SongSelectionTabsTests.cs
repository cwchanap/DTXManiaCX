#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.SongSelection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DTXMania.Test.TestData;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage
{
    // HPA-802 task 3: the song-selection tabs owner (active tab, flat-tab caches,
    // empty/error states, refresh protocol, and the whole bookmark write path) is
    // exercised with constructor-built objects — a real game-owned SongLibrary, the
    // real intake, a real StageBackgroundWorkScope drained/retired by the test, and
    // narrow constructor loader/writer seams gated by deterministic TCS tasks. No
    // stage internals, no sleeps, and no background-queue reflection in the race
    // tests: interleavings are pinned by awaiting each controlled load/write before
    // issuing the next request, and the scope drain applies worker terminals exactly
    // as the game loop does.
    [Trait("Category", "Unit")]
    public class SongSelectionTabsTests
    {
        #region Fixture

        // One controlled loader invocation. Tests await Started to pin the request
        // order (only one request outstanding at a time), then complete Result to
        // release the worker's terminal.
        public sealed class ControlledLoad
        {
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<List<SongListNode>> Result { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Counts every loader invocation and records them in request order so tests can
        // assert exactly how many queries ran (the deferred-wait contract) and drive
        // their completion.
        private sealed class ControlledLoader
        {
            private readonly object _gate = new();
            private readonly List<ControlledLoad> _loads = new();
            private int _invoked;
            private bool _releasedAll;

            public int InvokedCount => Volatile.Read(ref _invoked);

            public ControlledLoad LoadAt(int index)
            {
                lock (_gate)
                    return _loads[index];
            }

            // Deterministic registration barrier: waits until the scope worker has
            // actually invoked the loader for this index before the test drives it
            // (no sleeps — the worker registers within microseconds of the request).
            public ControlledLoad LoadWhenRegistered(int index)
            {
                Assert.True(
                    SpinWait.SpinUntil(
                        () => { lock (_gate) return _loads.Count > index; },
                        TimeSpan.FromSeconds(10)),
                    $"loader invocation {index} was never registered");
                return LoadAt(index);
            }

            public Func<Task<List<SongListNode>>> Loader() => () =>
            {
                var load = new ControlledLoad();
                bool released;
                lock (_gate)
                {
                    _loads.Add(load);
                    released = _releasedAll;
                }
                Interlocked.Increment(ref _invoked);
                load.Started.TrySetResult(true);
                if (released)
                    load.Result.TrySetResult(new List<SongListNode>());
                return load.Result.Task;
            };

            // Failure-path cleanup: settle every registered load (and every later one)
            // so a worker blocked in RunTabLoad can finish when an assertion fails
            // before the test drives its loaders. The empty result is irrelevant: the
            // test already failed and scope retirement drops the terminal.
            public void ReleaseAll()
            {
                ControlledLoad[] loads;
                lock (_gate)
                {
                    _releasedAll = true;
                    loads = _loads.ToArray();
                }
                foreach (var load in loads)
                    load.Result.TrySetResult(new List<SongListNode>());
            }
        }

        // One controlled bookmark write through the writer seam. When a real library is
        // given, the REAL SetBookmarkAsync runs (gated behind the invocation's gate when
        // the song id is listed); otherwise the write is a recording-only gated task so
        // controlled loaders can derive their rows from writes that actually settled.
        private sealed class ControlledWrite
        {
            public int SongId;
            public bool State;
            public TaskCompletionSource<bool> Gate { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Task = Task.CompletedTask;
        }

        private sealed class ControlledWriter
        {
            private readonly object _gate = new();
            private readonly List<ControlledWrite> _writes = new();
            private readonly Func<SongLibrary, int, bool, Task>? _override;

            private ControlledWriter(Func<SongLibrary, int, bool, Task> overrideBody)
            {
                _override = overrideBody;
            }

            public ControlledWriter()
            {
            }

            public static ControlledWriter Overriding(Func<SongLibrary, int, bool, Task> body) => new(body);

            public IReadOnlyList<ControlledWrite> Writes
            {
                get { lock (_gate) return _writes.ToArray(); }
            }

            public int InvokedCount => Writes.Count;

            // Deterministic registration barrier: waits until the writer seam has actually
            // been invoked for write `index` (the invocation happens inside a pool
            // continuation that chains behind the prior per-song write, so it is never
            // synchronous with the toggle call).
            public ControlledWrite AwaitWriteRegistered(int index)
            {
                Assert.True(
                    SpinWait.SpinUntil(
                        () => { lock (_gate) return _writes.Count > index; },
                        TimeSpan.FromSeconds(10)),
                    $"writer invocation {index} was never registered");
                lock (_gate)
                    return _writes[index];
            }

            public void AwaitWriteInvoked(int count)
            {
                Assert.True(
                    SpinWait.SpinUntil(
                        () => { lock (_gate) return _writes.Count >= count; },
                        TimeSpan.FromSeconds(10)),
                    $"only {Writes.Count} of {count} expected writer invocations registered");
            }

            public Func<SongLibrary, int, bool, Task> Writer() => (library, songId, state) =>
            {
                if (_override != null)
                    return _override(library, songId, state);

                var write = new ControlledWrite { SongId = songId, State = state };
                // Recording-only write: completes exactly when the gate releases. The
                // REAL gated task is assigned BEFORE the write is published under the
                // lock, so a test that retrieves the write can never await the default
                // CompletedTask placeholder instead of its settlement barrier.
                write.Task = write.Gate.Task.ContinueWith(_ => true, TaskScheduler.Default);
                lock (_gate)
                    _writes.Add(write);
                return write.Task;
            };
        }

        private static SongLibrary CreateLibrary()
        {
            var dbPath = Path.Combine(
                Path.GetTempPath(), "DTXManiaCX_Tests", "SongSelectionTabsTests",
                $"{Guid.NewGuid():N}.db");
            return new SongLibrary(new SongDatabase(dbPath));
        }

        private static readonly ILogger NoLogger = NullLogger.Instance;

        // Counts the fault-rollback continuation's warnings (thread-safe). The
        // continuation performs its handoff — scope.Post for a live origin scope, the
        // retired-scope drop otherwise — STRICTLY BEFORE it logs its warning (program
        // order inside the continuation), so this count is the post-handoff signal the
        // rollback tests await before draining/asserting. It deliberately excludes the
        // disposal drain's own warning for the same faulted write, which would
        // otherwise satisfy the wait before the rollback continuation ran. No new
        // production telemetry.
        private sealed class CapturingLogger : ILogger
        {
            private int _rollbackWarnings;

            public int RollbackWarnings => Volatile.Read(ref _rollbackWarnings);

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning
                    && formatter(state, exception).StartsWith(
                        "Bookmark save failed", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _rollbackWarnings);
                }
            }
        }

        private sealed class GatedRealWriterHarness
        {
            private readonly TaskCompletionSource<bool> _gate =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _skipDbWork;
            private int _releasedRealWrite;
            private Task? _writeTask;

            public Task Started => _started.Task;

            public Func<SongLibrary, int, bool, Task> Writer() => (lib, id, state) =>
            {
                _started.TrySetResult(true);
                var task = _gate.Task.ContinueWith(
                    _ => Volatile.Read(ref _skipDbWork) != 0
                        ? Task.CompletedTask
                        : lib.Queries.SetBookmarkAsync(id, state),
                    TaskScheduler.Default).Unwrap();
                Volatile.Write(ref _writeTask, task);
                return task;
            };

            public void ReleaseRealWrite()
            {
                Volatile.Write(ref _releasedRealWrite, 1);
                _gate.TrySetResult(true);
            }

            public void Cleanup(
                SongSelectionTabs tabs,
                CapturingLogger logger,
                params StageBackgroundWorkScope?[] scopes)
            {
                if (Volatile.Read(ref _releasedRealWrite) == 0)
                    Volatile.Write(ref _skipDbWork, 1);
                _gate.TrySetResult(true);

                var writeTask = Volatile.Read(ref _writeTask);
                if (writeTask != null)
                {
                    try { writeTask.Wait(TimeSpan.FromSeconds(5)); }
                    catch { /* ignore fault from purged db */ }
                }
                tabs.WaitForPendingWritesAtDisposal();

                if (Volatile.Read(ref _releasedRealWrite) != 0 && writeTask is { IsFaulted: true } or { IsCanceled: true })
                    SpinWait.SpinUntil(() => logger.RollbackWarnings >= 1, TimeSpan.FromSeconds(5));

                foreach (var scope in scopes)
                    scope?.Retire();
            }
        }

        // SUBMISSION-RECORDER SEAM FIXTURE (fix round 2 follow-up): the constructor
        // I/O-execution seam WRAPS the REAL StageBackgroundWorkScope.Run query
        // submission. Each submission ("ISSUE") is recorded synchronously on the
        // update thread AT LAUNCH — before any worker can run — so an assert on the
        // test thread immediately after a drain has a happens-before edge to every
        // submission issued so far: zero records at a boundary proves no query was
        // ISSUED there, independent of adversarial worker/loader scheduling (the
        // old weakness: a wrongly-issued query could register only later, after the
        // final assert). HoldWorkers gates the ACTUAL worker behind a release gate
        // so a mutant fails at the submission boundary even when its loader can
        // never run; ReleaseWorkers lets the real workers execute for the
        // distinctive-rows proof. Delegates to the real scope.Run — same worker,
        // same terminals. Test/I-O seam only; no production telemetry.
        private sealed class SubmissionRecorder
        {
            private int _submitted;
            private TaskCompletionSource<bool>? _release;
            private volatile bool _workersHeld;

            public int Submissions => Volatile.Read(ref _submitted);

            public SongSelectionTabs.SubmitTabLoadQuery Seam() => (scope, work, onSuccess, onError) =>
            {
                Interlocked.Increment(ref _submitted); // ISSUE: recorded before any worker can run
                if (_workersHeld)
                {
                    var held = work;
                    work = token =>
                    {
                        // Gate the ACTUAL worker; the scope's real Run schedules and
                        // executes it exactly as production would once the gate opens.
                        // Fail closed: a mutant whose worker can never run must fail the
                        // test, not hang silently until the timeout passes.
                        var release = _release;
                        if (release != null && !release.Task.Wait(TimeSpan.FromSeconds(10)))
                            throw new TimeoutException("held tab-load worker was never released");
                        return held(token);
                    };
                }

                return scope.Run(work, onSuccess, onError);
            };

            // Hold the actual worker of every subsequent submission (launches are
            // still recorded synchronously; loaders cannot run until release).
            public void HoldWorkers()
            {
                _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _workersHeld = true;
            }

            public void ReleaseWorkers()
            {
                _workersHeld = false;
                _release?.TrySetResult(true);
            }
        }

        private static SongSelectionTabs CreateTabs(
            SongLibrary library,
            StageBackgroundWorkScope? scope,
            Func<Task<List<SongListNode>>>? loadRecentPlaysAsync = null,
            Func<Task<List<SongListNode>>>? loadBookmarkedNodesAsync = null,
            Func<SongLibrary, int, bool, Task>? setBookmarkAsync = null,
            ILogger? logger = null,
            SongSelectionTabs.SubmitTabLoadQuery? submitTabLoadQuery = null)
        {
            return new SongSelectionTabs(
                () => library,
                new SongSelectionLibraryIntake(() => library),
                () => scope,
                logger ?? NoLogger,
                loadRecentPlaysAsync,
                loadBookmarkedNodesAsync,
                setBookmarkAsync,
                submitTabLoadQuery);
        }

        private static SongSelectionTabs CreateTabs(
            Func<SongLibrary> getLibrary,
            SongSelectionLibraryIntake intake,
            Func<StageBackgroundWorkScope?> getScope,
            Func<Task<List<SongListNode>>>? loadRecentPlaysAsync = null,
            Func<Task<List<SongListNode>>>? loadBookmarkedNodesAsync = null,
            Func<SongLibrary, int, bool, Task>? setBookmarkAsync = null,
            ILogger? logger = null,
            SongSelectionTabs.SubmitTabLoadQuery? submitTabLoadQuery = null)
        {
            return new SongSelectionTabs(
                getLibrary,
                intake,
                getScope,
                logger ?? NoLogger,
                loadRecentPlaysAsync,
                loadBookmarkedNodesAsync,
                setBookmarkAsync,
                submitTabLoadQuery);
        }

        // Applies queued scope terminals on the calling thread (as the game loop does)
        // until the condition holds. Spin-based: no sleeps, no queue reflection.
        private static void DrainUntil(
            StageBackgroundWorkScope scope,
            Func<bool> condition,
            string because = "")
        {
            Assert.True(
                SpinWait.SpinUntil(() => { scope.Drain(); return condition(); }, TimeSpan.FromSeconds(10)),
                because.Length > 0 ? because : "the expected scope-drain condition was not reached in time");
        }

        // SINGLE-STEP COMPLETION BARRIER (HPA-802): the worker task returned by
        // BeginRecentPlaysLoad / RequestBookmarksRefresh / SwitchToTab completes only
        // AFTER that one step's terminal has been enqueued into the scope (a retired
        // scope completes it WITHOUT enqueueing — which is why null-ness of the task
        // and enqueue-ness are asserted, not assumed). A single scope.Drain() that
        // follows the await therefore provably processes that step's terminal (FIFO,
        // ≤64 per drain), so the negative assertions below fail even under adversarial
        // scheduling. It is a barrier for ONE worker step, never for a whole refresh:
        // a wait step's re-read and the eventual query enqueue their own terminals.
        private static async Task AwaitEnqueuedThenProcessAsync(
            Task? workerTask,
            StageBackgroundWorkScope scope,
            string because = "")
        {
            Assert.True(
                workerTask != null,
                because.Length > 0
                    ? because
                    : "no worker step was issued: a null task is not a completion barrier");
            await workerTask!.WaitAsync(TimeSpan.FromSeconds(10));
            scope.Drain();
        }

        // ROLLBACK HANDOFF SIGNAL: the fault-rollback continuation posts into the
        // origin scope (or runs the retired-scope drop) BEFORE logging its warning.
        // Waiting for the warning therefore proves the handoff already happened;
        // awaiting the antecedent write alone does NOT — the continuation is scheduled
        // asynchronously and may run at any later time.
        private static void AwaitRollbackHandoff(
            CapturingLogger logger,
            int expectedWarnings,
            string because)
        {
            Assert.True(
                SpinWait.SpinUntil(() => logger.RollbackWarnings >= expectedWarnings, TimeSpan.FromSeconds(10)),
                because);
        }

        private static SongListNode Score(string title, int songId, bool bookmarked = false, string chartPath = "")
        {
            var chart = new SongChart { FilePath = string.IsNullOrEmpty(chartPath) ? $"/c/{title}.dtx" : chartPath };
            var song = new SongEntity
            {
                Id = songId,
                Title = title,
                IsBookmarked = bookmarked,
                Charts = new List<SongChart> { chart }
            };
            chart.Song = song;
            chart.SongId = songId;
            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                DatabaseSongId = songId,
                DatabaseSong = song,
                DatabaseChart = chart,
            };
        }

        private static SongLibrarySnapshot Snapshot(
            long version,
            IReadOnlyList<SongListNode> roots,
            IReadOnlyList<string> activeRoots) =>
            new(version, roots, activeRoots, enumeratedFileCount: roots.Count, discoveredScoreCount: roots.Count);

        private static List<SongListNode> TakeLibraryRoots(SongLibrary library) =>
            GetPrivateField<List<SongListNode>>(library, "_rootSongs")!.ToList();

        private static void RestoreLibraryRoots(SongLibrary library, List<SongListNode> saved)
        {
            var roots = GetPrivateField<List<SongListNode>>(library, "_rootSongs")!;
            roots.Clear();
            roots.AddRange(saved);
        }

        #endregion

        #region Row projection and distinct empty/error flags

        [Fact]
        public async Task BuildRecentPlaysRows_WithCachedNodes_ShouldReturnRowsAndClearEmptyFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());

            tabs.BeginRecentPlaysLoad();
            var load = loader.LoadWhenRegistered(0);
            await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            load.Result.SetResult(new List<SongListNode> { Score("R1", 1), Score("R2", 2) });

            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 2 });

            var rows = tabs.BuildRecentPlaysRows();

            Assert.Equal(2, rows.Count);
            Assert.Equal("R1", rows[0].Title);
            Assert.False(tabs.ShowEmptyRecentMessage);
            Assert.False(tabs.RecentPlaysLoadFailed);
            scope.Retire();
        }

        [Fact]
        public async Task BuildRecentPlaysRows_WhenCacheEmpty_ShouldSetEmptyFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());

            tabs.BeginRecentPlaysLoad();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>());
            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 0 });

            var rows = tabs.BuildRecentPlaysRows();

            Assert.Empty(rows);
            Assert.True(tabs.ShowEmptyRecentMessage);
            scope.Retire();
        }

        [Fact]
        public void BuildRecentPlaysRows_WhenCacheNeverLoaded_ShouldNotShowEmpty()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope);

            var rows = tabs.BuildRecentPlaysRows();

            Assert.Empty(rows);
            Assert.False(tabs.ShowEmptyRecentMessage);
            scope.Retire();
        }

        [Fact]
        public void BuildRecentPlaysRows_WhenLoadFailed_ShouldNotSetEmptyFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: FaultingLoader());

            tabs.BeginRecentPlaysLoad();
            DrainUntil(scope, () => tabs.RecentPlaysLoadFailed);

            var rows = tabs.BuildRecentPlaysRows();

            Assert.Empty(rows);
            // A failed load gets its own distinct message; it must not look like
            // "no plays yet".
            Assert.False(tabs.ShowEmptyRecentMessage);
            Assert.True(tabs.RecentPlaysLoadFailed);
            scope.Retire();
        }

        [Fact]
        public async Task BuildRecentPlaysRows_ShouldLabelSpeedVariantRows()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());

            var node = Score("Recent Song", 5);
            node.RecentPlaySpeedPercent = 75;
            tabs.BeginRecentPlaysLoad();
            var load = loader.LoadWhenRegistered(0);
            await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            load.Result.SetResult(new List<SongListNode> { node });

            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });

            var rows = tabs.BuildRecentPlaysRows();

            Assert.Equal("Recent Song [0.75x]", Assert.Single(rows).Title);
            scope.Retire();
        }

        [Fact]
        public void BuildBookmarksRows_WhenCacheNeverLoaded_ShouldNotShowEmptyMessage()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope);

            var rows = tabs.BuildBookmarksRows();

            Assert.Empty(rows);
            Assert.False(tabs.ShowEmptyBookmarksMessage);
            scope.Retire();
        }

        [Fact]
        public async Task BuildBookmarksRows_WithCachedNodes_ShouldReturnRowsAndClearEmptyFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader.Loader());

            tabs.RequestBookmarksRefresh();
            var load = loader.LoadWhenRegistered(0);
            await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            load.Result.SetResult(new List<SongListNode> { Score("B1", 1), Score("B2", 2) });

            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 2 });

            var rows = tabs.BuildBookmarksRows();

            Assert.Equal(2, rows.Count);
            Assert.False(tabs.ShowEmptyBookmarksMessage);
            Assert.False(tabs.BookmarksLoadFailed);
            scope.Retire();
        }

        [Fact]
        public async Task BuildBookmarksRows_WhenCacheEmpty_ShouldSetEmptyFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader.Loader());

            tabs.RequestBookmarksRefresh();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>());
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 0 });

            var rows = tabs.BuildBookmarksRows();

            Assert.Empty(rows);
            Assert.True(tabs.ShowEmptyBookmarksMessage);
            scope.Retire();
        }

        [Fact]
        public void BuildBookmarksRows_WhenLoadFailed_ShouldNotSetEmptyFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: FaultingLoader());

            tabs.RequestBookmarksRefresh();
            DrainUntil(scope, () => tabs.BookmarksLoadFailed);

            var rows = tabs.BuildBookmarksRows();

            Assert.Empty(rows);
            Assert.False(tabs.ShowEmptyBookmarksMessage);
            Assert.True(tabs.BookmarksLoadFailed);
            scope.Retire();
        }

        [Fact]
        public void BuildRecentPlaysRows_WhenCachedRowsOutsideActiveRoots_ShouldHideThemWithoutDiscardingCache()
        {
            var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var loader = new ControlledLoader();
            var tabs = CreateTabs(
                () => library, intake, () => scope,
                loadRecentPlaysAsync: loader.Loader());
            var active = Score("Active", 1, chartPath: "/library/active/active.dtx");
            var inactive = Score("Inactive", 2, chartPath: "/library/inactive/inactive.dtx");

            intake.Install(Snapshot(16, new[] { active }, new[] { "/library/active" }));
            tabs.BeginRecentPlaysLoad();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { active, inactive });
            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 2 });

            var rows = tabs.BuildRecentPlaysRows();

            Assert.Single(rows);
            Assert.Equal("Active", rows[0].Title);
            Assert.Equal(2, tabs.RecentPlayNodes!.Count); // cache intact
            scope.Retire();
            library.Dispose();
        }

        [Fact]
        public void BuildBookmarksRows_WhenCachedRowsOutsideActiveRoots_ShouldHideThemWithoutDiscardingCache()
        {
            var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var loader = new ControlledLoader();
            var tabs = CreateTabs(
                () => library, intake, () => scope,
                loadBookmarkedNodesAsync: loader.Loader());
            var active = Score("Active", 1, chartPath: "/library/active/active.dtx");
            var inactive = Score("Inactive", 2, chartPath: "/library/inactive/inactive.dtx");

            intake.Install(Snapshot(15, new[] { active }, new[] { "/library/active" }));
            tabs.RequestBookmarksRefresh();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { active, inactive });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 2 });

            var rows = tabs.BuildBookmarksRows();

            Assert.Equal(new[] { "Active" }, rows.Select(node => node.Title));
            Assert.Equal(2, tabs.BookmarkNodes!.Count);
            scope.Retire();
            library.Dispose();
        }

        [Fact]
        public void BuildBookmarksRows_WhenRootIsReadded_ShouldMakeTheRetainedCachedRowVisibleAgain()
        {
            var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var loader = new ControlledLoader();
            var tabs = CreateTabs(
                () => library, intake, () => scope,
                loadBookmarkedNodesAsync: loader.Loader());
            var active = Score("Active", 1, chartPath: "/library/active/active.dtx");
            var readded = Score("Readded", 2, chartPath: "/library/readded/readded.dtx");

            tabs.RequestBookmarksRefresh();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { active, readded });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 2 });

            intake.Install(Snapshot(151, new[] { active }, new[] { "/library/active" }));
            Assert.Equal(new[] { "Active" }, tabs.BuildBookmarksRows().Select(node => node.Title));

            intake.Install(Snapshot(152, new[] { active, readded }, new[] { "/library/active", "/library/readded" }));
            Assert.Equal(new[] { "Active", "Readded" }, tabs.BuildBookmarksRows().Select(node => node.Title));
            Assert.Equal(2, tabs.BookmarkNodes!.Count);
            scope.Retire();
            library.Dispose();
        }

        [Fact]
        public void BuildBookmarksRows_WhenCachedChartRemovedFromPublishedSnapshot_ShouldHideButRetainCacheRow()
        {
            var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var loader = new ControlledLoader();
            var tabs = CreateTabs(
                () => library, intake, () => scope,
                loadBookmarkedNodesAsync: loader.Loader());
            var retained = Score("Retained", 1, chartPath: "/library/active/retained.dtx");
            var removed = Score("Removed", 2, chartPath: "/library/active/removed.dtx");

            intake.Install(Snapshot(32, new[] { retained }, new[] { "/library/active" }));
            tabs.RequestBookmarksRefresh();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { retained, removed });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 2 });

            var rows = tabs.BuildBookmarksRows();

            Assert.Equal(new[] { "Retained" }, rows.Select(node => node.Title));
            Assert.Equal(2, tabs.BookmarkNodes!.Count);
            scope.Retire();
            library.Dispose();
        }

        #endregion

        #region Refresh request/consumption protocol

        [Fact]
        public void RefreshConsumption_WhenCompletionRequestsRefreshDuringConsumption_ShouldKeepRefreshPending()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope);

            tabs.RequestAsyncTabListRefresh();
            var consumedVersion = tabs.BeginTabListRefreshConsumption();
            tabs.RequestAsyncTabListRefresh();
            tabs.CompleteTabListRefreshConsumption(consumedVersion);

            Assert.True(tabs.TabListNeedsRefresh);
            scope.Retire();
        }

        [Fact]
        public void RefreshConsumption_WhenNoRequestRacesConsumption_ShouldLeaveFlagClear()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope);

            tabs.RequestAsyncTabListRefresh();
            var consumedVersion = tabs.BeginTabListRefreshConsumption();
            tabs.CompleteTabListRefreshConsumption(consumedVersion);

            Assert.False(tabs.TabListNeedsRefresh);
            scope.Retire();
        }

        [Fact]
        public void ResetForActivation_ShouldResetTabStateButKeepDurableWriteState()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var writer = new ControlledWriter();
            var bookmarksLoader = new ControlledLoader();
            var submissions = new SubmissionRecorder();
            var tabs = CreateTabs(
                library,
                scope,
                loadBookmarkedNodesAsync: bookmarksLoader.Loader(),
                setBookmarkAsync: writer.Writer(),
                submitTabLoadQuery: submissions.Seam());

            tabs.SwitchToTab(SongSelectionTab.RecentPlays); // sets ActiveTab + refresh flag
            tabs.RequestAsyncTabListRefresh();
            var node = Score("Song", 11);
            tabs.ToggleBookmark(node); // registers a pending write (durable across activations)

            tabs.ResetForActivation();

            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab);
            Assert.False(tabs.TabListNeedsRefresh);
            Assert.Null(tabs.RecentPlayNodes);
            Assert.Null(tabs.BookmarkNodes);
            Assert.False(tabs.ShowEmptyRecentMessage);
            Assert.False(tabs.RecentPlaysLoadFailed);
            Assert.False(tabs.ShowEmptyBookmarksMessage);
            Assert.False(tabs.BookmarksLoadFailed);

            // The durable pending write survives activation reset and still gates the
            // owed Bookmarks query: no query is submitted while its gate is closed.
            var submissionsBeforeRefresh = submissions.Submissions;
            tabs.RequestBookmarksRefresh();
            scope.Drain();
            Assert.Equal(submissionsBeforeRefresh, submissions.Submissions);

            var write = writer.AwaitWriteRegistered(0);
            write.Gate.SetResult(true);
            DrainUntil(scope, () => submissions.Submissions == submissionsBeforeRefresh + 1,
                "the owed Bookmarks query was not submitted after the pending write settled");
            bookmarksLoader.LoadWhenRegistered(0).Result.SetResult(
                new List<SongListNode> { Score("Song", 11, bookmarked: true) });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 },
                "the owed Bookmarks query result was never applied");
            Assert.Equal(submissionsBeforeRefresh + 1, submissions.Submissions);
            scope.Retire();
        }

        #endregion

        #region Recent loads (per-tab generation)

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenScopeActive_ShouldApplyResultOnDrain()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());

            tabs.BeginRecentPlaysLoad();
            var load = loader.LoadWhenRegistered(0);
            await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            load.Result.SetResult(new List<SongListNode> { Score("Fresh", 9) });

            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });
            Assert.Equal("Fresh", tabs.RecentPlayNodes![0].Title);
            scope.Retire();
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenScopeRetires_ShouldPreserveExistingNodes()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());

            tabs.BeginRecentPlaysLoad();
            var load = loader.LoadWhenRegistered(0);
            await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Navigate away before the worker produces its result: the queued completion
            // (and any not-yet-queued one) must drop instead of reaching torn-down state.
            scope.Retire();
            load.Result.SetResult(new List<SongListNode> { Score("Stale completion", 1) });

            // Static by scope semantics: a retired scope detaches queued items and drops
            // direct results, so no terminal can ever apply — no grace period needed.
            Assert.Null(tabs.RecentPlayNodes);
        }

        [Fact]
        public void BeginRecentPlaysLoad_ShouldInvokeLoaderOnWorkerThreadNotUpdateThread()
        {
            var updateThreadId = Environment.CurrentManagedThreadId;
            int? loaderThreadId = null;
            using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            connection.Open();
            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(connection)
                .Options;
            using (var schema = new SongDbContext(options))
                schema.Database.EnsureCreated();

            using var dbService = new SongDatabase(options, () =>
            {
                loaderThreadId = Environment.CurrentManagedThreadId;
                return new SongDbContext(options);
            });
            using var library = new SongLibrary(dbService);
            var scope = new StageBackgroundWorkScope();

            // The real query itself must run off the update thread; it is wrapped only to
            // observe the thread (the DB access stays real).
            Func<Task<List<SongListNode>>> loader = () => library.Queries.GetRecentlyPlayedNodesAsync(20);
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader);

            tabs.BeginRecentPlaysLoad();

            Assert.True(
                SpinWait.SpinUntil(() => loaderThreadId.HasValue, TimeSpan.FromSeconds(5)),
                "the recent-plays DB query was never invoked");
            // The test thread stays alive (blocked on SpinUntil above) while the query
            // runs, so a pool thread can never reuse the update thread's id here.
            Assert.NotEqual(updateThreadId, loaderThreadId!.Value);

            DrainUntil(scope, () => tabs.RecentPlayNodes != null);
            Assert.Empty(tabs.RecentPlayNodes!);
        }

        [Fact]
        public void BeginRecentPlaysLoad_WhenDbThrows_ShouldSetLoadFailedAndFlagRefresh()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: FaultingLoader());
            tabs.SetActiveTabQuiet(SongSelectionTab.RecentPlays);

            tabs.BeginRecentPlaysLoad();

            DrainUntil(scope, () => tabs.RecentPlaysLoadFailed);
            Assert.True(tabs.TabListNeedsRefresh);
            scope.Retire();
        }

        [Fact]
        public void BeginRecentPlaysLoad_WhenScopeRetiresBeforeFault_ShouldDropStaleFault()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: FaultingLoader());

            tabs.BeginRecentPlaysLoad();

            // The faulted result must drop with the retired scope instead of flagging
            // the load failed for a navigation that already happened.
            scope.Retire();

            // Static by scope semantics: no terminal of a retired scope can ever apply.
            Assert.False(tabs.RecentPlaysLoadFailed);
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenAllSongsTab_ShouldNotFlagListRefresh()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());
            Assert.Equal(SongSelectionTab.AllSongs, tabs.ActiveTab); // activation default

            tabs.BeginRecentPlaysLoad();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { Score("Warm", 3) });

            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });

            // Cache-warming must NOT repopulate the All Songs list (it would reset the
            // user's selection to index 0).
            Assert.False(tabs.TabListNeedsRefresh);
            scope.Retire();
        }

        [Fact]
        public void BeginRecentPlaysLoad_OnSuccess_ShouldClearPreExistingLoadFailedFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var faultsRemaining = 1;
            var controlled = loader.Loader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: () =>
                Interlocked.Decrement(ref faultsRemaining) >= 0
                    ? Task.FromException<List<SongListNode>>(new InvalidOperationException("database unavailable"))
                    : controlled());

            tabs.BeginRecentPlaysLoad(); // faults
            DrainUntil(scope, () => tabs.RecentPlaysLoadFailed);

            tabs.BeginRecentPlaysLoad(); // succeeds
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>());
            DrainUntil(scope, () => !tabs.RecentPlaysLoadFailed);

            Assert.False(tabs.RecentPlaysLoadFailed);
            scope.Retire();
        }

        [Fact]
        public void BeginRecentPlaysLoad_WhenSuccessApplicationThrows_ShouldKeepLoadFailedFlagAndPreviousCache()
        {
            using var library = CreateLibrary();
            var faults = new List<Exception>();
            var scope = new StageBackgroundWorkScope(faults.Add);
            var loader = new ControlledLoader();
            var controlled = loader.Loader();
            var faultingLibraryAccess = false;
            var faultsRemaining = 1;
            var tabs = CreateTabs(
                () => faultingLibraryAccess ? throw new InvalidOperationException("library snapshot unavailable") : library,
                new SongSelectionLibraryIntake(() => library),
                () => scope,
                loadRecentPlaysAsync: () =>
                    Interlocked.Decrement(ref faultsRemaining) >= 0
                        ? Task.FromException<List<SongListNode>>(new InvalidOperationException("database unavailable"))
                        : controlled());

            tabs.BeginRecentPlaysLoad(); // faults
            DrainUntil(scope, () => tabs.RecentPlaysLoadFailed);

            faultingLibraryAccess = true;
            tabs.BeginRecentPlaysLoad(); // rows load, but applying bookmark state throws
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { Score("Fresh", 9) });
            DrainUntil(scope, () => faults.Count == 1, "the success application's fault was never drained");

            Assert.Single(faults);
            Assert.True(tabs.RecentPlaysLoadFailed);
            Assert.Null(tabs.RecentPlayNodes);
            scope.Retire();
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenBookmarksCacheDisagreesWithAppliedSnapshot_ShouldUseAppliedSnapshot()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var recentLoader = new ControlledLoader();
            var bookmarksLoader = new ControlledLoader();
            var tabs = CreateTabs(
                () => library,
                intake,
                () => scope,
                loadRecentPlaysAsync: recentLoader.Loader(),
                loadBookmarkedNodesAsync: bookmarksLoader.Loader());

            try
            {
                intake.Install(Snapshot(
                    101,
                    new[] { Score("Library", 42, bookmarked: false) },
                    new[] { "/library/active" }));
                tabs.RequestBookmarksRefresh();
                bookmarksLoader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>
                {
                    Score("Bookmark cache", 42, bookmarked: true)
                });
                DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

                tabs.BeginRecentPlaysLoad();
                var load = recentLoader.LoadWhenRegistered(0);
                await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var freshNode = Score("Fresh", 42, bookmarked: false);
                load.Result.SetResult(new List<SongListNode> { freshNode });
                DrainUntil(scope, () => ReferenceEquals(tabs.RecentPlayNodes?.FirstOrDefault(), freshNode));

                Assert.False(freshNode.DatabaseSong!.IsBookmarked);
            }
            finally
            {
                scope.Retire();
            }
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenAppliedSnapshotDisagreesWithCachedRow_ShouldNotInheritCachedBookmarkState()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var loader = new ControlledLoader();
            var tabs = CreateTabs(
                () => library,
                intake,
                () => scope,
                loadRecentPlaysAsync: loader.Loader());

            try
            {
                tabs.BeginRecentPlaysLoad();
                loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>
                {
                    Score("Cached", 42, bookmarked: true)
                });
                DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });

                // The applied library snapshot is the canonical browse-tree view. A fresh
                // Recent row must take its state from that view (plus explicit toggle
                // reconciliation), never by copying the previously cached Recent row.
                intake.Install(Snapshot(
                    101,
                    new[] { Score("Library", 42, bookmarked: false) },
                    new[] { "/library/active" }));

                tabs.BeginRecentPlaysLoad();
                var load = loader.LoadWhenRegistered(1);
                await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var freshNode = Score("Fresh", 42, bookmarked: false);
                load.Result.SetResult(new List<SongListNode> { freshNode });
                DrainUntil(scope, () => ReferenceEquals(tabs.RecentPlayNodes?.FirstOrDefault(), freshNode));

                Assert.False(freshNode.DatabaseSong!.IsBookmarked);
            }
            finally
            {
                scope.Retire();
            }
        }

        [Fact]
        public async Task BeginRecentPlaysLoad_WhenToggleOccursDuringQuery_ShouldPreserveNewerBookmarkState()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                () => library,
                intake,
                () => scope,
                loadRecentPlaysAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());

            var selectedNode = Score("Selected", 42, bookmarked: false);

            try
            {
                // The newer bookmark exists only on the currently applied Recent row:
                // the detached row returned by the newer query must not overwrite it.
                tabs.BeginRecentPlaysLoad();
                var initialLoad = loader.LoadWhenRegistered(0);
                await initialLoad.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                initialLoad.Result.SetResult(new List<SongListNode> { selectedNode });
                DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });

                tabs.BeginRecentPlaysLoad();
                var load = loader.LoadWhenRegistered(1);
                await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.True(tabs.ToggleBookmark(selectedNode));
                Assert.True(selectedNode.DatabaseSong!.IsBookmarked);

                var queryNode = Score("Recent", 42, bookmarked: false);
                load.Result.SetResult(new List<SongListNode> { queryNode });
                DrainUntil(scope, () => ReferenceEquals(tabs.RecentPlayNodes?.FirstOrDefault(), queryNode));

                Assert.Same(queryNode, tabs.RecentPlayNodes![0]);
                Assert.True(tabs.RecentPlayNodes[0].DatabaseSong!.IsBookmarked);
            }
            finally
            {
                scope.Retire();
            }
        }

        // STALE RECENT CORRECTION (HPA-802): each flat tab owns its own query generation.
        // Two same-activation loads where the OLDER completes LAST must reject the stale
        // result instead of overwriting the newer load's rows (the old code applied
        // results in completion order).
        [Fact]
        public async Task BeginRecentPlaysLoad_WhenOlderLoadCompletesLast_ShouldRejectStaleResult()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());

            var loadATask = tabs.BeginRecentPlaysLoad(); // load A (older) — worker task = real enqueue barrier
            var older = loader.LoadWhenRegistered(0);
            await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            tabs.BeginRecentPlaysLoad(); // load B (newer, only issued after A started)
            var newer = loader.LoadWhenRegistered(1);
            await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            newer.Result.SetResult(new List<SongListNode> { Score("Fresh", 1) });
            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 } && tabs.RecentPlayNodes[0].Title == "Fresh");

            older.Result.SetResult(new List<SongListNode> { Score("Stale", 2), Score("Stale2", 3) });
            // REAL COMPLETION BARRIER: awaiting A's worker task proves A's success
            // terminal is ENQUEUED; the drain that follows provably processes it, so the
            // assertion below fails if the stale terminal is (wrongly) applied.
            await AwaitEnqueuedThenProcessAsync(loadATask, scope);

            var nodes = tabs.RecentPlayNodes;
            Assert.NotNull(nodes);
            Assert.Single(nodes);
            Assert.Equal("Fresh", nodes[0].Title);
            scope.Retire();
        }

        // STALE RECENT CORRECTION: the same generation guard must reject the OLDER
        // FAILURE too — a stale fault must not flag the load failed over the newer
        // load's success. The registration barrier pins which controlled invocation
        // belongs to which launch (each new request is issued only after the previous
        // load registered), so the fault is driven on the older load by identity.
        [Fact]
        public async Task BeginRecentPlaysLoad_WhenOlderFaultCompletesLast_ShouldRejectStaleFailure()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadRecentPlaysAsync: loader.Loader());
            try
            {
                var loadATask = tabs.BeginRecentPlaysLoad(); // load A (older) — worker task = real enqueue barrier
                var older = loader.LoadWhenRegistered(0);
                await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

                tabs.BeginRecentPlaysLoad(); // load B (newer) — issued only after A started
                var newer = loader.LoadWhenRegistered(1);
                await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                newer.Result.SetResult(new List<SongListNode> { Score("Fresh", 1) });
                DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });

                older.Result.SetException(new InvalidOperationException("stale database failure"));
                // REAL COMPLETION BARRIER: A's fault terminal is ENQUEUED once A's worker
                // task completes; the drain that follows provably processes it, so the
                // assertion below fails even under adversarial scheduling.
                await AwaitEnqueuedThenProcessAsync(loadATask, scope);

                Assert.False(tabs.RecentPlaysLoadFailed);
                Assert.Single(tabs.RecentPlayNodes!);
            }
            finally
            {
                scope.Retire();
            }
        }

        #endregion

        #region Bookmarks loads (per-tab generation, parity with Recent)

        // Every externally callable Bookmarks refresh entry is RequestBookmarksRefresh
        // (HPA-802 review follow-up): the raw BeginBookmarksLoad launcher is private, so
        // the per-tab generation tests below drive REAL refresh requests — with no
        // writes pending they launch their query immediately, and each returns the
        // worker task used as the real completion barrier below.
        [Fact]
        public async Task RequestBookmarksRefresh_WhenScopeActive_ShouldApplyResultOnDrain()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader.Loader());

            tabs.RequestBookmarksRefresh();
            var load = loader.LoadWhenRegistered(0);
            await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            load.Result.SetResult(new List<SongListNode> { Score("Loaded", 4) });

            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });
            Assert.Equal("Loaded", tabs.BookmarkNodes![0].Title);
            scope.Retire();
        }

        [Fact]
        public async Task RequestBookmarksRefresh_WhenScopeRetiresBeforeCompletion_ShouldDropQueuedResult()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader.Loader());

            tabs.RequestBookmarksRefresh();
            var load = loader.LoadWhenRegistered(0);
            await load.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            scope.Retire();
            load.Result.SetResult(new List<SongListNode> { Score("Stale completion", 1) });

            // Static by scope semantics: a retired scope detaches queued items and drops
            // direct results, so no terminal can ever apply — no grace period needed.
            Assert.Null(tabs.BookmarkNodes);
        }

        [Fact]
        public void RequestBookmarksRefresh_ShouldInvokeLoaderOnWorkerThreadNotUpdateThread()
        {
            var updateThreadId = Environment.CurrentManagedThreadId;
            int? loaderThreadId = null;
            using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            connection.Open();
            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(connection)
                .Options;
            using (var schema = new SongDbContext(options))
                schema.Database.EnsureCreated();

            using var dbService = new SongDatabase(options, () =>
            {
                loaderThreadId = Environment.CurrentManagedThreadId;
                return new SongDbContext(options);
            });
            using var library = new SongLibrary(dbService);
            var scope = new StageBackgroundWorkScope();
            Func<Task<List<SongListNode>>> loader = () => library.Queries.GetBookmarkedNodesAsync();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader);

            tabs.RequestBookmarksRefresh();

            Assert.True(
                SpinWait.SpinUntil(() => loaderThreadId.HasValue, TimeSpan.FromSeconds(5)),
                "the bookmark DB query was never invoked");
            Assert.NotEqual(updateThreadId, loaderThreadId!.Value);

            DrainUntil(scope, () => tabs.BookmarkNodes != null);
            Assert.Empty(tabs.BookmarkNodes!);
        }

        [Fact]
        public void RequestBookmarksRefresh_WhenDbThrows_ShouldSetLoadFailedAndFlagRefresh()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: FaultingLoader());
            tabs.SetActiveTabQuiet(SongSelectionTab.Bookmarks);

            tabs.RequestBookmarksRefresh();

            DrainUntil(scope, () => tabs.BookmarksLoadFailed);
            Assert.True(tabs.TabListNeedsRefresh);
            scope.Retire();
        }

        [Fact]
        public void RequestBookmarksRefresh_WhenScopeRetiresBeforeFault_ShouldDropStaleFault()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: FaultingLoader());

            tabs.RequestBookmarksRefresh();
            scope.Retire();

            // Static by scope semantics: no terminal of a retired scope can ever apply.
            Assert.False(tabs.BookmarksLoadFailed);
        }

        [Fact]
        public void RequestBookmarksRefresh_OnSuccess_ShouldClearPreExistingLoadFailedFlag()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var faultsRemaining = 1;
            var controlled = loader.Loader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: () =>
                Interlocked.Decrement(ref faultsRemaining) >= 0
                    ? Task.FromException<List<SongListNode>>(new InvalidOperationException("database unavailable"))
                    : controlled());

            tabs.RequestBookmarksRefresh(); // faults
            DrainUntil(scope, () => tabs.BookmarksLoadFailed);

            tabs.RequestBookmarksRefresh(); // succeeds
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>());
            DrainUntil(scope, () => !tabs.BookmarksLoadFailed);

            Assert.False(tabs.BookmarksLoadFailed);
            scope.Retire();
        }

        [Fact]
        public async Task RequestBookmarksRefresh_WhenNewerRefreshSupersedes_ShouldPreserveFreshResult()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader.Loader());

            var loadATask = tabs.RequestBookmarksRefresh(); // request A (older) — immediate launch, no writes pending
            var older = loader.LoadWhenRegistered(0);
            await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            tabs.RequestBookmarksRefresh(); // request B (newer) — supersedes A's query generation
            var newer = loader.LoadWhenRegistered(1);
            await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            newer.Result.SetResult(new List<SongListNode> { Score("Fresh", 1) });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 } && tabs.BookmarkNodes[0].Title == "Fresh");

            older.Result.SetResult(new List<SongListNode>());
            // REAL COMPLETION BARRIER: A's stale terminal is ENQUEUED once A's worker
            // task completes; the drain that follows provably processes it.
            await AwaitEnqueuedThenProcessAsync(loadATask, scope);

            var nodes = tabs.BookmarkNodes;
            Assert.NotNull(nodes);
            Assert.Single(nodes);
            Assert.Equal("Fresh", nodes[0].Title);
            scope.Retire();
        }

        [Fact]
        public async Task RequestBookmarksRefresh_WhenFaultedAndSuperseded_ShouldDiscardStaleFault()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader.Loader());
            try
            {
                // The registration barrier pins each controlled invocation to its launch:
                // request A is driven to a fault only after the newer request B has applied.
                var loadATask = tabs.RequestBookmarksRefresh(); // request A (older) — immediate launch
                var older = loader.LoadWhenRegistered(0);
                await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

                tabs.RequestBookmarksRefresh(); // request B (newer) — supersedes A's generation
                var newer = loader.LoadWhenRegistered(1);
                await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                newer.Result.SetResult(new List<SongListNode> { Score("Fresh", 1) });
                DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

                older.Result.SetException(new InvalidOperationException("stale database failure"));
                // REAL COMPLETION BARRIER: A's stale fault terminal is ENQUEUED once A's
                // worker task completes; the drain that follows provably processes it.
                await AwaitEnqueuedThenProcessAsync(loadATask, scope);

                // The stale fault must not flag the load failed over the newer load's result.
                Assert.False(tabs.BookmarksLoadFailed);
                Assert.Single(tabs.BookmarkNodes!);
            }
            finally
            {
                scope.Retire();
            }
        }

        [Fact]
        public async Task RequestBookmarksRefresh_RapidRequests_ShouldApplyNewestLoad()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var tabs = CreateTabs(library, scope, loadBookmarkedNodesAsync: loader.Loader());

            // Request order pinned by awaiting each start before issuing the next:
            // request A (older) then request B (newest).
            var loadATask = tabs.RequestBookmarksRefresh();
            var older = loader.LoadWhenRegistered(0);
            await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            tabs.RequestBookmarksRefresh(); // request B (newest) — supersedes A's query generation
            var newer = loader.LoadWhenRegistered(1);
            await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            newer.Result.SetResult(new List<SongListNode>());
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 0 });

            older.Result.SetResult(new List<SongListNode> { Score("Stale", 1) });
            // REAL COMPLETION BARRIER: A's stale terminal is ENQUEUED once A's worker
            // task completes; the drain that follows provably processes it.
            await AwaitEnqueuedThenProcessAsync(loadATask, scope);

            Assert.NotNull(tabs.BookmarkNodes);
            Assert.Empty(tabs.BookmarkNodes!);
            scope.Retire();
        }

        #endregion

        #region Deferred Bookmarks refresh (wait-then-load)

        // PARITY (pre-existing behavior): switching into Bookmarks defers the load
        // while a bookmark write is pending, then loads once after it settles.
        [Fact]
        public async Task SwitchToBookmarks_WhileWritePending_ShouldDeferLoadUntilWriteSettles()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());

            // Seed the cache through the real load path (query 1), then toggle a write.
            tabs.RequestBookmarksRefresh();
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { Score("Stale", 1) });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

            var node = Score("Song", 42);
            tabs.ToggleBookmark(node); // pending write
            tabs.SwitchToTab(SongSelectionTab.RecentPlays);
            var switchWait = tabs.SwitchToTab(SongSelectionTab.Bookmarks); // deferred refresh waits for the write

            // Load is deferred until the pending write settles; the stale list remains
            // and the loader has not been invoked again.
            Assert.Equal(1, loader.InvokedCount);
            Assert.Single(tabs.BookmarkNodes!);

            var write = writer.AwaitWriteRegistered(0);
            write.Gate.SetResult(true);
            await write.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // REAL COMPLETION BARRIER: the wait's worker task completes only after its
            // re-read terminal is ENQUEUED; the drain that follows provably processes it
            // (re-read: nothing pending → the owed query is LAUNCHED during that drain).
            // The worker itself starts on the thread pool, so registration is pinned by
            // the bounded LoadWhenRegistered barrier rather than an immediate count.
            await AwaitEnqueuedThenProcessAsync(switchWait, scope);
            loader.LoadWhenRegistered(1);

            // The deferred load runs on the worker; releasing it queues the completion
            // which the drain applies.
            loader.LoadWhenRegistered(1).Result.SetResult(new List<SongListNode>());
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 0 });
            Assert.Empty(tabs.BookmarkNodes!);
            scope.Retire();
        }

        // W1/W2 REGRESSION (HPA-802): W1 bookmarks A → switch into Bookmarks (wait) →
        // W2 unbookmarks cached B → finish W1 and drain (still no query while W2 is
        // pending) → finish W2 and drain (exactly one query) → A present, B absent.
        // SUBMISSION-BOUNDARY PROOF (fix round 2 follow-up): the I/O-execution seam
        // records every query submission synchronously at launch on the update
        // thread — a launch happens only inside test-driven drains, so the asserts
        // below are exact happens-before boundaries, not invocation-time counts:
        // ZERO records at the W1-settle boundary proves no query was ISSUED while
        // W2 was pending, even if an adversarial scheduler delayed its loader
        // registration past every later assert. The actual workers are held and
        // released only after the counts are proven; the released query's
        // DISTINCTIVE final-intent rows must then actually apply.
        [Fact]
        public async Task Bookmarks_W1ThenSwitchThenW2_ShouldQueryExactlyOnceAfterBothWritesAndShowFinalIntent()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var writer = new ControlledWriter();
            var submissions = new SubmissionRecorder();
            // Query rows derive from the writes that actually settled (test-thread
            // flags set before any subsequent drain, so the loader invoked after
            // ReleaseWorkers provably sees them): song 11 (A) appears once its
            // bookmark write settled; song 22 (B) stays until its un-bookmark write
            // settled. A present / B absent is therefore the committed final intent.
            var w1Settled = false;
            var w2Settled = false;
            Func<Task<List<SongListNode>>> rowsLoader = () =>
            {
                var rows = new List<SongListNode>();
                if (w1Settled)
                    rows.Add(Score("A", 11, bookmarked: true));
                if (!w2Settled)
                    rows.Add(Score("B", 22, bookmarked: true));
                return Task.FromResult(rows);
            };
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: rowsLoader,
                setBookmarkAsync: writer.Writer(),
                submitTabLoadQuery: submissions.Seam());

            // Seed the bookmark cache: B (song 22, bookmarked). A (song 11) is not yet
            // bookmarked, so it is absent from the seeded query. This is submission 1.
            tabs.RequestBookmarksRefresh();
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });
            Assert.Equal(1, submissions.Submissions);
            var nodeB = tabs.BookmarkNodes![0];
            var nodeA = Score("A", 11);

            // Hold the actual worker of every subsequent query submission: the proof
            // below is about ISSUES (launches), not worker/loader scheduling.
            submissions.HoldWorkers();

            // W1: bookmark A (write held in flight).
            tabs.ToggleBookmark(nodeA);
            Assert.True(nodeA.DatabaseSong!.IsBookmarked);
            var w1 = writer.AwaitWriteRegistered(0);
            Assert.Equal(11, w1.SongId);

            // Switch into Bookmarks: the refresh waits for W1. The returned worker task
            // is the wait's real completion barrier.
            var switchWait = tabs.SwitchToTab(SongSelectionTab.Bookmarks);

            // W2: unbookmark cached B (issued while W1 is pending; the toggle itself is
            // synchronous, only its writer invocation is observed later).
            tabs.ToggleBookmark(nodeB);
            Assert.False(nodeB.DatabaseSong!.IsBookmarked);

            // Finish W1; W2 stays pending on its own song id.
            w1.Gate.SetResult(true);
            await w1.Task.WaitAsync(TimeSpan.FromSeconds(5));
            w1Settled = true; // test-thread write, happens-before the drains below
            var w2 = writer.AwaitWriteRegistered(1);
            Assert.Equal(22, w2.SongId);

            // ENQUEUE BARRIER: awaiting the wait's worker task proves W1's wait terminal
            // is ENQUEUED, and the drain that follows provably processes it. That
            // terminal's continuation re-reads pending writes on the update thread
            // DURING this drain and re-chains on the SAME request id/scope because W2
            // is still pending. Every query launch happens on the update thread inside
            // a drain, and the seam records it synchronously, so the next assert is an
            // exact boundary: a launch issued during this drain is already recorded; a
            // launch issued later cannot appear retroactively.
            await AwaitEnqueuedThenProcessAsync(switchWait, scope);

            // SUBMISSION BOUNDARY: with W2 still pending, NO query may have been
            // issued — the deferred refresh must re-read pending writes, not load.
            // (W2's un-bookmark toggle optimistically dropped B from the cache; the
            // emptiness is legitimate — the proof here is the zero-submission record.)
            Assert.Equal(1, submissions.Submissions);
            Assert.Empty(tabs.BookmarkNodes!);

            // Finish W2 and drain: the re-chained wait settles, sees no pending writes
            // and issues the owed query — synchronously during one of these drains, so
            // hitting the record is the launch barrier itself.
            w2.Gate.SetResult(true);
            await w2.Task.WaitAsync(TimeSpan.FromSeconds(5));
            w2Settled = true; // test-thread write, happens-before the drains below
            DrainUntil(scope, () => submissions.Submissions == 2,
                "the owed bookmarks query was never issued after both writes settled");

            // EXACTLY-ONE ISSUE: the chain ended at the launch (no further work is in
            // flight), so no further submission can appear.
            Assert.Equal(2, submissions.Submissions);

            // The issued query reflects the FINAL intent and actually applies it:
            // release the held worker, the loader derives A-bookmarked/no-B rows, and
            // the drain applies them (A present, B absent).
            submissions.ReleaseWorkers();
            DrainUntil(scope, () =>
                tabs.BookmarkNodes is { Count: 1 } &&
                (tabs.BookmarkNodes![0].DatabaseSongId ?? 0) == 11,
                "the owed bookmarks query did not apply the final-intent rows (A present, B absent)");
            Assert.Equal(new[] { 11 }, tabs.BookmarkNodes!.Select(node => node.DatabaseSongId ?? 0));
            scope.Retire();
        }

        // SUPERSEDING REFRESH (HPA-802): a newer DEFERRED refresh request — the public
        // RequestBookmarksRefresh entry, not the raw query launcher — supersedes the
        // older outstanding wait: exactly the SURVIVING request's query runs and applies
        // its distinctive rows; the superseded request owns no work.
        // ONE-PENDING-WRITE PROOF (fix round 2 final): both wait handles are captured
        // BEFORE the single pending write resolves, so each returned worker task is a
        // real enqueue barrier for its OWN terminal — awaiting both (only AFTER the
        // write settles; the wait workers await that write) and draining once each
        // proves both callbacks were PROCESSED, regardless of which terminal enqueued
        // first or whether the first drain handled both (hence no zero-submission
        // assert in between). This removes the untracked re-chain tail of the old
        // two-write shape, whose late-enqueueing terminal could dodge every drain.
        // The I/O seam records every launch synchronously at issue time, so the
        // exactly-one equality after both drains — with the workers still held — is
        // exact: a superseded request launching ANYWHERE in this interleaving would
        // already have recorded a third submission. The released survivor must then
        // apply its distinctive two-row post-settle result (never satisfiable by the
        // one-row seeded cache).
        [Fact]
        public async Task Bookmarks_WhenNewerExplicitRefreshSupersedesOlderWait_ShouldRunExactlyOneQuery()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var writer = new ControlledWriter();
            var submissions = new SubmissionRecorder();
            var writesSettled = false;
            Func<Task<List<SongListNode>>> rowsLoader = () =>
            {
                var rows = writesSettled
                    ? new List<SongListNode>
                    {
                        Score("Survivor", 11, bookmarked: true),
                        Score("Survivor+", 33, bookmarked: true)
                    }
                    : new List<SongListNode> { Score("Seeded", 11, bookmarked: true) };
                return Task.FromResult(rows);
            };
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: rowsLoader,
                setBookmarkAsync: writer.Writer(),
                submitTabLoadQuery: submissions.Seam());

            // Seed the cache (submission 1: pre-settle rows → one "Seeded" row), then
            // hold the actual query workers: every launch below is still recorded
            // synchronously at issue time, but no loader can run until release.
            tabs.RequestBookmarksRefresh();
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });
            Assert.Equal(1, submissions.Submissions);
            submissions.HoldWorkers();

            // ONE pending write (song 11, the cached row), and BOTH deferred refresh
            // requests issued against it while the write is still gated: the tab-switch
            // request 1 and the explicit RequestBookmarksRefresh request 2, which
            // supersedes it. Both wait handles exist before the write resolves.
            var nodeA = tabs.BookmarkNodes![0];
            tabs.ToggleBookmark(nodeA);
            var write1 = writer.AwaitWriteRegistered(0);
            var wait1 = tabs.SwitchToTab(SongSelectionTab.Bookmarks); // deferred request 1
            var wait2 = tabs.RequestBookmarksRefresh();               // deferred request 2 (supersedes 1)

            // Resolve the single write BEFORE awaiting either wait: the wait workers
            // await this write, so awaiting earlier could never complete. The
            // test-thread flag write happens-before every drain below (release TCS),
            // so the surviving loader (invoked after ReleaseWorkers) provably derives
            // the post-settle rows.
            write1.Gate.SetResult(true);
            await write1.Task.WaitAsync(TimeSpan.FromSeconds(5));
            writesSettled = true;

            // Both wait steps are real enqueue barriers: each worker task completes
            // only after ITS OWN terminal is enqueued, and each drain provably
            // processes it (FIFO). After both awaits both callbacks were processed —
            // the first drain may legitimately have processed both, which is why no
            // zero-submission assert sits in between.
            await AwaitEnqueuedThenProcessAsync(wait1, scope, "deferred request 1 issued no wait step");
            await AwaitEnqueuedThenProcessAsync(wait2, scope, "deferred request 2 issued no wait step");

            // EXACTLY-ONE post-seed submission after both callbacks were processed,
            // while the workers are still held: the surviving request's launch was
            // recorded synchronously during one of the two drains, and a superseded
            // request that launched ANYWHERE above would already have recorded a
            // third submission.
            Assert.Equal(2, submissions.Submissions);

            // The SURVIVING query applied its distinctive post-settle rows: release the
            // held workers — the two-row result can never come from the one-row seeded
            // cache.
            submissions.ReleaseWorkers();
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 2 },
                "the surviving refresh never applied its distinctive rows");
            Assert.Equal(new[] { 11, 33 }, tabs.BookmarkNodes!.Select(node => node.DatabaseSongId ?? 0));

            // FINAL equality after the application drain: every launch happens on the
            // update thread inside a drain and all of those have been observed above,
            // so nothing can be recorded in between.
            Assert.Equal(2, submissions.Submissions);
            scope.Retire();
        }

        // SCOPE RETIREMENT / REENTRY: the owed bookmarks query drops when the activation
        // scope retires while the wait is outstanding (durable writes are never canceled),
        // and a refresh on a fresh reentry scope re-enters the same wait path and finally
        // runs exactly one query. Deterministic: each phase proves itself with query-count
        // equality at an application barrier.
        [Fact]
        public async Task Bookmarks_WhenScopeRetiresDuringDeferredWait_ShouldDropTheOwedQueryUntilReentry()
        {
            using var library = CreateLibrary();
            StageBackgroundWorkScope? currentScope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                () => library,
                new SongSelectionLibraryIntake(() => library),
                () => currentScope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());
            try
            {
                var node = Score("A", 11);
                tabs.ToggleBookmark(node); // pending write
                var write = writer.AwaitWriteRegistered(0);
                tabs.SwitchToTab(SongSelectionTab.Bookmarks); // deferred wait on scope A
                var scopeA = currentScope!;

                scopeA.Retire(); // navigation away: the owed query drops with the scope

                // The write itself is never canceled: it completes successfully.
                write.Gate.SetResult(true);
                await write.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(write.Task.IsCompletedSuccessfully);

                // REENTRY: same owner, fresh activation scope. The refresh re-enters the
                // wait path (nothing pending → immediate launch) and runs exactly once.
                currentScope = new StageBackgroundWorkScope();
                var scopeB = currentScope;
                tabs.RequestBookmarksRefresh();
                DrainUntil(scopeB, () => loader.InvokedCount == 1,
                    "the reentry refresh did not run exactly one query");
                loader.LoadWhenRegistered(0).Result.SetResult(
                    new List<SongListNode> { Score("A", 11, bookmarked: true) });
                DrainUntil(scopeB, () => tabs.BookmarkNodes is { Count: 1 });
                // The retired wait added no query anywhere.
                Assert.Equal(1, loader.InvokedCount);
                scopeB!.Retire();
            }
            finally
            {
                currentScope!.Retire();
            }
        }

        // FAULTED WRITES SETTLE: a faulted pending write must release the owed load
        // (the wait includes faults, so the refresh never hangs). GATED SETTLEMENT
        // (HPA-802 fix round 2): the write faults only when the test releases the gate
        // — AFTER the refresh has snapshotted it as incomplete — so the refresh
        // deterministically enters the WAIT branch instead of an already-settled-write
        // immediate launch. ISOLATION: the deferred refresh is requested WITHOUT
        // entering the Bookmarks tab, so the fault's rollback cannot launch a
        // replacement refresh (the rollback reload only runs on the Bookmarks tab).
        // The loader records, AT INVOCATION, whether the write had settled: the
        // request's OWN owed reload must run exactly once AND only after the fault —
        // an immediate launch (write still pending at invocation) fails the record, a
        // dropped wait fails the count, an extra rollback-triggered reload fails both.
        [Fact]
        public async Task Bookmarks_WhenPendingWriteFaults_ShouldStillRunTheOwedQuery()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var faultGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? writeTask = null;
            var writeGate = new object();
            var writer = ControlledWriter.Overriding((_, _, _) =>
            {
                var write = faultGate.Task.ContinueWith(
                    _ => Task.FromException(new InvalidOperationException("forced persist fault")),
                    TaskScheduler.Default).Unwrap();
                lock (writeGate)
                    writeTask = write;
                return write;
            });

            var settledAtInvocation = new List<bool>();
            var recordGate = new object();
            Func<Task<List<SongListNode>>> loader = () =>
            {
                bool settled;
                lock (writeGate)
                    settled = writeTask?.IsCompleted == true;
                lock (recordGate)
                    settledAtInvocation.Add(settled);
                return Task.FromResult(new List<SongListNode>());
            };

            var submissions = new SubmissionRecorder();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader,
                setBookmarkAsync: writer.Writer(),
                submitTabLoadQuery: submissions.Seam());

            var node = Score("A", 11);
            tabs.ToggleBookmark(node);
            Assert.True(node.DatabaseSong!.IsBookmarked); // optimistic flip

            // The write is provably INCOMPLETE when the refresh snapshots it (the fault
            // gate is still held), so the wait branch — not an immediate launch — is
            // entered; the returned task is that wait step's completion barrier.
            var waitTask = tabs.RequestBookmarksRefresh(); // active tab stays AllSongs
            Assert.Equal(0, submissions.Submissions);

            // Release the fault: the write settles and releases the wait.
            faultGate.SetResult(true);

            // REAL COMPLETION BARRIER: the wait worker releases only when the write's
            // fault is observed by its WhenAll over the snapshot batch, and its task
            // completes only after the launch terminal is enqueued; the drain provably
            // processes it — the re-read sees the settled write and LAUNCHES the owed
            // query during that drain. Registration is a pool hop, pinned below.
            await AwaitEnqueuedThenProcessAsync(
                waitTask, scope,
                "the deferred refresh issued no wait step for the pending write");
            Assert.Equal(1, submissions.Submissions);
            Assert.True(
                SpinWait.SpinUntil(
                    () => { lock (recordGate) return settledAtInvocation.Count >= 1; },
                    TimeSpan.FromSeconds(10)),
                "the owed bookmarks query did not run after the faulted write settled");

            // The self-completing query's terminal is applied by the drain below.
            DrainUntil(scope, () => tabs.BookmarkNodes != null,
                "the fresh bookmarks result was never applied");
            lock (recordGate)
            {
                Assert.Equal(1, settledAtInvocation.Count); // no rollback-triggered replacement query
                Assert.True(settledAtInvocation[0],
                    "a bookmarks query ran while the write was still pending (immediate launch bypassing the wait)");
            }
            scope.Retire();
        }

        // CANCELED WRITES SETTLE: a canceled pending write must release the owed load
        // (the wait includes the write's OWN cancellation, so the refresh never hangs).
        // Same gated-settlement and All-tab isolation as the fault twin: the write is
        // canceled only after the refresh snapshotted it, so exactly-one record taken
        // AFTER the cancellation proves the original wait launched the reload itself.
        [Fact]
        public async Task Bookmarks_WhenPendingWriteCancels_ShouldStillRunTheOwedQuery()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var cancelGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? writeTask = null;
            var writeGate = new object();
            var writer = ControlledWriter.Overriding((_, _, _) =>
            {
                var write = cancelGate.Task.ContinueWith(
                    _ => Task.FromCanceled(new CancellationToken(true)),
                    TaskScheduler.Default).Unwrap();
                lock (writeGate)
                    writeTask = write;
                return write;
            });

            var settledAtInvocation = new List<bool>();
            var recordGate = new object();
            Func<Task<List<SongListNode>>> loader = () =>
            {
                bool settled;
                lock (writeGate)
                    settled = writeTask?.IsCompleted == true;
                lock (recordGate)
                    settledAtInvocation.Add(settled);
                return Task.FromResult(new List<SongListNode>());
            };

            var submissions = new SubmissionRecorder();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader,
                setBookmarkAsync: writer.Writer(),
                submitTabLoadQuery: submissions.Seam());

            var node = Score("A", 11);
            tabs.ToggleBookmark(node);

            // Snapshot sees the INCOMPLETE write (gate held) → wait branch.
            var waitTask = tabs.RequestBookmarksRefresh(); // active tab stays AllSongs
            Assert.Equal(0, submissions.Submissions);

            cancelGate.SetResult(true); // the write's OWN cancellation releases the wait

            await AwaitEnqueuedThenProcessAsync(
                waitTask, scope,
                "the deferred refresh issued no wait step for the pending write");
            Assert.Equal(1, submissions.Submissions);
            Assert.True(
                SpinWait.SpinUntil(
                    () => { lock (recordGate) return settledAtInvocation.Count >= 1; },
                    TimeSpan.FromSeconds(10)),
                "the owed bookmarks query did not run after the canceled write settled");
            DrainUntil(scope, () => tabs.BookmarkNodes != null,
                "the fresh bookmarks result was never applied");
            lock (recordGate)
            {
                Assert.Equal(1, settledAtInvocation.Count); // no rollback-triggered replacement query
                Assert.True(settledAtInvocation[0],
                    "a bookmarks query ran while the write was still pending (immediate launch bypassing the wait)");
            }
            scope.Retire();
        }

        // TOGGLE INVALIDATION OF A RUNNING QUERY: a toggle bumps only the bookmarks
        // QUERY generation — never the deferred request id — so the already-launched
        // query is rejected (no stale rows applied) and the still-current refresh
        // intent retries through the same wait path and re-queries once.
        [Fact]
        public async Task Bookmarks_WhenToggleInvalidatesRunningQuery_ShouldRetryWithoutApplyingStaleRows()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());

            // Launch a bookmarks query and hold it in flight (real refresh request — no
            // writes pending → immediate launch; the returned task is the barrier).
            var runningTask = tabs.RequestBookmarksRefresh();
            var running = loader.LoadWhenRegistered(0);
            await running.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Toggle while the query runs: bumps the query generation only.
            var node = Score("A", 11);
            tabs.ToggleBookmark(node);
            var write = writer.AwaitWriteRegistered(0);

            // The running query completes with a pre-toggle snapshot: it must NOT apply.
            running.Result.SetResult(new List<SongListNode> { Score("A", 11, bookmarked: true) });
            // REAL COMPLETION BARRIER: the stale terminal is ENQUEUED once the query's
            // worker task completes; the drain that follows provably processes it (the
            // stale callback then re-enters the wait path on the write). The assertion
            // below fails even under adversarial scheduling.
            await AwaitEnqueuedThenProcessAsync(runningTask, scope);
            Assert.Null(tabs.BookmarkNodes);

            // The owed refresh retries through the same wait path: exactly one new
            // query after the toggle's write settles.
            write.Gate.SetResult(true);
            await write.Task.WaitAsync(TimeSpan.FromSeconds(5));
            DrainUntil(scope, () => loader.InvokedCount == 2,
                "the invalidated query's refresh intent was not retried after the toggle's write settled");

            var retry = loader.LoadWhenRegistered(1);
            retry.Result.SetResult(new List<SongListNode> { Score("A", 11, bookmarked: true) });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });
            scope.Retire();
        }

        // REQUEST-TIME INVALIDATION (HPA-802 review, follow-up shape): a newer Bookmarks
        // refresh must invalidate an already-running query AT REQUEST TIME. With the raw
        // launcher private, the reachable interleaving through the public API is: request
        // 1 launches its query (no writes pending), then a toggle makes a write pending
        // and request 2 (a real deferred refresh) waits — the running query's rows must
        // never apply, and request 2's own query only runs once the write settles.
        [Fact]
        public async Task RequestBookmarksRefresh_WhileAnOlderRequestsQueryIsRunning_ShouldRejectItsRowsUntilTheNewestQuery()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());

            // Request 1: query in flight with a fresh generation, held by the gate.
            var runningTask = tabs.RequestBookmarksRefresh();
            var running = loader.LoadWhenRegistered(0);
            await running.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // A toggle makes a write pending, then request 2 (deferred — it must wait).
            var node = Score("A", 11);
            tabs.ToggleBookmark(node);
            var write = writer.AwaitWriteRegistered(0);
            tabs.RequestBookmarksRefresh();

            // The running query completes pre-refresh: it must be rejected — its terminal
            // is provably processed by the real completion barrier below.
            running.Result.SetResult(new List<SongListNode> { Score("Stale", 1) });
            await AwaitEnqueuedThenProcessAsync(runningTask, scope);
            Assert.Null(tabs.BookmarkNodes);

            // And request 2 has not launched its own query yet (the write is pending):
            // every enqueued terminal has been processed above, so the count is exact.
            Assert.Equal(1, loader.InvokedCount);

            // The refresh's own query runs after the write settles and applies fresh rows.
            write.Gate.SetResult(true);
            await write.Task.WaitAsync(TimeSpan.FromSeconds(5));
            DrainUntil(scope, () => loader.InvokedCount == 2, "the refresh's own query never launched");
            loader.LoadWhenRegistered(1).Result.SetResult(new List<SongListNode> { Score("A", 11, bookmarked: true) });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });
            Assert.Equal(new[] { 11 }, tabs.BookmarkNodes!.Select(n => n.DatabaseSongId ?? 0));
            scope.Retire();
        }

        // CAPTURED-SCOPE RETRY (HPA-802 review): a stale completion's retry must re-enter
        // the wait path on the scope its query was LAUNCHED on — re-resolving the scope at
        // retry time hands a stale request's follow-up work to a newer activation. The
        // accessor below is repointed at a NEWER scope that the test NEVER drains: if the
        // retry were re-resolved onto that scope, its owed query could never apply rows;
        // the positive proof that the retry ran on the CAPTURED scope is the fresh rows
        // applying there. (The owed query dropping when the captured scope retires is
        // covered by Bookmarks_WhenScopeRetiresDuringDeferredWait...)
        [Fact]
        public async Task RequestBookmarksRefresh_WhenStaleCompletionRetries_ShouldStayOnTheCapturedScope()
        {
            using var library = CreateLibrary();
            StageBackgroundWorkScope? currentScope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                () => library,
                new SongSelectionLibraryIntake(() => library),
                () => currentScope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());
            try
            {
                var scopeA = currentScope!;
                var runningTask = tabs.RequestBookmarksRefresh(); // query on scope A
                var running = loader.LoadWhenRegistered(0);
                await running.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

                tabs.ToggleBookmark(Score("A", 11)); // bumps the query generation
                var write = writer.AwaitWriteRegistered(0);
                write.Gate.SetResult(true);
                await write.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // A newer activation's scope BEFORE the stale completion drains — and it
                // is never drained, so a re-resolved retry could never apply anything.
                currentScope = new StageBackgroundWorkScope();

                running.Result.SetResult(new List<SongListNode> { Score("Stale", 1) });
                // REAL COMPLETION BARRIER: the stale terminal is ENQUEUED once the
                // query's worker task completes; the drain that follows provably
                // processes it on the CAPTURED scope and re-enters the wait path there.
                await AwaitEnqueuedThenProcessAsync(runningTask, scopeA);

                // The retry launched on the captured scope: KEEP DRAINING scope A while
                // waiting for the retry's registration (HPA-802 fix round 2 — the old
                // bare LoadWhenRegistered spin could deadlock on CORRECT code: the
                // owner's tracked write task can settle a scheduling hop after the
                // writer seam's inner task the test awaited, so the retry legitimately
                // re-enters the wait path and needs additional scope-A drains before
                // its terminal launches the query). Scope B is never drained.
                DrainUntil(scopeA, () => loader.InvokedCount >= 2,
                    "the retried refresh never launched on the captured scope");
                var retry = loader.LoadAt(1);
                retry.Result.SetResult(new List<SongListNode> { Score("Fresh", 11, bookmarked: true) });
                DrainUntil(scopeA, () => tabs.BookmarkNodes is { Count: 1 } && tabs.BookmarkNodes[0].Title == "Fresh",
                    "the retried refresh never applied on the captured scope");
                Assert.Equal(2, loader.InvokedCount);
                scopeA.Retire();
            }
            finally
            {
                currentScope!.Retire();
            }
        }

        // EVERY REFRESH ENTRY WAITS FOR PENDING WRITES (HPA-802 review follow-up): with
        // the raw query launcher private, the direct public Bookmarks refresh entry —
        // RequestBookmarksRefresh, used by the warm load and the rollback reload — must
        // defer its query behind an in-flight write exactly like the tab switch does.
        [Fact]
        public async Task RequestBookmarksRefresh_WhileWritePending_ShouldDeferTheOwedQueryUntilItSettles()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());

            var node = Score("A", 11);
            tabs.ToggleBookmark(node); // pending write
            var write = writer.AwaitWriteRegistered(0);

            // The refresh waits: the loader must not be invoked while the write is in
            // flight, and no query result may appear.
            var refreshTask = tabs.RequestBookmarksRefresh();
            Assert.Equal(0, loader.InvokedCount);
            Assert.Null(tabs.BookmarkNodes);

            write.Gate.SetResult(true);
            await write.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // REAL COMPLETION BARRIER: the wait worker's task completes only after its
            // re-read terminal is ENQUEUED; the drain that follows provably processes it
            // and LAUNCHES the owed query (nothing pending anymore). Registration is a
            // pool hop, pinned by the bounded LoadWhenRegistered barrier.
            await AwaitEnqueuedThenProcessAsync(refreshTask, scope);
            loader.LoadWhenRegistered(0);
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { Score("A", 11, bookmarked: true) });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });
            Assert.Equal(new[] { 11 }, tabs.BookmarkNodes!.Select(n => n.DatabaseSongId ?? 0));
            scope.Retire();
        }

        #endregion

        #region Toggle path (in-memory + persistence seams)

        [Fact]
        public async Task ToggleBookmark_ShouldFlipFlagAndReconcileAcrossAllSurfaces()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var recentLoader = new ControlledLoader();
            var bookmarkLoader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadRecentPlaysAsync: recentLoader.Loader(),
                loadBookmarkedNodesAsync: bookmarkLoader.Loader(),
                setBookmarkAsync: writer.Writer());

            var selectedSong = new SongEntity { Id = 42, Title = "S", IsBookmarked = false };
            var rootSong = new SongEntity { Id = 42, Title = "S", IsBookmarked = false };
            var recentSong = new SongEntity { Id = 42, Title = "S", IsBookmarked = false };
            var bookmarkSong = new SongEntity { Id = 42, Title = "S", IsBookmarked = false };
            var node = new SongListNode { Type = NodeType.Score, Title = "S", DatabaseSong = selectedSong };

            // Seed the cross-list representations through the real loads.
            tabs.BeginRecentPlaysLoad();
            recentLoader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>
            {
                new() { Type = NodeType.Score, DatabaseSong = recentSong }
            });
            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });
            tabs.RequestBookmarksRefresh();
            bookmarkLoader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode>
            {
                new() { Type = NodeType.Score, DatabaseSong = bookmarkSong }
            });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

            var savedRoots = TakeLibraryRoots(library);
            var roots = GetPrivateField<List<SongListNode>>(library, "_rootSongs")!;
            roots.Clear();
            roots.Add(new SongListNode { Type = NodeType.Score, Title = "S", DatabaseSong = rootSong });
            try
            {
                Assert.True(tabs.ToggleBookmark(node));

                Assert.True(selectedSong.IsBookmarked);  // direct flip on the selected node
                Assert.True(rootSong.IsBookmarked);      // reconciled via the shared roots
                Assert.True(recentSong.IsBookmarked);    // reconciled via the Recent cache
                Assert.True(bookmarkSong.IsBookmarked);  // reconciled via the Bookmarks cache
            }
            finally
            {
                RestoreLibraryRoots(library, savedRoots);
                scope.Retire();
            }
        }

        [Fact]
        public void ToggleBookmark_WhenAppliedSnapshotExists_ShouldReconcileAppliedSongAndLeaveManagerViewBystanderUntouched()
        {
            var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var intake = new SongSelectionLibraryIntake(() => library);
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                () => library, intake, () => scope,
                setBookmarkAsync: writer.Writer());

            var selectedSong = new SongEntity { Id = 42, Title = "Selected", IsBookmarked = false };
            var appliedSong = new SongEntity { Id = 42, Title = "Applied", IsBookmarked = false };
            // A manager-view bystander sharing the same song id must NOT be reconciled
            // when an applied snapshot owns the canonical view for this toggle.
            var bystanderSong = new SongEntity { Id = 42, Title = "Bystander", IsBookmarked = false };
            var selectedNode = new SongListNode { Type = NodeType.Score, Title = "Selected", DatabaseSongId = 42, DatabaseSong = selectedSong };
            var appliedNode = new SongListNode { Type = NodeType.Score, Title = "Applied", DatabaseSongId = 42, DatabaseSong = appliedSong };
            var bystanderNode = new SongListNode { Type = NodeType.Score, Title = "Bystander", DatabaseSongId = 42, DatabaseSong = bystanderSong };

            intake.Install(Snapshot(101, new[] { appliedNode }, new[] { "/library/active" }));

            var savedRoots = TakeLibraryRoots(library);
            var roots = GetPrivateField<List<SongListNode>>(library, "_rootSongs")!;
            roots.Clear();
            roots.Add(bystanderNode);
            try
            {
                Assert.True(tabs.ToggleBookmark(selectedNode));

                Assert.True(appliedSong.IsBookmarked);     // reconciled via the applied snapshot
                Assert.False(bystanderSong.IsBookmarked);  // manager-view bystander untouched
            }
            finally
            {
                RestoreLibraryRoots(library, savedRoots);
                scope.Retire();
                library.Dispose();
            }
        }

        [Fact]
        public async Task ToggleBookmark_OnBookmarksTab_WhenUnbookmarking_ShouldRemoveCachedRowAndFlagRefresh()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());

            // Seed the cache through the real load path and enter the Bookmarks tab.
            // (The switch-launched load must reach the worker; drain-based wait keeps
            // this deterministic regardless of whether the launch is direct or posted.)
            var node = Score("Song", 42, bookmarked: true);
            tabs.SwitchToTab(SongSelectionTab.Bookmarks);
            DrainUntil(scope, () => loader.InvokedCount >= 1);
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { node });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

            Assert.True(tabs.ToggleBookmark(node));

            Assert.False(node.DatabaseSong!.IsBookmarked);
            Assert.Empty(tabs.BookmarkNodes!);
            Assert.True(tabs.TabListNeedsRefresh);
            writer.AwaitWriteInvoked(1); // persistence queued for song 42
            Assert.Equal(42, writer.Writes[0].SongId);
            scope.Retire();
        }

        [Fact]
        public async Task ToggleBookmark_OnBookmarksTab_WhenBookmarking_ShouldKeepRow()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var loader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadBookmarkedNodesAsync: loader.Loader(),
                setBookmarkAsync: writer.Writer());

            var node = Score("Song", 42);
            tabs.SwitchToTab(SongSelectionTab.Bookmarks);
            DrainUntil(scope, () => loader.InvokedCount >= 1);
            loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { node });
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

            // The update loop consumes any pending refresh when it projects (the switch
            // and the load completion queued one); consume the same way so the assertion
            // below isolates the TOGGLE's contribution.
            int consumedVersion = tabs.BeginTabListRefreshConsumption();
            tabs.CompleteTabListRefreshConsumption(consumedVersion);

            Assert.True(tabs.ToggleBookmark(node));

            Assert.True(node.DatabaseSong!.IsBookmarked);
            Assert.Single(tabs.BookmarkNodes!); // row NOT removed because newState == true
            Assert.False(tabs.TabListNeedsRefresh); // bookmarking needs no repopulate
            scope.Retire();
        }

        [Fact]
        public void ToggleBookmark_ShouldInvokeWriterWithPersistedIdWhenEntityIdIsZero()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(library, scope, setBookmarkAsync: writer.Writer());
            var song = new SongEntity { Id = 0, Title = "S", IsBookmarked = false };
            var node = new SongListNode { Type = NodeType.Score, Title = "S", DatabaseSong = song, DatabaseSongId = 77 };

            Assert.True(tabs.ToggleBookmark(node));

            // Must key persistence on the persisted id (77), never the zero entity id.
            writer.AwaitWriteInvoked(1);
            var invoked = Assert.Single(writer.Writes);
            Assert.Equal(77, invoked.SongId);
            Assert.True(invoked.State);
            scope.Retire();
        }

        [Fact]
        public void ToggleBookmark_WhenZeroEntityIdNodeInRoots_ShouldReconcileByPersistedId()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(library, scope, setBookmarkAsync: writer.Writer());
            var selectedSong = new SongEntity { Id = 0, Title = "S", IsBookmarked = false };
            var rootNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "S",
                DatabaseSong = new SongEntity { Id = 0, Title = "S", IsBookmarked = false },
                DatabaseSongId = 77
            };
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = "S",
                DatabaseSong = selectedSong,
                DatabaseSongId = 77
            };

            var savedRoots = TakeLibraryRoots(library);
            var roots = GetPrivateField<List<SongListNode>>(library, "_rootSongs")!;
            roots.Clear();
            roots.Add(rootNode);
            try
            {
                Assert.True(tabs.ToggleBookmark(node));

                Assert.True(selectedSong.IsBookmarked);           // direct flip on the selected node
                Assert.True(rootNode.DatabaseSong!.IsBookmarked); // reconciled via persisted id
            }
            finally
            {
                RestoreLibraryRoots(library, savedRoots);
                scope.Retire();
            }
        }

        [Fact]
        public void ToggleBookmark_WhenNoPersistedIdAndZeroEntityId_ShouldFlipSelectedNodeOnlyAndSkipWriter()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(library, scope, setBookmarkAsync: writer.Writer());
            var selectedSong = new SongEntity { Id = 0, Title = "S", IsBookmarked = false };
            var node = new SongListNode { Type = NodeType.Score, Title = "S", DatabaseSong = selectedSong, DatabaseSongId = null };
            var bystanderSong = new SongEntity { Id = 0, Title = "Other", IsBookmarked = false };
            var bystander = new SongListNode { Type = NodeType.Score, Title = "Other", DatabaseSong = bystanderSong, DatabaseSongId = null };

            var savedRoots = TakeLibraryRoots(library);
            var roots = GetPrivateField<List<SongListNode>>(library, "_rootSongs")!;
            roots.Clear();
            roots.Add(bystander);
            try
            {
                Assert.False(tabs.ToggleBookmark(node)); // no full persistence path ran

                // The selected node's optimistic flip is allowed to stand (visual feedback);
                // the shared path is skipped: the bystander zero-id node is untouched and
                // no persistence is queued under the sentinel key 0.
                Assert.True(selectedSong.IsBookmarked);
                Assert.False(bystanderSong.IsBookmarked);
                Assert.Equal(0, writer.InvokedCount);
            }
            finally
            {
                RestoreLibraryRoots(library, savedRoots);
                scope.Retire();
            }
        }

        [Fact]
        public async Task ToggleBookmark_ShouldChainWritesPerSongSoLastIntentWins()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(library, scope, setBookmarkAsync: writer.Writer());
            var node = Score("Song", 42);

            tabs.ToggleBookmark(node); // ON — write held in flight
            var first = writer.AwaitWriteRegistered(0);
            tabs.ToggleBookmark(node); // OFF — must chain after the first write
            Assert.Equal(1, writer.InvokedCount); // the second write cannot start yet

            first.Gate.SetResult(true);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));

            writer.AwaitWriteInvoked(2);
            var second = writer.Writes[1];
            Assert.Equal(42, second.SongId);
            Assert.False(second.State); // last user intent
            scope.Retire();
        }

        // INDEPENDENT GENERATIONS: a bookmark toggle invalidates in-flight BOOKMARKS
        // queries only — an in-flight RECENT query must still apply its result, and the
        // deferred bookmarks request id must stay untouched (the owed reload survives).
        [Fact]
        public async Task ToggleBookmark_ShouldBumpBookmarksQueryGenerationOnly()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var recentLoader = new ControlledLoader();
            var bookmarkLoader = new ControlledLoader();
            var writer = new ControlledWriter();
            var tabs = CreateTabs(
                library, scope,
                loadRecentPlaysAsync: recentLoader.Loader(),
                loadBookmarkedNodesAsync: bookmarkLoader.Loader(),
                setBookmarkAsync: writer.Writer());

            // Launch one in-flight query per flat tab (Bookmarks via the real refresh
            // request — no writes pending → immediate launch; both return worker tasks).
            var recentTask = tabs.BeginRecentPlaysLoad();
            var recent = recentLoader.LoadWhenRegistered(0);
            await recent.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var bookmarksTask = tabs.RequestBookmarksRefresh();
            var bookmarks = bookmarkLoader.LoadWhenRegistered(0);
            await bookmarks.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var node = Score("A", 11);
            tabs.ToggleBookmark(node);

            // The RECENT result still applies (toggle did not touch the Recent generation).
            recent.Result.SetResult(new List<SongListNode> { Score("Recent", 1) });
            DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });

            // The BOOKMARKS result is rejected (toggle bumped the query generation)...
            bookmarks.Result.SetResult(new List<SongListNode> { Score("Stale", 2) });
            // REAL COMPLETION BARRIER: the stale terminal is ENQUEUED once the query's
            // worker task completes; the drain that follows provably processes it.
            await AwaitEnqueuedThenProcessAsync(bookmarksTask, scope);
            Assert.Null(tabs.BookmarkNodes);

            // ...and the owed refresh retries through the same wait path once the
            // toggle's write settles.
            var write = writer.AwaitWriteRegistered(0);
            write.Gate.SetResult(true);
            await write.Task.WaitAsync(TimeSpan.FromSeconds(5));
            DrainUntil(scope, () => bookmarkLoader.InvokedCount == 2,
                "the invalidated bookmarks query was not retried after the toggle's write settled");

            bookmarkLoader.LoadWhenRegistered(1).Result.SetResult(new List<SongListNode>());
            DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 0 });
            scope.Retire();
        }

        #endregion

        #region Real SQLite durability (migrated from stage-level BookmarkToggleTests)

        [Fact]
        public async Task ToggleBookmark_RapidDoubleToggle_ShouldPersistLastIntentInDb()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_order_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                // Initially BOOKMARKED: the expected final state (OFF) differs from the
                // stored value, so an untouched database or a no-op/faulting writer
                // cannot pass — the final un-bookmarked read is only reachable through
                // a successful real write.
                var seed = new SongEntity { Title = "Song", Artist = "A", IsBookmarked = true };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;

                // Observer over the REAL write: SetBookmarkAsync runs unmodified; the test
                // only records the returned task per invocation (invocation order = chain
                // order, because the per-song chain starts write N+1 only after write N).
                var writeTasks = new List<(bool State, Task Task)>();
                var writeTasksGate = new object();
                var firstWriteCompletedWhenSecondEntered = false;
                Func<SongLibrary, int, bool, Task> observingRealWriter = (lib, id, state) =>
                {
                    var write = lib.Queries.SetBookmarkAsync(id, state);
                    lock (writeTasksGate)
                    {
                        if (writeTasks.Count == 1)
                            firstWriteCompletedWhenSecondEntered = writeTasks[0].Task.IsCompleted;
                        writeTasks.Add((state, write));
                    }
                    return write;
                };

                var scope = new StageBackgroundWorkScope();
                var tabs = CreateTabs(manager, scope, setBookmarkAsync: observingRealWriter);
                var node = Score("Song", songId);

                tabs.ToggleBookmark(node); // ON
                tabs.ToggleBookmark(node); // OFF — chained behind the first per-song write

                // Barrier: BOTH serialized invocations registered. The second one can only
                // register after the first write completed (per-song chain), so reaching
                // two registrations already proves the serialized ordering ran.
                Assert.True(
                    SpinWait.SpinUntil(
                        () => { lock (writeTasksGate) return writeTasks.Count >= 2; },
                        TimeSpan.FromSeconds(10)),
                    "the serialized write chain never issued both toggles (a no-op writer cannot pass)");
                Task firstWrite, secondWrite;
                lock (writeTasksGate)
                {
                    Assert.True(writeTasks[0].State, "the first chained write must carry the ON intent");
                    Assert.False(writeTasks[1].State, "the second chained write must carry the OFF intent");
                    firstWrite = writeTasks[0].Task;
                    secondWrite = writeTasks[1].Task;
                }

                Assert.True(firstWriteCompletedWhenSecondEntered,
                    "the second serialized writer entered before the first real write completed");

                // Both REAL writes must have completed successfully (a faulting write
                // fails here, before any persisted-state assertion).
                await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(firstWrite.IsCompletedSuccessfully, "the ON write did not complete successfully");
                Assert.True(secondWrite.IsCompletedSuccessfully, "the OFF write did not complete successfully");

                // The serialized per-song chain settles before the DB read: the disposal
                // drain is the production bounded wait over the pending writes.
                tabs.WaitForPendingWritesAtDisposal();

                var persisted = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Id == songId);
                Assert.False(persisted.IsBookmarked); // last intent (OFF) won over the seeded ON
                scope.Retire();
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // MIGRATED SQLITE #2 (stage-level 798–907, persistence/retirement half): a gated
        // REAL commit still pending when its activation's scope retires lands in the
        // database, and the reentry refresh reloads the row with its difficulty labels
        // intact. The stage-level test keeps only the selection/difficulty/chart-identity
        // integration; the default production write path runs here (no writer seam).
        [Fact]
        public async Task ToggleBookmark_WhenCommitCrossesScopeRetirement_ShouldPersistAndReloadRowWithDifficulty()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_commit_nav_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var basic = new SongChart { Song = seed, FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                var advanced = new SongChart { Song = seed, FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 55 };
                using (var seedContext = db.CreateContext())
                {
                    seedContext.Songs.Add(seed);
                    seedContext.SongCharts.AddRange(basic, advanced);
                    await seedContext.SaveChangesAsync();
                }
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;

                // Gate the REAL write via the writer seam so the commit is provably still
                // pending when navigation retires the activation scope; the seam writes
                // through the RECEIVED library (the toggle-time capture) — the same
                // production contract the default writer implements.
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task? gatedWrite = null;
                var gatedWriteGate = new object();
                Func<SongLibrary, int, bool, Task> gatedRealWriter = (lib, id, state) =>
                {
                    var write = gate.Task.ContinueWith(
                        _ => lib.Queries.SetBookmarkAsync(id, state),
                        TaskScheduler.Default).Unwrap();
                    lock (gatedWriteGate)
                        gatedWrite = write;
                    return write;
                };

                StageBackgroundWorkScope? currentScope = new StageBackgroundWorkScope();
                var tabs = CreateTabs(
                    () => manager,
                    new SongSelectionLibraryIntake(() => manager),
                    () => currentScope,
                    setBookmarkAsync: gatedRealWriter);
                try
                {
                    var node = Score("Song", songId);
                    tabs.ToggleBookmark(node); // optimistic ON; write gated behind the seam
                    Assert.True(node.DatabaseSong!.IsBookmarked);
                    Assert.True(
                        SpinWait.SpinUntil(() => { lock (gatedWriteGate) return gatedWrite != null; }, TimeSpan.FromSeconds(10)),
                        "the writer seam was never invoked");
                    lock (gatedWriteGate)
                        Assert.False(gatedWrite!.IsCompleted, "the write must still be pending before navigation");

                    // Navigation: retire the activation scope while the write is pending.
                    var scopeA = currentScope!;
                    scopeA.Retire();

                    // Release; the real commit lands after navigation.
                    gate.SetResult(true);
                    Task writeTask;
                    lock (gatedWriteGate)
                        writeTask = gatedWrite!;
                    await writeTask.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.True(writeTask.IsCompletedSuccessfully);

                    // The committed write landed in the database (fresh SongDatabase read).
                    using var verifyDb = new SongDatabase(dbPath);
                    await verifyDb.InitializeDatabaseAsync();
                    var persisted = (await new SongCatalogStore(verifyDb).GetSongsAsync()).Single(s => s.Id == songId);
                    Assert.True(persisted.IsBookmarked);

                    // REENTRY: fresh activation scope; the Bookmarks reload defers behind
                    // the settled writes and reloads the row with its difficulty labels.
                    currentScope = new StageBackgroundWorkScope();
                    var scopeB = currentScope;
                    tabs.SwitchToTab(SongSelectionTab.Bookmarks);
                    DrainUntil(scopeB, () => tabs.BookmarkNodes is { Count: 1 },
                        "the reentry Bookmarks reload never applied the reloaded row");

                    var reloaded = tabs.BookmarkNodes!.Single();
                    Assert.Equal(songId, reloaded.DatabaseSongId);
                    Assert.False(string.IsNullOrEmpty(reloaded.DifficultyLabels[0]));
                    Assert.False(string.IsNullOrEmpty(reloaded.DifficultyLabels[1]));
                    Assert.Equal(55, reloaded.Scores[1]?.DifficultyLevel);
                    scopeB!.Retire();
                }
                finally
                {
                    currentScope!.Retire();
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task ToggleBookmark_WhenQueuedTogglesBothFail_ShouldRevertToLastPersistedState()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var logger = new CapturingLogger();
            var firstWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeInvocations = 0;
            Func<SongLibrary, int, bool, Task> writer = (_, _, _) =>
                Interlocked.Increment(ref writeInvocations) == 1
                    ? firstWrite.Task
                    : secondWrite.Task;
            var intake = new SongSelectionLibraryIntake(() => library);
            var tabs = CreateTabs(() => library, intake, () => scope, setBookmarkAsync: writer, logger: logger);
            var node = Score("Song", 42, bookmarked: false);
            intake.Install(Snapshot(101, new[] { node }, new[] { "/library/active" }));

            try
            {
                tabs.ToggleBookmark(node); // ON, held by the first write
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref writeInvocations) == 1, TimeSpan.FromSeconds(10)));
                tabs.ToggleBookmark(node); // OFF, chained behind the first write
                Assert.False(node.DatabaseSong!.IsBookmarked);

                firstWrite.SetException(new InvalidOperationException("first write failed"));
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref writeInvocations) == 2, TimeSpan.FromSeconds(10)));
                secondWrite.SetException(new InvalidOperationException("second write failed"));

                AwaitRollbackHandoff(logger, 2,
                    "both fault rollback handoffs never completed (warnings missing)");
                scope.Drain();

                Assert.False(node.DatabaseSong!.IsBookmarked);
            }
            finally
            {
                scope.Retire();
            }
        }

        [Fact]
        public async Task ToggleBookmark_WhenPersistFaults_ShouldRevertInMemoryFlagAfterDrain()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_revert_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                await db.PurgeDatabaseAsync(); // make the bookmark write fault

                var scope = new StageBackgroundWorkScope();
                var logger = new CapturingLogger();
                var tabs = CreateTabs(manager, scope, logger: logger);
                var node = Score("Song", songId);
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                roots.Add(node);
                try
                {
                    tabs.ToggleBookmark(node); // optimistic flip to true
                    Assert.True(node.DatabaseSong!.IsBookmarked);

                    // ROLLBACK HANDOFF SIGNAL (HPA-802 fix round 2): the fault
                    // continuation is scheduled asynchronously — it cannot be awaited and
                    // completing the write does not prove it ran. It Posts the rollback
                    // into the scope BEFORE logging its warning, so the signal proves the
                    // Post happened; the drain that follows provably processes the posted
                    // rollback on the update thread.
                    AwaitRollbackHandoff(logger, 1,
                        "the fault rollback handoff never completed (no warning logged)");
                    DrainUntil(scope, () => !node.DatabaseSong!.IsBookmarked,
                        "the in-memory flag was not reverted after the failed persist");

                    Assert.False(node.DatabaseSong!.IsBookmarked); // reverted to pre-toggle state
                }
                finally
                {
                    RestoreLibraryRoots(manager, savedRoots);
                    scope.Retire();
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // A committed bookmark persistence task may outlive navigation; only the UI
        // rollback is dropped (the scope retired), never the write itself. On a FAILED
        // write there is no committed intent: the library entity must revert (worker-side,
        // version-guarded). With no newer activation current, the retired activation's UI
        // list keeps the optimistic value until the next activation reset discards it.
        [Fact]
        public async Task ToggleBookmark_WhenScopeRetiresBeforeFault_ShouldRevertSharedEntityButDropUiRollback()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_revert_drop_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                await db.PurgeDatabaseAsync(); // make the bookmark write fault

                var scope = new StageBackgroundWorkScope();
                var logger = new CapturingLogger();
                var recentLoader = new ControlledLoader();
                var tabs = CreateTabs(manager, scope, loadRecentPlaysAsync: recentLoader.Loader(), logger: logger);
                var node = Score("Song", songId);
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                roots.Add(node);
                try
                {
                    // A distinct entity in a per-activation UI list: with no newer
                    // activation current, the dropped rollback never reaches it (only the
                    // shared entity reverts on the worker).
                    var recentEntity = new SongEntity { Id = songId, Title = "Recent", IsBookmarked = false };
                    var recentNode = new SongListNode { Type = NodeType.Score, Title = "Recent", DatabaseSong = recentEntity };
                    tabs.BeginRecentPlaysLoad();
                    recentLoader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { recentNode });
                    DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });

                    tabs.ToggleBookmark(recentNode); // optimistic flip to true
                    Assert.True(node.DatabaseSong!.IsBookmarked);
                    Assert.True(recentEntity.IsBookmarked);

                    // Navigate away before the persist fault lands: retiring the scope drops
                    // the posted UI rollback.
                    scope.Retire();

                    // HANDOFF SIGNAL (HPA-802 fix round 2): the retired-scope drop runs
                    // (worker-side shared revert) strictly before the warning; after the
                    // signal the revert is already observable — no drain, no grace period.
                    AwaitRollbackHandoff(logger, 1,
                        "the fault rollback handoff never completed (no warning logged)");
                    Assert.True(
                        SpinWait.SpinUntil(() => !node.DatabaseSong!.IsBookmarked, TimeSpan.FromSeconds(5)),
                        "the library entity was not reverted after the failed persist");
                    // The per-activation UI list keeps its optimistic value here because
                    // no newer activation exists to receive the cache rollback.
                    Assert.True(recentEntity.IsBookmarked);
                }
                finally
                {
                    RestoreLibraryRoots(manager, savedRoots);
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // Final-review regression: toggling from the RECENT tab optimistically flips a
        // per-activation entity DISTINCT from the game-owned library entity. The fault
        // continuation's worker-side revert must cover the shared snapshot — the state
        // that survives navigation — when the posted UI rollback was dropped.
        [Fact]
        public async Task ToggleBookmark_FromRecent_WhenScopeRetiresBeforeFault_ShouldRevertSharedSnapshotEntity()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_revert_shared_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                await db.PurgeDatabaseAsync(); // make the bookmark write fault

                // Gated REAL write: the fault lands only when the test releases it, after
                // the retirement below (no mock-only substitution — SetBookmarkAsync runs).
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Func<SongLibrary, int, bool, Task> gatedRealWriter = (lib, id, state) =>
                    gate.Task.ContinueWith(
                        _ => lib.Queries.SetBookmarkAsync(id, state),
                        TaskScheduler.Default).Unwrap();

                var scope = new StageBackgroundWorkScope();
                var logger = new CapturingLogger();
                var recentLoader = new ControlledLoader();
                var tabs = CreateTabs(manager, scope, loadRecentPlaysAsync: recentLoader.Loader(), setBookmarkAsync: gatedRealWriter, logger: logger);
                var sharedEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = false };
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                roots.Add(new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = sharedEntity });
                var recentEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = false };
                var recentNode = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = recentEntity };
                try
                {
                    tabs.BeginRecentPlaysLoad();
                    recentLoader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { recentNode });
                    DrainUntil(scope, () => tabs.RecentPlayNodes is { Count: 1 });
                    // Quiet tab set: the Recent rows are already loaded, and SwitchToTab
                    // would launch a SECOND Recent query whose result the test never
                    // supplies — its worker would block forever if it started before the
                    // retirement below.
                    tabs.SetActiveTabQuiet(SongSelectionTab.RecentPlays);

                    tabs.ToggleBookmark(recentNode); // optimistic flip to true
                    Assert.True(recentEntity.IsBookmarked);
                    Assert.True(sharedEntity.IsBookmarked); // reconciled onto the shared snapshot too

                    // Navigate away before the persist fault lands.
                    scope.Retire();

                    // Release the gate as a FAILURE: the write faults against the purged DB;
                    // the posted UI rollback drops and the worker-side revert is the only
                    // path that can make reentry DB-consistent.
                    gate.SetException(new InvalidOperationException("forced fault for the shared-snapshot revert test"));
                    // HANDOFF SIGNAL: the retired-scope drop runs strictly before the
                    // warning, so the shared revert is already observable after it.
                    AwaitRollbackHandoff(logger, 1,
                        "the fault rollback handoff never completed (no warning logged)");
                    Assert.True(
                        SpinWait.SpinUntil(() => !sharedEntity.IsBookmarked, TimeSpan.FromSeconds(5)),
                        "the shared snapshot entity was not reverted after the failed persist");
                }
                finally
                {
                    RestoreLibraryRoots(manager, savedRoots);
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // SET.def duplicate variant: the shared snapshot can hold MULTIPLE entity
        // instances for one song. After navigation and a failed persist, every duplicate
        // must revert.
        [Fact]
        public async Task ToggleBookmark_WhenSnapshotHoldsDuplicateEntities_ShouldRevertEveryDuplicateAfterFailedPersist()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_revert_dups_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                await db.PurgeDatabaseAsync(); // make the bookmark write fault

                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Func<SongLibrary, int, bool, Task> gatedRealWriter = (lib, id, state) =>
                    gate.Task.ContinueWith(
                        _ => lib.Queries.SetBookmarkAsync(id, state),
                        TaskScheduler.Default).Unwrap();

                var scope = new StageBackgroundWorkScope();
                var logger = new CapturingLogger();
                var bookmarkLoader = new ControlledLoader();
                var tabs = CreateTabs(manager, scope, loadBookmarkedNodesAsync: bookmarkLoader.Loader(), setBookmarkAsync: gatedRealWriter, logger: logger);
                var dupStampedEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = true };
                var dupZeroIdEntity = new SongEntity { Id = 0, Title = "Song", IsBookmarked = true };
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                roots.Add(new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = dupStampedEntity });
                roots.Add(new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSongId = songId, DatabaseSong = dupZeroIdEntity });
                var bookmarkEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = true };
                var bookmarkNode = new SongListNode
                {
                    Type = NodeType.Score,
                    Title = "Song",
                    DatabaseSongId = songId,
                    DatabaseSong = bookmarkEntity
                };
                try
                {
                    tabs.SwitchToTab(SongSelectionTab.Bookmarks);
                    DrainUntil(scope, () => bookmarkLoader.InvokedCount >= 1);
                    bookmarkLoader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { bookmarkNode });
                    DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

                    tabs.ToggleBookmark(bookmarkNode); // optimistic flip to false
                    Assert.False(bookmarkEntity.IsBookmarked);
                    Assert.False(dupStampedEntity.IsBookmarked);
                    Assert.False(dupZeroIdEntity.IsBookmarked);

                    scope.Retire();

                    gate.SetException(new InvalidOperationException("forced fault for the duplicate-revert test"));
                    // HANDOFF SIGNAL: the retired-scope drop runs strictly before the
                    // warning, so every duplicate revert is already observable after it.
                    AwaitRollbackHandoff(logger, 1,
                        "the fault rollback handoff never completed (no warning logged)");
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => dupStampedEntity.IsBookmarked && dupZeroIdEntity.IsBookmarked,
                            TimeSpan.FromSeconds(5)),
                        "the shared snapshot duplicate entities were not reverted after the failed persist");
                }
                finally
                {
                    RestoreLibraryRoots(manager, savedRoots);
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // Stale-revert supersession over a REAL queued rollback (the test-only
        // ApplyBookmarkRevert wrapper is deleted): three real toggles queue a serialized
        // per-song chain whose FIRST write faults behind a gate while writes #2/#3 run
        // against the real database. Releasing the gate runs write #1 (fault) then its
        // queued rollback — which is stale because toggles #2/#3 bumped the generation —
        // so the committed last intent (ON) must survive untouched in memory and in the
        // database while the rollback is drained and skipped by the generation guard.
        [Fact]
        public async Task ToggleBookmark_WhenNewerToggleSupersedesFailedWrite_ShouldKeepNewerIntentViaRealQueuedRollback()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_newer_wins_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;

                var scope = new StageBackgroundWorkScope();
                var logger = new CapturingLogger();
                // First invocation: gated and doomed to fault. Later invocations: the REAL
                // SetBookmarkAsync, so the newer writes commit while the older one fails.
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var registered = new List<Task>();
                var registeredGate = new object();
                Func<SongLibrary, int, bool, Task> writer = (lib, id, state) =>
                {
                    Task write;
                    lock (registeredGate)
                    {
                        write = registered.Count == 0
                            ? gate.Task.ContinueWith(
                                _ => Task.FromException<object?>(new InvalidOperationException("write #1 forced fault")),
                                TaskScheduler.Default).Unwrap()
                            : lib.Queries.SetBookmarkAsync(id, state);
                        registered.Add(write);
                    }
                    return write;
                };
                var tabs = CreateTabs(manager, scope, setBookmarkAsync: writer, logger: logger);
                var sharedEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = false };
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                roots.Add(new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = sharedEntity });
                var recentEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = false };
                var recentNode = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = recentEntity };
                try
                {
                    Assert.True(tabs.ToggleBookmark(recentNode)); // toggle #1: ON, generation 1 — write gated
                    Assert.True(sharedEntity.IsBookmarked);
                    // The writer seam runs inside a pool continuation chained behind the
                    // prior write, so registration is asynchronous: barrier, not peek.
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => { lock (registeredGate) return registered.Count >= 1; },
                            TimeSpan.FromSeconds(10)),
                        "write #1 was never registered");

                    tabs.ToggleBookmark(recentNode); // toggle #2: OFF, generation 2
                    Assert.False(sharedEntity.IsBookmarked);
                    lock (registeredGate)
                        Assert.Single(registered); // write #2 chains behind the gated #1

                    tabs.ToggleBookmark(recentNode); // toggle #3: ON, generation 3 — last intent
                    Assert.True(sharedEntity.IsBookmarked);
                    lock (registeredGate)
                        Assert.Single(registered); // write #3 chains behind #2 (still gated)

                    // Release write #1: it faults, its rollback is queued, and the chain
                    // settles OFF->ON in the database through the real writes.
                    gate.SetResult(true);
                    Task first;
                    Task second;
                    Task third;
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => { lock (registeredGate) return registered.Count >= 3; },
                            TimeSpan.FromSeconds(10)),
                        "writes #2/#3 were never registered");
                    lock (registeredGate)
                    {
                        first = registered[0];
                        second = registered[1];
                        third = registered[2];
                    }
                    try
                    {
                        await first.WaitAsync(TimeSpan.FromSeconds(10));
                        Assert.Fail("write #1 was expected to fault");
                    }
                    catch (InvalidOperationException)
                    {
                        // the forced fault of the older write
                    }

                    await second.WaitAsync(TimeSpan.FromSeconds(10));
                    await third.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.True(first.IsFaulted); // the forced fault of the older write
                    Assert.True(second.IsCompletedSuccessfully); // write #2 committed
                    Assert.True(third.IsCompletedSuccessfully); // the newer intent committed

                    // ROLLBACK HANDOFF SIGNAL (HPA-802 fix round 2): the rollback
                    // continuation is scheduled asynchronously — completing the write
                    // tasks does NOT prove it ran (a continuation can always run later
                    // than its antecedent's observable completion; inlining is never
                    // guaranteed). The continuation Posts the stale rollback into the
                    // live scope BEFORE logging its warning, so after the signal a single
                    // drain provably processes the queued stale rollback (the generation
                    // guard skips it). No sentinel, no grace period, no inlining claim.
                    AwaitRollbackHandoff(logger, 1,
                        "the stale rollback handoff never completed (no warning logged)");
                    tabs.WaitForPendingWritesAtDisposal();
                    scope.Drain();

                    // The last intent stands in memory and in the database: a missing
                    // generation guard would flip the shared entity back to OFF here.
                    Assert.True(sharedEntity.IsBookmarked,
                        "the superseded generation-1 rollback reverted the newer committed intent");
                    var persisted = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Id == songId);
                    Assert.True(persisted.IsBookmarked);
                }
                finally
                {
                    RestoreLibraryRoots(manager, savedRoots);
                    scope.Retire();
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // A persist fault from activation A can land after the player has already
        // re-entered Song Select in activation B. The shared entity still reverts on the
        // worker, and the current activation's Recent cache must reconcile the same
        // version-guarded rollback; otherwise a row loaded from the pre-write database
        // keeps the optimistic star forever.
        [Fact]
        public async Task ToggleBookmark_WhenFaultLandsAfterScopeSwap_ShouldReconcileCurrentActivationRecentCache()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_revert_react_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                await db.PurgeDatabaseAsync(); // make the bookmark write fault once it runs

                var recentLoader = new ControlledLoader();
                var writerHarness = new GatedRealWriterHarness();

                StageBackgroundWorkScope? currentScope = new StageBackgroundWorkScope();
                StageBackgroundWorkScope? scopeA = null;
                StageBackgroundWorkScope? scopeB = null;
                var logger = new CapturingLogger();
                var tabs = CreateTabs(
                    () => manager,
                    new SongSelectionLibraryIntake(() => manager),
                    () => currentScope,
                    loadRecentPlaysAsync: recentLoader.Loader(),
                    setBookmarkAsync: writerHarness.Writer(),
                    logger: logger);
                var sharedEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = false };
                var sharedNode = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = sharedEntity };
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                roots.Add(sharedNode);
                try
                {
                    tabs.ToggleBookmark(sharedNode); // optimistic ON in activation A
                    Assert.True(sharedEntity.IsBookmarked);
                    await writerHarness.Started.WaitAsync(TimeSpan.FromSeconds(5));

                    // Navigate away, then re-enter before the write fails. Activation B's
                    // warm Recent query reads the pre-write OFF state from the database,
                    // while the shared tree still carries the optimistic ON.
                    scopeA = currentScope!;
                    scopeA.Retire();
                    currentScope = null;
                    currentScope = new StageBackgroundWorkScope();
                    scopeB = currentScope;
                    tabs.ResetForActivation();
                    tabs.BeginRecentPlaysLoad();
                    var reentryLoad = recentLoader.LoadWhenRegistered(0);
                    await reentryLoad.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var reentryEntity = new SongEntity { Id = songId, Title = "Recent", IsBookmarked = false };
                    var reentryNode = new SongListNode { Type = NodeType.Score, Title = "Recent", DatabaseSong = reentryEntity };
                    reentryLoad.Result.SetResult(new List<SongListNode> { reentryNode });
                    DrainUntil(scopeB!, () => tabs.RecentPlayNodes is { Count: 1 });
                    Assert.True(reentryEntity.IsBookmarked);

                    // Release the real write; it faults against the purged database. The
                    // rollback handoff runs before the warning, so the drain below is a
                    // deterministic application barrier for anything handed to scope B.
                    writerHarness.ReleaseRealWrite();
                    AwaitRollbackHandoff(logger, 1,
                        "the rollback handoff never completed (no warning logged)");
                    Assert.True(
                        SpinWait.SpinUntil(() => !sharedEntity.IsBookmarked, TimeSpan.FromSeconds(5)),
                        "the shared entity was not reverted after the failed persist");

                    scopeB!.Drain();
                    Assert.False(reentryEntity.IsBookmarked,
                        "the current activation's Recent cache kept the stale optimistic bookmark");
                }
                finally
                {
                    writerHarness.Cleanup(tabs, logger, currentScope, scopeA, scopeB);
                    RestoreLibraryRoots(manager, savedRoots);
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // A persist fault from activation A can also land after re-entry while activation B
        // is on the Bookmarks tab: RevertDroppedBookmarkRollback forwards ApplyBookmarkRevertCore
        // to B, which reconciles the shared/intake state and calls RequestBookmarksRefresh so
        // an un-bookmarked row reloads as bookmarked. Uses SetActiveTabQuiet(Bookmarks) so
        // setup does not queue its own refresh and the submission count isolates the reload
        // triggered by the forwarded callback itself.
        [Fact]
        public async Task ToggleBookmark_WhenUnbookmarkFaultLandsAfterScopeSwapOnBookmarksTab_ShouldReloadBookmarks()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_revert_react_bm_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A", IsBookmarked = true };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                await db.PurgeDatabaseAsync(); // make the bookmark write fault once it runs

                var bookmarkLoader = new ControlledLoader();
                var submissions = new SubmissionRecorder();
                var writerHarness = new GatedRealWriterHarness();

                StageBackgroundWorkScope? currentScope = new StageBackgroundWorkScope();
                StageBackgroundWorkScope? scopeA = null;
                StageBackgroundWorkScope? scopeB = null;
                var logger = new CapturingLogger();
                var tabs = CreateTabs(
                    () => manager,
                    new SongSelectionLibraryIntake(() => manager),
                    () => currentScope,
                    loadBookmarkedNodesAsync: bookmarkLoader.Loader(),
                    setBookmarkAsync: writerHarness.Writer(),
                    logger: logger,
                    submitTabLoadQuery: submissions.Seam());
                var sharedEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = true };
                var sharedNode = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = sharedEntity };
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                roots.Add(sharedNode);
                try
                {
                    tabs.ToggleBookmark(sharedNode); // optimistic OFF in activation A
                    Assert.False(sharedEntity.IsBookmarked);
                    await writerHarness.Started.WaitAsync(TimeSpan.FromSeconds(5));

                    // Navigate away and re-enter on the Bookmarks tab quietly (without
                    // queueing a tab-switch refresh) before the write fails.
                    scopeA = currentScope!;
                    scopeA.Retire();
                    currentScope = new StageBackgroundWorkScope();
                    scopeB = currentScope;
                    tabs.ResetForActivation();
                    tabs.SetActiveTabQuiet(SongSelectionTab.Bookmarks);
                    Assert.Equal(SongSelectionTab.Bookmarks, tabs.ActiveTab);
                    Assert.Equal(0, submissions.Submissions);
                    Assert.Equal(0, bookmarkLoader.InvokedCount);

                    // Release the real write; it faults against the purged database. The
                    // rollback handoff runs before the warning, reverting the shared entity
                    // and posting ApplyBookmarkRevertCore into scope B.
                    writerHarness.ReleaseRealWrite();
                    AwaitRollbackHandoff(logger, 1,
                        "the rollback handoff never completed (no warning logged)");
                    Assert.True(
                        SpinWait.SpinUntil(() => sharedEntity.IsBookmarked, TimeSpan.FromSeconds(5)),
                        "the shared entity was not reverted after the failed un-bookmark");
                    Assert.Equal(0, submissions.Submissions);

                    // Draining scope B executes the forwarded ApplyBookmarkRevertCore callback,
                    // which must itself request and submit the Bookmarks reload.
                    scopeB!.Drain();
                    Assert.Equal(1, submissions.Submissions);

                    int nextLoad = 0;
                    DrainUntil(scopeB, () =>
                    {
                        while (nextLoad < bookmarkLoader.InvokedCount)
                        {
                            bookmarkLoader.LoadAt(nextLoad).Result.TrySetResult(
                                new List<SongListNode> { Score("Song", songId, bookmarked: true) });
                            nextLoad++;
                        }
                        return tabs.BookmarkNodes is { Count: 1 };
                    }, "the re-entry Bookmarks tab did not reload the persisted bookmark after the failed un-bookmark");

                    Assert.Equal(1, submissions.Submissions);
                    Assert.Equal(1, bookmarkLoader.InvokedCount);
                    Assert.False(tabs.BookmarksLoadFailed);
                    var rows = tabs.BuildBookmarksRows();
                    Assert.Single(rows);
                    Assert.True(rows[0].DatabaseSong!.IsBookmarked);
                    Assert.False(tabs.ShowEmptyBookmarksMessage);
                }
                finally
                {
                    bookmarkLoader.ReleaseAll();
                    writerHarness.Cleanup(tabs, logger, currentScope, scopeA, scopeB);
                    RestoreLibraryRoots(manager, savedRoots);
                }
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        // MIGRATED SQLITE #3 (stage-level 1120–1237) + CAPTURED-LIBRARY WRITE REGRESSION
        // (HPA-802 review): writes queued while the accessor is REPLACED by another
        // library still land in the ORIGINAL database, because the queued write executes
        // against the library captured at TOGGLE time — the write path must never
        // re-resolve the accessor. The regression drives the production write path with
        // the toggle-time capture and asserts every write received exactly that instance
        // while the accessor pointed elsewhere; the bounded disposal drain waits for the
        // writes; and a later fault rollback reverts the CAPTURED library's shared entity
        // after the accessor cleared.
        [Fact]
        public async Task WaitForPendingWritesAtDisposal_WhenLibraryClearedDuringQueuedWrites_ShouldStillPersistAndRevertCapturedLibrary()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_shutdown_{Guid.NewGuid():N}.db");
            var decoyPath = Path.Combine(Path.GetTempPath(), $"tabs_shutdown_decoy_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            var decoy = new SongLibrary(new SongDatabase(decoyPath));
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFaultGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A", IsBookmarked = false };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;

                // Gated REAL writes: the first write of each phase is held by its gate;
                // later writes chain behind it (per-song serialization) and run against
                // the REAL database of the RECEIVED library — the production-supplied
                // toggle-time capture, not a fixture-captured manager.
                var received = new List<SongLibrary>();
                var receivedGate = new object();
                Func<TaskCompletionSource<bool>, Func<SongLibrary, int, bool, Task>> makeWriter = gate => (lib, id, state) =>
                {
                    lock (receivedGate)
                        received.Add(lib);
                    return gate.Task.ContinueWith(
                        _ => lib.Queries.SetBookmarkAsync(id, state),
                        TaskScheduler.Default).Unwrap();
                };

                SongLibrary? current = manager;
                var savedRoots = TakeLibraryRoots(manager);
                var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                roots.Clear();
                var nodeEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = false };
                var node = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSong = nodeEntity };
                roots.Add(node);

                try
                {
                    var scope = new StageBackgroundWorkScope();
                    var tabs = CreateTabs(
                        () => current!,
                        new SongSelectionLibraryIntake(() => current!),
                        () => scope,
                        setBookmarkAsync: makeWriter(releaseFirst));

                    // Queue three rapid toggles while write #1 is still blocked: ON -> OFF -> ON.
                    tabs.ToggleBookmark(node);
                    tabs.ToggleBookmark(node);
                    tabs.ToggleBookmark(node);
                    Assert.True(nodeEntity.IsBookmarked);

                    // CAPTURED-LIBRARY WRITE REGRESSION: while write #1 is still gated,
                    // REPLACE the accessor with a DIFFERENT library. The queued writes must
                    // still land in the ORIGINAL database — and every seam invocation must
                    // have received the toggle-time capture, never the decoy.
                    current = decoy;

                    // Simulate shutdown disposing the original library before the queued
                    // writes run; the disposal drain must still complete them against the
                    // captured library.
                    manager.Dispose();

                    var drainStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var disposeWait = Task.Run(() =>
                    {
                        drainStarted.SetResult(true);
                        tabs.WaitForPendingWritesAtDisposal();
                    });
                    await drainStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(disposeWait.Wait(TimeSpan.FromMilliseconds(200)),
                        "the disposal drain must not finish while a write is gated");
                    releaseFirst.SetResult(true);
                    Assert.True(disposeWait.Wait(TimeSpan.FromSeconds(10)),
                        "the disposal drain did not finish after the gated write was released");

                    lock (receivedGate)
                    {
                        Assert.Equal(3, received.Count);
                        Assert.All(received, lib => Assert.Same(manager, lib));
                    }

                    var verifyDb = new SongDatabase(dbPath);
                    await verifyDb.InitializeDatabaseAsync();
                    var persisted = (await new SongCatalogStore(verifyDb).GetSongsAsync()).Single(s => s.Id == songId);
                    Assert.True(persisted.IsBookmarked);
                    using var verifyDbOwner = verifyDb;

                    // Also verify fault rollback uses the captured library after the
                    // accessor CLEARED: a fresh tabs owner toggles OFF behind a gate,
                    // retires its scope, the accessor clears, then the gate releases; the
                    // write faults against the purged DB and the captured library's shared
                    // entity must revert.
                    current = manager;
                    await db.PurgeDatabaseAsync();
                    var scope2 = new StageBackgroundWorkScope();
                    var logger2 = new CapturingLogger();
                    var tabs2 = CreateTabs(
                        () => current!,
                        new SongSelectionLibraryIntake(() => current!),
                        () => scope2,
                        setBookmarkAsync: makeWriter(releaseFaultGate),
                        logger: logger2);
                    tabs2.ToggleBookmark(node); // optimistic OFF
                    Assert.False(nodeEntity.IsBookmarked);
                    scope2.Retire();
                    current = null;
                    Assert.False(nodeEntity.IsBookmarked);

                    releaseFaultGate.SetException(new InvalidOperationException("forced persist fault"));
                    // HANDOFF SIGNAL: the retired-scope drop reverts the captured library
                    // strictly before the warning; after it the revert is observable.
                    AwaitRollbackHandoff(logger2, 1,
                        "the captured-library fault rollback handoff never completed (no warning logged)");
                    Assert.True(
                        SpinWait.SpinUntil(() => nodeEntity.IsBookmarked, TimeSpan.FromSeconds(5)),
                        "captured library entity did not revert after the game library cleared");
                }
                finally
                {
                    RestoreLibraryRoots(manager, savedRoots);
                }
            }
            finally
            {
                releaseFirst.TrySetResult(true);
                releaseFaultGate.TrySetResult(true);
                manager.Dispose();
                decoy.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                SqliteTestPools.ClearForFile(decoyPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
                try { File.Delete(decoyPath); } catch { /* ignore */ }
            }
        }

        // DEFAULT-WRITER REGRESSION (HPA-802 review follow-up, finding 1): a queued write
        // must execute against the library CAPTURED AT TOGGLE TIME even when the accessor
        // is repointed mid-chain, and the decoy library must never be written. This test
        // uses the UNMODIFIED default production writer (no writer seam). Deterministic
        // gate: a held write transaction on the ORIGINAL database FILE blocks the first
        // queued write (CommandTimeout 30 s vs a millisecond-scale release); the per-song
        // chain then guarantees the second write cannot start until the first completes —
        // strictly after the test repointed the accessor to the decoy. A writer that
        // re-resolved the accessor at execution time would therefore resolve the DECOY
        // for the second intent and both persisted-value assertions below would fail.
        [Fact]
        public async Task DefaultWriter_WhenAccessorRepointedDuringQueuedChain_ShouldPersistToCapturedLibraryAndNeverTheDecoy()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_default_writer_{Guid.NewGuid():N}.db");
            var decoyPath = Path.Combine(Path.GetTempPath(), $"tabs_default_writer_decoy_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            var decoy = new SongLibrary(new SongDatabase(decoyPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                await decoy.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                // Initially BOOKMARKED in the CAPTURED database: the expected final value
                // (un-bookmarked) DIFFERS from the stored value, so an entirely failed or
                // no-op write chain leaves the row bookmarked and fails the assertions
                // below — the test cannot pass without a successful real write landing in
                // this database. Any stray write to the decoy is caught by the decoy
                // assertions further down (its seeded row is bookmarked too; an OFF write
                // would flip it).
                var seed = new SongEntity { Title = "Song", Artist = "A", IsBookmarked = true };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                // The decoy copy is seeded BOOKMARKED so any stray write to it is visible.
                var decoySeed = new SongEntity { Title = "Song", Artist = "A", IsBookmarked = true };
                var decoyChart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await decoy.Database.SeedSongAsync(decoySeed, decoyChart);

                using (var locker = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
                {
                    locker.Open();
                    using var lockTx = locker.BeginTransaction();
                    using (var lockCmd = locker.CreateCommand())
                    {
                        lockCmd.Transaction = lockTx;
                        lockCmd.CommandText = "CREATE TABLE IF NOT EXISTS write_gate (id INTEGER); INSERT INTO write_gate VALUES (1);";
                        lockCmd.ExecuteNonQuery();
                    }

                    // NO writer seam: the default production writer runs.
                    SongLibrary? current = manager;
                    var scope = new StageBackgroundWorkScope();
                    var tabs = new SongSelectionTabs(
                        () => current!,
                        new SongSelectionLibraryIntake(() => current!),
                        () => scope,
                        NoLogger);

                    var savedRoots = TakeLibraryRoots(manager);
                    var roots = GetPrivateField<List<SongListNode>>(manager, "_rootSongs")!;
                    roots.Clear();
                    var nodeEntity = new SongEntity { Id = songId, Title = "Song", IsBookmarked = false };
                    var node = new SongListNode { Type = NodeType.Score, Title = "Song", DatabaseSongId = songId, DatabaseSong = nodeEntity };
                    roots.Add(node);
                    try
                    {
                        tabs.ToggleBookmark(node); // T0: ON  → W0 queued (captured manager), blocked on the file lock
                        tabs.ToggleBookmark(node); // T1: OFF → W1 chained behind W0, cannot start yet

                        // Repoint the accessor while W1 is deterministically still queued.
                        current = decoy;

                        // Release the file lock: W0 completes against the CAPTURED manager,
                        // and only then does W1 execute — against whatever its write used.
                        lockTx.Commit();
                    }
                    finally
                    {
                        RestoreLibraryRoots(manager, savedRoots);
                    }

                    // Bounded production drain: returns only when BOTH writes settled.
                    tabs.WaitForPendingWritesAtDisposal();

                    // The T1 intent (OFF) landed in the ORIGINALLY CAPTURED database ...
                    var verifyDb = new SongDatabase(dbPath);
                    await verifyDb.InitializeDatabaseAsync();
                    var persisted = (await new SongCatalogStore(verifyDb).GetSongsAsync()).Single(s => s.Id == songId);
                    Assert.False(persisted.IsBookmarked,
                        "the second queued write did not land in the captured library's database");

                    // ... and the decoy was never written: its seeded bookmarked row is intact.
                    var verifyDecoy = new SongDatabase(decoyPath);
                    await verifyDecoy.InitializeDatabaseAsync();
                    var decoyPersisted = (await new SongCatalogStore(verifyDecoy).GetSongsAsync()).Single(s => s.Title == "Song");
                    Assert.True(decoyPersisted.IsBookmarked,
                        "a queued write hit the decoy library the accessor had been repointed to");

                    scope.Retire();
                }
            }
            finally
            {
                manager.Dispose();
                decoy.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                SqliteTestPools.ClearForFile(decoyPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
                try { File.Delete(decoyPath); } catch { /* ignore */ }
            }
        }

        // FAILED UNBOOKMARK RESTORATION: an un-bookmark on the Bookmarks tab wrongly
        // removes the cached row when the persist faults; the posted rollback restores
        // the in-memory state and re-syncs from the authoritative store so the row
        // reappears.
        [Fact]
        public async Task ToggleBookmark_UnbookmarkOnBookmarksTab_WhenWriteFaults_ShouldRestoreTheRow()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tabs_restore_{Guid.NewGuid():N}.db");
            var manager = new SongLibrary(new SongDatabase(dbPath));
            try
            {
                await manager.InitializeAsync(purgeDatabaseFirst: true);
                var db = manager.Database;
                var seed = new SongEntity { Title = "Song", Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(seed, chart);
                var songId = (await new SongCatalogStore(db).GetSongsAsync()).Single(s => s.Title == "Song").Id;
                await db.PurgeDatabaseAsync(); // make the write fault

                var loader = new ControlledLoader();
                var scope = new StageBackgroundWorkScope();
                var logger = new CapturingLogger();
                var tabs = CreateTabs(manager, scope, loadBookmarkedNodesAsync: loader.Loader(), logger: logger);
                var node = Score("Song", songId, bookmarked: true);

                tabs.SwitchToTab(SongSelectionTab.Bookmarks);
                DrainUntil(scope, () => loader.InvokedCount >= 1);
                loader.LoadWhenRegistered(0).Result.SetResult(new List<SongListNode> { node });
                DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

                tabs.ToggleBookmark(node); // removes the row optimistically
                Assert.Empty(tabs.BookmarkNodes!);

                // The fault rollback restores the in-memory state and re-syncs the tab
                // from the authoritative store (a new bookmarks query runs). HANDOFF
                // SIGNAL first: the rollback Post happens before the warning; the drains
                // that follow apply the posted rollback, which launches the re-sync.
                AwaitRollbackHandoff(logger, 1,
                    "the fault rollback handoff never completed (no warning logged)");
                DrainUntil(scope, () => loader.InvokedCount == 2,
                    "the rollback's authoritative re-sync did not run");
                loader.LoadWhenRegistered(1).Result.SetResult(new List<SongListNode> { Score("Song", songId, bookmarked: true) });
                DrainUntil(scope, () => tabs.BookmarkNodes is { Count: 1 });

                Assert.True(tabs.BookmarkNodes![0].DatabaseSong!.IsBookmarked);
                scope.Retire();
            }
            finally
            {
                manager.Dispose();
                SqliteTestPools.ClearForFile(dbPath);
                try { File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        #endregion

        #region Tab state

        [Fact]
        public void SwitchToTab_ShouldSetActiveTabAndFlagRefresh()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope);

            tabs.SwitchToTab(SongSelectionTab.RecentPlays);

            Assert.Equal(SongSelectionTab.RecentPlays, tabs.ActiveTab);
            Assert.True(tabs.TabListNeedsRefresh);
            scope.Retire();
        }

        [Fact]
        public void SetActiveTabQuiet_ShouldSetTabWithoutRefreshingLoadState()
        {
            using var library = CreateLibrary();
            var scope = new StageBackgroundWorkScope();
            var tabs = CreateTabs(library, scope);
            tabs.RequestAsyncTabListRefresh();

            tabs.SetActiveTabQuiet(SongSelectionTab.Bookmarks);

            Assert.Equal(SongSelectionTab.Bookmarks, tabs.ActiveTab);
            Assert.True(tabs.TabListNeedsRefresh); // untouched by the quiet set
            scope.Retire();
        }

        #endregion

        #region Helpers

        private static Func<Task<List<SongListNode>>> FaultingLoader() => () =>
            Task.FromException<List<SongListNode>>(new InvalidOperationException("database unavailable"));

        #endregion
    }
}
