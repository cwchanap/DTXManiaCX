# HPA-501 Reusable CX Automation Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the proven CX process, Game API, telemetry, and polling helpers into plain `net8.0` Automation projects, while making startup identity reliable for recorder use and centralizing E2E launch policy.

**Architecture:** Add `DTXMania.Automation` plus `DTXMania.Automation.Tests`. Keep process lifecycle, JSON-RPC transport, telemetry projection, and polling as small primitives. The process driver owns launch-readiness semantics: project launch requires the exact launch token; direct executable launch may fall back to PID. E2E gets one local launch-policy owner for repo root, port, project override, and simulated MIDI. HPA-503 remains the recorder lifecycle owner.

**Tech Stack:** .NET 8, C#, xUnit, `System.Diagnostics.Process`, `HttpClient`, `System.Text.Json`, existing CX Game API/E2E fixtures, GitHub Actions.

## Global Constraints

- Source of truth: [`2026-08-10-hpa-501-automation-foundation-design.md`](../specs/2026-08-10-hpa-501-automation-foundation-design.md).
- Automation and Automation.Tests target plain `net8.0`.
- Automation must not reference E2E, MonoGame game projects, or MCP.
- Automation.Tests references only Automation plus test packages.
- Keep E2E on `net8.0-windows7.0`; do not claim macOS gameplay-E2E support.
- Use current E2E/main-test package versions in Automation.Tests: `Microsoft.NET.Test.Sdk 18.8.1`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.5`.
- Do not normalize unrelated package versions in `DTXMania.Test.Mac.csproj`.
- `GameLaunchTarget` stores only `Kind` and `Path`; no unused platform/RID state.
- Project path defaults come from `GameProjectPaths.Windows` / `.Mac` / `.Current`.
- Executable launch uses the exact caller path; do not infer RID, publish layout, app bundle, or configuration.
- `DTXMANIA_APPDATA_ROOT` and `DTXMANIA_LAUNCH_TOKEN` are driver-owned and cannot be generic overrides.
- Project readiness requires launch-token equality; its `dotnet run` PID is not readiness evidence.
- Direct executable readiness accepts matching token or matching owned executable PID.
- Startup timeout must contain the last observed health identity.
- Simulated MIDI is E2E policy, not a production-driver boolean.
- `GetHealthAsync` is the only public health transport method; do not add `IsHealthyAsync` or `IsHealthyForLaunchAsync`.
- Keep generic JSON-RPC send private.
- Preserve input wire values `0..5` exactly.
- No API-key sanitizer/redaction work in HPA-501.
- Preserve owned-process-tree cleanup; never discover/kill unrelated CX processes.
- No `AutomationSession`, DI, generic process/transport framework, OBS, FFmpeg, recorder CLI/sandbox, HPA-510 commands, or compatibility shims.
- Use RED -> focused GREEN -> review -> commit for each task.

## Existing Behavior to Reuse

- Process lifecycle: `DTXMania.E2E/Process/GameProcessDriver.cs`.
- JSON-RPC client: `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`.
- Telemetry projection: `DTXMania.E2E/Telemetry/E2EGameState.cs`.
- Polling: `DTXMania.E2E/Support/Eventually.cs`.
- Launch-token health parsing semantics: `MCP/Server/GameInteractionService.cs::WaitForGameReadyAsync` / `TryReadHealthIdentityAsync`.
- `/health` producer: `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs` (`processId`, `launchToken`).
- E2E process serialization: `DTXMania.E2E/AssemblyInfo.cs`.

## File Responsibility Map

### New production project

- `DTXMania.Automation/DTXMania.Automation.csproj` — plain `net8.0` library.
- `DTXMania.Automation/Process/GameProjectPaths.cs` — Windows/Mac/default project paths.
- `DTXMania.Automation/Process/GameLaunchTarget.cs` — `Project` / `Executable` + exact path only.
- `DTXMania.Automation/Process/GameProcessStartOptions.cs` — working directory, target, app-data root, launch token, environment overrides.
- `DTXMania.Automation/Process/GameHealthSnapshot.cs` — process ID + launch token observation.
- `DTXMania.Automation/Process/GameProcessDriver.cs` — one owned process, output capture, readiness, exit wait, cleanup.
- `DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs` — base URI and API key.
- `DTXMania.Automation/JsonRpc/GameApiInputType.cs` — stable consumer-side input wire values.
- `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs` — health observation + explicit Game API commands.
- `DTXMania.Automation/Telemetry/GameStateSnapshot.cs` — consumer telemetry projection.
- `DTXMania.Automation/Support/Eventually.cs` — bounded polling.

### New Automation tests

- `DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`.
- `DTXMania.Automation.Tests/AssemblyInfo.cs` — disable test parallelization.
- `DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs`.
- `DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs` — trait `Category=Automation-Process`.
- `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs`.
- `DTXMania.Automation.Tests/Telemetry/GameStateSnapshotTests.cs`.
- `DTXMania.Automation.Tests/Support/EventuallyTests.cs`.

### E2E migration

- Modify `DTXMania.E2E/DTXMania.E2E.csproj` — reference Automation, keep current TFM/game refs.
- Modify `DTXMania.E2E/Fixtures/E2EGameProject.cs` — only reader of `DTXMANIA_E2E_GAME_PROJECT`.
- Create `DTXMania.E2E/Fixtures/E2EGameLaunch.cs` — repo root, API port, launch options, MIDI policy.
- Create `DTXMania.E2E/AutomationContractTests.cs` — producer/consumer wire contracts.
- Modify `GameplayAutoPlaySmokeTests.cs`, `MidiGameplaySmokeTests.cs`, `DrumMappingStageSmokeTests.cs`, `CrashReportingSmokeTests.cs`.
- Delete the extracted E2E process/JSON-RPC/telemetry/support copies and their pure unit tests.

### Repository/CI

- Modify `DTXMania.sln`.
- Modify `justfile`.
- Modify `.github/workflows/build-and-test.yml`.

---

## Task 1: Create Automation Projects and Minimal Launch Contracts

**Files:**

- Create `DTXMania.Automation/DTXMania.Automation.csproj`.
- Create `DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`.
- Create `DTXMania.Automation.Tests/AssemblyInfo.cs`.
- Create `DTXMania.Automation/Process/GameProjectPaths.cs`.
- Create `DTXMania.Automation/Process/GameLaunchTarget.cs`.
- Create `DTXMania.Automation/Process/GameProcessStartOptions.cs`.
- Create `DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs`.
- Modify `DTXMania.sln`.

**Interfaces produced:**

```csharp
namespace DTXMania.Automation.Process;

