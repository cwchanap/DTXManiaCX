# HPA-190 SQLite Config Persistence Design

**Issue:** [HPA-190](https://linear.app/cwchanap/issue/HPA-190/move-configini-to-sqlite)  
**Date:** 2026-08-18  
**Status:** Proposed — revised after dependency, tooling, recorder, and E2E review

## Goal and driver

Move CX-owned live configuration from `Config.ini` to a small SQLite database while preserving the current runtime settings model and behavior.

This is a project direction, not a fix for unsafe INI writes. The current `ConfigManager.SaveConfig` already writes `Config.ini` atomically through a temporary file plus `File.Move`. HPA-190 instead standardizes CX-owned mutable state on a machine-readable transactional store while retaining INI as a compatibility/import format.

After HPA-190:

- `ConfigData` remains the in-memory runtime truth;
- `<app-data>/config.db` is the only live CX settings store;
- edits and deferred flushes replace one canonical key/value snapshot transactionally;
- `Config.ini` is import/bootstrap input only when `config.db` does not exist;
- `songs.db` remains independently owned by the song catalog;
- old INI files can remain on disk without overriding newer DB values;
- recorder and E2E workflows remain functional without creating a second live settings model.

## Non-goals

- no EF config `DbContext` or migration project;
- no shared config/song database;
- no profiles, cloud sync, encryption, or secrets vault;
- no new settings assembly;
- no continuous SQLite -> INI mirror;
- no broad Config UI expansion;
- no recorder OBS/media hardening.

## Existing architecture to preserve

`ConfigManager` already owns:

- `ConfigData`;
- all INI key parsing;
- legacy `DTXPath` -> ordered `SongRoot.N` handling;
- song/skin path normalization;
- generated Game API key behavior;
- key-binding and MIDI-threshold validation;
- immediate song-root persistence/rollback;
- deferred persistence for ordinary edits.

Stages and gameplay already consume `IConfigManager.Config`. HPA-190 is therefore a storage replacement under an existing runtime boundary, not a second configuration subsystem.

`songs.db` is intentionally unsuitable for settings because `SongDatabaseService` may recreate the catalog database during recovery. A song-index recovery must never delete user settings.

## Storage decision

Use a separate:

```text
<app-data>/config.db
```

with one stable v1 table:

```sql
CREATE TABLE ConfigEntries (
    Key   TEXT PRIMARY KEY NOT NULL,
    Value TEXT NOT NULL
);
PRAGMA user_version = 1;
```

Use direct `Microsoft.Data.Sqlite`. Do not model settings as relational entities.

### Why key/value snapshot

The current persisted format is already logically flat: scalar keys plus dynamic prefixes such as `SongRoot.N`, `Key.*`, `SystemKey.*`, and `MidiVelocity.*`. A key/value table therefore maps the existing contract directly and avoids per-setting DB schema changes.

A save replaces the complete logical snapshot in one transaction. This intentionally removes stale dynamic rows when bindings, roots, or MIDI thresholds are deleted.

## `SqliteConfigStore`

Add one concrete internal `SqliteConfigStore` in the game configuration area. Do **not** add `IConfigStore` unless implementation evidence shows a real fake is required.

Minimal responsibilities:

```csharp
bool Exists { get; }
IReadOnlyDictionary<string, string> Load();
void Save(IReadOnlyDictionary<string, string> entries);
```

`Load` must:

1. open the existing DB read-only;
2. require `PRAGMA user_version = 1`;
3. require `ConfigEntries`;
4. return the complete row set;
5. fail loudly for an unreadable/unsupported existing DB.

`Save` must:

1. create the parent directory;
2. open/create `config.db`;
3. begin one transaction;
4. create the v1 table/version when needed;
5. delete the previous rows;
6. insert the full snapshot with parameters;
7. commit.

No WAL, tuning pragmas, custom recovery framework, or failed-initial-create sidecar cleanup is required for HPA-190. If a rare failed first write leaves an unusable DB, normal existing-DB validation fails loudly; deleting `config.db` and relaunching is the manual recovery path.

## Persisted key contract

Build the DB snapshot by refactoring the logical content of today's `SaveConfig` rather than inventing a new serializer.

Persist:

```text
DTXManiaVersion
SkinPath
SongRoot.N
UseBoxDefSkin
SystemSkinRoot
LastUsedSkin
ScreenWidth
ScreenHeight
FullScreen
VSyncWait
ScrollSpeed
PlaySpeedPercent
PitchSemitones
Metronome
AutoPlay
NoFail
AudioLatencyOffsetMs
EnableGameApi
GameApiPort
GameApiKey
Key.*
Key.Unbound.*
Key.UnboundButton.*
SystemKey.*
MidiVelocity.*
```

Do **not** persist duplicate `DTXPath`. It remains:

- accepted from legacy INI when no `SongRoot.N` exists;
- mirrored in memory from `SongRoot.0` for existing code compatibility.

Do not start persisting currently non-persisted `ConfigData` fields as part of this ticket.

### Culture contract

Because the SQLite writer is canonical, parsing must also be canonical. While `ParseConfigLine` is already being reused for DB rows and legacy INI:

- parse integers with `CultureInfo.InvariantCulture`;
- route all persisted booleans through the existing robust boolean parser (or equivalent ordinal/invariant comparison);
- remove the remaining culture-sensitive `value.ToLower() == "true"` cases.

This applies equally to legacy INI and DB rows and prevents the reader/writer pair from being culture-asymmetric.

## App paths

Add explicit sibling helpers:

```text
AppPaths.GetConfigDatabasePath()   -> <app-data>/config.db
AppPaths.GetLegacyConfigFilePath() -> <app-data>/Config.ini
```

Retire ambiguous production use of `GetConfigFilePath()` once callers migrate.

Register both the live DB path and retained legacy INI path with crash-report sensitive-prefix redaction.

## `ConfigManager` lifecycle

`ConfigManager` owns its persistence destination. Normal callers no longer pass file paths.

Startup:

```text
config.db exists
  -> load DB rows
  -> never consult Config.ini

config.db missing + Config.ini exists
  -> parse INI using current compatibility rules
  -> normalize/migrate
  -> save canonical config.db
  -> leave Config.ini bytes untouched

neither exists
  -> defaults
  -> normalize
  -> save canonical config.db
```

If DB loading fails, fail instead of falling back to stale INI.

Keep the current generated API-key behavior: if Game API is enabled with an empty key, generate and persist one.

### Testability

Keep an internal constructor/load seam that accepts explicit database path, legacy INI path, base directory/path-policy inputs, and logger as needed by tests. Unit tests must not depend on process-global `DTXMANIA_APPDATA_ROOT` mutation.

## Runtime edit semantics

Preserve current behavior:

- setters update `ConfigData` immediately;
- ordinary edits set one dirty flag;
- `FlushPendingSave()` writes one full snapshot and clears the flag only on success;
- a failed deferred save keeps the flag set for retry;
- song-root Apply persists immediately before live reload;
- failed song-root persistence restores the prior roots/`DTXPath` and returns `PersistenceFailed`;
- existing config events keep their behavior.

Replace `_pendingSavePath` / `_loadedConfigPath` with a simple pending-save flag.

## Internal API cleanup

Move the interface break and every caller together so no checkpoint is knowingly uncompilable:

```text
LoadConfig()
SetSongRoots(roots)
SetScrollSpeed(percent)
AdjustScrollSpeed(stepDelta)
SetSkinPath(skinPath)
FlushPendingSave()
```

Remove public `SaveConfig(filePath)` in favor of private/internal persistence owned by `ConfigManager`.

Known production callers include:

- `Game1.cs`;
- `ConfigStage.cs`;
- `PerformanceStage.cs`;
- `SongSelectionStage.cs`;
- `CrashContextPublisher.cs` for path redaction.

Implementation must run a repository-wide call-site scan before the interface-change commit rather than treating test fakes as optional follow-up work.

## Configuration editability after INI becomes import-only

Three Game API values currently have no in-game editing surface:

```text
EnableGameApi
GameApiPort
GameApiKey
```

HPA-190 will **not** add new ConfigStage UI for these developer-facing settings. Instead, make the new developer workflow explicit:

- `MCP/README.md` must document `config.db` as authoritative and show how to read/update these rows with `sqlite3` after first launch;
- reading the API key must no longer instruct users to inspect `Config.ini`;
- do not re-import INI based on timestamps; DB authority remains deterministic.

This is an accepted developer papercut for this slice. A proper Game API Config UI can be a later feature if it becomes worth the product work.

### Existing tooling that edits INI

HPA-190 must not silently break repository tooling:

- `just install-cx-neon activate=true` currently edits `Config.ini`; after migration it must update `config.db` when the DB exists, while retaining INI bootstrap behavior when the DB does not yet exist. If the required SQLite CLI is unavailable, fail clearly rather than report a false activation.
- `tools/hpa192/benchmark-startup.sh` creates a fresh temporary app-data root, so its INI is still a valid intentional bootstrap fixture.
- `tools/hpa192/benchmark-critical-path.sh` clones seeded app-data for warm scenarios; its current `Config.ini` assertions no longer prove the active settings once the seed contains `config.db`. Update the runner and its tests so warm-scenario configuration checks inspect/patch the authoritative DB rather than stale INI bytes.
- use a repo scan for `Config.ini` to identify any additional live-authority assumptions.

## Recorder integration

Do **not** make `DTXMania.Game` depend on `DTXMania.Automation` merely to share the config store. `DTXMania.Automation` is a platform-neutral external automation library; making the game runtime depend on it would reverse the intended dependency direction.

Also do not maintain a second production SQLite writer in `DTXMania.VideoRecorder`.

Use the smaller boundary:

```text
source real app-data
  config.db (authoritative)
      |
      v
RecordingSandbox read-only SQLite reader
  -> validate/patch rows in memory
  -> write fresh sandbox Config.ini bootstrap input
      |
      v
sandbox game startup
  ConfigManager production INI importer
  -> creates sandbox config.db
```

The recorder therefore needs only a small read-only understanding of v1 `config.db`. It does **not** copy the source DB, `-wal`, or `-shm`, and does not write a second DB implementation.

Recorder validation keeps the existing presentation requirements, translated to DB rows:

- at least one valid absolute `SongRoot.N`;
- absolute `SystemSkinRoot`;
- nonblank `SkinPath`, either `Default` or absolute.

Remove the recorder's `DTXPath` requirement.

Patch recorder-owned values in memory before writing the sandbox bootstrap INI:

```text
EnableGameApi=True
fresh GameApiPort
fresh GameApiKey
AutoPlay=True
NoFail=True
ScreenWidth=1280
ScreenHeight=720
FullScreen=False
```

`dtx-video doctor` checks source `config.db`, not `Config.ini`.

### Recorder/game schema compatibility proof

Add one non-OBS integration test in the existing E2E support project:

1. use the real `ConfigManager`/store with explicit paths to create a source `config.db`;
2. call the real `RecordingSandbox` against that source;
3. assert the recorder creates sandbox bootstrap INI and does not directly create/copy a DB;
4. load the sandbox through a real explicit-path `ConfigManager`;
5. assert source values plus recorder overrides round-trip correctly and a sandbox `config.db` is created.

Use test-only `InternalsVisibleTo("DTXMania.E2E")` and an E2E project reference to `DTXMania.VideoRecorder` rather than adding a production dependency from the game to Automation.

This test is the guard against future config schema drift.

## Recorder diagnostics

`RecorderDiagnosticsTests` currently asserts that `Config.ini` is never copied into diagnostic output. Extend this assertion to `config.db` as well, because the DB contains `GameApiKey`.

No config storage file should be copied into the sanitized diagnostic bundle.

## E2E persistence behavior

Fresh E2E fixtures may continue writing `Config.ini`: that intentionally exercises first-launch bootstrap.

Post-run persistence assertions must not read that bootstrap file. Add explicit paths to the fixture model and load the DB through real `ConfigManager`:

```text
fixture.LegacyConfigPath
fixture.ConfigDatabasePath
```

Before any post-run load/assertion:

```text
assert config.db exists
-> then load it with explicit paths
-> then assert ConfigData
```

The existence assertion is load-bearing: without it, a test could fall back to the original bootstrap INI and falsely pass.

Add `InternalsVisibleTo("DTXMania.E2E")` so the E2E support helper can use the explicit-path ConfigManager constructor without mutating process-global app-data environment state.

### E2E artifacts

Do not blindly `File.Copy(config.db)` from `E2EArtifactWriter.CopyFixtureFiles` while the game process may still own the DB. The current copy runs from `finally` blocks before the surrounding process bundle is necessarily disposed.

Keep the bootstrap INI as input evidence if useful, but do not treat it as post-run persistence evidence. Persistence correctness is proven through the explicit ConfigManager DB load. If a physical DB artifact is ever retained, capture it only after process shutdown or through a SQLite-consistent snapshot operation; HPA-190 does not require that artifact.

## NX compatibility meaning

HPA-190 does **not** discover a DTXManiaNX installation or search for NX configuration files.

For this issue, "Config.ini compatibility for import from NX" means:

1. copy the desired NX/legacy `Config.ini` into the CX app-data root **before the first SQLite-backed launch** (or after deliberately removing `config.db` to perform a fresh re-import);
2. launch CX;
3. CX imports the subset of legacy keys it already understands and persists the canonical result to `config.db`.

Automatic NX path discovery and a dedicated Import Config UI are out of scope.

## Failure behavior

- existing unreadable/unsupported `config.db`: fail loudly; never fall back to INI;
- deferred DB save failure: keep dirty state for retry;
- song-root immediate save failure: restore old roots and report persistence failure;
- legacy import failure before DB creation: surface the error;
- recorder missing/unreadable source DB: fail preflight with an actionable message to launch CX once normally;
- no automatic deletion/recovery framework for malformed config DBs in this ticket.

## Required verification

### Store / ConfigManager

Cover:

- new DB create/load/save round-trip;
- full snapshot replacement removes stale rows;
- unsupported/missing schema fails;
- defaults -> DB;
- INI-only -> one-time DB import without modifying INI;
- DB + conflicting INI -> DB wins;
- `DTXPath`-only legacy import -> `SongRoot.0`;
- normalization/API-key generation persist corrections;
- deferred save retry;
- song-root rollback;
- invariant numeric/boolean parsing.

### Interface / caller migration

Require zero production references to old path-shaped config APIs before the checkpoint is committed.

### Recorder

Cover source DB validation, row preservation/override behavior, doctor source check, no `DTXPath` requirement, source isolation, and no config files in diagnostics.

### Cross-contract / E2E

Run:

- the new recorder <-> ConfigManager E2E-support compatibility test;
- full `Category=E2E-Support`;
- Windows `Category=E2E` gameplay persistence smoke;
- full game unit suite/build on the implementation host;
- full VideoRecorder test suite.

No live OBS recording is required: the cross-contract test directly proves that a production-generated source DB is consumable by the recorder and that recorder-generated sandbox bootstrap input is consumable by production `ConfigManager`.

## Acceptance criteria

HPA-190 is complete when:

1. normal CX starts from and saves to `config.db` only;
2. legacy INI imports exactly once when DB is absent and remains untouched;
3. conflicting stale INI cannot override a DB;
4. current runtime config behavior, bindings, roots, events, and normalization are preserved;
5. `songs.db` code/lifecycle is unchanged;
6. all `IConfigManager` callers are migrated in one compile-green slice;
7. crash redaction includes live DB and legacy INI paths;
8. MCP docs, CX Neon activation tooling, and HPA-192 warm benchmark tooling no longer assume live INI authority;
9. recorder validates source `config.db`, writes only sandbox bootstrap INI, and the production ConfigManager compatibility test passes;
10. E2E post-run persistence assertions require and load `config.db` rather than stale INI;
11. Windows/macOS applicable build/test gates remain green.
