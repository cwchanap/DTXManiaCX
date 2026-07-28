# HPA-192 Second-Wave Startup Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce the frozen 100-chart fresh-start median from 7,402 ms to 2,221 ms or less while preserving the first-wave import, lifecycle, cancellation, migration, and user-data contracts.

**Architecture:** A launch-owned coordinator starts the existing song operation immediately after configuration is loaded, while MonoGame continues graphics and content initialization. `StartupStage` becomes a milestone consumer that drains completed display phases without owning or restarting the work. A service-proven fresh database may use a guarded, single-transaction prepared SQLite writer built from the same pure import plan as an EF fresh path; every existing database remains on the first-wave EF reconciliation path.

**Tech Stack:** .NET 8, C# 12, MonoGame 3.8, Entity Framework Core SQLite, Microsoft.Data.Sqlite, xUnit, Moq, Bash, SQLite CLI, GitHub Actions.

## Global Constraints

- Treat the committed design at
  `docs/superpowers/specs/2026-07-28-hpa-192-second-wave-startup-optimization-design.md`
  as the normative contract. If source reality forces a contract change, stop,
  amend and review the design, then update this plan before continuing.
- Keep the first-wave product commit
  `c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b` as the behavioral reference and
  the original baseline commit
  `5ea3f95d208ba7b15019429f63d7edd0bbf7009d` as the performance reference.
- Before Task 0 instrumentation, require no product-code delta from the
  first-wave commit:

  ```bash
  rtk git diff --quiet \
    c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b -- \
    DTXMania.Game DTXMania.Test DTXMania.E2E MCP
  ```

- Task 0 is a hard go/no-go gate. Do not begin Tasks 1 through 8 if the median
  fixed-cost lower bound is 2,221 ms or greater.
- Passing Task 0 authorizes implementation; it is not performance acceptance.
- Keep production force enumeration enabled. Move its test seam to
  `BaseGame.CreateStartupSongLoadRequest`; do not change the production value.
- Capture `SongManager.Instance` on the game thread before starting the worker.
  The worker must never resolve the singleton.
- Keep exactly one startup song operation per process launch. Activation,
  deactivation, external API calls, and repeated `Start` calls must not create
  another operation.
- Do not parallelize chart parsing, add a second SQLite writer, or change
  parser/grouping/path-identity semantics.
- Preserve first-wave atomic import, complete-batch, canonical path,
  legacy-alias, fallback, cancellation, publish-after-commit, migration, and
  user-data preservation behavior.
- Never hold a coordinator lock while awaiting database, enumeration, import,
  cache hierarchy, or publication work.
- Do not execute UI callbacks on the worker thread. Publish immutable progress
  snapshots and let `StartupStage` read them during `Update`.
- Preserve at least one rendered Startup frame, one `HPA192_STARTUP` summary,
  and one internal Startup-to-Title request.
- Keep `total_ms` as Startup activation-to-summary. New coordinator timings are
  intentionally non-additive.
- Gate every valid external stage target until the coordinator, Startup frame,
  Startup summary, and normal Title transition are all complete.
- Keep `GetGameState`, health, input, and screenshots available during startup
  when they do not read song hierarchy, open the songs database, or leave
  Startup.
- Use a five-second production shutdown bound and an injectable short bound in
  tests. Never dispose coordinator-owned database or logging dependencies
  underneath a timed-out worker.
- Direct SQL is eligible only for a schema created and proven fresh by the same
  `SongDatabaseService` initialization, a complete batch, empty import tables,
  verified foreign keys, verified AUTOINCREMENT DDL, and unused one-shot
  eligibility.
- Any direct-writer guard failure before mutation selects EF. Any failure after
  mutation starts rolls back and propagates; it must not retry through EF.
- Existing or merely empty databases always use the existing EF reconciliation
  path and report `persistence_path=ef`.
- The accepted wave-two benchmark must report
  `persistence_path=fresh_sqlite`, 100 charts, and 27 songs in every wave-two
  sample.
- Keep third-party benchmark assets local. Commit only source, tests, scripts,
  the existing manifest, and textual evidence.
- Prefix repository commands with `rtk`.
- Use Conventional Commit subjects under 72 characters.
- Run specification and code-quality review after every product task. Run a
  whole-branch review/fix loop before the final benchmark.

## Acceptance and Stop Conditions

Implementation is complete only when all of the following are true:

- Task 0 reports a median fixed-cost lower bound below 2,221 ms.
- Focused coordinator, Startup lifecycle, API, MCP, initialization, writer,
  schema, migration, and preservation tests pass.
- The full macOS-safe unit suite passes with `ALSOFT_DRIVERS=null`.
- Both game platform builds and the MCP project/tests pass in CI.
- E2E support tests pass and both drum-mapping flows wait for Title plus
  `ExternalStageChangesReady`.
- The fixed final Release output completes the predetermined balanced six-run
  sequence.
- The historical 7,402 ms baseline remains documented, the balanced rerun of
  the original baseline commit has complete evidence, and the wave-two median
  is 2,221 ms or less.
- The calculated improvement is at least 70 percent.
- Every accepted wave-two sample has the exact frozen chart-path set, 100
  charts, 27 songs, one startup summary, and
  `persistence_path=fresh_sqlite`.

Stop and report without broadening the implementation when any of these occurs:

- The Task 0 median fixed floor is at least 2,221 ms.
- Current source cannot satisfy a committed lifecycle or data contract without
  changing the design.
- Structural fresh-versus-migrated schema comparison fails for a real semantic
  difference.
- The final median misses either performance gate.
- Correctness, lifecycle, migration, preservation, or benchmark integrity
  evidence fails after focused diagnosis.

---

## Planned File Structure

### Timing and benchmark preflight

- Create `DTXMania.Game/Lib/Stage/StartupTimingTrace.cs`.
- Create `DTXMania.Test/Stage/StartupTimingTraceTests.cs`.
- Create `tools/hpa192/summarize-timing-preflight.sh`.
- Create `tools/hpa192/test-timing-preflight.sh`.
- Create `tools/hpa192/run-balanced-benchmark.sh` in Task 8.
- Create `tools/hpa192/test-balanced-benchmark.sh` in Task 8.
- Modify `DTXMania.Game/Program.cs`.
- Modify `DTXMania.Game/Game1.cs`.
- Modify `DTXMania.Game/Lib/Stage/IStageGame.cs`.
- Modify `DTXMania.Game/Lib/Stage/StartupStage.cs`.
- Modify `tools/hpa192/benchmark-startup.sh`.
- Modify `docs/performance/HPA-192-startup-benchmark.md`.

### Coordinator and launch lifecycle

- Create `DTXMania.Game/Lib/Song/StartupSongLoadContracts.cs`.
- Create `DTXMania.Game/Lib/Song/StartupSongLoadCoordinator.cs`.
- Create `DTXMania.Test/Song/StartupSongLoadCoordinatorTests.cs`.
- Create `DTXMania.Test/Stage/StartupStageCoordinatorTests.cs`.
- Create `DTXMania.Test/BaseGameStartupSongLoadTests.cs`.
- Modify `DTXMania.Game/Lib/Song/SongManager.cs`.
- Modify `DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs`.
- Modify `DTXMania.Game/Lib/Stage/StartupStage.cs`.
- Modify `DTXMania.Game/Lib/Stage/IStageGame.cs`.
- Modify `DTXMania.Game/Game1.cs`.
- Modify `DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs`.
- Modify `DTXMania.Test/Stage/StartupStageLogicTests.cs`.
- Modify `DTXMania.Test/Stage/StartupStageAdditionalCoverageTests.cs`.
- Modify `DTXMania.Test/BaseGameTests.cs`.

### External readiness and protocol migration

- Create `MCP.Test/MCP.Test.csproj`.
- Create `MCP.Test/Server/JsonRpcClientTests.cs`.
- Create `MCP.Test/Server/GameInteractionStageChangeTests.cs`.
- Modify `DTXMania.Game/Lib/GameApi.cs`.
- Modify `DTXMania.Game/Lib/GameApiImplementation.cs`.
- Modify `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`.
- Modify `DTXMania.Game/Lib/JsonRpc/JsonRpcMessage.cs`.
- Modify `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs`.
- Modify `DTXMania.Game/Game1.cs`.
- Modify `DTXMania.Test/GameApi/GameApiImplementationTests.cs`.
- Modify `DTXMania.Test/GameApi/GameTelemetrySnapshotTests.cs`.
- Modify `DTXMania.Test/JsonRpc/JsonRpcServerIntegrationTests.cs`.
- Modify `DTXMania.Test/JsonRpc/JsonRpcServerValidationTests.cs`.
- Modify `DTXMania.Test/JsonRpc/JsonRpcServerTests.cs`.
- Modify `DTXMania.Test/BaseGameTests.cs`.
- Modify `DTXMania.Test/BaseGameStartupSongLoadTests.cs`.
- Modify `MCP/Server/JsonRpcClient.cs`.
- Modify `MCP/Server/GameInteractionService.cs`.
- Modify `MCP/Server/GameInteractionMcpToolHandlers.cs`.
- Modify `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`.
- Modify `DTXMania.E2E/JsonRpc/JsonRpcGameClientTests.cs`.
- Modify `DTXMania.E2E/Telemetry/E2EGameState.cs`.
- Modify `DTXMania.E2E/Telemetry/E2EGameStateTests.cs`.
- Modify `DTXMania.E2E/DrumMappingStageSmokeTests.cs`.
- Modify `DTXMania.sln`.
- Modify `.github/workflows/build-and-test.yml`.

### Fresh initialization and import

- Create `DTXMania.Game/Lib/Song/Entities/FreshImportPlan.cs`.
- Create `DTXMania.Game/Lib/Song/Entities/FreshSqliteSongImporter.cs`.
- Create `DTXMania.Test/Song/SongDatabaseServiceFreshInitializationTests.cs`.
- Create `DTXMania.Test/Song/FreshImportPlanTests.cs`.
- Create `DTXMania.Test/Song/FreshSqliteSongImporterTests.cs`.
- Create `DTXMania.Test/Song/SqliteSchemaSnapshot.cs`.
- Create `DTXMania.Test/Song/SongDatabaseSchemaContractTests.cs`.
- Modify `DTXMania.Game/Lib/Song/SongImportModels.cs`.
- Modify `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`.
- Modify first-wave import, migration, preservation, and model tests where a
  new result field or test seam requires compilation changes.

---

## Execution and Review Protocol

For every product task after Task 0:

1. Dispatch one implementation subagent with only that task and the committed
   design/plan paths.
2. Require test-first evidence: focused RED, minimal implementation, focused
   GREEN.
3. Run the task's integration and regression commands in the primary agent.
4. Dispatch a fresh specification reviewer; fix every verified contract gap.
5. Dispatch a fresh code-quality reviewer; fix every verified correctness,
   maintainability, or test gap.
6. Re-run the focused and integration commands after review fixes.
7. Commit only that task.
8. Update the checklist before beginning the next task.

Do not run product implementation tasks in parallel. Database and lifecycle
tasks share source and must land sequentially.

---

### Task 0: Measure the Fixed Cost and Enforce the Go/No-Go Gate

**Files:**

- Create: `DTXMania.Game/Lib/Stage/StartupTimingTrace.cs`
- Create: `DTXMania.Test/Stage/StartupTimingTraceTests.cs`
- Create: `tools/hpa192/summarize-timing-preflight.sh`
- Create: `tools/hpa192/test-timing-preflight.sh`
- Modify: `DTXMania.Game/Program.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupStage.cs`
- Modify: `tools/hpa192/benchmark-startup.sh`
- Modify: `docs/performance/HPA-192-startup-benchmark.md`

**Produces:**

