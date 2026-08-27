# HPA-16 Automatic Result Screenshot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` and execute this plan task-by-task. Keep all implementation on this same PR.

**Goal:** Save exactly one PNG of the fully revealed Result frame to the game’s app-data `Screenshots` directory without adding a second framebuffer/PNG pipeline or affecting Result progression when capture fails.

**Architecture:** Reuse `BaseGame`’s existing pending screenshot request and `RenderTarget2D.SaveAsPng()` fulfillment. Expose that same queue through `IStageGame`, request capture at the end of a stable Result draw, then persist the returned bytes asynchronously to `AppPaths.GetScreenshotsRoot()`.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, existing DTXMania E2E/JSON-RPC harness.

**Spec:** `docs/superpowers/specs/2026-08-26-hpa-16-automatic-result-screenshot-design.md`

## Global constraints

- One PR for planning + implementation.
- Reuse the existing `_pendingScreenshot` / `CapturePendingScreenshot()` / `CaptureRenderTargetAsPng()` path.
- Trigger from stable Result **Draw**, not Update.
- One automatic attempt per Result activation, even after failure.
- Runtime output is `<AppDataRoot>/Screenshots`; do not reuse E2E `ArtifactRoot`.
- No MCP/Automation production dependency.
- No config toggle, UI toast, retry queue, screenshot manager, retention policy, or song-title filename work.
- Screenshot failure is warning-only and must never block Result interaction/progression.

---

### Task 1: Expose the existing capture queue and runtime screenshot path

**Files:**
- Modify: `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Test/Utilities/AppPathsTests.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`

**Produces:** a stage-facing request that is demonstrably the same screenshot queue already used by the Game API, plus one game-owned writable screenshot directory.

- [ ] **Step 1: Pin the runtime path contract**

Add a focused `AppPathsTests` assertion first:

```text
GetScreenshotsRoot()
  is rooted
  filename/last directory segment == "Screenshots"
  parent == AppPaths.GetAppDataRoot()
```

Expected on current main: FAIL because the method does not exist.

- [ ] **Step 2: Add the smallest `AppPaths` helper**

In `AppPaths.cs`, add:

```csharp
public static string GetScreenshotsRoot()
{
    return Path.GetFullPath(Path.Combine(GetAppDataRoot(), "Screenshots"));
}
```

Do not create the directory in the getter. Creation belongs to the write operation, matching the other path helpers.

- [ ] **Step 3: Pin stage/API capture reuse before changing production**

In `BaseGameTests`, add coverage that requests a screenshot through the stage contract and proves it uses the existing pending slot:

1. create the existing headless `BaseGame` test instance;
2. cast to `IStageGame` and request a screenshot;
3. assert `_pendingScreenshot` is populated and its task is the returned task;
4. while it is still pending, call `((IGameContext)game).CaptureScreenshotAsync()`;
5. assert the second request completes with null, proving both interfaces share the same single pending slot.

Do not add a new render-target encoder test; existing BaseGame coverage already owns pending capture fulfillment and unavailable-device behavior.

- [ ] **Step 4: Expose capture through `IStageGame` without widening the interface graph**

In `IStageGame`, add a default method returning null for headless/test implementations:

```csharp
Task<byte[]?> CaptureScreenshotAsync() => Task.FromResult<byte[]?>(null);
```

Add the required `System.Threading.Tasks` import.

Do **not** make `IStageGame : IGameContext`.

- [ ] **Step 5: Refactor BaseGame to one queue helper**

Move the existing body of explicit `IGameContext.CaptureScreenshotAsync()` into one private helper, e.g. `QueueScreenshotCapture()`.

Both explicit interfaces must forward to it:

```text
IGameContext.CaptureScreenshotAsync() -> QueueScreenshotCapture()
IStageGame.CaptureScreenshotAsync()  -> QueueScreenshotCapture()
```

Keep all existing behavior unchanged:

- `Interlocked.CompareExchange` still permits only one pending request;
- a second request returns a completed null task;
- `BaseGame.Draw()` still owns capture fulfillment;
- `CaptureRenderTargetAsPng()` stays the only PNG encoder.

