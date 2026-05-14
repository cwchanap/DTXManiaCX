# Unit Test Coverage to 90% — Design

**Date:** 2026-05-14
**Status:** Approved (pending user review of spec)
**Target:** Raise line coverage of `DTXMania.Game.Mac` / `DTXMania.Game` from 81.92% to ≥ 90%.

## Goal & Success Criteria

- Raise line coverage on the unified game source to **≥ 90%** in both the Mac test suite (`DTXMania.Test.Mac.csproj`) and the Windows full suite (`DTXMania.Test.csproj`).
- **Windows full suite is the gating metric** (it has access to graphics-dependent tests excluded on Mac). Codecov is configured server-side and already gates PRs.
- Approximately **+3,606 net lines** need to move from "uncovered" into "covered" or "excluded".
- Branch coverage (currently 78.16%) is not gated but should rise as a side effect.

### Non-goals

- **No behavior changes.** All refactors are pure extractions: move code, do not rewrite logic. New tests must encode existing behavior, not a re-imagined version of it.
- No new coverage gates in `.csproj` or MSBuild. Codecov already enforces.
- Not a stylistic / dead-code sweep. We touch what we need to touch and stop.

## Baseline (Mac, most recent run)

- Lines: **36,570 / 44,640 covered (81.92%)**
- Branches: 6,793 / 8,691 (78.16%)
- Top files by missing-line count:

| File | Coverage | Missing | Notes |
|---|---:|---:|---|
| `Lib/Song/Components/SongStatusPanel.cs` | 21.9% | 1,276 | Tests excluded from Mac build |
| `Lib/Song/Components/SongBarRenderer.cs` | 25.1% | 502 | Tests excluded from Mac build |
| `Lib/Stage/PerformanceStage.cs` | 74.9% | 460 | Orchestration logic, lots of `Draw*` |
| `Lib/Stage/SongSelectionStage.cs` | 82.7% | 418 | Mostly draw paths |
| `Lib/Song/Components/SongListDisplay.cs` | 82.1% | 346 | |
| `Lib/Resources/ManagedFont.cs` | 75.0% | 310 | Draw + font caching |
| `Lib/Stage/SongTransitionStage.cs` | 71.9% | 284 | |
| `Lib/Stage/TitleStage.cs` | 67.2% | 282 | |
| `Lib/Stage/Performance/NoteRenderer.cs` | 63.7% | 278 | |
| `Lib/Stage/Performance/PooledEffectsManager.cs` | 4.0% | 238 | Tests excluded from Mac build |

A Windows coverage baseline must be obtained as the **first implementation action** to confirm starting position on the gating metric.

## Strategy: Three Pillars

The gap is closed by three pillars working together. Each is bounded by an explicit rule.

### Pillar 1 — Direct tests for testable logic

Add `Scenario_ShouldExpect` xUnit tests (with Moq) for code that is plain logic — parsers, managers, filter rules, layout math, state machines. No graphics seam needed.

**Primary targets** (and estimated added covered lines):

- `DTXChartParser.cs` — 90% → 95%+ (~60 lines)
- `SongManager.cs` — 94.8% → 98% (~60 lines)
- `ResourceManager.cs` — 86.8% → 95% (~80 lines)
- `JsonRpcServer.cs` — 91.9% → 96% (~50 lines)
- `ConfigStage.cs` non-Draw paths — 86.1% → 92% (~60 lines)
- `ModularInputManager.cs` — 86.1% → 94% (~40 lines)
- `SkinManager.cs` — 78.6% → 92% (~40 lines)
- `AppPaths.cs`, `DefaultGraphicsGenerator.cs`, `PreviewImagePanel.cs`, `SongDatabaseService.cs` — fill obvious gaps (~260 lines combined)

**Estimated total: +650 covered lines.**

### Pillar 2 — `[ExcludeFromCodeCoverage]` on genuinely graphics-bound code

Continue the existing convention (21 such attributes already in the codebase). Apply at the **method level only** — never at the class level.

**What qualifies for exclusion:**

- `Draw(...)` overrides that only call `SpriteBatch.Draw` / `_renderer.Draw` with no branching.
- `LoadContent` / `UnloadContent` methods that only invoke MonoGame loaders.
- Internal `DrawXxx` helpers on stages and components that purely emit graphics primitives.
- MonoGame plumbing such as resize callbacks that contain no decision logic.

**What does NOT qualify (must be tested instead, possibly after Pillar-3 extraction):**

- Any method containing a non-trivial conditional, loop, or state mutation.
- Methods that select between draw paths based on game state — extract the selector, exclude the tail.
- Methods that compute layout / coordinates — extract math into a logic class, test that.

**Each exclusion gets** `using System.Diagnostics.CodeAnalysis;` plus a one-line comment such as `/// <summary>Pure draw method; no logic to assert.</summary>`.

**Primary targets:** `Draw*` methods on `PerformanceStage`, `TitleStage`, `SongTransitionStage`, `ResultStage`, `NoteRenderer`, `GaugeDisplay`, `ComboDisplay`, `PadRenderer`, `EffectsManager`, `BitmapFont`, `ManagedFont`, `ManagedSpriteTexture`, plus `LoadContent` glue across stages.

**Estimated valid-line reduction: ~1,400 lines.** This effectively pushes the percentage up without writing tests for code that cannot be reasonably unit tested.

### Pillar 3 — Targeted extraction for worst offenders

For files where exclusion alone cannot reach 90% because real logic is intermixed with drawing, extract the logic into a sibling class that can be tested on Mac (no `GraphicsDevice` required).

**Each extraction is a pure move-and-call:** the new class's public API matches the methods it replaces, and the original file's behavior is unchanged.

