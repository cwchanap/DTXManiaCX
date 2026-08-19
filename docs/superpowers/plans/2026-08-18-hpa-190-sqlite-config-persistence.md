# HPA-190 SQLite Config Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Keep HPA-190 as a persistence migration; do not introduce an EF settings model, config profiles, a shared config/song database, or a new settings assembly.

**Goal:** Make `<app-data>/config.db` the sole authoritative CX configuration store, import `Config.ini` once when the database is absent, migrate every path-shaped `IConfigManager` caller in one compile-green slice, and preserve recorder/E2E behavior on the SQLite-backed contract.

**Architecture:** `ConfigData` remains runtime truth. `ConfigManager` keeps parsing, validation, normalization, events, and deferred-save semantics while one concrete `SqliteConfigStore` replaces normal INI writes. `Config.ini` remains bootstrap input only. The recorder duplicates only the tiny v1 SQLite row contract; E2E reuses the real `ConfigManager` for post-run assertions.

**Tech Stack:** .NET 8, C#, MonoGame, `Microsoft.Data.Sqlite`, existing EF-backed `songs.db`, xUnit, existing `DTXMania.E2E` harness.

**Spec:** `docs/superpowers/specs/2026-08-18-hpa-190-sqlite-config-persistence-design.md`

**Expected effort:** 2–3 engineer days.

## Global constraints

- `ConfigData` remains the live runtime truth; do not add a second settings model.
- Use a separate `<app-data>/config.db`; never put settings in `songs.db`.
- v1 schema is exactly `ConfigEntries(Key TEXT PRIMARY KEY NOT NULL, Value TEXT NOT NULL)` plus `PRAGMA user_version = 1`.
- Use direct `Microsoft.Data.Sqlite`; do not create a config `DbContext` or migration project.
- Persist the settings current `ConfigManager.SaveConfig` persists, minus duplicate `DTXPath`.
- `DTXPath` remains legacy INI input/in-memory compatibility only; SQLite persists `SongRoot.N`.
- Existing `config.db` always wins; never fall back to INI if the database exists but cannot be read.
- Leave imported `Config.ini` untouched; do not continuously mirror SQLite back to INI.
- Preserve path normalization, bindings, MIDI thresholds, generated API key, events, clamping, deferred flush, and song-root rollback behavior.
- Remove obsolete storage-path arguments from internal `IConfigManager` APIs; do not add compatibility overloads.
- Change the interface and every production caller/test fake in the same compile-green checkpoint.
- Preserve crash-report redaction for both `config.db` and retained legacy INI.
- Recorder doctor and sandbox must use `config.db`; no authoritative INI dependency remains after migration.
- Recorder reads/writes rows; do not copy source `config.db`, `-wal`, or `-shm`.
- E2E may author INI before first launch, but post-launch persistence assertions must use `config.db` / `ConfigManager`.
- Do not change `SongDbContext`, `SongDatabaseService`, `RecordWorkflow`, OBS behavior, or recorder output policy.

## Planned files

