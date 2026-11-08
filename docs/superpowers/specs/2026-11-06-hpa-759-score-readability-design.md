# HPA-759 — Score consistency and CX Neon readability design

**Linear:** https://linear.app/cwchanap/issue/HPA-759/fix-playtest-score-inconsistencies-and-cx-neon-readability  
**Delivery:** one implementation PR, ~1.5 engineer days, hard cap 3; design/docs only until review. Production fixes and tests go onto this **same PR**.
**Art dependency:** [HPA-794 — generate F rank badge](https://linear.app/cwchanap/issue/HPA-794/art-generate-cx-neon-f-rank-badge-for-result) is a **separate art ticket/PR** that must supply the asset before this implementation is complete.

## Goal and current baseline

Fix discrepancies reproduced in the 2026-11-04 native player playtest: Result E versus saved-history F for the same low-achievement run, Result skill 0.35 versus Song Select 0.34 for a skill of 0.345, and dark-on-dark Config text under CX Neon. Preserve score storage, gameplay calculations, and the player journey.

Rechecked current `main` on 2026-11-06:
- `ResultScreenModel.ComputeRank` has **7** bands (SS>=95, S>=80, A>=73, B>=63, C>=53, D>=45, else E).
- `SongScore.NormalizeRankPercentage`, `ComputeRankIndex` and `RankString` already express **8** persisted rank bands (SS>=95, S>=90, A>=80, B>=70, C>=60, D>=50, E>=40, else F); `SongDatabaseService` records the current-run history label using this policy. Existing Song Select rank icons likewise include F.
- `ResultScreenRenderer` and `TexturePath` have Result rank art **only for SS through E**. Its unknown-rank fallback currently loads E art.
- The Result uses custom `"0.00"` formatting while Song Select's HighSkill and the saved-history line use standard `"F2"`. **The format strings are the discrepancy, not SQLite**: SQLite REAL preserves the double. On .NET 10, 0.345 formats as 0.35 with `"0.00"` but 0.34 with `"F2"`.
- `ConfigStage` still hard-codes `SelectedMenuText` and `DescriptionTitleText` for light NX texture cells. It already resolves item value colors from `ISkinTheme`. `System/CXNeon/Theme.ini` contains a light `UI.TextPrimary`.
- **HPA-722 is Done:** `DrumConfigRenderer` now draws Drum Mapping with `SkinPalette` text on dark panels instead of the old `DrumConfigStage` dark-text instruction. HPA-722 intentionally changed the product caption to **MIDI**, superseding HPA-520's earlier HDMI wording. Treat this as the latest approved product decision, not a new HPA-759 change.

## Decision 1 — One 8-band grade policy

**Choose the existing CX persisted SongScore policy** for all newly computed run ranks. It is already the storage/history and Song Select contract. **This deliberately changes Result grading away from DTXManiaNX**, whose original seven-band policy (SS>=95, S>=80, A>=73, B>=63, C>=53, D>=45, else E) is currently used by `ResultScreenModel.ComputeRank` and enforced by `Create_ShouldComputeNXRankFromPlayingSkill`. The eight-band SS–F policy is CX-specific; this visible change is accepted to make Result agree with CX persistence/history/filter/badges. Do not introduce a competing table or migrate data.

| Playing skill | Grade | Stored bucket |
| --- | --- | --- |
| >=95 | SS | 95 |
| >=90 and <95 | S | 90 |
| >=80 and <90 | A | 80 |
| >=70 and <80 | B | 70 |
| >=60 and <70 | C | 60 |
| >=50 and <60 | D | 50 |
| >=40 and <50 | E | 40 |
| <40 | F | 0 |

**Visible Result regrades after adopting CX policy (NX/current Result → CX/new Result):**

| Playing skill range | Before | After |
| --- | --- | --- |
| 80–<90 | S | A |
| 73–<80 | A | B |
| 63–<70 | B | C |
| 53–<60 | C | D |
| 45–<50 | D | E |
| <40 | E | F |

Other intervals keep the same letter. **NX-imported ranks are not retroactively regraded:** e.g. an imported NX S with an 85% achievement can remain stored as CX S/bucket 90, while a *new* 85% CX run displays A. This is a known and accepted legacy-vs-new-run discrepancy; fixing old imports is out of scope.

Introduce one small `SongScore.RunRankBucket(double playingSkill)` that owns floor-to-integer and uses existing `NormalizeRankPercentage`; call it for the Result `SongScore.ComputeRankIndex`, the new-run BestRank update, and history `RankString`. Never duplicate the floor conversion across three callers. Extend the presentation `ResultRank` enum with F (index 7), and render the same letter in Result and the *current-run* saved-history line. Update/rename the NX-specific Result rank test to assert the CX contract.

**F artwork contract:** no F Result sprite is currently shipped, but the existing deterministic `tools/skingen/generate_source.py` already generates the SS–E shield badges through `rank_badge`. [HPA-794](https://linear.app/cwchanap/issue/HPA-794/art-generate-cx-neon-f-rank-badge-for-result) owns adding **one F source/manifest/generated badge** in a separate art PR. This game-code PR consumes that artifact by adding `TexturePath.ResultRankF`, mapping `GetRankTexturePath(ResultRank.F)` to it and `GetRankBackgroundPath(ResultRank.F)` to existing E background. Remove misleading unknown-rank `_ => E` badge/background fallbacks; do not silently draw E for an invalid value. SS–E drawing/reveal stays unchanged. **No custom F glyph renderer or new runtime font path**. The release flattens `System/CXNeon/Graphics` into bundled base `System/Graphics`; do not rely on the development-only NX pack. Verify the badge in default and CX Neon with real screenshots.

**Persistence and history:** keep `SongScore.BestRank` bucket values, `NormalizeStoredBestRank`, NX import mapping, rank multipliers, schema, and historical rows unchanged. Old/imported best rank ordinals have known ambiguous semantics (notably stored zero); **do not reinterpret or rewrite them here**. New result and new history line must agree for a single run. The Song Select badge represents the **best** run, so it may legitimately differ from the most recent Result if an older run was better. Never force the best rank down to match the latest run. Failed/cleared status and Result plate remain independent of grade.

## Decision 2 — One shared two-decimal skill presentation policy

Introduce a tiny pure `SkillValueFormatter.Format(double)` next to `Lib/Song/ChartLevelFormatter.cs`, reusing Result's existing presentation rule: `Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture)`. This preserves the existing .NET custom-format midpoint behavior without any `decimal` conversion or change to raw `double` skill values. `"0.00"` and `"F2"` may produce different halfway results for the *same double*, even with no persistence involved.

Use this one formatter in **three** places: `ResultScreenModel.FormatDecimal`, `SongStatusPanel`'s HighSkill text, and `SongDatabaseService`'s new-run history line (replace its inline `{4:F2}` with already formatted text). For Song Select, extract a small `internal static` text resolver that handles `SongScore.HasBeenPlayed` and returns `"---"` for unplayed charts; keep its draw method GPU-only. Test that resolver in a Mac-included test file (e.g. `SongStatusPanelSkillTextTests.cs`), **not** excluded `SongStatusPanelLogicTests.cs`.

Required fixtures: 0.345 → 0.35, 1.005 → 1.01, 2.675 → 2.68, 0, an ordinary value, and invariant locale; unplayed remains `"---"`. The SQLite round-trip is **not a cause of this bug** and does not need a new dedicated test. Distinguish current-run GameSkill on Result from historical best HighSkill on Song Select; compare displays only when the underlying values match. Preserve percent/level formatting and calculations.

## Decision 3 — Theme-aware Config foregrounds, no re-skin

Reuse existing `ISkinTheme.GetColor` with two narrowly scoped resolvers in `ConfigStage`:
- Selected category text: `Config.SelectedMenuText` -> `UI.Accent` -> current `SelectedMenuText` constant. The selected state resolves through the accent step, matching `ResolveValueColor`'s selected branch and CX Neon's use of accent to mark the live element.
- Description title: `Config.DescriptionTitleText` -> `UI.TextPrimary` -> current `DescriptionTitleText` constant.

Use each resolver at the existing draw sites, with `_resourceManager.CurrentTheme ?? SkinTheme.Empty`. CX Neon already supplies a light `UI.TextPrimary`; themeless/light NX retains the dark historical colors. Explicit skin keys allow other skins to override those surfaces. Do not change Config layout or item value colors. Test empty theme, generic Neon palette, and explicit overrides. Inspect actual contrast on CX Neon and default NX/light skin: theoretical color tests do not prove readability.

For Drum Mapping, the new HPA-722 renderer uses palette-driven text. **Verify** its header, instructions, selection and status on both skins; only make a local color correction if the new screen still reproduces a genuine issue. No popup/focus/layout rewrite or new image art.

## Risk and product decision

The chief risk is **visible Result regrading**, not schema migration. Players accustomed to NX may see a lower letter for the same achievement. The current CX score history and badge already use eight grades; convergence reduces user confusion within CX at the cost of deliberate NX parity. Verify exact boundaries and include before/after evidence in the implementation PR. Keep imported best-rank labels as stored; do not attempt an ambiguous historical migration.

## Product/UX observations recorded but intentionally deferred

- Song Select returning to All Songs instead of originating Bookmarks/folder is current behavior, not a confirmed regression. Defer changing return navigation context.
- Tab/B shortcut discoverability, clickable tab labels, and two-step Enter launch feedback are product improvements; do not bundle them in this bugfix.
- HPA-520 previously requested “HDMI device”; the newer approved HPA-722 specifies **MIDI**, which the new renderer uses. Retain HPA-722's decision; do not silently revert the caption or change device discovery.

These choices satisfy HPA-759's requirement to record decisions without expanding an already multi-defect ticket.

## Acceptance and proof

1. Every rank boundary (exact, immediately below, including 0/40/50/60/70/80/90/95) yields matching Result rank text, current-run saved history text, and expected bucket for new runs. Result **F** displays F, not E. Existing best-rank and NX import tests continue to pass.
2. The **same** 0.345 GameSkill displays **0.35** on Result and (when it is also the best run) **0.35** on Song Select; saved-history PlayingSkill uses the same formatter. The numeric score persisted in SQLite is unchanged.
3. Config description title and selected category remain readable on CX Neon and the light/default skin. HPA-722 Drum Mapping remains legible.
4. After HPA-794's F badge is available, repeat one failed low-score and one completed No Fail run through Result -> saved history -> Song Select; confirm grades, skill, save status, and no crashes. Capture screenshots of the **actual F badge** on Result, Config/Drum Mapping on both skins, and compare to player-facing text.
5. Run focused grade/formatter/theme tests **including Mac-safe HighSkill display and run-rank helper tests**, the applicable game/test suites, and a supported native player smoke. Record actual results, not presumed success.

## Explicit exclusions

No schema or historical-rank migration; no grade rebalancing beyond adopting existing CX persisted bands; **F asset generation is separately scoped to HPA-794**; no new result-art renderer/SFX; no changes to NX or DTXCreator; no scoring, judgement, input, or timing changes; no Song Select navigation redesign; no global theme abstraction; no new CI workflow. One implementation ticket, **one PR for HPA-759**.