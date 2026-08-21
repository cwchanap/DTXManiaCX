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

After HPA-10, `GaugeManager` is the sole owner of whether the run may enter a failed state. `PerformanceStage` treats `Failed` / `HasFailed` as authoritative and never re-evaluates `NoFail` after the run snapshot is frozen.

## Scope

Add three persisted Drums settings:

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| Risky | integer 0–10 | 0 | `0 = Off`; otherwise fail on the Nth `Poor`/`Miss` |
| Damage Level | Low / Normal / High | Normal | scales only `Miss` gauge damage |
| Auto Add Gauge | toggle | On | when Off during AutoPlay, auto judgements do not change the gauge |

Retain the existing `No Fail` toggle and make it authoritative over gauge failure state.

Explicitly out of scope:

- duplicate `StageFailed` setting;
- StoicMode;
- HAZARD;
- guitar/bass failure rules;
- per-lane AutoPlay;
- NX special Risky gauge drain/rendering;
- a generic rules service, registry, strategy hierarchy, or judgement-origin field.

## Configuration model

### GaugeDamageLevel

Add one closed enum in its own sibling config file:

`DTXMania.Game/Lib/Config/GaugeDamageLevel.cs`

```csharp
public enum GaugeDamageLevel
{
    Low,
    Normal,
    High
}
```

Keeping the enum outside `ConfigData.cs` follows the existing config-value/range file convention and avoids making the data bag own unrelated type declarations.

### RiskyRange

Add `DTXMania.Game/Lib/Config/RiskyRange.cs` as the single source of truth for the numeric contract:

```csharp
public static class RiskyRange
{
    public const int Min = 0;
    public const int Max = 10;
    public const int Step = 1;
    public const int Default = 0;

    public static int Clamp(int value) => Math.Clamp(value, Min, Max);

    public static string Format(int value) =>
        value == 0 ? "Off" : value.ToString(CultureInfo.InvariantCulture);
}
```

Use this in config parsing, persistence normalization, setters, ConfigStage bounds/formatting, and the `GaugeManager` constructor. Do not repeat `0`, `10`, or the Off formatter at those call sites.

### ConfigData

Add:

```csharp
public int Risky { get; set; } = RiskyRange.Default;
public GaugeDamageLevel DamageLevel { get; set; } = GaugeDamageLevel.Normal;
public bool AutoAddGauge { get; set; } = true;
```

### Persistence

Persist exactly these SQLite keys:

```text
Risky
DamageLevel
AutoAddGauge
```

Parsing rules:

- `Risky`: invariant integer parse, then `RiskyRange.Clamp`.
- `AutoAddGauge`: existing `TryParseBool`.
- `DamageLevel`: accept enum **names only**, case-insensitively. Numeric enum strings are invalid even when their underlying value is defined.

`Enum.TryParse + Enum.IsDefined` is insufficient because values such as `"0"`, `"1"`, and `"2"` parse to defined enum members. Match against `Enum.GetNames<GaugeDamageLevel>()` first, then parse the matched name. Invalid/missing values leave the default `Normal`.

Persist `DamageLevel` using `ToString()` so the store contains `Low`, `Normal`, or `High`, never numeric values.

### Setters

`IConfigManager` gains only:

```csharp
void SetRisky(int value);
void SetDamageLevel(GaugeDamageLevel value);
void SetAutoAddGauge(bool value);
```

All three follow existing no-op-on-equal + deferred-save behavior. `SetRisky` uses `RiskyRange.Clamp`.

## Config UI

Keep the current `System / Drums / Exit` navigation. These settings belong under **Drums**.

The complete Drums list after HPA-10 is:

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

Use the existing `IntegerConfigItem`, `DropdownConfigItem`, and `ToggleConfigItem`.

- `Risky` uses `RiskyRange.Min`, `Max`, `Step`, and `Format`.
- `Damage Level` options come from `Enum.GetNames<GaugeDamageLevel>()`, not duplicated string literals.

Exact descriptions:

- **Auto Add Gauge:** `Allow Auto Play judgements to change the life gauge.`
- **No Fail:** `Continue playing without entering a failed gauge state.`
- **Risky:** `Fail after this many Poor/Miss judgements. Off uses the life gauge.`
- **Damage Level:** `Controls Miss damage to the life gauge.`

## GaugeManager runtime policy

Extend the existing constructor only:

```csharp
public GaugeManager(
    float startingLife = StartingLife,
    GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal,
    int riskyLimit = RiskyRange.Default,
    bool failureEnabled = true)
```

Store immutable run policy plus mutable remaining Risky count. Clamp `riskyLimit` through `RiskyRange.Clamp`. Existing parameterless callers retain current behavior.

No new interface or policy object is needed.

### Damage Level

Keep all existing adjustments except `Miss`:

| Level | Miss multiplier | Miss delta |
| --- | ---: | ---: |
| Low | 0.5x | -1.5 |
| Normal | 1.0x | -3.0 |
| High | 1.5x | -4.5 |

`Poor` remains `-1.5` at every level. `Normal` is byte-for-behavior compatible with today's CX tuning.

### Failure order

For each judgement:

1. Apply the life adjustment.
2. If `Risky > 0` and the judgement is `Poor` or `Miss`, decrement remaining Risky once.
3. If `failureEnabled == false`, never set `HasFailed` and never raise `Failed`.
4. Else if `Risky > 0`, fail only when remaining Risky reaches zero.
5. Else fail using the existing life threshold.

A real failure remains terminal: set `HasFailed`, raise `Failed` once, and ignore later judgements.

`failureEnabled=false` is different: the gauge never enters the terminal failed state, so later judgements continue to modify life. This is the No Fail defect fix.

`Reset()` restores life, clears `HasFailed`, and restores the initial Risky count.

## Frozen performance-run snapshot

The run policy must be captured at one instant.

`InitializeAutoPlay()` is already the synchronous per-activation point that freezes `_autoPlayEnabled`. Extend that same method to capture:

```text
_autoPlayEnabled
_autoAddGaugeEnabled
_gaugeDamageLevel
_riskyLimit
_gaugeFailureEnabled = !NoFail
```

Use the existing null-safe `?.` / `??` config access pattern. Give the four new private fields safe initial values matching current defaults so reflection tests that invoke `InitializeGameplayManagers()` directly remain valid even without a configured `ConfigManager`.

`InitializeGameplayManagers()` must read only these frozen fields when constructing `GaugeManager`; it must not dereference `_game.ConfigManager.Config`.

This prevents two different config snapshots between synchronous activation and later async manager construction, and avoids NREs in existing deterministic tests that create an uninitialized `BaseGame` without `ConfigManager`.

## PerformanceStage orchestration

### Gauge forwarding

`OnJudgementMade` continues to forward to score, combo, skill, UI feedback, and note state exactly as today.

Only the gauge call is conditional:

```csharp
if (!_autoPlayEnabled || _autoAddGaugeEnabled)
{
    _gaugeManager?.ProcessJudgement(e);
}
```

No autoplay-origin field is added to `JudgementEvent`.

### Failure authority

Once `GaugeManager` owns `failureEnabled`, a raised `Failed` event is authoritative.

`OnPlayerFailed` becomes:

```csharp
private void OnPlayerFailed(object? sender, FailureEventArgs e)
{
    if (!_stageCompleted)
    {
        FinalizePerformance(CompletionReason.PlayerFailed);
    }
}
```

`CheckStageCompletion` likewise stops reading `Config.NoFail`:

```csharp
if (_gaugeManager?.HasFailed == true)
{
    FinalizePerformance(CompletionReason.PlayerFailed);
}
```

The old test `OnPlayerFailed_WhenNoFailEnabled_ShouldNotFinalizePerformance` must be replaced because it encodes the split ownership being removed. No Fail is instead proven at `GaugeManager`: `Failed` never fires, `HasFailed` remains false, and a later Perfect still changes life after the gauge reaches zero.

