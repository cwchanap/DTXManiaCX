# HPA-15 Hit Timing Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional Drums setting that shows transient manual-hit timing error using the existing judgement delta and a corrected release-safe CX Neon lag-number atlas.

**Architecture:** `PerformanceStage` owns policy: freeze the config flag and filter Miss/AutoPlay judgements. `PerformanceUILayout.HitTimingFeedback` owns pure projection/geometry. `HitTimingFeedbackDisplay` owns one transient timing value per lane and one reloadable lag-number texture. The existing CX Neon asset path is corrected before renderer work so development and release use the same atlas contract.

**Tech Stack:** C# / .NET 8, MonoGame, existing SQLite config store, xUnit + Moq, Python 3 + Pillow for skingen tests.

**Spec:** `docs/superpowers/specs/2026-08-25-hpa-15-hit-timing-feedback-design.md`

## Global Constraints

- One HPA-15 PR only; implementation continues on this draft planning PR.
- Keep the whole slice within 2–3 engineer days.
- Default `Hit Timing Feedback` is Off and current gameplay visuals remain unchanged.
- Display only existing `JudgementEvent.DeltaMs`; do not recalculate timing or apply latency compensation again.
- No feedback for `Miss` or AutoPlay-resolved lanes.
- Late values are unsigned; the SLOW color bank carries the positive direction. Do not add a plus glyph.
- Keep the setting frozen per performance activation; do not add a live config event.
- Keep config/AutoPlay eligibility in `PerformanceStage`; the display is presentation-only.
- Keep one timing value per lane; do not change judgement-word stacking.
- Correct the existing generated CX Neon lag atlas; do not add a new asset path or hand-authored art.
- Do not add `ShowLagTimeColor`, counters, result statistics, debug HUD, logging controls, telemetry, new timing windows, a popup framework, or a font fallback.
- Do not refactor existing texture owners into a shared reload framework in this PR; mirror the existing single-texture invalidation behavior locally.

---

### Task 0: Correct and pin the CX Neon lag-number atlas

**Why first:** releases do not ship repo-root `System/Graphics`; `.github/workflows/release.yml` stages `System/CXNeon/Graphics`. The current CX Neon `7_lag numbers.png` is 128×128 but uses a 5×2 digits-only layout, so the planned 4×3 source rectangles would render garbage while still passing a size check.

**Files:**
- Modify: `tools/skingen/generate_source.py`
- Modify: `tools/skingen/test_skingen.py`
- Regenerate: `tools/skingen/source/7_lag numbers.png`
- Regenerate: `System/CXNeon/Graphics/7_lag numbers.png`
- Verify only: `tools/skingen/manifest.json` remains the existing 128×128 authored-dimension/copy-recipe contract

**Atlas contract:**

```text
Canvas:          128 x 128
Glyph:           15 x 19
ColumnsPerBank:  4
SlotsPerBank:    12
FAST origin:     0,0
SLOW origin:     64,64
Digits:          slots 0..9
Slot 10:         reserved/unused
Minus:           slot 11
FAST styling:    CX Neon cyan
SLOW styling:    CX Neon danger/red
```

- [ ] **Step 1: Add a failing semantic atlas test**

Extend `tools/skingen/test_skingen.py` with a focused lag-atlas contract test. Use Pillow and pin both:

- generated source path: `tools/skingen/source/7_lag numbers.png`
- committed release artifact: `System/CXNeon/Graphics/7_lag numbers.png`

For each image assert:

- image size is exactly 128×128
- 4×3 cell math at `(0,0)` and `(64,64)`
- slots `0..9` and `11` contain non-transparent pixels in both banks
- both bank quadrants are non-empty and visibly distinct
- slot 10 is not treated as a plus contract

Do **not** use `System/Graphics/7_lag numbers.png` as release proof; that tree is explicitly excluded from release packaging.

- [ ] **Step 2: Run skingen tests and confirm RED**

Run:

```bash
python -m unittest discover -s tools/skingen -p "test_*.py" -v
```

Expected: the new contract fails against the current 5×2 CX Neon atlas.

- [ ] **Step 3: Replace the current lag-number generator recipe**

In `generate_source.py`, replace the 5-column / 25×60 digits-only drawing with one 128×128 atlas that draws the two required 4×3 banks.

Use existing palette constants:

- FAST bank: `CYAN`
- SLOW bank: `DANGER`

Render digits `0..9` and `-` in slot 11. Leave slot 10 unused; do not invent a plus glyph.

Keep `7_lag numbers.png` in `_ALWAYS_REGENERATE` so layout corrections cannot be masked by a stale source file.

- [ ] **Step 4: Regenerate only the affected source and pack artifact**

