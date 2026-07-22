using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Config
{
    [Trait("Category", "Unit")]
    public class ConfigStageSkinSwitcherTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _skinRoot;

        public ConfigStageSkinSwitcherTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), "dtx-skinswitch-" + Guid.NewGuid().ToString("N"));
            // The skin root's leaf must literally be "System" so SkinManager.GetSkinName maps the
            // base root to "Default". The root itself is then the "Default" skin; CXNeon is a
            // custom skin under it, so discovery yields the options ["Default", "CXNeon"].
            _skinRoot = Path.Combine(_tempBase, "System");
            CreateSkin(_skinRoot);
            CreateSkin(Path.Combine(_skinRoot, "CXNeon"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempBase, recursive: true); } catch { /* best effort */ }
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
            // Simulate the real ResourceManager's initialized state: the
            // effective skin path is the absolute default System root, not the
            // relative "System/" placeholder the mock defaults to. Without this,
            // SkinManager.GetSkinName's path-equality check (which replaced the
            // old literal-"System" segment check) can't match the mock's
            // relative path against SkinManager._defaultSkinPath, and the
            // dropdown mislabels the default skin as "System" instead of
            // "Default". Tests that need a non-default starting skin override
            // this by calling SetSkinPath again after CreateStage.
            resourceManager.SetSkinPath(_skinRoot + Path.DirectorySeparatorChar);
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
        public void SkinDropdown_NextValue_WithFailedSwitch_ShouldResyncIndexAndRetryOnNextAttempt()
        {
            // Regression guard: DropdownConfigItem.NextValue advances the
            // private index BEFORE invoking the setValue callback (SwitchSkin).
            // When the switch fails (e.g. the target skin disappeared between
            // discovery and selection), the index is left on the rejected
            // option while GetCurrentSkinName still reports the old skin. The
            // next NextValue would then wrap from the wrong index and skip the
            // retry. SwitchSkin must resync the dropdown index on failure so
            // the next attempt re-tries the rejected entry once it is valid
            // again.
            var (stage, _, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
                var item = GetSkinItem(stage);

                // Delete CXNeon so the first NextValue's switch fails validation.
                Directory.Delete(Path.Combine(_skinRoot, "CXNeon"), recursive: true);

                // First NextValue: dropdown advances to CXNeon, SwitchSkin
                // fails (skin no longer validates), index resyncs to Default.
                item.NextValue();
                Assert.Contains("System", resourceManager.GetCurrentEffectiveSkinPath());
                Assert.DoesNotContain("CXNeon", resourceManager.GetCurrentEffectiveSkinPath());
                Assert.Equal("Skin: Default", item.GetDisplayText());

                // Recreate CXNeon so the next attempt can succeed.
                CreateSkin(Path.Combine(_skinRoot, "CXNeon"));

                // Second NextValue: with the resync fix the index was pinned
                // back to Default, so this advances to CXNeon again and the
                // switch now succeeds. Without the fix the index was left on
                // CXNeon, so this wraps to Default and switches to Default
                // (already active) — CXNeon would be silently skipped.
                item.NextValue();
                Assert.Contains("CXNeon", resourceManager.GetCurrentEffectiveSkinPath());
                Assert.Equal("Skin: CXNeon", item.GetDisplayText());
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

        [Fact]
        public void SwitchSkin_WithNullSkinManager_ShouldReturnEarly()
        {
            // Without SetupConfigItems, _skinManager is null. SwitchSkin should no-op
            // rather than NRE.
            var (stage, configManager, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                var skinBefore = resourceManager.GetCurrentEffectiveSkinPath();
                var configBefore = configManager.Config.SkinPath;

                ReflectionHelpers.InvokePrivateMethod(stage, "SwitchSkin", "CXNeon");

                Assert.Equal(skinBefore, resourceManager.GetCurrentEffectiveSkinPath());
                Assert.Equal(configBefore, configManager.Config.SkinPath);
            }
        }

        [Fact]
        public void SetupConfigItems_WithoutResourceManager_ShouldOmitSkinItem()
        {
            // When the game has no ResourceManager (headless reflection path), the
            // Skin dropdown is skipped entirely — the skinItem != null false branch.
            var configManager = new ConfigManager();
            configManager.Config.SystemSkinRoot = _skinRoot;
            var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
            // ResourceManager deliberately NOT set — stays null

            var stage = new ConfigStage(game);
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

                var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
                Assert.NotNull(categories);
                var systemItems = categories![0].Items;
                Assert.DoesNotContain(systemItems, i => i.Name == "Skin");
            }
        }

        [Fact]
        public void SwitchSkin_WithResourceManagerSet_ShouldLiveReloadTextures()
        {
            // Setting the private _resourceManager field simulates InitializeGraphics having
            // run, so SwitchSkin takes the live-reload branch (ReleaseTextures + LoadSkinTextures).
            var (stage, configManager, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
                // Simulate InitializeGraphics having set the resource manager field.
                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager);

                ReflectionHelpers.InvokePrivateMethod(stage, "SwitchSkin", "CXNeon");

                // Skin was switched and persisted...
                Assert.Contains("CXNeon", resourceManager.GetCurrentEffectiveSkinPath());
                Assert.Contains("CXNeon", configManager.Config.SkinPath);
                // ...and the live-reload branch ran without throwing (textures stay null
                // because MockResourceManager returns null for missing graphics device).
                Assert.Null(ReflectionHelpers.GetPrivateField<ITexture?>(stage, "_backgroundTexture"));
            }
        }

        [Fact]
        public void SwitchSkin_ShouldRouteThroughResourceManagerSetSkinPathBeforeSkinChangedFires()
        {
            // Load-bearing invariant: ConfigStage.SwitchSkin must route through
            // ResourceManager.SetSkinPath (which evicts the skin-dependent cache
            // and raises SkinChanged) BEFORE its own ReleaseTextures/LoadSkinTextures
            // reload. If the ordering ever breaks — e.g. LoadSkinTextures runs before
            // SetSkinPath — the stage would reload from the OLD skin's paths while the
            // cache still holds stale entries, silently shipping the wrong art.
            //
            // The eviction itself is unit-tested at the ResourceManager level
            // (SetSkinPath_WhenSkinChanges_ShouldEvictCacheBeforeRaisingSkinChanged).
            // This test guards the integration contract: by the time SkinChanged fires
            // (synchronously, inside SetSkinPath), the effective skin path must already
            // be the NEW skin — proving SetSkinPath ran to completion before any
            // SkinChanged subscriber (including ConfigStage's own reload) can act.
            var (stage, _, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager);

                string? effectivePathAtSkinChanged = null;
                resourceManager.SkinChanged += (_, args) =>
                {
                    effectivePathAtSkinChanged = resourceManager.GetCurrentEffectiveSkinPath();
                };

                ReflectionHelpers.InvokePrivateMethod(stage, "SwitchSkin", "CXNeon");

                // The handler ran (SetSkinPath was reached) and at that moment the
                // effective path was already CXNeon — SetSkinPath completed before
                // SkinChanged was raised, so a synchronous reload resolves new-skin paths.
                Assert.NotNull(effectivePathAtSkinChanged);
                Assert.Contains("CXNeon", effectivePathAtSkinChanged);
            }
        }

        [Fact]
        public void GetCurrentSkinName_WithUnresolvablePath_ShouldReturnDefault()
        {
            // When GetSkinName returns empty (e.g. a bare separator path), GetCurrentSkinName
            // falls back to "Default".
            var (stage, _, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
                // A path of just a separator yields no segments → GetSkinName returns "".
                resourceManager.SetSkinPath(Path.DirectorySeparatorChar.ToString());

                var name = ReflectionHelpers.InvokePrivateMethod<string>(stage, "GetCurrentSkinName");
                Assert.Equal("Default", name);
            }
        }

        [Fact]
        public void OnDeactivate_AfterSetupConfigItems_ShouldDisposeSkinManager()
        {
            // OnDeactivate must dispose the SkinManager created by SetupConfigItems.
            var (stage, _, _, inputManager) = CreateStage();
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
                Assert.NotNull(ReflectionHelpers.GetPrivateField<SkinManager>(stage, "_skinManager"));

                ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

                Assert.Null(ReflectionHelpers.GetPrivateField<SkinManager>(stage, "_skinManager"));
            }
        }

        [Fact]
        public void SwitchSkin_WithExternalSkin_ShouldBeReSelectableAfterSwitchingAway()
        {
            // When the active skin lives outside the discovered system skins (e.g. a
            // dev-preview checkout), SetupConfigItems inserts its leaf name into the
            // dropdown. Switching to a system skin and back must re-select the external
            // skin by its captured path, since SwitchToSystemSkin only resolves names
            // from the discovered system set.
            var externalSkinDir = Path.Combine(_tempBase, "ExternalSkins", "DevPreview");
            CreateSkin(externalSkinDir);
            var externalSkinPath = externalSkinDir + Path.DirectorySeparatorChar;

            var (stage, configManager, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                // Start on the external skin so SetupConfigItems treats it as current.
                resourceManager.SetSkinPath(externalSkinPath);
                configManager.Config.SkinPath = externalSkinPath;

                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

                // The external skin name must appear in the dropdown options.
                var item = GetSkinItem(stage);
                Assert.Contains("DevPreview", item.GetDisplayText());

                // Switch away to the discovered "Default" system skin.
                ReflectionHelpers.InvokePrivateMethod(stage, "SwitchSkin", "Default");
                Assert.Contains("System", resourceManager.GetCurrentEffectiveSkinPath());
                Assert.DoesNotContain("DevPreview", resourceManager.GetCurrentEffectiveSkinPath());

                // Switch back to the external skin by name — must resolve via the
                // captured path, not the discovered system set.
                ReflectionHelpers.InvokePrivateMethod(stage, "SwitchSkin", "DevPreview");
                var effective = resourceManager.GetCurrentEffectiveSkinPath();
                Assert.Contains("DevPreview", effective);
                Assert.Equal(effective, configManager.Config.SkinPath);
            }
        }

        [Fact]
        public void SetupConfigItems_WithExternalSkinCollidingOnExternalSuffix_ShouldProduceUniqueLabel()
        {
            // Regression: when an external skin is named "Foo" and the discovered
            // system skins include both "Foo" (which triggers the " (external)"
            // suffix) and "Foo (external)" (which collides with that suffix), the
            // old logic produced two dropdown entries both labeled "Foo (external)".
            // SwitchSkin routes every label matching _externalSkinLabel to the
            // external path, so the system skin "Foo (external)" was unreachable.
            // The external label must be disambiguated against ALL existing
            // labels, not just the bare colliding name.
            var externalSkinDir = Path.Combine(_tempBase, "ExternalSkins", "Foo");
            CreateSkin(externalSkinDir);
            var externalSkinPath = externalSkinDir + Path.DirectorySeparatorChar;
            // System skin named "Foo" (triggers the suffix) and a second system
            // skin literally named "Foo (external)" (collides with the suffix).
            CreateSkin(Path.Combine(_skinRoot, "Foo"));
            CreateSkin(Path.Combine(_skinRoot, "Foo (external)"));

            var (stage, configManager, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                // Start on the external "Foo" skin so SetupConfigItems treats it
                // as current and inserts an external entry.
                resourceManager.SetSkinPath(externalSkinPath);
                configManager.Config.SkinPath = externalSkinPath;

                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

                var item = GetSkinItem(stage) as DropdownConfigItem;
                Assert.NotNull(item);
                var options = ReflectionHelpers.GetPrivateField<string[]>(item, "_availableValues");

                // No duplicate labels allowed.
                var distinctCount = options.Select(o => o).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                Assert.Equal(options.Length, distinctCount);

                // The external entry must be disambiguated beyond "Foo (external)"
                // (which is the system skin's label) — expect "Foo (external) 2".
                Assert.Contains("Foo (external) 2", options);
                Assert.Contains("Foo (external)", options);

                // The system skin "Foo (external)" must remain reachable: its
                // label does NOT match _externalSkinLabel, so SwitchSkin falls
                // through to SwitchToSystemSkin.
                var externalLabel = ReflectionHelpers.GetPrivateField<string?>(stage, "_externalSkinLabel");
                Assert.Equal("Foo (external) 2", externalLabel);

                ReflectionHelpers.InvokePrivateMethod(stage, "SwitchSkin", "Foo (external)");
                var effective = resourceManager.GetCurrentEffectiveSkinPath();
                Assert.Contains("Foo (external)", effective);
                Assert.DoesNotContain("ExternalSkins", effective);
            }
        }

        [Fact]
        public void SetupConfigItems_WithInvalidCurrentSkinPath_ShouldNotRegisterExternalEntry()
        {
            // When the current skin path is invalid (e.g. an app-data System
            // directory that exists but lacks the validation files, while the
            // bundled System root is valid and discovered as "Default"), the
            // dropdown must NOT register the invalid path as an external
            // "System" entry. Such an entry would be unselectable —
            // SwitchToSkinPath validates on disk and rejects it — leaving the
            // player with a broken option. The effective skin is the bundled
            // fallback, so the display should read "Default".
            var (stage, _, resourceManager, inputManager) = CreateStage();
            using (inputManager)
            {
                // An invalid skin directory: exists on disk but has no
                // Graphics/1_background.jpg, so PathValidator rejects it.
                var invalidPath = Path.Combine(_tempBase, "InvalidSystem");
                Directory.CreateDirectory(invalidPath);
                resourceManager.SetSkinPath(invalidPath + Path.DirectorySeparatorChar);

                ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

                // No external entry should be captured for the invalid path.
                var externalPath = ReflectionHelpers.GetPrivateField<string?>(stage, "_externalSkinPath");
                Assert.Null(externalPath);

                // The displayed skin name should fall back to "Default"
                // because the current path is invalid and the effective skin
                // is the discovered default.
                Assert.Equal("Skin: Default", GetSkinItem(stage).GetDisplayText());
            }
        }
    }
}
