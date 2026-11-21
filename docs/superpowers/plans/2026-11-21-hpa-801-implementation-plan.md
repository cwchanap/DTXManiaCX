# HPA-801 — Revised implementation plan (one PR, <=3 engineer days)

**Design:** [reviewed HPA-801 design](../../proposals/2026-11-21-hpa-801-design.md)  
**Reviews:** [first](https://github.com/cwchanap/DTXManiaCX/pull/252#pullrequestreview-6007914307) · [second](https://github.com/cwchanap/DTXManiaCX/pull/252#pullrequestreview-6008285041)  
**Linear:** https://linear.app/cwchanap/issue/HPA-801  
**Work on this existing draft:** `plan/hpa-801-song-library-responsibilities` / PR #252. **Do not create a second implementation PR.**  
**Baseline:** `main` `5e8b47e311badceafc8942b804a255f0527f9463`, 2026-11-21. Recheck latest main when implementation starts.  
**Status:** docs-only review revision; no code implementation, test run, CI E2E or screenshot verification has been performed for this revision.

## Non-negotiable agent contract

Mechanical responsibility split only. Preserve current playable chart hierarchy, SQL schema, configuration, level mapping, input, audio/scoring semantics, scan error/cancellation behavior and rendered UI. **No DI framework, generic repository hierarchy, compatibility facade, new service interfaces, second update-thread queue, new image/SFX, or UI redesign.** Max budget is **3 engineer days** including test migration, CI E2E and inspecting the CI's already-generated screenshot tour (no additional baseline run).

Keep every full relevant suite green **after each extraction**, not just after the final stage rewiring. Use *behavioral* tests; do not translate assertions of old private fields or retain dead production members solely for tests. The existing constructor seam matrix in the design is the migration map. **Busy/cancel, incomplete batch, post-commit finalization/publication, score idempotency and coherent snapshots are essential regressions to migrate—not tests to delete.**

## Step 0 — baseline and production-call audit (0.10 day)

- [ ] Check PR base and current `main`, find all production/test references to `SongManager`, `SongDatabaseService`, `IStageGame` and existing production stage consumers.
- [ ] Record baseline `dotnet test DTXMania.Test/DTXMania.Test.csproj` outcome and existing `gameplay-e2e-windows` run/artifact availability. Do not call an unrun test passing.
- [ ] Verify suspected unused public APIs are genuinely absent in production, including reflection/dynamic paths; only then proceed with deletion. Inventory live `NeedsEnumerationAsync` connectivity/count checks, `RefreshSongListFromDatabaseAsync` after NX import, existing injected DB context-count/DDL-failure/identity-filter tests, and `IStageGame` default-member/uninitialized-stage stubs.

## Step 1 — delete dead surface before moving classes (0.25 day)

- [ ] Remove verified test-only methods/events and obsolete wrappers listed in Design Decision 0 (including `SongDiscovered`/`EnumerationCompleted`, search/genre/top-score dead paths, old boolean score save, obsolete DB cleanup, `SongListNode.PopulateScoresFromDatabase` and singleton-only helpers). Remove their test-only expectations and DB seeding method in favor of fixture SQL/EF setup.
- [ ] Remove `SetCurrentSearchPaths` and its test-only pending-root behavior; consolidate `_pendingSearchPaths` into the one published active-roots field after verifying live paths. **Keep** `RefreshSongListFromDatabaseAsync` for Config NX-import, now operating on published active roots, and keep `NeedsEnumerationAsync`'s live connectivity/has-songs decision via narrow store helpers rather than deleting it with the old aggregate stats method.
- [ ] Preserve `SongLibraryPublished` and event error isolation. Run focused tests; skip any deletion if the audit finds a production use and note the exception in the PR.
- [ ] **Exit:** no tests are broken by intentionally removed live behavior; no dead methods will be carried into the extracted services.

## Step 2 — remove the singleton now; wire non-null DB and stage access (0.55 day)

- [ ] Rename `SongManager` → `SongLibrary` **before** extracting subordinate components. Replace `Instance`/`ResetInstanceForTesting`/`Clear` and the global serialized `SongManager` test collection. Keep existing code in the renamed class until its owner moves.
- [ ] Construct `SongDatabase(AppPaths.GetSongsDatabasePath())` and one `SongLibrary` with logger in **`BaseGame.CreateLoadContentServices` before `StageManager` creation**, not the `BaseGame` constructor. Move `Directory.CreateDirectory` from `SongDatabase` path constructor to `InitializeAsync()` to avoid unsandboxed host AppData writes in ordinary game construction. Dispose library/DB after `StageManager.Dispose()`. The database reference is non-null but uninitialized until Startup, and `CreateContext` keeps its initialized guard.
- [ ] Add default interface member **`SongLibrary? IStageGame.SongLibrary => null`** (actual interface syntax as appropriate), with a production `BaseGame` implementation returning the one game-owned instance. This protects unrelated `MinimalStageGameStub`, TitleStage/StageManager stubs and mocks. **Do not eagerly dereference `game.SongLibrary` in `ConfigStage` constructors**: capture `game` in NX-import/reload/refresh delegates and resolve on execution; `SongLibraryReloadService` must resolve it in `ReloadAsync` (while retaining its injected test delegate). Genuine library operations require a non-null instance; no silent fallback.
- [ ] Startup calls library/database `InitializeAsync`, not `GetSongsDatabasePath`/`InitializeDatabaseServiceAsync(path)`. Delete obsolete virtual path getter; update affected `StartupStageLogicTests.CreateStage` fixture (currently nulls `_songManager`) with explicit library injection for real operations; keep uninitialized unrelated stage fakes intact.
- [ ] Rewire Startup, Song Selection event/snapshot, lazy Config/reload/NX import + post-import `RefreshSongListFromDatabaseAsync`, and Result, plus logging and **only the affected** test fakes. Keep HPA-799 `BackgroundWork.Latest` on the Song Selection stage. **No temporarily forwarded SongManager or static locator**.
- [ ] Establish final optional constructor delegates (see design seam table) so tests can be migrated directly **once** to `SongLibrary` and collaborators rather than rewritten again after each extraction. Remove mutable per-test setter hooks when constructor seams are available.
- [ ] **Exit:** game compiles and boots, canonical tests pass, all `SongManager.Instance` and singleton collection references gone.

## Step 3 — database ownership and two stores (0.50 day)

- [ ] Narrow `SongDatabaseService` into `SongDatabase` with context options, lifecycle, schema/version and `CreateContext`. Put batch import/chart inventory/watermark/rejected counts in `SongCatalogStore`; score transactions, `RunId` receipts and history in `SongScoreStore`.
- [ ] Reuse the current EF queries, SQLite transactions and short-lived DbContexts; no new schema or migration. Stores share the **same non-null `SongDatabase`**. Delete the old monolith, not a forwarding wrapper.
- [ ] Remove old `GetDatabaseStatsCoreAsync`, but **retain essential DB test injection** via one internal `SongDatabase(DbContextOptions<SongDbContext> options, Func<SongDbContext>? contextFactory = null, bool initialized = true)` overload in place of the four existing overloads. Keep counting/throwing contexts for single-transaction import, failed-DDL initialized guard and worker-thread query tests. Move optional `Func<int, bool>? activeChartIdentityFilter` into `SongCatalogStore`'s constructor, preserving the case-collision/legacy-rewrite test. Tests continue using real temporary or in-memory SQLite rather than mock repositories.
- [ ] Replace stale aggregate `DatabaseStats` lookups in `NeedsEnumerationAsync` with `SongDatabase.CanConnectAsync()` + `SongCatalogStore.HasAnySongsAsync()`, preserving existing first-run, uninitialized/failed-connect and empty-library rescan behavior. Do not run five unrelated `CountAsync` queries to decide if any songs exist. Assert freshness tests directly.
- [ ] **Exit:** focused SQLite tests pass; DB file layout and observable data are unchanged.

## Step 4 — `SongDefinitionReader` (0.25 day)

- [ ] Construct **one** `SongDefinitionReader` under `SongLibrary` and pass it to **both** `SongLibraryScanner` and `SongHierarchyBuilder`. Move SET.def/box.def line reading, encoding fallbacks, normalization, parsing and metadata into it with optional constructor `ReadAllLines` delegate. No publication state or DbContext; no second reader/seam on warm path.
- [ ] Move the existing parser tests, including malformed/encoding cases. Preserve grouping, difficulty labels, skin/color and root titles.
- [ ] **Exit:** definition tests green without mutable SongLibrary parser hooks.

## Step 5 — `SongHierarchyBuilder` and cold/warm parity (0.40 day)

- [ ] Extract warm-start `BuildHierarchyFromCharts`, `EnsureFolderHierarchy`, preparation and pending-node finalization into `SongHierarchyBuilder`, using the **same** library-owned `SongDefinitionReader` as the cold scanner for box.def and SET labels. Keep cold-start tree built **during scan traversal**, not flattened and rebuilt.
- [ ] Centralize one BOX/root-node helper (can be internal/static on builder) called by cold and warm paths; match skin path, genre, text color, breadcrumb, root header, bundled root and root-title disambiguation.
- [ ] Extend existing `BuildSongListFromDatabase_ShouldMatchTheEnumeratedHierarchy` with `SkinPath`/colors, two configured song roots, bundled root, SET.def and nested BOX cases. Compare hierarchy, order, chart counts, labels and active roots after cold build versus DB reload.
- [ ] **Exit:** extended parity gate and other hierarchy tests pass.

## Step 6 — stateless scanner, `SongLibrary` scan transaction (0.40 day)

- [ ] Extract file traversal, chart inventory and `NeedsEnumerationAsync(activeRoots)` into `SongLibraryScanner`; inject `ParseSongEntities`, `EnumerateFiles` and `EnumerateDirectories` via its constructor, sharing the library's definition reader. The scanner does **not** own locks, cancellations, publication or score state. Its freshness checks use `SongDatabase.CanConnectAsync()` / `SongCatalogStore.HasAnySongsAsync()` in place of old aggregate DB stats.
- [ ] Keep `SongLibrary.ScanAndPublishAsync` as **the sole orchestration owner**: acquire single-flight slot, build batch, commit `SongCatalogStore` import, finalize via `SongHierarchyBuilder`, publish atomically, write metadata/watermark, release slot in `finally`. Preserve distinct `SongEnumerationPostCommitException` phases.
- [ ] Move `FindMovedFileAsync` to use explicitly passed canonical `activeRoots` instead of `_currentSearchPaths`/hard-coded defaults, retaining existing heuristic. Note the intentional configured-root search correction in PR and test moved chart→rescan.
- [ ] Migrate **all** meaningful injected blocked-import, competing scan, cancellation-winddown, incomplete batch, post-commit injection, bad root, rejected-tab and watermark tests to constructor seams on `SongLibrary`. Never delete these contract tests. Keep stage publication on HPA-799's update thread.
- [ ] **Exit:** scanner/snapshot/regression tests pass; no partial or stale publication.

## Step 7 — score and query services (0.30 day)

- [ ] Extract `SongScoreService` using `SongScoreStore` + library's locked in-memory refresh. Keep saved-score success only after matching chart/instrument/speed variant is refreshed, and idempotent `RunId` retry receipts. Preserve NX import behavior.
- [ ] Extract **one** `SongQueryService` handling live recent plays and bookmark read/toggle **with its SQL inline** against `SongDatabase`; avoid `SongQueryStore`. Preserve recency ordering, active-root filtering and bookmark nodes. Do not resurrect unused search/genre queries.
- [ ] Keep `SongLibrary.RefreshSongListFromDatabaseAsync` for Config's post-NX-import stage path: snapshot published active roots, rebuild via `SongHierarchyBuilder`, and publish a coherent new hierarchy so updated score badges appear without restart. Preserve its tests (`ConfigStageNxImportTests` and `SongManagerBulkEnumerationTests`). Switch other stage calls to `library.Scores` and `library.Queries`. Migrate affected behavioral tests; verify no old `SongManager`/`SongDatabaseService` references remain in production or tests.
- [ ] **Exit:** score and recent/bookmark tests green; Result and Song Selection semantics unchanged.

## Step 8 — CI E2E, MCP observation and existing screenshot check (0.25 day)

Primary end-to-end gate: existing Windows `gameplay-e2e-windows` job in `.github/workflows/build-and-test.yml`, which runs `dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"` against a real game process, then uploads the HPA-797 screenshot tour. **Use MCP to run/observe the relevant GitHub Actions execution, read result/logs, retrieve artifacts and evaluate acceptance**. Do not build another launcher or E2E framework.

- [ ] Confirm `GameplayAutoPlaySmokeTests.GameplaySmoke_ShouldPersistIndependentSpeedBucketsAndReuseBucketAcrossPitches` exercises three launches (cold + two warm) over one songs.db and persists independent speed buckets. Check actual run/assertions via CI logs; also rely on existing autoplay/Tab E2E and recent/bookmark/Config reload behavioral tests. Add an E2E case only for a verified uncovered blocker, not as a blanket new screenshot tour.
- [ ] **CI constraint:** `gameplay-e2e-windows` skips **draft** pull requests. For implementation acceptance, mark **this same PR** ready for review to get the normal PR CI run, or use an already-supported non-draft/dispatch run. Do not mark it ready during docs-only planning and do not treat a skipped job as passing.
- [ ] **Visual acceptance (small):** through GitHub MCP retrieve the **existing** tour artifact and perform an agent screenshot **visual check** of `04-song-select.png`, `05-lanes-performance.png`, `06-result.png` and `07-tab-performance.png` for readable titles/level/score, correct song data, and no missing labels, obvious overlap or clipping. **No** extra `main` CI run, baseline image diff, new screenshot suite, manual approval or custom comparator. Existing PNG integrity checks are not a content check; the hierarchy parity and E2E assertions cover deterministic data equivalence.
- [ ] Link CI outcome, logs, screenshot evidence and any expected visual differences in this PR. One supported platform (Windows CI) is sufficient; no second native platform check absent platform-specific changes.
- [ ] **Exit:** full canonical unit suite + passing non-skipped Windows graphical E2E + MCP inspection of the job's existing screenshots; no regressions.

### Commands to verify locally before CI

`dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj` (or `DTXMania.Game.Mac.csproj` on macOS)  
`dotnet test DTXMania.Test/DTXMania.Test.csproj`  
`dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"` (on an existing supported graphical runner)

Use the current .NET SDK/project target; do **not** revive `DTXMania.Test.Mac.csproj` or retarget the runtime.

## End-to-end acceptance criteria (automated / agent-observed, no human gate)

- [ ] Exactly **one** PR for this ticket. `SongManager.Instance`, `ResetInstanceForTesting`, `Clear`, obsolete singleton collection and monolithic `SongDatabaseService` are removed. Concrete library/stores/services have clear responsibilities, stable constructor seams and no forwarding facade.
- [ ] Cold scan and warm-cache hierarchy parity passes for SET.def, BOX/root metadata/skin colors, multiple roots and bundled placement. Competing/canceled scan, incomplete batch, post-commit failure, dirty-root watermark and missing/moved file tests pass with no partial publication.
- [ ] Result score save persists `RunId` idempotent receipts, speed-bucket/skill/history semantics and in-memory refresh-before-success. Recent plays/bookmarks and active-root query behavior remain correct.
- [ ] Canonical full unit tests pass, and a **non-skipped** `gameplay-e2e-windows` run proves actual cold/warm process launch, gameplay completion and persistent Result score. Its outcome/logs/artifacts are checked through MCP on one platform.
- [ ] MCP-backed **visual check of the existing CI screenshot tour** confirms Song Selection, Lanes/Tab gameplay and Result have readable title/level/score values, no clipped/missing text and no obvious overlap; no separate `main` baseline screenshot comparison. Cold/warm hierarchy parity and gameplay E2E assertions cover data equality. PNG file presence/integrity alone is not visual acceptance.
- [ ] No schema, gameplay, UI, config, new assets, security infrastructure, backward compatibility shim or second PR introduced.

## Work budget and stop rule

| Work | Days |
| --- | ---: |
| Baseline/reference audit | 0.10 |
| Delete dead surface | 0.25 |
| Singleton removal + stage/test migration | 0.55 |
| SQLite two-store extraction + tests | 0.50 |
| SET.def/box.def reader + tests | 0.25 |
| Hierarchy builder + parity test | 0.40 |
| Stateless scanner + contracts | 0.40 |
| Score/query services + tests | 0.30 |
| MCP-backed CI E2E + existing tour inspection | 0.25 |
| **Total** | **3.00** |

If the full behavioral scope cannot fit within three days, stop and ask to rescope **this same ticket/PR** rather than opening parallel PRs, leaving a singleton shim or silently dropping critical regression tests. No human acceptance step is required.