- One process-monotonic timeline with exactly seven milestones.
- Exactly one Release-visible `HPA192_TIMING` line at completed Title.
- Three fresh diagnostic runs, raw interval evidence, medians, overlap window,
  and fixed-cost lower bound.
- A recorded hard decision to stop or continue.

- [ ] **Step 0.1: Verify the product starting point**

Run:

```bash
rtk git rev-parse HEAD
rtk git merge-base HEAD c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b
rtk git diff --quiet \
  c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b -- \
  DTXMania.Game DTXMania.Test DTXMania.E2E MCP
rtk shasum -a 256 docs/performance/HPA-192-corpus-manifest.tsv
rtk proxy wc -l docs/performance/HPA-192-corpus-manifest.tsv
```

Expected:

- Merge base is the first-wave product commit.
- The product diff command exits zero.
- The manifest hash is
  `0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b`.
- The manifest contains 592 rows.

- [ ] **Step 0.2: Write failing monotonic timeline tests**

Test the following behaviors in `StartupTimingTraceTests`:

```csharp
[Fact]
public void Format_WhenAllMilestonesRecorded_ShouldEmitExactIntervalsOnce()
{
    var clock = new FakeMonotonicClock(
        0, 100, 400, 500, 520, 900, 1900);
    var wallClock = new FakeUtcMicrosecondClock(
        processEntry: 10_000_000,
        titleCompleted: 11_900_000);
    var trace = StartupTimingTrace.Start(clock, wallClock);

    trace.MarkConfigLoaded();
    trace.MarkLoadContentComplete();
    trace.MarkStartupActivated();
    trace.MarkStartupFirstDraw();
    trace.MarkSummaryAndTitleRequested();
    trace.MarkTitleCompleted();

    Assert.Equal(
        "HPA192_TIMING entry_to_config_ms=100 " +
        "config_to_load_content_ms=300 " +
        "load_content_to_startup_ms=100 " +
        "startup_to_first_draw_ms=20 " +
        "startup_to_summary_ms=400 " +
        "summary_to_title_ms=1000 " +
        "entry_to_title_ms=1900 " +
        "entry_unix_us=10000000 " +
        "title_unix_us=11900000",
        trace.TryFormatCompletedLine());
    Assert.Null(trace.TryFormatCompletedLine());
}

[Fact]
public void Markers_WhenRepeatedOrOutOfOrder_ShouldNotPublishInvalidTimeline()
{
    // Repeated markers are idempotent; an impossible ordering never emits.
}
```

Also cover:

- process entry is captured before `Game1` construction;
- every interval uses one monotonic origin;
- the two UTC microsecond anchors are captured adjacent to process-entry and
  Title-complete monotonic markers solely to align the process timeline with
  the runner timeline;
- duplicate lifecycle reports do not emit another line;
- incomplete timelines do not emit;
- values use invariant whole milliseconds.

- [ ] **Step 0.3: Run the timing test and verify RED**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~StartupTimingTraceTests"
```

Expected: compilation fails because the timing types do not exist.

- [ ] **Step 0.4: Implement the timing model without product reordering**

Use a testable monotonic clock and a single-emission guard. The production
shape should remain narrow:

```csharp
internal enum StartupTimingMilestone
{
    ProcessEntry,
    ConfigLoaded,
    LoadContentComplete,
    StartupActivated,
    StartupFirstDraw,
    SummaryAndTitleRequested,
    TitleCompleted
}

internal sealed class StartupTimingTrace
{
    public static StartupTimingTrace StartProcess(); // captures UTC + monotonic entry
    public void Mark(StartupTimingMilestone milestone);
    public string? TryFormatCompletedLine();
}
```

Do not log on each marker. Only Title completion may claim and print the one
formatted line. Print it with `Console.Out.WriteLine`, not `ILogger` or
`Debug`, so the Release runner can match the anchored raw line.

- [ ] **Step 0.5: Wire all seven markers**

Wire markers at these exact semantic boundaries:

1. `Program.cs`: call `StartProcess()` before constructing `Game1`, then pass
   the trace through an internal `Game1(StartupTimingTrace)` constructor.
2. `BaseGame.Initialize`: immediately after `ConfigManager.LoadConfig`.
3. `BaseGame.LoadContent`: after resource/skin setup and immediately before
   `StageManager.ChangeStage(StageType.Startup)`. This is the diagnostic
   `LoadContentComplete` boundary; do not move API or stage construction.
4. `StartupStage.OnActivate`: report activation after activation-local state is
   initialized.
5. `StartupStage.OnDraw`: report only after a Startup frame was actually drawn.
6. `StartupStage.WriteSummaryOnce`/transition gate: report after the summary is
   emitted and immediately before the internal Title request.
7. `BaseGame.Update`: after `StageManager.Update`, when the published stage is
   Title and `IsTransitioning` is false, mark Title completed and print the one
   timing line before draining external actions.

Keep the public parameterless `BaseGame`/`Game1` constructors for existing
callers and tests. Add internal trace-taking constructors in the same assembly;
the parameterless path uses a disabled trace.

Add these default no-op methods to public `IStageGame` so existing fakes do not
all require changes and no internal timing type leaks into a public signature:

```csharp
void ReportStartupActivated() { }
void ReportStartupFrameRendered() { }
void ReportStartupSummaryAndTitleRequested() { }
```

`StartupStage` invokes those semantic methods. `BaseGame` maps them to the
internal timing milestones and, in Task 2, to readiness-barrier evidence.

- [ ] **Step 0.6: Verify focused timing tests are GREEN**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~StartupTimingTraceTests|FullyQualifiedName~BaseGameTests|FullyQualifiedName~StartupStageLogicTests"
```

Expected: all selected tests pass.

- [ ] **Step 0.7: Write failing shell arithmetic/validation tests**

`tools/hpa192/test-timing-preflight.sh` must generate synthetic run artifacts
and assert:

- exactly one `HPA192_TIMING` line is required when timing is enabled;
- missing, duplicate, nonnumeric, negative, or internally inconsistent fields
  fail closed;
- all three diagnostic runs are preserved;
- medians use the middle value of three, not an average;
- accepted benchmark sequence files are never read or written;
- a deliberately large post-Title HTTP polling delay changes
  `title_poll_lag_ms` and `wall_ms` but does not change the fixed floor;
- the calculations are:

  ```text
  entry_to_startup =
      entry_to_config
    + config_to_load_content
    + load_content_to_startup

  external_launch_to_entry =
      (entry_unix_us - launch_start_unix_us) / 1000

  external_launch_to_startup =
      external_launch_to_entry
    + entry_to_startup

  title_poll_lag =
      (launch_end_unix_us - title_unix_us) / 1000

  config_to_startup =
      config_to_load_content
    + load_content_to_startup

  fixed_floor =
      external_launch_to_startup
    + startup_to_first_draw
    + summary_to_title
  ```

The runner's `wall_ms` ends after HTTP polling observes Title and therefore
must not be used to derive the fixed floor. Validate that both cross-process
wall-clock deltas are nonnegative and within a sane diagnostic bound; fail the
preflight if the system clock changes. All in-process phase intervals remain
monotonic.

- a median fixed floor of 2,220 permits the next task;
- a median fixed floor of 2,221 or more stops the wave.

Run:

```bash
rtk bash tools/hpa192/test-timing-preflight.sh
```

Expected: failure because the summarizer is absent.

- [ ] **Step 0.8: Implement the preflight summarizer and runner mode**

Add opt-in runner controls without weakening default validation:

```text
HPA192_REQUIRE_TIMING=1
HPA192_REQUIRE_EXTERNAL_READY=0
HPA192_EXPECT_PERSISTENCE_PATH=
HPA192_EXPECT_SONG_COUNT=
```

For Task 0:

- require exactly one `HPA192_TIMING` line;
- write it into the per-run result artifact beside `wall_ms`;
- preserve external launch/Title-observation UTC microseconds plus the process
  entry/Title UTC anchors;
- keep fresh app-data, unique port/token, exact chart paths, and one startup
  summary;
- do not require readiness because the first-wave product has no readiness
  field;
- do not count these runs as acceptance.

`summarize-timing-preflight.sh` consumes exactly three named result files,
prints each raw interval and each derived value, then prints medians and one
machine-readable decision:

```text
HPA192_PREFLIGHT median_fixed_floor_ms=... target_ms=2221 decision=continue
```

or:

```text
HPA192_PREFLIGHT median_fixed_floor_ms=... target_ms=2221 decision=stop
```

- [ ] **Step 0.9: Verify shell tests and syntax**

```bash
rtk bash -n tools/hpa192/benchmark-startup.sh
rtk bash -n tools/hpa192/summarize-timing-preflight.sh
rtk bash -n tools/hpa192/test-timing-preflight.sh
rtk bash tools/hpa192/test-timing-preflight.sh
```

Expected: all commands pass.

- [ ] **Step 0.10: Commit instrumentation before measuring**

```bash
rtk git add \
  DTXMania.Game/Program.cs \
  DTXMania.Game/Game1.cs \
  DTXMania.Game/Lib/Stage/IStageGame.cs \
  DTXMania.Game/Lib/Stage/StartupStage.cs \
  DTXMania.Game/Lib/Stage/StartupTimingTrace.cs \
  DTXMania.Test/Stage/StartupTimingTraceTests.cs \
  tools/hpa192/benchmark-startup.sh \
  tools/hpa192/summarize-timing-preflight.sh \
  tools/hpa192/test-timing-preflight.sh
rtk git commit -m "perf: instrument HPA-192 startup timing"
```

The fixed Release must be built from this committed state, not from an
uncommitted source tree.

- [ ] **Step 0.11: Build one fixed first-wave-plus-instrumentation Release**

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj \
  --configuration Release \
  --output TestResults/hpa-192/builds/timing-preflight
rtk shasum -a 256 \
  TestResults/hpa-192/builds/timing-preflight/DTXMania.Game.Mac.dll
rtk shasum -a 256 tools/hpa192/benchmark-startup.sh
```

Record the first-wave product commit, committed instrumentation commit, DLL
hash, runner hash, SDK/runtime, machine, OS, and corpus hash before running
samples.

- [ ] **Step 0.12: Run exactly three fresh diagnostics**

Using the frozen local corpus and fixed Release output:

```bash
HPA192_REQUIRE_TIMING=1 \
  rtk bash tools/hpa192/benchmark-startup.sh \
  TestResults/hpa-192/builds/timing-preflight \
  "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" \
  timing-preflight 1

HPA192_REQUIRE_TIMING=1 \
  rtk bash tools/hpa192/benchmark-startup.sh \
  TestResults/hpa-192/builds/timing-preflight \
  "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" \
  timing-preflight 2

HPA192_REQUIRE_TIMING=1 \
  rtk bash tools/hpa192/benchmark-startup.sh \
  TestResults/hpa-192/builds/timing-preflight \
  "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" \
  timing-preflight 3
```

Then:

```bash
rtk bash tools/hpa192/summarize-timing-preflight.sh \
  TestResults/hpa-192/timing-preflight/run-1.result.txt \
  TestResults/hpa-192/timing-preflight/run-2.result.txt \
  TestResults/hpa-192/timing-preflight/run-3.result.txt
```

- [ ] **Step 0.13: Record the hard decision**

Append a dated preflight section to
`docs/performance/HPA-192-startup-benchmark.md` containing:

- all three raw external and internal intervals;
- all derived intervals per run;
- medians;
- the exact fixed-floor formula;
- the overlapable `config_to_startup` median;
- fixed output and runner hashes;
- explicit statement that diagnostics are excluded from acceptance;
- `continue` or `stop`.

If the median fixed floor is at least 2,221 ms:

- commit the evidence;
- stop implementation;
- report that a new measured design is required.

If it is below 2,221 ms:

- commit the evidence;
- continue to Task 1.

- [ ] **Step 0.14: Verify and commit the preflight report**

```bash
rtk git diff --check
rtk rg -n \
  "median_fixed_floor_ms|config_to_startup|decision=|diagnostic" \
  docs/performance/HPA-192-startup-benchmark.md
