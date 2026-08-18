# HPA-190 SQLite Config Persistence Implementation Plan

> **For agentic workers:** use `superpowers:subagent-driven-development` or `superpowers:executing-plans`. Keep this as a persistence migration. Preserve `ConfigData` and existing config-stage behavior; do not introduce an EF settings model, config profiles, or a shared config/song database.

**Goal:** Make `<app-data>/config.db` the sole authoritative CX configuration store, import `Config.ini` once when the database is absent, remove persistence paths from `IConfigManager` callers, and migrate the video recorder sandbox to the same SQLite-backed contract.

**Architecture:** Keep `ConfigManager` as the owner of runtime `ConfigData`, validation, normalization, events, and deferred-save semantics. Add one internal direct-SQLite key/value store underneath it. Treat the current INI parser as a bootstrap importer only. Keep `songs.db` separate. Update `RecordingSandbox` to read the source `config.db` rows and write a fresh patched sandbox database rather than copying a physical SQLite file.

**Tech stack:** .NET 8, C#, `Microsoft.Data.Sqlite` / SQLite, xUnit, existing MonoGame game projects and platform-neutral `DTXMania.VideoRecorder`.

**Spec:** `docs/superpowers/specs/2026-08-18-hpa-190-sqlite-config-persistence-design.md`

**Expected effort:** 2–3 engineer days.

## Global constraints

- `ConfigData` remains the live runtime truth. Do not add a second settings object.
- Use a separate `<app-data>/config.db`; never put settings in `songs.db`.
- Use one `ConfigEntries(Key TEXT PRIMARY KEY, Value TEXT NOT NULL)` table and `PRAGMA user_version = 1`.
- Use direct `Microsoft.Data.Sqlite`; do not create an EF `DbContext` or migration project for configuration.
- Persist the same settings that current `SaveConfig` persists. Do not expand HPA-190 into volume/buffer behavior changes.
- `DTXPath` is legacy INI input/in-memory compatibility only. SQLite persists ordered `SongRoot.N` rows instead.
- If `config.db` exists, it always wins. Never re-import a conflicting/stale `Config.ini` automatically.
- Leave imported `Config.ini` untouched. Do not continuously mirror SQLite back to INI.
- Preserve current path normalization, key-binding validation, generated API-key behavior, events, and song-root rollback behavior.
- Remove obsolete persistence-path parameters from internal `IConfigManager` APIs instead of keeping compatibility overloads.
- Recorder sandboxing must continue to leave the developer's source app-data unchanged.
- The recorder must read/write rows, not copy `config.db`, `-wal`, or `-shm` files.
- Do not change recorder commands, `RecordWorkflow`, OBS integration, recording output rules, or HPA-504/505/506/507 hardening.
- Do not change `SongDbContext` or `SongDatabaseService`.

## Planned files

```text
Create:
  DTXMania.Game/Lib/Config/SqliteConfigStore.cs
  DTXMania.Test/Config/SqliteConfigStoreTests.cs

Modify:
  DTXMania.Game/Lib/Utilities/AppPaths.cs
  DTXMania.Game/Lib/Config/IConfigManager.cs
  DTXMania.Game/Lib/Config/ConfigManager.cs
  DTXMania.Game/Game1.cs
  DTXMania.Game/Lib/Stage/ConfigStage.cs

  DTXMania.Test/Utilities/AppPathsTests.cs
  DTXMania.Test/Config/ConfigManagerTests.cs
  DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs
  DTXMania.Test/Config/ConfigManagerSkinPathTests.cs
  DTXMania.Test/Config/ConfigStageLogicTests.cs
  DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs
  DTXMania.Test/BaseGameTests.cs

  DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
  DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs
  DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs

Normally unchanged:
  DTXMania.Game/Lib/Config/ConfigData.cs
  DTXMania.Game/Lib/Song/Entities/SongDbContext.cs
  DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs
  DTXMania.VideoRecorder/Workflow/**
  DTXMania.VideoRecorder/Obs/**
  .github/workflows/**
```

If the `IConfigManager` signature cleanup exposes an additional compile-time test fake, update that fake only; do not widen production scope or add compatibility overloads to silence tests.

