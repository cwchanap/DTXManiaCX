# HPA-15 Hit Timing Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional Drums setting that shows transient signed timing error for manual hits using the existing judgement delta and bundled lag-number sprite sheet.

**Architecture:** `PerformanceStage` remains the policy owner: it freezes the config flag and filters out Miss/AutoPlay judgements. `PerformanceUILayout.HitTimingFeedback` owns the pure rounding, glyph-packing, and lane-position contract. A small `HitTimingFeedbackDisplay` owns only one transient timing value per lane plus the `7_lag numbers.png` texture. Reuse existing config persistence, judgement presentation, and deterministic test seams; add no debug HUD or timing subsystem.

**Tech Stack:** C# / .NET 8, MonoGame, existing SQLite config store, xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-08-25-hpa-15-hit-timing-feedback-design.md`

## Global Constraints

- One HPA-15 PR only; implementation continues on this draft planning PR.
- Default `Hit Timing Feedback` is Off and existing gameplay visuals remain unchanged.
- Display only the existing `JudgementEvent.DeltaMs`; do not recalculate timing or apply latency compensation again.
- No timing feedback for `Miss` or AutoPlay-resolved lanes.
- No `ShowLagTimeColor`, aggregate counters, result statistics, debug HUD, logging controls, telemetry, judgement-window changes, or new art.
- Keep the setting frozen per performance activation; do not add a live config event.
- Keep config/AutoPlay eligibility in `PerformanceStage`; the display is presentation-only.
- Keep one timing value per lane; do not change the existing judgement-word stacking behavior.
- Do not add a generic popup/animation/rendering framework or a font fallback.

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
- Produces: `ConfigData.ShowHitTimingFeedback : bool`, default `false`
- Produces: `IConfigManager.SetShowHitTimingFeedback(bool value)`
- Persists: SQLite key `ShowHitTimingFeedback`
- UI label: `Hit Timing Feedback`, immediately after `Combo` in Drums config

- [ ] **Step 1: Add failing config-model and interface-surface tests**

Add the default assertion:

```csharp
Assert.False(new ConfigData().ShowHitTimingFeedback);
```

Extend the existing `ConfigManager_PerLaneAutoPlayMutators_ShouldBeExposedByInterface` reflection test with:

```csharp
Assert.NotNull(typeof(IConfigManager).GetMethod("SetShowHitTimingFeedback"));
```

This explicitly pins the interface contract; setter behavior tests alone do not prove the member was added to `IConfigManager`.

- [ ] **Step 2: Add failing setter and SQLite persistence coverage**

Mirror `ShowCombo` exactly:

- `false -> true` updates `Config.ShowHitTimingFeedback` and schedules a deferred save
- setting `true` again is a no-op
- persist `true`, reload through a fresh `ConfigManager`, assert `true`
- loading a snapshot without `ShowHitTimingFeedback` leaves the default `false`

- [ ] **Step 3: Add failing ConfigStage contract coverage**

Extend the exact Drums-item order so the visual section ends:

```text
Lane Display
Judge Line
Lane Flush
Combo
Hit Timing Feedback
```

Exercise the row and assert it dispatches through `SetShowHitTimingFeedback`.

Update `DrumConfigStageTests.StubConfigManager` only because the new interface member requires it to compile; do not add a second test double.

- [ ] **Step 4: Run focused config tests and confirm RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXMania.Test.Config|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: compile/test failures identify the missing property, interface setter, persistence key, and UI row.

- [ ] **Step 5: Implement the smallest config change**

Follow `ShowCombo` exactly:

- add `ShowHitTimingFeedback = false` to `ConfigData`
- add `SetShowHitTimingFeedback(bool)` to `IConfigManager` and `ConfigManager`
- parse the key only when it is a valid boolean
- add the key to `BuildPersistedEntries`
- setter returns when unchanged; otherwise assign and `MarkDirty()`
- add one `ToggleConfigItem` immediately after `Combo`

