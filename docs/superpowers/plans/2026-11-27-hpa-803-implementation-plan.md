# HPA-803 — Implementation plan (one PR, <=3 engineer days)

**Design:** [declarative settings and SQLite-only configuration](../../proposals/2026-11-27-hpa-803-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-803  
**Branch:** `plan/hpa-803-declarative-config`; implement in the same PR.  
**Baseline:** `e7204eba135e0b1b201135e2a5027b6d94fa0539` (2026-11-27); recheck before coding.  
**Revision:** addresses [review 6091001518](https://github.com/cwchanap/DTXManiaCX/pull/256#pullrequestreview-6091001518) and [review 6095110317](https://github.com/cwchanap/DTXManiaCX/pull/256#pullrequestreview-6095110317).  
**Status:** planning only; none of the implementation, test or MCP checkboxes below has been executed.

## Agent contract

Follow the design and repository AGENTS.md/CLAUDE.md: .NET 10, unified unit project, one PR and incremental reviewable commits. The reviewed approach is a typed property/default, one shared descriptor definition registered in All, and explicit placement references in consuming screens. No runtime attribute/reflection catalog, copied UI metadata, new setting, migration layer, framework, schema bump, asset work or unrelated rendering/input change.

Delete INI/bootstrap work first because it is independent of the descriptor/UI change. Keep each completed phase compiling and test-green. Isolate constructor/call-site churn in a mechanical commit coordinated with fixture migration and signature deletion; do not leave a broken intermediate tree or retain a compatibility overload in the final change. Use existing helpers and tests. If a concrete blocker exceeds the three-day budget, report it in this PR instead of silently expanding scope or splitting PRs.

| Phase | Estimate |
| --- | --- |
| 0. Baseline contracts | 0.30 day |
| 1. SQLite-only startup and automation | 0.95 day |
| 2. Typed descriptors and manager | 0.75 day |
| 3. Both settings UIs | 0.55 day |
| 4. End-to-end acceptance | 0.45 day |
| Total | 3.00 days |

## 0. Baseline contracts

- [ ] Use `rg` to inventory Config.ini, LegacyConfigPath, GetLegacyConfigFilePath, legacyIniPath, ImportLegacyIni, DTXPath, RandomFromSubBox, bare AutoPlay, ConfigManager constructors, named scalar setters, ResetToDefaults and the five dead scalar properties. Include current launch/docs references and crash-report registration. Distinguish live graphics APIs, FFmpeg reset behavior, key-binding defaults, NX score metadata and skin INIs.
- [ ] Run focused Config and QuickSettingsPopup tests plus recorder/E2E-support suites; record any existing failures. Use current headless ConfigStageTestFactory and popup CreateItems seams.
- [ ] Commit golden UI contract tests from the baseline: per Config category and for the popup, exact ordered GetDisplayText() + Description sequences for defaults and one representative non-default config. Use literal expected results, deterministic roots/skin fixtures and controlled FFmpeg availability. Include special rows; a headless setup that omits Skin must not be claimed as full menu coverage. Retain existing skin-switcher coverage.
- [ ] Capture same-skin 1280x720 baseline System/Drums pages, scrolled controls and Quick Settings popup through MCP. Keep images outside the tracked tree. These are the visual baseline for final agent inspection; golden text tests do not replace them.

## 1. SQLite-only startup and automation

Primary files: ConfigManager.cs, AppPaths.cs, ConfigDbSchema.cs/SqliteConfigStore.cs; Diagnostics/CrashReporting/CrashContextPublisher.cs; E2E Fixtures/{E2EFixtureBuilder,E2EFixture,E2ERunPaths}.cs, Support/{E2EConfigPersistence,E2EArtifactWriter}.cs, GameplayAutoPlaySmokeTests and DrumMappingStageSmokeTests; DTXPathCompatibilityArchitectureTests; VideoRecorder Sandbox/{RecordingSandbox,SourceConfigDatabase}.cs and csproj; related tests and the three live docs below.

- [ ] Set Pooling=false on both SqliteConfigStore connection builders. Retain read-only Load, exact schema validation and parameterized transactional Save.
- [ ] Link the store alongside ConfigDbSchema in the recorder. Append DTXMANIA_RECORDER_CONFIG_STORE to that project's DefineConstants only, and conditionally place both linked types in DTXMania.VideoRecorder.Config (game builds retain DTXMania.Game.Lib.Config). Update SourceConfigDatabase, RecordingSandbox and recorder helpers to use the recorder namespace; game/E2E fixtures use the game namespace. Delegate SourceConfigDatabase.Load to the shared reader. Build DTXMania.E2E immediately: both referenced assemblies grant it internal visibility, so an unchanged namespace would cause CS0433. No new assembly, extern alias or game/MonoGame dependency.
- [ ] Replace recorder INI output with a sandbox config.db seeded from read-only source rows plus current owned overrides: new API key/port, API enabled, AutoPlay 0..9, NoFail, resolution/windowed. Preserve live unowned settings and opaque rows. Remove INI serialization and bridge-only escaping/trimming restrictions; preserve actual absolute-path validation. Dispose handles before launch; never copy or modify source DB/WAL/SHM.
- [ ] Change E2E fixtures and legacy-dependent unit fixtures to seed current v1 rows with the shared writer. Use SongRoot.0, existing skin/display/gameplay/API settings and correct manual/AutoPlay modes. Migrate MidiGameplaySmokeTests, GameplayAutoPlaySmokeTests and any other direct INI edits. Delete GameplayAutoPlaySmokeTests' post-Build config.db deletion and INI-only explanation; Build now replaces the complete profile snapshot. Preserve shared songs.db across profiles and close writers before launching.
- [ ] Remove the INI import branch, legacy path field/constructor parameter and GetLegacyConfigFilePath. Migrate constructor callers in a mechanical commit once fixtures use SQLite; coordinate signature/callers so builds remain valid. Remove DTXPath migration, RandomFromSubBox and bare AutoPlay branches. Clear DTXPathCompatibilityArchitectureTests.allowedFiles and remove obsolete production DTXPath comments so reintroduction fails. Preserve canonical paths/skin-token correction and current schema failures.
- [ ] Simplify the lower LoadConfig paths now that missing-DB creation returns early: remove !_storeLoaded from mustPersist, replace lower Save-or-Create branches with SaveSnapshotToStore, and remove the unreachable correction-catch first-create rethrow. Keep _storeLoaded itself, SaveSnapshotToStore/MarkDirty guards, early first-create failure propagation, correction-write dirty retry and generated-key rollback.
- [ ] Remove the legacy path's CrashContextPublisher registration and matching CrashContextPublisherTests/AppPathsTests assertions; preserve all active sensitive-path/API-key handling. Remove LegacyConfigPath from E2E records and all callers.
- [ ] Delete ArtifactWriter's bootstrap-Config.ini copy and GameplayAutoPlaySmokeTests' bootstrap-config-{suffix}.ini snapshots with no seed-evidence replacement. Preserve chart/audio/log/state/screenshot diagnostics. Update E2EConfigPersistence: preseeded DB existence is not proof that the game saved a value.
- [ ] Fix DrumMapping_ResetViaKeyboard_WipesCustomBindingOnSave: seed Key.Z=0 in config.db and assert the raw row exists before launch. Drive keyboard Reset and exit/flush, then read through SqliteConfigStore.Load and assert the raw row is absent. Remove the redundant in-memory capture setup and old file-existence explanation; the separate positive binding smoke still covers capture. This assertion must fail if reset/flush leaves the seeded row unchanged.
- [ ] Migrate RecorderConfigCompatibilityTests to real source ConfigManager DB → recorder-created config.db → real ConfigManager load. Verify no INI, overrides, live unowned values, source bytes unchanged and cleanup. Update RecordingSandboxTests, ProgramTests sandbox discovery and TestSourceConfigDatabase. Replace dead-volume/LastUsedSkin preservation examples with live settings or an explicitly opaque row; do not preserve dead game contracts.
- [ ] Assert immediate sandbox deletion after ordinary read/write disposal without ClearAllPools before the assertion. Safety-net pool cleanup may run afterward in teardown. Keep malformed/unsupported source, absolute-path, creation failure and failure-evidence tests.
- [ ] Keep existing unit first-create/failure tests, and add a small permanent Category=E2E no-DB test with stale INI. Start via GameProcessDriver without an API-health wait, bounded-wait for the game-created DB, inspect its completed snapshot read-only and assert default rows plus unchanged INI; dispose the process. Do not call ConfigManager.LoadConfig before the assertion. Keep seeded bundled-default-skin coverage separate. Assert obsolete rows are ignored/not re-emitted without deleting legitimate NX/skin tests.
- [ ] Update only the verified live docs: CLAUDE.md (AGENTS.md is its symlink), MCP/README.md and tools/README.md. Recorder runbooks already use config.db; the HPA-192 benchmark is a historical record. There is no repository scripts directory or cloud UAT recipe to edit, and an unidentified external recipe is not a PR dependency. A scoped `rg` check leaves no operational INI bootstrap/serializer/path API.
- [ ] Build E2E and run focused game/config, recorder and E2E-support suites, including baseline UI contracts. Retain raw-SQL setup for deliberately malformed schema fixtures. Phase 4 executes the affected graphical profile/reset and first-run cases. Commit the SQLite-only phase before descriptor work.

## 2. Typed descriptors and manager

Primary files: ConfigData.cs, new ConfigSetting.cs/ConfigSettings.cs, ConfigManager.cs, IConfigManager.cs; Config tests; GameplayAudioSession comments. Names may follow existing project conventions.

- [ ] Remove ResetToDefaults, its API-only tests, MasterVolume/BGMVolume/SEVolume/DTXManiaVersion/LastUsedSkin and their config load/save/default assertions. Delete obsolete volume TODO comments. Leave NX score metadata and the live FFmpeg reset-to-default behavior intact.
- [ ] Define typed immutable descriptors with nameof keys, get/set delegates, type codecs and one Normalize delegate. Use internal IScalarSetting and internal All/key lookup for all 27 live scalars. Expose public sealed ConfigSetting<T> handles with internal construction only for the 19 ordinary scalar edits. Give SkinPath, SystemSkinRoot, UseBoxDefSkin, ScreenWidth, ScreenHeight, EnableGameApi, GameApiPort and GameApiKey separate internal persistence-only descriptors that are not assignable to ConfigSetting<T>. Share descriptor mechanics/codecs; no second registry or runtime permission switch. Keep defaults in ConfigData initializers and read fresh default values when needed.
- [ ] Add optional UI specifications to the ordinary handles, reusing existing range/format helpers. Expose existing lane/note OptionTable objects internally and add EnumOptionTable<GaugeDamageLevel> using current Low/Normal/High labels/order, so all three dropdowns use one factory path. Undefined GaugeDamageLevel must normalize to Normal, not default(TEnum)/Low; retain name-only persistence codecs.
- [ ] Replace scalar parse cases and manual save entries with descriptor lookup/iteration. Normalize on load, edit and serialization; save emits Format(Normalize(Get(config))) without mutating live values or publishing events. Keep collection parsing/snapshots separate, all current path/API lifecycle rules, null string persistence and UI/storage bound distinctions.
- [ ] Add Set<T>(ConfigSetting<T>, T) for ordinary handles only. It normalizes, compares, assigns only a changed value and marks dirty. One explicit scroll-speed case uses existing isolated event dispatch with old/new values. Load publishes no scroll edit event. Retain AdjustScrollSpeed as a thin typed-Set call, and preserve compound resolution/skin/root and collection APIs; the eight special descriptors cannot be supplied to Set<T>.
- [ ] Migrate UI/test callers and mocks from the config scalar setters listed in the design, then remove those named methods from class/interface. In this phase, existing hand-built UI items can call typed Set until phase 3 replaces their construction. Preserve unrelated GraphicsManager and note-renderer APIs.
- [ ] Add permanent All/key/type coverage and uniqueness tests using reflection only in tests: every public read/write ConfigData scalar must be represented or explicitly excluded, every descriptor registered, and the eight special descriptors not assignable to ordinary edit handles. Iterate all descriptors for default/non-default codec and actual SQLite round-trips. Handle path defaults through existing lifecycle tests instead of assuming raw strings remain unchanged.
- [ ] Test invalid bool/int/enum input, numeric-enum rejection, invariant formatting, string null handling and meaningful path/API cases. Use independently specified expected boundaries/format strings for snap/clamp/enum fallback; do not invent an invalid arbitrary string case. Test no-op scalar mutation, deferred flush/retry, subscriber isolation and load without edit events. Keep API-key rollback, fresh AppPaths defaults, Default token relocation and collection regressions.
- [ ] Run focused suites and the unified unit project. Keep golden UI tests green. Commit `refactor(HPA-803): centralize typed scalar settings`.

## 3. Both settings UIs

Primary files: new ConfigItemFactory.cs, ConfigStage.cs, Song/Components/QuickSettingsPopup.cs; existing ConfigItems classes; ConfigStageLogicTests/Tests/SkinSwitcherTests, PlaySpeedAndPitchConfigTests and QuickSettingsPopupTests.

- [ ] Build ordinary widgets through one factory using descriptor UI metadata and typed Set. Each screen keeps an explicit list of descriptor references and special items; list position owns order. No category/order numbers, merge logic or duplicated labels/ranges/formatters.
- [ ] Keep ConfigStage's resolution, skin discovery/collision handling/immediate switch, folder/key/NX navigation, AutoPlay summary/lanes and Exit explicit. Preserve the popup's Auto Mode control and six-setting subset/order. Hidden descriptors remain hidden.
- [ ] Keep narrow screen-local edit/description overrides: ConfigStage unavailable-FFmpeg edits reset an existing non-default speed/pitch, with its existing warning suffix; popup unavailable speed edits are ignored and descriptions remain empty. Do not change either behavior while sharing metadata.
- [ ] Extend permanent tests so every descriptor with UI metadata constructs the expected existing widget type and edits through the real common mutation path. Keep independent default/non-default baseline text/order/description assertions for both screens, plus FFmpeg availability, AutoPlay, skin and navigation contracts. No temporary new-setting experiment, shipping dummy property or new framework test seam.
- [ ] Run both UI suites, baseline contracts and relevant manager tests. Commit `refactor(HPA-803): share settings items across both screens`.

## 4. End-to-end acceptance

Run acceptance on one supported graphical platform. Existing cross-platform CI stays intact; no second native-platform acceptance or human signoff gate is required. This documentation update remains draft. These are implementation completion gates, not claims that validation has already run.

### Automated gates

- [ ] Build `DTXMania.Game/DTXMania.Game.Windows.csproj` or `DTXMania.Game/DTXMania.Game.Mac.csproj`, plus `MCP/MCP.csproj`, with repository-pinned .NET 10.
- [ ] Run `dotnet test DTXMania.Test/DTXMania.Test.csproj`, `dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj` and `dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`. Run MCP.Tests if MCP-facing launch/test code changes.
- [ ] Run `dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"`; on macOS use `-p:EnableWindowsTargeting=true` as documented. Run graphical `Category=E2E` with DTXMANIA_E2E_GAME_PROJECT set to the selected platform project and existing prerequisites. Confirm the multi-profile gameplay, persisted-binding reset and new no-DB tests actually execute, alongside manual/AutoPlay, display modes, imported-chart and screenshot-tour coverage.
- [ ] Run prepared-chart recording coverage separately: PreparedChartRecordingSmokeTests has Category=AudioE2E, so the E2E filter does not execute it. Use `--filter "Category=AudioE2E&FullyQualifiedName~PreparedChartRecordingSmokeTests"` with existing graphics/audio-runtime and game-project prerequisites (current macOS AudioE2E CI uses ALSOFT_DRIVERS=null; physical audio hardware is not required). Its existing proof reaches Performance from an E2E fixture, not a recorder sandbox or Result. Do not count skipped/unselected tests as a pass; use the configured runner/CI and report a blocker if it cannot execute.

### Single MCP workflow and visual checks

Use one acceptance scenario, including its required restart, on the recorder-created sandbox. Source-byte preservation, override and cleanup assertions also remain in the automated recorder/compatibility tests. No separate manual no-DB or recorder launch, and no full OBS capture, is required.

- [ ] Reuse existing fixture/sandbox setup: create a source v1 DB with production ConfigManager and a deterministic chart, then let RecordingSandbox create the isolated acceptance config.db. Launch this actual recorder output for the whole scenario. Configure MCP using its generated DTXMANIA_API_URL/DTXMANIA_API_KEY and DTXMANIA_PROJECT_PATH without logging the key. Verify the initial roots/skin/owned settings and drive Title → Config → System/Drums with existing MCP tools. Do not build a new acceptance harness or CLI mode.
- [ ] Before changing the seeded baseline values, compare agent-inspected game_screenshot captures with phase-0 views at the same size/skin/fixture values: System/Drums top and scrolled content plus Quick Settings. Check text, descriptions/warnings, focus, order, typography, alignment, clipping and scrolling. Golden tests supplement this required visual check. No mockup is attached; keep images outside git.
- [ ] Through MCP, change a bool, scroll speed, an enum and an integer boundary away from their seeds; exercise AutoPlay and special controls. Open SongSelect Quick Settings using normal input, verify shared values and Auto Mode, edit one shared setting and verify it in Config. Then exit normally to flush all edits.
- [ ] Inspect actual canonical rows after shutdown, relaunch the same sandbox once and verify the edited values in both UIs through MCP. A pre-existing DB alone is insufficient proof. Select/play the deterministic fixture and verify gameplay plus Result. This combined flow proves the recorder-created DB launches and persists edits; no second recorder MCP scenario is needed. Close the owned process and clean up the acceptance sandbox.
### Completion report

- [ ] Check only scoped source/test/docs changed; no artifacts, compatibility aliases, dead setters or parallel metadata registry. Verify live docs use SQLite seeding.
- [ ] Record baseline/final test results and MCP/screenshot evidence in this same PR. Report any blocked graphical/audio checks explicitly instead of claiming acceptance.
- [ ] After implementation is complete, mark the same PR ready for review and verify required CI. Build/test jobs skip draft PR events and run on ready_for_review; skipped draft jobs are not green validation. Keep the current design-only PR draft.

## Definition of done

- All live scalar behavior/metadata is shared through typed descriptors; adding an ordinary setting requires its typed property/default, registered descriptor and explicit screen reference, with no new parsing/save/setter branches.
- Both settings screens preserve baseline visible behavior and pass permanent text/order contracts plus the required MCP screenshot check.
- Dead config fields/APIs and INI bootstrap are removed. Current defaults, collection semantics, path/API rules, edit notifications, dirty-save retry and v1 schema remain correct; the generic edit API cannot accept special persistence descriptors.
- Fresh startup, seeded E2E and recorder sandbox use config.db without linked-type ambiguity. Profile fixtures retain their seeded DB, Reset removes a binding that was persisted, and a real edited value survives restart. Source preservation/cleanup tests and the single MCP gameplay/Result scenario pass.
- All work and acceptance stay in one PR within the scoped budget. No human signoff criterion, generated artifacts or extra native-platform acceptance.