public static class GameProjectPaths
{
    public const string Windows = "DTXMania.Game/DTXMania.Game.Windows.csproj";
    public const string Mac = "DTXMania.Game/DTXMania.Game.Mac.csproj";
    public static string Current { get; }
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

public sealed record GameProcessStartOptions(
    string WorkingDirectory,
    GameLaunchTarget Target,
    string AppDataRoot,
    string LaunchToken,
    IReadOnlyDictionary<string, string?>? EnvironmentOverrides = null);
```

- [ ] **Step 1: Create the project files with current repository package versions**

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

Do not change any existing package version.

- [ ] **Step 2: Carry over E2E's process-test serialization policy**

`DTXMania.Automation.Tests/AssemblyInfo.cs`:

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

Keep this assembly-wide. The fast HTTP/DTO tests are cheap to serialize and a single rule is safer than collection-level exceptions.

- [ ] **Step 3: Write launch-contract tests**

Required cases:

```csharp
[Fact]
public void ProjectPaths_ShouldExposeExactRepositoryPaths()
{
    Assert.Equal("DTXMania.Game/DTXMania.Game.Windows.csproj", GameProjectPaths.Windows);
    Assert.Equal("DTXMania.Game/DTXMania.Game.Mac.csproj", GameProjectPaths.Mac);
}

[Fact]
public void Project_WithOverride_ShouldKeepExactOverride()
{
    var target = GameLaunchTarget.Project("custom/Game.csproj");
    Assert.Equal(GameLaunchKind.Project, target.Kind);
    Assert.Equal("custom/Game.csproj", target.Path);
}

[Fact]
public void Project_WithBlankOverride_ShouldUseCurrentPlatformDefault()
{
    var target = GameLaunchTarget.Project(" ");
    Assert.Equal(GameProjectPaths.Current, target.Path);
}

[Fact]
public void Executable_ShouldKeepExactCallerPath()
{
    var target = GameLaunchTarget.Executable("/tmp/DTXMania.Game");
    Assert.Equal(GameLaunchKind.Executable, target.Kind);
    Assert.Equal("/tmp/DTXMania.Game", target.Path);
}

[Fact]
public void Executable_WithBlankPath_ShouldReject()
{
    Assert.Throws<ArgumentException>(() => GameLaunchTarget.Executable(" "));
}
```

`GameProjectPaths.Current` must select Windows on Windows, Mac on macOS, and throw `PlatformNotSupportedException` elsewhere.

- [ ] **Step 4: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameLaunchTargetTests
```

Expected: compile/test failure because launch types do not exist.

- [ ] **Step 5: Implement only the launch contracts and add projects to the solution**

```bash
rtk dotnet sln DTXMania.sln add DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet sln DTXMania.sln add DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

Do not add file-existence validation or publish-output inference.

- [ ] **Step 6: Verify GREEN**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameLaunchTargetTests
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
```

- [ ] **Step 7: Commit**

```bash
rtk git add DTXMania.Automation DTXMania.Automation.Tests DTXMania.sln
rtk git commit -m "feat: add automation launch contracts"
```

---

## Task 2: Extract the Owned Process Driver and Correct Startup Identity

**Files:**

- Create `DTXMania.Automation/Process/GameHealthSnapshot.cs`.
- Create `DTXMania.Automation/Process/GameProcessDriver.cs`.
- Create `DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs`.
- Reuse `DTXMania.E2E/Process/GameProcessDriver.cs`.

**Interfaces produced:**

```csharp
public sealed record GameHealthSnapshot(int? ProcessId, string? LaunchToken);

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

- [ ] **Step 1: Port the current generated-child test harness**

Keep a tiny temp `Child.csproj` and deterministic modes:

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

Mark the class:

```csharp
[Trait("Category", "Automation-Process")]
public sealed class GameProcessDriverTests
```

- [ ] **Step 2: Add process lifecycle tests**

Required cases:

- project target sets driver-owned app-data/token plus generic set/remove overrides and drains stdout/stderr;
- direct executable target runs the exact built apphost path;
- duplicate `Start` rejects;
- generic override of app-data/launch-token rejects case-insensitively;
- natural exit/burst output drains terminal text;
- double disposal is idempotent;
- caller cancellation propagates;
- owned process early exit reports exit code/stdout/stderr.

- [ ] **Step 3: Add the readiness tests that distinguish Project and Executable semantics**

**Project token is authoritative:**

```csharp
[Fact]
public async Task WaitForStartup_ProjectMatchingToken_ShouldSucceed()
{
    process.Start(projectOptions);

    await process.WaitForStartupAsync(
        _ => Task.FromResult<GameHealthSnapshot?>(
            new(processId: process.ProcessId, launchToken: projectOptions.LaunchToken)),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(10),
        CancellationToken.None);
}
```

**Project PID must not bypass token mismatch:**

```csharp
[Fact]
public async Task WaitForStartup_ProjectMatchingPidButWrongToken_ShouldTimeoutWithObservedIdentity()
{
    process.Start(projectOptions);

    var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
        process.WaitForStartupAsync(
            _ => Task.FromResult<GameHealthSnapshot?>(
                new(processId: process.ProcessId, launchToken: "stale-token")),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None));

