# HPA-192 Startup Critical-Path Diagnostic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instrument the existing fresh-start path, collect a fail-closed 15-sample macOS Release diagnostic, and publish a non-overlapping critical-path report that selects the scope of a separately reviewed optimization design.

**Architecture:** The launch-owned `StartupTimingTrace` creates an optional, thread-safe `StartupCriticalPathTrace` from the same process-entry clocks. An internal host capability exposes that recorder to game and stage code without extending `IStageGame`, while an explicitly passed internal observer carries only database and enumeration events into `SongManager` and `SongDatabaseService`. The recorder alone validates and publishes the pinned success or failure line after the first valid Title render-target blit; separate shell tooling validates artifacts, runs the fixed scenario sequence, and summarizes exclusive intervals.

**Tech Stack:** .NET 8, C# 12, MonoGame 3.8, Entity Framework Core SQLite 9.0.17, Microsoft.Data.Sqlite, xUnit, Moq, Bash, SQLite CLI, Perl `Time::HiRes`, and Git.

## Global Constraints

- Treat
  `docs/superpowers/specs/2026-07-28-hpa-192-startup-critical-path-diagnostic-design.md`
  as the normative contract. If current source cannot satisfy it without a
  behavioral change, stop, amend and review the design, and then update this
  plan before continuing.
- This is a diagnostic-only wave. Do not shorten the transition, move work to
  another thread, preload or defer resources, change SQLite behavior, change
  forced enumeration, or implement any measured optimization.
- Stop after the diagnostic report and its design recommendation have been
  reviewed. A product optimization requires a new design and plan.
- Preserve the existing `HPA192_STARTUP` and `HPA192_TIMING` lines byte for
  byte in field names, order, rounding, milestone meaning, and one-line
  behavior.
- Emit the companion line only when `HPA192_CRITICAL_PATH=1`.
  `HPA192_EXIT_AFTER_CRITICAL_PATH=1` has no effect unless the companion
  recorder is enabled.
- Keep `IStageGame` unchanged. Resolve the recorder only through a null-safe
  cast to an internal `IStartupCriticalPathHost`; never cast to `BaseGame`.
- Use one process-monotonic clock for every in-process origin and duration.
  Capture the companion entry UTC value from the same adjacent process-entry
  read already owned by `StartupTimingTrace`.
- Keep one private recorder lock. Capture a timestamp before acquiring it,
  mutate only fixed-size state while holding it, and format and write output
  only after releasing it.
- Never hold a recorder, activation, manager, or database lock while awaiting
  work.
- Every observer call is optional and exception-contained. Instrumentation
  must not change return values, exception behavior, retry behavior,
  transaction behavior, publication, cancellation, or cleanup.
- Counts increment immediately before the measured call. Durations close in
  `finally`, including throwing attempts.
- The first terminal launch outcome dominates. Late worker observations may
  not alter timestamps, counters, outcome, or publication.
- Publish at most one of `HPA192_CRITICAL_PATH` or
  `HPA192_CRITICAL_PATH_FAILURE` per enabled launch, flush it, and request
  normal game exit on the next update only when the exit flag is enabled.
- A success endpoint is valid only after `StageManager.Draw` has returned for
  a non-transitioning Title stage and a non-disposed render target has been
  copied to the backbuffer. Do not include `CompleteBaseDraw`, framework
  presentation, swap, compositor, or display latency.
- Reject the first Title backbuffer sample if a screenshot request is pending.
  The benchmark must not call any HTTP, Game API, or screenshot endpoint.
- Keep every duration exclusive according to the approved formulas. Calculate
  residuals from already-truncated millisecond values so in-process
  partitions reconcile exactly.
- Keep the frozen third-party corpus machine-local. Continue using the
  committed 592-file manifest, exactly 100 supported charts, and 27 logical
  `SET.def` groups.
- Prefix repository commands with `rtk`. Use Conventional Commit subjects in
  imperative mood and under 72 characters.
- Run focused tests and a specification/code-quality review after every
  production task. Preserve unrelated user changes.

## Acceptance and Stop Conditions

The diagnostic implementation is complete only when:

- the disabled path emits neither companion prefix and preserves both existing
  timing lines;
- the enabled success line has exactly the approved 81 fields in exact order;
- all unit, lifecycle, observer, shell, and cross-line reconciliation tests
  pass;
- the Mac Release game and Windows game compile;
- the full macOS-safe suite passes with `ALSOFT_DRIVERS=null`;
- no touched production file introduces a new compiler warning;
- one clean, hashed preinitialized seed exists;
- all 15 predetermined slots have one valid artifact, with invalid attempts
  retained and no slot exceeding three attempts; and
- the committed report records hashes, raw lines, medians, ranges, invalid
  attempts, exclusive intervals, and a ranked savings budget.

Stop and report without broadening scope when:

- source reality requires a production behavior or public-contract change;
- a slot remains invalid after its third same-scenario attempt;
- fixed bytes or identities change during the run;
- a partition, clock, count, corpus, shutdown, or terminal validation fails
  persistently; or
- no safe exclusive savings combination projects Scenario A to 2,000 ms or
  less.

---

## Planned File Structure

### Recorder and host integration

- Create `DTXMania.Game/Lib/Stage/StartupCriticalPathTrace.cs`.
- Create `DTXMania.Game/Lib/Stage/StartupCriticalPathHost.cs`.
- Create `DTXMania.Test/Stage/StartupCriticalPathTraceTests.cs`.
- Modify `DTXMania.Game/Lib/Stage/StartupTimingTrace.cs`.
- Modify `DTXMania.Game/Game1.cs`.
- Modify `DTXMania.Test/Stage/StartupTimingTraceTests.cs`.
- Modify `DTXMania.Test/BaseGameTests.cs`.

### Stage and asynchronous lifecycle

- Modify `DTXMania.Game/Lib/Stage/StageManager.cs`.
- Modify `DTXMania.Game/Lib/Stage/BaseStage.cs`.
- Modify `DTXMania.Game/Lib/Stage/StartupStage.cs`.
- Modify `DTXMania.Game/Lib/Stage/TitleStage.cs`.
- Modify `DTXMania.Test/Stage/StageManagerTransitionTests.cs`.
- Modify `DTXMania.Test/Stage/BaseStageTests.cs`.
- Modify `DTXMania.Test/Stage/StartupStageLogicTests.cs`.
- Modify `DTXMania.Test/Stage/TitleStageLogicTests.cs`.

### Song and SQLite observation

- Create `DTXMania.Game/Lib/Song/StartupSongLoadTimingObserver.cs`.
- Create `DTXMania.Test/Song/StartupSongLoadTimingObserverTests.cs`.
- Modify `DTXMania.Game/Lib/Song/SongManager.cs`.
- Modify
  `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`.
- Modify `DTXMania.Test/Song/SongManagerTests.cs`.
- Modify `DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs`.
- Modify `DTXMania.Test/Song/SongDatabaseServiceTests.cs`.

### Diagnostic tooling and evidence

- Create `tools/hpa192/summarize-critical-path.sh`.
- Create `tools/hpa192/benchmark-critical-path.sh`.
- Create `tools/hpa192/test-critical-path.sh`.
- Modify `docs/performance/HPA-192-startup-benchmark.md`.
- Verify, but do not modify,
  `docs/performance/HPA-192-corpus-manifest.tsv`.

No project-file change is planned. Both test projects already include new
`.cs` files through their compile globs, and the Mac exclusions do not exclude
the deterministic tests above.

## Component Contracts

### Internal host contract

```csharp
internal interface IStartupCriticalPathHost
{
    StartupCriticalPathTrace? StartupCriticalPathTrace { get; }
}

internal static class StartupCriticalPathHost
{
    internal static StartupCriticalPathTrace? Resolve(IStageGame game) =>
        (game as IStartupCriticalPathHost)?.StartupCriticalPathTrace;
}
```

`BaseGame` implements this interface explicitly. Existing `IStageGame` test
doubles resolve `null` and require no changes.

### Explicit song observer contract

```csharp
internal enum StartupDatabaseTimingSpan
{
    ServiceSetup,
    CorruptionProbe,
    InvalidRecovery,
    EnsureCreated,
    EncodingPragmas,
    VersionWork,
    SchemaEnsures
}

internal enum StartupOperationOutcome
{
    Success,
    Failure,
    Cancellation
}

internal interface IStartupSongLoadTimingObserver
{
    void BeginDatabaseSpan(StartupDatabaseTimingSpan span);
    void EndDatabaseSpan(StartupDatabaseTimingSpan span);
    void RecordUnexpectedTableExistsPath();
    void RecordEnumerationTerminal(
        SongEnumerationResult? result,
        StartupOperationOutcome outcome);
}
```

`StartupCriticalPathTrace` implements this interface. Extension helpers named
`TryBeginDatabaseSpan`, `TryEndDatabaseSpan`,
`TryRecordUnexpectedTableExistsPath`, and
`TryRecordEnumerationTerminal` catch observer exceptions and return without
altering production control flow.

### Recorder categories

```csharp
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
```

The recorder exposes named internal methods over those fixed arrays:

```csharp
internal void RecordExactlyOnce(StartupCriticalPathMilestone milestone);
internal void RecordFirstObservationBegin(
    StartupCriticalPathMilestone begin,
    StartupCriticalPathMilestone end);
internal void RecordFirstObservationEnd(
    StartupCriticalPathMilestone begin,
    StartupCriticalPathMilestone end);
internal void BeginAggregate(StartupCriticalPathAggregate aggregate);
internal void EndAggregate(StartupCriticalPathAggregate aggregate);
internal void IncrementStartupUpdate(double elapsedSeconds);
internal void IncrementCompletedStartupDraw();
internal void IncrementTransitionUpdate(double elapsedSeconds);
internal void IncrementTitleSoundLoad();
internal void MarkTitleGameStartFallbackRan();
internal void RecordDatabaseTaskReturned(bool wasTerminal);
internal void RecordEnumerationTaskReturned(bool wasTerminal);
internal void RecordEnumerationResult(SongEnumerationResult result);
internal void RecordTitleCompletionLookup(bool cacheHit);
internal void Fail(string error, string lastMilestone, bool cancellation = false);
internal bool TryPublishTerminal(TextWriter writer);
```

