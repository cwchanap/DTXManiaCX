using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Config;
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
