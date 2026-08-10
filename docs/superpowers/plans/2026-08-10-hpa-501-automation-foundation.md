# HPA-501 Reusable CX Automation Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the proven CX process, Game API, telemetry, and polling helpers from the Windows-targeted E2E project into a small plain-`net8.0` library that E2E and HPA-503 can consume, while preserving launch identity and centralizing E2E launch policy.

**Architecture:** Add `DTXMania.Automation` plus a platform-neutral test project. Keep process ownership, JSON-RPC transport, telemetry projection, and polling as separate primitives. Port MCP's existing `/health` launch-token/process-ID identity semantics without referencing MCP. Migrate E2E through one fixture-local launch adapter; do not add a workflow/session coordinator because HPA-503 remains lifecycle owner.

**Tech Stack:** .NET 8, C#, xUnit, `System.Diagnostics.Process`, `HttpClient`, `System.Text.Json`, existing CX Game API/E2E fixtures, GitHub Actions.

## Global Constraints

- Source of truth: [`2026-08-10-hpa-501-automation-foundation-design.md`](../specs/2026-08-10-hpa-501-automation-foundation-design.md).
- `DTXMania.Automation` and `DTXMania.Automation.Tests` target plain `net8.0`.
- Automation must not reference `DTXMania.E2E`, either MonoGame game project, or MCP.
- Automation.Tests must reference only Automation plus test packages.
- Keep `DTXMania.E2E` on `net8.0-windows7.0`.
- Use `GamePlatform.Windows` / `GamePlatform.Mac`; project launch does not select a RID or architecture.
- Default project paths remain `DTXMania.Game/DTXMania.Game.Windows.csproj` and `DTXMania.Game/DTXMania.Game.Mac.csproj`.
- `GameLaunchTarget.Project(..., projectPathOverride)` treats null/blank override as “use the platform default”, matching current `E2EGameProject` behavior.
- Executable launch requires a non-blank exact caller-supplied path; do not infer publish/RID/configuration paths.
- `DTXMANIA_APPDATA_ROOT` and `DTXMANIA_LAUNCH_TOKEN` are driver-owned and cannot be generic overrides.
- Startup readiness must match the launched identity using `/health` launch token or owned process ID; success status alone is insufficient.
- Simulated MIDI is E2E policy, not a production-driver boolean.
- Keep generic JSON-RPC send private.
- Preserve input wire values `0..5` exactly.
- Do not add API-key sanitization/redaction logic in HPA-501.
- Preserve owned-process-tree cleanup and never discover/kill unrelated CX processes.
- No OBS, FFmpeg, CLI, recording sandbox, prepared-chart commands, DI, generic process framework, workflow state machine, or compatibility shims.
- Use RED -> focused GREEN -> review -> commit for each task.

## Existing Behavior to Reuse

- Process lifecycle: `DTXMania.E2E/Process/GameProcessDriver.cs`.
- JSON-RPC client: `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`.
- Telemetry projection: `DTXMania.E2E/Telemetry/E2EGameState.cs`.
- Polling: `DTXMania.E2E/Support/Eventually.cs`.
- Launch identity: `MCP/Server/GameInteractionService.cs::WaitForGameReadyAsync` and `TryReadHealthIdentityAsync`.
- `/health` fields: `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs` (`processId`, `launchToken`).

## File Responsibility Map

### New production project

- `DTXMania.Automation/DTXMania.Automation.csproj` — plain `net8.0` library.
- `DTXMania.Automation/Process/GameLaunchTarget.cs` — Windows/Mac + Project/Executable contract.
- `DTXMania.Automation/Process/GameProcessStartOptions.cs` — working directory, target, app-data root, launch token, environment overrides.
- `DTXMania.Automation/Process/GameProcessDriver.cs` — one owned process, output capture, identity-aware startup wait, exit wait, cleanup.
- `DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs` — base URI and API key.
- `DTXMania.Automation/JsonRpc/GameApiHealthSnapshot.cs` — `/health` process identity projection.
- `DTXMania.Automation/JsonRpc/GameApiInputType.cs` — stable consumer-side input wire values.
- `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs` — health identity + explicit Game API commands.
- `DTXMania.Automation/Telemetry/GameStateSnapshot.cs` — consumer telemetry projection.
- `DTXMania.Automation/Support/Eventually.cs` — bounded polling.