    Assert.Contains("stale-token", ex.Message, StringComparison.Ordinal);
    Assert.Contains(process.ProcessId!.Value.ToString(), ex.Message, StringComparison.Ordinal);
}
```

**Executable PID fallback is valid:**

```csharp
[Fact]
public async Task WaitForStartup_ExecutableMatchingPid_ShouldSucceedWhenTokenDoesNotMatch()
{
    process.Start(executableOptions);

    await process.WaitForStartupAsync(
        _ => Task.FromResult<GameHealthSnapshot?>(
            new(processId: process.ProcessId, launchToken: "different-token")),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(10),
        CancellationToken.None);
}
```

Also test no parseable health -> timeout message says no health identity observed.

- [ ] **Step 4: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "Category=Automation-Process"
```

- [ ] **Step 5: Move the current process lifecycle behavior and replace `E2EFixture` with options**

Command construction remains:

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

Always use `UseShellExecute=false`, redirected output/error, `CreateNoWindow=true`, and `options.WorkingDirectory`.

- [ ] **Step 6: Preserve environment ownership exactly**

```csharp
private const string AppDataRootEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
private const string LaunchTokenEnvironmentVariable = "DTXMANIA_LAUNCH_TOKEN";
```

Set both from options before generic overrides. Null generic values remove inherited state. Remove `enableSimulatedMidi` from the production driver.

