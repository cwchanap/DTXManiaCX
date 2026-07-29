# HPA-192 Startup Critical-Path Diagnostic Design

**Date:** 2026-07-28

**Status:** First written-spec review addressed; approval pending

## Summary

The stopped second-wave plan proved that the current fresh-start fixed-cost
floor cannot meet the 2,221 ms target. Its three final diagnostic runs reported
fixed floors of 3,779 ms, 2,981 ms, and 2,988 ms. The representative 2,988 ms
floor consists of:

```text
external launch to Startup activation       650 ms
Startup activation to first Startup draw  1,187 ms
summary request to completed Title         1,151 ms
                                            -------
fixed-cost lower bound                     2,988 ms
```

The retained evidence also reports:

- fresh SQLite initialization: 1,018–1,059 ms;
- discovery and parsing: 141–176 ms;
- batched persistence: 433–434 ms;
- cleanup: 1–2 ms;
- hierarchy publication: 3 ms; and
- summary-to-completed-Title: 1,127–1,714 ms.

The last interval contains a deliberate one-second
`StartupToTitleTransition` plus synchronous Title activation and resource
loading. The first-Startup-draw interval is similarly large, but the current
markers cannot identify whether post-`LoadContent` initialization, fixed-step
catch-up, asynchronous dispatch, SQLite work, or scheduling is responsible.
The current Title milestone is also earlier than visual readiness: it is
recorded when Title is current and the transition is complete, before Title's
first draw.

This design authorizes a diagnostic-only wave that measures those missing
boundaries. It does not authorize a product optimization. The resulting
evidence will support a separate product design whose conservative projection
must reach 2,000 ms or less, leaving at least 221 ms of headroom beneath the
binding 2,221 ms acceptance gate.

## Relationship to Earlier HPA-192 Work

This is a new design cycle. It does not continue or implicitly authorize Tasks
1 through 8 in
`2026-07-28-hpa-192-second-wave-startup-optimization.md`.

The diagnostic builds on:

- first-wave product commit
  `c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b`;
- committed timing instrumentation
  `5569ba548b15c5cc515897d5a3ec31b5e88e01f3`;
- the frozen 100-chart benchmark corpus and its committed manifest;
- the retained final timing-preflight artifacts; and
- the first-wave atomic import, lifecycle, cancellation, migration,
  publication, and user-data preservation contracts.

Individual ideas from the stopped second-wave design may be reconsidered only
after this diagnostic identifies the measured critical path. Their prior
approval is not implementation authority.

## Goals

1. Establish a Release-mode baseline from external process launch through the
   first completed Title draw.
2. Identify what delays the first completed Startup draw.
3. Split fresh SQLite initialization into actionable exclusive intervals.
4. Separate transition waiting from Startup deactivation, Title construction,
   Title activation, resource loading, first update, and first draw.
5. Distinguish asynchronous operation execution from synchronous task-return
   delay and later frame-based completion observation.
6. Produce a non-overlapping savings budget for the next product design.

## Binding Future Acceptance Contract

The later product optimization will be evaluated against:

- the frozen 100-chart corpus;
- isolated fresh app data;
- a fixed Release build outside the debugger;
- median external launch to first completed Title draw of 2,221 ms or less;
- at least one completed Startup draw;
- a visible Startup-to-Title transition whose duration may change; and
- all existing data, lifecycle, cancellation, rollback, and publication
  invariants.

This diagnostic does not claim or test final product acceptance. It establishes
the new endpoint baseline and the evidence needed to design toward it.

## Platform Scope

The 15-run diagnostic matrix and the 2,221 ms acceptance gate are macOS-only
measurements on the pinned benchmark machine. They are not cross-platform
performance claims.

The instrumentation is shared product code and must compile on both Mac and
Windows. With diagnostic timing enabled, both platforms emit the same pinned
`HPA192_CRITICAL_PATH` schema; with it disabled, neither emits the line.
Focused deterministic trace tests run in both platform test projects. Windows
CI must build the Windows game and pass those tests, but it does not run the
Mac-local corpus benchmark.

## Non-Goals

This wave will not:

- shorten or otherwise alter the Startup-to-Title transition;
- move song or database work onto another thread;
- preload, defer, or remove Title resources;
- change SQLite schema creation, validation, or migrations;
- introduce a direct SQLite writer;
- parallelize chart parsing;
- introduce concurrent SQLite writers;
- change production forced-enumeration behavior;
- change grouping, canonical path identity, `set.def`, or `box.def` semantics;
- change database rollback, cancellation, or publication behavior; or
- treat a diagnostic result as final HPA-192 performance acceptance.

