# Config Stage NX Layout Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `ConfigStage` from a single flat list into the DTXManiaNX two-column master-detail layout (left category menu, right per-category item list, description panel, header/footer).

**Architecture:** Extract pure, fully-testable helpers — `ConfigUILayout` (coordinate math) and `ConfigCategory` (model) — and render the NX layout *in-stage* through `ConfigStage`'s existing overridable draw seam (`BeginDrawFrame`/`EndDrawFrame`/`DrawFilledRectangle`/`GetViewport` + null-guarded texture draws), so all tests stay Mac-safe with no GraphicsDevice. Categories are System / Drums / Exit; key mapping reuses the existing `DrumConfigStage` and `SystemKeyAssignPanel`; persist-on-edit is unchanged.

**Tech Stack:** .NET 8, MonoGame 3.8 (`Microsoft.Xna.Framework`), xUnit + Moq, existing `ReflectionHelpers` test utilities.

## Global Constraints

- Virtual layout space is **1280×720**; all coordinates are in that space.
- Code conventions: 4-space indent, LF, UTF-8; PascalCase types, `_camelCase` private fields.
- Use `TexturePath` constants, never hardcoded texture path strings.
- Every texture load is **best-effort** (try/catch → null) and every texture draw is **null-guarded**; missing art must never throw and must fall back to fills/text.
- Persist-on-edit is preserved: `ConfigManager.Config` is the single source of truth; edits apply live and a deferred save is flushed on exit. No working copy, no discard.
- Mac build/test: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj` and `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`. No new files may require a GraphicsDevice on Mac.
- Categories are exactly **System**, **Drums**, **Exit** (no Guitar/Bass). Exit is a category with zero items.
- Commit after each task with a Conventional Commit subject under 72 chars.

---

## File Structure

**Create:**
- `DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs` — pure coordinate constants + index→Rectangle/Vector2 helpers.
- `DTXMania.Game/Lib/Stage/Config/ConfigCategory.cs` — pure category model.
- `DTXMania.Test/UI/ConfigUILayoutTests.cs` — layout math tests (Mac-safe).
- `DTXMania.Test/Config/ConfigCategoryTests.cs` — model tests (Mac-safe).

**Modify:**
- `DTXMania.Game/Lib/Config/IConfigItem.cs` — add `string Description { get; }`.
- `DTXMania.Game/Lib/Config/ConfigItems.cs` — implement `Description` on `BaseConfigItem`.
- `DTXMania.Game/Lib/Resources/TexturePath.cs` — add 10 config constants + register in arrays.
- `DTXMania.Game/Lib/Stage/ConfigStage.cs` — full rewrite to categories + focus model + NX rendering.
- `DTXMania.Test/Resources/TexturePathTests.cs` — membership assertions for new constants.
- `DTXMania.Test/Config/ConfigStageLogicTests.cs` — add the new category/focus/render tests **and** migrate the flat-list tests (the new tests reuse this file's existing helpers: `CreateStage`, `CreateRenderSpyStageWithGraphicsDevice`, `CreateStageWithMockConfig`, `SetKeyboardStates`, `RenderSpyConfigStage`).
- `DTXMania.Test/Config/ConfigItemTests.cs` — `Description` default + round-trip.

**Established test patterns to reuse (already in `ConfigStageLogicTests.cs`):**
- `CreateStage()` → `(ConfigStage, ConfigManager, InputManagerCompat)`; wires the game via `SetProperty(game, nameof(BaseGame.ConfigManager/InputManager), …)`.
- `InitializeStageMenu(stage, includePanels)` → invokes `SetupConfigItems` (+ `InitializePanels`).
- Drive input with `SetKeyboardStates(stage, new KeyboardState(Keys.X), new KeyboardState())` then `InvokePrivateMethod(stage, "HandleInput")`; default map: `Escape`→Back, `Enter`→Activate, `Up/Down/Left/Right`→Move*.
- Verify a stage change with `stage.StageManager = new Moq.Mock<IStageManager>().Object` then `stageManager.Verify(m => m.ChangeStage(StageType.Title, It.Is<IStageTransition>(t => t is CrossfadeTransition)), Times.Once)`.
- Verify a save flush with `CreateStageWithMockConfig(inputManager)` → `mockConfig.Verify(c => c.FlushPendingSave(), Times.Once)`.
- Render assertions: `CreateRenderSpyStageWithGraphicsDevice()` → `RenderSpyConfigStage` with `RectangleDrawCalls` and `InitializeDrawingState()` (textures + fonts null, so fallback `DrawFilledRectangle` rects are captured).

No `DTXMania.Test.Mac.csproj` change is required (no GraphicsDevice-dependent new files).

---

### Task 1: Add `Description` to config items

**Files:**
- Modify: `DTXMania.Game/Lib/Config/IConfigItem.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigItems.cs:9-29` (`BaseConfigItem`)
- Test: `DTXMania.Test/Config/ConfigItemTests.cs`

**Interfaces:**
- Produces: `string IConfigItem.Description { get; }`; `BaseConfigItem.Description` is `{ get; init; }` defaulting to `""`, settable via object initializer. Inherited unchanged by `ReadOnlyConfigItem`, `DropdownConfigItem`, `ToggleConfigItem`, `IntegerConfigItem`, `NavigationConfigItem`.

- [ ] **Step 1: Write the failing test**

Append to `DTXMania.Test/Config/ConfigItemTests.cs` (inside the existing test class):

```csharp
[Fact]
public void Description_WhenNotSet_ShouldDefaultToEmptyString()
{
    var item = new ToggleConfigItem("Fullscreen", () => false, _ => { });

    Assert.Equal(string.Empty, item.Description);
}

