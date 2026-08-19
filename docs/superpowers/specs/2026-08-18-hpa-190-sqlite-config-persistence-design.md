# HPA-190 SQLite Config Persistence Design

**Issue:** [HPA-190](https://linear.app/cwchanap/issue/HPA-190/move-configini-to-sqlite)  
**Date:** 2026-08-18  
**Status:** Proposed — revised after call-site review

## Goal

Move CX-owned configuration persistence from `Config.ini` to a small SQLite database while preserving the current runtime configuration model and edit behavior.

After HPA-190:

- `ConfigData` remains the in-memory source of truth used by stages and gameplay;
- normal CX startup reads `<app-data>/config.db`;
- normal edits and deferred flushes write `<app-data>/config.db` transactionally;
- `Config.ini` is legacy input only, used to bootstrap `config.db` when no config database exists;
- an existing CX/NX `Config.ini` upgrades automatically on first launch and remains untouched afterward;
- `songs.db` remains independent from user configuration;
- the video recorder reads and writes the same v1 key/value schema without referencing `DTXMania.Game`;
- `dtx-video doctor` validates `config.db`, not stale INI state;
- E2E may still author INI as disposable bootstrap input, but post-launch persistence assertions and artifacts use `config.db` / `ConfigManager`;
- crash redaction registers both the live configuration database path and the retained legacy INI path.

This is a persistence migration, not a redesign of the configuration UI, settings model, song database, recorder workflow, or E2E architecture.

## Current-state findings

### ConfigManager owns both runtime state and INI persistence

`DTXMania.Game/Lib/Config/ConfigManager.cs` currently:

- owns `ConfigData`;
- parses `Config.ini` key/value lines;
- performs legacy `DTXPath` -> ordered `SongRoot.N` migration;
- normalizes song and skin paths;
- generates a Game API key when needed;
- serializes the complete persisted snapshot back to `Config.ini`;
- performs immediate writes for song-root changes;
- performs deferred writes for ordinary config edits.

The runtime contract is already useful: callers consume `IConfigManager.Config`, and typed setters mutate that live object. HPA-190 replaces storage under that contract rather than creating a second settings model.

### Persistence-path APIs have more callers than ConfigStage

`IConfigManager` currently exposes storage-shaped APIs:

```text
LoadConfig(filePath)
SaveConfig(filePath)
SetSongRoots(configFilePath, roots)
SetScrollSpeed(configFilePath, percent)
AdjustScrollSpeed(configFilePath, stepDelta)
SetSkinPath(configFilePath, skinPath)
```

Known production consumers include:

- `DTXMania.Game/Game1.cs` — startup load;
- `DTXMania.Game/Lib/Stage/ConfigStage.cs` — scroll speed, skin, song roots;
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — gameplay scroll-speed hotkeys;
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs` — Song Select scroll-speed hotkeys.

There are also tests/fakes that compile against these signatures, including performance coverage, Song Select coverage, Drum Config stubs, song-root persistence, play-speed/pitch persistence, system-key persistence, and ConfigManager persistence tests.

The pathless interface change must therefore be one compile-green slice: change `IConfigManager`, all production consumers, and all compile-time fakes/assertions together. Do not checkpoint a branch where the interface changed but gameplay callers still use the old signature.

### `songs.db` is not a safe home for configuration

`SongDatabaseService` owns `songs.db` and intentionally contains database validation/recovery paths that can recreate the song catalog when the file is invalid or incompatible. User settings must not share that lifecycle: rebuilding a song index must never discard display, input, skin, API, or gameplay settings.

Configuration therefore needs its own database file.

### Recorder source-of-truth assumptions exist in two places

`DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs` currently reads the developer's `Config.ini`, validates presentation paths, applies recorder-owned overrides, and writes a fresh sandbox INI.

`DTXMania.VideoRecorder/Program.cs` also makes `dtx-video doctor` explicitly check `<source-app-data>/Config.ini` before calling `RecordingSandbox.ValidateSourceConfig`.

Updating only `RecordingSandbox` would leave doctor reporting a missing source config after real CX has migrated to SQLite. The recorder slice must update both surfaces and their tests.

### E2E uses INI as both bootstrap input and persistence evidence

`DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs` creates a disposable `Config.ini` before launch. Keeping that as bootstrap input is useful because it exercises the real one-time migration path.

Post-launch assumptions are different:

- `DrumMappingStageSmokeTests` reads `fixture.ConfigPath` after stage exit and asserts persisted bindings in INI text;
- `GameplayAutoPlaySmokeTests` snapshots INI text;
- `E2EArtifactWriter.CopyFixtureFiles` always copies `Config.ini` as the config artifact;
- `E2EFixtureBuilderTests` directly calls `ConfigManager.LoadConfig(fixture.ConfigPath)`;
- `MidiGameplaySmokeTests` patches the pre-launch INI, which remains a valid bootstrap use.

After HPA-190 the first game launch creates `config.db` and stops updating the legacy INI. Therefore post-run INI assertions become stale even though bootstrap authoring remains valid.

E2E already references the matching game project and disables test parallelization, so it can verify persisted runtime state through `ConfigManager` while temporarily pointing `DTXMANIA_APPDATA_ROOT` at the fixture. Do not add a third SQLite writer/parser to E2E.

### Crash redaction names the old config path

`CrashContextPublisher.RegisterSensitivePrefixes` currently registers `AppPaths.GetConfigFilePath()` and `GetSongsDatabasePath()` among other paths. HPA-190 changes the live config filename, so this existing privacy boundary must move with it.

Register `GetConfigDatabasePath()` and retain `GetLegacyConfigFilePath()` while legacy INI import remains supported.

### SQLite is already a game dependency

The game already uses SQLite through `Microsoft.EntityFrameworkCore.Sqlite`. The configuration store can use `Microsoft.Data.Sqlite` directly without adding an EF config model or migration framework. `DTXMania.VideoRecorder` is platform-neutral and does not reference the game, so it may add its own direct `Microsoft.Data.Sqlite` reference for the tiny shared schema.

## Approaches considered

### A. Separate `config.db` with a key/value snapshot — recommended

Use one stable table:

```sql
CREATE TABLE ConfigEntries (
    Key   TEXT PRIMARY KEY NOT NULL,
    Value TEXT NOT NULL
);
```

Persist the same logical keys already used by `Config.ini` (`SkinPath`, `ScreenWidth`, `SongRoot.0`, `Key.*`, `SystemKey.*`, `MidiVelocity.*`, and so on). Save the complete canonical snapshot in one SQLite transaction.

**Pros**

- smallest change to the proven parser/config model;
- no per-setting database schema migrations;
- dynamic collections map naturally to prefixed keys;
- removed bindings/roots disappear naturally when a full snapshot replaces prior rows;
- recorder can patch the same tiny schema without referencing MonoGame/game code;
- future scalar settings remain cheap to add.

**Cons**

- values remain strings at the storage boundary and are typed by `ConfigManager`, as they are today.

### B. Put configuration tables in `songs.db`

Rejected. It couples durable user preferences to a database whose existing service may recreate it for catalog recovery and mixes unrelated ownership.

### C. Add a normalized EF Core config model

Rejected. A config `DbContext` introduces migrations and mapping work for values that are never queried relationally.

### D. Store one JSON document inside SQLite

Rejected. It makes recorder overrides less direct and provides little benefit over a JSON settings file.

## Design decision

Use approach A.

```text
Stages / gameplay / BaseGame
          |
          v
     IConfigManager
          |
          v
      ConfigManager -------------------> ConfigData (runtime truth)
          |
          +--- SqliteConfigStore ------> <app-data>/config.db
          |
          +--- legacy INI importer ----> <app-data>/Config.ini
                  (only if config.db is absent)

VideoRecorder
  Program doctor + RecordingSandbox
          |
          +--- read source config.db rows
          +--- validate/patch rows
          +--- write fresh sandbox config.db

E2E
  pre-launch Config.ini -> real bootstrap path
  post-launch config.db -> ConfigManager assertions/artifact
```

There is no second configuration domain object and no shared config database with the song catalog.

## SQLite storage contract

Add:

```text
<AppDataRoot>/config.db
```

through `AppPaths.GetConfigDatabasePath()`. Rename the old INI path helper to `GetLegacyConfigFilePath()` so production code cannot confuse it with the live store.

The v1 schema is intentionally tiny:

```sql
CREATE TABLE ConfigEntries (
    Key   TEXT PRIMARY KEY NOT NULL,
    Value TEXT NOT NULL
);

PRAGMA user_version = 1;
```

No EF `DbContext`, migrations assembly, repository pattern, or general settings framework is required.

### Store responsibilities

Add one internal concrete `SqliteConfigStore` with a narrow shape equivalent to:

```csharp
internal sealed class SqliteConfigStore
{
    public SqliteConfigStore(string databasePath);
    public bool Exists { get; }
    public IReadOnlyDictionary<string, string> Load();
    public void Save(IReadOnlyDictionary<string, string> entries);
}
```

Do not add `IConfigStore` merely because there is one implementation. ConfigManager tests can use explicit temporary database/legacy paths and deterministic invalid path states. Introduce an interface only if implementation proves a fake is genuinely required; if so, keep it internal and limited to these three operations.

`Save` must:

1. create the parent directory when needed;
2. create the v1 database/table when the database does not yet exist;
3. begin a transaction;
4. replace the complete `ConfigEntries` snapshot;
5. commit only after every entry is written.

A full snapshot is intentional. Configuration is tiny, edits are already debounced, and snapshot replacement removes stale dynamic keys without delete bookkeeping.

`Load` fails clearly if an existing file is not a readable supported configuration database. Do not silently delete it or fall back to a potentially stale `Config.ini` once `config.db` exists.

Do not enable WAL or add tuning pragmas for this settings database.

## Persisted key contract

Keep the existing logical key names so migration remains mechanical. SQLite covers what current `ConfigManager.SaveConfig` persists:

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

`DTXPath` is legacy INI compatibility input and an in-memory mirror of `SongRoot.0`; do not persist a duplicate SQLite row.

Do not opportunistically add currently non-persisted `ConfigData` fields such as volume/buffer fields in HPA-190.

Use invariant integer formatting and one canonical boolean format when writing rows.

## ConfigManager lifecycle

### Startup

Change `ConfigManager` to own explicit database/legacy paths rather than receiving a save path from callers.

```text
LoadConfig()
  |
  +-- config.db exists
  |     -> load ConfigEntries
  |     -> apply typed values to ConfigData
  |
  +-- config.db missing + Config.ini exists
  |     -> parse legacy INI using current compatibility rules
  |     -> normalize/migrate
  |     -> save canonical config.db
  |
  +-- neither exists
        -> defaults
        -> normalize
        -> save canonical config.db
```

After database load or legacy import, retain existing normalization and generated-Game-API-key behavior. If those operations alter values, persist the corrected SQLite snapshot immediately as today.

### Legacy INI import

Keep current permissive behavior:

- ignore sections/comments/unknown lines;
- split only on the first `=`;
- accept legacy `DTXPath` when indexed `SongRoot.N` entries are absent;
- keep current boolean parsing and binding validation;
- keep current skin/song-root normalization rules.

The import is one-way. Once `config.db` exists, startup ignores `Config.ini`, even if the INI changes later.

Do not delete, rename, or rewrite the source INI during import.

### Logical snapshot builder

Refactor the current `SaveConfig` serialization into one canonical `BuildPersistedEntries()` operation. It emits the same persisted settings minus `DTXPath`; physical INI formatting is no longer part of normal saves.

Reuse `ParseConfigLine` and existing song-root finalization for both database rows and legacy INI assignments. Do not build a second typed deserializer.

### Runtime edits

Preserve existing behavior:

- `ConfigData` changes immediately;
- normal setters mark one deferred save dirty;
- `FlushPendingSave()` writes one complete SQLite snapshot;
- failed deferred writes keep the dirty flag set for retry;
- song-root Apply persists before live reload and restores prior in-memory roots if persistence fails;
- events remain unchanged.

Replace `_pendingSavePath` / `_loadedConfigPath` with a simple dirty flag.

## Pathless `IConfigManager` cleanup must be compile-green

Use these runtime signatures:

```text
LoadConfig()
SetSongRoots(roots)
SetScrollSpeed(percent)
AdjustScrollSpeed(stepDelta)
SetSkinPath(skinPath)
FlushPendingSave()
```

Remove public `SaveConfig(filePath)` in favor of ConfigManager's private persistence operation.

Change the interface and all known production callers in the same implementation checkpoint:

- `Game1.cs`;
- `ConfigStage.cs`;
- `PerformanceStage.cs`;
- `SongSelectionStage.cs`.

Retarget all compile-time mocks/fakes/assertions that use the old signatures in the same checkpoint. Before committing, scan the repository for:

```text
GetConfigFilePath(
LoadConfig(
SaveConfig(
AdjustScrollSpeed(
SetScrollSpeed(
SetSkinPath(
SetSongRoots(
```

Classify every hit as updated runtime code, legacy/bootstrap code, unrelated API with the same method name, or historical docs. Do not use compatibility overloads just to keep old tests compiling.

## Crash-report privacy integration

`CrashContextPublisher.RegisterSensitivePrefixes` must register:

```text
AppPaths.GetConfigDatabasePath()
AppPaths.GetLegacyConfigFilePath()
```

alongside existing app-data/song/cache/crash-report paths.

This is not new telemetry or hardening scope; it preserves the existing sanitizer boundary after the authoritative config file changes name.

## Recorder integration

The recorder migrates with the authoritative store.

### RecordingSandbox

Source contract becomes:

```text
<source-app-data>/config.db
```

The sandbox:

1. opens source DB read-only;
2. requires `PRAGMA user_version = 1` and `ConfigEntries`;
3. reads rows into a dictionary rather than copying the database file;
4. validates:
   - at least one indexed `SongRoot.N`;
   - every song root absolute;
   - absolute `SystemSkinRoot`;
   - `SkinPath` is `Default` or absolute;
5. does **not** require `DTXPath`;
6. applies recorder-owned overrides:
   - `EnableGameApi=True`;
   - fresh `GameApiPort` / `GameApiKey`;
   - `AutoPlay=True`;
   - `NoFail=True`;
   - `ScreenWidth=1280`;
   - `ScreenHeight=720`;
   - `FullScreen=False`;
7. writes a brand-new sandbox `config.db` with the v1 schema.

Do not copy source `config.db`, `-wal`, or `-shm` files. Do not write a compatibility INI into the sandbox.

### Doctor

`Program.RunDoctorAsync` must use the same source filename/validation contract. Its `Source config` gate checks `config.db`, then calls `RecordingSandbox.ValidateSourceConfig`.

Retarget recorder fixtures in:

- `ProgramTests`;
- `RecorderCommandLineTests`;
- `RecorderGameLaunchPolicyTests`;
- `RecordingSandboxTests`.

`DTXMania.VideoRecorder` may add a direct `Microsoft.Data.Sqlite` package reference. Do not reference `DTXMania.Game` or create a shared settings assembly.

`dtx-video` command syntax, workflow, OBS behavior, and app-data override behavior remain unchanged.

## E2E integration

Keep the throwaway fixture bootstrap simple:

```text
E2EFixtureBuilder writes Config.ini
-> game launches with DTXMANIA_APPDATA_ROOT=<fixture>/appdata
-> ConfigManager imports Config.ini and creates config.db
-> runtime edits update config.db only
```

The fixture should distinguish the paths explicitly, e.g. `LegacyConfigPath` and `ConfigDatabasePath`, rather than continuing to call the legacy path `ConfigPath` after it stops being authoritative.

### Post-run persistence assertions

Do not inspect old INI text after launch. For assertions such as Drum Mapping persistence:

1. preserve the current process/stage flow;
2. point the test process `DTXMANIA_APPDATA_ROOT` at `fixture.AppDataRoot` within a try/finally;
3. create a normal `ConfigManager` and call `LoadConfig()`;
4. assert `Config.KeyBindings`, `UnboundDrum*`, or other typed runtime state;
5. restore the prior environment variable.

E2E disables test parallelization, so this temporary process-level environment override does not race another E2E test.

`MidiGameplaySmokeTests` may continue patching `LegacyConfigPath` before the first launch because that is specifically exercising bootstrap input.

`E2EArtifactWriter.CopyFixtureFiles` should copy `config.db` as the post-run configuration artifact (when present), plus the legacy INI only when retaining bootstrap evidence is useful. Do not treat the INI copy as persisted state.

`GameplayAutoPlaySmokeTests` should stop naming pre/post-run INI snapshots as authoritative config. If a profile needs persisted config evidence after launch, use the database artifact/typed load path.

Do not create an E2E SQLite store/writer. E2E already references the game project and should reuse `ConfigManager` for interpretation.

## Failure behavior

Keep failure handling simple:

- unreadable/unsupported existing `config.db` at game startup: fail/log; do not fall back to INI;
- SQLite save failure during deferred flush: keep dirty and retry next flush;
- SQLite save failure during song-root Apply: restore prior roots and return `PersistenceFailed`;
- legacy INI import/save failure before first DB exists: surface the error; do not leave a usable partial database;
- missing `config.db` in recorder source app-data: fail with the existing “open CX once and exit normally” recovery direction.

No corruption-recovery framework is required.

## Testing strategy

### Store

Use real temporary SQLite files for:

- create/save/load round trip;
- replacement removes stale keys;
- user version/schema validation;
- failed initial creation does not leave a usable partial DB.

Prefer deterministic invalid path shapes over timing/locking tests.

### ConfigManager + caller migration

Prove:

```text
no DB + no INI -> defaults -> config.db
no DB + legacy INI -> import -> config.db -> INI bytes unchanged
config.db + conflicting INI -> database wins
DTXPath-only INI -> SongRoot.0
normalization/API key corrections -> persisted DB rows
edit + FlushPendingSave -> DB changes
failed flush -> dirty remains -> later retry succeeds
failed SetSongRoots -> in-memory roots restored
invalid existing DB -> no INI fallback
```

Retain behavioral tests for bindings, MIDI thresholds, events, clamping, path normalization, and skin token behavior. Update every mock/fake to the pathless interface.

### Recorder

Run the full recorder suite after retargeting all source config fixtures, not just `RecordingSandboxTests`. Include a doctor test proving a valid `config.db` is accepted without `Config.ini`.

### E2E

Run the E2E suite as the real persistence proof, especially Drum Mapping:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj \
  --configuration Debug \
  --filter "Category=E2E"
```

On Windows, this is the authoritative native gameplay persistence gate. Keep pre-launch INI authoring where useful, but post-run persistence evidence must come from `config.db` / `ConfigManager`.

## Acceptance criteria

HPA-190 is complete when:

1. fresh CX launch creates and subsequently reloads `config.db` without creating/updating `Config.ini` unless INI existed as bootstrap input;
2. legacy `Config.ini` imports exactly once when `config.db` is absent and source bytes remain unchanged;
3. later CX edits survive restart through SQLite even when old INI values conflict;
4. config-stage edits, gameplay/Song Select scroll-speed hotkeys, bindings, song roots, normalization, and generated API keys keep existing behavior;
5. `songs.db` is unchanged and independent;
6. crash sanitizer registers the live config DB and retained legacy INI path;
7. `dtx-video doctor` and `RecordingSandbox` use `config.db` with no authoritative INI dependency;
8. recorder tests pass with SQLite source fixtures;
9. E2E Drum Mapping proves a runtime edit persisted by reloading through `ConfigManager`, and E2E artifacts retain `config.db`;
10. Windows/macOS game/test builds remain green and the native Windows `Category=E2E` gate passes.

## Out of scope

- configuration profiles or multiple users;
- cloud sync;
- settings encryption or a secrets vault;
- normalized per-category EF entities;
- config EF migrations or a general database migration framework beyond v1 validation;
- merging `config.db` into `songs.db`;
- a new settings/shared assembly;
- a new E2E SQLite persistence implementation;
- in-game Import/Export Config UI changes;
- continuously mirroring SQLite changes back to `Config.ini`;
- changing currently non-persisted settings behavior;
- recorder OBS/media hardening from HPA-504/HPA-505/HPA-506/HPA-507.