## Risks to keep visible

- The old INI may remain on disk after migration. Database-first startup is what prevents stale values from coming back.
- The recorder currently assumes the INI is authoritative. HPA-190 is not complete until recorder source/sandbox persistence moves to SQLite too.
- `songs.db` has recovery/recreation behavior; sharing it would make config durability depend on song-index health.
- Dynamic settings (`SongRoot.N`, key mappings, MIDI thresholds) require snapshot replacement so removed keys do not linger.
- SQLite file existence alone must not cause an unreadable database to be silently reset or re-imported from stale INI.

---

## Task 1: Add the standalone SQLite config store

**Scope:** persistence primitive and app-data path only. Do not change `ConfigManager` behavior yet.

**Files:**
- Create: `DTXMania.Game/Lib/Config/SqliteConfigStore.cs`
- Create: `DTXMania.Test/Config/SqliteConfigStoreTests.cs`
- Modify: `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- Modify: `DTXMania.Test/Utilities/AppPathsTests.cs`

### 1.1 Pin app-data path names

Add:

```csharp
AppPaths.GetConfigDatabasePath() // <app-data>/config.db
AppPaths.GetLegacyConfigFilePath() // <app-data>/Config.ini
```

Retire `GetConfigFilePath()` in the same implementation branch once callers move in Task 3. Do not keep two ambiguously named live-config helpers.

Update `AppPathsTests` to prove both paths honor `DTXMANIA_APPDATA_ROOT` and remain siblings under the selected app-data root.

### 1.2 Write failing store tests first

Create focused tests for the v1 store:

```text
Save_NewDatabase_CreatesVersion1SchemaAndRows
Load_RoundTripsEntries
Save_ReplacesSnapshotAndRemovesStaleRows
Load_ExistingUnsupportedUserVersion_Throws
Load_ExistingDatabaseWithoutConfigEntries_Throws
Save_FailedInitialCreate_DoesNotLeaveUsablePartialSnapshot
```

Use a temporary directory per test and real SQLite files. Assertions should inspect `PRAGMA user_version` and row contents, not implementation-private SQL strings.

For the failed-initial-create case, use a deterministic invalid/unwritable target shape already supported by the test host (for example a path whose parent component is an existing file) rather than timing-sensitive database-lock tests.

### 1.3 Implement `SqliteConfigStore`

Keep the contract internal and narrow. A practical shape is:

```csharp
internal interface IConfigStore
{
    bool Exists { get; }
    IReadOnlyDictionary<string, string> Load();
    void Save(IReadOnlyDictionary<string, string> entries);
}

internal sealed class SqliteConfigStore : IConfigStore
{
    // one database path, v1 schema, load/save only
}
```

It may live in the same file; do not create a DI framework or repository layer.

`Load()`:

1. open the existing database read-only;
2. require `PRAGMA user_version = 1`;
3. require `ConfigEntries` to exist;
4. read every key/value row;
5. reject duplicate/impossible schema states through normal SQLite failures.

`Save()`:

1. validate arguments before opening a transaction;
2. create the parent directory;
3. open read/write-create;
4. begin one transaction;
5. create the table when needed and set `user_version = 1`;
6. delete the old rows;
7. insert the complete snapshot with parameters;
8. commit.

If a brand-new database creation fails before commit, best-effort remove the newly created database/journal sidecars so the next launch can retry legacy bootstrap. Do not delete a database that existed before the failed save.

Do not enable WAL or add tuning pragmas for a tiny settings database.

### 1.4 Run focused tests

On macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~SqliteConfigStoreTests|FullyQualifiedName~AppPathsTests"
```

On Windows, use `DTXMania.Test/DTXMania.Test.csproj` with the same filter.

### 1.5 Commit checkpoint

```bash
git add \
  DTXMania.Game/Lib/Config/SqliteConfigStore.cs \
  DTXMania.Game/Lib/Utilities/AppPaths.cs \
  DTXMania.Test/Config/SqliteConfigStoreTests.cs \
  DTXMania.Test/Utilities/AppPathsTests.cs
git commit -m "feat: add SQLite configuration store"
```