Do not add a migration, event, settings object, enum, color control, or compatibility alias.

- [ ] **Step 6: Re-run focused config tests and confirm GREEN**

Run the command from Step 4. Expected: PASS.

- [ ] **Step 7: Commit Task 1**

```bash
git add DTXMania.Game/Lib/Config DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Test/Config DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "feat: add hit timing feedback setting"
```

---

### Task 2: Define the lag-number projection and lane-local display

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- Modify: `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs`
- Create: `DTXMania.Game/Lib/Stage/Performance/HitTimingFeedbackDisplay.cs`
- Create: `DTXMania.Test/Stage/Performance/HitTimingFeedbackDisplayTests.cs`

**Interfaces:**
- `PerformanceUILayout.HitTimingFeedback` owns all fixed geometry and delta projection
- `HitTimingFeedbackDisplay.Spawn(JudgementEvent judgementEvent)`
- `HitTimingFeedbackDisplay.Update(double deltaTime)`
- `HitTimingFeedbackDisplay.Draw(SpriteBatch spriteBatch)`
- `HitTimingFeedbackDisplay : IDisposable`
- internal `HitTimingFeedbackDisplay.CreateForTesting(...)`
- internal active-lane count/read seam for deterministic stage tests

- [ ] **Step 1: Pin the exact 4×3 bank geometry in `PerformanceUILayoutMoreTests`**

Add `PerformanceUILayout.HitTimingFeedback` and first write tests for these constants:

```text
GlyphWidth            = 15
GlyphHeight           = 19
ColumnsPerBank        = 4
SlotsPerBank          = 12
FastBankOrigin        = 0,0
SlowBankOrigin        = 64,64
RequiredTextureWidth  = 128
RequiredTextureHeight = 128
PlusSlot              = 10
MinusSlot             = 11
```

Pin row-major source rectangles in both banks. At minimum assert slots `0`, `9`, `10`, and `11`:

```text
FAST slot 0  -> ( 0,  0, 15, 19)
FAST slot 9  -> (15, 38, 15, 19)
FAST slot 10 -> (30, 38, 15, 19)
FAST slot 11 -> (45, 38, 15, 19)

SLOW slot 0  -> ( 64,  64, 15, 19)
SLOW slot 9  -> ( 79, 102, 15, 19)
SLOW slot 10 -> ( 94, 102, 15, 19)
SLOW slot 11 -> (109, 102, 15, 19)
```

Also pin the wrap boundaries at slots 4 and 8 so nobody later changes the 4-column packing accidentally.

Production formula:

```text
sourceX = bankX + (slot % 4) * 15
sourceY = bankY + (slot / 4) * 19
```

Do not copy this formula into `HitTimingFeedbackDisplay`.

- [ ] **Step 2: Pin round-before-sign projection**

Add a pure projection helper under `PerformanceUILayout.HitTimingFeedback`. It may return a tiny nested readonly value such as `(Text, UseSlowBank)`; do not create a service/interface.

Use:

```csharp
var rounded = (int)Math.Round(deltaMs, MidpointRounding.AwayFromZero);
```

Then pin:

```text
-18.5 -> "-19", FAST
+24.5 -> "+25", SLOW
-0.4  -> "0",   FAST
+0.4  -> "0",   FAST
-0.5  -> "-1",  FAST
+0.5  -> "+1",  SLOW
```

The rounded value decides sign and color bank. Raw `DeltaMs < 0` must not produce a signed/FAST zero after rounding.

- [ ] **Step 3: Pin full-run lane positioning**

Add `GetLaneRunPosition(int laneIndex, int glyphCount)` to the same layout owner and test:

```text
x = GetLaneX(laneIndex) - (glyphCount * GlyphWidth) / 2f
y = SpriteJudgementTextAssets judgement Y + 34
```