| File | Extract to | What moves | Est. lines won |
|---|---|---|---:|
| `Lib/Song/Components/SongStatusPanel.cs` | `SongStatusPanelLogic` (sibling) | Layout calculation, score formatting, animation state, per-difficulty selection | ~900 |
| `Lib/Song/Components/SongBarRenderer.cs` | `SongBarRenderLogic` (sibling) | Bar layout, label truncation, color selection, scroll math | ~350 |
| `Lib/Stage/PerformanceStage.cs` | `PerformanceStageLoader`, `PerformanceStageEndFlow` (siblings) | Song-load pipeline, BGM scheduling, end-of-song flow | ~300 |
| `Lib/Stage/SongSelectionStage.cs` | (mostly Pillar 1/2; small inline extractions) | — | ~200 |

**Estimated total: +1,750 covered lines.**

**Process per extraction:**

1. Write characterization tests against the *new* class first (defining its expected API and behavior, copying behavior from the source).
2. Move the implementation into the new class.
3. Re-point the original site to call the new class.
4. Run the suite. If anything fails, the extraction is not pure — fix and re-verify.

## Estimated Arithmetic to 90%

Starting: 36,570 / 44,640 covered (81.92%). Target: ≥ 40,176 covered (90%).

| Pillar | Coverage gain (lines) |
|---|---:|
| P1 — direct tests | +650 |
| P2 — exclusions (denominator shrinks ~1,400) | ≈ +1,400 effective |
| P3 — extractions + tests | +1,750 |
| **Net effect** | **≈ +3,800** |

Math: 44,640 valid − 1,400 excluded = 43,240 valid. Need 0.9 × 43,240 = **38,916 covered**. We project ~38,970 covered, placing us at **~90.1%** with a small buffer for shortfall on any pillar.

## File & Test Conventions

- **Test file location:** `DTXMania.Test/<MirrorOfSourceDir>/<ClassName>Tests.cs` (existing pattern).
- **Test method naming:** `Scenario_ShouldExpect` (existing convention; enforced in recent commits).
- **xUnit traits:** Use `[Trait("Category", "Audio")]` etc. only where existing categories apply; do not invent new ones for this work.
- **Moq:** Mock interfaces (`IResourceManager`, `IConfigManager`, `IGraphicsManager`, etc.). Never construct `GraphicsDevice` in Mac tests.
- **Mac exclusion list:** Any new test that genuinely needs `GraphicsDevice` must be added to the `Compile Include` exclusion list in `DTXMania.Test/DTXMania.Test.Mac.csproj` — same pattern documented in CLAUDE.md.
- **Extracted logic classes:** live next to their source (`Lib/Song/Components/SongStatusPanelLogic.cs`) and share the same namespace.
- **Exclusion attribute:** `[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]` at the method level only, with a brief XML summary.

## Verification

1. Local Mac run before each commit:
   ```bash
   dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
     --collect:"XPlat Code Coverage" \
     --settings coverlet.runsettings \
     --results-directory ./TestResults
   ```
2. Generate a local report (e.g., `reportgenerator`) to confirm Mac is climbing toward 90% before push.
3. CI runs both Mac and Windows jobs; codecov reports both.
4. The Windows full suite is the gating metric. PR is not mergeable until Windows ≥ 90%.

## Risks & Mitigations

**Risk 1 — Extractions accidentally change behavior.**
*Mitigation:* Characterization tests written against the new class **before** the move. Pure move-and-call only. Any test failure after the move means the extraction is not pure; fix or revert.

**Risk 2 — Over-eager exclusion hides real testable logic.**
*Mitigation:* Hard rule — never exclude a method with non-trivial conditional, loop, or state mutation. Every exclusion gets a one-line comment explaining why. Reviewable as a list of attribute additions.

**Risk 3 — Single sweeping PR is hard to review.**
*Mitigation:* Organize commits by pillar × subsystem (`test:`, `refactor:`, etc., per Conventional Commits). Each commit stays focused so the PR is reviewable commit-by-commit even if it is large.

**Risk 4 — Windows baseline differs significantly from Mac.**
*Mitigation:* First implementation step is to obtain a Windows coverage baseline (from the latest GitHub Actions artifact, or by running locally on a Windows machine). If Windows already exceeds 90%, scope contracts; if it is lower than Mac, scope expands. The plan adapts based on this data.

**Risk 5 — `PerformanceStage` extraction is tangled with MonoGame update loop.**
*Mitigation:* Extract only the discrete, stateful pieces (song loader pipeline, end-of-song flow). Do not attempt to lift the entire `Update` method. If `PerformanceStageLoader` is too intertwined to extract cleanly, fall back to Pillar 2 (exclude its draw paths) and accept whatever coverage gain remains.

## Delivery

**Single sweeping PR**, with commits grouped by pillar and subsystem. Suggested commit sequence:

1. `chore: capture Windows coverage baseline` (artifact / notes)
2. `test: cover Resources/Config/JsonRpc gaps` (Pillar 1, low risk)
3. `refactor: extract SongStatusPanelLogic` + `test: cover SongStatusPanelLogic` (Pillar 3)
4. `refactor: extract SongBarRenderLogic` + `test: cover SongBarRenderLogic` (Pillar 3)
5. `refactor: extract PerformanceStage loader/end-flow` + `test: cover extracted helpers` (Pillar 3)
6. `test: cover Song/SongManager edge cases` (Pillar 1)
7. `test: cover Input/Modular paths` (Pillar 1)
8. `test: exclude pure draw methods across stages` (Pillar 2)
9. `test: exclude pure draw helpers in Performance components` (Pillar 2)
10. `test: exclude graphics-bound resource methods` (Pillar 2)
11. `chore: final coverage verification` (verify both suites ≥ 90%)

## Open Questions

- Do we want a coverage badge in the README updated as part of this work? (Not in scope unless requested.)
