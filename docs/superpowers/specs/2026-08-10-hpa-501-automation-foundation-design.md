# HPA-501 Reusable CX Automation Foundation Design

**Issue:** [HPA-501](https://linear.app/cwchanap/issue/HPA-501/extract-minimal-reusable-cx-process-and-json-rpc-helpers)  
**Date:** 2026-08-10  
**Status:** Revised after second design review

## Context

HPA-500's recorder roadmap starts by reusing process and Game API support already proven by `DTXMania.E2E`. HPA-501 is an unblocked prerequisite for HPA-503.

The useful behavior already exists, but ownership is wrong for recorder reuse:

- `DTXMania.E2E/Process/GameProcessDriver.cs` owns `dotnet run`, environment setup, stdout/stderr capture, exit waiting, and process-tree cleanup, but accepts `E2EFixture` directly.
- `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs` handles health, `getGameState`, screenshots, key/MIDI input, stage change, and JSON-RPC errors, but references the game assembly's `InputType` and the E2E telemetry DTO.
- `DTXMania.E2E/Telemetry/E2EGameState.cs` is the consumer-side telemetry projection.
- `DTXMania.E2E/Support/Eventually.cs` already provides bounded polling.
- E2E smoke files duplicate repository-root discovery, API-port selection, project selection, and simulated-MIDI launch policy.
- `DTXMania.E2E.csproj` targets `net8.0-windows7.0`, so recorder production code must not depend on it.

There is also a proven stale-process protection in `MCP/Server/GameInteractionService.cs`: `/health` exposes `launchToken` and `processId`, and MCP checks that readiness belongs to the launch it initiated. HPA-501 should reuse that behavior without referencing MCP.

One important correction is required when porting that rule. Project launch uses `dotnet run --project`; the `Process` owned by the launcher is not a reliable identity for the final game process reported by `Environment.ProcessId`. Therefore:

- **Project launch readiness is gated by launch-token equality.**
- **Executable launch may additionally use PID equality as a fallback.**

HPA-501 should extract these proven primitives, remove the known E2E policy duplication, and stop there.

## Goals

- Add a small `DTXMania.Automation` library targeting plain `net8.0`.
- Keep Automation free of references to `DTXMania.E2E`, MonoGame platform projects, and MCP.
- Support project launch through the current Windows/Mac project paths and caller-supplied executable launch without RID/publish-path inference.
- Preserve owned-process stdout/stderr capture, duplicate-start rejection, bounded exit waiting, and process-tree cleanup.
- Preserve `DTXMANIA_APPDATA_ROOT` and `DTXMANIA_LAUNCH_TOKEN` as driver-owned environment values.
- Make project startup require a matching `/health` launch token; allow PID fallback only for direct executable launch.
- Preserve the last observed health identity and include it in startup-timeout diagnostics.
- Fail immediately when the owned launcher/executable exits before readiness.
- Preserve narrow Game API behavior: `/health`, `getGameState`, screenshot, key/MIDI input, stage change, and private JSON-RPC transport.
- Make API endpoint/key explicit connection inputs.
- Keep input/telemetry wire models independent from game assembly types while retaining E2E producer/consumer contract tests.
- Move pure reusable support tests into a plain `net8.0` `DTXMania.Automation.Tests` project and run it on Windows and macOS CI.
- Preserve the current E2E gameplay behavior/artifacts while migrating smoke tests to Automation.
- Make one E2E-local owner responsible for repo-root discovery, API-port selection, game-project selection, and simulated-MIDI environment policy.

## Non-goals

- OBS, FFmpeg, recording sandbox, recorder CLI, output publication, or video validation.
- Prepared-chart/preview commands from HPA-510.
- `AutomationSession`, workflow/capture-session state machines, DI, generic process runners, or generic JSON-RPC transports.
- Depending on MCP project types or `MCP/Server/JsonRpcClient.cs`.
- Retargeting all of `DTXMania.E2E` to plain `net8.0`.
- Adding macOS gameplay E2E. The reusable Automation suite is cross-platform; the existing gameplay-E2E project remains Windows-targeted.
- Changing Game API server contracts or `InputType` numeric values.
- Backward-compatible wrappers for old E2E helper namespaces.
- API-key diagnostic sanitization. HPA-503 remains responsible for not persisting secrets in recorder diagnostics.
- Migrating MCP to consume `DTXMania.Automation` in this ticket. **Follow-up:** once HPA-501 lands, converge MCP readiness parsing onto Automation so the launch-identity rule does not remain duplicated long term.

## Approaches Considered

### Extract proven primitives and narrow launch policy — selected

Move the existing process/JSON-RPC/telemetry/polling behavior into Automation, port the useful MCP launch-token semantics, and centralize E2E-only launch helpers.

This gives HPA-503 a production-safe dependency with minimal new architecture.

### Add an `AutomationSession` facade — rejected

HPA-503 owns recorder lifecycle. Combining process, HTTP, and polling here would create a second lifecycle owner before the recorder proves that abstraction useful.

### Multi-target or source-link E2E — rejected

Keeping reusable code inside the test project preserves the wrong ownership and still makes HPA-503 depend on test structure.

## Chosen Architecture

### 1. Plain Automation library and test project

Create:

```text
DTXMania.Automation/DTXMania.Automation.csproj
DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

Both target plain `net8.0`.

`DTXMania.Automation` has no game/E2E/MCP project reference.

`DTXMania.Automation.Tests` references Automation plus the repository-current E2E/main-test package versions:

```text
Microsoft.NET.Test.Sdk       18.8.1
xunit                        2.9.3
xunit.runner.visualstudio    3.1.5
```

Current `DTXMania.Test.Mac.csproj` intentionally still has older test-runner/Test SDK versions; HPA-501 does not normalize unrelated project packages. Automation.Tests proves its own package combination on both Windows and macOS CI.

Because process tests spawn real child processes, add:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

in `DTXMania.Automation.Tests/AssemblyInfo.cs`, mirroring the safety policy already used by E2E. Mark real process tests with `Trait("Category", "Automation-Process")` so they can be isolated when diagnosing CI/runtime cost.

### 2. Launch target stores only launch behavior and path

`GamePlatform` was dead data on the target: command construction uses only launch kind/path, and executable launch must not infer architecture or RID. Remove it from `GameLaunchTarget`.

Keep project constants reusable:

```csharp
namespace DTXMania.Automation.Process;

public static class GameProjectPaths
{
    public const string Windows = "DTXMania.Game/DTXMania.Game.Windows.csproj";
    public const string Mac = "DTXMania.Game/DTXMania.Game.Mac.csproj";

    public static string Current
    {
        get
        {
            if (OperatingSystem.IsWindows()) return Windows;
            if (OperatingSystem.IsMacOS()) return Mac;
            throw new PlatformNotSupportedException(
                "DTXManiaCX automation project launch supports Windows and macOS only.");
        }
    }
}

public enum GameLaunchKind
{
    Project,
    Executable
}

public sealed record GameLaunchTarget(GameLaunchKind Kind, string Path)
{
    public static GameLaunchTarget Project(string? projectPathOverride = null);
    public static GameLaunchTarget Executable(string executablePath);
}
```

`Project(...)` uses a non-blank override exactly; otherwise it uses `GameProjectPaths.Current`.

`Executable(...)` requires an exact non-blank caller path.

Commands remain:

```text
Project    -> dotnet run --project <Target.Path>
Executable -> <Target.Path>
```

No shell and no publish/RID/configuration inference.

```csharp
public sealed record GameProcessStartOptions(
    string WorkingDirectory,
    GameLaunchTarget Target,
    string AppDataRoot,
    string LaunchToken,
    IReadOnlyDictionary<string, string?>? EnvironmentOverrides = null);
```

### 3. `GameProcessDriver` owns one process and readiness identity

Move/adapt the current driver to `DTXMania.Automation/Process/GameProcessDriver.cs`.

Define a small transport-neutral health identity beside the process types:

```csharp
public sealed record GameHealthSnapshot(
    int? ProcessId,
    string? LaunchToken);
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
        Func<CancellationToken, Task<GameHealthSnapshot?>> healthProbe,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken);

    public Task<int> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);

    public ValueTask DisposeAsync();
}
```

The driver remembers the successful start's `Target.Kind` and launch token.

#### Readiness rule

For each successful health observation:

```text
Project launch:
    ready only when health.LaunchToken == expected launch token

