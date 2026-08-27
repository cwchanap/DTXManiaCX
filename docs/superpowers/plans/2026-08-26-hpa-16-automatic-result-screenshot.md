# HPA-16 Automatic Result Screenshot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` and execute this plan task-by-task. Keep all implementation on this same PR.

**Goal:** Save exactly one PNG of the fully revealed Result frame to the game’s app-data `Screenshots` directory without adding a second framebuffer/PNG pipeline or affecting Result progression when an accepted capture fails.

**Architecture:** Reuse `BaseGame`’s existing pending screenshot request and `RenderTarget2D.SaveAsPng()` fulfillment. Expose that same queue through `IStageGame`, request capture at the end of an eligible Result draw, distinguish an unaccepted busy-slot request from an accepted attempt, then persist accepted bytes asynchronously through a pure/testable `AppPaths` filename contract.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, existing DTXMania E2E/JSON-RPC harness.

**Spec:** `docs/superpowers/specs/2026-08-26-hpa-16-automatic-result-screenshot-design.md`

## Global constraints

- One PR for planning + implementation.
- Reuse the existing `_pendingScreenshot` / `CapturePendingScreenshot()` / `CaptureRenderTargetAsPng()` path.
- Trigger from Result **Draw**, not Update.
- One **accepted** automatic capture request per Result activation. A BaseGame busy-slot rejection is not an attempt and may retry on a later Result draw.
- Do not add a `CurrentPhase == StagePhase.Normal` guard: current `StageManager.DrawTransition()` still draws outgoing Result pixels unchanged, and the exact natural-reveal-completion + exit-input edge depends on that draw remaining eligible.
- Runtime output is `<AppDataRoot>/Screenshots`; do not reuse E2E `ArtifactRoot`.
- Reuse `AppPaths.EnsureDirectory()` for directory creation.
- No MCP/Automation production dependency.
- No config toggle, UI toast, retry queue, screenshot manager, retention policy, or song-title filename work. HPA-16 is always-on; “optional” means non-fatal side effect for this slice.
- Accepted capture/filesystem failure is warning-only and must never block Result interaction/progression.
- The existing Windows full-AutoPlay E2E is a required merge gate because the macOS unit suite cannot exercise the graphics-bound Result `OnDraw()` wiring.

---

### Task 1: Expose the existing capture queue and pin the persist contract

**Files:**
- Modify: `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Test/Utilities/AppPathsTests.cs`
- Modify: `DTXMania.Test/Stage/IStageGameContractTests.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`

**Produces:** a stage-facing request that is demonstrably the same screenshot queue already used by the Game API, plus exact runtime directory/filename contracts that are unit-testable without a live game.

- [ ] **Step 1: Pin the screenshot root and exact filename contract**

Add focused `AppPathsTests` first.

Root contract:

```text
GetScreenshotsRoot()
  is rooted
  last directory segment == "Screenshots"
  parent == AppPaths.GetAppDataRoot()
```

Also mirror the existing `GetCrashReportsRoot()` override-shape test: set `DTXMANIA_APPDATA_ROOT` to a temp root and assert `GetScreenshotsRoot()` equals `<override>/Screenshots`.

Filename contract: pass a fixed timestamp to a tiny pure helper and assert the complete path, for example:

```text
root = /tmp/screenshots
2026-08-26 22:17:55.123
=> /tmp/screenshots/result-20260826-221755-123.png
```

Expected on current main: compile/test failure because these helpers do not exist.

- [ ] **Step 2: Add only the two small `AppPaths` helpers**

In `AppPaths.cs`, add:

```csharp
public static string GetScreenshotsRoot()
{
    return Path.GetFullPath(Path.Combine(GetAppDataRoot(), "Screenshots"));
}

internal static string BuildResultScreenshotPath(string screenshotsRoot, DateTime timestamp)
{
    return Path.Combine(screenshotsRoot, $"result-{timestamp:yyyyMMdd-HHmmss-fff}.png");
}
```

