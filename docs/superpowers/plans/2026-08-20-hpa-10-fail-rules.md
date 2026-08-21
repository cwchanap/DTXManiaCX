# HPA-10 Gameplay Fail Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persisted Risky, Damage Level, and Auto Add Gauge controls while making the existing No Fail option authoritative over gauge failure state.

**Architecture:** Keep the existing config and gameplay ownership boundaries. `ConfigData`/`ConfigManager` own persisted settings, `ConfigStage` exposes them, `GaugeManager` owns immutable per-run damage/failure policy, and `PerformanceStage` only decides whether AutoPlay judgements are forwarded to the gauge. Do not add a rules service, strategy hierarchy, or new judgement-origin model.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, existing SQLite-backed `ConfigManager` persistence.

**Spec:** `docs/superpowers/specs/2026-08-20-hpa-10-fail-rules-design.md`

## Global Constraints

- Keep HPA-10 to one implementation PR and at most 3 engineer-days.
- Keep the existing `System / Drums / Exit` config navigation; new gameplay controls belong under `Drums`.
- Reuse existing `NoFail`; do not add a second `StageFailed` setting.
- Do not implement `StoicMode`, HAZARD, guitar/bass rules, per-lane autoplay, or NX special Risky gauge rendering.
- Preserve current CX defaults: Normal damage keeps Miss at `-3.0f`; AutoPlay continues to affect gauge unless the new toggle is disabled.
- `Risky > 0` fails on the Nth `Poor`/`Miss`; it replaces life-threshold failure for that run.
- `NoFail=true` prevents both life-threshold and Risky failure and must keep the gauge processing later judgements.
- `AutoAddGauge=false` affects only gauge forwarding during AutoPlay; score, combo, skill, note resolution, and judgement feedback are unchanged.
- Do not add backward-compatibility aliases for settings that never existed in CX.

---

## File map

### Configuration

- Modify `DTXMania.Game/Lib/Config/ConfigData.cs` — define `GaugeDamageLevel` and the three new persisted values.
- Modify `DTXMania.Game/Lib/Config/IConfigManager.cs` — expose the three narrow setters used by the config screen.
- Modify `DTXMania.Game/Lib/Config/ConfigManager.cs` — parse, persist, and mutate the settings through the existing deferred-save path.
- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs` — add Drums items using existing `IntegerConfigItem`, `DropdownConfigItem`, and `ToggleConfigItem`.

### Gameplay

- Modify `DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs` — own damage level, Risky counter, and whether failure is enabled for the run.
- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — build `GaugeManager` from frozen config and gate AutoPlay-to-gauge forwarding.

### Tests

- Modify `DTXMania.Test/Config/ConfigDataTests.cs`.
- Modify `DTXMania.Test/Config/ConfigManagerTests.cs`.
- Modify `DTXMania.Test/Config/ConfigStageLogicTests.cs`.
- Modify `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` only as required to keep its `IConfigManager` test double complete.
- Modify `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`.
- Modify `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs` where threshold-focused assertions fit better than the general gauge suite.
- Modify `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs` for stage wiring and AutoPlay/NoFail integration.

---

### Task 1: Persist and expose the three HPA-10 settings

**Estimate:** 0.75 engineer-day

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Test: `DTXMania.Test/Config/ConfigDataTests.cs`
- Test: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Test: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

**Interfaces:**
- Consumes: existing `ConfigData`, `IConfigManager`, `ConfigManager.MarkDirty()`, `TryParseBool`, `IntegerConfigItem`, `DropdownConfigItem`, `ToggleConfigItem`.
- Produces: `GaugeDamageLevel`, `ConfigData.Risky`, `ConfigData.DamageLevel`, `ConfigData.AutoAddGauge`, and setters `SetRisky`, `SetDamageLevel`, `SetAutoAddGauge`.

- [ ] **Step 1: Add failing default/config-surface tests**

In `ConfigDataTests.cs`, pin the defaults:

```csharp
[Fact]
public void Constructor_FailRuleDefaults_PreserveCurrentGameplay()
{
    var config = new ConfigData();

    Assert.Equal(0, config.Risky);
    Assert.Equal(GaugeDamageLevel.Normal, config.DamageLevel);
    Assert.True(config.AutoAddGauge);
}
```

In `ConfigStageLogicTests.cs`, extend the existing Drums inventory expectation so the affected tail is exactly:

```text
Auto Play
Auto Add Gauge
No Fail
Risky
Damage Level
Drum Key Mapping
```

Extend the existing toggle/integer/dropdown activation coverage to prove selecting each item mutates the corresponding `ConfigData` value through `IConfigManager`; do not create a second config-screen harness.

- [ ] **Step 2: Run the focused configuration tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: compile/test failure because `GaugeDamageLevel` and the new config members/setters do not exist.

- [ ] **Step 3: Add the config model and manager interface**

In `ConfigData.cs`, add the enum beside the other configuration value types and add the properties with these exact defaults:

```csharp
public enum GaugeDamageLevel
{
    Low,
    Normal,
    High
}

