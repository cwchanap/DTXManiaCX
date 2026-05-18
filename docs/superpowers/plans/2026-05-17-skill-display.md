# Skill Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement DTXManiaNX-style Playing Skill computation + live in-game display (left panel + right gauge), plus end-of-song Game Skill persistence so the Song Selection skill point panel shows real values.

**Architecture:** Add a new `SkillManager` (subscribes to `JudgementManager.JudgementMade`, reads `MaxCombo` from `ComboManager`, emits `SkillChanged`), plus two new display components (`SkillPanelDisplay`, `SkillMeterDisplay`). Wire them into `PerformanceStage` mirroring the existing `ScoreManager`/`ScoreDisplay` pattern. Add static skill-formula helpers to `SongScore`. Extend `PerformanceSummary` with skill fields and persist via a new `UpdateScoreAsync` overload that `ResultStage` fires after each play. Includes a prerequisite mechanical rename of `JudgementType.Just` → `JudgementType.Perfect` so the new code reads naturally.

**Tech Stack:** .NET 8, MonoGame 3.8 (DesktopGL on Mac, WindowsDX on Windows), xUnit + Moq for tests, EF Core SQLite for persistence.

**Spec:** [`docs/superpowers/specs/2026-05-17-skill-display-design.md`](../specs/2026-05-17-skill-display-design.md)

**Working directory:** `/Users/chanwaichan/workspace/DTXmaniaCX`

