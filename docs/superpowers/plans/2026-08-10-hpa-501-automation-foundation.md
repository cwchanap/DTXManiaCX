# HPA-501 Reusable CX Automation Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the proven CX process, Game API, telemetry, and polling helpers from the Windows-targeted E2E project into a small plain-`net8.0` library that both E2E and the future recorder can consume.

**Architecture:** Add `DTXMania.Automation` plus a platform-neutral test project. Keep process ownership, JSON-RPC transport, telemetry projection, and polling as separate primitives; make platform/launch selection explicit through small option records; then migrate E2E to consume those primitives while keeping game-specific compatibility checks inside E2E. Do not add a workflow/session coordinator—HPA-503 remains the recorder lifecycle owner.

**Tech Stack:** .NET 8, C#, xUnit, `System.Diagnostics.Process`, `HttpClient`, `System.Text.Json`, existing CX Game API/E2E fixtures, GitHub Actions.

## Global Constraints

- Source of truth: [`2026-08-10-hpa-501-automation-foundation-design.md`](../specs/2026-08-10-hpa-501-automation-foundation-design.md).
- `DTXMania.Automation` and `DTXMania.Automation.Tests` target plain `net8.0`.
- `DTXMania.Automation` must not reference `DTXMania.E2E` or either MonoGame game project.
- `DTXMania.Automation.Tests` must not reference either MonoGame game project.
- Keep `DTXMania.E2E` on its existing `net8.0-windows7.0` target; this ticket restructures reusable support instead of retargeting all E2E code.
- Support exactly `WindowsX64` and `MacArm64` launch identities for HPA-501.
- Project launch defaults remain `DTXMania.Game/DTXMania.Game.Windows.csproj` and `DTXMania.Game/DTXMania.Game.Mac.csproj`.
- Executable launch requires an exact caller-supplied path; do not infer publish/RID/configuration output paths.
- `DTXMANIA_APPDATA_ROOT` and `DTXMANIA_LAUNCH_TOKEN` are driver-owned environment variables and cannot be overridden through generic environment overrides.
- Simulated MIDI is E2E policy expressed through environment overrides, not a production `GameProcessDriver` flag.
- Keep generic JSON-RPC send private. Preserve explicit health, game-state, screenshot, key/MIDI, and stage-change methods only.
- The automation library owns its wire enum/DTO; game assembly compatibility is tested from E2E rather than via a project reference.
- Preserve current `InputType` numeric wire values `0..5` exactly.
- Never include the configured CX API key in thrown diagnostic text; sanitize response/error content before constructing exceptions.
- Preserve owned-process-tree cleanup and do not discover/kill unrelated CX processes.
- No OBS, FFmpeg, CLI, recording sandbox, prepared-chart commands, DI container, generic process framework, or workflow state machine.
- No backward-compatible wrappers for old E2E helper namespaces.
- Use RED -> focused GREEN -> review -> commit for every task.

## File Responsibility Map

### New production project

- `DTXMania.Automation/DTXMania.Automation.csproj`: plain `net8.0` reusable automation library.
- `DTXMania.Automation/Process/GameLaunchTarget.cs`: platform/kind/path launch contract and default project mappings.
- `DTXMania.Automation/Process/GameProcessStartOptions.cs`: working directory, target, app-data root, launch token, environment overrides.
- `DTXMania.Automation/Process/GameProcessDriver.cs`: one owned process, output capture, startup wait, exit wait, cleanup.
- `DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs`: base URI and API key.
- `DTXMania.Automation/JsonRpc/GameApiInputType.cs`: stable consumer-side input wire values.
- `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs`: health and explicit JSON-RPC commands with sanitized diagnostics.
- `DTXMania.Automation/Telemetry/GameStateSnapshot.cs`: consumer-side telemetry projection.
- `DTXMania.Automation/Support/Eventually.cs`: bounded polling helper.

### New platform-neutral tests

- `DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`.
- `DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs`.
- `DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs`.
- `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs`.
- `DTXMania.Automation.Tests/Telemetry/GameStateSnapshotTests.cs`.
- `DTXMania.Automation.Tests/Support/EventuallyTests.cs`.

### E2E migration