`RecordExactlyOnce` rejects duplicates. The first-observation pair retains only
its first matched begin/end. Aggregates reject nested or unmatched spans, sum
repeated legal spans, and enforce the approved count bounds. Counter windows
close at first Startup draw, summary request, and transition completion.

### Pinned success-field list

`StartupCriticalPathTrace.SuccessFieldNames` is the following fixed array.
Tests and shell tooling independently pin the same order:

```text
outcome
error
entry_unix_us
title_backbuffer_unix_us
entry_to_title_backbuffer_ms
load_content_complete_from_entry_ms
startup_construct_begin_from_entry_ms
startup_construct_end_from_entry_ms
startup_activate_begin_from_entry_ms
startup_activation_from_entry_ms
startup_activate_end_from_entry_ms
load_content_return_from_entry_ms
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
enumeration_unattributed_ms
db_service_setup_ms
db_corruption_probe_ms
db_invalid_recovery_count
db_invalid_recovery_ms
db_ensure_created_count
db_ensure_created_ms
db_encoding_pragmas_ms
db_version_work_ms
db_schema_ensures_ms
db_init_unattributed_ms
summary_request_from_entry_ms
title_construct_begin_from_entry_ms
title_construct_end_from_entry_ms
transition_start_from_entry_ms
transition_complete_from_entry_ms
transition_update_count
transition_game_time_ms
startup_deactivate_begin_from_entry_ms
startup_deactivate_end_from_entry_ms
title_activate_begin_from_entry_ms
title_activate_end_from_entry_ms
title_first_update_begin_from_entry_ms
title_first_update_end_from_entry_ms
title_stage_draw_begin_from_entry_ms
title_stage_draw_end_from_entry_ms
title_backbuffer_blit_begin_from_entry_ms
title_backbuffer_blit_end_from_entry_ms
summary_to_title_unattributed_ms
title_gpu_setup_ms
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

---

## Execution and Review Protocol

- [ ] Before Task 1, verify the worktree and the approved source baseline:

  ```bash
  cd /Users/chanwaichan/workspace/DTXmaniaCX/.worktrees/hpa-192-batched-import
  rtk git status --short
  rtk git diff --quiet \
    5569ba548b15c5cc515897d5a3ec31b5e88e01f3 -- \
    DTXMania.Game DTXMania.Test
  ```

  Expected: clean status and exit 0 from the product/test comparison.

- [ ] Capture pre-change Release build warnings for later comparison:

  ```bash
  rtk mkdir -p TestResults/hpa-192/critical-path-development
  rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj \
    -c Release --no-incremental \
    > TestResults/hpa-192/critical-path-development/build-before.log
  ```

  Expected: build succeeds. Retain the log locally.

- [ ] For every production task, run its focused tests, review the diff against
  the approved design, review it for behavior drift and telemetry-induced
  exceptions, and commit only after both reviews pass.

- [ ] Do not start a later task while an earlier task's focused tests or review
  remain unresolved.

### Task 1: Build the Launch-Owned Recorder and Pinned Schema

**Files:**

- Create: `DTXMania.Game/Lib/Stage/StartupCriticalPathTrace.cs`
- Create: `DTXMania.Test/Stage/StartupCriticalPathTraceTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupTimingTrace.cs`
- Modify: `DTXMania.Test/Stage/StartupTimingTraceTests.cs`

**Consumes:** Existing injected monotonic and UTC clocks and the process-entry
capture in `StartupTimingTrace`.

**Produces:** One optional companion recorder with deterministic milestone,
aggregate, counter, terminal, validation, and publication behavior.

- [ ] **Step 1: Add failing launch and compatibility tests**

  Add tests named:

  ```text
  StartProcess_WhenCriticalPathFlagMissing_ShouldLeaveCompanionDisabled
  StartProcess_WhenCriticalPathFlagIsOne_ShouldShareEntryClocksWithCompanion
  Format_WhenCriticalPathEnabled_ShouldKeepCompatibilityLineByteForByte
  Disabled_WhenExitFlagIsOne_ShouldPublishNeitherCompanionPrefix
  ```

  Put environment-mutating tests in a
  `[CollectionDefinition("StartupCriticalPathEnvironment",
  DisableParallelization = true)]` collection, and save and restore both
  variables in `finally`. Use the deterministic `Start` overload rather than
  the process environment for all formatter arithmetic.

  Run:

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StartupTimingTraceTests"
  ```

  Expected: FAIL because the companion factory/property overload does not
  exist.

- [ ] **Step 2: Refactor process entry once and create the optional companion**

  Change the deterministic factory to:

  ```csharp
  internal static StartupTimingTrace Start(
      IMonotonicClock clock,
      IUtcMicrosecondClock wallClock,
      bool enableCriticalPath = false,
      bool exitAfterCriticalPath = false)
  ```

  Read one monotonic entry timestamp and one adjacent UTC entry value, store
  them in the compatibility trace, and pass those exact values and clock
  instances to `StartupCriticalPathTrace.Start`. `StartProcess` enables the
  companion only when `HPA192_CRITICAL_PATH` has exact value `1`, and captures
  the exit flag only when both variables have exact value `1`; `Disabled`
  always has a null companion. Expose the captured value as an immutable
  recorder property for `BaseGame`; do not reread process environment per
  frame. Do not change `TryFormatCompletedLine`.

- [ ] **Step 3: Add failing recorder-category tests**

  Cover these exact cases:

  ```text
  Publish_WhenComplete_ShouldWriteExactEightyOneFieldLineAndFlushOnce
  Publish_WhenLifecycleMilestoneDuplicates_ShouldWriteFailureOnly
  FirstObservation_WhenRepeated_ShouldKeepFirstMatchedPair
  Aggregate_WhenRepeatedWithinBound_ShouldAccumulateAndCount
  Aggregate_WhenNestedOrOverBound_ShouldInvalidate
  Counters_WhenWindowsClose_ShouldIgnoreLaterSamples
  Publish_WhenMilestoneMissingOrOutOfOrder_ShouldWriteFailureOnly
  Terminal_WhenFailureWins_ShouldIgnoreLateWorkerCompletion
  Terminal_WhenCancellationWins_ShouldUseCancellationOutcome
  Publish_WhenClockValueOverflowsOrExceedsBound_ShouldWriteFailureOnly
  Publish_WhenConcurrentEventsAcquireLockOutOfTimestampOrder_ShouldValidateSnapshot
  Publish_WhenWriterThrows_ShouldContainTelemetryFailure
  Publish_WhenDisabled_ShouldWriteNothing
  ```

  The success test must compare the entire line, assert 82 space-delimited
  tokens including the prefix, and independently compare the 81 parsed names
  to `SuccessFieldNames`.

  Run:

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StartupCriticalPathTraceTests"
  ```

  Expected: FAIL because the recorder implementation is incomplete.

- [ ] **Step 4: Implement fixed-size state and immutable terminal snapshots**

  Use arrays indexed by the milestone and aggregate enums, explicit booleans
  for first-observation pairs, and scalar counters. Store elapsed game time as
  `TimeSpan` ticks and truncate once at snapshot creation. Do not allocate a
  per-frame or per-sound collection.

  Validate async moments as a dependency graph, not by enum ordinal:

  ```text
  invoke <= task_return <= observed
  invoke <= terminal <= observed
  task_returned_terminal=0 requires task_return <= terminal
  task_returned_terminal=1 requires terminal <= task_return
  ```

  Apply this independently to database initialization and enumeration. Equal
  timestamps are valid. This permits a completed async method to record its
  internal terminal before its completed `Task` is returned to the caller.

  `TryPublishTerminal` must:

  1. return `false` without reserving anything when the trace is still open and
     no first blit-end exists;
  2. under the lock, let an existing failure/cancellation win or atomically
     reserve the post-blit terminal snapshot so later events are ignored;
  3. copy that immutable snapshot under the same lock;
  4. release the lock;
  5. validate required milestones, temporal order, bounds, counts, flags, and
     residual formulas;
  6. choose success or the fixed failure schema;
  7. write exactly one line and call `Flush`; and
  8. mark publication once without allowing a later terminal change.

  A writer exception is caught, returns `false`, schedules no automatic exit,
  and never escapes into the game loop. It leaves no accepted line, so the
  external runner rejects or times out that attempt.

  The failure line is exactly:

  ```text
  HPA192_CRITICAL_PATH_FAILURE outcome=<failure|cancellation> error=<safe-token> last_milestone=<safe-token>
  ```

  Safe tokens retain only ASCII letters, digits, `.`, `_`, and `-`; empty
  values become `unknown`.

- [ ] **Step 5: Implement exact producer arithmetic**

  Convert origins with checked integer arithmetic:

  ```csharp
  long MillisecondsFromEntry(long timestamp) =>
      checked((timestamp - _entryTimestamp) * 1_000 / _clock.TimestampFrequency);
  ```

  Calculate each origin-derived child first, then calculate these residuals
  from those truncated values:

  ```text
  post_load_unattributed =
      initialize_complete - load_content_complete
      - startup_construct - startup_activate
      - (base_initialize_return - load_content_return)
      - input_manager - saved_bindings - graphics_initialize - render_target

  db_init_unattributed =
      db_terminal - db_invoke
      - db_service_setup - db_corruption_probe - db_invalid_recovery
      - db_ensure_created - db_encoding_pragmas
      - db_version_work - db_schema_ensures

  enumeration_unattributed =
      enumeration_terminal - enumeration_invoke
      - discovery_parse - persistence - cleanup - hierarchy

  title_activation_unattributed =
      title_activate_end - title_activate_begin
      - title_gpu_setup - title_background - title_menu - title_font
      - title_cursor_sound - title_decide_sound
      - title_game_start_sound - title_game_start_fallback

  summary_to_title_unattributed =
      title_backbuffer_blit_end - summary_request
      - title_construct - (transition_complete - transition_start)
      - startup_deactivate - title_activate - title_first_update
      - title_stage_draw - title_backbuffer_blit
  ```

  Reject a negative result. Require UTC values at most
  `4102444800000000`, origins/durations at most `300000` ms, counters at most
  `100000`, recovery count at most 2, ensure-created count from 1 through 2,
  and all boolean fields to be 0 or 1.

- [ ] **Step 6: Run recorder and compatibility tests**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StartupCriticalPathTraceTests|FullyQualifiedName~StartupTimingTraceTests"
  ```

  Expected: PASS.

