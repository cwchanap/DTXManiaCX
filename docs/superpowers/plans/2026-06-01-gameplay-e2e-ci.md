# Gameplay E2E CI Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a CI-run AutoPlay full-journey E2E test that launches the real game, uses isolated generated app data and chart data, drives Title -> SongSelect -> Performance -> Result, and asserts structured result telemetry.

**Architecture:** Add a small app-data override seam, expose read-only stage telemetry through the existing authenticated JSON-RPC Game API, and keep live-process E2E tests in a separate xUnit project so normal unit test jobs do not run them accidentally. The first CI lane runs on Windows, uploads artifacts, and is non-blocking while it stabilizes.

**Tech Stack:** .NET 8, MonoGame 3.8, xUnit, Moq, ASP.NET/Kestrel JSON-RPC, GitHub Actions, existing DTXManiaCX stage/input/resource systems.

**Spec:** `docs/superpowers/specs/2026-06-01-gameplay-e2e-ci-design.md`

---

## File Map

**Modify:**
- `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- `DTXMania.Game/Lib/GameApiImplementation.cs`
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- `DTXMania.Game/Lib/Stage/ResultStage.cs`
- `DTXMania.Test/Utilities/AppPathsTests.cs`
- `DTXMania.Test/GameApi/GameApiImplementationTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- `DTXMania.Test/Stage/ResultStageTests.cs`
- `.github/workflows/build-and-test.yml`

**Create:**
- `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`
- `DTXMania.Game/Lib/Stage/IStageTelemetryProvider.cs`
- `DTXMania.E2E/DTXMania.E2E.csproj`
- `DTXMania.E2E/Fixtures/E2ERunPaths.cs`
- `DTXMania.E2E/Fixtures/E2EFixture.cs`
- `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`
- `DTXMania.E2E/Process/GameProcessDriver.cs`
- `DTXMania.E2E/Support/E2EArtifactWriter.cs`
- `DTXMania.E2E/Support/Eventually.cs`
- `DTXMania.E2E/Telemetry/E2EGameState.cs`
- `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`
- `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`
- `DTXMania.E2E/JsonRpc/JsonRpcGameClientTests.cs`

## Important Implementation Notes

- Keep the live-process E2E test out of `DTXMania.Test.csproj`. The current Windows/macOS test jobs run existing test projects without category exclusion, so putting the E2E fact there would make the normal jobs launch MonoGame unintentionally.
- Use a separate `DTXMania.E2E` xUnit project. The CI E2E job is the only job that runs it.
- Do not use MCP in the E2E test. Call the JSON-RPC endpoint directly.
- Do not store generated E2E files under the repository except copied failure artifacts in `TestResults/e2e/`.
- Prefer telemetry assertions over screenshots. Screenshots are failure artifacts only.
- Commit after each task or tightly related pair of files.

---

## Task 1: Add App Data Root Override

**Files:**
- Modify: `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- Modify: `DTXMania.Test/Utilities/AppPathsTests.cs`

### Step 1: Write failing tests

Add these tests to `DTXMania.Test/Utilities/AppPathsTests.cs` near the existing `GetAppDataRoot` tests:

```csharp
[Fact]
public void GetAppDataRoot_WhenEnvironmentOverrideIsSet_ShouldUseOverride()
{
    const string envName = "DTXMANIA_APPDATA_ROOT";
    var previous = Environment.GetEnvironmentVariable(envName);
    var overrideRoot = Path.Combine(Path.GetTempPath(), "dtx-appdata-root-" + Guid.NewGuid().ToString("N"));

    try
    {
        Environment.SetEnvironmentVariable(envName, overrideRoot);

        var root = AppPaths.GetAppDataRoot();

        Assert.Equal(Path.GetFullPath(overrideRoot), root);
        Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "Config.ini"), AppPaths.GetConfigFilePath());
        Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "DTXFiles"), AppPaths.GetDefaultSongsPath());
        Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "System"), AppPaths.GetDefaultSystemSkinRoot());
        Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "songs.db"), AppPaths.GetSongsDatabasePath());
    }
    finally
    {
        Environment.SetEnvironmentVariable(envName, previous);
    }
}

[Fact]
public void GetAppDataRoot_WhenEnvironmentOverrideIsBlank_ShouldUseDefaultRoot()
{
    const string envName = "DTXMANIA_APPDATA_ROOT";
    var previous = Environment.GetEnvironmentVariable(envName);

    try
    {
        Environment.SetEnvironmentVariable(envName, "   ");

        var root = AppPaths.GetAppDataRoot();

        Assert.False(string.IsNullOrWhiteSpace(root));
        Assert.True(Path.IsPathRooted(root));
        Assert.Equal("DTXManiaCX", Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)));
    }
    finally
    {
        Environment.SetEnvironmentVariable(envName, previous);
    }
}
```

### Step 2: Run tests to verify they fail

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~AppPathsTests"
```

Expected: the first new test fails because `AppPaths.GetAppDataRoot()` ignores `DTXMANIA_APPDATA_ROOT`.

### Step 3: Implement the override

In `DTXMania.Game/Lib/Utilities/AppPaths.cs`, add a constant near `AppName`:

```csharp
private const string AppDataRootOverrideEnvVar = "DTXMANIA_APPDATA_ROOT";
```

At the start of `GetAppDataRoot()`, before OS-specific path selection, add:

