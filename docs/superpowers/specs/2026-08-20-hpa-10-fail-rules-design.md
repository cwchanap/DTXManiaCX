# HPA-10 Gameplay Fail Rules Design

**Date:** 2026-08-20  
**Status:** Approved for implementation planning  
**Linear:** HPA-10

## Objective

Add the remaining useful DTXManiaNX-inspired gameplay failure controls without recreating obsolete NX configuration or introducing a generic rules framework.

HPA-10 stays one implementation ticket and one PR. The implementation extends the existing `ConfigData` → `ConfigManager` → `ConfigStage` → `PerformanceStage` / `GaugeManager` path.

## Current CX behavior

CX already has the required seams:

- `ConfigData` stores gameplay settings such as `AutoPlay` and `NoFail`.
- `ConfigManager` persists those values through the SQLite-backed key/value snapshot and exposes narrow setters.
- `ConfigStage` presents gameplay options in the `Drums` category.
- `PerformanceStage` forwards each judgement to score, combo, gauge, and skill managers.
- `GaugeManager` owns life changes and raises `Failed` when its failure criterion is met.

There is one existing defect that HPA-10 must close: `NoFail` is currently checked only by `PerformanceStage.OnPlayerFailed`. `GaugeManager` still sets `HasFailed = true` and then ignores later judgements, so the stage continues with a terminal frozen gauge. `CheckStageCompletion` also live-reads `Config.NoFail`, creating a second failure-policy decision.

After HPA-10, `GaugeManager` is the sole owner of whether the run may enter a failed state. `PerformanceStage` treats `Failed` / `HasFailed` as authoritative and never re-evaluates `NoFail`.

## NX findings that affect scope

### Risky

NX `CActPerfCommonGauge` treats `Risky > 0` as an alternate failure rule. It decrements the remaining count on `Poor` and `Miss` and fails when the count reaches zero.

CX will preserve that judgement set but will not port NX's special Risky gauge visualization or drain math. The normal CX life gauge continues to move; Risky changes only the failure criterion.

### Damage Level

NX applies the configured damage factor to `Miss`. CX will keep its current gauge tuning and apply a small relative multiplier only to `Miss`:

| Level | Miss multiplier | CX Miss delta |
| --- | ---: | ---: |
| Low | 0.5x | -1.5 |
| Normal | 1.0x | -3.0 |
| High | 1.5x | -4.5 |

`Poor` remains `-1.5` for all levels. `Normal` therefore preserves current behavior.

### StageFailed / No Fail

Do **not** add a second `StageFailed` setting. CX already exposes the inverse behavior as `NoFail`.

- `NoFail=false`: gauge/Risky failure is enabled.
- `NoFail=true`: the gauge may reach zero but must not enter the terminal failed state or raise `Failed`.

The existing No Fail menu copy changes to:

> Continue playing without entering a failed gauge state.

### AutoAddGage

NX conditionally forwards autoplay judgements to its gauge. CX can implement the same useful behavior without extending `JudgementEvent`: AutoPlay is global for a performance and `PerformanceStage` already drives auto hits itself.

Add a CX-named `AutoAddGauge` setting. When AutoPlay is active and `AutoAddGauge=false`, `PerformanceStage` skips only `_gaugeManager.ProcessJudgement(...)`. Score, combo, skill, note resolution, pad/judgement feedback, and audio behavior remain unchanged.

Default `AutoAddGauge=true` so current AutoPlay behavior is preserved.

### StoicMode

Do not implement `StoicMode` in HPA-10. It is a presentation concern, the corresponding NX config item is commented out, and CX does not need a presentation-policy abstraction for this task.

## User-facing settings

Keep the existing `System / Drums / Exit` navigation. The affected `Drums` list is:

1. Scroll Speed
2. Play Speed
3. Pitch
4. Metronome
5. Auto Play
6. Auto Add Gauge
7. No Fail
8. Risky
9. Damage Level
10. Drum Key Mapping

New controls:

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| Risky | integer 0–10 | 0 | `0 = Off`; otherwise fail on the Nth `Poor` / `Miss` |
| Damage Level | Low / Normal / High | Normal | scales only `Miss` gauge damage |
| Auto Add Gauge | toggle | On | when Off during Auto Play, auto judgements do not change the gauge |

Descriptions:

- **Auto Add Gauge:** `Allow Auto Play judgements to change the life gauge.`
- **No Fail:** `Continue playing without entering a failed gauge state.`
- **Risky:** `Fail after this many Poor/Miss judgements. Off uses the life gauge.`
- **Damage Level:** `Controls Miss damage to the life gauge.`

## Configuration model and persistence

Create the closed enum in its own config file, following existing `Lib/Config` value/range types such as `PlaySpeedRange`, `PitchRange`, and `ScrollSpeedRange`:

`DTXMania.Game/Lib/Config/GaugeDamageLevel.cs`

```csharp
namespace DTXMania.Game.Lib.Config
{
    public enum GaugeDamageLevel
    {
        Low,
        Normal,
        High
    }
}
```

Extend `ConfigData` with:

```csharp
public int Risky { get; set; } = 0;
public GaugeDamageLevel DamageLevel { get; set; } = GaugeDamageLevel.Normal;
public bool AutoAddGauge { get; set; } = true;
```

Persist exactly these keys in the existing SQLite snapshot:

```text
Risky
DamageLevel
AutoAddGauge
```

Parsing rules:

- `Risky`: invariant integer, clamp to `0..10`.
- `DamageLevel`: case-insensitive enum name only. Require both `Enum.TryParse(...)` and `Enum.IsDefined(...)`; numeric strings such as `0`, `1`, and `2` are invalid persisted values and leave the default `Normal` unchanged.
- `AutoAddGauge`: reuse `TryParseBool`.

