# HPA-799 — Reviewed implementation plan (one PR; <=3 engineer days)

**Design:** [reviewed HPA-799 design](../../proposals/2026-11-16-hpa-799-design.md)  
**Review addressed:** https://github.com/cwchanap/DTXManiaCX/pull/250#pullrequestreview-5941857470  
**Linear:** https://linear.app/cwchanap/issue/HPA-799/architecture-one-shared-helper-for-background-work-applied-on-the  
**Branch:** plan/hpa-799-stage-background-work  
**Baseline:** main d8a0b5ef3b4555012a2861c8dd302b0fe01914d9; prerequisites HPA-796/HPA-797 merged.  
**Status:** Reviewed docs-only planning PR. No production change, test execution or screenshots have been made on this branch.

## Agent contract and scope

Implement on **this exact branch and PR**. One Linear ticket = **one PR**; total budget **~2.9 engineer days including test rewrites**, hard limit <=3. Prefer KISS: a stage-local activation scope drained at the top of BaseStage.Update. The existing BaseGame Game API action queue drains after StageManager.Update and is **not** suitable for stage state-before-input semantics or current headless tests. Do not edit Game1/IStageGame, add a scheduler, new public test seam, production E2E delay hook, DI or compatibility layer. Do not refactor HPA-800/801/802 or change gameplay, art, assets, scores or audio timing.

Keep independent per-request versions and operation leases where two requests can race within one activation. Rewrite tests referencing deleted stage internals instead of retaining obsolete production fields. Document real test results, not expectations.

### Step 0 — Baseline and existing test inventory (~0.15 day)

- Recheck main SHA, builds, unit test and CI status. Reuse the current HPA-797 seven-stage screenshot tour artifacts; do not generate a new before/after pixel baseline.
- Inventory worker/progress/terminal entry points in StartupStage, SongSelectionStage, PerformanceStage, ConfigStage and Config/SongFolderPanel.
- Reserve time to update approximately **60 private-field/queue references** in SongSelectionStageTabTests (~22), SongSelectionPublicationTests (~17), StartupStageLogicTests (~7), SongSelectionStageBookmarkToggleTests (~6), PerformanceStageDeterministicTests (~4), ConfigStageLogicTests (~4). Recount on current main. PerformanceStageDeterministicTests and PerformanceStageFontLifecycleTests also use GetUninitializedObject; async-path tests must use actual stage activation or a minimal internal test scope, not a magic nullable scope.

**Exit:** baseline and test-impact inventory attached to PR.

### Step 1 — One tiny helper and BaseStage drain (~0.45 day including tests)

Files: Lib/Stage/BaseStage.cs, a new Lib/Stage/StageBackgroundWorkScope.cs, and focused stage tests. **No IStageGame or Game1 edits.**

1. Create a new scope per successful BaseStage.Activate, retire it **before OnDeactivate**, and drain at the **start of BaseStage.Update**, before OnFirstUpdate and OnUpdate. The scope owns one ConcurrentQueue<Action>; cap its drain at **64 actions/frame**. The separate existing Game API queue is unchanged.
2. Expose only Token, Post(Action), Latest<T>(Action<T>) as an IProgress<T>, Run<T>(worker, success, error, optional onDropped), Drain and Retire. Latest holds the newest value and queues **at most one pending callback per reporter**. Success/error/terminal callbacks are FIFO and **never coalesced**.
3. Run observes worker exceptions and expected cancellation without unobserved faulted tasks. Scope identity blocks late and queued callbacks after leave/re-entry. Retire cancels non-blockingly. Call onDropped **exactly once** for owned data queued-before-retire or returned-after-retire. A simple per-scope CTS with no timers, linked tokens or WaitHandle can be canceled without deferred disposal boilerplate; this does **not** excuse disposing other operation/picker CTS, leases or audio.
4. Deterministic tests use stage.Update to drain: apply-before-input, one terminal result, 10,000 progress events -> one pending update, 64-action budget, current error/cancellation, late Post, queued-before-retire and completed-after-retire resource disposal, and leave/re-enter. Do not use BaseGame's private drain or a second fake scheduler.

**Exit:** focused helper tests green; stage can be tested via stage.Update. Commit on this PR.

### Step 2 — Startup, Config and SongFolderPanel (~0.6 day including test rewrites)

Files: StartupStage.cs, ConfigStage.cs, Config/SongFolderPanel.cs and existing tests.

1. Startup runs its enumeration/cache/fallback **multi-step chain using worker-local intermediate decisions**, including the former _cacheLoadPathSelected choice. Do not post a value mid-chain that the worker must then read on a later frame. Publish one terminal result and coalesced Latest progress. Remove activation generation, gate and old task observers once covered. SongManager's library threading is out of scope.
2. Config uses Latest for reload/import status and FIFO Post for **terminal** status. Keep ConfigSongOperationLease, mutual exclusion and per-operation CTS. Require current lease/operation identity before applying progress; a late progress report must not overwrite a terminal status. Remove _songOperationUpdates and activation generation, not domain ownership.
3. **SongFolderPanel is an additional caller.** Pass the parent Config scope to its picker completion. Remove _pickerCompletions and the panel generation counter, but keep IsActive and a **captured picker CTS identity check**: the panel can close/reopen without Config leaving. Dispose picker CTS on delivered and dropped completions; cancellation is non-blocking.
4. Rewrite affected reflection tests to assert stage/panel behavior and drive stage.Update for async callbacks. Cover empty roots, cache fallback failure, progress flood, Config terminal/canceled import, panel close/reopen, and old picker completing after a newer open.

