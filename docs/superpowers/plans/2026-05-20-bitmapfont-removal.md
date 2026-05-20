# BitmapFont Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete `Lib/Resources/BitmapFont.cs` and route every consumer through `IFont`/`ManagedFont` (backed by MonoGame `SpriteFont`).

**Architecture:** Direct replacement — no shim. Each stage/component that holds a `BitmapFont` field is migrated one at a time to `IFont` loaded via `ResourceManager.LoadFont`. The `FontType.Thin` emphasis style is replaced by a Bold SpriteFont variant added in step 1. `BitmapFont.cs` and its two test files are deleted last, after every reference is gone.

**Tech Stack:** .NET 8, MonoGame 3.8 (`SpriteFont`, Content Pipeline), xUnit + Moq, project's existing `IResourceManager`/`IFont`/`ManagedFont` types.

**Reference spec:** `docs/superpowers/specs/2026-05-20-bitmapfont-removal-design.md`

---

## Task 1: Add Bold SpriteFont asset + style-aware ManagedFont factory

**Files:**
- Create: `DTXMania.Game/Content/NotoSerifJP-Bold.spritefont`
- Modify: `DTXMania.Game/Content/Content.mgcb`
- Modify: `DTXMania.Game/Lib/Resources/ManagedFont.cs:104-152`
- Test: `DTXMania.Test/Resources/ManagedFontFactoryTests.cs`

- [ ] **Step 1.1: Verify whether `NotoSerifJP.ttf` ships with a Bold weight**

Run:
```bash
fc-scan --format "%{family}\t%{style}\t%{weight}\n" DTXMania.Game/Content/NotoSerifJP.ttf 2>&1 | head -5
```
Expected: One line printed. If `weight` is `200` (Regular only), you must add a Bold TTF. If the line shows multiple weights or has weight ≥ 700, you can rely on the existing TTF. If `fc-scan` is unavailable, inspect the file size — a multi-weight TTC will be > 10 MB; a single-weight TTF is typically < 5 MB.

If a Bold TTF is needed, download from https://fonts.google.com/noto/specimen/Noto+Serif+JP (the "Bold" weight) and save as `DTXMania.Game/Content/NotoSerifJP-Bold.ttf`. In the spritefont below, set `<FontName>NotoSerifJP-Bold</FontName>` instead of `<FontName>NotoSerifJP</FontName>`.

- [ ] **Step 1.2: Create the Bold spritefont definition**

Create `DTXMania.Game/Content/NotoSerifJP-Bold.spritefont`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<XnaContent xmlns:Graphics="Microsoft.Xna.Framework.Content.Pipeline.Graphics">
  <Asset Type="Graphics:FontDescription">
    <FontName>NotoSerifJP</FontName>
    <Size>14</Size>
    <Spacing>0</Spacing>
    <UseKerning>true</UseKerning>
    <Style>Bold</Style>
    <DefaultCharacter>*</DefaultCharacter>
    <CharacterRegions>
      <CharacterRegion>
        <Start>&#32;</Start>
        <End>&#126;</End>
      </CharacterRegion>
      <CharacterRegion>
        <Start>&#12352;</Start>
        <End>&#12447;</End>
      </CharacterRegion>
      <CharacterRegion>
        <Start>&#12448;</Start>
        <End>&#12543;</End>
      </CharacterRegion>
      <CharacterRegion>
        <Start>&#12289;</Start>
        <End>&#12351;</End>
      </CharacterRegion>
      <CharacterRegion>
        <Start>&#19968;</Start>
        <End>&#40959;</End>
      </CharacterRegion>
      <CharacterRegion>
        <Start>&#63744;</Start>
        <End>&#64255;</End>
      </CharacterRegion>
      <CharacterRegion>
        <Start>&#65280;</Start>
        <End>&#65519;</End>
      </CharacterRegion>
    </CharacterRegions>
  </Asset>
</XnaContent>
```

If step 1.1 determined a separate Bold TTF is needed, change `<FontName>NotoSerifJP</FontName>` to `<FontName>NotoSerifJP-Bold</FontName>` and ensure `NotoSerifJP-Bold.ttf` is in the same directory.

- [ ] **Step 1.3: Register the Bold spritefont in `Content.mgcb`**

Append to `DTXMania.Game/Content/Content.mgcb` (after the existing `NotoSerifJP-48.spritefont` block):

```
#begin NotoSerifJP-Bold.spritefont
/importer:FontDescriptionImporter
/processor:FontDescriptionProcessor
/processorParam:PremultiplyAlpha=True
/processorParam:TextureFormat=Compressed
/build:NotoSerifJP-Bold.spritefont
```

- [ ] **Step 1.4: Verify the Content Pipeline builds the new asset**

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -20
```
Expected: Build succeeds. `NotoSerifJP-Bold.xnb` should appear in `DTXMania.Game/Content/bin/DesktopGL/`.

Run:
```bash
ls DTXMania.Game/Content/bin/DesktopGL/NotoSerifJP-Bold.xnb
```
Expected: File exists.

- [ ] **Step 1.5: Write failing test for style-aware factory**

Create `DTXMania.Test/Resources/ManagedFontFactoryTests.cs`:

```csharp
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using Xunit;

namespace DTXMania.Test.Resources
{
    public class ManagedFontFactoryTests
    {
        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize14_PrefersBoldAssetName()
        {
            // Use reflection because the method is private static and we want to
            // test asset-name selection without requiring a real ContentManager.
            var method = typeof(ManagedFont).GetMethod(
                "GetBestSpriteFontAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var result = (string)method!.Invoke(null, new object[] { 14, FontStyle.Bold })!;
            Assert.Equal("NotoSerifJP-Bold", result);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenRegularRequestedAtSize14_PrefersRegularAssetName()
        {
            var method = typeof(ManagedFont).GetMethod(
                "GetBestSpriteFontAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            var result = (string)method!.Invoke(null, new object[] { 14, FontStyle.Regular })!;
            Assert.Equal("NotoSerifJP", result);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize24_FallsBackToRegular24()
        {
            // No -24-Bold asset exists; the factory should fall back to the closest
            // size in Regular rather than picking 14-Bold (which is the wrong size).
            var method = typeof(ManagedFont).GetMethod(
                "GetBestSpriteFontAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            var result = (string)method!.Invoke(null, new object[] { 24, FontStyle.Bold })!;
            Assert.Equal("NotoSerifJP-24", result);
        }
    }
}
```