## Architecture

### Two-stage optimization cycle

The overall optimization cycle has two stages:

1. **This diagnostic wave:** telemetry, validation, runner support, and
   evidence only.
2. **A later product wave:** separately designed and approved after the
   diagnostic report is reviewed.

The product wave may combine earlier background work, SQLite initialization
changes, a shorter visible transition, and Title-resource preloading or
deferral. The diagnostic result, not this candidate list, selects its scope.

### Existing output compatibility

The existing `HPA192_STARTUP` and `HPA192_TIMING` lines remain byte-for-byte
compatible in field names and semantics. In particular, the existing
`TitleCompleted` milestone remains in `BaseGame.Update` after Title becomes
current and the transition completes. Its `summary_to_title_ms` and
`entry_to_title_ms` fields do not move to the later draw endpoint. Its
existing one-line emission flag is independent of the companion recorder's
later success/failure publication flag, so emitting `HPA192_TIMING` cannot
suppress `HPA192_CRITICAL_PATH`.

The diagnostic adds one `HPA192_CRITICAL_PATH` line. It is emitted once, after
the first Title frame is copied to the backbuffer. The line uses the same
process-monotonic clock as the existing timing trace and includes UTC
microsecond anchors captured adjacent to process entry and that backbuffer
copy. The runner continues to pair those anchors with independent external UTC
and monotonic anchors.

Normal runs with diagnostic timing disabled emit no
`HPA192_CRITICAL_PATH` line and retain current behavior.

### Launch-owned recorder

`StartupTimingTrace` remains the launch-owned host integration point. It owns
or delegates to an internal `StartupCriticalPathTrace` recorder that:

- uses a single process-monotonic clock;
- accepts game-thread and worker-thread observations safely;
- stores bounded milestones and aggregates;
- never invokes UI callbacks;
- never controls stage, database, or shutdown decisions; and
- publishes at most one terminal critical-path line.

The recorder is not a global singleton. Tests may inject deterministic
monotonic and UTC clocks.

All recorder state is protected by one private lock. A record call captures
its timestamp, then holds the lock only long enough to update fixed-size
milestone slots, duplicate flags, bounded counters, and terminal state. It
does not format output or invoke callbacks while locked. Cross-milestone order
is validated from the terminal snapshot rather than from lock-acquisition
order, so valid concurrent worker/game-thread observations cannot fail merely
because they acquired the lock in the opposite order. Publication copies one
immutable snapshot under the lock and validates and formats it after releasing
the lock.

### Explicit database observer

Database initialization accepts an optional, explicitly passed diagnostic
observer through internal overloads. The existing public
`SongManager.InitializeDatabaseServiceAsync` contract remains unchanged and
delegates with no observer.

The observer is passed from the launch trace through Startup orchestration to
`SongManager` and `SongDatabaseService`. It must not be stored globally or in
ambient async state. Observer callbacks record timestamps only and must not
change exception, transaction, migration, or retry behavior.

### Central first-draw endpoint

Two Title draw boundaries are recorded centrally in `BaseGame.Draw` while:

- the current stage is Title; and
- no stage transition remains active.

The first boundary follows `StageManager.Draw` and measures Title stage
rendering into the virtual render target. The terminal boundary follows
`DrawRenderTargetToBackBuffer` and precedes `CompleteBaseDraw`; this is the
accepted first completed Title draw. If no valid render target is copied to
the backbuffer, the terminal milestone is not recorded and the sample is
invalid.

This endpoint is later than the current Title-activation marker and does not
depend on API polling or stdout observation. The design does not claim to
measure the platform compositor's later physical display presentation.

## Measurement Model

### Post-LoadContent initialization

Record boundaries for:

- return from `base.Initialize`, which encompasses `LoadContent`;
- `InputManager` creation;
- saved system-binding application;
- graphics-manager initialization;
- main render-target acquisition; and
- completion of `BaseGame.Initialize`.

These intervals explain the work between `LoadContent` completion, Startup
activation, normal loop entry, and the first Startup draw.

### Startup frame lifecycle

Record:

- first Startup update begin and end;
- first Startup draw begin and end;
- number of fixed-step updates before the first Startup draw; and
- accumulated game time before the first Startup draw.

