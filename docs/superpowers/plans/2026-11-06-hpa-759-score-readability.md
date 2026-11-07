# HPA-759 — Single-PR implementation plan (revised after design review)

**Design:** [HPA-759 design](../specs/2026-11-06-hpa-759-score-readability-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-759/fix-playtest-score-inconsistencies-and-cx-neon-readability  
**Workflow:** this PR is documentation-only until approved. Put all HPA-759 C# implementation and tests on **this same PR** (no second implementation PR). Target **~1.5 engineer days**, hard cap 3.
**Art prerequisite:** [HPA-794 — generate F rank badge](https://linear.app/cwchanap/issue/HPA-794/art-generate-cx-neon-f-rank-badge-for-result) is a **separate art ticket and PR**. HPA-759 consumes its checked-in F asset; never generate or commit image art in this task.

## Task 0 — Confirm baseline and RED tests (~0.2 day)

- Re-read current `ResultScreenModel.ComputeRank`, `SongScore.ComputeRankIndex/NormalizeRankPercentage/RankString`, `SongDatabaseService` new-run BestRank/history paths, `SongStatusPanel.DrawSkillPointSection`, `ResultScreenRenderer`, `ConfigStage`, and the HPA-722 `DrumConfigRenderer`.
- Run existing relevant tests, recording the baseline. Add failing focused tests for: 85% and <40% grade divergence, the same 0.345 displayed differently by Result vs Song Select/history, and CX Neon title/category text colors.
- The Result policy intentionally shifts from NX's seven grades (SS–E) to the CX persisted eight grades (SS–F). **This visible regrade is a conscious product decision**, not a mistaken claim about NX. An NX-imported 85% S can retain S while a fresh CX 85% run is A; do not modify old imports or persisted buckets.
- Confirm the **separate HPA-794 art PR** will supply the 274×274 F badge via existing `skingen` pipeline; otherwise complete non-art tasks first and leave F integration pending.

## Task 1 — One CX run-grade helper, explicit F texture (~0.5 day)

**Production:** `DTXMania.Game/Lib/Song/Entities/SongScore.cs`, `SongDatabaseService.cs`, `Stage/Result/ResultScreenModel.cs`, `Stage/Result/ResultScreenRenderer.cs`, `Resources/TexturePath.cs`.

**Tests:** `DTXMania.Test/Song/SongScoreTests.cs`, `SongDatabaseServiceTests.cs`, `Stage/Result/ResultScreenModelTests.cs`, `ResultScreenRendererTests.cs`, **and** `DTXMania.Test/Resources/TexturePathTests.cs`.

1. Add **one** small `SongScore.RunRankBucket(double playingSkill)` implementing `NormalizeRankPercentage((int)Math.Floor(playingSkill))`. Use the returned bucket in all **three** current-run paths: Result `ComputeRank` (via `SongScore.ComputeRankIndex`), `SongDatabaseService` BestRank update and history `RankString`. Do not copy the floor/threshold logic anywhere else. Preserve existing `BestRank` schema, `NormalizeStoredBestRank`, NX importer, `RankMultiplier`, and best-of-all semantics.
2. Add `ResultRank.F = 7` and `GetRankLabel` F. Replace/rename `Create_ShouldComputeNXRankFromPlayingSkill` so it tests the **chosen CX** contract. Parametrize exact/just-below 40/50/60/70/80/90/95 and 0/100; assert Result/current-run history and stored bucket agree. Include NX→CX regrade cases (e.g., 85% Result S→A, <40% E→F), and the valid case where latest run and historical **best** badge have different grades.
3. **After HPA-794's art PR merges**, add `TexturePath.ResultRankF` **and include it in `TexturePath.GetAllTexturePaths()`** so C# pack-validation gates actually require it. Extend `TexturePathTests.ResultStageAssets_ShouldUseNXPaths` to assert `Graphics/8_rankF.png` (the test name is historical; F is CX-only), and `ResultStageRequiredAssets_ShouldBeIncludedInAllTexturePaths` to assert required inclusion. Do **not** add a new F background: the existing E background remains the mapped optional background. Map `GetRankTexturePath(ResultRank.F)` to `8_rankF.png` and `GetRankBackgroundPath(ResultRank.F)` to the existing E background. Replace misleading unknown-rank `_ => E` badge/background fallbacks rather than silently displaying E. Keep the existing texture/reveal code; **no F text renderer**, new font or procedural drawing in the game.
4. Ensure the F image is present in `System/CXNeon/Graphics` and covered by the existing `skingen` manifest and `CxNeonPackTests`. Release builds flatten this pack into their **bundled default** `System/Graphics`; repo-root NX reference art is **not shipped**. For the F badge, take real screenshots with **CX Neon selected** and with a **non-CX-Neon skin while the bundled/staged default System fallback contains `Graphics/8_rankF.png`**. A plain dev checkout's legacy NX `System/Graphics` does not contain F, and `ResourceManager.ResourceExists` checks active → fallback → bundled paths, **not** `System/CXNeon/` automatically; missing F in an un-staged NX-only development run is an environment setup issue, not a regression in the shipped skin. Do not copy an extra F badge into the NX reference pack or change ResourceManager's general fallback logic.

**Exit:** one shared SS–F classifier for *new* runs, correct F badge, unchanged historical storage.

## Task 2 — Three-site shared skill text, Mac-safe tests (~0.25 day)

**Production:** add tiny pure `DTXMania.Game/Lib/Song/SkillValueFormatter.cs` beside `ChartLevelFormatter.cs`. Call it from `ResultScreenModel.FormatDecimal`, Song Select HighSkill, and the `SongDatabaseService` saved-history **PlayingSkill** text.

**Tests:** `DTXMania.Test/Song/SkillValueFormatterTests.cs`; a **Mac-included** `DTXMania.Test/UI/SongStatusPanelSkillTextTests.cs` (or included `SongStatusPanelAchievementRateTests.cs`); existing history test.

1. Use the current Result rule `Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture)` as the single source. The problem is custom `"0.00"` versus standard `"F2"`, **not SQLite precision**. Do not convert doubles to `decimal`, adjust the skill formula or mutate persisted values.
2. Extract a tiny `internal static` Song Select HighSkill **text resolver** returning shared-formatted skill for played scores and `"---"` otherwise. Keep GPU-only drawing separate. Test this resolver from a file **included in the Mac test project**; `UI/SongStatusPanelLogicTests.cs` is explicitly excluded.
3. Replace `{4:F2}` in the saved-history line with `{4}` fed a string from the shared formatter. Keep every other history field and record-saving behavior unchanged.
4. Mac-safe fixtures: `0.345 -> 0.35`, `1.005 -> 1.01`, `2.675 -> 2.68`, 0, a regular value, invariant culture and unplayed `---`. Assert Result, Song Select and saved-history consistency **when comparing the same numeric skill** (Result GameSkill and Song Select best HighSkill are different after a worse new run). No dedicated SQLite round-trip test is needed for this display issue.

## Task 3 — Theme-aware Config foregrounds; verify Drum Mapping (~0.25 day)

**Production:** `DTXMania.Game/Lib/Stage/ConfigStage.cs` only (unless verified theme token need).  
**Tests:** `DTXMania.Test/Config/ConfigStageThemeColorTests.cs` and any nearby affected draw logic tests.

1. Add narrow resolvers `ResolveSelectedMenuTextColor(theme)` and `ResolveDescriptionTitleTextColor(theme)`: `Config.*` specific key → `UI.TextPrimary` → existing NX dark constants. Wire only these two current draw calls; existing value-color resolution and layout remain untouched.
2. Test empty-theme defaults, CX Neon `UI.TextPrimary`, explicit Config overrides. Capture actual CX Neon and default/light screenshots; static color tests alone do not prove visibility.
3. Inspect the **new HPA-722** Drum Mapping screen with its palette-based text on both skins. Do not reinstate obsolete dark instruction styling, redo its layout, or reverse HPA-722's explicit **MIDI** caption decision. Touch only genuinely reproducing new contrast defects if any.

## Task 4 — Player verification and PR evidence (~0.25–0.5 day)

- After the F art is available, exercise a failed low-achievement run and a completed No Fail low-achievement run. Capture actual Result F badge in **CX Neon** and in a **packaged/staged default-fallback configuration** (e.g., the game's `<bin>/System/Graphics` populated from `System/CXNeon/Graphics`); capture Config/Drum Mapping separately on **CX Neon and the local light NX skin**. Do not expect F to load from an un-staged NX-only development `System/Graphics` tree. Also capture saved-history rank/value and Song Select best badge, including an intentionally changed NX→CX grade boundary (such as 85%). Check score saving and previous best retention.
- Run focused tests (adapt to environment/current SDK):
  - `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenModelTests|FullyQualifiedName~ResultScreenRendererTests|FullyQualifiedName~SongScoreTests|FullyQualifiedName~SongDatabaseServiceTests|FullyQualifiedName~SkillValueFormatterTests|FullyQualifiedName~SongStatusPanelSkillTextTests|FullyQualifiedName~ConfigStageThemeColorTests|FullyQualifiedName~TexturePathTests|FullyQualifiedName~CxNeonPackTests"`
  - `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
  - `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
  - On Windows, corresponding `DTXMania.Game.Windows.csproj` and `DTXMania.Test.csproj` build/tests; use existing Windows E2E where practical.
- The out-of-process E2E project is Windows-only. On Mac, use the repository's existing game-MCP/native screenshot route rather than creating a new E2E or image-capture harness.
- Update **this same draft PR** with actual commands, test counts, screenshots, expected NX→CX grade difference and any remaining limits. Do not claim graphical validation based on headless tests.

## Guardrails and handoff

**Preserve:** existing SQLite best-rank buckets, legacy ordinal import mapping, NX imported labels, best-score qualification, score/history data shape, judgement/skill formulas, skin layout, font/resource lifecycle, timing/input rules. Reuse `SongScore` and `ISkinTheme`; no new grade engine or theme framework.

**Do not bundle:** HPA-794 badge generation (separate art PR), new SFX, navigation context, control hints, two-step Enter UI, Tab mode changes, Drum Mapping redesign, DTXCreator, schema changes, historical rank rewrites, new CI jobs. If the visible regrade away from NX is challenged, settle the product policy in design review rather than switching policies silently.

## Done means

- [ ] One `RunRankBucket` governs all new-run grade uses; Result/current history agree at every boundary. Intentional NX→CX visible regrade is documented.
- [ ] F badge from the **separate HPA-794 art PR** renders correctly in Result with CX Neon selected **and** via the staged/bundled default-skin fallback; no incorrect E badge or new glyph-render path. `TexturePathTests` explicitly covers the path and required inventory.
- [ ] 0.345 displays 0.35 in all three text surfaces, using a common helper; Mac-included Song Select resolver tests run.
- [ ] CX Neon/default Config colors are legible; redesigned Drum Mapping visually verified.
- [ ] Full and focused tests plus **real** screenshots and failed/No Fail player paths are recorded; HPA-759 uses only **this PR** for its implementation.