- [ ] **Step 7: Implement launch-kind-aware readiness and timeout diagnostics**

Store launch kind/token after successful `Start`.

Pseudo-code:

```csharp
GameHealthSnapshot? lastObserved = null;
var deadline = Stopwatch.StartNew();

while (deadline.Elapsed < timeout)
{
    cancellationToken.ThrowIfCancellationRequested();
    ThrowIfOwnedProcessExited();

    var health = await healthProbe(cancellationToken);
    if (health is not null)
    {
        lastObserved = health;

        if (string.Equals(health.LaunchToken, _launchToken, StringComparison.Ordinal))
            return;

        if (_launchKind == GameLaunchKind.Executable
            && health.ProcessId.HasValue
            && health.ProcessId == _process.Id)
        {
            return;
        }
    }

    ThrowIfOwnedProcessExited();
    await Task.Delay(interval, cancellationToken);
}

throw CreateStartupTimeoutException(lastObserved);
```

`CreateStartupTimeoutException` includes launch kind, owned PID, and last observed PID/token or an explicit no-observation marker.

- [ ] **Step 8: Preserve cleanup/output-drain behavior and verify GREEN**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "Category=Automation-Process"
```

- [ ] **Step 9: Commit**

```bash
rtk git add DTXMania.Automation/Process DTXMania.Automation.Tests/Process
rtk git commit -m "feat: extract owned game process driver"
```

---

## Task 3: Extract Game API Transport, Telemetry, and Polling

**Files:**

- Create `DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs`.
- Create `DTXMania.Automation/JsonRpc/GameApiInputType.cs`.
- Create `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs`.
- Create `DTXMania.Automation/Telemetry/GameStateSnapshot.cs`.
- Create `DTXMania.Automation/Support/Eventually.cs`.
- Create matching test files under Automation.Tests.

**Interfaces produced:**

```csharp
public sealed record GameApiConnectionOptions(Uri BaseUri, string ApiKey);

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
Task<GameHealthSnapshot?> GetHealthAsync(CancellationToken cancellationToken);
Task<GameStateSnapshot> GetGameStateAsync(CancellationToken cancellationToken);
Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken);
Task SendMidiNoteAsync(int noteNumber, int velocity, TimeSpan holdDuration, CancellationToken cancellationToken);
Task ChangeStageAsync(string stageName, CancellationToken cancellationToken);
Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken);
```

- [ ] **Step 1: Add `/health` parsing tests using a fake `HttpMessageHandler`**

Required cases:

```text
{"processId":1234,"launchToken":"abc"}   -> GameHealthSnapshot(1234,"abc")
{"processId":"1234","launchToken":"abc"} -> GameHealthSnapshot(1234,"abc")
```

Also cover:

- success response with missing identity fields -> snapshot with null fields;
- malformed JSON -> null;
- non-success HTTP -> null;
- transient `HttpRequestException` / per-request timeout -> null;
- caller cancellation -> propagate `OperationCanceledException`.

Do not implement launch matching in the client.

- [ ] **Step 2: Port current JSON-RPC/input tests**

Assert the numeric wire payloads remain:

```text
KeyPress=2
KeyRelease=3
MidiNoteOn=4
MidiNoteOff=5
```

Add HTTP/protocol failure tests with useful method/status/body detail. Do not add sanitizer assertions.

- [ ] **Step 3: Port telemetry tests as `GameStateSnapshotTests`**

Use raw camel-case JSON only in Automation.Tests. Preserve every current accessor/default behavior; do not reference game producer types here.

- [ ] **Step 4: Port `Eventually` tests**

Cover eventual success, timeout with last value, timeout retaining last transient exception, and caller cancellation.

- [ ] **Step 5: Verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "FullyQualifiedName~JsonRpcGameClientTests|FullyQualifiedName~GameStateSnapshotTests|FullyQualifiedName~EventuallyTests"
```

- [ ] **Step 6: Port health parsing from MCP and JSON-RPC behavior from E2E**

Use explicit base URI:

```csharp
private Uri Resolve(string relativePath) => new(_connection.BaseUri, relativePath);
```

`GetHealthAsync` only parses/returns observation; it does not decide readiness.

Use `GameApiInputType` instead of game `InputType`. Keep generic transport private. Send API key via `X-Api-Key` when non-blank.