rtk git add docs/performance/HPA-192-startup-benchmark.md
rtk git commit -m "docs: record HPA-192 timing preflight"
```

---

### Task 1: Add the One-Shot Startup Song Load Coordinator

**Files:**

- Create: `DTXMania.Game/Lib/Song/StartupSongLoadContracts.cs`
- Create: `DTXMania.Game/Lib/Song/StartupSongLoadCoordinator.cs`
- Create: `DTXMania.Test/Song/StartupSongLoadCoordinatorTests.cs`
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs`
- Modify: `DTXMania.Game/Lib/Song/SongImportModels.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs`
- Modify: `DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs`
- Modify: `DTXMania.Test/Stage/StartupStageLogicTests.cs`

**Produces:**

- Immutable launch request, progress snapshot, and terminal result contracts.
- A one-shot coordinator that owns database initialization, path selection,
  enumeration/cache loading, fallback, publication, and final timings.
- Strong cancellation checkpoints around traversal and parsing.
- No BaseGame or StartupStage integration yet.

- [ ] **Step 1.1: Define the public-to-stage operation boundary**

Use one read-only operation interface so `StartupStage` cannot start, cancel,
or dispose the coordinator:

```csharp
public interface IStartupSongLoadOperation
{
    Task DatabaseReady { get; }
    Task<StartupSongLoadResult> Completion { get; }
    StartupSongLoadProgressSnapshot LatestProgress { get; }
    TimeSpan Elapsed { get; }
}
```

The internal coordinator implements this interface and additionally owns
`Start()` and disposal. Define:

```csharp
public sealed record StartupSongLoadRequest
{
    public IReadOnlyList<string> SongRoots { get; }
    public string DatabasePath { get; }
    public bool ForceEnumeration { get; }

    public StartupSongLoadRequest(
        IEnumerable<string> songRoots,
        string databasePath,
        bool forceEnumeration)
    {
        SongRoots = Array.AsReadOnly(songRoots.ToArray());
        DatabasePath = databasePath;
        ForceEnumeration = forceEnumeration;
    }
}

public enum StartupSongLoadStep
{
    NotStarted,
    DatabaseInitialization,
    PathSelection,
    CacheLoading,
    DiscoveryAndParsing,
    Persistence,
    HierarchyFinalization,
    Complete
}

public enum SongPersistencePath
{
    Ef,
    FreshSqlite
}

public sealed record StartupSongLoadProgressSnapshot(
    StartupSongLoadStep Step,
    bool DatabaseReady,
    bool IsTerminal,
    StartupSongLoadPath Path,
    StartupSongLoadOutcome? Outcome,
    string? Error,
    string? Operation,
    string? File,
    string? Directory,
    int ProcessedCount,
    int DiscoveredCharts,
    int ParsedCharts,
    int LogicalGroups);
```

`StartupSongLoadResult` must retain all existing summary durations/counts plus
operation duration, selected persistence path, hierarchy-ready state, and
sanitized error. Move `StartupSongLoadPath` and `StartupSongLoadOutcome` from
the stage summary file into the coordinator contract file so the stage
consumes domain contracts rather than owning them.

Because `IStartupSongLoadOperation` is public and
`CreateStartupSongLoadRequest` is protected on public `BaseGame`, every type
appearing in those signatures (`StartupSongLoadRequest`,
`StartupSongLoadResult`, `StartupSongLoadProgressSnapshot`,
`StartupSongLoadStep`, `StartupSongLoadPath`,
`StartupSongLoadOutcome`, and `SongPersistencePath`) must also be public.
`StartupSongLoadCoordinator` itself remains internal.

Expected operational outcomes, including cancellation and a handled
enumeration/import failure after the one cache fallback, complete
`Completion` with a terminal result. An unexpected defect may fault
`Completion`; before doing so the coordinator must publish a terminal failure
snapshot, complete the database milestone signal so consumers cannot hang,
and attach fault observation. `StartupStage` handles both a terminal result
and a faulted completion without restarting the operation.

- [ ] **Step 1.2: Write failing coordinator contract and one-shot tests**

Cover:

- repeated and concurrent `Start()` calls return the same completion task;
- mutating the caller-owned roots array/list and later config values does not
  change the request's defensive read-only copy;
- the supplied `SongManager` instance is used and the worker never resolves
  `SongManager.Instance`;
- the worker is scheduled off the caller thread;
- database-ready completes exactly once after successful initialization;
- terminal-before-database-ready completes the database milestone signal so no
  consumer hangs; the terminal result or fault remains authoritative;
- latest immutable snapshot is replayable by a late consumer;
- progress contains domain steps, not `StartupPhase`;
- final result is authoritative when an older progress snapshot was read;
- expected operational failure becomes one terminal failure result;
- unexpected failure is observed and cannot become unobserved task failure;
- cache and forced-enumeration paths preserve current behavior;
- enumeration failure attempts committed-cache fallback once;
- publication occurs only after successful commit;
- `SongManager.SetInitialized()` is called exactly once after the selected
  path/fallback finishes and immediately before terminal publication,
  preserving the current SaveSongsDB behavior for terminal outcomes;
- cancellation before start, during traversal, after parsing, and during
  import reaches one terminal cancellation result.

Representative test:

```csharp
[Fact]
public async Task Start_WhenCalledConcurrently_ShouldRunPipelineExactlyOnce()
{
    var harness = CoordinatorHarness.Create();
    var coordinator = harness.CreateCoordinator();

    var outerStarts = Enumerable.Range(0, 16)
        .Select(_ => Task.Factory.StartNew(
            coordinator.Start,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default))
        .ToArray();

    var completions = await Task.WhenAll(outerStarts);
    Assert.All(completions, task => Assert.Same(completions[0], task));

    harness.ReleasePipeline();
    await completions[0];

    Assert.Equal(1, harness.DatabaseInitializeCalls);
    Assert.Equal(1, harness.EnumerationCalls);
}
```

- [ ] **Step 1.3: Run focused tests and verify RED**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~StartupSongLoadCoordinatorTests"
```

Expected: compilation fails because coordinator contracts do not exist.

- [ ] **Step 1.4: Implement one-shot scheduling and immutable progress**

Implementation requirements:

- capture the request and `SongManager` in the constructor;
- create one `TaskCompletionSource` for database readiness with
  `RunContinuationsAsynchronously`;
- protect only task creation and snapshot replacement;
- use one `Task.Run` for the complete pipeline;
- never call user/UI code from the worker;
- make `Start()` idempotent under concurrent calls;
- expose the same completion task on every access;
- mark terminal snapshot/result in `finally`-safe logic;
- ensure terminal failure/cancellation also releases `DatabaseReady`;
- attach an observer to any unexpected fault.

- [ ] **Step 1.5: Move the existing logical startup flow into the coordinator**

Preserve this order:

1. Initialize the database service.
2. Publish database-ready and its duration.
3. Select forced enumeration or committed cache.
4. On cache: build the committed hierarchy once.
5. On enumeration: discover/parse into the temporary hierarchy.
6. Import through the existing first-wave EF path.
7. Publish the temporary hierarchy only after commit.
8. On enumeration failure, try committed-cache fallback once.
9. Call `SongManager.SetInitialized()` exactly once before terminal
   publication, including the failure/cancellation outcomes that Startup would
   currently drain through `SaveSongsDB`.
10. Return one terminal result.

Use narrow internal delegates/interfaces in tests rather than mocking concrete
EF internals. Do not add a second implementation of grouping or publication.
Move the existing
`PerformPhaseOperationSync_SaveSongsDb_ShouldOnlyMarkInitialized` coverage to
the coordinator owner. Application-shutdown cancellation need not reach
Startup summary/Title, but any coordinator terminal publication must happen
after this compatibility flag is set.

- [ ] **Step 1.6: Strengthen enumeration cancellation boundaries**

In `SongManager`, call `ThrowIfCancellationRequested`:

- before and after each root;
- immediately before and after directory enumeration;
- immediately before and after file enumeration;
- immediately before and after each chart parse;
- before batch import;
- before hierarchy publication.

The parser delegate has no token. One active parse remains the maximum normal
cancellation-latency unit.

Add deterministic delegate tests that cancel at every boundary and assert:

- no import begins after cancellation;
- no temporary hierarchy publishes;
- `_enumCancellation` single-flight state is released;
- later enumeration can start normally.

- [ ] **Step 1.7: Migrate ownership-specific tests**

Move cache-versus-enumeration and fallback decision tests out of
`StartupStageLogicTests` into coordinator tests. Keep graphics, theme, layout,
rendered-frame, and presentation tests in the stage suite.

Update summary tests only for the moved enums; do not append second-wave timing
fields until Task 2 has the actual lifecycle values.

- [ ] **Step 1.8: Run focused and first-wave regressions**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~StartupSongLoadCoordinatorTests|FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~SongDatabaseServiceBulkImportTests|FullyQualifiedName~StartupSongLoadSummaryTests"
```

Expected: all selected tests pass.

- [ ] **Step 1.9: Review and commit**

Run specification and quality reviews, fix verified findings, then:

```bash
rtk git diff --check
rtk git add \
  DTXMania.Game/Lib/Song/StartupSongLoadContracts.cs \
  DTXMania.Game/Lib/Song/StartupSongLoadCoordinator.cs \
  DTXMania.Game/Lib/Song/SongManager.cs \
  DTXMania.Game/Lib/Song/SongImportModels.cs \
  DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs \
  DTXMania.Test/Song/StartupSongLoadCoordinatorTests.cs \
  DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs \
  DTXMania.Test/Stage/StartupStageLogicTests.cs
rtk git commit -m "feat: add startup song load coordinator"
```

---

### Task 2: Integrate the Coordinator, Startup UI, Readiness, and Shutdown

**Files:**

- Create: `DTXMania.Test/BaseGameStartupSongLoadTests.cs`
- Create: `DTXMania.Test/Stage/StartupStageCoordinatorTests.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Game/Lib/GameApi.cs`
- Modify: `DTXMania.Game/Lib/GameApiImplementation.cs`
- Modify: `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`
- Modify: `DTXMania.Game/Lib/JsonRpc/JsonRpcMessage.cs`
- Modify: `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs`
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`
- Modify: `DTXMania.Test/GameApi/GameApiImplementationTests.cs`
- Modify: `DTXMania.Test/GameApi/GameTelemetrySnapshotTests.cs`
- Modify: `DTXMania.Test/JsonRpc/JsonRpcServerIntegrationTests.cs`
- Modify: `DTXMania.Test/JsonRpc/JsonRpcServerValidationTests.cs`
- Modify: `DTXMania.Test/JsonRpc/JsonRpcServerTests.cs`
- Modify: `DTXMania.Test/Stage/StartupStageLogicTests.cs`
- Modify: `DTXMania.Test/Stage/StartupStageAdditionalCoverageTests.cs`
- Modify: `DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs`

**Produces:**

- `BaseGame` starts one coordinator immediately after config loading.
- `StartupStage` only attaches to and displays that operation.
- Completed phases drain in one bounded update.
- Startup still renders one frame and emits one summary/Title request.
- Every external stage change is atomically fenced until the complete launch
  barrier and receives a typed JSON-RPC result.
- Shutdown rejects work, cancels and observes the coordinator, then disposes in
  dependency order or retains worker dependencies after timeout.

- [ ] **Step 2.1: Write failing BaseGame early-start tests**

Add test seams:

```csharp
protected virtual StartupSongLoadRequest CreateStartupSongLoadRequest();
internal virtual StartupSongLoadCoordinator CreateStartupSongLoadCoordinator(
    StartupSongLoadRequest request,
    SongManager songManager,
    CancellationToken cancellationToken);
