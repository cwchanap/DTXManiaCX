# HPA-10 Gameplay Fail Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persisted Risky, Damage Level, and Auto Add Gauge controls while making the existing No Fail option authoritative over gauge failure state.

**Architecture:** `ConfigData` / `ConfigManager` own persisted settings, `ConfigStage` exposes them, `GaugeManager` owns immutable per-run damage/failure policy, and `PerformanceStage` freezes all relevant settings at one activation-time snapshot. A raised `GaugeManager.Failed` event or `HasFailed=true` is authoritative; `PerformanceStage` never re-reads `Config.NoFail` for failure decisions.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, SQLite-backed `ConfigManager`.

**Spec:** `docs/superpowers/specs/2026-08-20-hpa-10-fail-rules-design.md`

## Global Constraints

- Keep HPA-10 to one implementation PR and at most 3 engineer-days.
- New controls stay in the existing Drums category.
- Reuse `NoFail`; do not add `StageFailed`.
- Do not implement StoicMode, HAZARD, guitar/bass rules, per-lane AutoPlay, NX special Risky rendering, or a gameplay-rules framework.
- Defaults remain `Risky=0`, `DamageLevel=Normal`, `AutoAddGauge=true`.
- `Risky > 0` fails on the Nth `Poor` / `Miss` and replaces life-threshold failure for the run.
- `NoFail=true` prevents both Risky and life-threshold failure and must keep the gauge processing later judgements.
- `AutoAddGauge=false` affects only gauge forwarding during AutoPlay.
- Local unit verification on this macOS development path uses `DTXMania.Test/DTXMania.Test.Mac.csproj`; hosted Windows CI owns the full Windows suite and gameplay E2E.

---

## File map

### Configuration

- Create: `DTXMania.Game/Lib/Config/GaugeDamageLevel.cs`
- Create: `DTXMania.Game/Lib/Config/RiskyRange.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Test: `DTXMania.Test/Config/ConfigDataTests.cs`
- Test: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Test: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Test doubles: existing `IConfigManager` stubs, including `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`, only where interface compilation requires it

### Gameplay

- Modify: `DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Test: `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`
- Test: `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs`
- Test: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

### Existing E2E contract

- Optionally modify only for explicit default assertions: `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`
- No new E2E harness or scenario class

---

## Task 1: Persist and expose Risky, Damage Level, and Auto Add Gauge

**Estimate:** 0.75 engineer-day

**Interfaces:**

- Produces `GaugeDamageLevel`.
- Produces `RiskyRange.Min/Max/Step/Default`, `RiskyRange.Clamp(int)`, `RiskyRange.Format(int)`.
- Produces `ConfigData.Risky`, `ConfigData.DamageLevel`, `ConfigData.AutoAddGauge`.
- Produces `IConfigManager.SetRisky`, `SetDamageLevel`, `SetAutoAddGauge`.

- [ ] **Step 1: Add failing default and UI-inventory tests**

In `ConfigDataTests.cs`, pin:

```csharp
[Fact]
public void Constructor_FailRuleDefaults_PreserveCurrentGameplay()
{
    var config = new ConfigData();

    Assert.Equal(RiskyRange.Default, config.Risky);
    Assert.Equal(GaugeDamageLevel.Normal, config.DamageLevel);
    Assert.True(config.AutoAddGauge);
}
```

In `ConfigStageLogicTests.SetupConfigItems_ShouldBuildSystemDrumsExitCategories`, keep the existing full `Assert.Collection` and change the Drums list to exactly:

```text
Scroll Speed
Play Speed
Pitch
Metronome
Auto Play
Auto Add Gauge
No Fail
Risky
Damage Level
Drum Key Mapping
```

Do not replace the full collection with a tail-only assertion.

Also extend existing item-activation tests so Auto Add Gauge, Risky, and Damage Level mutate through `IConfigManager`.

- [ ] **Step 2: Run focused config tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: compile/test failure because the new config types/properties/setters do not exist.

- [ ] **Step 3: Add the two small config value/range files**

`GaugeDamageLevel.cs`:

```csharp
namespace DTXMania.Game.Lib.Config;

public enum GaugeDamageLevel
{
    Low,
    Normal,
    High
}
```

