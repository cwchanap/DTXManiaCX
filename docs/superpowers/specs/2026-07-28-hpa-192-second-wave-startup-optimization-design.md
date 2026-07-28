# HPA-192 Second-Wave Startup Optimization Design

**Issue:** [HPA-192](https://linear.app/cwchanap/issue/HPA-192/optimize-fresh-startup-song-loading-with-batched-sqlite-import)  
**Date:** 2026-07-28  
**Status:** Design amended after review; implementation planning pending
committed-spec review

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

Approximately 465 ms inside the startup-stage summary is not attributed to
those database and song phases. It includes startup graphics/font work and
frame-by-frame phase progression.

The median difference between external launch-to-Title time and the
Startup-activation-to-summary duration is approximately 1,744 ms. The paired
first-wave differences were 1,651 ms, 1,744 ms, and 1,753 ms. This interval
also contains process/runtime startup and configuration loading before the
coordinator's earliest safe post-configuration start, so it is an upper bound
on potentially hidden work, not a measured overlap window.

This timing shape makes either isolated optimization insufficient:

- Database-only work cannot reliably remove the full 1,667 ms external gap.
- Conservative median arithmetic for moving the existing song operation
  earlier produces a theoretical estimate near 2,209 ms, leaving only 12 ms
  below the gate. That estimate is not a measured second-wave result and does
  not account for the unavailable pre-configuration portion, scheduling, or
  CPU contention.

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

## Launch-Scoped Ownership

### Coordinator responsibility

A new coordinator owns the startup song operation for the lifetime of one
`BaseGame` launch. Final type names may vary, but its contract has these roles:

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

Capturing `SongManager.Instance` before `Task.Run` prevents the existing
double-checked singleton initialization from racing between the game and
worker threads. The worker uses only the captured instance; it does not
resolve the singleton again.

### Launch readiness barrier

While the coordinator is nonterminal, it is the sole production owner of
song hierarchy mutation and song-database access. No other production path
may enumerate `SongManager.RootSongs` or open a song database context during
that interval.

The external game API enforces this boundary by rejecting a request to change
away from Startup while the coordinator is nonterminal. The request is
rejected immediately with a machine-readable not-ready error; it is not
queued or deferred. In particular, an early request cannot force
`SongSelect`, whose activation reads the live `RootSongs` view and opens
recent-play or bookmark database contexts.

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
no `SongManager` mutation continues into Title. On application shutdown,
`BaseGame` cancels and observes the coordinator before disposing services that
the operation may still use. Faults are always observed, including failures
that happen before Startup activation.

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

This removes the previous one-completed-phase-per-frame serialization.
There are no fixed sleeps or minimum phase durations.

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
database was created during the current service initialization.

A database is fresh for this purpose only when:

- no database existed when initialization began; or
- an invalid database was deliberately removed through the existing recovery
  path and a replacement was successfully created.

EF Core `EnsureCreatedAsync` remains the schema authority. The direct writer
does not own table or index creation.

For a database created during this initialization:

- Run the required SQLite pragmas.
- Create/update the database-version marker.
- Do not run legacy additive-column, foreign-key-rebuild, index-repair, or
  receipt-migration probes against a schema that `EnsureCreatedAsync` just
  produced from the current model.

For a pre-existing database:

- Keep validity and Unicode checks unchanged.
- Keep every current migration and repair probe unchanged.
- Keep fail-fast behavior for genuine schema errors.
- Never select the fresh direct writer.

The fresh state is internal service state, not inferred later from a zero row
count alone. A pre-existing but empty database therefore remains on the
normal EF path.

## Guarded Direct-SQLite Fresh Import

### Eligibility

The direct writer is eligible only when all of these conditions hold:

- The current service instance created or recovered the database.
- No successful import has committed through this service instance.
- Songs, charts, and scores are verified empty before writing.
- The import request represents a complete enumeration batch.
- The normal import serialization gate grants this operation sole writer
  ownership.

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

The implementation must not associate entities by the output order of a
multi-row `RETURNING` clause; SQLite does not guarantee that order.

### Commands and transaction

The writer uses:

- One open `SqliteConnection`.
- One explicit transaction.
- One reusable parameterized insert command for songs.
- One reusable parameterized insert command for charts.
- One reusable parameterized insert command for scores.
- The same database column mappings, null semantics, enum values, Boolean
  representation, timestamps, defaults, and maximum lengths as the EF model.

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

### Coordinator tests

- Starts exactly once under repeated and concurrent access.
- Captures immutable configured paths.
- Starts after config loading and before content/stage construction.
- Receives the `SongManager` instance captured on the game thread and never
  resolves the singleton on its worker.
- Publishes database-ready and terminal completion once.
- Replays the latest progress snapshot to a late consumer.
- Observes an early fault.
- Cancels and observes on application disposal.
- Never leaves work running after a terminal result.

### Readiness-barrier and API tests

- Holds the coordinator inside its writer transaction and verifies that an API
  request to change from Startup to SongSelect is rejected as not ready.
- Verifies the rejected request does not queue a later transition.
- Verifies no second song database context opens and no `RootSongs` read or
  enumeration occurs while the coordinator is held.
- Verifies ordinary `GetGameState` polling remains available and database-free
  during the operation.
- Allows the same stage-change request after terminal completion.

### Startup-stage tests

- Attaches to an already running coordinator.
- Attaches to an already completed coordinator.
- Reactivation does not start a duplicate operation.
- The former `SongListDB` and `EnumerateSongs` phase paths cannot launch
  database initialization or song loading.
- Completed display-only phases drain in one bounded update.
- An unfinished milestone stops phase draining.
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
- A recovered invalid database records fresh eligibility.
- A pre-existing empty database is not considered fresh.
- A fresh schema skips legacy probes but contains every current column, index,
  foreign key, and version marker.
- A second startup on that database takes the complete existing-database path.
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
- Committed explicit IDs advance every affected `sqlite_sequence` entry to at
  least the inserted maximum.
- A later EF insert generates a higher, non-conflicting ID.
- Cancellation after one or more row commands rolls back all tables.
- Rollback restores every affected `sqlite_sequence` entry to its
  pre-transaction state.
- Injected song, chart, and score command failures each roll back all tables.
- A failed guard selects EF before mutation.
- A command/schema failure does not silently retry through EF.

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
- A pinned benchmark runner.
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

The final report records commits, binary hashes, runner hash, environment,
manifest, run order, every raw wall time, summary line, database counts, path
hashes, medians, and calculation.

Acceptance requires all of the following:

- Baseline median remains evidence-backed.
- Second-wave median external launch-to-Title wall time is 2,221 ms or less.
- Second-wave improvement against the original baseline is at least 70
  percent.
- Every accepted second-wave run has 100 exact chart paths.
- Every accepted second-wave run has 100 charts and 27 songs.
- Every accepted run emits exactly one summary.
- No correctness or lifecycle acceptance gate fails.

Coordinator, overlap, stage-wait, and persistence timings are diagnostic. They
do not replace or relax the external wall-time gate.

## Implementation Sequence

The later implementation plan will split work into independently reviewed
tasks:

1. Coordinator contracts and one-shot lifecycle tests.
2. `BaseGame` early-start ownership, captured `SongManager`, game-API readiness
   barrier, and disposal.
3. `StartupStage` milestone consumption, legacy-launcher removal, early-fault
   lifecycle, bounded phase draining, and telemetry.
4. Fresh-database initialization state and migration-probe bypass.
5. Pure fresh-import planning and direct prepared-command writer.
6. Parity, rollback, ID, migration, and full-suite verification.
7. Balanced benchmark and report update.

The implementation plan will use subagent-driven development with
task-by-task specification and quality reviews, followed by a final
whole-branch review.

## Risks and Mitigations

### Background work delays MonoGame initialization

The coordinator may contend for CPU with graphics/content initialization. The
observed 1,744 ms external-minus-summary median includes work before the
coordinator can start and therefore cannot be treated as free overlap. The
direct fresh writer aims to shorten the coordinator below the actual
post-configuration graphics/content window, changing which concurrent path
binds launch time; bounded phase draining then removes completed stage work
from the residual critical path. Only the balanced external benchmark proves
whether the combined result has sufficient headroom.

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
