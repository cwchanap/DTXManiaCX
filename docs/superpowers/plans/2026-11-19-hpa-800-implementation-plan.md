# HPA-800 — Reviewed implementation plan (one PR; <=3 engineer days)

**Design:** [revised HPA-800 design](../../proposals/2026-11-19-hpa-800-design.md)  
**Reviews addressed:** [initial](https://github.com/cwchanap/DTXManiaCX/pull/251#pullrequestreview-5987239671) · [revised-plan](https://github.com/cwchanap/DTXManiaCX/pull/251#pullrequestreview-5987491281)  
**Linear:** https://linear.app/cwchanap/issue/HPA-800  
**Branch:** `plan/hpa-800-performance-session-playfields` — implement **on this existing draft PR**, not a second PR.  
**Baseline:** main `241d78d8ed040a3d7d69f01562462b5ad57ad7ab`, after HPA-799 PR #250.  
**Status:** Docs-only review revision; no production code, tests or screenshots have been executed.

## Agent contract / budget

No behavior, timing, visual, scoring or MIDI change. No pause/replay, no new Tab effects/BPM UI, no added art/SFX and no new async queue or DI framework. Respect YAGNI/KISS. Hard ceiling **3 engineer days**, including test migration, MCP E2E and visual inspection.

**Execution budget:** baseline 0.10d; audio session + its tests 0.65d; gameplay rules + their tests 0.75d; two playfields + their tests 0.85d; automated end-to-end/visual verification 0.50d; ~0.15d buffer. In each coding step move or retire **that step's** private-method tests immediately; do not defer all old tests to a final 0.7d sweep. Keep the full canonical suite green at each step.

The existing `PerformanceStageDeterministicTests.cs` has ~8,438 lines, hundreds of private reflection calls and ~288 tests. Do **not** translate them 1:1. The current `InvokePrivateMethod` target names are the migration map; retain unique observable regression scenarios in focused tests, delete duplicate/private-implementation assertions, and preserve existing dedicated component tests. Do not retain dead production fields for old tests. No private-field reflection or `GetUninitializedObject` in surviving PerformanceStage-specific tests.

## Step 0 — Baseline and references (~0.10 day)

- [ ] Verify current main/PR head, HPA-799 scope behavior, and status of existing unit/E2E tests; note any pre-existing failures separately.
- [ ] Read `PerformanceStage`, `StageBackgroundWorkScope`, `SkillPanelDisplay`, `SkillManager`, current full-autoplay/Tab E2E, and HPA-797 screenshot paths. Use behavior groupings below; no test-by-test triage exercise.
- [ ] Capture the current main's existing E2E tour PNGs (`05-lanes-performance.png` / `07-tab-performance.png`) as a comparison baseline through the MCP-driven acceptance harness in Step 4; no new harness.

## Step 1 — `GameplayAudioSession` and its tests (~0.65 day)

Files: `Lib/Stage/Performance/GameplayAudioSession.cs`, `PerformanceStage.cs`, affected session/audio tests.

- [ ] Keep scheduling `BackgroundWork.Run`/`Post`/`Latest` and its chart/progress/error callbacks in the stage. Move the worker-local `PrepareGameplayCore` into a static/pure `GameplayAudioSession.Prepare(...)` with chart/path, speed/pitch, requested notation style, logger, progress, prepared-audio variant dependencies and cancellation passed **as immutable snapshots before worker launch**. No worker reads mutable stage or GPU state.
- [ ] Preserve **chart first**, including notation chart publish behind LOADING, then audio-preparation success/failure. Leave `ChartManager` creation for Step 2, from the update-thread chart callback. On chart-apply failure, do not bind the prepared set; on stale completion, run `onDropped` disposal **exactly once**.
- [ ] Move into one concrete session: `AudioLoader`, `PreparedGameplayAudioSet`, `SongTimer`, `ChipSoundCache`, BGM scheduling, active BGM instances and metronome. Make `UpdateTimer(GameTime)`, `GetCurrentMs(GameTime)`, `IsPlaying`, `IsPaused`, and chart-aware `Start(GameTime, ParsedChart)` explicit. The `Start` call retains BGM mute/volume/pan behavior; `UpdateTimer` stays in its old stage-update order. Methods for bind/start/advance(raw clock)/play chip/stop/dispose should preserve existing event order, volume/pan, silent-clock fallback, playback modifiers and no audio bleed after Back/Result. Do not refactor parser, timer, audio variants or video scheduling internals.
- [ ] Delete unused `LoadBGMSoundsAsync` rather than moving it. In the **same step**, update `PerformanceStageFontLifecycleTests` (its setup injects `_bgmSounds`/`_scheduledBGMEvents` that are moving) and remove reflection-only tests with no real production path; migrate real BGM, metronome, chip, `StartSong`-audio and preparation tests to session constructors and the existing scope fixture.
- [ ] Test chart-before-audio; progress; cancellation + reactivation; stale audio onDropped and once-only disposal; binding failure; silent clock; frozen profiles; BGM at exact boundaries/simultaneous overdue events; metronome and WAV volume/pan; Back/Result stop.

**Exit:** `dotnet test DTXMania.Test/DTXMania.Test.csproj` **fully green**, not only new tests. Changes to old tests for moved session methods are part of this commit.

## Step 2 — GPU-free `GameplayRules` + shared HUD snapshot (~0.75 day)

Files: `Lib/Stage/Performance/GameplayRules.cs`, `PerformanceStage.cs`, `TabHudDisplay.cs`, `SkillPanelDisplay.cs`, affected rules/HUD tests.

- [ ] Construct **one concrete GPU-free `GameplayRules` at `OnActivate`**, with frozen run settings/automated lanes so loading-time pad filtering works before chart arrival. At the update-thread chart callback, call `BindChart(chart)` to create `ChartManager` and judgement/score/combo/gauge/skill managers. Move event fan-out, AutoAddGauge policy, autoplay cursor, time-compensated judgement, completion/check + summary building there. Stage still performs Back/Result transition and owns ready/loading lifecycle.
- [ ] Preserve exact note/hit ordering. Automated notes: `ResolveAutoHit` first; maintain its emitted judgement events; then playfield `OnAutoHit(lane)` pad feedback and session chip sound. Manual hits use compensated clock for judgement/chip lookup; BGM/BGA/autoplay/visuals use raw chart clock. Preserve the real-time end-of-song buffer.
- [ ] Remove the dependency of Tab HUD on Lanes widgets. Adapt the existing `TabHudFrameState` into a **single immutable shared frame snapshot** (name `PerformanceFrameState`; no parallel duplicate gameplay DTO) via `GameplayRules.BuildFrameState(rawMs)` with `IsDanger`, `IsMaxSkill`, `Progress`, `ElapsedMs`, `TotalMs` as well as metrics from `ScoreManager.CurrentScore`, `ComboManager.CurrentCombo/MaxCombo`, `SkillManager.CurrentSkill` and its O(1) judgement counters. Verify counts against `JudgementManager.GetJudgementCount` in unit tests, **not five scans of the note chart per rendered frame**. Keep final Result summary/telemetry semantics.
- [ ] Move Fast/Slow from `SkillPanelDisplay.ProcessJudgement` into the rules: same `HitTimingFeedback.ProjectDelta` rounded milliseconds, hits only, no increments for rounded-zero/autoplay. `SkillPanelDisplay` becomes presentation-only and receives the shared snapshot. **Keep** its static formatting/badge helpers (consumed by `TabHudDisplay` and `ResultScreenModel`); migrate `SkillPanelDisplayProcessJudgementTests` count and Fast/Slow cases to `GameplayRules`, but retain tests for static rendering-format helpers. Lanes and Tab never reach across playfields for data.
- [ ] Migrate `FreezeRunConfiguration`, `ProcessAutoPlay`, `UpdateGameplayManagers`, `OnJudgementMade`, `CheckStageCompletion`, `FinalizePerformance`, failure/summary and judgement-count tests to direct `GameplayRules` construction. Use stage `PopulateTelemetry` for outer read-only behavior; remove duplicate reflection-specific tests. At this step, replace off-thread live manager reads (especially the five `JudgementManager.GetJudgementCount` scans) with a **single-read immutable published gameplay/telemetry snapshot** from the update thread; capture audio progress, effective mode and the immutable last-hit triple as needed. Publish at activation/update/chart/audio completion/cleanup, and preserve the public Game API field contract. Test concurrent telemetry polling during note processing, late audio callbacks and deactivation.

**Exit:** Full canonical unit suite **green**; shared metrics snapshot verified and GPU-free rules covered without private stage reflection.

## Step 3 — `LanesPlayfield` / `TabPlayfield` + their tests (~0.85 day)

Files: `Lib/Stage/Performance/IPerformancePlayfield.cs`, `LanesPlayfield.cs`, `TabPlayfield.cs`, `PerformanceStage.cs`, relevant visual tests.

- [ ] Define minimal interface: effective mode, update, `OnChartPublished(bpm, scrollSpeedPercent)`, `OnLaneInput`, `OnAutoHit`, `OnJudgement(event, snapshot)`, `OnScrollSpeedChanged`, `DrawBase/Overlay/Hud/Additive` within stage-owned `SpriteBatch` passes, dispose. No generic hierarchy.
- [ ] Stage creates Lanes visual components at activation so pad input flashes during LOADING/countdown. On chart publication, `OnChartPublished` forwards `SetBpm(chart.Bpm)` and the then-current scroll-speed into the Lanes note renderer; Tab may no-op, but the stage must not downcast. Preserve changes made during audio loading. Lanes owns lane-note rendering, judgement line, pads, Lanes popups/effects, lane flash, skill/score/combo/gauge view widgets and scroll-speed note positioning; it only **renders** the shared gameplay model.
- [ ] When chart/notation publishes, try to construct Tab strip + HUD + art on update thread. Publish Tab only if **both** ready, else immediately release partial Tab resources and keep complete Lanes. Preserve fallback and pass order, including video behind strip; no per-playfield Begin/End.
- [ ] Route manual hits, auto pad feedback, judgement popup/key flashes and scroll-speed changes through the active playfield. Remove stage `OnScoreChanged`/`OnComboChanged`/`OnSkillChanged`, updating score/combo/skill visuals after **each** processed judgement from shared frame metrics; do not wait until Draw for combo, because `ComboDisplay.Combo` animates on each increase; eliminate `_notationStrip == null` and `IsTabHudActive` branches from stage draw/input/judgement hot paths. Keep the single mode-selection branch at chart publication.
- [ ] Add `protected virtual` factories for playfield creation (existing stage seam pattern) so a headless constructor-created stage can be tested with fake visuals; guard `CreateChartVideoPlayer` / `InitializeChartVideoPlayer` and other GPU-only activation code when `CreateSpriteBatch` yields null (not just the playfield factories). `OnDraw` should continue returning early on null SpriteBatch; do not create a fake GraphicsDevice or DI framework. Retain existing borrowed `ITexture` owner/child dispose order.
- [ ] Delete dead-only texture loads/fields (`_laneDividerTexture`, `_laneFlashTexture`, `_progressFillTexture`, `_comboDigitsTexture`, `_scoreDigitsTexture`, `_judgeStringsTexture`) after confirming no live reads. Delete unused stage-only `_isPaused` and pause overlay; **do not** change `SongTimer` pause capability or danger overlay.
- [ ] Port `Draw*`, `TryConstructNotationStrip`, pad feedback, Tab readiness, effects/scroll-speed and texture-lifetime tests alongside extraction; trim private-state duplicates, retain unique fallback/ownership/visual gating assertions.

**Exit:** Full canonical unit suite **green**; stage only orchestrates activation/loading, input/Back/Result, four SpriteBatch passes and telemetry; no mode branch scattered through rendering.

### Cross-cutting owner checklist (review 2; apply in Step 3, not a separate PR)

- [ ] **Stage-retained:** background/BGA video, `_shutterTexture` + `DrawShutters`, `_dangerOverlayTexture` + `DrawOverlays`, `_uiManager`, `_readyFont`, `_scrollSpeedIndicator`, `_chartVideoPlayer`, `_lastLaneHit`, stage loading/READY text. Shutters, UI manager and danger pulse must still draw in **both** modes and with exactly the prior layer order.
- [ ] **Shared visual state:** `GameplayRules.BuildFrameState` contains `IsDanger`, `IsMaxSkill`, `ElapsedMs`, `TotalMs`, `Progress`, score/combo/counts/Fast/Slow; Lanes and Tab render the same authoritative values.
- [ ] **Cached white pixel:** `PerformanceStage` owns `_fallbackWhiteTextureResource` and lends `Texture2D?` to both playfield factories and its own loading-text rendering. Dispose Lanes borrowers on Tab switch, but release the stage-owned pixel **only at final teardown after all visual borrowers**. Do not hand its ownership to Lanes.
- [ ] **Current live event semantics:** update Lanes display fields on every processed judgement, keeping `ComboDisplay.Combo` hit-scale pulses; UI format helpers stay in `SkillPanelDisplay`.
- [ ] **Stage test seams:** no unconditional `_spriteBatch.GraphicsDevice` use during GPU-free activation; migrate `PerformanceStageFontLifecycleTests` along with audio-field removal, and complete constructor-based stage test migration here.

## Step 4 — MCP end-to-end and visual acceptance (~0.50 day)

On **one supported native platform** (macOS is enough for platform-neutral changes), **MCP must drive and report the E2E acceptance**. Reuse the **existing repeatable out-of-process JSON-RPC E2E suite** rather than building another manual click script.

- [ ] MCP runs build and canonical unit tests at implementation head (e.g. `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`; `dotnet test DTXMania.Test/DTXMania.Test.csproj`), recording command, exit code, totals and tested SHA. Cross-build Windows only as useful; it does not count as Windows-native gameplay.
- [ ] MCP runs `Category=E2E` at baseline revision and at implementation head (on macOS: `DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Mac.csproj ALSOFT_DRIVERS=null dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter 'Category=E2E' -p:EnableWindowsTargeting=true`). Its existing game process/JSON-RPC fixtures assert Lanes and effective Tab autoplay, judgements, result transitions, input/MIDI, BGA and tab import. Do not claim standalone CLI output is MCP acceptance unless the MCP actually ran/observed it.
- [ ] **Visual check via MCP:** collect baseline and head HPA-797 `05-lanes-performance.png` and `07-tab-performance.png` 1280x720 images (and Result if touched), and visually inspect both against each other for layout, clipping, combo/score/skill, staff/notes/hit marker, flashing overlays, render depth, BGA occlusion and CX Neon. Save screenshots and comparison observations as PR evidence. No Linear mockup is provided.
- [ ] Unit-test Tab missing strip/font/HUD fallback through injected factories (full Lanes only), chart BPM/scroll-speed propagation, shared danger/shutter/UI draw order, combo animation and stage-owned white pixel disposal. Run an automated Game API telemetry polling + transition/teardown scenario to ensure the API thread does not enumerate mutable judgement collections or race a disposed session; do not damage the skin to force an E2E error. Validate Back-to-SongSelect and normal Result/complete, **not** pause or replay, which do not exist.
- [ ] Attach MCP logs/telemetry/test output, screenshot artifact links and any limitations to **this PR**, with a factual tested SHA. One platform is sufficient absent platform-specific implementation. Silent-clock tests do not prove audible playback; skipped graphics tests do not prove visuals.

**Exit:** MCP E2E, canonical unit suite and visual screenshot comparison pass on the implementation head. No human acceptance step.

## Final PR criteria

- [ ] `PerformanceStage` is primarily orchestration; `GameplayAudioSession` owns prepared sounds/transport, `GameplayRules` owns shared game state, and one `IPerformancePlayfield` is active.
- [ ] Both HUDs use the same **manager-sourced immutable** state, O(1) counters and a single correct Fast/Slow classification; Game API telemetry reads one published snapshot instead of live manager state. No extra frame-wide note scans.
- [ ] Existing input/autoplay/judgement/score/timing contracts, chart-first publication, fallback, BGM/BGA layering and immediate audio stop remain unchanged.
- [ ] No second queue, DI container, new gameplay feature, dead legacy state or test compatibility fields introduced.
- [ ] Full unit suite remains green **after each extraction**, and MCP runs the existing E2E suite plus fresh baseline/head visual comparison on one supported platform.
- [ ] Implementation and verification stay in this **one PR** within 3 engineer days.

**Scope risk:** Hundreds of old reflection assertions are not all valuable coverage; only the actual externally observable regression cases should survive. If this rewrite cannot fit the hard timebox without losing unique behavior coverage, report the blocker on this PR before proceeding; do not silently weaken tests or create an extra implementation PR.