- [ ] **Step 1.6: Run test to verify it fails**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ManagedFontFactoryTests" 2>&1 | tail -15
```
Expected: FAIL with `NullReferenceException` (method `GetBestSpriteFontAssetName` does not exist).

- [ ] **Step 1.7: Refactor `GetBestSizeSpriteFont` to be style-aware**

In `DTXMania.Game/Lib/Resources/ManagedFont.cs`, replace lines 87-152 (the `CreateFont` static + `GetBestSizeSpriteFont`) with:

```csharp
        public static ManagedFont CreateFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
        {
            lock (_fontFactoryLock)
            {
                if (_contentManager == null)
                    throw new InvalidOperationException("Font factory not initialized. Call InitializeFontFactory first.");

                var spriteFont = GetBestSizeSpriteFont(size, style);
                return new ManagedFont(spriteFont, fontPath, size, style);
            }
        }

        /// <summary>
        /// Get the best SpriteFont for the requested size and style.
        /// Prefers a Bold-variant asset when style=Bold and one exists at that size.
        /// Falls back to the closest Regular-size asset otherwise.
        /// </summary>
        private static SpriteFont GetBestSizeSpriteFont(int requestedSize, FontStyle style)
        {
            var assetName = GetBestSpriteFontAssetName(requestedSize, style);

            if (!_loadedFonts.TryGetValue(assetName, out var spriteFont))
            {
                try
                {
                    spriteFont = _contentManager.Load<SpriteFont>(assetName);
                    _loadedFonts[assetName] = spriteFont;
                    System.Diagnostics.Debug.WriteLine($"ManagedFont: Loaded {assetName} for requested size {requestedSize} style {style}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ManagedFont: Failed to load {assetName}, falling back to default. Error: {ex.Message}");

                    if (_defaultFont == null)
                    {
                        try
                        {
                            _defaultFont = _contentManager.Load<SpriteFont>("NotoSerifJP");
                            _loadedFonts["NotoSerifJP"] = _defaultFont;
                        }
                        catch (Exception defaultEx)
                        {
                            throw new NotSupportedException(
                                "Cannot create font - failed to load any SpriteFont. " +
                                "Please ensure NotoSerifJP.spritefont is built in your Content project. Error: " + defaultEx.Message);
                        }
                    }
                    spriteFont = _defaultFont;
                }
            }

            return spriteFont;
        }

        /// <summary>
        /// Pick the closest available SpriteFont asset for the requested (size, style).
        /// Bold variant is only available at size 14; for other sizes Bold falls back to Regular.
        /// </summary>
        private static string GetBestSpriteFontAssetName(int requestedSize, FontStyle style)
        {
            var availableRegular = new[]
            {
                (size: 14, assetName: "NotoSerifJP"),
                (size: 24, assetName: "NotoSerifJP-24"),
                (size: 48, assetName: "NotoSerifJP-48")
            };

            if (style == FontStyle.Bold)
            {
                var boldVariants = new[]
                {
                    (size: 14, assetName: "NotoSerifJP-Bold")
                };

                var closestBold = boldVariants
                    .OrderBy(x => Math.Abs(x.size - requestedSize))
                    .First();

                var closestRegular = availableRegular
                    .OrderBy(x => Math.Abs(x.size - requestedSize))
                    .First();

                // Use Bold only if its size is closer than the closest Regular asset.
                // Otherwise prefer the right-sized Regular over the wrong-sized Bold.
                if (Math.Abs(closestBold.size - requestedSize) <= Math.Abs(closestRegular.size - requestedSize))
                {
                    return closestBold.assetName;
                }
                return closestRegular.assetName;
            }

            return availableRegular
                .OrderBy(x => Math.Abs(x.size - requestedSize))
                .First()
                .assetName;
        }
```

- [ ] **Step 1.8: Run tests to verify they pass**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ManagedFontFactoryTests" 2>&1 | tail -15
```
Expected: 3 passing tests.

- [ ] **Step 1.9: Run full Mac test suite to verify no regressions**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj 2>&1 | tail -10
```
Expected: All previously-passing tests still pass.

- [ ] **Step 1.10: Commit**

```bash
git add DTXMania.Game/Content/NotoSerifJP-Bold.spritefont \
        DTXMania.Game/Content/Content.mgcb \
        DTXMania.Game/Lib/Resources/ManagedFont.cs \
        DTXMania.Test/Resources/ManagedFontFactoryTests.cs
# Also add NotoSerifJP-Bold.ttf if step 1.1 required adding one
git commit -m "feat: add Bold SpriteFont variant and style-aware factory"
```

---

## Task 2: Migrate `SongStatusPanel` — drop `_levelNumberFont`

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`
- Modify: `DTXMania.Test/UI/SongStatusPanelLogicTests.cs`

- [ ] **Step 2.1: Remove `USE_SPRITE_FONT` and `_levelNumberFont` from `SongStatusPanel.cs`**

Edit `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs:25-26`:
- Delete the `USE_SPRITE_FONT` const line: `private const bool USE_SPRITE_FONT = false;`

Edit lines 73-74:
- Delete `// Level number bitmap font for difficulty level display` and `private BitmapFont _levelNumberFont;`