**Build/test commands** (Mac, used throughout):
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test  DTXMania.Test/DTXMania.Test.Mac.csproj
```

---

## File Structure

**New files:**

| Path | Responsibility |
|---|---|
| `DTXMania.Game/Lib/Stage/Performance/SkillManager.cs` | Computes Playing Skill from judgement events; raises `SkillChanged` |
| `DTXMania.Game/Lib/Stage/Performance/SkillPanelDisplay.cs` | Renders left-side panel: level + skill % + MAX badge |
| `DTXMania.Game/Lib/Stage/Performance/SkillMeterDisplay.cs` | Renders right-side vertical gauge with text overlay |
| `DTXMania.Test/Stage/Performance/SkillManagerTests.cs` | Unit tests for `SkillManager` (Mac-safe) |
| `DTXMania.Test/Stage/Performance/SkillPanelDisplayLogicTests.cs` | Formatting helper tests (Mac-excluded) |
| `DTXMania.Test/Stage/Performance/SkillMeterDisplayLogicTests.cs` | Gauge math tests (Mac-excluded) |
| `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs` | Static formula helper tests (Mac-safe) |
| `DTXMania.Test/Stage/PerformanceStageSkillIntegrationTests.cs` | Summary population E2E (Mac-excluded) |

**Modified files:**

| Path | Change |
|---|---|
| `DTXMania.Game/Lib/Song/Entities/JudgementTypes.cs` | `Just` → `Perfect` rename + constants |
| `DTXMania.Game/Lib/Song/Entities/SongScore.cs` | New static `CalculatePlayingSkill`/`CalculateGameSkill`; fix `CalculateSkill()` body |
| `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` | New `UpdateScoreAsync(chartId, instrument, summary)` overload |
| `DTXMania.Game/Lib/Song/SongManager.cs` | New `UpdateScoreAsync(chartId, instrument, summary)` overload |
| `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs` | `"---"` fallback when unplayed |
| `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs` | `JustCount` → `PerfectCount`; new `PlayingSkill`/`GameSkill`/`ChartLevel`/`ChartLevelDec` |
| `DTXMania.Game/Lib/Stage/PerformanceStage.cs` | Wire `SkillManager` + displays; populate skill in summary |
| `DTXMania.Game/Lib/Stage/ResultStage.cs` | Call `UpdateScoreAsync` after data extraction |
| `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs` | New `SkillMeter` static class |
| `DTXMania.Game/Lib/Resources/TexturePath.cs` | `PerfGraphMain`, `PerfGraphGauge` constants |
| `DTXMania.Test/DTXMania.Test.Mac.csproj` | Add three new test files to exclusion list |
| Many call sites in `DTXMania.Game/**` and `DTXMania.Test/**` | `JudgementType.Just` → `JudgementType.Perfect` |

---

## Task 1: Rename `JudgementType.Just` → `JudgementType.Perfect`

**Files (enum + tokens):**
- Modify: `DTXMania.Game/Lib/Song/Entities/JudgementTypes.cs`
- Modify: all `*.cs` files in `DTXMania.Game/` and `DTXMania.Test/` that reference `JudgementType.Just`, `JustWindowMs`, `JustScore`, `JustCount`

- [ ] **Step 1: Rename enum value, constants, and switch arms in `JudgementTypes.cs`**

Open `DTXMania.Game/Lib/Song/Entities/JudgementTypes.cs`. Apply these exact edits:

Change enum (line ~14):
```csharp
        /// <summary>
        /// Perfect timing hit (highest accuracy)
        /// </summary>
        Perfect,
```

Change constants (line ~187, ~216, and lines 251, 268):
```csharp
        /// <summary>
        /// Perfect timing window for "Perfect" judgement (±25ms)
        /// </summary>
        public const double PerfectWindowMs = 25.0;
```
```csharp
        /// <summary>
        /// Score points awarded for a "Perfect" hit
        /// </summary>
        public const int PerfectScore = 1000;
```

In `GetJudgementType` (line 247-257):
```csharp
            if (absDelta <= PerfectWindowMs) return JudgementType.Perfect;
```

In `GetScoreValue` (line 266-274):
```csharp
                JudgementType.Perfect => PerfectScore,
```

In doc comment for `Type` property (line ~62):
```csharp
        /// Type of the judgement (Perfect/Great/Good/Poor/Miss)
```

- [ ] **Step 2: Find all remaining references in the Game project**

Run:
```bash
grep -rn "JudgementType\.Just\|JustWindowMs\|JustScore\|JustCount" DTXMania.Game --include="*.cs"
```

Expected: a list of file:line locations.

- [ ] **Step 3: For each Game-project file, replace tokens**

For each file in the grep output, use `Edit` with `replace_all=true` on each token separately. The tokens are unambiguous within their files:
- `JudgementType.Just` → `JudgementType.Perfect`
- `JustWindowMs` → `PerfectWindowMs`
- `JustScore` → `PerfectScore`
- `JustCount` → `PerfectCount` (only in `PerformanceSummary.cs` and `PerformanceStage.cs`)

Specifically include the following known files (verify with grep above is the source of truth):
- `DTXMania.Game/Lib/Stage/Performance/ScoreManager.cs`
- `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`
- `DTXMania.Game/Lib/Stage/Performance/JudgementTextPopup.cs`
- `DTXMania.Game/Lib/Stage/Performance/GaugeManager.cs`
- `DTXMania.Game/Lib/Stage/Performance/ComboManager.cs`
- `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs`

- [ ] **Step 4: Find all references in the Test project**

Run:
```bash
grep -rn "JudgementType\.Just\|JustWindowMs\|JustScore\|JustCount" DTXMania.Test --include="*.cs"
```

- [ ] **Step 5: Replace tokens in each Test file**

Same approach as Step 3, applied to every file the grep returns. Known files include:
- `DTXMania.Test/Song/JudgementEventTests.cs`
- `DTXMania.Test/Stage/Performance/JudgementManagerTests.cs`
- `DTXMania.Test/Stage/Performance/JudgementManagerHitWindowTests.cs`
- `DTXMania.Test/Stage/Performance/JudgementManagerAdditionalTests.cs`
- `DTXMania.Test/Stage/Performance/JudgementTextPopupTests.cs`
- `DTXMania.Test/Stage/Performance/JudgementTextPopupLogicTests.cs`
- `DTXMania.Test/Stage/Performance/ScoreManagerTests.cs`
- `DTXMania.Test/Stage/Performance/ScoreManagerTotalScoreCapTests.cs`
- `DTXMania.Test/Stage/Performance/ComboManagerTests.cs`
- `DTXMania.Test/Stage/Performance/ComboManagerResetOnPoorTests.cs`
- `DTXMania.Test/Stage/Performance/GaugeManagerTests.cs`
- `DTXMania.Test/Stage/Performance/GaugeManagerFailThresholdTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceSummaryTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStatsTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStageJudgementIntegrationTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStageCoverageTests.cs`
- `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`
- `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`
- `DTXMania.Test/Helpers/MockGameplayComponents.cs`

- [ ] **Step 6: Build to verify no syntax errors remain**

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: `Build succeeded`. If errors mention missing `JudgementType.Just`, `JustWindowMs`, `JustScore`, or `JustCount`, re-run the greps in Steps 2/4 and replace anything missed.

- [ ] **Step 7: Run the full Mac test suite**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: All tests pass. The rename is mechanical; no test behavior should have changed.

- [ ] **Step 8: Verify no `Just` token leaked into strings or comments**

Run:
```bash
grep -rn "\.Just\b\|JustScore\|JustWindow\|JustCount" DTXMania.Game DTXMania.Test --include="*.cs"
```

Expected: empty output (or only matches inside unrelated identifiers like `JustifyContent` — visually inspect to confirm none are stale references).

- [ ] **Step 9: Commit**

```bash
git add DTXMania.Game DTXMania.Test
git commit -m "refactor: rename JudgementType.Just to Perfect for DTXManiaNX parity

Mechanical rename touching the enum value, related constants
(JustWindowMs/Score), the PerformanceSummary.JustCount field,
and all call sites in Game + Test projects.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: Add `SongScore.CalculatePlayingSkill` static helper (TDD)

**Files:**
- Create: `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongScore.cs`

- [ ] **Step 1: Write the failing test file**

Create `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs` with:

```csharp
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song.Entities
{
    /// <summary>
    /// Tests for SongScore static skill formula helpers.
    /// Reference values verified against DTXManiaNX CScoreIni.tCalculatePlayingSkill
    /// (Score,Song/CScoreIni.cs:1641) and tCalculateGameSkillFromPlayingSkill (line 1623).
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongScoreSkillFormulaTests
    {
        #region CalculatePlayingSkill

        [Fact]
        public void CalculatePlayingSkill_AllPerfectFullCombo_ShouldReturn100()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 100, great: 0, maxCombo: 100);
            Assert.Equal(100.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_AllGreatFullCombo_ShouldReturn50()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 0, great: 100, maxCombo: 100);
            Assert.Equal(50.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_HalfPerfectHalfMiss_ShouldReturn50()
        {
            // Perfect%=50*0.85=42.5, Great%=0, Combo%=50*0.15=7.5 → 50.0
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 50, great: 0, maxCombo: 50);
            Assert.Equal(50.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_MixedPerfectGreatFullCombo_ShouldReturn60()
        {
            // 20*0.85 + 80*0.35 + 100*0.15 = 17 + 28 + 15 = 60
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 20, great: 80, maxCombo: 100);
            Assert.Equal(60.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_NoHits_ShouldReturnZero()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 100, perfect: 0, great: 0, maxCombo: 0);
            Assert.Equal(0.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_ZeroTotalNotes_ShouldReturnZero()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: 0, perfect: 0, great: 0, maxCombo: 0);
            Assert.Equal(0.0, result, 6);
        }

        [Fact]
        public void CalculatePlayingSkill_NegativeTotalNotes_ShouldReturnZero()
        {
            double result = SongScore.CalculatePlayingSkill(totalNotes: -1, perfect: 0, great: 0, maxCombo: 0);
            Assert.Equal(0.0, result, 6);
        }

        #endregion
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreSkillFormulaTests"
```

Expected: build failure with "SongScore does not contain a definition for CalculatePlayingSkill" or similar.

- [ ] **Step 3: Add `CalculatePlayingSkill` static method to `SongScore.cs`**

Open `DTXMania.Game/Lib/Song/Entities/SongScore.cs`. Add this method inside the class, near the existing static `RankMultiplier` helper (around line 335):

```csharp
        /// <summary>
        /// DTXManiaNX-faithful Playing Skill: Perfect%·0.85 + Great%·0.35 + Combo%·0.15.
        /// Returns 0 when totalNotes is zero or negative.
        /// Reference: DTXManiaNX CScoreIni.tCalculatePlayingSkill (Score,Song/CScoreIni.cs:1641).
        /// </summary>
        public static double CalculatePlayingSkill(int totalNotes, int perfect, int great, int maxCombo)
        {
            if (totalNotes <= 0) return 0.0;
            double perfectRate = 100.0 * perfect  / totalNotes;
            double greatRate   = 100.0 * great    / totalNotes;
            double comboRate   = 100.0 * maxCombo / totalNotes;
            return perfectRate * 0.85 + greatRate * 0.35 + comboRate * 0.15;
        }
```

- [ ] **Step 4: Run test, verify it passes**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreSkillFormulaTests"
```

Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongScore.cs DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs
git commit -m "feat: add SongScore.CalculatePlayingSkill helper

DTXManiaNX-faithful Playing Skill formula
(Perfect% × 0.85 + Great% × 0.35 + Combo% × 0.15).
Tested against reference fixtures.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 3: Add `SongScore.CalculateGameSkill` static helper (TDD)

**Files:**
- Modify: `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongScore.cs`

- [ ] **Step 1: Append CalculateGameSkill tests to the test file**

Open `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs`. Before the closing `}` of the class, add:

```csharp
        #region CalculateGameSkill

        [Fact]
        public void CalculateGameSkill_PlayingSkill100_Level850_ShouldReturn170()
        {
            // level >= 100 branch: actualLevel = 850/100 = 8.5; 100 * 8.5 * 0.2 = 170
            double result = SongScore.CalculateGameSkill(playingSkill: 100.0, level: 850, levelDec: 0);
            Assert.Equal(170.0, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_PlayingSkill60_Level78Dec33_ShouldReturn97_56()
        {
            // level < 100 branch: actualLevel = 78/10.0 + 33/100.0 = 7.8 + 0.33 = 8.13; 60 * 8.13 * 0.2 = 97.56
            double result = SongScore.CalculateGameSkill(playingSkill: 60.0, level: 78, levelDec: 33);
            Assert.Equal(97.56, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_ZeroPlayingSkill_ShouldReturnZero()
        {
            double result = SongScore.CalculateGameSkill(playingSkill: 0.0, level: 78, levelDec: 33);
            Assert.Equal(0.0, result, 6);
        }

        [Fact]
        public void CalculateGameSkill_PlayingSkill100_Level50Dec0_ShouldReturn100()
        {
            // level < 100 branch: actualLevel = 50/10 + 0/100 = 5.0; 100 * 5.0 * 0.2 = 100
            double result = SongScore.CalculateGameSkill(playingSkill: 100.0, level: 50, levelDec: 0);
            Assert.Equal(100.0, result, 6);
        }

        #endregion
```

- [ ] **Step 2: Run tests, verify the new ones fail**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreSkillFormulaTests"
```

Expected: build failure with "SongScore does not contain a definition for CalculateGameSkill".

- [ ] **Step 3: Add `CalculateGameSkill` static method to `SongScore.cs`**

In `DTXMania.Game/Lib/Song/Entities/SongScore.cs`, right after `CalculatePlayingSkill`, add:

```csharp
        /// <summary>
        /// DTXManiaNX-faithful Game Skill: PlayingSkill · actualLevel · 0.2.
        /// actualLevel encodes the chart's difficulty value with its decimal:
        ///   level &gt;= 100  → actualLevel = level / 100        (e.g. 850 → 8.50)
        ///   level &lt;  100  → actualLevel = level / 10 + levelDec / 100  (e.g. 78 + 33 → 7.8 + 0.33 = 8.13)
        /// Reference: DTXManiaNX CScoreIni.tCalculateGameSkillFromPlayingSkill (Score,Song/CScoreIni.cs:1623).
        /// </summary>
        public static double CalculateGameSkill(double playingSkill, int level, int levelDec)
        {
            double actualLevel = level >= 100
                ? level / 100.0
                : (level / 10.0) + (levelDec / 100.0);
            return playingSkill * actualLevel * 0.2;
        }
```

- [ ] **Step 4: Run tests, verify they pass**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreSkillFormulaTests"
```

Expected: 11 tests pass (7 from Task 2 + 4 new).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongScore.cs DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs
git commit -m "feat: add SongScore.CalculateGameSkill helper

DTXManiaNX-faithful Game Skill formula (PlayingSkill · Level · 0.2)
with separate branches for level >= 100 vs the level + decimal
encoding used by older DTX charts.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: Replace the old `SongScore.CalculateSkill()` instance method body

**Files:**
- Modify: `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongScore.cs`
- Possibly modify: any existing test that asserted the OLD formula (use grep to find)

- [ ] **Step 1: Append failing test for the rewritten CalculateSkill**

Append to `DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs` before the closing `}`:

```csharp
        #region CalculateSkill (instance)

        [Fact]
        public void CalculateSkill_UsesPlayingTimesGameFormula()
        {
            // PlayingSkill from (50 Perfect, 0 Great, 50 MaxCombo, 100 Total) = 50.0
            // GameSkill with DifficultyLevel=78, levelDec=0 → actualLevel=7.8 → 50 * 7.8 * 0.2 = 78.0
            var score = new SongScore
            {
                BestScore = 500000,
                DifficultyLevel = 78,
                TotalNotes = 100,
                BestPerfect = 50,
                BestGreat = 0,
                MaxCombo = 50
            };
            score.CalculateSkill();
            Assert.Equal(78.0, score.SongSkill, 6);
            Assert.Equal(78.0, score.HighSkill, 6);
        }

        [Fact]
        public void CalculateSkill_ZeroBestScore_ShouldZeroSongSkill()
        {
            var score = new SongScore { BestScore = 0, DifficultyLevel = 78, TotalNotes = 100, BestPerfect = 100, MaxCombo = 100 };
            score.CalculateSkill();
            Assert.Equal(0.0, score.SongSkill);
        }

        [Fact]
        public void CalculateSkill_ZeroDifficulty_ShouldZeroSongSkill()
        {
            var score = new SongScore { BestScore = 500000, DifficultyLevel = 0, TotalNotes = 100, BestPerfect = 100, MaxCombo = 100 };
            score.CalculateSkill();
            Assert.Equal(0.0, score.SongSkill);
        }

        [Fact]
        public void CalculateSkill_PreservesExistingHigherHighSkill()
        {
            var score = new SongScore
            {
                BestScore = 500000,
                DifficultyLevel = 78,
                TotalNotes = 100,
                BestPerfect = 50, BestGreat = 0, MaxCombo = 50,
                HighSkill = 90.0
            };
            score.CalculateSkill();
            Assert.Equal(78.0, score.SongSkill, 6);
            Assert.Equal(90.0, score.HighSkill, 6);
        }

        #endregion
```

- [ ] **Step 2: Run, verify they fail (old formula returns different values)**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreSkillFormulaTests.CalculateSkill_"
```

Expected: 4 failures (e.g. `Assert.Equal Failure: expected 78.0, actual <some old-formula value>`).

- [ ] **Step 3: Replace `CalculateSkill()` body in `SongScore.cs`**

Open `DTXMania.Game/Lib/Song/Entities/SongScore.cs`. Locate the existing method at line ~265:

```csharp
        public void CalculateSkill()
        {
            if (BestScore == 0 || DifficultyLevel == 0)
            {
                SongSkill = 0;
                return;
            }
            
            // DTXMania skill calculation formula
            // Skill = (Score / 1000000) * DifficultyLevel * Multiplier
            var scoreRatio = (double)BestScore / 1000000.0;
            var difficultyMultiplier = DifficultyLevel / 100.0;
            var rankMultiplier = GetRankMultiplier();
            
            SongSkill = scoreRatio * difficultyMultiplier * rankMultiplier * 100.0;
            
            if (SongSkill > HighSkill)
                HighSkill = SongSkill;
        }
```

Replace with:
```csharp
        /// <summary>
        /// Recomputes SongSkill using the DTXManiaNX Game Skill formula.
        /// Note: this only knows DifficultyLevel (whole part), so it is a coarser
        /// approximation than the persistence path, which receives the precise
        /// chart level + levelDec via PerformanceSummary. Kept for legacy/manual triggers.
        /// </summary>
        public void CalculateSkill()
        {
            if (BestScore == 0 || DifficultyLevel == 0)
            {
                SongSkill = 0;
                return;
            }

            double playing = CalculatePlayingSkill(TotalNotes, BestPerfect, BestGreat, MaxCombo);
            SongSkill = CalculateGameSkill(playing, DifficultyLevel, 0);

            if (SongSkill > HighSkill)
                HighSkill = SongSkill;
        }
```

- [ ] **Step 4: Find and inspect any test asserting the old formula**

Run:
```bash
grep -rn "CalculateSkill\|SongSkill\|HighSkill" DTXMania.Test --include="*.cs"
```

Visually inspect each match. If a test calls `CalculateSkill()` and asserts the OLD formula value (the `scoreRatio · DifficultyLevel/100 · RankMultiplier · 100` shape), update its expected value to the new formula or rewrite it to assert against `CalculatePlayingSkill`/`CalculateGameSkill` directly.

- [ ] **Step 5: Run full Mac test suite**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: All tests pass including the 4 new `CalculateSkill_*` tests.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongScore.cs DTXMania.Test/Song/Entities/SongScoreSkillFormulaTests.cs
git commit -m "fix: rewrite SongScore.CalculateSkill with DTXManiaNX formula

Replaces the previous score-ratio·rank-multiplier formula with the
DTXManiaNX Playing Skill · Game Skill chain, delegating to the new
static helpers added in the prior commit.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 5: Add `TexturePath` constants for `7_Graph_main.png` and `7_Graph_Gauge.png`

**Files:**
- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs`

- [ ] **Step 1: Locate the Performance asset region**

Run:
```bash
grep -n "SkillPanel\|7_Graph\|GaugeFill" DTXMania.Game/Lib/Resources/TexturePath.cs
```

Note the line number of the existing `SkillPanel` constant (around line 273) — we'll insert near it.

- [ ] **Step 2: Add two new constants in `TexturePath.cs`**

In `DTXMania.Game/Lib/Resources/TexturePath.cs`, right after the existing `SkillPanel` constant, add:

```csharp
        /// <summary>
        /// Performance skill meter background graph (right-side vertical gauge frame)
        /// </summary>
        public const string PerfGraphMain = "Graphics/7_Graph_main.png";

        /// <summary>
        /// Performance skill meter filling bar (right-side vertical gauge bar)
        /// </summary>
        public const string PerfGraphGauge = "Graphics/7_Graph_Gauge.png";
```

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 4: Verify assets exist on disk**

Run:
```bash
ls System/Graphics/7_Graph_main.png System/Graphics/7_Graph_Gauge.png
```

Expected: both paths print. If missing, stop and ask the user — the assets should be under the default skin.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Resources/TexturePath.cs
git commit -m "feat: add TexturePath constants for skill meter graphics

PerfGraphMain and PerfGraphGauge for the right-side vertical
skill meter assets (7_Graph_main.png, 7_Graph_Gauge.png).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 6: Add `PerformanceUILayout.SkillMeter` layout constants

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`

- [ ] **Step 1: Find the existing `SkillPanel` region**

Run:
```bash
grep -n "region Skill Panel\|endregion" DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs | head -20
```

Identify the `#endregion` line that closes the existing `Skill Panel (7_SkillPanel.png)` block (around line 401).

- [ ] **Step 2: Insert a new `SkillMeter` region**

In `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`, immediately after the existing Skill Panel `#endregion` (line ~401), add:

```csharp
        #region Skill Meter (right-side vertical gauge — 7_Graph_main.png, 7_Graph_Gauge.png)

        /// <summary>
        /// Right-side vertical skill gauge meter.
        /// Geometry mirrors DTXManiaNX CActPerfSkillMeter drums layout (Code/Stage/07.Performance/CActPerfSkillMeter.cs).
        /// </summary>
        public static class SkillMeter
        {
            /// <summary>Top-left of the gauge frame on screen.</summary>
            public static readonly Vector2 BackgroundPosition = new Vector2(900, 50);

            /// <summary>Source rect in 7_Graph_main.png for the background frame.</summary>
            public static readonly Rectangle BackgroundSourceRect = new Rectangle(2, 2, 251, 584);

            /// <summary>Offset of the filling bar relative to BackgroundPosition.</summary>
            public static readonly Vector2 GaugeOffset = new Vector2(45, 0);

            /// <summary>Width of the filling bar in pixels.</summary>
            public const int GaugeWidth = 30;

            /// <summary>Source X/Y of the filling bar in 7_Graph_Gauge.png (height is dynamic).</summary>
            public static readonly Vector2 GaugeSourceXY = new Vector2(2, 2);

            /// <summary>Bar height in pixels when Skill == 100.</summary>
            public const int GaugeMaxHeight = 434;

            /// <summary>Y of the bar bottom (= BackgroundPosition.Y + 477).</summary>
            public const int GaugeBaselineY = 527;

            /// <summary>Numeric label is drawn this many pixels above the bar top.</summary>
            public const int NumberOffsetFromTopOfBar = -10;

            /// <summary>"Current" label sprite source rect in 7_Graph_main.png.</summary>
            public static readonly Rectangle LabelSourceRect = new Rectangle(260, 2, 30, 120);

            /// <summary>Where the "current" label is drawn on screen.</summary>
            public static readonly Vector2 LabelPosition = new Vector2(945, 407);
        }

        #endregion
```

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs
git commit -m "feat: add PerformanceUILayout.SkillMeter constants

Layout numbers for the right-side vertical skill gauge, mirroring
the DTXManiaNX CActPerfSkillMeter drums geometry.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 7: Implement `SkillManager` (TDD)

**Files:**
- Create: `DTXMania.Test/Stage/Performance/SkillManagerTests.cs`
- Create: `DTXMania.Game/Lib/Stage/Performance/SkillManager.cs`

- [ ] **Step 1: Write the failing test file**

Create `DTXMania.Test/Stage/Performance/SkillManagerTests.cs` with:

```csharp
using System;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Tests for SkillManager. Verifies DTXManiaNX-faithful Playing Skill computation
    /// based on live judgement events and combo state.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SkillManagerTests
    {
        private static JudgementEvent JudgEvent(JudgementType type) =>
            new JudgementEvent(noteRef: 0, lane: 0, deltaMs: 0.0, type: type);

        #region Constructor

        [Fact]
        public void Constructor_ValidTotalNotes_ShouldInitializeZero()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            Assert.Equal(0.0, sm.CurrentSkill);
            Assert.False(sm.IsMax);
            Assert.Equal(0, sm.PerfectCount);
        }

        [Fact]
        public void Constructor_ZeroTotalNotes_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => new SkillManager(0, new ComboManager()));
        }

        [Fact]
        public void Constructor_NegativeTotalNotes_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => new SkillManager(-5, new ComboManager()));
        }

        [Fact]
        public void Constructor_NullComboManager_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new SkillManager(100, null!));
        }

        #endregion

        #region ProcessJudgement counts

        [Fact]
        public void ProcessJudgement_PerfectIncrementsPerfectCount()
        {
            var sm = new SkillManager(10, new ComboManager());
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            Assert.Equal(1, sm.PerfectCount);
            Assert.Equal(0, sm.GreatCount);
        }

        [Fact]
        public void ProcessJudgement_AllFiveTypesIncrementCorrectly()
        {
            var sm = new SkillManager(10, new ComboManager());
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Great));
            sm.ProcessJudgement(JudgEvent(JudgementType.Good));
            sm.ProcessJudgement(JudgEvent(JudgementType.Poor));
            sm.ProcessJudgement(JudgEvent(JudgementType.Miss));
            Assert.Equal(1, sm.PerfectCount);
            Assert.Equal(1, sm.GreatCount);
            Assert.Equal(1, sm.GoodCount);
            Assert.Equal(1, sm.PoorCount);
            Assert.Equal(1, sm.MissCount);
        }

        [Fact]
        public void ProcessJudgement_NullEvent_ShouldBeIgnored()
        {
            var sm = new SkillManager(10, new ComboManager());
            sm.ProcessJudgement(null!);
            Assert.Equal(0, sm.PerfectCount);
        }

        #endregion

        #region CurrentSkill formula

        /// <summary>
        /// All Perfect + full combo → 100. Combo is fed via the real ComboManager.
        /// </summary>
        [Fact]
        public void CurrentSkill_AllPerfectFullCombo_ShouldReach100()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            for (int i = 0; i < 100; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
                sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            }
            Assert.Equal(100.0, sm.CurrentSkill, 6);
            Assert.True(sm.IsMax);
        }

        [Fact]
        public void CurrentSkill_AllGreatFullCombo_ShouldReach50()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            for (int i = 0; i < 100; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Great));
                sm.ProcessJudgement(JudgEvent(JudgementType.Great));
            }
            Assert.Equal(50.0, sm.CurrentSkill, 6);
            Assert.False(sm.IsMax);
        }

        [Fact]
        public void CurrentSkill_HalfPerfectHalfMiss_WithPartialCombo_ShouldReturn49_85()
        {
            // Sequence: 1 Perfect, 1 Miss (combo break), 49 Perfect (combo 1→49, max=49), 49 Miss.
            // Final tally: PerfectCount=50, MissCount=50, MaxCombo=49.
            // Skill = 50*0.85 + 0 + 49*0.15 = 42.5 + 7.35 = 49.85.
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect)); // combo=1, max=1
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            combo.ProcessJudgement(JudgEvent(JudgementType.Miss));    // combo reset
            sm.ProcessJudgement(JudgEvent(JudgementType.Miss));
            for (int i = 0; i < 49; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
                sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            }
            for (int i = 0; i < 49; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Miss));
                sm.ProcessJudgement(JudgEvent(JudgementType.Miss));
            }
            Assert.Equal(49.85, sm.CurrentSkill, 4);
        }

        [Fact]
        public void CurrentSkill_NoJudgements_ShouldBeZero()
        {
            var sm = new SkillManager(100, new ComboManager());
            Assert.Equal(0.0, sm.CurrentSkill);
        }

        #endregion

        #region SkillChanged event

        [Fact]
        public void ProcessJudgement_ShouldRaiseSkillChanged()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            SkillChangedEventArgs? captured = null;
            sm.SkillChanged += (s, e) => captured = e;

            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));

            Assert.NotNull(captured);
            Assert.Equal(0.0, captured!.PreviousSkill);
            Assert.True(captured.CurrentSkill > 0.0);
            Assert.Equal(JudgementType.Perfect, captured.JudgementType);
        }

        #endregion

        #region Reset

        [Fact]
        public void Reset_ShouldClearCountsAndFireEvent()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));

            SkillChangedEventArgs? captured = null;
            sm.SkillChanged += (s, e) => captured = e;
            sm.Reset();

            Assert.Equal(0, sm.PerfectCount);
            Assert.Equal(0.0, sm.CurrentSkill);
            Assert.NotNull(captured);
            Assert.Equal(0.0, captured!.CurrentSkill);
        }

        #endregion
    }
}
```

- [ ] **Step 2: Run, verify build fails**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SkillManagerTests"
```

Expected: build failure with "The type or namespace name 'SkillManager' could not be found".

- [ ] **Step 3: Create `SkillManager.cs`**

Create `DTXMania.Game/Lib/Stage/Performance/SkillManager.cs`:

```csharp
#nullable enable

using System;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Computes DTXManiaNX-faithful Playing Skill (0.0-100.0) live during gameplay.
    /// Subscribes via PerformanceStage to JudgementManager events and reads MaxCombo
    /// from the injected ComboManager, so combo state is never duplicated here.
    /// Reference: DTXManiaNX CScoreIni.tCalculatePlayingSkill (Score,Song/CScoreIni.cs:1641).
    /// </summary>
    public class SkillManager : IDisposable
    {
        private readonly int _totalNotes;
        private readonly ComboManager _comboManager;
        private int _perfect;
        private int _great;
        private int _good;
        private int _poor;
        private int _miss;
        private double _currentSkill;
        private bool _disposed;

        public event EventHandler<SkillChangedEventArgs>? SkillChanged;

        public int  PerfectCount => _perfect;
        public int  GreatCount   => _great;
        public int  GoodCount    => _good;
        public int  PoorCount    => _poor;
        public int  MissCount    => _miss;
        public int  TotalNotes   => _totalNotes;
        public double CurrentSkill => _currentSkill;
        public bool IsMax => _currentSkill >= 100.0;

        public SkillManager(int totalNotes, ComboManager comboManager)
        {
            if (totalNotes <= 0)
                throw new ArgumentException("Total notes must be greater than 0", nameof(totalNotes));
            _totalNotes = totalNotes;
            _comboManager = comboManager ?? throw new ArgumentNullException(nameof(comboManager));
        }

        public void ProcessJudgement(JudgementEvent? judgementEvent)
        {
            if (_disposed || judgementEvent == null) return;

            switch (judgementEvent.Type)
            {
                case JudgementType.Perfect: _perfect++; break;
                case JudgementType.Great:   _great++;   break;
                case JudgementType.Good:    _good++;    break;
                case JudgementType.Poor:    _poor++;    break;
                case JudgementType.Miss:    _miss++;    break;
            }

            double previous = _currentSkill;
            _currentSkill = SongScore.CalculatePlayingSkill(
                _totalNotes, _perfect, _great, _comboManager.MaxCombo);

            SkillChanged?.Invoke(this, new SkillChangedEventArgs
            {
                PreviousSkill = previous,
                CurrentSkill  = _currentSkill,
                IsMax         = IsMax,
                JudgementType = judgementEvent.Type
            });
        }

        public void Reset()
        {
            if (_disposed) return;
            double previous = _currentSkill;
            _perfect = _great = _good = _poor = _miss = 0;
            _currentSkill = 0.0;
            SkillChanged?.Invoke(this, new SkillChangedEventArgs
            {
                PreviousSkill = previous,
                CurrentSkill  = 0.0,
                IsMax         = false,
                JudgementType = null
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing) _disposed = true;
        }
    }

    /// <summary>
    /// Event payload for SkillManager.SkillChanged.
    /// </summary>
    public class SkillChangedEventArgs : EventArgs
    {
        public double PreviousSkill { get; set; }
        public double CurrentSkill  { get; set; }
        public bool   IsMax         { get; set; }
        public JudgementType? JudgementType { get; set; }
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SkillManagerTests"
```

Expected: 14 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/SkillManager.cs DTXMania.Test/Stage/Performance/SkillManagerTests.cs
git commit -m "feat: add SkillManager for live Playing Skill computation

Subscribes to JudgementManager events via PerformanceStage and reads
MaxCombo from an injected ComboManager. Raises SkillChanged carrying
the new value plus IsMax flag, ready for display components to consume.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 8: Implement `SkillPanelDisplay` (left status panel)

**Files:**
- Create: `DTXMania.Test/Stage/Performance/SkillPanelDisplayLogicTests.cs`
- Create: `DTXMania.Game/Lib/Stage/Performance/SkillPanelDisplay.cs`

- [ ] **Step 1: Write the failing logic-test file**

Create `DTXMania.Test/Stage/Performance/SkillPanelDisplayLogicTests.cs` with:

```csharp
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Logic tests for SkillPanelDisplay formatting helpers.
    /// Mac-excluded because the type lives alongside graphics resources.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SkillPanelDisplayLogicTests
    {
        // Level encoding (DTXManiaNX-faithful):
        //   level <  100 → displayed = level/10 + levelDec/100  (e.g. 80+50 → 8.50)
        //   level >= 100 → displayed = level/100                (e.g. 850 → 8.50)
        [Theory]
        [InlineData(78,  0, "7.80")]
        [InlineData(80, 50, "8.50")]
        [InlineData(78, 33, "8.13")]
        [InlineData(50,  0, "5.00")]
        [InlineData(850, 0, "8.50")]
        [InlineData( 0,  0, "--")]
        public void FormatLevelText_ReturnsExpected(int level, int levelDec, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatLevelText(level, levelDec));
        }

        [Theory]
        [InlineData(  0.0,  "  0.00")]
        [InlineData( 87.42, " 87.42")]
        [InlineData(100.0, "100.00")]
        [InlineData( 50.5,  " 50.50")]
        public void FormatSkillText_ReturnsExpected(double skill, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatSkillText(skill));
        }
    }
}
```

- [ ] **Step 2: Run, verify build fails**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~SkillPanelDisplayLogicTests" 2>&1 | tail -10
```

(Note: full Windows project here because Mac excludes display logic tests — we'll register the new file in the Mac exclusion list in Task 16.)

Expected: build failure with "The type or namespace name 'SkillPanelDisplay' could not be found".

- [ ] **Step 3: Create `SkillPanelDisplay.cs`**

Create `DTXMania.Game/Lib/Stage/Performance/SkillPanelDisplay.cs`:

```csharp
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Renders the left-side performance status panel: difficulty level,
    /// current skill percentage, and (optionally) the MAX badge sprite.
    /// </summary>
    public class SkillPanelDisplay : IDisposable
    {
        private readonly IResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SongChart? _chart;
        private ManagedFont? _font;
        private ITexture? _maxBadgeTexture;
        private bool _disposed;

        private static readonly Color ShadowColor = new Color(0, 0, 0, 128);
        private static readonly Vector2 ShadowOffset = new Vector2(2, 2);

        public double Skill   { get; set; }
        public bool   ShowMax { get; set; }

        public SkillPanelDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice, SongChart? chart)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice  = graphicsDevice  ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _chart           = chart;

            _font = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", 24, FontStyle.Bold);
            try { _maxBadgeTexture = _resourceManager.LoadTexture(TexturePath.SkillMax); }
            catch { _maxBadgeTexture = null; }
        }

        public void Update(double deltaTime) { /* no animation yet */ }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _font == null) return;

            int level    = _chart?.DrumLevel    ?? 0;
            int levelDec = _chart?.DrumLevelDec ?? 0;

            string levelText = FormatLevelText(level, levelDec);
            _font.DrawStringWithShadow(spriteBatch, levelText,
                PerformanceUILayout.SkillPanel.LevelNumber.StartPosition,
                Color.White, ShadowColor, ShadowOffset);

            string skillText = FormatSkillText(Skill);
            _font.DrawStringWithShadow(spriteBatch, skillText,
                PerformanceUILayout.SkillPanel.SkillPercent.NumbersPosition,
                Color.White, ShadowColor, ShadowOffset);

            _font.DrawStringWithShadow(spriteBatch, "%",
                PerformanceUILayout.SkillPanel.SkillPercent.PercentPosition,
                Color.White, ShadowColor, ShadowOffset);

            if (ShowMax && _maxBadgeTexture != null)
            {
                _maxBadgeTexture.Draw(spriteBatch,
                    PerformanceUILayout.SkillPanel.SkillPercent.MaxBadgePosition);
            }
        }

        public static string FormatLevelText(int level, int levelDec)
        {
            if (level <= 0) return "--";
            // Match DTXManiaNX CScoreIni.tCalculateGameSkillFromPlayingSkill encoding:
            //   level >= 100 → displayed = level/100  (e.g. 850 → 8.50)
            //   level <  100 → displayed = level/10 + levelDec/100  (e.g. 80+50 → 8.50)
            double actual = level >= 100
                ? level / 100.0
                : (level / 10.0) + (levelDec / 100.0);
            return actual.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string FormatSkillText(double skill)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0,6:##0.00}", skill);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _font?.Dispose();
                _font = null;
                _maxBadgeTexture = null;  // not owned — IResourceManager handles refcount
            }
            _disposed = true;
        }
    }
}
```

- [ ] **Step 4: Verify expected level encoding**

Walk through the Theory cases mentally against the DTXManiaNX formula:
- `(78,  0)`: actual = 78/10 + 0/100 = 7.8 → `"7.80"`
- `(80, 50)`: actual = 80/10 + 50/100 = 8.5 → `"8.50"`
- `(78, 33)`: actual = 78/10 + 33/100 = 8.13 → `"8.13"`
- `(50,  0)`: actual = 5.0 → `"5.00"`
- `(850, 0)`: `level >= 100` branch → 850/100 = 8.5 → `"8.50"` (modern encoding)
- `(0,  0)`: level ≤ 0 → `"--"`

- [ ] **Step 5: Run tests, verify they pass (Windows project)**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~SkillPanelDisplayLogicTests"
```