`DTXMania.Game` already exposes internals to both test assemblies, so no visibility change is required.

Do not create the directory in either helper. Directory creation belongs to the write operation and should reuse the already-existing `AppPaths.EnsureDirectory()` helper.

Do not add collision handling, sanitization, retention, or a generic artifact-path framework.

- [ ] **Step 3: Pin only the useful `IStageGame` default-interface-member behavior**

Update `DTXMania.Test/Stage/IStageGameContractTests.cs`, next to the existing startup-report and `CrashReportInbox` DIM tests.

Add:

```text
IStageGame_DefaultCaptureScreenshotAsync_ShouldReturnNull_WhenImplementationDoesNotOverrideIt
```

using the existing `MinimalStageGameStub` without adding a stub implementation. Await the call through `IStageGame` and assert null.

Do **not** add `CaptureScreenshotAsync` to the existing reflection-only interface-declaration assertion. The executable DIM test already references the member directly and is the meaningful contract here.

This test pins source compatibility/default behavior for existing stage stubs. It does **not** replace the concrete BaseGame forwarding test below.

- [ ] **Step 4: Pin BaseGame’s concrete stage-interface forward and shared slot**

In `BaseGameTests`, add coverage that requests a screenshot through the stage contract and proves it uses the existing pending slot:

1. create the existing headless `BaseGame` test instance;
2. cast to `IStageGame` and request a screenshot;
3. assert `_pendingScreenshot` is populated and its task is the returned task;
4. while it is still pending, call `((IGameContext)game).CaptureScreenshotAsync()`;
5. assert the second request completes synchronously with null.

This test catches the important dual-interface footgun: if `BaseGame` forgets to implement the new `IStageGame` member, the interface default would return null and `_pendingScreenshot` would remain empty.

Do not add a new render-target encoder test; existing BaseGame coverage already owns pending capture fulfillment and unavailable-device behavior.

- [ ] **Step 5: Expose capture through `IStageGame` without widening the interface graph**

In `IStageGame`, add a default method returning null for headless/test implementations:

```csharp
Task<byte[]?> CaptureScreenshotAsync() => Task.FromResult<byte[]?>(null);
```

Add the required `System.Threading.Tasks` import.

Do **not** make `IStageGame : IGameContext`.

- [ ] **Step 6: Refactor BaseGame to one queue helper**

Move the existing body of explicit `IGameContext.CaptureScreenshotAsync()` into one private helper, e.g. `QueueScreenshotCapture()`.

Both explicit interfaces must forward to it:

```text
IGameContext.CaptureScreenshotAsync() -> QueueScreenshotCapture()
IStageGame.CaptureScreenshotAsync()  -> QueueScreenshotCapture()
```

Keep all existing behavior unchanged:

- `Interlocked.CompareExchange` still permits only one pending request;
- an accepted request returns the pending TCS task, which remains incomplete until `BaseGame.Draw()` resumes after `StageManager.Draw()`;
- a busy second request returns `Task.FromResult<byte[]?>(null)`, i.e. synchronously completed null;
- `BaseGame.Draw()` still owns capture fulfillment;
- `CaptureRenderTargetAsPng()` stays the only PNG encoder.