---

## Task 2: Make ConfigManager database-authoritative with one-time INI import

**Scope:** move persistence beneath the existing runtime config model. No ConfigStage/recorder changes yet.

**Files:**
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerSkinPathTests.cs`

### 2.1 Add migration-first failing tests

Before changing production behavior, add/retarget tests for these authority rules:

```text
LoadConfig_NoDatabaseNoIni_CreatesDatabaseFromDefaults
LoadConfig_NoDatabaseLegacyIni_ImportsAndCreatesDatabase
LoadConfig_LegacyIniImport_DoesNotModifyIniBytes
LoadConfig_DatabaseAndConflictingIni_DatabaseWins
LoadConfig_DtxPathOnlyLegacyIni_PersistsSongRootZero
LoadConfig_NormalizedSkinOrSongRoot_PersistsCorrectionToDatabase
LoadConfig_ApiEnabledWithoutKey_GeneratesAndPersistsKey
FlushPendingSave_ChangedSetting_UpdatesDatabase
FlushPendingSave_FailedWrite_RemainsDirtyForRetry
SetSongRoots_FailedDatabaseWrite_RestoresPreviousRoots
LoadConfig_ExistingInvalidDatabase_DoesNotFallbackToLegacyIni
```

Retain the existing binding, unbound-button, MIDI threshold, scroll-speed, skin-token, and path-normalization tests. Replace INI-text persistence assertions with `SqliteConfigStore.Load()` assertions where appropriate.

### 2.2 Give ConfigManager explicit persistence dependencies

The default constructor should compose:

```text
SqliteConfigStore(AppPaths.GetConfigDatabasePath())
legacy INI path = AppPaths.GetLegacyConfigFilePath()
SongRootPolicy.ForCurrentPlatform()
```

Keep an internal constructor for tests that accepts the store, legacy INI path, song-root policy, and logger. Preserve the existing explicit-`baseDir` load seam used by bundled-skin relocation tests; adapt it to the new no-file-path `LoadConfig` lifecycle rather than deleting that testability.

Do not make `ConfigManager` depend on `SongDatabaseService`.

### 2.3 Separate logical entries from physical format

Keep current parse/validation behavior, but stop serializing an INI document for normal saves.

Add one internal/private canonical snapshot builder in `ConfigManager`, conceptually:

```csharp
IReadOnlyDictionary<string, string> BuildPersistedEntries()
```

It should emit the current persisted contract with invariant formatting:

- scalar keys;
- `SongRoot.0..N`;
- active key bindings;
- explicit unbound lanes/buttons;
- system key bindings;
- positive MIDI velocity thresholds.

Do not emit the legacy duplicate `DTXPath` row into SQLite.

Reuse the existing `ParseConfigLine`/song-root finalization rules to apply database rows and legacy INI assignments to `ConfigData`. Do not build a second typed deserializer.

### 2.4 Implement database-first startup

Change the public lifecycle to:

```csharp
void LoadConfig();
```

and retain an internal base-directory overload for normalization tests.

Required ordering:

```text
reset/prepare ConfigData collections
-> if config store exists:
     load database entries
   else if legacy INI exists:
     parse legacy INI
   else:
     keep defaults