Expected: 10 tests pass (6 level + 4 skill Theory rows).

Note: Mac project will skip these tests once Task 16 adds the exclusion. Until then the Mac build will compile the test file but its `Draw` codepath won't run (logic tests don't call `Draw`).

- [ ] **Step 6: Build Mac project to confirm compilation still succeeds**

Run:
```bash
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: `Build succeeded`. The class is referenced but `Draw`-time graphics paths aren't exercised by these logic tests.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/SkillPanelDisplay.cs DTXMania.Test/Stage/Performance/SkillPanelDisplayLogicTests.cs
git commit -m "feat: add SkillPanelDisplay left-side panel renderer

Renders difficulty level + live skill percentage + optional MAX
badge using existing PerformanceUILayout.SkillPanel positions.
Static FormatLevelText / FormatSkillText helpers are unit-tested
without constructing the graphics-bound class.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 9: Implement `SkillMeterDisplay` (right vertical gauge)

**Files:**
- Create: `DTXMania.Test/Stage/Performance/SkillMeterDisplayLogicTests.cs`
- Create: `DTXMania.Game/Lib/Stage/Performance/SkillMeterDisplay.cs`

- [ ] **Step 1: Write the failing logic-test file**

Create `DTXMania.Test/Stage/Performance/SkillMeterDisplayLogicTests.cs`:

```csharp
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Logic tests for SkillMeterDisplay math helpers.
    /// Mac-excluded because the type lives alongside graphics resources.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SkillMeterDisplayLogicTests
    {
        [Theory]
        [InlineData(  0.0,   0)]
        [InlineData( 50.0, 217)]
        [InlineData(100.0, 434)]
        [InlineData(150.0, 434)]   // clamped
        [InlineData( -5.0,   0)]   // clamped low
        public void ComputeGaugeHeight_ReturnsExpected(double skill, int expected)
        {
            Assert.Equal(expected, SkillMeterDisplay.ComputeGaugeHeight(skill));
        }

        [Fact]
        public void ComputeBarTopY_FullGauge_ShouldEqualBackgroundTop()
        {
            // GaugeBaselineY (527) - GaugeMaxHeight (434) = 93 (= BackgroundPosition.Y + 43)
            Assert.Equal(93, SkillMeterDisplay.ComputeBarTopY(100.0));
        }

        [Fact]
        public void ComputeBarTopY_EmptyGauge_ShouldEqualBaseline()
        {
            Assert.Equal(527, SkillMeterDisplay.ComputeBarTopY(0.0));
        }

        [Fact]
        public void ShouldDrawBar_ZeroSkill_ShouldBeFalse()
        {
            Assert.False(SkillMeterDisplay.ShouldDrawBar(0.0));
        }

        [Fact]
        public void ShouldDrawBar_AnyPositiveSkill_ShouldBeTrue()
        {
            Assert.True(SkillMeterDisplay.ShouldDrawBar(0.01));
            Assert.True(SkillMeterDisplay.ShouldDrawBar(100.0));
        }
    }
}
```

- [ ] **Step 2: Run, verify build fails**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~SkillMeterDisplayLogicTests" 2>&1 | tail -10
```

