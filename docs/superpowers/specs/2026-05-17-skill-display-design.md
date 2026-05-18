# Skill Level Display (Gameplay + Song Selection) — Design

Date: 2026-05-17
Status: Draft

## Goal

Implement DTXManiaNX-style **Playing Skill** computation and live display during the Performance stage, plus end-of-song persistence so the **Game Skill** appears on the Song Selection panel after play. Score display already exists and is out of scope to rework.

## Background

DTXManiaNX defines two skill values (`CScoreIni.cs`):

- **Playing Skill** (`tCalculatePlayingSkill`, line 1641): `Perfect% × 0.85 + Great% × 0.35 + Combo% × 0.15`. Range 0-100. Drives the in-game skill meter.
- **Game Skill** (`tCalculateGameSkillFromPlayingSkill`, line 1623): `PlayingSkill × Level × 0.2`. Stored per chart, drives the Song Selection skill point display.

DTXmaniaCX currently has neither. The infrastructure (managers, displays, layout constants, texture assets) is mostly in place — this design fills the calculation + display + persistence gap.

## Scope

In scope:
1. Rename `JudgementType.Just` → `JudgementType.Perfect` (and `JustWindowMs`, `JustScore`, `JustCount`) to match DTXManiaNX's `EJudgement.Perfect`.
2. New `SkillManager` that computes Playing Skill from live judgement events.
3. New `SkillPanelDisplay` (left status panel: difficulty level + skill % + MAX badge).
4. New `SkillMeterDisplay` (right vertical gauge: sprite background + filling bar + numeric overlay).
5. Fix the formula in `SongScore.CalculateSkill()` to use DTXManiaNX's Game Skill formula.
6. End-of-song save flow: `PerformanceSummary` carries skill values, `ResultStage` writes to DB via a new `UpdateScoreAsync` overload, `SongStatusPanel` reads through.

Out of scope:
- Ghost / target skill comparison (`dbグラフ値目標`, online stats formula).
- Multi-instrument auto-revision factor (`dbCalcReviseValForDrGtBsAutoLanes`) — drums-only today.
- DTXManiaNX `Bad`/`Auto` judgement enum values — CX has no gameplay path for them.
- Persisting separate `BestPlayingSkill` — `HighSkill` (Game) is sufficient for per-song display.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Skill formula in gameplay | Playing Skill (0-100) | DTXManiaNX modern mode (`nSkillMode=1`). Game Skill belongs to Song Selection/Result. |
| Display style | Both — left panel + right gauge | Most faithful to DTXManiaNX. |
| Render approach | Text font (ManagedFont) for numbers | Consistent with existing ScoreDisplay/ComboDisplay. Sprite assets used only for gauge background/fill bar. |
| Behavior | DTXManiaNX-faithful | Update every judgement; show difficulty level; show MAX badge at 100; `XX.XX%` format. |
| `JudgementType.Just` rename | Yes — full rename | Match DTXManiaNX naming exactly. Mechanical change touching ~28 files. |
| Song Selection scope | Formula fix + end-of-song save | Make Song Selection skill actually meaningful, not just a fixed display. |
| Manager / Display split | Three separate components | Mirrors existing `ScoreManager/Display`, `ComboManager/Display`, `GaugeManager/Display` pattern. |

## Architecture

### Component map

```
PerformanceStage
├── JudgementManager   (existing)  ── raises JudgementMade event
│
├── ScoreManager       (existing)  ── score multipliers
├── ComboManager       (existing)  ── tracks CurrentCombo, MaxCombo
├── GaugeManager       (existing)  ── life gauge
├── SkillManager       (NEW)       ── reads MaxCombo from ComboManager;
│                                     tracks Perfect/Great/Good/Poor/Miss counts;
│                                     emits SkillChanged
│
├── ScoreDisplay       (existing)
├── ComboDisplay       (existing)
├── GaugeDisplay       (existing)
├── SkillPanelDisplay  (NEW)       ── left status panel
└── SkillMeterDisplay  (NEW)       ── right vertical gauge
```

### Event flow

```
JudgementManager.JudgementMade
  └─► PerformanceStage.OnJudgementMade
        ├─► ScoreManager.ProcessJudgement   → ScoreChanged → ScoreDisplay
        ├─► ComboManager.ProcessJudgement   → ComboChanged → ComboDisplay
        ├─► GaugeManager.ProcessJudgement   → GaugeChanged → GaugeDisplay
        └─► SkillManager.ProcessJudgement   → SkillChanged → SkillPanelDisplay + SkillMeterDisplay
```

## Detailed design

