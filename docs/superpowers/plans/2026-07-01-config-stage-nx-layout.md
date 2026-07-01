# Config Stage NX Layout Refinement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refine the Config stage to match DTXManiaNX's visual language — aligned category menu, correctly proportioned scrolling item rows with per-cell legible text, and a readable description panel — while adding a scrolling viewport so the list scales as categories grow.

**Architecture:** Pure layout math (menu metrics, item scroll positions, description positions) lives in `ConfigUILayout` and is unit-tested without a GraphicsDevice. `ConfigStage` holds a single eased scroll position `_itemScroll` that tracks `SelectedIndex` (wrap-snapped) and renders items through the pure helpers. The selected item is locked to a fixed focus row with a fixed cursor; items scroll under it. Draw methods are visually verified; logic is covered by existing `ConfigStage*Tests`.

**Tech Stack:** .NET 8, MonoGame 3.8 (SpriteBatch), xUnit + Moq.

## Global Constraints

- Virtual coordinate space is 1280×720 (copied verbatim from spec / `ConfigUILayout`).
- Runtime skin art is present at `~/Library/Application Support/DTXManiaCX/System/Graphics/`; every draw stays null-guarded with the existing fallback fill (no behavior change if art is absent).
- Draw/layout only: no change to input handling, navigation, value editing, deferred save, or NX-score import.
- Mac test project is `DTXMania.Test/DTXMania.Test.Mac.csproj`; `ConfigUILayoutTests` runs there (pure, no GraphicsDevice).
- Follow `.editorconfig`: 4-space indent, LF, `_camelCase` private fields.

---

## Reference constants (authoritative for all tasks)

Menu (left categories):
- `MenuPanelRect = (245,140,180,172)` (unchanged)
- `MenuLabelCenterX = 335`, `MenuCursorX = 250`, `MenuCursorWidth = 170`, `MenuCursorHeight = 32`
- `MenuFirstCursorY = 148`, `MenuRowStride = 32`
- `MenuCursorRect(i) = (250, 148 + i*32, 170, 32)`

Item list (scrolling viewport):
- `ItemListX = 420`, `ItemBoxHeight = 80`, `ItemBoxNormalWidth = 538`, `ItemBoxOtherWidth = 438`
- `ItemRowStride = 67`, `ItemFocusRowTopY = 189`
- `ItemVisibleTopY = 105`, `ItemVisibleBottomY = 690`
- `ItemNameOffsetX = 20`, `ItemValueOffsetX = 260`, `ItemTextOffsetY = 24`, `ItemValueMaxWidth = 116`
- `ItemCursorRect = (413, 193, 497, 68)`
- `RowTopY(i, p) = ItemFocusRowTopY + round((i - p) * ItemRowStride)`
- `IsRowVisible(rowTopY) = (rowTopY + ItemBoxHeight) > ItemVisibleTopY && rowTopY < ItemVisibleBottomY`

Description panel:
- `DescriptionPanelRect = (800,270,280,360)` (unchanged)
- `DescriptionTitlePos = (818, 300)` (white top → dark text)
- `DescriptionBodyPos = (818, 448)` (black bottom → light text)
- `DescriptionWrapWidth = 248`, `DescriptionLineHeight = 22`

Colors (added to `ConfigStage`):
- `LightText = (235,238,248)` (existing) — names on dark cell, menu labels on purple, description body on black
- `ValueDarkText = (24,24,32)` — value on white cell
- `SelectedNameText = (255,238,120)` — selected name / nav marker on dark
- `SelectedValueText = (168,52,0)` — selected value on white cell
- `SelectedMenuText = (36,24,72)` — selected menu label on light cursor
- `DescriptionTitleText = (24,24,32)` — title on white top

---

