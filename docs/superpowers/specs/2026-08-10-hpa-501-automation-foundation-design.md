# HPA-501 Reusable CX Automation Foundation Design

**Issue:** [HPA-501](https://linear.app/cwchanap/issue/HPA-501/extract-minimal-reusable-cx-process-and-json-rpc-helpers)  
**Date:** 2026-08-10  
**Status:** Proposed for review

## Context

HPA-500's recorder roadmap intentionally starts by reusing the process and Game API support already proven by `DTXMania.E2E`. HPA-501 is the first unblocked prerequisite for the Windows recorder vertical slice and blocks HPA-503.

The current implementation already has the behavior we need, but it is coupled to the Windows-targeted E2E assembly:

- `DTXMania.E2E/Process/GameProcessDriver.cs` owns `dotnet run`, environment setup, stdout/stderr capture, exit waiting, and process-tree cleanup, but accepts an `E2EFixture` directly.
- `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs` handles health, `getGameState`, screenshots, key/MIDI input, and JSON-RPC errors, but references the game assembly's `InputType` enum and the E2E telemetry DTO.
- `DTXMania.E2E/Telemetry/E2EGameState.cs` is the consumer-side telemetry projection used by smoke tests.
- `DTXMania.E2E/Support/Eventually.cs` already provides bounded polling.
- `DTXMania.E2E/Fixtures/E2EGameProject.cs` chooses the Windows or Mac project path, but that platform-selection policy lives inside the test project.
- `DTXMania.E2E.csproj` targets `net8.0-windows7.0` and conditionally references a MonoGame platform project. As a result, the reusable helpers cannot be consumed by a plain `net8.0` recorder or validated independently on Apple Silicon.

This ticket should extract those proven primitives, not redesign CX automation.

## Goals

- Add a small `DTXMania.Automation` library targeting plain `net8.0`.
- Keep the library free of references to `DTXMania.E2E`, `DTXMania.Game.Windows`, and `DTXMania.Game.Mac`.
- Support explicit Windows x64 and Apple Silicon macOS launch targets.
- Support either `dotnet run --project <csproj>` or a caller-supplied executable path without introducing a generic subprocess framework.
- Preserve owned-process stdout/stderr capture, duplicate-start rejection, bounded exit waiting, and process-tree cleanup.
- Add a bounded startup-health wait that fails immediately when the owned process exits before becoming healthy.
- Preserve the existing narrow Game API client behavior: health, `getGameState`, screenshot, key/MIDI input, stage change, and private JSON-RPC request transport.
- Make the Game API endpoint and API key explicit connection inputs rather than relying on `HttpClient.BaseAddress` configuration outside the client.
- Keep wire contracts independent from game assembly types while retaining a producer/consumer contract test in `DTXMania.E2E`.
- Redact the API key from JSON-RPC diagnostics before an exception message is exposed.
- Move reusable support tests into a platform-neutral `DTXMania.Automation.Tests` project and run them on both Windows and macOS CI.
- Migrate the existing E2E smoke tests to consume the extracted helpers without changing their gameplay behavior or artifact contract.

## Non-goals

- OBS, FFmpeg, recording sandbox, recorder CLI, output publishing, or video artifact validation.
- Prepared-chart, preview-control, or Song Select commands from HPA-510.
- A game-wide automation session, capture session, workflow state machine, service container, or dependency-injection layer.
- A generic process runner or generic HTTP/JSON-RPC framework.
- Retargeting all of `DTXMania.E2E` to plain `net8.0`; it still directly tests game producer types and Windows gameplay flows.
- Removing the E2E fixture builder, artifact writer, persistence checks, or game-project test policy.
- Changing Game API server contracts or numeric `InputType` wire values.
- Adding macOS gameplay E2E to this ticket. HPA-501 only makes the reusable support library build/test cross-platform.
- Backward-compatible wrappers for the old `DTXMania.E2E.Process`, `.JsonRpc`, `.Telemetry`, or `.Support` namespaces. There are no external consumers.

## Approaches Considered

### 1. Extract the proven primitives with small option/wire types — selected

Move the existing process driver, JSON-RPC client, telemetry projection, and polling helper into `DTXMania.Automation`. Replace only their test/game-specific dependencies with small automation-owned contracts.

This keeps behavior familiar, gives HPA-503 a production-safe dependency, and minimizes implementation risk.

### 2. Add an `AutomationSession` facade that owns process + HTTP + polling — rejected

A facade would make HPA-501 responsible for lifecycle orchestration that belongs to HPA-503's recorder workflow. It would also create a second state/lifecycle owner before the recorder has proven what it needs.

HPA-501 should expose composable primitives; HPA-503 should own the imperative workflow.

### 3. Multi-target or source-link the E2E project — rejected

Keeping reusable code inside the test project, multi-targeting `DTXMania.E2E`, or linking the same source files into another assembly preserves confusing ownership and makes future recorder changes depend on test-project structure.

A small production library is clearer and cheaper to maintain.

## Chosen Architecture

## 1. Add `DTXMania.Automation` and `DTXMania.Automation.Tests`

Create:

```text
DTXMania.Automation/DTXMania.Automation.csproj
DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

Both target plain `net8.0` with nullable reference types and implicit usings enabled.

`DTXMania.Automation` has no project references to game or test assemblies.

`DTXMania.Automation.Tests` references only `DTXMania.Automation` plus xUnit/test packages. It must not reference either MonoGame platform project.

Add both projects to `DTXMania.sln`.

`DTXMania.E2E` keeps its existing `net8.0-windows7.0` target and platform game reference because unrelated E2E tests still consume game entities, EF models, and producer-side telemetry types. It adds one project reference to `DTXMania.Automation`.

## 2. Explicit launch target and start options

Keep launch selection deliberately small:

```csharp
namespace DTXMania.Automation.Process;

public enum GamePlatform
{
    WindowsX64,
    MacArm64
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

Default project mappings are the current repository-relative paths:

```text
WindowsX64 -> DTXMania.Game/DTXMania.Game.Windows.csproj
MacArm64   -> DTXMania.Game/DTXMania.Game.Mac.csproj
```

`Project(...)` accepts an explicit override so E2E can continue honoring `DTXMANIA_E2E_GAME_PROJECT` without putting that test-specific environment variable into the production library.

`Executable(...)` requires the exact executable path. HPA-501 does not guess build configuration, RID output folders, app-bundle paths, or publish layout.

The process command is derived only from `GameLaunchKind`:

```text
Project    -> dotnet run --project <Target.Path>
Executable -> <Target.Path>
```

No shell is used.

## 3. `GameProcessDriver` remains a single-owned-process primitive

Move and adapt the existing driver into:

```text
DTXMania.Automation/Process/GameProcessDriver.cs
```

Public surface:

```csharp
public sealed class GameProcessDriver : IAsyncDisposable
{
    public string StandardOutput { get; }
    public string StandardError { get; }
    public int? ExitCode { get; }

    public void Start(GameProcessStartOptions options);

    public Task WaitForStartupAsync(
        Func<CancellationToken, Task<bool>> healthProbe,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken);

    public Task<int> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);

    public ValueTask DisposeAsync();
}
```

### Environment contract

The driver owns these variables:

```text
DTXMANIA_APPDATA_ROOT
DTXMANIA_LAUNCH_TOKEN
```

It always writes them from `GameProcessStartOptions` and rejects attempts to override/remove them through `EnvironmentOverrides`.

All other overrides are applied exactly as the current driver does:

- non-null value -> set/replace inherited environment variable;
- null -> remove inherited environment variable.

E2E-specific simulated MIDI behavior is no longer a production-driver boolean. The E2E fixture adapter passes `DTXMANIA_ENABLE_SIMULATED_MIDI=1` or removes it through `EnvironmentOverrides`.

### Startup wait contract

`WaitForStartupAsync` is intentionally narrow. It repeatedly calls the supplied health probe until it returns `true`.

Before each probe and after a failed probe it checks whether the owned process exited. If so, it fails immediately with an `InvalidOperationException` containing the exit code and captured stdout/stderr rather than waiting for the full startup timeout.

Timeout produces `TimeoutException`. Caller cancellation produces `OperationCanceledException` and is never converted to a timeout.

The method does not know about JSON-RPC types; callers can pass `client.IsHealthyAsync` or another future probe.

### Cleanup contract

Preserve current behavior:

- only one `Start` per driver instance;
- capture stdout/stderr asynchronously and drain terminal output;
- `DisposeAsync` kills the owned process tree when still running;
- process-exit races are tolerated;
- cleanup is idempotent and does not throw merely because the process already exited;
- disposal does not create a global process registry or attempt to clean unrelated CX processes.

## 4. Game API connection and wire contracts are automation-owned

Create:

```text
DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs
DTXMania.Automation/JsonRpc/GameApiInputType.cs
DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs
DTXMania.Automation/Telemetry/GameStateSnapshot.cs
```

Connection input:

```csharp
public sealed record GameApiConnectionOptions(Uri BaseUri, string ApiKey);
```

The client constructor becomes:

```csharp
public JsonRpcGameClient(
    HttpClient httpClient,
    GameApiConnectionOptions connection);
```

The client builds request URIs from `connection.BaseUri`; callers do not need to configure `HttpClient.BaseAddress`.

### Input wire enum

Do not reference `DTXMania.Game.Lib.InputType` from the automation library. Define the protocol values locally:

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

The existing public helpers continue to use key/MIDI-specific methods rather than exposing arbitrary `GameInput` objects to callers.

A producer/consumer contract test remains in `DTXMania.E2E` and compares these numeric values with `DTXMania.Game.Lib.InputType`. This catches server/client drift without introducing a production project reference.

### Telemetry DTO

Rename the extracted consumer projection from `E2EGameState` to `GameStateSnapshot` and preserve the currently consumed telemetry accessors.

It remains tolerant of missing or null telemetry so startup/result probes degrade to defaults rather than throwing.

The pure JSON behavior is tested in `DTXMania.Automation.Tests`.

A separate E2E contract test serializes the game assembly's `GameTelemetrySnapshot` using the server's camel-case behavior and deserializes it into `GameStateSnapshot`. This preserves the valuable producer/consumer compatibility check while keeping the automation test project platform-neutral.

## 5. Keep JSON-RPC narrow and redact secret-bearing diagnostics

Preserve these public methods:

```csharp
Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
Task<GameStateSnapshot> GetGameStateAsync(CancellationToken cancellationToken);
Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken);
Task SendMidiNoteAsync(int noteNumber, int velocity, TimeSpan holdDuration, CancellationToken cancellationToken);
Task ChangeStageAsync(string stageName, CancellationToken cancellationToken);
Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken);
```

Keep the generic JSON-RPC send method private. HPA-510 or HPA-503 may add explicit methods later when those commands exist; HPA-501 does not expose unrestricted mutation transport as public API.

For non-success HTTP or JSON-RPC error responses, exception diagnostics may include the response body because it is useful for development, but the exact configured API key must be replaced with `[REDACTED]` before being placed in an exception message.

Use a tiny internal string replacement helper; do not add logging infrastructure or a general secret-management system.

Requests continue to send the API key only through the `X-Api-Key` header.

## 6. Move `Eventually` unchanged except for namespace

Create:

```text
DTXMania.Automation/Support/Eventually.cs
```

Keep the existing behavior:

- bounded timeout;
- caller cancellation wins;
- transient probe exceptions are retained as the final timeout cause;
- successful predicate returns the last value.

Do not create retry policies, backoff strategies, or resilience packages.

## 7. E2E becomes a consumer of Automation

Delete the extracted implementation copies from `DTXMania.E2E` after call sites compile against `DTXMania.Automation`:

```text
DTXMania.E2E/Process/GameProcessDriver.cs
DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs
DTXMania.E2E/Telemetry/E2EGameState.cs
DTXMania.E2E/Support/Eventually.cs
```

Move their pure support tests into `DTXMania.Automation.Tests` rather than testing duplicate behavior in two projects.

Update the gameplay smoke tests to use:

```text
DTXMania.Automation.Process
DTXMania.Automation.JsonRpc
DTXMania.Automation.Telemetry
DTXMania.Automation.Support
```

Keep E2E-owned concerns in E2E:

- `E2EFixture` / `E2EFixtureBuilder`;
- artifact writer;
- score/persistence verification;
- test-specific API port selection;
- `DTXMANIA_E2E_GAME_PROJECT` override;
- simulated MIDI enable/remove policy.

Change `E2EGameProject` from returning a raw string to returning an explicit `GameLaunchTarget`:

```csharp
public static GameLaunchTarget ResolveLaunchTarget();
```

It chooses `GamePlatform.WindowsX64` on Windows and `GamePlatform.MacArm64` otherwise, then passes the existing environment override into `GameLaunchTarget.Project(...)`.

This keeps OS/test policy outside the reusable library while making the selected platform explicit at the automation boundary.

## 8. Cross-platform test and CI contract

Add a `justfile` variable/recipe for the new test project:

```text
automation_test := DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
just automation-test
```

`just automation-test` runs only the platform-neutral support tests.

Update `just e2e-support` to run `automation-test` first and then the remaining `Category=E2E-Support` tests in `DTXMania.E2E`.

Update `.github/workflows/build-and-test.yml` so both Windows and macOS jobs run:

```text
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

The existing Windows gameplay E2E job still runs the E2E smoke and E2E-specific support/contract tests.

Do not add macOS gameplay E2E in HPA-501.

## Error Handling

- Blank working directory, app-data root, launch token, or target path -> argument exception before process creation.
- Duplicate `Start` -> `InvalidOperationException`.
- Reserved environment override -> `ArgumentException`.
- Process cannot be created -> `InvalidOperationException` preserving the underlying failure where useful.
- Process exits before health -> fail immediately with exit code and captured output.
- Startup timeout -> `TimeoutException`.
- Caller cancellation -> `OperationCanceledException`.
- JSON-RPC HTTP failure -> exception includes status and sanitized body.
- JSON-RPC protocol error or missing result -> exception includes method name and sanitized response/error detail.
- Cleanup after natural exit or prior cleanup -> succeeds without surfacing benign process-exit races.

## Testing Strategy

### `DTXMania.Automation.Tests`

Use process/HTTP seams only; never launch a MonoGame window.

Required coverage:

- default Windows project target;
- default Apple Silicon Mac project target;
- project override;
- explicit executable target;
- process command/working-directory/environment construction through a tiny generated child `net8.0` console project;
- reserved environment rejection;
- duplicate start rejection;
- stdout/stderr draining;
- natural exit;
- early exit while waiting for startup;
- startup timeout;
- startup cancellation;
- process-tree/owned-process cleanup and repeated disposal;
- JSON-RPC health success/failure;
- `getGameState` deserialization;
- key and MIDI wire values;
- screenshot extraction;
- JSON-RPC error parsing;
- API-key redaction from error diagnostics;
- `Eventually` success, transient exception retry, timeout, and cancellation;
- `GameStateSnapshot` defaults for absent/null telemetry.

### `DTXMania.E2E`

Keep only contract/integration coverage that requires game types:

- `GameApiInputType` numeric values equal producer `InputType` values;
- `GameTelemetrySnapshot` camel-case JSON still maps to every `GameStateSnapshot` field consumed by E2E;
- existing gameplay smoke flows continue to use the extracted process/client/polling primitives and produce the same artifacts.

## Scope / Delivery Size

This remains one implementation issue and should fit within roughly 2-3 engineer days because it is primarily a move-and-decouple operation with focused contract additions.

Keep the implementation to four reviewable slices:

1. project + launch/process primitive;
2. JSON-RPC + telemetry + polling primitive;
3. E2E migration and producer/consumer contract checks;
4. cross-platform build/test wiring and final validation.

If implementation starts creating an automation session coordinator, generic process interfaces, retry-policy abstractions, or recorder-specific state, stop and move that work back to HPA-503/HPA-510 as appropriate.

## Acceptance Criteria Mapping

- `DTXMania.Automation` builds on Windows and macOS: plain `net8.0`, no MonoGame project reference, validated in both CI jobs.
- Existing E2E process/API behavior uses the extracted library: old implementation copies are deleted and smoke-test imports point at Automation.
- Explicit Windows x64 / Mac arm64 selection: `GameLaunchTarget.Project(GamePlatform, override)` plus explicit executable target.
- Owned launch/cleanup/stdout/stderr: preserved in `GameProcessDriver` and covered by generated child-process tests.
- Health polling, startup timeout, and early exit: `WaitForStartupAsync` plus `Eventually` tests.
- `getGameState`, screenshot, and narrow request support: preserved as explicit client methods; generic send stays private.
- Wire DTO independence: automation-owned input enum and telemetry snapshot; game compatibility tested from E2E only.
- API-key redaction: JSON-RPC failure tests assert the configured key never appears in exception messages.
- HPA-503 can launch/control CX without importing E2E: it can reference only `DTXMania.Automation` and construct process/API inputs directly.