Do not add `IsHealthyAsync`, `IsHealthyForLaunchAsync`, logging infrastructure, or redaction helpers.

- [ ] **Step 7: Move/rename telemetry and move `Eventually`**

Keep behavior unchanged except namespace/type names.

- [ ] **Step 8: Verify GREEN and dependency independence**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk rg -n "using DTXMania\.Game|using DTXMania\.E2E|using .*MCP|ProjectReference.*(DTXMania\.Game|DTXMania\.E2E|MCP)" DTXMania.Automation
```

Expected dependency scan: no matches.

- [ ] **Step 9: Commit**

```bash
rtk git add DTXMania.Automation/JsonRpc DTXMania.Automation/Telemetry DTXMania.Automation/Support DTXMania.Automation.Tests
rtk git commit -m "feat: extract game API automation client"
```

---

## Task 4: Centralize E2E Launch Policy and Migrate Every Smoke Launch

**Files:**

- Modify `DTXMania.E2E/DTXMania.E2E.csproj`.
- Modify `DTXMania.E2E/Fixtures/E2EGameProject.cs`.
- Create `DTXMania.E2E/Fixtures/E2EGameLaunch.cs`.
- Create `DTXMania.E2E/AutomationContractTests.cs`.
- Modify the four smoke suites.
- Delete old helper implementation/test files after migration.

**E2E policy owner:**

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

- [ ] **Step 1: Reference Automation from E2E without changing E2E's target framework**

Add:

```xml
<ProjectReference Include="..\DTXMania.Automation\DTXMania.Automation.csproj" />
```

Keep `net8.0-windows7.0` and current conditional game references.

- [ ] **Step 2: Make `E2EGameProject` the only game-project env reader**

```csharp
public static GameLaunchTarget ResolveLaunchTarget()
{
    var overridePath = Environment.GetEnvironmentVariable(GameProjectEnvironmentVariable);
    return GameLaunchTarget.Project(overridePath);
}
```

Project defaults now live in Automation's `GameProjectPaths.Current`.

- [ ] **Step 3: Add E2E launch-policy tests before migration**

Required cases:

```csharp
[Fact]
public void ResolveApiPort_ValidEnvironmentPort_ShouldUseIt()
{
    // Set DTXMANIA_E2E_API_PORT=18080; assert 18080.
}

[Theory]
[InlineData("0")]
[InlineData("-1")]
[InlineData("65536")]
[InlineData("not-a-port")]
public void ResolveApiPort_InvalidEnvironmentPort_ShouldUseValidEphemeralPort(string raw)
{
    // Assert result is 1..65535 and not the invalid override.
}

[Fact]
public void CreateOptions_Default_ShouldExplicitlyRemoveSimulatedMidi()
{
    var options = E2EGameLaunch.CreateOptions(fixture);
    Assert.Null(options.EnvironmentOverrides!["DTXMANIA_ENABLE_SIMULATED_MIDI"]);
    Assert.False(string.IsNullOrWhiteSpace(options.LaunchToken));
    Assert.Equal(E2EGameLaunch.ResolveRepoRoot(), options.WorkingDirectory);
}

[Fact]
public void CreateOptions_EnableMidi_ShouldSetSimulatedMidi()
{
    var options = E2EGameLaunch.CreateOptions(fixture, enableSimulatedMidi: true);
    Assert.Equal("1", options.EnvironmentOverrides!["DTXMANIA_ENABLE_SIMULATED_MIDI"]);
}