Use the existing judgement-text vertical contract (`JudgementLineY - SpriteJudgementTextAssets.JudgementLineOffsetY`) for Y. Do not left-align the first glyph at lane center.

- [ ] **Step 4: Add failing display lifecycle/resource tests**

`HitTimingFeedbackDisplayTests` should pin only display ownership/lifecycle; geometry/projection stays in `PerformanceUILayoutMoreTests`.

Cover:

- one active slot per lane
- a new hit on the same lane replaces/restarts that slot
- different lanes coexist
- expiry uses `PerformanceUILayout.SpriteJudgementTextAssets.TotalDurationSeconds`
- the timing number simply fades; no pop/scale behavior
- draw uses the projected glyph sequence and `GetLaneRunPosition`
- missing/disposed/<128×128 texture is a safe no-op
- dispose releases the display's own texture reference once

Use a fixed `PerformanceUILayout.LaneCount`-sized state collection. Do not reuse `SpriteJudgementTextPopupManager`, whose list intentionally stacks judgement words.

- [ ] **Step 5: Run Task 2 tests and confirm RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~HitTimingFeedbackDisplayTests"
```

Expected: FAIL because the new layout contract/display do not exist yet.

- [ ] **Step 6: Implement the smallest layout/display code**

`PerformanceUILayout.HitTimingFeedback` is the single source for:

- 128×128 required size
- 15×19 glyphs
- 4×3 packing
- FAST/SLOW origins
- digit/plus/minus slots
- round-before-sign projection
- source rectangles
- run centering
- 34 px vertical offset
- shared short lifetime

`HitTimingFeedbackDisplay` should:

- load only `TexturePath.LagNumbers`
- reject/release an invalid texture and then remain a no-op; no retry framework
- keep one active state per lane
- call the layout projection/mapper rather than duplicate math
- fade/update/draw active states
- expose `CreateForTesting` plus an internal active-lane count/read seam
- release its texture on `Dispose`

It must not read config, inspect AutoPlay, recalculate timing, publish counters, or provide a font fallback.

- [ ] **Step 7: Re-run Task 2 tests and confirm GREEN**

Run the command from Step 5. Expected: PASS.

- [ ] **Step 8: Commit Task 2**

```bash
git add DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs DTXMania.Game/Lib/Stage/Performance/HitTimingFeedbackDisplay.cs DTXMania.Test/Stage/Performance/HitTimingFeedbackDisplayTests.cs
git commit -m "feat: render hit timing feedback"
```

---

### Task 3: Wire eligible manual judgements through `PerformanceStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

**Interfaces:**
- Consumes: `ConfigData.ShowHitTimingFeedback`
- Consumes: `FrozenAutoPlayLanes`
- Consumes: `HitTimingFeedbackDisplay`
- Eligibility: `_visualGates.ShowHitTimingFeedback && e.IsHit() && !FrozenAutoPlayLanes.Contains(e.Lane)`

- [ ] **Step 1: Add failing run-freeze coverage**

Extend the existing `FreezeRunConfiguration` characterization:

- config Off -> frozen timing flag Off
- config On -> frozen timing flag On
- changing config after freeze does not alter the current run

Add positive `ShowHitTimingFeedback` to `PerformanceVisualGates`. The all-zero/default record must continue to mean current CX visuals: no timing feedback.

- [ ] **Step 2: Add failing judgement-routing coverage using the real display test seam**

Create a valid mocked lag texture and build `HitTimingFeedbackDisplay.CreateForTesting(...)`. Inject it into `_hitTimingFeedbackDisplay`, just as current tests inject `_spriteJudgementTextPopupManager`.

Invoke `OnJudgementMade` through the existing reflection seam and assert the display's active-lane count/read state.

Pin this matrix:

| Toggle | Lane | Judgement | Timing display |
| --- | --- | --- | --- |
| Off | manual | Perfect | no |
| On | manual | Perfect/Great/Good/Poor | yes |
| On | manual | Miss | no |
| On | AutoPlay | Perfect | no |
| On | manual lane while another lane is AutoPlay | hit | yes |