- [ ] **Step 7: Run focused Task 1 tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~IStageGameContractTests|FullyQualifiedName~BaseGameTests"
```

Expected: PASS.

---

### Task 2: Schedule one accepted Result capture and make failure non-fatal

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Modify: `DTXMania.Test/Stage/ResultStageTests.cs`

**Produces:** one accepted asynchronous save attempt after fully revealed Result content has actually rendered, while a request rejected by the already-busy shared slot may retry on a later Result draw.

- [ ] **Step 1: Extend the existing Result test seam, not the harness**

Reuse `ResultStageTests.InspectableResultStage`.

Keep queue acceptance in the scheduler and add one overrideable persistence seam on production `ResultStage` that receives the already-returned capture task:

```text
CaptureAndSaveResultScreenshotAsync(Task<byte[]?> captureTask)
```

The inspectable test subclass should be able to count accepted persistence calls and optionally return/throw a failed task without constructing a live `GraphicsDevice` or touching the real filesystem.

For the busy-slot test, use a small controllable `IStageGame` fake/stub (or adapt the existing inspectable constructor to accept `IStageGame`) whose `CaptureScreenshotAsync()` can return either:

- `Task.FromResult<byte[]?>(null)` for “slot busy / request rejected”; or
- an incomplete `TaskCompletionSource<byte[]?>.Task` for “request accepted”.

Do not add a screenshot service abstraction or a second Result test fixture.

- [ ] **Step 2: Add one named scheduling helper shared by unit tests and `OnDraw()`**

Add an internal helper such as:

```text
TryScheduleResultScreenshot()
```

Do **not** mark this helper `[ExcludeFromCodeCoverage]`.

Focused tests call this exact method directly, and production `OnDraw()` calls the same method after `_spriteBatch.End()`. This keeps the scheduling logic unit-tested even though the graphics-bound `OnDraw()` remains `[ExcludeFromCodeCoverage]` and cannot run headlessly.

- [ ] **Step 3: Add characterization tests for accepted-vs-busy one-shot behavior**

Required cases against `TryScheduleResultScreenshot()`:

1. reveal incomplete -> capture queue is not called;
2. reveal complete + fake returns an incomplete pending task -> one accepted persistence call and one-shot consumed;
3. invoke again while still complete after acceptance -> queue/persistence are not called again;
4. fake first returns synchronously completed null -> no accepted persistence call and one-shot remains false; switch the fake to an incomplete pending task and invoke again -> exactly one accepted attempt;
5. accepted capture/persist throws -> safe wrapper observes/logs it and a later eligible draw does not retry;
6. `OnActivate()` resets the accepted-attempt flag for a new Result activation.

The tests should pin behavior, not private task plumbing or exact log text.

Do **not** add a `StagePhase.Normal` characterization. Current `StageManager.DrawTransition()` does not apply fade alpha to outgoing pixels, and a Normal-only predicate would introduce a missing-screenshot edge when natural reveal completion and exit input occur in the same update.

- [ ] **Step 4: Add the one-shot field and reset**

In `ResultStage` add only:

```text
_resultScreenshotRequested
```

Reset it to `false` in `OnActivate()` because `StageManager` caches the same Result stage instance across plays.

The flag means “automatic screenshot request was accepted / an actual attempt is now owned by HPA-16”, not merely “we called the queue API”.

A retry count, queue, cancellation source, phase flag, or screenshot lifecycle enum is unnecessary.

- [ ] **Step 5: Trigger after Result rendering without phase-gating**

At the end of `OnDraw()`, after the Result renderer/fallback has drawn and `_spriteBatch.End()` has completed, call `TryScheduleResultScreenshot()`.

The initial eligibility guard is only:

```text
_revealState?.IsComplete == true
&& !_resultScreenshotRequested
```

Do **not** add `CurrentPhase == StagePhase.Normal`.

Why: in current `StageManager.DrawTransition()`, fade alpha is not applied to the outgoing stage’s pixels; Result is drawn unchanged while fade-out alpha is positive. If the player presses Activate/Back in the exact update where natural reveal completion occurs, that outgoing transition draw can be the first eligible fully revealed Result draw. A Normal-only guard would skip it and then deactivate Result without ever requesting the PNG.

Fast-forward still needs no special case: when reveal is incomplete, the first Activate/Back calls `_revealState.Complete()` and is consumed; a later input is required to leave Result.

- [ ] **Step 6: Distinguish an unaccepted busy request, then persist through existing ownership**

Inside `TryScheduleResultScreenshot()` after the eligibility guard:

1. call `_game.CaptureScreenshotAsync()` immediately while still inside Result `OnDraw()`;
2. detect the concrete BaseGame busy sentinel precisely:

```csharp
if (captureTask.IsCompletedSuccessfully && captureTask.Result is null)
{
    // Existing _pendingScreenshot slot is busy; this request was never queued.
    // Keep _resultScreenshotRequested false so the next Result draw may retry.
    return;
}
```

Do **not** use `captureTask.IsCompleted` alone. A synchronously faulted task or hypothetical synchronously completed non-null implementation is not the BaseGame busy sentinel and should count as an attempt/result rather than being retried forever.

3. set `_resultScreenshotRequested = true` **before** starting asynchronous observation/persistence;
4. launch only the safe wrapper, passing the already-returned `captureTask`;
5. in `CaptureAndSaveResultScreenshotAsync(captureTask)`, await the PNG bytes;
6. treat accepted-task null/empty bytes as a non-fatal failed attempt with no retry;
7. resolve `AppPaths.GetScreenshotsRoot()`;
8. call `AppPaths.EnsureDirectory(root)`;
9. call `AppPaths.BuildResultScreenshotPath(root, DateTime.Now)`;
10. write the bytes asynchronously.

If the direct `_game.CaptureScreenshotAsync()` call itself throws synchronously, catch/log it at the scheduler boundary and consume the one-shot rather than creating an unbounded retry loop. The concrete BaseGame queue is not expected to throw during normal operation.

The request is issued during `ResultStage.OnDraw()`. When control returns to `BaseGame.Draw()`, the existing pending-request logic captures the still-bound Result render target from that same draw.

Do not inline another filename formatter in `ResultStage`; the fixed-timestamp AppPaths unit test owns that contract.

- [ ] **Step 7: Wrap accepted capture/persist failure safely**

The draw method must never synchronously wait for capture or disk I/O.

Use a private safe async wrapper that catches all exceptions from `CaptureAndSaveResultScreenshotAsync(captureTask)` and logs one warning via the existing `game.LoggerFactory` infrastructure. Follow the existing Result-stage style for observing async failures; discard only the safe wrapper task.

The one-shot flag remains set after any **accepted** capture/encode/write failure. Do not retry those failures on later Result draws.

A synchronously completed null task from BaseGame is different: the automatic request never entered the queue, so it leaves the flag false and may retry on the next Result draw. This is not a new retry queue.

- [ ] **Step 8: Run focused Result tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ResultStageTests"
```

