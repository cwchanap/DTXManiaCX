# HPA-510 Prepared Chart Recording Commands Design

**Issue:** [HPA-510](https://linear.app/cwchanap/issue/HPA-510/add-minimal-prepared-chart-recording-commands-in-song-select)  
**Date:** 2026-08-11  
**Status:** Revised after implementation review

## Context

HPA-501 has landed on `main`, so the reusable `DTXMania.Automation` process and JSON-RPC foundation is available. HPA-510 is now the only code prerequisite blocking HPA-503, the Windows recording vertical slice.

The recorder does not need general remote control of the game. It needs four narrow operations while Song Select is active:

```text
prepareVideoChart(chartPath)
startPreparedPreview()
activatePreparedChart()
cancelPreparedChart()
```

The implementation should make one exact indexed chart visible through the normal Song Select UI, hold its declared preview audio ready but stopped, play that preview under explicit recorder control, and then activate the chart through the same path a player uses.

This design deliberately keeps recording preparation inside `SongSelectionStage`. It does not add a game-wide capture session, generic mutation API, recorder state machine, database query layer, or synthetic input navigation.

## Goals

- Resolve one caller-supplied **absolute chart path** to the exact chart already present in the active Song Select library.
- Support root-level charts, nested BOX hierarchy, multi-chart/SET-defined rows, ordinary single-file multi-instrument rows, and duplicate titles.
- Project the resolved chart through the existing Song Select presentation path so status, preview image, play history, selected row, difficulty, and breadcrumb are normal UI state.
- Load the preview declared by the **resolved chart**, not merely the node's primary chart.
- Keep the prepared preview stopped until explicitly started.
- Reuse the existing preview sound resource, volume, looping, and BGM fade behavior.
- Keep all stage mutations on the game update thread.
- Activate through the existing Song Select transition construction and report a real success/failure result when the global transition debounce rejects activation.
- Expose only the prepared identity/state/elapsed telemetry required by HPA-503.
- Lock the game-to-Automation telemetry contract to the existing camelCase JSON wire format.
- Extend `DTXMania.Automation` with the four explicit client calls so HPA-503 never needs access to the private generic JSON-RPC transport.

## Non-goals

- A general Song Select remote-navigation API.
- A generic command dispatcher or arbitrary main-thread mutation endpoint.
- A game-wide recording/capture coordinator or immutable recording session ID.
- A separate render-generation/readiness state machine. Screenshot completion remains the presentation barrier.
- OBS, process launch, app-data sandboxing, Result hold timing, or video artifact handling.
- Changing the normal interactive preview delay or primary-chart preview behavior when no prepared chart is active.
- Restoring a pre-command filter/tab/breadcrumb after cancellation. The recorder runs in disposable app data; snapshot/restore machinery is unnecessary for MVP.
- Waiting inside the game for the stage-transition debounce window. A blocked activation returns `success=false`; the preparation remains available for a bounded caller retry.
- Direct transition to Performance.

## Current Reuse Survey

The required building blocks already exist:

- `SongSelectionStage` owns selection, BOX navigation, status/history/preview UI, preview sound lifecycle, BGM fading, and the player activation path.
- `SongSelectionStage` already retains the coherent applied `SongLibrarySnapshot`, including `RootSongs` and canonical `ActiveRoots`.
- `SongSelectionStage.GetNodeChartPaths` already understands that a visible row can represent the primary `DatabaseChart` plus additional `DatabaseSong.Charts`.
- `SongPathIdentity` already owns path normalization and root-containment primitives.
- `SongListNode.Scores` carries difficulty slots; persisted multi-chart rows carry `SongScore.ChartId`.
- `SongListNode.CreateSongNode` can legitimately assign the same `SongChart.Id` to DRUMS, GUITAR, and BASS score slots for one physical DTX file, so `ChartId` alone is not always a unique slot identity.
- `SongChartHelper.GetCurrentDifficultyChart` is the existing runtime fallback from a visible difficulty slot to its chart and is useful for legacy/SET rows where a direct `ChartId` match is unavailable.
- `SongListDisplay` already owns row index, difficulty, selection events, scroll target, and normal Song Select presentation updates.
- `SongSelectionStage.LoadPreviewSound` / `TryLoadPreviewSoundFile`, `CreatePreviewSoundInstance`, `StopCurrentPreview`, and `StartBGMFade` already implement the preview resource and playback behavior.
- `SongSelectionStage.SelectSong` owns the normal transition data but silently returns while `_game.CanPerformStageTransition()` is false, so prepared activation must surface that gate rather than clearing state first.
- `IGameContext.QueueMainThreadAction` is the existing mutation seam used by the Game API. `BaseGame.CaptureScreenshotAsync` demonstrates the existing `TaskCompletionSource(...RunContinuationsAsynchronously)` pattern for an awaited game-thread result.
- `JsonRpcServer` already authenticates every JSON-RPC request and serializes public DTOs with camelCase property names.
- `GameTelemetrySnapshot` plus `IStageTelemetryProvider` is the existing producer telemetry seam.
- `DTXMania.Automation.JsonRpc.JsonRpcGameClient` owns the consumer JSON-RPC transport and keeps `SendAsync` private.
- `DTXMania.E2E/AutomationContractTests.cs` already performs the producer-to-`GameStateSnapshot` camelCase contract round trip and is the required place to lock the three new telemetry fields.

The design should extend these seams rather than create parallel infrastructure.

## Approaches Considered

### 1. Stage-owned prepared state plus four explicit commands — selected

Add a small amount of state and four command methods to `SongSelectionStage`, expose them through explicit Game API / JSON-RPC handlers, and add matching methods to `DTXMania.Automation`.

This gives HPA-503 exactly what it needs while preserving the normal Song Select and transition paths.

### 2. Recorder drives Song Select with repeated key input — rejected

This is already the failure mode HPA-510 exists to remove. It is slow, fragile with nested boxes/filter state, and cannot safely disambiguate duplicate titles.

### 3. Add a general game automation/session coordinator — rejected

HPA-503 owns orchestration. A second lifecycle/session abstraction inside the game would duplicate ownership before a second use case proves it useful.

### 4. Resolve chart paths through a new database repository/service — rejected

The active `SongLibrarySnapshot` is already the authoritative, coherent view used by Song Select. A second DB query path could disagree with the rendered hierarchy and would add unnecessary synchronization.

## Chosen Architecture

### 1. Prepared state is update-thread-owned by `SongSelectionStage`

Keep a minimal stage-local record for the current preparation, for example:

```text
PreparedChartSelection
- SongListNode node
- SongChart chart
- int difficultyIndex
- string telemetryIdentity
```

Keep only the additional runtime fields needed for preview control:

```text
PreparedPreviewState: None | Prepared | Playing | Failed
PreparedPreviewElapsedMs: double
_isProjectingPreparedSelection: bool
```

No lock is required for this state because every command mutation and elapsed-time update runs on the game update thread. Game telemetry reads the same fields through the existing stage telemetry provider.

A preparation is cleared by:

- `cancelPreparedChart`;
- preparing a replacement chart;
- normal interactive row navigation away from the prepared row;
- normal interactive difficulty navigation away from the prepared slot;
- successful activation after the transition gate has accepted the request;
- stage deactivation.

`_isProjectingPreparedSelection` suppresses invalidation while `prepareVideoChart` itself projects the target row/difficulty. It also suppresses the normal automatic preview load during that projection; the exact resolved chart preview is loaded explicitly afterward.

Cleanup remains idempotent. `StopCurrentPreview` already nulls disposed resources, so repeated cleanup must never stop/dispose one instance twice.

### 2. Exact chart resolution uses the applied library snapshot

`prepareVideoChart` accepts only a non-empty, fully-qualified path.

On the update thread:

1. Require Song Select to have an applied `SongLibrarySnapshot`.
2. Normalize the requested chart path with `SongPathIdentity`.
3. Require it to be under one of `snapshot.ActiveRoots`.
4. Recursively walk `snapshot.RootSongs` and BOX children.
5. For each Score node, inspect the node's primary `DatabaseChart` and every chart in `DatabaseSong.Charts`.
6. Match by normalized path using the existing platform path-identity semantics. Never use title/artist text.
7. Require exactly one visible node/chart match.
8. Resolve the visible difficulty slot using the following precedence:
   - collect non-null score slots whose non-zero `SongScore.ChartId` equals the resolved `SongChart.Id`;
   - if exactly one slot matches, use it;
   - if multiple slots share that ChartId, use the **unique DRUMS slot** when exactly one candidate has `Instrument == EInstrumentPart.DRUMS`; this is the normal one-file multi-instrument case and matches CX's drum-gameplay path;
   - if ChartId did not establish a slot, scan valid score slots and reuse `SongChartHelper.GetCurrentDifficultyChart(node, i)`, matching that chart's normalized `FilePath` to the requested path;
   - use the fallback only when it yields one slot; fail rather than guess if ambiguity remains.
9. Retain the ancestor BOX chain needed to reproduce the normal browse context.

The DRUMS tie-break is required because one physical DTX file can legitimately populate DRUMS/GUITAR/BASS slots with the same `SongChart.Id`. Requiring ChartId uniqueness would reject normal indexed content.

This handles duplicate titles naturally because title never participates in identity. It also reuses the current SET/multi-chart mapping instead of introducing a second difficulty-order algorithm.

### 3. Reuse normal browse navigation, but do not synthesize input

Preparation must leave Song Select in a normal browse context with the target row visible.

Use existing stage behavior:

1. Switch to the All Songs projection and clear the active search/filter projection for this recorder operation.
2. Return to the root browse list and clear the navigation stack.
3. Reuse `NavigateIntoBox` for each resolved ancestor BOX so the normal breadcrumb/current-list state is rebuilt.
4. Select the target row and difficulty through `SongListDisplay`.

Add one narrow programmatic selection method to `SongListDisplay`, such as:

```text
SetSelection(index, difficulty)
```

It should atomically set row + difficulty, reset the scroll target consistently, and raise the existing `SelectionChanged` path once. This avoids the current property-order problem where `CurrentList` can temporarily select row 0 and load the wrong preview before the final index is applied.

This is an internal UI convenience, not a remote navigation API.

During prepare projection, `_isProjectingPreparedSelection` lets normal status/history/image/breadcrumb presentation update while skipping prepared-state invalidation and automatic preview loading. The flag is cleared in `finally` so a projection failure cannot leave normal Song Select behavior suppressed.

### 4. Prepared preview uses the resolved `SongChart`

This is the one place where blindly reusing current interactive preview loading is incorrect: `LoadPreviewSound(SongListNode)` reads `selectedNode.DatabaseChart`, which is the row's primary chart and may not be the chart selected by the recorder.

Extract/reuse the existing path/load primitives so prepared loading can accept the exact resolved `SongChart`:

```text
SongChart.FilePath directory
+ SongChart.PreviewFile
-> absolute preview path
-> existing TryLoadPreviewSoundFile
```

Prepare fails clearly when:

- `PreviewFile` is absent;
- the resolved preview file does not exist;
- the resource manager reports a load failure or unsupported asset.

After successful load:

- `_previewSound` is retained exactly as today;
- automatic preview delay is disabled while a prepared chart exists;
- no preview instance is created;
- preview state becomes `Prepared`;
- elapsed time is zero.

Normal interactive preview behavior remains unchanged when no preparation exists.

### 5. Explicit start owns exactly one looped preview instance

`startPreparedPreview` requires a prepared chart and loaded preview resource.

Reuse `CreatePreviewSoundInstance`, `SongSelectionUILayout.Audio.PreviewSoundVolume`, looping, `Play()`, and `StartBGMFade(true)`.

Behavior:

- first successful start creates one instance, sets volume/looping, plays, resets elapsed to zero, and reports `Playing`;
- a repeated start while already playing is idempotent success and does not create another instance;
- creation/playback failure disposes any partial instance, reports `Failed`, and never substitutes Song Select BGM or full-song audio.

In `SongSelectionStage.OnUpdate`, increment `PreparedPreviewElapsedMs` from `GameTime.ElapsedGameTime` only when prepared state is `Playing` **and** the sound instance currently reports `SoundState.Playing`.

If an explicitly-started instance unexpectedly stops, mark the prepared preview `Failed` rather than silently starting another source. HPA-503 will observe the state/elapsed telemetry and fail its bounded wait.

### 6. Navigation invalidation returns cleanly to interactive preview behavior

Prepared state must not survive when the visible selection no longer represents the prepared chart.

`OnSongSelectionChanged`:

- while `_isProjectingPreparedSelection` is true, do not invalidate preparation and skip its normal automatic preview load;
- otherwise, when the selected row differs from the prepared row, clear the prepared state first, then continue the handler's existing row-selection behavior; the normal `LoadPreviewSound(e.SelectedSong)` path therefore resumes the ordinary delayed primary-chart preview.

`OnDifficultyChanged`:

- while `_isProjectingPreparedSelection` is true, do not invalidate preparation;
- otherwise, when `e.NewDifficulty` differs from the prepared slot on the same row, clear the prepared state;
- after clearing, explicitly call the existing `LoadPreviewSound(e.Song)` path so the row returns to the same primary-chart delayed preview behavior normal Song Select uses after a row selection.

The difficulty-change reload is only an exit from prepared mode. Normal interactive difficulty changes when no prepared chart exists remain unchanged.

### 7. Activation observes the debounce gate before consuming preparation

Current `SelectSong` silently returns when `_game.CanPerformStageTransition()` is false. Prepared activation cannot clear state first and then call that method because the API could report success while no transition started.

Keep one shared transition path, but separate **eligibility** from **transition start** inside `SongSelectionStage`:

```text
CanStartSongSelection(node)
- valid Score node
- StageManager available
- _game.CanPerformStageTransition() == true

StartSongSelection(node)
- _game.MarkStageTransition()
- build selectedSong / selectedDifficulty / songId shared data
- StageManager.ChangeStage(SongTransition, ...)
```

The existing player `SelectSong` path calls these two helpers and may ignore the returned bool.

`activatePreparedChart`:

1. Require a current preparation.
2. Require `_selectedSong` / `_currentDifficulty` still match it.
3. Evaluate the same transition-eligibility gate.
4. If debounce or stage-manager availability blocks the transition, return `success=false` and **leave the preparation and preview intact**.
5. Once eligibility succeeds, stop/dispose the prepared preview and clear prepared state.
6. Immediately call the shared transition-start body on the same update-thread action.

Do not wait 0.5 seconds inside the game and do not duplicate the transition shared-data construction. A successful result means the normal `SongTransitionStage` change was actually started.

### 8. Game API commands queue and await the update-thread result

Extend `IGameApi` / `GameApiImplementation` with four explicit methods. Keep the result contract narrow, for example:

```text
PreparedChartCommandResult
- bool Success
- string? Error
```

`GameApiImplementation` should use one private helper that:

- creates a `TaskCompletionSource` with `TaskCreationOptions.RunContinuationsAsynchronously`;
- queues an action through `IGameContext.QueueMainThreadAction`;
- inside the queued action, verifies the current stage is `SongSelectionStage` and executes the requested stage command;
- completes the task with the command result.

The caller therefore receives the actual prepare/start/cancel/activate result, not merely "action queued". Do not copy `ChangeStageAsync`'s fire-and-forget semantics and do not add a general main-thread dispatcher abstraction.

### 9. JSON-RPC stays explicit and authenticated

Add four cases/handlers to `JsonRpcServer`:

```text
prepareVideoChart   params: { chartPath: string }
startPreparedPreview
activatePreparedChart
cancelPreparedChart
```

The existing server-level API-key check already protects them.

Validate JSON shape in the server. Stage/domain failures may be returned as a normal command result (`success=false`, sanitized `error`) so callers receive useful failure text without adding a new JSON-RPC error taxonomy.

Do not expose any method that accepts arbitrary stage/action names or mutation payloads.

### 10. Automation exposes the same four narrow operations

Extend `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs` with public methods for the four commands. Reuse its private `SendAsync` transport and one small command-result parser.

HPA-503 should be able to call:

```text
PrepareVideoChartAsync(path)
StartPreparedPreviewAsync()
ActivatePreparedChartAsync()
CancelPreparedChartAsync()
```

without accessing generic JSON-RPC.

A `success=false` result becomes a clear `InvalidOperationException` containing the server-provided safe error message. In particular, a debounce-blocked activation is an observable failed command rather than a silent no-op.

### 11. Telemetry adds only recorder-required fields and locks the wire names

Extend `GameTelemetrySnapshot` with:

```text
PreparedChartIdentity
PreparedPreviewState
PreparedPreviewElapsedMs
```

Because `JsonRpcServer` serializes DTOs with `JsonNamingPolicy.CamelCase`, the wire contract is exactly:

```text
preparedChartIdentity
preparedPreviewState
preparedPreviewElapsedMs
```

`SongSelectionStage.PopulateTelemetry` supplies the producer values from stage-owned prepared state.

Identity policy:

- prefer `chart:<SongChart.Id>` when the indexed chart has a stable non-zero database ID;
- otherwise use a root-relative normalized path, never the absolute song-root prefix.

Extend `DTXMania.Automation.Telemetry.GameStateSnapshot` with read-only accessors for those exact camelCase keys.

The existing `DTXMania.E2E/AutomationContractTests.GameTelemetrySnapshot_CamelCaseRoundTrip_ShouldExposeAllConsumedFields` test **must** be extended with all three producer values and assertions against all three Automation accessors. This is a required producer/consumer contract gate, not an optional extra test.

Screenshot remains the render barrier: after prepare returns, HPA-503 calls the existing screenshot operation and successful image completion proves Song Select completed a Draw pass.

## Failure Semantics

Preparation fails without leaving active prepared state for:

- blank or non-absolute path;
- library not ready;
- path outside every active root;
- no exact indexed chart match;
- ambiguous indexed match;
- no unique visible difficulty slot after the DRUMS tie-break and existing path-helper fallback;
- missing preview declaration/file;
- unreadable or unsupported preview resource.

Start fails for no prepared chart/resource or playback creation failure.

Activation fails if preparation is absent, the prepared selection is no longer active, the stage manager is unavailable, or the global transition debounce is active. Debounce/stage-manager failure leaves the preparation intact for retry.

Cancellation is idempotent success.

Errors should describe the category without publishing an absolute path into telemetry/log snapshots intended for recorder diagnostics.

## Testing Strategy

Add focused tests rather than a second end-to-end framework.

### Song Select

Cover:

- root-level exact path resolution;
- nested BOX resolution and browse context;
- multi-chart/SET row resolves the exact requested chart and correct difficulty slot;
- ordinary one-file multi-instrument row where DRUMS/GUITAR/BASS slots share one ChartId resolves the unique DRUMS slot;
- duplicate titles resolve by path;
- outside-root and unindexed failures;
- resolved chart preview is used even when it differs from the node's primary chart preview;
- missing/unreadable/unsupported preview failure;
- prepare leaves preview loaded but stopped and suppresses automatic timer playback;
- prepare projection does not invalidate itself through `SelectionChanged`/`DifficultyChanged`;
- repeated start creates only one instance;
- elapsed time advances only while the instance reports Playing;
- row navigation away clears prepared state and resumes normal row preview behavior;
- difficulty navigation away clears prepared state and resumes the normal primary-chart delayed preview;
- cancel, replacement, user navigation, and Deactivate clean up exactly once;
- activation while debounce is blocked returns failure, starts no transition, and preserves prepared state/preview;
- activation after the gate is available clears preview and reuses the existing transition data path;
- normal interactive preview delay/loop/BGM behavior remains unchanged with no prepared chart.

### Game API / JSON-RPC

Cover:

- command action is queued to the main thread and the returned task completes after execution;
- command rejects non-SongSelect current stage;
- `prepareVideoChart` parameter validation;
- authenticated JSON-RPC routing and success/failure result shape;
- debounce-blocked activation propagates `success=false` instead of returning a queued-success result.

### Automation contract

Cover:

- each new public client method sends the expected method/params;
- command failure text is propagated;
- `AutomationContractTests.GameTelemetrySnapshot_CamelCaseRoundTrip_ShouldExposeAllConsumedFields` includes `preparedChartIdentity`, `preparedPreviewState`, and `preparedPreviewElapsedMs` and proves the Automation accessors read them.

Use existing unit-test seams and mocks. No real MonoGame window, OBS server, or recorder CLI is required for HPA-510 unit coverage.

## Expected Scope

This remains one implementation PR, approximately 2–3 engineer days:

- exact resolver + browse projection;
- prepared preview lifecycle + navigation/debounce-safe activation + telemetry;
- explicit Game API/JSON-RPC/Automation wiring;
- focused regression/contract tests.

If implementation starts producing a generic automation framework, recorder session object, or alternate Song Select data model, reduce scope back to the stage-owned four-command contract above.