-> finalize song roots
-> normalize paths
-> generate Game API key when required
-> persist if this was first DB creation or normalization/key generation changed values
```

When a database exists but `Load()` fails, propagate/log the failure. Do not consult `Config.ini` as fallback.

On successful legacy import, leave the INI untouched.

### 2.5 Replace path-shaped persistence state

Update `IConfigManager`/`ConfigManager` signatures to remove obsolete file paths:

```csharp
void LoadConfig();
SongRootUpdateResult SetSongRoots(IReadOnlyList<string> roots);
void SetScrollSpeed(int percent);
void AdjustScrollSpeed(int stepDelta);
void SetSkinPath(string skinPath);
void FlushPendingSave();
```

Remove public `SaveConfig(string filePath)`; use a private `PersistConfig()` operation instead.

Replace `_pendingSavePath` / `_loadedConfigPath` with one `_savePending` boolean.

`MarkDirty()` only sets the flag. `FlushPendingSave()`:

- no-ops when clean;
- saves the complete snapshot when dirty;
- clears the flag only on success;
- logs and keeps it set on failure so a later flush retries.

### 2.6 Preserve immediate song-root rollback

`SetSongRoots(roots)` must keep today's contract:

1. validate/canonicalize;
2. return `Unchanged` without write when equivalent;
3. snapshot old roots/DTXPath;
4. mutate `ConfigData`;
5. call `PersistConfig()` immediately;
6. restore old values and return `PersistenceFailed` if the write fails;
7. raise `SongRootsChanged` only after persistence succeeds.

Do not turn song-root Apply into a deferred write because ConfigStage starts a live library reload immediately after success.

### 2.7 Run focused ConfigManager tests

On macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~ConfigManager"
```

On Windows, run the Windows test project with the same filter.

### 2.8 Commit checkpoint

```bash
git add \
  DTXMania.Game/Lib/Config/IConfigManager.cs \
  DTXMania.Game/Lib/Config/ConfigManager.cs \
  DTXMania.Test/Config/ConfigManagerTests.cs \
  DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs \
  DTXMania.Test/Config/ConfigManagerSkinPathTests.cs
git commit -m "feat: persist CX configuration in SQLite"
```

---

## Task 3: Migrate runtime callers and the recorder sandbox

**Scope:** complete the breaking internal API cleanup and protect the shipped recorder workflow from stale INI configuration.

**Files:**
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs`
- Modify: `DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj`
- Modify: `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`
- Modify: `DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs`

### 3.1 Remove config persistence paths from game callers

Update startup:

```text
ConfigManager = new ConfigManager();
ConfigManager.LoadConfig();
```

Update `ConfigStage` to call the path-free setter APIs for scroll speed, skin switching, and song-root Apply.

Delete production uses of `AppPaths.GetConfigFilePath()` and then remove that ambiguous helper from `AppPaths` if no longer referenced. `GetLegacyConfigFilePath()` should appear only in configuration bootstrap/import code and tests.

Update BaseGame/ConfigStage test fakes to implement the new interface. Keep assertions about stage behavior/events; do not add path assertions back through a different seam.

### 3.2 Add recorder SQLite dependency

Add a direct package reference to `DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj`:

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.18" />
```

Match the SQLite/EF package family already used by the game. Do not reference `DTXMania.Game` from the recorder.

### 3.3 Write recorder regression tests first

Retarget `RecordingSandboxTests` from text INI fixtures to v1 SQLite fixtures.

Cover:

```text
ValidateSourceConfig_MissingConfigDatabase_FailsBeforeRunDirectory
ValidateSourceConfig_MissingSongRoot_Fails
ValidateSourceConfig_RelativeSongRoot_Fails
ValidateSourceConfig_RelativeSystemSkinRoot_Fails
ValidateSourceConfig_DefaultSkin_IsAccepted
Create_PreservesNonOwnedSourceEntries
Create_OverridesRecorderOwnedEntries
Create_WritesFreshVersion1Database
Create_DoesNotModifySourceDatabase
Create_DoesNotRequireOrCopyWalShmOrConfigIni
```

Use test helper methods inside `RecordingSandboxTests` to create/read the two-column schema. Do not add a production shared-config package only for tests.

### 3.4 Replace recorder INI patching with row patching

Change `RecordingSandbox` source/sandbox filenames to `config.db`.

Read the source with a read-only SQLite connection and require:

```text
PRAGMA user_version = 1
ConfigEntries exists
```

Load entries into a dictionary. Keep the current presentation-path normalization gate, adapted to SQLite keys:

- at least one `SongRoot.N`, ordered/index-validated;
- every root absolute;
- `SystemSkinRoot` present and absolute;
- nonblank `SkinPath`, either `Default` or absolute.

The new database no longer persists `DTXPath`, so remove that recorder validation.

Patch the existing recorder-owned keys plus fresh API port/key in-memory. Then create a brand-new sandbox database with the v1 schema and patched rows.

