# HPA-190 SQLite Config Persistence Design

**Issue:** [HPA-190](https://linear.app/cwchanap/issue/HPA-190/move-configini-to-sqlite)  
**Date:** 2026-08-18  
**Status:** Proposed

## Goal

Move CX-owned configuration persistence from `Config.ini` to a small SQLite database while preserving the current runtime configuration model and edit behavior.

After HPA-190:

- `ConfigData` remains the in-memory source of truth used by stages and gameplay;
- normal CX startup reads `<app-data>/config.db`;
- normal edits and deferred flushes write `<app-data>/config.db` transactionally;
- `Config.ini` is legacy input only, used to bootstrap `config.db` when no config database exists;
- an existing CX `Config.ini` upgrades automatically on first launch, and an NX `Config.ini` can use the same compatibility path;
- the video recorder continues to build an isolated per-run configuration without mutating the developer's real app-data;
- `songs.db` remains independent from user configuration.

This is a persistence migration, not a redesign of the configuration UI, settings model, or song database.

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

The runtime contract is already good: callers consume `IConfigManager.Config`, and typed setters mutate that live object. HPA-190 should replace storage under that contract rather than creating a second settings model.

### Config file paths leak into callers

`IConfigManager` currently exposes persistence-path-shaped APIs such as:

```text
LoadConfig(filePath)
SaveConfig(filePath)
SetSongRoots(configFilePath, roots)
SetScrollSpeed(configFilePath, percent)
AdjustScrollSpeed(configFilePath, stepDelta)
SetSkinPath(configFilePath, skinPath)
```

`BaseGame` and `ConfigStage` consequently know about `AppPaths.GetConfigFilePath()` even though they should only request configuration operations.

Once SQLite owns persistence, keeping these parameters would preserve an obsolete abstraction. HPA-190 can remove them because CX has no backward-compatibility requirement for internal APIs.

### `songs.db` is not a safe home for configuration

`SongDatabaseService` owns `songs.db` and intentionally contains database validation/recovery paths that can recreate the song catalog when the file is invalid or incompatible. User settings must not share that lifecycle: rebuilding a song index must never discard display, input, skin, API, or gameplay settings.

Configuration therefore needs its own database file.

### The recorder currently depends on authoritative `Config.ini`

`DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs` currently reads the developer's `Config.ini`, validates presentation paths, applies recorder-owned overrides, and writes a fresh sandbox `Config.ini`.

That approach becomes incorrect once normal gameplay stops updating the INI file. Leaving it unchanged would make recordings inherit stale presentation settings after the first SQLite-backed config edit.

HPA-190 must update the recorder sandbox in the same slice.

### SQLite is already a project dependency

The game already uses SQLite through `Microsoft.EntityFrameworkCore.Sqlite`. The configuration store can use `Microsoft.Data.Sqlite` directly without adding an EF model or migration framework to the game.

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
- removed bindings/roots disappear naturally when a full snapshot replaces the prior rows;
- the recorder can patch the same small schema without referencing the MonoGame game project;
- future settings remain cheap to add.

**Cons**

- values remain strings at the storage boundary and are typed by `ConfigManager`, as they are today.

### B. Put configuration tables in `songs.db`

Rejected. It couples durable user preferences to a database whose existing service may recreate it for catalog recovery. It also mixes unrelated ownership and makes recording/app-data isolation harder to reason about.

### C. Add a normalized EF Core config model

Rejected. A table/entity per configuration category or strongly typed row model would introduce schema migrations and mapping work for values that are never queried relationally. That is unnecessary infrastructure for a local settings store.

### D. Store one JSON document inside SQLite

Rejected. It technically moves the file into SQLite but gives little benefit over a JSON settings file and makes the recorder patch path more cumbersome. The existing key/value representation already provides the right granularity.

## Design decision

Use approach A.

```text
BaseGame / ConfigStage
        |
        v
   IConfigManager
        |
        v
    ConfigManager --------------------> ConfigData (runtime truth)
        |
        +---- SqliteConfigStore ------> <app-data>/config.db
        |
        +---- legacy INI importer ----> <app-data>/Config.ini
                    (only when config.db does not exist)

VideoRecorder RecordingSandbox
        |
        +---- read source config.db entries
        +---- validate presentation paths
        +---- apply recorder-owned overrides
        +---- write fresh sandbox config.db
```

There is no second configuration domain object and no shared config database with the song catalog.

## SQLite storage contract

Add:

```text
<AppDataRoot>/config.db
```

through a new `AppPaths.GetConfigDatabasePath()` helper. Rename/retain the old INI path helper only as an explicitly legacy path, for example `GetLegacyConfigFilePath()`, so production code cannot accidentally treat `Config.ini` as the live store.

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

Add one internal `SqliteConfigStore` with a narrow contract equivalent to:

```csharp
bool Exists { get; }
IReadOnlyDictionary<string, string> Load();
void Save(IReadOnlyDictionary<string, string> entries);
```

`Save` must:

1. create the parent directory when needed;
2. create the v1 database/table when the database does not yet exist;
3. begin a transaction;
4. replace the complete `ConfigEntries` snapshot;
5. commit only after every entry is written.

A full snapshot is intentional. The configuration is tiny, edits are already debounced, and snapshot replacement removes stale dynamic keys without adding delete bookkeeping.

`Load` should fail clearly if an existing file is not a readable supported configuration database. Do not silently delete it or fall back to a potentially stale `Config.ini` once a `config.db` exists.

## Persisted key contract

Keep the existing logical key names so the migration is mechanical and easy to inspect. The SQLite snapshot should cover the fields that `ConfigManager.SaveConfig` persists today, including:

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

`DTXPath` remains a legacy INI compatibility input and an in-memory mirror of the first song root; the new database does not need to persist a duplicate `DTXPath` row.

Do not opportunistically add currently non-persisted `ConfigData` fields such as volume/buffer fields in HPA-190. That would be a separate behavior change rather than a storage migration.

Use invariant formatting for integer values and one canonical boolean format when writing SQLite rows.

## ConfigManager lifecycle

### Startup

Change `ConfigManager` to own its persistence destination rather than receiving it from callers.

Conceptually:

```text
LoadConfig()
  |
  +-- config.db exists
  |     -> load ConfigEntries
  |     -> apply typed values to ConfigData
  |
  +-- config.db missing + Config.ini exists
  |     -> parse legacy INI with current compatibility rules
  |     -> normalize/migrate values
  |     -> save canonical config.db
  |
  +-- neither exists
        -> use defaults
        -> normalize paths
        -> save canonical config.db
```

After either database load or legacy import, keep the existing normalization and generated-Game-API-key behavior. If normalization or API-key generation changes in-memory values, persist the corrected SQLite snapshot immediately as today.

### Legacy INI import

Keep the current permissive INI behavior:

- ignore sections/comments/unknown lines;
- split only on the first `=`;
- accept legacy `DTXPath` when indexed `SongRoot.N` entries are absent;
- keep current boolean parsing and binding validation;
- keep current skin/song-root normalization rules.

The import is one-way. Once `config.db` exists, startup reads it and ignores `Config.ini` even if the INI later changes. This prevents an old compatibility file from overwriting newer CX settings.

Do not delete, rename, or rewrite the source INI during import. It remains user-owned compatibility input/evidence.

### Runtime edits

Preserve the existing edit semantics:

- `ConfigData` changes immediately;
- normal setters mark one deferred save dirty;
- `FlushPendingSave()` writes one SQLite snapshot;
- song-root Apply persists before starting the library reload and restores the old in-memory roots if persistence fails;
- event behavior remains unchanged.

Replace path-based dirty state with a simple dirty flag because `ConfigManager` now owns exactly one config store.

The obsolete public `SaveConfig(filePath)` surface can become a private persistence operation.

## Internal API cleanup

Update `IConfigManager` to remove obsolete config-file path arguments:

```text
LoadConfig()
SetSongRoots(roots)
SetScrollSpeed(percent)
AdjustScrollSpeed(stepDelta)
SetSkinPath(skinPath)
FlushPendingSave()
```

Callers should not know whether persistence is SQLite, a file, or something else.

This is a deliberate breaking internal cleanup. Do not add compatibility overloads merely to preserve old tests.

## Recorder sandbox integration

The recorder must migrate with the authoritative store.

Update `RecordingSandbox` so the source contract becomes:

```text
<source-app-data>/config.db
```

The sandbox should:

1. open the source database read-only through `Microsoft.Data.Sqlite`;
2. read the `ConfigEntries` snapshot rather than copying the physical database file;
3. validate the same presentation-critical settings it validates today:
   - at least one absolute `SongRoot.N`;
   - absolute `SystemSkinRoot`;
   - `SkinPath` is either `Default` or absolute;
4. apply recorder-owned overrides:
   - `EnableGameApi=True`;
   - fresh `GameApiPort` / `GameApiKey`;
   - `AutoPlay=True`;
   - `NoFail=True`;
   - `ScreenWidth=1280`;
   - `ScreenHeight=720`;
   - `FullScreen=False`;
5. write a fresh sandbox `config.db` containing only the resulting canonical entries.

Do not copy the source SQLite file, `-wal`, or `-shm`. Reading rows and writing a fresh database keeps the existing disposable-run isolation and avoids coupling the sandbox to SQLite journaling state.

`DTXMania.VideoRecorder` may add a direct `Microsoft.Data.Sqlite` package reference. Do not make the platform-neutral recorder reference `DTXMania.Game` or create a new shared configuration assembly solely for this two-column schema.

`dtx-video` command syntax, workflow, OBS behavior, and app-data override behavior remain unchanged.

## Failure behavior

Keep failure handling simple and deterministic:

- missing `config.db` in a recorder source app-data root: fail preflight with an actionable “open CX once and exit normally” message;
- unreadable/unsupported existing `config.db` at game startup: log and fail rather than silently replacing user settings;
- SQLite save failure during ordinary deferred flush: retain the dirty flag and retry on the next flush, matching current behavior;
- SQLite save failure during song-root Apply: restore the prior in-memory roots and return `PersistenceFailed`, matching current behavior;
- legacy INI import failure before the first database exists: surface the error; do not create a partial database.

No corruption-recovery framework is required in this task.

## Testing strategy

### Config store

Add focused SQLite tests for:

- create/save/load round trip;
- replacement removes stale keys;
- a failed transaction does not expose a partial logical snapshot;
- existing unsupported/corrupt database fails rather than being silently reset.

### ConfigManager migration

Update the existing `ConfigManager` tests to prove:

```text
no DB + no INI
  -> defaults
  -> config.db created

no DB + legacy Config.ini
  -> current INI parsing/migration rules apply
  -> canonical config.db created
  -> source Config.ini left untouched

config.db + conflicting Config.ini
  -> config.db wins

edit + FlushPendingSave
  -> database row changes

DTXPath-only legacy INI
  -> imported as SongRoot.0

skin/song-root normalization
  -> corrected value persisted in config.db

Game API enabled with empty key
  -> generated key persisted in config.db
```

Keep existing behavioral tests for bindings, MIDI thresholds, events, path normalization, and setter clamping; only their persistence assertions should move from INI text to SQLite entries.

### Runtime wiring

Update BaseGame/ConfigStage tests so callers no longer pass a persistence path into `IConfigManager`.

### Recorder

Update `RecordingSandboxTests` to build a source SQLite configuration and verify:

- source entries are preserved when not recorder-owned;
- owned entries are patched in the fresh sandbox database;
- source database is unchanged;
- missing source database fails before creating a run directory;
- invalid/missing required presentation paths fail;
- no `Config.ini`, `-wal`, or `-shm` copy is required in the sandbox contract.

## Acceptance criteria

HPA-190 is complete when:

1. a fresh CX launch creates and subsequently reloads `config.db` without creating/updating `Config.ini`;
2. an existing legacy `Config.ini` is imported exactly once when `config.db` is absent;
3. a later CX edit survives restart through SQLite even when the old INI contains conflicting values;
4. config-stage edits, bindings, song roots, path normalization, and generated API keys retain their existing runtime behavior;
5. `songs.db` remains unchanged and independent;
6. `dtx-video` builds a fresh SQLite-backed sandbox from the current source configuration and its focused tests pass;
7. Windows and macOS game/test builds remain green.

## Out of scope

- configuration profiles or multiple users;
- cloud sync;
- settings encryption or a secrets vault;
- normalized per-category EF entities;
- a database migration framework beyond the v1 key/value schema;
- merging `config.db` into `songs.db`;
- a new in-game “Import Config” / “Export Config” UI;
- continuously mirroring SQLite changes back to `Config.ini`;
- changing currently non-persisted settings behavior;
- recorder OBS/media hardening from HPA-504/HPA-505/HPA-506/HPA-507.
