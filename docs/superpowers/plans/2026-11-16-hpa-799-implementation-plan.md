# HPA-799 — Reviewed implementation plan (one PR; <=3 engineer days)

**Design:** [reviewed HPA-799 design](../../proposals/2026-11-16-hpa-799-design.md)  
**Review addressed:** https://github.com/cwchanap/DTXManiaCX/pull/250#pullrequestreview-5941857470  
**Linear:** https://linear.app/cwchanap/issue/HPA-799/architecture-one-shared-helper-for-background-work-applied-on-the  
**Branch:** plan/hpa-799-stage-background-work  
**Baseline:** main d8a0b5ef3b4555012a2861c8dd302b0fe01914d9; prerequisites HPA-796/HPA-797 merged.  
**Status:** Reviewed docs-only planning PR. No production change, test execution or screenshots have been made on this branch.
**Rev. 2 review:** https://github.com/cwchanap/DTXManiaCX/pull/250#pullrequestreview-5942668982 — lifecycle, resource ownership, Startup completion and headless test seams incorporated.

## Agent contract and scope

Implement on **this exact branch and PR**. One Linear ticket = **one PR**; total budget **~2.9 engineer days including test rewrites**, hard limit <=3. Prefer KISS: a stage-local activation scope drained at the top of BaseStage.Update. The existing BaseGame Game API action queue drains after StageManager.Update and is **not** suitable for stage state-before-input semantics or current headless tests. Do not edit Game1/IStageGame, add a scheduler, new public test seam, production E2E delay hook, DI or compatibility layer. Do not refactor HPA-800/801/802 or change gameplay, art, assets, scores or audio timing.

Keep independent per-request versions and operation leases where two requests can race within one activation. Rewrite tests referencing deleted stage internals instead of retaining obsolete production fields. Document real test results, not expectations.

### Step 0 — Baseline and existing test inventory (~0.15 day)

- Recheck main SHA, builds, unit test and CI status. Reuse the current HPA-797 seven-stage screenshot tour artifacts; do not generate a new before/after pixel baseline.
- Inventory worker/progress/terminal entry points in StartupStage, SongSelectionStage, PerformanceStage, ConfigStage and Config/SongFolderPanel.
- Reserve time for **more than the previous ~60 reflection references**. Inventory SongSelectionStageTabTests, SongSelectionPublicationTests, StartupStageLogicTests (including `_currentAsyncTask`, `_cacheFallbackAttempted` and SynchronizationContextScope tests), SongSelectionStageBookmarkToggleTests, PerformanceStageDeterministicTests, ConfigStageLogicTests, **ConfigStageNxImportTests** and **Config/SongFolderPanelTests**. Also inspect PerformanceStageFontLifecycleTests and GetUninitializedObject construction. Do not assume headless PerformanceStage.Activate is possible (it calls InitializeComponents and needs GraphicsDevice).

**Exit:** baseline and test-impact inventory attached to PR.

### Step 1 — One tiny helper and BaseStage drain (~0.45 day including tests)

Files: Lib/Stage/BaseStage.cs, a new Lib/Stage/StageBackgroundWorkScope.cs, and focused stage tests. **No IStageGame or Game1 edits.**