Find the `LoadLevelNumberFont` method (use `grep -n "LoadLevelNumberFont" DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`) and delete it entirely. Also delete any calls to it (typically in the panel's load/initialization method).

Edit `DrawDifficultyText` (around line 1032). Replace its body with:

```csharp
        private void DrawDifficultyText(SpriteBatch spriteBatch, string text, int x, int y, int cellWidth, int cellHeight, Color color)
        {
            const int nxTopOffset = 3;
            const int rightPadding = 10;

            var font = _smallFont ?? _font;
            if (font == null) return;

            var textWidth = font.MeasureString(text).X;
            DrawTextWithShadow(spriteBatch, font, text, new Vector2(x + cellWidth - textWidth - rightPadding, y + nxTopOffset), color);
        }
```

- [ ] **Step 2.2: Remove the BitmapFont using directive if no longer used**

Run:
```bash
grep -c "BitmapFont" DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs
```
Expected: 0. If non-zero, find and delete the remaining references.

- [ ] **Step 2.3: Update tests in `SongStatusPanelLogicTests.cs`**

Find every `BitmapFont` reference (`grep -n "BitmapFont\|_levelNumberFont" DTXMania.Test/UI/SongStatusPanelLogicTests.cs`). For each test:
- `Assert.False(GetField<BitmapFont>(panel, "_levelNumberFont").IsLoaded);` → DELETE the assertion (the field no longer exists; the behavior the assertion guarded is now unconditional).
- `public void LoadLevelNumberFont_WhenBitmapFontCreationFails_ShouldLeaveFontUnloaded()` → DELETE the entire test method.

Run:
```bash
grep -c "BitmapFont" DTXMania.Test/UI/SongStatusPanelLogicTests.cs
```
Expected: 0.

- [ ] **Step 2.4: Build and verify**

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
```
Expected: Build succeeds (the `BitmapFont` class still exists; only `SongStatusPanel`'s field is gone).

Note: `DTXMania.Test/UI/SongStatusPanelLogicTests.cs` is in the Mac excludes list — it won't run via the Mac test project. Verify it compiles for Windows by running:

```bash
dotnet build DTXMania.Test/DTXMania.Test.csproj 2>&1 | tail -5
```
Expected: Build succeeds.

- [ ] **Step 2.5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs DTXMania.Test/UI/SongStatusPanelLogicTests.cs
git commit -m "refactor: remove BitmapFont path from SongStatusPanel"
```

---

## Task 3: Migrate `SongSelectionStage` — drop `_bitmapFont`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:36,170-185`

- [ ] **Step 3.1: Identify all `_bitmapFont` and `BitmapFont` references**

Run:
```bash
grep -n "BitmapFont\|_bitmapFont" DTXMania.Game/Lib/Stage/SongSelectionStage.cs
```
Expected: A few lines including a field declaration, a load block around line 170, and any draw-call usage.

- [ ] **Step 3.2: Delete the field and load block**

Edit `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:36`:
- Delete `private BitmapFont _bitmapFont;`

Edit lines 170-185 (the bitmap font load try/catch block). Delete the whole block. The stage already has `IFont uiFont` initialized below — keep that path as the sole source of text rendering.

- [ ] **Step 3.3: Update any `_bitmapFont` draw calls**

For each remaining `_bitmapFont.DrawText(...)` call, replace with `uiFont.DrawString(spriteBatch, text, new Vector2(x, y), color)`. If the stage stored the `IFont` instance as a field for use across `OnDraw`, ensure it's accessible — it may already be wired through `_uiFont` or similar. Use `grep -n "uiFont\|IFont " DTXMania.Game/Lib/Stage/SongSelectionStage.cs` to confirm.

- [ ] **Step 3.4: Build**

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
```
Expected: Build succeeds.

- [ ] **Step 3.5: Run tests**

Run:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelection" 2>&1 | tail -10
```
Expected: All passing.

- [ ] **Step 3.6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "refactor: remove BitmapFont path from SongSelectionStage"
```

---

## Task 4: Migrate `ResultStage` from `BitmapFont` to `IFont`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Modify: `DTXMania.Test/Stage/ResultStageTests.cs`
- Modify: `DTXMania.Test/Stage/ResultStageCoverageTests.cs`

- [ ] **Step 4.1: Change field type and factory method**

In `DTXMania.Game/Lib/Stage/ResultStage.cs:36`:
- Change `private BitmapFont _resultFont;` → `private IFont _resultFont;`

In `DTXMania.Game/Lib/Stage/ResultStage.cs:198-202`, replace `CreateResultFont`:

```csharp
        internal virtual IFont CreateResultFont()
        {
            return _resourceManager.LoadFont("NotoSerifJP", 14);
        }
```

Find and update every `_resultFont.DrawText(...)` call in `ResultStage.cs`. Run:
```bash
grep -n "_resultFont" DTXMania.Game/Lib/Stage/ResultStage.cs
```

Replace each `_resultFont.DrawText(_spriteBatch, text, x, y, color, BitmapFont.FontType.Normal)` (or similar) with:
```csharp
_resultFont.DrawString(_spriteBatch, text, new Vector2(x, y), color);
```

Replace `_resultFont.MeasureText(text)` (if any) with `_resultFont.MeasureString(text)`.

Remove `using DTXMania.Game.Lib.Resources;` only if not used — likely it stays (for `IFont`). Check by removing and rebuilding if uncertain.

- [ ] **Step 4.2: Update `ResultStageTests.cs`**

In `DTXMania.Test/Stage/ResultStageTests.cs`, find `TrackingBitmapFont` (line ~624). The simplest path is to replace the tracking subclass with a `Mock<IFont>` and stop using `FormatterServices.GetUninitializedObject`. Delete:

```csharp
        private sealed class TrackingBitmapFont : BitmapFont
        {
            public TrackingBitmapFont() : base((IResourceManager)null!, new BitmapFont.BitmapFontConfig(), allowNullGraphicsDevice: true)
            { ... }
            ...
        }
```

Replace each `(TrackingBitmapFont)FormatterServices.GetUninitializedObject(typeof(TrackingBitmapFont))` usage with:
```csharp
var font = new Mock<IFont>().Object;
```

Update `Assert.Null(GetPrivateField<BitmapFont>(stage, "_resultFont"));` to `Assert.Null(GetPrivateField<IFont>(stage, "_resultFont"));`.

In the test-only stage subclass that overrides `CreateResultFont`, change its signature:
```csharp
        internal override IFont CreateResultFont()
        {
            return _trackedFont;
        }
```
Where `_trackedFont` is a `Mock<IFont>().Object` stored on the test subclass.

- [ ] **Step 4.3: Update `ResultStageCoverageTests.cs`**

In `DTXMania.Test/Stage/ResultStageCoverageTests.cs`, around line 232, change:
```csharp
internal override BitmapFont CreateResultFont()
```
to:
```csharp
internal override IFont CreateResultFont()
```

`SetPrivateField(stage, "_resultFont", null)` lines do not need changes (field is now `IFont`, still nullable as reference type).

- [ ] **Step 4.4: Build and run tests**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultStage" 2>&1 | tail -10
```
Expected: Build succeeds; ResultStage tests pass.

- [ ] **Step 4.5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ResultStage.cs DTXMania.Test/Stage/ResultStageTests.cs DTXMania.Test/Stage/ResultStageCoverageTests.cs
git commit -m "refactor: migrate ResultStage from BitmapFont to IFont"
```

---

## Task 5: Migrate `PerformanceStage` READY overlay to `IFont`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs:90,800-810,955-970`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

- [ ] **Step 5.1: Replace `_readyFont` field type**

In `DTXMania.Game/Lib/Stage/PerformanceStage.cs:90`:
- Change `private BitmapFont _readyFont = null!;` → `private IFont _readyFont = null!;`

- [ ] **Step 5.2: Replace the font-loading block**

Around lines 800-810, replace:
```csharp
                var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
                _readyFont = new BitmapFont(_spriteBatch.GraphicsDevice, _resourceManager, consoleFontConfig);
```

with:
```csharp
                _readyFont = _resourceManager.LoadFont("NotoSerifJP", 24);
```

Note: 24px chosen for overlay readability (spec section "Per-site mapping").

- [ ] **Step 5.3: Replace the draw call**

Around line 967, replace:
```csharp
                _readyFont.DrawText(_spriteBatch, text, textX, textY, color, BitmapFont.FontType.Normal, 0.1f);
```

with:
```csharp
                _readyFont.DrawString(_spriteBatch, text, new Vector2(textX, textY), color,
                    rotation: 0f, origin: Vector2.Zero, scale: Vector2.One, effects: SpriteEffects.None, layerDepth: 0.1f);
```

If `MeasureText` is called nearby for centering, change to `MeasureString` (signature is the same — both return `Vector2`).

- [ ] **Step 5.4: Update `PerformanceStageDeterministicTests.cs`**

Run:
```bash
grep -n "_readyFont\|BitmapFont" DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
```

Update every `GetPrivateField<BitmapFont>(stage, "_readyFont")` to `GetPrivateField<IFont>(stage, "_readyFont")`. The `SetPrivateField(stage, "_readyFont", null)` calls do not need type changes.

- [ ] **Step 5.5: Build and run tests**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
```
Expected: Build succeeds.

Note: `PerformanceStageDeterministicTests.cs` is NOT in the Mac excludes; it should run.
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceStageDeterministic" 2>&1 | tail -10
```
Expected: All passing.

- [ ] **Step 5.6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
git commit -m "refactor: migrate PerformanceStage READY overlay to IFont"
```

---

## Task 6: Migrate `ScrollSpeedIndicator` to `IFont`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs`
- Modify: `DTXMania.Test/Stage/Performance/ScrollSpeedIndicatorTests.cs`

- [ ] **Step 6.1: Update `ScrollSpeedIndicator.cs`**

Read the file first:
```bash
cat DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs
```

Make these changes:
- Line 16: `private readonly BitmapFont? _font;` → `private readonly IFont? _font;`
- Line 33: `public ScrollSpeedIndicator(BitmapFont? font)` → `public ScrollSpeedIndicator(IFont? font)`
- Line 64 (and any other draw calls): change `_font.DrawText(spriteBatch, text, x, y, color, BitmapFont.FontType.Normal, 0.0f)` to `_font.DrawString(spriteBatch, text, new Vector2(x, y), color)`. The 0.0f layer depth is the default for `DrawString(SpriteBatch, string, Vector2, Color)`.
- Change any `_font.MeasureText(...)` to `_font.MeasureString(...)`.
- Change any `_font.IsLoaded` checks to `_font != null` (the `IFont` interface has no `IsLoaded`; `ResourceManager.LoadFont` returns a fallback rather than failing, so the loaded-vs-not-loaded distinction collapses).

- [ ] **Step 6.2: Update callers of `ScrollSpeedIndicator` constructor**

Run:
```bash
grep -rn "new ScrollSpeedIndicator" DTXMania.Game/ DTXMania.Test/
```
Expected: At least one production caller (likely in `PerformanceStage`) and test callers. For each, ensure the argument is now an `IFont?` — typically the production caller can pass `_readyFont` (already migrated in Task 5) or load its own font.

For PerformanceStage specifically, search for `ScrollSpeedIndicator` and update:
```csharp
new ScrollSpeedIndicator(_readyFont)
```
(or whichever font is appropriate — if it had its own `BitmapFont`, load `_resourceManager.LoadFont("NotoSerifJP", 14)` and pass that).

- [ ] **Step 6.3: Update tests**

In `DTXMania.Test/Stage/Performance/ScrollSpeedIndicatorTests.cs`:

The current tests use `ReflectionHelpers.CreateUninitialized<BitmapFont>()` then assign config fields by reflection. Replace `CreateTestBitmapFont()` (around line 323) with:

```csharp
        private static IFont CreateTestFont()
        {
            return new Mock<IFont>().Object;
        }
```

Replace every `var font = CreateTestBitmapFont();` with `var font = CreateTestFont();`.

Remove the comments referring to "BitmapFont" in the class header (around lines 21-22): rewrite to reference `IFont`.

- [ ] **Step 6.4: Build and run**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ScrollSpeedIndicator" 2>&1 | tail -10
```
Expected: Build succeeds, tests pass.

- [ ] **Step 6.5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs DTXMania.Test/Stage/Performance/ScrollSpeedIndicatorTests.cs DTXMania.Game/Lib/Stage/PerformanceStage.cs
git commit -m "refactor: migrate ScrollSpeedIndicator to IFont"
```

---

## Task 7: Migrate `SongTransitionStage` level-number font to `IFont`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongTransitionStage.cs:43-44,490-500,580-620`

- [ ] **Step 7.1: Replace `_levelNumberFont` field**

In `DTXMania.Game/Lib/Stage/SongTransitionStage.cs:58` (the `_levelNumberFont` line):
- Change `private BitmapFont _levelNumberFont;` → `private IFont _levelNumberFont;`

- [ ] **Step 7.2: Replace the font load block**

Around lines 494-495:
```csharp
                var levelNumberConfig = BitmapFont.CreateLevelNumberFontConfig();
                _levelNumberFont = new BitmapFont(_game.GraphicsDevice, _resourceManager, levelNumberConfig);
```

Replace with:
```csharp
                _levelNumberFont = _resourceManager.LoadFont("NotoSerifJP", 24);
```

- [ ] **Step 7.3: Replace draw calls**

Run:
```bash
grep -n "_levelNumberFont" DTXMania.Game/Lib/Stage/SongTransitionStage.cs
```

For each `DrawText` call, replace with `DrawString(spriteBatch, text, new Vector2(x, y), color)`. For `MeasureText`, use `MeasureString`. For `IsLoaded` checks, use `_levelNumberFont != null`.

- [ ] **Step 7.4: Build**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
```
Expected: Build succeeds.

- [ ] **Step 7.5: Run smoke test (manual)**

```bash
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```
Navigate to a song, trigger transition, verify level number text appears (visual style will change from pixel-art digits to NotoSerifJP — this is expected per the spec).

- [ ] **Step 7.6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongTransitionStage.cs
git commit -m "refactor: migrate SongTransitionStage level font to IFont"
```

---

## Task 8: Migrate `StartupStage` to `IFont` + Bold

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/StartupStage.cs`
- Modify: `DTXMania.Test/Stage/StartupStageLogicTests.cs`

- [ ] **Step 8.1: Replace `_bitmapFont` field and add Bold**

In `DTXMania.Game/Lib/Stage/StartupStage.cs:46`:
- Change `private BitmapFont _bitmapFont;` → `private IFont _font;`
- Add immediately below: `private IFont _boldFont;`

- [ ] **Step 8.2: Replace the factory hook**

Find `CreateBitmapFontCore` (around line 135). Replace:
```csharp
        protected virtual BitmapFont CreateBitmapFontCore(GraphicsDevice graphicsDevice, IResourceManager resourceManager, BitmapFont.BitmapFontConfig config)
        {
            return new BitmapFont(graphicsDevice, resourceManager, config);
        }
```

with:
```csharp
        protected virtual IFont CreateFontCore(IResourceManager resourceManager, int size, FontStyle style)
        {
            return resourceManager.LoadFont("NotoSerifJP", size, style);
        }
```

- [ ] **Step 8.3: Replace the font-loading block**

Around lines 172-173:
```csharp
            var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
            _bitmapFont = CreateBitmapFontCore(graphicsDevice, _resourceManager, consoleFontConfig);
```

Replace with:
```csharp
            _font = CreateFontCore(_resourceManager, 14, FontStyle.Regular);
            _boldFont = CreateFontCore(_resourceManager, 14, FontStyle.Bold);
```

- [ ] **Step 8.4: Replace `DrawTextWithFallback` helper**

Around line 747, replace the signature:
```csharp
        private void DrawTextWithFallback(string text, int x, int y, BitmapFont.FontType fontType = BitmapFont.FontType.Normal, Color? fallbackColor = null)
```

with:
```csharp
        private void DrawTextWithFallback(string text, int x, int y, bool bold = false, Color? fallbackColor = null)
```

Inside the body, replace:
```csharp
                int fallbackHeight = fontType == BitmapFont.FontType.Thin ? FALLBACK_SMALL_FONT_HEIGHT : FALLBACK_FONT_HEIGHT;
```

with:
```csharp
                int fallbackHeight = bold ? FALLBACK_SMALL_FONT_HEIGHT : FALLBACK_FONT_HEIGHT;
```

And the bitmap-font draw call:
```csharp
                _bitmapFont.DrawText(_spriteBatch, versionText, x, y, Color.White, BitmapFont.FontType.Normal);
```

becomes:
```csharp
                (bold ? _boldFont : _font).DrawString(_spriteBatch, versionText, new Vector2(x, y), Color.White);
```

Around line 805, the call site for progress-message rendering changes from:
```csharp
DrawTextWithFallback(_currentProgressMessage, x, y, BitmapFont.FontType.Thin, Color.Yellow);
```

to:
```csharp
DrawTextWithFallback(_currentProgressMessage, x, y, bold: true, fallbackColor: Color.Yellow);
```

Audit the rest of the file for any other `BitmapFont.FontType.*` references and replace similarly (Thin → `bold: true`, Normal → `bold: false`).

- [ ] **Step 8.5: Update `StartupStageLogicTests.cs`**

Run:
```bash
grep -n "BitmapFont\|CreateBitmapFontCore\|_bitmapFont" DTXMania.Test/Stage/StartupStageLogicTests.cs
```

For each test:
- `Assert.Null(ReflectionHelpers.GetPrivateField<BitmapFont>(stage, "_bitmapFont"));` → `Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));`
- The subclass override at line 920: change
  ```csharp
  protected override BitmapFont CreateBitmapFontCore(GraphicsDevice graphicsDevice, IResourceManager resourceManager, BitmapFont.BitmapFontConfig config)
  ```
  to:
  ```csharp
  protected override IFont CreateFontCore(IResourceManager resourceManager, int size, FontStyle style)
  ```
  and have it return a `Mock<IFont>().Object`.

- [ ] **Step 8.6: Build and test**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
dotnet build DTXMania.Test/DTXMania.Test.csproj 2>&1 | tail -5
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~StartupStage" 2>&1 | tail -10
```
Expected: All builds succeed, tests pass.

