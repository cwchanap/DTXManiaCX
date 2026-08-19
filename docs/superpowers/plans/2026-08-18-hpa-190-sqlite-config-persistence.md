# HPA-190 SQLite Config Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`. Execute this as **one implementation PR**. Do not split planning/runtime/recorder/tooling into separate PRs.

**Goal:** Make `<app-data>/config.db` the sole live CX configuration store, keep `Config.ini` as explicit bootstrap/import input, migrate every caller/tool that assumes live INI authority, and preserve recorder/E2E behavior without introducing a second SQLite writer.

**Architecture:** `ConfigData` remains runtime truth. A concrete game-owned `SqliteConfigStore` persists one v1 key/value snapshot. `ConfigManager` remains the only production SQLite writer. `RecordingSandbox` reads the source DB but writes a disposable sandbox `Config.ini`, then the sandbox game creates its DB through the real ConfigManager import path. E2E proves this cross-project contract directly.

**Tech stack:** .NET 8, C#, MonoGame, `Microsoft.Data.Sqlite` 9.0.18, xUnit, existing E2E/VideoRecorder projects.

**Spec:** `docs/superpowers/specs/2026-08-18-hpa-190-sqlite-config-persistence-design.md`

**Expected effort:** 2–3 engineer days.

## Global constraints

- Keep `ConfigData` as the single runtime settings object.
- Use separate `<app-data>/config.db`; do not touch `SongDbContext` / `SongDatabaseService`.
- Schema v1: `ConfigEntries(Key TEXT PRIMARY KEY NOT NULL, Value TEXT NOT NULL)` + `PRAGMA user_version = 1`.
- Direct SQLite only; no config EF model/migrations.
- No `IConfigStore` unless implementation proves a concrete fake is necessary.
- No WAL/tuning pragmas or custom config DB recovery framework.
- `Config.ini` is read only when `config.db` is absent.
- Never automatically choose INI based on timestamp/newness.
- `DTXPath` stays legacy input/in-memory mirror; do not persist it in SQLite.
- Preserve deferred saves, song-root immediate persistence/rollback, events, normalization, and API-key generation.
- Change the pathless `IConfigManager` interface and all callers in the same compile-green task.
- Do not make `DTXMania.Game` reference `DTXMania.Automation` for config persistence.
- Recorder reads authoritative DB rows but does not write/copy SQLite DBs.
- Fresh E2E/recorder sandboxes may intentionally use INI as first-launch bootstrap input.
- Update repository docs/tooling that currently treats `Config.ini` as live mutable state.

## Planned files

Expected production/tooling changes:

```text
Create:
  DTXMania.Game/Lib/Config/SqliteConfigStore.cs

Modify:
  DTXMania.Game/DTXMania.Game.Mac.csproj
  DTXMania.Game/DTXMania.Game.Windows.csproj
  DTXMania.Game/Lib/Utilities/AppPaths.cs
  DTXMania.Game/Lib/Config/IConfigManager.cs
  DTXMania.Game/Lib/Config/ConfigManager.cs
  DTXMania.Game/Game1.cs
  DTXMania.Game/Lib/Stage/ConfigStage.cs
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  DTXMania.Game/Lib/Stage/SongSelectionStage.cs
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs
  DTXMania.Game/Lib/Resources/ResourceManager.cs  # E2E InternalsVisibleTo

  DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
  DTXMania.VideoRecorder/Program.cs
  DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs
  DTXMania.VideoRecorder/Properties/AssemblyInfo.cs

  DTXMania.E2E/DTXMania.E2E.csproj
  DTXMania.E2E/Fixtures/E2EFixture.cs
  DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs
  DTXMania.E2E/Support/E2EArtifactWriter.cs
  DTXMania.E2E/DrumMappingStageSmokeTests.cs

  MCP/README.md
  justfile
  tools/hpa192/benchmark-critical-path.sh
  tools/hpa192/test-critical-path.sh
```