Expected: PASS.

---

### Task 3: Prove the actual Result Draw wiring and close the slice

**Files:**
- Modify: `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`

**Produces:** black-box proof that the production Result draw, BaseGame capture queue, PNG encoding, app-data path, and accepted one-shot behavior work together.

The project targets `net8.0-windows7.0`. This live E2E is therefore a required Windows PR-CI merge gate; the macOS unit suite is necessary but not sufficient because `ResultStage.OnDraw()` is graphics-bound and `[ExcludeFromCodeCoverage]`.

- [ ] **Step 1: Extend the existing full-AutoPlay smoke only**

Do not create a new E2E harness or another end-to-end journey.

In `GameplayFullAutoPlay_ShouldJudgeEveryNoteAndComplete()` keep the existing Title -> Song Select -> Performance -> Result journey.

The existing Result telemetry wait is **not** screenshot readiness:

```text
StageCompleted == true
```

is published as soon as Result has a `PerformanceSummary`, roughly 1.15 seconds before `ResultRevealState.IsComplete` on an un-fast-forwarded result. Keep that assertion for its existing purpose, then independently wait for the game-owned screenshot file.

The existing happy-path `SaveScreenshotAsync()` call at Song Select remains unchanged; do not add another happy-path API screenshot during Result validation.

- [ ] **Step 2: Poll for one game-owned PNG after the existing Result telemetry assertion**

Resolve:

```text
<fixture.AppDataRoot>/Screenshots
```

