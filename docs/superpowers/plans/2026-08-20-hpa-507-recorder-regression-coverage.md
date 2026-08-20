# HPA-507 Focused Recorder Regression Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add only the remaining high-value `dtx-video` workflow/cleanup regression tests that are not already covered by HPA-503, HPA-513, and HPA-515.

**Architecture:** Keep the existing `RecordWorkflow` and its private test fakes. Extend `RecordWorkflowTests.cs` with small fault-injection knobs and focused characterization tests; production code should remain unchanged unless a test exposes a real ownership/failure-precedence bug.

**Tech Stack:** .NET 8, xUnit, existing `DTXMania.VideoRecorder.Tests`, existing `DTXMania.E2E` prepared-chart smoke.

**Spec:** `docs/superpowers/specs/2026-08-20-hpa-507-recorder-regression-coverage-design.md`

## Global Constraints

- Keep HPA-507 to one PR and under one engineer day.
- Continue implementation on the same HPA-507 branch/PR created for these planning docs; do not open a second implementation PR.
- Do not add a new test framework, mock library, fake obs-websocket server, workflow/state-machine abstraction, or E2E harness.
- Reuse the private `FakeGame`, `FakeObs`, event list, counters, state queue, timeout options, and cancellation hooks already in `RecordWorkflowTests.cs`.
- Characterization tests may be GREEN immediately on current production code. Do not manufacture a RED state by changing production code or weakening assertions.
- Modify `RecordWorkflow.cs` only if a new focused test proves a real contract violation.
- Do not duplicate existing missing/empty artifact or secret-redaction tests.
- Do not add Windows Game Capture, macOS permission diagnostics, strict MP4/remux, or full-song CI coverage.

---

### Task 1: Pin pre-OBS prepare failure and owned-preview timeout behavior

**Files:**
- Modify: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Modify only if a real bug is exposed: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`

**Interfaces:**
- Consumes: existing `RecordWorkflow.RunAsync(CancellationToken)`, `IGameRecordingControl`, `IObsRecorder`, `FastOptions(...)`.
- Produces: focused characterization coverage for one-shot prepare failure and preview-readiness timeout cleanup.

- [ ] **Step 1: Add one-shot prepare failure injection to the existing fake**

Add one optional exception property to `FakeGame`, for example:

```csharp
public Exception? PrepareException { get; init; }
```

`PrepareVideoChartAsync` should increment `PrepareCount`, record the existing `prepare` event, then throw that exception when configured. Do not add a general scripted-failure engine.

- [ ] **Step 2: Add the exact-chart prepare failure characterization test**

Add a focused test named along these lines:

```text
RunAsync_PrepareFailure_ShouldFailOnceBeforeObsAndDispose
```

Arrange Title -> populated Song Select and inject an `InvalidOperationException("chart not found in active library")` from prepare.

Assert:

```text
PrepareCount == 1
exception contains the original actionable message
no obs:connect
no obs:start
no obs:stop
game dispose occurs
OBS dispose occurs
```

This test is allowed to pass immediately on current `main`.

- [ ] **Step 3: Run only the prepare-failure test**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter FullyQualifiedName~RunAsync_PrepareFailure_ShouldFailOnceBeforeObsAndDispose
```

Expected: PASS if current one-shot/pre-ownership cleanup behavior is intact. If it fails, inspect the failure before changing production code.

- [ ] **Step 4: Add the prepared-preview timeout characterization test**

Add a focused test named along these lines:

```text
RunAsync_PreviewTimeout_ShouldStopOwnedObsWithoutActivatingChart
```

Arrange Title -> populated Song Select -> preview states that never satisfy both `PreparedPreviewState == Playing` and elapsed preview >= 10 seconds. Use a short `StageTimeout` through the existing options.

Assert:

```text
OBS start occurs
activate does not occur
TimeoutException remains the workflow failure
StopCallCount == 1
game dispose occurs
OBS dispose occurs
```

Do not add separate timeout tests for SongTransition, Performance, and Result; current tests already exercise those later bounded waits.

- [ ] **Step 5: Run the two Task 1 tests together**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RunAsync_PrepareFailure_ShouldFailOnceBeforeObsAndDispose|FullyQualifiedName~RunAsync_PreviewTimeout_ShouldStopOwnedObsWithoutActivatingChart"
```

Expected: PASS.

- [ ] **Step 6: Fix production only if a test exposed a real bug**

If either test fails because `RecordWorkflow` violates the design contract, make the smallest local correction in `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`.

Allowed examples:

```text
- preserve the original prepare exception instead of retrying/wrapping it;
- prevent activation after preview timeout;
- ensure owned OBS stop still runs from finally.
```

Not allowed:

```text
- new workflow abstractions;
- new interfaces;
- generic retry policy;
- refactoring unrelated stages.
```

- [ ] **Step 7: Commit the focused Task 1 slice**

Stage only the touched workflow test file and `RecordWorkflow.cs` if it was genuinely needed.

Suggested commit:

```text
test: cover recorder prepare and preview failures
```

---

### Task 2: Pin OBS stop-failure precedence and disposal

**Files:**
- Modify: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Modify only if a real bug is exposed: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`

