# HPA-507 Focused Recorder Regression Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add only the remaining high-value `dtx-video` workflow/cleanup regression tests that are not already covered by HPA-503, HPA-513, and HPA-515.

**Architecture:** Keep the existing `RecordWorkflow` and its private test fakes. Extend `RecordWorkflowTests.cs` with two small fault-injection knobs plus precise preview near-miss snapshots; production code should remain unchanged unless a characterization test exposes a real ownership/failure-precedence bug.

**Tech Stack:** .NET 8, xUnit, existing `DTXMania.VideoRecorder.Tests`.

**Spec:** `docs/superpowers/specs/2026-08-20-hpa-507-recorder-regression-coverage-design.md`

## Global Constraints

- Keep HPA-507 to one PR and under one engineer day.
- Continue implementation on this same HPA-507 branch/PR; do not open a second implementation PR.
- Do not add a test framework, mock library, fake obs-websocket server, workflow/state-machine abstraction, or E2E harness.
- Reuse the private `FakeGame`, `FakeObs`, event list, counters, state queue, timeout options, and cancellation hooks already in `RecordWorkflowTests.cs`.
- Add only `FakeGame.PrepareException`, `FakeObs.StopException`, and the minimum `Preview(...)` helper parameterization needed by the new tests.
- Characterization tests may be GREEN immediately on current production code. Do not manufacture a RED production change.
- Modify `RecordWorkflow.cs` only if a focused test proves a real contract violation.
- Do not duplicate existing missing/empty artifact or secret-redaction tests.
- Do not add Windows Game Capture, macOS permission diagnostics, strict MP4/remux, or full-song CI coverage.
- HPA-507 task verification is the `DTXMania.VideoRecorder.Tests` project only. Existing AudioE2E remains owned by the normal CI job.

---

### Task 1: Pin pre-OBS prepare failure and the exact preview-readiness barrier

**Files:**
- Modify: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Modify only if a real bug is exposed: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`

**Interfaces:**
- Consumes: existing `RecordWorkflow.RunAsync(CancellationToken)`, `FakeGame`, `FakeObs`, `FastOptions(...)`, `SongSelect(...)`, and `Preview()` helpers.
- Produces: characterization coverage for one-shot prepare failure and the `Playing && elapsed >= 10_000` preview barrier after OBS ownership.

- [ ] **Step 1: Add one-shot prepare failure injection to `FakeGame`**

Add exactly one optional property:

```csharp
public Exception? PrepareException { get; init; }
```

Update `PrepareVideoChartAsync` so it preserves the current counter/event behavior before throwing:

```csharp
public Task PrepareVideoChartAsync(string chartPath, CancellationToken token)
{
    PrepareCount++;
    PrepareSelectedSongTitle = LastSelectedSongTitle;
    Events.Add("prepare");

    if (PrepareException is not null)
        throw PrepareException;

    return Task.CompletedTask;
}
```

Do not add a generic scripted-failure engine.

- [ ] **Step 2: Add the exact-chart prepare failure characterization test**

Add:

```text
RunAsync_PrepareFailure_ShouldFailOnceBeforeObsAndDispose
```

Arrange only the states needed to reach prepare:

```csharp
var game = new FakeGame(
    Title(),
    SongSelect("indexed chart"))
{
    PrepareException = new InvalidOperationException(
        "The requested chart is not available in the active song library.")
};
var obs = new FakeObs(game.Events);
```

Use `new FakeObs(game.Events)` deliberately so `obs:*` events and game events share one authoritative list.

Assert:

```text
InvalidOperationException message contains exactly the production-facing chart-unavailable text
PrepareCount == 1
game.Events does not contain obs:connect
game.Events does not contain obs:start
game.Events does not contain obs:stop
game.Events contains dispose
obs.DisposeCallCount == 1
game.Events contains obs:dispose
```

This test may pass immediately on current `main`.

- [ ] **Step 3: Run only the prepare-failure test**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter FullyQualifiedName~RunAsync_PrepareFailure_ShouldFailOnceBeforeObsAndDispose
```

Expected: PASS if current one-shot/pre-ownership cleanup behavior is intact. If it fails, inspect the failure before changing production code.

- [ ] **Step 4: Parameterize the existing `Preview()` helper for deliberate near-misses**

Keep the default behavior unchanged for existing tests while allowing state and elapsed-time overrides. One acceptable shape is:

```csharp
private static GameStateSnapshot Preview(
    string state = "Playing",
    long elapsedMs = 10_000) =>
    Snapshot(
        "SongSelect",
        $"\"preparedPreviewState\":{JsonSerializer.Serialize(state)},"
        + $"\"preparedPreviewElapsedMs\":{elapsedMs}");
```

Do not add a second fake-state builder.

- [ ] **Step 5: Add the preview predicate timeout characterization test**

Add:

```text
RunAsync_PreviewTimeout_ShouldPinPredicateAndStopOwnedObs
```

Arrange a **distinct** populated Song Select for pre-prepare readiness, then two post-start near-misses:

```csharp
var game = new FakeGame(
    Title(),
    SongSelect("indexed chart"),
    Preview(state: "Playing", elapsedMs: 9_999),
    Preview(state: "Prepared", elapsedMs: 10_000));
var obs = new FakeObs(game.Events);
```

Use a short existing `StageTimeout`, for example 25 ms.

Why both near-misses are required:

```text
- deleting the >= 10_000 check makes Playing/9,999 pass and reaches activate;
- deleting the Playing check makes Prepared/10,000 pass and reaches activate;
- with the production conjunction intact, neither passes and the wait times out.
```

Assert:

```text
TimeoutException is thrown
game.Events contains obs:start
game.Events contains start-preview
game.Events does not contain activate
obs.StopCallCount == 1
game.Events contains obs:stop
game.Events contains dispose
obs.DisposeCallCount == 1
game.Events contains obs:dispose
```

Do not put preview fields on the pre-prepare Song Select snapshot; that gate checks only stage/title and would consume the state before OBS starts.

Do not add separate timeout tests for SongTransition, Performance, and Result; current tests already cover later bounded waits.

- [ ] **Step 6: Run the two Task 1 tests together**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RunAsync_PrepareFailure_ShouldFailOnceBeforeObsAndDispose|FullyQualifiedName~RunAsync_PreviewTimeout_ShouldPinPredicateAndStopOwnedObs"
```

Expected: PASS.

- [ ] **Step 7: Fix production only if a test exposed a real bug**

If either test proves `RecordWorkflow` violates the design contract, make the smallest local correction in `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`.

Allowed examples:

```text
preserve the original prepare exception instead of retrying/wrapping it
prevent activation after preview timeout
ensure owned OBS stop still runs from finally
```

Not allowed:

```text
new workflow abstractions
new interfaces
generic retry policy
refactoring unrelated stages
```

- [ ] **Step 8: Commit the Task 1 slice**

Stage only the touched workflow test file and `RecordWorkflow.cs` if it was genuinely needed.

Suggested commit:

```text
test: cover recorder prepare and preview failures
```

---

### Task 2: Pin both OBS stop-failure precedence branches independently

**Files:**
- Modify: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Modify only if a real bug is exposed: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`

**Interfaces:**
- Consumes: existing `RecordWorkflow` ownership-aware `try/finally`, the normal happy journey, and the already-established unexpected-stage-order post-ownership timeout pattern.
- Produces: independent regression coverage for cleanup failure after success and cleanup failure after an existing primary workflow failure.

- [ ] **Step 1: Add stop failure injection to `FakeObs`**

Add exactly one optional property:

```csharp
public Exception? StopException { get; init; }
```

Update `StopRecordAsync` so the attempt remains observable before the injected failure:

```csharp
public Task<string> StopRecordAsync(CancellationToken token)
{
    StopCallCount++;
    Events.Add("obs:stop");

    if (StopException is not null)
        throw StopException;

    return Task.FromResult("raw-output.mp4");
}
```

Do not add stop retries or a cleanup fault framework.

- [ ] **Step 2: Add successful-journey / failed-stop coverage**

Add:

```text
RunAsync_StopFailureAfterSuccessfulJourney_ShouldDisposeAndSurfaceStopFailure
```

Use the normal happy journey:

```csharp
Title()
SongSelect("indexed chart")
Preview()
Transition()
Performance()
Result()
```

Inject:

```csharp
StopException = new InvalidOperationException("stop failed")
```

Assert:

```text
InvalidOperationException with "stop failed" is thrown
StopCallCount == 1
obs:stop occurs
game dispose occurs
Obs DisposeCallCount == 1
obs:dispose occurs
```

This pins the `primaryFailure is null && cleanupFailure is not null` branch.

- [ ] **Step 3: Add primary-failure / failed-stop precedence coverage using an existing post-ownership timeout**