Do not copy the source SQLite file or journal sidecars. Do not create a compatibility `Config.ini` inside the sandbox.

Keep `RunRoot`, `AppDataRoot`, API port/key, cleanup, and the public recorder workflow contract unchanged. Rename `ConfigPath` only if needed for clarity; avoid wider workflow churn when callers only need the app-data root/API facts.

### 3.5 Run focused runtime + recorder tests

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecordingSandboxTests"
```

On macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~BaseGameTests|FullyQualifiedName~ConfigStage"
```

On Windows, run the Windows test project with the same filter.

### 3.6 Commit checkpoint

```bash
git add \
  DTXMania.Game/Game1.cs \
  DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Game/Lib/Utilities/AppPaths.cs \
  DTXMania.Test/BaseGameTests.cs \
  DTXMania.Test/Config/ConfigStageLogicTests.cs \
  DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs \
  DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj \
  DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs \
  DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs
git commit -m "feat: migrate config consumers to SQLite"
```

---

## Task 4: Prove upgrade, restart, and cross-project behavior

**Scope:** verification only unless a failing test exposes an HPA-190 regression.

**Files:** normally no new production files.

### 4.1 Run the migration acceptance matrix

Use a temporary `DTXMANIA_APPDATA_ROOT` for each manual smoke so no developer settings are touched.

#### Fresh install

```text
empty app-data
-> launch CX
-> config.db created
-> Config.ini absent
-> exit/relaunch
-> same settings reload from config.db
```

#### Existing CX/NX INI upgrade

```text
app-data contains Config.ini only
-> launch CX
-> values imported
-> config.db created
-> original Config.ini bytes unchanged
-> edit one visible setting in ConfigStage
-> exit
-> manually make old Config.ini conflict
-> relaunch
-> SQLite-edited value wins
```

Include a `DTXPath`-only legacy fixture in automated coverage; the manual smoke only needs one representative real INI.

#### Recorder isolation

```text
source app-data contains current config.db
-> dtx-video source validation succeeds
-> create one sandbox in tests/diagnostic run
-> sandbox owns a fresh config.db
-> source DB unchanged
-> no source Config.ini dependency
```

A full live OBS recording is not required for this persistence ticket if recorder sandbox/unit tests plus existing workflow tests are green. HPA-515 already accepted the recording journey; do not turn HPA-190 into another media acceptance project.

### 4.2 Run platform-neutral recorder suite

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug
```

### 4.3 Run game tests/builds

On macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

On Windows/CI:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Debug
```

Do not edit CI unless an existing job demonstrably excludes the affected game/recorder tests.

### 4.4 Final repository audit

Search the final branch and require:

```text
production GetConfigFilePath() references: 0
normal CX writes to Config.ini: 0
Config.ini usage: legacy bootstrap/import only
config.db usage: ConfigManager + recorder sandbox contract
SongDbContext/SongDatabaseService changes: 0
```

Also verify there is no new EF config context, config migration framework, or config/song database coupling.

### 4.5 Commit checkpoint

If Task 4 requires only test adjustments, commit them with their owning test files. If no changes are needed, do not create an empty verification commit.

---

## Definition of done

- [ ] `<app-data>/config.db` is the sole live CX configuration store.
- [ ] Existing `Config.ini` imports only when `config.db` is absent.
- [ ] Legacy INI import leaves the source file untouched.
- [ ] Once the DB exists, conflicting INI values cannot override it.
- [ ] Current persisted scalar, song-root, binding, system-key, and MIDI-threshold values round-trip through SQLite.
- [ ] Deferred save retry and immediate song-root rollback semantics are preserved.
- [ ] `IConfigManager` callers no longer pass storage paths.
- [ ] `songs.db` ownership/recovery code is unchanged.
- [ ] Recorder source/sandbox configuration uses v1 `config.db` rows and never copies SQLite journal files.
- [ ] Recorder source app-data no longer requires an authoritative `Config.ini`.
- [ ] Focused config/recorder tests pass.
- [ ] Full native game test/build gate passes on the implementation host; Windows CI supplies the opposite-platform gate when needed.
