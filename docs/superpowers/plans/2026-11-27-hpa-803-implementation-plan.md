# HPA-803 — Implementation plan (one PR, <=3 engineer days)

**Design:** [declarative settings and SQLite-only configuration](../../proposals/2026-11-27-hpa-803-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-803  
**Branch:** `plan/hpa-803-declarative-config`; implement in the same PR.  
**Baseline:** `e7204eba135e0b1b201135e2a5027b6d94fa0539` (2026-11-27); recheck before coding.  
**Revision:** [review 6091001518](https://github.com/cwchanap/DTXManiaCX/pull/256#pullrequestreview-6091001518).  
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

- [ ] Use `rg` to inventory Config.ini, LegacyConfigPath, GetLegacyConfigFilePath, legacyIniPath, ImportLegacyIni, DTXPath, RandomFromSubBox, bare AutoPlay, ConfigManager constructors, named scalar setters, ResetToDefaults and the five dead scalar properties. Include scripts/docs and crash-report registration. Distinguish live graphics APIs, FFmpeg reset behavior, key-binding defaults, NX score metadata and skin INIs.
- [ ] Run focused Config and QuickSettingsPopup tests plus recorder/E2E-support suites; record any existing failures. Use current headless ConfigStageTestFactory and popup CreateItems seams.
- [ ] Commit golden UI contract tests from the baseline: per Config category and for the popup, exact ordered GetDisplayText() + Description sequences for defaults and one representative non-default config. Use literal expected results, deterministic roots/skin fixtures and controlled FFmpeg availability. Include special rows; a headless setup that omits Skin must not be claimed as full menu coverage. Retain existing skin-switcher coverage.
- [ ] Capture same-skin 1280x720 baseline System/Drums pages, scrolled controls and Quick Settings popup through MCP. Keep images outside the tracked tree. These are the visual baseline for final agent inspection; golden text tests do not replace them.

## 1. SQLite-only startup and automation

Primary files: ConfigManager.cs, AppPaths.cs, SqliteConfigStore.cs; Diagnostics/CrashReporting/CrashContextPublisher.cs; E2E Fixtures/{E2EFixtureBuilder,E2EFixture,E2ERunPaths}.cs and Support/{E2EConfigPersistence,E2EArtifactWriter}.cs; affected smoke/support tests; VideoRecorder Sandbox/{RecordingSandbox,SourceConfigDatabase}.cs and csproj; recorder tests and live docs.

- [ ] Set Pooling=false on both SqliteConfigStore connection builders. Retain read-only Load, exact schema validation and parameterized transactional Save. Link the store alongside ConfigDbSchema in the recorder and delegate SourceConfigDatabase.Load to it. Keep source/path validation and recorder independence from MonoGame/game project references.
- [ ] Replace recorder INI output with a sandbox config.db seeded from read-only source rows plus current owned overrides: new API key/port, API enabled, AutoPlay 0..9, NoFail, resolution/windowed. Preserve live unowned settings and opaque rows. Remove INI serialization and bridge-only escaping/trimming restrictions; preserve actual absolute-path validation. Dispose handles before launch; never copy or modify source DB/WAL/SHM.
- [ ] Change E2E fixtures and legacy-dependent unit fixtures to seed current v1 rows with the shared writer. Use SongRoot.0, existing skin/display/gameplay/API settings and correct manual/AutoPlay modes. Migrate MidiGameplaySmokeTests, GameplayAutoPlaySmokeTests and any other direct INI edits. Close writers before launching.
- [ ] Remove the INI import branch, legacy path field/constructor parameter and GetLegacyConfigFilePath. Migrate constructor callers in a clearly separated mechanical commit once their fixture inputs use SQLite; coordinate signature/callers so builds remain valid. Remove DTXPath migration, RandomFromSubBox and bare AutoPlay branches. Keep canonical paths/skin-token correction, schema errors, load-before-save, key provisioning/rollback and save retry.
- [ ] Remove the legacy path's CrashContextPublisher registration and matching CrashContextPublisherTests/AppPathsTests assertions; preserve all active sensitive-path/API-key handling. Remove LegacyConfigPath from E2E records and all callers.
- [ ] Delete ArtifactWriter's bootstrap-Config.ini copy and GameplayAutoPlaySmokeTests' bootstrap-config-{suffix}.ini snapshots with no seed-evidence replacement. Preserve chart/audio/log/state/screenshot diagnostics. Update E2EConfigPersistence: preseeded DB existence is not proof that the game saved a value.
- [ ] Migrate RecorderConfigCompatibilityTests to real source ConfigManager DB → recorder-created config.db → real ConfigManager load. Verify no INI, overrides, live unowned values, source bytes unchanged and cleanup. Update RecordingSandboxTests, ProgramTests sandbox discovery and TestSourceConfigDatabase. Replace dead-volume/LastUsedSkin preservation examples with live settings or an explicitly opaque row; do not preserve dead game contracts.
- [ ] Assert immediate sandbox deletion after ordinary read/write disposal without ClearAllPools before the assertion. Safety-net pool cleanup may run afterward in teardown. Keep malformed/unsupported source, absolute-path, creation failure and failure-evidence tests.
- [ ] Add a true no-DB startup case with a stale INI and assert default DB creation without reading/changing the INI. Retain seeded bundled-default-skin coverage separately. Assert obsolete rows are ignored and not re-emitted by a game snapshot; do not remove legitimate NX/skin tests.
- [ ] Update active CLAUDE.md (AGENTS.md is its symlink), MCP/README, tools/README, recorder runbooks, scripts/benchmark recipes and the cloud UAT recipe with correct v1 SQLite seeding. Keep historical records. A scoped `rg` check leaves no operational INI bootstrap/serializer/path API.
- [ ] Run focused game/config tests, full recorder and E2E-support suites, including the baseline UI contract tests. Commit the SQLite-only phase before descriptor work.

## 2. Typed descriptors and manager

Primary files: ConfigData.cs, new ConfigSetting.cs/ConfigSettings.cs, ConfigManager.cs, IConfigManager.cs; Config tests; GameplayAudioSession comments. Names may follow existing project conventions.

- [ ] Remove ResetToDefaults, its API-only tests, MasterVolume/BGMVolume/SEVolume/DTXManiaVersion/LastUsedSkin and their config load/save/default assertions. Delete obsolete volume TODO comments. Leave NX score metadata and the live FFmpeg reset-to-default behavior intact.
- [ ] Define typed immutable descriptors with nameof keys, get/set delegates, type codecs, one Normalize delegate and optional UI specification. Reuse existing ranges, custom formatters and enum option tables. Register each live scalar in All and build an ordinal key lookup. Keep defaults in ConfigData initializers; derive fallback values from fresh instances when needed. The public manager signature requires an accessible ConfigSetting<T> handle; construction remains internal.
- [ ] Replace scalar parse cases and manual save entries with descriptor lookup/iteration. Normalize on load, edit and serialization; save emits Format(Normalize(Get(config))) without mutating live values or publishing events. Keep collection parsing/snapshots separate, all current path/API lifecycle rules, null string persistence and UI/storage bound distinctions.
- [ ] Add Set<T>(ConfigSetting<T>, T). It normalizes, compares, assigns only a changed value and marks dirty. One explicit scroll-speed case uses existing isolated event dispatch with old/new values. Load publishes no scroll edit event. Retain AdjustScrollSpeed as a thin typed-Set call, compound resolution/skin/root APIs and all live collection operations.
- [ ] Migrate UI/test callers and mocks from the config scalar setters listed in the design, then remove those named methods from class/interface. In this phase, existing hand-built UI items can call typed Set until phase 3 replaces their construction. Preserve unrelated GraphicsManager and note-renderer APIs.
- [ ] Add permanent All/key/type coverage and uniqueness tests using reflection only in tests: every public read/write ConfigData scalar must be represented or have an explicit justified exclusion, and every declared descriptor must be registered. Iterate all descriptors for default/non-default codec and actual SQLite round-trips. Handle path defaults through existing lifecycle tests instead of assuming raw strings stay unnormalized.
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
- [ ] Run `dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"`; on macOS use `-p:EnableWindowsTargeting=true` as documented. Run the graphical `Category=E2E` suite with DTXMANIA_E2E_GAME_PROJECT set to the selected platform project and its existing prerequisites. Check actual executed tests, including manual/AutoPlay, display modes, imported-chart and screenshot-tour coverage.
- [ ] Run prepared-chart recording coverage separately: PreparedChartRecordingSmokeTests has Category=AudioE2E, so the E2E filter does not execute it. Use `--filter "Category=AudioE2E&FullyQualifiedName~PreparedChartRecordingSmokeTests"` with the suite's existing graphics/audio-runtime and game-project prerequisites (current macOS AudioE2E CI also covers it with ALSOFT_DRIVERS=null; physical audio hardware is not required). Do not count skipped or unselected tests as a pass; execute the equivalent prepared-chart flow through MCP on the chosen acceptance platform if the test's runner requirements are unavailable there.

### MCP workflow and visual checks

- [ ] Seed isolated app-data config.db and a deterministic chart; configure MCP with DTXMANIA_API_URL, DTXMANIA_API_KEY and DTXMANIA_PROJECT_PATH. Start the real game and drive Title → Config → System/Drums through existing game_get_state, game_send_key/game_click, game_change_stage where appropriate and game_screenshot.
- [ ] Change a bool, scroll speed, an enum and an integer boundary away from seeded values; exercise AutoPlay and special controls. Exit normally to flush, inspect actual canonical DB rows after shutdown, then relaunch the same app-data root and verify UI values. A DB that already existed before launch is insufficient evidence.
- [ ] Open SongSelect Quick Settings through its normal input, inspect its order/values/Auto Mode, edit a shared setting, close the popup and verify the shared setting and persistence after normal exit/restart. Then select/play the fixture chart and verify gameplay and Result.
- [ ] Compare agent-inspected MCP screenshots against the phase-0 baseline at the same size/skin: System/Drums top and scrolled content plus the popup. Check text, values, description/warnings, focus, order, typography, alignment, clipping and scroll behavior. Golden tests cover exact text contracts; screenshots remain the required visual check. No mockup is attached. Keep images outside git.
- [ ] Demonstrate a real no-DB default startup with stale INI ignored. Launch the isolated game without an API-health wait because defaults disable the API; bounded-wait for the game process to create config.db, then inspect the completed snapshot with read-only SqliteConfigStore.Load and assert default rows plus unchanged INI. Do not call ConfigManager.LoadConfig first: it could create the DB and give a false pass. Close/dispose the owned process. Keep this case separate from the seeded MCP run.
- [ ] Recorder/backend E2E: create source DB using production ConfigManager, create a recorder sandbox DB, launch the real game from it, connect MCP using sandbox credentials and drive a prepared chart through gameplay/result. Verify source bytes unchanged, preserved roots/skin/live values, owned overrides and success cleanup. The cross-project round-trip supports this proof; do not substitute a mocked store for the real flow. Full OBS capture is unnecessary for this config-only change.

### Completion report

- [ ] Check only scoped source/test/docs changed; no artifacts, compatibility aliases, dead setters or parallel metadata registry. Verify live docs use SQLite seeding.
- [ ] Record baseline/final test results and MCP/screenshot evidence in this same PR. Report any blocked graphical/audio checks explicitly instead of claiming acceptance.
- [ ] After implementation is complete, mark the same PR ready for review and verify required CI. Build/test jobs skip draft PR events and run on ready_for_review; skipped draft jobs are not green validation. Keep the current design-only PR draft.

## Definition of done

- All live scalar behavior/metadata is shared through typed descriptors; adding an ordinary setting requires its typed property/default, registered descriptor and explicit screen reference, with no new parsing/save/setter branches.
- Both settings screens preserve baseline visible behavior and pass permanent text/order contracts plus the required MCP screenshot check.
- Dead config fields/APIs and INI bootstrap are removed. Current defaults, collection semantics, path/API rules, edit notifications, dirty-save retry and v1 schema remain correct.
- Fresh startup, seeded E2E and recorder sandbox use config.db. A real changed value persists across restart; source DB stays unchanged; real gameplay and cleanup pass.
- All work and acceptance stay in one PR within the scoped budget. No human signoff criterion, generated artifacts or extra native-platform acceptance.