- Modify `DTXMania.E2E/DTXMania.E2E.csproj`: add `DTXMania.Automation` project reference.
- Modify `DTXMania.E2E/Fixtures/E2EGameProject.cs`: return explicit automation launch target while preserving `DTXMANIA_E2E_GAME_PROJECT` override.
- Create `DTXMania.E2E/AutomationContractTests.cs`: compare automation wire values/telemetry projection with producer game types.
- Modify `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`.
- Modify `DTXMania.E2E/MidiGameplaySmokeTests.cs`.
- Modify `DTXMania.E2E/DrumMappingStageSmokeTests.cs`.
- Modify `DTXMania.E2E/CrashReportingSmokeTests.cs`.
- Delete extracted copies and their pure support tests from `DTXMania.E2E/Process`, `JsonRpc`, `Telemetry`, and `Support` after migration.

### Repository/CI

- Modify `DTXMania.sln`.
- Modify `justfile`.
- Modify `.github/workflows/build-and-test.yml`.

---

## Task 1: Create the Plain Automation Projects and Explicit Launch Contract

**Files:**

- Create: `DTXMania.Automation/DTXMania.Automation.csproj`
- Create: `DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj`
- Create: `DTXMania.Automation/Process/GameLaunchTarget.cs`
- Create: `DTXMania.Automation/Process/GameProcessStartOptions.cs`
- Create: `DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs`
- Modify: `DTXMania.sln`

**Interfaces produced:**

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

- [ ] **Step 1: Create the two project files before production types**

Use the same xUnit/test package versions currently used by `DTXMania.E2E`.

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

- [ ] **Step 2: Add failing launch-target tests**

```csharp
using DTXMania.Automation.Process;

namespace DTXMania.Automation.Tests.Process;

public sealed class GameLaunchTargetTests
{
    [Fact]
    public void Project_WindowsX64_ShouldUseWindowsGameProject()
    {
        var target = GameLaunchTarget.Project(GamePlatform.WindowsX64);

        Assert.Equal(GameLaunchKind.Project, target.Kind);
        Assert.Equal("DTXMania.Game/DTXMania.Game.Windows.csproj", target.Path);
    }

    [Fact]
    public void Project_MacArm64_ShouldUseMacGameProject()
    {
        var target = GameLaunchTarget.Project(GamePlatform.MacArm64);

        Assert.Equal(GameLaunchKind.Project, target.Kind);
        Assert.Equal("DTXMania.Game/DTXMania.Game.Mac.csproj", target.Path);
    }

    [Fact]
    public void Project_WithOverride_ShouldUseExactOverride()
    {
        var target = GameLaunchTarget.Project(
            GamePlatform.WindowsX64,
            "custom/Game.Windows.csproj");

        Assert.Equal("custom/Game.Windows.csproj", target.Path);
    }

    [Fact]
    public void Executable_ShouldRetainExplicitPlatformAndPath()
    {
        var target = GameLaunchTarget.Executable(
            GamePlatform.MacArm64,
            "/tmp/DTXMania.Game");

        Assert.Equal(GamePlatform.MacArm64, target.Platform);
        Assert.Equal(GameLaunchKind.Executable, target.Kind);
        Assert.Equal("/tmp/DTXMania.Game", target.Path);
    }

    [Fact]
    public void Executable_WithBlankPath_ShouldReject()
    {
        Assert.Throws<ArgumentException>(() =>
            GameLaunchTarget.Executable(GamePlatform.WindowsX64, " "));
    }
}
```

- [ ] **Step 3: Run the focused tests and verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameLaunchTargetTests
```

Expected: compile/test failure because the launch types do not exist.

- [ ] **Step 4: Implement only the launch target and start-options records**

Use a switch expression for project defaults and `ArgumentException.ThrowIfNullOrWhiteSpace` for caller-supplied override/executable paths when present.

Do not validate file existence here; project/executable existence is an execution-time concern and tests must be able to construct targets without repository/platform side effects.

- [ ] **Step 5: Add both projects to the solution**

```bash
rtk dotnet sln DTXMania.sln add DTXMania.Automation/DTXMania.Automation.csproj
rtk dotnet sln DTXMania.sln add DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