public int Risky { get; set; } = 0;
public GaugeDamageLevel DamageLevel { get; set; } = GaugeDamageLevel.Normal;
public bool AutoAddGauge { get; set; } = true;
```

In `IConfigManager.cs`, add:

```csharp
void SetRisky(int value);
void SetDamageLevel(GaugeDamageLevel value);
void SetAutoAddGauge(bool value);
```

Update `DrumConfigStageTests`' existing config-manager stub in the same edit so interface compilation remains green once production code is implemented.

- [ ] **Step 4: Add failing parse/save/setter tests**

Extend `ConfigManagerTests.cs` using the file's existing temporary-store helpers. Pin these concrete cases:

```text
Risky=-4      -> 0
Risky=0       -> 0
Risky=7       -> 7
Risky=99      -> 10
DamageLevel=low    -> Low
DamageLevel=NORMAL -> Normal
DamageLevel=High   -> High
DamageLevel=garbage -> default Normal
AutoAddGauge=false -> false
AutoAddGauge=0     -> false
AutoAddGauge=on    -> true
```

Also round-trip one non-default snapshot:

```csharp
configManager.SetRisky(4);
configManager.SetDamageLevel(GaugeDamageLevel.High);
configManager.SetAutoAddGauge(false);
configManager.FlushPendingSave();
```

Reload and assert `4`, `High`, and `false`. Add a setter-clamping assertion that `SetRisky(42)` stores `10` and `SetRisky(-1)` stores `0`.

- [ ] **Step 5: Implement parse, persistence, and setters**

Add these parse branches to `ConfigManager.ParseConfigLine`:

```csharp
case "Risky":
    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var risky))
        Config.Risky = Math.Clamp(risky, 0, 10);
    break;
case "DamageLevel":
    if (Enum.TryParse<GaugeDamageLevel>(value, true, out var damageLevel))
        Config.DamageLevel = damageLevel;
    break;
case "AutoAddGauge":
    if (TryParseBool(value, out var autoAddGauge))
        Config.AutoAddGauge = autoAddGauge;
    break;
```

Add exactly these entries to `BuildPersistedEntries()`:

```csharp
entries["Risky"] = Math.Clamp(Config.Risky, 0, 10)
    .ToString(CultureInfo.InvariantCulture);
entries["DamageLevel"] = Config.DamageLevel.ToString();
entries["AutoAddGauge"] = Config.AutoAddGauge.ToString();
```

Implement setters with no-op-on-equal behavior and the existing deferred dirty flag:

```csharp
public void SetRisky(int value)
{
    var clamped = Math.Clamp(value, 0, 10);
    if (clamped == Config.Risky)
        return;

    Config.Risky = clamped;
    MarkDirty();
}

public void SetDamageLevel(GaugeDamageLevel value)
{
    if (value == Config.DamageLevel)
        return;

    Config.DamageLevel = value;
    MarkDirty();
}

public void SetAutoAddGauge(bool value)
{
    if (value == Config.AutoAddGauge)
        return;

    Config.AutoAddGauge = value;
    MarkDirty();
}
```

- [ ] **Step 6: Add the Drums menu items**

In `ConfigStage.SetupConfigItems()`, keep all three items in the `Drums` category and add:

```csharp
var autoAddGaugeItem = new ToggleConfigItem(
    "Auto Add Gauge",
    () => _configManager.Config.AutoAddGauge,
    value => _configManager.SetAutoAddGauge(value))
{ Description = "Allow Auto Play judgements to change the life gauge." };

var riskyItem = new IntegerConfigItem(
    "Risky",
    () => _configManager.Config.Risky,
    value => _configManager.SetRisky(value),
    minValue: 0,
    maxValue: 10,
    step: 1,
    valueFormatter: value => value == 0 ? "Off" : value.ToString(CultureInfo.InvariantCulture))
{ Description = "Fail after this many Poor/Miss judgements. Off uses the life gauge." };

