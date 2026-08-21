# HPA-507 Focused Recorder Regression Coverage Design

**Issue:** [HPA-507](https://linear.app/cwchanap/issue/HPA-507/optional-add-focused-recorder-workflow-and-cleanup-tests)  
**Date:** 2026-08-20  
**Status:** Revised after second planning review

## Goal

Close the remaining expensive recorder workflow/cleanup gaps with focused characterization tests. Keep the shipped `RecordWorkflow`, existing private fakes, and current CI structure; do not add a mock library, fake obs-websocket server, workflow abstraction, or new E2E harness.

Expected implementation: one small test-focused PR, well under one engineer day.

## Existing coverage we will reuse

`RecordWorkflowTests.cs` already covers the happy journey, populated Song Select readiness, unexpected stage order, pre-existing OBS, start failure, cancellation after ownership, invalid Performance readiness, incomplete Result, later-stage cancellation, and idempotent game-control disposal.

Other suites already cover artifact presence, diagnostics redaction, OBS protocol/client behavior, prepared-chart real-CX behavior, and native Windows/macOS recorder acceptance. `PreparedChartRecordingSmokeTests` remains owned by the existing `AudioE2E` CI job; HPA-507 does not add or require another graphical gate.

## Five residual contracts

### 1. Prepare failure is one-shot and pre-OBS

`RecordWorkflow` calls `PrepareVideoChartAsync` once before connecting to OBS. Characterize that boundary with `FakeGame.PrepareException` and assert:

- the exact injected exception instance escapes unchanged;
- `PrepareCount == 1`;
- no `obs:connect`, `obs:start`, or `obs:stop` occurs;
- game and OBS disposal still occur.

Do not assert the text of an exception that the fake itself constructed. Transport error-message fidelity is already covered by `JsonRpcGameClientTests`; the exact Song Select copy is not an HPA-507 recorder contract.

### 2. The preview barrier pins both `Playing` and ten seconds

After a distinct populated Song Select snapshot is consumed for prepare, queue these two post-start near-misses:

```text
SongSelect + Playing  +  9_999 ms
SongSelect + Prepared + 10_000 ms
```

Use a `StageTimeout` around 250 ms rather than 25 ms. `Eventually.UntilAsync` is wall-clock bounded, so an extremely small budget can expire before both probes execute on a stalled CI runner.

The test must assert all three Song Select snapshots were consumed:

```csharp
Assert.Equal(3, game.Events.Count(e => e == "state:SongSelect"));
```

That makes the mutation argument real:

- removing the elapsed floor accepts the first near-miss and reaches `activate`;
- removing the `Playing` check accepts the second near-miss and reaches `activate`;
- failing to consume both near-misses fails the test instead of passing by timeout.

Also assert `obs:start` and `start-preview` occurred, `activate` did not, OBS stopped once, and both disposals ran.

### 3. Post-Result screenshot failure remains fatal but recoverable

HPA-503 defines the sequence as:

```text
completed Result -> Result screenshot barrier -> 5s no-input hold -> OBS stop -> verify/publish
```

The Result screenshot is therefore part of the recording acceptance barrier, not optional decoration. Keep current fail-closed behavior: an empty second screenshot fails the run before the five-second hold and prevents normal publication.

Add `FakeGame.ResultScreenshot` plus a screenshot call counter so the first pre-OBS barrier remains valid and only the post-Result barrier can fail. Characterize that failure with a real `RecorderDiagnostics` instance and assert:

- both screenshot calls occurred;
- the five-second hold did not occur;
- owned OBS still stopped once;
- game and OBS disposal still occurred;
- diagnostics retained the raw OBS output path after the stop.

This intentionally preserves a failed run while leaving its raw take discoverable for manual recovery; no production behavior change is proposed.

### 4. Stop failure after success becomes the run failure

Add `FakeObs.StopException`. Run the normal happy journey and assert the injected stop failure escapes after game/OBS disposal.

Pass a `RecorderDiagnostics` instance and write/read its `run.json` after the failure to assert the workflow recorded a failed `Stop` OBS outcome. This cheaply pins the user-visible cleanup evidence without adding a separate diagnostics scenario.

Do not add stop retries or exception aggregation.

### 5. Stop failure never replaces an existing primary failure

Use the already-covered unexpected-stage-order arrangement after OBS ownership, not the new preview test. Inject the same stop failure and assert:

- the original `TimeoutException` remains primary;
- stop was attempted once;
- game and OBS disposal still occurred.

This independently pins the `primaryFailure != null` cleanup branch.

## Test seam

Keep everything inside `RecordWorkflowTests.cs`:

- `FakeGame.PrepareException`;
- `FakeGame.ResultScreenshot` plus screenshot call count;
- `FakeObs.StopException`;
- parameterized `Preview(state, elapsedMs)`;
- a tiny test-local helper only if useful for writing/reading `RecorderDiagnostics` JSON.

Production code should remain unchanged unless a focused test exposes a genuine violation of the contracts above. Any necessary correction stays local to `RecordWorkflow.cs`.

## Verification boundary

Task-specific verification is only:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Normal repository CI still runs its existing `AudioE2E` job. Do not add a separate prepared-chart E2E invocation to HPA-507.

## Risks and mitigations

### CI-stall vacuity

A tiny wall-clock timeout could let the preview test pass without consuming both near-misses. Mitigate with ~250 ms and the explicit three-SongSelect consumption assertion.

### Result screenshot policy

Failing the Result screenshot means the required render barrier and five-second hold were not completed. Keep the run failed/unpublished, but prove the raw OBS path is retained in diagnostics after owned stop so the take is not silently lost.

## Out of scope

- pinning exact Song Select error copy;
- Windows Game Capture source validation (HPA-504);
- macOS ScreenCaptureKit/permission diagnostics (HPA-505);
- strict MP4/remux fallback (HPA-506);
- exhaustive state-machine testing;
- stop retries or exception aggregation;
- new recorder architecture or test infrastructure.

## Acceptance criteria

HPA-507 is complete when the five tests above pass, the full recorder test project is green, the implementation remains on this single PR, and the final diff introduces no new test/runtime abstraction. Production changes are expected to be zero unless one characterization test proves a real defect.