protected virtual TimeSpan StartupSongLoadShutdownTimeout =>
    TimeSpan.FromSeconds(5);
```

Expose the read-only operation through public `IStageGame` as a nullable
default property so unrelated concrete test fakes continue to compile:

```csharp
IStartupSongLoadOperation? StartupSongLoadOperation => null;
```

`BaseGame` returns the launch operation. Production `StartupStage` treats a
missing operation as a retained terminal failure rather than starting legacy
work; focused stage tests supply a fake operation.

Test:

- config load finishes before request creation;
- production request captures the configured DTX path, app-data database path,
  and `ForceEnumeration = true`;
- an override can return `ForceEnumeration = false`;
- `SongManager.Instance` is captured on the game thread;
- coordinator starts before graphics construction and `base.Initialize`;
- `LoadContent` and Startup activation reuse the same operation;
- repeated lifecycle access does not restart it;
- immutable path capture survives config mutation;
- a startup fault before stage creation remains observable.

- [ ] **Step 2.2: Write failing Startup consumer/drain tests**

Use a fake `IStartupSongLoadOperation` with controllable milestones. Cover the
complete table:

| Operation state | Drained phases | Waiting phase |
| --- | --- | --- |
| Nonterminal, database not ready | `SystemSounds`, `ConfigValidation` | `SongListDB` |
| Database ready, nonterminal | Through `LoadScoreFiles` | `EnumerateSongs` |
| Terminal success | Every remaining phase | `Complete` |
| Terminal failure | Every remaining phase | `Complete` |
| Terminal before database-ready | Every remaining phase | `Complete` |

Also test:

- the loop is bounded by the finite phase count;
- duration metadata never gates advancement;
- no old phase helper can initialize a database or load songs;
- attaching to an already running operation;
- attaching to an already completed operation;
- deactivation does not cancel, observe, or restart the operation;
- reactivation attaches to the same completion task;
- a retired activation cannot mutate current UI state;
- one rendered Startup frame is required;
- exactly one summary and internal Title request;
- early failure: attach, drain, render once, then emit one failure summary and
  one Title request on the next update;
- application-shutdown cancellation does not require a summary or Title.

- [ ] **Step 2.3: Write failing readiness, API, telemetry, and JSON-RPC tests**

Add this exact lifecycle bridge to `IGameContext` in
`GameApiImplementation.cs`:

```csharp
bool ExternalStageChangesReady { get; }
bool IsShuttingDown { get; }
bool QueueMainThreadAction(Action action);
```

Introduce the exact result and API signature:

```csharp
public enum StageChangeRequestResult
{
    Accepted,
    UnknownStage,
    StartupNotReady,
    ShuttingDown
}

Task<StageChangeRequestResult> ChangeStageAsync(string stageName);
```

Hold the coordinator/lifecycle barrier and test:

- every `Enum.GetValues<StageType>()` target, explicitly including `Startup`,
  `Title`, `Config`, and `SongSelect`, returns `StartupNotReady`;
- a rejected request never enters the main-thread queue and cannot execute in
  a later frame;
- invalid input is parsed first and returns `UnknownStage`, even while startup
  is blocked or shutdown has begun;
- a request linearized after shutdown publication returns `ShuttingDown`,
  distinct from `StartupNotReady`;
- a shutdown race that makes atomic queue admission return false also returns
  `ShuttingDown`, never `Accepted`;
- `Accepted` means queued, not transition-completed;
- `GetGameState` remains available and database-free while startup is blocked;
- no `RootSongs` read/enumeration or second song-database context occurs while
  the coordinator is deliberately held nonterminal;
- readiness remains false for every partial launch-barrier combination and
  becomes true only after coordinator terminal, Startup frame, summary, and
  completed normal Title transition;
- the launch-monotonic readiness value is not cleared by later normal stage
  transitions or shutdown.

Add non-null Boolean `ExternalStageChangesReady` to
`GameTelemetrySnapshot`. Test both the ordinary snapshot path and a stage that
implements `IStageTelemetryProvider`; the provider-copy path must retain
`ExternalStageChangesReady=true` before provider telemetry is populated.

Add the named JSON-RPC constant:

```csharp
public const int StartupNotReady = -32004;
```

Verify the exact wire mapping:

| Result | JSON-RPC code | Structured data |
| --- | ---: | --- |
| `UnknownStage` | `InvalidParams` (`-32602`) | validation data only |
| `StartupNotReady` | `StartupNotReady` (`-32004`) | `reason=startup_not_ready`, sanitized parsed stage |
| `ShuttingDown` | existing `GameNotRunning` (`-32001`) | `reason=shutting_down`, sanitized parsed stage |
| `Accepted` | success | existing success object |

Update
`JsonRpcServerTests.JsonRpcErrorCodes_ShouldHaveCorrectValues` to pin
`StartupNotReady == -32004`. Migrate every current `Task<bool>` mock to the
typed result. The stage value in an error must come from the parsed enum, not
raw user-controlled text.

- [ ] **Step 2.4: Write failing shutdown-order tests**

Use injected events/spies to assert this exact order:

1. mark shutting down;
2. atomically reject queue and screenshot admission, exchange any
   `_pendingScreenshot`, and cancel/complete it before waiting on HTTP
   shutdown;
3. cancel/observe API startup and call `JsonRpcServer.StopAsync` so Kestrel
   stops accepting requests;
4. cancel the coordinator token;
5. observe the coordinator for the configured bound;
6. if terminal, dispose coordinator/database dependencies;
7. dispose StageManager;
8. dispose resources;
9. dispose graphics;
10. dispose the already-stopped API/server objects;
11. dispose logger factory last.

Cover:

- responsive cancellation completes and is observed;
- an in-flight screenshot request is canceled before the listener-stop wait
  and disposal completes without another `Draw`;
- a screenshot request racing after shutdown cannot install a new pending
  completion source;
- a request cannot reach the game after the listener-stop checkpoint and
  before StageManager/resource teardown;
- coordinator success/failure already terminal;
- a hung parse exceeds an injected short timeout;
- timeout emits exactly one Release-visible
  `HPA192_SHUTDOWN timeout_ms=... step=...`;
- step/error values are one-line sanitized tokens;
- timeout attaches an eventual fault observer;
- unrelated teardown continues;
- coordinator database dependencies and logger factory are retained;
- `SongManager.Clear` or equivalent database disposal is not called after
  timeout.

Write `HPA192_SHUTDOWN` directly to `Console.Out`; provider-formatted logging
may accompany it but cannot replace the anchored Release-visible line.

- [ ] **Step 2.5: Run all new suites and verify RED**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~BaseGameStartupSongLoadTests|FullyQualifiedName~StartupStageCoordinatorTests|FullyQualifiedName~GameApiImplementationTests|FullyQualifiedName~GameTelemetrySnapshotTests|FullyQualifiedName~JsonRpcServerIntegrationTests|FullyQualifiedName~JsonRpcServerValidationTests|FullyQualifiedName~JsonRpcServerTests|FullyQualifiedName~BaseGameTests"
```

Expected: compile/test failure because BaseGame ownership, stage consumption,
the launch barrier, and the typed protocol do not exist.

- [ ] **Step 2.6: Integrate early launch ownership in BaseGame**

Immediately after config load:

1. capture configured roots and `AppPaths` database path;
2. capture `SongManager.Instance` on the game thread;
3. create the application-owned `CancellationTokenSource`;
4. create the coordinator through the test seam;
5. call `Start()` once;
6. retain its read-only operation for `IStageGame`.

Do this before:

- `GraphicsManager` construction;
- `base.Initialize`;
- `LoadContent`;
- `ResourceManager`;
- `StageManager`;
- Startup activation.

Do not await the coordinator on the MonoGame thread.

- [ ] **Step 2.7: Replace StartupStage load ownership**

Remove coordinator-path ownership from `StartupStage`:

- `_songManager`;
- `_cancellationTokenSource`;
- `_currentAsyncTask`;
- `_operationPerformedForPhase`;
- `_needsEnumeration`;
- `_selectedLoadPath`, `_songLoadOutcome`, `_songLoadError`,
  `_enumerationResult`, and stage-owned coordinator timing/count fields;
- `MarkSongManagerInitialized` and the SaveSongsDB side effect;
- `ForceEnumeration`;
- `InitializeDatabaseServiceAsync`;
- `RunSongLoadAsync`;
- every phase launcher that can start database or enumeration work.

Retain activation generation only for UI, summary, and transition side effects.
On activation, attach to `_game.StartupSongLoadOperation` and replay
`LatestProgress`.

Rebuild `WriteSummaryOnce` from the authoritative retained
`StartupSongLoadResult` (or the normalized failure created from a faulted
completion), never from mutable fields that the removed stage loaders used to
populate. Tests must assert nonzero coordinator timings/counts and the correct
success, cache fallback, cancellation, and failure path/outcome values when
the stage attaches both before and after completion.

Update `StartupStageAdditionalCoverageTests` in the same change. Remove or
replace its reflection assertions for `_currentAsyncTask`,
`_cancellationTokenSource`, `HasAsyncOperation`, and retired task ownership
with coordinator-consumer and no-legacy-launcher assertions.

- [ ] **Step 2.8: Implement a table-driven bounded phase drain**

Express coordinator state as milestone predicates, not elapsed time:

```csharp
private bool CanCompletePhase(
    StartupPhase phase,
    StartupSongLoadProgressSnapshot progress)
{
    if (progress.IsTerminal)
        return true;

    return phase switch
    {
        StartupPhase.SystemSounds => true,
        StartupPhase.ConfigValidation => true,
        StartupPhase.SongListDB => progress.DatabaseReady,
        StartupPhase.SongsDB => progress.DatabaseReady,
        StartupPhase.LoadScoreCache => progress.DatabaseReady,
        StartupPhase.LoadScoreFiles => progress.DatabaseReady,
        _ => false
    };
}
```

On each update:

- read one immutable snapshot;
- drain while the current phase is complete;
- cap iterations at the number of `StartupPhase` values;
- stop at `SongListDB`, `EnumerateSongs`, or `Complete`;
- let terminal state dominate database readiness;
- use `_phaseInfo.duration` only in drawing interpolation.

- [ ] **Step 2.9: Extend the one-line startup summary**

Append, in this order:

```text
operation_ms=...
pre_stage_ms=...
stage_wait_ms=...
persistence_path=ef|fresh_sqlite
```

Definitions:

- `operation_ms`: coordinator start to terminal.
- `pre_stage_ms`: coordinator elapsed before current Startup activation.
- `stage_wait_ms`: activation to terminal, or zero if already terminal.
- `total_ms`: unchanged activation-to-summary.

Tests must allow coordinator phase totals and `operation_ms` to exceed
`total_ms`.

- [ ] **Step 2.10: Implement the launch barrier and typed protocol atomically**

Land the coordinator ownership, Startup consumer, readiness fence, typed API,
telemetry, and JSON-RPC mapping in this same commit. Do not leave an
intermediate commit in which the coordinator can mutate the hierarchy while
an external stage request can reach `SongSelect`.

`BaseGame` owns atomic launch-monotonic readiness and shutdown state. Publish
readiness on the game thread only after:

1. the coordinator is terminal;
2. Startup has reported its rendered frame;
3. Startup has reported its summary;
4. `StageManager.Update` has completed the normal transition, the published
   current stage is Title, and `IsTransitioning == false`.

Perform that publication after the qualifying `StageManager.Update` and
before draining accepted main-thread actions. Do not clear readiness during
ordinary transitions or shutdown.

`GameApiImplementation.ChangeStageAsync` must:

1. parse and validate first, returning `UnknownStage` for invalid input;
2. return `ShuttingDown` when the shutdown state is already published;
3. while readiness is false, recheck shutdown before returning
   `StartupNotReady`;
