# HPA-759 — Score consistency and CX Neon readability design

**Linear:** https://linear.app/cwchanap/issue/HPA-759/fix-playtest-score-inconsistencies-and-cx-neon-readability  
**Delivery:** one implementation PR, <=3 engineer days; this draft starts with design/docs only. Production fixes and tests go onto this **same PR** after review.

## Goal and current baseline

Fix discrepancies reproduced in the 2026-11-04 native player playtest: Result E versus saved-history F for the same low-achievement run, Result skill 0.35 versus Song Select 0.34 for a skill of 0.345, and dark-on-dark Config text under CX Neon. Preserve score storage, gameplay calculations, and the player journey.

Rechecked current `main` on 2026-11-06:
- `ResultScreenModel.ComputeRank` has **7** bands (SS>=95, S>=80, A>=73, B>=63, C>=53, D>=45, else E).
- `SongScore.NormalizeRankPercentage`, `ComputeRankIndex` and `RankString` already express **8** persisted rank bands (SS>=95, S>=90, A>=80, B>=70, C>=60, D>=50, E>=40, else F); `SongDatabaseService` records the current-run history label using this policy. Existing Song Select rank icons likewise include F.
- `ResultScreenRenderer` and `TexturePath` have Result rank art **only for SS through E**. Its unknown-rank fallback currently loads E art.
- `ResultScreenModel.FormatDecimal` uses `"0.00"` and `SongStatusPanel.DrawSkillPointSection` uses `"F2"` directly; their source numbers may differ in binary representation before versus after SQLite persistence.
- `ConfigStage` still hard-codes `SelectedMenuText` and `DescriptionTitleText` for light NX texture cells. It already resolves item value colors from `ISkinTheme`. `System/CXNeon/Theme.ini` contains a light `UI.TextPrimary`.
- **HPA-722 is Done:** `DrumConfigRenderer` now draws Drum Mapping with `SkinPalette` text on dark panels instead of the old `DrumConfigStage` dark-text instruction. HPA-722 intentionally changed the product caption to **MIDI**, superseding HPA-520's earlier HDMI wording. Treat this as the latest approved product decision, not a new HPA-759 change.

## Decision 1 — One 8-band grade policy

**Use the existing persisted SongScore policy** for all newly computed run ranks. It is already the storage/history and Song Select contract; do not introduce a competing table or a database migration.

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

`ResultScreenModel.ComputeRank` should reuse `SongScore.ComputeRankIndex` (with the same floor/percentage treatment that `SongDatabaseService` applies) instead of duplicating thresholds. Extend the presentation `ResultRank` enum with F (index 7), and render the same letter in `ResultScreenModel.GetRankLabel`, Result display, and current-run history.

**F artwork contract:** no F result sprite exists. Show a *real F*, not the current E sprite fallback: for F use the existing lowest-tier result **background** only, and render a legible, theme-aware **F text glyph** in the rank badge location using existing fonts/reveal timing. SS–E continue to use existing sprites unchanged. No image generation or new asset workflow in this ticket; polished F art can be a separate art ticket if later wanted. Test the F resource/fallback selection separately from GPU output and inspect a real screenshot.

**Persistence and history:** keep `SongScore.BestRank` bucket values, `NormalizeStoredBestRank`, NX import mapping, rank multipliers, schema, and historical rows unchanged. Old/imported best rank ordinals have known ambiguous semantics (notably stored zero); **do not reinterpret or rewrite them here**. New result and new history line must agree for a single run. The Song Select badge represents the **best** run, so it may legitimately differ from the most recent Result if an older run was better. Never force the best rank down to match the latest run. Failed/cleared status and Result plate remain independent of grade.

## Decision 2 — One explicit two-decimal skill presentation policy

Add a minimal shared pure formatter (e.g. `Lib/UI/SkillValueFormatter.cs`) used by both `ResultScreenModel` and `SongStatusPanel`. Display non-negative finite skill values with two decimals, invariant culture, **round midpoint away from zero**. Convert to `decimal` for the final two-decimal display rounding to avoid binary midpoint discrepancies; do not alter the underlying `double` score values or database writes.

Required fixture: **0.345 -> "0.35"** on both Result and Song Select, including a fresh and a SQLite round-tripped value. Cover nearby values, 0, high skill, and locale independence. Preserve the existing `"---"` placeholder for charts with no played score. Distinguish current-run GameSkill on Result from historical best HighSkill on Song Select; test consistency only when the compared values are the same. Do not modify level formatting, percent formatting, skill formula, record qualification, or score saving.

## Decision 3 — Theme-aware Config foregrounds, no re-skin

Reuse existing `ISkinTheme.GetColor` with two narrowly scoped resolvers in `ConfigStage`:
- Selected category text: `Config.SelectedMenuText` -> `UI.TextPrimary` -> current `SelectedMenuText` constant.
- Description title: `Config.DescriptionTitleText` -> `UI.TextPrimary` -> current `DescriptionTitleText` constant.

Use each resolver at the existing draw sites, with `_resourceManager.CurrentTheme ?? SkinTheme.Empty`. CX Neon already supplies a light `UI.TextPrimary`; themeless/light NX retains the dark historical colors. Explicit skin keys allow other skins to override those surfaces. Do not change Config layout or item value colors. Test empty theme, generic Neon palette, and explicit overrides. Inspect actual contrast on CX Neon and default NX/light skin: theoretical color tests do not prove readability.

For Drum Mapping, the new HPA-722 renderer uses palette-driven text. **Verify** its header, instructions, selection and status on both skins; only make a local color correction if the new screen still reproduces a genuine issue. No popup/focus/layout rewrite or new image art.

## Product/UX observations recorded but intentionally deferred

- Song Select returning to All Songs instead of originating Bookmarks/folder is current behavior, not a confirmed regression. Defer changing return navigation context.
- Tab/B shortcut discoverability, clickable tab labels, and two-step Enter launch feedback are product improvements; do not bundle them in this bugfix.
- HPA-520 previously requested “HDMI device”; the newer approved HPA-722 specifies **MIDI**, which the new renderer uses. Retain HPA-722's decision; do not silently revert the caption or change device discovery.

These choices satisfy HPA-759's requirement to record decisions without expanding an already multi-defect ticket.

## Acceptance and proof

1. Every rank boundary (exact, immediately below, including 0/40/50/60/70/80/90/95) yields matching Result rank text, current-run saved history text, and expected bucket for new runs. Result **F** displays F, not E. Existing best-rank and NX import tests continue to pass.
2. A persisted 0.345 GameSkill displays **0.35** on Result and, when it is the best run, **0.35** on Song Select. Stored values remain unchanged.
3. Config description title and selected category remain readable on CX Neon and the light/default skin. HPA-722 Drum Mapping remains legible.
4. Repeat one failed low-score and one completed No Fail run through Result -> saved history -> Song Select; confirm appropriate grades, skill, save status, and no crashes. Capture screenshots of F on Result, Config/Drum Mapping on both skins, and compare to actual player-facing text.
5. Run focused grade/formatter/theme tests, the applicable game/test suites, and a supported native player smoke. Record actual results, not presumed success.

## Explicit exclusions

No schema or historical-rank migration; no grade rebalancing beyond adopting existing persisted bands; no new result art/SFX; no changes to NX or DTXCreator; no scoring, judgement, input, or timing changes; no Song Select navigation redesign; no global theme abstraction; no new CI workflow. One ticket, **one PR**.