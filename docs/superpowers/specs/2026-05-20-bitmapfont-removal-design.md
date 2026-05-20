# Replace BitmapFont with ManagedFont

**Status:** Approved (design)
**Date:** 2026-05-20

## Goal

Delete `Lib/Resources/BitmapFont.cs` and route all text rendering through `IFont` / `ManagedFont` (backed by MonoGame `SpriteFont`). All current consumers are migrated; no shim/adapter is introduced.

This is a deliberate visual-style change: DTXMania's classic pixel-art console font, level-number digits, and judgement text are replaced by TTF-rendered (`NotoSerifJP`) text. The user has accepted this tradeoff.

## Scope

**Removed entirely:**
- `Lib/Resources/BitmapFont.cs` (class, `BitmapFontConfig`, `FontType` enum, factories `CreateConsoleFontConfig`/`CreateLevelNumberFontConfig`/`CreateJudgementTextFontConfig`).
- `DTXMania.Test/Resources/BitmapFontTests.cs`.
- `DTXMania.Test/Resources/BitmapFontConfigTests.cs`.
- `BitmapFont`-related `<Compile Include>` exclusions in `DTXMania.Test/DTXMania.Test.Mac.csproj`.
- `BitmapFont` fields in stages and components: `_bitmapFont`, `_levelNumberFont`, `_resultFont`, `_readyFont`.
- `USE_SPRITE_FONT` toggle and `useBitmapFont` branch in `SongStatusPanel`.
- `FontType` parameter on all draw helpers.

**Kept as-is:**
- `IFont` interface.
- `ManagedFont` (with style-awareness added in step 1).
- `IResourceManager.LoadFont`.
- Console-font texture files on disk (`Console_font.png`, etc.) and `TexturePath` constants — orphaned but not deleted (skin assets, removal is out of scope).

## Per-site mapping

| Site | Was | Becomes | Notes |
|---|---|---|---|
| `StartupStage` | Console font (Normal/Thin) | `NotoSerifJP` 14 Regular + 14 Bold for progress messages | `CreateBitmapFontCore` becomes `CreateFontCore`; fallback rendering retained but simplified. |
| `ConfigStage` | Console font, Thin for selected | 14 Regular + 14 Bold for selection emphasis | `isSelected` chooses bold font. |
| `ResultStage` | Console font | 14 Regular | `CreateResultFont` returns `IFont`. |
| `SongSelectionStage` | Console font + existing `uiFont` | 14 Regular (single path) | Removes `_bitmapFont` field and parallel fallback. |
| `SongTransitionStage` | Level Number font (variable-width pixel digits) | 24 Regular | Visual style change. |
| `PerformanceStage` (READY overlay) | Console font | 24 Regular | Bumped from 16px because overlay needs readability. |
| `KeyAssign/DrumKeyAssignPanel` | Console font, Thin for emphasized rows | 14 Regular + 14 Bold | `IKeyAssignPanel.Draw` signature changes (see Interfaces). |
| `KeyAssign/SystemKeyAssignPanel` | Same | Same | Same signature change. |
| `Performance/ScrollSpeedIndicator` | Console font | 14 Regular | Constructor takes `IFont?`. |
| `Performance/JudgementTextPopup` | Judgement font (12x18 cells, console-font texture) | 48 Regular | Bumped from 18px to give "punch" closer to the original feel. Injected `drawText` callback retyped to `Action<IFont, SpriteBatch, string, int, int, Color>`. |
| `Song/Components/SongStatusPanel` | Level Number font + existing `IFont _managedFont` | 24 Regular for level digits via existing `ManagedFont` path | `USE_SPRITE_FONT` toggle and `_levelNumberFont` field removed. |

## Bold variant handling

`FontType.Thin` (the emphasis style for selected/highlighted items) is replaced by a Bold SpriteFont. `FontType.WhiteThin` is dead code today and is dropped without replacement.

**Asset work:**
- Add `DTXMania.Game/Content/NotoSerifJP-Bold.spritefont` with `<Size>14</Size>` and `<Style>Bold</Style>`.
- Register in `Content.mgcb`.
- Verify `NotoSerifJP.ttf` exposes a Bold weight. If it does not, add `NotoSerifJP-Bold.ttf` from Google Fonts (Noto Serif JP ships with multiple weights) and update the spritefont's `<FontName>` to reference it. As a last resort, accept Content Pipeline's algorithmic bold synthesis (lower quality, but functional).
- Only size 14 Bold is added. Emphasis is only used at the console-font tier.

**`ManagedFont` changes:**
- `GetBestSizeSpriteFont` becomes style-aware. When `FontStyle.Bold` is requested, prefer the `-Bold` asset name variant. Fall back to Regular if the Bold asset fails to load.
- Available font table after this change:
  - `(14, Regular, "NotoSerifJP")`
  - `(14, Bold,    "NotoSerifJP-Bold")`
  - `(24, Regular, "NotoSerifJP-24")`
  - `(48, Regular, "NotoSerifJP-48")`

**Call-site pattern:**
```csharp
var font = isSelected ? _boldFont : _font;
font.DrawString(_spriteBatch, label, new Vector2(x, y), Color.White);
```
Both fonts loaded once during stage activation via `_resourceManager.LoadFont("NotoSerifJP", 14)` and `_resourceManager.LoadFont("NotoSerifJP", 14, FontStyle.Bold)`.

## Interface changes