- [ ] **Step 7: Review and commit**

  Verify that `StartupTimingTrace.TryFormatCompletedLine` has no output delta,
  that no callback or writer is invoked while locked, and that every recorder
  mutation returns harmlessly after terminal state.

  ```bash
  rtk git diff --check
  rtk git add \
    DTXMania.Game/Lib/Stage/StartupCriticalPathTrace.cs \
    DTXMania.Game/Lib/Stage/StartupTimingTrace.cs \
    DTXMania.Test/Stage/StartupCriticalPathTraceTests.cs \
    DTXMania.Test/Stage/StartupTimingTraceTests.cs
  rtk git commit -m "feat: add startup critical-path recorder"
  ```

### Task 2: Wire the Internal Host, Initialization Tail, Blit, and Exit

**Files:**

- Create: `DTXMania.Game/Lib/Stage/StartupCriticalPathHost.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`
- Modify: `DTXMania.Test/Stage/StartupTimingTraceTests.cs`

**Consumes:** The companion recorder from Task 1.

**Produces:** Host-only access, post-`LoadContent` initialization origins,
central Title draw/blit boundaries, failure publication, and next-update exit.

- [ ] **Step 1: Add failing host-capability tests**

  Add:

  ```text
  Resolve_WhenGameDoesNotImplementHost_ShouldReturnNull
  Resolve_WhenBaseGameOwnsEnabledTrace_ShouldReturnCompanion
  Resolve_ShouldNotRequireConcreteBaseGame
  ```

  The third test uses an `IStageGame` test double that implements only the
  internal host interface. Do not add methods to `IStageGame`.

- [ ] **Step 2: Implement the internal host facade**

  Add the exact interface/helper from **Component Contracts**. Implement the
  interface explicitly in `BaseGame`:

  ```csharp
  StartupCriticalPathTrace?
      IStartupCriticalPathHost.StartupCriticalPathTrace =>
          _startupTimingTrace?.CriticalPathTrace;
  ```

- [ ] **Step 3: Add failing initialization-tail tests**

  Extend the existing `LoadContentTestableBaseGame` seams and add tests for:

  ```text
  Initialize_WhenDiagnosticEnabled_ShouldRecordPostLoadBoundariesInOrder
  LoadContent_WhenStartupChangesStage_ShouldRecordReturnAfterActivation
  LoadContent_WhenGameApiDisabled_ShouldNotQueueApiStartup
  Initialize_WhenDiagnosticDisabled_ShouldKeepExistingCallsUnchanged
  ```

  Assert the following origin order:

  ```text
  load_content_complete
  startup construction and activation
  load_content_return
  base_initialize_return
  input manager begin/end
  saved bindings begin/end
  graphics initialize begin/end
  render target begin/end
  initialize complete
  ```

  Run the new tests. Expected: FAIL on missing origins.

- [ ] **Step 4: Instrument initialization without moving work**

  Place begin/end markers immediately around the existing expressions. Mark
  `LoadContentReturn` at the end of the override, `BaseInitializeReturn`
  immediately after `base.Initialize()`, and `InitializeComplete` after the
  existing final render-target log. Use `try/finally` for paired durations
  while preserving existing exception propagation.

  At the existing `MarkLoadContentComplete` call, also record the companion
  `LoadContentComplete` origin. In `ReportStartupActivated`, keep the
  compatibility marker and record companion `StartupActivation` at that same
  report point. Do not map compatibility `StartupFirstDraw`,
  `SummaryAndTitleRequested`, or `TitleCompleted` onto companion milestones;
  their approved companion boundaries are deliberately different.

  Do not force-disable the API in product code. The fixed configuration uses
  `EnableGameApi=False`; the test proves that existing branch and the runner
  rejects any other configuration.

- [ ] **Step 5: Add failing Title endpoint and shutdown tests**

  Extend `DrawHarnessBaseGame` with an injected `StringWriter` and a
  `RequestGameExit` call counter. Add:

  ```text
  Draw_WhenTitleReady_ShouldEndStageDrawAfterStageManagerDrawReturns
  Draw_WhenValidRenderTargetBlits_ShouldPublishAfterBlitBeforeCompleteBaseDraw
  Draw_WhenRenderTargetMissingOrDisposed_ShouldNotPublishSuccess
  Draw_WhenFirstTitleFrameHasPendingScreenshot_ShouldPublishFailure
  Draw_WhenRepeatedAfterPublication_ShouldNotPublishAgain
  Update_WhenExitAfterPublicationEnabled_ShouldExitOnFollowingUpdate
  Update_WhenExitFlagEnabledButRecorderDisabled_ShouldNotExit
  OnGameExiting_BeforeSuccess_ShouldPublishExitFailureAndStillFlushConfig
  ```

  Preserve the existing draw-call order assertions for render target binding,
  clearing, screenshot fulfillment, blit, and `CompleteBaseDraw`.

- [ ] **Step 6: Implement central publication and next-update exit**

  Add these test seams:

  ```csharp
  internal virtual TextWriter StartupDiagnosticWriter => Console.Out;
  internal virtual void RequestGameExit() => Exit();
  ```

  At the beginning of `Update`, consume a previously scheduled diagnostic exit
  and return after calling `RequestGameExit`. A terminal line published during
  the current update or draw sets the pending flag but never exits in that same
  callback. Schedule it only when the enabled recorder's immutable
  `ExitAfterPublication` property is true.

  In `Draw`:

  1. detect non-transitioning Title before `StageManager.Draw`;
  2. record first stage-draw begin;
  3. call `StageManager.Draw`;
  4. record first stage-draw end;
  5. reject the trace if the exchanged screenshot request is non-null;
  6. record blit begin only for a valid render target;
  7. perform `DrawRenderTargetToBackBuffer`;
  8. record blit end;
  9. ask the recorder to publish and flush; and
  10. call `CompleteBaseDraw`.

  If drawing or blitting throws, do not synthesize its end milestone.
  Capture `title_backbuffer_unix_us` inside the recorder immediately adjacent
  to the accepted first blit-end timestamp.

  After the existing stage update and compatibility `TitleCompleted` handling,
  call `TryPublishTerminal` once so a worker-recorded failure or cancellation
  is flushed without waiting for a draw. A nonterminal successful trace returns
  `false` there and can publish only from the later post-blit call. Call the
  same method from `OnGameExiting` after declaring `exit_before_success`.

- [ ] **Step 7: Run host and BaseGame tests**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~BaseGameTests|FullyQualifiedName~StartupTimingTraceTests|FullyQualifiedName~StartupCriticalPathTraceTests"
  ```

  Expected: PASS.

- [ ] **Step 8: Review and commit**

  Confirm the current `HPA192_TIMING` emission remains in `BaseGame.Update`
  against `TitleCompleted`, the new success endpoint is later, no HTTP
  readiness path was introduced, and ordinary exit/config flushing is intact.

  ```bash
  rtk git diff --check
  rtk git add \
    DTXMania.Game/Game1.cs \
    DTXMania.Game/Lib/Stage/StartupCriticalPathHost.cs \
    DTXMania.Test/BaseGameTests.cs \
    DTXMania.Test/Stage/StartupTimingTraceTests.cs
  rtk git commit -m "feat: trace startup host lifecycle"
  ```

### Task 3: Instrument Stage Construction, Activation, and Transition

**Files:**

- Modify: `DTXMania.Game/Lib/Stage/StageManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/BaseStage.cs`
- Modify: `DTXMania.Test/Stage/StageManagerTransitionTests.cs`
- Modify: `DTXMania.Test/Stage/BaseStageTests.cs`

**Consumes:** Host resolution from Task 2.

**Produces:** Startup/Title construction, Startup/Title activation, background,
transition, cache-hit, Startup deactivation, first-update, and first-draw
evidence.

- [ ] **Step 1: Add failing lazy-construction and transition tests**

  Add:

  ```text
  GetOrCreateStage_WhenStartupCacheMiss_ShouldRecordConstructionAroundWiring
  ChangeStage_WhenTitleCacheMiss_ShouldConstructBeforeTransitionStart
  CompleteTransition_WhenTitleLookupRepeats_ShouldRequireCacheHit
  Update_WhenTitleTransitionRuns_ShouldAccumulateCountAndGameTime
  CompleteTransition_ShouldRecordCompletionBeforeStartupDeactivation
  CompleteTransition_WhenTitleConstructsTwice_ShouldInvalidateTrace
  ```

  Use an internal host test double with a deterministic trace. Verify the
  required timestamp order, not recorder-lock acquisition order.

- [ ] **Step 2: Instrument `StageManager` at existing control-flow points**

  In `GetOrCreateStage`, wrap only Startup and Title cache misses. The end
  marker follows `StageManager` assignment and `_stages` insertion so
  construction includes wiring and caching. Use `try/finally` so a throwing
  construction attempt still closes its real span; the missing successful
  cache insertion then prevents a success snapshot.

  In `ChangeStage`, record `TransitionStart` immediately before
  `_currentTransition.Start()` only when the current stage is Startup and the
  target is Title. Do not include `GetOrCreateStage` in the transition
  interval. The initial null-to-Startup instant transition must emit no
  Startup-to-Title transition marker.

  In `Update`, increment the transition counter and accumulated `deltaTime`
  immediately before `_currentTransition.Update(deltaTime)` only for that
  recorded Startup-to-Title transition.

  At `CompleteTransition` entry for the Startup-to-Title path, record
  `TransitionComplete`; wrap the outgoing Startup `Deactivate()` call with
  exact begin/end markers. Require the second Title lookup to hit the cache; a
  miss is already rejected as duplicate Title construction. Other transitions
  retain their current behavior without touching these markers.

- [ ] **Step 3: Add failing BaseStage boundary tests**

  Add:

  ```text
  Activate_WhenStartup_ShouldBeginAfterInactiveGuardAndEndAfterOnActivate
  Activate_WhenTitle_ShouldIncludeBackgroundAndOnActivate
  Activate_WhenAlreadyActive_ShouldNotRecordAnotherActivation
  LoadStageBackground_WhenTitleLoadThrows_ShouldCloseMeasuredSpan
  Update_WhenStartupFirstCall_ShouldRecordWholeFirstUpdateOnce
  Update_WhenTitleFirstCall_ShouldRecordWholeFirstUpdateOnce
  Draw_WhenStartupFirstCall_ShouldRecordWholeFirstDrawOnce
  Draw_WhenStartupCompletes_ShouldCountDrawsUntilSummaryClosesWindow
  ```

  Assert Title background load time is a child of Title activation, and that a
  background exception remains swallowed exactly as today.

- [ ] **Step 4: Instrument `BaseStage` through the helper**

  Resolve the optional trace once per method. In `Activate`, keep the inactive
  early return first, then record Startup or Title begin. End in `finally`
  after `OnActivate`.

  In `LoadStageBackground`, wrap only the Title load attempt with the
  `TitleBackground` aggregate.

  In first Startup/Title `Update` and first Startup `Draw`, use the recorder's
  first-observation pair in `try/finally`. Increment the Startup update
  aggregate before the update body and increment completed Startup draws only
  after a successful draw body returns.

- [ ] **Step 5: Run stage lifecycle tests**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StageManagerTransitionTests|FullyQualifiedName~BaseStageTests|FullyQualifiedName~StartupCriticalPathTraceTests"
  ```

  Expected: PASS.