- [ ] **Step 8.7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/StartupStage.cs DTXMania.Test/Stage/StartupStageLogicTests.cs
git commit -m "refactor: migrate StartupStage to IFont with Bold variant"
```

---

## Task 9: Migrate `ConfigStage` to `IFont` + Bold

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`

- [ ] **Step 9.1: Replace field declarations**

In `DTXMania.Game/Lib/Stage/ConfigStage.cs:54`:
- Change `private BitmapFont _bitmapFont;` → `private IFont _font;`
- Add: `private IFont _boldFont;`

- [ ] **Step 9.2: Replace the font-loading block**

Around lines 182-183:
```csharp
                var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
                _bitmapFont = new BitmapFont(graphicsDevice, _resourceManager, consoleFontConfig);
```

Replace with:
```csharp
                _font = _resourceManager.LoadFont("NotoSerifJP", 14);
                _boldFont = _resourceManager.LoadFont("NotoSerifJP", 14, FontStyle.Bold);
```

- [ ] **Step 9.3: Replace draw calls**

Run:
```bash
grep -n "_bitmapFont\|BitmapFont.FontType" DTXMania.Game/Lib/Stage/ConfigStage.cs
```

For each occurrence:
- `_bitmapFont.DrawText(_spriteBatch, titleText, x, y, Color.White, BitmapFont.FontType.Normal);` → `_font.DrawString(_spriteBatch, titleText, new Vector2(x, y), Color.White);`
- `var fontType = isSelected ? BitmapFont.FontType.Thin : BitmapFont.FontType.Normal;` → `var font = isSelected ? _boldFont : _font;`
- Subsequent `_bitmapFont.DrawText(_spriteBatch, text, x, y, color, fontType);` → `font.DrawString(_spriteBatch, text, new Vector2(x, y), color);`
- `_bitmapFont.MeasureText(...)` → use `_font.MeasureString(...)` (or `_boldFont.MeasureString` if measuring bold text).