```csharp
var overrideRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideEnvVar);
if (!string.IsNullOrWhiteSpace(overrideRoot))
{
    return Path.GetFullPath(ExpandHomePath(overrideRoot));
}
```

### Step 4: Run tests to verify pass

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~AppPathsTests"
```

Expected: all `AppPathsTests` pass.

### Step 5: Commit

```bash
git add DTXMania.Game/Lib/Utilities/AppPaths.cs DTXMania.Test/Utilities/AppPathsTests.cs
git commit -m "test: isolate app data root for e2e runs"
```

---

## Task 2: Add Game Telemetry Model And API Integration

**Files:**
- Create: `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`
- Create: `DTXMania.Game/Lib/Stage/IStageTelemetryProvider.cs`
- Modify: `DTXMania.Game/Lib/GameApiImplementation.cs`
- Modify: `DTXMania.Test/GameApi/GameApiImplementationTests.cs`

### Step 1: Write failing Game API telemetry tests

Add this using to `DTXMania.Test/GameApi/GameApiImplementationTests.cs`:

```csharp
using DTXMania.Game.Lib.Stage.Performance;
```

Add these tests near `GetGameStateAsync_WithStageAndConfig_ShouldPopulateFields`:

```csharp
[Fact]
public async Task GetGameStateAsync_ShouldIncludeBaseTelemetrySnapshot()
{
    var stage = new Mock<IStage>();
    stage.SetupGet(s => s.Type).Returns(StageType.Title);
    stage.SetupGet(s => s.CurrentPhase).Returns(StagePhase.Normal);

    var stageManager = new Mock<IStageManager>();
    stageManager.SetupGet(sm => sm.CurrentStage).Returns(stage.Object);
    stageManager.SetupGet(sm => sm.IsTransitioning).Returns(false);

    var gameContext = new Mock<IGameContext>();
    gameContext.SetupGet(g => g.StageManager).Returns(stageManager.Object);

    var api = new GameApiImplementation(gameContext.Object);

    var state = await api.GetGameStateAsync();

    var telemetry = Assert.IsType<GameTelemetrySnapshot>(state.CustomData["telemetry"]);
    Assert.Equal("Title", telemetry.StageType);
    Assert.Equal("Normal", telemetry.StagePhase);
    Assert.False(telemetry.IsTransitioning);
}

[Fact]
public async Task GetGameStateAsync_WhenStageProvidesTelemetry_ShouldIncludeStageDetails()
{
    var stage = new Mock<IStage>();
    stage.SetupGet(s => s.Type).Returns(StageType.Performance);
    stage.SetupGet(s => s.CurrentPhase).Returns(StagePhase.Normal);
    stage.As<IStageTelemetryProvider>()
        .Setup(s => s.PopulateTelemetry(It.IsAny<GameTelemetrySnapshot>()))
        .Callback<GameTelemetrySnapshot>(telemetry =>
        {
            telemetry.SelectedSongTitle = "E2E AutoPlay Smoke";
            telemetry.SelectedDifficulty = 0;
            telemetry.AutoPlayEnabled = true;
            telemetry.PerformanceReady = true;
            telemetry.Score = 12345;
            telemetry.TotalNotes = 4;
            telemetry.PerfectCount = 4;
        });

    var stageManager = new Mock<IStageManager>();
    stageManager.SetupGet(sm => sm.CurrentStage).Returns(stage.Object);

    var gameContext = new Mock<IGameContext>();
    gameContext.SetupGet(g => g.StageManager).Returns(stageManager.Object);

    var api = new GameApiImplementation(gameContext.Object);

    var state = await api.GetGameStateAsync();

    var telemetry = Assert.IsType<GameTelemetrySnapshot>(state.CustomData["telemetry"]);
    Assert.Equal("Performance", telemetry.StageType);
    Assert.Equal("E2E AutoPlay Smoke", telemetry.SelectedSongTitle);
    Assert.True(telemetry.AutoPlayEnabled);
    Assert.True(telemetry.PerformanceReady);
    Assert.Equal(12345, telemetry.Score);
    Assert.Equal(4, telemetry.TotalNotes);
    Assert.Equal(4, telemetry.TotalJudgements);
}
```

### Step 2: Run tests to verify they fail

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~GameApiImplementationTests"
```

Expected: build fails because `GameTelemetrySnapshot` and `IStageTelemetryProvider` do not exist.

### Step 3: Add telemetry DTO

Create `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`:

```csharp
#nullable enable

using System;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib;

public sealed class GameTelemetrySnapshot
{
    public string StageName { get; set; } = "Unknown";
    public string StageType { get; set; } = "Unknown";
    public string StagePhase { get; set; } = "Unknown";
    public bool IsTransitioning { get; set; }

    public string? SelectedSongTitle { get; set; }
    public int? SelectedDifficulty { get; set; }
    public bool? InStatusPanel { get; set; }
    public bool? ChartLoaded { get; set; }

    public bool? PerformanceReady { get; set; }
    public bool? AutoPlayEnabled { get; set; }
    public bool? StageCompleted { get; set; }
    public int? Score { get; set; }
    public int? CurrentCombo { get; set; }
    public int? MaxCombo { get; set; }
    public float? Gauge { get; set; }
    public bool? HasFailed { get; set; }

    public int? PerfectCount { get; set; }
    public int? GreatCount { get; set; }
    public int? GoodCount { get; set; }
    public int? PoorCount { get; set; }
    public int? MissCount { get; set; }
    public int? TotalNotes { get; set; }
    public bool? ClearFlag { get; set; }
    public string? CompletionReason { get; set; }

    public int TotalJudgements =>
        (PerfectCount ?? 0) +
        (GreatCount ?? 0) +
        (GoodCount ?? 0) +
        (PoorCount ?? 0) +
        (MissCount ?? 0);

    public void ApplyPerformanceSummary(PerformanceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Score = summary.Score;
        MaxCombo = summary.MaxCombo;
        ClearFlag = summary.ClearFlag;
        PerfectCount = summary.PerfectCount;
        GreatCount = summary.GreatCount;
        GoodCount = summary.GoodCount;
        PoorCount = summary.PoorCount;
        MissCount = summary.MissCount;
        TotalNotes = summary.TotalNotes;
        Gauge = summary.FinalLife;
        CompletionReason = summary.CompletionReason.ToString();
    }
}
```

### Step 4: Add stage telemetry provider interface

Create `DTXMania.Game/Lib/Stage/IStageTelemetryProvider.cs`:

```csharp
#nullable enable

using DTXMania.Game.Lib;

namespace DTXMania.Game.Lib.Stage;

public interface IStageTelemetryProvider
{
    void PopulateTelemetry(GameTelemetrySnapshot telemetry);
}
```

### Step 5: Integrate telemetry into GameApiImplementation

In `DTXMania.Game/Lib/GameApiImplementation.cs`, add:

```csharp
using DTXMania.Game.Lib.Stage;
```

Add this private helper inside `GameApiImplementation`:

```csharp
private GameTelemetrySnapshot BuildTelemetrySnapshot()
{
    var stageManager = _game.StageManager;
    var currentStage = stageManager?.CurrentStage;

    var telemetry = new GameTelemetrySnapshot
    {
        StageName = currentStage?.GetType().Name ?? "Unknown",
        StageType = currentStage?.Type.ToString() ?? "Unknown",
        StagePhase = currentStage?.CurrentPhase.ToString() ?? "Unknown",
        IsTransitioning = stageManager?.IsTransitioning ?? false
    };

    if (currentStage is IStageTelemetryProvider provider)
    {
        provider.PopulateTelemetry(telemetry);
    }

    return telemetry;
}
```

In `GetGameStateSafe()`, add the telemetry entry to `CustomData`:

```csharp
["telemetry"] = BuildTelemetrySnapshot()
```

Keep the existing `CurrentStage` assignment unchanged for compatibility.

### Step 6: Run tests to verify pass

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~GameApiImplementationTests"
```

Expected: all `GameApiImplementationTests` pass.

### Step 7: Commit

```bash
git add DTXMania.Game/Lib/GameTelemetrySnapshot.cs \
        DTXMania.Game/Lib/Stage/IStageTelemetryProvider.cs \
        DTXMania.Game/Lib/GameApiImplementation.cs \
        DTXMania.Test/GameApi/GameApiImplementationTests.cs
git commit -m "feat: expose game telemetry through api state"
```

---

## Task 3: Add Stage Telemetry Providers

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify: `DTXMania.Test/Stage/ResultStageTests.cs`

### Step 1: Add failing PerformanceStage telemetry test

In `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`, add a test near other state-extraction tests:

```csharp
[Fact]
public void PopulateTelemetry_WhenManagersExist_ShouldExposePerformanceState()
{
    var stage = CreateStage();
    var chart = new ParsedChart("telemetry.dtx");
    chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });
    var chartManager = new ChartManager(chart);
    var scoreManager = new ScoreManager(1);
    var comboManager = new ComboManager();
    var gaugeManager = new GaugeManager();
    var judgementManager = new JudgementManager(new MockInputManagerCompat(), chartManager);
    var selectedSong = new SongListNode { Title = "E2E AutoPlay Smoke" };

    ReflectionHelpers.SetPrivateField(stage, "_selectedSong", selectedSong);
    ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 0);
    ReflectionHelpers.SetPrivateField(stage, "_chartManager", chartManager);
    ReflectionHelpers.SetPrivateField(stage, "_scoreManager", scoreManager);
    ReflectionHelpers.SetPrivateField(stage, "_comboManager", comboManager);
    ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", gaugeManager);
    ReflectionHelpers.SetPrivateField(stage, "_judgementManager", judgementManager);
    ReflectionHelpers.SetPrivateField(stage, "_autoPlayEnabled", true);
    ReflectionHelpers.SetPrivateField(stage, "_isReady", true);
    ReflectionHelpers.SetPrivateField(stage, "_stageCompleted", false);

    var telemetry = new GameTelemetrySnapshot();

    stage.PopulateTelemetry(telemetry);

    Assert.Equal("E2E AutoPlay Smoke", telemetry.SelectedSongTitle);
    Assert.Equal(0, telemetry.SelectedDifficulty);
    Assert.True(telemetry.AutoPlayEnabled);
    Assert.True(telemetry.PerformanceReady);
    Assert.False(telemetry.StageCompleted);
    Assert.Equal(1, telemetry.TotalNotes);
    Assert.Equal(0, telemetry.Score);
    Assert.Equal(0, telemetry.CurrentCombo);
    Assert.Equal(0, telemetry.MaxCombo);
    Assert.Equal(GaugeManager.StartingLife, telemetry.Gauge);
}
```

Add missing usings if needed:

```csharp
using DTXMania.Game.Lib;
```

### Step 2: Add failing ResultStage telemetry test

In `DTXMania.Test/Stage/ResultStageTests.cs`, add:

```csharp
[Fact]
public void PopulateTelemetry_WhenPerformanceSummaryExists_ShouldExposeResultSummary()
{
#pragma warning disable SYSLIB0050
    var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
    var selectedSong = new SongListNode { Title = "E2E AutoPlay Smoke" };
    var summary = new PerformanceSummary
    {
        Score = 1000000,
        MaxCombo = 4,
        ClearFlag = true,
        PerfectCount = 4,
        TotalNotes = 4,
        FinalLife = 100f,
        CompletionReason = CompletionReason.SongComplete
    };

    ReflectionHelpers.SetPrivateField(stage, "_selectedSong", selectedSong);
    ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 0);
    ReflectionHelpers.SetPrivateField(stage, "_performanceSummary", summary);

    var telemetry = new GameTelemetrySnapshot();

    stage.PopulateTelemetry(telemetry);

    Assert.Equal("E2E AutoPlay Smoke", telemetry.SelectedSongTitle);
    Assert.Equal(0, telemetry.SelectedDifficulty);
    Assert.Equal(1000000, telemetry.Score);
    Assert.Equal(4, telemetry.MaxCombo);
    Assert.Equal(4, telemetry.PerfectCount);
    Assert.Equal(4, telemetry.TotalNotes);
    Assert.True(telemetry.ClearFlag);
    Assert.Equal("SongComplete", telemetry.CompletionReason);
}
```

Add missing using:

```csharp
using DTXMania.Game.Lib;
```

### Step 3: Run tests to verify they fail

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PopulateTelemetry"
```