Use the repository's existing namespace style if this folder is not file-scoped.

`RiskyRange.cs`:

```csharp
using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Config;

public static class RiskyRange
{
    public const int Min = 0;
    public const int Max = 10;
    public const int Step = 1;
    public const int Default = 0;

    public static int Clamp(int value) => Math.Clamp(value, Min, Max);

    public static string Format(int value) =>
        value == Default ? "Off" : value.ToString(CultureInfo.InvariantCulture);
}
```

Use `RiskyRange` everywhere instead of repeating the `0..10` contract.

- [ ] **Step 4: Add ConfigData and interface members**

In `ConfigData.cs`:

```csharp
public int Risky { get; set; } = RiskyRange.Default;
public GaugeDamageLevel DamageLevel { get; set; } = GaugeDamageLevel.Normal;
public bool AutoAddGauge { get; set; } = true;
```

In `IConfigManager.cs`:

```csharp
void SetRisky(int value);
void SetDamageLevel(GaugeDamageLevel value);
void SetAutoAddGauge(bool value);
```

Update existing interface test doubles in the same compile slice; do not add a new fake abstraction.

- [ ] **Step 5: Add failing parse/persist/setter tests**

Extend `ConfigManagerTests.cs` using existing temp-store helpers. Pin:

```text
Risky=-4  -> 0
Risky=0   -> 0
Risky=7   -> 7
Risky=99  -> 10
DamageLevel=low    -> Low
DamageLevel=NORMAL -> Normal
DamageLevel=High   -> High
DamageLevel=garbage -> Normal
DamageLevel=0 -> Normal (invalid numeric input)
DamageLevel=1 -> Normal (invalid numeric input; same value as default by coincidence)
DamageLevel=2 -> Normal (invalid numeric input)
AutoAddGauge=false -> false
AutoAddGauge=0     -> false
AutoAddGauge=on    -> true
```

Round-trip one non-default snapshot:

```csharp
manager.SetRisky(4);
manager.SetDamageLevel(GaugeDamageLevel.High);
manager.SetAutoAddGauge(false);
manager.FlushPendingSave();
```

Reload and assert `4`, `High`, `false`. Also assert setter clamping uses `RiskyRange`.

- [ ] **Step 6: Implement name-only DamageLevel parsing and persistence**

`Enum.TryParse + Enum.IsDefined` must **not** be used as the numeric-string guard; defined numeric strings still succeed.

Use enum-name matching first:

```csharp
case "DamageLevel":
{
    var matchedName = Enum.GetNames<GaugeDamageLevel>()
        .FirstOrDefault(name =>
            string.Equals(name, value, StringComparison.OrdinalIgnoreCase));

    if (matchedName != null)
        Config.DamageLevel = Enum.Parse<GaugeDamageLevel>(matchedName);
    break;
}
```

Risky parse:

```csharp
case "Risky":
    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var risky))
        Config.Risky = RiskyRange.Clamp(risky);
    break;
```

Auto Add Gauge parse uses existing `TryParseBool`.

Snapshot entries:

```csharp
entries["Risky"] = RiskyRange.Clamp(Config.Risky)
    .ToString(CultureInfo.InvariantCulture);
entries["DamageLevel"] = Config.DamageLevel.ToString();
entries["AutoAddGauge"] = Config.AutoAddGauge.ToString();
```

Setters follow `SetMetronome`'s no-op-on-equal pattern; `SetRisky` calls `RiskyRange.Clamp`.

- [ ] **Step 7: Add the ConfigStage controls and corrected No Fail copy**

Use the existing item types.

Risky:

```csharp
var riskyItem = new IntegerConfigItem(
    "Risky",
    () => _configManager.Config.Risky,
    value => _configManager.SetRisky(value),
    minValue: RiskyRange.Min,
    maxValue: RiskyRange.Max,
    step: RiskyRange.Step,
    valueFormatter: RiskyRange.Format)
{
    Description = "Fail after this many Poor/Miss judgements. Off uses the life gauge."
};
```

Damage Level options come from the enum itself:

```csharp
var damageLevelItem = new DropdownConfigItem(
    "Damage Level",
    () => _configManager.Config.DamageLevel.ToString(),
    Enum.GetNames<GaugeDamageLevel>(),
    value =>
    {
        if (Enum.TryParse<GaugeDamageLevel>(value, true, out var level))
            _configManager.SetDamageLevel(level);
    })
{
    Description = "Controls Miss damage to the life gauge."
};
```

Auto Add Gauge description:

```text
Allow Auto Play judgements to change the life gauge.
```

Change existing No Fail description to exactly:

```text
Continue playing without entering a failed gauge state.
```

- [ ] **Step 8: Verify Task 1 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 9: Commit Task 1**

```bash
git add -- \
  DTXMania.Game/Lib/Config/GaugeDamageLevel.cs \
  DTXMania.Game/Lib/Config/RiskyRange.cs \
  DTXMania.Game/Lib/Config/ConfigData.cs \
  DTXMania.Game/Lib/Config/IConfigManager.cs \
  DTXMania.Game/Lib/Config/ConfigManager.cs \
  DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Test/Config/ConfigDataTests.cs \
  DTXMania.Test/Config/ConfigManagerTests.cs \
  DTXMania.Test/Config/ConfigStageLogicTests.cs \
  DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "feat: add configurable gameplay fail rules"
```

---

## Task 2: Make GaugeManager the single damage/failure authority

**Estimate:** 0.75 engineer-day

**Interfaces:**

Produces:

```csharp
GaugeManager(
    float startingLife = StartingLife,
    GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal,
    int riskyLimit = RiskyRange.Default,
    bool failureEnabled = true)
```

Existing `GaugeChanged`, `Failed`, `HasFailed`, and `GetLifeAdjustment` contracts remain.

- [ ] **Step 1: Add failing DamageLevel tests**

In `GaugeManagerTests.cs`:

```csharp
[Theory]
[InlineData(GaugeDamageLevel.Low, -1.5f)]
[InlineData(GaugeDamageLevel.Normal, -3.0f)]
[InlineData(GaugeDamageLevel.High, -4.5f)]
public void GetLifeAdjustment_Miss_UsesDamageLevel(
    GaugeDamageLevel level,
    float expected)
{
    var manager = new GaugeManager(damageLevel: level);
    Assert.Equal(expected, manager.GetLifeAdjustment(JudgementType.Miss));
}
```

Also pin `Poor == -1.5f` for all three levels and retain the existing parameterless adjustment theory.

- [ ] **Step 2: Add failing Risky and No Fail tests**

Pin public behavior:

1. `riskyLimit:3` fails on the third combined `Poor`/`Miss`, not on Perfect/Great/Good.
2. Risky mode ignores life-threshold failure until its counter reaches zero.
3. `failureEnabled:false` never sets `HasFailed` or fires `Failed` when life reaches zero.
4. After reaching zero with failure disabled, a later Perfect increases life, proving the gauge is not terminal/frozen.
5. `Reset()` restores the Risky allowance.
6. A real failure raises `Failed` exactly once and later judgements are ignored.
7. Constructor Risky input uses `RiskyRange.Clamp` semantics.

- [ ] **Step 3: Run focused gauge tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: failures because the current manager has no policy inputs.

- [ ] **Step 4: Implement the minimal constructor state**

Add fields for:

```text
_damageLevel
_initialRiskyLimit
_remainingRisky
_failureEnabled
```

Constructor stores `RiskyRange.Clamp(riskyLimit)` and initializes remaining count from the clamped value.

Do not add `IGaugeRule`, a policy record, service registration, or an event bus.

- [ ] **Step 5: Scale only Miss damage**

Keep current adjustments unchanged except:

```csharp
JudgementType.Miss => -3.0f * GetMissDamageMultiplier(_damageLevel)
```

with:

```csharp
GaugeDamageLevel.Low => 0.5f,
GaugeDamageLevel.Normal => 1.0f,
GaugeDamageLevel.High => 1.5f,
_ => 1.0f
```

Do not change Poor, Good, Great, or Perfect tuning.

- [ ] **Step 6: Implement ordered failure semantics**