Only aggregates are retained. The trace must not emit a line per frame.

### Asynchronous operation lifecycle

For both database initialization and enumeration/import, record four distinct
moments:

1. invocation begins;
2. the `Task` is returned to the Startup caller;
3. the operation records its real terminal completion internally; and
4. a later Startup update observes `Task.IsCompleted`.

Derived intervals expose:

- synchronous work before task return;
- asynchronous operation duration;
- completion-to-observation frame lag; and
- whether the task was already terminal when returned.

Worker completion records data only. It cannot advance phases or execute UI
work.

### SQLite initialization

Record exclusive intervals for:

- service construction and option setup;
- database existence/corruption probe;
- invalid-file recovery, when used;
- `EnsureCreated`;
- encoding and PRAGMA configuration;
- version-table work; and
- aggregate schema/index/foreign-key validation and migration work.

The exclusive intervals must reconcile with the enclosing database
initialization duration within the whole-millisecond truncation allowance.
Conditional intervals use an explicit zero/not-run representation rather than
omitting fields.

### Enumeration and import

Retain the existing discovery/parsing, persistence, cleanup, and hierarchy
durations. Add only:

- invocation-to-task-return;
- internal terminal completion;
- completion-observation lag; and
- enclosing operation origin relative to process entry.

Persistence is subdivided only in a later product design if the new evidence
selects its stable 433–434 ms interval as a target.

### Startup-to-Title path

Record:

- summary emitted and transition requested;
- transition update count;
- transition accumulated game time;
- transition completion entry;
- Startup deactivation begin/end;
- Title construction begin/end;
- Title activation begin/end;
- Title stage-background load in `BaseStage` and Title menu load in
  `TitleStage`;
- Title font load;
- each Title sound load and fallback;
- first Title update begin/end;
- first Title stage draw begin/end; and
- first Title backbuffer blit begin/end.

Transition update details are retained as bounded counters and aggregate
durations, never per-frame log events.

### Milestone taxonomy

The existing compatibility trace keeps its current semantics: all existing
`StartupTimingMilestone` values are ordered, first-write-wins observations.
Duplicate calls remain ignored so `HPA192_TIMING` behavior does not change.

The companion critical-path recorder has three explicit categories:

1. **Exactly-once lifecycle milestones.** Post-`LoadContent` initialization
   boundaries; database and enumeration invocation, task return, and internal
   terminal completion; SQLite subphase begin/end pairs; summary and transition
   request; transition completion; Startup deactivation; Title construction,
   activation, and resource subphase pairs; and terminal line publication.
   Duplicates invalidate the critical-path trace.
2. **First-observation milestones.** First Startup update, first Startup draw,
   first game-thread observation of each completed task, first Title update,
   first Title stage draw, and first Title backbuffer copy. Their hooks may run
   repeatedly; only the first begin/end pair is retained and later calls are
   ignored. An end is valid only for the begin captured for that same first
   observation.
3. **Bounded aggregates.** Startup updates before first draw, accumulated game
   time before first draw, completed Startup draws before transition,
   transition update count, and transition accumulated game time. Aggregates
   stop accepting updates when their enclosing lifecycle interval closes.

Conditional lifecycle work such as invalid-database recovery and the
game-start-sound fallback has a `*_ran` flag. When it does not run, its
duration is the canonical value zero and no begin/end callback is required.

### Pinned `HPA192_CRITICAL_PATH` schema

The line has the following fields in this fixed order. Every
`*_from_entry_ms` value is a process-monotonic timestamp expressed as whole
milliseconds relative to process entry. Every other `*_ms` value is a
whole-millisecond duration. The physical output is one
`HPA192_CRITICAL_PATH` prefix followed by space-separated `name=value` tokens
in exactly this order; the vertical list below names those tokens.

