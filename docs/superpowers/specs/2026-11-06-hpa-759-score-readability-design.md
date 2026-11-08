# HPA-759 — Score consistency and CX Neon readability design

**Linear:** https://linear.app/cwchanap/issue/HPA-759/fix-playtest-score-inconsistencies-and-cx-neon-readability  
**Delivery:** design and implementation are on the same PR.
**Art dependency:** [HPA-794 — generate F rank badge](https://linear.app/cwchanap/issue/HPA-794/art-generate-cx-neon-f-rank-badge-for-result) supplied the CX Neon F badge in a separate art PR.

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

| Displayed playing skill (rounded to two decimals) | Grade | Stored bucket |
| --- | --- | --- |
| >=95 | SS | 95 |
| >=90 and <95 | S | 90 |
| >=80 and <90 | A | 80 |
| >=70 and <80 | B | 70 |
| >=60 and <70 | C | 60 |
| >=50 and <60 | D | 50 |
| >=40 and <50 | E | 40 |
| <40 | F | 0 |

**Representative Result regrades after adopting CX policy (old NX Result → new CX Result):**

| Raw playing skill range, away from rounding boundaries | Before | After |
| --- | --- | --- |
| 80–<90 | S | A |
| 73–<80 | A | B |
| 63–<70 | B | C |
| 53–<60 | C | D |
| 45–<50 | D | E |
| <40 | E | F |

Away from rounding boundaries, other intervals keep the same letter. Rounding can also promote a raw value just below a CX boundary: 94.995 was S under the old NX thresholds but displays 95.00 and grades SS under the new policy. **NX-imported ranks are not retroactively regraded:** e.g. an imported NX S with an 85% achievement can remain stored as CX S/bucket 90, while a *new* 85% CX run displays A. This is a known and accepted legacy-vs-new-run discrepancy; fixing old imports is out of scope.

`SongScore.RunRankBucket(double playingSkill)` clamps finite values to 0–100, converts to decimal, rounds to two places away from zero, floors the rounded value, and uses `NormalizeRankPercentage`. This aligns the grade with the displayed custom `"0.00"` value: 39.995 displays 40.00 and grades E. Non-finite values use F. Result `SongScore.ComputeRankIndex`, the new-run BestRank update, and history `RankString` share this helper. The presentation `ResultRank` enum includes F (index 7), so Result and the *current-run* saved-history line show the same letter.

**F artwork contract:** [HPA-794](https://linear.app/cwchanap/issue/HPA-794/art-generate-cx-neon-f-rank-badge-for-result) supplied the F source, manifest entry and generated CX Neon badge. `TexturePath.ResultRankF` maps F to that badge and the existing E background. Unknown ranks have no badge/background fallback, and a missing rank badge is traced once when the Result loads. SS–E drawing/reveal stays unchanged. **No custom F glyph renderer or new runtime font path**. The release flattens `System/CXNeon/Graphics` into bundled base `System/Graphics`; the development-only NX pack has no F badge. [Native screenshots](https://github.com/cwchanap/DTXManiaCX/pull/225#issuecomment-6711317746) cover CX Neon and staged default fallback.

**Persistence and history:** keep `SongScore.BestRank` bucket values, `NormalizeStoredBestRank`, NX import mapping, rank multipliers, schema, and historical rows unchanged. Old/imported best rank ordinals have known ambiguous semantics (notably stored zero); **do not reinterpret or rewrite them here**. New result and new history line must agree for a single run. The Song Select badge represents the **best** run, so it may legitimately differ from the most recent Result if an older run was better. Never force the best rank down to match the latest run. Failed/cleared status and Result plate remain independent of grade.

## Decision 2 — One shared two-decimal skill presentation policy

Introduce a tiny pure `SkillValueFormatter.Format(double)` next to `Lib/Song/ChartLevelFormatter.cs`, reusing Result's existing presentation rule: `Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture)`. This preserves the existing .NET custom-format midpoint behavior without any `decimal` conversion or change to raw `double` skill values. `"0.00"` and `"F2"` may produce different halfway results for the *same double*, even with no persistence involved.

Use this one formatter in **three** places: `ResultScreenModel.Create`, `SongStatusPanel`'s HighSkill text, and `SongDatabaseService`'s new-run history line. Song Select's `internal static` text resolver handles `SongScore.HasBeenPlayed` and returns `"---"` for unplayed charts; its draw method remains GPU-only. Test that resolver in a Mac-included test file (e.g. `SongStatusPanelSkillTextTests.cs`), **not** excluded `SongStatusPanelLogicTests.cs`.

Required fixtures: 0.345 → 0.35, 1.005 → 1.01, 2.675 → 2.68, 0, an ordinary value, and invariant locale; unplayed remains `"---"`. The SQLite round-trip is **not a cause of this bug** and does not need a new dedicated test. Distinguish current-run GameSkill on Result from historical best HighSkill on Song Select; compare displays only when the underlying values match. Preserve percent/level formatting and calculations.

## Decision 3 — Theme-aware Config foregrounds, no re-skin

Reuse existing `ISkinTheme.GetColor` with two narrowly scoped resolvers in `ConfigStage`:
- Selected category text: `Config.SelectedMenuText` -> `UI.Accent` -> current `SelectedMenuText` constant. CX Neon specifies light `Config.SelectedMenuText` for contrast on its teal cursor band.
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

1. Every rank boundary (exact and values on either side of the two-decimal rounding threshold, including 0/40/50/60/70/80/90/95) yields matching Result rank text, current-run saved history text, and expected bucket for new runs. Result **F** displays F, not E. Existing best-rank and NX import tests continue to pass.
2. The **same** 0.345 GameSkill displays **0.35** on Result and (when it is also the best run) **0.35** on Song Select; saved-history PlayingSkill uses the same formatter. The numeric score persisted in SQLite is unchanged.
3. Config description title and selected category remain readable on CX Neon and the light/default skin. HPA-722 Drum Mapping remains legible.
4. Exercise one failed low-score and one completed No Fail run through Result -> saved history -> Song Select; confirm grades, skill, save status, and no crashes. Capture screenshots of the **actual F badge** on Result, Config/Drum Mapping on both skins, and compare to player-facing text.
5. Run focused grade/formatter/theme tests **including Mac-safe HighSkill display and run-rank helper tests**, the applicable game/test suites, and a supported native player smoke. Record actual results, not presumed success.

## Explicit exclusions

No schema or historical-rank migration; no grade rebalancing beyond adopting existing CX persisted bands; **F asset generation is separately scoped to HPA-794**; no new result-art renderer/SFX; no changes to NX or DTXCreator; no scoring, judgement, input, or timing changes; no Song Select navigation redesign; no global theme abstraction; no new CI workflow. One implementation ticket, **one PR for HPA-759**.