- [ ] **Step 9.4: Update IKeyAssignPanel-related call sites**

`ConfigStage` calls `IKeyAssignPanel.Draw(_spriteBatch, _bitmapFont, _whitePixel, ...)`. Defer the interface signature change to Task 10, but rename the local field to keep the call compiling temporarily:

```csharp
// Pass both fonts so the panel can pick bold for emphasis (signature update in Task 10).
_activePanel?.Draw(_spriteBatch, _font, _boldFont, _whitePixel, viewportWidth, viewportHeight);
```

Or, if the interface change happens after this task: keep the call as `_activePanel?.Draw(_spriteBatch, /* regular */ _font, _whitePixel, ...)`. The expected interface signature is set in Task 10. Pick the path that lets Task 9 build green.

**Recommendation:** Do task 9 and task 10 as a single landing — change `IKeyAssignPanel.Draw` signature in Task 10 first, then this call site naturally needs both fonts. If you're committing per task as written, just do this call-site update at the end of Task 10 and skip it here.

- [ ] **Step 9.5: Update `ConfigStageLogicTests.cs`**

In `DTXMania.Test/Config/ConfigStageLogicTests.cs`, line 874:
```csharp
public void Draw(SpriteBatch spriteBatch, BitmapFont? bitmapFont, Texture2D? whitePixel, int viewportWidth, int viewportHeight)
```