### New platform-neutral tests

- `DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`.
- `DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs`.
- `DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs`.
- `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs`.
- `DTXMania.Automation.Tests/Telemetry/GameStateSnapshotTests.cs`.
- `DTXMania.Automation.Tests/Support/EventuallyTests.cs`.

### E2E migration

- Modify `DTXMania.E2E/DTXMania.E2E.csproj` — reference Automation.
- Modify `DTXMania.E2E/Fixtures/E2EGameProject.cs` — return `GameLaunchTarget`.
- Create `DTXMania.E2E/Fixtures/E2EGameLaunch.cs` — one fixture/environment launch adapter.
- Create `DTXMania.E2E/AutomationContractTests.cs` — game producer vs Automation wire contracts.
- Modify `GameplayAutoPlaySmokeTests.cs`, `MidiGameplaySmokeTests.cs`, `DrumMappingStageSmokeTests.cs`, `CrashReportingSmokeTests.cs`.
- Delete extracted E2E helper implementations and pure support tests after migration.

### Repository/CI

- Modify `DTXMania.sln`.
- Modify `justfile`.
- Modify `.github/workflows/build-and-test.yml`.

---

## Task 1: Create Automation Projects and Launch Contracts

**Files:**

- Create `DTXMania.Automation/DTXMania.Automation.csproj`.
- Create `DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`.
- Create `DTXMania.Automation/Process/GameLaunchTarget.cs`.
- Create `DTXMania.Automation/Process/GameProcessStartOptions.cs`.
- Create `DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs`.
- Modify `DTXMania.sln`.

**Interfaces:**

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

- [ ] **Step 1: Create project files with current repository package versions**

`DTXMania.Automation/DTXMania.Automation.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

`DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <ProjectReference Include="..\DTXMania.Automation\DTXMania.Automation.csproj" />
  </ItemGroup>
</Project>
```

Do not upgrade existing packages.

- [ ] **Step 2: Add launch-target tests**

Required cases and exact outcomes:

- `Project_Windows_ShouldUseWindowsProject` -> `DTXMania.Game/DTXMania.Game.Windows.csproj`.
- `Project_Mac_ShouldUseMacProject` -> `DTXMania.Game/DTXMania.Game.Mac.csproj`.
- `Project_WithOverride_ShouldKeepExactOverride` -> exact non-blank override path is preserved.
- `Project_WithBlankOverride_ShouldUseDefaultProject` -> whitespace override falls back to the platform default, preserving current E2E behavior.
- `Executable_ShouldKeepExactCallerPath` -> exact executable path is preserved and `Kind == Executable`.
- `Executable_WithBlankPath_ShouldReject` -> `ArgumentException`.

Representative assertions:

```csharp
Assert.Equal(
    "DTXMania.Game/DTXMania.Game.Windows.csproj",
    GameLaunchTarget.Project(GamePlatform.Windows, " ").Path);

Assert.Throws<ArgumentException>(() =>
    GameLaunchTarget.Executable(GamePlatform.Windows, " "));
```

- [ ] **Step 3: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameLaunchTargetTests
```

Expected: compile/test failure because launch types are not implemented.

- [ ] **Step 4: Implement only the launch records/enums**

`Project` selects the override only when `!string.IsNullOrWhiteSpace(projectPathOverride)`; otherwise use the platform default. `Executable` rejects null/blank. Do not check file existence here.

- [ ] **Step 5: Add projects to solution and verify GREEN**

```bash
rtk dotnet sln DTXMania.sln add DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet sln DTXMania.sln add DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameLaunchTargetTests
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
```

- [ ] **Step 6: Commit**