- [ ] **Step 6: Run focused GREEN and project builds**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameLaunchTargetTests
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
```

Expected: all launch tests pass and the library builds without a MonoGame project reference.

- [ ] **Step 7: Commit Task 1**

```bash
rtk git add DTXMania.Automation DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj DTXMania.Automation.Tests/Process/GameLaunchTargetTests.cs DTXMania.sln
rtk git commit -m "feat: add reusable automation launch contracts"
```

---

## Task 2: Extract Owned Process Launch, Startup Wait, Output Capture, and Cleanup

**Files:**

- Create: `DTXMania.Automation/Process/GameProcessDriver.cs`
- Create: `DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs`
- Reference source behavior: `DTXMania.E2E/Process/GameProcessDriver.cs`
- Reference existing tests: `DTXMania.E2E/Process/GameProcessDriverTests.cs`

**Consumes:**

```csharp
GameLaunchTarget
GameProcessStartOptions
```

**Produces:**

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

- [ ] **Step 1: Port the existing generated-child tests and add the missing process cases**

Keep the current temp `Child.csproj` approach so tests exercise real `ProcessStartInfo` behavior without launching MonoGame.

The child must support environment-controlled modes:

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

Console.WriteLine("set=" + (Environment.GetEnvironmentVariable("DTX_AUTOMATION_SET") ?? "<null>"));
Console.WriteLine("removed=" + (Environment.GetEnvironmentVariable("DTX_AUTOMATION_REMOVE") ?? "<null>"));
Console.Error.WriteLine("child-stderr");
return 23;
```

Required tests:

```csharp
[Fact]
public async Task Start_ProjectTarget_ShouldSetOwnedEnvironmentAndDrainOutput()
{
    // Build temp Child.csproj, create GameProcessStartOptions with a Project target override,
    // set DTX_AUTOMATION_SET and remove DTX_AUTOMATION_REMOVE, then wait for exit.
    // Assert exit 23, expected stdout/stderr, and child-observed app-data/launch-token values.
}

[Fact]
public async Task Start_Twice_ShouldRejectSecondStart()
{
    // Start a child in wait mode, then assert second Start throws InvalidOperationException.
}

[Fact]
public async Task WaitForStartup_WhenProcessExitsEarly_ShouldFailImmediatelyWithOutput()
{
    // Start exit-early mode; healthProbe always false.
    // Assert InvalidOperationException contains exit code 42, early-stdout, early-stderr.
}

[Fact]
public async Task WaitForStartup_WhenHealthNeverSucceeds_ShouldTimeout()
{
    // Start wait mode; healthProbe always false; use ~100 ms timeout / ~10 ms interval.
    // Assert TimeoutException.
}

[Fact]
public async Task WaitForStartup_WhenCancelled_ShouldPropagateCancellation()
{
    // Start wait mode; cancel token; assert OperationCanceledException.
}

[Fact]
public async Task DisposeAsync_CalledTwice_ShouldBeIdempotent()
{
    // Start wait mode; call DisposeAsync twice; second call must not throw.
}
```

Retain the current burst-output drain regression test as well.

- [ ] **Step 2: Run process tests and verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameProcessDriverTests
```

Expected: failure because `GameProcessDriver` is not implemented.

- [ ] **Step 3: Move the current driver behavior and remove `E2EFixture` coupling**

The core `Start` shape must be:

```csharp
public void Start(GameProcessStartOptions options)
{
    ArgumentNullException.ThrowIfNull(options);
    ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);
    ArgumentException.ThrowIfNullOrWhiteSpace(options.AppDataRoot);
    ArgumentException.ThrowIfNullOrWhiteSpace(options.LaunchToken);

    if (_process is not null)
        throw new InvalidOperationException("Game process has already been started.");

    ValidateEnvironmentOverrides(options.EnvironmentOverrides);

    var startInfo = CreateStartInfo(options);
    _process = System.Diagnostics.Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start game process.");

    // Preserve current stdout/stderr event handlers and Begin*ReadLine calls.
}
```

`CreateStartInfo` derives only these commands:

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

Always use:

```csharp
UseShellExecute = false;
RedirectStandardOutput = true;
RedirectStandardError = true;
CreateNoWindow = true;
WorkingDirectory = options.WorkingDirectory;
```

- [ ] **Step 4: Preserve environment ownership and generic override semantics**

```csharp
private const string AppDataRootEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
private const string LaunchTokenEnvironmentVariable = "DTXMANIA_LAUNCH_TOKEN";
```

Set them from options before applying generic overrides. `ValidateEnvironmentOverrides` rejects either name case-insensitively.

For other overrides:

```csharp
if (value is null)
    startInfo.Environment.Remove(key);