Expected: build fails because the stages do not implement `PopulateTelemetry`.

### Step 4: Implement SongSelectionStage telemetry

Change the class declaration in `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`:

```csharp
public class SongSelectionStage : BaseStage, IStageTelemetryProvider
```

Add this method near the bottom before disposal/logging regions:

```csharp
public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
{
    ArgumentNullException.ThrowIfNull(telemetry);

    telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
    telemetry.SelectedDifficulty = _currentDifficulty;
    telemetry.InStatusPanel = _isInStatusPanel;
}
```

### Step 5: Implement SongTransitionStage telemetry

Change the class declaration in `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`:

```csharp
public class SongTransitionStage : BaseStage, IStageTelemetryProvider
```

Add:

```csharp
public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
{
    ArgumentNullException.ThrowIfNull(telemetry);

    telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
    telemetry.SelectedDifficulty = _selectedDifficulty;
    telemetry.ChartLoaded = _chartLoaded;
}
```

### Step 6: Implement PerformanceStage telemetry

Change the class declaration in `DTXMania.Game/Lib/Stage/PerformanceStage.cs`:

```csharp
public class PerformanceStage : BaseStage, IStageTelemetryProvider
```

Add:

```csharp
public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
{
    ArgumentNullException.ThrowIfNull(telemetry);

    telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
    telemetry.SelectedDifficulty = _selectedDifficulty;
    telemetry.PerformanceReady = _isReady && !_isLoading && _readyCountdown <= 0;
    telemetry.AutoPlayEnabled = _autoPlayEnabled;
    telemetry.StageCompleted = _stageCompleted;
    telemetry.Score = _scoreManager?.CurrentScore ?? 0;
    telemetry.CurrentCombo = _comboManager?.CurrentCombo ?? 0;
    telemetry.MaxCombo = _comboManager?.MaxCombo ?? 0;
    telemetry.Gauge = _gaugeManager?.CurrentLife ?? 0.0f;
    telemetry.HasFailed = _gaugeManager?.HasFailed ?? false;
    telemetry.TotalNotes = _chartManager?.TotalNotes ?? 0;
    telemetry.PerfectCount = _judgementManager?.GetJudgementCount(JudgementType.Perfect) ?? 0;
    telemetry.GreatCount = _judgementManager?.GetJudgementCount(JudgementType.Great) ?? 0;
    telemetry.GoodCount = _judgementManager?.GetJudgementCount(JudgementType.Good) ?? 0;
    telemetry.PoorCount = _judgementManager?.GetJudgementCount(JudgementType.Poor) ?? 0;
    telemetry.MissCount = _judgementManager?.GetJudgementCount(JudgementType.Miss) ?? 0;
}
```

### Step 7: Implement ResultStage telemetry

Change the class declaration in `DTXMania.Game/Lib/Stage/ResultStage.cs`:

```csharp
public class ResultStage : BaseStage, IStageTelemetryProvider
```

Add:

```csharp
public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
{
    ArgumentNullException.ThrowIfNull(telemetry);

    telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
    telemetry.SelectedDifficulty = _selectedDifficulty;

    if (_performanceSummary != null)
    {
        telemetry.ApplyPerformanceSummary(_performanceSummary);
    }
}
```