```bash
rtk git add DTXMania.Automation DTXMania.Automation.Tests DTXMania.sln
rtk git commit -m "feat: add automation launch contracts"
```

---

## Task 2: Extract Owned Process Launch and Identity-Aware Startup Wait

**Files:**

- Create `DTXMania.Automation/Process/GameProcessDriver.cs`.
- Create `DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs`.
- Reuse behavior from `DTXMania.E2E/Process/GameProcessDriver.cs`.

**Interface:**

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

- [ ] **Step 1: Port the current generated-child process test harness**

Keep the existing temp `Child.csproj` approach. Extend the child with deterministic modes selected by `DTX_AUTOMATION_CHILD_MODE`:

```csharp
var mode = Environment.GetEnvironmentVariable("DTX_AUTOMATION_CHILD_MODE");

if (mode == "wait")
{
    await Task.Delay(TimeSpan.FromMinutes(1));
    return 0;
}

if (mode == "exit-early")
{
    Console.WriteLine("early-stdout");
    Console.Error.WriteLine("early-stderr");
    return 42;
}

if (mode == "burst")
{
    Console.Write(new string('x', 1_000_000));
    Console.Write("terminal-output");
    return 0;
}

Console.WriteLine("appdata=" + Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT"));
Console.WriteLine("token=" + Environment.GetEnvironmentVariable("DTXMANIA_LAUNCH_TOKEN"));
Console.WriteLine("set=" + Environment.GetEnvironmentVariable("DTX_AUTOMATION_SET"));
Console.WriteLine("removed=" + (Environment.GetEnvironmentVariable("DTX_AUTOMATION_REMOVE") ?? "<null>"));
Console.Error.WriteLine("child-stderr");
return 23;
```

- [ ] **Step 2: Add process-driver tests**

Required cases:

- `Start_ProjectTarget_ShouldSetOwnedEnvironmentAndDrainOutput`: launch the temp project; assert exit `23`, app-data/token values, set/remove override behavior, stdout/stderr.
- `Start_ExecutableTarget_ShouldLaunchExactBuiltAppHost`: build the same temp child once, resolve the generated apphost (`Child.exe` on Windows, `Child` otherwise), launch with `GameLaunchTarget.Executable`, assert exit `23`. This validates executable command construction without RID guessing.
- `Start_Twice_ShouldRejectSecondStart`: start `wait`, call `Start` again, assert `InvalidOperationException`.
- reserved app-data/launch-token generic overrides -> `ArgumentException`.
- `WaitForStartup_ShouldPassLaunchTokenAndOwnedProcessIdToProbe`: capture delegate arguments and assert they match start options / `ProcessId`.
- `WaitForStartup_WhenOwnedProcessExits_ShouldFailImmediately`: start `exit-early`, probe returns false, assert exception contains exit `42`, `early-stdout`, and `early-stderr`.
- `WaitForStartup_WhenProbeNeverMatches_ShouldTimeout`: `wait` child + always-false probe -> `TimeoutException`.
- `WaitForStartup_WhenCancelled_ShouldPropagateCancellation`: caller token cancellation -> `OperationCanceledException`.
- `DisposeAsync_CalledTwice_ShouldBeIdempotent`: no second-dispose exception.
- burst output is completely drained and contains `terminal-output` after natural exit/disposal.

Identity delegate assertion:

```csharp
string? observedToken = null;
int? observedPid = null;

await process.WaitForStartupAsync(
    (token, pid, _) =>
    {
        observedToken = token;
        observedPid = pid;
        return Task.FromResult(true);
    },
    TimeSpan.FromSeconds(1),
    TimeSpan.FromMilliseconds(10),
    CancellationToken.None);

Assert.Equal(startOptions.LaunchToken, observedToken);
Assert.Equal(process.ProcessId, observedPid);
```