## Task 1: Menu metrics in `ConfigUILayout`

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs`
- Test: `DTXMania.Test/UI/ConfigUILayoutTests.cs`

**Interfaces:**
- Produces: `MenuLabelCenterX` (int), `MenuFirstCursorY` (int), `MenuRowStride` (int), `MenuCursorRect(int) -> Rectangle`.

- [ ] **Step 1: Replace the menu test with the new metrics**

In `ConfigUILayoutTests.cs`, replace `MenuRowY_ShouldStepBy34` and `MenuCursorRect_ShouldSitOnSelectedRow` with:

```csharp
[Theory]
[InlineData(0, 148)]
[InlineData(1, 180)]
[InlineData(2, 212)]
public void MenuCursorRect_ShouldStepBy32(int index, int expectedY)
{
    Assert.Equal(new Rectangle(250, expectedY, 170, 32), ConfigUILayout.MenuCursorRect(index));
}

[Fact]
public void MenuLabelCenterX_ShouldBePanelCenter()
{
    Assert.Equal(335, ConfigUILayout.MenuLabelCenterX);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: FAIL (compile error / old values).

- [ ] **Step 3: Update `ConfigUILayout` menu section**

Replace the "Left category menu" block with:

```csharp
// Left category menu.
public static Rectangle MenuPanelRect => new(245, 140, 180, 172);
public const int MenuLabelCenterX = 335;
public const int MenuCursorX = 250;
public const int MenuCursorWidth = 170;
public const int MenuCursorHeight = 32;
public const int MenuFirstCursorY = 148;
public const int MenuRowStride = 32;
public static Rectangle MenuCursorRect(int index) =>
    new(MenuCursorX, MenuFirstCursorY + index * MenuRowStride, MenuCursorWidth, MenuCursorHeight);
```

Remove the old `MenuFirstRowY`, `MenuRowStride` (old value), `MenuCursorWidth/Height` (old), `MenuRowY`, and the old `MenuCursorRect`.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: PASS (menu tests; other tests may still fail — updated in later tasks).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs DTXMania.Test/UI/ConfigUILayoutTests.cs
git commit -m "refactor: align config menu metrics to NX (stride 32, panel-center labels)"
```

---

## Task 2: Item scroll math in `ConfigUILayout`

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs`
- Test: `DTXMania.Test/UI/ConfigUILayoutTests.cs`

**Interfaces:**
- Produces: `ItemListX`, `ItemBoxHeight`, `ItemBoxNormalWidth`, `ItemBoxOtherWidth`, `ItemRowStride`, `ItemFocusRowTopY`, `ItemValueMaxWidth` (all int consts); `ItemCursorRect` (Rectangle); `RowTopY(int index, double scroll) -> int`; `IsRowVisible(int rowTopY) -> bool`; `ItemBoxRect(int rowTopY, int width) -> Rectangle`; `ItemNamePos(int rowTopY) -> Vector2`; `ItemValuePos(int rowTopY) -> Vector2`.

- [ ] **Step 1: Replace the item-list tests**

In `ConfigUILayoutTests.cs`, replace `ItemRowRect_ShouldStepBy60`, `ItemCursorRect_ShouldFrameTheRow`, `ItemTextPositions_ShouldOffsetFromRow`, and `ItemValueRightX_ShouldSitInsideTheBoxClearOfTheDescriptionPanel` with:

```csharp
[Fact]
public void RowTopY_ShouldCenterSelectedItemWhenSettled()
{
    // p == selected index -> that row sits at the focus row.
    Assert.Equal(189, ConfigUILayout.RowTopY(3, 3.0));
    Assert.Equal(189, ConfigUILayout.RowTopY(0, 0.0));
}

[Fact]
public void RowTopY_ShouldStepByStride()
{
    Assert.Equal(189 + 67, ConfigUILayout.RowTopY(4, 3.0));
    Assert.Equal(189 - 67, ConfigUILayout.RowTopY(2, 3.0));
}

[Theory]
[InlineData(189, true)]    // focus row
[InlineData(690, false)]   // top at footer edge -> below band
[InlineData(-213, false)]  // far above -> hidden behind header
[InlineData(60, true)]     // top above header bottom but box still intrudes band
public void IsRowVisible_ShouldGateOnVisibleBand(int rowTopY, bool expected)
{
    Assert.Equal(expected, ConfigUILayout.IsRowVisible(rowTopY));
}

[Fact]
public void ItemBoxAndTextPositions_ShouldMatchNx()
{
    Assert.Equal(new Rectangle(420, 189, 538, 80), ConfigUILayout.ItemBoxRect(189, ConfigUILayout.ItemBoxNormalWidth));
    Assert.Equal(new Rectangle(420, 189, 438, 80), ConfigUILayout.ItemBoxRect(189, ConfigUILayout.ItemBoxOtherWidth));
    Assert.Equal(new Vector2(440, 213), ConfigUILayout.ItemNamePos(189));
    Assert.Equal(new Vector2(680, 213), ConfigUILayout.ItemValuePos(189));
    Assert.Equal(new Rectangle(413, 193, 497, 68), ConfigUILayout.ItemCursorRect);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: FAIL (members undefined).

- [ ] **Step 3: Replace the item-list section of `ConfigUILayout`**

Replace the "Right item list" block with:

```csharp
// Right item list — scrolling viewport (selected item locked to the focus row).
public const int ItemListX = 420;
public const int ItemBoxHeight = 80;
public const int ItemBoxNormalWidth = 538;   // 4_itembox.png: dark name cell + white value cell
public const int ItemBoxOtherWidth = 438;    // 4_itembox other.png: single dark cell (navigation)
public const int ItemRowStride = 67;         // NX stride; boxes overlap 13px as the art tiles
public const int ItemFocusRowTopY = 189;     // panel-top Y of the centered/selected row
public const int ItemVisibleTopY = 105;      // header bottom
public const int ItemVisibleBottomY = 690;   // footer top
public const int ItemNameOffsetX = 20;
public const int ItemValueOffsetX = 260;
public const int ItemTextOffsetY = 24;
// Value text left-aligns at ItemListX+ItemValueOffsetX (680) and must stay left of the
// description panel (x=800), so cap its width. 800 - 680 - 4 margin = 116.
public const int ItemValueMaxWidth = 116;
// Selection cursor is fixed at the focus row; items scroll under it (NX 4_itembox cursor.png 497x68).
public static Rectangle ItemCursorRect => new(413, 193, 497, 68);

// Panel-top Y of item `index` given the eased scroll position `scroll` (fractional index at focus).
public static int RowTopY(int index, double scroll) =>
    ItemFocusRowTopY + (int)System.Math.Round((index - scroll) * ItemRowStride);

// True when the row's box intersects the visible band between header and footer.
public static bool IsRowVisible(int rowTopY) =>
    (rowTopY + ItemBoxHeight) > ItemVisibleTopY && rowTopY < ItemVisibleBottomY;

public static Rectangle ItemBoxRect(int rowTopY, int width) =>
    new(ItemListX, rowTopY, width, ItemBoxHeight);
public static Vector2 ItemNamePos(int rowTopY) =>
    new(ItemListX + ItemNameOffsetX, rowTopY + ItemTextOffsetY);
public static Vector2 ItemValuePos(int rowTopY) =>
    new(ItemListX + ItemValueOffsetX, rowTopY + ItemTextOffsetY);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: PASS for the item tests (description test updated next task).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs DTXMania.Test/UI/ConfigUILayoutTests.cs
git commit -m "feat: add NX scrolling-viewport item layout math to ConfigUILayout"
```

---

## Task 3: Description title/body positions in `ConfigUILayout`

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs`
- Test: `DTXMania.Test/UI/ConfigUILayoutTests.cs`

**Interfaces:**
- Produces: `DescriptionTitlePos` (Vector2), `DescriptionBodyPos` (Vector2), `DescriptionWrapWidth` (int), `DescriptionLineHeight` (int). Removes `DescriptionTextPos`.

- [ ] **Step 1: Replace the description test**

Replace `DescriptionText_ShouldStartInsidePanel` with:

```csharp
[Fact]
public void DescriptionTitleAndBody_ShouldSitOnCorrectArtCells()
{
    // Title on the white upper region; body on the black lower region.
    Assert.Equal(new Vector2(818, 300), ConfigUILayout.DescriptionTitlePos);
    Assert.Equal(new Vector2(818, 448), ConfigUILayout.DescriptionBodyPos);
    Assert.Equal(248, ConfigUILayout.DescriptionWrapWidth);
    Assert.True(ConfigUILayout.DescriptionBodyPos.Y > ConfigUILayout.DescriptionTitlePos.Y);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: FAIL (`DescriptionTitlePos` undefined).

- [ ] **Step 3: Update the description section of `ConfigUILayout`**

Replace the "Description panel" block with:

```csharp
// Description panel — art is white (top) over black (bottom); use both cells.
public static Rectangle DescriptionPanelRect => new(800, 270, 280, 360);
public static Vector2 DescriptionTitlePos => new(818, 300);   // white upper region -> dark text
public static Vector2 DescriptionBodyPos => new(818, 448);    // black lower region -> light text
public const int DescriptionWrapWidth = 248;
public const int DescriptionLineHeight = 22;
```

- [ ] **Step 4: Run to verify the full layout test file passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: PASS (all layout tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs DTXMania.Test/UI/ConfigUILayoutTests.cs
git commit -m "feat: split config description panel into title/body art cells"
```

---

## Task 4: Colors + menu draw in `ConfigStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs` (color fields near line 74; `DrawCategoryMenu` ~line 698)

**Interfaces:**
- Consumes: `ConfigUILayout.MenuCursorRect`, `MenuLabelCenterX`, `MenuCursorHeight` (Task 1).
- Produces: new `Color` fields `ValueDarkText`, `SelectedNameText`, `SelectedValueText`, `SelectedMenuText`, `DescriptionTitleText`.

- [ ] **Step 1: Add color constants**

After the existing `ValueText` field, add:

```csharp
// Value text sits on the itembox's white right cell, so it must be dark.
private static readonly Color ValueDarkText = new(24, 24, 32);
// Selected name/nav marker sit on a dark cell -> bright warm highlight.
private static readonly Color SelectedNameText = new(255, 238, 120);
// Selected value sits on the white cell -> dark warm highlight (readable on white).
private static readonly Color SelectedValueText = new(168, 52, 0);
// Selected menu label sits on the light menu cursor bar -> dark text.
private static readonly Color SelectedMenuText = new(36, 24, 72);
// Description title sits on the panel's white upper region -> dark text.
private static readonly Color DescriptionTitleText = new(24, 24, 32);
```

- [ ] **Step 2: Rewrite `DrawCategoryMenu` label loop for alignment + selected color**

Replace the label loop (the `for` over `_categories`) with:

```csharp
for (int i = 0; i < _categories.Count; i++)
{
    bool selected = i == _currentCategoryIndex;
    var font = selected ? _boldFont : _font;
    var color = selected ? SelectedMenuText : LightText;
    var label = _categories[i].Name;
    var size = font.MeasureString(label);
    var cursor = ConfigUILayout.MenuCursorRect(i);
    // Center the label horizontally on the panel and vertically within the cursor band.
    var pos = new Vector2(
        ConfigUILayout.MenuLabelCenterX - size.X / 2f,
        cursor.Y + (ConfigUILayout.MenuCursorHeight - size.Y) / 2f);
    font.DrawString(_spriteBatch, label, pos, color);
}
```

Also update the cursor draw in `DrawCategoryMenu` to use `ConfigUILayout.MenuCursorRect(_currentCategoryIndex)` (already does via `cursorRect` — confirm it still compiles after Task 1 removed `MenuRowY`).

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded (no references to removed `MenuRowY`).

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs
git commit -m "feat: align config menu labels and darken selected label for the light cursor"
```

---

## Task 5: Scroll state + scrolling item list in `ConfigStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs` (`OnActivate`, `OnUpdate`, `DrawItemList`, fields)

**Interfaces:**
- Consumes: `ConfigUILayout.RowTopY`, `IsRowVisible`, `ItemBoxRect`, `ItemNamePos`, `ItemValuePos`, `ItemCursorRect`, `ItemBoxNormalWidth`, `ItemBoxOtherWidth`, `ItemValueMaxWidth` (Task 2); colors (Task 4).
- Produces: `_itemScroll` (double) eased toward `SelectedIndex`.

- [ ] **Step 1: Add the scroll field**

After `private bool _focusOnMenu = true;` add:

```csharp
// Eased scroll position (fractional item index currently at the focus row). Tracks the
// selected item; snaps on a wrap/jump so last<->first never scrolls the whole list.
private double _itemScroll;
```

- [ ] **Step 2: Reset scroll on activate**

In `OnActivate`, after `_focusOnMenu = true;` add:

```csharp
_itemScroll = 0;
```

- [ ] **Step 3: Step the scroll in `OnUpdate`**

In `OnUpdate`, after the `HandleInput();` call (and only when no active panel), add a step. Replace the tail of `OnUpdate` so it reads:

```csharp
HandleInput();
UpdateItemScroll(deltaTime);
```

Then add the method:

```csharp
private void UpdateItemScroll(double deltaTime)
{
    if (_categories.Count == 0)
        return;
    var category = _categories[_currentCategoryIndex];
    double target = category.HasItems ? category.SelectedIndex : 0;
    double delta = target - _itemScroll;
    if (System.Math.Abs(delta) > 1.5)
    {
        _itemScroll = target;   // wrap / multi-row jump: snap, don't scroll the whole list
        return;
    }
    double factor = System.Math.Min(1.0, deltaTime * 15.0);
    _itemScroll += delta * factor;
    if (System.Math.Abs(target - _itemScroll) < 0.01)
        _itemScroll = target;
}
```

- [ ] **Step 4: Rewrite `DrawItemList` as a scrolling viewport**

Replace the body of `DrawItemList` (after the `if (_categories.Count == 0) return;` guard) with:

```csharp
var category = _categories[_currentCategoryIndex];
var items = category.Items;

// Box pass.
for (int i = 0; i < items.Count; i++)
{
    int rowTopY = ConfigUILayout.RowTopY(i, _itemScroll);
    if (!ConfigUILayout.IsRowVisible(rowTopY))
        continue;
    bool isNav = items[i] is NavigationConfigItem;
    var boxTex = isNav ? _itemBoxOtherTexture : _itemBoxTexture;
    int boxWidth = isNav ? ConfigUILayout.ItemBoxOtherWidth : ConfigUILayout.ItemBoxNormalWidth;
    var boxRect = ConfigUILayout.ItemBoxRect(rowTopY, boxWidth);
    if (boxTex?.Texture != null)
        _spriteBatch.Draw(boxTex.Texture, boxRect, Color.White);
    else
        DrawFilledRectangle(boxRect, ItemBoxFallbackColor);
}

// Fixed cursor at the focus row (items scroll under it).
if (!_focusOnMenu && category.HasItems)
{
    if (_itemBoxCursorTexture?.Texture != null)
        _spriteBatch.Draw(_itemBoxCursorTexture.Texture, ConfigUILayout.ItemCursorRect, Color.White);
    else
        DrawFilledRectangle(ConfigUILayout.ItemCursorRect, ItemCursorFallback);
}

if (_font == null || _boldFont == null)
    return;

// Text pass.
for (int i = 0; i < items.Count; i++)
{
    int rowTopY = ConfigUILayout.RowTopY(i, _itemScroll);
    if (!ConfigUILayout.IsRowVisible(rowTopY))
        continue;
    var item = items[i];
    bool selected = !_focusOnMenu && i == category.SelectedIndex;
    bool isNav = item is NavigationConfigItem;
    var font = selected ? _boldFont : _font;

    font.DrawString(_spriteBatch, item.Name, ConfigUILayout.ItemNamePos(rowTopY),
        selected ? SelectedNameText : LightText);

    var value = GetItemValueText(item);
    if (!string.IsNullOrEmpty(value))
    {
        var displayValue = TextHelper.TruncateToWidth(value, ConfigUILayout.ItemValueMaxWidth, font);
        var valuePos = ConfigUILayout.ItemValuePos(rowTopY);
        // Nav marker (">") sits on the dark "other" box -> light; real values sit on the
        // itembox white cell -> dark.
        Color valueColor = isNav
            ? (selected ? SelectedNameText : LightText)
            : (selected ? SelectedValueText : ValueDarkText);
        font.DrawString(_spriteBatch, displayValue, valuePos, valueColor);
    }
}
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Run the config logic tests (no behavior regressions)**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStage"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs
git commit -m "feat: scroll config item list with NX centered focus and per-cell text colors"
```

---

## Task 6: Description panel title/body in `ConfigStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs` (`DrawDescriptionPanel` ~line 819)

**Interfaces:**
- Consumes: `ConfigUILayout.DescriptionTitlePos`, `DescriptionBodyPos`, `DescriptionWrapWidth`, `DescriptionLineHeight`, `DescriptionPanelRect`; `DescriptionTitleText`, `LightText`.

- [ ] **Step 1: Rewrite the text portion of `DrawDescriptionPanel`**

Keep the panel-background draw. Replace everything after it (from `var category = ...`) with:

```csharp
var category = _categories[_currentCategoryIndex];

// Title: the focused item's name (or the category name on menu focus) on the white upper cell.
string title = _focusOnMenu
    ? category.Name
    : (category.SelectedItem?.Name ?? category.Name);
if (!string.IsNullOrEmpty(title) && _boldFont != null)
    _boldFont.DrawString(_spriteBatch, title, ConfigUILayout.DescriptionTitlePos, DescriptionTitleText);

// Body: the description text, wrapped, on the black lower cell.
string text = _focusOnMenu
    ? category.Description
    : (category.SelectedItem?.Description ?? string.Empty);
if (string.IsNullOrEmpty(text) || _font == null)
    return;

var pos = ConfigUILayout.DescriptionBodyPos;
foreach (var line in WrapText(_font, text, ConfigUILayout.DescriptionWrapWidth))
{
    _font.DrawString(_spriteBatch, line, pos, LightText);
    pos.Y += ConfigUILayout.DescriptionLineHeight;
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded (no remaining references to removed `DescriptionTextPos`).

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs
git commit -m "feat: render config description as title on white cell + body on black cell"
```

---

## Task 7: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS (all tests, including `ConfigUILayoutTests` and `ConfigStage*Tests`).

- [ ] **Step 2: Visual verification**

Launch the game, open Config (Title → Config), and confirm each state:
- Menu labels are centered and vertically aligned in their cursor band; the selected label is dark on the light cursor.
- Item rows render full-size (no squish); names are light on the dark cell; values are dark on the white cell; the selected row is framed by the fixed cursor at center; selected name is warm yellow, selected value dark orange.
- Scroll to the last item (System has 7): the selected item stays centered, rows scroll under the cursor, and no duplicate rows appear.
- The read-only "DTX Folder" row shows its path in the white value cell (dark, truncated).
- Navigation rows ("System Key Mapping", "Import NX Scores", "Drum Key Mapping") show a light ">" on the dark "other" box.
- Description panel: bold title on the white upper region (dark), wrapped body on the black lower region (light).

Fine-tune `ItemValueOffsetX`, `ItemNameOffsetX`, `ItemTextOffsetY`, `DescriptionTitlePos`/`DescriptionBodyPos` in `ConfigUILayout` (and the matching test expectations) if a screenshot shows any text off its cell.

- [ ] **Step 3: Final commit if any tuning was applied**

```bash
git add -A
git commit -m "fix: tune config text offsets against visual verification"
```

---

## Self-Review

**Spec coverage:**
- Menu alignment → Task 1 + Task 4. ✓
- Text readability (value on white, selected colors, menu selected, description) → Task 4, 5, 6. ✓
- "Missing" background / squished rows → Task 2 + Task 5 (native box sizes). ✓
- Scrolling for future expansion → Task 2 (`RowTopY`/`IsRowVisible`) + Task 5 (`_itemScroll`, wrap-snap). ✓
- Tests updated → Tasks 1–3; regressions guarded by Task 5 step 6 / Task 7. ✓

**Placeholder scan:** none — every step has concrete code/constants.

**Type consistency:** `RowTopY(int,double)`, `IsRowVisible(int)`, `ItemBoxRect(int,int)`, `ItemNamePos(int)`, `ItemValuePos(int)`, `MenuCursorRect(int)`, `_itemScroll` (double) used identically across Tasks 2/5. Color names (`ValueDarkText`, `SelectedNameText`, `SelectedValueText`, `SelectedMenuText`, `DescriptionTitleText`) defined in Task 4 and consumed in Tasks 5/6. ✓