### 1. JudgementType rename (prerequisite)

Mechanical rename, executed first as its own commit so the new code reads naturally.

- `JudgementTypes.cs`: enum value `Just` → `Perfect`; constants `JustWindowMs` → `PerfectWindowMs`, `JustScore` → `PerfectScore`; switch arms in `GetJudgementType` / `GetScoreValue`; doc comments mentioning "Just".
- All call sites in `DTXMania.Game/Lib/Stage/Performance/*.cs` and `DTXMania.Game/Lib/Stage/PerformanceStage.cs`.
- All call sites in `DTXMania.Test/**` (~14 test files).
- `PerformanceSummary.JustCount` → `PerformanceSummary.PerfectCount`.

Strategy: per-file `Edit` with `replace_all` where the token is unambiguous; targeted edits for the constant/method name renames. Build + run full test suite after the rename, expect green.

### 2. `SkillManager`

`DTXMania.Game/Lib/Stage/Performance/SkillManager.cs`

```csharp
public class SkillManager : IDisposable
{
    public SkillManager(int totalNotes, ComboManager comboManager);
    public double CurrentSkill { get; }       // 0.0 to 100.0
    public int PerfectCount { get; }
    public int GreatCount { get; }
    public int GoodCount { get; }
    public int PoorCount { get; }
    public int MissCount { get; }
    public bool IsMax => CurrentSkill >= 100.0;
    public event EventHandler<SkillChangedEventArgs>? SkillChanged;
    public void ProcessJudgement(JudgementEvent e);
    public void Reset();
    public void Dispose();
}

public class SkillChangedEventArgs : EventArgs
{
    public double PreviousSkill { get; set; }
    public double CurrentSkill  { get; set; }
    public bool   IsMax         { get; set; }
    public JudgementType? JudgementType { get; set; }
}
```

**Formula** (matches DTXManiaNX `tCalculatePlayingSkill`, line 1641):

```
perfectRate = 100 × PerfectCount / TotalNotes
greatRate   = 100 × GreatCount   / TotalNotes
comboRate   = 100 × MaxCombo     / TotalNotes
CurrentSkill = perfectRate × 0.85 + greatRate × 0.35 + comboRate × 0.15
```

- `TotalNotes` is fixed at construction. `MaxCombo` is read live from injected `ComboManager` — no duplicated state.
- Constructor rejects `totalNotes <= 0` with `ArgumentException`, mirroring `ScoreManager` (`ScoreManager.cs:75`).
- `ProcessJudgement` switches on `JudgementEvent.Type` to increment the corresponding count, then recomputes `CurrentSkill` and emits `SkillChanged`.
- `Reset` clears all counts, sets skill to 0, fires `SkillChanged` with new value 0.
- Implementation calls `SongScore.CalculatePlayingSkill` (defined below) for the actual math — single source of truth.

### 3. `SkillPanelDisplay`

`DTXMania.Game/Lib/Stage/Performance/SkillPanelDisplay.cs`

Renders the left-side status panel using positions already defined in `PerformanceUILayout.SkillPanel`.

```csharp
public class SkillPanelDisplay : IDisposable
{
    public SkillPanelDisplay(IResourceManager rm, GraphicsDevice gd, SongChart? chart);
    public double Skill   { get; set; }
    public bool   ShowMax { get; set; }
    public void Update(double deltaTime);
    public void Draw(SpriteBatch sb);
    public void Dispose();
}
```

Layout:

| Element | Position constant | Format |
|---|---|---|
| Difficulty level number | `SkillPanel.LevelNumber.StartPosition = (40, 540)` | `$"{level/10}.{levelDec:D2}"` e.g. `"7.83"` |
| Skill percentage | `SkillPanel.SkillPercent.NumbersPosition = (80, 527)` | `string.Format("{0,5:##0.00}", skill)` → `" 87.42"` |
| Percent symbol | `SkillPanel.SkillPercent.PercentPosition = (239, 537)` | `"%"` |
| MAX badge | `SkillPanel.SkillPercent.MaxBadgePosition = (149, 527)` | sprite from `7_skill max.png`; drawn only when `ShowMax` |

Resources:
- One `ManagedFont` (24pt bold) for level + skill text.
- One `ManagedTexture` for `7_skill max.png` via new `TexturePath.SkillMax` constant.
- All disposed symmetrically.

Edge cases:
- `chart == null`: render `"--"` for level, `0.00` for skill, no badge.
- `DrumLevel == 0` but Guitar/Bass non-zero: pick first non-zero (priority Drum → Guitar → Bass). Drums-only today; defensive only.

### 4. `SkillMeterDisplay`