```text
outcome
error
entry_unix_us
title_backbuffer_unix_us
entry_to_title_backbuffer_ms

load_content_complete_from_entry_ms
base_initialize_return_from_entry_ms
input_manager_begin_from_entry_ms
input_manager_end_from_entry_ms
saved_bindings_begin_from_entry_ms
saved_bindings_end_from_entry_ms
graphics_initialize_begin_from_entry_ms
graphics_initialize_end_from_entry_ms
render_target_begin_from_entry_ms
render_target_end_from_entry_ms
initialize_complete_from_entry_ms
post_load_unattributed_ms

startup_activation_from_entry_ms
startup_first_update_begin_from_entry_ms
startup_first_update_end_from_entry_ms
startup_first_draw_begin_from_entry_ms
startup_first_draw_end_from_entry_ms
startup_updates_before_first_draw
startup_game_time_before_first_draw_ms
startup_draws_before_transition

db_invoke_from_entry_ms
db_task_return_from_entry_ms
db_terminal_from_entry_ms
db_observed_from_entry_ms
db_task_returned_terminal

enumeration_invoke_from_entry_ms
enumeration_task_return_from_entry_ms
enumeration_terminal_from_entry_ms
enumeration_observed_from_entry_ms
enumeration_task_returned_terminal

db_service_setup_ms
db_corruption_probe_ms
db_invalid_recovery_ran
db_invalid_recovery_ms
db_ensure_created_ms
db_encoding_pragmas_ms
db_version_work_ms
db_schema_validation_ms
db_init_unattributed_ms

summary_request_from_entry_ms
transition_start_from_entry_ms
transition_complete_from_entry_ms
transition_update_count
transition_game_time_ms
startup_deactivate_begin_from_entry_ms
startup_deactivate_end_from_entry_ms
title_construct_begin_from_entry_ms
title_construct_end_from_entry_ms
title_activate_begin_from_entry_ms
title_activate_end_from_entry_ms
title_first_update_begin_from_entry_ms
title_first_update_end_from_entry_ms
title_stage_draw_begin_from_entry_ms
title_stage_draw_end_from_entry_ms
title_backbuffer_blit_begin_from_entry_ms
title_backbuffer_blit_end_from_entry_ms
summary_to_title_unattributed_ms

title_background_ms
title_menu_ms
title_font_ms
title_cursor_sound_ms
title_decide_sound_ms
title_game_start_sound_ms
title_game_start_fallback_ran
title_game_start_fallback_ms
title_sound_load_count
title_activation_unattributed_ms

title_backbuffer_published
```

The database subphase durations are exclusive and, with
`db_init_unattributed_ms`, reconcile the enclosing database operation. Title
resource durations are exclusive children of
`title_activate_begin_from_entry_ms` to
`title_activate_end_from_entry_ms`; `title_activation_unattributed_ms`
reconciles that partition. The summarizer derives dispatch, operation,
observation, post-`LoadContent`, transition, and draw intervals from the
pinned origin fields and rejects negative or non-reconciling results.

Title sound instrumentation is statically bounded to cursor, decide, game
start, and the optional game-start fallback. It does not allocate a dynamic
per-sound collection or emit a line per sound. The count must equal
`3 + title_game_start_fallback_ran`.

The reconciliation fields use these exact formulas after converting every
origin difference to whole milliseconds:

```text
post_load_unattributed_ms =
    (initialize_complete - load_content_complete)
    - (base_initialize_return - load_content_complete)
    - input_manager
    - saved_bindings
    - graphics_initialize
    - render_target

db_init_unattributed_ms =
    (db_terminal - db_invoke)
    - db_service_setup
    - db_corruption_probe
    - db_invalid_recovery
    - db_ensure_created
    - db_encoding_pragmas
    - db_version_work
    - db_schema_validation

title_activation_unattributed_ms =
    (title_activate_end - title_activate_begin)
    - title_background
    - title_menu
    - title_font
    - title_cursor_sound
    - title_decide_sound
    - title_game_start_sound
    - title_game_start_fallback

summary_to_title_unattributed_ms =
    (title_backbuffer_blit_end - summary_request)
    - title_construction
    - (transition_complete - transition_start)
    - startup_deactivation
    - title_activation
    - title_first_update
    - title_stage_draw
    - title_backbuffer_blit
```

Every named duration above is the matching end origin minus its begin origin.
The producer calculates each residual from the already-truncated enclosing and
child intervals; the summarizer requires exact equality and a nonnegative
result. The duplicate `load_content_complete` and `startup_activation` origins
must also reconcile with the unchanged segmented `HPA192_TIMING` line within
the maximum truncation loss implied by that line's number of constituent
segments. A negative interval or a cross-line difference beyond that bound
invalidates the sample.

Numeric fields are canonical unsigned decimals. UTC anchors are at most
`4102444800000000` microseconds (2100-01-01 UTC); monotonic origin values and
durations are at most 300,000 ms; counters are at most 100,000; and `*_ran`,
`*_terminal`, and `*_published` fields are exactly `0` or `1`. In the success
schema, `outcome` is exactly `success` and `error` is exactly `none`. Missing,
duplicate, reordered, or unknown fields invalidate the line.

