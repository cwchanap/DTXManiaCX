# HPA-10 Gameplay Fail Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persisted Risky, Damage Level, and Auto Add Gauge controls while making the existing No Fail option authoritative over gauge failure state.

**Architecture:** Keep the existing config and gameplay ownership boundaries. `ConfigData` / `ConfigManager` own persisted settings, `ConfigStage` exposes them, `GaugeManager` owns immutable per-run damage/failure policy, and `PerformanceStage` freezes AutoPlay-related forwarding and treats `GaugeManager.Failed` / `HasFailed` as authoritative. Do not add a rules service, strategy hierarchy, or judgement-origin model.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, existing SQLite-backed `ConfigManager` persistence.

**Spec:** `docs/superpowers/specs/2026-08-20-hpa-10-fail-rules-design.md`

## Global Constraints

- Keep HPA-10 to one implementation PR and at most 3 engineer-days.
- Keep the existing `System / Drums / Exit` navigation; new gameplay controls belong under `Drums`.
- Reuse existing `NoFail`; do not add `StageFailed`.
- Do not implement `StoicMode`, HAZARD, guitar/bass rules, per-lane AutoPlay, or NX special Risky rendering.
- Preserve current defaults: `Risky=0`, `DamageLevel=Normal`, `AutoAddGauge=true`.
- `Risky > 0` fails on the Nth `Poor` / `Miss` and replaces life-threshold failure for that run.
- `NoFail=true` prevents both Risky and life-threshold failure and must keep the gauge processing later judgements.
- `AutoAddGauge=false` affects only gauge forwarding during AutoPlay.
- `GaugeManager` is the only No Fail/failure-policy authority once a run starts; `PerformanceStage` must not live-read `Config.NoFail` after construction.
- Do not add backward-compatibility aliases for settings that never existed in CX.

---

## File map

### Configuration

- Create: `DTXMania.Game/Lib/Config/GaugeDamageLevel.cs` — closed Low/Normal/High enum.
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs` — add three persisted values.
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs` — expose three narrow setters.
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs` — strict parse, snapshot persistence, and setters.
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs` — add Drums controls and update No Fail copy.

### Gameplay

- Modify: `DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs` — own damage level, Risky state, and failure-enabled policy.
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — freeze AutoAddGauge, construct GaugeManager from the run snapshot, gate gauge forwarding, and remove duplicate No Fail decisions.

### Tests

- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`.
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`.
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`.
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` only to keep its existing `IConfigManager` double complete.
- Modify: `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`.
- Modify: `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs` where threshold-specific assertions already live.
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs` for stage ownership/wiring.

---

### Task 1: Persist and expose the three settings

**Estimate:** 0.75 engineer-day

**Files:**
- Create: `DTXMania.Game/Lib/Config/GaugeDamageLevel.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Test: `DTXMania.Test/Config/ConfigDataTests.cs`
- Test: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Test: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

**Interfaces:**
- Produces: `GaugeDamageLevel`, `ConfigData.Risky`, `ConfigData.DamageLevel`, `ConfigData.AutoAddGauge`, `SetRisky(int)`, `SetDamageLevel(GaugeDamageLevel)`, `SetAutoAddGauge(bool)`.
- Reuses: `TryParseBool`, invariant integer parsing, `MarkDirty()`, and existing `IntegerConfigItem` / `DropdownConfigItem` / `ToggleConfigItem`.

- [ ] **Step 1: Add RED defaults and full Drums-inventory coverage**

Add to `ConfigDataTests.cs`:

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

Update the existing `SetupConfigItems_ShouldBuildSystemDrumsExitCategories` assertion in `ConfigStageLogicTests.cs`. Keep its existing single full `Assert.Collection`; the Drums list must become exactly:

```csharp
Assert.Collection(categories[1].Items,
    i => Assert.Equal("Scroll Speed", i.Name),
    i => Assert.Equal("Play Speed", i.Name),
    i => Assert.Equal("Pitch", i.Name),
    i => Assert.Equal("Metronome", i.Name),
    i => Assert.Equal("Auto Play", i.Name),
    i => Assert.Equal("Auto Add Gauge", i.Name),
    i => Assert.Equal("No Fail", i.Name),
    i => Assert.Equal("Risky", i.Name),
    i => Assert.Equal("Damage Level", i.Name),
    i => Assert.Equal("Drum Key Mapping", i.Name));
```

Also extend the existing item-mutation coverage rather than creating a second ConfigStage harness.

- [ ] **Step 2: Run focused config tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: compile/test failure because the new enum, config values, and setters do not exist.

- [ ] **Step 3: Create the enum and config surface**

Create `GaugeDamageLevel.cs`:

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

Add to `ConfigData.cs` beside the existing gameplay settings:

```csharp
public int Risky { get; set; } = 0;
public GaugeDamageLevel DamageLevel { get; set; } = GaugeDamageLevel.Normal;
public bool AutoAddGauge { get; set; } = true;
```

Add to `IConfigManager.cs`:

```csharp
void SetRisky(int value);
void SetDamageLevel(GaugeDamageLevel value);
void SetAutoAddGauge(bool value);
```

Update the existing `DrumConfigStageTests` config-manager stub in the same edit.

- [ ] **Step 4: Add RED parse/persist/setter tests**

Extend `ConfigManagerTests.cs` with the existing temporary SQLite helpers. Pin:

```text
Risky=-4       -> 0
Risky=7        -> 7
Risky=99       -> 10
DamageLevel=low     -> Low
DamageLevel=NORMAL  -> Normal
DamageLevel=High    -> High
DamageLevel=garbage -> Normal
DamageLevel=0       -> Normal
DamageLevel=1       -> Normal
DamageLevel=2       -> Normal
AutoAddGauge=false  -> false
AutoAddGauge=0      -> false
AutoAddGauge=on     -> true
```

The numeric Damage Level cases deliberately remain the default `Normal`; `Enum.TryParse` alone accepts numeric strings and is not sufficient.

Round-trip a non-default snapshot:

```csharp
configManager.SetRisky(4);
configManager.SetDamageLevel(GaugeDamageLevel.High);
configManager.SetAutoAddGauge(false);
configManager.FlushPendingSave();
```

Reload and assert `4`, `High`, `false`. Also pin `SetRisky(42) -> 10`, `SetRisky(-1) -> 0`, and no-op-on-equal behavior for all three setters.

- [ ] **Step 5: Implement strict parse, snapshot persistence, and setters**

Add parse branches to `ConfigManager.ParseConfigLine`:

```csharp
case "Risky":
    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var risky))
        Config.Risky = Math.Clamp(risky, 0, 10);
    break;

case "DamageLevel":
    if (Enum.TryParse<GaugeDamageLevel>(value, true, out var damageLevel) &&
        Enum.IsDefined(damageLevel))
    {
        Config.DamageLevel = damageLevel;
    }
    break;

case "AutoAddGauge":
    if (TryParseBool(value, out var autoAddGauge))
        Config.AutoAddGauge = autoAddGauge;
    break;
```

Add snapshot entries:

```csharp
entries["Risky"] = Math.Clamp(Config.Risky, 0, 10)
    .ToString(CultureInfo.InvariantCulture);
entries["DamageLevel"] = Config.DamageLevel.ToString();
entries["AutoAddGauge"] = Config.AutoAddGauge.ToString();
```

Implement setters using the existing no-op-on-equal + `MarkDirty()` pattern. `SetRisky` clamps to `0..10`.

- [ ] **Step 6: Add UI controls and correct No Fail copy**

Keep the existing `noFailItem`, but change its description to:

```csharp
{ Description = "Continue playing without entering a failed gauge state." };
```