- [ ] **Step 6: Run focused Task 1 tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~BaseGameTests"
```

Expected: PASS.

---

### Task 2: Schedule one stable Result capture and make failure non-fatal

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Modify: `DTXMania.Test/Stage/ResultStageTests.cs`

**Produces:** one asynchronous save attempt after the fully revealed Result frame has actually rendered.

- [ ] **Step 1: Extend the existing Result test seam, not the harness**

Reuse `ResultStageTests.InspectableResultStage`.

Add one overrideable core seam on production `ResultStage`:

```text
CaptureAndSaveResultScreenshotAsync()
```

The inspectable test subclass should be able to count calls and optionally return/throw a failed task without constructing a live `GraphicsDevice` or touching the real filesystem.

Do not add a new Result test fixture or screenshot service abstraction.

- [ ] **Step 2: Add characterization tests for the one-shot state machine**

Test the extracted scheduling helper directly.

Required cases:

1. reveal incomplete -> zero attempts;
2. reveal complete -> one attempt;
3. invoke again while still complete -> still one attempt;
4. capture/persist throws -> safe wrapper observes/logs it and a later stable-frame check still does not retry;
5. `OnActivate()` resets the attempt flag for a new Result activation.

The tests should pin the contract, not private task plumbing or exact log text.

- [ ] **Step 3: Add the one-shot fields and reset**

In `ResultStage` add only the state needed by the behavior:

```text
_resultScreenshotRequested
```

Initialize/reset it to `false` in `OnActivate()`.

A separate retry count, queue, cancellation source, or screenshot lifecycle enum is unnecessary.

- [ ] **Step 4: Trigger after Result rendering**

At the end of `OnDraw()`, after the Result renderer/fallback has drawn and `_spriteBatch.End()` has completed, invoke the scheduling helper.

The helper should return unless:

```text
_revealState?.IsComplete == true
&& !_resultScreenshotRequested
```

Set `_resultScreenshotRequested = true` before launching the async work.

Do not put this trigger in `OnUpdate()`: input can transition the stage later in the same update, causing the next draw to belong to Song Select.

- [ ] **Step 5: Implement capture + persistence through existing ownership**

Production `CaptureAndSaveResultScreenshotAsync()` should:

1. call `_game.CaptureScreenshotAsync()` immediately;
2. await the returned PNG bytes;
3. return quietly (warning optional) if bytes are null/empty;
4. resolve `AppPaths.GetScreenshotsRoot()`;
5. create the directory;
6. write `result-yyyyMMdd-HHmmss-fff.png` asynchronously.

The request is issued during `ResultStage.OnDraw()`. When control returns to `BaseGame.Draw()`, the existing pending-request logic captures the still-bound Result render target from that same frame.

Use local time for the timestamp so files are human-browsable. Do not include song metadata in the filename.

- [ ] **Step 6: Wrap the fire-and-forget task safely**

The draw method must never synchronously wait for capture or disk I/O.

Use a private safe async wrapper that catches all exceptions from `CaptureAndSaveResultScreenshotAsync()` and logs one warning via the existing `game.LoggerFactory` infrastructure. Discard only that safe wrapper task.

The one-shot flag remains set after any failure. Do not retry on later Result frames.

- [ ] **Step 7: Run focused Result tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ResultStageTests"
```

Expected: PASS.

---

### Task 3: Prove the real game writes exactly one PNG and close the slice

**Files:**
- Modify: `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`

**Produces:** black-box proof that the production Result draw, BaseGame capture queue, PNG encoding, app-data path, and one-shot behavior work together.

- [ ] **Step 1: Extend the existing full-AutoPlay smoke**

Do not create a new E2E harness or another end-to-end journey.

In `GameplayFullAutoPlay_ShouldJudgeEveryNoteAndComplete()`:

1. keep the existing Title -> Song Select -> Performance -> Result journey;
2. after Result is reached, resolve `Path.Combine(fixture.AppDataRoot, "Screenshots")`;
3. poll until exactly one `result-*.png` appears;
4. assert the file is non-empty and begins with the PNG signature;
5. allow additional Result frames to run;
6. enumerate again and assert there is still exactly one matching file.

The fixture already maps `DTXMANIA_APPDATA_ROOT` to `fixture.AppDataRoot`, so this exercises the production `AppPaths` contract without test-only path injection.

Do not copy the automatic screenshot into `fixture.ArtifactRoot` as part of production behavior. CI can collect it separately later if desired; HPA-16 is about the game-owned runtime path.

- [ ] **Step 2: Run the focused unit surface again**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~BaseGameTests|FullyQualifiedName~ResultStageTests"
```

Expected: PASS.

- [ ] **Step 3: Run full unit suite + game build**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

Windows remains owned by PR CI.

- [ ] **Step 4: Run/verify the existing live E2E where supported**

Use the repository’s normal E2E invocation and filter to `GameplayFullAutoPlay_ShouldJudgeEveryNoteAndComplete` when running locally. If local platform prerequisites are unavailable, PR CI is the authoritative live-game gate; do not build a second harness to compensate.

Expected evidence:

```text
<fixture.AppDataRoot>/Screenshots/result-*.png
count == 1
valid PNG bytes
```

- [ ] **Step 5: Minimal interactive smoke**

1. Launch the game normally.
2. Complete one song and leave the Result screen untouched until its reveal completes.
3. Confirm one new PNG exists under the platform app-data `DTXManiaCX/Screenshots` directory.
4. Leave the Result screen visible for several more seconds and confirm no additional PNG is created.
5. Complete a second song and confirm exactly one additional PNG appears.
6. Confirm Result navigation remains normal even if the screenshot directory is temporarily unwritable (manual check only if convenient; automated failure coverage owns the contract).

- [ ] **Step 6: Final scope audit**

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
DTXMania.Test/BaseGameTests.cs
DTXMania.Test/Stage/ResultStageTests.cs
DTXMania.E2E/GameplayAutoPlaySmokeTests.cs
```

Reject any new screenshot manager/service graph, framebuffer encoder, MCP/Automation dependency, config schema, UI indicator, retry queue, retention policy, or DTXManiaNX change.

Keep implementation commits on this same draft PR.