`DTXMania.Game/Lib/Stage/Performance/SkillMeterDisplay.cs`

Renders the right-side vertical gauge using sprite background + sprite fill + text overlay. Mirrors DTXManiaNX `CActPerfSkillMeter` geometry simplified (no ghost comparison).

```csharp
public class SkillMeterDisplay : IDisposable
{
    public SkillMeterDisplay(IResourceManager rm, GraphicsDevice gd);
    public double Skill { get; set; }   // 0.0 to 100.0
    public void Update(double deltaTime);
    public void Draw(SpriteBatch sb);
    public void Dispose();
}
```

New layout constants under `PerformanceUILayout.SkillMeter`:

| Constant | Value | Meaning |
|---|---|---|
| `BackgroundPosition` | `(900, 50)` | Top-left of frame on screen |
| `BackgroundSourceRect` | `Rectangle(2, 2, 251, 584)` | Source rect in `7_Graph_main.png` |
| `GaugeOffset` | `(45, 0)` | Bar offset relative to background origin |
| `GaugeSourceRectXY` | `(2, 2)` width 30 | Source X/Y in `7_Graph_Gauge.png` |
| `GaugeMaxHeight` | `434` | Bar height in px at skill = 100% |
| `GaugeBaselineY` | `527` | Y of bar bottom (= `BackgroundPosition.Y + 477`) |
| `NumberOffsetFromTopOfBar` | `-10` | Numeric label is drawn 10px above the bar top |
| `LabelSourceRect` | `Rectangle(260, 2, 30, 120)` | "Current" label sprite from `7_Graph_main.png` |
| `LabelPosition` | `(945, 407)` | Where the "current" label is drawn |

Draw order per frame:
1. Background sprite at `BackgroundPosition`.
2. `gaugeHeight = Math.Min(GaugeMaxHeight, (int)(GaugeMaxHeight × Skill / 100.0))`.
3. `barTopY = GaugeBaselineY - gaugeHeight`.
4. Bar sprite (source rect width 30 × height `gaugeHeight`) at `(BackgroundPosition.X + GaugeOffset.X, barTopY)`. **Skip when `gaugeHeight <= 0`.**
5. "Current" label sprite at `LabelPosition`.
6. Numeric text `string.Format("{0,5:##0.00}", Skill)` at `(BackgroundPosition.X + GaugeOffset.X - 1, barTopY + NumberOffsetFromTopOfBar)`.

Resources:
- Two `ManagedTexture` (background + gauge) via new `TexturePath.PerfGraphMain` and `TexturePath.PerfGraphGauge`.
- One `ManagedFont` (12pt bold) for floating numeric label.

Edge cases:
- Texture load failure: fall back to drawing a 1px white rectangle for background/bar so layout stays visible. Throws nothing.

### 5. `SongScore` formula fix

`DTXMania.Game/Lib/Song/Entities/SongScore.cs`

Add static helpers (sibling to `RankString`, `RankMultiplier`):

```csharp
public static double CalculatePlayingSkill(int totalNotes, int perfect, int great, int maxCombo)
{
    if (totalNotes <= 0) return 0.0;
    double perfectRate = 100.0 * perfect  / totalNotes;
    double greatRate   = 100.0 * great    / totalNotes;
    double comboRate   = 100.0 * maxCombo / totalNotes;
    return perfectRate * 0.85 + greatRate * 0.35 + comboRate * 0.15;
}

public static double CalculateGameSkill(double playingSkill, int level, int levelDec)
{
    double actualLevel = level >= 100
        ? level / 100.0                          // already-scaled 850 → 8.50
        : (level / 10.0) + (levelDec / 100.0);   // 78 + 33 → 7.83
    return playingSkill * actualLevel * 0.2;
}
```

Replace `CalculateSkill()` body — discard the old `scoreRatio × difficultyMultiplier × rankMultiplier × 100` formula. The fixed version uses the new static helpers but only knows `DifficultyLevel` (whole part), so it's a coarser recomputation suitable for legacy/manual triggers:

```csharp
public void CalculateSkill()
{
    if (BestScore == 0 || DifficultyLevel == 0)
    {
        SongSkill = 0;
        return;
    }
    double playing = CalculatePlayingSkill(TotalNotes, BestPerfect, BestGreat, MaxCombo);
    SongSkill = CalculateGameSkill(playing, DifficultyLevel, 0);
    if (SongSkill > HighSkill) HighSkill = SongSkill;
}
```

