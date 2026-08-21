# HPA-10 Gameplay Fail Rules Design

**Date:** 2026-08-20  
**Status:** Approved for implementation planning  
**Linear:** HPA-10

## Objective

Add the remaining useful DTXManiaNX-inspired gameplay failure controls without recreating obsolete NX configuration or introducing a generic rules framework.

HPA-10 stays one implementation ticket and one PR. The deliverable is a small extension of the existing `ConfigData` → `ConfigManager` → `ConfigStage` → `PerformanceStage`/`GaugeManager` path.

## Current CX behavior

CX already has most of the infrastructure this task needs:

- `ConfigData` stores gameplay settings such as `AutoPlay` and `NoFail`.
- `ConfigManager` persists those values through the SQLite-backed key/value snapshot and exposes narrow setters.
- `ConfigStage` presents gameplay options in the `Drums` category.
- `PerformanceStage` forwards every judgement to score, combo, gauge, and skill managers.
- `GaugeManager` owns life changes and raises `Failed` when life drops below `FailureThreshold`.

There is one existing behavioral defect that HPA-10 must correct while touching this flow: `NoFail` is currently checked only by `PerformanceStage.OnPlayerFailed`. `GaugeManager` still sets `HasFailed = true` and then ignores all later judgements. The stage continues, but the gauge is permanently frozen. `NoFail` should prevent the gauge manager from entering its terminal failed state, not merely ignore the resulting stage transition.

## NX findings that affect scope

The legacy source is useful as behavioral reference, but the Linear description is older than the current CX architecture and should not be ported literally.

### Risky

NX `CActPerfCommonGauge` treats `Risky > 0` as an alternate failure rule. It decrements the remaining count on `Poor` and `Miss` and fails when the count reaches zero. HPA-10 will preserve that actual NX judgement set rather than the issue's shorthand wording of only "misses".

CX does not need NX's separate Risky gauge rendering math. The existing life gauge may continue to update normally; Risky changes the **failure criterion**, not the renderer.

### DamageLevel

NX applies the configured damage factor to `Miss`. CX will keep its existing gauge tuning and layer the setting over it instead of importing the entire NX damage table.

Use CX-relative factors:

| Level | Miss multiplier |
| --- | ---: |
| Low | 0.5x |
| Normal | 1.0x |
| High | 1.5x |

`Normal` therefore preserves today's `Miss = -3.0` behavior. `Poor` remains `-1.5` for every level.

### StageFailed

Do **not** add a second `StageFailed` configuration property. CX already exposes the inverse concept as `NoFail`, and adding both creates two sources of truth for the same policy.

`NoFail=false` means stage failure is enabled. `NoFail=true` means no gauge- or Risky-driven failure is allowed.

### AutoAddGage

NX conditionally forwards autoplay judgements to its gauge. CX can implement the same useful behavior without extending `JudgementEvent`: AutoPlay is global in CX, and `PerformanceStage` already freezes the AutoPlay setting for the run and drives auto hits through `ResolveAutoHit`.

Add a CX-named `AutoAddGauge` setting. When AutoPlay is active and `AutoAddGauge=false`, `PerformanceStage` must skip only `_gaugeManager.ProcessJudgement(...)`. Score, combo, skill, judgement feedback, and note resolution remain unchanged.

Default `AutoAddGauge=true` so existing CX AutoPlay behavior is preserved after upgrade.

### StoicMode

Do not implement `StoicMode` in HPA-10.

It is not a failure/gauge rule; it is a visual-feedback policy. The corresponding legacy config item is commented out in the NX config menu, and CX has no reason to add a visual-mode abstraction solely for this stale item. If wanted later, it should be a separate presentation ticket with a concrete UX requirement.

## User-facing settings

Keep the current `System / Drums / Exit` navigation. These settings belong in **Drums**, beside the existing Auto Play / No Fail controls. Do not reopen the config-navigation design just because the old issue says "System menu".

Add:

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| Risky | integer 0–10 | 0 | `0 = Off`; otherwise fail on the Nth `Poor`/`Miss` judgement |
| Damage Level | Low / Normal / High | Normal | scales only `Miss` gauge damage by 0.5x / 1.0x / 1.5x |
| Auto Add Gauge | toggle | On | when Off during Auto Play, auto judgements do not change the gauge |

Retain the existing `No Fail` toggle.

Recommended Drums order around the affected controls:

1. Auto Play
2. Auto Add Gauge
3. No Fail
4. Risky
5. Damage Level
6. Drum Key Mapping

Descriptions should make precedence clear:

- **Auto Add Gauge:** "Allow Auto Play judgements to change the life gauge."
- **No Fail:** "Continue playing without entering a failed gauge state."
- **Risky:** "Fail after this many Poor/Miss judgements. Off uses the life gauge."
- **Damage Level:** "Controls Miss damage to the life gauge."

## Runtime rules

### GaugeManager owns failure policy

Extend `GaugeManager` with the minimum immutable run configuration it needs:

```csharp
public GaugeManager(
    float startingLife = StartingLife,
    GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal,
    int riskyLimit = 0,
    bool failureEnabled = true)
```

