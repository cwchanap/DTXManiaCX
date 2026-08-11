# HPA-510 Prepared Chart Recording Commands Implementation Plan

**Issue:** [HPA-510](https://linear.app/cwchanap/issue/HPA-510/add-minimal-prepared-chart-recording-commands-in-song-select)  
**Design:** `docs/superpowers/specs/2026-08-11-hpa-510-prepared-chart-recording-design.md`  
**Target size:** one implementation PR, 2–3 engineer days

## Objective

Add the smallest production API HPA-503 needs to prepare one exact indexed chart in Song Select, explicitly start its declared preview, activate it through the normal player path, and cancel/clean up safely.

Keep the implementation local to existing Song Select, Game API/JSON-RPC, telemetry, and Automation seams. Do not introduce a general automation/session framework.

## File Map

### Game / Song Select

- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
  - exact active-library chart resolution;
  - prepared selection/preview state;
  - programmatic browse projection;
  - prepare/start/activate/cancel commands;
  - elapsed preview telemetry;
  - cleanup on navigation/deactivation.
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
  - accessors for prepared identity/state/elapsed time.

### Tests

Prefer one focused new Song Select fixture rather than spreading preparation cases across unrelated files:

- `DTXMania.Test/Stage/SongSelectionStagePreparedChartTests.cs` — new.
- `DTXMania.Test/GameApi/GameApiImplementationTests.cs` — extend.
- `DTXMania.Test/JsonRpc/JsonRpcServerInternalTests.cs` and/or existing JSON-RPC integration tests — extend only where current seams fit.
- `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs` — extend.
- `DTXMania.E2E/Telemetry/E2EGameStateTests.cs` or the current producer/consumer telemetry contract test — extend only if needed to keep game/Automation field names aligned.

Do not add a new E2E harness or recorder test project in this ticket.

---

## Task 1 — Resolve and project an exact indexed chart

**Goal:** given one absolute chart path, identify exactly one active Song Select row + difficulty and show it through the normal UI without synthetic input.

### 1.1 Write failing focused tests first

In `SongSelectionStagePreparedChartTests.cs`, construct minimal published-library hierarchies covering:

- root-level chart;
- chart below one or more BOX nodes;
- multi-chart/SET-style row where the requested chart is not the node's primary chart;
- duplicate song titles with different file paths;
- path outside active roots;
- path under an active root but absent from the indexed snapshot;
- no unique difficulty mapping.

Assertions should verify the resolved node and difficulty come from path identity only. No title matching is allowed.

### 1.2 Add a stage-private resolver

In `SongSelectionStage.cs`:

- require a fully-qualified non-blank path;
- use the currently applied `SongLibrarySnapshot` as the authoritative library view;
- normalize with `SongPathIdentity`;
- require containment under `snapshot.ActiveRoots`;
- recursively traverse root/BOX nodes and inspect both `DatabaseChart` and `DatabaseSong.Charts`;
- require one exact normalized chart match;
- resolve the visible difficulty slot by `SongScore.ChartId` first;
- for legacy/SET fallback, scan valid score slots and reuse `SongChartHelper.GetCurrentDifficultyChart` rather than recreating chart-order rules;
- retain the ancestor BOX chain and matched `SongChart` in the resolution result.

Keep this helper inside the game assembly. Do not expose library traversal through Game API.

### 1.3 Add one atomic `SongListDisplay` selection seam

Add a small method such as `SetSelection(int index, int difficulty)` that:

- validates the current list/index;
- applies row and difficulty together;
- resets scroll target/counters coherently;
- calls the existing selection update/event path once.

Do not add arbitrary list mutation or remote-navigation methods.

Add focused component/unit coverage if existing `SongListDisplay` tests do not already cover the required event behavior.

### 1.4 Project through normal Song Select browse state

In the prepare path:

- switch to All Songs and default/no-filter projection for the recorder operation;
- clear/rebuild normal navigation state;
- reuse existing BOX navigation helpers for the resolved ancestor chain;
- select the target row+difficulty through the new atomic component method;
- suppress only automatic preview loading during this synchronous programmatic projection so status/history/image/breadcrumb still update without briefly playing/loading the wrong row's audio.

**Task 1 acceptance:** exact root/nested/SET/duplicate-title selection is deterministic by path, and the normal Song Select UI state points at the requested row/difficulty.

---

## Task 2 — Add prepared preview lifecycle, activation, and telemetry

**Goal:** load the exact chart's preview stopped, play it only on command, measure only actual playback time, and cleanly reuse existing activation.

### 2.1 Write failing preview lifecycle tests

Add cases for:

- prepare loads the requested `SongChart.PreviewFile`, not the node's primary chart preview;
- missing preview declaration;
- missing preview file;
- resource-load/unsupported failure;
- successful prepare creates no sound instance and does not auto-play after the normal preview delay;
- first start creates one looped instance at existing preview volume;
- repeated start while playing creates no second instance;
- elapsed ms advances only when `ISoundInstance.State == Playing`;
- playback creation/start failure reports failed state with no BGM/full-song fallback;
- cancel is idempotent;
- replacement preparation stops/disposes the old instance once;
- normal row/difficulty navigation away clears prepared state;
- Deactivate clears prepared state and resources;
- no prepared state leaves existing interactive preview behavior unchanged.

Reuse existing `ISound` / `ISoundInstance` mocks and preview tests.

### 2.2 Add minimal stage-owned prepared state

In `SongSelectionStage.cs`, add only:

- resolved prepared selection (node/chart/difficulty/telemetry identity);
- preview state (`None`, `Prepared`, `Playing`, `Failed`);
- elapsed milliseconds;
- a temporary synchronous flag used only to suppress automatic preview loading while prepare projects the selection.

Do not add locks or a session/generation model; mutations and elapsed updates are update-thread-owned.

### 2.3 Load the exact resolved chart preview

Reuse current preview resource methods, but factor enough path/loading logic so prepared loading can accept the resolved `SongChart` directly.

Requirements:

- resolve preview relative to the matched chart's `FilePath` directory;
- use the existing resource manager/sound type support;
- keep the loaded `_previewSound` but leave `_previewSoundInstance` null;
- disable normal delayed auto-start while prepared;
- state = `Prepared`, elapsed = 0 only after load succeeds;
- any prepare failure leaves no active prepared state.

Do not change normal interactive `LoadPreviewSound(SongListNode)` semantics outside prepared mode.

### 2.4 Implement explicit start/cancel

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

During `OnUpdate`, add elapsed time only when prepared state is Playing and the sound instance actually reports Playing. If an explicitly-started loop unexpectedly stops, move to Failed and do not auto-restart or substitute another source.

### 2.5 Activate through the existing player path

`ActivatePreparedChart` must:

- require the prepared row/difficulty still be the selected row/difficulty;
- capture node/difficulty;
- stop and clear prepared preview before transition;
- call existing `SelectSong(node)`.

Do not reconstruct transition shared data or call Performance directly.

Extend existing Song Select activation tests to assert the normal selected-song/difficulty/song-id transition contract remains the one used.

### 2.6 Add three telemetry fields

In `GameTelemetrySnapshot.cs` and `SongSelectionStage.PopulateTelemetry` add only:

- `PreparedChartIdentity`;
- `PreparedPreviewState`;
- `PreparedPreviewElapsedMs`.

Identity:

- `chart:<database-id>` when a non-zero chart ID exists;
- otherwise a root-relative normalized path with no absolute root prefix.

**Task 2 acceptance:** recorder can prepare a stopped preview, start exactly one loop, wait on elapsed telemetry, activate through normal SongTransition, and all exit/replacement paths clean up.

---

## Task 3 — Wire the explicit Game API, JSON-RPC, and Automation client

**Goal:** expose the stage behavior to HPA-503 without exposing generic mutation or transport APIs.

### 3.1 Add Game API contract and queue/await helper

In `GameApi.cs`, add a narrow prepared-command result and four methods equivalent to:

```text
PrepareVideoChartAsync(chartPath)
StartPreparedPreviewAsync()
ActivatePreparedChartAsync()
CancelPreparedChartAsync()
```

In `GameApiImplementation.cs`, implement one private queue helper using the existing `IGameContext.QueueMainThreadAction` plus `TaskCompletionSource` with asynchronous continuations.

The queued action must perform the current-stage check and execute the Song Select command on the update thread. The returned task completes with the actual command result, not merely after enqueue.

Add tests that capture/execute the queued action and verify:

- no stage mutation occurs before the queued action runs;
- the task completes after execution;
- non-SongSelect current stage produces a clear failure;
- stage failure text propagates.

Do not add a reusable dispatcher class.

### 3.2 Add four explicit JSON-RPC handlers

In `JsonRpcServer.cs`:

- add explicit route cases only for the four method names;
- validate `prepareVideoChart` has object params and a non-empty string `chartPath`;
- call the corresponding Game API method;
- return a small `{ success, error? }` result;
- rely on the existing server-level API key authentication.

Extend current JSON-RPC tests for method routing, invalid prepare params, and command failure shape.

No generic `executeGameAction` endpoint.

### 3.3 Extend `DTXMania.Automation`

In `JsonRpcGameClient.cs`, add the four public async wrappers using existing private `SendAsync`.

Add one small command-result parser shared by the four methods:

- success=true returns normally;
- missing/false success throws `InvalidOperationException` with server-provided safe error text.

In `GameStateSnapshot.cs`, add accessors for the three prepared telemetry fields.

Extend `JsonRpcGameClientTests` to assert exact method names/params and error propagation. Extend the existing producer/consumer telemetry contract test if one is required to keep camel-case field naming locked between game and Automation.

**Task 3 acceptance:** HPA-503 can prepare/start/activate/cancel using `DTXMania.Automation` only, and can poll prepared telemetry using the existing `GetGameStateAsync` path.

---

## Task 4 — Regression and acceptance validation

### 4.1 Focused tests

Run the new/focused suites first. Use the repository's current project appropriate to the host, for example:

```bash
# Song Select / Game API / JSON-RPC focused tests
# Use the relevant test project for the current OS.
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~PreparedChart"

# Reusable Automation contract
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

On macOS, use `DTXMania.Test/DTXMania.Test.Mac.csproj` for the game test suite.

### 4.2 Full regression suite

Run the full platform-appropriate game unit suite plus Automation tests.

At minimum verify:

- existing Song Select preview/BGM tests stay green;
- existing Song Select navigation/filter/BOX tests stay green;
- existing SongTransition tests stay green;
- Game API/JSON-RPC tests stay green;
- Automation tests stay green.

HPA-510 does **not** need to add OBS/recorder E2E. HPA-503 is the live Windows recording validation consumer.

### 4.3 Manual/API smoke for implementation PR

Use an indexed test chart with preview audio and the existing Game API:

1. enter Song Select;
2. call prepare with the exact absolute chart path;
3. verify telemetry reports Prepared and a redacted identity;
4. take a screenshot and verify the requested row/difficulty/status UI is rendered;
5. wait long enough to prove normal preview delay does not auto-play;
6. call start, then poll until prepared elapsed >= 10,000 ms;
7. call activate and confirm transition follows SongTransition;
8. repeat with cancel/reprepare to confirm no preview audio bleeds across attempts.

This smoke is implementation validation only; do not create a permanent recorder harness in HPA-510.

---

## Scope Guardrails for the Implementing Agent

Stop and simplify if the implementation starts adding any of the following:

- recording session IDs/state machines;
- generic Game API mutation dispatch;
- a new song repository/query service;
- title-based or input-driven navigation;
- alternate chart/difficulty ordering rules instead of current Song Select helpers;
- a second preview audio abstraction;
- OBS/process/FFmpeg concerns;
- render-ready generation counters.

The desired end state is four explicit commands layered on the existing Song Select state machine, with `DTXMania.Automation` wrappers ready for HPA-503.

## Definition of Done

- Exact absolute chart path selects the correct indexed row+difficulty for root, nested, SET/multi-chart, and duplicate-title cases.
- Outside-root/unindexed/ambiguous/missing-preview/unsupported-preview cases fail clearly.
- Prepare leaves the exact chart preview loaded but stopped.
- Start creates exactly one looped instance using existing preview volume/path behavior.
- Prepared elapsed telemetry advances only while the instance is actually Playing.
- Cancel/replacement/navigation/deactivation/activation clean up without audio bleed or double disposal.
- Activation reuses `SelectSong -> SongTransitionStage` and does not jump directly to Performance.
- Telemetry exposes only prepared identity/state/elapsed, with no absolute path.
- JSON-RPC commands are explicit and use existing API-key authentication.
- `DTXMania.Automation` exposes four typed wrappers; generic JSON-RPC remains private.
- Existing interactive Song Select behavior is unchanged when no prepared chart is active.
- Platform-appropriate game unit suite and Automation tests pass.