Then use the existing bounded polling helper to wait up to about 10 seconds until exactly one `result-*.png` exists. The budget intentionally covers:

- up to ~1.15 seconds of Result reveal after `StageCompleted` first becomes true;
- PNG encoding on the draw thread;
- asynchronous directory/file write.

Do not replace this with an immediate `File.Exists` check or a fixed reveal-only sleep.

- [ ] **Step 3: Validate bytes and accepted one-shot behavior**

For the single file:

1. assert it is non-empty;
2. assert it begins with the PNG signature;
3. allow additional Result frames to run (a short bounded delay is enough);
4. enumerate `result-*.png` again;
5. assert the count remains exactly one.

The E2E glob is intentionally only an integration locator. Exact `Screenshots` root and `result-yyyyMMdd-HHmmss-fff.png` formatting are already pinned by `AppPathsTests`.

Do not copy the player screenshot into `fixture.ArtifactRoot` as production behavior.

- [ ] **Step 4: Preserve the existing catch-path visual diagnostic**

Keep the current `catch` behavior that calls `SaveScreenshotAsync(..., "failure-fullautoplay.png", ...)` before rethrowing.

That diagnostic runs only after the test has already failed. If it happens to contend with an automatic Result capture, it cannot create a false pass or a new test failure; the run is already doomed. The Windows CI screenshot is useful evidence for failures in the Result window and should not be removed merely to protect the automatic screenshot of an already-failed run.

No MCP/Automation production behavior changes are needed.

- [ ] **Step 5: Run the focused unit surface again**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~IStageGameContractTests|FullyQualifiedName~BaseGameTests|FullyQualifiedName~ResultStageTests"
```

Expected: PASS.

- [ ] **Step 6: Run full unit suite + game build**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

- [ ] **Step 7: Require the existing Windows live E2E before merge**

Run/verify `GameplayFullAutoPlay_ShouldJudgeEveryNoteAndComplete` through the repository’s normal E2E invocation on Windows/PR CI.

Expected evidence:

```text
<fixture.AppDataRoot>/Screenshots/result-*.png
count == 1
valid PNG bytes
count still == 1 after later Result frames
```

Do not waive this gate based only on green macOS unit tests: Task 2 proves the scheduling helper, while this E2E proves `OnDraw()` actually invokes it after eligible Result content is rendered.

- [ ] **Step 8: Minimal interactive smoke**

1. Launch the game normally.
2. Complete one song and leave the Result screen untouched until its reveal completes.
3. Confirm one new PNG exists under the platform app-data `DTXManiaCX/Screenshots` directory.
4. Leave the Result screen visible for several more seconds and confirm no additional PNG is created.
5. Complete a second song and confirm exactly one additional PNG appears.
6. Optionally occupy the Game API screenshot slot around Result and confirm the automatic screenshot retries on a later Result draw rather than permanently consuming the one-shot.
7. Confirm Result navigation remains normal even if the screenshot directory is temporarily unwritable (manual check only if convenient; automated failure coverage owns the contract).

- [ ] **Step 9: Final scope audit**

Expected production changes only:

```text
DTXMania.Game/Lib/Utilities/AppPaths.cs
DTXMania.Game/Lib/Stage/IStageGame.cs
DTXMania.Game/Game1.cs
DTXMania.Game/Lib/Stage/ResultStage.cs
```

Expected test changes only:

```text
DTXMania.Test/Utilities/AppPathsTests.cs
DTXMania.Test/Stage/IStageGameContractTests.cs
DTXMania.Test/BaseGameTests.cs
DTXMania.Test/Stage/ResultStageTests.cs
DTXMania.E2E/GameplayAutoPlaySmokeTests.cs
```

Reject any new screenshot manager/service graph, framebuffer encoder, MCP/Automation dependency, config schema, UI indicator, retry/persistence queue, retention policy, phase-state machinery, or DTXManiaNX change.

Keep implementation commits on this same draft PR.