This is a mock implementing `IKeyAssignPanel`. Update it to match the new signature (defined in Task 10):
```csharp
public void Draw(SpriteBatch spriteBatch, IFont? font, IFont? boldFont, Texture2D? whitePixel, int viewportWidth, int viewportHeight)
```

The test method around line 683 (`DrawTitle_WhenBitmapFontMissing_ShouldFallbackToRectangleDrawing`) tests fallback behavior. Rename to `DrawTitle_WhenFontMissing_ShouldFallbackToRectangleDrawing` and update field-name references from `_bitmapFont` to `_font`.

- [ ] **Step 9.6: Build (some failures expected if Task 10 not yet done)**

If you're doing Task 9 and Task 10 in sequence as a combined PR, defer the build check to after Task 10. Otherwise:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
```

If the `IKeyAssignPanel` signature mismatch surfaces, proceed to Task 10 immediately rather than committing here.

- [ ] **Step 9.7: Commit**

Defer commit until Task 10 lands together. If Task 10 isn't started, stash the changes:
```bash
git stash push -m "ConfigStage migration (pre-IKeyAssignPanel update)"
```
and proceed to Task 10. Reapply with `git stash pop` after Task 10 changes the interface.

---

## Task 10: Migrate `IKeyAssignPanel` interface + both implementations

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/IKeyAssignPanel.cs`
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/DrumKeyAssignPanel.cs`
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs` (caller — see Task 9 step 9.4)

- [ ] **Step 10.1: Update `IKeyAssignPanel.cs`**

Replace `DTXMania.Game/Lib/Stage/KeyAssign/IKeyAssignPanel.cs:39-40`:
```csharp
        void Draw(SpriteBatch spriteBatch, BitmapFont? bitmapFont, Texture2D? whitePixel,
                  int viewportWidth, int viewportHeight);
```

with:
```csharp
        void Draw(SpriteBatch spriteBatch, IFont? font, IFont? boldFont, Texture2D? whitePixel,
                  int viewportWidth, int viewportHeight);
```

- [ ] **Step 10.2: Update `DrumKeyAssignPanel.cs`**

In `DrumKeyAssignPanel.cs`, change the `Draw` method signature (around line 276) to match the interface. Inside, replace every `bitmapFont` reference with the appropriate font argument.

Update the `DrawText` helper (around line 339):
```csharp
        private static void DrawText(SpriteBatch sb, IFont? font, IFont? boldFont, string text, int x, int y,
            Color color, bool thin)
        {
            var picked = thin ? (boldFont ?? font) : font;
            if (picked == null) return;
            picked.DrawString(sb, text, new Vector2(x, y), color);
        }
```

(The parameter name `thin` is kept for minimal diff but now triggers Bold rendering — the public effect "emphasize this row" is preserved. Optionally rename `thin` → `bold` and flip the ternary; pick one and apply consistently.)

Update `DrawFooterRow` (around line 331) similarly to accept and forward both fonts.

Update every call within `Draw` to pass both fonts to `DrawText`:
```csharp
DrawText(spriteBatch, font, boldFont, "DRUM KEY MAPPING", panelX, y, Color.White, false);
// ... etc
```

- [ ] **Step 10.3: Update `SystemKeyAssignPanel.cs`**

Apply the same pattern as Task 10.2 to `SystemKeyAssignPanel.cs`. The signatures and helper methods follow the same shape; mirror the changes.

- [ ] **Step 10.4: Update `ConfigStage` caller**

