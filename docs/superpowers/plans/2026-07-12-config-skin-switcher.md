# Config-Stage Skin Switcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "Skin" dropdown in the Config stage's System category that switches among discovered skins live and persists the choice to Config.ini.

**Architecture:** `ConfigStage.SetupConfigItems()` builds the dropdown from `SkinManager` discovery (first runtime consumer of that class). The setter calls the existing `SwitchToSystemSkin`, persists via a new `IConfigManager.SetSkinPath` (persist-on-edit, mirrors `SetScrollSpeed`), and re-loads the stage's own textures through a newly factored `LoadSkinTextures()`. Other stages pick the skin up on their next `OnActivate` — no event plumbing.

**Tech Stack:** .NET 8 / MonoGame, xUnit + existing `ReflectionHelpers`/`MockResourceManager` test seams.

**Spec:** `docs/superpowers/specs/2026-07-12-config-skin-switcher-design.md`

## Global Constraints

- .NET 8.0; no new NuGet dependencies. `.editorconfig`: 4-space indent, spaces only, LF, UTF-8; `_camelCase` private fields.
- Conventional Commits, subject < 72 chars, imperative mood.
- Untouched dropdown ⇒ zero behavior change. **No existing test may be modified** (only additions; adding a member to the `StubConfigManager` test double to keep it compiling is required, not a test modification).
- Mac test suite must pass after every task: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj` (new test files auto-included via wildcard; no GraphicsDevice in new tests).
- New C# test files: xUnit + `Scenario_ShouldExpect` naming + `[Trait("Category", "Unit")]`.
- **Persisted value shape (spec amendment, verified against code):** `Config.SkinPath` is an *absolute* path after `ConfigManager.NormalizeConfigPaths` (`ConfigManager.cs:917` resolves it via `AppPaths.ResolvePathOrDefault`), and `ConfigData.SkinPath`'s default is the absolute `AppPaths.GetDefaultSystemSkinRoot()`. Therefore the switcher persists **the effective absolute skin path** (what `GetCurrentEffectiveSkinPath()` returns after the switch), not a relative form. This round-trips byte-identically through startup (`Game1.cs:238` `ResourceManager.SetSkinPath(config.SkinPath)`). Task 1 amends the spec doc accordingly.

---

### Task 1: `IConfigManager.SetSkinPath` persist-on-edit setter

**Files:**
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs` (after `SetVSync`, ~line 83)
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs` (after `SetVSync`, ~line 641)
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` (`StubConfigManager`, ~line 1028 — add the new interface member so the stub compiles)
- Modify: `docs/superpowers/specs/2026-07-12-config-skin-switcher-design.md` (persisted-shape amendment)
- Test: `DTXMania.Test/Config/ConfigManagerSkinPathTests.cs` (new)

**Interfaces:**
- Consumes: existing `MarkDirty(string?)` / `FlushPendingSave()` deferred-save mechanism in `ConfigManager`.
- Produces: `void SetSkinPath(string configFilePath, string skinPath)` on `IConfigManager` — Task 2's dropdown setter calls exactly this signature. Semantics: no-op (and no dirty mark) when `skinPath` is null/whitespace or equals `Config.SkinPath`; otherwise sets `Config.SkinPath` and marks a deferred save to `configFilePath`. No event raised.

- [ ] **Step 1: Write the failing tests**

Create `DTXMania.Test/Config/ConfigManagerSkinPathTests.cs`:

```csharp
using System;
using System.IO;
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    [Trait("Category", "Unit")]
    public class ConfigManagerSkinPathTests : IDisposable
    {
        private readonly string _tempDir;

        public ConfigManagerSkinPathTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "dtx-skinpath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private string ConfigPath => Path.Combine(_tempDir, "Config.ini");

        [Fact]
        public void SetSkinPath_WithNewValue_ShouldUpdateConfigAndDeferSave()
        {
            var manager = new ConfigManager();
            var newSkin = Path.Combine(_tempDir, "System", "CXNeon") + Path.DirectorySeparatorChar;

            manager.SetSkinPath(ConfigPath, newSkin);

            Assert.Equal(newSkin, manager.Config.SkinPath);
            Assert.False(File.Exists(ConfigPath)); // write is deferred, not immediate

            manager.FlushPendingSave();

            Assert.True(File.Exists(ConfigPath));
            Assert.Contains($"SkinPath={newSkin}", File.ReadAllText(ConfigPath));
        }

        [Fact]
        public void SetSkinPath_WithUnchangedValue_ShouldNotDeferSave()
        {
            var manager = new ConfigManager();
            var current = manager.Config.SkinPath;

            manager.SetSkinPath(ConfigPath, current);
            manager.FlushPendingSave();

            Assert.Equal(current, manager.Config.SkinPath);
            Assert.False(File.Exists(ConfigPath)); // nothing marked dirty -> nothing written
        }

        [Fact]
        public void SetSkinPath_WithWhitespaceValue_ShouldBeIgnored()
        {
            var manager = new ConfigManager();
            var current = manager.Config.SkinPath;

            manager.SetSkinPath(ConfigPath, "   ");
            manager.FlushPendingSave();

            Assert.Equal(current, manager.Config.SkinPath);
            Assert.False(File.Exists(ConfigPath));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerSkinPathTests"`