Add:

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
        if (Enum.TryParse<GaugeDamageLevel>(value, true, out var level) &&
            Enum.IsDefined(level))
        {
            _configManager.SetDamageLevel(level);
        }
    })
{ Description = "Controls Miss damage to the life gauge." };
```

Keep the complete Drums ordering from Step 1. Do not create a formatter/helper abstraction for these three items.

- [ ] **Step 7: Verify Task 1 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 8: Commit Task 1**

```bash
git add DTXMania.Game/Lib/Config/GaugeDamageLevel.cs \
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

### Task 2: Make GaugeManager the sole failure-policy owner

**Estimate:** 0.75 engineer-day

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs`
- Test: `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`
- Test: `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs`

**Interfaces:**
- Consumes: `GaugeDamageLevel`, `JudgementEvent`, `JudgementType`.
- Produces: `GaugeManager(float startingLife = StartingLife, GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal, int riskyLimit = 0, bool failureEnabled = true)`; existing events remain unchanged.

- [ ] **Step 1: Add RED Damage Level tests**

Add:

```csharp
[Theory]
[InlineData(GaugeDamageLevel.Low, -1.5f)]
[InlineData(GaugeDamageLevel.Normal, -3.0f)]
[InlineData(GaugeDamageLevel.High, -4.5f)]
public void GetLifeAdjustment_Miss_UsesDamageLevel(GaugeDamageLevel level, float expected)
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

Keep the existing parameterless adjustment theory unchanged; it must continue to pass after implementation.

- [ ] **Step 2: Add RED Risky and failure-disabled tests**

Add public-behavior tests for:

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
public void FailureDisabled_NeverRaisesFailedAndContinuesAfterZero()
{
    var manager = new GaugeManager(startingLife: 1.0f, failureEnabled: false);
    var failures = 0;
    manager.Failed += (_, _) => failures++;

    manager.ProcessJudgement(new JudgementEvent(1, 0, 0, JudgementType.Miss));
    Assert.False(manager.HasFailed);
    Assert.Equal(0.0f, manager.CurrentLife);
    Assert.Equal(0, failures);

    manager.ProcessJudgement(new JudgementEvent(2, 0, 0, JudgementType.Perfect));
    Assert.Equal(2.0f, manager.CurrentLife);
    Assert.False(manager.HasFailed);
    Assert.Equal(0, failures);
}
```

Also pin:

- Risky ignores low-life threshold before the counter is exhausted.
- Risky counter reacts only to `Poor` / `Miss`.
- a real failure raises `Failed` exactly once.
- `Reset()` restores the initial Risky counter and clears `HasFailed`.

- [ ] **Step 3: Run focused gauge tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: compile/test failure because the policy-aware constructor does not exist.

- [ ] **Step 4: Implement immutable GaugeManager policy**

Add private readonly fields for damage level, clamped initial Risky limit, and failure-enabled state, plus mutable remaining Risky count.

Constructor:

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
    _remainingRisky = _riskyLimit;
    _failureEnabled = failureEnabled;
    _hasFailed = false;
}
```

Keep the existing base deltas; change only Miss:

```csharp
JudgementType.Miss => -3.0f * (_damageLevel switch
{
    GaugeDamageLevel.Low => 0.5f,
    GaugeDamageLevel.High => 1.5f,
    _ => 1.0f,
}),
```

- [ ] **Step 5: Implement one ordered failure decision**

Inside `ProcessJudgement`, preserve the current early return for disposed/already-failed instances, then:

```csharp
var previousLife = _currentLife;
var adjustment = GetLifeAdjustment(judgementEvent.Type);
_currentLife = Math.Clamp(_currentLife + adjustment, MinLife, MaxLife);

if (_riskyLimit > 0 &&
    (judgementEvent.Type == JudgementType.Poor || judgementEvent.Type == JudgementType.Miss) &&
    _remainingRisky > 0)
{
    _remainingRisky--;
}

var justFailed = false;
if (_failureEnabled)
{
    var shouldFail = _riskyLimit > 0
        ? _remainingRisky <= 0
        : _currentLife < FailureThreshold;

    if (shouldFail)
    {
        _hasFailed = true;
        justFailed = true;
        Failed?.Invoke(this, new FailureEventArgs
        {
            FinalLife = _currentLife,
            JudgementType = judgementEvent.Type
        });
    }
}
```

Then raise the existing `GaugeChanged` event with `JustFailed = justFailed`.

Update `Reset()` to restore `_remainingRisky = _riskyLimit` in addition to the existing life/failed reset.

Do not add a public Risky-state API solely for tests.

- [ ] **Step 6: Verify Task 2 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: PASS, including all existing parameterless-manager tests.

- [ ] **Step 7: Commit Task 2**

```bash
git add DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs \
        DTXMania.Test/Stage/Performance/GaugeManagerTests.cs \
        DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs
git commit -m "feat: apply gameplay failure policy in gauge manager"
```

---

### Task 3: Wire the frozen run policy through PerformanceStage

**Estimate:** 0.75 engineer-day

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Test: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

**Interfaces:**
- Consumes: Task 1 config values and Task 2 `GaugeManager` constructor/events.
- Produces: frozen `_autoAddGaugeEnabled`; no new public API.

- [ ] **Step 1: Replace the obsolete No Fail handler test before production edits**

Delete/replace the existing test:

```text
OnPlayerFailed_WhenNoFailEnabled_ShouldNotFinalizePerformance
```

That test encodes the split ownership HPA-10 is removing. Replace it with a test whose config can even contain `NoFail=true`, proving the handler no longer cares about mutable config:

```csharp
[Fact]
public void OnPlayerFailed_WhenRaised_ShouldFinalizePerformanceRegardlessOfLiveConfig()
{
    var game = ReflectionHelpers.CreateGame();
    ReflectionHelpers.SetProperty(
        game,
        nameof(BaseGame.ConfigManager),
        CreateConfigManager(new ConfigData { NoFail = true }));
    var stage = CreateStage(game);

    ReflectionHelpers.InvokePrivateMethod(
        stage,
        "OnPlayerFailed",
        null,
        new FailureEventArgs
        {
            FinalLife = 0.0f,
            JudgementType = JudgementType.Miss
        });

    Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_stageCompleted"));
}
```

Keep `OnPlayerFailed_WhenStageAlreadyCompleted_ShouldNotTransitionAgain`.

No Fail itself is proven in Task 2 by `Failed` never firing and the gauge accepting a later Perfect.

- [ ] **Step 2: Add RED frozen-setting and gauge-construction tests**

Extend existing reflection/headless tests to prove:

1. `InitializeAutoPlay()` freezes both `AutoPlay` and `AutoAddGauge` into stage fields.
2. `InitializeGameplayManagers()` constructs a gauge whose behavior matches `DamageLevel`, `Risky`, and `NoFail` from that run snapshot.
3. Mutating `Config.NoFail` after gauge construction does not change the gauge's failure policy.

Do not add a second production constructor or DI container seam for these tests.

- [ ] **Step 3: Add RED forwarding tests**

Drive the existing private `OnJudgementMade` seam with managers installed and pin:

- AutoPlay On + AutoAddGauge Off: gauge life does not change.
- AutoPlay On + AutoAddGauge On: gauge life changes.
- AutoPlay Off + AutoAddGauge Off: gauge life still changes.
- score/combo/skill processing still occurs when only gauge forwarding is suppressed.

- [ ] **Step 4: Add RED defensive completion-poll ownership test**

Construct a `GaugeManager` that is already failed **before** installing it on the stage, so no stage `Failed` subscription has run. Then set live `Config.NoFail=true` and invoke `CheckStageCompletion` while the chart is not at song end.

