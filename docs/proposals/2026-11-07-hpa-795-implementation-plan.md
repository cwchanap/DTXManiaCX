# HPA-795 — Implementation plan (reviewed)

**Design:** [HPA-795 revised design](./2026-11-07-hpa-795-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-795/cleanup-delete-dead-code-legacy-shims-the-startup-tracer-and-db  
**Delivery:** **One implementation PR (this draft)**; budget <=3 engineer days.  
**Status:** Plan only; do not claim production behavior changed or CI passed.

## Agent instructions

Implement the approved design on the existing draft PR; read the current repository first. Prefer deleting redundant paths over adding wrappers/architecture. Keep each section as a coherent, green commit on **the same branch**. The song DB policy must land **before** removal of stored-rank conversion. Do not remove NX score.ini import mapping.

**Required invariants:** fresh/repeated startup; cached song library; Japanese filenames/titles; normal chart and NX import; song selection, playback and results; Config.ini import; crash report startup milestones and Game API. Config SQLite storage is out of scope.

## Section 1 — Remove startup profiler, retain crash evidence

**Review these files:**
- `DTXMania.Game/Program.cs` and `Game1.cs`
- `Lib/Stage/{StartupCriticalPathTrace,StartupCriticalPathHost,StartupTimingTrace,StartupStage,TitleStage,BaseStage,StageManager,StartupSongLoadSummary}.cs`
- `Lib/Song/StartupSongLoadTimingObserver.cs`, `SongImportModels.cs`, `SongManager.cs`; `Lib/Song/Entities/SongDatabaseService.cs`
- `Lib/Diagnostics/CrashReporting/{CrashContextPublisher,CrashLogFieldPolicy,CrashReportStore}.cs`
- `tools/hpa192/`; corresponding tracer/summary tests and startup/crash tests.

1. **Move the surviving type dependency first.** Introduce a small closed crash-startup milestone enum in `Lib/Diagnostics/CrashReporting/` with just `StartupActivation`, `StartupFirstDrawEnd`, `SummaryRequest`. Update Game1, CrashContextPublisher, CrashLogFieldPolicy, CrashReportStore and affected tests; retain `Enum.IsDefined` and report `Unknown` for unknown/obsolete milestones. Do **not** retain the tracer's broad enum or replace with arbitrary strings.
2. Remove the profiler graph and its observer parameters/overloads: StartupCriticalPathTrace, StartupCriticalPathHost, StartupTimingTrace, StartupSongLoadTimingObserver, Program/Game1 tracer construction, stage/manager/database spans, diagnostic env vars and exit logic, and `tools/hpa192/`.
3. Remove `StartupSongLoadSummary` and `HPA192_STARTUP`, its test and reporting-only state. Remove the four timing `TimeSpan` fields in SongImportModels and the associated stopwatches/data plumbing; **keep** counters, actual bulk-import results and other production behavior. If `StartupStage.WriteSummaryOnce` exists only for profiling, remove it and its invocation; no replacement timing logger is required.
4. Delete tests dedicated to the removed profiler/observer contracts and adapt functional startup, song enumeration, cancellation and crash tests to the current constructor paths. Keep crash breadcrumbs, `CrashContextPublisher`, normal startup/Title transition, Game API action queue and screenshots.
5. Search **active** game code, test code and tools for `HPA192` (bare token), `StartupTimingTrace`, `StartupCriticalPathTrace`, `IStartupSongLoadTimingObserver` and `StartupSongLoadSummary`. Fix remaining live references. Historical docs are out of scope.

**Evidence:** Compile local applicable project(s), run focused startup/crash/song tests. Trigger Windows + macOS CI on the draft branch (see verification section). Commit only after green.

## Section 2 — One song DB version and reset path

**Primary files:**
- `Lib/Song/Entities/SongDatabaseService.cs`
- `Lib/Song/Entities/SongDbContext.cs` (current model reference)
- `Lib/Song/SongManager.cs`
- `DTXMania.Test/Song/{SongDatabaseServiceTests,SongManagerTests,SongDatabaseServiceCoverageTests}.cs` and any old migration tests.
- **Reference existing config DB pattern:** `Lib/Config/ConfigDbSchema.cs` uses `PRAGMA user_version`, but do **not** change it.

1. Replace the old `HasProperUnicodeConfigurationAsync`/`__DatabaseVersion` probe with a **single version probe** in SongDatabaseService: `CurrentSongSchemaVersion = 1`; open a short SQLite connection and read `PRAGMA user_version`. Missing file means fresh creation; invalid SQLite (`SqliteException`) counts as mismatch. A mismatch including old version 0 calls the **existing throwing** `PurgeDatabaseAsync` exactly once under the initialization semaphore; explicit user-requested purge remains supported.
2. Collapse all competing reset paths: remove `IsValidSqliteDatabaseAsync`, `HandleInvalidDatabaseFileAsync` (including `Task.Delay(100)`), `HasProperUnicodeConfigurationAsync`, `EnsureDatabaseVersionTableAsync`, exception-message catch/retry branches (including `table already exists` success), and SongManager's pre-initialization `IsDatabaseCorruptedAsync` probe/branch. Preserve the public `purgeDatabaseFirst` behavior without probing twice or adding another delete helper.
3. Use the existing EF model to `EnsureCreatedAsync`, then `EnsureEnumerationMetadataTableAsync` (make its **creation** failure throw), then set `PRAGMA user_version = CurrentSongSchemaVersion`, and finally `_isInitialized = true`. A partial create with version 0 will self-reset next run; a matching-version DB is reopened without a rebuild.
4. Delete `ConfigureUtf8EncodingAsync` and both startup PRAGMAs (`journal_mode=DELETE`, `case_sensitive_like=OFF`), the additive `Ensure*Column` routines, playback/history/receipt schema migrations, and their obsolete tests. Keep the current entity model, runtime `__EnumerationMetadata` read/write, receipt CRUD and Japanese text semantics. The in-memory test constructor stays separate from file-version checks.
5. Add focused tests:
   - Fresh DB: all required EF tables and enumeration metadata, user_version 1, Japanese title/file roundtrip.
   - Reopen valid version: scores, ranks, bookmarks, play counts, history and enumeration cache state retained.
   - Version 0/mismatch/invalid file: one purge/recreate and re-enumeration; no stale schema survives.
   - Failed delete and failed metadata initialization: error propagates; no initialized/stamped success.
   - Explicit purge still works; test injected in-memory context still works; config SQLite remains unaffected.
   - **Version-bump guard:** pin expected `CurrentSongSchemaVersion` and a SHA-256 of deterministic `SongDbContext.Database.GenerateCreateScript()` plus the `__EnumerationMetadata` DDL in a test. On schema drift, failure should instruct: “schema changed → bump CurrentSongSchemaVersion and update hash”. No migration registry or runtime hashing.
6. When revising tests that built legacy schema fixtures, replace with current-schema fixtures or explicit version-0 reset expectations rather than preserving old migration behavior.

**Evidence:** Run focused DB/cache/NX-import tests, then manually dispatch CI on the draft branch. Commit green schema changes **before** moving to stored-rank cleanup.

## Section 3 — Delete dead app shims after schema reset is in place

**Primary files:**
- `Lib/UI/Components/{UIButton,UIList,UIImage}.cs`; `Lib/UI/Layout/PerformanceUILayout.cs`
- `Lib/Song/Entities/{SongChart,SongScore}.cs`
- `Lib/Stage/PerformanceStage.cs`, `Lib/Song/SongManager.cs`
- `Lib/Config/{ConfigData,ConfigManager}.cs`
- `Lib/Song/{NxScoreImporter,Filtering/SongListFilterService}.cs`
- `Lib/Song/Components/{SongStatusPanel,SongBarRenderer}.cs`
- `CLAUDE.md`, `AGENTS.md`, relevant tests in `DTXMania.Test` and `DTXMania.E2E`.

1. Audit actual production uses before deletion. Remove the three test-only UI components, unused `PerformanceUILayout` legacy aliases/layout groups, unused entity compatibility properties, PerformanceStage old-callers/reflection fallback and SongManager parse-only reflection helper where production has no dependency. Delete their now-obsolete tests, but do not remove tests for surviving behavior.
2. **Stored rank conversion has live callers; it is only obsolete after Section 2.** Find **all** `NormalizeStoredBestRank` usages, including five external files and **SongScore's own `RankName` and `UpdatePerformance`**; replace with `NormalizeRankPercentage`. Remove the ordinal-conversion helpers and obsolete legacy rank tests. Preserve `NxScoreImporter.MapNxRankToBucket` and its `.score.ini` tests: importing NX ranks is **format support, not DB-app compatibility**.
3. Remove `ConfigData.DTXPath` property/mirror, mirror writes and redundant sync tests. Retain the **`DTXPath=` Config.ini input key** from DTXMania/NX, which the existing `ConfigManager` local variable and `FinalizeSongRoots` already map to `SongRoots`. Preserve `SongRoot.N` precedence. Update tests and fixtures that *read* `Config.DTXPath` (notably `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`) to read `SongRoots[0]`; do not delete Config.ini input fixtures.
4. Correct **both occurrences** of the deleted component list in **each** `CLAUDE.md` and `AGENTS.md`. Leave unrelated old design docs for HPA-798.
5. Grep for removed symbols in production/tests and build both platform targets. Confirm both Windows-only `SongStatusPanelLogicTests` and E2E fixture tests compile (the former is excluded from Test.Mac). No other task's refactor belongs here.

**Evidence:** Run config/rank/UI/song-manager/performance tests and manually dispatch CI. Commit this section green.

## Section 4 — Final verification in the **same PR**

On the working branch (do not mark Ready for Review just to get CI), run:

```sh
# Existing workflow supports workflow_dispatch; draft-only skip applies to pull_request.
gh workflow run build-and-test.yml --ref plan/hpa-795-remove-legacy-infrastructure
```

Run that **after each completed section**, inspect both Windows and macOS jobs and fix failures before moving on. Use one coherent green commit per section for easier review/bisect; avoid scattering the task across multiple PRs. CI jobs from `workflow_dispatch` may require checking their completion separately. Follow `.github/workflows/build-and-test.yml` for current exact SDK/project/coverage commands.

Final smoke checks: fresh title, cached song library, selecting and playing a song in lanes/tab, score/result persistence on a same-version restart, version-0 reset, imported `DTXPath=` Config.ini, Japanese title, NX score.ini, Game API screenshot and crash report startup milestones. Don't invent a new E2E framework.

Before marking ready:
- [ ] No live `HPA192` instrumented code, redundant song-DB repair paths, deleted compatibility symbols, or broken active agent component lists.
- [ ] Startup crash milestone sanitizer remains strict (`Enum.IsDefined`).
- [ ] Japanese text and NX input support persist; matching-version scores/bookmarks persist.
- [ ] CI evidence attached/referenced in PR body, including Windows-only tests and the accepted **songs.db schema-mismatch reset** of scores/history/bookmarks.
- [ ] No implementation scope leaked into HPA-796/797/798/804.

## Three-day guardrail

Target: tracer + crash enum (0.75 day), DB consolidation + tests (1 day), dead shims/ranks/config + docs (0.75 day), final CI/smoke checks (0.5 day). If the work exceeds three engineer days, report concrete blockers and get scope direction rather than silently starting another PR.

**Do not claim green CI until the actual dispatch runs finish.**