```text
Create:
  DTXMania.Game/Lib/Config/SqliteConfigStore.cs
  DTXMania.Test/Config/SqliteConfigStoreTests.cs

Config persistence + runtime callers:
  DTXMania.Game/Lib/Utilities/AppPaths.cs
  DTXMania.Game/Lib/Config/IConfigManager.cs
  DTXMania.Game/Lib/Config/ConfigManager.cs
  DTXMania.Game/Game1.cs
  DTXMania.Game/Lib/Stage/ConfigStage.cs
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  DTXMania.Game/Lib/Stage/SongSelectionStage.cs
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs

Known affected game tests:
  DTXMania.Test/Utilities/AppPathsTests.cs
  DTXMania.Test/Config/ConfigManagerTests.cs
  DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs
  DTXMania.Test/Config/ConfigManagerSkinPathTests.cs
  DTXMania.Test/Config/SongRootConfigTests.cs
  DTXMania.Test/Config/PlaySpeedAndPitchConfigTests.cs
  DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs
  DTXMania.Test/BaseGameTests.cs
  DTXMania.Test/Config/ConfigStageLogicTests.cs
  DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs
  DTXMania.Test/Stage/Performance/PerformanceStageCoverageTests.cs
  DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs
  DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs
  DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
  DTXMania.Test/Stage/ConfigStageSongFolderFormatTests.cs
  DTXMania.Test/Stage/ConfigStageNxImportTests.cs
  DTXMania.Test/Stage/ConfigStageSongOperationAdditionalTests.cs
  DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs

Recorder:
  DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
  DTXMania.VideoRecorder/Program.cs
  DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs
  DTXMania.VideoRecorder.Tests/ProgramTests.cs
  DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs
  DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs
  DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs

E2E:
  DTXMania.E2E/Fixtures/E2EFixture.cs
  DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs
  DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs
  DTXMania.E2E/Support/E2EArtifactWriter.cs
  DTXMania.E2E/DrumMappingStageSmokeTests.cs
  DTXMania.E2E/GameplayAutoPlaySmokeTests.cs
  DTXMania.E2E/MidiGameplaySmokeTests.cs

Normally unchanged:
  DTXMania.Game/Lib/Config/ConfigData.cs
  DTXMania.Game/Lib/Song/Entities/SongDbContext.cs
  DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs
  DTXMania.VideoRecorder/Workflow/**
  DTXMania.VideoRecorder/Obs/**
  .github/workflows/**
```

This is the known inventory, not permission to ignore new compile-time hits. Before each interface-changing commit, run the repository scans listed in Task 2 and classify every result.

## Risks to keep visible

- A stale legacy INI intentionally remains on disk; database-first load is what prevents it from winning later.
- `IConfigManager` is widely used by stage tests/fakes; changing it before caller migration creates an uncompilable checkpoint.
- `PerformanceStage` and `SongSelectionStage` persist hotkey scroll-speed changes outside ConfigStage.
- Recorder `Program.RunDoctorAsync` independently checks `Config.ini`; changing only `RecordingSandbox` is insufficient.
- E2E currently treats INI as post-run evidence; those checks become false after the first SQLite-backed launch.
- Crash sanitizer currently registers the old config path; moving persistence without moving that registration weakens existing privacy coverage.
- `songs.db` recovery can recreate the catalog; config must not share that lifecycle.

---

## Task 1: Add the concrete SQLite config store and explicit paths

**Deliverable:** v1 store exists and is independently tested; normal ConfigManager behavior is unchanged.

**Files:**
- Create: `DTXMania.Game/Lib/Config/SqliteConfigStore.cs`
- Create: `DTXMania.Test/Config/SqliteConfigStoreTests.cs`
- Modify: `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- Modify: `DTXMania.Test/Utilities/AppPathsTests.cs`

- [ ] **Step 1: Add failing path tests**

Pin:

```csharp
AppPaths.GetConfigDatabasePath();   // <app-data>/config.db
AppPaths.GetLegacyConfigFilePath(); // <app-data>/Config.ini
```

Both must honor `DTXMANIA_APPDATA_ROOT` and be siblings under the resolved app-data root.

- [ ] **Step 2: Add failing real-SQLite store tests**

Cover:

```text
Save_NewDatabase_CreatesVersion1SchemaAndRows
Load_RoundTripsEntries
Save_ReplacesSnapshotAndRemovesStaleRows
Load_ExistingUnsupportedUserVersion_Throws
Load_ExistingDatabaseWithoutConfigEntries_Throws
Save_FailedInitialCreate_DoesNotLeaveUsablePartialSnapshot
```

Use temporary files and inspect `PRAGMA user_version`/rows. Use deterministic invalid path shapes, not timing-sensitive locks.

- [ ] **Step 3: Implement only `SqliteConfigStore`**

Use one concrete internal class:

```csharp
internal sealed class SqliteConfigStore
{
    public SqliteConfigStore(string databasePath);
    public bool Exists { get; }
    public IReadOnlyDictionary<string, string> Load();
    public void Save(IReadOnlyDictionary<string, string> entries);
}
```

Do **not** introduce `IConfigStore` by default. If a later test genuinely cannot be expressed with the concrete store/path, stop and justify the interface before adding it.

`Load()` opens read-only, requires user version 1 and `ConfigEntries`, then returns all rows.

`Save()` creates the parent, opens read/write-create, creates v1 schema as needed, deletes old rows, inserts the complete snapshot in one transaction, and commits. Do not enable WAL/tuning pragmas.

If first creation fails, best-effort remove only newly-created DB/journal sidecars; never delete a DB that existed before the save.

- [ ] **Step 4: Run focused tests**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug \
  --filter "FullyQualifiedName~SqliteConfigStoreTests|FullyQualifiedName~AppPathsTests"
```