## Known Risky UI limitation

HPA-10 deliberately does not port NX Risky gauge rendering. This creates a temporary presentation limitation: the normal life gauge can reach zero / danger while a Risky run is still valid because remaining Poor/Miss allowance has not been exhausted.

Do not silently interpret the life gauge as Risky state. HPA-10 documentation and PR notes must call this out explicitly.

The intended follow-up is a small HUD change that shows the remaining Risky allowance using `GaugeManager` as the source of truth, without changing failure semantics or porting NX drain math. A Linear follow-up was attempted during planning but could not be created because the workspace has reached its free issue limit; keep this work out of HPA-10 until tracking capacity is available.

## Testing strategy

Use existing suites only.

### Configuration

Extend:

- `ConfigDataTests.cs`
- `ConfigManagerTests.cs`
- `ConfigStageLogicTests.cs`
- existing `IConfigManager` test doubles as required

Pin:

- defaults;
- Risky clamping through `RiskyRange`;
- DamageLevel accepts names case-insensitively;
- numeric strings (`0`, `1`, `2`) are rejected as persisted enum values;
- AutoAddGauge boolean parsing;
- round-trip persistence;
- complete ten-item Drums inventory and descriptions.

### Gauge rules

Extend:

- `GaugeManagerTests.cs`
- `GaugeManagerFailThresholdTests.cs`

Pin:

- Normal damage unchanged;
- Low/High scale only Miss;
- Risky fails on exactly the Nth `Poor`/`Miss`;
- Risky ignores life-threshold failure before the counter is exhausted;
- No Fail never enters terminal state and continues processing later recovery judgements;
- Reset restores Risky count;
- Failed fires once for real failure.

### Stage integration

Extend `PerformanceStageDeterministicTests.cs` to prove:

- all run policy fields freeze together in `InitializeAutoPlay()`;
- `InitializeGameplayManagers()` works with safe defaults when `ConfigManager` is absent in existing reflection tests;
- AutoAddGauge Off suppresses only autoplay-to-gauge forwarding;
- manual play still updates gauge;
- a raised `Failed` event always finalizes with `CompletionReason.PlayerFailed`;
- `CheckStageCompletion` trusts `HasFailed` without a live NoFail read.

### Existing E2E contract

The current E2E fixture uses `AutoPlay=True` and `NoFail=True`. Because `AutoAddGauge` is absent, it must default to true; the frozen run must therefore be AutoPlay + gauge updates + `failureEnabled=false` and still reach a cleared Result.

Do not add a new E2E harness. Require the existing Windows gameplay E2E smoke to remain green in CI, and add only small fixture/default assertions if useful while implementing.

## Risks

- **Direct manager-test seam:** several deterministic tests invoke `InitializeGameplayManagers()` on a `BaseGame` with no `ConfigManager`. Safe field defaults plus freezing in `InitializeAutoPlay()` prevent HPA-10 from introducing NREs there.
- **E2E NoFail + AutoPlay path:** the existing smoke run depends on both flags. The change must preserve its successful clear while moving NoFail ownership into `GaugeManager`.

## Legacy reference boundary

Behavioral references to `CActPerfCommonGauge` and the commented StoicMode item were verified against the separate `cwchanap/DTXmaniaNX` legacy repository during planning. That repository is not guaranteed to exist inside a CX implementation checkout despite older `CLAUDE.md` wording. Treat these references as reviewed design intent; implementation does not need to modify or build the legacy repository.

## Verification environment

On macOS, use `DTXMania.Test/DTXMania.Test.Mac.csproj`; it includes all Config, GaugeManager, and PerformanceStage deterministic files used by HPA-10 while excluding graphics-dependent tests that are unsafe under DesktopGL/SDL.

Hosted Windows CI remains authoritative for the full `DTXMania.Test.csproj` suite and the existing gameplay E2E smoke.

## Size

Expected implementation size remains **2–3 engineer-days in one PR**.
