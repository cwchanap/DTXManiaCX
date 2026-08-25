# HPA-15 Hit Timing Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional Drums setting that shows transient signed timing error for manual hits using the existing judgement delta and bundled lag-number sprite sheet.

**Architecture:** `PerformanceStage` remains the policy owner: it freezes the config flag and filters out Miss/AutoPlay judgements. A small `HitTimingFeedbackDisplay` owns only transient per-lane timing presentation and the `7_lag numbers.png` texture. Reuse existing config persistence, layout, resource ownership, and deterministic stage test seams; add no debug HUD or timing subsystem.

**Tech Stack:** C# / .NET 8, MonoGame, existing SQLite config store, xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-08-25-hpa-15-hit-timing-feedback-design.md`

## Global Constraints

- One HPA-15 PR only; implementation continues on this draft planning PR.
- Default `Hit Timing Feedback` is Off and existing gameplay visuals remain unchanged.
- Display only the existing `JudgementEvent.DeltaMs`; do not recalculate timing or apply latency compensation again.
- No timing feedback for `Miss` or AutoPlay-resolved lanes.
- No `ShowLagTimeColor`, aggregate counters, result statistics, debug HUD, logging controls, telemetry, or new art.
- Keep the setting frozen per performance activation; do not add a live config event.
- Keep the display component presentation-only; config and AutoPlay policy stay in `PerformanceStage`.
- Do not add a generic popup/animation/rendering framework.

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
- Modify only as required for interface compilation: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

**Interfaces:**
- Produces: `ConfigData.ShowHitTimingFeedback : bool`, default `false`
- Produces: `IConfigManager.SetShowHitTimingFeedback(bool value)`
- Persists: SQLite key `ShowHitTimingFeedback`
- UI label: `Hit Timing Feedback`, immediately after `Combo` in Drums config

- [ ] **Step 1: Add failing config-model and setter tests**

Pin these behaviors before production edits:

```csharp
Assert.False(new ConfigData().ShowHitTimingFeedback);
```

For `ConfigManager`, mirror the existing `ShowCombo` test pattern: changing `false -> true` updates `Config.ShowHitTimingFeedback` and schedules a deferred save; setting the same value again is a no-op.

- [ ] **Step 2: Add failing SQLite round-trip coverage**

Extend the existing config persistence test to write `ShowHitTimingFeedback=true`, reload through a fresh `ConfigManager`, and assert `true`. Also cover an input snapshot without the key and assert the default remains `false`.

- [ ] **Step 3: Add failing ConfigStage contract coverage**

Extend the exact Drums-item ordering assertion so the visual section ends:

```text
Lane Display
Judge Line
Lane Flush
Combo
Hit Timing Feedback
```

Exercise the toggle and assert the typed `SetShowHitTimingFeedback` path is used. Update only the hand-written `IConfigManager` stubs that fail compilation after adding the interface member.

- [ ] **Step 4: Run the focused config tests and confirm RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXMania.Test.Config|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: failures/compile errors identify the missing property, setter, persistence key, and UI row.

- [ ] **Step 5: Implement the smallest config change**

Follow the current `ShowCombo` pattern exactly:

- add `ShowHitTimingFeedback = false` to `ConfigData`
- add `SetShowHitTimingFeedback(bool)` to `IConfigManager` and `ConfigManager`
- parse `ShowHitTimingFeedback` only when it is a valid boolean
- add it to `BuildPersistedEntries`
- setter returns when unchanged, otherwise assigns and calls `MarkDirty()`
- add one `ToggleConfigItem` after `Combo`

Do not add a migration, event, settings object, enum, color control, or compatibility alias.

- [ ] **Step 6: Re-run focused config tests and confirm GREEN**

Run the command from Step 4. Expected: PASS.

- [ ] **Step 7: Commit Task 1**

```bash
git add DTXMania.Game/Lib/Config DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Test/Config DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "feat: add hit timing feedback setting"
```

---

### Task 2: Add the lane-local lag-number display

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Performance/HitTimingFeedbackDisplay.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- Create: `DTXMania.Test/Stage/Performance/HitTimingFeedbackDisplayTests.cs`

**Interfaces:**
- Consumes: `JudgementEvent.DeltaMs`, `JudgementEvent.Lane`, `TexturePath.LagNumbers`
- Produces: `HitTimingFeedbackDisplay.Spawn(JudgementEvent judgementEvent)`
- Produces: `HitTimingFeedbackDisplay.Update(double deltaTime)`
- Produces: `HitTimingFeedbackDisplay.Draw(SpriteBatch spriteBatch)`
- Produces: `IDisposable` resource cleanup
- Test seam: narrow internal factory/read access comparable to `SpriteJudgementTextPopupManager.CreateForTesting`

- [ ] **Step 1: Pin the NX lag-number geometry in layout tests/display tests**

Define a small `PerformanceUILayout.HitTimingFeedback` owner for:

```text
Glyph size:       15 x 19
Glyphs per bank:  12
FAST bank origin: 0,0
SLOW bank origin: 64,64
Digit slots:      0..9
Plus slot:        10
Minus slot:       11
Vertical offset:  sprite judgement Y + 34 px
Lifetime:         SpriteJudgementTextAssets.TotalDurationSeconds
```

Tests must assert source rectangles from these constants rather than duplicating the math in production and test code.

- [ ] **Step 2: Add failing formatting/direction tests**

Cover at least:

```text
-18.x ms -> rounded negative value, FAST bank
+24.x ms -> rounded positive value, SLOW bank
0 ms     -> unsigned zero glyph
```