else
    startInfo.Environment[key] = value;
```

Do not carry the old `enableSimulatedMidi` argument into Automation.

- [ ] **Step 5: Implement bounded startup wait with early-exit detection**

Keep it local to the driver; do not introduce a launcher/session type.

```csharp
public async Task WaitForStartupAsync(
    Func<CancellationToken, Task<bool>> healthProbe,
    TimeSpan timeout,
    TimeSpan interval,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(healthProbe);
    var process = _process
        ?? throw new InvalidOperationException("Game process has not been started.");
    var deadline = DateTimeOffset.UtcNow + timeout;

    while (DateTimeOffset.UtcNow < deadline)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfExitedBeforeStartup(process);

        if (await healthProbe(cancellationToken))
            return;

        ThrowIfExitedBeforeStartup(process);
        await Task.Delay(interval, cancellationToken);
    }

    ThrowIfExitedBeforeStartup(process);
    throw new TimeoutException("Timed out waiting for the game process to become healthy.");
}
```

`ThrowIfExitedBeforeStartup` must include the exit code and current captured stdout/stderr in the thrown message. It must not wait for the full timeout after an exit.

- [ ] **Step 6: Preserve current exit waiting and cleanup behavior**

Move `WaitForExitAsync`, output-drain tracking, and `DisposeAsync` from E2E with namespace/coupling changes only.

After cleanup, set `_process = null` so repeated disposal is a no-op.

- [ ] **Step 7: Run process GREEN repeatedly**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameProcessDriverTests
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter FullyQualifiedName~GameProcessDriverTests
```

Expected: both runs pass; the second run guards against leaked child processes/ports/temp state.

- [ ] **Step 8: Commit Task 2**

```bash
rtk git add DTXMania.Automation/Process/GameProcessDriver.cs DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs
rtk git commit -m "feat: extract owned game process driver"
```

---

## Task 3: Extract the Game API Client, Wire DTO, Telemetry Projection, and Polling Helper

**Files:**

- Create: `DTXMania.Automation/JsonRpc/GameApiConnectionOptions.cs`
- Create: `DTXMania.Automation/JsonRpc/GameApiInputType.cs`
- Create: `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs`
- Create: `DTXMania.Automation/Telemetry/GameStateSnapshot.cs`
- Create: `DTXMania.Automation/Support/Eventually.cs`
- Create: `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs`
- Create: `DTXMania.Automation.Tests/Telemetry/GameStateSnapshotTests.cs`
- Create: `DTXMania.Automation.Tests/Support/EventuallyTests.cs`
- Reference: existing E2E JsonRpc/Telemetry/Support implementations and tests

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

public sealed class JsonRpcGameClient
{
    public JsonRpcGameClient(HttpClient httpClient, GameApiConnectionOptions connection);

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
    public Task<GameStateSnapshot> GetGameStateAsync(CancellationToken cancellationToken);
    public Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken);
    public Task SendMidiNoteAsync(int noteNumber, int velocity, TimeSpan holdDuration, CancellationToken cancellationToken);
    public Task ChangeStageAsync(string stageName, CancellationToken cancellationToken);
    public Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Add failing JSON-RPC tests using an in-memory `HttpMessageHandler`**

Port the current key/MIDI/game-state tests and add URI/error/redaction coverage.

Required new tests:

```csharp
[Fact]
public async Task IsHealthyAsync_ShouldUseConfiguredBaseUriAndApiKey()
{
    using var handler = new RecordingHandler();
    using var httpClient = new HttpClient(handler);
    var client = new JsonRpcGameClient(
        httpClient,
        new GameApiConnectionOptions(new Uri("http://127.0.0.1:19090/"), "secret"));

    Assert.True(await client.IsHealthyAsync(CancellationToken.None));
    Assert.Equal(new Uri("http://127.0.0.1:19090/health"), handler.RequestUris.Single());
    Assert.Equal("secret", handler.ApiKeys.Single());
}

[Fact]
public async Task JsonRpcError_WhenBodyContainsApiKey_ShouldRedactSecretFromException()
{
    using var handler = RecordingHandler.JsonRpcError("authentication failed for secret-token");
    using var httpClient = new HttpClient(handler);
    var client = new JsonRpcGameClient(
        httpClient,
        new GameApiConnectionOptions(new Uri("http://127.0.0.1:19090/"), "secret-token"));

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        client.GetGameStateAsync(CancellationToken.None));

    Assert.DoesNotContain("secret-token", exception.ToString(), StringComparison.Ordinal);
    Assert.Contains("[REDACTED]", exception.Message, StringComparison.Ordinal);
}
```