- [ ] **Step 6: Review and commit**

  Verify non-Startup/Title stages execute the same branches with a null or
  ignored recorder, instant transitions preserve their current synchronous
  completion, and the completion-time Title lookup emits no second
  construction milestone.

  ```bash
  rtk git diff --check
  rtk git add \
    DTXMania.Game/Lib/Stage/StageManager.cs \
    DTXMania.Game/Lib/Stage/BaseStage.cs \
    DTXMania.Test/Stage/StageManagerTransitionTests.cs \
    DTXMania.Test/Stage/BaseStageTests.cs
  rtk git commit -m "feat: trace startup stage transitions"
  ```

### Task 4: Add the Exception-Safe Observer and Trace SQLite Initialization

**Files:**

- Create: `DTXMania.Game/Lib/Song/StartupSongLoadTimingObserver.cs`
- Create: `DTXMania.Test/Song/StartupSongLoadTimingObserverTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupCriticalPathTrace.cs`
- Modify:
  `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Modify: `DTXMania.Test/Song/SongDatabaseServiceTests.cs`

**Consumes:** Fixed aggregates and terminal semantics from Task 1.

**Produces:** An explicitly passed, exception-contained observer and exclusive
service-level SQLite initialization spans.

- [ ] **Step 1: Add the observer contract and failing containment tests**

  Add the exact observer interface and enums from **Component Contracts**, plus
  extension methods that invoke one callback inside `try/catch`.

  Add tests:

  ```text
  TryBeginDatabaseSpan_WhenObserverThrows_ShouldNotPropagate
  TryEndDatabaseSpan_WhenObserverThrows_ShouldNotPropagate
  TryRecordEnumerationTerminal_WhenObserverThrows_ShouldNotPropagate
  TryRecordUnexpectedTableExistsPath_WhenObserverThrows_ShouldNotPropagate
  Extensions_WhenObserverIsNull_ShouldBeNoOps
  ```

  Run:

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StartupSongLoadTimingObserverTests"
  ```

  Expected: FAIL before the contract exists, then PASS after the no-op helpers
  are implemented.

- [ ] **Step 2: Make the critical recorder the concrete observer**

  Implement `IStartupSongLoadTimingObserver` explicitly. Map each database
  span to its corresponding `StartupCriticalPathAggregate`.
  `RecordUnexpectedTableExistsPath` declares terminal failure with error
  `unexpected_table_exists_path`.

  Map enumeration success to `EnumerationTerminal` plus
  `RecordEnumerationResult`; require a non-null result. Map failure and
  cancellation to the real terminal timestamp and the matching first terminal
  launch outcome. Add focused adapter tests for all three outcomes. Task 5
  supplies these callbacks from the actual `EndEnumeration` boundary.

- [ ] **Step 3: Add failing fresh/preinitialized SQLite observation tests**

  Extend real temporary-file tests with recording and throwing observers:

  ```text
  InitializeDatabaseAsync_WhenFresh_ShouldRecordOneEnsureAndSchemaPartition
  InitializeDatabaseAsync_WhenPreinitialized_ShouldRecordProbeAndSchemaPartition
  InitializeDatabaseAsync_WhenObserverThrows_ShouldStillInitialize
  InitializeDatabaseAsync_WhenInvalidFileRecovered_ShouldCountRecovery
  ```

  The public method remains:

  ```csharp
  public Task InitializeDatabaseAsync() =>
      InitializeDatabaseAsync(observer: null);
  ```

  Add only an internal overload:

  ```csharp
  internal async Task InitializeDatabaseAsync(
      IStartupSongLoadTimingObserver? observer)
  ```

  Run the focused tests. Expected: FAIL because no spans are emitted.

- [ ] **Step 4: Instrument probes, recovery, creation, and configuration**

  Preserve the semaphore, initialized guard, catch filters, and assignments.
  Wrap these exact expressions:

  - `IsValidSqliteDatabaseAsync` and
    `HasProperUnicodeConfigurationAsync` separately into the accumulated
    `CorruptionProbe` bucket;
  - every `HandleInvalidDatabaseFileAsync` call in
    `InvalidRecovery`;
  - each `EnsureCreatedAsync` call in `EnsureCreated`, including the first
    throwing attempt and retry;
  - only the two PRAGMAs plus their local catch in `EncodingPragmas`;
  - only `EnsureDatabaseVersionTableAsync` in `VersionWork`; and
  - the four schema ensure calls together in `SchemaEnsures`.

  Begin each span immediately before its call and end in `finally`.
  `InvalidRecovery` and `EnsureCreated` counts increment in the recorder on
  begin. Do not access `context.Model` and do not add a context-construction
  bucket.

- [ ] **Step 5: Add narrow test seams for caught retry branches**

  Change only the initialization helpers required to script existing branches
  from `private` to `internal virtual`:

  ```csharp
  internal virtual bool InitializationDatabaseFileExists();
  internal virtual SongDbContext CreateInitializationContext();
  internal virtual Task<bool> IsValidSqliteDatabaseAsync();
  internal virtual Task<bool> HasProperUnicodeConfigurationAsync();
  internal virtual Task HandleInvalidDatabaseFileAsync();
  internal virtual Task EnsureCreatedForInitializationAsync(
      SongDbContext context);
  ```

  Their default bodies must be the current expressions. Add an internal
  constructor overload that accepts options, a context factory, and an
  `initialized` boolean so tests can exercise initialization without changing
  the existing test-friendly constructor's initialized semantics.

- [ ] **Step 6: Test throwing attempts and terminal catch paths**

  Add:

  ```text
  InitializeDatabaseAsync_WhenFirstEnsureThrowsNotDatabase_ShouldMeasureBothAttempts
  InitializeDatabaseAsync_WhenEnsureThrowsTableExists_ShouldNotifyObserverAndKeepProductionSuccess
  InitializeDatabaseAsync_WhenSchemaEnsureThrows_ShouldCloseSpanAndPropagate
  InitializeDatabaseAsync_WhenVersionWorkSwallowsFailure_ShouldRetainCurrentSuccess
  ```

  Assert counts are incremented before calls, elapsed spans remain nonzero for
  scripted throwing attempts, and the table-exists branch still sets
  `_isInitialized` while its critical trace becomes failure-only.

- [ ] **Step 7: Run SQLite and observer tests**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StartupSongLoadTimingObserverTests|FullyQualifiedName~SongDatabaseServiceTests|FullyQualifiedName~StartupCriticalPathTraceTests"
  ```

  Expected: PASS.

- [ ] **Step 8: Review and commit**

  Compare every observed call with the original ordering. Confirm the
  best-effort PRAGMA/version catches still swallow exactly what they swallowed,
  schema failures still propagate, and retry/table-exists behavior is
  unchanged when the observer is null.

  ```bash
  rtk git diff --check
  rtk git add \
    DTXMania.Game/Lib/Song/StartupSongLoadTimingObserver.cs \
    DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs \
    DTXMania.Game/Lib/Stage/StartupCriticalPathTrace.cs \
    DTXMania.Test/Song/StartupSongLoadTimingObserverTests.cs \
    DTXMania.Test/Song/SongDatabaseServiceTests.cs
  rtk git commit -m "feat: trace SQLite startup initialization"
  ```

### Task 5: Pass the Observer Through SongManager and Close Enumeration

**Files:**

- Modify: `DTXMania.Game/Lib/Song/SongManager.cs`
- Modify: `DTXMania.Test/Song/SongManagerTests.cs`
- Modify: `DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs`

**Consumes:** The observer and service overload from Task 4.

**Produces:** Manager setup/probe/recovery timing and a true enumeration
terminal callback after `EndEnumeration`.

- [ ] **Step 1: Add failing public-compatibility and manager-span tests**

  Add:

  ```text
  InitializeDatabaseServiceAsync_PublicOverload_ShouldUseNullObserver
  InitializeDatabaseServiceAsync_WhenCreatingService_ShouldMeasureSetupOnce
  InitializeDatabaseServiceAsync_WhenServiceExists_ShouldNotMeasureSetupAgain
  InitializeDatabaseServiceAsync_ShouldMeasureManagerCorruptionProbe
  InitializeDatabaseServiceAsync_WhenPurgeRuns_ShouldMeasureRecovery
  InitializeDatabaseServiceAsync_WhenObserverThrows_ShouldPreserveBooleanResult
  ```

  Keep the existing public signature unchanged and delegate to:

  ```csharp
  internal Task<bool> InitializeDatabaseServiceAsync(
      string? databasePath,
      bool purgeDatabaseFirst,
      IStartupSongLoadTimingObserver? observer);
  ```

- [ ] **Step 2: Instrument actual service construction and manager calls**

  Inside `_lockObject`, enter `ServiceSetup` only when
  `_databaseService` is null, and close it after the first
  `new SongDatabaseService(resolvedDatabasePath)` returns or throws. Do not
  time a lock-only cache hit.

  Wrap the manager's complete `IsDatabaseCorruptedAsync(db)` call in
  `CorruptionProbe`. Wrap either manager `PurgeDatabaseAsync` call in
  `InvalidRecovery`. Pass the same observer to
  `db.InitializeDatabaseAsync(observer)`.

- [ ] **Step 3: Add failing enumeration-terminal tests**

  Add:

  ```text
  EnumerateAndImportSongsAsync_PublicOverload_ShouldUseNullObserver
  EnumerateAndImportSongsAsync_WhenSuccessful_ShouldNotifyAfterEndEnumeration
  EnumerateAndImportSongsAsync_WhenFaulted_ShouldNotifyFailureAfterCleanup
  EnumerateAndImportSongsAsync_WhenCancelled_ShouldNotifyCancellationAfterCleanup
  EnumerateAndImportSongsAsync_WhenObserverThrows_ShouldPreserveResultOrException
  ```

  Add:

  ```csharp
  internal Task<SongEnumerationResult> EnumerateAndImportSongsAsync(
      string[] searchPaths,
      IProgress<EnumerationProgress>? progress,
      CancellationToken cancellationToken,
      IStartupSongLoadTimingObserver? observer);
  ```

- [ ] **Step 4: Notify terminal outcome after cleanup**

  In `EnumerateAndImportSongsCoreAsync`, retain a nullable result and an
  outcome initialized to failure. Set success only after the result has been
  constructed. Catch only `OperationCanceledException` to select cancellation
  and rethrow. In `finally`, call `EndEnumeration(linked)` first and then the
  exception-contained terminal observer.

  The successful callback receives the exact `SongEnumerationResult` instance
  whose four `TimeSpan` children feed `HPA192_STARTUP`. The critical recorder
  records `EnumerationTerminal`, truncates those four values once, and
  calculates `enumeration_unattributed_ms`. A failure/cancellation callback
  records the real terminal timestamp and declares the corresponding terminal
  launch outcome.

- [ ] **Step 5: Run SongManager tests**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~SongManagerTests|FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~StartupSongLoadTimingObserverTests|FullyQualifiedName~StartupCriticalPathTraceTests"
  ```

  Expected: PASS.