In `ConfigStage`, ensure the call to the active panel passes both fonts:
```csharp
_activePanel?.Draw(_spriteBatch, _font, _boldFont, _whitePixel, viewportWidth, viewportHeight);
```

- [ ] **Step 10.5: Build (combined Task 9 + Task 10)**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
dotnet build DTXMania.Test/DTXMania.Test.csproj 2>&1 | tail -5
```
Expected: Both build green.

- [ ] **Step 10.6: Run ConfigStage tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStage" 2>&1 | tail -10
```
Expected: All passing.

- [ ] **Step 10.7: Commit combined Task 9 + Task 10**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs \
        DTXMania.Game/Lib/Stage/KeyAssign/IKeyAssignPanel.cs \
        DTXMania.Game/Lib/Stage/KeyAssign/DrumKeyAssignPanel.cs \
        DTXMania.Game/Lib/Stage/KeyAssign/SystemKeyAssignPanel.cs \
        DTXMania.Test/Config/ConfigStageLogicTests.cs
git commit -m "refactor: migrate ConfigStage and KeyAssign panels to IFont + Bold"
```

---

## Task 11: Migrate `JudgementTextPopupManager` to `IFont`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/JudgementTextPopup.cs:104-340`
- Modify: `DTXMania.Test/Stage/Performance/JudgementTextPopupLogicTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/JudgementTextPopupTests.cs` (Mac-excluded)

- [ ] **Step 11.1: Update `JudgementTextPopupManager` field, delegate type, and load helper**

In `DTXMania.Game/Lib/Stage/Performance/JudgementTextPopup.cs`, line 104:
- Change `private readonly BitmapFont? _font;` → `private readonly IFont? _font;`

Line 107:
- Change `private readonly Action<BitmapFont, SpriteBatch, string, int, int, Color> _drawText;` → `private readonly Action<IFont, SpriteBatch, string, int, int, Color> _drawText;`

Lines 142 and 145, 159 and 161 (constructor signatures): change `BitmapFont?` → `IFont?` and the `Action<BitmapFont, ...>` → `Action<IFont, ...>`.

Replace `LoadJudgementFont` (lines 304-326) with:

```csharp
        private static IFont? LoadJudgementFont(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            ArgumentNullException.ThrowIfNull(resourceManager);

            try
            {
                return resourceManager.LoadFont("NotoSerifJP", 48);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JudgementTextPopupManager: {ex.GetType().Name} loading judgement font: {ex.Message}");
                return null;
            }
        }
```

Delete `CreateJudgementTextFontConfig` (lines 328-332) entirely — it referenced `BitmapFont.CreateJudgementTextFontConfig()` which is going away.

Replace `DrawTextWithBitmapFont` (line 334) with:
```csharp
        private static void DrawTextWithFont(IFont font, SpriteBatch spriteBatch, string text, int x, int y, Color color)
        {
            font.DrawString(spriteBatch, text, new Vector2(x, y), color);
        }
```

Update the constructor body assignment at line 167:
```csharp
            _drawText = drawText ?? DrawTextWithFont;
```

- [ ] **Step 11.2: Update the Draw method**

In `Draw` (line 221-241), replace the `!_font.IsLoaded` check:
```csharp
            if (_disposed || spriteBatch == null || _font == null || !_font.IsLoaded)
                return;
```

with:
```csharp
            if (_disposed || spriteBatch == null || _font == null)
                return;
```

`IFont` has no `IsLoaded`; `ResourceManager.LoadFont` returns a fallback font instead of an unloaded sentinel.

- [ ] **Step 11.3: Update `Dispose`**

In the `Dispose(bool disposing)` override (around line 359), the line `_font?.Dispose();` remains valid (`IFont : IDisposable`). No change.

- [ ] **Step 11.4: Update `JudgementTextPopupLogicTests.cs`**

This test file has heavy `BitmapFont` usage. Rewrite the helpers (around lines 386-405):

```csharp
        private static IFont CreateLoadedFont()
        {
            var mock = new Mock<IFont>();
            // No need to set up "IsLoaded" — IFont has none, and the production code
            // no longer reads such a property.
            return mock.Object;
        }

        private static IFont CreateUnloadedFont()
        {
            // Returning null models the "load failed" case. Tests should expect the
            // manager's Draw() to early-return when _font == null.
            return null!;
        }

        private static Mock<IResourceManager> CreateFontResourceManager()
        {
            var rm = new Mock<IResourceManager>();
            rm.Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<FontStyle>()))
              .Returns(new Mock<IFont>().Object);
            rm.Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>()))
              .Returns(new Mock<IFont>().Object);
            return rm;
        }
```

