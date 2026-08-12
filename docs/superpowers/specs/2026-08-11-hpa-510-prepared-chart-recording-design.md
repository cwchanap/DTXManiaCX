# HPA-510 Prepared Chart Recording Commands Design

**Issue:** [HPA-510](https://linear.app/cwchanap/issue/HPA-510/add-minimal-prepared-chart-recording-commands-in-song-select)  
**Date:** 2026-08-11  
**Status:** Revised after second implementation review

## Context

HPA-501 has landed on `main`, so the reusable `DTXMania.Automation` process and JSON-RPC foundation is available. HPA-510 is the remaining CX-side prerequisite for HPA-503's Windows recording vertical slice.

The recorder needs exactly four Song Select operations:

```text
prepareVideoChart(chartPath)
startPreparedPreview()
activatePreparedChart()
cancelPreparedChart()
```

The implementation must prepare one exact indexed chart through normal Song Select state, hold its declared preview audio stopped, play it only on command, and activate it through the same Song Select -> SongTransition path used by a player.

Prepared state remains owned by `SongSelectionStage`. Do not add a recorder session, generic mutation endpoint, database query path, or synthetic-input navigation.

## Goals

- Resolve one caller-supplied absolute chart path against the currently applied `SongLibrarySnapshot`.
- Support root-level charts, nested BOX hierarchy, SET/multi-chart rows, ordinary one-file multi-instrument rows, and duplicate titles.
- Project the exact row + difficulty through normal Song Select UI state.
- Load preview audio from the resolved `SongChart`, not merely the row's primary `DatabaseChart`.
- Keep preview loaded but stopped until `startPreparedPreview`.
- Reuse current preview volume, loop, BGM fade, sound resource, and cleanup behavior.
- Keep every stage mutation on the game update thread and await the real command outcome.
- Reuse the normal `SelectSong` transition construction while surfacing transition-debounce rejection.
- Expose exactly `PreparedChartIdentity`, `PreparedPreviewState`, and `PreparedPreviewElapsedMs` to HPA-503.
- Lock the camelCase game -> Automation telemetry contract.
- Validate the command path once against a live game using the existing E2E fixture/CI machinery.

## Non-goals

- General Song Select remote navigation.
- Generic game-thread dispatch or arbitrary mutation APIs.
- A capture/recording session object or state machine.
- A second song repository/query layer.
- A new render-readiness state machine; screenshot completion remains the presentation barrier.
- OBS, process launch, sandbox construction, video finalization, or Result timing.
- Restoring the pre-command tab/filter/breadcrumb after cancel; HPA-503 uses disposable app data.
- Changing normal interactive preview behavior when no prepared chart exists.
- Waiting inside the game for transition debounce to expire.
- Direct transition to Performance.

## Verified reuse seams

Use the existing code rather than parallel abstractions:

- `_appliedLibrarySnapshot`, `GetNodeChartPaths`, and `SongPathIdentity` for exact active-library path resolution.
- `RestoreNavigationPaths(paths, snapshot)` for rebuilding `_navigationStack`, `_currentSongList`, and `_currentBreadcrumb` without repopulating at every BOX level.
- `RefreshSongListForActiveTab()` for one final list projection after navigation state is rebuilt.
- `SongListDisplay` for the final row/difficulty UI selection.
- `SongListNode.Scores[].ChartId` plus `SongChartHelper.GetCurrentDifficultyChart` for difficulty mapping.
- `TryLoadPreviewSoundFile`, `CreatePreviewSoundInstance`, `StopCurrentPreview`, and `StartBGMFade` for preview lifecycle.
- `IGameContext.QueueMainThreadAction` plus `TaskCompletionSource(...RunContinuationsAsynchronously)` for awaited game-thread commands.
- `SelectSong` for normal SongTransition shared-data construction.
- `JsonRpcServer`'s existing authenticated method switch and `JsonRpcGameClient.SendAsync` for transport.
- `DTXMania.E2E/AutomationContractTests.cs` for game -> camelCase -> Automation telemetry contract coverage.
- `E2EFixtureBuilder`, `E2EGameLaunch`, `Eventually`, and the existing Windows `Category=E2E` CI job for one live validation.

`GetStableSelectionIdentity` is **not** the prepared-chart telemetry identity seam. It is a row-selection identity: it prefers `song:<DatabaseSongId>` when present and only falls back to the first chart path. HPA-510 must identify the exact resolved chart/difficulty, not merely its row.

## Chosen architecture

### 1. Minimal stage-owned prepared state

Keep one update-thread-owned record:

```text
PreparedChartSelection
- SongListNode node
- SongChart chart
- int difficultyIndex
- string telemetryIdentity
```

Additional fields:

```text
PreparedPreviewState: None | Prepared | Playing | Failed
PreparedPreviewElapsedMs: double
_isProjectingPreparedSelection: bool
```

No lock, session ID, generation object, or separate state machine is required.

Prepared state is cleared by:

- cancel;
- replacement prepare;
- real user row navigation away;
- real user difficulty navigation away;
- successful activation after transition eligibility is confirmed;
- stage deactivation.

The projection suppression flag is scoped to the **entire prepare projection**, not only the final row selection.

### 2. Exact path resolver over the applied snapshot

`prepareVideoChart` accepts only a non-blank fully-qualified chart path.

On the update thread:

1. Require `_appliedLibrarySnapshot`.
2. Normalize the requested path with `SongPathIdentity`.
3. Require containment under one of `snapshot.ActiveRoots`.
4. Recursively walk `snapshot.RootSongs` and BOX children.
5. For Score nodes, inspect `DatabaseChart` plus `DatabaseSong.Charts`.
6. Match normalized chart path only; never title/artist.
7. Require one exact node/chart match.
8. Resolve the visible difficulty slot:
   - collect non-null score slots with matching non-zero `ChartId`;
   - if exactly one matches, use it;
   - when several slots share the same ChartId, use the unique DRUMS slot if exactly one candidate is DRUMS;
   - otherwise scan valid slots through `GetCurrentDifficultyChart(node, index)` and compare the returned chart path to the resolved path;
   - fail if no unique slot remains.
9. Return the node, resolved chart, difficulty index, and ancestor BOX paths.

The DRUMS tie-break is required because a single physical DTX can populate DRUMS/GUITAR/BASS slots with the same `SongChart.Id`.

### 3. Projection uses one existing navigation rebuild and one UI refresh

Do **not** call `NavigateIntoBox` once per ancestor. It calls `PopulateSongList()` on each level; `SongListDisplay.CurrentList` resets selection to index 0 and raises `SelectionChanged`, which would repeatedly stop/load the wrong preview during prepare.

Instead, run the whole projection inside:

```text
_isProjectingPreparedSelection = true
try
    reset recorder-owned tab/filter projection
    RestoreNavigationPaths(ancestorPaths, snapshot)
    RefreshSongListForActiveTab() exactly once
    SetSelection(targetIndex, difficultyIndex)
finally
    _isProjectingPreparedSelection = false
```

Add one narrow `SongListDisplay.SetSelection(index, difficulty)` method that applies row + difficulty atomically, updates scroll state coherently, and raises the normal selection path once.

During the suppression scope, normal status/history/image/breadcrumb presentation may update, but prepared-state invalidation and normal automatic preview loading must not run.

This is an internal UI seam, not a remote navigation API.

### 4. Prepared preview loads the exact resolved chart

Current `LoadPreviewSound(SongListNode)` hardcodes `selectedNode.DatabaseChart`. Prepared mode must instead resolve:

```text
resolvedChart.FilePath directory
+ resolvedChart.PreviewFile
-> absolute preview path
-> TryLoadPreviewSoundFile
```

Prepare fails for missing declaration, missing file, unreadable/unsupported load, or null sound resource.

One important existing side effect must be overridden explicitly: successful `TryLoadPreviewSoundFile` sets:

```text
_previewPlayDelay = 0
_isPreviewDelayActive = true
```

After the prepared load succeeds, immediately force:

```text
_previewPlayDelay = 0
_isPreviewDelayActive = false
```

so the normal one-second timer cannot auto-start the prepared preview.

Successful prepare leaves `_previewSound` retained, `_previewSoundInstance == null`, state `Prepared`, and elapsed `0`.

### 5. Explicit preview start and elapsed telemetry

`startPreparedPreview`:

- requires a prepared chart/resource;
- creates one instance with the existing helper;
- applies existing preview volume and `IsLooped = true`;
- calls `Play()` once;
- starts the existing BGM fade-out;
- sets state `Playing` and elapsed `0`;
- returns idempotent success when already playing without creating a second instance.

On each Song Select update, increment `PreparedPreviewElapsedMs` only while state is `Playing` **and** the sound instance reports `SoundState.Playing`.

If the explicitly-started instance unexpectedly stops, set state `Failed`; do not auto-restart or substitute Song Select BGM/full-song audio.

`PreparedPreviewElapsedMs` stays in scope because both HPA-510 and HPA-503 explicitly require CX-reported actual playback time, and HPA-503 polls `>= 10_000`. Caller wall-clock time is not the accepted contract.

### 6. User navigation exits prepared mode cleanly

`OnSongSelectionChanged`:

- while `_isProjectingPreparedSelection`, do not invalidate prepared state and skip normal automatic preview loading;
- otherwise, if the row moves away from the prepared row, clear prepared state first and continue today's normal row-selection preview path.

`OnDifficultyChanged`:

- while `_isProjectingPreparedSelection`, do not invalidate;
- otherwise, if difficulty moves away from the prepared slot, clear prepared state;
- after this prepared-mode exit, reload the row through the existing `LoadPreviewSound(e.Song)` path so normal delayed primary-chart preview behavior resumes;
- leave ordinary difficulty changes unchanged when there is no preparation.

Cleanup remains idempotent.

### 7. Activation separates eligibility from transition start

Current `SelectSong` silently returns if `_game.CanPerformStageTransition()` is false. Prepared activation must observe this rather than reporting success after a no-op.

Factor the existing method into one small eligibility gate and one shared start body, e.g.:

```text
CanStartSongSelection(node)
StartSongSelection(node)
```

Eligibility checks:

- valid Score node;
- `StageManager` available;
- `_game.CanPerformStageTransition()`.

The start body remains the sole owner of:

```text
_game.MarkStageTransition()
selectedSong / selectedDifficulty / songId
StageManager.ChangeStage(StageType.SongTransition, ...)
```

`activatePreparedChart`:

1. Require the prepared row/difficulty still match current selection.
2. Check the shared transition eligibility.
3. If blocked, return `success=false` and leave prepared state/preview intact.
4. If eligible, stop/dispose the preview and clear prepared state.
5. Immediately execute the shared transition-start body on the same update-thread action.
6. Return success only after the normal stage change has been invoked.

Do not wait 0.5 seconds inside the game.

### 8. Result contract stays narrow and intentionally untyped

Use:

```text
PreparedChartCommandResult
- bool Success
- string? Error
```

Do **not** add a cross-assembly error-code enum in HPA-510. The only planned consumer, HPA-503, treats any command failure as a failed recording run. Its activation occurs only after at least 10 seconds of prepared preview playback, far beyond the 0.5-second global transition debounce, so it has no required retry branch that needs to distinguish `TransitionBlocked` from fatal prepare errors.

Keeping preparation intact on a blocked activation is local correctness and preserves diagnostics/manual retry capability; it is not a new recorder retry protocol. Add structured error codes only when a real caller needs programmatic recovery.

### 9. Game API and JSON-RPC queue and await real results

Add four explicit `IGameApi` methods and one private `GameApiImplementation` queue helper:

- create `TaskCompletionSource` with asynchronous continuations;
- queue through `IGameContext.QueueMainThreadAction`;
- verify current stage is `SongSelectionStage` inside the queued action;
- execute the stage command;
- complete with its actual result.

Do not copy `ChangeStageAsync`'s fire-and-forget semantics and do not create a generic dispatcher.

`JsonRpcServer` adds only:

```text
prepareVideoChart { chartPath }
startPreparedPreview
activatePreparedChart
cancelPreparedChart
```

Existing API-key authentication protects them. Domain failures return `{ success:false, error:"..." }` without a new JSON-RPC error taxonomy.

`DTXMania.Automation.JsonRpcGameClient` adds four public wrappers over private `SendAsync`; `success=false` throws `InvalidOperationException` with the safe server message.

### 10. Telemetry keeps all three accepted fields

Producer fields:

```text
PreparedChartIdentity
PreparedPreviewState
PreparedPreviewElapsedMs
```

Wire names are exactly:

```text
preparedChartIdentity
preparedPreviewState
preparedPreviewElapsedMs
```

Identity policy remains exact-chart-specific and non-absolute:

- `chart:<SongChart.Id>` when the resolved chart has a stable non-zero database ID;
- otherwise a normalized root-relative chart path.

Do not use `GetStableSelectionIdentity`: it often returns `song:<id>` and therefore cannot identify which chart/difficulty inside a multi-chart row was prepared. Do not expose the absolute requested path; HPA-510 explicitly requires a stable database/chart identity or redacted root-relative path.

Extend `GameStateSnapshot` with accessors for all three keys, and extend `AutomationContractTests.GameTelemetrySnapshot_CamelCaseRoundTrip_ShouldExposeAllConsumedFields` with non-default producer values and assertions for all three consumer accessors.

### 11. Validation uses the existing E2E harness, not a manual checklist

Retain focused unit/contract tests, then add one live `Category=E2E` prepared-chart smoke using existing `E2EFixtureBuilder`, `E2EGameLaunch`, `JsonRpcGameClient`, and `Eventually`.

Minimal fixture change: add a `#PREVIEW` line referencing the already-generated `autoplay-tone.wav`.

Live flow:

1. Launch the existing isolated fixture and enter Song Select.
2. `prepareVideoChart(fixture.ChartPath)`.
3. Take a screenshot as the render barrier.
4. Wait past the normal automatic preview delay and assert prepared state remains `Prepared` with elapsed `0`.
5. `startPreparedPreview()`.
6. Poll until state is `Playing` and `PreparedPreviewElapsedMs >= 10_000`.
7. `activatePreparedChart()`.
8. Assert SongTransition is observed before Performance.

This reuses the existing Windows E2E job; it does not add an E2E framework, fake OBS server, or recorder harness.

## Failure semantics

Preparation returns failure and leaves no prepared state for:

- blank/non-absolute path;
- library not ready;
- path outside active roots;
- unindexed path;
- ambiguous node/chart match;
- unresolved/ambiguous difficulty slot;
- missing preview declaration/file;
- preview load failure.

Start fails for missing preparation/resource or sound-instance/playback failure.

Activation fails if preparation is absent, current row/difficulty no longer matches it, or transition eligibility is blocked. A blocked transition preserves the preparation.

Cancel is idempotent success.

## Risks and mitigations

### Projection side effects — primary risk

Repeated `NavigateIntoBox -> PopulateSongList -> CurrentList` would repeatedly select row 0 and trigger preview/BGM side effects during prepare. Mitigate by using `RestoreNavigationPaths`, one `RefreshSongListForActiveTab`, one atomic `SetSelection`, and one suppression scope around the entire projection.

### Exact-chart vs row identity

Existing row identity can collapse a multi-chart row to `song:<id>`. Keep resolver/telemetry identity tied to the resolved `SongChart` instead.

### Preview auto-start leakage

`TryLoadPreviewSoundFile` activates the normal delay timer on successful load. Prepared load must explicitly clear `_isPreviewDelayActive` before returning success.

### Activation no-op

Global transition debounce currently fails silently in `SelectSong`. Surface the eligibility result before consuming preparation.

## Expected scope

Still one implementation PR, approximately 2–3 engineer days:

1. exact resolver + side-effect-free browse projection;
2. prepared preview lifecycle + activation + telemetry;
3. explicit Game API/JSON-RPC/Automation wiring + contract tests;
4. focused regression tests + one existing-harness live E2E smoke.

If implementation starts producing a session framework, database repository, generic mutation dispatcher, alternate navigation model, or separate preview abstraction, reduce scope back to the four-command stage-owned design.