### Step 8: Run targeted tests

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PopulateTelemetry|FullyQualifiedName~GameApiImplementationTests"
```

Expected: targeted telemetry tests pass.

### Step 9: Commit

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
        DTXMania.Game/Lib/Stage/SongTransitionStage.cs \
        DTXMania.Game/Lib/Stage/PerformanceStage.cs \
        DTXMania.Game/Lib/Stage/ResultStage.cs \
        DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs \
        DTXMania.Test/Stage/ResultStageTests.cs
git commit -m "feat: add stage telemetry for gameplay e2e"
```

---

## Task 4: Create E2E Test Project And Fixture Builder

**Files:**
- Create: `DTXMania.E2E/DTXMania.E2E.csproj`
- Create: `DTXMania.E2E/Fixtures/E2ERunPaths.cs`
- Create: `DTXMania.E2E/Fixtures/E2EFixture.cs`
- Create: `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- Create: `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`

### Step 1: Create the E2E project file

Create `DTXMania.E2E/DTXMania.E2E.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

### Step 2: Add fixture builder tests first

Create `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`:

```csharp
namespace DTXMania.E2E.Fixtures;

[Trait("Category", "E2E-Support")]
public sealed class E2EFixtureBuilderTests
{
    [Fact]
    public void Build_ShouldWriteConfigAndGeneratedChart()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-e2e-fixture-" + Guid.NewGuid().ToString("N"));
        var repoRoot = Directory.GetCurrentDirectory();

        try
        {
            var fixture = E2EFixtureBuilder.Build(root, repoRoot, apiPort: 18080);

            Assert.True(File.Exists(fixture.ConfigPath));
            Assert.True(File.Exists(fixture.ChartPath));
            Assert.True(Directory.Exists(fixture.AppDataRoot));
            Assert.True(Directory.Exists(fixture.DtxRoot));

            var config = File.ReadAllText(fixture.ConfigPath);
            Assert.Contains("EnableGameApi=True", config);
            Assert.Contains("GameApiKey=e2e-autoplay-smoke-key", config);
            Assert.Contains("GameApiPort=18080", config);
            Assert.Contains("AutoPlay=True", config);
            Assert.Contains(fixture.DtxRoot, config);

            var chart = File.ReadAllText(fixture.ChartPath);
            Assert.Contains("#TITLE: E2E AutoPlay Smoke", chart);
            Assert.Contains("#BPM: 120", chart);
            Assert.Contains("#00011:", chart);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
```

### Step 3: Run tests to verify they fail

Run:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~E2EFixtureBuilderTests"
```

Expected: build fails because fixture classes do not exist.

### Step 4: Add fixture path records

Create `DTXMania.E2E/Fixtures/E2ERunPaths.cs`:

```csharp
namespace DTXMania.E2E.Fixtures;

public sealed record E2ERunPaths(
    string RunRoot,
    string AppDataRoot,
    string DtxRoot,
    string SongDirectory,
    string ConfigPath,
    string ChartPath,
    string ArtifactRoot);
```

Create `DTXMania.E2E/Fixtures/E2EFixture.cs`:

```csharp
namespace DTXMania.E2E.Fixtures;

public sealed record E2EFixture(
    string RunRoot,
    string AppDataRoot,
    string DtxRoot,
    string SongDirectory,
    string ConfigPath,
    string ChartPath,
    string ArtifactRoot,
    int ApiPort,
    string ApiKey)
{
    public Uri ApiBaseUri => new($"http://127.0.0.1:{ApiPort}/");
    public Uri JsonRpcUri => new($"http://127.0.0.1:{ApiPort}/jsonrpc");
}
```

### Step 5: Add fixture builder implementation

Create `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`:

```csharp
using System.Text;

namespace DTXMania.E2E.Fixtures;

public static class E2EFixtureBuilder
{
    public const string ApiKey = "e2e-autoplay-smoke-key";
    public const string SongTitle = "E2E AutoPlay Smoke";

    public static E2EFixture Build(string runRoot, string repoRoot, int apiPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        runRoot = Path.GetFullPath(runRoot);
        repoRoot = Path.GetFullPath(repoRoot);

        var appDataRoot = Path.Combine(runRoot, "appdata");
        var dtxRoot = Path.Combine(runRoot, "DTXFiles");
        var songDirectory = Path.Combine(dtxRoot, "AutoPlaySmoke");
        var artifactRoot = Path.Combine(runRoot, "TestResults", "e2e");
        var configPath = Path.Combine(appDataRoot, "Config.ini");
        var chartPath = Path.Combine(songDirectory, "autoplay-smoke.dtx");
        var systemRoot = Path.Combine(repoRoot, "System");

        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(songDirectory);
        Directory.CreateDirectory(artifactRoot);

        File.WriteAllText(configPath, BuildConfig(dtxRoot, systemRoot, apiPort), Encoding.UTF8);
        File.WriteAllText(chartPath, BuildChart(), Encoding.UTF8);

        return new E2EFixture(
            runRoot,
            appDataRoot,
            dtxRoot,
            songDirectory,
            configPath,
            chartPath,
            artifactRoot,
            apiPort,
            ApiKey);
    }