- `IKeyAssignPanel.Draw(SpriteBatch, BitmapFont?, Texture2D?, ...)` → `IKeyAssignPanel.Draw(SpriteBatch, IFont?, IFont?, Texture2D?, ...)` (separate Regular and Bold). Only two implementers in this repo; no external consumers.
- `ScrollSpeedIndicator` constructor: `ScrollSpeedIndicator(BitmapFont?)` → `ScrollSpeedIndicator(IFont?)`.
- `JudgementTextPopup` injected drawText delegate: `Action<BitmapFont, SpriteBatch, string, int, int, Color>` → `Action<IFont, SpriteBatch, string, int, int, Color>`.
- Stage virtual test seams (`CreateResultFont`, `CreateBitmapFontCore`) return `IFont` and are renamed where the old name embeds "Bitmap" (`CreateBitmapFontCore` → `CreateFontCore`).

## Test strategy

**Delete:**
- `BitmapFontTests.cs`, `BitmapFontConfigTests.cs`.
- Their `<Compile Include>` exclusions from `DTXMania.Test.Mac.csproj`.

**Update (existing tests that reference `BitmapFont`):**
- `Resources/BitmapFontConfigTests.cs` — deleted (above).
- `Resources/BitmapFontTests.cs` — deleted (above).
- `UI/SongStatusPanelLogicTests.cs` — switch level-number assertions to `IFont` mock; drop `useBitmapFont` branch coverage.
- `Config/ConfigStageLogicTests.cs` — drop `FontType` checks; assert bold-vs-regular by which `IFont` instance is selected.
- `Stage/ResultStageTests.cs` — override `CreateResultFont()` to return an `IFont` mock.
- `Stage/ResultStageCoverageTests.cs` — same.
- `Stage/StartupStageLogicTests.cs` — override `CreateFontCore` (renamed); fallback-rendering tests now operate on `IFont`.
- `Stage/Performance/JudgementTextPopupLogicTests.cs` — inject `IFont` mock and updated drawText callback.
- `Stage/Performance/JudgementTextPopupTests.cs` — same; keep Mac exclusion only if the test still requires a real `GraphicsDevice`.
- `Stage/Performance/PerformanceStageDeterministicTests.cs` — `_readyFont` references switch to `IFont`.
- `Stage/Performance/ScrollSpeedIndicatorTests.cs` — constructor argument retyped.

**Add:**
- `Resources/ManagedFontFactoryTests.cs` — verify `GetBestSizeSpriteFont` picks `NotoSerifJP-Bold` for `(size=14, style=Bold)` and falls back to Regular when Bold isn't loaded. This is the only new logic the migration introduces.

**Mac test project:**
- Remove `<Compile Include>` entries that exclusively cover the two deleted BitmapFont test files. Leave the rest of the exclusion list intact.
- New `IFont`-based tests must not require a real `GraphicsDevice` (use `Mock<IFont>` rather than loading SpriteFonts in unit tests).

## Implementation order

Each step ends green on the platforms it touches.

1. **Bold asset + style-aware factory.** Add `NotoSerifJP-Bold.spritefont` (+ TTF if needed), register in `Content.mgcb`, extend `GetBestSizeSpriteFont` for style awareness. Add `ManagedFontFactoryTests.cs`. No call-site changes.

2. **Easy stages (parallel `IFont` path already exists).** `SongStatusPanel` — drop `USE_SPRITE_FONT` and `_levelNumberFont`. `SongSelectionStage` — drop `_bitmapFont`.

3. **Simple stages.** `ResultStage`, `PerformanceStage` READY overlay, `ScrollSpeedIndicator`, `SongTransitionStage`. Each: one field swap and updated draw calls. Run the game locally to verify each before committing.

4. **Menu stages with Bold emphasis.** `StartupStage`, `ConfigStage`, `KeyAssign/DrumKeyAssignPanel`, `KeyAssign/SystemKeyAssignPanel`. Each gets `_font` + `_boldFont`. `IKeyAssignPanel.Draw` signature change ripples to both implementers (no external consumers).

5. **`JudgementTextPopup`.** Switch injected `drawText` to `Action<IFont, SpriteBatch, ...>`. Update its tests.

6. **Final removal.** Delete `BitmapFont.cs`, `BitmapFontTests.cs`, `BitmapFontConfigTests.cs`. Update `DTXMania.Test.Mac.csproj` to drop the now-irrelevant exclusion entries. Build verifies no references remain.

## Risks

- **Bold TTF availability.** If `NotoSerifJP.ttf` lacks a Bold weight, the Content Pipeline will synthesize one (lower fidelity). Step 1 must verify and, if needed, drop in a real Bold TTF before proceeding.
- **Mac vs Windows test divergence.** Several BitmapFont tests are currently Mac-excluded. The replacement tests must not require a `GraphicsDevice` in unit tests — use `Mock<IFont>` for the new paths.
- **Test-seam renames.** Subclasses in tests that override `CreateBitmapFontCore` / `CreateResultFont` will fail to compile until updated to the `IFont`-returning signatures. This is intentional and caught at build time.
- **READY overlay and Judgement popup size bumps (24 and 48).** These are intentional deviations from the original pixel sizes (16 and 18) — confirmed by the user as the right call for legibility/impact.

## Out of scope

- Removing orphaned console-font textures from `System/Graphics/` or `TexturePath` constants.
- Reworking the skin system to drop bitmap-font asset paths.
- Localization audit (NotoSerifJP already covers JP — no regression).
- Migrating `ComboDisplay`, `ScoreDisplay`, `SkillMeterDisplay`, `SkillPanelDisplay`, `SongListDisplay` (already use `ManagedFont`; no change needed).