Expected: build FAILURE — `'ConfigManager' does not contain a definition for 'SetSkinPath'`.

- [ ] **Step 3: Add the interface member**

In `DTXMania.Game/Lib/Config/IConfigManager.cs`, directly after the `SetVSync` declaration:

```csharp
        /// <summary>
        /// Sets the skin path (<see cref="ConfigData.SkinPath"/>, the directory the resource
        /// manager loads skin assets from) and marks a deferred save pending. No event raised.
        /// No-op when the value is null/whitespace or unchanged.
        /// </summary>
        void SetSkinPath(string configFilePath, string skinPath);
```

- [ ] **Step 4: Implement in ConfigManager**

In `DTXMania.Game/Lib/Config/ConfigManager.cs`, directly after the `SetVSync` one-liner (~line 641):

```csharp
        /// <summary>Sets the skin path (<see cref="ConfigData.SkinPath"/>) and marks a deferred save pending. No event raised. No-op when null/whitespace or unchanged.</summary>
        public void SetSkinPath(string configFilePath, string skinPath)
        {
            if (string.IsNullOrWhiteSpace(skinPath) || skinPath == Config.SkinPath)
                return;

            Config.SkinPath = skinPath;
            MarkDirty(configFilePath);
        }
```

- [ ] **Step 5: Add the member to StubConfigManager**

`DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` contains `private sealed class StubConfigManager : IConfigManager` (~line 1028) — it will no longer compile without the new member. Open the class, find its existing scalar setters (`SetVSync`, `SetScrollSpeed`, …) and add a member in the same style. If the neighbors are pure no-ops, add:

```csharp
            public void SetSkinPath(string configFilePath, string skinPath) { }
```

If the neighbors mutate their `Config` property, mirror that instead (`Config.SkinPath = skinPath;`). Do not change any existing member.

- [ ] **Step 6: Run the focused tests, then the full suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerSkinPathTests"`
Expected: PASS (3 tests).

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS, no existing test broken.

- [ ] **Step 7: Amend the spec's persisted-shape decision**

In `docs/superpowers/specs/2026-07-12-config-skin-switcher-design.md`:

Replace the decision-table row:

```markdown
| Persisted value shape | Portable relative form: `System/` for Default, `System/<Name>/` otherwise |
```

with:

```markdown
| Persisted value shape | The effective absolute skin path (`Config.SkinPath` is absolute after `NormalizeConfigPaths`; the identical value round-trips through startup) |
```

And in the "Switch flow (dropdown setter)" section, replace the sentence "Stored value: the portable relative form." with:

```markdown
   Stored value: the effective absolute skin path the switch produced —
   `Config.ini` already stores absolute paths after `NormalizeConfigPaths`
   resolves them at load, so this matches existing config behavior exactly.
```

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Config/IConfigManager.cs DTXMania.Game/Lib/Config/ConfigManager.cs \
        DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs \
        DTXMania.Test/Config/ConfigManagerSkinPathTests.cs \
        docs/superpowers/specs/2026-07-12-config-skin-switcher-design.md
git commit -m "feat: add IConfigManager.SetSkinPath persist-on-edit setter"
```

---

### Task 2: ConfigStage skin dropdown with live reload

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs` (field ~line 46; `OnDeactivate` ~line 227; `InitializeGraphics` ~lines 328-337; `SetupConfigItems` ~lines 414-467; new private methods)
- Test: `DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs` (new)