- [ ] **Step 6: Review and commit**

  Verify public callers compile unchanged, `Clear` still retires and disposes
  manager state, enumeration publication still precedes result construction,
  and observer notification alone follows `EndEnumeration`.

  ```bash
  rtk git diff --check
  rtk git add \
    DTXMania.Game/Lib/Song/SongManager.cs \
    DTXMania.Test/Song/SongManagerTests.cs \
    DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs
  rtk git commit -m "feat: observe startup song operations"
  ```

### Task 6: Trace Startup Async Dispatch, Completion, and Observation

**Files:**

- Modify: `DTXMania.Game/Lib/Stage/StartupStage.cs`
- Modify: `DTXMania.Test/Stage/StartupStageLogicTests.cs`

**Consumes:** Recorder APIs from Task 1 and the observer/manager overloads from
Tasks 4 and 5.

**Produces:** Database/enumeration invocation, task return, internal terminal,
game-thread observation, summary request, failure, cancellation, and
activation-generation evidence.

- [ ] **Step 1: Add failing database lifecycle tests**

  Add deterministic test subclasses and tests for:

  ```text
  DatabaseTask_WhenSynchronous_ShouldRecordInvokeReturnTerminalAndObserved
  DatabaseTask_WhenDelayed_ShouldSeparateReturnTerminalAndObservation
  DatabaseTask_WhenCompletionWaitsFrames_ShouldRecordObservationLag
  DatabaseTask_WhenCoreReturnsFalse_ShouldFailDiagnosticOnly
  DatabaseTask_WhenCoreThrows_ShouldRetainExistingStartupBehaviorAndFailTrace
  ```

  Preserve the existing protected virtual one-argument initialization seam as
  the external subclass compatibility surface. Keep the observer internal and
  add a same-assembly derived-class overload:

  ```csharp
  private protected virtual Task<bool> InitializeDatabaseServiceCoreAsync(
      string databasePath,
      IStartupSongLoadTimingObserver? observer)
  ```

  When `observer` is null, delegate to the existing virtual method so current
  subclasses and tests keep their behavior.

- [ ] **Step 2: Place database anchors around the existing stopwatch**

  At entry to `InitializeDatabaseServiceForActivationAsync`, record
  `DatabaseInvoke` adjacent to `Stopwatch.StartNew`. Stop the stopwatch and
  record `DatabaseTerminal` in `finally` before conditionally publishing
  `_databaseInitializationDuration`.

  In `PerformPhaseOperationSync`, call the async wrapper, assign its returned
  task, then record `DatabaseTaskReturn` and the immediate `IsCompleted` flag.
  In `UpdateCurrentPhase`, record `DatabaseObserved` on the first game-thread
  update that observes completion.

  A `false` result declares diagnostic failure
  `database_initialization_failed` but leaves the existing Startup phase
  behavior unchanged.

- [ ] **Step 3: Add failing enumeration lifecycle tests**

  Cover:

  ```text
  EnumerationTask_WhenSynchronous_ShouldRecordAllFourMoments
  EnumerationTask_WhenDelayed_ShouldSeparateReturnTerminalAndObserved
  EnumerationTask_WhenFaulted_ShouldPublishFailureAfterCleanup
  EnumerationTask_WhenCancelled_ShouldPublishCancellationAfterCleanup
  EnumerationTask_WhenActivationRetiresWhilePending_ShouldFailInvalidation
  EnumerationTask_WhenLateCompletionFollowsTerminal_ShouldNotMutateTrace
  ```

  Preserve the existing protected virtual three-argument enumeration seam as
  the external subclass compatibility surface. Keep the observer internal and
  add a same-assembly derived-class overload:

  ```csharp
  private protected virtual Task<SongEnumerationResult> EnumerateSongsCoreAsync(
      string[] songPaths,
      IProgress<EnumerationProgress> progressReporter,
      CancellationToken cancellationToken,
      IStartupSongLoadTimingObserver? observer)
  ```

  Delegate to the existing virtual overload when the observer is null.

- [ ] **Step 4: Place enumeration and summary anchors**

  Immediately before `EnumerateSongsCoreAsync`, record
  `EnumerationInvoke`. Immediately after it returns a `Task`, record
  `EnumerationTaskReturn` and `Task.IsCompleted`. Pass the observer through to
  `SongManager`; Task 5 records the true terminal moment after
  `EndEnumeration`.

  Record `EnumerationObserved` when the later Startup update first sees its
  enclosing song-load task complete. When retiring an activation, declare
  `activation_generation_invalidated` only if its async operation remains
  incomplete; normal completed Startup deactivation must remain valid.

  In the completion branch, preserve this order:

  ```text
  WriteSummaryOnce
  ReportStartupSummaryAndTitleRequested
  Record SummaryRequest
  ChangeStage(Title, StartupToTitleTransition)
  ```

  The marker is immediately before `ChangeStage`, after the compatibility
  report, and closes Startup draw/counter windows.

- [ ] **Step 5: Run Startup lifecycle tests**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StartupStageLogicTests|FullyQualifiedName~StartupCriticalPathTraceTests"
  ```

  Expected: PASS.

- [ ] **Step 6: Review and commit**

  Verify worker completion records data only, phase changes still occur on the
  game thread, the existing stopwatch feeds the unchanged summary, normal
  deactivation stays valid, and no activation lock is held across an await.

  ```bash
  rtk git diff --check
  rtk git add \
    DTXMania.Game/Lib/Stage/StartupStage.cs \
    DTXMania.Test/Stage/StartupStageLogicTests.cs
  rtk git commit -m "feat: trace startup async lifecycle"
  ```

### Task 7: Trace Title GPU and Resource Loading

**Files:**

- Modify: `DTXMania.Game/Lib/Stage/TitleStage.cs`
- Modify: `DTXMania.Test/Stage/TitleStageLogicTests.cs`

**Consumes:** The outer Title activation and background spans from Task 3.

**Produces:** Exclusive GPU, menu, font, primary sound, and explicit fallback
durations plus bounded sound counts.

- [ ] **Step 1: Add narrow graphics creation seams and failing GPU tests**

  Add protected virtual seams whose default bodies are the current operations:

  ```csharp
  protected virtual SpriteBatch CreateTitleSpriteBatch(
      GraphicsDevice graphicsDevice);
  protected virtual Texture2D CreateTitleWhitePixel(
      GraphicsDevice graphicsDevice);
  protected virtual void SetTitleWhitePixelData(Texture2D texture);
  ```

  Add:

  ```text
  OnActivate_ShouldMeasureOneGpuAggregateAroundAllThreeOperations
  OnActivate_WhenGpuSetupThrows_ShouldCloseAggregateAndPropagate
  ```

  The aggregate starts before `SpriteBatch` construction and ends after
  `SetData`, in `finally`. Do not split it into three output fields.

- [ ] **Step 2: Add failing menu and font timing tests**

  Add:

  ```text
  LoadMenuTexture_ShouldMeasureAttemptWhenLoadSucceeds
  LoadMenuTexture_ShouldMeasureAttemptWhenLoadThrows
  LoadVersionFont_ShouldMeasureOnlyLoadAttempt
  LoadVersionFont_ShouldMeasureAttemptWhenLoadThrows
  ```

  In `LoadMenuTexture`, wrap only the
  `_resourceManager.LoadTexture(TexturePath.TitleMenu)` expression. In
  `LoadVersionFont`, leave reference removal and nulling outside the aggregate
  and wrap only `_resourceManager.LoadFont("NotoSerifJP", 14)`. Keep the
  existing catches and optional-resource behavior; their logging time remains
  in `title_activation_unattributed_ms`.

- [ ] **Step 3: Add failing bounded sound tests**

  Extend the existing sound tests to assert:

  ```text
  LoadSoundEffects_WhenPrimaryLoadsSucceed_ShouldCountThreeAttempts
  LoadSoundEffects_WhenPrimaryReturnsNull_ShouldRetainMeasuredDuration
  LoadSoundEffects_WhenGameStartThrows_ShouldMarkFallbackAndCountFourAttempts
  LoadSoundEffects_WhenFallbackThrows_ShouldCloseBothAttemptSpans
  LoadSoundEffects_WhenResourceManagerUsesSilentFallback_ShouldNotMarkTitleFallback
  ```

  Increment `title_sound_load_count` immediately before every `LoadSound`
  call. Begin the sound's aggregate before the call and close in `finally`.
  Set `title_game_start_fallback_ran` immediately before only the explicit
  second call in the catch block.

- [ ] **Step 4: Implement resource spans without changing catches**

  Resolve the optional trace through `StartupCriticalPathHost.Resolve(_game)`.
  For menu, font, and sound fields, begin immediately before the resource
  manager call and end in an inner `finally` immediately after that call
  returns or throws. Preserve all existing assignments, debug messages, and
  catch blocks outside the measured call. Conditional fallback duration
  remains canonical zero when its call never runs.

- [ ] **Step 5: Run Title and enclosing-stage tests**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~TitleStageLogicTests|FullyQualifiedName~BaseStageTests|FullyQualifiedName~StartupCriticalPathTraceTests"
  ```

  Expected: PASS.