Also assert key/MIDI requests still send numeric types `2`, `3`, `4`, `5`.

- [ ] **Step 2: Add failing telemetry tests without a game reference**

Move the pure `E2EGameState` tests and rename the subject to `GameStateSnapshot`.

Use raw JSON only in this project:

```csharp
[Fact]
public void Telemetry_WhenCustomDataContainsTelemetry_ShouldExposeValues()
{
    var state = new GameStateSnapshot
    {
        CustomData = new Dictionary<string, JsonElement>
        {
            ["telemetry"] = JsonDocument.Parse(
                "{\"stageType\":\"Result\",\"score\":500,\"clearFlag\":true}").RootElement
        }
    };

    Assert.Equal("Result", state.StageType);
    Assert.Equal(500, state.Score);
    Assert.True(state.ClearFlag);
}
```

Preserve default-value coverage for absent telemetry and null numeric/bool fields.

- [ ] **Step 3: Add failing `Eventually` behavior tests**

Required cases:

```csharp
[Fact]
public async Task UntilAsync_WhenPredicateEventuallyMatches_ShouldReturnValue()
{
    var attempt = 0;
    var result = await Eventually.UntilAsync(
        _ => Task.FromResult(++attempt),
        value => value == 3,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(1),
        "three attempts",
        CancellationToken.None);

    Assert.Equal(3, result);
}

[Fact]
public async Task UntilAsync_WhenProbeKeepsFailing_ShouldTimeoutWithLastError()
{
    var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
        Eventually.UntilAsync<int>(
            _ => throw new InvalidOperationException("probe failed"),
            _ => true,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(5),
            "probe",
            CancellationToken.None));

    Assert.Contains("probe failed", exception.ToString(), StringComparison.Ordinal);
}
```

Add caller-cancellation coverage.

- [ ] **Step 4: Run all Task 3 tests and verify RED**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "FullyQualifiedName~JsonRpcGameClientTests|FullyQualifiedName~GameStateSnapshotTests|FullyQualifiedName~EventuallyTests"
```

- [ ] **Step 5: Move `GameStateSnapshot` and `Eventually` with namespace/name-only changes**

For `GameStateSnapshot`, preserve the current telemetry accessors consumed by E2E. Do not prune fields just because HPA-503 does not immediately need them; existing E2E is an intended consumer of the extracted library.

For `Eventually`, preserve the current transient-exception and timeout behavior exactly.

- [ ] **Step 6: Implement automation-owned Game API connection and input wire enum**

`GameApiConnectionOptions` validates a non-null absolute `BaseUri`. The API key may be empty because the existing client already supports no header when blank.

Use `GameApiInputType` numeric values exactly as listed in Global Constraints.

- [ ] **Step 7: Move/adapt `JsonRpcGameClient` without a game project reference**

Use absolute request URIs built from the connection:

```csharp
private Uri Resolve(string relativePath) => new(_connection.BaseUri, relativePath);
```

Continue sending the key only when non-blank:

```csharp
if (!string.IsNullOrWhiteSpace(_connection.ApiKey))
    request.Headers.Add("X-Api-Key", _connection.ApiKey);
```

Replace the old `InputType` casts with `GameApiInputType` values.

Keep the generic transport private:

```csharp
private Task<JsonElement> SendAsync(
    string method,
    object? parameters,
    CancellationToken cancellationToken);