Clamp `riskyLimit` to `0..10` in the constructor. Store the initial and remaining Risky counts internally so `Reset()` can restore them.

No `IGaugeRule`, strategy hierarchy, policy service, event bus, or DI registration is needed.

### Life adjustment

Keep existing adjustment constants. Change only `Miss`:

```csharp
JudgementType.Miss => -3.0f * GetMissDamageMultiplier(_damageLevel)
```

where:

```csharp
Low    => 0.5f
Normal => 1.0f
High   => 1.5f
```

### Failure precedence

For each judgement:

1. Update the life gauge using the selected damage level.
2. If `Risky > 0` and judgement is `Poor` or `Miss`, decrement the remaining Risky count once.
3. If `failureEnabled == false`, never set `HasFailed` and never raise `Failed`.
4. Else if `Risky > 0`, fail only when the Risky count reaches zero. Low life does not independently fail the stage in Risky mode.
5. Else use the existing life threshold failure rule.

This keeps `NoFail` authoritative and makes Risky a true alternate failure criterion, matching NX's high-level behavior without porting its special gauge visualization.

Once a real failure occurs, retain today's terminal behavior: `HasFailed=true`, raise `Failed` once, and ignore later judgements.

### AutoPlay gauge forwarding

`PerformanceStage.OnJudgementMade` remains the orchestration point. Keep every existing manager call except conditionally gate the gauge call:

```csharp
var autoAddGauge = _game?.ConfigManager?.Config?.AutoAddGauge ?? true;
if (!_autoPlayEnabled || autoAddGauge)
{
    _gaugeManager?.ProcessJudgement(e);
}
```

Prefer freezing this value together with the other per-performance config during initialization rather than re-reading mutable config on every judgement if the stage already follows that pattern.

Do not modify `JudgementEvent` just to distinguish autoplay from manual input.

## Persistence

Add the following `ConfigData` members:

```csharp
public int Risky { get; set; } = 0;
public GaugeDamageLevel DamageLevel { get; set; } = GaugeDamageLevel.Normal;
public bool AutoAddGauge { get; set; } = true;
```

Persist them through the existing SQLite snapshot with keys:

```text
Risky
DamageLevel
AutoAddGauge
```

Parsing rules:

- `Risky`: integer, clamp to `0..10`.
- `DamageLevel`: case-insensitive enum parse; invalid/missing values leave the default `Normal`.
- `AutoAddGauge`: reuse `TryParseBool`.

No migration alias is required. CX currently has no persisted version of these settings.

`IConfigManager` gains only the setters required by `ConfigStage`:

```csharp
void SetRisky(int value);
void SetDamageLevel(GaugeDamageLevel value);
void SetAutoAddGauge(bool value);
```

All three use the existing deferred-save path.

## PerformanceStage construction

At performance initialization, construct the gauge from the frozen run settings:

```csharp
_gaugeManager = new GaugeManager(
    GaugeManager.StartingLife,
    config.DamageLevel,
    config.Risky,
    failureEnabled: !config.NoFail);
```

Keep `GaugeManager.Failed += OnPlayerFailed` as the only stage-failure event seam. Once `GaugeManager` understands `NoFail`, `OnPlayerFailed` should no longer need to re-check `Config.NoFail`; a raised `Failed` event already means the configured rules permit failure.

This removes the current split ownership where the manager believes the player failed but the stage disagrees.

## Testing strategy

Use existing suites only.

### Configuration

Extend:

- `DTXMania.Test/Config/ConfigDataTests.cs`
- `DTXMania.Test/Config/ConfigManagerTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` for `IConfigManager` stub compilation where needed

Pin defaults, parsing/persistence, setter clamping, and Drums-menu inventory.

### Gauge rules

Extend:

- `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`
- `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs`

Pin:

- Normal damage is unchanged.
- Low/High modify only Miss damage.
- Risky fails on exactly the Nth `Poor`/`Miss`.
- Risky mode ignores life-threshold failure before the counter is exhausted.
- `failureEnabled=false` never enters failed state and continues processing later recovery judgements.
- `Reset()` restores the Risky count.
- `Failed` still fires exactly once for a real failure.

### Stage integration

Use `PerformanceStageDeterministicTests.cs` / existing headless seams to prove:

- run settings are passed into `GaugeManager`;
- `AutoAddGauge=false` skips gauge updates only during AutoPlay;
- manual play still updates gauge regardless of `AutoAddGauge`;
- No Fail no longer leaves a terminal/frozen gauge;
- a genuine configured failure still finalizes with `PlayerFailed`.

Do not add a full game E2E harness for this task.

## Non-goals

- Stoic / hidden judgement UI
- Guitar/bass failure rules
- HAZARD mode
- per-lane AutoPlay
- NX special Risky gauge rendering or danger artwork
- importing the complete NX gauge delta table
- score/skill changes tied to AutoAddGauge
- new config categories or navigation
- a generic gameplay-rules framework
- backward-compatibility aliases for settings that never existed in CX

## Size

Expected implementation size: **2–3 engineer-days**, one implementation PR, using existing config and performance test suites.