**Exit:** Startup, Config and panel suites green, no stale UI update. Commit on this PR.

### Step 3 — SongSelectionStage (~0.6 day including ~45 test-reference rewrites)

Files: SongSelectionStage.cs and existing initialization, publication, tab and bookmark tests.

1. Replace activation-tagged initializer, tab-load, bookmark rollback and library notification queues with scope-guarded callbacks. Use Latest for library publication version; on update fetch the **newest coherent snapshot**, never overwrite it with an older initializer result.
2. Retain same-activation versions for tab/bookmark load requests, per-song bookmark toggle generations and DB-write serialization. A committed bookmark persistence task may outlive navigation; cancel/drop only stale UI rollback, not user intent. Remove the activation-only counters and queue gates.
3. Refactor tests referencing removed private queues/generations; tests of async completion drive stage.Update. Verify leave/re-enter, newest snapshot over slow initializer, rapid bookmarks, tab loads, filters and selected/prepared chart behavior. Avoid compatibility fields/test-only production API.

**Exit:** focused SongSelect/Tab/bookmark/publication tests green. Commit on this PR.

### Step 4 — PerformanceStage (~0.7 day including tests)

Files: PerformanceStage.cs and existing audio, notation and Performance tests.

1. Keep chart parse and audio **file preparation** on worker. Return parsed chart + prepared audio as worker locals; never publish _chartManager, InitializeGameplayManagers, audio bindings or _isReady from the worker. On the update-thread terminal callback, initialize ChartManager/gameplay managers, notation resources, sound/cache bindings, BGM schedule, SongTimer, metronome, readiness and failure state.
2. Use Latest for audio preparation progress. Dispose stale PreparedGameplayAudioSet via onDropped **exactly once** (queued before retire or completed afterward). Prevent old callbacks from mutating a newly reactivated stage. Retain prepared-chart reuse, pitch/speed, audio timing, Tab fallback, and no-audio behavior.
3. Remove redundant _activationGeneration, pending notation handoff and CTS-observation scaffolding, and only AudioLifecycleGate sections proven redundant after update-thread serialization. Keep independent audio-internal synchronization.
4. Rewrite reflection-based async tests to construct/activate a stage or attach a narrowly scoped test fixture; pure helper tests do not need GPU activation. Test canceled/failed/late parse and preparation, readiness, Lanes/Tab and exact-once audio cleanup.

**Exit:** Performance/audio/Tab tests green, no worker-owned stage mutation. Commit on this PR.

### Step 5 — Automated E2E + screenshot check (~0.45 day)

- Build the Windows and macOS game projects on matching CI/agents; run the canonical DTXMania.Test unit suite on both. Run existing gameplay Category=E2E smoke on at least one desktop platform.
- On **one** platform, use the live MCP bridge to navigate Startup/Title -> SongSelect -> Lanes -> Result -> SongSelect -> Tab -> Result -> Config and folder panel. Verify stage telemetry, game readiness, chart/score/audio behavior and absence of unexpected errors. MCP journey is required; unit E2E alone is not sufficient.
- Prove **forced late completion races with deterministic unit tests**, **not** injected delays in production, fake MCP conditions or new E2E fixture hooks. The ordinary live tour is not claimed to prove those races.
- Reuse the **bundled CX Neon HPA-797 seven-stage screenshot tour** (Title, Config, DrumConfig, SongSelect, Lanes, Tab, Result). Assert captured PNG files are valid and agent-visually inspect correct stage, clipping, missing UI and readability. No HPA-799 UI mockup exists; no new pixel diff/screenshot framework and no human sign-off.
- Attach actual command logs, live MCP observations and screenshot artifacts/agent observations. Record unverified items explicitly. Keep this one PR as draft until implementation/acceptance evidence exists.

## Required acceptance checklist

- [ ] Stage-local queue is drained by BaseStage.Update **before input**, capped at 64 callbacks/frame; BaseGame Game API queue remains untouched
- [ ] Latest coalesces progress (at most one queued update per reporter); terminal results are FIFO and not lost behind bulk progress
- [ ] Activation identity invalidates queued/late results; non-blocking Deactivate, expected cancellation and exactly-once owned-result disposal are tested
- [ ] Startup, SongSelection, Performance, Config and SongFolderPanel migrate without duplicated lifecycle counters/queues; same-activation domain order and picker CTS identity preserved
- [ ] Startup worker decisions are local and published once; Config lease/final status and SongSelect snapshot/bookmark persistence stay correct
- [ ] Performance chart/managers/renderer/audio binding & ready/error state are update-thread-only; stale prepared audio disposed, gameplay unchanged
- [ ] Existing reflection-based tests affected by deleted implementation details are updated on this PR; no unnecessary production compatibility API
- [ ] Both platform game builds and canonical unit suites pass; existing gameplay E2E passes
- [ ] MCP-driven **ordinary** end-to-end stage tour passes on one desktop platform; forced async races tested deterministically in unit tests
- [ ] Seven bundled CX Neon E2E screenshots verified and visually checked by the agent (no pixel diff gate, no manual sign-off)
- [ ] Exactly **one PR**, <=3 engineer days, no new art/SFX or unrelated refactor

**Planning-only note:** This file defines acceptance for future implementation; nothing here asserts that implementation or any checks have already run.