Run:

```bash
python tools/skingen/generate_source.py
python tools/skingen/skingen.py compose --only "7_lag numbers.png"
python tools/skingen/skingen.py validate
```

`generate_source.py` may touch its normal generated source inventory; before committing, ensure the actual diff is limited to the lag-number source/artifact plus intentional generator/test changes. Do not accept unrelated regenerated art.

The manifest already declares the authored image as 128×128. Do not add a second semantic-cell schema to `manifest.json`; the generator test and later `PerformanceUILayout` tests are the semantic packing contract.

- [ ] **Step 5: Re-run skingen tests and confirm GREEN**

Run:

```bash
python -m unittest discover -s tools/skingen -p "test_*.py" -v
python tools/skingen/skingen.py validate
```

Expected: PASS.

- [ ] **Step 6: Commit Task 0**

```bash
git add tools/skingen/generate_source.py tools/skingen/test_skingen.py \
  "tools/skingen/source/7_lag numbers.png" \
  "System/CXNeon/Graphics/7_lag numbers.png"
git commit -m "fix: align CX Neon lag number atlas"
```

---

### Task 1: Persist and expose the Drums toggle

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Modify only for interface compilation: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

**Interfaces:**
- `ConfigData.ShowHitTimingFeedback : bool`, default `false`
- `IConfigManager.SetShowHitTimingFeedback(bool value)`
- SQLite key `ShowHitTimingFeedback`
- UI row `Hit Timing Feedback`, immediately after `Combo`

- [ ] **Step 1: Add failing model/interface tests**

Pin:

```csharp
Assert.False(new ConfigData().ShowHitTimingFeedback);
Assert.NotNull(typeof(IConfigManager).GetMethod("SetShowHitTimingFeedback"));
```

Add the second assertion to the existing `ConfigManager_PerLaneAutoPlayMutators_ShouldBeExposedByInterface` surface test.

- [ ] **Step 2: Add failing setter/persistence tests**

Mirror `ShowCombo`:

- `false -> true` changes config and schedules deferred save
- setting the same value again is a no-op
- `true` round-trips through SQLite
- missing key leaves default `false`

- [ ] **Step 3: Add failing ConfigStage ordering/dispatch coverage**

Pin the visual rows as:

```text
Lane Display
Judge Line
Lane Flush
Combo
Hit Timing Feedback
```

Exercise the toggle and assert `SetShowHitTimingFeedback` is used.

Update only `DrumConfigStageTests.StubConfigManager` if the new interface member requires it to compile.

- [ ] **Step 4: Confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXMania.Test.Config|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

- [ ] **Step 5: Implement the smallest config change**

Follow `ShowCombo` exactly:

- add default property
- add interface/manager setter
- parse only valid booleans
- add persisted entry
- unchanged setter is no-op; changed setter calls `MarkDirty()`
- add one `ToggleConfigItem` after `Combo`

No migration, event, settings object, enum, color option, or compatibility alias.

- [ ] **Step 6: Confirm GREEN**

Re-run Step 4.

- [ ] **Step 7: Commit Task 1**

```bash
git add DTXMania.Game/Lib/Config DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Test/Config DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "feat: add hit timing feedback setting"
```

---

### Task 2: Define projection/geometry and add `HitTimingFeedbackDisplay`

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- Modify: `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs`
- Create: `DTXMania.Game/Lib/Stage/Performance/HitTimingFeedbackDisplay.cs`
- Create: `DTXMania.Test/Stage/Performance/HitTimingFeedbackDisplayTests.cs`

**Interfaces:**
- `PerformanceUILayout.HitTimingFeedback` owns fixed geometry and pure delta projection
- `HitTimingFeedbackDisplay.Spawn(JudgementEvent)`
- `HitTimingFeedbackDisplay.Update(double)`
- `HitTimingFeedbackDisplay.Draw(SpriteBatch)`
- `HitTimingFeedbackDisplay : IDisposable`
- internal `CreateForTesting(...)` + active-lane read seam

- [ ] **Step 1: Pin source-rectangle geometry and derived minimum bounds**

Add `PerformanceUILayout.HitTimingFeedback` with:

```text
GlyphWidth      = 15
GlyphHeight     = 19
ColumnsPerBank  = 4
SlotsPerBank    = 12
FastBankOrigin  = 0,0
SlowBankOrigin  = 64,64
Digit slots     = 0..9
MinusSlot       = 11
```

There is **no `PlusSlot`**.

Pin source rectangles for at least slots `0`, `9`, and `11` in both banks, plus wrap boundaries at slots `4` and `8`.

Production formula:

```text
sourceX = bankX + (slot % ColumnsPerBank) * GlyphWidth
sourceY = bankY + (slot / ColumnsPerBank) * GlyphHeight
```

Derive runtime minimum texture dimensions from the occupied source extents:

```text
RowsPerBank          = 3
RequiredTextureWidth = 64 + 4 * 15 = 124
RequiredTextureHeight= 64 + 3 * 19 = 121
```

Do not hardcode 128×128 as the runtime minimum. The shipped authored canvas is 128×128; the renderer only requires the cells it actually samples.

- [ ] **Step 2: Pin round-before-direction projection**

Use:

```csharp
var rounded = (int)Math.Round(deltaMs, MidpointRounding.AwayFromZero);
```

Pin:

```text
-18.5 -> "-19", FAST
+24.5 -> "25",  SLOW
-0.4  -> "0",   FAST
+0.4  -> "0",   FAST
-0.5  -> "-1",  FAST
+0.5  -> "1",   SLOW
```

The rounded value decides direction/bank. Positive values are unsigned; SLOW color carries the direction.

- [ ] **Step 3: Pin full-run lane positioning**

Add `GetLaneRunPosition(int laneIndex, int glyphCount)`:

```text
x = GetLaneX(laneIndex) - (glyphCount * GlyphWidth) / 2f
y = (JudgementLineY - SpriteJudgementTextAssets.JudgementLineOffsetY) + 34
```

- [ ] **Step 4: Add failing display lifecycle/bounds/resource tests**

Cover:

- fixed `LaneCount`-sized state
- same-lane hit replaces/restarts
- different lanes coexist
- out-of-range lanes (`< 0`, `>= LaneCount`) are ignored without throwing
- shared judgement duration; fade only, no pop/scale
- draw uses layout projection/source rectangles/run position
- initial missing/invalid/<derived-minimum texture is a safe no-op
- held texture invalidated mid-stage performs bounded reload behavior
- failed reload does not retry every frame
- successful reload permits a future invalidation episode
- dispose releases held texture once

Use current `ITexture` / `IResourceManager` mocks; no live `GraphicsDevice`.

- [ ] **Step 5: Confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~HitTimingFeedbackDisplayTests"
```

- [ ] **Step 6: Implement the smallest layout/display code**

`PerformanceUILayout.HitTimingFeedback` is the only owner of:

- 15×19 / 4×3 geometry
- FAST/SLOW origins
- derived required extents
- digits + minus mapping
- round-before-direction projection
- source rectangles
- run centering
- 34 px vertical offset
- shared short lifetime

`HitTimingFeedbackDisplay` should:

- load only `TexturePath.LagNumbers`
- retain the resource manager only for bounded invalidation recovery
- guard lane bounds before indexing state or calling layout helpers
- keep one active state per lane
- call layout projection/mapper instead of duplicating math
- fade/update/draw active states
- expose the narrow test factory/read seam
- release its texture on dispose

For texture invalidation, mirror the existing **single-texture** behavior from `SpriteJudgementTextPopupManager`: safe release, one retry per invalidation episode, reset retry guard after success, sticky failure so it does not reload every frame. Do not copy the font-fallback migration behavior.

Do **not** extract a shared texture helper or modify `SpriteJudgementTextPopupManager` / `NxAttackEffectManager` in HPA-15. Those owners have different single-vs-multi-texture/fallback semantics and are stable production code.

- [ ] **Step 7: Confirm GREEN**

Re-run Step 5.

- [ ] **Step 8: Commit Task 2**

```bash
git add DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs \
  DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs \
  DTXMania.Game/Lib/Stage/Performance/HitTimingFeedbackDisplay.cs \
  DTXMania.Test/Stage/Performance/HitTimingFeedbackDisplayTests.cs
git commit -m "feat: render hit timing feedback"
```

---

### Task 3: Wire eligible manual judgements through `PerformanceStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

**Interfaces:**
- `ConfigData.ShowHitTimingFeedback`
- `FrozenAutoPlayLanes`
- `HitTimingFeedbackDisplay`
- eligibility: `_visualGates.ShowHitTimingFeedback && e.IsHit() && !FrozenAutoPlayLanes.Contains(e.Lane)`

- [ ] **Step 1: Add failing run-freeze coverage**

Extend current `FreezeRunConfiguration` characterization:

- config Off -> frozen timing flag Off
- config On -> frozen timing flag On
- later config mutation does not change current run

Add positive `ShowHitTimingFeedback` to `PerformanceVisualGates`; the default/zero record must still mean current visuals.

- [ ] **Step 2: Add failing judgement-routing coverage with the real display seam**

