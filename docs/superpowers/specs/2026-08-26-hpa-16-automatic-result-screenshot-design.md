# HPA-16 Automatic Result Screenshot Design

## Summary

HPA-16 automatically saves one PNG after the Result screen has reached its fully revealed state.

The implementation should reuse the screenshot capture path already owned by `BaseGame`: a request stores one pending `TaskCompletionSource<byte[]?>`, `BaseGame.Draw()` renders the stage into the fixed virtual render target, then fulfills that pending request from the still-bound render target through `CapturePendingScreenshot()` / `CaptureRenderTargetAsPng()`.

The Result stage must not read the framebuffer, encode PNG data, call MCP/Automation, or create a second screenshot subsystem.

The main design correction from the original ticket wording is output location. Current CX tooling has no reusable runtime screenshot directory:

- MCP returns screenshots inline as image content;
- E2E writes screenshots to a fixture-specific `ArtifactRoot` / `DTXMANIA_E2E_ARTIFACT_ROOT`, which is test-only and intentionally outside normal application data.

For normal gameplay, HPA-16 establishes one small runtime convention instead: `<AppDataRoot>/Screenshots`, exposed by `AppPaths.GetScreenshotsRoot()`.

## Reuse survey

### Existing capture primitive

`DTXMania.Game/Game1.cs` already owns the correct render-thread capture lifecycle:

1. `IGameContext.CaptureScreenshotAsync()` atomically installs one pending screenshot request;
2. `BaseGame.Draw()` renders `StageManager.Draw(...)` into the fixed 1280x720 render target;
3. before unbinding that render target, `BaseGame.Draw()` exchanges the pending request and resolves it with `CapturePendingScreenshot(_renderTarget)`;
4. `CaptureRenderTargetAsPng()` uses MonoGame `RenderTarget2D.SaveAsPng()` and returns the PNG bytes.

This path must remain the only framebuffer-read / PNG-encoding implementation.

### Existing Result completion predicate

`DTXMania.Game/Lib/Stage/Result/ResultRevealState.cs` already defines the stable visual state:

```csharp
public bool IsComplete => ElapsedSeconds >= TotalRevealSeconds;
```

It also owns the fast-forward path through `Complete()`. `ResultStage` already consumes the first Activate/Back while reveal is incomplete, calls `Complete()`, and only permits a later input to leave Result. That guarantees a draw opportunity for the completed Result frame without another timer or renderer-derived predicate.

### Existing output paths

`AppPaths` is the game-owned cross-platform authority for writable application data. HPA-16 should add:

```text
<AppDataRoot>/Screenshots
```

through `AppPaths.GetScreenshotsRoot()`.

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

- `IStageGameContractTests` exercises the default method on `MinimalStageGameStub` and proves an implementation that does not override the new optional capability receives null without breaking existing stubs;
- `BaseGameTests` calls the member through `IStageGame` and proves `BaseGame` overrides/forwards it into the real shared `_pendingScreenshot` slot. This test is what prevents a forgotten `BaseGame` forward from silently using the default no-op.

### 2. Trigger from Result Draw, not Update

The screenshot request must be queued at the end of a stable Result draw.

Requesting it from `ResultStage.OnUpdate()` is subtly incorrect: input handling in that same update can transition away from Result before the next draw, allowing the pending request to capture Song Select instead.

The safe ordering is:

```text
ResultStage.OnDraw
  -> draw stable Result content
  -> SpriteBatch.End()
  -> TryScheduleResultScreenshot()
BaseGame.Draw resumes
  -> fulfill pending request from the still-bound Result render target
  -> unbind + letterbox blit
```

`TryScheduleResultScreenshot()` is the single scheduling helper used by both production `OnDraw()` and focused unit tests. Do not mark that helper `[ExcludeFromCodeCoverage]`; only the graphics-bound `OnDraw()` remains excluded/headless-unfriendly.

### 3. One request per Result activation

Add one stage-lifetime flag:

```text
_resultScreenshotRequested
```

Reset it to `false` in `OnActivate()`.

`TryScheduleResultScreenshot()` returns unless:

- `_revealState?.IsComplete == true`; and
- `_resultScreenshotRequested == false`.

Set the flag to `true` **before** starting the asynchronous capture/persist operation. A capture or filesystem failure must not cause repeated requests every later Result frame.

The flag means “automatic screenshot attempt already started”, not “file write succeeded”. This matches the ticket’s no-retry requirement.