    private static string BuildConfig(string dtxRoot, string systemRoot, int apiPort)
    {
        return string.Join('\n', new[]
        {
            "ScreenWidth=1280",
            "ScreenHeight=720",
            "FullScreen=False",
            "VSyncWait=False",
            "MasterVolume=100",
            "MusicVolume=100",
            "SoundEffectVolume=100",
            "AutoPlay=True",
            "NoFail=True",
            $"SkinPath={systemRoot}",
            $"SystemSkinRoot={systemRoot}",
            $"DTXPath={dtxRoot}",
            "UseBoxDefSkin=False",
            "ScrollSpeed=100",
            "AudioLatencyOffsetMs=0",
            "EnableGameApi=True",
            $"GameApiPort={apiPort}",
            $"GameApiKey={ApiKey}",
            string.Empty
        });
    }

    private static string BuildChart()
    {
        return string.Join('\n', new[]
        {
            "#TITLE: E2E AutoPlay Smoke",
            "#ARTIST: CI",
            "#BPM: 120",
            "#DLEVEL: 10",
            string.Empty,
            "; Short deterministic AutoPlay pattern with no external audio dependencies.",
            "#00011: 0100000000000000",
            "#00012: 0001000000000000",
            "#00013: 0000010000000000",
            "#00111: 0100000000000000",
            "#00112: 0001000000000000",
            "#00113: 0000010000000000",
            string.Empty
        });
    }
}
```

### Step 6: Run tests to verify pass

Run:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~E2EFixtureBuilderTests"
```

Expected: fixture builder tests pass.

### Step 7: Commit

```bash
git add DTXMania.E2E/DTXMania.E2E.csproj \
        DTXMania.E2E/Fixtures/E2ERunPaths.cs \
        DTXMania.E2E/Fixtures/E2EFixture.cs \
        DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs \
        DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs
git commit -m "test: add gameplay e2e fixture project"
```

---

## Task 5: Add JSON-RPC Client, Process Driver, And Artifact Helpers

**Files:**
- Create: `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`
- Create: `DTXMania.E2E/JsonRpc/JsonRpcGameClientTests.cs`
- Create: `DTXMania.E2E/Process/GameProcessDriver.cs`
- Create: `DTXMania.E2E/Support/E2EArtifactWriter.cs`
- Create: `DTXMania.E2E/Support/Eventually.cs`
- Create: `DTXMania.E2E/Telemetry/E2EGameState.cs`

### Step 1: Add JSON-RPC client tests

Create `DTXMania.E2E/JsonRpc/JsonRpcGameClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace DTXMania.E2E.JsonRpc;

[Trait("Category", "E2E-Support")]
public sealed class JsonRpcGameClientTests
{
    [Fact]
    public async Task SendKeyAsync_ShouldSendPressAndReleaseRequests()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:18080/") };
        var client = new JsonRpcGameClient(httpClient, "secret");

        await client.SendKeyAsync("Enter", TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("\"method\":\"sendInput\"", handler.RequestBodies[0]);
        Assert.Contains("\"type\":2", handler.RequestBodies[0]);
        Assert.Contains("\"Enter\"", handler.RequestBodies[0]);
        Assert.Contains("\"type\":3", handler.RequestBodies[1]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var responseJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new { success = true }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }
}
```

### Step 2: Run test to verify fail

Run:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~JsonRpcGameClientTests"
```

Expected: build fails because `JsonRpcGameClient` does not exist.

### Step 3: Add E2E state DTO

Create `DTXMania.E2E/Telemetry/E2EGameState.cs`:

```csharp
using System.Text.Json;

namespace DTXMania.E2E.Telemetry;

public sealed class E2EGameState
{
    public string CurrentStage { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> CustomData { get; set; } = new();

    public JsonElement Telemetry
    {
        get
        {
            if (CustomData.TryGetValue("telemetry", out var telemetry))
                return telemetry;

            throw new InvalidOperationException("Game state did not include customData.telemetry.");
        }
    }

    public string StageType => GetTelemetryString("stageType") ?? string.Empty;
    public string? SelectedSongTitle => GetTelemetryString("selectedSongTitle");
    public string? CompletionReason => GetTelemetryString("completionReason");
    public int TotalNotes => GetTelemetryInt("totalNotes") ?? 0;
    public int Score => GetTelemetryInt("score") ?? 0;
    public bool ClearFlag => GetTelemetryBool("clearFlag") ?? false;
    public int TotalJudgements => GetTelemetryInt("totalJudgements") ?? 0;

    private string? GetTelemetryString(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private int? GetTelemetryInt(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private bool? GetTelemetryBool(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
```

### Step 4: Add JSON-RPC client

Create `DTXMania.E2E/JsonRpc/JsonRpcGameClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using DTXMania.E2E.Telemetry;

namespace DTXMania.E2E.JsonRpc;

public sealed class JsonRpcGameClient
{
    private readonly HttpClient _httpClient;
    private int _nextId;

    public JsonRpcGameClient(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrWhiteSpace(apiKey))
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<E2EGameState> GetGameStateAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync("getGameState", null, cancellationToken);
        return result.Deserialize<E2EGameState>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("getGameState returned an empty result.");
    }

    public async Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken)
    {
        await SendInputAsync(2, key, cancellationToken);
        if (holdDuration > TimeSpan.Zero)
            await Task.Delay(holdDuration, cancellationToken);
        await SendInputAsync(3, key, cancellationToken);
    }

    public async Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken)
    {
        var result = await SendAsync("takeScreenshot", null, cancellationToken);
        return result.TryGetProperty("imageData", out var imageData) ? imageData.GetString() : null;
    }

    private Task<JsonElement> SendInputAsync(int type, string key, CancellationToken cancellationToken)
    {
        return SendAsync("sendInput", new { type, data = key }, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextId),
            method,
            @params = parameters
        };

        using var response = await _httpClient.PostAsJsonAsync("jsonrpc", request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"JSON-RPC {method} failed: {error}");

        return root.GetProperty("result").Clone();
    }
}
```

### Step 5: Add process driver

Create `DTXMania.E2E/Process/GameProcessDriver.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Process;

public sealed class GameProcessDriver : IAsyncDisposable
{
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private Process? _process;