- [ ] **Step 6: Review and commit**

  Confirm exactly three primary calls remain, the fallback call remains only
  in the Game Start catch, a null/silent fallback is not reclassified, and
  Title activation residual subtracts only exclusive child spans.

  ```bash
  rtk git diff --check
  rtk git add \
    DTXMania.Game/Lib/Stage/TitleStage.cs \
    DTXMania.Test/Stage/TitleStageLogicTests.cs
  rtk git commit -m "feat: trace Title startup resources"
  ```

### Task 8: Build the Fail-Closed Artifact Validator and Summarizer

**Files:**

- Create: `tools/hpa192/summarize-critical-path.sh`
- Create: `tools/hpa192/test-critical-path.sh`

**Consumes:** Three raw machine-readable product lines plus external launch,
observation, process-exit, identity, and hash fields.

**Produces:** Per-attempt acceptance/rejection with derived intervals, plus
five-sample scenario medians, ranges, and exclusive savings inputs.

Each artifact has exactly one `HPA192_ATTEMPT` line with these `name=value`
tokens in this literal order:

```text
scenario slot attempt
launch_start_unix_us launch_start_monotonic_us
observation_unix_us observation_monotonic_us
exit_code timed_out forced_cleanup game_api_enabled
database_charts database_songs
game_sha256 runner_sha256 summarizer_sha256
corpus_manifest_sha256 corpus_observed_sha256 system_manifest_sha256
config_sha256 config_observed_sha256
empty_manifest_sha256 empty_observed_sha256
seed_manifest_sha256 seed_observed_sha256
chart_paths_sha256 expected_chart_paths_sha256
```

- [ ] **Step 1: Write failing synthetic success and schema tests**

  `test-critical-path.sh` creates a temporary artifact tree and emits complete
  synthetic result files. The helper writes the full 81-field companion line,
  not a shortened fixture.

  Cover:

  ```text
  one valid A artifact
  exact 81-field name and order check
  missing field
  duplicate field
  reordered field
  unknown field
  duplicate companion line
  simultaneous success and failure prefixes
  unsafe outcome or error token
  signed, leading-zero, overflowing, and out-of-range number
  noncanonical boolean
  missing HPA192_STARTUP or HPA192_TIMING
  ```

  Start with:

  ```bash
  rtk bash tools/hpa192/test-critical-path.sh
  ```

  Expected: FAIL because the summarizer does not exist.

- [ ] **Step 2: Pin token parsing and numeric bounds independently**

  In `summarize-critical-path.sh`, define the full field-name array literally
  and require each token at index `n + 1` to have that key. Do not discover
  fields dynamically.

  Reuse the existing length-first `decimal_at_most` pattern so shell arithmetic
  never sees unvalidated text. Apply:

  ```text
  UTC microseconds <= 4102444800000000
  every origin/duration milliseconds <= 300000
  every counter <= 100000
  flags exactly 0 or 1
  recovery count 0..2 representable, exactly 0 accepted
  ensure-created count 1..2 representable, exactly 1 accepted
  ```

- [ ] **Step 3: Add failing temporal and partition tests**

  Mutate one synthetic artifact per case:

  ```text
  regressing required milestone
  task-return ordering inconsistent with task_returned_terminal flag
  negative post-load residual
  negative database residual
  negative enumeration residual
  negative Title activation residual
  negative summary-to-Title residual
  each exact additive equality off by one
  startup activation outside its enclosing span
  title_sound_load_count not equal to 3 plus fallback flag
  zero completed Startup draws
  title_backbuffer_published zero
  recovery count nonzero
  ensure-created count two
  ```

  Derive origins with subtraction only after validation. Re-derive the four
  directly observable companion residuals exactly: post-load, database, Title
  activation, and summary-to-Title. The producer's fifth exact residual uses
  enumeration child durations that are available to the artifact only through
  rounded `HPA192_STARTUP` fields, so validate it through the approved
  four-millisecond cross-line representation bound in Step 4.

- [ ] **Step 4: Add cross-line rounding and clock tests**

  Cover:

  ```text
  load_content_complete differs from two HPA192_TIMING segments by 2 ms
  startup_activation differs from three HPA192_TIMING segments by 3 ms
  db_init_ms differs from database parent by 2 ms
  enumeration rounded children differ from parent/residual by 5 ms
  process UTC versus monotonic differs by more than 50 ms
  external UTC versus monotonic differs by more than 50 ms
  entry before launch or Title before entry
  stdout observation before Title UTC
  ```

  Accept only:

  ```text
  abs(load_content_complete - (entry_to_config + config_to_load_content)) <= 1
  abs(startup_activation - (entry_to_config + config_to_load_content
      + load_content_to_startup)) <= 2
  abs(db_init_ms - (db_terminal - db_invoke)) <= 1
  abs(enumeration_parent - (rounded children + emitted residual)) <= 4
  ```

  `enumeration_parent` is
  `enumeration_terminal_from_entry_ms -
  enumeration_invoke_from_entry_ms`.

- [ ] **Step 5: Add identity, hash, scenario, and sentinel tests**

  Reject duplicate canonical paths, duplicate scenario/slot identities, an
  unplanned scenario, mixed fixed hashes, changed config/corpus/empty
  directory/seed bytes, incorrect chart paths or counts, a nonzero exit,
  timeout/forced cleanup, a Game API-enabled config, and access to an
  acceptance sentinel FIFO.

  Scenario expectations are exact:

  ```text
  A: parsed=100 groups=27 database charts=100 songs=27
  B: parsed=0 groups=0 database charts=0 songs=0
  C: parsed=100 groups=27 database charts=100 songs=27
  ```

- [ ] **Step 6: Implement derived exclusive intervals**

  For each accepted artifact, emit:

  ```text
  external_launch_to_entry_ms
  external_launch_to_title_backbuffer_ms
  stdout_observation_lag_ms
  entry_to_load_content_complete_ms
  load_content_complete_to_initialize_complete_ms
  initialize_complete_to_summary_request_ms
  summary_request_to_title_backbuffer_ms

  initialize_complete_to_db_invoke_ms
  db_operation_ms
  db_terminal_to_observed_ms
  db_observed_to_enumeration_invoke_ms
  enumeration_operation_ms
  enumeration_terminal_to_observed_ms
  enumeration_observed_to_summary_request_ms

  db_invoke_to_task_return_ms
  db_async_after_task_return_ms
  db_terminal_before_task_return_ms
  enumeration_invoke_to_task_return_ms
  enumeration_async_after_task_return_ms
  enumeration_terminal_before_task_return_ms
  ```

  Derive the external bridge with exact integer-millisecond arithmetic:

  ```text
  external_launch_to_entry_ms =
      (entry_unix_us - launch_start_unix_us) / 1000
  external_launch_to_title_backbuffer_ms =
      external_launch_to_entry_ms + entry_to_title_backbuffer_ms
  stdout_observation_lag_ms =
      (observation_unix_us - title_backbuffer_unix_us) / 1000
  ```

  The accepted end-to-end duration uses the in-process monotonic
  `entry_to_title_backbuffer_ms` span. Direct external and process UTC spans
  are clock-alignment checks only.

  The five intervals from external launch-to-entry through
  summary-to-backbuffer are the top-level exclusive partition. The database
  and enumeration operation/observation sequence is an exclusive refinement
  of `initialize_complete_to_summary_request_ms`.

  Dispatch-to-task-return and the conditional before/after-return values are
  diagnostic annotations: they may overlap their enclosing operation and must
  not enter the savings budget. Require exactly one of each operation's
  `async_after_task_return` and `terminal_before_task_return` values to be
  nonzero unless the timestamps are equal. Keep frame/update markers as
  annotations for the same reason.

- [ ] **Step 7: Implement five-sample summaries**

  Require exactly five accepted artifacts for each scenario and the fixed
  sequence identities. Sort numerically and use the third value as median.
  Print minimum, median, and maximum for the accepted end-to-end metric and
  every exclusive interval. Invalid attempts are listed with scenario, slot,
  attempt, rejection reason, and artifact hash but excluded from arithmetic.

- [ ] **Step 8: Run shell tests and syntax checks**

  ```bash
  rtk bash -n tools/hpa192/summarize-critical-path.sh
  rtk bash -n tools/hpa192/test-critical-path.sh
  rtk bash tools/hpa192/test-critical-path.sh
  ```

  Expected: `critical-path shell tests passed`.

- [ ] **Step 9: Review and commit**

  Verify field order is independently duplicated rather than read from product
  source, every arithmetic operand is canonical and bounded first, and no
  acceptance-sequence or measured artifact can be rewritten.

  ```bash
  rtk git diff --check
  rtk git add \
    tools/hpa192/summarize-critical-path.sh \
    tools/hpa192/test-critical-path.sh
  rtk git commit -m "test: validate HPA-192 critical-path artifacts"
  ```