Create `HitTimingFeedbackDisplay.CreateForTesting(...)`, inject it into `_hitTimingFeedbackDisplay`, invoke `OnJudgementMade`, and inspect active-lane state.

Pin:

| Toggle | Lane | Judgement | Timing display |
| --- | --- | --- | --- |
| Off | manual | Perfect | no |
| On | manual | Perfect/Great/Good/Poor | yes |
| On | manual | Miss | no |
| On | AutoPlay | Perfect | no |
| On | manual lane while another lane is AutoPlay | hit | yes |

Do not use `DeltaMs == 0` to detect AutoPlay.

Keep existing score/combo/gauge/skill/effect/pad/judgement-word assertions intact.

- [ ] **Step 3: Retarget the existing cleanup test before deleting `_lagNumbersTexture`**

Current cleanup coverage injects `_lagNumbersTexture` and verifies `RemoveReference()`.

Change it deliberately:

1. remove the stale field setup/assertion
2. create `HitTimingFeedbackDisplay.CreateForTesting(lagTextureMock, ...)`
3. inject it into `_hitTimingFeedbackDisplay`
4. call `CleanupComponents`
5. verify `lagTextureMock.RemoveReference()` once through display disposal

Do not delete resource-ownership coverage.

- [ ] **Step 4: Confirm RED**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter FullyQualifiedName~DTXMania.Test.Stage.Performance
```

- [ ] **Step 5: Integrate into the existing stage lifecycle**

Make only these stage changes:

- add `_hitTimingFeedbackDisplay`
- construct it with other presentation components
- update it beside sprite/font judgement popup updates
- draw it inside `DrawJudgementTexts`
- keep the existing alpha-blended base pass; no new `SpriteBatch.Begin/End`
- dispose it with other performance components
- freeze positive `ShowHitTimingFeedback`
- inside `OnJudgementMade`, spawn only for:

```csharp
_visualGates.ShowHitTimingFeedback
    && e.IsHit()
    && !FrozenAutoPlayLanes.Contains(e.Lane)
```

- remove stale `_lagNumbersTexture` field/load/release, which currently loads `TexturePath.LagIndicator`

Do not touch `JudgementManager`, `AudioLatencyOffsetMs`, scoring, combo/gauge/skill rules, AutoPlay resolution, results, or telemetry.

- [ ] **Step 6: Confirm performance namespace GREEN**

Re-run Step 4.

- [ ] **Step 7: Run full Mac tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

- [ ] **Step 8: Perform manual smoke**

Use a simple chart:

1. Default Off has no visual change.
2. Enable the toggle and start a new run.
3. Early hit => cyan/FAST with `-`; late hit => danger/red/SLOW with unsigned magnitude.
4. Near-zero rounded value => unsigned `0`.
5. Same-lane timing values replace; judgement words may stack independently.
6. Multi-glyph values stay centered on the lane.
7. Partial AutoPlay only suppresses automated lanes.
8. Restart and confirm SQLite persistence.
9. Exercise the normal alt-tab/fullscreen/device-reset smoke path and confirm timing feedback recovers rather than remaining dead.

If the 34 px offset overlaps default-skin judgement text, adjust only the layout constant and its test; do not redesign either popup system.

- [ ] **Step 9: Commit Task 3**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs \
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
git commit -m "feat: show manual hit timing feedback"
```

---

## Risks

### Release-pack drift

The dev/reference NX sheet and the shipped CX Neon sheet can differ while sharing dimensions. Task 0 must finish before Task 2, and the skingen test must inspect the committed `System/CXNeon` artifact.

### Dimension-only false confidence

A 128×128 image can still have wrong packing. The Python asset test pins real pixel occupancy/bank distinction; C# layout tests separately pin source rectangles and derived runtime bounds.

### Device-reset divergence

A permanently disabled timing display after texture invalidation would differ from existing judgement presentation. Mirror the existing single-texture retry semantics locally, but keep the broader refactor outside HPA-15.

---

## Final verification

Before marking the PR ready for review:

```bash
python -m unittest discover -s tools/skingen -p "test_*.py" -v
python tools/skingen/skingen.py validate
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Confirm the final diff stays within HPA-15:

- corrected existing CX Neon lag-number source + shipped artifact
- one persisted boolean and one Drums row
- one pure lag-number layout/projection contract
- one small lane-local display component with bounded texture recovery
- existing `PerformanceStage` judgement/update/draw/cleanup wiring
- focused asset/config/layout/display/stage tests

No plus glyph, telemetry, debug/result timing statistics, `ShowLagTimeColor`, judgement-word rewrite, new sprite pass, cross-component texture refactor, or timing-rule change should appear.