Expected after HPA-10: `_stageCompleted == true` because `HasFailed` is authoritative. This test must fail against today's live `Config.NoFail` check.

- [ ] **Step 5: Run focused stage tests and confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: failures from the old/new ownership expectations until production wiring changes.

- [ ] **Step 6: Freeze AutoAddGauge with AutoPlay**

Add a stage field defaulting to current behavior:

```csharp
private bool _autoAddGaugeEnabled = true;
```

Extend `InitializeAutoPlay()`:

```csharp
var config = _game?.ConfigManager?.Config;
_autoPlayEnabled = config?.AutoPlay ?? false;
_autoAddGaugeEnabled = config?.AutoAddGauge ?? true;
_autoPlayNoteIndex = 0;
```

Do not re-read `AutoAddGauge` from config inside `OnJudgementMade`.

- [ ] **Step 7: Construct GaugeManager from the run snapshot**

In `InitializeGameplayManagers()`, replace the parameterless gauge construction with:

```csharp
var config = _game.ConfigManager.Config;
_gaugeManager = new GaugeManager(
    GaugeManager.StartingLife,
    config.DamageLevel,
    config.Risky,
    failureEnabled: !config.NoFail);
```

Keep the existing event subscriptions:

```csharp
_gaugeManager.GaugeChanged += OnGaugeChanged;
_gaugeManager.Failed += OnPlayerFailed;
```

- [ ] **Step 8: Gate only AutoPlay-to-gauge forwarding**

Keep the existing manager order and change only the gauge call:

```csharp
_scoreManager?.ProcessJudgement(e);
_comboManager?.ProcessJudgement(e);
if (!_autoPlayEnabled || _autoAddGaugeEnabled)
{
    _gaugeManager?.ProcessJudgement(e);
}
_skillManager?.ProcessJudgement(e);
_skillPanelDisplay?.ProcessJudgement(e, _comboManager?.MaxCombo ?? 0);
```

Leave note resolution, attack/pad effects, judgement text, and sound paths unchanged.

- [ ] **Step 9: Remove both duplicate No Fail decisions**

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

Use the repository's actual `CompletionReason`, not `PerformanceEndReason`.

At the end of `CheckStageCompletion`, replace the live config branch with:

```csharp
if (_gaugeManager?.HasFailed == true)
{
    FinalizePerformance(CompletionReason.PlayerFailed);
}
```

There must be no `Config.NoFail` read in either path after this task.

- [ ] **Step 10: Verify Task 3 GREEN**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~GaugeManagerTests|FullyQualifiedName~GaugeManagerFailThresholdTests"
```

Expected: PASS.

- [ ] **Step 11: Run the full shared regression gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
```

Expected: PASS. Do not add a new HPA-10 E2E harness.

- [ ] **Step 12: Commit Task 3**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs \
        DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
git commit -m "feat: wire configured fail rules into gameplay"
```

---

## Final PR acceptance

Before marking HPA-10 ready for final review:

- [ ] `Risky=0`, `DamageLevel=Normal`, `AutoAddGauge=true`, `NoFail=false` reproduce current normal gauge behavior.
- [ ] `NoFail=true` can reach zero, does not raise `Failed`, and can recover with a later positive judgement.
- [ ] Risky fails on exactly the configured `Poor` / `Miss` count and ignores low-life failure before then.
- [ ] Damage Level changes only Miss damage.
- [ ] Auto Add Gauge Off changes only AutoPlay→gauge forwarding.
- [ ] `OnPlayerFailed` and `CheckStageCompletion` contain no live `Config.NoFail` decision.
- [ ] Config persistence rejects numeric Damage Level strings and stores enum names.
- [ ] The full Drums inventory and No Fail description match the design spec.
- [ ] No production/test types beyond the single `GaugeDamageLevel` enum are introduced for rules abstraction.
- [ ] Full `DTXMania.Test` suite is green.
