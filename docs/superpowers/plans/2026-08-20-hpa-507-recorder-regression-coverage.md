# HPA-507 Focused Recorder Regression Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add five focused characterization tests for the remaining expensive recorder workflow/cleanup gaps.

**Architecture:** Extend only the private fakes in `RecordWorkflowTests.cs`: three one-purpose fault knobs plus preview parameterization. Keep production unchanged unless a new test proves a real contract violation.

**Tech Stack:** .NET 8, xUnit, existing `DTXMania.VideoRecorder.Tests`.

**Spec:** `docs/superpowers/specs/2026-08-20-hpa-507-recorder-regression-coverage-design.md`

## Global Constraints

- One HPA-507 PR; continue on the existing branch/PR.
- No mock library, fake obs-websocket server, workflow/state-machine abstraction, new E2E harness, stop retry, or exception aggregation.
- HPA-507 verification is only `DTXMania.VideoRecorder.Tests`; existing `AudioE2E` remains CI-owned.
- Characterization tests may be green on current `main`; do not manufacture production changes.
- If production must change, keep the correction local to `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`.

---

### Task 1: Add the five characterization tests on the existing private fakes

**Files:**
- Modify: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Modify only if a test proves a bug: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`

**Interfaces:**
- Consumes: `RecordWorkflow.RunAsync`, existing `FakeGame`, `FakeObs`, `FastOptions`, `SongSelect`, `Preview`, and `RecorderDiagnostics`.
- Produces: five regression tests covering prepare ownership, preview readiness, Result screenshot failure, and both stop-failure precedence branches.

- [ ] **Step 1: Extend the existing fake seam only**

Add these three fault knobs and one counter:

```csharp
public Exception? PrepareException { get; init; }
public string? ResultScreenshot { get; init; } = "c2NyZWVuc2hvdA==";
public int ScreenshotCallCount { get; private set; }
```

Update prepare without adding a general fault engine:

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

Make only the second screenshot configurable:

```csharp
public Task<string?> TakeScreenshotBase64Async(CancellationToken token)
{
    Events.Add("screenshot");
    ScreenshotCallCount++;
    return Task.FromResult<string?>(
        ScreenshotCallCount == 1 ? "c2NyZWVuc2hvdA==" : ResultScreenshot);
}
```

Add the OBS stop knob:

```csharp
public Exception? StopException { get; init; }

public Task<string> StopRecordAsync(CancellationToken token)
{
    StopCallCount++;
    Events.Add("obs:stop");
    if (StopException is not null)
        throw StopException;
    return Task.FromResult("raw-output.mp4");
}
```

Parameterize the existing preview helper while preserving its defaults:

```csharp
private static GameStateSnapshot Preview(
    string state = "Playing",
    double elapsedMs = 10_000) =>
    Snapshot(
        "SongSelect",
        $"\"preparedPreviewState\":{JsonSerializer.Serialize(state)},"
        + $"\"preparedPreviewElapsedMs\":{elapsedMs}");
```

- [ ] **Step 2: Add prepare-failure ownership coverage**

Add `RunAsync_PrepareFailure_ShouldFailOnceBeforeObsAndDispose`.

Arrange `Title -> populated SongSelect`, share events with `new FakeObs(game.Events)`, and inject a single `InvalidOperationException` instance through `PrepareException`.

Assert:

```csharp
Assert.Same(failure, exception);
Assert.Equal(1, game.PrepareCount);
Assert.DoesNotContain("obs:connect", game.Events);
Assert.DoesNotContain("obs:start", game.Events);
Assert.DoesNotContain("obs:stop", game.Events);
Assert.Contains("dispose", game.Events);
Assert.Equal(1, obs.DisposeCallCount);
Assert.Contains("obs:dispose", game.Events);
```

Do not assert the fake's injected message text.

- [ ] **Step 3: Add a mutation-sensitive preview-barrier timeout**

Add `RunAsync_PreviewTimeout_ShouldConsumeNearMissesAndStopOwnedObs` with:

```csharp
var game = new FakeGame(
    Title(),
    SongSelect("indexed chart"),
    Preview(state: "Playing", elapsedMs: 9_999),
    Preview(state: "Prepared", elapsedMs: 10_000));