Windows uses `DTXMania.Test/DTXMania.Test.csproj` with the same filter.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Config/SqliteConfigStore.cs \
  DTXMania.Game/Lib/Utilities/AppPaths.cs \
  DTXMania.Test/Config/SqliteConfigStoreTests.cs \
  DTXMania.Test/Utilities/AppPathsTests.cs
git commit -m "feat: add SQLite configuration store"
```

---

## Task 2: Make ConfigManager database-authoritative and migrate all game callers atomically

**Deliverable:** game project compiles with pathless `IConfigManager`; SQLite is authoritative; every known production caller/test fake is migrated in this same checkpoint.

**Files:** all files under “Config persistence + runtime callers” and “Known affected game tests” above.

- [ ] **Step 1: Add/retarget migration tests before behavior changes**

Required authority cases:

```text
LoadConfig_NoDatabaseNoIni_CreatesDatabaseFromDefaults
LoadConfig_NoDatabaseLegacyIni_ImportsAndCreatesDatabase
LoadConfig_LegacyIniImport_DoesNotModifyIniBytes
LoadConfig_DatabaseAndConflictingIni_DatabaseWins
LoadConfig_DtxPathOnlyLegacyIni_PersistsSongRootZero
LoadConfig_NormalizedSkinOrSongRoot_PersistsCorrectionToDatabase
LoadConfig_ApiEnabledWithoutKey_GeneratesAndPersistsKey
FlushPendingSave_ChangedSetting_UpdatesDatabase
FlushPendingSave_FailedWrite_RemainsDirtyAndCanRetry
SetSongRoots_FailedDatabaseWrite_RestoresPreviousRoots
LoadConfig_ExistingInvalidDatabase_DoesNotFallbackToLegacyIni
```

For deterministic post-load write failure without a fake store: after a successful load, remove the temporary DB and create a directory at the same `config.db` path; flush/SetSongRoots must fail. Remove that directory and retry to prove recovery. Do not add an interface only for failure injection.

Retain existing binding, unbound-button, MIDI threshold, scroll-speed, skin-token, path-normalization, play-speed/pitch, and system-key behavioral coverage; change only persistence assertions/signatures.

- [ ] **Step 2: Refactor serialization into canonical rows**

Add one ConfigManager helper conceptually:

```csharp
IReadOnlyDictionary<string, string> BuildPersistedEntries();
```

It emits the current `SaveConfig` contract using invariant formatting, including dynamic `SongRoot.N`, bindings/unbound entries, system keys, and positive MIDI thresholds. Do not emit `DTXPath` to SQLite.

Reuse `ParseConfigLine` and existing song-root finalization for both DB rows and INI bootstrap. Do not create a second typed deserializer.

- [ ] **Step 3: Implement database-first `LoadConfig()`**

Default construction owns:

```text
SqliteConfigStore(AppPaths.GetConfigDatabasePath())
legacy INI path = AppPaths.GetLegacyConfigFilePath()
SongRootPolicy.ForCurrentPlatform()
```

Keep an internal test constructor accepting explicit database path, legacy path, `SongRootPolicy`, and logger. Preserve the existing explicit `baseDir` normalization seam.

Required load order:

```text
prepare ConfigData collections
-> DB exists: load rows; any load failure stops here
-> else INI exists: parse legacy INI
-> else defaults
-> finalize song roots
-> normalize paths
-> generate API key if required
-> persist first DB and/or corrections
```

Never consult INI after an existing DB load fails.

- [ ] **Step 4: Replace path-shaped save state**

Use a `_savePending` boolean, not `_pendingSavePath` / `_loadedConfigPath`.

`FlushPendingSave()` saves the full snapshot, clears dirty only on success, and leaves dirty on failure.

`SetSongRoots` keeps immediate persist + rollback + post-success event semantics.

- [ ] **Step 5: Change `IConfigManager` and every production caller together**

Target interface:

```csharp
void LoadConfig();
SongRootUpdateResult SetSongRoots(IReadOnlyList<string> roots);
void SetScrollSpeed(int percent);
void AdjustScrollSpeed(int stepDelta);
void SetSkinPath(string skinPath);
void FlushPendingSave();
```

Remove public `SaveConfig(string)` in favor of private persistence.

In the same working tree update:

```text
Game1.cs
ConfigStage.cs
PerformanceStage.cs
SongSelectionStage.cs
```

Specifically remove `AppPaths.GetConfigFilePath()` from performance/Song Select scroll-speed hotkeys, not only ConfigStage.

- [ ] **Step 6: Preserve crash-path sanitization**

In `CrashContextPublisher.RegisterSensitivePrefixes`, replace the old live config registration with:

```text
GetConfigDatabasePath
GetLegacyConfigFilePath
```

Keep the existing app-data/songs/cache/crash-report path registrations. Update `CrashContextPublisherTests` to pin both config paths.

- [ ] **Step 7: Retarget every compile-time fake/assertion**

At minimum update the known affected test files listed above. `DrumConfigStageTests` must replace the old `StubConfigManager` signatures and `_pendingSavePath` reflection with the new dirty-state contract.

Performance/Song Select tests must verify `AdjustScrollSpeed(+/-1)` without a string path.

- [ ] **Step 8: Run mandatory repository scan before committing**

Search:

```bash
rg 'GetConfigFilePath\(|LoadConfig\(|SaveConfig\(|AdjustScrollSpeed\(|SetScrollSpeed\(|SetSkinPath\(|SetSongRoots\(' \
  DTXMania.Game DTXMania.Test DTXMania.E2E DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests
```

For every hit, explicitly classify it as migrated code, intentional legacy-bootstrap use, unrelated same-name API, or remaining bug. Do not commit until game/test projects compile with the new interface.

- [ ] **Step 9: Run focused + build gate**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug \
  --filter "FullyQualifiedName~ConfigManager|FullyQualifiedName~PerformanceStage|FullyQualifiedName~SongSelectionStage|FullyQualifiedName~DrumConfigStage|FullyQualifiedName~CrashContextPublisher"
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

Windows uses matching Windows projects.

- [ ] **Step 10: Commit the compile-green caller slice**

Stage all production/test files changed by this task together. Do not split the interface definition from callers into separate commits.

```bash
git commit -m "feat: persist CX configuration in SQLite"
```

---

## Task 3: Migrate the complete recorder source-config surface

**Deliverable:** `RecordingSandbox` and `dtx-video doctor` both use `config.db`; full recorder tests have no authoritative INI fixtures.

**Files:**
- `DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj`
- `DTXMania.VideoRecorder/Program.cs`
- `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`
- `DTXMania.VideoRecorder.Tests/ProgramTests.cs`
- `DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs`
- `DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs`
- `DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs`

- [ ] **Step 1: Add direct SQLite dependency to recorder**

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.18" />
```

Do not reference `DTXMania.Game` or create a shared persistence assembly.

