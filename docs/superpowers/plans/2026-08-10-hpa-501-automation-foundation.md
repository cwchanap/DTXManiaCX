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
- Executable launch requires an exact caller-supplied path; do not infer publish/RID/configuration paths.
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

Do not redesign these behaviors from scratch:

- Process lifecycle: `DTXMania.E2E/Process/GameProcessDriver.cs`.
- JSON-RPC client: `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`.
- Telemetry projection: `DTXMania.E2E/Telemetry/E2EGameState.cs`.
- Polling: `DTXMania.E2E/Support/Eventually.cs`.
- Launch identity check: `MCP/Server/GameInteractionService.cs::WaitForGameReadyAsync` and `TryReadHealthIdentityAsync`.
- `/health` producer fields: `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs` (`processId`, `launchToken`).

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

## Task 1: Create Automation Projects and Explicit Launch Contracts

**Files:**

- Create `DTXMania.Automation/DTXMania.Automation.csproj`.
- Create `DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`.
- Create `DTXMania.Automation/Process/GameLaunchTarget.cs`.
- Create `DTXMania.Automation/Process/GameProcessStartOptions.cs`.
- Create `DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs`.
- Modify `DTXMania.sln`.

**Interfaces produced:**

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

- [ ] **Step 1: Create the project files with repository-current package versions**

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

Do not change package versions in existing projects.

- [ ] **Step 2: Add failing launch-contract tests**

```csharp
[Fact]
public void Project_Windows_ShouldUseWindowsProject()
{
    var target = GameLaunchTarget.Project(GamePlatform.Windows);
    Assert.Equal(GameLaunchKind.Project, target.Kind);
    Assert.Equal("DTXMania.Game/DTXMania.Game.Windows.csproj", target.Path);
}

[Fact]
public void Project_Mac_ShouldUseMacProject()
{
    var target = GameLaunchTarget.Project(GamePlatform.Mac);
    Assert.Equal("DTXMania.Game/DTXMania.Game.Mac.csproj", target.Path);
}

[Fact]
public void Project_WithOverride_ShouldKeepExactPath()
{
    var target = GameLaunchTarget.Project(GamePlatform.Windows, "custom/Game.csproj");
    Assert.Equal("custom/Game.csproj", target.Path);
}

[Fact]
public void Executable_ShouldKeepCallerPathWithoutRidInference()
{
    var target = GameLaunchTarget.Executable(GamePlatform.Mac, "/tmp/DTXMania.Game");
    Assert.Equal(GameLaunchKind.Executable, target.Kind);
    Assert.Equal("/tmp/DTXMania.Game", target.Path);
}
```

Also reject blank explicit project overrides and executable paths.

- [ ] **Step 3: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameLaunchTargetTests
```

Expected: compile failure because launch types do not exist.

- [ ] **Step 4: Implement the minimal records/enums**

Use a switch expression for project defaults and `ArgumentException.ThrowIfNullOrWhiteSpace` for explicit paths. Do not check file existence in the value object.

- [ ] **Step 5: Add both projects to the solution and verify GREEN**

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
- Reference `DTXMania.E2E/Process/GameProcessDriver.cs` for move-first behavior.

**Interface produced:**

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

- [ ] **Step 1: Port the current temp-child process tests and add identity cases**

Keep the generated `Child.csproj` strategy so tests execute a real child without MonoGame. Give the child modes for normal exit, early exit, long wait, and burst stdout.

Required tests:

```csharp
[Fact]
public async Task Start_ProjectTarget_ShouldSetOwnedEnvironmentAndDrainOutput() { /* real temp child */ }

[Fact]
public async Task Start_Twice_ShouldRejectSecondStart() { /* long-running child */ }

[Fact]
public async Task WaitForStartup_ShouldPassLaunchTokenAndOwnedProcessIdToProbe()
{
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
}

[Fact]
public async Task WaitForStartup_WhenOwnedProcessExits_ShouldFailWithExitAndOutput() { /* exit code 42 */ }

[Fact]
public async Task WaitForStartup_WhenProbeNeverMatches_ShouldTimeout() { /* long-running child */ }

[Fact]
public async Task WaitForStartup_WhenCancelled_ShouldPropagateCancellation() { /* caller token */ }

[Fact]
public async Task DisposeAsync_CalledTwice_ShouldBeIdempotent() { /* long-running child */ }
```

Retain the existing burst-output terminal-drain regression test.

- [ ] **Step 2: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameProcessDriverTests
```

- [ ] **Step 3: Move the current driver and replace `E2EFixture` with options**

Core validation:

