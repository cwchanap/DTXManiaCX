# HPA-501 Reusable CX Automation Foundation Design

**Issue:** [HPA-501](https://linear.app/cwchanap/issue/HPA-501/extract-minimal-reusable-cx-process-and-json-rpc-helpers)  
**Date:** 2026-08-10  
**Status:** Revised after design review

## Context

HPA-500's recorder roadmap starts by reusing process and Game API support already proven by `DTXMania.E2E`. HPA-501 is an unblocked prerequisite for HPA-503.

The current behavior is useful but owned by the Windows-targeted E2E assembly:

- `DTXMania.E2E/Process/GameProcessDriver.cs` owns `dotnet run`, environment setup, stdout/stderr capture, exit waiting, and process-tree cleanup, but accepts `E2EFixture` directly.
- `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs` handles health, `getGameState`, screenshots, key/MIDI input, stage change, and JSON-RPC errors, but references the game assembly's `InputType` and E2E telemetry DTO.
- `DTXMania.E2E/Telemetry/E2EGameState.cs` is the consumer-side telemetry projection.
- `DTXMania.E2E/Support/Eventually.cs` already provides bounded polling.
- `DTXMania.E2E/Fixtures/E2EGameProject.cs` contains the platform project-selection policy, while two smoke tests duplicate that selection instead of calling it.
- `DTXMania.E2E.csproj` targets `net8.0-windows7.0` and conditionally references a MonoGame platform project.

There is one additional proven behavior outside E2E that HPA-501 should reuse: `MCP/Server/GameInteractionService.cs` does not accept any successful `/health` response as readiness. It parses `processId` and `launchToken` from `/health` and waits until the response identifies the process it just launched. This prevents a stale CX process already bound to the same endpoint from satisfying startup.

HPA-501 should extract these proven primitives and identity semantics, not redesign automation.

## Goals

- Add a small `DTXMania.Automation` library targeting plain `net8.0`.
- Keep the library free of references to `DTXMania.E2E`, `DTXMania.Game.Windows`, `DTXMania.Game.Mac`, and MCP.
- Support explicit Windows and macOS launch identities without pretending the library selects a RID or architecture.
- Support either `dotnet run --project <csproj>` or a caller-supplied executable path; do not guess publish/RID/configuration paths.
- Preserve owned-process stdout/stderr capture, duplicate-start rejection, bounded exit waiting, and process-tree cleanup.
- Preserve the launch token in `GameProcessStartOptions` and use `/health` `launchToken`/`processId` identity during startup readiness, following the existing MCP behavior.
- Fail startup immediately when the owned process exits before becoming ready.
- Preserve the current narrow Game API client behavior: health, `getGameState`, screenshot, key/MIDI input, stage change, and private JSON-RPC request transport.
- Make Game API endpoint/key explicit connection inputs rather than relying on caller-configured `HttpClient.BaseAddress`.
- Keep wire contracts independent from game assembly types while retaining E2E producer/consumer contract tests.
- Move reusable support tests into a platform-neutral `DTXMania.Automation.Tests` project and run them on Windows and macOS CI.
- Migrate existing E2E smoke tests to consume the extracted helpers without changing gameplay behavior or artifact contracts.
- Centralize E2E-only launch environment policy so simulated MIDI is always explicitly enabled or removed instead of depending on parent-process inheritance.

## Non-goals

- OBS, FFmpeg, recording sandbox, recorder CLI, output publishing, or video validation.
- Prepared-chart, preview-control, or Song Select commands from HPA-510.
- A game-wide automation session, capture session, workflow state machine, service container, or dependency-injection layer.
- A generic process runner or generic HTTP/JSON-RPC framework.
- Depending on `MCP/Server/JsonRpcClient.cs` or any MCP project type.
- Retargeting all of `DTXMania.E2E` to plain `net8.0`; E2E still directly tests game producer types and Windows gameplay flows.
- Removing the E2E fixture builder, artifact writer, persistence checks, or test-specific game-project policy.
- Changing Game API server contracts or numeric `InputType` wire values.
- Adding macOS gameplay E2E. HPA-501 only makes reusable support build/test cross-platform.
- Backward-compatible wrappers for old E2E helper namespaces.
- API-key diagnostic sanitization. The Game API is loopback/local for this workflow and the client does not put the key in request bodies; HPA-503 remains responsible for not writing keys into its own diagnostics.

## Approaches Considered

### 1. Extract proven primitives and reuse MCP health identity — selected

Move the existing process driver, JSON-RPC client, telemetry projection, and polling helper into `DTXMania.Automation`. Replace only test/game-specific dependencies with small automation-owned contracts. Port MCP's `/health` identity comparison into the reusable client/startup path without adding an MCP dependency.

This gives HPA-503 a production-safe dependency while keeping ownership obvious.

### 2. Add an `AutomationSession` facade — rejected

A process+HTTP facade would make HPA-501 own lifecycle orchestration that belongs to HPA-503's recorder workflow. It would introduce a second lifecycle owner before the recorder has proven that abstraction is useful.

### 3. Multi-target or source-link E2E — rejected

Keeping reusable code inside the test project preserves confusing ownership and still couples the recorder to E2E project structure.

## Chosen Architecture

### 1. Plain automation library and tests

Create:

```text
DTXMania.Automation/DTXMania.Automation.csproj
DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

Both target plain `net8.0` with nullable reference types and implicit usings enabled.

`DTXMania.Automation` has no project references to game, E2E, or MCP assemblies.

`DTXMania.Automation.Tests` references only `DTXMania.Automation` plus the same test packages already used by `DTXMania.E2E` on current `main`:

```text
Microsoft.NET.Test.Sdk       18.8.1
xunit                        2.9.3
xunit.runner.visualstudio    3.1.5
```

Add both projects to `DTXMania.sln`.

`DTXMania.E2E` keeps `net8.0-windows7.0` and its game-project reference because unrelated tests still consume game/EF/producer types. It adds a reference to `DTXMania.Automation`.

### 2. Explicit launch target and start options

Use names that describe what HPA-501 actually selects. The project path itself is architecture-agnostic; this ticket does not select a RID.

```csharp
namespace DTXMania.Automation.Process;

public enum GamePlatform
{
    Windows,
    Mac
}

public enum GameLaunchKind
{
    Project,
    Executable
}

public sealed record GameLaunchTarget(
    GamePlatform Platform,
    GameLaunchKind Kind,
    string Path)
{
    public static GameLaunchTarget Project(
        GamePlatform platform,
        string? projectPathOverride = null);

    public static GameLaunchTarget Executable(
        GamePlatform platform,
        string executablePath);
}

public sealed record GameProcessStartOptions(
    string WorkingDirectory,
    GameLaunchTarget Target,
    string AppDataRoot,
    string LaunchToken,
    IReadOnlyDictionary<string, string?>? EnvironmentOverrides = null);
```

Default project mappings:

```text
Windows -> DTXMania.Game/DTXMania.Game.Windows.csproj
Mac     -> DTXMania.Game/DTXMania.Game.Mac.csproj
```

`Project(...)` accepts an override so E2E can continue honoring `DTXMANIA_E2E_GAME_PROJECT` without putting that test-only environment variable in Automation.

`Executable(...)` uses the exact caller path. It does not infer a RID, architecture, publish layout, app bundle, or configuration.

Commands are derived only from launch kind:

```text
Project    -> dotnet run --project <Target.Path>
Executable -> <Target.Path>
```

No shell is used.

### 3. `GameProcessDriver` stays a single-owned-process primitive

Move/adapt the current driver into:

```text
DTXMania.Automation/Process/GameProcessDriver.cs
```

Public surface:

```csharp
public sealed class GameProcessDriver : IAsyncDisposable
{
    public string StandardOutput { get; }
    public string StandardError { get; }
    public int? ProcessId { get; }
    public int? ExitCode { get; }

    public void Start(GameProcessStartOptions options);

    public Task WaitForStartupAsync(
        Func<string, int?, CancellationToken, Task<bool>> launchHealthProbe,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken);

    public Task<int> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);

    public ValueTask DisposeAsync();
}
```

The driver stores the non-blank `LaunchToken` from the successful `Start` call. `WaitForStartupAsync` passes the expected launch token and owned process ID into the supplied probe. This keeps the process layer independent from HTTP/JSON-RPC types while ensuring callers do not reconstruct launch identity themselves:

```csharp
await process.WaitForStartupAsync(
    client.IsHealthyForLaunchAsync,
    timeout,
    interval,
    cancellationToken);