- [ ] **Step 3: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameProcessDriverTests
```

- [ ] **Step 4: Move current driver behavior and replace `E2EFixture` with options**

Validate `WorkingDirectory`, `AppDataRoot`, `LaunchToken`, and target path before starting.

Command construction:

```csharp
if (options.Target.Kind == GameLaunchKind.Project)
{
    startInfo.FileName = "dotnet";
    startInfo.ArgumentList.Add("run");
    startInfo.ArgumentList.Add("--project");
    startInfo.ArgumentList.Add(options.Target.Path);
}
else
{
    startInfo.FileName = options.Target.Path;
}
```

Always use `UseShellExecute = false`, redirected stdout/stderr, `CreateNoWindow = true`, and `options.WorkingDirectory`.

- [ ] **Step 5: Preserve environment ownership**

```csharp
private const string AppDataRootEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
private const string LaunchTokenEnvironmentVariable = "DTXMANIA_LAUNCH_TOKEN";
```

Set both from options. Reject generic overrides for either case-insensitively. For other keys, null removes inherited state and non-null sets it. Remove the production `enableSimulatedMidi` boolean.

- [ ] **Step 6: Add identity-aware bounded startup polling**

Store the launch token after successful process creation. Poll with this order:

```csharp
if (_process.HasExited)
    throw CreateEarlyExitException(_process);

if (await launchHealthProbe(_launchToken, _process.Id, cancellationToken))
    return;

if (_process.HasExited)
    throw CreateEarlyExitException(_process);

await Task.Delay(interval, cancellationToken);
```

Use a bounded deadline/`Stopwatch`. Do not convert caller cancellation into timeout. Early-exit exception must include exit code and captured output.

- [ ] **Step 7: Preserve existing cleanup/output-drain behavior and verify GREEN**

Reuse current process-tree kill and exit-race handling rather than adding process abstractions.

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameProcessDriverTests
```

- [ ] **Step 8: Commit**

```bash
rtk git add DTXMania.Automation/Process DTXMania.Automation.Tests/Process
rtk git commit -m "feat: extract owned game process driver"
```

---

## Task 3: Extract JSON-RPC, Health Identity, Telemetry, and Eventually

**Files:**

- Create `DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs`.
- Create `DTXMania.Automation/JsonRpc/GameApiHealthSnapshot.cs`.
- Create `DTXMania.Automation/JsonRpc/GameApiInputType.cs`.
- Create `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs`.
- Create `DTXMania.Automation/Telemetry/GameStateSnapshot.cs`.
- Create `DTXMania.Automation/Support/Eventually.cs`.
- Create matching test files under `DTXMania.Automation.Tests`.

**Interfaces:**

```csharp
public sealed record GameApiConnectionOptions(Uri BaseUri, string ApiKey);
public sealed record GameApiHealthSnapshot(int? ProcessId, string? LaunchToken);

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

- [ ] **Step 1: Add `/health` identity tests with a fake `HttpMessageHandler`**

Required cases:

- `GetHealthAsync_ShouldParseNumericProcessIdAndLaunchToken`: `{"status":"ok","processId":1234,"launchToken":"abc"}` -> `1234`, `abc`.
- `GetHealthAsync_ShouldParseStringProcessId`: `{"processId":"1234","launchToken":"abc"}` -> `1234`.
- matching expected token returns true even if PID differs.
- missing/mismatched token but matching expected PID returns true, matching MCP fallback semantics.
- wrong token + wrong PID returns false.
- success response with malformed JSON returns false/null rather than throwing from readiness polling.
- connection refusal / HTTP timeout returns false for readiness unless caller cancellation token itself is canceled.

Representative identity rule:

```csharp
if (!string.IsNullOrWhiteSpace(expectedLaunchToken)
    && string.Equals(health.LaunchToken, expectedLaunchToken, StringComparison.Ordinal))
    return true;