### Task 9: Build the Fixed Seed and Interleaved Matrix Runner

**Files:**

- Create: `tools/hpa192/benchmark-critical-path.sh`
- Modify: `tools/hpa192/test-critical-path.sh`

**Consumes:** One fixed Mac Release output, the committed corpus manifest,
repository `System` tree, an immutable empty directory, and the summarizer.

**Produces:** One clean hashed seed and an append-only A/B/C attempt tree with
the predetermined 15-slot sequence.

- [ ] **Step 1: Add failing runner argument/configuration tests**

  Cover missing arguments, missing binary/corpus, dirty output namespace,
  corpus manifest mismatch, wrong supported-chart count, wrong `SET.def`
  count, nonempty empty-directory fixture, and a configuration whose exact
  `[Api]` value is not `EnableGameApi=False`.

  The runner interface is:

  ```text
  benchmark-critical-path.sh prepare-seed GAME_DIR CORPUS RESULT_ROOT
  benchmark-critical-path.sh matrix GAME_DIR CORPUS RESULT_ROOT
  ```

  Run the shell test. Expected: FAIL until the runner exists.

- [ ] **Step 2: Freeze and record all input identities**

  At command start:

  - canonicalize every input path;
  - acquire the existing owner-aware HPA-192 lock;
  - regenerate the corpus manifest and compare it byte for byte with
    `docs/performance/HPA-192-corpus-manifest.tsv`;
  - require 100 supported charts and 27 `SET.def` files;
  - create a canonical sorted `System` tree manifest;
  - create an empty-directory manifest whose byte length is zero; and
  - record SHA-256 values for the game DLL, runner, summarizer, corpus
    manifest, System manifest, fixed `Config.ini`, and empty manifest.

  Write the fixed config with:

  ```ini
  [System]
  SkinPath=<canonical repository System path>
  DTXPath=<scenario song path>
  [Skin]
  SystemSkinRoot=<canonical repository System path>
  [Display]
  ScreenWidth=1280
  ScreenHeight=720
  FullScreen=False
  VSyncWait=False
  [Api]
  EnableGameApi=False
  ```

  Scenario-specific `DTXPath` is the only allowed config difference and its
  hash is recorded per scenario.

- [ ] **Step 3: Implement one no-API attempt**

  For each attempt:

  1. prepare fresh app data for A/B or clone the seed directory for C;
  2. verify prelaunch directory identity and fixed config;
  3. capture external UTC and `CLOCK_MONOTONIC` microseconds adjacent to
     process launch;
  4. launch with both diagnostic flags set to `1`;
  5. poll only the local stdout file for either companion prefix;
  6. capture adjacent external observation clocks at the first prefix;
  7. abort observation immediately on the failure prefix;
  8. allow a bounded post-publication grace period for self-exit;
  9. require exit code zero and never send input or HTTP;
  10. inspect the closed SQLite database and sorted chart paths; and
  11. call `summarize-critical-path.sh --validate-attempt` to append derived
      fields and an acceptance/rejection record.

  Write exactly one `HPA192_ATTEMPT` line using the Task 8 field order before
  the three raw product lines. Numeric values are canonical unsigned decimals,
  `timed_out`, `forced_cleanup`, and `game_api_enabled` are exactly `0` or
  `1`, and every hash is lowercase 64-hex SHA-256.

  A no-line process has no in-process deadline. The runner enforces a 60-second
  external timeout, preserves stdout/stderr and metadata, then terminates only
  that validated PID for cleanup and rejects the attempt.

- [ ] **Step 4: Implement clean seed preparation**

  `prepare-seed` performs one excluded Scenario A setup launch using the same
  fixed binary/configuration and waits for clean self-exit. It verifies 100
  charts and 27 songs, absence of live `-wal`/`-shm` files, and then retains a
  canonical copy at:

  ```text
  RESULT_ROOT/seed/appdata
  RESULT_ROOT/seed/manifest.tsv
  RESULT_ROOT/seed/identity.txt
  ```

  Hash the full closed app-data manifest. `matrix` refuses to run if those
  bytes differ.

- [ ] **Step 5: Implement the append-only matrix**

  Pin the array literally:

  ```bash
  scenarios=(A B C B C A C A B A C B C B A)
  ```

  Number slots 01 through 15. Each slot begins at attempt 1 and accepts only
  its assigned scenario. On rejection, retain that directory and append
  attempt 2, then attempt 3. Never rename accepted evidence or reuse an
  attempt number.

  After the third rejection for one slot, write:

  ```text
  HPA192_CRITICAL_PATH_DECISION decision=stop reason=diagnostic_harness slot=<slot> scenario=<scenario>
  ```

  Exit nonzero without launching later slots.

- [ ] **Step 6: Add runner process-control tests**

  Use small synthetic executable scripts to cover success publication followed
  by zero exit, failure publication, no-line timeout, line followed by stuck
  process, nonzero post-publication exit, PID-scoped cleanup, no HTTP command,
  no screenshot command, three-attempt stop, same-scenario replacement, and
  the exact 15-slot order.

- [ ] **Step 7: Run all shell tests**

  ```bash
  rtk bash -n tools/hpa192/benchmark-critical-path.sh
  rtk bash -n tools/hpa192/summarize-critical-path.sh
  rtk bash -n tools/hpa192/test-critical-path.sh
  rtk bash tools/hpa192/test-critical-path.sh
  ```

  Expected: `critical-path shell tests passed`.

- [ ] **Step 8: Review and commit**

  Confirm the runner contains no `curl`, API key, JSON-RPC, screenshot, or
  stage-poll logic; validates PIDs before termination; never removes a
  non-temporary broad path; and hashes inputs before measurement.

  ```bash
  rtk git diff --check
  rtk git add \
    tools/hpa192/benchmark-critical-path.sh \
    tools/hpa192/test-critical-path.sh
  rtk git commit -m "test: add HPA-192 diagnostic matrix runner"
  ```

### Task 10: Run the Whole-Branch Review and Freeze the Release Output

**Files:**

- Verify: all production and test files touched in Tasks 1 through 9
- Verify: `DTXMania.Game/DTXMania.Game.Mac.csproj`
- Verify: `DTXMania.Game/DTXMania.Game.Windows.csproj`
- Verify: `DTXMania.Test/DTXMania.Test.Mac.csproj`
- Verify: `DTXMania.Test/DTXMania.Test.csproj`
- Verify: all three `tools/hpa192/*critical-path.sh` scripts

**Consumes:** The complete diagnostic implementation.

**Produces:** A reviewed, clean revision and one immutable Mac Release output
for seed creation and all 15 measured slots.

- [ ] **Step 1: Run the complete focused test set**

  ```bash
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --filter "FullyQualifiedName~StartupCriticalPathTraceTests|FullyQualifiedName~StartupTimingTraceTests|FullyQualifiedName~BaseGameTests|FullyQualifiedName~StageManagerTransitionTests|FullyQualifiedName~BaseStageTests|FullyQualifiedName~StartupStageLogicTests|FullyQualifiedName~TitleStageLogicTests|FullyQualifiedName~StartupSongLoadTimingObserverTests|FullyQualifiedName~SongManagerTests|FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~SongDatabaseServiceTests"
  ```

  Expected: PASS.

- [ ] **Step 2: Run timing-disabled compatibility proof**

  Add or run a focused process/unit test that clears both environment
  variables and asserts:

  ```text
  existing HPA192_STARTUP line unchanged
  existing HPA192_TIMING line unchanged
  zero HPA192_CRITICAL_PATH lines
  zero HPA192_CRITICAL_PATH_FAILURE lines
  no automatic exit request
  ```

  Expected: PASS.

- [ ] **Step 3: Run shell integrity tests**

  ```bash
  rtk bash -n tools/hpa192/benchmark-critical-path.sh
  rtk bash -n tools/hpa192/summarize-critical-path.sh
  rtk bash -n tools/hpa192/test-critical-path.sh
  rtk bash tools/hpa192/test-critical-path.sh
  ```

  Expected: all syntax checks exit 0 and the synthetic suite prints
  `critical-path shell tests passed`.

- [ ] **Step 4: Run the full macOS-safe suite**

  ```bash
  ALSOFT_DRIVERS=null rtk dotnet test \
    DTXMania.Test/DTXMania.Test.Mac.csproj \
    -c Release
  ```

  Expected: PASS.

- [ ] **Step 5: Build both game platforms**

  ```bash
  rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj \
    -c Release --no-incremental \
    > TestResults/hpa-192/critical-path-development/build-after.log
  rtk dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj \
    -c Release --no-incremental
  ```

  Expected: both builds succeed. Compare touched-file warning lines in
  `build-after.log` with `build-before.log`; no new warning may originate from
  a touched production file. Shared trace tests must also pass in the Windows
  CI job.

- [ ] **Step 6: Run a whole-branch specification review**

  Review the complete diff against every design section, with explicit checks
  for:

  ```text
  exact 81-field order
  byte-compatible existing lines
  no IStageGame expansion
  no concrete BaseGame cast
  terminal dominance
  observer containment
  post-EndEnumeration terminal
  post-blit endpoint
  screenshot rejection
  next-update exit
  SQLite exclusivity
  Title sound semantics
  runner retry and hash rules
  diagnostic-only scope
  ```

  Fix every valid issue with a failing regression test first. Rerun the
  affected focused tests and the shell suite.

- [ ] **Step 7: Run a whole-branch code-quality review**

  Inspect for lock-held formatting/callbacks, clock reads after lock
  acquisition, unbounded collections, unchecked arithmetic, observer-induced
  exceptions, changed catches, changed task scheduling, duplicate
  instrumentation, broad cleanup commands, and shell arithmetic before input
  validation.

  Fix every valid issue and rerun its proof.

- [ ] **Step 8: Re-run final verification after review fixes**

  ```bash
  ALSOFT_DRIVERS=null rtk dotnet test \
    DTXMania.Test/DTXMania.Test.Mac.csproj \
    -c Release
  rtk bash tools/hpa192/test-critical-path.sh
  rtk git diff --check
  rtk git status --short
  ```

  Expected: tests pass, shell tests pass, whitespace check passes, and status
  is clean. Commit review fixes with a scoped Conventional Commit before
  continuing.

