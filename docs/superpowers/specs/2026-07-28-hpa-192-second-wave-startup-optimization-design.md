# HPA-192 Second-Wave Startup Optimization Design

**Issue:** [HPA-192](https://linear.app/cwchanap/issue/HPA-192/optimize-fresh-startup-song-loading-with-batched-sqlite-import)  
**Date:** 2026-07-28  
**Status:** Approved for implementation planning

## Context

The first HPA-192 implementation replaced per-chart persistence and reloads
with parse-then-import enumeration, one `SongDbContext`, one explicit SQLite
transaction, one `SaveChangesAsync`, one hierarchy publication, and one
aggregate startup summary.

The balanced six-run benchmark on the frozen 100-chart corpus produced:

- Original baseline median launch-to-Title wall time: 7,402 ms.
- First-wave optimized median launch-to-Title wall time: 3,888 ms.
- Improvement: 47.47 percent.
- Required 70-percent external threshold: 2,221 ms or less.

The first wave passes the separate eight-second target but misses the required
relative improvement.

The optimized median startup summary is 2,144 ms. Its principal song-loading
costs are:

- Database initialization: 1,082 ms.
- Discovery and parsing: 143 ms.
- Persistence: 449 ms.
- Cleanup: 1 ms.
- Hierarchy construction: 4 ms.

The component medians total 1,679 ms. Subtracting them from the 2,144 ms
median Startup summary leaves a 465 ms diagnostic residual that includes
startup graphics/font work and frame-by-frame phase progression.

The median difference between external launch-to-Title time and the
Startup-activation-to-summary duration is approximately 1,744 ms. The paired
first-wave differences were 1,651 ms, 1,744 ms, and 1,753 ms. This interval
contains both work before Startup activation and the explicit one-second
Startup-to-Title transition after the summary. It is not an overlap window.
Only the portion after configuration loading and before Startup activation
can hide coordinator work; that portion has not yet been measured.

This timing shape makes either isolated optimization insufficient:

- The required external reduction is 1,667 ms: 3,888 minus 2,221.
- An impossible best case that removes all 1,679 ms of measured song-phase
  medians gives 2,209 ms: 3,888 minus 1,679. This leaves only 12 ms below the
  gate before any scheduling, polling, or contention noise.
- Bounded phase draining can eliminate at most eight extra update intervals
  when the coordinator is terminal before Startup's first update. At 60 Hz
  that ceiling is approximately 133 ms, so draining is the smallest lever.

The second wave therefore combines launch-time overlap, removal of serialized
display-only frames, and a guarded direct-SQLite path for a database created
during the same launch.

The first-wave implementation and its contracts remain the behavioral
baseline. In particular, this design does not weaken atomic import,
chart-path identity, user-data preservation, cancellation, fallback,
publish-after-commit, or startup lifecycle fencing.

## Goals

- Reduce the median external launch-to-Title wall time to 2,221 ms or less on
  the frozen 100-chart corpus.
- Achieve at least a 70-percent improvement against the original 7,402 ms
  baseline median.
- Begin the startup song operation as soon as configuration and application
  paths are available.
- Overlap database initialization, enumeration, and persistence with MonoGame
  graphics, content, and stage initialization.
- Retain exactly one startup song operation per game launch.
- Avoid frame-by-frame serialization when coordinator milestones have already
  completed.
- Preserve at least one rendered Startup frame before Title.
- Add a guarded direct-SQLite fresh-import path that supplies timing margin
  without changing existing-database behavior.
- Preserve one atomic transaction, cancellation rollback, deterministic IDs,
  and publish-after-commit semantics.
- Keep the existing aggregate summary fields compatible while exposing
  overlap and persistence-path diagnostics.
- Verify correctness with unit, integration, lifecycle, full-suite, and
  balanced benchmark evidence.

## Non-goals

- Parser I/O redesign.
- Parallel chart parsing.
- More than one SQLite writer.
- Changes to DTX, GDA, G2D, BMS, BME, or BML parsing semantics.
- Changes to `set.def` or `box.def` grouping.
- Changes to canonical or legacy-alias chart-path identity.
- Changes to existing-database migration or preservation semantics.
- Enabling, retiring, or configuring the hard-coded force-enumeration
  behavior.
- SQLite journal-mode changes.
- ReadyToRun, native AOT, packaging, or runtime-distribution changes.
- Song-selection preview or first-selected-song asset optimization.
- Replacing EF Core as the schema authority or as the normal
  existing-database data-access path.
- Changing the external acceptance metric after the first-wave miss.

## Chosen Approach

The second wave uses a staged hybrid:

1. A launch-scoped coordinator starts the existing logical song-load operation
   immediately after configuration loading.
2. `StartupStage` attaches to that operation and drains milestones that have
   already completed instead of starting or serializing them.
3. A database created during the same launch may use a direct,
   prepared-command SQLite writer after strict freshness and emptiness guards.
4. Existing databases continue through the complete EF preload, reconciliation,
   migration, preservation, cleanup, and save path.

Two narrower alternatives were rejected:

- **Overlap only:** the idealized result leaves approximately 12 ms of margin,
  so ordinary scheduling or CPU contention could fail the acceptance gate.
- **Database tuning only:** even removing all measured initialization and
  persistence time would not provide a robust external 70-percent result.

Packaging/runtime startup changes were also rejected for this wave because
they broaden deployment risk and are unnecessary until the in-process overlap
and startup-stage serialization costs are addressed.

## Pre-Implementation Timing Gate

Before implementing the coordinator, fresh-initialization split, or direct
writer, an instrumentation-only Task 0 based on first-wave product commit
`c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b` measures the Release build on the
frozen corpus. It changes telemetry only and records process-monotonic
milestones for:

1. Process entry.
2. Configuration loaded and the earliest legal coordinator start.
3. `LoadContent` completed.
4. Startup activated.
5. The first Startup frame rendered.
6. The startup summary emitted and the Title transition requested.
7. The Startup-to-Title transition completed.

At Title completion it emits exactly one Release-visible `HPA192_TIMING` line
with `entry_to_config_ms`, `config_to_load_content_ms`,
`load_content_to_startup_ms`, `startup_to_first_draw_ms`,
`startup_to_summary_ms`, `summary_to_title_ms`, `entry_to_title_ms`,
`entry_unix_us`, and `title_unix_us`. The durations share one
process-monotonic origin. The UTC microsecond anchors are captured immediately
adjacent to the process-entry and completed-Title monotonic markers; they
bridge the process timeline to the runner timeline without replacing
monotonic interval arithmetic. This line is separate from and does not alter
the single `HPA192_STARTUP` summary contract.

The benchmark runner retains its external launch and Title-observation
timestamps as `launch_start_unix_us` and `launch_end_unix_us`. Three fresh
diagnostic runs use the same machine, corpus, Release configuration, app-data
isolation, and runner rules as the accepted benchmark. The report preserves
every raw interval and the medians. These runs are diagnostic and never count
toward the predetermined acceptance sequence.

The diagnostic computes:

- External launch to Startup activation, which remains on the wall-clock path
  even when coordinator work overlaps it.
- Configuration loaded to Startup activation, the only pre-Startup interval
  available for coordinator overlap.
- Startup activation to first draw.
- Summary/transition request to Title completion, which is not overlapable
  because the coordinator must already be terminal.
- A fixed-cost lower bound consisting of external launch to Startup
  activation, one required Startup frame, and summary-to-Title completion.

The exact bridge and lower bound are:

```text
entry_to_startup_ms =
    entry_to_config_ms +
    config_to_load_content_ms +
    load_content_to_startup_ms

external_launch_to_entry_ms =
    (entry_unix_us - launch_start_unix_us) / 1000

external_launch_to_startup_ms =
    external_launch_to_entry_ms + entry_to_startup_ms

title_poll_lag_ms =
    (launch_end_unix_us - title_unix_us) / 1000

fixed_cost_lower_bound_ms =
    external_launch_to_startup_ms +
    startup_to_first_draw_ms +
    summary_to_title_ms
```

The runner's external `wall_ms` ends only when HTTP polling observes Title.
It is retained as a diagnostic/acceptance value but never used to derive the
fixed-cost lower bound, because doing so would incorrectly include
`title_poll_lag_ms`. Negative, inconsistent, or implausibly skewed anchor
values invalidate that diagnostic run rather than silently changing the
formula.

If that fixed-cost lower bound is at least 2,221 ms, implementation stops
before the higher-risk database tasks because no song-side optimization in
this design can meet the gate. Otherwise the measured remaining budget and
overlap window size the coordinator and direct-writer tasks. Passing this
preflight is permission to implement the wave, not performance acceptance.

## Launch-Scoped Ownership

### Coordinator responsibility

`StartupSongLoadCoordinator` owns the startup song operation for the lifetime
of one `BaseGame` launch. The implementation plan and tests use the
provisional companion names `StartupSongLoadRequest`,
`StartupSongLoadProgressSnapshot`, and `StartupSongLoadResult` so ownership is
unambiguous. Its contract has these roles:

- **Immutable request:** configured song roots, songs database path,
  force-enumeration value, and any options required by the existing startup
  path.
- **Database-ready completion:** a completion point used by the Startup UI to
  advance the database phase.
- **Terminal completion:** one task returning the final selected path, outcome,
  hierarchy readiness, counts, timings, and error.
- **Latest progress snapshot:** immutable, thread-safe progress that a stage
  attaching late can replay immediately.
- **Application cancellation:** a token owned by `BaseGame`, not by one
  `StartupStage` activation.
- **Captured song owner:** the `SongManager` instance obtained on the game
  thread before the background worker starts.

The coordinator is one-shot and idempotent. Repeated calls to start or retrieve
the operation return the same task. It must not run a second database
initialization, enumeration, import, fallback, or hierarchy publication.

The coordinator runs the SQLite and song pipeline on a background worker.
This ensures synchronous EF model construction and Microsoft.Data.Sqlite
commands cannot serialize the main MonoGame initialization thread.

### Earliest safe start

`BaseGame.Initialize` creates and starts the coordinator immediately after:

1. `ConfigManager` is constructed.
2. `ConfigManager.LoadConfig` completes.
3. The configured DTX path and `AppPaths` database path can be captured.
4. `SongManager.Instance` is obtained on the game thread and passed to the
   coordinator.

It starts before graphics-manager construction, `base.Initialize`,
`LoadContent`, resource-manager creation, stage-manager creation, and Startup
activation. The song pipeline has no graphics or content dependency.

The request captures immutable path values. A later configuration mutation
cannot change an in-flight operation.

The current `StartupStage.ForceEnumeration` protected-virtual test seam cannot
survive this ownership move because the request is created before
`StartupStage` exists. `BaseGame` therefore exposes one protected-virtual
`CreateStartupSongLoadRequest` factory. Production creates a request with the
existing hard-coded `forceEnumeration = true`; launch/coordinator tests
override the factory or pass a request with false. Cache-versus-enumeration
decision tests move from `StartupStageLogicTests` to coordinator tests.
Startup-stage tests consume fake coordinator milestones and no longer
override `ForceEnumeration`.

Capturing `SongManager.Instance` before `Task.Run` prevents the existing
double-checked singleton initialization from racing between the game and
worker threads. The worker uses only the captured instance; it does not
resolve the singleton again.

### Launch readiness barrier

While the coordinator is nonterminal, it is the sole production owner of
song hierarchy mutation and song-database access. No other production path
may enumerate `SongManager.RootSongs` or open a song database context during
that interval.

`BaseGame` owns an atomic, launch-monotonic
`ExternalStageChangesReady` flag. It begins false and becomes true on the game
thread only after all of these events have occurred:

1. The coordinator is terminal.
2. Startup has rendered at least one frame.
3. Startup has emitted its one summary.
4. The normal internal Startup-to-Title transition has completed.

The qualifying game-thread update publishes readiness after
`StageManager.Update` has returned with Title current and
`IsTransitioning == false`, and before that frame drains accepted main-thread
actions.

Application shutdown uses a separate shutting-down flag and rejects new API
work; it does not reverse this launch-monotonic readiness value.

`IGameContext` exposes the exact lifecycle bridge used by the API:

```csharp
bool ExternalStageChangesReady { get; }
bool IsShuttingDown { get; }
bool QueueMainThreadAction(Action action);
```

Queue admission and shutdown publication share one lifecycle gate, so the
Boolean result is the atomic admission decision rather than a later
best-effort observation.

`IGameApi.ChangeStageAsync` changes from `Task<bool>` to the exact signature
`Task<StageChangeRequestResult> ChangeStageAsync(string stageName)`. It parses
the requested stage and evaluates external readiness on the API request thread
before calling `QueueMainThreadAction`. While
`ExternalStageChangesReady` is false, every valid external target, including
`Startup`, `Title`, and `SongSelect`, returns not-ready and queues nothing. The
internal
`StageManager.ChangeStage` contract is unchanged, so Startup can still request
the normal Title transition after its terminal and rendered-frame gates.

The API returns a discriminated `StageChangeRequestResult`:

- `Accepted`: the action was queued; this does not promise that the transition
  has completed.
- `UnknownStage`: parsing or enum validation failed.
- `StartupNotReady`: startup lifecycle fencing rejected the request before
  queuing.
- `ShuttingDown`: application teardown rejected the request before or during
  atomic queue admission.

JSON-RPC preserves the existing `InvalidParams` (`-32602`) response for
`UnknownStage`. `StartupNotReady` maps through a new named
`JsonRpcErrorCodes.StartupNotReady = -32004` constant with
`data.reason = "startup_not_ready"` and the requested target in sanitized
data. `ShuttingDown` maps through the existing
`JsonRpcErrorCodes.GameNotRunning = -32001` constant with
`data.reason = "shutting_down"` and the sanitized parsed target. Invalid input
is parsed first and therefore remains `UnknownStage`; a valid request
linearized after shutdown publication is `ShuttingDown`, not
`StartupNotReady`.

The MCP service extracts `JsonRpcException.ErrorCode` and `ErrorData.reason`
into a provisional
`StageChangeServiceResult(Success, Message, ErrorCode, Reason)` return value;
`ErrorCode` and `Reason` are nullable for non-JSON-RPC failures. The MCP
handler includes both fields in its structured result instead of collapsing
them into an undifferentiated text failure. There is no REST stage-change
endpoint in the current product, so this design adds no REST contract.

The signature and wire change explicitly covers:

- `DTXMania.Game/Lib/GameApi.cs`.
- `DTXMania.Game/Lib/GameApiImplementation.cs`.
- `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`.
- `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs`.
- `DTXMania.Game/Lib/JsonRpc/JsonRpcMessage.cs`.
- `MCP/Server/JsonRpcClient.cs` error-data preservation.
- `MCP/Server/GameInteractionService.cs`.
- `MCP/Server/GameInteractionMcpToolHandlers.cs`.
- `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`.
- `DTXMania.E2E/Telemetry/E2EGameState.cs`.
- `DTXMania.E2E/DrumMappingStageSmokeTests.cs`.

`GameTelemetrySnapshot` exposes the same
`ExternalStageChangesReady` value, and `E2EGameState` exposes a typed Boolean
accessor. The readiness field is authoritative; external clients must not
infer readiness from `StageType == Title` alone. The two drum-mapping E2E
flows wait until both Title and external readiness are observed before calling
`ChangeStageAsync`. This avoids a request-thread race between Title becoming
visible and the launch-monotonic flag being published. Their stale comment
claiming that stage type changes when a transition is merely queued is
corrected; `StageManager` publishes the target stage only when the transition
completes.

This prevents an early external request from forcing `SongSelect`, whose
activation reads the live `RootSongs` view and opens recent-play or bookmark
database contexts. Keeping the gate closed through Title-transition
completion also prevents an accepted API request from bypassing the required
Startup frame or summary, or from being queued into an in-progress
Startup-to-Title transition.

Health/state polling, screenshots, and input remain available where they do
not read `SongManager`, open the songs database, or leave Startup. The
ordinary `GetGameState` polling used by the benchmark remains permitted and
database-free. MCP callers use the same game API and therefore inherit the
same readiness gate.

This launch-scoped readiness barrier is the concurrency contract for the
second wave. It prevents a second context using the current `Cache=Shared`
connection from overlapping the coordinator transaction. The design does not
add a global service-wide read/write lease or change the public `RootSongs`
representation; those broader changes are not required once premature stage
activation is fenced.

### Lifetime and disposal

The application owns cancellation because the work can begin before
`StartupStage` exists. Stage deactivation only detaches its view of progress;
it does not cancel or restart the launch operation.

Normal transition to Title occurs only after the coordinator is terminal, so
no `SongManager` mutation continues into Title.

The coordinator cancellation source is owned only by `BaseGame` and is never
stored in Startup activation state. The current load-owning behavior of
`StartupStage.BeginActivationScope`, `RetireActivationScope`, and
`_cancellationTokenSource` is removed from the coordinator path. Activation
and deactivation only advance the activation generation, reset or clear UI
guards, and attach or detach the coordinator view. They never cancel or
observe the coordinator task.

Graceful application shutdown occurs in this order:

1. Mark the application as shutting down so new API actions are rejected.
2. Under the same lifecycle gate, reject queue and screenshot admission,
   then exchange any pending screenshot completion source. After releasing the
   gate, complete that source as canceled before listener shutdown. A later
   screenshot request returns an already-canceled task and cannot install new
   pending work.
3. Cancel/observe any in-progress API startup and call
   `JsonRpcServer.StopAsync` so an already-running Kestrel listener stops
   accepting requests. The API startup cancellation token alone does not stop
   a running listener. Pending screenshot work must be canceled first because
   an HTTP screenshot handler can otherwise wait for a future Draw while the
   game thread synchronously waits for Kestrel shutdown.
4. Cancel the coordinator token.
5. Observe the coordinator task, including cancellation or fault, for the
   fixed five-second `StartupSongLoadShutdownTimeout`.
6. When the task becomes terminal within the bound, dispose its database
   dependencies, then `StageManager`, resources, graphics, and the
   already-stopped API/server objects, with logging last.

Enumeration checks cancellation before and after each root, directory
enumeration, file enumeration, and chart parse. The current chart-parser
delegate does not accept a token, so one in-flight chart parse is the maximum
ordinary cancellation-latency unit. EF and direct persistence retain their
transaction rollback and between-command cancellation checks.

If the five-second bound expires, shutdown emits a Release-visible
`HPA192_SHUTDOWN` warning containing the timeout and latest sanitized
coordinator step, attaches a continuation that observes any eventual fault,
and proceeds with `StageManager`, resource, graphics, and API/server teardown.
It must not dispose the coordinator-owned database service or logger factory
underneath the still-running worker; those objects remain retained for process
termination. The timeout is injectable in lifecycle tests so they do not wait
five real seconds.

This coordinator fence must precede the current StageManager-first teardown.
Faults are always observed, including failures that happen before Startup
activation. A hard process kill executes no graceful lifecycle contract; its
missing startup summary makes that benchmark attempt invalid.

Startup reactivation attaches to the same running or completed operation. Its
activation generation continues guarding UI mutations and summary/transition
side effects, but it no longer owns the underlying load lifetime.

## Coordinator Data Flow

The coordinator performs the current logical startup sequence:

1. Initialize `SongDatabaseService`.
2. Publish the database-ready milestone and duration.
3. Run the existing cache-versus-enumeration decision.
4. On the cache path, build the committed database hierarchy once.
5. On the enumeration path, enumerate and parse into a temporary hierarchy.
6. Persist through the selected EF or guarded fresh-SQLite path.
7. Publish the temporary hierarchy only after the transaction commits.
8. On enumeration failure, retain the existing single-attempt committed-cache
   fallback behavior.
9. Return one immutable terminal result.

The coordinator does not duplicate `SongManager` grouping or hierarchy
ownership. `SongManager` remains responsible for traversal, parsing,
temporary hierarchy construction, and publication. `SongDatabaseService`
remains responsible for transaction and persistence mechanics.

Progress publication must not execute Startup UI code on the worker thread.
The coordinator replaces its immutable latest snapshot under a short lock or
equivalent atomic mechanism. `StartupStage` reads that snapshot during normal
updates.

`StartupSongLoadProgressSnapshot` contains:

- A domain-level step such as database initialization, path selection, cache
  loading, discovery/parsing, persistence, hierarchy finalization, or
  complete. It does not contain the UI-owned `StartupPhase`.
- `DatabaseReady`, `IsTerminal`, selected load path, nullable terminal
  outcome, and sanitized error.
- Current operation, file, directory, and processed count from the existing
  enumeration progress contract.
- Discovered-chart, parsed-chart, and logical-group counts.

The terminal result remains the authoritative source for final timings and
counts. A late stage first receives the latest snapshot and then observes the
same terminal task; snapshot replay never starts work.

No coordinator lock is held while awaiting database, enumeration, import, or
hierarchy tasks.

## Startup-Stage Integration

`StartupStage` becomes a consumer of the launch operation:

- On activation, it resets activation-local rendering, summary, and transition
  guards.
- It attaches to the coordinator's database-ready and terminal completion
  points.
- It immediately reads the latest progress snapshot.
- It never creates a second database or enumeration task.

The legacy phase launchers are removed from the stage. In particular,
`PerformPhaseOperationSync` must not call
`InitializeDatabaseServiceAsync` from `SongListDB` or `RunSongLoadAsync` from
`EnumerateSongs`, and phase-only helper paths that could launch either
operation are removed rather than retained as fallback entry points. The
coordinator is the only startup-song-operation launcher.

The existing phase names remain available for the Startup UI. On each update,
the stage advances through already completed display-only phases in a bounded
loop. It stops when it reaches an unfinished coordinator milestone or
`Complete`. The loop is bounded by the finite startup-phase count so a logic
error cannot spin indefinitely.

The milestone-to-phase mapping is:

| Coordinator state | Phases that may drain | Waiting phase |
| --- | --- | --- |
| Nonterminal, database not ready | `SystemSounds`, `ConfigValidation` | `SongListDB` waits for database-ready or terminal |
| Database ready, nonterminal | `SongListDB`, `SongsDB`, `LoadScoreCache`, `LoadScoreFiles` | `EnumerateSongs` waits for terminal |
| Terminal success or failure | Every remaining phase through `SaveSongsDB`, then `Complete` | None |
| Terminal before database-ready | Terminal dominates and satisfies both milestone waits | None |

The terminal-dominates rule prevents an initialization fault from leaving the
UI stuck at `SongListDB`. The mapping is table-driven and does not infer
worker state from elapsed time or display-phase names.

This removes the previous one-completed-phase-per-frame serialization.
There are no fixed sleeps or minimum phase durations.

The current `_phaseInfo` duration values are not dead gating state:
`DrawCurrentProgress` still uses them to interpolate the visual progress bar.
If retained, they remain presentation-only and never control phase
advancement. They may be removed only if coordinator snapshot progress fully
replaces that interpolation with equivalent rendering coverage; Task 2 does
not delete the tuple element as an unrelated cleanup.

The rendered-frame gate remains:

- Startup must draw at least one frame.
- The coordinator must be terminal.
- The activation generation must still be current.
- Only then may the stage emit its summary and request Title once.

Existing failure and cache-fallback product behavior is preserved. The
coordinator changes ownership and timing, not the user's recovery semantics.

An operation that faults before Startup activation remains a retained
terminal result. Startup attaches without throwing or restarting the
operation. Its first update may drain the completed display phases through
`Complete`, but the rendered-frame gate prevents a transition. After one
Startup draw, the next update emits exactly one failure summary and requests
exactly one Title transition. Application-shutdown cancellation is separate:
disposal cancels and observes the operation without requiring a summary or
Title transition.

## Fresh-Database Detection and Initialization

`SongDatabaseService.InitializeDatabaseAsync` records whether the usable
database was created during the current service initialization. The
service—not `SongManager`, the coordinator, or a later row-count query—owns
the evidence.

Initialization captures whether the file exists at entry and whether a
service recovery path successfully deletes it. This covers:

- A path that was absent when initialization entered.
- An invalid SQLite header deleted inside initialization.
- A missing or obsolete Unicode/version marker deleted by the current
  recreation policy.
- A caught `file is not a database` recovery retry.
- A corruption or explicit purge performed through
  `SongDatabaseService.PurgeDatabaseAsync` before initialization enters.

The return value from `EnsureCreatedAsync` is also retained. The fresh flag is
set only after all required fresh initialization completes and
`EnsureCreatedAsync` confirms that it created the schema after an absent or
successfully deleted file. A failed/best-effort deletion that leaves the old
file in place cannot establish freshness.

The following paths are explicitly nonfresh:

- A pre-existing valid database, even when every import table is empty.
- An `EnsureCreatedAsync` result indicating that the schema already existed.
- The `table already exists` recovery catch.
- A test/service constructor supplied with an already-created schema.

The flag is sticky within the current service initialization epoch. Any
successful EF or direct import commit consumes that epoch's one-import
eligibility, including a successful zero-row plan. A successful explicit
purge ends the old epoch and lets the next initialization prove new freshness;
a failed purge does not. Restore clears initialization/fresh evidence and
direct eligibility, and the restored bytes traverse the complete existing
database path as nonfresh state. Disposal ends the final epoch. A later
application launch creates a new service, observes the now-existing file, and
therefore cannot select the direct writer unless that launch deliberately
deletes and recreates the database again.

EF Core `EnsureCreatedAsync` remains the schema authority. The direct writer
does not own table or index creation.

The current post-`EnsureCreatedAsync` configuration is split into explicit
fresh and existing paths:

- **Fresh:** apply the current `PRAGMA journal_mode = DELETE` and
  `PRAGMA case_sensitive_like = OFF` bootstrap behavior, then create/update
  the `__DatabaseVersion` Unicode marker. The two pragma writes retain their
  current best-effort behavior; marker failure is fail-fast on this path.
  Otherwise a populated fresh database would be treated as obsolete and
  deleted on the next launch.
- **Existing:** retain the complete current configuration behavior, including
  validity and Unicode checks, the existing best-effort version-marker
  handling, and every additive-column, foreign-key-rebuild, index-repair,
  playback-speed, history, and receipt migration.

The fresh path does not run bookmark, NX-import, performance-history,
playback-speed, index-repair, or receipt probes against a schema that
`EnsureCreatedAsync` just produced from the current model. This split is a
required part of the database-initialization timing thesis, not an optional
micro-optimization.

For a pre-existing database:

- Keep validity and Unicode checks unchanged.
- Keep every current migration and repair probe unchanged.
- Keep fail-fast behavior for genuine schema errors.
- Never select the fresh direct writer.

If required fresh schema creation or version marking fails, initialization
fails and the direct writer is never attempted. The fresh state is not
inferred later from a zero row count alone.

## Guarded Direct-SQLite Fresh Import

### Eligibility

The direct writer is eligible only when all of these conditions hold:

- The current service instance created or recovered the database.
- No successful import has committed in the current initialization epoch.
- Songs, charts, and scores are verified empty before writing.
- The import request represents a complete enumeration batch.
- The one-shot coordinator is executing inside `SongManager`'s existing
  enumeration single-flight and owns the only production import operation for
  this launch.

If an eligibility guard fails before any mutation, the operation uses the
existing EF bulk importer.

An exception after direct writing starts rolls back and propagates. It does not
silently retry through EF, because doing so could mask schema drift or a
mapping defect. The normal startup failure/cache-fallback policy then applies.

### Pure fresh-import planning

A shared pure `FreshImportPlan` builder converts the complete request into a
deterministic entity graph and row order without querying existing song
state. It is the single source of truth for the fresh-database rules consumed
by:

- The EF importer's verified-empty fresh branch.
- The guarded direct-SQLite writer.
- Hierarchy finalization after persistence-specific identity assignment.

The planner owns:

- Canonical normalized chart paths.
- Complete discovered-path membership.
- Duplicate candidate suppression.
- Skipped discovered-path counts.
- Deterministic import-group ordering.
- Deterministic primary song metadata per group.
- One new song per new group.
- One chart per accepted candidate.
- Missing initial score creation for the instruments represented by a chart.
- Existing added, skipped, and conflict counter meanings.
- `IsBookmarked = false` for every new song.
- Exactly one zeroed initial score with `PlaySpeedPercent = 100` for each
  represented instrument.
- No non-100-percent score variants.
- Empty performance history.
- Zero score-save receipts.

Business rules must not be duplicated in SQL strings or in separate EF and
direct-writer planners. A thin direct-writer identity-allocation layer may add
explicit IDs to the shared plan, but it must not regroup, filter, or recreate
domain defaults.

### ID allocation

The writer verifies empty target tables and allocates deterministic positive
integer IDs for songs, charts, and initial scores. Explicit IDs remove
generated-key round trips and let the returned in-memory entities receive
their database identities before hierarchy finalization.

The current `Songs.Id`, `SongCharts.Id`, and `SongScores.Id` columns are
`INTEGER PRIMARY KEY AUTOINCREMENT`. The transaction inserts explicit positive
keys into those columns. On commit, SQLite must advance each affected
`sqlite_sequence` entry to at least the highest inserted key; a later ordinary
EF insert must therefore receive a non-conflicting higher ID. On rollback,
both table rows and `sqlite_sequence` must remain at their pre-transaction
state.

Before direct mutation, the writer verifies the EF-created `sqlite_master` DDL
for all three tables still declares the expected AUTOINCREMENT primary keys.
A mismatch disables the direct path before mutation and selects EF. Tests
assert the fresh `EnsureCreatedAsync` DDL directly so a future model/provider
change produces a focused contract failure rather than a misleading sequence
assertion.

The implementation must not associate entities by the output order of a
multi-row `RETURNING` clause; SQLite does not guarantee that order.

### Commands and transaction

The writer uses:

- One open `SqliteConnection` created by `SongDatabaseService` from the same
  data source and `Cache=Shared` setting as the EF path, using the service
  default 30-second command timeout; the existing EF bulk importer retains
  its deliberate 120-second override. The writer does not independently
  format a second connection string.
- One explicit transaction.
- One reusable parameterized insert command for songs.
- One reusable parameterized insert command for charts.
- One reusable parameterized insert command for scores.
- The same database column mappings, null semantics, enum values, Boolean
  representation, timestamps, defaults, and maximum lengths as the EF model.

Before beginning the transaction, the writer executes
`PRAGMA foreign_keys = ON` on that connection and verifies the returned state
is enabled. Failure disables the direct path before mutation. Rows are
inserted parent-first in `Songs`, `SongCharts`, then `SongScores` order.

Every INSERT names its complete persisted column set explicitly and never
depends on table-column order. A single reviewed writer mapping owns those
column lists and parameter binders. It uses the same provider representation
as EF for nullable values, integers, reals, Boolean `0`/`1`, integer enums,
and SQLite date/time text. Tests compare the mapping with the EF-created
schema and compare every persisted fresh row field-by-field with the EF
importer. Defaults may be omitted only where the shared fresh plan and a
schema assertion prove the documented EF default.

Each reusable command is configured once, explicitly prepared after its
parameters are defined, and then reuses those parameters by rebinding values
for each deterministic row. Command reuse and parameter rebinding provide the
per-row savings. Explicit preparation primarily front-loads the first
compilation and schema/mapping error; the design does not assume it eliminates
all per-row SQLite work. Microsoft.Data.Sqlite's ADO async methods execute
synchronously because SQLite has no asynchronous I/O, so the direct command
loop runs synchronously on the coordinator's background worker.

Cancellation is checked before planning, before the transaction, and between
row commands. Cancellation or any exception rolls back the whole transaction.
No partial song, chart, or score rows may remain.

The direct writer reports the same aggregate milestones and returns the same
`SongBulkImportResult` semantics as the EF importer. Fresh cleanup duration and
counts are zero because a verified-empty database has no stale rows.

### Fresh benchmark versus warm production path

Accepted benchmark runs use a fresh app-data/database root. They therefore
exercise `fresh_sqlite` when every freshness, schema, emptiness, and
single-operation guard passes.

The current force-enumeration value remains hard-coded true and is captured
unchanged by the coordinator. On an ordinary relaunch with an existing
database, the application still enumerates, but the import always uses the
first-wave EF path. Warm launches benefit from coordinator overlap and bounded
phase draining, not from the direct writer. The direct writer is used again
only when that launch deliberately deletes or recovers the file and the
service proves that it created a new schema.

## Existing-Database Contract

Existing databases always use the first-wave EF importer. The second wave does
not alter:

- Exact-first canonical path matching.
- Platform-specific legacy-alias matching.
- Binary unique-index conflict protection.
- Malformed persisted-path retention.
- Existing song/chart ID retention.
- Bookmark preservation.
- Score, playback-speed variant, and pitch-history preservation.
- Performance-history preservation.
- Durable score-save receipts.
- Stale chart and empty-song cleanup.
- Duplicate title/artist separation across directories.
- Ambiguous group handling.
- One explicit transaction and one `SaveChangesAsync`.

The complete existing migration and preservation suites are acceptance gates,
not optional regression coverage.

## Failure and Cancellation Semantics

- Failure before database creation leaves the coordinator faulted and observed.
- Failure during initialization retains the current recovery rules.
- Root-level enumeration failure prevents import.
- Per-chart parse failure retains the discovered path according to the
  first-wave contract.
- Cancellation before import prevents transaction creation.
- Cancellation during EF or direct persistence rolls back.
- Direct-writer schema or mapping failure rolls back and propagates.
- Enumeration import failure attempts committed-cache fallback at most once.
- Temporary hierarchy publication occurs only after commit.
- Startup deactivation cannot publish a retired result, emit a duplicate
  summary, or request a duplicate Title transition.
- Application disposal cancels and observes the launch operation.

## Telemetry

Exactly one Release-visible `HPA192_STARTUP` line remains required.

The existing fields and meanings remain available, including:

- `path`
- `outcome`
- `total_ms`
- `db_init_ms`
- `discovery_parse_ms`
- `persistence_ms`
- `cleanup_ms`
- `hierarchy_ms`
- chart/group/import counts
- sanitized `error`

`total_ms` remains the Startup activation-to-summary duration for compatibility.
The second wave appends:

- `operation_ms`: coordinator start to terminal result.
- `pre_stage_ms`: coordinator work elapsed before the current Startup
  activation.
- `stage_wait_ms`: current Startup activation to coordinator terminal
  completion, or zero when already complete.
- `persistence_path`: `ef` or `fresh_sqlite`.

Coordinator phase timings populate the existing database, discovery,
persistence, cleanup, hierarchy, count, outcome, and error fields. Appended
fields let the report distinguish actual song-operation duration from work
hidden behind MonoGame initialization.

These timing windows are intentionally non-additive. Coordinator phase fields
can include work completed before Startup activation, so `db_init_ms` or
another phase field may legitimately exceed `total_ms`; no invariant requires
their sum to fit within `total_ms`. `operation_ms` is the internal
coordinator-to-coordinator comparison window.

The external launch-to-Title wall time recorded by the benchmark runner remains
the sole primary performance measurement.

## Test Strategy

Implementation follows test-driven development.

### Pre-implementation timing tests

- Process milestones are monotonic and appear exactly once in lifecycle order.
- The diagnostic runner preserves all three fresh raw runs and calculates
  medians without admitting them into the acceptance sequence.
- Interval arithmetic separates launch-to-Startup, config-to-Startup,
  activation-to-first-draw, and summary-to-Title.
- A fixed-cost lower bound at or above 2,221 ms stops the wave before
  coordinator or database implementation; a lower value permits planning but
  does not count as acceptance.

### Coordinator tests

- Starts exactly once under repeated and concurrent access.
- Captures immutable configured paths.
- Production request creation captures `forceEnumeration = true`; an
  overridden `CreateStartupSongLoadRequest` can supply false without creating
  a `StartupStage`.
- Former Startup-stage cache-versus-enumeration tests execute against the
  coordinator request seam.
- Starts after config loading and before content/stage construction.
- Receives the `SongManager` instance captured on the game thread and never
  resolves the singleton on its worker.
- Publishes database-ready and terminal completion once.
- Replays the latest progress snapshot to a late consumer.
- Publishes domain steps and the specified operation/file/directory/count
  fields without exposing `StartupPhase`.
- A terminal result remains authoritative when the latest progress snapshot
  was observed earlier.
- Observes an early fault.
- Cancels and observes on application disposal.
- Cancellation is checked around roots, directory/file enumeration, and every
  chart parse.
- A responsive worker terminates and is observed within the shutdown bound.
- An injected hung parse triggers the timeout warning, retains worker-owned
  dependencies, and attaches an eventual fault observer while unrelated
  teardown continues.
- Rejects new API actions, cancels and observes the coordinator, then disposes
  dependent managers in the specified order.
- Cancels an in-flight screenshot before waiting for listener shutdown,
  completes disposal without another Draw, and rejects a screenshot racing
  after shutdown without installing new pending work.
- Never leaves work running after a terminal result.

### Readiness-barrier and API tests

- Holds startup before external readiness and verifies every valid external
  target, including Startup, Title, Config, and SongSelect, returns
  `StartupNotReady` before queuing.
- Verifies `UnknownStage`, `StartupNotReady`, and `ShuttingDown` remain
  distinct, including an atomic queue-admission race with shutdown.
- Verifies JSON-RPC uses `-32004` with
  `data.reason = "startup_not_ready"`, shutdown uses existing `-32001` with
  `data.reason = "shutting_down"`, and MCP preserves both code/reason pairs.
- Verifies every stage-change caller compiles against
  `Task<StageChangeRequestResult>` and the named JSON-RPC constant.
- Verifies the rejected request never enters the main-thread queue and cannot
  trigger a later transition.
- Verifies no second song database context opens and no `RootSongs` read or
  enumeration occurs while the coordinator is held.
- Verifies ordinary `GetGameState` polling remains available and database-free
  during the operation.
- Keeps external changes blocked after coordinator completion but before the
  Startup frame, summary, and normal Title-transition completion.
- Allows the same request after `ExternalStageChangesReady` becomes true and
  defines `Accepted` as queued rather than transition-completed.
- Verifies the internal Startup-to-Title request bypasses only the external API
  fence and retains its lifecycle gates.
- Round-trips `ExternalStageChangesReady` through `GameTelemetrySnapshot` and
  `E2EGameState`.
- Both drum-mapping E2E flows wait for Title plus readiness before requesting
  `DrumConfig`, and the client still throws immediately for an unexpected
  JSON-RPC error.

### Startup-stage tests

- Attaches to an already running coordinator.
- Attaches to an already completed coordinator.
- Reactivation does not start a duplicate operation.
- Deactivation does not cancel or observe the coordinator; reactivation
  attaches to the same launch task.
- The former `SongListDB` and `EnumerateSongs` phase paths cannot launch
  database initialization or song loading.
- Table-driven tests cover every milestone-to-phase row.
- A nonterminal unfinished database milestone stops at `SongListDB`.
- Database-ready/nonterminal state drains through `LoadScoreFiles` and stops
  at `EnumerateSongs`.
- Terminal success and failure drain through `Complete` in one bounded update.
- Terminal-before-database-ready dominates both waits and cannot stick.
- Phase duration metadata never gates advancement; if retained, it continues
  to drive only progress-bar interpolation.
- At least one Startup frame renders before Title.
- Exactly one summary and Title request occur.
- Retired activations cannot mutate current UI or terminal state.
- Fault and cancellation summaries remain machine-readable.
- A fault completed before activation drains only after attachment, renders
  one Startup frame, then emits one failure summary and one Title request.
- Application-disposal cancellation does not require a summary or Title
  request.
- New telemetry fields retain all existing summary fields.
- Coordinator phase timings may exceed `total_ms`, and `operation_ms` retains
  the complete coordinator duration.

### Fresh initialization tests

- A new database records fresh eligibility.
- Invalid-header, Unicode/version recreation, caught-not-a-database,
  corruption-purge, and explicit-purge paths record fresh eligibility only
  after successful deletion and schema creation.
- A failed deletion, `table already exists` catch, pre-created test schema,
  and pre-existing empty database are not considered fresh.
- `EnsureCreatedAsync` must report schema creation before the fresh flag can
  become true, and the flag remains sticky within that initialization epoch.
- Any successful EF or direct commit consumes the current epoch's eligibility,
  including a zero-row plan; a second zero-row request uses existing EF
  reconciliation.
- Successful purge begins a new initialization epoch, failed purge does not,
  and restore invalidates fresh/direct eligibility before taking the existing
  path.
- A structural differential test compares a fresh database with a legacy
  fixture after the complete existing-database migration path. It compares
  user table/index names and types plus normalized `PRAGMA table_info`,
  `index_list`/`index_info`/`index_xinfo`, and `foreign_key_list` metadata,
  including indexed-column collation/key roles and normalized partial-index
  predicates. It includes the `__DatabaseVersion` marker and compares its
  semantic `Feature`/`Version`; `AppliedAt` must be parseable but its
  wall-clock value is excluded.
- The differential does not compare raw normalized `sqlite_master` SQL text or
  provider-generated internal object names; semantically equivalent DDL can
  differ in formatting or ordering. The direct-writer AUTOINCREMENT guard
  remains a separate exact DDL assertion.
- A fresh schema skips legacy probes while matching the fully migrated
  structural contract.
- Fresh bootstrap attempts the named pragmas with their current best-effort
  semantics, treats version-marker failure as fatal, and never executes a
  legacy probe.
- A second startup on that database takes the complete existing-database path.
- A pre-existing empty database reports `persistence_path=ef`.
- Every existing migration and repair test remains green.

### Direct-writer tests

- Fresh output matches the EF importer for songs, charts, scores,
  relationships, IDs, metadata, counters, and returned path map.
- The 100-chart shape persists as 100 charts and 27 logical songs.
- Duplicate candidates and undiscovered candidates preserve current counters.
- Explicit IDs are deterministic and present in the returned entity graph.
- Fresh songs are unbookmarked; represented instruments receive exactly one
  zeroed 100-percent score; no speed variants, performance history, or
  score-save receipts are created.
- Fresh `sqlite_master` DDL proves AUTOINCREMENT primary keys for Songs,
  SongCharts, and SongScores before sequence assertions run.
- Committed explicit IDs advance every affected `sqlite_sequence` entry to at
  least the inserted maximum.
- A later EF insert generates a higher, non-conflicting ID.
- Cancellation after one or more row commands rolls back all tables.
- Rollback restores every affected `sqlite_sequence` entry to its
  pre-transaction state.
- Injected song, chart, and score command failures each roll back all tables.
- A failed guard selects EF before mutation.
- A command/schema failure does not silently retry through EF.
- The direct connection uses the service-owned data source, `Cache=Shared`,
  and command timeout, and verifies foreign-key enforcement before mutation.
- Schema-contract tests cover every explicit insert column, parent-first
  foreign-key order, null, Boolean, enum, date/time, integer, and real
  conversion.

### Regression verification

- Targeted coordinator, Startup, import, preservation, migration, and summary
  tests.
- Complete Mac suite with `ALSOFT_DRIVERS=null`.
- Release Mac build used by the benchmark.
- Existing environment-only audio behavior is documented separately if the
  null audio driver is omitted.

## Benchmark Protocol and Acceptance

The benchmark continues to use the immutable local corpus:

- 100 supported chart files.
- 27 `SET.def` logical groups.
- 592 manifest rows.
- Manifest SHA-256:
  `0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b`.

The second-wave comparison uses:

- Original baseline commit:
  `5ea3f95d208ba7b15019429f63d7edd0bbf7009d`.
- A fixed Release output from the final second-wave product commit.
- Pinned `benchmark-startup.sh` single-run validation and
  `run-balanced-benchmark.sh` acceptance orchestration scripts, with separate
  SHA-256 values.
- Fresh app-data and database roots for every accepted run.
- Distinct loopback API ports and launch tokens.
- Exact chart-path equality with the frozen manifest.
- Exactly one startup summary per run.
- A predetermined balanced order:
  baseline, wave two, wave two, baseline, baseline, wave two.

Diagnostics run while developing are disclosed but excluded from the
predetermined acceptance sequence. A failed accepted attempt is retained as
diagnostic evidence and the entire sequence restarts in a clean namespace
rather than resuming or overwriting it.

The final report records commits, binary hashes, both runner-script hashes,
environment, manifest, run order, every raw wall time, summary line, database
counts, path hashes, medians, and calculation.

Acceptance requires all of the following:

- Baseline median remains evidence-backed.
- Second-wave median external launch-to-Title wall time is 2,221 ms or less.
- Second-wave improvement against the original baseline is at least 70
  percent.
- Every accepted second-wave run has 100 exact chart paths.
- Every accepted second-wave run has 100 charts and 27 songs.
- Every accepted second-wave run reports
  `persistence_path=fresh_sqlite`.
- Every accepted run emits exactly one summary.
- No correctness or lifecycle acceptance gate fails.

Coordinator, overlap, stage-wait, and persistence timings are diagnostic. They
do not replace or relax the external wall-time gate.

If the second-wave median misses either performance gate, the report retains
and records the failed result and implementation work stops without changing
the gate or broadening this design. Any further optimization requires a new
measurement-based diagnosis and a separately reviewed design. Cold EF-model
work, runtime packaging, ReadyToRun, and AOT are not pre-authorized as part of
this wave.

Intermediate overlap-only or overlap-plus-drain builds may be measured and
retained as disclosed diagnostics under the existing exclusion rules. They
are never substituted for the predetermined acceptance samples.

## Implementation Sequence

The implementation plan at
`docs/superpowers/plans/2026-07-28-hpa-192-second-wave-startup-optimization.md`
splits work into independently reviewed tasks:

0. Instrumented first-wave Release timing, three-run diagnostic report, and
   the fixed-cost go/no-go gate.
1. Named coordinator request/progress/result contracts and one-shot lifecycle
   tests.
2. One atomic vertical integration of `BaseGame` early-start ownership,
   `StartupStage` table-driven milestone consumption, readiness/API/JSON-RPC
   fencing, telemetry, and bounded coordinator-first disposal. The early
   coordinator cannot land in a commit where external stage changes remain
   unfenced.
3. MCP error preservation, E2E readiness propagation/waiting, and CI coverage.
4. Service-owned fresh-database evidence, strict fresh bootstrap, and
   existing-probe split.
5. Shared pure fresh-import planning and a verified-empty fresh EF semantic
   oracle.
6. Guarded prepared direct writer with explicit schema mapping, connection,
   foreign-key, cancellation, transaction, and ID contracts.
7. Structural fresh-versus-migrated schema differential, parity, rollback,
   DDL/sequence, migration, lifecycle, and full-suite verification.
8. Balanced benchmark orchestration and report update.

The implementation plan will use subagent-driven development with
task-by-task specification and quality reviews, followed by a final
whole-branch review.

## Risks and Mitigations

### Background work delays MonoGame initialization

The coordinator may contend for CPU with graphics/content initialization. The
observed 1,744 ms external-minus-summary median includes both work before the
coordinator can start and the explicit one-second post-summary Title
transition; neither may be treated as free overlap. Task 0 measures the actual
configuration-to-Startup window before higher-risk work begins. The direct
fresh writer aims to shorten the coordinator below that measured window,
changing which concurrent path binds launch time. Bounded phase draining then
removes at most eight extra update intervals from the residual critical path.
Only the balanced external benchmark proves whether the combined result has
sufficient headroom.

### Raw SQL drifts from the EF model

EF remains the schema authority. The direct path is fresh-only, uses explicit
column lists and parameters, and is guarded by schema/parity integration
tests. Existing databases never use it.

### Explicit IDs conflict with later EF writes

The path is empty-database-only, allocates positive IDs deterministically, and
must prove that a later EF-generated insert receives a higher ID.

### Operation outlives its UI

`BaseGame` owns cancellation and observation. Startup activation generations
guard UI side effects, and Title waits for a terminal result.

### Progress crosses threads

The worker publishes immutable snapshots only. Startup reads snapshots on the
game thread; worker callbacks never touch graphics or stage state.

### Startup becomes visually imperceptible

The existing at-least-one-rendered-frame gate remains even when every
coordinator milestone completed before Startup activation.

## References

- [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
- [Microsoft.Data.Sqlite parameters](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/parameters)
- [Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)
- [SQLite AUTOINCREMENT](https://www.sqlite.org/autoinc.html)
- [SQLite RETURNING](https://www.sqlite.org/lang_returning.html)
- `docs/superpowers/specs/2026-07-27-hpa-192-batched-song-import-design.md`
- `docs/performance/HPA-192-startup-benchmark.md`