Use normal midpoint-away-from-zero rounding so the presentation is deterministic. Keep the raw `JudgementEvent.DeltaMs` untouched.

- [ ] **Step 3: Add failing lifecycle and lane-ownership tests**

Pin these rules:

- at most one active value per lane
- a second hit on the same lane replaces/restarts that lane's value
- hits on different lanes coexist
- values expire after the shared judgement-popup lifetime
- draw position is centered on `PerformanceUILayout.GetLaneX(lane)`
- missing/invalid `LagNumbers` texture is a safe no-op
- dispose releases only the display's own texture reference

Do not require a live `GraphicsDevice`; use an `ITexture`/draw seam consistent with current performance presentation tests.

- [ ] **Step 4: Run the new display tests and confirm RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter FullyQualifiedName~HitTimingFeedbackDisplayTests
```

Expected: FAIL because the display/layout contract does not exist yet.

- [ ] **Step 5: Implement `HitTimingFeedbackDisplay`**

Keep the class narrow:

- load `TexturePath.LagNumbers` once
- keep a fixed lane-sized active-state collection rather than a general popup list
- round only for display
- map formatted characters to the fixed glyph slots/color bank
- center the glyph run on the lane
- fade using the existing judgement-popup total lifetime
- safely skip drawing when the texture is unavailable

The class must not read `ConfigData`, `FrozenAutoPlayLanes`, score state, timers, or telemetry.

- [ ] **Step 6: Re-run the new display tests and confirm GREEN**

Run the command from Step 4. Expected: PASS.

- [ ] **Step 7: Commit Task 2**

```bash
git add DTXMania.Game/Lib/Stage/Performance/HitTimingFeedbackDisplay.cs DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs DTXMania.Test/Stage/Performance/HitTimingFeedbackDisplayTests.cs
git commit -m "feat: render hit timing feedback"
```

---

### Task 3: Wire manual judgements through PerformanceStage

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify only if existing assertions require it: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`
- Modify only if existing reflection names/cleanup assertions require it: `DTXMania.Test/Stage/Performance/PerformanceStageCoverageTests.cs`

**Interfaces:**
- Consumes: `ConfigData.ShowHitTimingFeedback`
- Consumes: `FrozenAutoPlayLanes`
- Consumes: `HitTimingFeedbackDisplay`
- Eligibility: `ShowHitTimingFeedback && e.IsHit() && !FrozenAutoPlayLanes.Contains(e.Lane)`

- [ ] **Step 1: Add failing run-freeze coverage**

Extend the existing `FreezeRunConfiguration` characterization to assert:

- config Off -> frozen timing flag Off
- config On -> frozen timing flag On
- changing config after freeze does not alter the current run

Add `ShowHitTimingFeedback` as a positive field on `PerformanceVisualGates`; the default/zero record must still mean the current Off behavior for reflection-created stage tests.

- [ ] **Step 2: Add failing judgement-routing coverage**

Using the existing reflection/test seams around `OnJudgementMade`, pin this matrix:

| Toggle | Lane | Judgement | Timing display |
| --- | --- | --- | --- |
| Off | manual | Perfect | no |
| On | manual | Perfect/Great/Good/Poor | yes |
| On | manual | Miss | no |
| On | AutoPlay | Perfect | no |
| On | manual lane while another lane is AutoPlay | hit | yes |

Keep existing score/combo/gauge/skill assertions in place; timing feedback is additive presentation only.

- [ ] **Step 3: Run the focused performance tests and confirm RED**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter FullyQualifiedName~DTXMania.Test.Stage.Performance
```

Expected: new timing-feedback routing/freeze assertions fail before stage integration.

- [ ] **Step 4: Integrate the display with the existing stage lifecycle**

Make only these stage changes:

- construct `HitTimingFeedbackDisplay` with other gameplay presentation components
- update it from the existing component-update path
- draw it in the normal alpha-blended gameplay pass with judgement feedback
- dispose it with the other components
- freeze `ShowHitTimingFeedback` in `PerformanceVisualGates`
- spawn only for the eligibility predicate above inside `OnJudgementMade`
- remove the stale `_lagNumbersTexture` field/load/release that currently loads `TexturePath.LagIndicator`

Do not touch judgement calculation, `AudioLatencyOffsetMs`, `JudgementManager`, score/combo/gauge/skill logic, AutoPlay resolution, results, or telemetry.

- [ ] **Step 5: Re-run the performance namespace and confirm GREEN**

Run the command from Step 3. Expected: PASS.

- [ ] **Step 6: Run the full Mac test project**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS.

- [ ] **Step 7: Perform manual smoke**

Use a simple chart:

1. Verify default Off has no visual change.
2. Enable the toggle, start a new run, and intentionally hit early/late.
3. Confirm direction/color and displayed magnitude look correct.
4. Confirm repeated same-lane hits replace rather than stack.
5. Enable partial AutoPlay and confirm only manual lanes display timing feedback.
6. Restart and confirm the toggle persisted.

If the fixed 34 px offset causes obvious overlap with existing judgement text on the default skin, adjust only the `PerformanceUILayout.HitTimingFeedback` vertical constant and its geometry test; do not redesign the renderer.

- [ ] **Step 8: Commit Task 3**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Test/Stage/Performance
git commit -m "feat: show manual hit timing feedback"
```

---

## Final verification

Before marking the PR ready for review:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Confirm the final diff stays within HPA-15: one persisted boolean, one Drums row, one small display component, layout constants, stage wiring, and focused tests. No telemetry/debug/result/timing-rule changes should appear.