[Fact]
public void Description_WhenSetViaInitializer_ShouldRoundTrip()
{
    var item = new ToggleConfigItem("Fullscreen", () => false, _ => { })
    {
        Description = "Toggles fullscreen display mode."
    };

    Assert.Equal("Toggles fullscreen display mode.", item.Description);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigItemTests.Description"`
Expected: FAIL — `'ToggleConfigItem' does not contain a definition for 'Description'`.

- [ ] **Step 3: Add `Description` to the interface**

In `DTXMania.Game/Lib/Config/IConfigItem.cs`, add inside the interface (after `string Name { get; }`):

```csharp
        /// <summary>
        /// Optional one-line help text shown in the config description panel. Empty by default.
        /// </summary>
        string Description { get; }
```

- [ ] **Step 4: Implement `Description` on `BaseConfigItem`**

In `DTXMania.Game/Lib/Config/ConfigItems.cs`, add to `BaseConfigItem` (after the `Name` property at line 11):

```csharp
        public string Description { get; init; } = "";
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigItemTests.Description"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Config/IConfigItem.cs DTXMania.Game/Lib/Config/ConfigItems.cs DTXMania.Test/Config/ConfigItemTests.cs
git commit -m "feat: add optional Description to config items"
```

---

### Task 2: Add config `TexturePath` constants

**Files:**
- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs` (constants in the `UI Panel Textures` region near line 126; arrays `GetAllTexturePaths` line 500, `GetPanelTextures` line 636)
- Test: `DTXMania.Test/Resources/TexturePathTests.cs`

**Interfaces:**
- Produces constants: `TexturePath.ConfigBackground`, `ConfigItemBar`, `ConfigMenuPanel`, `ConfigMenuCursor`, `ConfigHeaderPanel`, `ConfigFooterPanel`, `ConfigItemBox`, `ConfigItemBoxOther`, `ConfigItemBoxCursor`, `ConfigDescriptionPanel`. All ten are in `GetAllTexturePaths()`; all except `ConfigBackground` are also in `GetPanelTextures()`.

- [ ] **Step 1: Write the failing test**

Append to `DTXMania.Test/Resources/TexturePathTests.cs` (inside the test class):

```csharp
[Fact]
public void ConfigTexturePaths_ShouldUseExpectedSkinFiles()
{
    Assert.Equal("Graphics/4_background.png", TexturePath.ConfigBackground);
    Assert.Equal("Graphics/4_item bar.png", TexturePath.ConfigItemBar);
    Assert.Equal("Graphics/4_menu panel.png", TexturePath.ConfigMenuPanel);
    Assert.Equal("Graphics/4_menu cursor.png", TexturePath.ConfigMenuCursor);
    Assert.Equal("Graphics/4_header panel.png", TexturePath.ConfigHeaderPanel);
    Assert.Equal("Graphics/4_footer panel.png", TexturePath.ConfigFooterPanel);
    Assert.Equal("Graphics/4_itembox.png", TexturePath.ConfigItemBox);
    Assert.Equal("Graphics/4_itembox other.png", TexturePath.ConfigItemBoxOther);
    Assert.Equal("Graphics/4_itembox cursor.png", TexturePath.ConfigItemBoxCursor);
    Assert.Equal("Graphics/4_Description Panel.png", TexturePath.ConfigDescriptionPanel);
}

[Fact]
public void GetAllTexturePaths_ShouldContainConfigPaths()
{
    var paths = TexturePath.GetAllTexturePaths();
    Assert.Contains(TexturePath.ConfigBackground, paths);
    Assert.Contains(TexturePath.ConfigMenuPanel, paths);
    Assert.Contains(TexturePath.ConfigDescriptionPanel, paths);
}

[Fact]
public void GetPanelTextures_ShouldContainConfigPanels_ButNotBackground()
{
    var paths = TexturePath.GetPanelTextures();
    Assert.Contains(TexturePath.ConfigMenuPanel, paths);
    Assert.Contains(TexturePath.ConfigItemBox, paths);
    Assert.DoesNotContain(TexturePath.ConfigBackground, paths);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests.ConfigTexturePaths_ShouldUseExpectedSkinFiles"`
Expected: FAIL — `'TexturePath' does not contain a definition for 'ConfigBackground'`.

- [ ] **Step 3: Add the constants**

In `DTXMania.Game/Lib/Resources/TexturePath.cs`, inside the `#region UI Panel Textures` block (e.g. after `SongStatusPanel` near line 115), add:

```csharp
        /// <summary>Config stage background (1280x720).</summary>
        public const string ConfigBackground = "Graphics/4_background.png";

        /// <summary>Config stage vertical divider between the menu and item columns (18x720, drawn at x=400).</summary>
        public const string ConfigItemBar = "Graphics/4_item bar.png";

        /// <summary>Config stage left category-menu panel (180x172, drawn at 245,140).</summary>
        public const string ConfigMenuPanel = "Graphics/4_menu panel.png";

        /// <summary>Config stage category-menu selection cursor.</summary>
        public const string ConfigMenuCursor = "Graphics/4_menu cursor.png";

        /// <summary>Config stage header panel (1280x105, drawn at 0,0).</summary>
        public const string ConfigHeaderPanel = "Graphics/4_header panel.png";

        /// <summary>Config stage footer panel (1280x30, drawn at 0,690).</summary>
        public const string ConfigFooterPanel = "Graphics/4_footer panel.png";

        /// <summary>Config stage normal item-row box.</summary>
        public const string ConfigItemBox = "Graphics/4_itembox.png";

        /// <summary>Config stage "other" item-row box (navigation/read-only items).</summary>
        public const string ConfigItemBoxOther = "Graphics/4_itembox other.png";

        /// <summary>Config stage item-row selection cursor.</summary>
        public const string ConfigItemBoxCursor = "Graphics/4_itembox cursor.png";

        /// <summary>Config stage description panel (280x360, drawn at 800,270).</summary>
        public const string ConfigDescriptionPanel = "Graphics/4_Description Panel.png";
```

- [ ] **Step 4: Register in `GetAllTexturePaths`**

In `GetAllTexturePaths()` (line ~500), add these entries before the closing `};` (after `HitFx`):

```csharp
                ,ConfigBackground,
                ConfigItemBar,
                ConfigMenuPanel,
                ConfigMenuCursor,
                ConfigHeaderPanel,
                ConfigFooterPanel,
                ConfigItemBox,
                ConfigItemBoxOther,
                ConfigItemBoxCursor,
                ConfigDescriptionPanel
```

(Note: the existing last entry `HitFx` has no trailing comma; the leading `,` above attaches cleanly. If `HitFx` already ends with a comma, drop the leading comma.)

- [ ] **Step 5: Register the panels in `GetPanelTextures`**

In `GetPanelTextures()` (line ~636), add before its closing `};`:

```csharp
                ,ConfigItemBar,
                ConfigMenuPanel,
                ConfigMenuCursor,
                ConfigHeaderPanel,
                ConfigFooterPanel,
                ConfigItemBox,
                ConfigItemBoxOther,
                ConfigItemBoxCursor,
                ConfigDescriptionPanel
```

(Same comma caveat as Step 4 — match the existing final entry's punctuation.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests"`
Expected: PASS — including the existing `GetAllTexturePaths_AllEntriesShouldStartWithGraphics`, `GetAllTexturePaths_ShouldNotDuplicateAnyPaths`, and `GetPanelTextures_AllEntriesShouldStartWithGraphics` guards.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Resources/TexturePath.cs DTXMania.Test/Resources/TexturePathTests.cs
git commit -m "feat: add config stage NX skin texture paths"
```

---

### Task 3: Add `ConfigUILayout`

**Files:**
- Create: `DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs`
- Test: `DTXMania.Test/UI/ConfigUILayoutTests.cs`

**Interfaces:**
- Produces (all `public static`, in namespace `DTXMania.Game.Lib.UI.Layout`):
  - consts `int ScreenWidth=1280`, `ScreenHeight=720`, `TitleY=40`, `MenuFirstRowY=156`, `MenuRowStride=34`, `MenuLabelCenterX=335`, `MenuCursorWidth=150`, `MenuCursorHeight=30`, `ItemListX=430`, `ItemFirstRowY=150`, `ItemRowStride=60`, `ItemBoxWidth=360`, `ItemBoxHeight=54`, `ItemNameInsetX=24`, `ItemValueInsetX=330`, `ItemTextInsetY=14`, `DescriptionWrapWidth=248`, `DescriptionLineHeight=22`, `string InstructionsText`.
  - props `Rectangle BackgroundRect/ItemBarRect/HeaderRect/FooterRect/MenuPanelRect/DescriptionPanelRect`, `Vector2 InstructionsPos/ImportStatusPos/DescriptionTextPos`.
  - helpers `int MenuRowY(int)`, `Rectangle MenuCursorRect(int)`, `int ItemRowY(int)`, `Rectangle ItemRowRect(int)`, `Rectangle ItemCursorRect(int)`, `Vector2 ItemNamePos(int)`, `Vector2 ItemValuePos(int)`.

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/UI/ConfigUILayoutTests.cs`:

```csharp
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class ConfigUILayoutTests
{
    [Fact]
    public void Panels_ShouldMatchNxCoordinates()
    {
        Assert.Equal(new Rectangle(0, 0, 1280, 720), ConfigUILayout.BackgroundRect);
        Assert.Equal(new Rectangle(400, 0, 18, 720), ConfigUILayout.ItemBarRect);
        Assert.Equal(new Rectangle(0, 0, 1280, 105), ConfigUILayout.HeaderRect);
        Assert.Equal(new Rectangle(0, 690, 1280, 30), ConfigUILayout.FooterRect);
        Assert.Equal(new Rectangle(245, 140, 180, 172), ConfigUILayout.MenuPanelRect);
        Assert.Equal(new Rectangle(800, 270, 280, 360), ConfigUILayout.DescriptionPanelRect);
    }

    [Theory]
    [InlineData(0, 156)]
    [InlineData(1, 190)]
    [InlineData(2, 224)]
    public void MenuRowY_ShouldStepBy34(int index, int expected)
    {
        Assert.Equal(expected, ConfigUILayout.MenuRowY(index));
    }

    [Fact]
    public void MenuCursorRect_ShouldSitOnSelectedRow()
    {
        Assert.Equal(new Rectangle(250, 153, 150, 30), ConfigUILayout.MenuCursorRect(0));
        Assert.Equal(new Rectangle(250, 221, 150, 30), ConfigUILayout.MenuCursorRect(2));
    }

    [Fact]
    public void ItemRowRect_ShouldStepBy60()
    {
        Assert.Equal(new Rectangle(430, 150, 360, 54), ConfigUILayout.ItemRowRect(0));
        Assert.Equal(new Rectangle(430, 210, 360, 54), ConfigUILayout.ItemRowRect(1));
    }

    [Fact]
    public void ItemCursorRect_ShouldFrameTheRow()
    {
        Assert.Equal(new Rectangle(426, 147, 368, 60), ConfigUILayout.ItemCursorRect(0));
        Assert.Equal(new Rectangle(426, 207, 368, 60), ConfigUILayout.ItemCursorRect(1));
    }

    [Fact]
    public void ItemTextPositions_ShouldOffsetFromRow()
    {
        Assert.Equal(new Vector2(454, 164), ConfigUILayout.ItemNamePos(0));
        Assert.Equal(new Vector2(760, 164), ConfigUILayout.ItemValuePos(0));
        Assert.Equal(new Vector2(454, 224), ConfigUILayout.ItemNamePos(1));
    }

    [Fact]
    public void DescriptionText_ShouldStartInsidePanel()
    {
        Assert.Equal(new Vector2(818, 288), ConfigUILayout.DescriptionTextPos);
        Assert.Equal(248, ConfigUILayout.DescriptionWrapWidth);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: FAIL — `The type or namespace name 'ConfigUILayout' does not exist`.

- [ ] **Step 3: Create the layout class**

Create `DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs`:

```csharp
#nullable enable

using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Layout
{
    /// <summary>
    /// Coordinate/size constants and index→position helpers for the NX-style master-detail
    /// Config stage, in the 1280x720 virtual space ported from DTXManiaNX CStageConfig /
    /// CActConfigList. Pure (no GraphicsDevice) so it is fully unit-testable.
    /// </summary>
    public static class ConfigUILayout
    {
        public const int ScreenWidth = 1280;
        public const int ScreenHeight = 720;

        // Background + vertical divider.
        public static Rectangle BackgroundRect => new(0, 0, ScreenWidth, ScreenHeight);
        public static Rectangle ItemBarRect => new(400, 0, 18, 720);

        // Header / footer / title / instructions / import status.
        public static Rectangle HeaderRect => new(0, 0, 1280, 105);
        public static Rectangle FooterRect => new(0, 690, 1280, 30);
        public const int TitleY = 40;
        public const string InstructionsText =
            "UP/DOWN select   LEFT/RIGHT change   ENTER choose   ESC back (saves automatically)";
        public static Vector2 InstructionsPos => new(16, 696);
        public static Vector2 ImportStatusPos => new(430, 600);

        // Left category menu.
        public static Rectangle MenuPanelRect => new(245, 140, 180, 172);
        public const int MenuFirstRowY = 156;
        public const int MenuRowStride = 34;
        public const int MenuLabelCenterX = 335;
        public const int MenuCursorWidth = 150;
        public const int MenuCursorHeight = 30;
        public static int MenuRowY(int index) => MenuFirstRowY + index * MenuRowStride;
        public static Rectangle MenuCursorRect(int index) =>
            new(250, MenuRowY(index) - 3, MenuCursorWidth, MenuCursorHeight);

        // Right item list.
        public const int ItemListX = 430;
        public const int ItemFirstRowY = 150;
        public const int ItemRowStride = 60;
        public const int ItemBoxWidth = 360;
        public const int ItemBoxHeight = 54;
        public const int ItemNameInsetX = 24;
        public const int ItemValueInsetX = 330;
        public const int ItemTextInsetY = 14;
        public static int ItemRowY(int row) => ItemFirstRowY + row * ItemRowStride;
        public static Rectangle ItemRowRect(int row) =>
            new(ItemListX, ItemRowY(row), ItemBoxWidth, ItemBoxHeight);
        public static Rectangle ItemCursorRect(int row) =>
            new(ItemListX - 4, ItemRowY(row) - 3, ItemBoxWidth + 8, ItemRowStride);
        public static Vector2 ItemNamePos(int row) =>
            new(ItemListX + ItemNameInsetX, ItemRowY(row) + ItemTextInsetY);
        public static Vector2 ItemValuePos(int row) =>
            new(ItemListX + ItemValueInsetX, ItemRowY(row) + ItemTextInsetY);

        // Description panel.
        public static Rectangle DescriptionPanelRect => new(800, 270, 280, 360);
        public static Vector2 DescriptionTextPos => new(818, 288);
        public const int DescriptionWrapWidth = 248;
        public const int DescriptionLineHeight = 22;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigUILayoutTests"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs DTXMania.Test/UI/ConfigUILayoutTests.cs
git commit -m "feat: add ConfigUILayout coordinate helpers"
```

---

### Task 4: Add `ConfigCategory`

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Config/ConfigCategory.cs`
- Test: `DTXMania.Test/Config/ConfigCategoryTests.cs`

**Interfaces:**
- Produces `public sealed class ConfigCategory` in namespace `DTXMania.Game.Lib.Stage.Config`:
  - ctor `ConfigCategory(string name, string description, IReadOnlyList<IConfigItem> items)`
  - `string Name { get; }`, `string Description { get; }`, `IReadOnlyList<IConfigItem> Items { get; }`, `int SelectedIndex { get; set; }`
  - `bool HasItems { get; }`, `IConfigItem? SelectedItem { get; }`
  - `void MoveSelectionUp()`, `void MoveSelectionDown()` (wrap; no-op when empty)

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Config/ConfigCategoryTests.cs`:

```csharp
using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Stage.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class ConfigCategoryTests
{
    private static IConfigItem Toggle(string name) =>
        new ToggleConfigItem(name, () => false, _ => { });

    [Fact]
    public void Constructor_ShouldExposeNameDescriptionAndItems()
    {
        var items = new List<IConfigItem> { Toggle("A"), Toggle("B") };

        var category = new ConfigCategory("Drums", "Drum settings.", items);

        Assert.Equal("Drums", category.Name);
        Assert.Equal("Drum settings.", category.Description);
        Assert.Equal(2, category.Items.Count);
        Assert.True(category.HasItems);
        Assert.Same(items[0], category.SelectedItem);
    }

    [Fact]
    public void MoveSelectionDown_ShouldWrapAround()
    {
        var category = new ConfigCategory("Drums", "", new List<IConfigItem> { Toggle("A"), Toggle("B") });

        category.MoveSelectionDown();
        Assert.Equal(1, category.SelectedIndex);

        category.MoveSelectionDown();
        Assert.Equal(0, category.SelectedIndex);
    }

    [Fact]
    public void MoveSelectionUp_FromFirst_ShouldWrapToLast()
    {
        var category = new ConfigCategory("Drums", "", new List<IConfigItem> { Toggle("A"), Toggle("B"), Toggle("C") });

        category.MoveSelectionUp();

        Assert.Equal(2, category.SelectedIndex);
    }

    [Fact]
    public void EmptyCategory_ShouldHaveNoItemsAndNoOpMoves()
    {
        var category = new ConfigCategory("Exit", "Leave.", new List<IConfigItem>());

        Assert.False(category.HasItems);
        Assert.Null(category.SelectedItem);

        category.MoveSelectionDown();
        category.MoveSelectionUp();

        Assert.Equal(0, category.SelectedIndex);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigCategoryTests"`
Expected: FAIL — `The type or namespace name 'ConfigCategory' does not exist`.

- [ ] **Step 3: Create the model**

Create `DTXMania.Game/Lib/Stage/Config/ConfigCategory.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Game.Lib.Stage.Config
{
    /// <summary>
    /// A left-column category in the NX Config stage: a labeled group of config items plus a
    /// help description. The Exit category is simply a category with no items
    /// (<see cref="HasItems"/> == false); no special flag is needed.
    /// </summary>
    public sealed class ConfigCategory
    {
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<IConfigItem> Items { get; }
        public int SelectedIndex { get; set; }

        public ConfigCategory(string name, string description, IReadOnlyList<IConfigItem> items)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public bool HasItems => Items.Count > 0;

        public IConfigItem? SelectedItem =>
            HasItems && SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

        public void MoveSelectionUp()
        {
            if (Items.Count == 0)
                return;
            SelectedIndex = (SelectedIndex - 1 + Items.Count) % Items.Count;
        }

        public void MoveSelectionDown()
        {
            if (Items.Count == 0)
                return;
            SelectedIndex = (SelectedIndex + 1) % Items.Count;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigCategoryTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Config/ConfigCategory.cs DTXMania.Test/Config/ConfigCategoryTests.cs
git commit -m "feat: add ConfigCategory model for master-detail config"
```

---

### Task 5: Rewrite `ConfigStage` to NX master-detail

This is the load-bearing task: it replaces the flat-list state, input, and rendering with the category + two-focus model and NX layout, and migrates the existing tests. Production and tests are in one task because the rewrite changes private fields the existing tests reference — the whole solution must build green at the end.

**Files:**
- Modify (full rewrite): `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs` (add the new tests + migrate the flat-list tests, reusing existing helpers)

**Interfaces:**
- Consumes: `ConfigUILayout` (Task 3), `ConfigCategory` (Task 4), `IConfigItem.Description` (Task 1), `TexturePath.Config*` (Task 2).
- Produces (private, reflection-accessed by tests): fields `_categories : List<ConfigCategory>`, `_currentCategoryIndex : int`, `_focusOnMenu : bool`; methods `SetupConfigItems()`, `HandleInput()`, `DrawCategoryMenu()`, `DrawItemList()`, `DrawConfigBackground()`; the render seam (`BeginDrawFrame`/`EndDrawFrame`/`DrawFilledRectangle`/`GetViewport`) is unchanged so `RenderSpyConfigStage` keeps working. `ChangeStage`/`StageManager` and `FlushPendingSave` paths are unchanged, so the existing `Moq.Mock<IStageManager>` / `CreateStageWithMockConfig` verification patterns keep working.

- [ ] **Step 1: Write the new behavior tests (failing)**

Add these tests **inside the existing `ConfigStageLogicTests` class** in `DTXMania.Test/Config/ConfigStageLogicTests.cs` (they reuse that file's helpers — do not duplicate helpers). First add `using DTXMania.Game.Lib.Stage.Config;` to the file's usings if absent.

```csharp
    [Fact]
    public void SetupConfigItems_ShouldBuildSystemDrumsExitCategories()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");

            Assert.Collection(categories!,
                c => Assert.Equal("System", c.Name),
                c => Assert.Equal("Drums", c.Name),
                c => Assert.Equal("Exit", c.Name));

            Assert.Collection(categories![0].Items,
                i => Assert.Equal("Screen Resolution", i.Name),
                i => Assert.Equal("Fullscreen", i.Name),
                i => Assert.Equal("VSync Wait", i.Name),
                i => Assert.Equal("Audio Latency Offset", i.Name),
                i => Assert.Equal("DTX Folder", i.Name),
                i => Assert.Equal("System Key Mapping", i.Name),
                i => Assert.Equal("Import NX Scores", i.Name));

            Assert.Collection(categories[1].Items,
                i => Assert.Equal("Scroll Speed", i.Name),
                i => Assert.Equal("Auto Play", i.Name),
                i => Assert.Equal("No Fail", i.Name),
                i => Assert.Equal("Drum Key Mapping", i.Name));

            Assert.False(categories[2].HasItems);
        }
    }

    [Fact]
    public void EveryConfigCategoryAndItem_ShouldHaveNonEmptyDescription()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");

            foreach (var category in categories!)
            {
                Assert.False(string.IsNullOrWhiteSpace(category.Description));
                foreach (var item in category.Items)
                    Assert.False(string.IsNullOrWhiteSpace(item.Description), $"{item.Name} needs a description");
            }
        }
    }

    [Fact]
    public void MenuActivateOnSettingsCategory_ShouldMoveFocusToItems()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_focusOnMenu"));
        }
    }

    [Fact]
    public void MenuActivateOnExitCategory_ShouldFlushAndTransitionToTitle()
    {
        using var inputManager = new InputManagerCompat(new ConfigManager());
        var (stage, mockConfig) = CreateStageWithMockConfig(inputManager);
        InitializeStageMenu(stage, includePanels: false);
        var stageManager = new Moq.Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 2); // Exit
        ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
        SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        mockConfig.Verify(c => c.FlushPendingSave(), Moq.Times.Once);
        stageManager.Verify(
            m => m.ChangeStage(StageType.Title, Moq.It.Is<IStageTransition>(t => t is CrossfadeTransition)),
            Moq.Times.Once);
    }

    [Fact]
    public void ItemsBackCommand_ShouldReturnFocusToMenu_WithoutLeavingStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
            SetKeyboardStates(stage, new KeyboardState(Keys.Escape), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_focusOnMenu"));
            stageManager.Verify(
                m => m.ChangeStage(Moq.It.IsAny<StageType>(), Moq.It.IsAny<IStageTransition>()),
                Moq.Times.Never);
        }
    }

    [Fact]
    public void MenuMoveDown_ShouldWrapAcrossCategories()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 2); // Exit (last)
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
        }
    }

    [Fact]
    public void DrawCategoryMenu_ShouldHighlightCurrentCategory()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 1);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCategoryMenu");

            Assert.Contains(stage.RectangleDrawCalls, c => c.Rectangle == ConfigUILayout.MenuCursorRect(1));
        }
    }

    [Fact]
    public void DrawItemList_WhenFocusOnItems_ShouldDrawItemCursorAtSelectedRow()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
            categories![0].SelectedIndex = 2;

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            Assert.Contains(stage.RectangleDrawCalls, c => c.Rectangle == ConfigUILayout.ItemCursorRect(2));
        }
    }

    [Fact]
    public void DrawItemList_WhenFocusOnMenu_ShouldNotDrawItemCursor()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            Assert.DoesNotContain(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.ItemCursorRect(0));
        }
    }
```

Add `using DTXMania.Game.Lib.UI.Layout;` for `ConfigUILayout`. The render tests require `RenderSpyConfigStage` to expose `InitializeDrawingState()` (it already does, per the existing `DrawTitle_WhenFontMissing...` test) and not to load real textures (it doesn't — `_resourceManager` is unset and the texture fields stay null, so draws fall through to `DrawFilledRectangle`).

- [ ] **Step 2: Run new tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStageLogicTests.SetupConfigItems_ShouldBuildSystemDrumsExitCategories"`
Expected: FAIL to compile — `ConfigCategory` / `_categories` do not exist in the (pre-rewrite) stage yet.

- [ ] **Step 3: Rewrite `ConfigStage.cs`**

Replace the entire contents of `DTXMania.Game/Lib/Stage/ConfigStage.cs` with:

```csharp
#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// NX-style master-detail configuration stage: a left category menu (System / Drums / Exit),
    /// a right per-category item list, a description panel, and header/footer panels. Reads
    /// <see cref="IConfigManager.Config"/> as the single source of truth; every edit applies
    /// immediately through the typed setters and marks a deferred save dirty. Back/Exit flushes
    /// the pending save and leaves — there is no working copy. Two focus states mirror the NX
    /// bFocusIsOnMenu pattern.
    /// </summary>
    public class ConfigStage : BaseStage
    {
        #region Private Fields

        private IConfigManager _configManager;
        private List<ConfigCategory> _categories = new();
        private int _currentCategoryIndex = 0;
        private bool _focusOnMenu = true;

        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;

        private SystemKeyAssignPanel? _systemPanel;
        private IKeyAssignPanel? _activePanel;

        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private IFont _font;
        private IFont _boldFont;
        private IResourceManager _resourceManager;

        private ITexture? _backgroundTexture;
        private ITexture? _itemBarTexture;
        private ITexture? _menuPanelTexture;
        private ITexture? _menuCursorTexture;
        private ITexture? _headerPanelTexture;
        private ITexture? _footerPanelTexture;
        private ITexture? _itemBoxTexture;
        private ITexture? _itemBoxOtherTexture;
        private ITexture? _itemBoxCursorTexture;
        private ITexture? _descriptionPanelTexture;

        // Light text reads on the dark NX config background.
        private static readonly Color LightText = new(235, 238, 248);
        private static readonly Color SelectedText = Color.Yellow;
        private static readonly Color ValueText = new(255, 226, 150);
        private static readonly Color MenuCursorFallback = new(64, 96, 160, 180);
        private static readonly Color ItemCursorFallback = new(96, 96, 160, 180);
        private static readonly Color ImportStatusColor = new(180, 220, 255);

        // Dark fill behind the UI when the background art is unavailable, so the light text stays
        // legible (light-on-light was the failure mode to avoid).
        private static readonly Color FallbackBackgroundColor = new(18, 20, 34);

        private volatile string _importStatus = "";
        private volatile bool _importRunning;
        private CancellationTokenSource? _importCts;

        #endregion

        #region Constructor

        public override StageType Type => StageType.Config;

        public ConfigStage(BaseGame game) : base(game)
        {
            _configManager = game.ConfigManager ?? throw new InvalidOperationException("ConfigManager not found");
        }

        #endregion

        #region Stage Lifecycle

        protected override void OnActivate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Config Stage");

            InitializeGraphics();
            SetupConfigItems();
            InitializePanels();

            _currentCategoryIndex = 0;
            _focusOnMenu = true;

            _previousKeyboardState = Keyboard.GetState();
            _currentKeyboardState = Keyboard.GetState();
        }

        protected override void OnUpdate(double deltaTime)
        {
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

            if (_activePanel?.IsActive == true)
            {
                _activePanel.Update(deltaTime, _currentKeyboardState, _previousKeyboardState);
                return;
            }

            HandleInput();
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            BeginDrawFrame();

            DrawConfigBackground();
            DrawItemBar();
            DrawCategoryMenu();
            DrawItemList();
            DrawDescriptionPanel();
            DrawHeaderFooter();
            DrawImportStatus();

            if (_activePanel?.IsActive == true)
            {
                var vp = GetViewport();
                _activePanel.Draw(_spriteBatch, _font, _boldFont, _whitePixel, vp.Width, vp.Height);
            }

            EndDrawFrame();
        }

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Config Stage");

            FlushPendingSaveSafely();

            _importCts?.Cancel();

            _activePanel?.Deactivate();
            _activePanel = null;

            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            ReleaseTextures();

            _previousKeyboardState = default;
            _currentKeyboardState = default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Disposing Config Stage resources");

                _importCts?.Cancel();
                _importCts?.Dispose();
                _importCts = null;

                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();

                _font?.RemoveReference();
                _boldFont?.RemoveReference();
                ReleaseTextures();

                _whitePixel = null;
                _spriteBatch = null;
                _font = null;
                _boldFont = null;
                _resourceManager = null; // shared game-wide instance; do NOT dispose
            }

            base.Dispose(disposing);
        }

        #endregion

        #region Initialization

        protected virtual void InitializeGraphics()
        {
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            _resourceManager = _game.ResourceManager;

            try
            {
                _font = _resourceManager.LoadFont("NotoSerifJP", 14);
                _boldFont = _resourceManager.LoadFont("NotoSerifJP", 14, FontStyle.Bold);
            }
            catch
            {
                _font?.RemoveReference();
                _font = null;
                _boldFont?.RemoveReference();
                _boldFont = null;
                _whitePixel?.Dispose();
                _whitePixel = null;
                _spriteBatch?.Dispose();
                _spriteBatch = null;
                throw;
            }

            // All skin art is best-effort; every draw is null-guarded with a fill/text fallback.
            _backgroundTexture = TryLoadTexture(TexturePath.ConfigBackground);
            _itemBarTexture = TryLoadTexture(TexturePath.ConfigItemBar);
            _menuPanelTexture = TryLoadTexture(TexturePath.ConfigMenuPanel);
            _menuCursorTexture = TryLoadTexture(TexturePath.ConfigMenuCursor);
            _headerPanelTexture = TryLoadTexture(TexturePath.ConfigHeaderPanel);
            _footerPanelTexture = TryLoadTexture(TexturePath.ConfigFooterPanel);
            _itemBoxTexture = TryLoadTexture(TexturePath.ConfigItemBox);
            _itemBoxOtherTexture = TryLoadTexture(TexturePath.ConfigItemBoxOther);
            _itemBoxCursorTexture = TryLoadTexture(TexturePath.ConfigItemBoxCursor);
            _descriptionPanelTexture = TryLoadTexture(TexturePath.ConfigDescriptionPanel);
        }

        private ITexture? TryLoadTexture(string path)
        {
            try
            {
                return _resourceManager.LoadTexture(path);
            }
            catch
            {
                return null;
            }
        }

        private void ReleaseTextures()
        {
            _backgroundTexture?.RemoveReference();
            _backgroundTexture = null;
            _itemBarTexture?.RemoveReference();
            _itemBarTexture = null;
            _menuPanelTexture?.RemoveReference();
            _menuPanelTexture = null;
            _menuCursorTexture?.RemoveReference();
            _menuCursorTexture = null;
            _headerPanelTexture?.RemoveReference();
            _headerPanelTexture = null;
            _footerPanelTexture?.RemoveReference();
            _footerPanelTexture = null;
            _itemBoxTexture?.RemoveReference();
            _itemBoxTexture = null;
            _itemBoxOtherTexture?.RemoveReference();
            _itemBoxOtherTexture = null;
            _itemBoxCursorTexture?.RemoveReference();
            _itemBoxCursorTexture = null;
            _descriptionPanelTexture?.RemoveReference();
            _descriptionPanelTexture = null;
        }

        private void SetupConfigItems()
        {
            var resolutionItem = new DropdownConfigItem(
                "Screen Resolution",
                () => $"{_configManager.Config.ScreenWidth}x{_configManager.Config.ScreenHeight}",
                new[] { "1280x720", "1920x1080", "2560x1440", "3840x2160" },
                value =>
                {
                    var parts = value.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                    {
                        _configManager.SetResolution(width, height);
                    }
                })
            { Description = "Sets the game window resolution." };

            var fullscreenItem = new ToggleConfigItem(
                "Fullscreen",
                () => _configManager.Config.FullScreen,
                value => _configManager.SetFullscreen(value))
            { Description = "Toggles fullscreen display mode." };

            var vsyncItem = new ToggleConfigItem(
                "VSync Wait",
                () => _configManager.Config.VSyncWait,
                value => _configManager.SetVSync(value))
            { Description = "Syncs drawing to the monitor refresh rate to reduce tearing." };

            var audioLatencyItem = new IntegerConfigItem(
                "Audio Latency Offset",
                () => _configManager.Config.AudioLatencyOffsetMs,
                value => _configManager.SetAudioLatency(value),
                minValue: 0,
                maxValue: 500,
                step: 10,
                valueFormatter: v => $"{v} ms")
            { Description = "Shifts audio timing to compensate for output latency." };

            var dtxFolderItem = new ReadOnlyConfigItem(
                "DTX Folder",
                () => _configManager.Config.DTXPath)
            { Description = "Folder scanned for songs and charts (read-only)." };

            var systemKeyItem = new NavigationConfigItem("System Key Mapping",
                () => OpenPanel(_systemPanel))
            { Description = "Assign keys for menu and system commands." };

            var importItem = new NavigationConfigItem("Import NX Scores",
                () => StartNxScoreImport())
            { Description = "Import play counts and scores from a DTXManiaNX database." };

            var scrollSpeedItem = new IntegerConfigItem(
                "Scroll Speed",
                () => _configManager.Config.ScrollSpeed,
                value => _configManager.SetScrollSpeed(AppPaths.GetConfigFilePath(), value),
                minValue: ScrollSpeedRange.Min,
                maxValue: ScrollSpeedRange.Max,
                step: ScrollSpeedRange.Step,
                valueFormatter: ScrollSpeedRange.Format)
            { Description = "Sets how fast notes scroll down the lanes." };

            var autoPlayItem = new ToggleConfigItem(
                "Auto Play",
                () => _configManager.Config.AutoPlay,
                value => _configManager.SetAutoPlay(value))
            { Description = "Plays the chart automatically without input." };

            var noFailItem = new ToggleConfigItem(
                "No Fail",
                () => _configManager.Config.NoFail,
                value => _configManager.SetNoFail(value))
            { Description = "Continue playing even when the life gauge is empty." };

            var drumKeyItem = new NavigationConfigItem("Drum Key Mapping",
                () => NavigateToDrumConfig())
            { Description = "Assign keys for each drum lane." };

            var systemItems = new List<IConfigItem>
            {
                resolutionItem, fullscreenItem, vsyncItem, audioLatencyItem,
                dtxFolderItem, systemKeyItem, importItem
            };

            var drumItems = new List<IConfigItem>
            {
                scrollSpeedItem, autoPlayItem, noFailItem, drumKeyItem
            };

            _categories = new List<ConfigCategory>
            {
                new ConfigCategory("System",
                    "System settings: display, audio, file paths, and system key bindings.",
                    systemItems),
                new ConfigCategory("Drums",
                    "Drum gameplay settings and drum pad key bindings.",
                    drumItems),
                new ConfigCategory("Exit",
                    "Save changes and return to the title screen.",
                    new List<IConfigItem>())
            };

            _currentCategoryIndex = 0;
            _focusOnMenu = true;
        }

        private void InitializePanels()
        {
            var inputManagerCompat = _game.InputManager
                ?? throw new InvalidOperationException("InputManager not available");

            _systemPanel = new SystemKeyAssignPanel(inputManagerCompat);
            _systemPanel._workingMappingProvider =
                () => new Dictionary<Keys, InputCommandType>(inputManagerCompat.GetKeyMappingSnapshot());
            _systemPanel._liveDrumBindingsProvider =
                () => new Dictionary<string, int>(inputManagerCompat.ModularInputManager.KeyBindings.ButtonToLane);
            _systemPanel._navigationMappingProvider =
                () => new Dictionary<Keys, InputCommandType>(inputManagerCompat.GetKeyMappingSnapshot());
            _systemPanel._commandPressedProvider = IsPanelCommandPressed;
            _systemPanel.Saved += OnPanelSaved;
            _systemPanel.Closed += OnPanelClosed;
        }

        private void OpenPanel(IKeyAssignPanel? panel)
        {
            if (panel == null) return;
            _activePanel = panel;
            _activePanel.Activate();
        }

        private void NavigateToDrumConfig()
        {
            ChangeStage(StageType.DrumConfig, new InstantTransition());
        }

        private void OnPanelSaved(object? sender, EventArgs e)
        {
            if (sender == _systemPanel)
            {
                _configManager.SetSystemKeyBindings(_systemPanel.GetWorkingMappingSnapshot());
            }
        }

        private void OnPanelClosed(object? sender, EventArgs e)
        {
            _activePanel = null;
        }

        private void StartNxScoreImport()
        {
            if (_importRunning)
                return;
            _importRunning = true;
            _importStatus = "Importing NX scores...";

            _importCts?.Cancel();
            _importCts?.Dispose();
            _importCts = new CancellationTokenSource();
            var token = _importCts.Token;

            IProgress<NxImportProgress> progress = new InlineProgress<NxImportProgress>(p =>
            {
                _importStatus = $"Importing... {p.Imported} imported / {p.Scanned} scanned";
            });

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await SongManager.Instance.ImportNxScoresAsync(progress, token);
                    _importStatus = result.DbUnavailable
                        ? "NX import unavailable (no database)"
                        : $"Imported {result.Imported} scores ({result.Scanned} charts scanned" +
                          (result.Errors > 0 ? $", {result.Errors} errors)" : ")");

                    if (result.Imported > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            await SongManager.Instance.RefreshSongListFromDatabaseAsync();
                        }
                        catch (System.Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"ConfigStage: NX import succeeded but song list refresh failed: {ex}");
                        }
                    }
                }
                catch (System.OperationCanceledException)
                {
                    _importStatus = "NX import cancelled";
                }
                catch (System.Exception ex)
                {
                    var detail = ex.GetBaseException().Message;
                    _importStatus = $"NX import failed: {detail}";
                    System.Diagnostics.Debug.WriteLine($"ConfigStage: NX import failed: {ex}");
                }
                finally
                {
                    _importRunning = false;
                }
            });
        }

        #endregion

        #region Input Handling

        private void HandleInput()
        {
            if (_focusOnMenu)
                HandleMenuInput();
            else
                HandleItemInput();
        }

        private void HandleMenuInput()
        {
            if (IsConfigNavigationCommandPressed(InputCommandType.Back))
            {
                ExitToTitle();
                return;
            }

            if (IsConfigNavigationCommandPressed(InputCommandType.MoveUp))
            {
                _currentCategoryIndex = (_currentCategoryIndex - 1 + _categories.Count) % _categories.Count;
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveDown))
            {
                _currentCategoryIndex = (_currentCategoryIndex + 1) % _categories.Count;
            }

            if (IsConfigNavigationCommandPressed(InputCommandType.Activate) ||
                IsConfigNavigationCommandPressed(InputCommandType.MoveRight))
            {
                var category = _categories[_currentCategoryIndex];
                if (category.HasItems)
                    _focusOnMenu = false;
                else
                    ExitToTitle();
            }
        }

        private void HandleItemInput()
        {
            var category = _categories[_currentCategoryIndex];

            if (IsConfigNavigationCommandPressed(InputCommandType.Back))
            {
                _focusOnMenu = true;
                return;
            }

            if (IsConfigNavigationCommandPressed(InputCommandType.MoveUp))
            {
                category.MoveSelectionUp();
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveDown))
            {
                category.MoveSelectionDown();
            }

            var item = category.SelectedItem;
            if (item == null)
                return;

            if (IsConfigNavigationCommandPressed(InputCommandType.MoveLeft))
            {
                item.PreviousValue();
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveRight))
            {
                item.NextValue();
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.Activate))
            {
                item.ToggleValue();
            }
        }

        private void ExitToTitle()
        {
            System.Diagnostics.Debug.WriteLine("Config: Returning to Title stage");
            FlushPendingSaveSafely();
            ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
        }

        private bool IsConfigNavigationCommandPressed(InputCommandType command)
        {
            if (_game.InputManager?.IsCommandPressed(command) == true)
                return true;

            var systemMap = _game.InputManager?.GetKeyMappingSnapshot();
            return systemMap != null && systemMap.Any(kvp =>
                kvp.Value == command &&
                _currentKeyboardState.IsKeyDown(kvp.Key) &&
                !_previousKeyboardState.IsKeyDown(kvp.Key));
        }

        private bool IsPanelCommandPressed(InputCommandType command)
        {
            var systemMap = _game.InputManager?.GetKeyMappingSnapshot();
            return systemMap != null && systemMap.Any(kvp =>
                kvp.Value == command &&
                ((_game.InputManager?.IsKeyPressed((int)kvp.Key) == true)
                 || (_currentKeyboardState.IsKeyDown(kvp.Key) && !_previousKeyboardState.IsKeyDown(kvp.Key))));
        }

        #endregion

        #region Event Handlers

        private void FlushPendingSaveSafely()
        {
            try
            {
                _configManager.FlushPendingSave();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConfigStage: failed to flush pending save: {ex}");
            }
        }

        #endregion

        #region Drawing (NX master-detail)

        private void DrawConfigBackground()
        {
            var viewport = GetViewport();
            var full = new Rectangle(0, 0, viewport.Width, viewport.Height);

            if (_backgroundTexture?.Texture != null)
                _spriteBatch.Draw(_backgroundTexture.Texture, full, Color.White);
            else
                DrawFilledRectangle(full, FallbackBackgroundColor);
        }

        private void DrawItemBar()
        {
            if (_itemBarTexture?.Texture != null)
                _spriteBatch.Draw(_itemBarTexture.Texture, ConfigUILayout.ItemBarRect, Color.White);
        }

        private void DrawCategoryMenu()
        {
            if (_menuPanelTexture?.Texture != null)
                _spriteBatch.Draw(_menuPanelTexture.Texture, ConfigUILayout.MenuPanelRect, Color.White);

            var cursorRect = ConfigUILayout.MenuCursorRect(_currentCategoryIndex);
            if (_menuCursorTexture?.Texture != null)
            {
                var tint = _focusOnMenu ? Color.White : new Color(255, 255, 255, 128);
                _spriteBatch.Draw(_menuCursorTexture.Texture, cursorRect, tint);
            }
            else
            {
                DrawFilledRectangle(cursorRect, MenuCursorFallback);
            }

            if (_font == null || _boldFont == null)
                return;

            for (int i = 0; i < _categories.Count; i++)
            {
                bool selected = i == _currentCategoryIndex;
                var font = selected ? _boldFont : _font;
                var color = selected ? SelectedText : LightText;
                var label = _categories[i].Name;
                var size = font.MeasureString(label);
                var pos = new Vector2(ConfigUILayout.MenuLabelCenterX - size.X / 2f, ConfigUILayout.MenuRowY(i));
                font.DrawString(_spriteBatch, label, pos, color);
            }
        }

        private void DrawItemList()
        {
            if (_categories.Count == 0)
                return;

            var category = _categories[_currentCategoryIndex];
            var items = category.Items;

            for (int row = 0; row < items.Count; row++)
            {
                var box = (items[row] is NavigationConfigItem || items[row] is ReadOnlyConfigItem)
                    ? _itemBoxOtherTexture
                    : _itemBoxTexture;
                if (box?.Texture != null)
                    _spriteBatch.Draw(box.Texture, ConfigUILayout.ItemRowRect(row), Color.White);
            }

            if (!_focusOnMenu && category.HasItems)
            {
                var cursorRect = ConfigUILayout.ItemCursorRect(category.SelectedIndex);
                if (_itemBoxCursorTexture?.Texture != null)
                    _spriteBatch.Draw(_itemBoxCursorTexture.Texture, cursorRect, Color.White);
                else
                    DrawFilledRectangle(cursorRect, ItemCursorFallback);
            }

            if (_font == null || _boldFont == null)
                return;

            for (int row = 0; row < items.Count; row++)
            {
                var item = items[row];
                bool selected = !_focusOnMenu && row == category.SelectedIndex;
                var font = selected ? _boldFont : _font;

                font.DrawString(_spriteBatch, item.Name, ConfigUILayout.ItemNamePos(row),
                    selected ? SelectedText : LightText);

                var value = GetItemValueText(item);
                if (!string.IsNullOrEmpty(value))
                {
                    font.DrawString(_spriteBatch, value, ConfigUILayout.ItemValuePos(row),
                        selected ? SelectedText : ValueText);
                }
            }
        }

        private static string GetItemValueText(IConfigItem item)
        {
            if (item is NavigationConfigItem)
                return ">";

            var text = item.GetDisplayText();
            var prefix = item.Name + ": ";
            return text.StartsWith(prefix, StringComparison.Ordinal)
                ? text.Substring(prefix.Length)
                : string.Empty;
        }

        private void DrawDescriptionPanel()
        {
            if (_categories.Count == 0)
                return;

            var category = _categories[_currentCategoryIndex];
            string text = _focusOnMenu
                ? category.Description
                : (category.SelectedItem?.Description ?? string.Empty);

            if (string.IsNullOrEmpty(text))
                return;

            if (_descriptionPanelTexture?.Texture != null)
                _spriteBatch.Draw(_descriptionPanelTexture.Texture, ConfigUILayout.DescriptionPanelRect, Color.White);

            if (_font == null)
                return;

            var pos = ConfigUILayout.DescriptionTextPos;
            foreach (var line in WrapText(_font, text, ConfigUILayout.DescriptionWrapWidth))
            {
                _font.DrawString(_spriteBatch, line, pos, LightText);
                pos.Y += ConfigUILayout.DescriptionLineHeight;
            }
        }

        private static IEnumerable<string> WrapText(IFont font, string text, int maxWidth)
        {
            var line = new StringBuilder();
            foreach (var word in text.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureString(candidate).X > maxWidth)
                {
                    yield return line.ToString();
                    line.Clear();
                    line.Append(word);
                }
                else
                {
                    if (line.Length > 0)
                        line.Append(' ');
                    line.Append(word);
                }
            }
            if (line.Length > 0)
                yield return line.ToString();
        }

        private void DrawHeaderFooter()
        {
            if (_headerPanelTexture?.Texture != null)
                _spriteBatch.Draw(_headerPanelTexture.Texture, ConfigUILayout.HeaderRect, Color.White);

            if (_font != null)
            {
                const string title = "CONFIGURATION";
                var size = _font.MeasureString(title);
                var pos = new Vector2((ConfigUILayout.ScreenWidth - size.X) / 2f, ConfigUILayout.TitleY);
                _font.DrawString(_spriteBatch, title, pos, LightText);
            }

            if (_footerPanelTexture?.Texture != null)
                _spriteBatch.Draw(_footerPanelTexture.Texture, ConfigUILayout.FooterRect, Color.White);

            if (_font != null)
                _font.DrawString(_spriteBatch, ConfigUILayout.InstructionsText, ConfigUILayout.InstructionsPos, LightText);
        }

        private void DrawImportStatus()
        {
            if (string.IsNullOrEmpty(_importStatus) || _font == null)
                return;

            _font.DrawString(_spriteBatch, _importStatus, ConfigUILayout.ImportStatusPos, ImportStatusColor);
        }

        [ExcludeFromCodeCoverage]
        protected virtual void BeginDrawFrame()
        {
            _spriteBatch.Begin();
        }

        [ExcludeFromCodeCoverage]
        protected virtual void EndDrawFrame()
        {
            _spriteBatch.End();
        }

        [ExcludeFromCodeCoverage]
        protected virtual void DrawFilledRectangle(Rectangle destinationRectangle, Color color)
        {
            if (_whitePixel == null)
                return;

            _spriteBatch.Draw(_whitePixel, destinationRectangle, color);
        }

        [ExcludeFromCodeCoverage]
        protected virtual Viewport GetViewport()
        {
            return _game.GraphicsDevice.Viewport;
        }

        #endregion

        /// <summary>
        /// Synchronous <see cref="IProgress{T}"/> that invokes the callback inline on the calling
        /// thread, preventing stale queued progress from overwriting the final import status.
        /// </summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;
            public InlineProgress(Action<T> callback) => _callback = callback;
            public void Report(T value) => _callback(value);
        }
    }
}
```

> **Transition/flush verification uses the existing seams unchanged:** the new tests verify `ChangeStage` via a `Moq.Mock<IStageManager>` on `stage.StageManager` and verify the save flush via `CreateStageWithMockConfig` — exactly like the existing `BackCommandPressed_*` and `ExitButton_*` tests. The rewrite keeps `ExitToTitle()` calling `ChangeStage(StageType.Title, new CrossfadeTransition(0.3))` and `FlushPendingSaveSafely()` calling `_configManager.FlushPendingSave()`, so those seams continue to work.

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStageLogicTests.SetupConfigItems_ShouldBuildSystemDrumsExitCategories|FullyQualifiedName~ConfigStageLogicTests.DrawCategoryMenu_ShouldHighlightCurrentCategory|FullyQualifiedName~ConfigStageLogicTests.MenuActivateOnExitCategory"`
Expected: PASS. If a rendering assertion fails, confirm the render-spy left every texture field null so the fallback `DrawFilledRectangle` path runs.

- [ ] **Step 5: Migrate the existing `ConfigStageLogicTests.cs`**

The existing flat-list tests reference removed members (`_configItems`, `_selectedIndex`, `GetConfigItemIndex`, `DrawTitle`, the Exit pseudo-button). Apply these changes in `DTXMania.Test/Config/ConfigStageLogicTests.cs`:

**(a) Delete** these now-obsolete tests (their behavior is replaced by `ConfigStageNxLayoutTests`):
- `SetupConfigItems_ShouldCreateExpectedItemsAndSelectFirstItem` (asserts 11 flat items)
- `MoveUpPressedAtFirstItem_ShouldWrapToExitButton`
- `MoveDownPressedAtExitButton_ShouldWrapToFirstItem`
- `ActivatePressedOnExitButton_ShouldReturnToTitleStage`
- `ExitButton_ShouldCallFlushPendingSaveAndTransitionToTitle`
- `ExitButton_WhenFlushThrows_ShouldStillTransitionToTitle`
- `HandleInput_ActivateOnExitButton_ShouldInvokeExitButtonClicked`
- `DrawTitle_WhenFontMissing_ShouldFallbackToRectangleDrawing` (DrawTitle is gone; title now drawn in `DrawHeaderFooter`)
- `OnDraw_WithActivePanel_ShouldDrawOverlayBeforeCompletingFrame` only if it asserts a removed draw method; keep it if it only checks the overlay path (it should still pass — verify).

**(b) Rewrite** the helper `GetConfigItemIndex` and the per-item tests to drive a category + selection instead of a flat index. Replace the `GetConfigItemIndex` helper (around line 1023) with:

```csharp
    /// <summary>
    /// Finds (categoryIndex, itemIndex) for a named item across all categories, sets the stage's
    /// current category + that category's SelectedIndex, and switches focus to the item list so a
    /// subsequent HandleInput acts on the item. Mirrors the new master-detail navigation.
    /// </summary>
    private static void SelectItemForEditing(ConfigStage stage, string itemName)
    {
        var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
        Assert.NotNull(categories);
        for (int c = 0; c < categories!.Count; c++)
        {
            for (int i = 0; i < categories[c].Items.Count; i++)
            {
                if (categories[c].Items[i].Name == itemName)
                {
                    ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", c);
                    categories[c].SelectedIndex = i;
                    ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
                    return;
                }
            }
        }
        Assert.Fail($"Config item '{itemName}' should exist.");
    }
```

Add `using DTXMania.Game.Lib.Stage.Config;` to the file's usings.

**(c) For each remaining test** that did `SetPrivateField(stage, "_selectedIndex", GetConfigItemIndex(stage, "X"))` (or a literal index) and then invoked input/edit, replace those two lines with `SelectItemForEditing(stage, "X");`. The affected tests and their item names:

| Test | Item to select |
| --- | --- |
| `DtxFolderItem_WhenActivated_ShouldNotMutateConfig` | `DTX Folder` |
| `MoveRightPressedOnResolution_ShouldMutateConfigViaSetter` | `Screen Resolution` |
| `MoveLeftPressedOnResolution_ShouldMutateConfigViaSetter` | `Screen Resolution` |
| `ActivatePressedOnToggleItem_ShouldMutateConfigViaSetter` | `Fullscreen` |
| `ActivatePressedOnDrumKeyMapping_ShouldChangeToDrumConfigStage` | `Drum Key Mapping` |
| `HandleInput_MoveLeftOnNavigationItem_ShouldNotMutateConfig` | `Drum Key Mapping` |
| `HandleInput_MoveRightOnNavigationItem_ShouldNotMutateConfig` | `System Key Mapping` |
| `SystemPanelBackCommand_ShouldClosePanelWithoutMutatingSystemBindings` | `System Key Mapping` |
| `AudioLatencyConfigItem_ShouldIncrementBy10Ms` | `Audio Latency Offset` |
| `AudioLatencyConfigItem_AtZero_ShouldNotDecrementBelowMin` | `Audio Latency Offset` |
| `AudioLatencyConfigItem_At500_ShouldNotIncrementAboveMax` | `Audio Latency Offset` |
| `ActivatePressedOnToggle_ShouldMutateConfigViaSetter` (Theory) | map each `selectedIndex` row to its name: replace the `[InlineData(...)]` selectedIndex column with the item name and select by name — `No Fail`, `Auto Play` (Drums), `Fullscreen`, `VSync Wait` (System) as the original data intended; verify against the original `propertyName` column and keep the property assertion unchanged. |

Worked example — `MoveRightPressedOnResolution_ShouldMutateConfigViaSetter` becomes:

```csharp
[Fact]
public void MoveRightPressedOnResolution_ShouldMutateConfigViaSetter()
{
    var (stage, configManager, inputManager) = CreateStage();
    using (inputManager)
    {
        InitializeStageMenu(stage, includePanels: true);
        SelectItemForEditing(stage, "Screen Resolution");
        SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        // (keep the original assertion comparing configManager.Config resolution to the expected next value)
    }
}
```

**(d) Migrate focus-dependent navigation tests.** Tests that previously asserted up/down moved `_selectedIndex` within the flat list (`OnUpdate_WithoutActivePanel_ShouldHandleMenuInput`, `HandleInput_WithNullActivePanel_ShouldHandleInputNormally`, `OnUpdate_WithActivePanel_ShouldForwardKeyboardStatesToPanelAndSkipMenuHandling`) now operate on category navigation while `_focusOnMenu == true`. For the two that assert a `Down` press advances selection, set `_focusOnMenu` appropriately:
- If testing **category** movement: keep `_focusOnMenu = true` and assert `_currentCategoryIndex` advanced.
- If testing **item** movement: `SetPrivateField(stage, "_focusOnMenu", false)`, set the category, press Down, and assert the category's `SelectedIndex` advanced.

Worked example — `OnUpdate_WithoutActivePanel_ShouldHandleMenuInput` (category movement). Preserve the original's `ForcedCommandInputManager(MoveDown)` + manual game wiring + `OnUpdate(0.25)`; only swap `_selectedIndex` for the category fields and assertion:

```csharp
[Fact]
public void OnUpdate_WithoutActivePanel_ShouldHandleMenuInput()
{
    var configManager = new ConfigManager();
    using var inputManager = new ForcedCommandInputManager(configManager, InputCommandType.MoveDown);
    var game = ReflectionHelpers.CreateGame();
    ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
    ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
    var stage = new ConfigStage(game);

    InitializeStageMenu(stage, includePanels: false);
    ReflectionHelpers.SetPrivateField(stage, "_activePanel", (IKeyAssignPanel?)null);
    ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
    ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);

    ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.25);

    Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
}
```

(The same `_selectedIndex` → `_currentCategoryIndex`/`_focusOnMenu` swap applies to `HandleInput_WithNullActivePanel_ShouldHandleInputNormally` and `OnUpdate_WithActivePanel_ShouldForwardKeyboardStatesToPanelAndSkipMenuHandling` — keep each test's original input mechanism and only change the field it reads/asserts.)

**(e) Fix `OnActivate_ShouldInitializeConfigItemsAndPanels`** (line ~404): replace its `_configItems` assertion with a `_categories` assertion:

```csharp
var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
Assert.NotNull(categories);
Assert.Equal(3, categories!.Count);
```

**(f) Keep unchanged** (they don't touch removed members): `Constructor_WithoutConfigManager...`, `SetupConfigItems_ShouldShowConfiguredDtxFolder` (but change its `_configItems` lookup to search `_categories[0].Items` for `DTX Folder`), `BackCommandPressed_*`, `IsConfigNavigationCommandPressed_*`, `IsPanelCommandPressed_*`, `OnPanelSaved_*`, `OnPanelClosed_*`, `OpenPanel_WithNullPanel...`, `OnDeactivate_ShouldFlushDirtyConfigToDisk`, `DrawBackground_ShouldFillViewportWithBackgroundColor` (update its expected fallback color to `new Color(18, 20, 34)` and its invoked method name to `DrawConfigBackground`), `OnDraw_WhenSpriteBatchMissing...`.

For `SetupConfigItems_ShouldShowConfiguredDtxFolder`, change the lookup to:

```csharp
var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
var item = categories!.SelectMany(c => c.Items).Single(i => i.Name == "DTX Folder");
Assert.Equal("DTX Folder: /tmp/custom dtx", item.GetDisplayText());
```

(add `using System.Linq;` if not present).

- [ ] **Step 6: Build and run the full Mac suite**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds, no warnings about unreachable old members.

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Config"`
Expected: PASS — `ConfigStageNxLayoutTests`, migrated `ConfigStageLogicTests`, `ConfigCategoryTests`, `ConfigItemTests`, `ConfigUILayoutTests`.

- [ ] **Step 7: Run the entire Mac test suite to catch collateral**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS. Fix any remaining references to removed `ConfigStage` members surfaced by the compiler.

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Test/Config/ConfigStageLogicTests.cs
git commit -m "feat: rebuild config stage with NX master-detail layout"
```

---

### Task 6: Visual & end-to-end verification

**Files:** none (verification only).

- [ ] **Step 1: Build and launch the game (Mac)**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: success.

- [ ] **Step 2: Drive to the Config stage and screenshot**

Launch the game and navigate Title → Config (the game exposes the JSON-RPC/MCP control surface described in CLAUDE.md). Capture a screenshot of the Config stage. Confirm visually:
- Left menu shows **System / Drums / Exit** with the current category highlighted.
- Right column shows the current category's items in NX item-box rows with values right-aligned.
- The description panel shows the focused category/item help text.
- Header (CONFIGURATION) and footer (instructions) panels render.

If running headless, instead set a breakpoint-free smoke check: confirm `OnDraw` runs without exceptions by launching and entering Config, then exiting.

- [ ] **Step 3: Manual interaction check**

Verify by keyboard:
- Up/Down on the menu cycles System ↔ Drums ↔ Exit.
- Enter on System/Drums moves focus to the item list; Esc returns to the menu.
- Left/Right changes a value (e.g. Scroll Speed) and it persists after leaving Config.
- Enter on Exit (or Esc on the menu) returns to Title and the edited value is saved in `Config.ini`.
- Drum Key Mapping opens `DrumConfigStage`; System Key Mapping opens the key panel overlay.

- [ ] **Step 4: Final full test pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS.

- [ ] **Step 5: Commit any verification fixes**

```bash
git add -A
git commit -m "fix: config stage NX layout verification adjustments"
```

(Skip if nothing changed.)

---

## Self-Review

**Spec coverage:**
- Two-column NX layout → Tasks 3 (coordinates) + 5 (rendering). ✓
- System/Drums/Exit categories + item mapping → Task 5 `SetupConfigItems` + `SetupConfigItems_ShouldBuildSystemDrumsExitCategories`. ✓
- English per-category + per-item descriptions → Task 1 (`Description`) + Task 5 (strings) + `EveryConfigCategoryAndItem_ShouldHaveNonEmptyDescription`. ✓
- Two-focus navigation (menu ↔ items; Exit/Esc behavior; remembered selection) → Task 5 `HandleMenuInput`/`HandleItemInput` + focus tests. ✓
- Reuse existing key UIs → Task 5 keeps `NavigateToDrumConfig` + `OpenPanel(_systemPanel)`. ✓
- Skin assets + best-effort fallback → Task 2 (paths) + Task 5 null-guarded draws + `DrawConfigBackground` fallback. ✓
- No new Mac exclusions; all Mac-testable → Tasks 3/4 pure, Task 5 reuses `RenderSpyConfigStage`. ✓
- Persist-on-edit preserved → Task 5 keeps `FlushPendingSaveSafely`/typed setters. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code. The Step 5 test-migration table is a mechanical per-test mapping (each row names the exact item to select), not a placeholder.

**Type consistency:** `ConfigCategory` members (`HasItems`, `SelectedItem`, `MoveSelectionUp/Down`, `SelectedIndex`) are used identically in Tasks 4 and 5. `ConfigUILayout` helper names (`MenuCursorRect`, `ItemCursorRect`, `ItemRowRect`, `ItemNamePos`, `ItemValuePos`, `MenuRowY`) match between Tasks 3 and 5 and the render tests. `IConfigItem.Description` (Task 1) is consumed by `SetupConfigItems` (Task 5) and asserted in Task 5 tests. Private field names (`_categories`, `_currentCategoryIndex`, `_focusOnMenu`, `_font`, `_boldFont`, `_spriteBatch`, `_whitePixel`) are consistent between the rewrite and the reflection-based tests.