    public string StandardOutput => _stdout.ToString();
    public string StandardError => _stderr.ToString();
    public int? ExitCode => _process?.HasExited == true ? _process.ExitCode : null;

    public void Start(string repoRoot, string gameProjectPath, E2EFixture fixture)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{gameProjectPath}\"")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        startInfo.Environment["DTXMANIA_APPDATA_ROOT"] = fixture.AppDataRoot;
        startInfo.Environment["DTXMANIA_LAUNCH_TOKEN"] = Guid.NewGuid().ToString("N");

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start game process.");
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) _stdout.AppendLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) _stderr.AppendLine(e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async ValueTask DisposeAsync()
    {
        if (_process == null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
```

### Step 6: Add support helpers

Create `DTXMania.E2E/Support/Eventually.cs`:

```csharp
namespace DTXMania.E2E.Support;

public static class Eventually
{
    public static async Task<T> UntilAsync<T>(
        Func<CancellationToken, Task<T>> probe,
        Func<T, bool> predicate,
        TimeSpan timeout,
        TimeSpan interval,
        string description,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        T last = default!;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await probe(cancellationToken);
            if (predicate(last))
                return last;

            await Task.Delay(interval, cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for {description}. Last value: {last}");
    }
}
```

Create `DTXMania.E2E/Support/E2EArtifactWriter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Support;

public static class E2EArtifactWriter
{
    public static async Task WriteTextAsync(E2EFixture fixture, string fileName, string content)
    {
        Directory.CreateDirectory(fixture.ArtifactRoot);
        await File.WriteAllTextAsync(Path.Combine(fixture.ArtifactRoot, fileName), content, Encoding.UTF8);
    }

    public static async Task WriteJsonAsync(E2EFixture fixture, string fileName, object value)
    {
        Directory.CreateDirectory(fixture.ArtifactRoot);
        await using var stream = File.Create(Path.Combine(fixture.ArtifactRoot, fileName));
        await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void CopyFixtureFiles(E2EFixture fixture)
    {
        Directory.CreateDirectory(fixture.ArtifactRoot);
        File.Copy(fixture.ConfigPath, Path.Combine(fixture.ArtifactRoot, "config.ini"), overwrite: true);
        File.Copy(fixture.ChartPath, Path.Combine(fixture.ArtifactRoot, "autoplay-smoke.dtx"), overwrite: true);
    }
}
```

### Step 7: Run support tests

Run:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
```

Expected: support tests pass.

### Step 8: Commit

```bash
git add DTXMania.E2E/JsonRpc \
        DTXMania.E2E/Process \
        DTXMania.E2E/Support \
        DTXMania.E2E/Telemetry
git commit -m "test: add e2e json-rpc process harness"
```

---

## Task 6: Add AutoPlay Full-Journey E2E Test

**Files:**
- Create: `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`

### Step 1: Add the full journey test

Create `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`:

```csharp
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.JsonRpc;
using DTXMania.E2E.Process;
using DTXMania.E2E.Support;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class GameplayAutoPlaySmokeTests
{
    [Fact(Timeout = 180_000)]
    public async Task AutoPlaySmoke_ShouldNavigateToResultAndReportClearSummary()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var repoRoot = FindRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-" + Guid.NewGuid().ToString("N"));
        var apiPort = GetPortFromEnvironmentOrDefault();
        var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort);
        await using var process = new GameProcessDriver();

        try
        {
            var projectPath = Environment.GetEnvironmentVariable("DTXMANIA_E2E_GAME_PROJECT")
                ?? (OperatingSystem.IsWindows()
                    ? "DTXMania.Game/DTXMania.Game.Windows.csproj"
                    : "DTXMania.Game/DTXMania.Game.Mac.csproj");

            process.Start(repoRoot, projectPath, fixture);

            using var httpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
            {
                BaseAddress = fixture.ApiBaseUri,
                Timeout = TimeSpan.FromSeconds(5)
            };
            var client = new JsonRpcGameClient(httpClient, fixture.ApiKey);

            await Eventually.UntilAsync(
                _ => client.IsHealthyAsync(cancellation.Token),
                healthy => healthy,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(500),
                "JSON-RPC health",
                cancellation.Token);

            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);

            await WaitForStageAsync(client, "SongSelect", TimeSpan.FromSeconds(45), cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);

            await WaitForStageAsync(client, "Performance", TimeSpan.FromSeconds(60), cancellation.Token);
            var resultState = await WaitForStageAsync(client, "Result", TimeSpan.FromSeconds(90), cancellation.Token);

            await E2EArtifactWriter.WriteJsonAsync(fixture, "final-state.json", resultState);

            Assert.Equal(E2EFixtureBuilder.SongTitle, resultState.SelectedSongTitle);
            Assert.True(resultState.TotalNotes > 0, "Expected generated chart to contain notes.");
            Assert.Equal(resultState.TotalNotes, resultState.TotalJudgements);
            Assert.True(resultState.ClearFlag);
            Assert.True(resultState.Score > 0);
            Assert.Equal("SongComplete", resultState.CompletionReason);
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr.log", process.StandardError);
        }
    }

    private static async Task<Telemetry.E2EGameState> WaitForStageAsync(
        JsonRpcGameClient client,
        string expectedStageType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, expectedStageType, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(500),
            expectedStageType,
            cancellationToken);
    }

    private static int GetPortFromEnvironmentOrDefault()
    {
        var raw = Environment.GetEnvironmentVariable("DTXMANIA_E2E_API_PORT");
        return int.TryParse(raw, out var port) ? port : 18080;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DTXMania.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from current directory.");
    }
}
```

### Step 2: Run E2E locally if the current platform can launch MonoGame

On macOS:

```bash
DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Mac.csproj \
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

On Windows:

```powershell
$env:ALSOFT_DRIVERS="null"
$env:DTXMANIA_E2E_GAME_PROJECT="DTXMania.Game/DTXMania.Game.Windows.csproj"
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

Expected: test may need one stabilization pass locally because it launches a real MonoGame window. If it fails, inspect `TestResults/e2e/` under the generated temp run root and fix the boundary named in the exception.

### Step 3: Make artifact path CI-friendly if needed

If local artifacts are hard to find, update `E2EFixtureBuilder.Build` so `artifactRoot` uses:

```csharp
var artifactRoot = Environment.GetEnvironmentVariable("DTXMANIA_E2E_ARTIFACT_ROOT");
if (string.IsNullOrWhiteSpace(artifactRoot))
    artifactRoot = Path.Combine(runRoot, "TestResults", "e2e");
```

Then CI can set `DTXMANIA_E2E_ARTIFACT_ROOT=TestResults/e2e`.

Add or update a fixture builder test to assert the env override.

### Step 4: Commit

```bash
git add DTXMania.E2E/GameplayAutoPlaySmokeTests.cs \
        DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs \
        DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs
git commit -m "test: add autoplay gameplay e2e smoke"
```

---

## Task 7: Add Non-Blocking Windows E2E CI Job

**Files:**
- Modify: `.github/workflows/build-and-test.yml`

### Step 1: Add the job

Append this job after `build-and-test-windows` and before `build-and-test-macos` or after both build/test jobs:

```yaml
  gameplay-e2e-windows:
    runs-on: windows-latest
    continue-on-error: true

    steps:
    - name: Checkout code
      uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Cache NuGet packages
      uses: actions/cache@v4
      with:
        path: ~/.nuget/packages
        key: ${{ runner.os }}-nuget-e2e-${{ hashFiles('**/*.csproj') }}
        restore-keys: |
          ${{ runner.os }}-nuget-

    - name: Restore Windows game
      run: dotnet restore DTXMania.Game/DTXMania.Game.Windows.csproj

    - name: Restore E2E project
      run: dotnet restore DTXMania.E2E/DTXMania.E2E.csproj

    - name: Build Windows game
      run: dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Debug --no-restore

    - name: Run gameplay E2E smoke
      env:
        ALSOFT_DRIVERS: 'null'
        DTXMANIA_E2E_GAME_PROJECT: DTXMania.Game/DTXMania.Game.Windows.csproj
        DTXMANIA_E2E_ARTIFACT_ROOT: TestResults/e2e
      run: dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --verbosity normal --logger trx --results-directory ./TestResults/e2e --filter "Category=E2E"

    - name: Upload gameplay E2E artifacts
      uses: actions/upload-artifact@v4
      if: always()
      with:
        name: gameplay-e2e-windows
        path: ./TestResults/e2e
```

### Step 2: Validate YAML shape locally

Run:

```bash
git diff --check .github/workflows/build-and-test.yml
```

Expected: no whitespace errors.

### Step 3: Commit

```bash
git add .github/workflows/build-and-test.yml
git commit -m "ci: add non-blocking gameplay e2e job"
```

---

## Task 8: Full Verification

**Files:**
- No intended source edits unless verification exposes a bug.

### Step 1: Run targeted unit/support suites

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~GameApiImplementationTests|FullyQualifiedName~PopulateTelemetry"
```

Expected: pass.

Run:

```bash
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
```

Expected: pass.

### Step 2: Run full Mac-safe test suite

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: pass.

### Step 3: Build Mac game

Run:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: pass.

### Step 4: Run local E2E smoke if feasible

Run on macOS:

```bash
DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Mac.csproj \
DTXMANIA_E2E_ARTIFACT_ROOT=TestResults/e2e \
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

Expected: pass if the local environment can launch a MonoGame window. If it fails for environment/window reasons, preserve artifacts and note the exact boundary. Do not claim the E2E lane is verified locally unless it actually reaches `Result`.

### Step 5: Final status check

Run:

```bash
git status --short
```

Expected: clean working tree after commits.

---

## Execution Handoff

After this plan is approved, implementation should be done with `superpowers:executing-plans` or `superpowers:subagent-driven-development`. Execute one task at a time, keep commits small, and rerun the targeted verification listed in each task before committing.