- [ ] **Step 9: Build and hash the fixed measurement output once**

  Require a clean measurement namespace:

  ```bash
  fixed_root="TestResults/hpa-192/critical-path-final"
  fixed_build="$fixed_root/build"
  test ! -e "$fixed_root"
  rtk mkdir -p "$fixed_build"
  rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj \
    -c Release -o "$fixed_build"
  rtk shasum -a 256 "$fixed_build/DTXMania.Game.Mac.dll"
  rtk git rev-parse HEAD
  ```

  Expected: one successful build. Record the exact commit and DLL hash in
  `$fixed_root/fixed-inputs.txt`. Do not rebuild between seed preparation and
  the last matrix slot.

### Task 11: Prepare the Seed and Collect the Fifteen Valid Samples

**Files:**

- Generate locally:
  `TestResults/hpa-192/critical-path-final/seed/**`
- Generate locally:
  `TestResults/hpa-192/critical-path-final/slots/**`
- Generate locally:
  `TestResults/hpa-192/critical-path-final/accepted-artifacts.txt`
- Generate locally:
  `TestResults/hpa-192/critical-path-final/summary.txt`
- Verify: `docs/performance/HPA-192-corpus-manifest.tsv`

**Consumes:** The fixed build from Task 10 and the machine-local frozen corpus.

**Produces:** Five accepted samples per scenario, all rejected attempts, one
hashed seed, and one generated summary. These ignored raw artifacts remain
local; their hashes and machine-readable evidence are copied into the report.

- [ ] **Step 1: Verify the pinned machine and corpus inputs**

  Use:

  ```bash
  repo="/Users/chanwaichan/workspace/DTXmaniaCX/.worktrees/hpa-192-batched-import"
  result_root="$repo/TestResults/hpa-192/critical-path-final"
  game_dir="$result_root/build"
  corpus="/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles"

  test -f "$game_dir/DTXMania.Game.Mac.dll"
  test -d "$corpus"
  test -z "$(rtk git status --porcelain)"
  rtk system_profiler SPHardwareDataType
  rtk sw_vers
  rtk dotnet --info
  ```

  Record the exact hardware, OS, architecture, SDK, and net8 runtime in an
  environment artifact. Do not proceed if the worktree is dirty or the fixed
  DLL hash differs from Task 10.

- [ ] **Step 2: Prepare and validate the excluded setup seed**

  ```bash
  rtk bash tools/hpa192/benchmark-critical-path.sh \
    prepare-seed "$game_dir" "$corpus" "$result_root"
  ```

  Expected: exit 0, 100 charts, 27 songs, no live WAL/SHM files, and seed
  identity/hash artifacts.

- [ ] **Step 3: Run the predetermined matrix**

  ```bash
  rtk bash tools/hpa192/benchmark-critical-path.sh \
    matrix "$game_dir" "$corpus" "$result_root"
  ```

  Expected: exit 0 after exactly 15 accepted slots in:

  ```text
  A, B, C, B, C, A, C, A, B, A, C, B, C, B, A
  ```

  Invalid attempts, if any, remain in their slot directories. If a slot
  reaches its third rejection, stop this plan immediately and report
  `decision=stop reason=diagnostic_harness`.

- [ ] **Step 4: Generate and retain the scenario summary**

  ```bash
  rtk bash tools/hpa192/summarize-critical-path.sh \
    --summarize "$result_root" |
    rtk tee "$result_root/summary.txt"
  ```

  Expected: exactly five valid A, five valid B, and five valid C artifacts;
  every partition and clock check passes; every valid attempt reports one
  completed Startup draw and one published Title backbuffer.

- [ ] **Step 5: Verify artifact immutability after summarization**

  Generate a canonical manifest of every seed, attempt, accepted-list, and
  summary artifact with relative path, byte length, and SHA-256 at
  `artifact-manifest.tsv`; exclude that manifest from its own inventory. Run
  the summarizer a second time to a separate temporary output and compare it
  byte for byte with `summary.txt`. Recompute the inventory to a temporary
  file and require it to match `artifact-manifest.tsv`, then record the
  manifest's own SHA-256 separately.

- [ ] **Step 6: Stop at evidence if any integrity check fails**

  Do not discard, replace across scenarios, renumber, edit, or average an
  invalid artifact. Preserve the rejection ledger and report the exact failed
  slot or invariant.

### Task 12: Publish the Diagnostic Report and Stop Before Product Design

**Files:**

- Modify: `docs/performance/HPA-192-startup-benchmark.md`
- Verify locally:
  `TestResults/hpa-192/critical-path-final/**`

**Consumes:** The immutable Task 11 evidence and final verification results.

**Produces:** A committed, reviewable critical-path report and either a
recommendation to open a separate product design or an explicit stop.

- [ ] **Step 1: Add the diagnostic scope and endpoint**

  Append a dated `HPA-192 startup critical-path diagnostic` section that
  states:

  - this is macOS-only diagnostic evidence, not final acceptance;
  - the Game API was disabled and never polled;
  - the endpoint is the first completed render-target-to-backbuffer copy;
  - `CompleteBaseDraw`, framework `EndDraw`/`Present`, buffer swap, vsync,
    compositor, and physical display are excluded; and
  - retained preflight timings use an earlier, API-enabled endpoint and are not
    arithmetically comparable.

- [ ] **Step 2: Record fixed revisions, environment, and hashes**

  Copy exact values for:

  ```text
  source commit
  game DLL
  runner
  summarizer
  shell test
  corpus manifest
  System manifest
  empty-directory manifest
  seed app-data manifest
  fixed configs
  complete artifact manifest
  ```

  Include the machine, OS, architecture, SDK, and net8 runtime from Task 11.

- [ ] **Step 3: Record the full attempt ledger and raw evidence**

  For all 15 slots, list scenario, slot, accepted attempt, artifact path, and
  SHA-256. List every rejected attempt with its exact rejection reason and
  hash.

  Preserve the complete raw `HPA192_STARTUP`, `HPA192_TIMING`, and
  `HPA192_CRITICAL_PATH` lines for each accepted artifact in a collapsed
  Markdown details block. Do not round or hand-edit those lines.

- [ ] **Step 4: Record scenario medians and ranges**

  Copy the generated minimum/median/maximum tables for A, B, and C, including
  the accepted external launch-to-Title-backbuffer metric, stdout observation
  lag, the five top-level exclusive timeline sections, dispatch/operation/
  observation splits, database partition, enumeration partition, transition
  partition, and Title activation partition.

  Label process entry through `load_content_complete` as the
  `external-bridge head`, including pre-`LoadContent` graphics settings. Do
  not label that interval SQLite or song loading.

- [ ] **Step 5: Build the ranked non-overlapping savings budget**

  Start from Scenario A's new external end-to-end median. Use only exclusive
  intervals and show:

  ```text
  measured interval
  Scenario A median
  conservative removable amount
  preserved remainder
  cumulative projected median
  contract or risk note
  ```

  Never count a nested database, enumeration, transition, frame, or Title
  resource interval again after its enclosing exclusive saving has been
  counted. Treat first-update/draw markers as scheduling annotations when they
  overlap an asynchronous operation.

- [ ] **Step 6: Apply the design decision rule without designing the product**

  If a safe exclusive combination projects the Scenario A median to 2,000 ms
  or less while preserving one Startup draw, a visible transition, and all
  first-wave contracts, end with:

  ```text
  decision=recommend_separate_product_design
  ```

  Name only the measured candidate intervals and their conservative budget.
  Do not specify implementation mechanics.

  Otherwise end with:

  ```text
  decision=stop reason=broader_runtime_or_packaging_design_required
  ```

- [ ] **Step 7: Record verification evidence**

  Include focused test results, the full macOS-safe test result, Mac and
  Windows build results, shell suite result, warning comparison, whole-branch
  review outcomes, and `git diff --check`.

- [ ] **Step 8: Review the report against raw artifacts**

  Independently re-open every value referenced in the report. Verify sample
  counts, order, hashes, medians, ranges, invalid-attempt exclusions, exclusive
  arithmetic, endpoint language, and decision threshold.

- [ ] **Step 9: Commit the report only**

  ```bash
  rtk git diff --check
  rtk git add docs/performance/HPA-192-startup-benchmark.md
  rtk git commit -m "docs: record HPA-192 critical-path diagnostic"
  rtk git status --short
  ```

  Expected: clean worktree. Stop here and request review of the evidence and
  recommendation. Do not start an optimization design or implementation in
  this plan.

## Final Handoff Checklist

- [ ] Existing `HPA192_STARTUP` output is byte-compatible.
- [ ] Existing `HPA192_TIMING` output and `TitleCompleted` meaning are
  byte-compatible.
- [ ] Disabled launches emit no companion prefix and never auto-exit.
- [ ] Enabled success emits exactly one 81-field line in pinned order.
- [ ] Failure/cancellation emits only the fixed failure schema.
- [ ] The first terminal outcome dominates late observations.
- [ ] No recorder lock is held during formatting, writing, callbacks, or
  awaits.
- [ ] `IStageGame` has no new public member and no stage casts to `BaseGame`.
- [ ] Database observer exceptions cannot affect production behavior.
- [ ] Enumeration terminal occurs after `EndEnumeration`.
- [ ] SQLite and Title resource spans are exclusive and close on throws.
- [ ] Success occurs only after a valid first Title backbuffer blit.
- [ ] A pending first-frame screenshot invalidates the attempt.
- [ ] Exit occurs through the normal path on the next update after publication.
- [ ] Runner code performs no HTTP, Game API, input, or screenshot operation.
- [ ] Fixed corpus, System, config, empty directory, seed, binary, tools, and
  artifacts are hashed.
- [ ] Each scenario has exactly five valid samples in the fixed sequence.
- [ ] Every invalid attempt is retained and excluded from arithmetic.
- [ ] Scenario medians/ranges and exclusive partitions reconcile.
- [ ] The savings budget contains no overlap or double subtraction.
- [ ] The report states the endpoint exclusions and platform scope.
- [ ] The plan ends at the reviewed diagnostic recommendation.