return expectedProcessId.HasValue && health.ProcessId == expectedProcessId;
```

- [ ] **Step 2: Port JSON-RPC/input tests**

Preserve current request behavior and assert wire numeric values:

```text
KeyPress=2
KeyRelease=3
MidiNoteOn=4
MidiNoteOff=5
```

Add HTTP/protocol failure tests that verify useful method/status/body detail. Do not add `[REDACTED]` assertions or sanitizer code.

- [ ] **Step 3: Port telemetry tests as `GameStateSnapshotTests`**

Use raw camel-case JSON only in Automation.Tests. Preserve every current accessor/default behavior; do not reference `GameTelemetrySnapshot` from this project.

- [ ] **Step 4: Port `Eventually` tests**

Cover eventual success, timeout with last value, timeout retaining the last transient exception, and caller cancellation.

- [ ] **Step 5: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "FullyQualifiedName~JsonRpcGameClientTests|FullyQualifiedName~GameStateSnapshotTests|FullyQualifiedName~EventuallyTests"
```

- [ ] **Step 6: Port MCP health parsing without an MCP dependency**

`GetHealthAsync` sends GET `/health`; on success, parse `processId` as number or string and `launchToken` as string, matching `TryReadHealthIdentityAsync`. Malformed identity remains not-ready.

`IsHealthyForLaunchAsync` applies token-first, PID-fallback matching. Keep basic `IsHealthyAsync` for endpoint liveness, but startup must call the identity-aware method.

- [ ] **Step 7: Move/adapt current JSON-RPC client**

Use explicit base URI:

```csharp
private Uri Resolve(string relativePath) => new(_connection.BaseUri, relativePath);
```

Use `GameApiInputType` instead of game `InputType`. Keep generic send private. Send API key via `X-Api-Key` when non-blank. Do not add redaction infrastructure.

- [ ] **Step 8: Move/rename telemetry and move `Eventually`**

Keep behavior unchanged except namespace/type name.

- [ ] **Step 9: Verify GREEN and independence**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk rg -n "using DTXMania\.Game|using DTXMania\.E2E|using .*MCP|ProjectReference.*(DTXMania\.Game|DTXMania\.E2E|MCP)" DTXMania.Automation
```

Expected dependency scan: no matches.

- [ ] **Step 10: Commit**

```bash
rtk git add DTXMania.Automation/JsonRpc DTXMania.Automation/Telemetry DTXMania.Automation/Support DTXMania.Automation.Tests
rtk git commit -m "feat: extract game API automation client"
```

---

## Task 4: Centralize E2E Launch Policy and Migrate Smoke Tests

**Files:**

- Modify `DTXMania.E2E/DTXMania.E2E.csproj`.
- Modify `DTXMania.E2E/Fixtures/E2EGameProject.cs`.
- Create `DTXMania.E2E/Fixtures/E2EGameLaunch.cs`.
- Create `DTXMania.E2E/AutomationContractTests.cs`.
- Modify the four smoke suites.
- Delete old helper implementation/test files after migration.

**Adapter:**

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

- [ ] **Step 1: Reference Automation from E2E without retargeting E2E**

Add:

```xml
<ProjectReference Include="..\DTXMania.Automation\DTXMania.Automation.csproj" />
```

Keep existing conditional game references and `net8.0-windows7.0`.

- [ ] **Step 2: Make `E2EGameProject` the single project-selection policy owner**

```csharp
public static GameLaunchTarget ResolveLaunchTarget()
{
    var overridePath = Environment.GetEnvironmentVariable(GameProjectEnvironmentVariable);
    var platform = OperatingSystem.IsWindows()
        ? GamePlatform.Windows
        : GamePlatform.Mac;

    return GameLaunchTarget.Project(platform, overridePath);
}
```

Because blank override maps to platform default, this preserves current behavior.

- [ ] **Step 3: Add E2E launch-adapter tests**

Required cases:

- default launch explicitly includes `DTXMANIA_ENABLE_SIMULATED_MIDI = null` and a non-blank fresh launch token;
- `enableSimulatedMidi: true` sets the value to `"1"`;
- scenario extras such as `DTXMANIA_E2E_CRASH_INJECTION=update` are merged;
- `extraEnvironment` attempting to supply `DTXMANIA_ENABLE_SIMULATED_MIDI` case-insensitively throws `ArgumentException`.

Core assertions:

```csharp
var normal = E2EGameLaunch.CreateOptions(repoRoot, fixture);
Assert.Null(normal.EnvironmentOverrides!["DTXMANIA_ENABLE_SIMULATED_MIDI"]);
Assert.False(string.IsNullOrWhiteSpace(normal.LaunchToken));