Inside `ProcessJudgement` preserve the existing top-level guard for disposed/real-failed state, then:

```text
apply life adjustment and clamp
if Risky active and judgement is Poor or Miss: decrement once
if failure disabled: shouldFail = false
else if Risky active: shouldFail = remainingRisky <= 0
else: shouldFail = life < FailureThreshold
if shouldFail: set HasFailed and fire Failed once
always fire GaugeChanged for processed judgement
```

Risky changes the failure criterion, not the normal life arithmetic.

- [ ] **Step 7: Make Reset restore Risky state**

Reset life and `HasFailed` as today, plus:

```csharp
_remainingRisky = _initialRiskyLimit;
```

Do not add a public Risky-readout API in HPA-10 solely for the deferred UI follow-up.

- [ ] **Step 8: Verify Task 2 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: PASS.

- [ ] **Step 9: Commit Task 2**

```bash
git add -- \
  DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs \
  DTXMania.Test/Stage/Performance/GaugeManagerTests.cs \
  DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs
git commit -m "feat: apply gameplay failure policy in gauge manager"
```

---

## Task 3: Freeze one run snapshot and wire PerformanceStage

**Estimate:** 0.75 engineer-day

**Files:**

- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Test: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Optional explicit fixture-default assertion: `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`

- [ ] **Step 1: Add failing snapshot/wiring tests**

Extend `PerformanceStageDeterministicTests.cs` to pin:

1. `InitializeAutoPlay()` freezes AutoPlay, AutoAddGauge, DamageLevel, Risky, and `!NoFail` together.
2. `InitializeGameplayManagers()` consumes those frozen fields and does not need a live `ConfigManager`.
3. Existing tests `InitializeGameplayManagers_WhenDependenciesExist_ShouldCreateManagersAndInitializeState` and the cleanup sibling continue to work when their `ReflectionHelpers.CreateGame()` has no ConfigManager.
4. AutoPlay + AutoAddGauge Off skips only the gauge call.
5. Manual play updates the gauge even when AutoAddGauge is Off.
6. A genuine `Failed` event finalizes with `CompletionReason.PlayerFailed`.
7. `CheckStageCompletion` finalizes when `HasFailed` is true without consulting current config.

Replace, do not preserve:

```text
OnPlayerFailed_WhenNoFailEnabled_ShouldNotFinalizePerformance
```

That test is the old split-ownership contract. Replace it with a test that a raised `Failed` event always finalizes; No Fail itself is already proven by GaugeManager never raising the event and continuing to process recovery.

- [ ] **Step 2: Run focused stage tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests"
```

Expected: failures because the current stage still reads NoFail in two places and does not freeze the new policy.

- [ ] **Step 3: Add safe frozen-policy fields**

Near existing `_autoPlayEnabled`, add fields with safe defaults so reflection tests that call manager initialization directly preserve current behavior:

```csharp
private bool _autoAddGaugeEnabled = true;
private GaugeDamageLevel _gaugeDamageLevel = GaugeDamageLevel.Normal;
private int _riskyLimit = RiskyRange.Default;
private bool _gaugeFailureEnabled = true;
```

Do not introduce a snapshot record for five scalar values.

- [ ] **Step 4: Freeze all run values in InitializeAutoPlay()**

Use one null-safe snapshot point:

```csharp
var config = _game?.ConfigManager?.Config;
_autoPlayEnabled = config?.AutoPlay ?? false;
_autoAddGaugeEnabled = config?.AutoAddGauge ?? true;
_gaugeDamageLevel = config?.DamageLevel ?? GaugeDamageLevel.Normal;
_riskyLimit = RiskyRange.Clamp(config?.Risky ?? RiskyRange.Default);
_gaugeFailureEnabled = !(config?.NoFail ?? false);
_autoPlayNoteIndex = 0;
```

Do not read these settings again in later async initialization.

- [ ] **Step 5: Construct GaugeManager only from frozen fields**

In `InitializeGameplayManagers()`:

```csharp
_gaugeManager = new GaugeManager(
    GaugeManager.StartingLife,
    _gaugeDamageLevel,
    _riskyLimit,
    _gaugeFailureEnabled);