4. submit the parsed target through atomic `QueueMainThreadAction`;
5. return `ShuttingDown` if admission loses the shutdown race;
6. return `Accepted` only when the queue accepted the action.

The queue and shutdown publication share one lifecycle gate so no action can
be admitted after shutdown. Keep FIFO behavior and the 64-actions-per-frame
drain cap. The queued action still uses internal
`StageManager.ChangeStage`; Startup's internal Title request never passes
through the external fence.

Map the four results exactly as specified in Step 2.3. Populate the non-null
readiness field on every telemetry snapshot and preserve it through the
provider-telemetry copy path.

- [ ] **Step 2.11: Implement bounded coordinator-first shutdown**

Keep synchronous MonoGame disposal safe:

- atomically publish `_isShuttingDown`;
- make queue and screenshot admission reject after that point;
- under the same lifecycle gate, exchange `_pendingScreenshot`; after
  releasing the gate, complete the captured source as canceled before waiting
  for server shutdown. A later screenshot request returns an already-canceled
  task and cannot install new pending work;
- cancel and observe any in-progress API start, then call
  `_jsonRpcServer.StopAsync().GetAwaiter().GetResult()` before disposing any
  manager; the existing API cancellation token only cancels startup and does
  not stop an already-running Kestrel listener;
- retain the stopped server object for disposal later in dependency order;
- cancel the coordinator token;
- wait with `Task.WhenAny`/bounded blocking only for the injected timeout;
- observe completed task outcome without rethrowing from teardown;
- on timeout, retain coordinator-owned dependencies and logger factory;
- always observe eventual faults with an execute-synchronously continuation;
- dispose logger factory only when no retained worker can use it.

- [ ] **Step 2.12: Run focused lifecycle and protocol tests**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~BaseGameStartupSongLoadTests|FullyQualifiedName~StartupStageCoordinatorTests|FullyQualifiedName~StartupStageAdditionalCoverageTests|FullyQualifiedName~StartupSongLoadSummaryTests|FullyQualifiedName~BaseGameTests|FullyQualifiedName~StartupStageLogicTests|FullyQualifiedName~GameApiImplementationTests|FullyQualifiedName~GameTelemetrySnapshotTests|FullyQualifiedName~JsonRpcServerIntegrationTests|FullyQualifiedName~JsonRpcServerValidationTests|FullyQualifiedName~JsonRpcServerTests"
```

Expected: all selected tests pass.

- [ ] **Step 2.13: Run coordinator and import regressions**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~StartupSongLoadCoordinatorTests|FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~SongDatabaseServiceBulkImportTests"
```

Expected: all selected tests pass.

- [ ] **Step 2.14: Review and commit**

After specification and quality review:

```bash
rtk git diff --check
rtk git add \
  DTXMania.Game/Game1.cs \
  DTXMania.Game/Lib/GameApi.cs \
  DTXMania.Game/Lib/GameApiImplementation.cs \
  DTXMania.Game/Lib/GameTelemetrySnapshot.cs \
  DTXMania.Game/Lib/JsonRpc/JsonRpcMessage.cs \
  DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs \
  DTXMania.Game/Lib/Stage/IStageGame.cs \
  DTXMania.Game/Lib/Stage/StartupStage.cs \
  DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs \
  DTXMania.Test/BaseGameStartupSongLoadTests.cs \
  DTXMania.Test/BaseGameTests.cs \
  DTXMania.Test/GameApi/GameApiImplementationTests.cs \
  DTXMania.Test/GameApi/GameTelemetrySnapshotTests.cs \
  DTXMania.Test/JsonRpc/JsonRpcServerIntegrationTests.cs \
  DTXMania.Test/JsonRpc/JsonRpcServerValidationTests.cs \
  DTXMania.Test/JsonRpc/JsonRpcServerTests.cs \
  DTXMania.Test/Stage/StartupStageCoordinatorTests.cs \
  DTXMania.Test/Stage/StartupStageAdditionalCoverageTests.cs \
  DTXMania.Test/Stage/StartupStageLogicTests.cs \
  DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs
rtk git commit -m "feat: overlap startup song loading safely"
```

---

### Task 3: Propagate Startup Readiness Through MCP, E2E, and CI

**Files:**

- Create: `MCP.Test/MCP.Test.csproj`
- Create: `MCP.Test/Server/JsonRpcClientTests.cs`
- Create: `MCP.Test/Server/GameInteractionStageChangeTests.cs`
- Modify: `MCP/Server/JsonRpcClient.cs`
- Modify: `MCP/Server/GameInteractionService.cs`
- Modify: `MCP/Server/GameInteractionMcpToolHandlers.cs`
- Modify: `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`
- Modify: `DTXMania.E2E/JsonRpc/JsonRpcGameClientTests.cs`
- Modify: `DTXMania.E2E/Telemetry/E2EGameState.cs`
- Modify: `DTXMania.E2E/Telemetry/E2EGameStateTests.cs`
- Modify: `DTXMania.E2E/DrumMappingStageSmokeTests.cs`
- Modify: `DTXMania.sln`
- Modify: `.github/workflows/build-and-test.yml`

**Produces:**

- MCP preservation of both startup-not-ready and shutdown code/reason.
- E2E telemetry/readiness waiting without inferring readiness from Title.
- Automated MCP coverage on both platforms and E2E support coverage on the
  Windows runner.

- [ ] **Step 3.1: Create the MCP test project and injection seams**

Create a normal `net8.0` xUnit project referencing `MCP/MCP.csproj`, using the
repository's current test package versions (`Microsoft.NET.Test.Sdk` 18.8.1,
xUnit 2.9.3, and xUnit runner 2.8.2). Add it to `DTXMania.sln` with:

```bash
rtk dotnet sln DTXMania.sln add MCP.Test/MCP.Test.csproj
```

Add a constructor seam allowing `JsonRpcClient` to receive a test
`HttpClient`/handler without owning a live port. Also add a
`GameInteractionService` client/factory seam; changing only
`JsonRpcClient` is insufficient because the service currently constructs its
own client.

Make ownership explicit:

- production-created client/`HttpClient` instances are disposed by their
  production owner;
- injected test instances are not disposed unless the injecting caller
  transfers ownership explicitly;
- service disposal observes the selected ownership mode exactly once.

- [ ] **Step 3.2: Write failing MCP transport and service tests**

Tests cover:

- a JSON-RPC error throws `JsonRpcException` with preserved `ErrorCode`;
- `ErrorData` remains a `JsonElement` containing `reason`;
- cancellation still propagates;
- ordinary HTTP/serialization failures retain nullable code/reason;
- `GameInteractionService.ChangeStageAsync` returns a named record:

  ```csharp
  public sealed record StageChangeServiceResult(
      bool Success,
      string Message,
      int? ErrorCode,
      string? Reason);
  ```

- startup-not-ready maps to `Success=false`, `ErrorCode=-32004`,
  `Reason="startup_not_ready"`;
- shutdown maps to `Success=false`, `ErrorCode=-32001`,
  `Reason="shutting_down"`;
- MCP structured content includes `action`, client/stage, `errorCode`, and
  `reason`;
- success retains null code/reason;
- injected-client and production-owned disposal follow the ownership
  contract.

- [ ] **Step 3.3: Run MCP tests and verify RED**

```bash
rtk dotnet test MCP.Test/MCP.Test.csproj
```

Expected: failure until constructors and typed service result are implemented.

- [ ] **Step 3.4: Implement MCP error preservation**

Do not parse an error code back out of message text. Read
`JsonRpcException.ErrorCode` and structured `ErrorData.reason`. Preserve
nullable values for non-JSON-RPC failures. Update the handler to use the named
result instead of tuple deconstruction and include code/reason in
`StructuredContent`. Preserve both `-32004/startup_not_ready` and
`-32001/shutting_down` without collapsing either into message text. Implement
the injection and disposal contract from Step 3.1.

- [ ] **Step 3.5: Round-trip readiness through E2E models and clients**

Add a required Boolean `ExternalStageChangesReady` member to `E2EGameState`;
do not infer readiness from `StageType`.

Test:

- JSON state without the required field fails loudly rather than defaulting to
  ready;
- false and true values round-trip;
- `JsonRpcGameClient` throws an unexpected JSON-RPC error immediately rather
  than treating it as a successful transition;
- existing error code/data remain available for diagnostics.

Update both drum-mapping smoke flows to wait for:

```csharp
state.StageType == "Title" && state.ExternalStageChangesReady
```

before requesting `DrumConfig`. Correct the stale comments:
`StageManager` publishes the target stage when the transition completes, not
when it is merely queued.

- [ ] **Step 3.6: Add MCP and E2E support tests to CI**

In `.github/workflows/build-and-test.yml`, run:

```bash
dotnet test MCP.Test/MCP.Test.csproj \
  --configuration Debug \
  --verbosity normal \
  --logger trx \
  --results-directory ./TestResults/mcp
```

in Windows and macOS jobs. Include MCP results in the existing uploaded result
directory. Do not mark the step optional.

Keep `Category=E2E-Support` in the Windows E2E job and ensure the modified
client/state tests are compiled and executed there. Do not add the
Windows-targeted E2E project to a local macOS command.

- [ ] **Step 3.7: Run local focused tests and require the Windows gate**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~GameApiImplementationTests|FullyQualifiedName~GameTelemetrySnapshotTests|FullyQualifiedName~JsonRpcServerIntegrationTests|FullyQualifiedName~JsonRpcServerValidationTests|FullyQualifiedName~JsonRpcServerTests|FullyQualifiedName~BaseGameStartupSongLoadTests|FullyQualifiedName~BaseGameTests"

rtk dotnet test MCP.Test/MCP.Test.csproj
```

Expected: all locally executable selected suites pass.

`DTXMania.E2E` targets `net8.0-windows7.0` and this repository does not enable
cross-target execution on macOS. Do not claim the Task 3 gate complete until
the Windows CI `Category=E2E-Support` result is green and both platform MCP
test steps pass.

- [ ] **Step 3.8: Review and commit**

After specification and quality review:

```bash
rtk git diff --check
rtk git add \
  MCP \
  MCP.Test \
  DTXMania.E2E \
  DTXMania.sln \
  .github/workflows/build-and-test.yml
rtk git commit -m "feat: propagate startup readiness to clients"
```

---

### Task 4: Make Fresh-Database Evidence Service-Owned

**Files:**

- Create: `DTXMania.Test/Song/SongDatabaseServiceFreshInitializationTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Modify: existing migration/repair tests only where the initialization seam
  requires it

**Produces:**

- Sticky proof that the service created the usable schema in this
  initialization.
- A strict, minimal fresh bootstrap.
- The complete current migration/probe path for every existing database.
- One-shot direct-import eligibility state owned by the service.

- [ ] **Step 4.1: Write a fresh-initialization state table**

Tests must cover:

| Entry/recovery path | `EnsureCreated` result | Fresh |
| --- | ---: | --- |
| File absent at entry | true | true |
| Invalid header deleted | true | true |
| Zero-byte/raw empty file deleted as an invalid header | true | true |
| Unicode/version recreation deleted | true | true |
| Caught not-a-database retry deleted | true | true |
| Explicit/corruption purge before initialize | true | true |
| Pre-existing valid database | false | false |
| File-backed current-schema database created by a prior service/launch but containing zero import rows | false | false |
| Failed deletion leaves old file | any | false |
| Table-already-exists recovery | false | false |
| Pre-created options constructor | n/a | false |

Add tests that freshness:

- is false until initialization fully succeeds;
- requires `EnsureCreatedAsync` to return true;
- remains sticky within the current initialization epoch;
- is false on the next service/launch against the created file;
- makes direct import unavailable after any successful EF or direct import
  commit in that initialization epoch, even when the plan has zero rows;
