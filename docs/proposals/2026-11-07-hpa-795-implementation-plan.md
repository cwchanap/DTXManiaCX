# HPA-795 — Implementation plan

**Design:** [HPA-795 design](./2026-11-07-hpa-795-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-795/cleanup-delete-dead-code-legacy-shims-the-startup-tracer-and-db  
**Delivery:** Add implementation commits to **this same draft PR**. No parallel PRs or new tickets. **Budget: <=3 engineer days total.**  
**Current state:** Planning only; no game code has been changed in this PR yet.

## Agent instructions

Implement the approved design using the code currently on main as the source of truth. Optimize for removal of dead code and small, maintainable changes. Do not add new architecture for a feature being deleted. Build incrementally and finish with a clean Windows/macOS CI result before switching the PR out of Draft.

**Treat these as invariants:**
- The game still starts and plays songs; Japanese DTX/NX/SET.def import still works.
- Config.ini DTXPath still **loads** as an input alias, even though ConfigData.DTXPath mirror is removed.
- CrashReportRuntime, CrashContextPublisher and the Game API/screenshot paths continue to work.
- Only **songs.db** may be deleted for a schema-version mismatch; config storage is not affected.
- Existing, same-version song scores/bookmarks survive a restart.

## 1. Inventory real call sites; remove dead app shims

**Primary files**
- DTXMania.Game/Lib/UI/Components/{UIButton,UIList,UIImage}.cs
- DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs
- DTXMania.Game/Lib/Song/Entities/{SongChart,SongScore}.cs
- DTXMania.Game/Lib/Stage/PerformanceStage.cs
- DTXMania.Game/Lib/Song/SongManager.cs
- DTXMania.Game/Lib/Config/{ConfigData,ConfigManager}.cs
- Tests under DTXMania.Test/UI, /Song, /Stage and /Config.

**Work**
1. Search the repository for every candidate member's uses **outside tests**. Delete only unused UI components/layout aliases and properties that serve old application callers. Remove their tests and test-only reflection hooks; retain tests of the current behavior.
2. Remove old persisted-rank ordinal conversion after confirming it is *only* app-version DB compatibility; keep NX score.ini import and canonical rank calculations.
3. Replace the synchronized ConfigData.DTXPath mirror with SongRoots. Keep the Config.ini DTXPath **parser** as an alias that assigns the first root, preserve SongRoot.N precedence, and update recorder/E2E production usage to the canonical SongRoots APIs rather than removing the supported INI key.
4. Remove the obsolete PerformanceStage legacy initialization/parse fallback only after tracing normal OnActivate / prepared-chart paths and their tests. Remove SongManager's parse-only helper that exists only for reflection tests; leave real song enumeration unchanged.
5. Build the game and run focused config/song/stage tests before continuing.

**Tests to maintain/adjust**
- DTXMania.Test/Config/SongRootConfigTests.cs and config persistence tests: indexed roots vs imported DTXPath, no in-memory mirror.
- Song manager and performance stage normal activation tests: current path works without reflection compatibility branches.
- Remove UIButton/UIList/UIImage-specific tests only when their production types are deleted; do not delete shared UIElement/UIContainer tests.

**Checkpoint:** No live production call site remains for deleted properties/classes and normal startup, song selection, and gameplay still compile.

## 2. Excise HPA-192 startup tracing end-to-end

**Primary files**
- DTXMania.Game/Program.cs and Game1.cs
- DTXMania.Game/Lib/Stage/{StartupCriticalPathTrace,StartupCriticalPathHost,StartupTimingTrace,StartupStage,TitleStage,BaseStage,StageManager}.cs
- DTXMania.Game/Lib/Song/StartupSongLoadTimingObserver.cs
- DTXMania.Game/Lib/Song/SongManager.cs
- DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs
- tools/hpa192/ and profiler-specific DTXMania.Test files.

**Work**
1. Remove the four tracing/observer implementation files and the HPA192_* environment-variable and diagnostic-exit behavior.
2. Delete tracing fields, constructor parameters, spans, milestone calls, timing observer overloads and safe-emit scaffolding across the game. Keep one direct implementation path per startup/song-loading operation; do **not** retain forwarding overloads merely for tests.
3. Change Program/Game1 wiring to pass only what the game actually uses (notably crash diagnostics). Preserve CrashContextPublisher milestones/breadcrumbs, normal lifecycle, main-thread queue and screenshot completion semantics.
4. Delete tools/hpa192 and tests that exclusively assert profiler output, timestamps, observer contracts and diagnostic exit. Update tests that currently inject StartupTimingTrace/RecordingObserver to exercise the remaining behavior without a tracer. Retain functional startup, song enumeration, cancellation and crash-report tests.
5. Search game code, tests and active tools for StartupCriticalPathTrace, StartupTimingTrace, IStartupSongLoadTimingObserver, HPA192_CRITICAL_PATH, HPA192_EXIT_AFTER_CRITICAL_PATH and HPA192_TIMING. There should be no active matches. Historical documents are HPA-798's responsibility.

**Checkpoint:** There is no profiler-specific startup path. Title transition, crash breadcrumbs and Game API health/screenshots still work.

## 3. Simplify song database bootstrap to one version marker

**Primary files**
- DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs
- DTXMania.Game/Lib/Song/Entities/SongDbContext.cs (read to confirm current model; avoid needless model changes)
- DTXMania.Test/Song/SongDatabaseServiceTests.cs and related schema/migration test files.

**Work**
1. Introduce a single CurrentSongSchemaVersion constant (starting at 1) and a narrow file-backed probe for PRAGMA user_version.
2. Before EF initialization, if songs.db is present and unreadable or has any different version, clear SQLite pools and recreate that **file only**. Reuse existing initialization concurrency semantics. Make reset failures fail initialization; do not copy the existing best-effort delete catch into the schema-mismatch path.
3. For new files, call EF EnsureCreatedAsync using the existing model. Preserve the needed PRAGMAs and initialization of __EnumerationMetadata. Write user_version **only after** everything needed for a usable fresh schema succeeds, then set _isInitialized.
4. For matching versions, do not rerun per-column upgrades. Remove the __DatabaseVersion/UnicodeCollation marker check, EnsureDatabaseVersionTableAsync, EnsureBookmarkColumnAsync, EnsureNxImportColumnsAsync, EnsurePerformanceHistoryScoreScopeAsync, EnsurePlaybackSpeedScoreScopeAsync, EnsureMigrationColumnAsync, EnsureScoreSaveReceiptsAsync, and any other code exclusively used by old-schema upgrades. Keep score-save receipt **data operations** and __EnumerationMetadata **runtime** operations.
5. Keep test-injected/in-memory DbContext constructors usable without file version probing. Delete or replace legacy schema-upgrade tests; do not discard assertions that cover current schema, persistence or failed initialization.
6. Do not touch SqliteConfigStore migrations or configuration database lifecycle.

**Required focused regression tests**
- Fresh database: creates current SongDbContext tables, __EnumerationMetadata and user_version=1; Japanese titles persist/reload.
- Reopen matching version: songs, scores, play counts, bookmarks and enumeration metadata are retained.
- Existing unversioned (0), older, or other-version DB: resets once, creates a valid new schema, and normal library enumeration repopulates it.
- Invalid/corrupt song DB recovers; reset deletion failure surfaces an error and never marks the service initialized.
- In-memory test constructor still works; separate config SQLite data is untouched.

**Checkpoint:** No schema migration SQL is executed on a normal startup; fresh and reused databases work.

## 4. Verify and document results (same PR)

1. Build both platforms with the repository's current SDK and project files:
   - Windows: dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -c Debug
   - macOS: dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
2. Run impacted unit suites, then the normal platform test suite (DTXMania.Test.csproj on Windows / DTXMania.Test.Mac.csproj on macOS). Run Automation.Tests and VideoRecorder.Tests if shared constructor/code changes affect them. Follow .github/workflows/build-and-test.yml for exact CI commands.
3. Smoke-check fresh startup, cached library restart, choosing a song, lanes gameplay, exiting to title, Japanese song titles, imported legacy Config.ini folder selection, and Game API screenshot/crash-report lifecycle as available in existing E2E tests. Do not introduce a new E2E framework.
4. Review the diff for accidental removal of format compatibility, used layout primitives, current score logic or unrelated cleanup. Any test deleted should have a clear production-deletion reason.
5. In the PR body, report test commands/results and the **intentional score/history/bookmark reset on song-schema version mismatch**. Draft PRs currently skip CI via workflow job conditions; after implementation and review, mark Ready for Review to run required CI. Do not claim CI success based on this documentation-only draft.

## Scope/time guardrail

Approximate work allocation: reference audit + dead shims (0.75 day); tracer removal (0.75 day); schema bootstrap + regressions (1 day); verification/fixes (0.5 day). If actual work cannot fit 3 engineer days, stop and surface concrete remaining work rather than broadening this PR or silently opening another PR.

**Completion:** All design acceptance contracts pass, Linear HPA-795 points to this PR, and the implementation lands as a single reviewed PR.