- [ ] **Step 2: Retarget all recorder source fixtures to v1 SQLite**

Create local test helpers for the two-column DB in recorder tests. Do not move those helpers into production or E2E.

Known INI fixtures to replace include `ProgramTests`, `RecorderCommandLineTests`, and `RecorderGameLaunchPolicyTests`, not only `RecordingSandboxTests`.

- [ ] **Step 3: Pin sandbox behavior with tests**

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

- [ ] **Step 4: Replace `RecordingSandbox` INI parsing/patching**

Source/sandbox filename becomes `config.db`.

Read source read-only, require user version 1 + `ConfigEntries`, then validate:

```text
at least one indexed SongRoot.N
all song roots absolute
SystemSkinRoot present + absolute
SkinPath nonblank and either Default or absolute
```

Remove `DTXPath` validation; SQLite does not persist it.

Patch existing recorder-owned keys plus fresh API port/key and write a brand-new v1 sandbox database. Do not copy source DB or sidecars and do not create sandbox INI.

- [ ] **Step 5: Update `Program.RunDoctorAsync`**

Its `Source config` gate must check `<SourceAppDataRoot>/config.db`, then call `RecordingSandbox.ValidateSourceConfig`. Add a test showing doctor/source validation succeeds with a valid DB and no INI.

Update `ProgramTests` sandbox-discovery helper to inspect sandbox `config.db` state rather than looking for a copied INI marker.

- [ ] **Step 6: Run full recorder suite**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
```

The full suite is required because source-config fixtures exist outside `RecordingSandboxTests`.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat: migrate recorder configuration to SQLite"
```

---

## Task 4: Migrate E2E persistence evidence without adding another SQL implementation

**Deliverable:** disposable INI bootstrap still works, but post-launch E2E assertions/artifacts use authoritative SQLite state; Windows E2E proves a real runtime edit persists.

**Files:**
- `DTXMania.E2E/Fixtures/E2EFixture.cs`
- `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`
- `DTXMania.E2E/Support/E2EArtifactWriter.cs`
- `DTXMania.E2E/DrumMappingStageSmokeTests.cs`
- `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`
- `DTXMania.E2E/MidiGameplaySmokeTests.cs`

- [ ] **Step 1: Make fixture path semantics explicit**

Keep pre-launch bootstrap INI, but rename/extend fixture paths conceptually to:

```text
LegacyConfigPath   = <appdata>/Config.ini
ConfigDatabasePath = <appdata>/config.db
```

`E2EFixtureBuilder` may continue writing legacy INI because fresh sandbox startup should exercise one-time import.

Update fixture tests to verify the bootstrap file, then load authoritative state through normal `ConfigManager` after pointing `DTXMANIA_APPDATA_ROOT` at the fixture root.

- [ ] **Step 2: Add a tiny E2E ConfigManager read pattern, not SQLite code**

For post-run assertions:

```csharp
var previous = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
try
{
    Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", fixture.AppDataRoot);
    var configManager = new ConfigManager();
    configManager.LoadConfig();
    // assert typed ConfigData
}
finally
{
    Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previous);
}
```

E2E has assembly-level parallelization disabled, so this process-level override is safe within this project. Factor this into an existing E2E support helper only if repetition warrants it; do not create a SQL reader/writer.

- [ ] **Step 3: Retarget Drum Mapping persistence assertions**

Replace post-run `File.ReadAllText(fixture.ConfigPath)` checks with typed `ConfigManager.Config.KeyBindings` / unbound state.

The bind smoke must prove the new binding exists after stage exit. The reset smoke must prove the custom binding is absent after reset + flush.

- [ ] **Step 4: Retarget artifacts**

`E2EArtifactWriter.CopyFixtureFiles` must copy `config.db` as post-run config evidence when present. It may additionally copy legacy INI as `bootstrap-config.ini`, but must not label it as persisted state.

`GameplayAutoPlaySmokeTests` should stop treating `.ini` snapshots as authoritative after launch. Keep only pre-launch bootstrap evidence when useful.