[Fact]
public void CreateOptions_ExtraEnvironment_ShouldMergeScenarioValues()
{
    var options = E2EGameLaunch.CreateOptions(
        fixture,
        extraEnvironment: new Dictionary<string, string?>
        {
            ["DTXMANIA_E2E_CRASH_INJECTION"] = "update"
        });

    Assert.Equal("update", options.EnvironmentOverrides!["DTXMANIA_E2E_CRASH_INJECTION"]);
}
```

Also assert extras cannot override the MIDI key case-insensitively.

- [ ] **Step 4: Implement `E2EGameLaunch` as the single launch-policy owner**

`ResolveRepoRoot` uses the current upward `DTXMania.sln` search once.

`ResolveApiPort` uses strict `1..65535` env validation, otherwise the existing bounded ephemeral/rebind strategy.

`CreateOptions` calls `ResolveRepoRoot`, `E2EGameProject.ResolveLaunchTarget`, uses `fixture.AppDataRoot`, creates a fresh launch token, and writes MIDI as `"1"` or null.

Do not add a free-port helper to Automation.

- [ ] **Step 5: Add producer/consumer contract tests**

Input enum:

```csharp
Assert.Equal((int)InputType.MouseClick, (int)GameApiInputType.MouseClick);
Assert.Equal((int)InputType.MouseMove, (int)GameApiInputType.MouseMove);
Assert.Equal((int)InputType.KeyPress, (int)GameApiInputType.KeyPress);
Assert.Equal((int)InputType.KeyRelease, (int)GameApiInputType.KeyRelease);
Assert.Equal((int)InputType.MidiNoteOn, (int)GameApiInputType.MidiNoteOn);
Assert.Equal((int)InputType.MidiNoteOff, (int)GameApiInputType.MidiNoteOff);
```

Move the current producer telemetry round-trip here: serialize game `GameTelemetrySnapshot` with camel-case JSON and deserialize into `GameStateSnapshot`, asserting all consumed fields.

- [ ] **Step 6: Migrate all four smoke suites**

At the top of each test:

```csharp
var repoRoot = E2EGameLaunch.ResolveRepoRoot();
var apiPort = E2EGameLaunch.ResolveApiPort();
```

Client:

```csharp
using var httpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
{
    Timeout = TimeSpan.FromSeconds(5)
};
var client = new JsonRpcGameClient(
    httpClient,
    new GameApiConnectionOptions(fixture.ApiBaseUri, fixture.ApiKey));
```

Normal launch/readiness:

```csharp
var startOptions = E2EGameLaunch.CreateOptions(fixture);
process.Start(startOptions);
await process.WaitForStartupAsync(
    client.GetHealthAsync,
    TimeSpan.FromSeconds(60),
    TimeSpan.FromMilliseconds(500),
    cancellationToken);
```

Midi uses `enableSimulatedMidi: true`. Crash passes only its crash-injection value through `extraEnvironment`.

Remove every private `FindRepoRoot`, `GetAvailablePort`, `GetPortFromEnvironmentOrDefault`, inline project selection, and direct MIDI environment policy from smoke files.

- [ ] **Step 7: Delete old E2E helper copies and pure support tests**

Delete the old process/JSON-RPC/telemetry/support implementations and their pure tests after migrated call sites compile. Do not leave forwarding wrappers.

- [ ] **Step 8: Run support tests and policy scans on a Windows-capable environment**

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
rtk rg -n "DTXMANIA_E2E_GAME_PROJECT" DTXMania.E2E --glob '*.cs'
rtk rg -n "DTXMANIA_ENABLE_SIMULATED_MIDI" DTXMania.E2E --glob '*.cs'
rtk rg -n "FindRepoRoot|GetAvailablePort|GetPortFromEnvironmentOrDefault" DTXMania.E2E/*SmokeTests.cs
```

Expected:

```text
DTXMANIA_E2E_GAME_PROJECT      -> Fixtures/E2EGameProject.cs only
DTXMANIA_ENABLE_SIMULATED_MIDI -> Fixtures/E2EGameLaunch.cs only
legacy private helper names    -> no smoke-test matches
```

- [ ] **Step 9: Require the live Windows gameplay smoke before Task 4 is accepted**

```bash
DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Windows.csproj \
  rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

This is a real behavioral gate because it launches CX and exercises the new token-based readiness path.

If implementation is performed on a non-Windows host, do **not** substitute a macOS gameplay run as proof. The existing E2E project is Windows-targeted. Task 4 remains pending live verification until the existing `gameplay-e2e-windows` CI job passes for the implementation branch.

- [ ] **Step 10: Commit only after the migration is reviewable and the required Windows live gate is green/pending solely on CI host availability**

```bash
rtk git add DTXMania.E2E
rtk git commit -m "refactor: consume reusable automation from e2e"
```

Document the Windows job result in the task review if it could not be run locally.

---

## Task 5: Add Cross-Platform Automation Validation and Final Scope Checks

**Files:**

- Modify `justfile`.
- Modify `.github/workflows/build-and-test.yml`.

- [ ] **Step 1: Add the dedicated cross-platform Automation recipe**

```make
automation_test := "DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj"

automation-test:
    dotnet test {{ automation_test }}
