# HPA-507 Focused Recorder Regression Coverage Design

**Issue:** [HPA-507](https://linear.app/cwchanap/issue/HPA-507/optional-add-focused-recorder-workflow-and-cleanup-tests)  
**Date:** 2026-08-20  
**Status:** Proposed

## Goal

Close the remaining high-value regression gaps around the shipped `dtx-video` workflow and cleanup behavior without creating a new recorder test framework, fake OBS server, or additional E2E harness.

This is a small characterization-test task. HPA-503 already shipped the recorder and broad portable coverage; HPA-513 and HPA-515 already supplied native Windows and Apple Silicon acceptance. HPA-507 should therefore add only tests that still protect meaningful ownership/error contracts.

Expected implementation size: well under one engineer day, one PR.

## Current baseline

The original HPA-507 checklist was written before HPA-503 implementation landed. Current `main` already contains most of it.

`DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs` already protects:

- the full happy-path step order;
- waiting for populated Song Select before prepare;
- unexpected stage order / bounded failure;
- refusing pre-existing OBS recording without claiming ownership;
- OBS start failure without stop ownership;
- cancellation after recorder-owned OBS start;
- invalid AutoPlay / empty-note Performance readiness;
- incomplete Result completion;
- cancellation during Performance;
- cancellation during the Result hold;
- idempotent `AutomationGameRecordingControl.DisposeAsync()`.

Other existing tests already protect:

- missing or empty raw artifacts in `RecordingArtifactVerifierTests`;
- secret redaction and bounded recorder diagnostics in `RecorderDiagnosticsTests`;
- OBS protocol/client behavior in the existing OBS test files;
- prepared-chart real-CX behavior in `PreparedChartRecordingSmokeTests`;
- native source/audio/ownership acceptance through HPA-513 and HPA-515.

Do not duplicate these scenarios in HPA-507.

## Residual risks worth protecting

### 1. Exact chart prepare fails before OBS ownership

`RecordWorkflow` intentionally performs `PrepareVideoChartAsync` once and preserves the actionable Game API error instead of polling/retrying a permanent library/chart failure.

Add a regression proving:

- the prepare exception is propagated unchanged enough to remain actionable;
- OBS is never connected, started, or stopped;
- game/OBS disposables are still cleaned up;
- prepare is invoked exactly once.

This protects the most useful pre-recording failure boundary and the one-shot design from HPA-503.

### 2. Prepared preview never becomes ready after recording starts

After OBS starts, the workflow waits for:

```text
StageType == SongSelect
PreparedPreviewState == Playing
PreparedPreviewElapsedMs >= 10_000
```

Add one timeout regression proving:

- the workflow does not activate the prepared chart prematurely;
- the recorder-owned OBS recording is stopped exactly once;
- the owned game and OBS client are disposed;
- the timeout remains the primary failure.

Do not add separate timeout tests for every stage. Existing unexpected-stage, invalid-Performance, and incomplete-Result tests already cover the same bounded wait machinery later in the journey.

### 3. OBS stop fails after an otherwise successful journey

Cleanup failures have two different contracts:

- if the journey succeeded, a recorder-owned OBS stop failure must fail the run;
- if the journey already failed, a secondary OBS stop failure must not replace the primary journey/cancellation failure.

Add one test for each branch. Both must still prove game/OBS disposal occurs after the stop attempt.

This is valuable because `RecordWorkflow` deliberately uses `ExceptionDispatchInfo` to preserve cleanup failure identity only when no primary failure exists.

## Chosen test shape

Extend the existing private fakes inside `RecordWorkflowTests.cs` rather than adding shared test infrastructure.

Minimal additions are enough:

- `FakeGame`: optional prepare exception injection;
- `FakeObs`: optional stop exception injection;
- reuse the existing event list, counters, state queue, fast timeouts, and cancellation hooks.

Do not introduce:

- a general fault-script engine;
- a workflow state machine;
- mock libraries;
- a fake obs-websocket server;
- new production interfaces only for tests;
- a new recorder E2E suite.

If a new characterization test fails against current production code, make the smallest correction in `RecordWorkflow.cs`. Do not refactor unrelated workflow code while touching it.

## Ownership and failure invariants

The tests should pin these rules explicitly:

### Before OBS ownership

When failure happens before `StartRecordAsync` succeeds:

- never call `StopRecordAsync`;
- dispose the game control;
- dispose the OBS client;
- preserve the original workflow failure.

### After OBS ownership

Once this run successfully starts OBS:

- attempt `StopRecordAsync` exactly once from `finally`;
- continue disposing the game and OBS client even if stop fails;
- never stop a recording that was already active before this run;
- if the main journey failed, keep that failure primary;
- if the main journey succeeded, surface the cleanup failure.

No retries are needed for OBS stop in this ticket. HPA-507 is regression coverage, not media-recovery hardening.

## E2E boundary

Do not add a new live recorder E2E.

The unique real-CX seam already exists in `DTXMania.E2E/PreparedChartRecordingSmokeTests.cs`, and HPA-513/HPA-515 already exercised the complete recorder with real OBS on supported native platforms.

HPA-507 implementation should run the existing prepared-chart smoke as a final integration gate when the environment supports it. The normal portable acceptance remains the `DTXMania.VideoRecorder.Tests` suite.

## Production-change policy

Expected production changes: **none**.

A production edit is justified only when a new focused test exposes a real violation of the ownership/failure rules above. If required, keep the change inside `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs` and avoid new abstractions.

## Acceptance criteria

HPA-507 is complete when:

1. exact prepare failure is characterized as one-shot and pre-OBS;
2. prepared-preview timeout proves owned OBS cleanup and no premature activation;
3. stop failure after success is surfaced after all cleanup;
4. stop failure after a primary workflow failure does not replace that primary failure;
5. no new test framework, protocol server, or E2E harness is introduced;
6. the full recorder test project passes on normal CI;
7. the existing prepared-chart E2E smoke remains green in its supported environment.

## Out of scope

- Windows Game Capture source validation (HPA-504);
- macOS ScreenCaptureKit/permission diagnostics (HPA-505);
- strict MP4 policy/remux fallback (HPA-506);
- exhaustive workflow state-transition testing;
- protocol fuzzing;
- full-song recording in normal CI;
- new recorder architecture or dependency injection.