For the end-of-song save path (the one that actually runs after every play), `UpdateScoreAsync` does **not** call `CalculateSkill()`. Instead it assigns `score.SongSkill = summary.GameSkill` directly — `summary.GameSkill` was already computed in `PerformanceStage` using both `ChartLevel` and `ChartLevelDec`, so the decimal is preserved. `CalculateSkill()` remains as a fallback for tests or future paths that only have `DifficultyLevel`.

### 6. End-of-song save flow

**Step 1** — extend `PerformanceSummary` (`Performance/PerformanceSummary.cs`):

```csharp
public double PlayingSkill { get; set; }
public double GameSkill    { get; set; }
public int    ChartLevel    { get; set; }   // e.g. 78 (raw stored value)
public int    ChartLevelDec { get; set; }   // e.g. 33
```

(`JustCount` → `PerfectCount` rename folded into Section 1.)

**Step 2** — populate in `PerformanceStage.OnStageCompleted` (around `PerformanceStage.cs:1733`):

```csharp
var chart = _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
int level    = chart?.DrumLevel    ?? 0;
int levelDec = chart?.DrumLevelDec ?? 0;
double playing = _skillManager?.CurrentSkill ?? 0.0;
double game    = SongScore.CalculateGameSkill(playing, level, levelDec);

_performanceSummary = new PerformanceSummary {
    /* existing fields */,
    PlayingSkill = playing, GameSkill = game,
    ChartLevel = level, ChartLevelDec = levelDec
};
```

**Step 3** — new `UpdateScoreAsync` overload on `SongManager` + `SongDatabaseService`:

```csharp
public async Task UpdateScoreAsync(int chartId, EInstrumentPart instrument, PerformanceSummary summary)
```

The DB-service implementation:
- Loads existing `SongScore` (or creates via existing helper if missing).
- If `summary.Score > BestScore`: updates `BestScore`, `BestPerfect/Great/Good/Poor/Miss`, `MaxCombo`, `FullCombo`, `BestAchievementRate`.
- Always updates `LastScore = summary.Score`, `LastSkillPoint = summary.GameSkill`, `LastPlayedAt = UtcNow`, increments `PlayCount`. If `summary.ClearFlag`, increments `ClearCount`.
- Assigns `score.SongSkill = summary.GameSkill` directly (the value was computed in `PerformanceStage` with the full level + levelDec — see Section 6 Step 2). If `summary.GameSkill > HighSkill`, bumps `HighSkill = summary.GameSkill`. Does **not** call `score.CalculateSkill()` — that would re-derive a coarser value and clobber the precise one.
- `SaveChangesAsync`.

**Step 4** — fire from `ResultStage` after `ExtractSharedData` (one-shot, fire-and-forget):

```csharp
if (_selectedChart != null && _performanceSummary != null)
{
    _ = SongManager.Instance.UpdateScoreAsync(
        _selectedChart.Id, EInstrumentPart.DRUMS, _performanceSummary);
}
```

Errors are caught and logged inside the service — UI does not block on persistence.

**Step 5** — optional UX polish in `SongStatusPanel.DrawSkillPointSection` (`SongStatusPanel.cs:859`):

```csharp
var skillValue = score.HasBeenPlayed
    ? score.HighSkill.ToString("F2")
    : "---";
```

(Today it shows `"0.00"` for unplayed charts.)

## Testing

All under `DTXMania.Test/`. New files:

| File | Mac-safe? | Scope |
|---|---|---|
| `Stage/Performance/SkillManagerTests.cs` | yes | Formula, event payload, `MaxCombo` read-through, constructor guard, `Reset`, `IsMax` boundary |
| `Stage/Performance/SkillPanelDisplayLogicTests.cs` | no — exclude | Format strings, `ShowMax` gate |
| `Stage/Performance/SkillMeterDisplayLogicTests.cs` | no — exclude | `gaugeHeight` math, `barTopY` math, skip-when-zero |
| `Song/Entities/SongScoreSkillFormulaTests.cs` | yes | Static helpers against DTXManiaNX reference fixtures |
| `Song/SongDatabaseServiceUpdateScoreTests.cs` | yes | New overload persists skill correctly, in-memory EF Core SQLite |
| `Stage/PerformanceStageSkillIntegrationTests.cs` | no — exclude | E2E: `PerformanceSummary.PlayingSkill`/`GameSkill` populated at stage completion |

Mechanical updates to existing tests for the `Just`→`Perfect` rename, and updates to tests that asserted the old `SongScore.CalculateSkill` formula.

Reference fixtures for formula tests (from `tCalculatePlayingSkill`):

