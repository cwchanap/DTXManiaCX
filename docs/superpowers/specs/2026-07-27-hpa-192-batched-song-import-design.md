# HPA-192 Batched SQLite Fresh Startup Import Design

**Issue:** [HPA-192](https://linear.app/cwchanap/issue/HPA-192/optimize-fresh-startup-song-loading-with-batched-sqlite-import)  
**Date:** 2026-07-27  
**Status:** Approved

## Context

A fresh DTXManiaCX startup currently interleaves chart parsing with database
persistence. Each chart can create a new EF Core context, query for existing
entities, call `SaveChangesAsync` multiple times, create score rows through
additional saves, and reload the complete song graph. After enumeration,
startup runs stale cleanup again, discards the hierarchy it already built,
reconstructs the hierarchy from SQLite, and queries database statistics in a
verification-only save phase.

`StartupStage` also assigns minimum durations to its phases. Completed work
therefore waits before startup advances.

The relevant implementation is concentrated in:

- `DTXMania.Game/Lib/Stage/StartupStage.cs`
- `DTXMania.Game/Lib/Song/SongManager.cs`
- `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- `DTXMania.Game/Lib/Song/DTXChartParser.cs`

The benchmark corpus is a machine-local prerequisite, not a repository asset.
It currently contains 100 supported chart files: the original 52 plus 48 charts
added under the isolated `DTXFiles.HPA-192-benchmark` folder. The files
represent 27 logical songs because every song folder uses `set.def` to group
three or four difficulty charts. Benchmark reports must record both chart and
logical-song counts so the result is not misrepresented as 100 flat songs.

The 48 added charts came from the shared
[Google Drive folder](https://drive.google.com/drive/folders/1GN8RQF1KUG8_UosPJMdvh7Hm9X4trD7o)
in these packages:

- `Kaisei Joushou Hareruya.zip`
- `butterfly (animetal).zip`
- `bokura no memories.zip`
- `shirahae.zip`
- `fantaisie Impromptu.zip`
- `Kokuu, mugi no kaze.zip`
- `yawaraka shensha.zip`
- `dark knight.zip`
- `link link fever!!!.zip`
- `ash to ash.zip`
- `orgasm.zip`
- `Ryu's Theme.zip` (stored locally as `Ryu_s Theme`)
- `linda linda.zip`
- `Gust of Wind.zip`

The third-party song assets must not be committed. Before the baseline is
recorded, the benchmark report will capture a sorted relative-path, file-size,
and SHA-256 manifest so all before/after runs use the exact same local bytes.
The performance gate is evaluated on this machine-local corpus, not in CI.

## Goals

- Reduce the median fresh-load time by at least 70 percent on the same
  100-chart corpus, with a target of eight seconds or less.
- Persist a fresh or re-enumerated library with one EF Core context and one
  explicit SQLite transaction.
- Use one `SaveChangesAsync` call for the import.
- Remove per-chart entity reloads.
- Preserve bookmarks, scores, speed variants, performance history, recent-play
  data, multi-difficulty grouping, and generated database IDs.
- Run stale cleanup and hierarchy construction at most once on each startup
  path.
- Keep progress responsive and make cancellation or failure atomic.
- Emit a single aggregate timing summary that is available in a Release build
  outside the debugger.
- Advance completed startup phases without simulated minimum durations while
  rendering the startup screen at least once.

## Non-goals

- Parser I/O redesign.
- Parallel chart parsing.
- Concurrent SQLite writers.
- A filesystem manifest or fingerprint system.
- Retiring, enabling, or making configurable the hard-coded
  `_forceEnumeration = true` production behavior.
- Changes to cache-validation correctness beyond removing duplicate decisions
  naturally replaced by the selected load path.
- Broader cached-startup optimization.
- Song-selection preview or first-song asset loading.
- DTXCreator changes.
- Multiple persistence batches unless measurement proves that the single
  tracked batch is itself the remaining bottleneck.

## Chosen Approach

Enumeration parses and organizes the complete library in memory first. A
database-owned bulk import then persists that completed enumeration in one
short transaction. This keeps filesystem I/O outside the transaction, keeps EF
Core details out of `SongManager`, and makes import atomicity independently
testable.

Two alternatives were rejected:

- Keeping a transaction open during parsing would extend the SQLite write
  session across filesystem I/O and couple parser failures to transaction
  lifetime.
- Letting `SongManager` own `SongDbContext` would leak database mechanics into
  startup orchestration and weaken rollback and invariant tests.

## Component Boundaries

### `SongManager`

`SongManager` remains responsible for:

- Traversing configured song roots.
- Parsing charts sequentially through `DTXChartParser`.
- Parsing `set.def` and `box.def`.
- Reporting per-file discovery progress to the startup UI.
- Constructing the folder and box hierarchy in temporary collections.
- Publishing the new root hierarchy only after the database import commits.

It no longer persists or reloads one chart at a time.

The public `DiscoveredScoreCount` and `EnumeratedFileCount` properties remain
as compatibility mirrors of the completed enumeration batch. Existing tests
consume them. Startup UI progress continues to come from
`EnumerationProgress`, not by polling these properties.

### `SongDatabaseService`

`SongDatabaseService` gains a bulk import API used by fresh and re-enumeration
startup. The API owns:

- One `SongDbContext`.
- One explicit SQLite transaction.
- Existing-state preload.
- Chart matching and song grouping.
- Metadata updates and new entity creation.
- Initial score creation.
- Stale chart and empty-song removal.
- One `SaveChangesAsync`.
- Commit or rollback.
- Import counts and database subphase timings.

The existing `AddSongAsync` API remains as a legacy/test helper so unrelated
fixtures do not require broad rewrites. Both production enumeration callers
are removed, and startup must use only the bulk import API. Its legacy
title/artist matching is therefore outside the production HPA-192 path.

### Import data contracts

The implementation may choose final type names, but the contracts have these
roles:

- **Import candidate:** parsed `Song`, parsed `SongChart`, normalized chart
  path, enumeration group key, and the pending node/group that will receive
  persisted identities.
- **Enumeration batch:** active search roots, every discovered normalized chart
  path, successfully parsed candidates, temporary hierarchy, completeness
  state, counts, and discovery/parsing duration.
- **Import result:** added, updated, preserved, skipped, and stale counts;
  persistence and cleanup durations; outcome; and the tracked entities needed
  to finalize nodes.
- **Startup summary:** selected cached or enumeration path, aggregate phase
  timings, counts, total duration, and success/cancellation/failure outcome.

These contracts keep parser, persistence, hierarchy publication, and startup
reporting independently testable.

## Identity and Grouping

Chart path is the primary identity.

A single helper normalizes chart paths before lookup or persistence:

- Convert to an absolute full path.
- Normalize directory separators consistently.
- Compare with the operating system's filesystem comparison semantics.
- Store the normalized value in `SongChart.FilePath`.

The existing unique `SongChart.FilePath` database index remains the final
constraint.

Title and artist must not globally identify a chart or song. Import grouping is
defined as follows:

- Charts listed by one `set.def` share the normalized `set.def` path as their
  group key.
- Ordinary charts share a group only when their normalized containing
  directory and parsed title/artist match.
- Equal title/artist values in different directories produce separate songs.

An existing chart matched by path keeps its current `SongId`. A new sibling may
join an existing song only when its import group resolves unambiguously to one
persisted song. If a legacy group resolves to multiple existing song IDs, the
import keeps those associations and reports a conflict instead of silently
reparenting charts or merging user data.

Within a new group, song-level metadata comes from a deterministic primary
candidate: the first `set.def` difficulty order when present, otherwise the
candidate with the lexically first normalized chart path.

## Enumeration Flow

1. Normalize and validate the active search roots.
2. Traverse roots sequentially.
3. Add each supported chart path to the complete discovery set before parsing.
4. Parse the chart.
5. On a normal malformed-chart result, count the error but retain the path in
   the discovery set. This prevents a temporary parse failure from deleting a
   previously persisted chart.
6. Add successful parses to an import group and build temporary folder/box
   structure.
7. Mark the batch complete only after every configured root finishes traversal.
8. If cancellation or a root-level traversal error occurs, do not start the
   database import.

The existing blanket catches in `StartupStage.EnumerateSongsAsync` and
`SongManager.EnumerateSongsAsync` must no longer convert cancellation or
failure into a successful count. Recursive traversal reports root-level I/O and
access failures as an incomplete batch. Per-chart and per-`set.def` parse
failures remain recoverable batch entries with their discovered paths retained.
Cancellation is always rethrown.

Per-directory, per-chart, and per-asset success `Debug.WriteLine` calls are
removed from the normal enumeration path. Progress callbacks continue to
report the current file and aggregate counts. Warning, conflict, parse-failure,
and unexpected-error diagnostics remain available in Debug builds.

## Bulk Import Algorithm

The database import runs only for a complete enumeration batch:

1. Create one `SongDbContext`.
2. Begin one explicit SQLite transaction with the caller's cancellation token.
3. Preload persisted charts under the active roots, including their songs,
   scores, and performance history.
4. Index existing charts by normalized path using the platform-aware comparer.
5. Process import groups deterministically:
   - Existing charts are updated in place.
   - Existing chart and song IDs are retained.
   - Persisted user-owned state is not overwritten.
   - New charts are attached to the group's unambiguous existing or new song.
   - New score rows are created only for missing chart, instrument, and play
     speed keys.
6. Compare persisted paths under active roots with the complete discovery set.
   Remove stale charts and then songs left with no charts. Records outside the
   active roots are untouched.
7. Call `SaveChangesAsync` once.
8. Use EF Core tracked relationships and generated keys to finalize pending
   nodes without queries.
9. Commit the transaction.
10. Return the finalized import result.

The import explicitly preserves:

- `Song.Id`, `SongChart.Id`, and existing chart-to-song associations.
- `Song.IsBookmarked`.
- All existing `SongScore` rows, including non-default play speeds.
- Best and last score fields, play and clear counts, ranks, full-combo state,
  and recent-play timestamps.
- All performance-history rows.

Parsed metadata may update song and chart presentation/gameplay fields, but it
must not replace persisted user-owned state.

`SongChart.FileHash` is not consumed by current production cache validation or
identity logic. The bulk path preserves an existing hash during rescans but
does not compute MD5 for new charts, avoiding the current second full-file
read. New bulk-imported charts leave the field empty. Hash-backed cache
validation, if needed later, requires a separate design.

The discovery-set cleanup in step 6 replaces both startup calls to
`CleanupStaleChartsAsync`: the post-enumeration call and the call inside
database hierarchy construction. The old per-chart `File.Exists` cleanup is
not invoked by either startup path after this change.

Legacy ambiguous groups contribute to the Release aggregate conflict count.
Debug builds additionally log one diagnostic per conflicting group containing
the normalized group key and involved song IDs. Conflicts are not shown in the
startup UI.

## Hierarchy Publication

Enumeration builds hierarchy containers and pending song groups in temporary
collections. After `SaveChangesAsync`, tracked entities supply generated song
and chart IDs and the already-preloaded score/history graph. Existing node
hydration helpers then finalize the score nodes without calling
`GetSongWithChartsAsync`.

`SongManager._rootSongs` is replaced only after the transaction commits. An
import cancellation or failure therefore cannot publish a hierarchy that
disagrees with the committed database.

The cached path continues to construct hierarchy from SQLite, but does so only
once. The enumeration path keeps its newly constructed hierarchy and never
rebuilds it from SQLite.

## Startup Orchestration

`StartupStage` keeps the user-facing phase names but changes their completion
rules:

- A synchronous phase completes after its operation runs.
- An asynchronous phase completes when its task reaches a terminal state.
- No phase waits for a configured minimum duration.
- Transition to Title requires at least one rendered startup frame, not a
  completion delay.

`OnDraw` sets a `_hasRenderedStartupFrame` guard. `OnUpdate` may request the
Title transition only when that guard was already set by a prior draw, which
also protects against MonoGame catch-up cycles that run multiple updates before
one draw.

The startup song-load path is selected once:

1. Initialize SQLite and start the aggregate timer.
2. Use the existing cache decision, with forced enumeration taking precedence.
3. If enumeration is required, discover, parse, build the temporary hierarchy,
   bulk-import, and publish it.
4. If the cache is valid, load entities and build the database hierarchy once.
5. Mark `SongManager` initialized.

The verification-only `SaveSongsDB` statistics query is removed or collapsed
into the finalization step. The `BuildSongLists` phase does not rebuild an
enumerated hierarchy.

Production currently hard-codes `_forceEnumeration = true`, so HPA-192's live
benchmark intentionally exercises only the fresh enumeration path. This task
does not enable or repair cached startup. The `needsEnumeration == false`
branch remains structurally correct and is exercised through a test seam, but
live cached-startup performance is not an HPA-192 acceptance gate.

## Instrumentation

Stopwatches record:

- Database initialization.
- Filesystem discovery and chart parsing.
- Database persistence.
- Stale cleanup.
- Hierarchy construction.
- Total song-loading startup time.

The completion record also contains:

- Cached or enumeration load path.
- Outcome.
- Discovered and parsed chart counts.
- Logical song/group count.
- Added, updated, preserved, skipped, conflict, and stale counts.

One concise summary line is written to standard output so the Release
benchmark can capture it without a debugger. The same summary is emitted for
success, cancellation, and failure. The startup UI continues to receive
progress updates independently of diagnostic output.

## Cancellation and Failure Handling

Temporary enumeration state is not published before commit.

The import:

- Uses `await using` for its context and transaction.
- Checks cancellation between preload, matching, mutation, cleanup, save, and
  commit stages.
- Passes the token to `SaveChangesAsync` and `CommitAsync`.
- On cancellation or failure, attempts rollback with a non-cancelled token and
  then rethrows the original exception.

`StartupStage` observes task outcomes rather than swallowing failures as
successful phase completion:

- **Success:** publish the imported hierarchy.
- **Cancellation:** leave the previously committed database and hierarchy
  unchanged.
- **Failure:** discard temporary hierarchy and attempt to load the last
  committed database cache without running stale cleanup. If no usable cache
  exists, finish with an empty song list and a visible error state instead of
  hanging.

Expected malformed individual charts remain counted skips. Incomplete root
traversal is a batch-level failure and cannot trigger stale cleanup.

Concretely, the existing catches in the startup enumeration wrapper and outer
`SongManager` enumeration rethrow cancellation and unexpected failures.
Recursive directory traversal marks the active root incomplete instead of
returning partial success. Narrow chart/definition parse catches add a
recoverable error to the batch rather than faulting the complete root.

## Testing

### Database import tests

- Fresh batch commits songs, charts, and initial scores.
- Generated IDs propagate to pending nodes.
- Cancellation after staging entities rolls back the entire import.
- A SQLite-triggered write failure rolls back the entire import.
- Rescan preserves bookmarks, all score variants, history, and recent-play
  state while updating parsed metadata.
- Duplicate title/artist charts in different directories remain separate.
- `set.def` difficulties group into one logical song.
- Existing score keys are not duplicated.
- Stale charts and empty songs are removed once.
- Records outside active search roots remain untouched.
- Legacy ambiguous groups are retained and reported.

### `SongManager` tests

- Enumeration returns a complete batch before persistence.
- Parse failures retain discovered paths.
- Incomplete traversal never starts import.
- Imported hierarchy is retained without a database rebuild.
- Generated chart IDs and persisted scores hydrate the final nodes.
- Existing `set.def`, `box.def`, bookmark, parsing, and hierarchy behavior
  remains intact.

### `StartupStage` tests

- Completed synchronous and asynchronous phases advance below the old minimum
  durations.
- Faulted and cancelled tasks produce their corresponding outcomes.
- Title transition waits for one rendered frame only.
- The injected `needsEnumeration == false` branch builds from SQLite once
  without changing the production forced-enumeration setting.
- Enumeration startup does not rebuild from SQLite.
- Finalization does not run the old database-statistics verification query.

### Regression suites

The macOS-safe test project must pass, including existing song manager,
database, parser, bookmark, score, recent-play, `set.def`, and `box.def` tests.
The matching Windows/full suites remain required in CI.

## Benchmark Procedure

The benchmark uses the same local 100-chart corpus before and after the change.

1. Build the macOS game project in Release configuration.
2. Run outside the debugger.
3. Use an isolated temporary app-data root whose `DTXPath` points to the
   benchmark corpus.
4. Begin each run without a `songs.db`.
5. Use the existing Game API to observe arrival at Title and capture external
   wall time.
6. Capture the aggregate startup summary from standard output.
7. Run three baseline imports on the pre-change commit.
8. Run three optimized imports on the implementation commit.
9. Record every run and compare medians.

The durable result is written to
`docs/performance/HPA-192-startup-benchmark.md` and includes:

- Hardware and operating system.
- .NET version.
- Commit IDs.
- Corpus location, chart count, and logical-song count.
- Per-run external wall time.
- Per-run aggregate phase timings.
- Baseline and optimized medians.
- Percentage improvement.

Implementation creates `docs/performance/` when the benchmark report is first
written. Before the baseline runs, the report also records the sorted
relative-path, file-size, and SHA-256 corpus manifest described above.

Acceptance requires at least a 70 percent median reduction. Eight seconds or
less remains the target, but the report must identify the measured remaining
bottleneck if the database work alone does not reach it.

## Acceptance Mapping

- One explicit transaction and one `SaveChangesAsync`: bulk import algorithm.
- No per-chart reload: tracked-entity node finalization.
- Cleanup and hierarchy at most once: selected startup path and import-owned
  cleanup.
- Completion-driven phases: terminal-task state plus one rendered frame.
- Responsive progress: discovery callbacks and aggregate persistence
  milestones.
- Atomic cancellation/failure: complete enumeration gate, transaction
  rollback, and post-commit publication.
- User-data preservation: path-based matching and in-place entity updates.
- Duplicate title/artist safety: directory or `set.def` group keys.
- Performance proof: repeatable three-run Release benchmark and durable report.

## Deferred Follow-up

If the post-change timing summary shows parsing as the dominant remaining cost,
a separate issue may consider buffered parser I/O or parallel parsing. If
unchanged-library startup remains slow, a separate issue may address filesystem
manifests and cache-validation correctness. Neither concern expands HPA-192.