**Interfaces:**
- Consumes: existing `RecordWorkflow` ownership-aware `try/finally` and `FakeObs` counters/events.
- Produces: regression coverage for cleanup failure after success and cleanup failure after a primary workflow failure.

- [ ] **Step 1: Add stop failure injection to `FakeObs`**

Add one optional exception property, for example:

```csharp
public Exception? StopException { get; init; }
```

`StopRecordAsync` must continue incrementing `StopCallCount` and recording `obs:stop` before throwing the injected exception. Keep its normal `raw-output.mp4` return path unchanged when no exception is configured.

- [ ] **Step 2: Add successful-journey / failed-stop coverage**

Add a test named along these lines:

```text
RunAsync_StopFailureAfterSuccessfulJourney_ShouldDisposeAndSurfaceStopFailure
```

Run the normal happy journey with `StopException = new InvalidOperationException("stop failed")`.

Assert:

```text
returned task fails with the stop error
StopCallCount == 1
game dispose occurs
Obs DisposeCallCount == 1
```

This proves a cleanup failure still fails an otherwise successful recording.

- [ ] **Step 3: Add primary-failure / failed-stop precedence coverage**

Add a test named along these lines:

```text
RunAsync_PrimaryFailureAndStopFailure_ShouldPreservePrimaryFailure
```

Arrange a workflow failure after OBS ownership, such as the prepared-preview timeout from Task 1, and also inject `StopException = new InvalidOperationException("stop failed")`.

Assert:

```text
primary TimeoutException is still thrown
StopCallCount == 1
game dispose occurs
Obs DisposeCallCount == 1
```

Do not require an aggregate exception. The design explicitly keeps the journey/cancellation failure primary.

- [ ] **Step 4: Run the two cleanup-precedence tests**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RunAsync_StopFailureAfterSuccessfulJourney_ShouldDisposeAndSurfaceStopFailure|FullyQualifiedName~RunAsync_PrimaryFailureAndStopFailure_ShouldPreservePrimaryFailure"
```

Expected: PASS if the current `ExceptionDispatchInfo`/`finally` contract is intact.

- [ ] **Step 5: Fix production only if the precedence contract is violated**

If current behavior fails either test, keep any change local to the `RecordWorkflow.RunAsync` cleanup block. Preserve these rules:

```text
primary workflow/cancellation failure > cleanup failure
cleanup failure fails the run only when there is no primary failure
all owned disposables are attempted even when OBS stop fails
```

Do not add exception aggregation or cleanup retry machinery.

- [ ] **Step 6: Commit the focused Task 2 slice**

Suggested commit:

```text
test: cover recorder cleanup failure precedence
```

---

### Task 3: Verify the residual coverage without expanding the harness

**Files:**
- No new production/test files expected.
- Existing integration gate: `DTXMania.E2E/PreparedChartRecordingSmokeTests.cs`

**Interfaces:**
- Consumes: the completed Task 1/2 tests plus the already-shipped prepared-chart real-CX smoke.
- Produces: final evidence that HPA-507 adds portable regression value without a new E2E path.

- [ ] **Step 1: Run the full recorder test project**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Expected: all tests pass. HPA-503 reported 49 tests at initial delivery; do not hard-code a final count because subsequent merged work has already expanded the suite.

- [ ] **Step 2: Run the existing prepared-chart E2E smoke in a supported environment**

Run on the environment already used for CX graphical E2E:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj \
  --filter FullyQualifiedName~PreparedChartRecordingSmokeTests
```

Expected: PASS.

If the environment cannot execute graphical CX E2E, rely on the normal CI job that already owns this test. Do not create a substitute fake/live recorder harness.

- [ ] **Step 3: Review the final diff for scope**

The expected implementation diff is primarily:

```text
DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs
```

`RecordWorkflow.cs` is acceptable only when one of the new tests exposed a real defect.

Confirm the diff does **not** contain:

```text
new mock/test framework dependencies
new OBS protocol server
new E2E test class
new workflow/session abstraction
media/remux logic
platform capture diagnostics
```

- [ ] **Step 4: Update the existing HPA-507 draft PR**

Push/commit the implementation to the same HPA-507 branch and keep using the same draft PR created for this plan. Update its summary and validation results; do not open a second PR for HPA-507.

- [ ] **Step 5: Mark HPA-507 complete only after CI is green**

After the same PR is reviewed and merged, update Linear with the final test coverage/validation summary and move HPA-507 to Done.

## Final self-review checklist

- [ ] Every residual risk in the design has one focused test.
- [ ] Existing HPA-503/HPA-513/HPA-515 coverage is reused instead of copied.
- [ ] No production abstraction was introduced for testability.
- [ ] Primary failure vs cleanup failure semantics are explicit and tested.
- [ ] The implementation stays within one HPA-507 PR.
- [ ] Recorder tests are green; existing prepared-chart E2E remains green through local or CI execution.