```

- [ ] **Step 8: Sanitize HTTP/JSON-RPC diagnostic bodies before throwing**

Add one private/internal helper in `JsonRpcGameClient.cs` unless a second consumer appears during implementation:

```csharp
private string Sanitize(string value)
{
    if (string.IsNullOrEmpty(_connection.ApiKey))
        return value;

    return value.Replace(
        _connection.ApiKey,
        "[REDACTED]",
        StringComparison.Ordinal);
}
```

For HTTP errors, do not call `EnsureSuccessStatusCode()` before capturing the body. Throw an `InvalidOperationException` containing status code plus `Sanitize(body)`.

For JSON-RPC errors, include `method` and sanitized error detail.

- [ ] **Step 9: Run Task 3 GREEN**

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --filter "FullyQualifiedName~JsonRpcGameClientTests|FullyQualifiedName~GameStateSnapshotTests|FullyQualifiedName~EventuallyTests"
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

Expected: all automation tests pass with no game assembly dependency.

- [ ] **Step 10: Commit Task 3**

```bash
rtk git add DTXMania.Automation/JsonRpc DTXMania.Automation/Telemetry DTXMania.Automation/Support DTXMania.Automation.Tests/JsonRpc DTXMania.Automation.Tests/Telemetry DTXMania.Automation.Tests/Support
rtk git commit -m "feat: extract reusable game api automation"
```

---

## Task 4: Migrate E2E to the Extracted Library and Preserve Producer/Consumer Contracts

**Files:**

- Modify: `DTXMania.E2E/DTXMania.E2E.csproj`
- Modify: `DTXMania.E2E/Fixtures/E2EGameProject.cs`
- Create: `DTXMania.E2E/AutomationContractTests.cs`
- Modify: `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`
- Modify: `DTXMania.E2E/MidiGameplaySmokeTests.cs`
- Modify: `DTXMania.E2E/DrumMappingStageSmokeTests.cs`
- Modify: `DTXMania.E2E/CrashReportingSmokeTests.cs`
- Delete: `DTXMania.E2E/Process/GameProcessDriver.cs`
- Delete: `DTXMania.E2E/Process/GameProcessDriverTests.cs`
- Delete: `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`
- Delete: `DTXMania.E2E/JsonRpc/JsonRpcGameClientTests.cs`
- Delete: `DTXMania.E2E/Telemetry/E2EGameState.cs`
- Delete/migrate pure telemetry tests from `DTXMania.E2E/Telemetry/E2EGameStateTests.cs`
- Delete: `DTXMania.E2E/Support/Eventually.cs`

**Consumes:** all Task 1-3 Automation public types.

- [ ] **Step 1: Add the Automation project reference to E2E**

Add alongside the existing platform-conditional game reference:

```xml
<ProjectReference Include="..\DTXMania.Automation\DTXMania.Automation.csproj" />
```

Do not remove the game project reference; other E2E tests still need producer/game types.

- [ ] **Step 2: Change `E2EGameProject` to return an explicit launch target**

```csharp
using DTXMania.Automation.Process;

namespace DTXMania.E2E.Fixtures;

public static class E2EGameProject
{
    public const string GameProjectEnvironmentVariable = "DTXMANIA_E2E_GAME_PROJECT";

    public static GameLaunchTarget ResolveLaunchTarget()
    {
        var overridePath = Environment.GetEnvironmentVariable(GameProjectEnvironmentVariable);
        var platform = OperatingSystem.IsWindows()
            ? GamePlatform.WindowsX64
            : GamePlatform.MacArm64;

        return GameLaunchTarget.Project(platform, overridePath);
    }
}
```

Do not move the `DTXMANIA_E2E_GAME_PROJECT` environment variable into Automation.

- [ ] **Step 3: Add producer/consumer contract tests before deleting old DTO dependencies**

`AutomationContractTests.cs` keeps the game reference where it belongs.

Wire-value test:

```csharp
[Fact]
public void GameApiInputType_ShouldMatchProducerInputTypeWireValues()
{
    Assert.Equal((int)InputType.MouseClick, (int)GameApiInputType.MouseClick);
    Assert.Equal((int)InputType.MouseMove, (int)GameApiInputType.MouseMove);
    Assert.Equal((int)InputType.KeyPress, (int)GameApiInputType.KeyPress);
    Assert.Equal((int)InputType.KeyRelease, (int)GameApiInputType.KeyRelease);
    Assert.Equal((int)InputType.MidiNoteOn, (int)GameApiInputType.MidiNoteOn);
    Assert.Equal((int)InputType.MidiNoteOff, (int)GameApiInputType.MidiNoteOff);
}
```

Move the existing `GameTelemetrySnapshot` camel-case round-trip test here, but deserialize into `GameStateSnapshot` instead of `E2EGameState`.

Keep this test tagged `Category=E2E-Support`.

- [ ] **Step 4: Run the new contract tests before call-site migration**

On Windows:

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter FullyQualifiedName~AutomationContractTests
```