Replace every `using var font = CreateLoadedBitmapFont();` with `var font = CreateLoadedFont();` (and remove `using` — mocked `IFont` instances don't require disposal). Replace `CreateUnloadedBitmapFont` similarly.

Update the reflection field types at line 234 and 306:
```csharp
var drawText = ReflectionHelpers.GetPrivateField<Action<IFont, SpriteBatch, string, int, int, Color>>(manager, "_drawText");
```

Update field-name reflection at line 305:
```csharp
var font = ReflectionHelpers.GetPrivateField<IFont>(manager, "_font");
```

Update test name assertions at lines 239 and 311:
```csharp
Assert.Equal("DrawTextWithFont", drawText!.Method.Name);
```

The test at line 280 (`LoadJudgementFont_WhenBitmapFontLoads_ShouldReturnFont`) — rename to `LoadJudgementFont_WhenFontLoads_ShouldReturnFont` and update the cast at line 290 from `(BitmapFont?)` to `(IFont?)`.

Delete the test at line 229 (`Draw_WhenUsingDefaultDrawHelper_ShouldInvokeBitmapFontPath`) — its naming/intent ties to a no-longer-existing branch; the equivalent assertion is "Draw uses the default `DrawTextWithFont` helper" which is covered by the existing helper-name assertion.

The constructor parameter list in `CreateForTesting` (line 365-370) — update types from `BitmapFont?` to `IFont?` and the `Action<BitmapFont, ...>` to `Action<IFont, ...>`.

- [ ] **Step 11.5: Update `JudgementTextPopupTests.cs` (Mac-excluded)**

Run:
```bash
grep -n "BitmapFont\|FontType" DTXMania.Test/Stage/Performance/JudgementTextPopupTests.cs
```

For each reference, apply the same pattern as Task 11.4. This file requires a real `GraphicsDevice` so it's excluded on Mac; verify it compiles via the Windows test project:

```bash
dotnet build DTXMania.Test/DTXMania.Test.csproj 2>&1 | tail -5
```

- [ ] **Step 11.6: Build and test**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
dotnet build DTXMania.Test/DTXMania.Test.csproj 2>&1 | tail -5
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~JudgementTextPopup" 2>&1 | tail -10
```
Expected: All builds green; JudgementTextPopupLogicTests passes (JudgementTextPopupTests is Mac-excluded).

- [ ] **Step 11.7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/JudgementTextPopup.cs \
        DTXMania.Test/Stage/Performance/JudgementTextPopupLogicTests.cs \
        DTXMania.Test/Stage/Performance/JudgementTextPopupTests.cs
git commit -m "refactor: migrate JudgementTextPopup to IFont"
```

---

## Task 12: Delete `BitmapFont.cs` and its test files

**Files:**
- Delete: `DTXMania.Game/Lib/Resources/BitmapFont.cs`
- Delete: `DTXMania.Test/Resources/BitmapFontTests.cs`
- Delete: `DTXMania.Test/Resources/BitmapFontConfigTests.cs`
- Modify: `DTXMania.Test/DTXMania.Test.Mac.csproj`

- [ ] **Step 12.1: Verify no remaining references**

Run:
```bash
grep -rn "BitmapFont" DTXMania.Game/ DTXMania.Test/ MCP/ 2>/dev/null | grep -v "/bin/\|/obj/" | head -20
```
Expected: No matches (or only matches inside `BitmapFont.cs` itself, which we're about to delete). If any matches in other files exist, **stop** — go back to the appropriate task and finish migration.

- [ ] **Step 12.2: Delete the source files**

```bash
rm DTXMania.Game/Lib/Resources/BitmapFont.cs
rm DTXMania.Test/Resources/BitmapFontTests.cs
rm DTXMania.Test/Resources/BitmapFontConfigTests.cs
```

- [ ] **Step 12.3: Update Mac test csproj `<Compile Include>`**

Edit `DTXMania.Test/DTXMania.Test.Mac.csproj`. The current exclusion list contains `Resources/BitmapFontTests.cs`. Open the file and remove that exact substring from the `Exclude` attribute on the `<Compile Include="**/*.cs" Exclude="...">` element.

Before (relevant portion):
```xml
Exclude="bin/**/*.cs;obj/**/*.cs;Graphics/RenderTargetManagerTests.cs;Graphics/GPURenderingSnapshotTests.cs;Resources/BitmapFontTests.cs;Performance/StressTestRunner.cs;...
```

After:
```xml
Exclude="bin/**/*.cs;obj/**/*.cs;Graphics/RenderTargetManagerTests.cs;Graphics/GPURenderingSnapshotTests.cs;Performance/StressTestRunner.cs;...
```

(Just remove the `Resources/BitmapFontTests.cs;` substring. `BitmapFontConfigTests.cs` was never in the exclude list since it didn't require graphics.)

- [ ] **Step 12.4: Build both projects**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj 2>&1 | tail -5
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj 2>&1 | tail -5
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj 2>&1 | tail -5
dotnet build DTXMania.Test/DTXMania.Test.csproj 2>&1 | tail -5
```
Expected: All four builds succeed.

- [ ] **Step 12.5: Run full Mac test suite**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj 2>&1 | tail -15
```
Expected: All tests pass.

- [ ] **Step 12.6: Commit**

```bash
git add -u DTXMania.Game/Lib/Resources/BitmapFont.cs \
           DTXMania.Test/Resources/BitmapFontTests.cs \
           DTXMania.Test/Resources/BitmapFontConfigTests.cs \
           DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "refactor: delete BitmapFont and its tests"
```

---

## Task 13: Final smoke test

**Files:** None modified.

- [ ] **Step 13.1: Run the game**

```bash
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

Manual checks (visual style will differ from pre-migration — that's expected):
- Startup screen shows version text and progress messages.
- Title → song select → status panel shows level number text (now NotoSerifJP 24).
- Song transition stage shows title/artist + level number.
- Performance stage: READY/GO overlay appears at size 24.
- Performance stage: judgements (Perfect/Great/Good/OK/Miss) appear as size-48 popups.
- Config stage menus render; selected items are in bold.
- KeyAssign panel (drum and system) renders; selected rows in bold.
- Scroll speed indicator shows the current value as plain text.
- Result stage renders score text.

- [ ] **Step 13.2: Run Mac test suite end-to-end**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj 2>&1 | tail -15
```
Expected: All tests pass.

- [ ] **Step 13.3: Run Windows test suite if possible**

If a Windows environment is available:
```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj 2>&1 | tail -15
```

Otherwise, rely on CI (`build-and-test.yml`) to catch regressions on the Windows-only test files (`SongStatusPanelLogicTests.cs`, `JudgementTextPopupTests.cs`, etc.).

- [ ] **Step 13.4: No commit needed** — this is a verification-only task.

---

## Self-review notes

1. **Spec coverage:** Every "Per-site mapping" row in the spec maps to a task here: Tasks 2-11 cover all 11 consumers, Task 1 covers Bold asset + factory, Task 12 covers final removal. ✓
2. **Placeholder scan:** No "TBD", "TODO", "implement later" — each step has executable content or specific instructions. ✓
3. **Type consistency:** Field renames are consistent (`_bitmapFont` → `_font` in `StartupStage`/`ConfigStage`/`SongSelectionStage`; `_levelNumberFont` kept name but retyped in `SongTransitionStage`; `_readyFont` and `_resultFont` retain names). Method renames (`CreateBitmapFontCore` → `CreateFontCore`, `DrawTextWithBitmapFont` → `DrawTextWithFont`) are consistent across production code, test overrides, and assertion strings. ✓
4. **Task ordering risk:** Tasks 9 and 10 are coupled by the `IKeyAssignPanel.Draw` signature change. The plan calls this out and recommends landing them together. ✓