- resets as part of a successful explicit purge before the next initialization
  epoch;
- becomes nonfresh/unavailable after restore, because restored bytes are
  pre-existing state rather than a schema created by that initialization.

- [ ] **Step 4.2: Add deterministic internal initialization seams**

Use narrow seams for cases the filesystem cannot induce reliably:

- file-exists observation;
- deletion attempt/result;
- `EnsureCreatedAsync` result;
- strict version-marker write;
- legacy-probe observation.

Keep production defaults as the real filesystem/EF operations. Do not expose
freshness mutation publicly.

Treat one service instance as a sequence of initialization epochs:

- `PurgeDatabaseAsync` resets `_isInitialized`, fresh evidence, and consumed
  state only after the purge succeeds; the next `InitializeDatabaseAsync` may
  prove a new fresh epoch.
- A failed purge cannot establish new freshness.
- `RestoreDatabaseAsync` clears initialization/fresh evidence and direct
  eligibility, then the restored file must traverse the complete existing
  initialization/migration path.
- Disposal ends the final epoch.

- [ ] **Step 4.3: Run fresh initialization tests and verify RED**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongDatabaseServiceFreshInitializationTests"
```

Expected: compile/test failure because freshness evidence and seams are absent.

- [ ] **Step 4.4: Split initialization into fresh and existing paths**

Capture:

- file existence at entry;
- whether an eligible recovery successfully deleted the file;
- the exact Boolean from `EnsureCreatedAsync`.

Set:

```csharp
private bool _isFreshDatabaseForCurrentInitialization;
private bool _hasSuccessfulImportCommitted;

internal bool IsFreshDatabaseForCurrentInitialization { get; }
internal bool IsFreshDirectImportAvailable { get; }
```

Implement the getters and all epoch mutations over these backing fields under
the existing initialization/state lock (or an equivalent atomic state
snapshot). The getter-only surface is intentional; auto-properties that cannot
be updated across purge/restore epochs are not an executable implementation.
`IsFreshDirectImportAvailable` derives from fresh evidence and
`!_hasSuccessfulImportCommitted`.

only after:

1. the file was absent or successfully deleted;
2. `EnsureCreatedAsync` returned true;
3. fresh bootstrap completed;
4. the strict database-version marker write succeeded.

Fresh bootstrap:

- best-effort `PRAGMA journal_mode=DELETE`;
- best-effort `PRAGMA case_sensitive_like=OFF`;
- strict version-marker table/row;
- no bookmark, NX, history, receipt, speed-scope, or other legacy probe.

Existing path:

- retains every current best-effort pragma;
- retains version marker and every migration/probe/repair;
- preserves current catch/recovery behavior.

- [ ] **Step 4.5: Preserve constructor semantics**

The options/context-factory constructors represent already-created test
schemas. They remain initialized and explicitly nonfresh. Do not infer
freshness from empty table counts.

- [ ] **Step 4.6: Run focused initialization and migration tests**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~SongDatabaseServiceFreshInitializationTests|FullyQualifiedName~SongDatabaseServiceBookmarkMigrationTests|FullyQualifiedName~Nx|FullyQualifiedName~PlaybackSpeed|FullyQualifiedName~PerformanceHistory|FullyQualifiedName~ScoreSaveReceipt"
```

Expected: all selected tests pass.

- [ ] **Step 4.7: Review and commit**

```bash
rtk git diff --check
rtk git add \
  DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs \
  DTXMania.Test/Song/SongDatabaseServiceFreshInitializationTests.cs \
  DTXMania.Test/Song
rtk git commit -m "refactor: split fresh database initialization"
```

Before committing, inspect the staged list and unstage unrelated song tests;
the broad final `git add` line is only for migration tests actually changed by
this task.

---

### Task 5: Build One Pure Fresh Import Plan and Prove It Through EF

**Files:**

- Create: `DTXMania.Game/Lib/Song/Entities/FreshImportPlan.cs`
- Create: `DTXMania.Test/Song/FreshImportPlanTests.cs`
- Modify: `DTXMania.Game/Lib/Song/SongImportModels.cs`
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Modify: `DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs`
- Modify: `DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs`

**Produces:**

- A deterministic, persistence-independent fresh entity graph.
- A verified-empty fresh EF branch using that graph.
- An explicit complete-batch contract and persistence-path result.
- No direct SQL yet.

- [ ] **Step 5.1: Extend import contracts**

Extend `SongBulkImportRequest` with `bool IsComplete`.
Extend `SongBulkImportResult` with the Task 1
`SongPersistencePath PersistencePath` contract.

Make `PersistencePath` the trailing positional parameter with
`SongPersistencePath.Ef` as its default so existing result construction
continues to compile and explicitly preserves EF semantics. Direct-writer
returns must still pass `FreshSqlite` explicitly. Incomplete batches are never
eligible for a fresh replacement import.

Update `SongManager.EnumerateAndImportSongsAsync` to pass
`batch.IsComplete` into the one `SongBulkImportRequest` construction. Add an
assertion in `SongManagerBulkEnumerationTests` that a complete root passes
`IsComplete=true`. Preserve the current earlier complete-batch gate: a
root/traversal failure produces an incomplete batch and must throw before the
importer/request call. Construct `IsComplete=false` only in direct
service-level guard tests; do not weaken the manager gate.

- [ ] **Step 5.2: Write failing pure-plan tests**

Define a pure graph such as:

```csharp
internal sealed record FreshImportPlan(
    IReadOnlyList<Song> Songs,
    IReadOnlyList<SongChart> Charts,
    IReadOnlyList<SongScore> Scores,
    IReadOnlyDictionary<string, SongChart> ChartsByPath,
    int Added,
    int Skipped,
    int Conflicts);
```

Tests cover:

- canonical path normalization and exact discovered membership;
- undiscovered candidates are skipped;
- duplicate candidates preserve first-wave skipped/conflict counters;
- deterministic group order, chart order, and primary chart;
- exactly one song per logical group;
- synthetic frozen shape creates 27 songs and 100 charts;
- every chart retains parsed metadata and its candidate relationship;
- fresh songs are unbookmarked;
- represented instruments get exactly one zeroed 100-percent score;
- no speed variants, history, or receipt rows;
- IDs remain unset/default at this stage;
- the same inputs always yield field-for-field equivalent plans;
- input collections are not mutated.
- a supplied fixed UTC timestamp becomes every new song's `CreatedAt` and
  `UpdatedAt`.

- [ ] **Step 5.3: Run plan tests and verify RED**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~FreshImportPlanTests"
```

Expected: compilation fails because the plan builder does not exist.

- [ ] **Step 5.4: Implement the pure builder**

The builder owns all creation of fresh Songs, SongCharts, and initial
SongScores. Neither EF nor the direct writer may independently recreate:

- grouping;
- defaults;
- score rows;
- counters;
- chart-path map;
- primary-chart selection.

It must not open SQLite, create a context, or publish hierarchy.

Capture one UTC timestamp at the planner boundary through an injected
clock/value and apply it to every new song. Do not let entity property
initializers call `DateTime.UtcNow` at different moments; otherwise repeated
plan construction and EF/direct parity cannot be deterministic.

- [ ] **Step 5.5: Write failing fresh-EF branch tests**

For a service with proven fresh state and verified empty import tables, test:

- one context;
- one explicit transaction;
- one `SaveChangesAsync`;
- the shared plan graph persists;
- database-generated IDs are copied into the returned graph/path map;
- 100 charts/27 songs;
- path/result counters match first-wave semantics;
- result reports `PersistencePath.Ef`;
- pre-existing empty database uses the full existing reconciliation branch;
- incomplete request uses the existing branch;
- existing database preservation behavior is unchanged;
- a successful zero-row fresh EF import consumes the epoch's eligibility, so
  a second zero-row request routes through existing EF reconciliation rather
  than selecting the fresh branch again.

- [ ] **Step 5.6: Add the fresh EF branch**

Before the existing preload/reconciliation work, select the fresh EF branch
only when:

- service freshness is proven;
- no successful import has committed in the current initialization epoch
  (`IsFreshDirectImportAvailable` is true);
- request is complete;
- Songs, SongCharts, and SongScores are empty.

Use the shared plan, one context, one explicit transaction, and one save.
This branch is the semantic oracle for Task 6. A successful EF commit marks
the initialization epoch as having imported, so a later call cannot switch to
direct SQL even if the committed plan had zero rows. A failed/rolled-back EF
attempt does not mark a successful import.

- [ ] **Step 5.7: Run focused plan/import tests**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~FreshImportPlanTests|FullyQualifiedName~SongDatabaseServiceBulkImportTests|FullyQualifiedName~SongManagerBulkEnumerationTests"
```

Expected: all selected tests pass.

- [ ] **Step 5.8: Run preservation regressions**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~Bookmark|FullyQualifiedName~PlaybackSpeed|FullyQualifiedName~PerformanceHistory|FullyQualifiedName~ScoreSaveReceipt|FullyQualifiedName~SongPathIdentity"
```

Expected: all selected tests pass.

- [ ] **Step 5.9: Review and commit**

```bash
rtk git diff --check
rtk git add \
  DTXMania.Game/Lib/Song/Entities/FreshImportPlan.cs \
  DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs \
  DTXMania.Game/Lib/Song/SongImportModels.cs \
  DTXMania.Game/Lib/Song/SongManager.cs \
  DTXMania.Test/Song/FreshImportPlanTests.cs \
  DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs \
  DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs
rtk git commit -m "refactor: share fresh song import planning"
```

---

### Task 6: Add the Guarded Prepared SQLite Fresh Writer

**Files:**

- Create: `DTXMania.Game/Lib/Song/Entities/FreshSqliteSongImporter.cs`
- Create: `DTXMania.Test/Song/FreshSqliteSongImporterTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/FreshImportPlan.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Modify: `DTXMania.Game/Lib/Song/SongImportModels.cs`
- Modify: coordinator/result tests for persistence-path propagation

**Produces:**

- Strict pre-mutation eligibility guards.
- Deterministic explicit IDs.
- One connection, one transaction, and three prepared reusable commands.
- Full rollback/sequence semantics.
- `persistence_path=fresh_sqlite` on successful eligible imports.

- [ ] **Step 6.1: Write failing guard tests**

Direct SQL is selected only when all are true:

- `IsFreshDatabaseForCurrentInitialization`;
- direct-import eligibility not consumed;
- request `IsComplete`;
- Songs, SongCharts, and SongScores are empty;
- service-owned connection opens against the same data source with
  `Cache=Shared` and 30-second timeout;
- `PRAGMA foreign_keys=ON` is verified;
- `sqlite_master` DDL for `Songs`, `SongCharts`, and `SongScores` contains the
  expected `INTEGER PRIMARY KEY AUTOINCREMENT`.

Classify failed guards before writing tests:

- A nonfresh service, incomplete request, or nonempty import table takes the
  complete first-wave existing-database reconciliation path.
- An epoch whose one-import eligibility is already consumed also takes the
  complete existing reconciliation path; it is not a pristine fresh fallback.
- A proven-fresh, complete, empty request whose direct-only foreign-key, DDL,
  connection, or injected test-disable guard fails before mutation takes the
  shared fresh EF branch.

Guard evaluation itself must occur before direct transaction mutation and must
not newly consume, reset, or otherwise change eligibility. Preserve its prior
state. A subsequent successful EF fallback commit then marks the epoch as
having imported. Every fallback reports `persistence_path=ef`.

- [ ] **Step 6.2: Write failing deterministic ID/plan tests**

The direct-write projection assigns positive IDs in deterministic plan order:

- songs first;
- charts grouped under song/plan order;
- scores under chart/plan order.

Tests must not rely on multi-row `RETURNING` order. Assert repeated plans
receive identical IDs and every relationship references the explicit parent
ID.