var damageLevelItem = new DropdownConfigItem(
    "Damage Level",
    () => _configManager.Config.DamageLevel.ToString(),
    new[] { "Low", "Normal", "High" },
    value =>
    {
        if (Enum.TryParse<GaugeDamageLevel>(value, true, out var level))
            _configManager.SetDamageLevel(level);
    })
{ Description = "Controls Miss damage to the life gauge." };
```

If `ConfigStage.cs` does not currently import `System.Globalization`, either add that using or format Risky with the existing local numeric convention; do not create a formatter helper solely for this item.

Build the tail of `drumItems` as:

```csharp
autoPlayItem,
autoAddGaugeItem,
noFailItem,
riskyItem,
damageLevelItem,
drumKeyItem
```

- [ ] **Step 7: Verify Task 1 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 8: Commit Task 1**

```bash
git add DTXMania.Game/Lib/Config/ConfigData.cs \
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

### Task 2: Make GaugeManager own damage and failure policy

**Estimate:** 0.75 engineer-day

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs`
- Test: `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`
- Test: `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs`

**Interfaces:**
- Consumes: `GaugeDamageLevel` from Task 1 and existing `JudgementEvent` / `JudgementType`.
- Produces: constructor `GaugeManager(float startingLife = StartingLife, GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal, int riskyLimit = 0, bool failureEnabled = true)` with existing `Failed`/`GaugeChanged` events unchanged.

- [ ] **Step 1: Add failing damage-level tests**

Extend `GaugeManagerTests.cs` with these exact expected adjustments:

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

[Theory]
[InlineData(GaugeDamageLevel.Low)]
[InlineData(GaugeDamageLevel.Normal)]
[InlineData(GaugeDamageLevel.High)]
public void GetLifeAdjustment_Poor_IsNotScaled(GaugeDamageLevel level)
{
    var manager = new GaugeManager(damageLevel: level);
    Assert.Equal(-1.5f, manager.GetLifeAdjustment(JudgementType.Poor));
}
```

Keep the existing default adjustment theory; it must remain green for a parameterless manager.

- [ ] **Step 2: Add failing Risky and NoFail-policy tests**

Add tests that drive public behavior only:

```csharp
[Fact]
public void Risky_FailsOnNthPoorOrMiss()
{
    var manager = new GaugeManager(startingLife: 100.0f, riskyLimit: 3);
    var failures = 0;
    manager.Failed += (_, _) => failures++;

    manager.ProcessJudgement(new JudgementEvent(1, 0, 0, JudgementType.Poor));
    manager.ProcessJudgement(new JudgementEvent(2, 0, 0, JudgementType.Perfect));
    manager.ProcessJudgement(new JudgementEvent(3, 0, 0, JudgementType.Miss));
    Assert.False(manager.HasFailed);

    manager.ProcessJudgement(new JudgementEvent(4, 0, 0, JudgementType.Poor));
    Assert.True(manager.HasFailed);
    Assert.Equal(1, failures);
}

[Fact]
public void Risky_IgnoresLifeThresholdUntilCounterIsExhausted()
{
    var manager = new GaugeManager(startingLife: 1.0f, riskyLimit: 2);

    manager.ProcessJudgement(new JudgementEvent(1, 0, 0, JudgementType.Miss));

    Assert.False(manager.HasFailed);
}

[Fact]
public void FailureDisabled_DoesNotFreezeGaugeAfterCrossingThreshold()
{
    var manager = new GaugeManager(startingLife: 1.0f, failureEnabled: false);

    manager.ProcessJudgement(new JudgementEvent(1, 0, 0, JudgementType.Miss));
    Assert.False(manager.HasFailed);
    Assert.Equal(0.0f, manager.CurrentLife);

    manager.ProcessJudgement(new JudgementEvent(2, 0, 0, JudgementType.Perfect));
    Assert.Equal(2.0f, manager.CurrentLife);
    Assert.False(manager.HasFailed);
}
```

Add one reset test: with `riskyLimit: 2`, consume one mistake, call `Reset()`, then require two new `Poor`/`Miss` events before failure.

- [ ] **Step 3: Run gauge tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: compile/test failure because the constructor does not accept rule settings and default Miss damage is not configurable.

- [ ] **Step 4: Add immutable run policy to GaugeManager**

Add fields:

```csharp
private readonly GaugeDamageLevel _damageLevel;
private readonly int _riskyLimit;
private readonly bool _failureEnabled;
private int _riskyRemaining;
```

Replace the constructor with:

```csharp
public GaugeManager(
    float startingLife = StartingLife,
    GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal,
    int riskyLimit = 0,
    bool failureEnabled = true)
{
    _currentLife = Math.Clamp(startingLife, MinLife, MaxLife);
    _damageLevel = damageLevel;
    _riskyLimit = Math.Clamp(riskyLimit, 0, 10);
    _riskyRemaining = _riskyLimit;
    _failureEnabled = failureEnabled;
    _hasFailed = false;
}
```

Do not expose mutable setters for these run settings.

- [ ] **Step 5: Scale only Miss damage**

Add a private helper:

```csharp
private float GetMissDamageMultiplier()
{
    return _damageLevel switch
    {
        GaugeDamageLevel.Low => 0.5f,
        GaugeDamageLevel.Normal => 1.0f,
        GaugeDamageLevel.High => 1.5f,
        _ => 1.0f
    };
}
```

Change only the Miss arm:

```csharp
JudgementType.Miss => -3.0f * GetMissDamageMultiplier(),
```

Leave Perfect/Great/Good/Poor constants unchanged.

- [ ] **Step 6: Replace the failure check with the ordered policy**

After clamping the new life value, compute Risky state and failure once:

```csharp
if (_riskyLimit > 0 &&
    (judgementEvent.Type == JudgementType.Poor ||
     judgementEvent.Type == JudgementType.Miss) &&
    _riskyRemaining > 0)
{
    _riskyRemaining--;
}

var shouldFail = false;
if (_failureEnabled)
{
    shouldFail = _riskyLimit > 0
        ? _riskyRemaining <= 0
        : _currentLife < FailureThreshold;
}

var justFailed = false;
if (shouldFail && !_hasFailed)
{
    _hasFailed = true;
    justFailed = true;
    Failed?.Invoke(this, new FailureEventArgs
    {
        FinalLife = _currentLife,
        JudgementType = judgementEvent.Type
    });
}
```

Keep the existing `GaugeChanged` payload and terminal `_hasFailed` early return. Remove only duplicate failure code made obsolete by this block.

In `Reset()` restore:

```csharp
_riskyRemaining = _riskyLimit;
```

- [ ] **Step 7: Verify Task 2 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: PASS, including all existing default gauge tests.

- [ ] **Step 8: Commit Task 2**

```bash
git add DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs \
        DTXMania.Test/Stage/Performance/GaugeManagerTests.cs \
        DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs
git commit -m "feat: apply gameplay failure policy in gauge manager"
```

---

### Task 3: Wire per-run rules through PerformanceStage

**Estimate:** 0.75 engineer-day

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Test: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

**Interfaces:**
- Consumes: Task 1 config values and Task 2 `GaugeManager` constructor.
- Produces: one coherent runtime path where `GaugeManager.Failed` means the configured rules really permit stage failure, and AutoPlay gauge forwarding respects `AutoAddGauge`.

- [ ] **Step 1: Add failing stage-integration tests**

Use the existing inspectable/headless `PerformanceStageDeterministicTests` seams rather than creating a new host.

Pin these cases:

1. Config `{ NoFail=false, Risky=0, DamageLevel=High }` constructs the gauge so a Miss uses `-4.5f`.
2. Config `{ NoFail=true }` lets a gauge reach zero and later recover from a Perfect without finalizing `PlayerFailed`.
3. Config `{ Risky=2, NoFail=false }` finalizes `PlayerFailed` on exactly the second `Poor`/`Miss` even if life is above the normal threshold.
4. AutoPlay + `AutoAddGauge=false`: resolved auto judgements still reach score/combo/skill but leave gauge life unchanged.
5. Manual play + `AutoAddGauge=false`: manual judgements still change gauge.
6. AutoPlay + `AutoAddGauge=true`: existing gauge behavior remains unchanged.

Where the existing deterministic stage exposes manager state through reflection/test subclasses, extend that same seam. Do not add a production testing interface.