Executable launch:
    ready when launch token matches
    OR, as a fallback, health.ProcessId == owned executable PID
```

Do not use the PID from `dotnet run` as project readiness evidence.

Before and after each health probe, check whether the owned launcher/executable exited. Early exit throws immediately with exit code and captured stdout/stderr.

Keep the **last non-null `GameHealthSnapshot`**. If the deadline expires, throw `TimeoutException` containing:

- launch kind;
- owned process ID;
- whether a matching token was required;
- last observed health `ProcessId` and `LaunchToken`, or an explicit "no parseable health identity observed" marker.

This replaces MCP's logger-only observed-vs-expected diagnostics with useful exception context without adding logging infrastructure.

#### Environment and cleanup

The driver owns and protects:

```text
DTXMANIA_APPDATA_ROOT
DTXMANIA_LAUNCH_TOKEN
```

Generic overrides keep current semantics: non-null sets/replaces; null explicitly removes inherited state.

Simulated MIDI is E2E policy, not a production-driver flag.

Preserve current process-tree kill, terminal stdout/stderr drain, exit-race handling, and idempotent disposal. Never discover/kill unrelated CX processes.

### 4. `JsonRpcGameClient` exposes health observation, not duplicate readiness policy

Create:

```text
DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs
DTXMania.Automation/JsonRpc/GameApiInputType.cs
DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs
DTXMania.Automation/Telemetry/GameStateSnapshot.cs
```

Connection:

```csharp
public sealed record GameApiConnectionOptions(Uri BaseUri, string ApiKey);
```

Client surface:

```csharp
Task<GameHealthSnapshot?> GetHealthAsync(CancellationToken cancellationToken);
Task<GameStateSnapshot> GetGameStateAsync(CancellationToken cancellationToken);
Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken);
Task SendMidiNoteAsync(int noteNumber, int velocity, TimeSpan holdDuration, CancellationToken cancellationToken);
Task ChangeStageAsync(string stageName, CancellationToken cancellationToken);
Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken);
```

Do **not** keep `IsHealthyAsync` or `IsHealthyForLaunchAsync`:

- `GetHealthAsync() != null` already represents a parseable successful health response;
- readiness identity belongs in the driver because it knows launch kind, expected token, and whether PID fallback is valid.

`GetHealthAsync` ports MCP's tolerant parsing:

- success response only;
- `processId` may be JSON number or numeric string;
- `launchToken` may be a string;
- transient HTTP/per-request timeout/malformed JSON returns null unless caller cancellation itself was requested.

Keep generic JSON-RPC send private. Use explicit base URI from `GameApiConnectionOptions`. Do not add redaction/logging infrastructure.

### 5. Automation-owned input and telemetry wire models

Keep stable protocol values locally:

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

Extract/rename `E2EGameState` to `GameStateSnapshot` without pruning fields/default behavior.

Automation.Tests covers raw JSON behavior only. E2E keeps the producer contract tests because it can see both Automation and game types:

- `GameApiInputType` integers match `DTXMania.Game.Lib.InputType`;
- game `GameTelemetrySnapshot` camel-case JSON deserializes into `GameStateSnapshot`.

### 6. Move `Eventually` unchanged except namespace

Create `DTXMania.Automation/Support/Eventually.cs` and preserve bounded timeout, caller cancellation, transient-probe exception retention, and last-value return. Do not add retry packages/backoff policy.

### 7. One E2E launch-policy owner

Create/expand:

```text
DTXMania.E2E/Fixtures/E2EGameLaunch.cs
```

It owns the launch policy duplicated across the four smoke suites:

```csharp
public static class E2EGameLaunch
{
    public static string ResolveRepoRoot();
    public static int ResolveApiPort();