- [ ] **Step 5: Preserve valid bootstrap mutation**

`MidiGameplaySmokeTests` may still patch `LegacyConfigPath` before starting the game. Rename references for clarity; no behavior expansion is needed.

- [ ] **Step 6: Run E2E support + native gameplay persistence**

At minimum:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug \
  --filter "Category=E2E"
```

The native Windows E2E job is the authoritative persistence acceptance gate because it exercises a real game process, stage input, deferred flush, and subsequent on-disk reload.

- [ ] **Step 7: Commit**

```bash
git commit -m "test: migrate E2E config evidence to SQLite"
```

---

## Task 5: Final migration and cross-project verification

**Deliverable:** no hidden INI authority remains; all affected projects are green.

- [ ] **Step 1: Run fresh-install acceptance**

```text
empty app-data
-> launch CX
-> config.db created
-> no Config.ini created
-> edit one setting
-> exit/relaunch
-> setting reloads from config.db
```

- [ ] **Step 2: Run legacy upgrade acceptance**

```text
Config.ini only
-> launch CX
-> values imported into config.db
-> original INI bytes unchanged
-> edit a visible setting
-> exit
-> mutate old INI to conflicting value
-> relaunch
-> DB value wins
```

Automated coverage must include `DTXPath`-only INI -> `SongRoot.0`.

- [ ] **Step 3: Run recorder isolation acceptance**

```text
source app-data has current config.db and no Config.ini
-> source validation/doctor succeeds
-> sandbox gets a fresh config.db
-> source DB unchanged
-> no DB/WAL/SHM physical copy
```

No live OBS recording is required solely for HPA-190 if the existing recorder workflow tests stay green.

- [ ] **Step 4: Run full suites/builds**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
```

Windows/CI:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Debug
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "Category=E2E"
```

- [ ] **Step 5: Final repository audit**

Require:

```text
production GetConfigFilePath() references: 0
normal CX Config.ini writes: 0
Config.ini runtime reads: legacy bootstrap only
config.db game ownership: ConfigManager/SqliteConfigStore
config.db recorder ownership: doctor + RecordingSandbox
E2E post-run persistence assertions: ConfigManager/config.db, not INI text
CrashContextPublisher: config.db + legacy INI redacted
SongDbContext/SongDatabaseService changes: 0
new EF config context/migration framework: 0
new shared settings assembly: 0
```

Use `rg` rather than memory. Inspect every remaining `Config.ini`, `GetConfigFilePath`, and old path-shaped interface hit.

- [ ] **Step 6: Do not create an empty verification commit**

If final verification exposes test-only corrections, commit them with the owning task. Otherwise stop after recording the evidence.

---

## Definition of done

- [ ] `<app-data>/config.db` is the sole live CX configuration store.
- [ ] Existing `Config.ini` imports only when `config.db` is absent and is never rewritten by normal CX persistence.
- [ ] Existing unreadable/unsupported `config.db` fails loudly and never falls back to INI.
- [ ] Current scalar, song-root, binding, system-key, and MIDI-threshold values round-trip through SQLite.
- [ ] `DTXPath` is not stored in SQLite; legacy `DTXPath` imports to `SongRoot.0`.
- [ ] Deferred retry and immediate song-root rollback semantics remain intact.
- [ ] `IConfigManager` callers no longer pass persistence paths, including Performance and Song Select hotkeys.
- [ ] Crash redaction covers live `config.db` and retained legacy INI.
- [ ] `songs.db` lifecycle code is untouched.
- [ ] Recorder doctor + sandbox use v1 `config.db`; all recorder source fixtures are migrated.
- [ ] E2E keeps INI only as bootstrap input and uses `ConfigManager` / `config.db` for post-run persistence evidence.
- [ ] Drum Mapping E2E proves a real runtime config edit survives flush/reload.
- [ ] Full game, recorder, and native E2E gates pass on their supported hosts.