```csharp
ArgumentNullException.ThrowIfNull(options);
ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);
ArgumentException.ThrowIfNullOrWhiteSpace(options.AppDataRoot);
ArgumentException.ThrowIfNullOrWhiteSpace(options.LaunchToken);
```

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

- [ ] **Step 4: Preserve environment ownership exactly**

```csharp
private const string AppDataRootEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
private const string LaunchTokenEnvironmentVariable = "DTXMANIA_LAUNCH_TOKEN";
```

Set both before generic overrides. Reject either reserved name case-insensitively. For all other overrides, null removes the inherited variable and non-null sets it.

Do not retain `enableSimulatedMidi` on the production driver.

- [ ] **Step 5: Add identity-aware bounded startup polling**

Store the launch token after successful process creation. On each iteration:

```csharp
if (_process.HasExited)
    throw CreateEarlyExitException(_process);

if (await launchHealthProbe(_launchToken, _process.Id, cancellationToken))
    return;

if (_process.HasExited)
    throw CreateEarlyExitException(_process);

await Task.Delay(interval, cancellationToken);
```

Use a deadline/`Stopwatch` so timeout is bounded. Early-exit exception includes exit code and captured stdout/stderr. Caller cancellation must not be converted into timeout.

- [ ] **Step 6: Preserve process-tree cleanup/output drain from the existing driver**

Move current kill-race handling and final stdout/stderr drain rather than replacing it with new process abstractions.

- [ ] **Step 7: Verify GREEN**

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
- Create matching tests under `DTXMania.Automation.Tests`.
- Reference E2E JSON-RPC/telemetry/support sources and MCP health parsing as move/port sources.

**Interfaces produced:**

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

`JsonRpcGameClient` public methods:

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

- [ ] **Step 1: Add failing `/health` identity tests using a fake `HttpMessageHandler`**

Required cases:

```csharp
[Fact]
public async Task GetHealthAsync_ShouldParseProcessIdAndLaunchToken()
{
    // GET /health -> {"status":"ok","processId":1234,"launchToken":"abc"}
    // Assert ProcessId=1234 and LaunchToken="abc".
}

[Fact]
public async Task IsHealthyForLaunchAsync_MatchingToken_ShouldReturnTrue() { /* PID may differ */ }

[Fact]
public async Task IsHealthyForLaunchAsync_MatchingProcessId_ShouldReturnTrue() { /* token may differ/missing */ }

[Fact]
public async Task IsHealthyForLaunchAsync_WrongTokenAndPid_ShouldReturnFalse() { }

[Fact]
public async Task IsHealthyForLaunchAsync_MalformedHealthPayload_ShouldReturnFalse() { }

[Fact]
public async Task IsHealthyForLaunchAsync_ConnectionFailure_ShouldReturnFalse() { }
```

This is the port of MCP's stale-process protection. Do not call MCP from the tests or production library.

- [ ] **Step 2: Add failing JSON-RPC/input tests**

Port current `JsonRpcGameClientTests` and assert numeric payloads remain:

```text
KeyPress=2
KeyRelease=3
MidiNoteOn=4
MidiNoteOff=5
```

Add HTTP/protocol failure tests that assert useful method/status/body information. Do not assert `[REDACTED]` and do not add sanitizer code.

- [ ] **Step 3: Add failing telemetry tests**

Rename the pure E2E DTO tests to `GameStateSnapshotTests`. Use raw camel-case JSON only; Automation.Tests must not reference game producer types.

Preserve every current accessor/default behavior, including score/save state, autoplay, playback profile, audio preparation, timing, lane-hit, and stage completion fields.

- [ ] **Step 4: Add failing `Eventually` tests**

Cover eventual success, timeout with last value, timeout retaining last transient exception, and caller cancellation.

- [ ] **Step 5: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "FullyQualifiedName~JsonRpcGameClientTests|FullyQualifiedName~GameStateSnapshotTests|FullyQualifiedName~EventuallyTests"
```

- [ ] **Step 6: Port MCP health parsing into the Automation client**

Resolve request URIs from the explicit base URI:

```csharp
private Uri Resolve(string relativePath) => new(_connection.BaseUri, relativePath);
```

`GetHealthAsync` sends GET `/health`, returns null for non-success/transient HTTP/malformed JSON, and parses number-or-string `processId` plus string `launchToken`, matching MCP's tolerant parser.

`IsHealthyForLaunchAsync`:

```csharp
var health = await GetHealthAsync(cancellationToken);
if (health is null)
    return false;

if (!string.IsNullOrWhiteSpace(expectedLaunchToken)
    && string.Equals(health.LaunchToken, expectedLaunchToken, StringComparison.Ordinal))
    return true;