Add:

```text
RunAsync_PrimaryFailureAndStopFailure_ShouldPreservePrimaryFailure
```

Do **not** reuse Task 1's preview timeout. Instead use the existing unexpected-stage-order pattern so cleanup precedence is independent of the new preview predicate test:

```csharp
var game = new FakeGame(
    Title(),
    SongSelect("indexed chart"),
    Preview(),
    Performance());
var obs = new FakeObs(game.Events)
{
    StopException = new InvalidOperationException("stop failed")
};
```

Use a short `StageTimeout`. This path starts OBS, completes preview, calls `activate`, then times out waiting for `SongTransition` because `Performance`/empty snapshots never satisfy that wait.

Assert:

```text
TimeoutException remains the thrown primary failure
game.Events contains activate
StopCallCount == 1
game.Events contains obs:stop
game dispose occurs
Obs DisposeCallCount == 1
game.Events contains obs:dispose
```

Do not require an aggregate exception and do not assert the stop error becomes the result. The journey failure remains primary.

- [ ] **Step 4: Run the two cleanup-precedence tests**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RunAsync_StopFailureAfterSuccessfulJourney_ShouldDisposeAndSurfaceStopFailure|FullyQualifiedName~RunAsync_PrimaryFailureAndStopFailure_ShouldPreservePrimaryFailure"
```

Expected: PASS if the current `ExceptionDispatchInfo`/`finally` contract is intact.

- [ ] **Step 5: Fix production only if the precedence contract is violated**

If current behavior fails either test, keep any production change local to the `RecordWorkflow.RunAsync` cleanup block. Preserve:

```text
primary workflow/cancellation failure > cleanup failure
cleanup failure fails the run only when there is no primary failure
all owned disposables are attempted even when OBS stop fails
```

Do not add exception aggregation or cleanup retry machinery.

- [ ] **Step 6: Commit the Task 2 slice**

Suggested commit:

```text
test: cover recorder cleanup failure precedence
```

---

### Task 3: Verify only the task-owned recorder coverage and final diff

**Files:**
- No new files expected.
- Expected implementation file: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`.
- Conditional production file only if a test proves a defect: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`.

**Interfaces:**
- Consumes: completed Task 1/2 tests.
- Produces: final evidence that HPA-507 closes only the residual portable regression gaps.

- [ ] **Step 1: Run the full recorder test project**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Expected: all tests pass. Do not hard-code a test count because subsequent recorder work may expand the suite.

Do not add a local `PreparedChartRecordingSmokeTests` run to HPA-507 acceptance. That test is already categorized `AudioE2E` and executed by the existing graphical CI job; this task does not change CX, JSON-RPC, or the E2E fixture.

- [ ] **Step 2: Review the final diff for scope**

Expected implementation diff:

```text
DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs
```

`RecordWorkflow.cs` is acceptable only if one of the four characterization tests exposed a real production defect.

Confirm the diff does **not** contain:

```text
new mock/test framework dependencies
new OBS protocol server
new E2E test class or fixture changes
new workflow/session abstraction
media/remux logic
platform capture diagnostics
```

- [ ] **Step 3: Run the recorder test project once more after any review-driven edits**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Expected: PASS.

Normal repository CI should still be green before merge, but HPA-507 does not introduce or require an additional AudioE2E gate.

- [ ] **Step 4: Update this existing HPA-507 draft PR**

Commit/push implementation to the same branch and update the PR summary/validation results. Do not open a second HPA-507 PR.

- [ ] **Step 5: Mark HPA-507 complete only after merge**

After this same PR is reviewed and merged, update Linear with the final recorder-test validation summary and move HPA-507 to Done.

## Final self-review checklist

- [ ] Prepare failure uses the real chart-unavailable message and the actual OBS event stream.
- [ ] Preview timeout uses both deliberate near-misses and proves `obs:start`, `start-preview`, no `activate`, stop once, and both disposals.
- [ ] Stop failure after success pins the cleanup-failure branch.
- [ ] Stop failure after an existing independent post-ownership timeout pins primary-failure precedence.
- [ ] Existing HPA-503/HPA-513/HPA-515 coverage is reused instead of copied.
- [ ] No production abstraction was introduced for testability.
- [ ] The implementation stays within one HPA-507 PR.
- [ ] `DTXMania.VideoRecorder.Tests` is green.
- [ ] No task-specific AudioE2E run was added.