Expected: build failure with "The type or namespace name 'SkillMeterDisplay' could not be found".

- [ ] **Step 3: Create `SkillMeterDisplay.cs`**

Create `DTXMania.Game/Lib/Stage/Performance/SkillMeterDisplay.cs`:

```csharp
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Renders the right-side vertical skill gauge meter. Uses 7_Graph_main.png as
    /// background, 7_Graph_Gauge.png as the filling bar, and a small text overlay
    /// for the numeric value. Mirrors DTXManiaNX CActPerfSkillMeter (drums layout).
    /// </summary>
    public class SkillMeterDisplay : IDisposable
    {
        private readonly IResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private ITexture? _backgroundTexture;
        private ITexture? _gaugeTexture;
        private ManagedFont? _font;
        private bool _disposed;

        private static readonly Color ShadowColor = new Color(0, 0, 0, 128);
        private static readonly Vector2 ShadowOffset = new Vector2(1, 1);

        public double Skill { get; set; }

        public SkillMeterDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice  = graphicsDevice  ?? throw new ArgumentNullException(nameof(graphicsDevice));

            try { _backgroundTexture = _resourceManager.LoadTexture(TexturePath.PerfGraphMain); }
            catch { _backgroundTexture = null; }
            try { _gaugeTexture = _resourceManager.LoadTexture(TexturePath.PerfGraphGauge); }
            catch { _gaugeTexture = null; }

            _font = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", 12, FontStyle.Bold);
        }

        public void Update(double deltaTime) { /* no animation yet */ }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null) return;

            var bgPos = PerformanceUILayout.SkillMeter.BackgroundPosition;

            // 1. Background
            if (_backgroundTexture != null)
            {
                _backgroundTexture.Draw(spriteBatch, bgPos,
                    PerformanceUILayout.SkillMeter.BackgroundSourceRect);
            }

            // 2-4. Filling bar
            int gaugeHeight = ComputeGaugeHeight(Skill);
            int barTopY     = ComputeBarTopY(Skill);
            if (ShouldDrawBar(Skill) && _gaugeTexture != null)
            {
                var barPos = new Vector2(
                    bgPos.X + PerformanceUILayout.SkillMeter.GaugeOffset.X,
                    barTopY);
                var barSrc = new Rectangle(
                    (int)PerformanceUILayout.SkillMeter.GaugeSourceXY.X,
                    (int)PerformanceUILayout.SkillMeter.GaugeSourceXY.Y,
                    PerformanceUILayout.SkillMeter.GaugeWidth,
                    gaugeHeight);
                _gaugeTexture.Draw(spriteBatch, barPos, barSrc);
            }

            // 5. "Current" label
            if (_backgroundTexture != null)
            {
                _backgroundTexture.Draw(spriteBatch,
                    PerformanceUILayout.SkillMeter.LabelPosition,
                    PerformanceUILayout.SkillMeter.LabelSourceRect);
            }

            // 6. Numeric overlay
            if (_font != null)
            {
                string text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,5:##0.00}", Skill);
                var textPos = new Vector2(
                    bgPos.X + PerformanceUILayout.SkillMeter.GaugeOffset.X - 1,
                    barTopY + PerformanceUILayout.SkillMeter.NumberOffsetFromTopOfBar);
                _font.DrawStringWithShadow(spriteBatch, text, textPos,
                    Color.White, ShadowColor, ShadowOffset);
            }
        }

        public static int ComputeGaugeHeight(double skill)
        {
            double clamped = System.Math.Clamp(skill, 0.0, 100.0);
            return (int)(PerformanceUILayout.SkillMeter.GaugeMaxHeight * clamped / 100.0);
        }

        public static int ComputeBarTopY(double skill)
        {
            return PerformanceUILayout.SkillMeter.GaugeBaselineY - ComputeGaugeHeight(skill);
        }

        public static bool ShouldDrawBar(double skill) => skill > 0.0;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _font?.Dispose();
                _font = null;
                _backgroundTexture = null;
                _gaugeTexture = null;
            }
            _disposed = true;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify they pass (Windows project)**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~SkillMeterDisplayLogicTests"
```