- [ ] **Step 2: Run the deterministic stage tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests"
```

Expected: failures because `PerformanceStage` still creates a default `GaugeManager`, forwards every AutoPlay judgement to it, and suppresses NoFail only after the manager has already failed.

- [ ] **Step 3: Freeze the Auto Add Gauge setting for the run**

Near the existing frozen AutoPlay state, add:

```csharp
private bool _autoAddGauge = true;
```

During performance initialization, capture:

```csharp
var config = _game?.ConfigManager?.Config ?? new ConfigData();
_autoPlayEnabled = config.AutoPlay;
_autoAddGauge = config.AutoAddGauge;
```

If the method already has a non-null `config` local, reuse it rather than introducing a second snapshot expression.

- [ ] **Step 4: Construct GaugeManager from the same run config**

Replace the default construction with:

```csharp
_gaugeManager = new GaugeManager(
    GaugeManager.StartingLife,
    config.DamageLevel,
    config.Risky,
    failureEnabled: !config.NoFail);
```

Keep the existing subscriptions:

```csharp
_gaugeManager.GaugeChanged += OnGaugeChanged;
_gaugeManager.Failed += OnPlayerFailed;
```

Do not move score/combo/skill responsibilities into `GaugeManager`.

- [ ] **Step 5: Gate only AutoPlay-to-gauge forwarding**

In `OnJudgementMade`, keep the current manager call order and replace the unconditional gauge call with:

```csharp
if (!_autoPlayEnabled || _autoAddGauge)
{
    _gaugeManager?.ProcessJudgement(e);
}
```

Do not skip `ScoreManager`, `ComboManager`, `SkillManager`, judgement text, or note state when Auto Add Gauge is Off.

- [ ] **Step 6: Remove the redundant NoFail check from OnPlayerFailed**

Once `GaugeManager` is constructed with `failureEnabled: !config.NoFail`, a `Failed` event is authoritative. Keep `OnPlayerFailed` focused on stage finalization:

```csharp
private void OnPlayerFailed(object? sender, FailureEventArgs e)
{
    if (!_stageCompleted)
    {
        FinalizePerformance(PerformanceEndReason.PlayerFailed);
    }
}
```

Preserve the repository's exact existing enum/type names around `FinalizePerformance`; the important change is removal of the second `Config.NoFail` decision, not unrelated refactoring.

- [ ] **Step 7: Verify Task 3 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: PASS.

- [ ] **Step 8: Commit Task 3**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs \
        DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
git commit -m "feat: wire configured fail rules into gameplay"
```

---

### Task 4: Full regression verification and PR closeout

**Estimate:** 0.25 engineer-day

**Files:**
- No production files expected beyond Tasks 1–3.
- Update plan/spec only if implementation uncovers a factual mismatch that changes the agreed behavior.

**Interfaces:**
- Consumes: completed HPA-10 implementation.
- Produces: one reviewable PR with config, gauge, and stage behavior covered by existing suites.

- [ ] **Step 1: Run the full game test project**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 2: Run a build to catch interface consumers outside the focused tests**

```bash
dotnet build DTXMania.sln --no-restore
```

Expected: PASS with no new compiler errors.

- [ ] **Step 3: Review the diff against the scope guard**

The final diff must contain no new:

```text
StoicMode
StageFailed config property
IGaugeRule / gauge strategy hierarchy
JudgementEvent autoplay-origin property
new config category
score/skill behavior tied to AutoAddGauge
```

It must contain the persisted keys exactly:

```text
Risky
DamageLevel
AutoAddGauge
```

- [ ] **Step 4: Commit any test-only closeout adjustment if required**

Only if Step 1 or Step 2 required a legitimate test/stub update that belongs to HPA-10:

```bash
git add <the HPA-10 test or stub file that was corrected>
git commit -m "test: complete HPA-10 regression coverage"
```

If no adjustment was required, make no empty commit.

---

## Acceptance checklist

- [ ] Risky is persisted, shown under Drums, clamps to `0..10`, and `0` means Off.
- [ ] Risky `N` fails on exactly the Nth `Poor`/`Miss` when No Fail is Off.
- [ ] Risky replaces life-threshold failure while active.
- [ ] Damage Level defaults to Normal and changes only Miss damage to `-1.5 / -3.0 / -4.5` for Low/Normal/High.
- [ ] Auto Add Gauge defaults On; Off prevents AutoPlay judgements from changing only the gauge.
- [ ] Existing No Fail prevents both normal and Risky failure and does not freeze gauge processing.
- [ ] `GaugeManager.Failed` is the single authoritative stage-failure event after initialization.
- [ ] No StoicMode, duplicate StageFailed, generic rules framework, or new E2E harness is added.
- [ ] Focused tests, full `DTXMania.Test`, and solution build pass.