var midi = E2EGameLaunch.CreateOptions(repoRoot, fixture, enableSimulatedMidi: true);
Assert.Equal("1", midi.EnvironmentOverrides!["DTXMANIA_ENABLE_SIMULATED_MIDI"]);
```

- [ ] **Step 4: Implement `E2EGameLaunch.CreateOptions`**

Use `E2EGameProject.ResolveLaunchTarget()`, `fixture.AppDataRoot`, and `Guid.NewGuid().ToString("N")` for launch token. Always put the MIDI key in the environment dictionary as `"1"` or `null`, then merge allowed scenario extras.

- [ ] **Step 5: Add producer/consumer contract tests**

Input values:

```csharp
Assert.Equal((int)InputType.MouseClick, (int)GameApiInputType.MouseClick);
Assert.Equal((int)InputType.MouseMove, (int)GameApiInputType.MouseMove);
Assert.Equal((int)InputType.KeyPress, (int)GameApiInputType.KeyPress);
Assert.Equal((int)InputType.KeyRelease, (int)GameApiInputType.KeyRelease);
Assert.Equal((int)InputType.MidiNoteOn, (int)GameApiInputType.MidiNoteOn);
Assert.Equal((int)InputType.MidiNoteOff, (int)GameApiInputType.MidiNoteOff);
```

Move the current producer telemetry round-trip here: serialize game `GameTelemetrySnapshot` with camel-case JSON and deserialize into Automation `GameStateSnapshot`, asserting all consumed fields.

- [ ] **Step 6: Migrate every smoke launch through the adapter**

Client construction:

```csharp
using var httpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
{
    Timeout = TimeSpan.FromSeconds(5)
};
var client = new JsonRpcGameClient(
    httpClient,
    new GameApiConnectionOptions(fixture.ApiBaseUri, fixture.ApiKey));
```

Normal startup:

```csharp
var startOptions = E2EGameLaunch.CreateOptions(repoRoot, fixture);
process.Start(startOptions);
await process.WaitForStartupAsync(
    client.IsHealthyForLaunchAsync,
    TimeSpan.FromSeconds(60),
    TimeSpan.FromMilliseconds(500),
    cancellationToken);
```

Midi smoke uses `enableSimulatedMidi: true`. Crash smoke passes only its crash-injection variable through `extraEnvironment`. DrumMapping and AutoPlay use normal launch.

Remove Midi/DrumMapping inline `DTXMANIA_E2E_GAME_PROJECT` reads.

- [ ] **Step 7: Delete old E2E helper copies and pure support tests**

Delete old `Process/GameProcessDriver.cs`, `JsonRpc/JsonRpcGameClient.cs`, `Telemetry/E2EGameState.cs`, `Support/Eventually.cs` and their pure unit tests after migrated call sites compile. Do not leave forwarding wrappers.

- [ ] **Step 8: Run E2E-support tests and policy scans**

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
rtk rg -n "DTXMANIA_E2E_GAME_PROJECT" DTXMania.E2E --glob '*.cs'
rtk rg -n "DTXMANIA_ENABLE_SIMULATED_MIDI" DTXMania.E2E --glob '*.cs'
```

Expected C# ownership:

```text
DTXMANIA_E2E_GAME_PROJECT       -> Fixtures/E2EGameProject.cs only
DTXMANIA_ENABLE_SIMULATED_MIDI  -> Fixtures/E2EGameLaunch.cs only
```

Scripts/workflows may continue setting `DTXMANIA_E2E_GAME_PROJECT` externally.

