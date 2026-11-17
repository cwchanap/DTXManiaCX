# HPA-799 — Implementation plan (one PR; <=3 engineer days)

**Design:** [HPA-799 design](../../proposals/2026-11-16-hpa-799-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-799/architecture-one-shared-helper-for-background-work-applied-on-the  
**Branch:** plan/hpa-799-stage-background-work  
**Baseline:** main d8a0b5ef3b4555012a2861c8dd302b0fe01914d9; prerequisites HPA-796/HPA-797 merged.  
**Status:** Docs-only planning PR. No production change, test execution or screenshots have been made on this branch.

## Agent contract and scope

Implement **on this exact branch and PR**. One Linear ticket = **one PR**; target ~2.5–3 engineer days maximum. Prefer KISS: reuse BaseGame's current action queue and BaseStage lifecycle; no scheduler framework, dependency injection, platform branching or compatibility layers. Do not implement HPA-800/801/802 or alter gameplay design, art, assets, scores or audio timing. Code is authoritative over these example file names/line references. If a stage has independent same-activation request versions, preserve that behavior instead of replacing it with only an activation scope.

### Step 0 — Baseline and controllable race cases (~0.2 day)

- Recheck main SHA/PR conflicts, unit build and the exact pending-work entrypoints in StartupStage, SongSelectionStage, ConfigStage and PerformanceStage. Record pre-existing errors rather than attributing them to this PR.
- Capture baseline bundled CX Neon E2E screenshots using the existing HPA-797 stage tour (or reuse its verified artifacts): Title, Config, SongSelect, Lanes, Tab, Result. Record the actual baseline fixture and screenshot paths.
- Write a tiny test matrix: old worker completes after Deactivate, old worker completes after new Activate, two requests within one active stage (latest wins), fault, cancellation and owned-result disposal.

**Exit:** scope matrix and baseline evidence in PR; no production code change yet.

### Step 1 — Tiny shared scope and main-thread post seam (~0.45 day)

Files: DTXMania.Game/Game1.cs, Lib/Stage/IStageGame.cs, Lib/Stage/BaseStage.cs, new Lib/Stage/StageBackgroundWorkScope.cs, focused DTXMania.Test stage/queue tests.

1. Forward one IStageGame post/queue method to **BaseGame._mainThreadActions**, the existing IGameContext queue. Keep DrainMainThreadActions's 64-per-frame cap and its position after StageManager.Update, before Draw. Generalize the action-failure log's misleading Game API label.
2. BaseStage makes a new scope at Activate, retires it **before OnDeactivate**, cancels work non-blockingly, and does not let callbacks from that scope apply after Deactivate or re-Activate. Capture the scope by object identity, not a long-lived IsActive boolean or per-stage generation number.
3. Keep the helper API limited to background work with guarded success/error publication and guarded Post for progress/events. Observe exceptions, distinguish cancellation, and provide optional dropped-result cleanup for IDisposable data. Scope disposal must not race worker token usage.
4. Add headless deterministic tests using a fake action queue that drains on demand. Include completion-after-leave, callback-queued-before-leave, leave/re-enter, exception observation, owned-result disposal and the game queue budget.

**Exit:** helper tests green; no new work queue or thread; commit on the same branch.

### Step 2 — StartupStage and ConfigStage (~0.55 day)

Files: StartupStage.cs, ConfigStage.cs and their existing tests.

1. Use the stage scope for Startup async song-load/cache paths and progress callbacks. Worker-owned values are returned or posted; only update-thread callbacks change stage progress, phase/fallback UI or request stage transitions. Preserve empty-root behavior, cache fallback and cancel-on-leave. Remove the obsolete activation gate/generation and task-observation boilerplate when fully covered.
2. Use guarded posts for Config song-folder reload / NX import progress and final status. Preserve the per-operation lease, only-current-operation terminal handling, error status and CTS disposal; operation ordering is not the same as activation validity. Remove the redundant per-activation queue and generation.
3. Exercise fallback errors, progress after cancellation, late terminal status, and Config -> Title -> Config while a worker is still active. Do not make database and filesystem operations run on the update thread.

**Exit:** focused stage/unit tests green; no stuck spinner or stale status; commit on this PR.

### Step 3 — SongSelectionStage (~0.65 day)

Files: SongSelectionStage.cs and its current song initialization, library publication, bookmarks and tab tests.

1. Replace the activation-tagged initializer and progress/completion queues with scope-guarded update-thread callbacks. Preserve current coherent library snapshot reconciliation (a newer publication must win over an older async initializer).
2. Route recent/bookmark tab load results and failed bookmark persist rollbacks via the scope. Keep per-song queued DB writes across navigation, the bookmark toggle version, latest-request-wins for tab/bookmark loads and filters. Retire only lifecycle-specific version counters and unnecessary queue/lock code; leave domain ordering controls in place.
3. Use tests that deliberately finish old activations late and interleave bookmark toggles, tab loads and library publication. Verify no BackBox/stale roots overwrite, no reverted newer bookmark, and no dropped intended DB write.

**Exit:** SongSelect test suite and relevant prepared-chart / SongSelect E2E pass; commit on this PR.

### Step 4 — PerformanceStage (~0.70 day)

Files: PerformanceStage.cs and relevant PerformanceStage / audio/notation tests.

1. Keep chart parsing and sound file preparation off-thread. Return a small prepared result (parsed chart, prepared sounds and pure notation as required) for update-thread publication; initialize/bind stage-owned playback, BGM and UI/graphics state on the update thread. Avoid extracting an HPA-800 session loader or new playfield abstraction.
2. Retire the scope on stage exit before teardown. A stale prepared audio result must be disposed **without** stage mutation. Preserve Lanes fallback on notation/font failure, prepared-chart path, runtime pitch/speed, audio timer and scheduled BGM order. Ensure current failure paths still surface load errors rather than hanging in loading.
3. Once publication and teardown are both update-thread owned, remove redundant _activationGeneration, pending notation transfer, CTS/continuation boilerplate and only proven-redundant AudioLifecycleGate sections. Keep other audio synchronization if it protects independent workers or callbacks.
4. Test cancellation and re-entry during parse/preparation, audio failures, stale result disposal, initial successful start and Tab fallback. No GPU creation from a worker.

**Exit:** focused Performance and audio tests green; no late binding/leak, changed audio behavior or readiness regression; commit on this PR.

### Step 5 — Full automated acceptance and visual evidence (~0.45 day)

- Build **both** game projects: dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj and dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj (use matching OS agents/CI as necessary). Run dotnet test DTXMania.Test/DTXMania.Test.csproj on each platform; do not rely on silently skipped graphics tests for visual acceptance.
- Run existing DTXMania.E2E Category=E2E smoke on at least one supported desktop platform. Additionally **drive a live game using MCP** (see MCP/README.md; actual registered tools are authoritative): use game_get_state and game_send_key/game_click for Startup/Title/SongSelect/Config and both modes. Use existing E2E fixture controls to force a slow background completion, switch away, re-enter and confirm that stale completions never change the new stage. Test Config canceled reload/import and Performance initialization interruption. Validate stage telemetry, chart/score preservation, one-shot completion and error logs; **MCP is required**, not a substitute in-process test.
- Capture after screenshots through the current screenshot/E2E tooling, with the same bundled CX Neon fixture as baseline. Compare Title, Config, SongSelect, Lanes, Tab, Result **stable panel/layout regions** against pre-change PNGs. Use a one-off pixel/region diff and agent visual inspection; ignore changing timers, notes and judgement animation only after identifying those regions. No UI mockup exists for HPA-799; a screenshot-based visual check is the criterion.
- Attach CLI/CI logs and MCP observations, before/after images and differences to this PR. Do not report any test as passed if not executed. Keep this same draft PR until the implementation and evidence exist.

## Required acceptance checklist

- [ ] Exactly one reused BaseGame main-thread action queue, still capped at 64 actions/frame
- [ ] BaseStage activation-scoped helper; old callbacks dropped through leave/re-entry; cancellation never blocks Deactivate
- [ ] All four stages use guarded update-thread publication; no redundant per-stage activation generations/queues for that work
- [ ] Same-activation request/operation ordering, library snapshot coherency and bookmark DB writes preserved
- [ ] Stale Performance prepared audio disposed; no cross-thread graphics/audio binding, startup/audio/Tab fallback preserved
- [ ] Deterministic unit tests pass for late success, error, cancellation, stale disposal, overlapping requests and reactivation
- [ ] Windows and macOS game builds and canonical unit tests pass; existing gameplay E2E passes
- [ ] **MCP-driven** end-to-end stage journey with forced late async completions passes on at least one platform
- [ ] Before/after bundled CX Neon screenshots compared for stable visual regions, with observed findings recorded; no unintended UI change
- [ ] Entire feature, tests and evidence remain in this **single PR**, with no human approval gate, art/SFX or unrelated refactor

**Planning-only note:** This file defines acceptance for future implementation; nothing here asserts that implementation or any checks have already run.