return expectedProcessId.HasValue && health.ProcessId == expectedProcessId;
```

`IsHealthyAsync` keeps endpoint-liveness semantics; startup call sites must use `IsHealthyForLaunchAsync` through `GameProcessDriver.WaitForStartupAsync`.

- [ ] **Step 7: Move/adapt the current JSON-RPC client**

Use `GameApiInputType` instead of game `InputType`. Keep generic transport private. Continue sending API key via `X-Api-Key` only when non-blank.

Do not add secret-sanitization infrastructure in this task.

- [ ] **Step 8: Move/rename telemetry and move `Eventually`**

Keep behavior intact except namespace/type name. Do not prune telemetry fields while extracting.

- [ ] **Step 9: Verify GREEN and independence**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk rg -n "DTXMania\.Game|DTXMania\.E2E|MCP" DTXMania.Automation --glob '*.cs' --glob '*.csproj'
```

Expected: tests pass; dependency scan has no project/type dependency (comments documenting source reuse are acceptable only if they do not become namespace imports/references).

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

**E2E adapter produced:**

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

- [ ] **Step 1: Reference Automation from E2E without changing E2E target framework**

Add:

```xml
<ProjectReference Include="..\DTXMania.Automation\DTXMania.Automation.csproj" />
```

Keep existing conditional game reference and `net8.0-windows7.0`.

- [ ] **Step 2: Change the single project-policy helper**

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

`GameLaunchTarget.Project` must treat null/blank override as default path, so the helper does not duplicate project constants.

- [ ] **Step 3: Add `E2EGameLaunch` tests before migrating call sites**

Place these E2E-support tests in the E2E project because they depend on `E2EFixture`.

Required behavior:

```csharp
[Fact]
public void CreateOptions_Default_ShouldExplicitlyRemoveSimulatedMidi()
{
    var options = E2EGameLaunch.CreateOptions(repoRoot, fixture);
    Assert.Null(options.EnvironmentOverrides!["DTXMANIA_ENABLE_SIMULATED_MIDI"]);
    Assert.False(string.IsNullOrWhiteSpace(options.LaunchToken));
}

[Fact]
public void CreateOptions_EnableMidi_ShouldSetSimulatedMidi()
{
    var options = E2EGameLaunch.CreateOptions(repoRoot, fixture, enableSimulatedMidi: true);
    Assert.Equal("1", options.EnvironmentOverrides!["DTXMANIA_ENABLE_SIMULATED_MIDI"]);
}

[Fact]
public void CreateOptions_ExtraEnvironment_ShouldMergeScenarioValues()
{
    var options = E2EGameLaunch.CreateOptions(
        repoRoot,
        fixture,
        extraEnvironment: new Dictionary<string, string?>
        {
            ["DTXMANIA_E2E_CRASH_INJECTION"] = "update"
        });

    Assert.Equal("update", options.EnvironmentOverrides!["DTXMANIA_E2E_CRASH_INJECTION"]);
}

[Fact]
public void CreateOptions_ExtraEnvironmentCannotOverrideMidiPolicy()
{
    Assert.Throws<ArgumentException>(() => E2EGameLaunch.CreateOptions(
        repoRoot,
        fixture,
        extraEnvironment: new Dictionary<string, string?>
        {
            ["DTXMANIA_ENABLE_SIMULATED_MIDI"] = "1"
        }));
}
```

- [ ] **Step 4: Implement `E2EGameLaunch.CreateOptions` as the only smoke-launch adapter**

Use `E2EGameProject.ResolveLaunchTarget()`, `fixture.AppDataRoot`, and a fresh `Guid.NewGuid().ToString("N")` launch token.

Build one environment dictionary that always contains:

```csharp
["DTXMANIA_ENABLE_SIMULATED_MIDI"] = enableSimulatedMidi ? "1" : null
```

Merge scenario extras after rejecting a case-insensitive attempt to supply the MIDI policy key.

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

Move the current producer-side telemetry round-trip from `E2EGameStateTests` here: serialize `GameTelemetrySnapshot` with camel-case options and deserialize into `GameStateSnapshot`, asserting all currently consumed fields.

- [ ] **Step 6: Migrate all four smoke suites**

Namespaces:

```csharp
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
```

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

Normal launch:

```csharp
var startOptions = E2EGameLaunch.CreateOptions(repoRoot, fixture);
process.Start(startOptions);
await process.WaitForStartupAsync(
    client.IsHealthyForLaunchAsync,
    TimeSpan.FromSeconds(60),
    TimeSpan.FromMilliseconds(500),
    cancellationToken);
```