- [ ] **Step 9: Run Windows gameplay E2E when executing on Windows**

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

Preserve current smoke assertions and artifacts.

- [ ] **Step 10: Commit**

```bash
rtk git add DTXMania.E2E
rtk git commit -m "refactor: consume reusable automation from e2e"
```

---

## Task 5: Add Cross-Platform Validation and Finalize Extraction

**Files:**

- Modify `justfile`.
- Modify `.github/workflows/build-and-test.yml`.

- [ ] **Step 1: Add Automation test recipe**

```make
automation_test := "DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj"

automation-test:
    dotnet test {{ automation_test }}
```

Update `e2e-support` so it runs Automation.Tests plus remaining E2E-specific support/contract tests.

- [ ] **Step 2: Run Automation.Tests in existing Windows and macOS CI jobs**

Add to both platform jobs:

```text
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug --verbosity normal
```

Do not add macOS gameplay E2E.

- [ ] **Step 3: Run local support validation**

```bash
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
rtk just e2e-support
```

- [ ] **Step 4: Run scope/dependency scans**

```bash
rtk rg -n "ProjectReference.*DTXMania\.E2E" --glob '*.csproj'
rtk rg -n "DTXMania\.E2E\.(Process|JsonRpc|Telemetry|Support)" DTXMania.E2E DTXMania.Automation --glob '*.cs'
rtk rg -n "AutomationSession|IProcessRunner|IProcessLauncher|IJsonRpcTransport" DTXMania.Automation --glob '*.cs'
rtk rg -n "Sanitize|REDACTED" DTXMania.Automation --glob '*.cs'
```

Expected: no production E2E reference, no old helper namespaces, no new session/generic transport abstraction, and no redaction detour.

- [ ] **Step 5: Verify the HPA-503 consumer path contains no E2E types**

The reusable API must support this shape:

```csharp
var startOptions = new GameProcessStartOptions(
    repoRoot,
    GameLaunchTarget.Project(GamePlatform.Windows),
    appDataRoot,
    launchToken,
    environment);

await using var process = new GameProcessDriver();
process.Start(startOptions);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
var game = new JsonRpcGameClient(
    http,
    new GameApiConnectionOptions(apiBaseUri, apiKey));

await process.WaitForStartupAsync(
    game.IsHealthyForLaunchAsync,
    TimeSpan.FromSeconds(60),
    TimeSpan.FromMilliseconds(500),
    cancellationToken);
```

- [ ] **Step 6: Final validation**

```bash
rtk git diff --check
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

On Windows, also run E2E support and gameplay smoke before marking implementation ready.

- [ ] **Step 7: Commit**

```bash
rtk git add justfile .github/workflows/build-and-test.yml
rtk git commit -m "ci: validate automation cross platform"
```

## Plan Self-Review

- **Spec coverage:** launch targets, process ownership, launch-token/PID startup identity, JSON-RPC, telemetry, polling, E2E migration, centralized launch environment, and cross-platform tests each map to an explicit task.
- **Scope:** five sequential slices; no recorder orchestration, OBS, FFmpeg, HPA-510 behavior, generic framework, or DI work.
- **Package consistency:** current repository versions are copied exactly: `18.8.1 / 2.9.3 / 3.1.5`; no package upgrade.
- **Identity reuse:** startup ports MCP's token/PID rule but Automation has no MCP dependency.
- **E2E policy:** every smoke launch routes through `E2EGameLaunch`; every project selection routes through `E2EGameProject`.
- **Security scope:** API-key sanitizer/redaction is intentionally absent; HPA-503 must not persist API keys in recorder diagnostics.
- **Naming:** `Windows` / `Mac` describe project selection without claiming RID enforcement.
- **Blank override behavior:** null/blank project override uses the platform default consistently in Task 1 and E2E migration.

## Implementation Handoff

Use `superpowers:subagent-driven-development`. Give each task to a fresh implementation agent and review before moving to the next; Tasks 2-5 consume interfaces defined by earlier tasks.