Expected: pass once Automation is referenced and the types from Tasks 1-3 exist.

- [ ] **Step 5: Migrate smoke-test namespaces and client construction**

Replace E2E helper namespaces with:

```csharp
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
```

Client construction becomes:

```csharp
using var httpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
{
    Timeout = TimeSpan.FromSeconds(5)
};
var client = new JsonRpcGameClient(
    httpClient,
    new GameApiConnectionOptions(fixture.ApiBaseUri, fixture.ApiKey));
```

Do not set `HttpClient.BaseAddress`; the connection options own the endpoint.

- [ ] **Step 6: Migrate process launch while keeping E2E-specific environment policy local**

At each launch, build options from fixture + explicit target:

```csharp
var environment = new Dictionary<string, string?>
{
    ["DTXMANIA_ENABLE_SIMULATED_MIDI"] = enableSimulatedMidi ? "1" : null
};

process.Start(new GameProcessStartOptions(
    WorkingDirectory: repoRoot,
    Target: E2EGameProject.ResolveLaunchTarget(),
    AppDataRoot: fixture.AppDataRoot,
    LaunchToken: Guid.NewGuid().ToString("N"),
    EnvironmentOverrides: environment));
```

Where a smoke test already has unrelated environment overrides, merge them into the same dictionary.

For non-MIDI tests, explicitly remove `DTXMANIA_ENABLE_SIMULATED_MIDI` so an inherited parent value cannot silently change the backend, preserving the current safety behavior.

- [ ] **Step 7: Use `WaitForStartupAsync` for the initial Game API health gate**

Replace repeated initial health polling:

```csharp
await process.WaitForStartupAsync(
    client.IsHealthyAsync,
    TimeSpan.FromSeconds(60),
    TimeSpan.FromMilliseconds(500),
    cancellationToken);
```

Continue using `Eventually.UntilAsync` for stage/telemetry predicates after startup.

This is the intended behavior change: a process that crashes during startup fails immediately with captured output rather than spending the remaining 60 seconds polling health.

- [ ] **Step 8: Rename telemetry type references to `GameStateSnapshot`**

Update helper method parameters/returns where explicit `E2EGameState` appears. Do not change the assertions or telemetry semantics.

- [ ] **Step 9: Delete old helper implementation copies and pure support tests**

After all E2E call sites compile against Automation, remove the old process/client/state/polling files listed in this task.

Do not leave namespace wrappers or forwarding classes.

Pure helper tests now live in `DTXMania.Automation.Tests`; only producer/game contract tests remain in E2E.

- [ ] **Step 10: Run support and compile validation**

Platform-neutral:

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
```

Windows E2E support:

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
```

Compile-time scan:

```bash
rtk rg "DTXMania\.E2E\.(Process|JsonRpc|Telemetry|Support)|E2EGameState" DTXMania.E2E
```

Expected: no old helper namespace/type references.

Production dependency scan:

```bash
rtk rg "DTXMania\.Game|DTXMania\.E2E" DTXMania.Automation -g '*.cs' -g '*.csproj'
```

Expected: no matches.

- [ ] **Step 11: Commit Task 4**

```bash
rtk git add DTXMania.E2E
rtk git commit -m "refactor: consume reusable automation from e2e"
```

---

## Task 5: Wire Cross-Platform Automation Tests into Developer Commands and CI

**Files:**

- Modify: `justfile`
- Modify: `.github/workflows/build-and-test.yml`

- [ ] **Step 1: Add an Automation test project variable and recipe**

Near existing project variables:

```make
automation_test := "DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj"
```

Add:

```make
# Run platform-neutral reusable automation support tests
automation-test:
    dotnet test {{ automation_test }}
```

Update `e2e-support`:

```make
e2e-support:
    dotnet test {{ automation_test }}
    dotnet test {{ e2e_project }} --filter "Category=E2E-Support"
```