Expected test updates/additions include:

```text
Create:
  DTXMania.Test/Config/SqliteConfigStoreTests.cs
  DTXMania.E2E/RecorderConfigCompatibilityTests.cs

Modify as needed by compile/search inventory:
  DTXMania.Test/Utilities/AppPathsTests.cs
  DTXMania.Test/Config/ConfigManagerTests.cs
  DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs
  DTXMania.Test/Config/ConfigManagerSkinPathTests.cs
  DTXMania.Test/Config/PlaySpeedAndPitchConfigTests.cs
  DTXMania.Test/Config/SongRootConfigTests.cs
  DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs
  DTXMania.Test/Config/ConfigStageLogicTests.cs
  DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs
  DTXMania.Test/BaseGameTests.cs
  DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs
  DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
  DTXMania.Test/Stage/Performance/PerformanceStageCoverageTests.cs
  DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs
  DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs

  DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs
  DTXMania.VideoRecorder.Tests/ProgramTests.cs
  DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs
  DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs
  DTXMania.VideoRecorder.Tests/Diagnostics/RecorderDiagnosticsTests.cs

  DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs
  DTXMania.E2E/GameplayAutoPlaySmokeTests.cs
  DTXMania.E2E/MidiGameplaySmokeTests.cs  # only if scan shows live-authority assumptions
```

Do not treat this list as permission to skip repository search. The interface/config-file scans below are required.

---

## Task 1: Add the concrete SQLite store and explicit app paths

**Deliverable:** tested v1 key/value store; ConfigManager still uses INI at the end of this task.

### Files

- Create `DTXMania.Game/Lib/Config/SqliteConfigStore.cs`
- Create `DTXMania.Test/Config/SqliteConfigStoreTests.cs`
- Modify `AppPaths.cs` + `AppPathsTests.cs`
- Add direct `Microsoft.Data.Sqlite` 9.0.18 package reference to both game platform projects if the new production code uses the namespace directly.

### Steps

- [ ] Add `GetConfigDatabasePath()` -> `<app-data>/config.db`.
- [ ] Rename/replace the legacy INI helper with `GetLegacyConfigFilePath()` -> `<app-data>/Config.ini`.
- [ ] Write failing real-SQLite tests for:
  - new save/load round trip;
  - `PRAGMA user_version = 1`;
  - snapshot replacement removes stale rows;
  - unsupported version fails;
  - missing `ConfigEntries` fails;
  - deterministic save failure (for example DB path is an existing directory) propagates.
- [ ] Implement concrete `SqliteConfigStore` with one transaction for table/version setup + delete + inserts.
- [ ] Do **not** add failed-initial-create sidecar cleanup or a partial-create recovery test.
- [ ] Do not enable WAL.

### Gate

Mac:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug \
  --filter "FullyQualifiedName~SqliteConfigStoreTests|FullyQualifiedName~AppPathsTests"
