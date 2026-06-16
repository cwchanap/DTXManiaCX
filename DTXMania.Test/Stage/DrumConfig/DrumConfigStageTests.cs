using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumConfigStageTests
    {
        [Fact]
        public void GetResetButtonRect_ReturnsCorrectPosition()
        {
            // Act - Use reflection to test the private static method
            var method = typeof(DrumConfigStage).GetMethod("GetResetButtonRect",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var rect = (Rectangle)method!.Invoke(null, new object[] { 1280, 720 });

            // Assert
            Assert.Equal(1280 - 210, rect.X);
            Assert.Equal(12, rect.Y);
            Assert.Equal(190, rect.Width);
            Assert.Equal(30, rect.Height);
        }

        [Fact]
        public void Save_WithNonConfigManager_ShouldNotApplyLiveBindings()
        {
            var inputConfig = new ConfigManager();
            using var input = new InputManagerCompat(inputConfig);
            var stubConfig = new StubConfigManager();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), stubConfig);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.X", 2);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());

            ReflectionHelpers.InvokePrivateMethod(stage, "Save");

            Assert.Equal(-1, input.ModularInputManager.KeyBindings.GetLane("Key.X"));
        }

        [Fact]
        public void Save_WithConfigManager_ShouldPersistAndApplyLiveBindings()
        {
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.X", 2);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
            try
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "Save");

                var configPath = AppPaths.GetConfigFilePath();
                Assert.True(File.Exists(configPath));
                Assert.Equal(2, input.ModularInputManager.KeyBindings.GetLane("Key.X"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void Save_WhenSaveConfigThrows_ShouldRollbackConfigAndNotApplyLiveBindings()
        {
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.X", 2);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());

            var tempFile = Path.GetTempFileName();
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempFile);
            try
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "Save");

                Assert.Equal(-1, input.ModularInputManager.KeyBindings.GetLane("Key.X"));
                Assert.False(configManager.Config.KeyBindings.ContainsKey("Key.X"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                File.Delete(tempFile);
            }
        }

        private sealed class StubConfigManager : IConfigManager
        {
            public ConfigData Config { get; } = new ConfigData();

            public event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

            public void LoadConfig(string filePath) { }

            public void SaveConfig(string filePath) { }

            public void ResetToDefaults() { }

            public void SetScrollSpeed(string configFilePath, int percent) { }

            public void AdjustScrollSpeed(string configFilePath, int stepDelta) { }

            public void FlushPendingSave() { }
        }
    }
}