## Concurrency and Terminal Semantics

The recorder applies these rules:

- lifecycle milestones expected exactly once invalidate the trace on duplicate;
- repeatable first-observation hooks use first-write-wins semantics;
- out-of-order milestones invalidate the trace;
- cancellation and faults record their real outcome but cannot publish a
  successful artifact;
- the first terminal launch outcome dominates later worker events;
- late events cannot overwrite timestamps or change the terminal outcome;
- no recorder lock is held while awaiting work; and
- observer failures are contained so telemetry cannot alter production
  behavior.

The benchmark accepts only the complete success schema above. A trace that
reaches failure or cancellation before the backbuffer milestone emits no
success line. It may emit exactly one
`HPA192_CRITICAL_PATH_FAILURE outcome=... error=... last_milestone=...` line,
where `outcome` is `failure` or `cancellation` and both text values use the
safe single-token rules. The runner retains and rejects that attempt; the
failure line never participates in timing arithmetic.

## Diagnostic Scenarios

The fixed diagnostic matrix contains three scenarios.

### A. Fresh database, frozen 100 charts

- empty isolated app-data root;
- exact frozen 100-chart path set;
- expected 100 parsed charts and 27 logical groups; and
- normal production forced enumeration.

### B. Fresh database, empty song directory

- empty isolated app-data root;
- empty immutable song directory;
- expected zero discovered and parsed charts; and
- normal production forced enumeration.

This isolates process, graphics, SQLite creation, transition, and Title costs
from chart work.

### C. Preinitialized database, frozen 100 charts

- isolated clone of one cleanly closed, hashed seed app-data directory;
- exact frozen 100-chart path set;
- expected 100 parsed charts and 27 logical groups; and
- normal production forced enumeration.

This compares fresh schema creation with the existing-database initialization
path without changing the production enumeration contract.

## Predetermined Run Sequence

Each scenario receives five valid measured samples. The fixed interleaved
sequence is:

```text
A, B, C, B, C, A, C, A, B, A, C, B, C, B, A
```

Rules:

- the sequence is committed before measurement;
- every slot receives a clean isolated app-data root or seed clone;
- cold-first samples are retained;
- invalid attempts remain preserved with their rejection reason;
- an invalid slot is replaced only with the same scenario identity;
- replacement attempts are appended and never renumber accepted evidence;
- each slot permits at most three total attempts: the initial attempt and two
  replacements;
- a slot that remains invalid after its third attempt stops the diagnostic
  wave with `decision=stop reason=diagnostic_harness`; later slots and product
  design do not proceed; and
- setup launches and seed creation are explicitly excluded from measurement.

The runner records the fixed binary, runner, summarizer, corpus manifest,
empty-directory identity, and database-seed hashes.

## Runner and Validation

The runner starts its external UTC and monotonic anchors immediately around
process launch. It waits for:

- one valid `HPA192_STARTUP` line;
- one valid existing `HPA192_TIMING` line;
- one valid `HPA192_CRITICAL_PATH` line;
- external observation of Title; and
- clean process shutdown.

Receipt of `HPA192_CRITICAL_PATH_FAILURE` aborts the attempt immediately and
records its rejection instead of waiting for the success line.

The accepted launch-to-first-Title-draw duration is bridged from the external
launch anchors to `title_backbuffer_unix_us` and
`entry_to_title_backbuffer_ms`. Stdout delay and API polling lag remain
reported diagnostics and never enter the duration.

Every result artifact records scenario, slot, attempt, fixed hashes, corpus
identity, counts, raw lines, derived intervals, clock checks, and acceptance
status.

Validation fails closed for:

- duplicate canonical artifacts;
- duplicate or mixed scenario/slot identities;
- changed input bytes;
- missing or duplicate machine-readable lines;
- signed, overflowing, noncanonical, or out-of-range numeric fields;
- inconsistent external UTC and monotonic elapsed time;
- inconsistent process UTC and monotonic elapsed time;
- missing, duplicate, or out-of-order milestones;
- additive partition failures;
- wrong chart paths, chart counts, or logical-group counts;
- zero completed Startup draws or an invalid first-Title-backbuffer
  publication;
- acceptance-sentinel access; or
- output from a binary or runner whose hash does not match the fixed set.