- [ ] **Step 6.3: Write failing complete column-mapping tests**

Assert the writer explicitly names and binds every persisted column.

`Songs`:

```text
Id, Title, Artist, Genre, Comment, CreatedAt, UpdatedAt, IsBookmarked
```

`SongCharts`:

```text
Id, SongId, FilePath, FileHash, FileSize, LastModified,
DifficultyLevel, DifficultyLabel, Bpm, Duration, BGMAdjust,
DrumLevel, DrumLevelDec, GuitarLevel, GuitarLevelDec,
BassLevel, BassLevelDec, HasDrumChart, HasGuitarChart,
HasBassChart, IsClassicDrums, IsClassicGuitar, IsClassicBass,
DrumNoteCount, GuitarNoteCount, BassNoteCount, PreviewFile,
PreviewImage, BackgroundFile, StageFile, FileFormat
```

`SongScores`:

```text
Id, ChartId, Instrument, PlaySpeedPercent, DifficultyLabel,
BestScore, BestRank, BestSkillPoint, BestAchievementRate,
FullCombo, Excellent, PlayCount, ClearCount, MaxCombo,
NxImportedPlayCount, NxImportedClearCount, HighSkill, SongSkill,
TotalNotes, BestPerfect, BestGreat, BestGood, BestPoor, BestMiss,
ProgressBar, LastPlayedAt, LastScore, LastSkillPoint, UsedDrumPad,
UsedKeyboard, UsedMidi, UsedJoypad, UsedMouse
```

Test bindings for:

- null as `DBNull.Value`;
- Boolean as 0/1;
- enums as integers;
- `DateTime` passed to the provider as `DateTime`;
- integers, reals, and strings;
- `SqliteParameter.Size` matching the EF model's `GetMaxLength()` metadata for
  every bounded text property;
- boundary and oversized text behavior matching an EF insert rather than
  assuming SQLite table DDL enforces `[MaxLength]`;
- default/unset nullable fields;
- every column exactly once.

Build the expected size manifest from `SongDbContext.Model` in the test and
compare it with the direct command parameters. Microsoft.Data.Sqlite uses
`SqliteParameter.Size` to truncate TEXT/BLOB values, while EF treats maximum
length as provider metadata, so missing or invented sizes can silently change
persisted values.

- [ ] **Step 6.4: Run writer tests and verify RED**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~FreshSqliteSongImporterTests"
```

Expected: compilation fails because the writer does not exist.

- [ ] **Step 6.5: Implement the service-owned connection factory**

`SongDatabaseService` creates the connection. The writer receives an already
configured connection/factory and must not format a second connection string.
Use the same data source and `Cache=Shared` setting plus the service's
30-second default command timeout. The existing first-wave EF bulk importer
keeps its deliberate 120-second per-context override; do not change it or
claim the two import timeouts are equal.

Open once. Enable and verify foreign keys before mutation.

- [ ] **Step 6.6: Implement prepared reusable commands**

For each table:

1. create one command;
2. attach the transaction;
3. name every persisted column;
4. define every parameter once;
5. set command timeout;
6. call `Prepare`;
7. rebind parameter values per row;
8. execute synchronously on the coordinator worker.

Insert parent-first:

1. Songs;
2. SongCharts;
3. SongScores.

Check cancellation before planning, before transaction start, between every
row command, and once more immediately before commit.

- [ ] **Step 6.7: Write rollback and trigger-failure tests**

Use a test-only row-write observer to cancel deterministically after one or
more commands. Add SQLite triggers that fail:

- first/selected song insert;
- chart insert;
- score insert.

Include cancellation immediately after the final score command. It must be
observed by the final pre-commit check and roll back rather than committing a
complete-looking batch.

For every case assert:

- no rows in any of the three tables;
- no hierarchy publication;
- every affected `sqlite_sequence` entry equals its pre-transaction value;
- exception/cancellation propagates;
- EF is not retried after mutation began;
- eligibility is not reported as successfully consumed.

- [ ] **Step 6.8: Write commit/sequence tests**

After successful explicit-ID commit:

- `sqlite_sequence` for every affected table is at least the inserted max;
- returned entities contain committed IDs;
- `ChartsByPath` references those entities;
- a later ordinary EF insert gets a higher non-conflicting ID;
- the successful-import epoch flag is set;
- a second import uses EF;
- result reports `FreshSqlite`.

- [ ] **Step 6.9: Write EF-versus-direct parity test**

Persist the same synthetic plan through fresh EF and direct SQL in separate
databases. Compare field-by-field:

- all song columns;
- all chart columns;
- all score columns;
- relationships;
- IDs after normalizing the expected allocation strategy;
- path map;
- counters;
- 100-chart/27-song shape.

Do not compare database file bytes or raw row order.

Add an internal test seam that disables direct-writer selection so the EF
fresh branch remains directly exercisable after Task 6. This is not a user
configuration flag; production always evaluates the real direct guards.

- [ ] **Step 6.10: Integrate direct selection**

Selection order inside `SongDatabaseService`:

1. Route nonfresh, incomplete, nonempty, or already-imported epochs directly
   to the complete existing EF reconciliation path without building a fresh
   plan.
2. For a proven-fresh, complete, empty, not-yet-imported epoch, build the pure
   fresh plan once.
3. Evaluate direct-only connection, foreign-key, DDL, and test-disable guards.
4. If a direct-only guard fails before mutation, execute the fresh EF branch
   with that same plan.
5. Otherwise execute the direct writer once.
6. Mark the epoch as having imported after either persistence path commits;
   do not mark it after rollback/failure.
7. Return the same aggregate result/path map.
8. Let the coordinator publish only after result success.

- [ ] **Step 6.11: Run focused writer and coordinator tests**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~FreshSqliteSongImporterTests|FullyQualifiedName~FreshImportPlanTests|FullyQualifiedName~SongDatabaseServiceBulkImportTests|FullyQualifiedName~StartupSongLoadCoordinatorTests"
```

Expected: all selected tests pass.

- [ ] **Step 6.12: Review and commit**

```bash
rtk git diff --check
rtk git add \
  DTXMania.Game/Lib/Song/Entities/FreshSqliteSongImporter.cs \
  DTXMania.Game/Lib/Song/Entities/FreshImportPlan.cs \
  DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs \
  DTXMania.Game/Lib/Song/SongImportModels.cs \
  DTXMania.Test/Song/FreshSqliteSongImporterTests.cs \
  DTXMania.Test/Song/FreshImportPlanTests.cs \
  DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs \
  DTXMania.Test/Song/StartupSongLoadCoordinatorTests.cs
rtk git commit -m "perf: add guarded fresh sqlite import"
```

---

### Task 7: Prove Schema, Parity, Rollback, Migration, and Lifecycle Contracts

**Files:**

- Create: `DTXMania.Test/Song/SqliteSchemaSnapshot.cs`
- Create: `DTXMania.Test/Song/SongDatabaseSchemaContractTests.cs`
- Modify: targeted tests only when a verified coverage gap is found

**Produces:**

- Fresh-versus-migrated structural differential.
- Exact direct-writer AUTOINCREMENT contract.
- Whole-system correctness evidence.
- Final whole-branch review/fix loop before measuring performance.

- [ ] **Step 7.1: Implement a semantic schema snapshot helper**

Capture and normalize:

- user table names and types;
- named user index names and uniqueness;
- `PRAGMA table_info`;
- `PRAGMA index_list` plus `PRAGMA index_info`;
- `PRAGMA index_xinfo` collation/key metadata;
- `PRAGMA foreign_key_list`;
- `__DatabaseVersion` table shape and row.

Sort by semantic keys. Exclude:

- raw normalized `sqlite_master.sql`;
- provider/internal index names from name equality;
- formatting/order details without semantic meaning.

Do not drop the semantics of a provider-generated auto-index. Normalize such
an index to a synthetic key containing its table, uniqueness, origin, and
ordered indexed columns. Fresh and migrated schemas must still match that
index's existence and behavior even though their internal names are ignored.

Normalize only the semantic `WHERE` predicate of partial indexes from their
`sqlite_master` entry; do not compare the complete raw DDL. This preserves
coverage of the current performance-history partial index. Include
`index_xinfo` so binary/path collation cannot drift while `index_info` still
looks identical.

For the `__DatabaseVersion` row compare `Feature` and `Version`. Assert that
`AppliedAt` is present and parseable but exclude its wall-clock value from
equality because the existing path writes `datetime('now')`.

- [ ] **Step 7.2: Create a fully migrated synthetic legacy fixture**

The fixture must enter through the real existing-database initialization path
and exercise every current migration family:

- an already-valid `UnicodeCollation` version 2 marker that the existing path
  updates without triggering recreation;
- bookmark;
- NX import columns;
- performance-history score scope;
- play-speed score scope;
- score-save receipts;
- any current schema repair invoked by `ConfigureUtf8EncodingAsync`.

Do not manufacture the final schema directly.

A fixture with a missing or old Unicode marker is deleted by
`HasProperUnicodeConfigurationAsync` before additive migrations and therefore
becomes a fresh recovery. Keep those recreation cases in Task 4; do not use
them as the Task 7 migrated-existing oracle.

- [ ] **Step 7.3: Write and run the structural differential**

Compare:

1. brand-new database through fresh bootstrap;
2. legacy fixture through the full existing migration path.

Expected: normalized semantic snapshots are identical.

Keep a separate exact assertion that EF-created DDL declares AUTOINCREMENT for
Songs, SongCharts, and SongScores.

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongDatabaseSchemaContractTests"
```

- [ ] **Step 7.4: Run all song/import/migration/preservation tests**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~SongDatabase|FullyQualifiedName~SongManager|FullyQualifiedName~SongPathIdentity|FullyQualifiedName~FreshImport|FullyQualifiedName~FreshSqlite|FullyQualifiedName~PlaybackSpeed|FullyQualifiedName~PerformanceHistory|FullyQualifiedName~ScoreSaveReceipt|FullyQualifiedName~Bookmark"
```

Expected: all selected tests pass.

- [ ] **Step 7.5: Run all lifecycle/protocol tests**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter \
  "FullyQualifiedName~Startup|FullyQualifiedName~BaseGame|FullyQualifiedName~GameApi|FullyQualifiedName~JsonRpc"

rtk dotnet test MCP.Test/MCP.Test.csproj
```

Expected: all locally executable selected tests pass. Separately require the
Windows/CI `Category=E2E-Support` command from Task 3 to pass. If no authorized
Windows runner is available, stop before final verification rather than
presenting a macOS-incompatible command as evidence.

- [ ] **Step 7.6: Run the complete macOS-safe suite and builds**

```bash
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj

rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj \
  --configuration Release

rtk dotnet build MCP/MCP.csproj \
  --configuration Release

rtk dotnet build MCP.Test/MCP.Test.csproj \
  --configuration Release
```

Expected: zero failures and zero build errors. Record any pre-existing warnings
without broad cleanup.

- [ ] **Step 7.7: Run whole-branch specification review**

Give a fresh reviewer:

- the committed design;
- this plan;
- diff from `c8a3140d...`;
- Task 0 evidence;
- focused/full test output.

Require explicit review of:

- terminal-dominates;
- publish-after-commit;
- exactly-once launch ownership;
- shutdown timeout retention;
- readiness barrier and queue atomicity;
- fresh evidence;
- pre-mutation fallback versus post-mutation propagation;
- all persisted columns;
- rollback and sequence behavior;
- existing-database preservation.

Fix every verified finding and re-run affected suites.

- [ ] **Step 7.8: Run whole-branch code-quality review**

Require explicit review for:

- task/fault observation;
- lock/await boundaries;
- thread affinity;
- cancellation placement;
- resource ownership/disposal;
- SQLite command/parameter reuse;
- error sanitization;
- tests that could pass without exercising the intended branch.

Fix verified findings and re-run affected suites.

- [ ] **Step 7.9: Verify no contract markers or legacy launchers remain**

```bash
rtk rg -n \
  "_cancellationTokenSource|_currentAsyncTask|_operationPerformedForPhase|ForceEnumeration|InitializeDatabaseServiceAsync|RunSongLoadAsync" \
  DTXMania.Game/Lib/Stage/StartupStage.cs