Expected: 9 tests pass.

- [ ] **Step 5: Build Mac project to confirm compilation**

Run:
```bash
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/SkillMeterDisplay.cs DTXMania.Test/Stage/Performance/SkillMeterDisplayLogicTests.cs
git commit -m "feat: add SkillMeterDisplay right-side vertical gauge

Renders 7_Graph_main background + 7_Graph_Gauge filling bar + text
overlay. Static ComputeGaugeHeight / ComputeBarTopY / ShouldDrawBar
helpers are unit-tested without constructing the graphics-bound class.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 10: Extend `PerformanceSummary` with skill + chart-level fields

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceSummaryTests.cs`

- [ ] **Step 1: Add new fields to `PerformanceSummary`**

Open `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs`. After the existing `MissCount` field (around line 50), add:

```csharp
        /// <summary>
        /// Playing Skill computed at end of song (0.0-100.0). DTXManiaNX formula.
        /// </summary>
        public double PlayingSkill { get; set; }

        /// <summary>
        /// Game Skill computed at end of song (PlayingSkill · actualLevel · 0.2).
        /// </summary>
        public double GameSkill { get; set; }

        /// <summary>
        /// Raw chart level (e.g. 78 means 7.8). Carried through so the precise level
        /// reaches the persistence layer without rounding.
        /// </summary>
        public int ChartLevel { get; set; }

        /// <summary>
        /// Chart level decimal part (e.g. 33 means an extra 0.033). Combined with ChartLevel.
        /// </summary>
        public int ChartLevelDec { get; set; }
```

Locate the default-init lines in the existing constructor (around line ~120, after `MissCount = 0;`) and add:

```csharp
            PlayingSkill   = 0.0;
            GameSkill      = 0.0;
            ChartLevel     = 0;
            ChartLevelDec  = 0;
```

- [ ] **Step 2: Add a test for the new defaults**

Open `DTXMania.Test/Stage/Performance/PerformanceSummaryTests.cs`. Find an existing default-constructor test (e.g. `Constructor_ShouldInitializeWithDefaultValues`) and add new asserts in it, OR add a new test:

```csharp
        [Fact]
        public void Constructor_ShouldInitializeSkillFieldsToZero()
        {
            var summary = new PerformanceSummary();
            Assert.Equal(0.0, summary.PlayingSkill);
            Assert.Equal(0.0, summary.GameSkill);
            Assert.Equal(0, summary.ChartLevel);
            Assert.Equal(0, summary.ChartLevelDec);
        }
```

- [ ] **Step 3: Build + test**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceSummaryTests"
```

Expected: existing tests still pass plus the new one.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs DTXMania.Test/Stage/Performance/PerformanceSummaryTests.cs
git commit -m "feat: add PlayingSkill/GameSkill/ChartLevel fields to PerformanceSummary

Carries the per-play skill values and the chart's level + levelDec
through to the result/persistence layer, so end-of-song save can
record the precise DTXManiaNX-style Game Skill.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 11: Wire `SkillManager` + displays into `PerformanceStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`

- [ ] **Step 1: Add fields**

Open `DTXMania.Game/Lib/Stage/PerformanceStage.cs`. Find the existing `_scoreManager`/`_scoreDisplay` field declarations (around line 60-72) and add three new fields nearby:

```csharp
        private SkillManager _skillManager = null!;
        private SkillPanelDisplay _skillPanelDisplay = null!;
        private SkillMeterDisplay _skillMeterDisplay = null!;