Persistent invalidation is a reportable diagnostic result, not permission to
relax validation. The report retains all attempts, the repeated rejection
reason, fixed hashes, and the exact slot at which the three-attempt bound
stopped the wave.

## Diagnostic Gate

The diagnostic wave is complete only when:

- exactly five valid samples exist for each scenario;
- each valid sample has at least one completed Startup draw and exactly one
  accepted first-Title-backbuffer milestone;
- all marker partitions reconcile;
- every fixed input and artifact is hashed;
- timing-disabled behavior remains unchanged;
- focused tests pass;
- the full macOS-safe test suite passes with `ALSOFT_DRIVERS=null`;
- touched production code introduces no new compiler warnings; and
- the committed report contains raw evidence, medians, ranges, invalid
  attempts, and the ranked savings budget.

The report computes scenario medians and ranges. It does not use HTTP polling
lag, stdout observation lag, setup launches, or invalid attempts in
performance arithmetic.

## Product-Design Decision Rule

After the diagnostic report is reviewed, a new product design may be drafted
only when:

1. every proposed saving maps to a measured exclusive interval;
2. overlapping savings are not counted twice;
3. the conservative projected fresh-100 median is 2,000 ms or less;
4. the proposal preserves a visible Startup-to-Title transition;
5. the proposal preserves at least one completed Startup draw; and
6. the proposal preserves all first-wave data and lifecycle contracts.

The 2,000 ms projection is a design threshold, not a replacement acceptance
criterion. Final acceptance remains a measured median of 2,221 ms or less from
external launch through the first completed Title draw.

If no safe combination projects to 2,000 ms or less, the diagnostic wave
stops and reports that a broader runtime or packaging design is required.

## Test Strategy

### Trace unit tests

Cover:

- unchanged first-write-wins behavior for the existing compatibility trace;
- deterministic formatting;
- exact field order and rejection of missing, duplicate, reordered, or unknown
  fields;
- ordered milestones;
- duplicate lifecycle invalidation;
- first-write-wins frame markers;
- missing milestones;
- terminal dominance;
- late worker completion;
- cancellation and failure;
- the fixed failure-line schema;
- concurrent recording and terminal-snapshot validation under the private
  lock;
- overflow and bounds;
- UTC/monotonic consistency; and
- disabled mode.

### Host and stage tests

Cover:

- post-`LoadContent` initialization wiring;
- first Startup update and draw boundaries;
- first Title stage-draw marker only after `StageManager.Draw` returns;
- terminal Title marker only after a valid render-target-to-backbuffer blit;
- summary and transition request exactly once;
- transition update aggregation;
- Startup deactivation;
- Title construction and activation;
- Title resource subintervals;
- first Title update, stage draw, and backbuffer blit; and
- repeated draws without duplicate publication.

### Async lifecycle tests

For database initialization and enumeration/import, cover:

- synchronous completion;
- delayed asynchronous completion;
- synchronous work before task return;
- completion before the next Startup update;
- observation lag across multiple updates;
- fault;
- cancellation;
- deactivation and activation-generation invalidation; and
- late completion after a terminal launch outcome.

### Database observer tests

Cover fresh and preinitialized databases, conditional invalid-file recovery,
schema validation, migration failure, and observer failure containment.
Existing public initialization callers must remain source- and
behavior-compatible.

### Runner tests

Synthetic shell tests cover all fail-closed artifact, identity, clock,
partition, count, hash, input-preservation, and sentinel rules. The runner,
summarizer, and shell-test scripts must pass `bash -n`.

### Integration verification

Before measurement:

- build the Mac Release game;
- run focused timing, Startup, Title, transition, SongManager, and database
  initialization tests;
- run the full macOS-safe test suite;
- verify the Windows game builds and the shared focused trace tests pass in
  Windows CI;
- run shell integrity tests;
- verify `git diff --check`; and
- record the fixed build and tool hashes.

## Deliverables

1. Diagnostic instrumentation and validation tests.
2. Fixed runner, summarizer, and synthetic shell tests.
3. One hashed preinitialized database seed.
4. Fifteen valid retained result artifacts plus all invalid attempts.
5. Updated `docs/performance/HPA-192-startup-benchmark.md`.
6. A ranked, exclusive critical-path and savings-budget report.
7. A separately reviewed product-design recommendation or an explicit stop.

## Implementation Boundary

There are no unresolved design items. Implementation planning must preserve
the exact scope and stop after the diagnostic report; it must not append a
conditional product optimization to the same plan.