**Interfaces:**
- Consumes: `IConfigManager.SetSkinPath(string configFilePath, string skinPath)` from Task 1; existing `SkinManager(IResourceManager, string? systemSkinRoot)`, `SkinManager.AvailableSystemSkins` (`IReadOnlyList<string>` of absolute paths), `SkinManager.SwitchToSystemSkin(string name) : bool`, static `SkinManager.GetSkinName(string path)` (base root → `"Default"`), `IResourceManager.GetCurrentEffectiveSkinPath()`, existing `TryLoadTexture`/`ReleaseTextures` in ConfigStage.
- Produces: private `void SwitchSkin(string skinName)` and `string GetCurrentSkinName()` on ConfigStage (reflection-tested); `LoadSkinTextures()` factored out of `InitializeGraphics`.

- [ ] **Step 1: Write the failing tests**

Create `DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs`. Before writing, open `DTXMania.Test/Config/ConfigStageLogicTests.cs:1-30` and copy any additional `using` directives needed for `InputManagerCompat`, `TestMidiDeviceBackend`, `ConfigCategory`, and `IConfigItem` (they live in that file's using block; `TestMidiDeviceBackend` is the same helper that file uses). Also check whether `ConfigCategory` exposes a `Name` property (see `DTXMania.Game/Lib/Config/ConfigItems.cs` or wherever `ConfigCategory` is defined — `ConfigStageLogicTests` constructs it with a name as first argument); if it does not, use the `categories[0]` fallback noted in the code comment below.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Config
{
    [Trait("Category", "Unit")]
    public class ConfigStageSkinSwitcherTests : IDisposable
    {
        private readonly string _skinRoot;

        public ConfigStageSkinSwitcherTests()
        {
            _skinRoot = Path.Combine(Path.GetTempPath(), "dtx-skinswitch-" + Guid.NewGuid().ToString("N"));
            // The root itself is the "Default" skin; CXNeon is a custom skin under it.
            CreateSkin(_skinRoot);
            CreateSkin(Path.Combine(_skinRoot, "CXNeon"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_skinRoot, recursive: true); } catch { /* best effort */ }
        }

        private static void CreateSkin(string dir)
        {
            var graphics = Path.Combine(dir, "Graphics");
            Directory.CreateDirectory(graphics);
            // PathValidator.IsValidSkinPath requires Graphics/1_background.jpg OR 2_background.jpg.
            File.WriteAllBytes(Path.Combine(graphics, "1_background.jpg"), new byte[] { 0xFF });
        }

        private (ConfigStage Stage, ConfigManager ConfigManager, MockResourceManager ResourceManager, InputManagerCompat InputManager) CreateStage()
        {
            var configManager = new ConfigManager();
            configManager.Config.SystemSkinRoot = _skinRoot;
            var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
            var resourceManager = new MockResourceManager();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ResourceManager), resourceManager);
            return (new ConfigStage(game), configManager, resourceManager, inputManager);
        }

        private static IConfigItem GetSkinItem(ConfigStage stage)
        {
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
            Assert.NotNull(categories);
            // SetupConfigItems builds the System category first; the Skin item must be in it.
            var systemItems = categories![0].Items;
            return systemItems.Single(i => i.Name == "Skin");
        }

        [Fact]
        public void SetupConfigItems_ShouldAddSkinDropdownToSystemCategory()
        {
            var (stage, _, _, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

                var item = GetSkinItem(stage);
                Assert.Equal("Skin: Default", item.GetDisplayText());
            }
        }

        [Fact]
        public void SkinDropdown_NextValue_ShouldSwitchSkinAndPersistSkinPath()
        {
            var (stage, configManager, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

                var item = GetSkinItem(stage);
                item.NextValue(); // "Default" -> "CXNeon" (Default sorts first in discovery)

                var effective = resourceManager.GetCurrentEffectiveSkinPath();
                Assert.Contains("CXNeon", effective);
                Assert.Equal(effective, configManager.Config.SkinPath);
                Assert.Equal("Skin: CXNeon", item.GetDisplayText());
            }
        }

        [Fact]
        public void SwitchSkin_WithUnknownName_ShouldNotPersistOrChangeSkin()
        {
            var (stage, configManager, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
                var skinBefore = resourceManager.GetCurrentEffectiveSkinPath();
                var configBefore = configManager.Config.SkinPath;

                ReflectionHelpers.InvokePrivateMethod(stage, "SwitchSkin", "DoesNotExist");

                Assert.Equal(skinBefore, resourceManager.GetCurrentEffectiveSkinPath());
                Assert.Equal(configBefore, configManager.Config.SkinPath);
            }
        }

        [Fact]
        public void SkinDropdown_DisplayText_ShouldTrackEffectiveSkin()
        {
            var (stage, _, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

                resourceManager.SetSkinPath(Path.Combine(_skinRoot, "CXNeon") + Path.DirectorySeparatorChar);

                Assert.Equal("Skin: CXNeon", GetSkinItem(stage).GetDisplayText());
            }
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStageSkinSwitcherTests"`
Expected: FAIL — `GetSkinItem` finds no item named "Skin" (`Single` throws `InvalidOperationException: Sequence contains no matching element`), and `SwitchSkin` reflection lookup fails (`Assert.NotNull(method)` in `ReflectionHelpers`).

- [ ] **Step 3: Implement in ConfigStage**

All edits in `DTXMania.Game/Lib/Stage/ConfigStage.cs`. First verify `using System.Linq;` is present in the file's using block; add it if missing (needed for `.Select`/`.Where`). `DTXMania.Game.Lib.Resources` (for `SkinManager`) and `DTXMania.Game.Lib.Utilities` (for `AppPaths`) are already imported — verify rather than duplicate.

**3a — field.** After `private IConfigManager _configManager;` (~line 46):

```csharp
        private SkinManager? _skinManager;
```

**3b — factor `LoadSkinTextures` out of `InitializeGraphics`.** Replace the nine `TryLoadTexture` assignments at the end of `InitializeGraphics` (lines 329-337, starting `_backgroundTexture = TryLoadTexture(TexturePath.ConfigBackground);`) with a single call:

```csharp
            LoadSkinTextures();
```

and add the new method directly after `InitializeGraphics` (keep the existing comment with the block):

```csharp
        /// <summary>
        /// Loads the stage's skin textures. Called from InitializeGraphics on activation and
        /// again after an in-stage skin switch (after ReleaseTextures) so the newly selected
        /// skin shows immediately. All skin art is best-effort; every draw is null-guarded
        /// with a fill/text fallback.
        /// </summary>
        private void LoadSkinTextures()
        {
            _backgroundTexture = TryLoadTexture(TexturePath.ConfigBackground);
            _itemBarTexture = TryLoadTexture(TexturePath.ConfigItemBar);
            _menuPanelTexture = TryLoadTexture(TexturePath.ConfigMenuPanel);
            _menuCursorTexture = TryLoadTexture(TexturePath.ConfigMenuCursor);
            _headerPanelTexture = TryLoadTexture(TexturePath.ConfigHeaderPanel);
            _footerPanelTexture = TryLoadTexture(TexturePath.ConfigFooterPanel);
            _itemBoxTexture = TryLoadTexture(TexturePath.ConfigItemBox);
            _itemBoxCursorTexture = TryLoadTexture(TexturePath.ConfigItemBoxCursor);
            _descriptionPanelTexture = TryLoadTexture(TexturePath.ConfigDescriptionPanel);
        }
```

(The original comment line `// All skin art is best-effort; every draw is null-guarded with a fill/text fallback.` above the block in `InitializeGraphics` moves into the new method's doc comment — delete it from `InitializeGraphics`.)

**3c — build the dropdown in `SetupConfigItems`.** The item is created **only when the game has a resource manager**. Rationale, and why this guard is load-bearing: `BaseGame.ResourceManager` is assigned in `LoadContent` (`Game1.cs:233`), before any stage can activate — at runtime it is never null. But ~20 existing tests across 4 files (`ConfigStageLogicTests`, `ConfigStageInputCoverageTests`, `ConfigStageNxImportTests`, `ConfigStageCoverageTests`) invoke `SetupConfigItems` via reflection on a game built by `ReflectionHelpers.CreateGame()` with **no** ResourceManager; an unguarded `new SkinManager(_game.ResourceManager, …)` throws `ArgumentNullException` and breaks them all. With the guard, those harnesses get the base menu unchanged — including the pinned item-sequence assertion in `SetupConfigItems_ShouldBuildSystemDrumsExitCategories` (`ConfigStageLogicTests.cs:927-934`), which keeps passing byte-identically — while this task's new tests set a `MockResourceManager` and cover the real-game path (8 items).

Directly after the `audioLatencyItem` initializer block (~line 422, after its `{ Description = ... };` line), insert:

```csharp
            // BaseGame guarantees ResourceManager after LoadContent, before any stage activates,
            // so at runtime this branch always runs. Headless reflection tests may build the
            // items without a resource manager; they get the base menu without the Skin item.
            DropdownConfigItem? skinItem = null;
            if (_game.ResourceManager != null)
            {
                _skinManager?.Dispose();
                _skinManager = new SkinManager(_game.ResourceManager, _configManager.Config.SystemSkinRoot);
                var skinNames = _skinManager.AvailableSystemSkins
                    .Select(SkinManager.GetSkinName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToArray();
                skinItem = new DropdownConfigItem(
                    "Skin",
                    GetCurrentSkinName,
                    // DropdownConfigItem rejects an empty options array; with nothing discovered,
                    // fall back to the current name so the item is a harmless no-op.
                    skinNames.Length > 0 ? skinNames : new[] { GetCurrentSkinName() },
                    value => SwitchSkin(value))
                { Description = "Switches the UI skin. Applies immediately; other screens update on entry." };
            }
```

Then add `skinItem` to the System category list — replace:

```csharp
            var systemItems = new List<IConfigItem>
            {
                resolutionItem, fullscreenItem, vsyncItem, audioLatencyItem,
                dtxFolderItem, systemKeyItem, importItem
            };
```

with:

```csharp
            var systemItems = new List<IConfigItem>
            {
                resolutionItem, fullscreenItem, vsyncItem, audioLatencyItem
            };
            if (skinItem != null)
            {
                systemItems.Add(skinItem);
            }
            systemItems.Add(dtxFolderItem);
            systemItems.Add(systemKeyItem);
            systemItems.Add(importItem);
```

And update the System category description string from `"System settings: display, audio, file paths, and system key bindings."` to `"System settings: display, audio, skin, file paths, and system key bindings."`.

**3d — the two private methods.** Add after `SetupConfigItems` (before `InitializePanels`):

```csharp
        private string GetCurrentSkinName()
        {
            var name = SkinManager.GetSkinName(_game.ResourceManager.GetCurrentEffectiveSkinPath());
            return string.IsNullOrEmpty(name) ? "Default" : name;
        }

        private void SwitchSkin(string skinName)
        {
            if (_skinManager == null)
                return;

            if (!_skinManager.SwitchToSystemSkin(skinName))
            {
                _logger.LogWarning("Skin switch to '{SkinName}' failed; keeping the current skin", skinName);
                return;
            }

            _configManager.SetSkinPath(AppPaths.GetConfigFilePath(),
                _game.ResourceManager.GetCurrentEffectiveSkinPath());

            _logger.LogInformation("Switched skin to '{SkinName}'", skinName);

            // Live reload of this stage's own art. Guarded: headless tests invoke
            // SetupConfigItems without InitializeGraphics, so _resourceManager is null there.
            if (_resourceManager != null)
            {
                ReleaseTextures();
                LoadSkinTextures();
            }
        }
```

**3e — cleanup in `OnDeactivate`.** After `_activePanel = null;` (~line 236), add:

```csharp
            _skinManager?.Dispose();
            _skinManager = null;
```

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStageSkinSwitcherTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full Mac suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS with **zero existing-test changes**. In particular `SetupConfigItems_ShouldBuildSystemDrumsExitCategories` (`ConfigStageLogicTests.cs:927-934`) pins the System category's exact 7-item sequence — it keeps passing because its harness game has no ResourceManager, so the guard in 3c hides the Skin item there. If any existing test fails, the implementation (usually a missed guard path) is wrong — do not modify the existing test.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Test/Config/ConfigStageSkinSwitcherTests.cs
git commit -m "feat: add skin switcher dropdown to config stage"
```

---

## Post-plan follow-ups (recorded, not tasks)

- Visual verification via the dtxmania MCP screenshot flow once a second complete skin exists on the runtime skin root (per-user App Support dir) — until then the dropdown legitimately lists only "Default".
- Known trade-off: the pinned menu test (`SetupConfigItems_ShouldBuildSystemDrumsExitCategories`) asserts the headless 7-item menu, not the runtime 8-item menu (the Skin item is guard-hidden in its harness). The new `ConfigStageSkinSwitcherTests` cover the runtime shape. If that pinned test should ever assert the runtime menu instead, that is a deliberate follow-up edit to an existing test, out of scope here.
- If a future stage needs skin operations, promote `SkinManager` to a game-level service then (Approach B from the spec) — not before.