1. Create one scope per successful BaseStage.Activate, retire **before OnDeactivate**, and drain at the start of BaseStage.Update before input (at most 64 actions/frame). Use a small queue of apply+optional-drop items protected by a short lock together with `_retired` state; BaseGame/Game API queue remains untouched. After draining, and again after OnFirstUpdate, check whether a callback caused a synchronous InstantTransition/Deactivate; never continue stage logic while inactive.
2. Expose only Token, Post, Latest<T>, Run<T>, Drain and Retire. Latest retains just the newest report with at most one queued apply; successful/failed **terminal** results remain FIFO and uncoalesced. Workers/enqueue and Retire share the same brief lock: Retire marks retired, **synchronously detaches all queued items** (because this scope will never Update again), then invokes their onDropped outside the lock; results arriving after retirement are dropped directly. Continue dropping other resources if one cleanup callback fails; never hold the lock while disposing or calling user code.
3. Run observes worker errors/cancellation and avoids unobserved faulted tasks; an exception inside an update-thread success callback still needs a local try/catch/finally in the owning stage. Retire cancels without awaiting the worker. Exactly-once drop/ownership transfer is required for queued-before-retire and completed-after-retire results, including concurrent enqueue/retire. Plain scope CTS without timers/linked tokens can remain undisposed; **per-operation/picker CTS, leases and prepared audio must be disposed**.
4. Test via stage.Update for real activated headless-friendly stages and via scope.Drain for isolated components. Check apply-before-input, 10k reports -> one queued apply, 64-action budget, canceled/current faults, queued-before-retire synchronous cleanup (without subsequent Update), completion after retire, concurrent enqueue/retire, and **mid-drain InstantTransition** dropping remaining work. Add exactly one internal `BaseStage.InstallTestBackgroundScope()` seam returning a scope for GPU-dependent GetUninitializedObject tests (no production lazy activation or public compatibility API); pure logic tests may still bypass activation.

**Exit:** focused helper tests green; stage can be tested via stage.Update. Commit on this PR.

### Step 2 — Startup, Config and SongFolderPanel (~0.6 day including test rewrites)

Files: StartupStage.cs, ConfigStage.cs, Config/SongFolderPanel.cs and existing tests.

1. Startup runs enumeration/cache/fallback as a **worker-local multi-step chain** and publishes one terminal result per async phase; Latest handles progress. Fix `UpdateCurrentPhase` to set the default status message **on phase entry**, not on every update, or latest progress is erased before Draw. For `SongListDB` and `EnumerateSongs`, do **not** advance on `_currentAsyncTask.IsCompleted` / `IsFaulted`: Run catches faults before it posts results. Update a simple stage-owned completion/error state in the update-thread success/error callbacks, then advance and log current failure once (startup continues on error as before). Retain empty-library/cache-fallback paths. Delete redundant generation/gate/task observers after migration.
2. Config uses Latest for reload/import status and FIFO Post for **terminal** status. Keep ConfigSongOperationLease, mutual exclusion and per-operation CTS. Require current lease/operation identity before applying progress; a late progress report must not overwrite a terminal status. Remove _songOperationUpdates and activation generation, not domain ownership.
3. ConfigStage passes the parent scope into **SongFolderPanel**; remove panel `_pickerCompletions` and activation generation, but keep `IsActive` and captured picker CTS identity for close/reopen during one Config activation. Dispose picker CTS on both completed and dropped paths. Update **Config/SongFolderPanelTests** to create a scope and make DrainPicker call `scope.Drain()` instead of `panel.Update`; adapt its no-longer-draining inactive-panel test. Keep one shared scope, never add a compatibility panel queue.
4. Explicitly adapt **ConfigStageNxImportTests**, which currently skips stage.Activate and invokes private `OnUpdate`, to install the internal test scope and drain it; don't try to Activate UI/GPU resources in these headless tests. Rewrite StartupStageLogicTests tests that inspect `_currentAsyncTask` or assume an immediate SynchronizationContext progress callback to drive the new phase/progress contract. Cover failed/late completion, non-overwritten progress, canceled NX import, Config terminal ordering and picker close/reopen.

**Exit:** Startup, Config and panel suites green, no stale UI update. Commit on this PR.

### Step 3 — SongSelectionStage (~0.6 day including ~45 test-reference rewrites)

Files: SongSelectionStage.cs and existing initialization, publication, tab and bookmark tests.

1. **Retire early:** SongSelectionStage overrides `Deactivate()` and calls `base.Deactivate()` *after* tearing down UI, input and fonts. Call protected idempotent `RetireBackgroundWork()` at the **start** of the override, preserving base.Deactivate's final cleanup. Convert activation-tagged initializer/tab/bookmark completion queues to stage posts and library publication to Latest; fetch the newest coherent library snapshot on the update thread, never overwriting it with an old initializer.
2. Retain same-activation versions for tab/bookmark load requests, per-song bookmark toggle generations and DB-write serialization. A committed bookmark persistence task may outlive navigation; cancel/drop only stale UI rollback, not user intent. Remove the activation-only counters and queue gates.
3. Rewrite reflection tests referencing removed queues/activation fields and drive stage.Update for normal active-stage completion. Headless tests may use the one internal test scope seam. Explicitly assert retirement before teardown, old callbacks cannot reach disposed UI, newest publication wins, rapid bookmarks/tab requests and preserved chart selection. No compatibility-only production fields.