Persist `DamageLevel` using `ToString()` so the database contains `Low`, `Normal`, or `High`, never numeric enum values.

`IConfigManager` gains only:

```csharp
void SetRisky(int value);
void SetDamageLevel(GaugeDamageLevel value);
void SetAutoAddGauge(bool value);
```

All three use the existing no-op-on-equal + deferred-save pattern.

No migration alias is required because these keys have never existed in CX.

## GaugeManager runtime policy

Extend `GaugeManager` with immutable per-run policy:

```csharp
public GaugeManager(
    float startingLife = StartingLife,
    GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal,
    int riskyLimit = 0,
    bool failureEnabled = true)
```

Clamp `riskyLimit` to `0..10` in the constructor. Keep initial and remaining Risky counts internally so `Reset()` restores them.

No `IGaugeRule`, strategy hierarchy, policy service, event bus, or DI registration is needed.

### Judgement processing order

For each judgement:

1. Apply the normal life adjustment, scaling only `Miss` by Damage Level.
2. If `Risky > 0` and the judgement is `Poor` or `Miss`, decrement the remaining Risky count once.
3. If `failureEnabled == false`, never set `HasFailed` and never raise `Failed`.
4. Else if `Risky > 0`, fail only when the Risky counter reaches zero; low life alone does not fail.
5. Else use the existing life-threshold failure rule.

Once a real failure occurs, retain current terminal behavior: set `HasFailed=true`, raise `Failed` once, and ignore later judgements.

When failure is disabled, reaching zero is not terminal; later positive judgements must continue to recover the gauge.

## PerformanceStage ownership

### Freeze run settings

Extend `InitializeAutoPlay()` to freeze both settings used by judgement forwarding:

```csharp
_autoPlayEnabled = config.AutoPlay;
_autoAddGaugeEnabled = config.AutoAddGauge;
```

Do not live-read `AutoAddGauge` per judgement.

At `InitializeGameplayManagers()`, construct the gauge from the current run snapshot:

```csharp
var config = _game.ConfigManager.Config;
_gaugeManager = new GaugeManager(
    GaugeManager.StartingLife,
    config.DamageLevel,
    config.Risky,
    failureEnabled: !config.NoFail);
```

### Gate only gauge forwarding

`OnJudgementMade` keeps the existing score/combo/skill order and conditionally forwards to the gauge:

```csharp
_scoreManager?.ProcessJudgement(e);
_comboManager?.ProcessJudgement(e);
if (!_autoPlayEnabled || _autoAddGaugeEnabled)
{
    _gaugeManager?.ProcessJudgement(e);
}
_skillManager?.ProcessJudgement(e);
```

No autoplay-origin field is added to `JudgementEvent`.

### One failure authority

A raised `Failed` event already means the frozen run policy allows failure. Therefore `OnPlayerFailed` must not read config:

```csharp
private void OnPlayerFailed(object? sender, FailureEventArgs e)
{
    if (!_stageCompleted)
    {
        FinalizePerformance(CompletionReason.PlayerFailed);
    }
}
```

`CheckStageCompletion` keeps the defensive polling path but also treats `HasFailed` as authoritative:

```csharp
if (_gaugeManager?.HasFailed == true)
{
    FinalizePerformance(CompletionReason.PlayerFailed);
}
```

It must not read `Config.NoFail`. This prevents a mid-run config change from disagreeing with the frozen `GaugeManager` policy.

## Testing strategy

Use existing suites only.

### Configuration

Extend:

- `DTXMania.Test/Config/ConfigDataTests.cs`
- `DTXMania.Test/Config/ConfigManagerTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` only for the existing `IConfigManager` test double

Pin defaults, clamping, strict Damage Level parsing (including numeric strings rejected), round-trip persistence, setters, the exact full Drums inventory, and the updated No Fail description.

### Gauge rules

Extend:

- `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`
- `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs`

Pin:

- parameterless/default behavior remains unchanged;
- Low/High scale only `Miss`;
- Risky fails exactly on the Nth `Poor` / `Miss`;
- Risky mode ignores life-threshold failure before counter exhaustion;
- `failureEnabled=false` never raises `Failed`, never sets `HasFailed`, and still accepts a later Perfect after reaching zero;
- `Reset()` restores Risky state;
- real failure still raises `Failed` once.

### Stage integration

Extend `PerformanceStageDeterministicTests.cs` to prove:

- AutoPlay and AutoAddGauge are frozen together;
- the configured Damage Level / Risky / No Fail snapshot is passed to `GaugeManager`;
- AutoAddGauge Off suppresses only gauge updates during AutoPlay;
- manual play still updates the gauge regardless of AutoAddGauge;
- replace the obsolete `OnPlayerFailed_WhenNoFailEnabled_ShouldNotFinalizePerformance` test with a test that any raised `Failed` event finalizes using `CompletionReason.PlayerFailed`;
- the defensive `CheckStageCompletion` poll finalizes whenever `HasFailed` is true, without consulting a later mutable `Config.NoFail` value.

Do not add a new E2E harness.

## Non-goals

- Stoic / hidden judgement UI
- HAZARD mode
- guitar/bass failure rules
- per-lane AutoPlay
- NX special Risky gauge rendering or danger artwork
- importing the complete NX gauge table
- score/skill changes tied to AutoAddGauge
- new config categories or navigation
- generic gameplay-rules abstractions
- backward-compatibility aliases for settings that never existed in CX

## Size

Expected implementation size: **2–3 engineer-days**, one implementation PR, using existing config and performance test suites.
