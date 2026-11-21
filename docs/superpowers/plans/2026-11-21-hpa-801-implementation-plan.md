# HPA-801 — Song library responsibilities: implementation plan

**Design:** [HPA-801 design](../../proposals/2026-11-21-hpa-801-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-801  
**Branch / delivery:** `plan/hpa-801-song-library-responsibilities` — implement **on this single existing draft PR**. Do not open implementation or test follow-up PRs for this ticket.  
**Baseline:** `main` `5e8b47e311badceafc8942b804a255f0527f9463`, 2026-11-21. Re-read changed code if main advances.  
**State:** plan only. No production code, tests, graphical E2E, or screenshots have been executed as part of this draft.

## Agent contract

**Budget:** at most **3 engineer days** including migration, tests, MCP-driven E2E and screenshot verification. Favor mechanical moves, constructor injection and existing abstractions over new framework design. The task is structural: preserve SQL schema, scan results, score receipts, stage lifecycle, events and UI. No backward-compatibility aliases, static service locator, new art/SFX, database migration, feature work or nullable cleanup.

Current code seams to inspect before editing: `SongManager`, `SongDatabaseService`, `SongLibrarySnapshot`, `SongEnumerationBatch`, `SongDbContext`, `BaseGame`/`IStageGame`, `StartupStage`, `SongSelectionStage`, `ResultStage`, `ConfigStage`/`SongLibraryReloadService`, and their dedicated tests. Avoid trying to translate *all* old private-field assertions 1:1: keep behavioral regression scenarios, retire duplicate implementation-detail tests only when their behaviors are covered.

## Step 0 — establish the baseline (0.10 day)

- [ ] Record the PR's actual base/head and list production/test references to `SongManager.Instance`, `ResetInstanceForTesting` and `SongDatabaseService` (including reflection-based tests). Read existing startup and score-persistence scenarios.
- [ ] Run/record the baseline canonical unit suite and available graphical E2E smoke before refactoring. Existing failures must be reported separately, not attributed to this task. Save existing tour screenshots as comparison fixtures.
- [ ] Confirm `DTXMania.Test/DTXMania.Test.csproj` is the only canonical unit project (the old separate Mac test project was merged in HPA-797). Preserve current target framework; do not retarget .NET.

## Step 1 — extract SQLite operations, preserve transactions (0.55 day)

- [ ] Narrow `SongDatabaseService` into a constructor-built `SongDatabase` that only owns context configuration, initialization, schema/version validation and context creation. Move bulk import + cleanup + scan metadata to `SongCatalogStore`; score history/save/receipts to `SongScoreStore`; recent/bookmark/search/genre SQL to `SongQueryStore`. Retain original SQL and EF Core query shapes.
- [ ] Give each store the same database context owner and explicit constructors; do not add generic repositories, new DB tables, a copy of the DbContext options, or a temporary global database singleton.
- [ ] Migrate DB tests to store constructors/in-memory SQLite first, including idempotent `RunId` saves, transaction rollback, duplicate rows, performance-history speed buckets, root-scoped metadata, cleanup, and schema/version checks. Remove old service tests only after equivalent behaviors remain covered.
- [ ] **Exit:** DB-focused tests green; schema and persisted outcomes unchanged.

## Step 2 — extract scanning, definitions, hierarchy and atomic publication (0.80 day)

- [ ] Add `SongDefinitionReader` for SET.def/box.def decoding and metadata; move the existing logic, fallback encodings and root/title handling without changing parsing behavior.
- [ ] Add `SongHierarchyBuilder` to assemble scan or database hierarchies. Keep SET grouping, BOX nesting, score hydration, difficulty labels, bundled-root placement, and root disambiguation.
- [ ] Add `SongLibraryScanner` for enumerating, change detection, cancel/single-flight, and orchestration with `SongCatalogStore`. Retain the exact scan → import → finalize → publish sequence. Keep the start-time watermark backoff, dirty recoverable-error roots, inactive-root row retention, rejected tabs and the cancellation/busy result contracts.
- [ ] Introduce `SongLibrary` as the sole owner of the shared published snapshot, counters, version, event subscriptions and locked score-mutation operation. Publish event handlers outside the lock. The scanner may prepare work on a worker, but only the complete result is published.
- [ ] Migrate `SongManagerBulkEnumerationTests`, `SongManagerLibrarySnapshotTests`, SET.def tests, root-policy tests and scan freshness/busy/cancel tests into constructor-built scanner/reader/builder/library tests. Preserve the scenarios that verify absence of partial publication and coherent snapshots.
- [ ] **Exit:** scanned and warm-cache hierarchies match; no singleton in the core library; scan tests green.

## Step 3 — score and query services (0.55 day)

- [ ] Add `SongScoreService` for score persistence → exact in-memory chart/instrument/speed refresh and NX score import. Keep `ScoreSaveResult`/`ScoreRefreshOutcome` meaning, RunId idempotency, score variant merging and concurrent newer-snapshot behavior. A committed DB write with failed cache refresh **must not** report success to Result.
- [ ] Add `SongQueryService` for recent/bookmarked/search/genre queries and mapping returned DB results to the *active published* hierarchy. Reuse `SongQueryStore`; keep ordering, filtering and bookmark semantics.
- [ ] Migrate `SongManagerUpdateScoreTests`, `SongManagerNxImportTests`, recent/bookmark tests, and result-save tests to public behavioral calls on constructed services; do not keep private reflection helpers alive merely for tests.
- [ ] **Exit:** score/query tests green; Result and Song Selection semantics unchanged.

## Step 4 — update the game wiring and remove the old classes (0.50 day)

- [ ] Construct one `SongLibrary` in `BaseGame` before stage use, expose it through `IStageGame`, and dispose it after stages stop. Use constructor-provided loggers; database path still comes from the existing Startup seam.
- [ ] Replace every remaining call site: `StartupStage` initializes/checks/scans/publishes through library/scanner; `SongSelectionStage` reads snapshot and `Queries` plus activation-scoped publication event; `ConfigStage`/`SongLibraryReloadService` use scanner and score importer; `ResultStage` uses `Scores`. Keep test-injectable stage delegates.
- [ ] Delete `SongManager`, its `Instance` and `ResetInstanceForTesting`, the singleton-only test collection, and the now-monolithic `SongDatabaseService`. Update stale documentation/comments about the singleton. Do not leave forwarding wrappers or test-only shims.
- [ ] Search all production and test code for references to removed types/members. Run the **full** unit suite, fixing regression failures as part of the same PR.
- [ ] **Exit:** build + full unit suite green, no removed symbols referenced, stage launch viable.

## Step 5 — automated E2E and visual parity (0.40 day)

Execute/observe the **real game process through MCP** on **one supported platform** (one platform is enough; no separate Windows/macOS/native sign-off unless a platform-specific code path changes). Reuse `DTXMania.E2E`, its JSON-RPC game API, `Category=E2E` fixtures and the HPA-797 screenshot tour.

- [ ] **Cold start:** first-run fixture scans/imports, displays correct song and difficulty/BOX hierarchy and progresses Startup → Title → Song Selection. Include bundled songs, SET.def grouping and an empty/unavailable-root case in deterministic coverage.
- [ ] **Warm start:** relaunch with the same `songs.db` and unmodified files; confirm the cached hierarchy is reused and no unnecessary rescan, count drift or duplicate song. Change a chart/root and exercise Config reload; verify the updated active list, with no stale/canceled publication. Extend **one focused E2E smoke** if current E2E cases do not cover this behavior; no new harness.
- [ ] **Player loop:** in the actual process navigate Song Selection (All/Recent/Bookmarks), play via the existing autoplay E2E, reach Result, persist a `RunId` score, return to Song Selection and confirm score/recent state. Verify a retry does not double-count. Use unit/integration tests for deeper score concurrent-race matrices.
- [ ] **Screenshot parity:** use MCP-captured 1280×720 output from the existing default CX Neon skin tour (Startup, Song Selection with song/BOX listing and Recent/Bookmarks, Lanes/Tab gameplay if covered, and Result). Compare to the baseline screenshots visually or with screenshot/image diff tooling: no missing labels, wrong song/level/score, duplicated boxes, overlap, clipping, or new layout differences. Capture after reload as well. There is **no new UI mockup** because no UI is added.
- [ ] Attach screenshots, E2E logs and command results to this PR. Do not assert E2E acceptance from unit tests alone.

Recommended commands (use the current runner's supported OS and launch setup):

`dotnet build DTXMania.Game/DTXMania.Game.csproj` (or its actual platform-specific game project)  
`dotnet test DTXMania.Test/DTXMania.Test.csproj`  
`dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"`

Check actual platform project paths first; do not revive `DTXMania.Test.Mac.csproj`.

**Stop condition:** if a regression shows this cannot fit inside the 3-day budget, identify the smallest remaining *behaviorally complete* slice and ask the project lead to rescope HPA-801 **before opening another PR**. Never leave the old singleton as a permanent compatibility layer to make the deadline.

## End-to-end acceptance criteria (all automated; no human acceptance gate)

- [ ] Singleton and monolithic class symbols are absent; each new component has one cohesive reason to change, constructor-built in tests and explicitly owned by the game. Only one implementation PR exists for HPA-801.
- [ ] Cold import and cached restart produce identical root/hierarchy/chart counts and active-root behavior; SET.def/box.def, bundled root, chart moves, parse failures, cancellation and root reload preserve published snapshot invariants.
- [ ] Score saves preserve `RunId` idempotency, speed buckets, rank/achievement/history and the write-plus-in-memory-refresh success contract. Bookmarks, recent plays, search and genre return the same results/order.
- [ ] Canonical build and full unit tests pass. MCP runs an actual out-of-process graphical E2E with cold/warm start, reload, navigation, gameplay/Result save and restart verification on one platform.
- [ ] Screenshot evidence shows CX Neon Startup, Song Selection/tabs, gameplay and Result match the baseline visually, including after library reload, with no overflow/overlap or missing UI data. Evidence is linked in the same PR.
- [ ] No database schema migration, config change, rendered UI change, playback change, new external dependency, new image/SFX, or extra PR was introduced.

## Time budget

| Work | Days |
| --- | ---: |
| Baseline/reference inventory | 0.10 |
| SQLite service extraction + tests | 0.55 |
| Scanner/reader/hierarchy/publication + tests | 0.80 |
| Scores/queries + tests | 0.55 |
| Game/stage wiring, removed symbols, full unit | 0.50 |
| MCP graphical E2E and screenshot parity | 0.40 |
| Contingency | 0.10 |
| **Total** | **3.00** |

The coding agent should report unresolved assumptions in the PR comments, not silently expand scope. Every extraction step includes its tests; do not defer all verification to the last step.