Do not change the gameplay `e2e` recipe beyond compile fixes required by Task 4.

- [ ] **Step 2: Add Automation.Tests to the normal Windows CI job**

After game/unit tests or as a focused step before tool tests:

```yaml
- name: Run automation support tests on Windows
  run: dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug --verbosity normal
```

This step must not require `DTXMania.Game.Windows.csproj` as a project reference.

- [ ] **Step 3: Add the same Automation.Tests command to macOS CI**

```yaml
- name: Run automation support tests on macOS
  run: dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug --verbosity normal
```

This is the acceptance gate proving the reusable foundation itself is no longer Windows-target coupled.

Do not add macOS gameplay E2E.

- [ ] **Step 4: Keep the Windows gameplay E2E support step for game-side contract tests**

The existing step remains:

```yaml
- name: Run E2E support tests
  run: dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --verbosity normal --logger trx --results-directory ./TestResults/e2e-support --filter "Category=E2E-Support"
```

It now covers E2E-owned producer/consumer contract tests rather than duplicate process/HTTP helper unit tests.

- [ ] **Step 5: Run final local validation available on the current platform**

Always run:

```bash
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
rtk dotnet build DTXMania.Automation/DTXMania.Automation.csproj
rtk git diff --check
```

On macOS also run the existing Mac-safe suite/build:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

On Windows also run:

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
rtk dotnet test DTXMania.Test/DTXMania.Test.csproj
rtk dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
```

Do not require a live gameplay E2E run as part of every local task commit; the existing Windows CI smoke remains the end-to-end gate.

- [ ] **Step 6: Self-review acceptance and dependency boundaries**

Run:

```bash
rtk rg "ProjectReference" DTXMania.Automation DTXMania.Automation.Tests
rtk rg "DTXMania\.Game|DTXMania\.E2E" DTXMania.Automation -g '*.cs' -g '*.csproj'
rtk rg "E2EGameState|DTXMania\.E2E\.(Process|JsonRpc|Telemetry|Support)" DTXMania.E2E
```

Expected:

- Automation has no game/E2E project reference.
- Automation.Tests references Automation only.
- E2E has no stale extracted-helper types/namespaces.

Review error messages from process startup and JSON-RPC tests and confirm the literal test API key never appears in thrown exception text.

- [ ] **Step 7: Commit Task 5**

```bash
rtk git add justfile .github/workflows/build-and-test.yml
rtk git commit -m "ci: validate automation support cross platform"
```

---

## Final Review Checklist

Before marking HPA-501 implementation ready for review:

- [ ] `DTXMania.Automation` targets only `net8.0` and references no game/E2E project.
- [ ] `DTXMania.Automation.Tests` passes without launching MonoGame.
- [ ] Default Windows/Mac project target selection and explicit executable target are covered.
- [ ] Process launch writes caller app-data root and launch token, applies generic environment overrides, and rejects overrides of owned variables.
- [ ] Duplicate start, early exit, timeout, cancellation, output draining, tree cleanup, and repeated disposal are covered.
- [ ] `JsonRpcGameClient` uses explicit `BaseUri` + `ApiKey` connection input.
- [ ] Key/MIDI wire values remain `2/3/4/5` and producer contract tests compare all `0..5` values.
- [ ] `GameStateSnapshot` retains all fields currently consumed by E2E.
- [ ] Generic JSON-RPC transport remains private.
- [ ] API-key redaction test proves the configured key is absent from exception diagnostics.
- [ ] Existing E2E smoke tests consume Automation instead of duplicate helper implementations.
- [ ] Initial E2E health gate uses `GameProcessDriver.WaitForStartupAsync`; later stage/telemetry waits continue using `Eventually`.
- [ ] Old E2E helper implementation copies are deleted.
- [ ] Automation tests run in both normal Windows and macOS CI jobs.
- [ ] macOS gameplay E2E, recorder workflow, OBS/FFmpeg, and prepared-chart behavior remain out of scope.

## Implementation Handoff

Use `superpowers:subagent-driven-development` for execution. Give each Task above to a fresh implementation agent and review each task before moving to the next. The tasks are sequential because later E2E migration consumes public interfaces created by Tasks 1-3.