    public static GameProcessStartOptions CreateOptions(
        E2EFixture fixture,
        bool enableSimulatedMidi = false,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null);
}
```

`ResolveRepoRoot` contains the existing `DTXMania.sln` upward search once.

`ResolveApiPort`:

- honors `DTXMANIA_E2E_API_PORT` only when it parses to `1..65535`;
- otherwise chooses an ephemeral loopback port using the existing bounded retry/rebind behavior;
- stays E2E-local; HPA-501 does not add a general free-port helper to Automation.

`E2EGameProject` remains the only E2E C# reader of `DTXMANIA_E2E_GAME_PROJECT` and returns:

```csharp
GameLaunchTarget.Project(overridePath)
```

so default platform selection stays in `GameProjectPaths.Current`.

`CreateOptions`:

- resolves the repo root itself;
- uses `fixture.AppDataRoot` and a fresh launch token;
- always sets `DTXMANIA_ENABLE_SIMULATED_MIDI` to `"1"` or explicitly removes it with null;
- merges scenario extras;
- rejects extras attempting to override the MIDI policy key.

Every smoke suite uses `ResolveRepoRoot`, `ResolveApiPort`, and `CreateOptions` rather than private copies.

Post-migration scans must show:

```text
DTXMANIA_E2E_GAME_PROJECT      -> E2EGameProject.cs only
DTXMANIA_ENABLE_SIMULATED_MIDI -> E2EGameLaunch.cs only
FindRepoRoot                   -> no smoke-test copies
GetAvailablePort               -> no smoke-test copies
GetPortFromEnvironmentOrDefault -> no smoke-test copies
```

### 8. E2E consumes Automation and old helpers are deleted

Delete after migration:

```text
DTXMania.E2E/Process/GameProcessDriver.cs
DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs
DTXMania.E2E/Telemetry/E2EGameState.cs
DTXMania.E2E/Support/Eventually.cs
```

Move pure helper tests into Automation.Tests. Keep E2E fixtures, artifact handling, persistence verification, launch policy, and producer contracts in E2E.

All gameplay startup gates become:

```csharp
process.Start(startOptions);
await process.WaitForStartupAsync(
    client.GetHealthAsync,
    TimeSpan.FromSeconds(60),
    TimeSpan.FromMilliseconds(500),
    cancellationToken);