```

Before each probe and after any false/transient result, the driver checks whether its process exited. Early exit throws immediately with the exit code and captured stdout/stderr. Timeout remains `TimeoutException`; caller cancellation remains `OperationCanceledException`.

#### Environment contract

The driver owns:

```text
DTXMANIA_APPDATA_ROOT
DTXMANIA_LAUNCH_TOKEN
```

It writes both from `GameProcessStartOptions` and rejects generic overrides for either name.

All other environment overrides retain current semantics:

- non-null -> set/replace inherited value;
- null -> explicitly remove inherited value.

Simulated MIDI is not a production-driver boolean after extraction.

#### Cleanup contract

Preserve existing behavior:

- one `Start` per driver instance;
- asynchronous stdout/stderr capture and final drain;
- kill the owned process tree when still running;
- tolerate process-exit races;
- idempotent cleanup;
- never discover or kill unrelated CX processes.

### 4. Reuse `/health` identity semantics in `JsonRpcGameClient`

The game server already returns:

```json
{
  "status": "ok",
  "processId": 1234,
  "launchToken": "..."
}
```

MCP already protects against stale-process readiness by accepting health only when `launchToken` matches the expected launch token or `processId` matches the launched process.

Port that behavior into Automation; do not reference MCP.

Create:

```text
DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs
DTXMania.Automation/JsonRpc/GameApiHealthSnapshot.cs
DTXMania.Automation/JsonRpc/GameApiInputType.cs
DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs
```

```csharp
public sealed record GameApiConnectionOptions(Uri BaseUri, string ApiKey);

