# HPA-16 Automatic Result Screenshot Design

## Summary

HPA-16 automatically saves one PNG after the Result screen has reached its fully revealed state.

The implementation should reuse the screenshot capture path already owned by `BaseGame`: a request stores one pending `TaskCompletionSource<byte[]?>`, `BaseGame.Draw()` renders the stage into the fixed virtual render target, then fulfills that pending request from the still-bound render target through `CapturePendingScreenshot()` / `CaptureRenderTargetAsPng()`.

The Result stage must not read the framebuffer, encode PNG data, call MCP/Automation, or create a second screenshot subsystem.

The main design correction from the original ticket wording is output location. Current CX tooling has no reusable runtime screenshot directory:

- MCP returns screenshots inline as image content;
- E2E writes screenshots to a fixture-specific `ArtifactRoot` / `DTXMANIA_E2E_ARTIFACT_ROOT`, which is test-only and intentionally outside normal application data.

For normal gameplay, HPA-16 establishes one small runtime convention instead: `<AppDataRoot>/Screenshots`, exposed by `AppPaths.GetScreenshotsRoot()`.

For this slice, “optional” means the screenshot is a non-fatal side effect rather than gameplay state. Automatic Result capture is always on; a user-facing enable/disable setting is intentionally deferred.

## Reuse survey

### Existing capture primitive

`DTXMania.Game/Game1.cs` already owns the correct render-thread capture lifecycle:

1. `IGameContext.CaptureScreenshotAsync()` atomically installs one pending screenshot request;
2. `BaseGame.Draw()` renders `StageManager.Draw(...)` into the fixed 1280x720 render target;
3. before unbinding that render target, `BaseGame.Draw()` exchanges the pending request and resolves it with `CapturePendingScreenshot(_renderTarget)`;
4. `CaptureRenderTargetAsPng()` uses MonoGame `RenderTarget2D.SaveAsPng()` and returns the PNG bytes.

This path must remain the only framebuffer-read / PNG-encoding implementation.

The current queue also exposes an important acceptance signal:

- an accepted request returns the pending `TaskCompletionSource` task, which cannot complete until `StageManager.Draw(...)` returns to `BaseGame.Draw()`;
- a request rejected because `_pendingScreenshot` is already occupied returns `Task.FromResult<byte[]?>(null)`, i.e. a synchronously completed null task.

HPA-16 can distinguish “request was never queued” from “accepted request later failed” without adding another queue API.

### Existing Result completion predicate

`DTXMania.Game/Lib/Stage/Result/ResultRevealState.cs` already defines the visual completion state:

```csharp
public bool IsComplete => ElapsedSeconds >= TotalRevealSeconds;
```

It also owns the fast-forward path through `Complete()`. `ResultStage` consumes Activate/Back while reveal is incomplete, calls `Complete()`, and only permits a later input to leave Result.

For natural reveal completion, input can begin the outgoing transition in the same update where `ElapsedSeconds` crosses `TotalRevealSeconds`. That does not make the following Result draw invalid in the current renderer: `StageManager.DrawTransition()` still calls the outgoing Result stage’s `Draw()` while fade-out alpha is positive, and the method currently does **not** apply that alpha to the rendered pixels (the fade application remains a TODO).

### Current transition-phase behavior

`BaseStage` sets `StagePhase.FadeOut` when a stage transition begins. However, current `StageManager.DrawTransition()` only uses `GetFadeOutAlpha()` to decide whether to call the outgoing stage; it does not blend or darken the stage output.

Conversely, a newly activated Result stage does not spend rendered frames in `FadeIn`: `StageManager.CompleteTransition()` activates the new stage, calls `OnTransitionIn(...)`, and immediately calls `OnTransitionComplete()`, which sets the stage to `Normal` before the next draw.

Therefore HPA-16 should **not** add `CurrentPhase == StagePhase.Normal` to the screenshot predicate. Doing so would suppress the valid outgoing Result draw when the player presses Activate/Back in the exact update that natural reveal completion occurs, potentially producing no automatic PNG at all. If transition rendering later starts applying fade alpha to stage pixels, that renderer change should revisit this contract explicitly.

### Existing output paths

`AppPaths` is the game-owned cross-platform authority for writable application data. HPA-16 should add:

```text
<AppDataRoot>/Screenshots
```

through `AppPaths.GetScreenshotsRoot()`.

`AppPaths.EnsureDirectory(path)` already owns the project’s small directory-creation primitive and should be reused by the write path. `GetScreenshotsRoot()` itself remains pure and must not create directories.

Do not reuse `E2EFixture.ArtifactRoot`: it is configurable per test run and exists for CI evidence, not player/runtime output.