| Perfect | Great | Good | Poor | Miss | MaxCombo | Total | PlayingSkill |
|---|---|---|---|---|---|---|---|
| 100 | 0 | 0 | 0 | 0 | 100 | 100 | 100.0 |
| 0 | 100 | 0 | 0 | 0 | 100 | 100 | 50.0 |
| 50 | 0 | 0 | 0 | 50 | 50 | 100 | 50.0 |
| 20 | 80 | 0 | 0 | 0 | 100 | 100 | 60.0 |
| 0 | 0 | 0 | 0 | 100 | 0 | 100 | 0.0 |

Game Skill fixtures (`PlayingSkill × Level × 0.2`):

| PlayingSkill | Level | LevelDec | GameSkill |
|---|---|---|---|
| 100.0 | 850 | 0 | `100 × 8.5 × 0.2` = 170.0 |
| 60.0 | 78 | 33 | `60 × 7.83 × 0.2` = 93.96 |
| 0.0 | 50 | 0 | 0.0 |

## Acceptance criteria

1. Rename: full test suite green on Mac + Windows after `Just` → `Perfect` rename. Zero new warnings.
2. Gameplay: left panel shows difficulty level + live skill % updating per hit + MAX badge at 100; right gauge bar height tracks skill, numeric overlay follows bar top.
3. Formula parity: `SkillManager.CurrentSkill` matches DTXManiaNX `tCalculatePlayingSkill` for the fixtures above.
4. Persistence: completing a song updates the DB; reloading Song Selection shows the new `HighSkill` in `SongStatusPanel.DrawSkillPointSection`.
5. No regressions: existing `ScoreDisplay`, `ComboDisplay`, `GaugeDisplay` behavior unchanged; existing tests still pass.

## File summary

**New** (8 files):
- `DTXMania.Game/Lib/Stage/Performance/SkillManager.cs`
- `DTXMania.Game/Lib/Stage/Performance/SkillPanelDisplay.cs`
- `DTXMania.Game/Lib/Stage/Performance/SkillMeterDisplay.cs`
- `DTXMania.Test/Stage/Performance/SkillManagerTests.cs`
- `DTXMania.Test/Stage/Performance/SkillPanelDisplayLogicTests.cs`
- `DTXMania.Test/Stage/Performance/SkillMeterDisplayLogicTests.cs`
- `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs`
- `DTXMania.Test/Stage/PerformanceStageSkillIntegrationTests.cs`

**Modified** (high-impact):
- `DTXMania.Game/Lib/Song/Entities/JudgementTypes.cs` — `Just`→`Perfect` rename + constants
- `DTXMania.Game/Lib/Song/Entities/SongScore.cs` — new static `CalculatePlayingSkill`/`CalculateGameSkill`; replace `CalculateSkill()` formula; accept `PerformanceSummary` in update path
- `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs` — `JustCount`→`PerfectCount`; new `PlayingSkill`/`GameSkill`/`ChartLevel`/`ChartLevelDec`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — new manager/displays wiring; populate skill in summary
- `DTXMania.Game/Lib/Stage/ResultStage.cs` — call `UpdateScoreAsync` with summary
- `DTXMania.Game/Lib/Song/SongManager.cs` + `Entities/SongDatabaseService.cs` — new `UpdateScoreAsync(chartId, instrument, summary)` overload
- `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs` — new `SkillMeter` static class
- `DTXMania.Game/Lib/Resources/TexturePath.cs` — add `PerfGraphMain`, `PerfGraphGauge`, `SkillMax`
- `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs` — optional `"---"` fallback for unplayed
- `DTXMania.Test/DTXMania.Test.Mac.csproj` — add three new test files to exclusion list
- Mechanical rename touches across `ComboManager.cs`, `GaugeManager.cs`, `JudgementManager.cs`, `JudgementTextPopup.cs`, `ScoreManager.cs`, and ~14 test files

## References

- DTXManiaNX `CScoreIni.tCalculatePlayingSkill` (`DTXManiaNX/DTXMania/Code/Score,Song/CScoreIni.cs:1641`)
- DTXManiaNX `CScoreIni.tCalculateGameSkillFromPlayingSkill` (`CScoreIni.cs:1623`)
- DTXManiaNX `CActPerfSkillMeter` (`DTXManiaNX/DTXMania/Code/Stage/07.Performance/CActPerfSkillMeter.cs`)
- DTXManiaNX live update site for in-game skill (`DTXManiaNX/DTXMania/Code/Stage/07.Performance/DrumsScreen/CStagePerfDrumsScreen.cs:566`)
- DTXManiaNX `EJudgement` enum (`DTXManiaNX/DTXMania/Code/App/CConstants.cs:191`)