```

Keep gameplay E2E documented as Windows-only. `e2e-support` may call `automation-test` first, but it remains a Windows E2E-project recipe.

- [ ] **Step 2: Run Automation.Tests in both existing Windows and macOS build/test jobs**

Add:

```text
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug --verbosity normal
```

to both jobs.

Do not add macOS gameplay E2E.

- [ ] **Step 3: Run platform-neutral validation on the current host**

```bash
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

To isolate process-cost/failures:

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "Category=Automation-Process"
```

- [ ] **Step 4: Run scope/dependency scans**

```bash
rtk rg -n "ProjectReference.*DTXMania\.E2E" --glob '*.csproj'
rtk rg -n "DTXMania\.E2E\.(Process|JsonRpc|Telemetry|Support)" DTXMania.E2E DTXMania.Automation --glob '*.cs'
rtk rg -n "AutomationSession|IProcessRunner|IProcessLauncher|IJsonRpcTransport" DTXMania.Automation --glob '*.cs'
rtk rg -n "IsHealthyAsync|IsHealthyForLaunchAsync" DTXMania.Automation --glob '*.cs'
rtk rg -n "Sanitize|REDACTED" DTXMania.Automation --glob '*.cs'
```

Expected: no production E2E reference, no old helper namespaces, no session/generic transport abstraction, no redundant health helpers, and no redaction detour.

- [ ] **Step 5: Verify the HPA-503 consumer path contains no E2E type**

Expected reusable shape:

```csharp
var startOptions = new GameProcessStartOptions(
    repoRoot,
    GameLaunchTarget.Project(),
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
    game.GetHealthAsync,
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

Before implementation is marked complete, the Windows `Category=E2E` gameplay smoke from Task 4 must also be green.

- [ ] **Step 7: Commit**

```bash
rtk git add justfile .github/workflows/build-and-test.yml
rtk git commit -m "ci: validate automation cross platform"
```

## Risks and Review Gates

### Launch identity behavior changes

Project startup changes from "any 200 health response" to exact launch-token identity. Unit tests cover stale identity and diagnostics, but the Windows live gameplay E2E is the acceptance gate for actual environment propagation.

### Process tests cost more than pure unit tests

Automation process tests spawn real child processes and run on both Windows/macOS. `AssemblyInfo.cs` serializes the suite to avoid process contention; `Category=Automation-Process` makes the expensive slice directly filterable. Keep the child fixture tiny. If CI cost proves material, optimize fixture reuse later rather than introducing a benchmark framework now.

### E2E is not cross-platform just because Automation is

`DTXMania.E2E` remains `net8.0-windows7.0`. macOS validates the Automation library/test suite only. Do not claim macOS gameplay E2E until a separate retargeting task proves it.

### MCP readiness temporarily remains duplicated

HPA-501 ports the semantics but does not migrate MCP. Record a follow-up to make MCP consume Automation after this API stabilizes; do not expand this task into MCP lifecycle refactoring.

## Plan Self-Review

- **Spec coverage:** launch targets, process ownership, project-token vs executable-PID readiness, timeout diagnostics, JSON-RPC, telemetry, polling, E2E policy consolidation, process-test serialization, Windows live E2E, and cross-platform Automation CI each map to an explicit task.
- **Scope:** five sequential slices; no recorder orchestration, OBS, FFmpeg, HPA-510 behavior, DI, generic process/transport framework, or MCP migration.
- **Package consistency:** current `main` E2E/main-test versions are copied exactly: `18.8.1 / 2.9.3 / 3.1.5`. Mac test-project package drift is intentionally not normalized here.
- **PID semantics:** project readiness never uses the `dotnet run` PID; executable PID is fallback only.
- **Diagnostics:** startup timeout includes the last observed health identity.
- **E2E policy:** repo-root, API-port, project selection, and simulated MIDI have one local owner.
- **Parallelism:** Automation process tests inherit E2E's no-parallel safety rule and are trait-filterable.
- **Health API:** `GetHealthAsync` is the only public health method; readiness belongs in the driver.
- **Security scope:** API-key redaction remains out of HPA-501.
- **MCP convergence:** explicitly documented as follow-up scope, not silently duplicated forever.

## Implementation Handoff

Use `superpowers:subagent-driven-development`. Give each task to a fresh implementation agent and review before moving to the next. Tasks are sequential because later E2E migration consumes the process/transport contracts defined in Tasks 1-3.
