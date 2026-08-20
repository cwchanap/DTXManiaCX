# HPA-507 Focused Recorder Regression Coverage Design

**Issue:** [HPA-507](https://linear.app/cwchanap/issue/HPA-507/optional-add-focused-recorder-workflow-and-cleanup-tests)  
**Date:** 2026-08-20  
**Status:** Revised after planning review

## Goal

Close the remaining high-value regression gaps around the shipped `dtx-video` workflow and cleanup behavior without creating a new recorder test framework, fake OBS server, or additional E2E harness.

This is a small characterization-test task. HPA-503 already shipped the recorder and broad portable coverage; HPA-513 and HPA-515 already supplied native Windows and Apple Silicon acceptance. HPA-507 should add only tests that still protect meaningful ownership/error contracts.

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

- prepare is invoked exactly once;
- the production-facing failure text remains actionable: `The requested chart is not available in the active song library.`;
- OBS is never connected, started, or stopped;
- game and OBS disposables are still cleaned up.

Use the OBS fake's actual event list. Prefer `new FakeObs(game.Events)` so the test can assert the unified event stream; never assert `obs:*` entries against `game.Events` when the fake is using its own private list.

### 2. Prepared preview predicate remains a real barrier after recording starts

After OBS starts, the workflow requires all three conditions:

```text
StageType == SongSelect
PreparedPreviewState == Playing
PreparedPreviewElapsedMs >= 10_000
```

A timeout test that merely exhausts `FakeGame`'s state queue is too weak: it would still time out if either preview predicate were accidentally removed.

After the distinct populated Song Select used for prepare, queue two deliberate near-misses:

```text
SongSelect + Playing  +  9_999 ms
SongSelect + Prepared + 10_000 ms
```

With the real predicate, neither state is accepted and the bounded wait times out. If the elapsed-time floor is removed, the first state passes; if the `Playing` requirement is removed, the second state passes. In either regression the workflow reaches `activate`, so the test fails instead of passing vacuously.

The test must also prove:

- `obs:start` happened;
- `start-preview` happened;
- `activate` did not happen;
- `TimeoutException` is the workflow failure;
- recorder-owned OBS is stopped exactly once;
- game and OBS disposables run.

Do not put the near-miss fields on the pre-prepare Song Select snapshot; that readiness gate only checks stage/title and would consume the state before OBS ownership.

Do not add separate timeout tests for every later stage. Existing unexpected-stage, invalid-Performance, and incomplete-Result tests already cover the same bounded wait machinery later in the journey.

### 3. OBS stop failure precedence is untested

Cleanup failures have two contracts:

- if the journey succeeded, a recorder-owned OBS stop failure must fail the run;
- if the journey already failed, a secondary OBS stop failure must not replace the primary journey/cancellation failure.

Add one test for each branch. Both must prove game/OBS disposal still occurs after the stop attempt.

The primary-failure branch must use an already-established post-ownership failure such as the existing unexpected-stage-order timeout after activation. Do not make this test depend on the new preview near-miss test; the cleanup-precedence contract should remain independently characterized.

This pins the existing `ExceptionDispatchInfo` cleanup contract without adding aggregation, stop retries, or recovery machinery.

## Chosen test shape

Extend the existing private fakes inside `RecordWorkflowTests.cs` rather than adding shared test infrastructure.

Minimal additions:

- `FakeGame.PrepareException`;
- `FakeObs.StopException`;
- parameterize the existing `Preview()` helper enough to create the two near-miss snapshots;
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

Do not add or require a new HPA-507 E2E gate.

`PreparedChartRecordingSmokeTests` is already an `AudioE2E` test owned by the existing graphical CI job, and HPA-513/HPA-515 already exercised the complete recorder with real OBS on supported native platforms. HPA-507 does not change CX, JSON-RPC, the E2E fixture, or native capture behavior.

The task-specific verification for HPA-507 is therefore only:

```text
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Normal repository CI may continue running its existing AudioE2E job; do not make a separate local prepared-chart E2E run part of this task's acceptance checklist.

## Production-change policy

Expected production changes: **none**.

A production edit is justified only when a new focused test exposes a real violation of the ownership/failure rules above. If required, keep the change inside `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs` and avoid new abstractions.

Characterization tests may be green immediately on current `main`. Do not manufacture a RED production change.

## Acceptance criteria

HPA-507 is complete when:

1. exact prepare failure is characterized as one-shot and pre-OBS using the real OBS event list;
2. prepared-preview timeout uses deliberate near-miss states that pin both `Playing` and the 10-second floor, proves preview start, owned OBS cleanup, and no premature activation;
3. stop failure after success is surfaced after all cleanup;
4. stop failure after an independent existing post-ownership primary failure does not replace that primary failure;
5. no new test framework, protocol server, or E2E harness is introduced;
6. the full `DTXMania.VideoRecorder.Tests` project passes;
7. the final diff remains limited to the existing recorder test seam plus `RecordWorkflow.cs` only if a characterization test proves a real defect.

## Out of scope

- Windows Game Capture source validation (HPA-504);
- macOS ScreenCaptureKit/permission diagnostics (HPA-505);
- strict MP4 policy/remux fallback (HPA-506);
- exhaustive workflow state-transition testing;
- protocol fuzzing;
- full-song recording in normal CI;
- new recorder architecture or dependency injection.
