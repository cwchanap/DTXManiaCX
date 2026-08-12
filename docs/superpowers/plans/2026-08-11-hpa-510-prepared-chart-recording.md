# HPA-510 Prepared Chart Recording Commands Implementation Plan

**Issue:** [HPA-510](https://linear.app/cwchanap/issue/HPA-510/add-minimal-prepared-chart-recording-commands-in-song-select)  
**Design:** `docs/superpowers/specs/2026-08-11-hpa-510-prepared-chart-recording-design.md`  
**Target size:** one implementation PR, 2–3 engineer days

## Objective

Add the smallest production API HPA-503 needs to prepare one exact indexed chart in Song Select, explicitly start its declared preview, activate it through the normal player path, and cancel/clean up safely.

Keep the implementation local to existing Song Select, Game API/JSON-RPC, telemetry, and Automation seams. Do not introduce a general automation/session framework.

## Global Constraints

- Prepared state remains owned by `SongSelectionStage`.
- The external contract remains exactly four commands: prepare, start preview, activate, cancel.
- Exact chart identity is normalized path, never title.
- All stage mutations execute on the game update thread and the Game API awaits their real result.
- Activation must respect the existing global stage-transition debounce and must not consume preparation when the debounce blocks the transition.
- HPA-510 selects the DRUMS slot when one physical DTX chart populates multiple instrument slots with the same `ChartId`.
- Screenshot completion remains the Song Select render barrier; add no render-generation state machine.
- The telemetry wire names are exactly `preparedChartIdentity`, `preparedPreviewState`, and `preparedPreviewElapsedMs`.
- No recorder session, DB query path, generic mutation API, synthetic key navigation, OBS, process, or FFmpeg work belongs in this PR.

## File Map

### Game / Song Select

- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
  - exact active-library chart resolution;
  - prepared selection/preview state;
  - programmatic browse projection;
  - clear-on-row/difficulty navigation rules;
  - debounce-safe prepare/start/activate/cancel commands;
  - elapsed preview telemetry.
- `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`
  - one narrow atomic row+difficulty programmatic selection method.
- `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`
  - three prepared-recording telemetry fields.

### Game API / transport

- `DTXMania.Game/Lib/GameApi.cs`
  - four explicit command methods and narrow command result contract.
- `DTXMania.Game/Lib/GameApiImplementation.cs`
  - queue each command through the existing main-thread action queue and await its actual result.
- `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs`
  - four explicit authenticated JSON-RPC routes/handlers.

### Automation consumer

- `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs`
  - public wrappers for the four new operations.
- `DTXMania.Automation/Telemetry/GameStateSnapshot.cs`
  - accessors for the three exact camelCase telemetry keys.

### Tests

- `DTXMania.Test/Stage/SongSelectionStagePreparedChartTests.cs` — new focused fixture.
- `DTXMania.Test/GameApi/GameApiImplementationTests.cs` — extend.
- `DTXMania.Test/JsonRpc/JsonRpcServerInternalTests.cs` and/or the existing JSON-RPC integration fixture — extend only where current seams fit.
- `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs` — extend.
- `DTXMania.E2E/AutomationContractTests.cs` — **mandatory** producer/consumer camelCase telemetry contract update.

Do not add a new E2E harness or recorder test project in this ticket.

---

## Task 1 — Resolve and project an exact indexed chart

**Goal:** given one absolute chart path, identify exactly one active Song Select row + gameplay difficulty and show it through the normal UI without synthetic input.

### 1.1 Write failing focused resolver tests first

In `SongSelectionStagePreparedChartTests.cs`, construct minimal published-library hierarchies covering:

- root-level chart;
- chart below one or more BOX nodes;
- multi-chart/SET-style row where the requested chart is not the node's primary chart;
- duplicate song titles with different file paths;
- ordinary one-file multi-instrument chart where DRUMS/GUITAR/BASS score slots all carry the same non-zero `ChartId`;
- path outside active roots;
- path under an active root but absent from the indexed snapshot;
- a genuinely ambiguous slot mapping after all supported tie-break/fallback rules.

For the shared-ChartId fixture, assert the resolver chooses the unique `EInstrumentPart.DRUMS` slot rather than rejecting the row.

Assertions must prove title never participates in identity.

### 1.2 Add a stage-private exact-path resolver

In `SongSelectionStage.cs`:

- require a fully-qualified non-blank path;
- use `_appliedLibrarySnapshot` as the authoritative active library;
- normalize with `SongPathIdentity`;
- require containment under `snapshot.ActiveRoots`;
- recursively traverse root/BOX nodes and inspect both `DatabaseChart` and `DatabaseSong.Charts`;
- require one exact normalized node/chart match;
- retain the ancestor BOX chain and matched `SongChart` in the resolution result.

Resolve the visible gameplay slot in this order:

1. Collect non-null `Scores[i]` with non-zero `ChartId == resolvedChart.Id`.
2. If exactly one matches, use it.
3. If multiple match, select the **unique DRUMS** candidate when exactly one has `Instrument == EInstrumentPart.DRUMS`.
4. If no slot was established, scan valid slots with `SongChartHelper.GetCurrentDifficultyChart(node, i)` and match the helper result's normalized `FilePath` to the resolved chart path.
5. Use the fallback only when exactly one slot matches; otherwise return an explicit ambiguity failure.

Do not recreate SET/chart ordering rules and do not require `ChartId` uniqueness by itself.

Keep this helper inside the game assembly. Do not expose library traversal through Game API.

### 1.3 Add one atomic `SongListDisplay` selection seam

Add a small method such as:

```text
SetSelection(int index, int difficulty)
```

It must:

- validate the current list/index;
- apply row and difficulty together;
- reset scroll target/counters coherently;
- run the existing selection update/event path once.

Do not add arbitrary list mutation or remote-navigation methods.

Add focused `SongListDisplay` coverage if the current component tests do not prove the one-event behavior.

### 1.4 Project through normal Song Select browse state

In `PrepareVideoChart`:

- switch to All Songs and default/no-filter projection for the recorder operation;
- return to the root browse list and clear the navigation stack;
- reuse existing BOX navigation helpers for the resolved ancestor chain;
- set `_isProjectingPreparedSelection = true` in a `try/finally` around the programmatic selection;
- select the target row+difficulty through the new atomic component method;
- while the flag is true, let status/history/image/breadcrumb presentation update but suppress prepared-state invalidation and automatic preview loading;
- always clear the flag in `finally`.

**Task 1 acceptance:** exact root/nested/SET/duplicate-title/single-file-multi-instrument selection is deterministic by path, and normal Song Select UI state points at the requested DRUMS gameplay slot.

---

## Task 2 — Add prepared preview lifecycle, navigation invalidation, activation, and telemetry

**Goal:** load the exact chart's preview stopped, play it only on command, measure actual playback time, exit prepared mode cleanly on user navigation, and activate only when the normal transition can really start.

### 2.1 Write failing lifecycle and activation tests

Add cases for:

- prepare loads the requested `SongChart.PreviewFile`, not the node's primary chart preview;
- missing preview declaration;
- missing preview file;
- resource-load/unsupported failure;
- successful prepare creates no sound instance and does not auto-play after the normal preview delay;
- prepare projection does not clear itself via selection/difficulty events;
- first start creates one looped instance at existing preview volume;
- repeated start while playing creates no second instance;
- elapsed ms advances only when `ISoundInstance.State == Playing`;
- playback creation/start failure reports failed state with no BGM/full-song fallback;
- cancel is idempotent;
- replacement preparation stops/disposes the old instance exactly once;
- row navigation away clears preparation and resumes the row's normal interactive preview path;
- difficulty navigation away clears preparation and resumes the normal primary-chart delayed preview;
- Deactivate clears prepared state/resources exactly once;
- activation while `CanPerformStageTransition()` is false returns failure, starts no transition, and preserves prepared state/preview;
- activation after the debounce gate is available clears preview and uses the existing `selectedSong` / `selectedDifficulty` / `songId` transition data path;
- no prepared state leaves existing interactive preview behavior unchanged.

Reuse existing `ISound` / `ISoundInstance` mocks and Song Select activation tests.

### 2.2 Add minimal stage-owned prepared state

In `SongSelectionStage.cs`, add only:

- resolved prepared selection (node/chart/difficulty/telemetry identity);
- preview state (`None`, `Prepared`, `Playing`, `Failed`);
- elapsed milliseconds;
- `_isProjectingPreparedSelection` for the synchronous prepare projection.

Do not add locks, session IDs, or a generation/state-machine framework; mutations and elapsed updates are update-thread-owned.

Provide one idempotent prepared cleanup path so cancel, replacement, navigation, activation, and Deactivate share resource release behavior.

### 2.3 Load the exact resolved chart preview

Reuse current preview resource methods, factoring only enough path/loading logic so prepared loading can accept the resolved `SongChart` directly.

Requirements:

- resolve preview relative to the matched chart's `FilePath` directory;
- use the existing resource manager/sound type support;
- keep the loaded `_previewSound` but leave `_previewSoundInstance` null;
- disable normal delayed auto-start while prepared;
- state = `Prepared`, elapsed = 0 only after load succeeds;
- any prepare failure leaves no active prepared state.

Do not change normal interactive `LoadPreviewSound(SongListNode)` semantics when no preparation exists.

### 2.4 Implement explicit start/cancel and elapsed tracking

`StartPreparedPreview`:

- require a prepared resource;
- create the instance with the existing helper;
- apply existing preview volume and looping;
- call `Play()` once;
- start the existing BGM fade-out;
- become `Playing`, elapsed = 0;
- repeated call while already playing is idempotent success.

`CancelPreparedChart`:

- reuse existing stop/dispose/release behavior;
- clear prepared state/identity/elapsed;
- remain idempotent.

During `OnUpdate`, add elapsed time only when prepared state is Playing and the sound instance reports Playing. If an explicitly-started loop unexpectedly stops, move to Failed and do not auto-restart or substitute another source.

### 2.5 Clear preparation on real user navigation, not prepare projection

Wire both selection paths.

`OnSongSelectionChanged`:

- if `_isProjectingPreparedSelection` is true, do not clear preparation and skip automatic preview loading for that projection;
- otherwise, if a prepared chart exists and the selected row moved away, clear it;
- continue the handler's existing row-selection preview behavior, so normal delayed `LoadPreviewSound(e.SelectedSong)` resumes.

`OnDifficultyChanged`:

- if `_isProjectingPreparedSelection` is true, do not clear preparation;
- otherwise, if a prepared chart exists and `e.NewDifficulty` differs from the prepared slot, clear it;
- after that prepared-mode exit, call the existing `LoadPreviewSound(e.Song)` so interactive Song Select resumes its normal primary-chart delayed preview;
- when no preparation exists, leave today's difficulty-change behavior untouched.

The cleanup path must remain idempotent so existing `StopCurrentPreview` calls cannot double-dispose an instance.

### 2.6 Make activation return a real transition outcome

Current `SelectSong` silently returns when the global debounce rejects the transition. Factor its logic into a small eligibility gate and one shared transition-start body; names may follow existing style, for example:

```text
CanStartSongSelection(node)
StartSongSelection(node)
```

Eligibility must reject:

- null/non-Score node;
- unavailable `StageManager`;
- `_game.CanPerformStageTransition() == false`.

The shared start body remains the sole owner of:

```text
_game.MarkStageTransition()
selectedSong
selectedDifficulty
songId
StageManager.ChangeStage(StageType.SongTransition, ...)
```

Normal interactive `SelectSong` uses the same gate/start helpers.

`ActivatePreparedChart` must:

1. require the prepared row/difficulty still equal `_selectedSong` / `_currentDifficulty`;
2. evaluate the shared eligibility gate;
3. when blocked, return `success=false` with a clear category such as transition debounce active and leave prepared state/preview intact;
4. only after the gate succeeds, stop/dispose the prepared preview and clear prepared state;
5. immediately execute the shared transition-start body on the same update-thread action;
6. return success only after `ChangeStage` was invoked.

Do not wait inside the game for the 0.5-second debounce and do not duplicate transition shared-data construction.

### 2.7 Add three producer telemetry fields

In `GameTelemetrySnapshot.cs` and `SongSelectionStage.PopulateTelemetry`, add only:

- `PreparedChartIdentity`;
- `PreparedPreviewState`;
- `PreparedPreviewElapsedMs`.

Identity:

- `chart:<database-id>` when a non-zero chart ID exists;
- otherwise a root-relative normalized path with no absolute root prefix.

The serialized wire names must be exactly:

```text
preparedChartIdentity
preparedPreviewState
preparedPreviewElapsedMs
```

**Task 2 acceptance:** recorder can prepare a stopped exact-chart preview, start exactly one loop, wait on elapsed telemetry, retry a debounce-blocked activation without losing preparation, activate through the normal SongTransition path, and all navigation/exit paths clean up correctly.

---

## Task 3 — Wire the explicit Game API, JSON-RPC, Automation client, and telemetry contract

**Goal:** expose the stage behavior to HPA-503 without exposing generic mutation/transport APIs, and lock producer/consumer wire names.

### 3.1 Add Game API contract and queue/await helper

In `GameApi.cs`, add a narrow prepared-command result and four methods equivalent to:

```text
PrepareVideoChartAsync(chartPath)
StartPreparedPreviewAsync()
ActivatePreparedChartAsync()
CancelPreparedChartAsync()
```

In `GameApiImplementation.cs`, implement one private queue helper using the existing `IGameContext.QueueMainThreadAction` plus:

```text
TaskCompletionSource(..., TaskCreationOptions.RunContinuationsAsynchronously)
```

The queued action must perform the current-stage check and execute the Song Select command on the update thread. The returned task completes with the actual command result, not merely after enqueue.

Add tests that capture/execute the queued action and verify:

- no stage mutation occurs before the queued action runs;
- the task completes only after execution;
- non-SongSelect current stage produces a clear failure;
- stage failure text, including debounce-blocked activation, propagates.

Do not copy `ChangeStageAsync`'s fire-and-forget contract and do not add a reusable dispatcher class.

### 3.2 Add four explicit JSON-RPC handlers

In `JsonRpcServer.cs`:

- add explicit route cases only for the four method names;
- validate `prepareVideoChart` has object params and a non-empty string `chartPath`;
- call the corresponding Game API method;
- return a small `{ success, error? }` result;
- rely on existing server-level API-key authentication.

Extend current JSON-RPC tests for method routing, invalid prepare params, normal failures, and debounce failure shape.

No generic `executeGameAction` endpoint.

### 3.3 Extend `DTXMania.Automation`

In `JsonRpcGameClient.cs`, add the four public async wrappers using existing private `SendAsync`.

Add one small command-result parser shared by the four methods:

- `success=true` returns normally;
- missing/false success throws `InvalidOperationException` with server-provided safe error text.

In `GameStateSnapshot.cs`, add accessors that read the exact keys:

```text
preparedChartIdentity
preparedPreviewState
preparedPreviewElapsedMs
```

Extend `JsonRpcGameClientTests` to assert exact method names/params and error propagation.

### 3.4 Mandatory game-to-Automation camelCase contract test

Extend:

```text
DTXMania.E2E/AutomationContractTests.cs
GameTelemetrySnapshot_CamelCaseRoundTrip_ShouldExposeAllConsumedFields
```

The producer `GameTelemetrySnapshot` fixture must set non-default values for all three fields. Serialize it with the existing camelCase serializer and assert the deserialized `GameStateSnapshot` returns those same values through its new accessors.

This is mandatory. Do not replace it with an Automation-only JSON fixture because the purpose is to catch producer property-name drift as well as consumer accessor drift.

**Task 3 acceptance:** HPA-503 can prepare/start/activate/cancel using `DTXMania.Automation` only and poll all three prepared telemetry values through the existing `GetGameStateAsync` path with the producer/consumer wire contract locked.

---

## Task 4 — Regression and acceptance validation

### 4.1 Focused tests

Run the new/focused suites first. Use the repository's host-appropriate game test project, for example:

```bash
# Song Select / Game API / JSON-RPC focused tests
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~PreparedChart"

# Reusable Automation tests
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj

# Existing producer/consumer contract
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~AutomationContractTests"
```

On macOS, use `DTXMania.Test/DTXMania.Test.Mac.csproj` for the game unit suite. Run the E2E-support contract on its supported host configuration; no live gameplay launch is required for this contract test.

### 4.2 Full regression suite

Run the full platform-appropriate game unit suite plus Automation tests.

At minimum verify:

- existing Song Select preview/BGM tests stay green;
- existing Song Select navigation/filter/BOX tests stay green;
- existing SongTransition tests stay green;
- Game API/JSON-RPC tests stay green;
- Automation tests stay green;
- `AutomationContractTests` stays green with the three new telemetry fields.

HPA-510 does **not** need to add OBS/recorder E2E. HPA-503 is the live Windows recording validation consumer.

### 4.3 Manual/API smoke for implementation PR

Use an indexed test chart with preview audio and the existing Game API:

1. enter Song Select;
2. call prepare with the exact absolute chart path;
3. verify telemetry reports `Prepared` and a redacted identity;
4. take a screenshot and verify the requested row/difficulty/status UI is rendered;
5. wait long enough to prove normal preview delay does not auto-play;
6. call start, then poll until prepared elapsed >= 10,000 ms;
7. call activate and confirm transition follows SongTransition;
8. separately exercise a debounce-blocked activation and verify preparation remains available for retry;
9. exercise user difficulty navigation after prepare and verify prepared state clears and the normal primary-chart delayed preview resumes;
10. repeat with cancel/reprepare to confirm no preview audio bleeds across attempts.

This smoke is implementation validation only; do not create a permanent recorder harness in HPA-510.

---

## Scope Guardrails for the Implementing Agent

Stop and simplify if the implementation starts adding any of the following:

- recording session IDs/state machines;
- generic Game API mutation dispatch;
- a new song repository/query service;
- title-based or input-driven navigation;
- alternate chart/difficulty ordering rules instead of current Song Select helpers plus the explicit DRUMS tie-break;
- a second preview audio abstraction;
- a game-owned debounce wait/retry loop;
- OBS/process/FFmpeg concerns;
- render-ready generation counters.

The desired end state is four explicit commands layered on the existing Song Select state machine, with `DTXMania.Automation` wrappers ready for HPA-503.

## Definition of Done

- Exact absolute chart path selects the correct indexed row+difficulty for root, nested, SET/multi-chart, duplicate-title, and ordinary shared-ChartId multi-instrument cases.
- Shared ChartId across DRUMS/GUITAR/BASS resolves to the unique DRUMS gameplay slot rather than failing as ambiguous.
- Outside-root/unindexed/truly-ambiguous/missing-preview/unsupported-preview cases fail clearly.
- Prepare leaves the exact chart preview loaded but stopped and cannot clear itself through projection events.
- Start creates exactly one looped instance using existing preview volume/path behavior.
- Prepared elapsed telemetry advances only while the instance is actually Playing.
- User row/difficulty navigation clears preparation; difficulty exit restores the normal primary-chart delayed preview.
- Debounce-blocked activation returns failure and preserves preparation; accepted activation clears audio/state and starts the existing SongTransition path.
- Cancel/replacement/navigation/deactivation/activation clean up without audio bleed or double disposal.
- Telemetry exposes only prepared identity/state/elapsed with exact camelCase wire keys and no absolute path.
- `AutomationContractTests` proves those producer wire keys reach the Automation accessors.
- JSON-RPC commands are explicit and use existing API-key authentication.
- `DTXMania.Automation` exposes four typed wrappers; generic JSON-RPC remains private.
- Existing interactive Song Select behavior is unchanged when no prepared chart is active.
- Platform-appropriate game unit suite, Automation tests, and the existing Automation producer/consumer contract pass.