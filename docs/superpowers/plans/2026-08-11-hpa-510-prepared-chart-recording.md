# HPA-510 Prepared Chart Recording Commands Implementation Plan

> **For agentic workers:** implement task-by-task and keep the four-command stage-owned scope. Do not introduce a generic automation/session framework.

**Issue:** [HPA-510](https://linear.app/cwchanap/issue/HPA-510/add-minimal-prepared-chart-recording-commands-in-song-select)  
**Design:** `docs/superpowers/specs/2026-08-11-hpa-510-prepared-chart-recording-design.md`  
**Target size:** one implementation PR, 2–3 engineer days

**Goal:** Let HPA-503 prepare one exact indexed chart in Song Select, explicitly play its preview for CX-reported time, and activate it through the normal SongTransition path.

**Architecture:** Keep all prepared state in `SongSelectionStage`. Resolve from the applied library snapshot, rebuild the browse projection with existing navigation reconciliation helpers, expose four awaited Game API/JSON-RPC commands, and reuse HPA-501 Automation transport.

**Tech stack:** .NET 8, MonoGame, xUnit, existing Game API/JSON-RPC, `DTXMania.Automation`, existing Windows E2E harness.

## Global constraints

- Exactly four commands: prepare, start preview, activate, cancel.
- All stage mutations run on the game update thread.
- Resolve by chart path, never title.
- No DB query/repository layer.
- No session ID/state-machine abstraction.
- No generic mutation/dispatch endpoint.
- Screenshot completion remains the render barrier.
- Keep all three accepted telemetry values: prepared identity, state, elapsed ms.
- Do not expose the absolute chart path in telemetry.
- Existing interactive Song Select preview behavior must remain unchanged outside prepared mode.

---

## File map

### Game / Song Select

- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
  - whole-tree exact chart resolver;
  - stage-owned prepared state;
  - side-effect-controlled browse projection;
  - exact-chart preview load/start/cancel;
  - activation eligibility/start split;
  - navigation cleanup and telemetry.
- `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`
  - atomic `SetSelection(index, difficulty)` seam.
- `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`
  - three prepared-chart telemetry fields.

### Game API / transport

- `DTXMania.Game/Lib/GameApi.cs`
  - narrow command result and four methods.
- `DTXMania.Game/Lib/GameApiImplementation.cs`
  - queue-and-await helper using existing main-thread queue.
- `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs`
  - four explicit authenticated routes.

### Automation

- `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs`
  - four public wrappers over private `SendAsync`.
- `DTXMania.Automation/Telemetry/GameStateSnapshot.cs`
  - three prepared telemetry accessors.

### Tests

- Create `DTXMania.Test/Stage/SongSelectionStagePreparedChartTests.cs`.
- Extend `DTXMania.Test/GameApi/GameApiImplementationTests.cs`.
- Extend existing JSON-RPC tests where the current seams fit.
- Extend `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs`.
- Extend `DTXMania.E2E/AutomationContractTests.cs`.
- Modify `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs` to declare its generated WAV as `#PREVIEW`.
- Create `DTXMania.E2E/PreparedChartRecordingSmokeTests.cs` using the existing `Category=E2E` harness.

Do not create a recorder test project, fake OBS server, or second E2E harness.

---

## Task 1 — Exact resolver and side-effect-free Song Select projection

**Goal:** resolve an exact active-library chart to one row/difficulty and project it once through normal UI state.

### 1.1 Add focused failing resolver tests

Cover:

- root-level chart;
- nested BOX chart;
- SET/multi-chart row where requested chart is not `DatabaseChart`;
- duplicate titles at different paths;
- outside-root path;
- active-root but unindexed path;
- ordinary single-file multi-instrument row where several score slots share one `ChartId`;
- unresolved ambiguity.

The test fixture must assert path identity only; title must never select the result.

### 1.2 Add a stage-private whole-tree resolver

In `SongSelectionStage.cs`:

1. require a fully-qualified non-blank path;
2. use `_appliedLibrarySnapshot` as the authoritative view;
3. normalize with `SongPathIdentity`;
4. require containment under `snapshot.ActiveRoots`;
5. recursively traverse root/BOX nodes;
6. inspect both `DatabaseChart` and `DatabaseSong.Charts`;
7. return exactly one `(node, chart, difficultyIndex, ancestorBoxPaths)`.

Difficulty precedence:

```text
matching non-zero ChartId
-> if one candidate: use it
-> if several: unique DRUMS candidate
-> otherwise GetCurrentDifficultyChart(slot) + exact path fallback
-> fail if still ambiguous
```

Do not add a new chart-ordering algorithm or database query.

### 1.3 Add atomic `SongListDisplay.SetSelection`

Add one internal/public-to-game-assembly method equivalent to:

```text
SetSelection(index, difficulty)
```

Requirements:

- validate current list/index;
- clamp/validate difficulty using existing semantics;
- apply row + difficulty as one operation;
- reset scroll target/counters coherently;
- invoke the normal selection update/event path exactly once.

Add focused component coverage for one event and final selected row/difficulty.

### 1.4 Project with existing reconciliation machinery

Do **not** call `NavigateIntoBox` for each ancestor.

Wrap the **entire** prepare projection in `_isProjectingPreparedSelection = true` / `finally false`:

1. switch to All Songs/default recorder browse projection;
2. clear active filter projection for this operation;
3. call `RestoreNavigationPaths(ancestorPaths, snapshot)`;
4. call `RefreshSongListForActiveTab()` once;
5. find target index in the projected list;
6. call `SetSelection(targetIndex, difficultyIndex)` once;
7. update normal breadcrumb/status hints through existing stage paths.

The suppression flag must prevent prepared invalidation and normal automatic preview loading during every projection side effect, including the one final refresh.

**Task 1 acceptance:** nested BOX preparation causes one final list projection/selection, not one stop/load/BGM cycle per ancestor, and the intended row/difficulty is visible.

---

## Task 2 — Prepared preview lifecycle, navigation invalidation, activation, and telemetry

**Goal:** load the exact preview stopped, play exactly one loop, measure actual CX playback, and activate only when the real transition can start.

### 2.1 Add failing lifecycle tests

Cover:

- prepared load uses resolved `SongChart.PreviewFile`, not primary `DatabaseChart`;
- missing declaration/file/load failure;
- successful prepare creates no `ISoundInstance`;
- successful prepared load leaves `_isPreviewDelayActive == false`;
- waiting beyond the normal preview delay does not auto-start;
- first start creates one looped instance at existing preview volume;
- repeated start creates no second instance;
- elapsed advances only while `State == Playing`;
- unexpected stop moves state to Failed;
- replacement/cancel/deactivate clean up exactly once;
- row navigation away clears prepared state;
- difficulty navigation away clears prepared state and resumes normal delayed primary-chart preview;
- `_isProjectingPreparedSelection` prevents prepare from clearing itself;
- no prepared state preserves today's interactive preview behavior.

Reuse existing `ISound` / `ISoundInstance` mocks.

### 2.2 Add minimal stage-owned prepared state

Add only:

```text
PreparedChartSelection(node, chart, difficultyIndex, telemetryIdentity)
PreparedPreviewState
PreparedPreviewElapsedMs
_isProjectingPreparedSelection
```

Use one idempotent prepared cleanup path shared by cancel, replacement, navigation, activation, and Deactivate.

No locks/session/generation framework.

### 2.3 Load the exact preview and explicitly defeat the normal delay timer

Factor only enough path logic to accept a resolved `SongChart` and reuse `TryLoadPreviewSoundFile`.

After `TryLoadPreviewSoundFile` succeeds, explicitly set:

```text
_previewPlayDelay = 0
_isPreviewDelayActive = false
```

This is mandatory because the existing helper sets `_isPreviewDelayActive = true` on every successful load.

Only then publish prepared state:

```text
_previewSound retained
_previewSoundInstance == null
state = Prepared
elapsed = 0
```

Any failure leaves no active preparation.

### 2.4 Implement start/cancel and elapsed update

`StartPreparedPreview`:

- require prepared resource;
- create one instance with existing helper;
- apply existing volume + looping;
- `Play()` once;
- start existing BGM fade-out;
- state `Playing`, elapsed `0`;
- repeated call while already playing is idempotent success.

`CancelPreparedChart`:

- reuse existing stop/dispose/release path;
- clear identity/state/elapsed;
- idempotent success.

In `OnUpdate`, add elapsed time only while state is Playing and the instance reports Playing. If it stops unexpectedly, mark Failed and do not substitute another audio source.

### 2.5 Wire row/difficulty invalidation

`OnSongSelectionChanged`:

- if projecting prepare, skip prepared invalidation and normal auto-preview load;
- otherwise clear preparation when moving to another row, then continue existing normal selection behavior.

`OnDifficultyChanged`:

- if projecting prepare, do not invalidate;
- otherwise clear when moving away from prepared difficulty;
- on that prepared-mode exit only, call existing `LoadPreviewSound(e.Song)` to resume normal delayed primary-chart preview;
- when no prepared chart exists, keep current difficulty behavior unchanged.

### 2.6 Split transition eligibility from transition start

Refactor the existing ~20-line `SelectSong` body into a small shared gate + shared start body.

Eligibility rejects:

- null/non-Score node;
- missing `StageManager`;
- `_game.CanPerformStageTransition() == false`.

The start body remains the only code that performs:

```text
_game.MarkStageTransition()
selectedSong / selectedDifficulty / songId shared data
StageManager.ChangeStage(SongTransition, ...)
```

Normal player `SelectSong` uses the same helpers.

`ActivatePreparedChart`:

1. require prepared row/difficulty still matches current selection;
2. run eligibility;
3. if blocked, return failure and preserve preparation/preview;
4. only after eligibility succeeds, clean prepared preview/state;
5. immediately call shared transition-start body;
6. return success only after the stage change was invoked.

Do not sleep/retry inside the game.

### 2.7 Add all three producer telemetry fields

Add:

```text
PreparedChartIdentity
PreparedPreviewState
PreparedPreviewElapsedMs
```

Identity policy:

- `chart:<SongChart.Id>` when ID is non-zero;
- otherwise normalized path relative to the matching active root.

Do not use `GetStableSelectionIdentity`; it identifies the row and commonly returns `song:<id>`, which is not sufficient for an exact chart/difficulty preparation.

Wire names must serialize exactly as:

```text
preparedChartIdentity
preparedPreviewState
preparedPreviewElapsedMs
```

**Task 2 acceptance:** prepared preview stays stopped until commanded, reports 10+ seconds of actual playing time, retries are possible after a blocked activation without losing state, and successful activation uses the normal SongTransition construction.

---

## Task 3 — Explicit Game API, JSON-RPC, Automation wrappers, and telemetry contract

**Goal:** expose only the four required operations and lock their game/consumer contract.

### 3.1 Add Game API contract + queue-and-await helper

In `GameApi.cs` add:

```text
PreparedChartCommandResult
- bool Success
- string? Error

PrepareVideoChartAsync(chartPath)
StartPreparedPreviewAsync()
ActivatePreparedChartAsync()
CancelPreparedChartAsync()
```

Do **not** add an error-code enum in this ticket. HPA-503 has no programmatic recovery branch: all command failures fail the recording run, and activation occurs after 10 seconds of preview, well beyond the 0.5-second transition debounce. Add structured codes only when a real caller needs branching.

In `GameApiImplementation.cs`, add one private helper using:

```text
IGameContext.QueueMainThreadAction
TaskCompletionSource(...RunContinuationsAsynchronously)
```

The queued action checks current stage, executes the Song Select command, and completes with the real result.

Tests must prove:

- no mutation before queued action execution;
- task not completed before action execution;
- wrong stage returns failure;
- stage failure text propagates;
- debounce-blocked activation returns failure rather than queued-success.

### 3.2 Add four authenticated JSON-RPC routes

Add only:

```text
prepareVideoChart { chartPath }
startPreparedPreview
activatePreparedChart
cancelPreparedChart
```

Validate prepare params. Return `{ success, error? }` using the existing server authentication and JSON-RPC envelope.

No `executeGameAction`/generic dispatcher and no new error taxonomy.

### 3.3 Extend Automation wrappers

In `JsonRpcGameClient` add:

```text
PrepareVideoChartAsync(path)
StartPreparedPreviewAsync()
ActivatePreparedChartAsync()
CancelPreparedChartAsync()
```

Use private `SendAsync` and one small command-result parser. `success=false` throws `InvalidOperationException` with the server's safe error text.

In `GameStateSnapshot` add accessors for exactly:

```text
preparedChartIdentity
preparedPreviewState
preparedPreviewElapsedMs
```

Extend client tests for exact method names/params and error propagation.

### 3.4 Mandatory producer/consumer camelCase contract

Extend:

```text
DTXMania.E2E/AutomationContractTests.cs
GameTelemetrySnapshot_CamelCaseRoundTrip_ShouldExposeAllConsumedFields
```

Set non-default values for the three game producer fields, serialize through the existing camelCase options, deserialize to `GameStateSnapshot`, and assert all three accessors.

Do not replace this with an Automation-only JSON fixture; the point is to catch producer and consumer name drift together.

**Task 3 acceptance:** HPA-503 can invoke all four commands and poll all three telemetry values through `DTXMania.Automation` only; generic JSON-RPC remains private.

---

## Task 4 — Regression suite plus one live prepared-chart E2E

**Goal:** replace the one-off manual checklist with repeatable proof using machinery already in CI.

### 4.1 Focused unit/contract tests

Run the host-appropriate game test project plus Automation tests. Focus first on:

- prepared-chart Song Select tests;
- existing Song Select preview/BGM tests;
- Song Select navigation/filter/BOX tests;
- SongTransition tests;
- Game API/JSON-RPC tests;
- Automation JSON-RPC tests;
- E2E-Support automation contract test.

Example:

```bash
# Windows game tests
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~PreparedChart"

# macOS game tests
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PreparedChart"

# Automation
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj

# Producer/consumer contract
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
```

### 4.2 Reuse the generated E2E fixture for preview audio

In `E2EFixtureBuilder.BuildChart()`, add:

```text
#PREVIEW: autoplay-tone.wav
```

using the existing `AudioFileName` constant rather than adding another generated asset.

### 4.3 Add one `Category=E2E` prepared-chart live smoke

Create `PreparedChartRecordingSmokeTests.cs` using existing:

- `E2EFixtureBuilder`;
- `E2EGameLaunch.CreateClientBundle`;
- `JsonRpcGameClient`;
- `Eventually.UntilAsync`;
- current stage waits/artifact helpers where reusable.

Required live flow:

1. build/launch isolated fixture;
2. enter SongSelect through the existing normal path;
3. call `PrepareVideoChartAsync(fixture.ChartPath)`;
4. call screenshot and require non-empty image;
5. wait longer than the normal automatic preview delay and assert state is `Prepared` with elapsed `0`;
6. call `StartPreparedPreviewAsync()`;
7. poll until state is `Playing` and elapsed `>= 10_000`;
8. call `ActivatePreparedChartAsync()`;
9. observe `SongTransition` before `Performance`.

Keep this test narrowly about HPA-510. Do not run to Result, inspect persistence, involve OBS, or duplicate `GameplayAutoPlaySmokeTests`' score-bucket purpose.

The existing `.github/workflows/build-and-test.yml` already runs `Category=E2E` on Windows, so no new CI job is required.

### 4.4 Full regression

Run the platform-appropriate full game unit suite and Automation tests. Verify the existing Windows gameplay E2E job remains green.

There is no separate manual smoke checklist after this change; the live E2E is the repeatable acceptance proof.

---

## Primary implementation risks

### Projection side effects

`NavigateIntoBox` repopulates the list at each level, which resets index and fires selection events. Avoid it during prepare projection: use `RestoreNavigationPaths` + one final refresh + one atomic selection under the full suppression scope.

### Prepared preview auto-start

`TryLoadPreviewSoundFile` sets `_isPreviewDelayActive = true` on success. Prepared loading must explicitly clear it.

### Exact chart identity

`GetStableSelectionIdentity` is row-oriented and can return `song:<id>`. Do not reuse it as exact prepared-chart telemetry identity.

### Activation debounce

`SelectSong` currently no-ops on debounce. Check eligibility before clearing preparation and reuse one shared transition-start body.

---

## Definition of done

- Exact absolute path selects the correct active row/difficulty for root, nested, SET/multi-chart, duplicate-title, and shared-ChartId multi-instrument cases.
- Prepare projection does not trigger wrong-row preview/BGM cycles while rebuilding BOX hierarchy.
- Prepare loads the resolved chart preview and explicitly leaves `_isPreviewDelayActive == false`.
- Start creates exactly one looped preview instance using current volume/BGM behavior.
- `PreparedPreviewElapsedMs` advances only while the instance reports Playing and reaches at least 10,000 ms in the live smoke.
- Cancel/replacement/navigation/deactivation clean up without double disposal or audio bleed.
- Blocked activation returns failure without consuming preparation; successful activation reuses normal SongTransition shared data.
- Telemetry exposes exactly prepared identity/state/elapsed with no absolute path and passes the mandatory game->Automation camelCase contract test.
- JSON-RPC exposes only four authenticated commands; generic transport stays private.
- Focused tests, full unit/Automation regression, and the existing-harness Windows `Category=E2E` prepared-chart smoke pass.
- No recorder session, generic dispatcher, song repository, fake OBS server, or new E2E framework is introduced.