```

- [ ] **Step 2: Construct the displays in the initialization region**

Find where `_scoreDisplay` is constructed (around line 358):

```csharp
            _scoreDisplay = new ScoreDisplay(_resourceManager, graphicsDevice);
            _comboDisplay = new ComboDisplay(_resourceManager, graphicsDevice);
```

Immediately after, add:

```csharp
            // Skill displays (SkillManager itself is constructed in InitializeGameplayManagers
            // because it needs ComboManager + ChartManager.TotalNotes which arrive later)
            var skillChart = _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
            _skillPanelDisplay = new SkillPanelDisplay(_resourceManager, graphicsDevice, skillChart);
            _skillMeterDisplay = new SkillMeterDisplay(_resourceManager, graphicsDevice);
```

- [ ] **Step 3: Construct `SkillManager` in `InitializeGameplayManagers`**

Find the existing code (around line 1069):

```csharp
            _scoreManager = new ScoreManager(_chartManager.TotalNotes);
            _comboManager = new ComboManager();
            _gaugeManager = new GaugeManager();
```

Add right after the `_gaugeManager` line:

```csharp
            _skillManager = new SkillManager(_chartManager.TotalNotes, _comboManager);
```

- [ ] **Step 4: Wire the event subscription in `WireUpEventHandlers`**

Find the section (around line 1098):

```csharp
            // Subscribe to manager events for UI updates
            _scoreManager.ScoreChanged += OnScoreChanged;
            _comboManager.ComboChanged += OnComboChanged;
            _gaugeManager.GaugeChanged += OnGaugeChanged;
            _gaugeManager.Failed += OnPlayerFailed;
```

Add:
```csharp
            _skillManager.SkillChanged += OnSkillChanged;
```

- [ ] **Step 5: Add `OnSkillChanged` handler**

After the existing `OnGaugeChanged` handler (search for `private void OnGaugeChanged`), add:

```csharp
        /// <summary>
        /// Handles skill changes and updates both display components.
        /// </summary>
        private void OnSkillChanged(object? sender, SkillChangedEventArgs e)
        {
            if (_skillPanelDisplay != null)
            {
                _skillPanelDisplay.Skill   = e.CurrentSkill;
                _skillPanelDisplay.ShowMax = e.IsMax;
            }
            if (_skillMeterDisplay != null)
            {
                _skillMeterDisplay.Skill = e.CurrentSkill;
            }
        }
```

- [ ] **Step 6: Forward judgement to `SkillManager` in `OnJudgementMade`**

Find `OnJudgementMade` (around line 1144). After the existing forwards (`_gaugeManager?.ProcessJudgement(e);`), add:

```csharp
            _skillManager?.ProcessJudgement(e);
```

- [ ] **Step 7: Call Update + Draw on the new components**

Find the existing `_scoreDisplay?.Update(deltaTime);` line (around line 1418). Add nearby:

```csharp
            _skillManager?.Update(0.0);          // currently no-op but kept for future animation hooks
            _skillPanelDisplay?.Update(deltaTime);
            _skillMeterDisplay?.Update(deltaTime);
```

Wait — `SkillManager` does not need an `Update` method. Remove the `_skillManager?.Update` line. Keep only:

```csharp
            _skillPanelDisplay?.Update(deltaTime);
            _skillMeterDisplay?.Update(deltaTime);
```

Find the existing `_scoreDisplay?.Draw(_spriteBatch);` line (around line 1544). Add nearby:

```csharp
            _skillPanelDisplay?.Draw(_spriteBatch);
            _skillMeterDisplay?.Draw(_spriteBatch);
```

- [ ] **Step 8: Add cleanup**

Find the existing `CleanupComponents` block where `_scoreDisplay?.Dispose()` is called (around line 404). Add:

```csharp
            _skillPanelDisplay?.Dispose();
            _skillPanelDisplay = null;
            _skillMeterDisplay?.Dispose();
            _skillMeterDisplay = null;
```

Find where `_scoreManager` is disposed/nulled (search for `_scoreManager = null;`). Add nearby:

```csharp
            _skillManager?.Dispose();
            _skillManager = null;
```

- [ ] **Step 9: Add symmetric event unsubscription**

Find `_scoreManager.ScoreChanged -= OnScoreChanged;` (around line 1370). Add:

```csharp
                if (_skillManager != null)
                    _skillManager.SkillChanged -= OnSkillChanged;
```

- [ ] **Step 10: Build + run full Mac test suite**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all tests pass. No new tests yet (Task 12 adds the integration test), but existing tests should be unaffected.

- [ ] **Step 11: Commit**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs
git commit -m "feat: wire SkillManager + skill displays into PerformanceStage

Constructs the manager and two display components alongside existing
score/combo/gauge counterparts. Subscribes to JudgementMade to drive
skill computation; OnSkillChanged pushes the value into both displays.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 12: Populate skill fields in `PerformanceSummary` at stage completion (TDD)

**Files:**
- Create: `DTXMania.Test/Stage/PerformanceStageSkillIntegrationTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`

- [ ] **Step 1: Write the failing integration test**

Create `DTXMania.Test/Stage/PerformanceStageSkillIntegrationTests.cs`:

```csharp
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Verifies PerformanceSummary skill fields are populated correctly when
    /// PerformanceStage builds it at completion. Done as a unit-level check by
    /// constructing the summary the same way PerformanceStage does, rather than
    /// running the full stage (which needs a graphics device).
    /// </summary>
    [Trait("Category", "Integration")]
    public class PerformanceStageSkillIntegrationTests
    {
        [Fact]
        public void BuildSummary_AllPerfect_ShouldPopulateMaxPlayingSkill()
        {
            // Simulate end-of-song state: 100 Perfect, full combo, on a chart with DrumLevel=78, DrumLevelDec=33 (actual level 8.13).
            int totalNotes = 100;
            int perfect    = 100;
            int great      = 0;
            int maxCombo   = 100;
            int level      = 78;
            int levelDec   = 33;

            double playing = SongScore.CalculatePlayingSkill(totalNotes, perfect, great, maxCombo);
            double game    = SongScore.CalculateGameSkill(playing, level, levelDec);

            var summary = new PerformanceSummary
            {
                TotalNotes    = totalNotes,
                PerfectCount  = perfect,
                MaxCombo      = maxCombo,
                PlayingSkill  = playing,
                GameSkill     = game,
                ChartLevel    = level,
                ChartLevelDec = levelDec
            };

            Assert.Equal(100.0, summary.PlayingSkill, 6);
            // actualLevel = 78/10 + 33/100 = 8.13; 100 * 8.13 * 0.2 = 162.6
            Assert.Equal(162.6, summary.GameSkill, 4);
            Assert.Equal(78, summary.ChartLevel);
            Assert.Equal(33, summary.ChartLevelDec);
        }

        [Fact]
        public void BuildSummary_NoHits_ShouldZeroSkill()
        {
            var summary = new PerformanceSummary
            {
                TotalNotes    = 100,
                PerfectCount  = 0,
                MaxCombo      = 0,
                PlayingSkill  = SongScore.CalculatePlayingSkill(100, 0, 0, 0),
                GameSkill     = SongScore.CalculateGameSkill(0.0, 78, 33),
                ChartLevel    = 78,
                ChartLevelDec = 33
            };

            Assert.Equal(0.0, summary.PlayingSkill);
            Assert.Equal(0.0, summary.GameSkill);
        }
    }
}
```

- [ ] **Step 2: Verify the integration test passes (it uses public API only)**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~PerformanceStageSkillIntegrationTests"
```