**Exit:** focused SongSelect/Tab/bookmark/publication tests green. Commit on this PR.

### Step 4 — PerformanceStage (~0.7 day including tests)

Files: PerformanceStage.cs and existing audio, notation and Performance tests.

1. **Snapshot** selected song/difficulty, parsed chart, playback modifiers and scroll-speed settings on the **update thread before Run**, so worker continuations do not read fields nulled by CleanupComponents. Keep chart parse/audio file preparation on the worker; return only local chart/prepared audio. Publish ChartManager, gameplay managers, notation, sound bindings/cache, BGM schedule, SongTimer, metronome, readiness/error on the update-thread success/error callbacks.
2. Use Latest for audio progress. Wrap the **update-thread onSuccess** binding in `try/catch/finally`: on binding failure call `FailLoading` (clearing loading/ready and retaining the original exception); ensure the locally owned PreparedGameplayAudioSet is disposed on all failures unless its ownership was successfully transferred to the stage (the stage then cleans it up). A **stale never-applied** prepared result is disposed exactly once via onDropped, including work queued before Retire. Test a zero-BPM SetBpm exception after preparation.
3. Remove `_activationGeneration`, pending notation handoff, CTS observation scaffolding and **AudioLifecycleGate** once all stage audio-binding and teardown operations are on the update thread (the existing gate has no other known callers). Keep only independently necessary audio-thread synchronization; preserve prepared chart reuse, sound fallback, runtime pitch/speed, BGM timing and Tab/Lanes behavior.
4. Update PerformanceStageDeterministicTests and PerformanceStageFontLifecycleTests without trying to call `PerformanceStage.Activate` headlessly (it calls `InitializeComponents` and needs GraphicsDevice). Use the single internal InstallTestBackgroundScope seam on a minimally configured stage for async/apply tests; direct logic tests may remain reflection-constructed. Prove canceled/late prep, exception during onSuccess, FailLoading state, and correct single-owner audio disposal.

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
- [ ] Synchronous Retire **drains/drops** owned queued work even without a future Update; concurrent enqueue/retire and mid-drain InstantTransition are safe, no callback reaches disposed UI and each owned result drops once
- [ ] Startup, SongSelection, Performance, Config and SongFolderPanel migrate without duplicated lifecycle counters/queues; same-activation domain order and picker CTS identity preserved
- [ ] Startup's async phase transitions follow **posted success/error**, not Run Task.IsCompleted, and coalesced progress stays visible through Draw; Config lease/final status and SongSelect snapshot/bookmark behavior remain correct
- [ ] Performance inputs are captured before Run; chart/managers/audio binding and ready/error state are update-thread-only; update-thread onSuccess failure calls FailLoading and disposes owned audio, and AudioLifecycleGate is removed
- [ ] A **single internal headless test scope seam** supports Performance/Config GetUninitializedObject tests; Startup phase, ConfigStageNxImportTests and standalone SongFolderPanelTests updated along with the earlier reflection test inventory (no overbuilt test harness)
- [ ] Both platform game builds and canonical unit suites pass; existing gameplay E2E passes
- [ ] MCP-driven **ordinary** end-to-end stage tour passes on one desktop platform; forced async races tested deterministically in unit tests
- [ ] Seven bundled CX Neon E2E screenshots verified and visually checked by the agent (no pixel diff gate, no manual sign-off)
- [ ] Exactly **one PR**, <=3 engineer days, no new art/SFX or unrelated refactor

**Planning-only note:** This file defines acceptance for future implementation; nothing here asserts that implementation or any checks have already run.