var obs = new FakeObs(game.Events);
```

Use:

```csharp
FastOptions(game.Events) with
{
    StageTimeout = TimeSpan.FromMilliseconds(250)
}
```

Assert the timeout and, critically, that both near-misses were really polled:

```csharp
Assert.Equal(3, game.Events.Count(e => e == "state:SongSelect"));
Assert.Contains("obs:start", game.Events);
Assert.Contains("start-preview", game.Events);
Assert.DoesNotContain("activate", game.Events);
Assert.Equal(1, obs.StopCallCount);
Assert.Contains("obs:stop", game.Events);
Assert.Contains("dispose", game.Events);
Assert.Equal(1, obs.DisposeCallCount);
```

This test must fail if either preview predicate is removed or if CI starvation prevents both near-misses from being consumed.

- [ ] **Step 4: Add post-Result screenshot-failure coverage**

Add `RunAsync_EmptyResultScreenshot_ShouldFailButRetainRawOutputEvidence` using the normal happy-state queue and:

```csharp
ResultScreenshot = string.Empty
```

Pass a real `RecorderDiagnostics` with a temporary output directory. Assert:

```text
InvalidOperationException is thrown for the empty screenshot
ScreenshotCallCount == 2
Result was reached
the 5-second delay event is absent
StopCallCount == 1
both disposals ran
```

Then call `diagnostics.WriteAsync(...)`, parse `run.json`, and assert:

```text
status == Failed
rawOutputPath ends with raw-output.mp4
```

This pins the chosen policy: the required Result render barrier fails the run, but owned OBS still stops and the raw take remains discoverable in diagnostics.

- [ ] **Step 5: Add stop-failure-after-success coverage including diagnostics wiring**

Add `RunAsync_StopFailureAfterSuccessfulJourney_ShouldDisposeSurfaceAndRecordStopFailure` using the normal happy journey and:

```csharp
StopException = new InvalidOperationException("stop failed")
```

Pass `RecorderDiagnostics`, assert the same injected stop exception escapes, stop is attempted once, and both disposals run.

Write/read `run.json` and assert `obsOutcomes` contains:

```json
{"operation":"Stop","succeeded":false,"detail":"stop failed"}
```

Do not add a separate diagnostics-only test.

- [ ] **Step 6: Add stop-failure precedence using the existing independent timeout path**

Add `RunAsync_PrimaryFailureAndStopFailure_ShouldPreservePrimaryFailure` using the existing unexpected-stage-order arrangement:

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

Use a bounded `StageTimeout` such as 250 ms. Assert:

```text
TimeoutException remains the thrown failure
activate occurred
StopCallCount == 1
obs:stop occurred
both disposals ran
```

Do not depend on the new preview-timeout test for this branch.

- [ ] **Step 7: Run the full recorder test project once**

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Expected: PASS.

If a new characterization test fails because production violates the documented contract, make only the smallest local `RecordWorkflow.cs` correction, then rerun this same command.

- [ ] **Step 8: Commit the complete HPA-507 test slice**

Stage only the touched test file plus `RecordWorkflow.cs` if a proven defect required it.

Suggested commit:

```text
test: cover recorder workflow cleanup regressions
```

---

### Task 2: Final scope review and same-PR handoff

**Files:**
- Expected implementation change: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Conditional only: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`

**Interfaces:**
- Consumes: Task 1's green recorder suite.
- Produces: a review-ready HPA-507 diff on the existing PR.

- [ ] **Step 1: Review the final diff**

Confirm there is no new dependency, test framework, E2E class, OBS protocol server, workflow/session abstraction, media/remux logic, or platform-capture diagnostic code.

- [ ] **Step 2: Update the existing draft PR validation summary**

Record the final `DTXMania.VideoRecorder.Tests` result and whether production remained unchanged. Keep implementation on this same HPA-507 PR.

- [ ] **Step 3: Complete Linear only after merge**

After this PR is reviewed and merged, update HPA-507 with the final validation summary and move it to Done.

No second local E2E run or duplicate full-suite invocation is part of HPA-507 unless a review-driven code edit occurs after Task 1 verification.