## Design

### 1. Expose the existing capture queue to stages

`ResultStage` receives `IStageGame`, while the current screenshot request is exposed only through `IGameContext`.

Add a default stage-facing member:

```csharp
Task<byte[]?> CaptureScreenshotAsync() => Task.FromResult<byte[]?>(null);
```

to `IStageGame` so existing headless/test implementations remain source-compatible.

In `BaseGame`, extract the current `IGameContext.CaptureScreenshotAsync()` body into one private queue helper and have both explicit interface implementations forward to it:

```text
IGameContext.CaptureScreenshotAsync ─┐
                                     ├─> one QueueScreenshotCapture() implementation
IStageGame.CaptureScreenshotAsync  ──┘
```

There must still be exactly one `_pendingScreenshot` slot and one render-target encoder.

Do not make `IStageGame` inherit `IGameContext`; their existing manager contracts differ and widening the whole stage interface graph is unnecessary for one capability.

Two tests pin the two different contracts:

- `IStageGameContractTests` exercises the new default method on `MinimalStageGameStub` and proves an implementation that does not override the optional capability receives null without breaking existing stubs;
- `BaseGameTests` calls the member through `IStageGame` and proves `BaseGame` overrides/forwards it into the real shared `_pendingScreenshot` slot. This prevents a forgotten `BaseGame` forward from silently using the default no-op.

Do not add `CaptureScreenshotAsync` to the existing reflection-only “interface declares methods” assertion. The executable DIM test already references the member directly, while `BaseGameTests` owns the behavior that can silently fail at runtime.

### 2. Trigger from Result Draw, not Update

The screenshot request must be queued at the end of the Result draw after the fully revealed content has rendered.

Requesting it from `ResultStage.OnUpdate()` is subtly incorrect: input handling in that same update can transition away from Result before the next draw, allowing a pending request to capture a later stage.

The safe ordering is:

```text
ResultStage.OnDraw
  -> draw Result content
  -> SpriteBatch.End()
  -> TryScheduleResultScreenshot()
BaseGame.Draw resumes
  -> fulfill accepted pending request from the still-bound Result render target
  -> unbind + letterbox blit
```

`TryScheduleResultScreenshot()` is the single scheduling helper used by both production `OnDraw()` and focused unit tests. Do not mark that helper `[ExcludeFromCodeCoverage]`; only the graphics-bound `OnDraw()` remains excluded/headless-unfriendly.

Do not phase-gate this helper with `CurrentPhase == StagePhase.Normal`. Current transition rendering still draws the outgoing Result pixels unchanged, and that draw is load-bearing for the exact natural-reveal-completion + exit-input edge case described above.

### 3. One accepted request per Result activation

Add one stage-lifetime flag:

```text
_resultScreenshotRequested
```

Reset it to `false` in `OnActivate()` because `StageManager` caches stage instances across plays.

`TryScheduleResultScreenshot()` returns unless:

- `_revealState?.IsComplete == true`; and
- `_resultScreenshotRequested == false`.

When eligible, call `_game.CaptureScreenshotAsync()` synchronously while still inside `ResultStage.OnDraw()`.

For the concrete `BaseGame` contract:

```csharp
var captureTask = _game.CaptureScreenshotAsync();
if (captureTask.IsCompletedSuccessfully && captureTask.Result is null)
{
    // The shared slot was already occupied; this request was never queued.
    // Leave the one-shot flag false so a later Result draw can try again.
    return;
}

_resultScreenshotRequested = true;
```

Use the precise “completed successfully with null” shape rather than `IsCompleted` alone. A synchronously faulted task or a hypothetical synchronously completed non-null implementation is an actual attempt/result and must not be mislabeled as queue contention.

Set `_resultScreenshotRequested = true` before asynchronous observation/persistence for every request that was not rejected as the BaseGame busy-null sentinel. Any later capture/encoding/filesystem failure is a consumed attempt and must not retry.

This is not a retry queue: HPA-16 only retries when the automatic request was never accepted by the existing one-slot queue.

### 4. Persist asynchronously and fail closed

The Result draw path must not block on PNG capture or disk I/O.

Keep queue acceptance in `TryScheduleResultScreenshot()` and pass the already-returned task into one internal virtual persistence seam, for example:

```csharp
CaptureAndSaveResultScreenshotAsync(Task<byte[]?> captureTask)
```

The production implementation should:

1. await the already-queued `captureTask`;
2. treat null/empty bytes as a non-fatal failed accepted attempt;
3. resolve `AppPaths.GetScreenshotsRoot()`;
4. call `AppPaths.EnsureDirectory(root)`;
5. build the final path through `AppPaths.BuildResultScreenshotPath(root, DateTime.Now)`;
6. write the PNG bytes asynchronously.

A private safe wrapper should catch/log all exceptions so the fire-and-forget task cannot become an unobserved failure. Follow the existing Result-stage pattern for observing asynchronous failures and obtain the logger through `game.LoggerFactory`; no UI message is required.

If `_game.CaptureScreenshotAsync()` itself throws synchronously, catch/log that failure at the scheduler boundary and consume the one-shot rather than creating an infinite retry loop. The concrete BaseGame queue is not expected to throw during normal operation.

No cancellation token is needed. Once an accepted stable Result capture exists, a subsequent stage transition should not cancel the file write.

### 5. Filename contract

Use a simple local-time timestamp:

```text
result-yyyyMMdd-HHmmss-fff.png
```

Keep filename construction as a tiny pure/testable helper, for example:

```csharp
internal static string BuildResultScreenshotPath(string screenshotsRoot, DateTime timestamp)
    => Path.Combine(screenshotsRoot, $"result-{timestamp:yyyyMMdd-HHmmss-fff}.png");
```

This keeps the exact directory/filename contract out of the async Result logic and lets a fixed timestamp unit test pin it. `GetScreenshotsRoot()` remains a pure getter and must not create directories.

Do not include song titles in filenames: sanitizing arbitrary chart metadata is unrelated work. Do not add retention, collision-management, or a screenshot index in this ticket.

Millisecond collisions are outside the practical one-result-at-a-time game flow; a failed write is already non-fatal by contract.

## Failure behavior

The automatic screenshot is optional evidence, never gameplay state.

The behaviors are intentionally different for queue rejection versus accepted-request failure:

- if another screenshot request already occupies `_pendingScreenshot`, BaseGame returns a synchronously completed null task; HPA-16 does **not** consume the one-shot and may retry on the next Result draw;
- if an accepted capture later resolves null/empty, throws, cannot encode, cannot create the directory, or cannot write the PNG, the failure is logged and the one-shot remains consumed;
- no failure changes Result interaction/progression or creates a persistence/retry queue.

## Test strategy

### `IStageGameContractTests`

Pin the new default-interface-member behavior next to the existing startup/CrashReportInbox DIM tests:

- `MinimalStageGameStub` requires no new implementation;
- `CaptureScreenshotAsync()` through that stub completes with null.

Do not extend the reflection declaration assertion for this member; the executable DIM test is the useful contract.

### `BaseGameTests`

Pin reuse and the concrete forward:

- a screenshot requested through `IStageGame` installs the same `_pendingScreenshot` task used by `IGameContext`;
- while that request is pending, a request through the other interface returns the existing synchronously completed null “busy” result.

Existing tests continue to own render-target fulfillment and unavailable-device behavior.

### `AppPathsTests`

Add two pure contracts:

- `GetScreenshotsRoot()` is rooted and equals the `Screenshots` child of `GetAppDataRoot()` / the configured `DTXMANIA_APPDATA_ROOT`;
- a fixed timestamp passed to `BuildResultScreenshotPath()` yields the exact expected `result-yyyyMMdd-HHmmss-fff.png` path.

The second assertion ensures later E2E use of `result-*.png` cannot be the only protection for filename drift.

### `ResultStageTests`

Reuse the existing `InspectableResultStage` seam and add only the screenshot behavior needed for HPA-16:

- incomplete reveal -> no request;
- complete reveal + accepted pending capture task -> one accepted attempt;
- repeated eligible draws after acceptance -> still one attempt;
- synchronously completed null capture task (slot busy) -> no accepted attempt / flag remains false, then a later draw can accept once the fake returns a pending task;
- accepted capture/persist failure -> swallowed/logged and no retry;
- `OnActivate()` resets the one-shot state for the next Result activation.

Call the same `TryScheduleResultScreenshot()` helper that `OnDraw()` uses. Do not construct a real `GraphicsDevice` just to execute the `[ExcludeFromCodeCoverage]` draw method.

### Existing gameplay E2E

Extend `GameplayFullAutoPlay_ShouldJudgeEveryNoteAndComplete()` instead of creating another live-game harness.

`ResultStage.PopulateTelemetry()` sets `StageCompleted = true` as soon as the performance summary exists, before the ~1.15 second reveal completes. Therefore **do not** interpret the existing `StageCompleted` wait as “screenshot is due.” Keep that wait for the existing gameplay assertion, then independently poll the game-owned screenshot directory until the automatic file appears.