public sealed record GameApiHealthSnapshot(
    int? ProcessId,
    string? LaunchToken);
```

Client surface:

```csharp
Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
Task<GameApiHealthSnapshot?> GetHealthAsync(CancellationToken cancellationToken);
Task<bool> IsHealthyForLaunchAsync(
    string expectedLaunchToken,
    int? expectedProcessId,
    CancellationToken cancellationToken);
Task<GameStateSnapshot> GetGameStateAsync(CancellationToken cancellationToken);
Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken);
Task SendMidiNoteAsync(int noteNumber, int velocity, TimeSpan holdDuration, CancellationToken cancellationToken);
Task ChangeStageAsync(string stageName, CancellationToken cancellationToken);
Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken);
```

`IsHealthyForLaunchAsync` follows the proven MCP rule:

1. Read `/health`.
2. Return false for connection failure, non-success response, malformed/missing identity, or mismatched identity.
3. Return true when the non-blank expected launch token equals `health.LaunchToken`.
4. Otherwise return true when expected process ID exists and equals `health.ProcessId`.

The basic `IsHealthyAsync` may remain for callers that genuinely need endpoint liveness, but startup uses `IsHealthyForLaunchAsync`.

Connection base URI is explicit in `GameApiConnectionOptions`; callers do not need `HttpClient.BaseAddress`.

Keep generic JSON-RPC send private.

HTTP/JSON-RPC failures may include status and response body for useful local diagnostics. Do not add a general redaction/logging framework in HPA-501.

### 5. Automation-owned input and telemetry wire models

Do not reference `DTXMania.Game.Lib.InputType` from Automation.

```csharp
public enum GameApiInputType
{
    MouseClick = 0,
    MouseMove = 1,
    KeyPress = 2,
    KeyRelease = 3,
    MidiNoteOn = 4,
    MidiNoteOff = 5
}
```

Extract/rename `E2EGameState` to `GameStateSnapshot` without pruning the current telemetry accessors. It remains tolerant of missing/null telemetry.

Pure JSON behavior belongs in `DTXMania.Automation.Tests`.

E2E retains producer/consumer contract tests that:

- compare `GameApiInputType` integer values to `DTXMania.Game.Lib.InputType`;
- serialize game `GameTelemetrySnapshot` with server-style camel-case JSON and deserialize into `GameStateSnapshot`.

This catches wire drift without giving Automation a game reference.

### 6. Move `Eventually` with behavior unchanged

Create:

```text
DTXMania.Automation/Support/Eventually.cs
```

Preserve bounded timeout, caller cancellation, transient probe exception retention, and last-value return. Do not add retry packages/backoff policy.

### 7. One E2E-local launch adapter owns fixture/environment policy

The reusable driver should not know what simulated MIDI means, but that policy must not become duplicated dictionaries at every smoke-test call site.

Keep `E2EGameProject` as the only E2E C# reader of `DTXMANIA_E2E_GAME_PROJECT`:

```csharp
public static GameLaunchTarget ResolveLaunchTarget();
```

It chooses `GamePlatform.Windows` on Windows and `GamePlatform.Mac` otherwise, then applies the existing project-path override if present.

Add one E2E-local adapter:

```text
DTXMania.E2E/Fixtures/E2EGameLaunch.cs
```

```csharp
public static class E2EGameLaunch
{
    public static GameProcessStartOptions CreateOptions(
        string repoRoot,
        E2EFixture fixture,
        bool enableSimulatedMidi = false,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null);
}
```

`CreateOptions`:

- calls `E2EGameProject.ResolveLaunchTarget()`;
- uses `repoRoot`, `fixture.AppDataRoot`, and a fresh launch token;
- always includes `DTXMANIA_ENABLE_SIMULATED_MIDI` as either `"1"` or `null`, so parent inheritance is never accidental;
- merges caller extras used by scenarios such as controlled crash injection;
- rejects `extraEnvironment` attempting to override `DTXMANIA_ENABLE_SIMULATED_MIDI` so the boolean remains the single E2E policy owner.

Every smoke suite uses this adapter. Midi and DrumMapping therefore stop duplicating project-path selection, and CrashReporting stops building a one-off environment dictionary without the explicit MIDI removal.

After migration, this source scan must return only `E2EGameProject.cs`:

```bash
rg -n "DTXMANIA_E2E_GAME_PROJECT" DTXMania.E2E --glob '*.cs'
```

And this scan must return only `E2EGameLaunch.cs`:

```bash
rg -n "DTXMANIA_ENABLE_SIMULATED_MIDI" DTXMania.E2E --glob '*.cs'
```

Repository scripts/workflows may continue setting `DTXMANIA_E2E_GAME_PROJECT`; the restriction is about C# policy ownership.

### 8. E2E consumes Automation; old copies are deleted

Delete after call sites compile against Automation:

```text
DTXMania.E2E/Process/GameProcessDriver.cs
DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs
DTXMania.E2E/Telemetry/E2EGameState.cs
DTXMania.E2E/Support/Eventually.cs
```

Move pure support tests into `DTXMania.Automation.Tests`.

Keep E2E-owned concerns in E2E: fixtures, artifact writing, persistence verification, port selection, launch-env policy, and producer contract tests.

### 9. Cross-platform support-test contract

Add a `justfile` recipe for the new platform-neutral test project and update `e2e-support` to include it.

Both Windows and macOS CI jobs run:

```text
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