Expected: 2 tests pass (the test only exercises public API; nothing needs to change in production code yet — that's intentional, this test guards the *contract* the production code in Step 3 must honor).

- [ ] **Step 3: Modify `PerformanceStage.OnStageCompleted` to populate the new fields**

In `DTXMania.Game/Lib/Stage/PerformanceStage.cs`, find the existing `_performanceSummary = new PerformanceSummary { ... }` (around line 1733). Update the object initializer to add the new properties:

Locate:
```csharp
            _performanceSummary = new PerformanceSummary
            {
                Score = _scoreManager?.CurrentScore ?? 0,
                MaxCombo = _comboManager?.MaxCombo ?? 0,
                ClearFlag = reason != CompletionReason.PlayerFailed,
                PerfectCount = _judgementManager?.GetJudgementCount(JudgementType.Perfect) ?? 0,
                GreatCount = _judgementManager?.GetJudgementCount(JudgementType.Great) ?? 0,
                GoodCount = _judgementManager?.GetJudgementCount(JudgementType.Good) ?? 0,
                PoorCount = _judgementManager?.GetJudgementCount(JudgementType.Poor) ?? 0,
                MissCount = _judgementManager?.GetJudgementCount(JudgementType.Miss) ?? 0,
                TotalNotes = _chartManager?.TotalNotes ?? 0,
                FinalLife = _gaugeManager?.CurrentLife ?? 0.0f,
                CompletionReason = reason
            };
```

Insert *before* this block:
```csharp
            var summaryChart = _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
            int summaryLevel    = summaryChart?.DrumLevel    ?? 0;
            int summaryLevelDec = summaryChart?.DrumLevelDec ?? 0;
            double summaryPlayingSkill = _skillManager?.CurrentSkill ?? 0.0;
            double summaryGameSkill    = SongScore.CalculateGameSkill(
                summaryPlayingSkill, summaryLevel, summaryLevelDec);
```

Then add the new properties inside the initializer (before the closing `};`):
```csharp
                ,
                PlayingSkill  = summaryPlayingSkill,
                GameSkill     = summaryGameSkill,
                ChartLevel    = summaryLevel,
                ChartLevelDec = summaryLevelDec
```

(Adjust comma placement — if the initializer already has trailing commas the engineer should mimic the existing style.)

- [ ] **Step 4: Verify the namespace import in `PerformanceStage.cs`**

Ensure the file has:
```csharp
using DTXMania.Game.Lib.Song.Entities;
```

(Already present per earlier grep; verify and add if missing.)

- [ ] **Step 5: Build + run Mac suite**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all tests still pass.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Test/Stage/PerformanceStageSkillIntegrationTests.cs
git commit -m "feat: populate skill fields on PerformanceSummary at stage completion

PerformanceStage now reads CurrentSkill from SkillManager and the
chart level+decimal from the selected SongChart, computing GameSkill
via SongScore.CalculateGameSkill. The summary carries precise values
through to the result/persistence layer.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 13: Add `SongDatabaseService.UpdateScoreAsync(chartId, instrument, summary)` overload (TDD)

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Create: `DTXMania.Test/Song/SongDatabaseServiceSkillSaveTests.cs`

- [ ] **Step 1: Inspect the existing `SongDatabaseService` setup**

Run:
```bash
grep -n "class SongDatabaseService\|CreateContext\|public.*UpdateScoreAsync" DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs | head -10
```

Identify the existing `UpdateScoreAsync` (line 430). The new overload will be added immediately after it.

- [ ] **Step 2: Inspect the existing SQLite test pattern**

Existing pattern in `DTXMania.Test/Song/SongDbContextTests.cs` uses a shared `Microsoft.Data.Sqlite.SqliteConnection` opened once + `UseSqlite(connection)` so multiple `SongDbContext` instances see the same in-memory DB. The class implements `IDisposable` to clean up the connection, and avoids `using var` on EF-related disposables (coverlet bug — see the file's class-doc comment for details).

We'll add a test-only constructor to `SongDatabaseService` that accepts `DbContextOptions<SongDbContext>` directly, so the test doesn't need reflection.

- [ ] **Step 3: Add an internal test-friendly constructor to `SongDatabaseService.cs`**

Open `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`. Find the existing primary constructor (around line 31):

```csharp
        public SongDatabaseService(string databasePath = "songs.db")
```

Add a second constructor immediately above or below it:

```csharp
        /// <summary>
        /// Test-friendly constructor that accepts pre-built DbContext options.
        /// Lets unit tests inject an in-memory SQLite connection without going through
        /// the file-path-based config path.
        /// </summary>
        internal SongDatabaseService(DbContextOptions<SongDbContext> options)
        {
            _options = options ?? throw new System.ArgumentNullException(nameof(options));
        }
```

Add `[assembly: InternalsVisibleTo("DTXMania.Test")]` to the Game project if it doesn't already have it. Check:
```bash
grep -rn "InternalsVisibleTo" DTXMania.Game --include="*.cs"
```

If no result, create `DTXMania.Game/Properties/AssemblyInfo.cs` (or add to an existing one) with:
```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("DTXMania.Test")]
```

- [ ] **Step 4: Write the failing test file**

Create `DTXMania.Test/Song/SongDatabaseServiceSkillSaveTests.cs`:

```csharp
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for the SongDatabaseService.UpdateScoreAsync overload that takes a
    /// PerformanceSummary and persists score + skill values.
    /// Shared SqliteConnection lifecycle pattern mirrors SongDbContextTests
    /// (which has notes on coverlet "using var" quirks for EF disposables).
    /// </summary>
    [Trait("Category", "Integration")]
    public class SongDatabaseServiceSkillSaveTests : System.IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly SongDatabaseService _svc;

        public SongDatabaseServiceSkillSaveTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();

            _options = new DbContextOptionsBuilder<SongDbContext>().UseSqlite(_connection).Options;

            // EnsureCreated on a fresh context, disposed via try/finally to avoid coverlet bug.
            var setupCtx = new SongDbContext(_options);
            try { setupCtx.Database.EnsureCreated(); }
            finally { setupCtx.Dispose(); }

            _svc = new SongDatabaseService(_options);
        }

        public void Dispose() { _connection.Dispose(); }

        private async Task<SongChart> SeedChartAsync()
        {
            var ctx = new SongDbContext(_options);
            try
            {
                var song  = new SongEntity { Title = "Test Song" };
                var chart = new SongChart { Song = song, FilePath = "test.dtx", DrumLevel = 78, DrumLevelDec = 33 };
                ctx.SongCharts.Add(chart);
                await ctx.SaveChangesAsync();
                return chart;
            }
            finally { ctx.Dispose(); }
        }

        private async Task SeedScoreAsync(int chartId, SongScore? seed = null)
        {
            var ctx = new SongDbContext(_options);
            try
            {
                seed ??= new SongScore { ChartId = chartId, Instrument = EInstrumentPart.DRUMS };
                seed.ChartId = chartId;
                seed.Instrument = EInstrumentPart.DRUMS;
                ctx.SongScores.Add(seed);
                await ctx.SaveChangesAsync();
            }
            finally { ctx.Dispose(); }
        }

        private async Task<SongScore> LoadSavedScoreAsync(int chartId)
        {
            var ctx = new SongDbContext(_options);
            try
            {
                return await ctx.SongScores.AsNoTracking().FirstAsync(s => s.ChartId == chartId);
            }
            finally { ctx.Dispose(); }
        }

        [Fact]
        public async Task UpdateScoreAsync_WithSummary_PersistsBestSkill()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id);

            var summary = new PerformanceSummary
            {
                Score = 800000,
                MaxCombo = 100,
                ClearFlag = true,
                PerfectCount = 100, GreatCount = 0, GoodCount = 0, PoorCount = 0, MissCount = 0,
                TotalNotes = 100,
                PlayingSkill = 100.0,
                GameSkill = 162.6,  // 100 * (7.8 + 0.33) * 0.2
                ChartLevel = 78, ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(800000, saved.BestScore);
            Assert.Equal(100, saved.BestPerfect);
            Assert.Equal(100, saved.MaxCombo);
            Assert.True(saved.FullCombo);
            Assert.Equal(162.6, saved.HighSkill, 4);
            Assert.Equal(162.6, saved.SongSkill, 4);
            Assert.Equal(162.6, saved.LastSkillPoint, 4);
            Assert.Equal(1, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_LowerScore_KeepsExistingBestButUpdatesLast()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 900000, BestPerfect = 95, MaxCombo = 90,
                HighSkill = 170.0, SongSkill = 170.0, PlayCount = 5
            });

            var lowerSummary = new PerformanceSummary
            {
                Score = 500000, MaxCombo = 50, TotalNotes = 100,
                PerfectCount = 50, MissCount = 50,
                PlayingSkill = 50.0, GameSkill = 78.0,
                ChartLevel = 78, ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, lowerSummary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(900000, saved.BestScore);       // unchanged
            Assert.Equal(170.0, saved.HighSkill, 4);     // unchanged
            Assert.Equal(500000, saved.LastScore);       // updated
            Assert.Equal(78.0, saved.LastSkillPoint, 4); // updated
            Assert.Equal(6, saved.PlayCount);            // incremented
        }
    }
}
```

- [ ] **Step 5: Run, verify build fails on missing overload**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceSkillSaveTests"
```

Expected: build failure because the `UpdateScoreAsync(int, EInstrumentPart, PerformanceSummary)` overload doesn't exist yet.

- [ ] **Step 6: Add the new overload to `SongDatabaseService.cs`**

In `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`, after the existing `UpdateScoreAsync` (ending around line 458), add:

```csharp
        /// <summary>
        /// Persists a complete PerformanceSummary into the SongScore for the given chart+instrument.
        /// Updates best fields only when the new score exceeds the existing best. Always updates the
        /// "last play" fields and increments PlayCount. The pre-computed summary.GameSkill is assigned
        /// directly (not re-derived via score.CalculateSkill()), preserving level-decimal precision.
        /// </summary>
        public async Task UpdateScoreAsync(int chartId, EInstrumentPart instrument, PerformanceSummary summary)
        {
            if (summary == null) return;

            using var context = CreateContext();
            var score = await context.SongScores
                .FirstOrDefaultAsync(s => s.ChartId == chartId && s.Instrument == instrument);
            if (score == null) return;

            // Best-score update branch
            if (summary.Score > score.BestScore)
            {
                score.BestScore     = summary.Score;
                score.BestPerfect   = summary.PerfectCount;
                score.BestGreat     = summary.GreatCount;
                score.BestGood      = summary.GoodCount;
                score.BestPoor      = summary.PoorCount;
                score.BestMiss      = summary.MissCount;
                score.MaxCombo      = summary.MaxCombo;
                score.FullCombo     = summary.ClearFlag && summary.MissCount == 0 && summary.PoorCount == 0;
                score.TotalNotes    = summary.TotalNotes;
            }

            // High skill is independent of best score (a played chart might raise skill without raising score in edge cases).
            if (summary.GameSkill > score.HighSkill)
            {
                score.HighSkill = summary.GameSkill;
            }
            score.SongSkill = summary.GameSkill;

            // "Last play" fields always update
            score.LastScore      = summary.Score;
            score.LastSkillPoint = summary.GameSkill;
            score.LastPlayedAt   = System.DateTime.UtcNow;
            score.PlayCount++;
            if (summary.ClearFlag) score.ClearCount++;

            await context.SaveChangesAsync();
        }
```

- [ ] **Step 7: Re-run the new tests**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceSkillSaveTests"
```

Expected: 2 tests pass.

- [ ] **Step 8: Full test suite**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Test/Song/SongDatabaseServiceSkillSaveTests.cs
git commit -m "feat: SongDatabaseService.UpdateScoreAsync(PerformanceSummary) overload

Persists best-score + best-judgement-counts + max-combo when newScore
exceeds best, and always updates HighSkill (when greater) + last-play
fields. PlayCount/ClearCount are incremented atomically.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 14: Add the matching `SongManager.UpdateScoreAsync(summary)` overload

**Files:**
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs`

- [ ] **Step 1: Locate the existing overload**

Run:
```bash
grep -n "UpdateScoreAsync" DTXMania.Game/Lib/Song/SongManager.cs
```

Expected: the original at line ~1545.

- [ ] **Step 2: Add the matching overload**

In `DTXMania.Game/Lib/Song/SongManager.cs`, immediately after the existing `UpdateScoreAsync` method, add:

```csharp
        /// <summary>
        /// Forwarding wrapper around SongDatabaseService.UpdateScoreAsync(summary).
        /// </summary>
        public async Task<bool> UpdateScoreAsync(int chartId, EInstrumentPart instrument, DTXMania.Game.Lib.Stage.Performance.PerformanceSummary summary)
        {
            try
            {
                await _databaseService.UpdateScoreAsync(chartId, instrument, summary);
                return true;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongManager.UpdateScoreAsync(summary) failed: {ex.Message}");
                return false;
            }
        }
```

(If the existing method returns `Task<bool>`, mirror its shape; if it returns `Task`, drop the `return true/false` lines and the return type.)

- [ ] **Step 3: Build**

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Song/SongManager.cs
git commit -m "feat: SongManager.UpdateScoreAsync(PerformanceSummary) wrapper

Thin forwarding wrapper so ResultStage can save via the singleton
manager without depending on SongDatabaseService directly.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 15: Call `UpdateScoreAsync` from `ResultStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`

- [ ] **Step 1: Inspect how ResultStage gets song + chart context**

Run:
```bash
grep -n "_sharedData\|_performanceSummary\|ExtractSharedData\|OnActivate" DTXMania.Game/Lib/Stage/ResultStage.cs | head -20
```

Identify:
- where `ExtractSharedData` is called from (typically `OnActivate` or `OnFirstUpdate`),
- whether `selectedSong` is already extracted from shared data (it should be available because `PerformanceStage` passes it forward).

If `selectedSong` is not currently extracted, add an extraction line in `ExtractSharedData`:

```csharp
            if (_sharedData != null
                && _sharedData.TryGetValue("selectedSong", out var songObj)
                && songObj is DTXMania.Game.Lib.Song.SongListNode song)
            {
                _selectedSong = song;
            }
            if (_sharedData != null
                && _sharedData.TryGetValue("selectedDifficulty", out var difficultyObj)
                && difficultyObj is int difficulty)
            {
                _selectedDifficulty = difficulty;
            }
```

Also ensure `PerformanceStage.TransitionToResultStage` forwards these — open `PerformanceStage.cs`, find:

```csharp
            var sharedData = new Dictionary<string, object>
            {
                { "performanceSummary", _performanceSummary }
            };
```

Replace with:
```csharp
            var sharedData = new Dictionary<string, object>
            {
                { "performanceSummary", _performanceSummary }
            };
            if (_selectedSong != null) sharedData["selectedSong"] = _selectedSong;
            sharedData["selectedDifficulty"] = _selectedDifficulty;
```

- [ ] **Step 2: Add fields to ResultStage if missing**

If `ResultStage` doesn't already have `_selectedSong`/`_selectedDifficulty` fields, add:

```csharp
        private DTXMania.Game.Lib.Song.SongListNode? _selectedSong;
        private int _selectedDifficulty;
```

- [ ] **Step 3: Add the save call after `ExtractSharedData`**

`GetCurrentDifficultyChart` is an extension method on `SongListNode` (defined in `DTXMania.Game/Lib/Song/SongChartHelper.cs`) returning `SongChart` directly — chart ID is `chart.Id`. Make sure the `SongChartHelper` namespace is imported via `using DTXMania.Game.Lib.Song;`.

Find where `ExtractSharedData` is called (typically inside `OnActivate`). Right after the call, add:

```csharp
            // Persist this play's score + skill values (fire-and-forget).
            if (_selectedSong != null && _performanceSummary != null)
            {
                var savedChart = _selectedSong.GetCurrentDifficultyChart(_selectedDifficulty);
                if (savedChart != null && savedChart.Id > 0)
                {
                    _ = DTXMania.Game.Lib.Song.SongManager.Instance.UpdateScoreAsync(
                        savedChart.Id,
                        DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS,
                        _performanceSummary);
                }
            }
```

- [ ] **Step 4: Build + test**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ResultStage.cs DTXMania.Game/Lib/Stage/PerformanceStage.cs
git commit -m "feat: persist score+skill from ResultStage after each play

ResultStage forwards the PerformanceSummary into
SongManager.UpdateScoreAsync immediately after extracting shared
data. PerformanceStage now also forwards selectedSong/Difficulty
through the StageManager handoff so ResultStage can look up the
chart ID for the save.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 16: SongStatusPanel — show `"---"` for unplayed charts

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`

- [ ] **Step 1: Locate the skill point text formatting**

Open `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`, around line 859:

```csharp
            var skillValue = score.HighSkill > 0 ? score.HighSkill.ToString("F2") : "0.00";
```

- [ ] **Step 2: Replace with HasBeenPlayed gate**

Change the line to:

```csharp
            var skillValue = score.HasBeenPlayed ? score.HighSkill.ToString("F2") : "---";
```

- [ ] **Step 3: Build + test**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all tests pass. If `SongStatusPanelLogicTests` exists in the Windows project and asserts the `"0.00"` literal, update that assertion to `"---"`.

Run:
```bash
grep -rn "\"0\.00\"\|'0.00'" DTXMania.Test --include="*.cs"
```

If matches, update them.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs DTXMania.Test
git commit -m "polish: show \"---\" for unplayed charts on Song Selection panel

More honest UX than the previous fixed \"0.00\" string when no score
has been recorded yet for a chart.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 17: Update Mac test project exclusions

**Files:**
- Modify: `DTXMania.Test/DTXMania.Test.Mac.csproj`

- [ ] **Step 1: Locate the existing `<Compile Include>` line**

Open `DTXMania.Test/DTXMania.Test.Mac.csproj` and find (around line 33):

```xml
    <Compile Include="**/*.cs" Exclude="bin/**/*.cs;obj/**/*.cs;...existing list..." />
```

- [ ] **Step 2: Append the three new graphics-bound test files**

Inside the `Exclude` attribute string (semicolon-separated), append these three filenames:

```
Stage/Performance/SkillPanelDisplayLogicTests.cs;Stage/Performance/SkillMeterDisplayLogicTests.cs;Stage/PerformanceStageSkillIntegrationTests.cs
```

The final attribute should look like (existing tokens preserved):
```xml
    <Compile Include="**/*.cs" Exclude="bin/**/*.cs;obj/**/*.cs;Graphics/RenderTargetManagerTests.cs;Graphics/GPURenderingSnapshotTests.cs;Resources/BitmapFontTests.cs;Performance/StressTestRunner.cs;QA/ComprehensiveQATestSuite.cs;StressTestConsole/**/*.cs;UI/SongBarRendererTests.cs;UI/SongBarRendererLogicTests.cs;UI/SongStatusPanelTests.cs;UI/SongStatusPanelLogicTests.cs;Stage/Performance/PerformanceStageCleanupVerificationTests.cs;Stage/Performance/JudgementTextPopupTests.cs;Stage/Performance/PooledEffectsManagerTests.cs;Resources/ResourceManagerTests.cs;Helpers/MockPerformanceStage.cs;Helpers/TestGraphicsDeviceService.cs;Stage/Performance/SkillPanelDisplayLogicTests.cs;Stage/Performance/SkillMeterDisplayLogicTests.cs;Stage/PerformanceStageSkillIntegrationTests.cs" />
```

- [ ] **Step 3: Build Mac project**

Run:
```bash
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: `Build succeeded`. The three graphics-bound test files are now excluded from the Mac build but their logic-test classes still compile because the production classes they reference (`SkillPanelDisplay`, `SkillMeterDisplay`) compile in the main `DTXMania.Game` project.

Wait — re-read: the `Skill*DisplayLogicTests` tests use **static helpers** on the display classes, so they don't actually need GraphicsDevice. They should be Mac-safe.

**Re-evaluate:** Run the tests on Mac to confirm:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SkillPanelDisplayLogicTests|FullyQualifiedName~SkillMeterDisplayLogicTests"
```

If they pass on Mac without the exclusion, **remove them from the exclusion list** — only keep `Stage/PerformanceStageSkillIntegrationTests.cs` excluded (it constructs a `PerformanceSummary` only, but the file name pattern in the spec listed it for exclusion; verify by running and decide).

In short: exclude only what truly needs graphics. The final exclusion additions should be **whichever subset of the three actually fails on Mac**.

- [ ] **Step 4: Final Mac test run**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all tests pass on Mac.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "test: exclude graphics-bound skill tests from Mac project

Adds the new skill integration test (and any display logic tests
that fail on Mac) to the Mac exclusion list.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 18: Manual verification + final sweep

**Files:** none — verification only

- [ ] **Step 1: Run full test suite both projects**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: green.

If on a machine with the Windows project available:
```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

- [ ] **Step 2: Run the game and play a chart**

Run:
```bash
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

Manual checklist:
- Start the game, pick a chart with `DrumLevel > 0`, begin play.
- During play, observe the **left status panel** at screen (22, 250-585):
  - Difficulty level displays as `"X.YZ"`.
  - Skill percentage updates after each hit, in `XX.XX%` format.
  - Hitting only Perfects → percentage climbs toward 100; MAX badge appears when it reaches 100.
- During play, observe the **right vertical gauge** at screen (900, 50-587):
  - Background frame visible.
  - Bar grows upward as skill increases; full at skill=100.
  - Numeric text floats just above bar top.
- Finish or fail the chart, observe the Result screen briefly, then return to Song Selection.
- On Song Selection panel for the same chart: `score.HighSkill` should now show a non-zero `F2` value matching the just-played skill.

- [ ] **Step 3: Final grep for any leaked `Just` references**

Run:
```bash
grep -rn "JudgementType\.Just\b\|JustWindowMs\|JustScore\|JustCount" DTXMania.Game DTXMania.Test --include="*.cs"
```

Expected: empty output.

- [ ] **Step 4: Final grep for placeholder strings**

Run:
```bash
grep -rn "TODO\|FIXME\|XXX" DTXMania.Game/Lib/Stage/Performance/Skill*.cs DTXMania.Game/Lib/Song/Entities/SongScore.cs
```

Expected: no matches in the new code.

- [ ] **Step 5: All done**

The implementation is complete. The branch carries one commit per phase, ready for review.

---

## Notes for the engineer

- **TDD discipline**: every task that writes new production code is structured Test-first → Run-to-fail → Implement → Run-to-pass. Don't skip the "run to fail" step — it's how you know the test actually exercises the code path.
- **DRY**: `SkillManager.ProcessJudgement` calls `SongScore.CalculatePlayingSkill` instead of inlining the formula — there is one source of truth for the math.
- **YAGNI**: ghost data, target comparison, online stats formula, separate `BestPlayingSkill` persistence are all explicitly deferred. Don't add them.
- **No new abstractions beyond what the spec asks for**: don't introduce `ISkillCalculator` or similar — keep `SongScore` static helpers as the single source of truth.
- **Frequent commits**: every task ends with a `git commit`. If a task feels too large to commit at once, split it — but the task boundaries already do this for you.
- **If a step's grep returns unexpected matches**, stop and read the surrounding code before editing. Don't blind-replace tokens in strings or doc comments unless you've verified they're stale references.