### 4. Persist asynchronously and fail closed

The Result draw path must not block on PNG capture or disk I/O.

Use one internal virtual `CaptureAndSaveResultScreenshotAsync()` seam on `ResultStage` for focused tests. The production implementation should:

1. await `_game.CaptureScreenshotAsync()`;
2. treat null/empty bytes as a non-fatal failed attempt;
3. resolve `AppPaths.GetScreenshotsRoot()`;
4. create that directory;
5. build the final path through `AppPaths.BuildResultScreenshotPath(root, DateTime.Now)`;
6. write the PNG bytes asynchronously.

A private safe wrapper should catch/log all exceptions so the fire-and-forget task cannot become an unobserved failure. Follow the existing Result-stage pattern for observing asynchronous failures and obtain the logger through `game.LoggerFactory`; no UI message is required.

No cancellation token is needed. Once the stable Result frame has requested a screenshot, a subsequent stage transition should not cancel the already-captured file write.

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

The following all log and continue without changing Result interaction/progression:

- another screenshot request is already pending and the shared capture queue returns null;
- graphics are unavailable and capture resolves null;
- render-target capture throws;
- the Screenshots directory cannot be created;
- the PNG cannot be written.

There is no retry queue and the one-shot flag remains set after failure.

## Test strategy

### `IStageGameContractTests`

Pin the new default-interface-member behavior next to the existing startup/CrashReportInbox DIM tests:

- `MinimalStageGameStub` requires no new implementation;
- `CaptureScreenshotAsync()` through that stub completes with null;
- the interface declaration test includes `CaptureScreenshotAsync`.

This is a source-compatibility/default-behavior contract. It complements rather than replaces the BaseGame forwarding test.

### `BaseGameTests`

Pin reuse and the concrete forward:

- a screenshot requested through `IStageGame` installs the same `_pendingScreenshot` task used by `IGameContext`;
- while that request is pending, a request through the other interface returns the existing “busy” null result.

Existing tests continue to own render-target fulfillment and unavailable-device behavior.

### `AppPathsTests`

Add two pure contracts:

- `GetScreenshotsRoot()` is rooted and equals the `Screenshots` child of `GetAppDataRoot()` / the configured `DTXMANIA_APPDATA_ROOT`;
- a fixed timestamp passed to `BuildResultScreenshotPath()` yields the exact expected `result-yyyyMMdd-HHmmss-fff.png` path.

The second assertion ensures later E2E use of `result-*.png` cannot be the only protection for filename drift.

### `ResultStageTests`

Reuse the existing `InspectableResultStage` seam and add only the screenshot behavior needed for HPA-16:

- incomplete reveal -> no screenshot attempt;
- complete reveal -> one attempt;
- repeated stable-frame checks -> still one attempt;
- failed capture/persist task -> swallowed/logged and no retry;
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

1. do not invoke `SaveScreenshotAsync()` / `takeScreenshot` while validating the automatic Result capture, because it shares the same BaseGame pending slot;
2. keep the existing Result/`StageCompleted` telemetry assertion;
3. poll for up to roughly 10 seconds until exactly one `result-*.png` exists (comfortably covering reveal + PNG encode + write);
4. verify the file is non-empty and has the PNG signature;
5. allow additional Result frames to run, then re-enumerate and assert the count remains one.

If the automatic-file assertion fails after Result has been entered, rely on process logs/artifacts rather than issuing a competing API screenshot from the test catch path.

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

Rejected because a same-update stage transition can occur before the capture is fulfilled, producing pixels from the next stage rather than the stable Result frame.

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
- retry or persistence queues;
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
- fast-forwarding the reveal still produces one stable Result screenshot before the player can leave on a later input;
- repeated Result frames do not create more files;
- capture still flows through the existing `_pendingScreenshot` / `CapturePendingScreenshot()` / `CaptureRenderTargetAsPng()` implementation;
- `IStageGame` default behavior and `BaseGame`’s explicit stage-interface forward are both unit-pinned;
- the screenshot root and exact fixed-timestamp filename construction are unit-pinned;
- capture or filesystem failure is logged and never blocks Result interaction or stage progression;
- the existing Windows full-AutoPlay E2E proves the actual Result Draw wiring without issuing a competing API screenshot in the Result validation window;
- MCP/Automation and E2E artifact-path ownership remain unchanged.
