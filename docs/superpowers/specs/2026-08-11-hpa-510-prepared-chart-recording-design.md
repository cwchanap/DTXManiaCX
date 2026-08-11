# HPA-510 Prepared Chart Recording Commands Design

**Issue:** [HPA-510](https://linear.app/cwchanap/issue/HPA-510/add-minimal-prepared-chart-recording-commands-in-song-select)  
**Date:** 2026-08-11  
**Status:** Draft for implementation review

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
- Support root-level charts, nested BOX hierarchy, multi-chart/SET-defined rows, and duplicate titles.
- Project the resolved chart through the existing Song Select presentation path so status, preview image, play history, selected row, difficulty, and breadcrumb are normal UI state.
- Load the preview declared by the **resolved chart**, not merely the node's primary chart.
- Keep the prepared preview stopped until explicitly started.
- Reuse the existing preview sound resource, volume, looping, and BGM fade behavior.
- Keep all stage mutations on the game update thread.
- Activate through the existing `SongSelectionStage.SelectSong` transition path.
- Expose only the prepared identity/state/elapsed telemetry required by HPA-503.
- Extend `DTXMania.Automation` with the four explicit client calls so HPA-503 never needs access to the private generic JSON-RPC transport.

## Non-goals

- A general Song Select remote-navigation API.
- A generic command dispatcher or arbitrary main-thread mutation endpoint.
- A game-wide recording/capture coordinator or immutable recording session ID.
- A separate render-generation/readiness state machine. Screenshot completion remains the presentation barrier.
- OBS, process launch, app-data sandboxing, Result hold timing, or video artifact handling.
- Changing the normal interactive preview delay or primary-chart preview behavior when no prepared chart is active.
- Restoring a pre-command filter/tab/breadcrumb after cancellation. The recorder runs in disposable app data; snapshot/restore machinery is unnecessary for MVP.
- Direct transition to Performance.

## Current Reuse Survey

The required building blocks already exist:

- `SongSelectionStage` owns selection, BOX navigation, status/history/preview UI, preview sound lifecycle, BGM fading, and the player activation path.
- `SongSelectionStage` already retains the coherent applied `SongLibrarySnapshot`, including `RootSongs` and canonical `ActiveRoots`.
- `SongSelectionStage.GetNodeChartPaths` already understands that a visible row can represent the primary `DatabaseChart` plus additional `DatabaseSong.Charts`.
- `SongPathIdentity` already owns path normalization and root containment semantics.
- `SongListNode.Scores` carries difficulty slots; persisted multi-chart rows carry `SongScore.ChartId`.
- `SongChartHelper.GetCurrentDifficultyChart` is the existing runtime fallback from a visible difficulty slot to its chart and is useful for legacy/SET rows where a direct `ChartId` match is unavailable.
- `SongListDisplay` already owns row index, difficulty, selection events, scroll target, and normal Song Select presentation updates.
- `SongSelectionStage.LoadPreviewSound` / `TryLoadPreviewSoundFile`, `CreatePreviewSoundInstance`, `StopCurrentPreview`, and `StartBGMFade` already implement the preview resource and playback behavior.
- `IGameContext.QueueMainThreadAction` is the existing mutation seam used by the Game API.
- `JsonRpcServer` already authenticates every JSON-RPC request and has explicit method routing.
- `GameTelemetrySnapshot` plus `IStageTelemetryProvider` is the existing producer telemetry seam.
- `DTXMania.Automation.JsonRpc.JsonRpcGameClient` now owns the consumer JSON-RPC transport and keeps `SendAsync` private.

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
```

No lock is required for this state because every command mutation and elapsed-time update runs on the game update thread. Game telemetry reads the same fields through the existing stage telemetry provider.

A preparation is cleared by:

- `cancelPreparedChart`;
- preparing a replacement chart;
- normal interactive row/difficulty navigation away from the prepared selection;
- activation;
- stage deactivation.

Cleanup remains idempotent. `StopCurrentPreview` already nulls disposed resources, so repeated cleanup must never stop/dispose one instance twice.

### 2. Exact chart resolution uses the applied library snapshot

`prepareVideoChart` accepts only a non-empty, fully-qualified path.

On the update thread:

1. Require Song Select to have an applied `SongLibrarySnapshot`.
2. Normalize the requested chart path with `SongPathIdentity`.
3. Require it to be under one of `snapshot.ActiveRoots`.
4. Recursively walk `snapshot.RootSongs` and BOX children.
5. For each Score node, inspect the node's primary `DatabaseChart` and every chart in `DatabaseSong.Charts`.
6. Match by normalized path using current-platform path identity rules. Never use title/artist text.
7. Require exactly one visible node/chart match.
8. Resolve the visible difficulty slot:
   - first match the resolved `SongChart.Id` against non-zero `SongListNode.Scores[i].ChartId`;
   - when that is unavailable, scan valid score slots and reuse `SongChartHelper.GetCurrentDifficultyChart(node, i)`, matching its normalized `FilePath` to the requested path;
   - fail rather than guess if no unique slot can be established.

The recursive walk should also retain the ancestor BOX chain needed to reproduce the normal browse context.

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

During the synchronous prepare projection, set a stage-local suppression flag so normal `OnSongSelectionChanged` presentation still runs but automatic preview loading/playback is skipped. After the target selection is established, load the resolved chart's preview explicitly.

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

### 6. Cancellation and activation reuse existing cleanup/transition paths

`cancelPreparedChart`:

- stops/disposes the current preview through existing cleanup;
- releases the preview sound reference;
- clears prepared identity/state/elapsed;
- allows the normal Song Select BGM fade-in behavior to resume.

`activatePreparedChart`:

1. Require a current preparation.
2. Capture the prepared node/difficulty locally.
3. Stop/dispose the prepared preview and clear prepared state before transition audio can bleed.
4. Ensure `_selectedSong` / `_currentDifficulty` still match the prepared selection.
5. Call the existing `SelectSong(node)` method.

Do not reproduce its shared-data construction. The existing path remains the sole owner of `selectedSong`, `selectedDifficulty`, `songId`, and the transition to `SongTransitionStage`.

### 7. Game API commands queue and await the update-thread result

Extend `IGameApi` / `GameApiImplementation` with four explicit methods. Keep the result contract narrow, for example:

```text
PreparedChartCommandResult
- bool Success
- string? Error
```

`GameApiImplementation` should use one private helper that:

- creates a `TaskCompletionSource` with asynchronous continuations;
- queues an action through `IGameContext.QueueMainThreadAction`;
- inside the queued action, verifies the current stage is `SongSelectionStage` and executes the requested stage command;
- completes the task with the command result.

The caller therefore receives the actual prepare/start/cancel/activate result, not merely "action queued". Do not add a general main-thread dispatcher abstraction.

### 8. JSON-RPC stays explicit and authenticated

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

### 9. Automation exposes the same four narrow operations

Extend `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs` with public methods for the four commands. Reuse its private `SendAsync` transport and one small command-result parser.

HPA-503 should be able to call:

```text
PrepareVideoChartAsync(path)
StartPreparedPreviewAsync()
ActivatePreparedChartAsync()
CancelPreparedChartAsync()
```

without accessing generic JSON-RPC.

A `success=false` result becomes a clear `InvalidOperationException` containing the server-provided safe error message.

### 10. Telemetry adds only recorder-required fields

Extend `GameTelemetrySnapshot` with:

```text
PreparedChartIdentity
PreparedPreviewState
PreparedPreviewElapsedMs
```

`SongSelectionStage.PopulateTelemetry` supplies them only from stage-owned prepared state.

Identity policy:

- prefer `chart:<SongChart.Id>` when the indexed chart has a stable non-zero database ID;
- otherwise use a root-relative normalized path, never the absolute song-root prefix.

Extend `DTXMania.Automation.Telemetry.GameStateSnapshot` with corresponding read-only accessors. Do not expose the absolute requested path in telemetry.

Screenshot remains the render barrier: after prepare returns, HPA-503 calls the existing screenshot operation and successful image completion proves Song Select completed a Draw pass.

## Failure Semantics

Preparation fails without leaving active prepared state for:

- blank or non-absolute path;
- library not ready;
- path outside every active root;
- no exact indexed chart match;
- ambiguous indexed match;
- no unique visible difficulty slot;
- missing preview declaration/file;
- unreadable or unsupported preview resource.

Start fails for no prepared chart/resource or playback creation failure.

Activation fails if preparation is absent or the prepared selection is no longer the active Song Select selection.

Cancellation is idempotent success.

Errors should describe the category without publishing an absolute path into telemetry/log snapshots intended for recorder diagnostics.

## Testing Strategy

Add focused tests rather than a second end-to-end framework.

### Song Select

Cover:

- root-level exact path resolution;
- nested BOX resolution and browse context;
- multi-chart/SET row resolves the exact requested chart and correct difficulty slot;
- duplicate titles resolve by path;
- outside-root and unindexed failures;
- resolved chart preview is used even when it differs from the node's primary chart preview;
- missing/unreadable/unsupported preview failure;
- prepare leaves preview loaded but stopped and suppresses automatic timer playback;
- repeated start creates only one instance;
- elapsed time advances only while the instance reports Playing;
- cancel, replacement, user navigation, and Deactivate clean up exactly once;
- activation stops preview before reusing `SelectSong`;
- normal interactive preview delay/loop/BGM behavior remains unchanged with no prepared chart.

### Game API / JSON-RPC

Cover:

- command action is queued to the main thread and the returned task completes after execution;
- command rejects non-SongSelect current stage;
- `prepareVideoChart` parameter validation;
- authenticated JSON-RPC routing and success/failure result shape.

### Automation contract

Cover:

- each new public client method sends the expected method/params;
- command failure text is propagated;
- prepared telemetry accessors deserialize from game JSON.

Use existing unit-test seams and mocks. No real MonoGame window, OBS server, or recorder CLI is required for HPA-510 unit coverage.

## Expected Scope

This should remain one implementation PR, approximately 2–3 engineer days:

- exact resolver + browse projection;
- prepared preview lifecycle + telemetry;
- explicit Game API/JSON-RPC/Automation wiring;
- focused regression/contract tests.

If implementation starts producing a generic automation framework, recorder session object, or alternate Song Select data model, reduce scope back to the stage-owned four-command contract above.