```

### 9. Validation contract

`DTXMania.Automation.Tests` runs on both Windows and macOS CI.

The existing gameplay-E2E project remains Windows-targeted. HPA-501 does not claim macOS gameplay-E2E support. The migration's live behavioral gate is therefore the existing Windows gameplay E2E:

```text
Category=E2E
```

Task 4 is not considered behaviorally verified until that Windows live smoke passes. `Category=E2E-Support` alone is insufficient because it does not launch the game or exercise launch-token readiness.

## Error Handling

- Blank working directory, app-data root, launch token, executable path, or API base URI -> argument exception before launch/request.
- Unsupported project-launch host -> `PlatformNotSupportedException`.
- Duplicate `Start` -> `InvalidOperationException`.
- Reserved app-data/launch-token generic override -> `ArgumentException`.
- Process cannot start -> launch exception preserving useful underlying detail.
- Project `/health` token mismatch -> keep polling regardless of PID.
- Executable `/health` token mismatch -> PID may satisfy fallback if it matches the owned executable.
- Owned launcher/executable exits before readiness -> immediate failure with exit code/stdout/stderr.
- Startup timeout -> `TimeoutException` containing the last observed health identity.
- Caller cancellation -> `OperationCanceledException`.
- JSON-RPC HTTP/protocol failure -> method/status/body detail as appropriate.
- Cleanup after natural exit or prior cleanup -> succeeds without surfacing benign exit races.

## Testing Strategy

### Automation.Tests

Use process/HTTP seams only; never launch MonoGame.

Required coverage:

- Windows/Mac project constants and current-host project selection;
- exact project override and executable path;
- unsupported host behavior where practical;
- command/environment construction;
- duplicate start rejection;
- terminal stdout/stderr drain;
- **Project:** matching token succeeds; same PID with wrong/missing token does not satisfy readiness;
- **Executable:** matching token succeeds; matching executable PID may satisfy fallback;
- mismatched health times out and timeout message includes the last observed health snapshot;
- malformed/no health identity remains not-ready;
- early process exit, timeout, cancellation, idempotent cleanup;
- JSON-RPC health parsing plus normal command success/error parsing;
- stable input numeric payloads;
- telemetry missing/null defaults;
- `Eventually` success/timeout/transient failure/cancellation.

Process tests are serialized assembly-wide and tagged `Category=Automation-Process`.

### E2E

- producer/consumer wire-contract tests remain game-coupled;
- all smoke launch policy goes through `E2EGameLaunch`/`E2EGameProject`;
- existing gameplay assertions and artifacts stay unchanged;
- the Windows live `Category=E2E` suite is required after the migration.

## Risks

### Readiness semantics become stricter

A successful `/health` response no longer means the launched game is ready. Project launch requires the exact launch token. A broken environment propagation could otherwise look like a generic 60-second hang, so timeout diagnostics must include the last observed health identity and the Windows gameplay smoke is a required gate.

### Process tests add CI work

Automation process tests create real child processes and some invoke `dotnet run`. They run on both Windows and macOS. Serialize them to avoid process contention, tag them for filtering, keep fixtures minimal, and do not turn them into MonoGame integration tests. Expect a modest CI-time increase; if it becomes material, optimize the fixture/build reuse later rather than adding a benchmark framework now.

### E2E remains Windows-targeted

Automation is cross-platform, but `DTXMania.E2E` remains `net8.0-windows7.0`. Do not use a macOS E2E command as evidence for HPA-501 unless that project is separately retargeted in a future issue.

### MCP temporarily keeps its own readiness implementation

HPA-501 copies the proven semantics, not the MCP dependency. This creates temporary duplication. Record MCP-to-Automation convergence as a follow-up; do not expand this ticket by rewriting MCP's generic client/lifecycle.

## Acceptance Criteria

- `DTXMania.Automation` and Automation.Tests target plain `net8.0` and have no game/E2E/MCP project references.
- Automation.Tests run on Windows and macOS CI with process tests serialized.
- Project startup accepts only a matching `/health` launch token.
- Executable startup accepts matching token or matching owned executable PID.
- Startup timeout reports the last observed health identity.
- Existing E2E health, telemetry, screenshot, input, stdout/stderr, and cleanup behavior flows through Automation.
- E2E repo-root, API-port, project-selection, and simulated-MIDI policy each have one C# owner rather than smoke-test copies.
- Old E2E helper copies are deleted with no shims.
- The Windows live gameplay E2E passes after migration.
- No production project references E2E or MCP.
- HPA-503 can launch/control one owned CX process by referencing Automation only.

## Self-review

- No lifecycle/session facade, DI container, generic subprocess/transport abstraction, recorder workflow, OBS/FFmpeg, or HPA-510 behavior was added.
- `GamePlatform` is not dead state on `GameLaunchTarget`.
- `IsHealthyAsync`/`IsHealthyForLaunchAsync` are not redundant public APIs; `GetHealthAsync` is the single health transport method and the driver owns readiness semantics.
- Project launch does not pretend its `dotnet run` PID is the game PID.
- E2E launch-policy duplication is reduced rather than merely moved.
- Current E2E/main test package versions are preserved; no unrelated package normalization is included.
- API-key sanitization remains out of scope.
- Risks cover the behavior change and CI cost, not just scope purity.