```

Do not dereference `_game.ConfigManager.Config` here. This avoids NREs in existing reflection tests and prevents config changes between activation and async manager creation from producing a mixed run policy.

- [ ] **Step 6: Gate only AutoPlay-to-gauge forwarding**

In `OnJudgementMade`:

```csharp
_scoreManager?.ProcessJudgement(e);
_comboManager?.ProcessJudgement(e);

if (!_autoPlayEnabled || _autoAddGaugeEnabled)
    _gaugeManager?.ProcessJudgement(e);

_skillManager?.ProcessJudgement(e);
```

Keep existing skill-panel, attack-effect, pad-feedback, and judgement-popup behavior unchanged.

- [ ] **Step 7: Remove both duplicate No Fail decisions**

`OnPlayerFailed` becomes:

```csharp
private void OnPlayerFailed(object? sender, FailureEventArgs e)
{
    if (!_stageCompleted)
        FinalizePerformance(CompletionReason.PlayerFailed);
}
```

`CheckStageCompletion` uses only manager state:

```csharp
if (_gaugeManager?.HasFailed == true)
    FinalizePerformance(CompletionReason.PlayerFailed);
```

Do not live-read `Config.NoFail` in either path.

- [ ] **Step 8: Verify Task 3 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: PASS.

- [ ] **Step 9: Commit Task 3**

```bash
git add -- \
  DTXMania.Game/Lib/Stage/PerformanceStage.cs \
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
git commit -m "feat: wire configured fail rules into gameplay"
```

---

## Task 4: Final regression and acceptance gate

**Estimate:** 0.25 engineer-day

- [ ] **Step 1: Run the complete Mac-safe unit suite locally**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
```

Expected: PASS. Do not substitute the Windows-only `DTXMania.Test.csproj` on macOS; it includes graphics-dependent tests excluded by the Mac project.

- [ ] **Step 2: Pin the existing E2E fixture assumption if the implementation touches fixture coverage**

The current fixture contains `AutoPlay=True` and `NoFail=True` but no `AutoAddGauge` key. If editing `E2EFixtureBuilderTests.cs`, add only these assertions after production `ConfigManager.LoadConfig()`:

```csharp
Assert.True(configManager.Config.AutoPlay);
Assert.True(configManager.Config.NoFail);
Assert.True(configManager.Config.AutoAddGauge);
```

No new fixture format or E2E scenario is needed.

- [ ] **Step 3: Require hosted Windows CI gates**

Before HPA-10 is considered complete, the PR must have green existing Windows coverage for:

```text
DTXMania.Test/DTXMania.Test.csproj full suite
existing gameplay E2E smoke: AutoPlay + NoFail reaches cleared Result
```

This is CI ownership; do not add a second local E2E harness.

- [ ] **Step 4: Record the known Risky presentation limitation in the PR**

State explicitly:

```text
Risky changes failure semantics only in HPA-10. The normal life gauge can show 0/danger while Risky allowance remains. Remaining-Risky HUD is intentionally deferred; Linear follow-up creation is currently blocked by the workspace free-issue limit.
```

Do not add Risky rendering to this PR to hide the limitation.

- [ ] **Step 5: Run diff hygiene**

```bash
git diff --check
git status --short
```

Expected: no whitespace errors; only HPA-10 files are modified.

---

## Self-review checklist

Before implementation handoff, verify:

- every `0..10` Risky rule goes through `RiskyRange`;
- DamageLevel persisted parsing accepts names only, not numeric enum strings;
- ConfigStage options come from `Enum.GetNames<GaugeDamageLevel>()`;
- all five run values freeze in `InitializeAutoPlay()`;
- `InitializeGameplayManagers()` does not dereference ConfigManager;
- `OnPlayerFailed` and `CheckStageCompletion` contain no `Config.NoFail` decision;
- `CompletionReason.PlayerFailed` is the only finalization type named;
- the old NoFail-swallow test is replaced, not kept;
- local commands use `DTXMania.Test.Mac.csproj`;
- Windows full suite and existing gameplay E2E remain CI gates;
- no Risky HUD/rules framework/HAZARD/guitar-bass scope slipped into HPA-10.