rtk rg -n \
  "HPA192_TIMING|HPA192_STARTUP|HPA192_SHUTDOWN|ExternalStageChangesReady|StartupNotReady|ShuttingDown|fresh_sqlite" \
  DTXMania.Game DTXMania.Test DTXMania.E2E MCP MCP.Test tools/hpa192

rtk git diff --check
```

Expected:

- first search has no matches;
- second search shows the intended source/tests;
- diff check passes.

- [ ] **Step 7.10: Commit final verification additions**

```bash
rtk git add \
  DTXMania.Test/Song/SqliteSchemaSnapshot.cs \
  DTXMania.Test/Song/SongDatabaseSchemaContractTests.cs
rtk git add -u
rtk git commit -m "test: verify HPA-192 second-wave contracts"
```

Inspect the staged diff before committing; include review fixes but no
benchmark output or local corpus files.

---

### Task 8: Run the Balanced Final Benchmark and Record Acceptance

**Files:**

- Create: `tools/hpa192/run-balanced-benchmark.sh`
- Create: `tools/hpa192/test-balanced-benchmark.sh`
- Modify: `tools/hpa192/benchmark-startup.sh`
- Modify: `tools/hpa192/test-timing-preflight.sh` if shared runner validation
  needs final-mode cases
- Modify: `docs/performance/HPA-192-startup-benchmark.md`

**Produces:**

- One pinned single-run validator and one pinned balanced orchestrator.
- Fixed original-baseline and wave-two Release outputs.
- Predetermined balanced six-run evidence.
- Final pass/fail report with no gate changes.

- [ ] **Step 8.1: Add final runner validation flags test-first**

Retain Task 0 timing mode and add independent final-mode controls:

```text
HPA192_REQUIRE_TIMING=0|1
HPA192_REQUIRE_EXTERNAL_READY=0|1
HPA192_EXPECT_PERSISTENCE_PATH=ef|fresh_sqlite|empty
HPA192_EXPECT_SONG_COUNT=integer|empty
```

Shell tests cover:

- baseline legacy mode can omit readiness and expects 24 songs;
- wave-two mode requires readiness true before Title is accepted;
- wave-two mode requires `persistence_path=fresh_sqlite`;
- wave-two mode requires 27 songs;
- both require 100 exact chart paths and one startup summary;
- a missing/duplicate summary, wrong path/count, false readiness, or path
  mismatch fails the sample;
- a failed sample cannot append an acceptance-order entry.
- the outer driver invokes exactly
  `baseline,wave2,wave2,baseline,baseline,wave2`;
- every invocation receives a distinct preallocated port and high-entropy
  launch token, including after an abandoned attempt;
- the attempt namespace must be new and empty;
- an invocation failure stops the loop before its order line and before any
  later sample;
- a restart requires a new attempt identifier and reruns all six samples.

`test-balanced-benchmark.sh` must use a stub runner so order/failure behavior
is deterministic and does not launch the game.

- [ ] **Step 8.2: Update final Title polling**

When `HPA192_REQUIRE_EXTERNAL_READY=1`, accept Title only when the JSON-RPC
state contains both:

```text
StageType == Title
telemetry.externalStageChangesReady == true
```

When disabled, preserve historical baseline compatibility.

Allow the outer driver to pass explicit `HPA192_API_PORT` and
`HPA192_LAUNCH_TOKEN` values. The single-run script records both in its result
metadata and refuses an invalid port or blank token. It may allocate its own
values only when those variables are absent for ad hoc diagnostics.

Implement `run-balanced-benchmark.sh` as the sole acceptance orchestrator. It:

1. receives fixed baseline and wave-two output directories, corpus, and a new
   attempt identifier;
2. rejects an existing attempt/result namespace;
3. allocates six distinct loopback ports before the loop;
4. generates six distinct tokens containing the attempt identifier, order,
   arm, run, and random suffix;
5. invokes `benchmark-startup.sh` with arm-specific validation flags;
6. appends one order record only after a successful invocation;
7. verifies the final order, port set, token set, and artifact set.

- [ ] **Step 8.3: Verify runner syntax and tests**

```bash
rtk bash -n tools/hpa192/benchmark-startup.sh
rtk bash -n tools/hpa192/summarize-timing-preflight.sh
rtk bash -n tools/hpa192/test-timing-preflight.sh
rtk bash -n tools/hpa192/run-balanced-benchmark.sh
rtk bash -n tools/hpa192/test-balanced-benchmark.sh
rtk bash tools/hpa192/test-timing-preflight.sh
rtk bash tools/hpa192/test-balanced-benchmark.sh
```

Expected: all pass.

- [ ] **Step 8.4: Fix and pin the final product and runner**

Before measuring:

```bash
rtk git status --short
rtk git diff --check
ALSOFT_DRIVERS=null \
  rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
rtk dotnet test MCP.Test/MCP.Test.csproj
```

Also require a green Windows/CI `Category=E2E-Support` result for the fixed
product commit before beginning acceptance measurements.

Commit the runner before building:

```bash
rtk git add \
  tools/hpa192/benchmark-startup.sh \
  tools/hpa192/run-balanced-benchmark.sh \
  tools/hpa192/test-timing-preflight.sh \
  tools/hpa192/test-balanced-benchmark.sh
rtk git commit -m "test: harden HPA-192 final benchmark"
```

Then record:

- original baseline commit;
- final wave-two product commit;
- runner commit;
- separate SHA-256 values for `benchmark-startup.sh` and
  `run-balanced-benchmark.sh`;
- both DLL SHA-256 values;
- manifest SHA-256;
- SDK/runtime, OS, and machine.

- [ ] **Step 8.5: Build both fixed Release outputs once**

Create detached worktrees for:

- `5ea3f95d208ba7b15019429f63d7edd0bbf7009d`;
- the final wave-two product commit.

Build each once to a clean, immutable output directory. Do not rebuild between
samples.

Verify both runner-script hashes and both output hashes immediately before the
first sample.

- [ ] **Step 8.6: Verify the frozen corpus**

Regenerate a temporary manifest from:

```text
/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles
```

Require:

- byte-for-byte equality with
  `docs/performance/HPA-192-corpus-manifest.tsv`;
- SHA-256
  `0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b`;
- 592 inventory rows;
- 100 supported chart files;
- 27 `SET.def` files.

- [ ] **Step 8.7: Run the predetermined balanced order**

Use a clean result namespace and this exact order:

```text
baseline
wave2
wave2
baseline
baseline
wave2
```

Invoke only the committed outer driver for acceptance:

```bash
rtk bash tools/hpa192/run-balanced-benchmark.sh \
  TestResults/hpa-192/builds/baseline-wave2-final \
  TestResults/hpa-192/builds/wave2-final \
  "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" \
  acceptance-attempt-001
```

If an attempt must restart, increment/use a new attempt identifier and leave
the old namespace untouched.

For baseline invocations:

```text
HPA192_REQUIRE_TIMING=0
HPA192_REQUIRE_EXTERNAL_READY=0
HPA192_EXPECT_PERSISTENCE_PATH=
HPA192_EXPECT_SONG_COUNT=24
```

For wave-two invocations:

```text
HPA192_REQUIRE_TIMING=0
HPA192_REQUIRE_EXTERNAL_READY=1
HPA192_EXPECT_PERSISTENCE_PATH=fresh_sqlite
HPA192_EXPECT_SONG_COUNT=27
```

The outer driver must set all four controls explicitly for every invocation;
it must not inherit an ambient Task 0 timing mode from the caller.

Every invocation uses:

- fresh app-data/database root;
- distinct loopback port;
- distinct launch token;
- fixed corpus;
- fixed output;
- pinned runner;
- outer-driver-assigned distinct port and token;
- exact chart-path validation;
- exactly one summary.

The outer driver is fail-fast and appends the order line only after a sample
passes all validation.

- [ ] **Step 8.8: Handle invalid samples without selection bias**

If any accepted attempt fails:

- retain it as diagnostic evidence;
- do not overwrite it;
- do not resume the partially completed order;
- create a new clean namespace;
- rerun the entire predetermined sequence.

Do not drop a slow valid sample.

- [ ] **Step 8.9: Calculate and record the result**

Report:

```text
baseline_median_ms
wave2_median_ms
improvement_percent =
    100 * (baseline_median_ms - wave2_median_ms) / baseline_median_ms
```

Here `baseline_median_ms` is the median of the three accepted samples from the
fixed original-baseline commit in this balanced sequence. Retain the
historical 7,402 ms median in the report for continuity; do not silently
substitute it for missing current baseline samples.

Acceptance is:

```text
wave2_median_ms <= 2221
improvement_percent >= 70
```

The report must include:

- commits and hashes;
- separate pinned hashes for `benchmark-startup.sh` and
  `run-balanced-benchmark.sh`;
- environment;
- corpus verification;
- runner behavior and flags;
- exact balanced order;
- all six raw wall times;
- all six summary lines;
- database/chart counts;
- chart-path hashes;
- medians and calculation;
- Task 0 diagnostics clearly separated from acceptance;
- explicit PASS or FAIL.

- [ ] **Step 8.10: Respect the final hard stop**

If PASS:

- record the accepted result;
- do not add unrelated optimization.

If FAIL:

- record the failed result;
- stop;
- do not change the 70-percent or 2,221 ms gates;
- do not add ReadyToRun, AOT, packaging, parser parallelism, or other
  unreviewed work;
- require a new measurement-based diagnosis and reviewed design.

- [ ] **Step 8.11: Verify and commit the benchmark report**

```bash
rtk git diff --check
rtk rg -n \
  "baseline_median|wave2_median|improvement|PASS|FAIL|fresh_sqlite|balanced" \
  docs/performance/HPA-192-startup-benchmark.md
rtk git status --short
rtk git add \
  docs/performance/HPA-192-startup-benchmark.md
rtk git commit -m "docs: record HPA-192 second-wave benchmark"
```

Do not stage `TestResults`, build outputs, detached worktrees, or corpus files.

---

## Final Handoff Checklist

- [ ] Task 0 fixed-floor decision is committed and permits implementation.
- [ ] Every product task has RED/GREEN evidence and two review passes.
- [ ] Coordinator is exactly-once and BaseGame-owned.
- [ ] StartupStage contains no load launcher or load-owning CTS.
- [ ] Startup milestone drain is bounded and terminal-dominates.
- [ ] One Startup frame, summary, and internal Title request are preserved.
- [ ] Shutdown timeout retains worker dependencies and observes eventual faults.
- [ ] External readiness is launch-monotonic and queue admission is atomic.
- [ ] JSON-RPC and MCP preserve both startup-not-ready and shutting-down
  code/reason pairs.
- [ ] Freshness proof is service-owned and legacy probes are skipped only when
  safely fresh.
- [ ] Fresh plan is shared by EF and direct paths.
- [ ] Direct writer guards run before mutation and post-mutation failures never
  retry through EF.
- [ ] All columns, relationships, rollback, and sequences are verified.
- [ ] Fresh and fully migrated schemas are semantically equivalent.
- [ ] Existing-database migration and user-data preservation suites pass.
- [ ] Full macOS-safe suite, MCP tests, E2E support, and Release builds pass.
- [ ] Whole-branch specification and quality reviews are clean.
- [ ] Final benchmark uses fixed outputs, both pinned runner scripts, the
  frozen corpus, and the predetermined balanced order.
- [ ] Final report records PASS or FAIL without changing the gate.