Because the fixture already launches with `DTXMANIA_APPDATA_ROOT = fixture.AppDataRoot`, the game should create:

```text
<fixture.AppDataRoot>/Screenshots/result-*.png
```

After entering Result:

1. keep the existing Result/`StageCompleted` telemetry assertion;
2. poll for up to roughly 10 seconds until exactly one `result-*.png` exists (comfortably covering reveal + PNG encode + write);
3. verify the file is non-empty and has the PNG signature;
4. allow additional Result frames to run, then re-enumerate and assert the count remains one.

The happy path should not add a new API `takeScreenshot` call during this Result validation. Preserve the existing catch-path `SaveScreenshotAsync()` diagnostic: it runs only after the test has already failed, so any slot collision cannot create a false passing/failing outcome, and the visual artifact is valuable for Windows CI diagnosis.

The E2E project targets `net8.0-windows7.0`, so this existing live E2E is a mandatory Windows PR-CI gate for merge. The macOS unit suite proves the pure/state-machine pieces but cannot substitute for the graphics-bound `OnDraw()` wiring.

## Expected implementation files

Production:

- `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- `DTXMania.Game/Lib/Stage/IStageGame.cs`
- `DTXMania.Game/Game1.cs`
- `DTXMania.Game/Lib/Stage/ResultStage.cs`

Tests:

- `DTXMania.Test/Utilities/AppPathsTests.cs`
- `DTXMania.Test/Stage/IStageGameContractTests.cs`
- `DTXMania.Test/BaseGameTests.cs`
- `DTXMania.Test/Stage/ResultStageTests.cs`
- `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`

No MCP or `DTXMania.Automation` production file should change.

## Rejected alternatives

### Capture during Result Update

Rejected because a same-update stage transition can occur before the capture is fulfilled, producing pixels from a later stage rather than the Result frame that just rendered.

### Require `CurrentPhase == StagePhase.Normal`

Rejected for current HPA-16. Today `StageManager.DrawTransition()` does not apply fade alpha to outgoing stage pixels; it draws Result unchanged while fade-out alpha is positive. A Normal-only guard would instead lose the only eligible draw when natural reveal completion and exit input happen in the same update. Revisit only if transition rendering later starts blending/fading the actual stage pixels.

### Treat a busy screenshot slot as a consumed attempt

Rejected because BaseGame distinguishes this cheaply: the busy branch returns a synchronously completed null task, meaning the automatic request was never queued. Leave the one-shot false and retry on a later Result draw; accepted capture/write failures still do not retry.

### Call MCP/Automation from gameplay

Rejected because those are external consumers of the game API. Gameplay must not depend upward on test/tooling layers, and MCP currently returns screenshot data inline rather than defining a runtime save directory.

### Reuse `E2EArtifactWriter`

Rejected because `ArtifactRoot` is a per-fixture CI evidence path, not a normal runtime path.

### Add a ScreenshotManager / queue / background worker

Rejected as unnecessary. `BaseGame` already has the one pending request queue and correct draw-thread capture point; HPA-16 only needs to expose and consume it.

## Out of scope

- manual screenshot hotkeys;
- screenshot UI/toast/notification;
- a screenshot enable/disable config option;
- a new retry or persistence queue (retrying an unaccepted busy-slot request on a later Result draw is allowed);
- screenshot gallery/history/index;
- retention or cleanup policy;
- song-title-based filenames;
- changing PNG resolution or letterbox behavior;
- changing MCP screenshot output;
- changing E2E artifact-root semantics;
- DTXManiaNX changes.

## Acceptance

HPA-16 is complete when:

- a normal completed play writes exactly one `result-yyyyMMdd-HHmmss-fff.png` to `<AppDataRoot>/Screenshots` after the Result reveal is complete;
- fast-forwarding the reveal still produces one Result screenshot before the player can leave on a later input;
- repeated Result draws after an accepted request do not create more files;
- a busy shared screenshot slot does not consume the automatic one-shot; a later eligible Result draw can retry once the slot is free;
- capture still flows through the existing `_pendingScreenshot` / `CapturePendingScreenshot()` / `CaptureRenderTargetAsPng()` implementation;
- `IStageGame` default behavior and `BaseGame`’s explicit stage-interface forward are both unit-pinned;
- the screenshot root and exact fixed-timestamp filename construction are unit-pinned;
- accepted capture or filesystem failure is logged and never blocks Result interaction or stage progression;
- the existing Windows full-AutoPlay E2E proves the actual Result Draw wiring while preserving the existing catch-path diagnostic screenshot;
- MCP/Automation and E2E artifact-path ownership remain unchanged.