The existing Windows gameplay E2E job remains Windows-only. Do not add macOS gameplay E2E in HPA-501.

## Error Handling

- Blank working directory, app-data root, launch token, target path, or API base URI -> argument exception before launch/request.
- Duplicate `Start` -> `InvalidOperationException`.
- Reserved process environment override -> `ArgumentException`.
- Process cannot be created -> launch failure preserving useful underlying detail.
- `/health` responds from another process/token -> keep polling; do not report ready.
- Owned process exits before matching health identity -> fail immediately with exit code and captured output.
- Startup timeout -> `TimeoutException`.
- Caller cancellation -> `OperationCanceledException`.
- JSON-RPC HTTP/protocol failure -> useful exception with method/status/body as appropriate.
- Cleanup after natural exit or prior cleanup -> succeeds without surfacing benign exit races.

## Testing Strategy

### `DTXMania.Automation.Tests`

Use process/HTTP seams only; never launch MonoGame.

Required coverage:

- Windows/Mac default project target and explicit override;
- caller-supplied executable target without RID/path guessing;
- command and environment construction;
- duplicate start rejection;
- stdout/stderr terminal drain;
- startup succeeds only for matching launch token or owned PID;
- wrong-token/wrong-PID health remains not-ready;
- malformed health identity remains not-ready;
- startup early process exit includes exit/output detail;
- startup timeout and caller cancellation;
- idempotent process-tree cleanup;
- JSON-RPC success/error parsing;
- input numeric payloads;
- game-state/screenshot parsing;
- `Eventually` success, timeout, transient failure, cancellation;
- telemetry missing/null default behavior.

### `DTXMania.E2E`

Retain game-coupled contract tests for input enum and telemetry JSON compatibility.

Migrate all smoke launches through `E2EGameLaunch.CreateOptions`. Preserve current artifacts and gameplay assertions.

## Acceptance Criteria

- `DTXMania.Automation` builds as plain `net8.0` on Windows and macOS.
- `DTXMania.Automation` and its tests have no game, E2E, or MCP project reference.
- Startup readiness cannot be satisfied by a stale CX process on the endpoint; matching launch token or owned PID is required.
- Existing E2E health, telemetry, screenshot, input, stdout/stderr, and cleanup behavior runs through Automation.
- E2E C# game-project env selection is centralized in `E2EGameProject`; simulated-MIDI launch policy is centralized in `E2EGameLaunch`.
- No production project references `DTXMania.E2E`.
- HPA-503 can launch and control one owned CX process by referencing `DTXMania.Automation` only.

## Self-review

- No `AutomationSession`, DI container, generic subprocess abstraction, OBS/FFmpeg, recorder workflow, or HPA-510 behavior was introduced.
- MCP behavior is reused semantically, but Automation has no MCP dependency.
- Launch platform naming no longer claims RID selection that the project path does not perform.
- Existing repository test package versions are copied exactly; no package upgrade is part of this ticket.
- API-key sanitization was removed as unnecessary local-only hardening; recorder diagnostics remain responsible for never persisting secrets.
- E2E environment/project policy has one owner instead of moving from one duplication pattern to another.