```

Windows uses `DTXMania.Test.csproj` with the same filter.

Commit checkpoint: `feat: add SQLite configuration store`.

---

## Task 2: Make ConfigManager DB-authoritative and migrate all game callers atomically

**Deliverable:** game builds after the pathless `IConfigManager` break; no production caller still treats `Config.ini` as live persistence.

### 2.1 Pin migration behavior with tests

- [ ] Retarget/add ConfigManager tests for:
  - no DB/no INI -> defaults -> DB created;
  - INI only -> import -> DB created -> INI bytes unchanged;
  - DB + conflicting INI -> DB wins;
  - existing invalid DB -> fail, no INI fallback;
  - legacy `DTXPath` only -> `SongRoot.0` persisted;
  - normalization corrections persisted to DB;
  - API key generation persisted;
  - deferred save updates DB;
  - failed deferred save remains pending;
  - failed immediate song-root save restores prior values;
  - binding/system-key/MIDI dynamic rows disappear after snapshot replacement.

### 2.2 Reuse current parser/snapshot logic

- [ ] Refactor the logical contents of today's `SaveConfig` into `BuildPersistedEntries()`.
- [ ] Exclude duplicate `DTXPath` from DB rows.
- [ ] Reuse `ParseConfigLine` for DB rows and INI assignments.
- [ ] In the same pass, make integer parsing use `CultureInfo.InvariantCulture` and route the remaining `UseBoxDefSkin` / `FullScreen` / `VSyncWait` boolean reads through invariant/ordinal boolean parsing.
- [ ] Do not add a second typed deserializer.

### 2.3 Compose persistence explicitly

Default `ConfigManager` owns:

```text
SqliteConfigStore(AppPaths.GetConfigDatabasePath())
legacy path = AppPaths.GetLegacyConfigFilePath()
SongRootPolicy.ForCurrentPlatform()
```

Keep an internal test constructor/load seam with explicit DB path + legacy INI path + existing path-policy/base-directory inputs. This is required for parallel-safe unit/E2E tests.

### 2.4 Replace path-shaped dirty state

- [ ] Replace `_pendingSavePath` / `_loadedConfigPath` with one pending-save boolean.
- [ ] `FlushPendingSave()` clears it only after successful SQLite save.
- [ ] `SetSongRoots` still persists immediately and rolls back on failure.

### 2.5 Break the interface only when all callers move

Change together:

```text
LoadConfig()
SetSongRoots(roots)
SetScrollSpeed(percent)
AdjustScrollSpeed(stepDelta)
SetSkinPath(skinPath)
FlushPendingSave()
```

Remove public `SaveConfig(filePath)`.

In the **same commit**, migrate:

- `Game1` startup;
- `ConfigStage` scroll speed, skin, song roots;
- `PerformanceStage` scroll-speed hotkeys;
- `SongSelectionStage` scroll-speed hotkeys;
- all mocks/stubs/reflection tests that pin old signatures or `_pendingSavePath`.

### 2.6 Preserve crash redaction

- [ ] Replace old config-path registration with both `GetConfigDatabasePath()` and `GetLegacyConfigFilePath()`.
- [ ] Extend `CrashContextPublisherTests` accordingly.

### Required pre-commit scan

Run and resolve **all** production/test hits:

```bash
rg -n "GetConfigFilePath\(|LoadConfig\(|SaveConfig\(|AdjustScrollSpeed\(|SetScrollSpeed\(|SetSkinPath\(|SetSongRoots\(" \
  DTXMania.Game DTXMania.Test DTXMania.E2E DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests
```

The checkpoint must compile; do not defer known callers to a later task.

### Gate

Run focused ConfigManager/stage tests plus a game build on the implementation platform.

Commit checkpoint: `feat: persist CX configuration in SQLite`.

---

## Task 3: Migrate recorder source handling without a second SQLite writer

**Deliverable:** recorder reads real `config.db`, writes only sandbox bootstrap INI, and `doctor` validates the DB source.

### 3.1 Recorder dependency

- [ ] Add direct `Microsoft.Data.Sqlite` 9.0.18 to `DTXMania.VideoRecorder.csproj` for read-only source DB access.
- [ ] Keep `DTXMania.VideoRecorder -> DTXMania.Automation` unchanged.
- [ ] Do **not** add `DTXMania.Game -> DTXMania.Automation` or a new shared settings assembly.

### 3.2 Retarget RecordingSandbox

Source contract:

```text
<source-app-data>/config.db
```

- [ ] Open source DB read-only.
- [ ] Require v1 user version and `ConfigEntries` table.
- [ ] Load rows into memory.
- [ ] Validate absolute `SongRoot.N`, `SystemSkinRoot`, and `SkinPath` (`Default` or absolute).
- [ ] Remove old `DTXPath` validation.
- [ ] Patch recorder-owned rows in memory.
- [ ] Serialize a **fresh sandbox Config.ini** containing the patched logical values.
- [ ] Do not copy/write `config.db`, `-wal`, or `-shm` in recorder code.
- [ ] Keep run-root/API/cleanup semantics unchanged.

The sandbox game will import that INI and create its own `config.db` through production `ConfigManager`.

### 3.3 Doctor and recorder tests

- [ ] `Program.RunDoctorAsync` checks `<source-app-data>/config.db` and calls the updated source validator.
- [ ] Retarget source fixtures in `ProgramTests`, `RecorderCommandLineTests`, `RecorderGameLaunchPolicyTests`, and `RecordingSandboxTests` where they currently plant authoritative INI.
- [ ] Recorder unit tests may use a tiny test-only SQL fixture helper to create malformed/valid v1 source DBs; this is not a production writer.
- [ ] Extend `RecorderDiagnosticsTests` to assert neither `Config.ini` nor `config.db` is copied into diagnostic output.

### 3.4 Add cross-project compatibility proof

Use existing test-only project boundaries instead of a production dependency:

- [ ] Add `[assembly: InternalsVisibleTo("DTXMania.E2E")]` to the game assembly (next to existing test IVTs).
- [ ] Add the same IVT to `DTXMania.VideoRecorder/Properties/AssemblyInfo.cs`.
- [ ] Add a test-only `ProjectReference` from `DTXMania.E2E` to `DTXMania.VideoRecorder`.
- [ ] Create `RecorderConfigCompatibilityTests` with `Category=E2E-Support`:
  1. create source `config.db` through real explicit-path `ConfigManager`;
  2. run real `RecordingSandbox.Create`;
  3. assert sandbox bootstrap INI exists and sandbox DB does not yet exist;
  4. load sandbox through real explicit-path `ConfigManager`;
  5. assert sandbox DB is created and source values + recorder overrides are correct;
  6. assert source DB remains unchanged.

This test is mandatory; it is what catches future schema/reader drift.

### Gate

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "Category=E2E-Support"
```

Commit checkpoint: `feat: migrate recorder configuration to SQLite source`.

---

## Task 4: Make E2E persistence assertions and repository tooling DB-aware

**Deliverable:** no false-green E2E persistence test and no documented/dev workflow silently edits ignored INI state.

### 4.1 E2E fixture model

Add explicit fixture paths:

```text
LegacyConfigPath
ConfigDatabasePath
```

Fresh fixture builder may continue writing only legacy INI before launch; this intentionally tests first-launch bootstrap.

### 4.2 Post-run ConfigManager helper

- [ ] Use explicit-path ConfigManager constructor; do not mutate process-global `DTXMANIA_APPDATA_ROOT`.
- [ ] **Assert `File.Exists(fixture.ConfigDatabasePath)` before loading.**
- [ ] Then load through ConfigManager and assert `ConfigData` values.

Update Drum Mapping bind/reset persistence tests to use this helper. The reset test must not pass by falling back to its original bootstrap INI.

### 4.3 E2E artifacts

`E2EArtifactWriter.CopyFixtureFiles` currently runs from `finally` while the process bundle may still be alive.

- [ ] Do not start copying live `config.db` from that helper.
- [ ] If useful, rename/copy the original INI as bootstrap-input evidence only.
- [ ] Use the explicit DB load assertion as persistence evidence.
- [ ] Do not require a physical DB artifact for HPA-190.

### 4.4 MCP documentation / API settings

`EnableGameApi`, `GameApiPort`, and `GameApiKey` have no current ConfigStage editor. Keep them developer-facing for this slice.

Update `MCP/README.md` so it:

- treats `config.db` as authoritative after first launch;
- no longer says the API key comes from `Config.ini`;
- shows `sqlite3` commands to read the API key and update Game API enable/port/key rows;
- states that INI is import/bootstrap only once DB exists.

Do not add Game API UI in HPA-190.

### 4.5 CX Neon just recipe

Update `just install-cx-neon activate=true`:

- DB exists -> update `SkinPath` / `LastUsedSkin` rows in `config.db` transactionally through `sqlite3`;
- DB absent -> retain current INI bootstrap behavior;
- DB exists but SQLite CLI unavailable -> fail clearly; never print a false successful activation.

### 4.6 HPA-192 benchmark tooling

`benchmark-startup.sh` uses a brand-new temporary app-data directory; its INI remains valid bootstrap input.

For `benchmark-critical-path.sh` / `test-critical-path.sh`:

- warm scenario C clones seed app-data and therefore will contain `config.db` after HPA-190;
- stop using copied `Config.ini` bytes as proof of active warm-scenario configuration;
- inspect/patch the authoritative DB where required and update runner tests/metadata accordingly;
- preserve benchmark semantics and frozen-input identity goals; do not redesign HPA-192.

### Required repository scan

Run **before** considering tooling complete:

```bash
rg -n "Config\.ini" . \
  -g '!docs/superpowers/specs/2026-08-18-hpa-190-sqlite-config-persistence-design.md' \
  -g '!docs/superpowers/plans/2026-08-18-hpa-190-sqlite-config-persistence.md'
```

Classify each hit as one of:

```text
legacy/bootstrap intentionally retained
live-authority assumption -> migrate
historical documentation -> leave if clearly historical
```

### Gate

Run:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "Category=E2E-Support"
```

On Windows/CI also run:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "Category=E2E"
```

Run the HPA-192 script tests affected by edits.

Commit checkpoint: `test: migrate config persistence consumers`.

---

## Task 5: Final migration verification

**Deliverable:** one implementation PR that is demonstrably DB-authoritative end-to-end.

### Automated gates

Mac implementation host:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "Category=E2E-Support"
```

Windows/CI:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Debug
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "Category=E2E"
```

### Manual migration smoke

Use temporary app-data roots only.

**Fresh:**

```text
empty app-data -> launch -> config.db exists -> no generated Config.ini -> relaunch -> settings load
```

**Legacy/NX import:**

```text
copy legacy/NX Config.ini into app-data with no config.db
-> launch
-> config.db created
-> source INI bytes unchanged
-> DB values active
```

**Authority:**

```text
DB exists + conflicting INI
-> launch
-> DB wins
```

**Recorder:**

```text
real source config.db
-> RecordingSandbox reads source rows
-> sandbox bootstrap INI generated
-> production ConfigManager imports it
-> sandbox config.db created
-> source DB unchanged
```

No live OBS run is needed because Task 3's cross-contract test directly executes the production config writer/importer boundaries the storage migration changes.

### Final scans

Require:

```text
production GetConfigFilePath() references: 0
normal CX Config.ini writes: 0
Config.ini live-authority tooling/docs: 0
SongDbContext/SongDatabaseService changes: 0
new config EF model/migrations: 0
Game -> Automation reference added for config: 0
```

## Definition of done

- [ ] `config.db` is sole live CX settings storage.
- [ ] INI imports only when DB is absent and is never auto-selected over DB.
- [ ] Existing persisted settings round-trip through one SQLite snapshot.
- [ ] Integer/boolean parsing is culture-stable.
- [ ] `DTXPath` is absent from DB but legacy import still produces `SongRoot.0`.
- [ ] Deferred retry and song-root rollback semantics are preserved.
- [ ] Every `IConfigManager` caller/fake uses the new pathless API.
- [ ] Crash redaction includes both DB and legacy INI paths.
- [ ] Recorder has no second SQLite writer and passes the real ConfigManager compatibility test.
- [ ] Recorder diagnostics contain neither config storage file.
- [ ] E2E post-run assertions require `config.db` existence before loading.
- [ ] MCP docs and CX Neon activation no longer instruct/edit stale live INI state.
- [ ] HPA-192 warm benchmark controls remain valid under DB authority.
- [ ] NX import wording is accurate: manual placement, no automatic NX discovery.
- [ ] Full applicable game/recorder/E2E gates pass in one implementation PR.