Do not use `DeltaMs == 0` as an AutoPlay detector; `ResolveAutoHit` happens to emit zero but the frozen lane set is the policy source.

Keep current score/combo/gauge/skill/effect/pad/judgement-word assertions intact.

- [ ] **Step 3: Retarget the existing cleanup test before deleting `_lagNumbersTexture`**

`PerformanceStageDeterministicTests` currently injects `_lagNumbersTexture` and verifies `RemoveReference()` during `CleanupComponents`.

Change that coverage deliberately:

1. remove the reflection setup/assertion for the stale `_lagNumbersTexture` field
2. create `HitTimingFeedbackDisplay.CreateForTesting(lagTextureMock, ...)`
3. inject it into `_hitTimingFeedbackDisplay`
4. invoke `CleanupComponents`
5. verify `lagTextureMock.RemoveReference()` exactly once through display disposal

Do not simply delete the resource-ownership assertion.

- [ ] **Step 4: Run the performance namespace and confirm RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter FullyQualifiedName~DTXMania.Test.Stage.Performance
```

Expected: the new freeze/routing/cleanup assertions fail before stage integration.

- [ ] **Step 5: Integrate the display into the existing judgement presentation lifecycle**

Make only these production changes:

- add `_hitTimingFeedbackDisplay`
- construct it with the other gameplay presentation components
- update it immediately beside `_spriteJudgementTextPopupManager` / `_fontJudgementTextPopupManager` updates
- draw it inside `DrawJudgementTexts` beside the current judgement-word draw calls
- keep the existing alpha-blended base pass; do not create a new `SpriteBatch.Begin/End`
- dispose it with the other performance components
- freeze positive `ShowHitTimingFeedback` in `PerformanceVisualGates`
- inside `OnJudgementMade`, call `Spawn(e)` only for:

```csharp
_visualGates.ShowHitTimingFeedback
    && e.IsHit()
    && !FrozenAutoPlayLanes.Contains(e.Lane)
```

- remove the stale `_lagNumbersTexture` field/load/release that currently loads `TexturePath.LagIndicator`

Do not touch `JudgementManager`, `AudioLatencyOffsetMs`, score/combo/gauge/skill logic, AutoPlay resolution, result data, or telemetry.

- [ ] **Step 6: Re-run the performance namespace and confirm GREEN**

Run the command from Step 4. Expected: PASS.

- [ ] **Step 7: Run the full Mac test project**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS.

- [ ] **Step 8: Perform manual smoke**

Use a simple chart:

1. Verify default Off has no visual change.
2. Enable the toggle, start a new run, and intentionally hit early/late.
3. Confirm FAST/negative and SLOW/positive banks plus displayed magnitude are correct.
4. Confirm near-zero values that round to zero appear as unsigned `0`.
5. Confirm repeated same-lane hits replace the timing number while judgement words may overlap independently.
6. Confirm multi-glyph numbers are centered on the lane rather than starting at lane center.
7. Enable partial AutoPlay and confirm only manual lanes display timing feedback.
8. Restart and confirm the toggle persisted.

If the fixed 34 px offset visibly overlaps the default-skin judgement word, adjust only the layout constant and its geometry test; do not redesign either popup system.

- [ ] **Step 9: Commit Task 3**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
git commit -m "feat: show manual hit timing feedback"
```

---

## Final verification

Before marking the PR ready for review:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Confirm the final diff stays within HPA-15:

- one persisted boolean and one Drums row
- one pure lag-number layout/projection contract
- one small lane-local display component
- existing `PerformanceStage` judgement/update/draw/cleanup wiring
- focused config/layout/display/stage tests

No telemetry, debug/result timing statistics, `ShowLagTimeColor`, judgement-word rewrite, new sprite pass, or timing-rule changes should appear.