MIDI launch:

```csharp
var startOptions = E2EGameLaunch.CreateOptions(
    repoRoot,
    fixture,
    enableSimulatedMidi: true);
```

Crash launch:

```csharp
var startOptions = E2EGameLaunch.CreateOptions(
    repoRoot,
    fixture,
    extraEnvironment: new Dictionary<string, string?>
    {
        ["DTXMANIA_E2E_CRASH_INJECTION"] = injectionPoint
    });
```

Do not leave raw project-path selection or one-off simulated-MIDI dictionaries in smoke tests.

- [ ] **Step 7: Delete extracted E2E copies after migration compiles**

Delete the old process/JSON-RPC/telemetry/support implementations and their pure support tests. Do not leave forwarding wrappers.

- [ ] **Step 8: Run focused E2E-support tests and policy scans**

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

Scripts/workflows may still set `DTXMANIA_E2E_GAME_PROJECT` externally.

- [ ] **Step 9: Run Windows gameplay E2E when available**

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

Existing smoke assertions/artifacts must remain unchanged apart from helper type names.

- [ ] **Step 10: Commit**

```bash
rtk git add DTXMania.E2E
rtk git commit -m "refactor: consume reusable automation from e2e"
```

---

## Task 5: Add Cross-Platform Automation Validation and Finalize the Extraction

**Files:**

- Modify `justfile`.
- Modify `.github/workflows/build-and-test.yml`.
- Review `DTXMania.sln` / project references from prior tasks.

- [ ] **Step 1: Add the dedicated test recipe**

Add:

```make
automation_test := "DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj"

automation-test:
    dotnet test {{ automation_test }}
```

Update `e2e-support` so local support validation runs Automation.Tests plus remaining E2E-specific support/contract tests.

- [ ] **Step 2: Run Automation.Tests in both existing Windows and macOS CI jobs**

Add a step to each platform build/test job:

```text
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug --verbosity normal
```

Do not add macOS gameplay E2E.

- [ ] **Step 3: Run the platform-neutral suite and build locally on the current host**

```bash
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
rtk just e2e-support
```

- [ ] **Step 4: Run repository dependency/scope scans**

```bash
rtk rg -n "ProjectReference.*DTXMania\.E2E" --glob '*.csproj'
rtk rg -n "DTXMania\.E2E\.(Process|JsonRpc|Telemetry|Support)" DTXMania.E2E DTXMania.Automation --glob '*.cs'
rtk rg -n "AutomationSession|IProcessRunner|IProcessLauncher|IJsonRpcTransport" DTXMania.Automation --glob '*.cs'
rtk rg -n "Sanitize|REDACTED" DTXMania.Automation --glob '*.cs'
```

Expected:

- no production project references E2E;
- old helper namespaces are gone;
- no session/generic process/transport abstraction was introduced;
- no API-key redaction detour was introduced.

- [ ] **Step 5: Verify HPA-503 consumer shape by compile-level inspection**

A future recorder must be able to perform this without importing E2E:

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

No E2E type should appear in this dependency path.

- [ ] **Step 6: Final validation**

```bash
rtk git diff --check
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

On Windows, also run existing E2E support and gameplay smoke before marking implementation ready for review.

- [ ] **Step 7: Commit**

```bash
rtk git add justfile .github/workflows/build-and-test.yml
rtk git commit -m "ci: validate automation cross platform"
```

## Plan Self-Review

- **Spec coverage:** launch targets, process ownership, launch-token/PID startup identity, JSON-RPC, telemetry, polling, E2E migration, centralized launch environment, and cross-platform tests all map to explicit tasks.
- **Scope:** five sequential slices; no recorder orchestration, OBS, FFmpeg, HPA-510 behavior, generic framework, or DI work.
- **Package consistency:** uses the current E2E versions `18.8.1 / 2.9.3 / 3.1.5`; no package upgrade.
- **Identity reuse:** startup health ports MCP's proven identity rule but keeps MCP out of the dependency graph.
- **E2E policy:** every smoke launch routes through `E2EGameLaunch` and every project selection through `E2EGameProject`.
- **Security scope:** API-key sanitizer/redaction work is intentionally absent from HPA-501; HPA-503 must not persist API keys in its own diagnostics.
- **Naming:** `Windows` / `Mac` describe project selection without claiming RID enforcement.

## Implementation Handoff

Use `superpowers:subagent-driven-development`. Give each task to a fresh implementation agent and review before moving to the next; Tasks 2-5 consume interfaces defined by earlier tasks.
