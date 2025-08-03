using System;
using System.Collections.Generic;
using DTX.Config;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    /// <summary>
    /// Unit tests for the ModularInputManager
    /// Tests keyboard input, key bindings, and runtime reconfiguration
    /// </summary>
    public class ModularInputManagerTests : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly ModularInputManager _inputManager;

        public ModularInputManagerTests()
        {
            _configManager = new ConfigManager();
            _inputManager = new ModularInputManager(_configManager);
        }

        [Fact]
        public void Constructor_ValidConfigManager_InitializesCorrectly()
        {
            // Assert
            Assert.NotNull(_inputManager.KeyBindings);
            Assert.NotNull(_inputManager.InputRouter);
            Assert.Equal(9, _inputManager.KeyBindings.ButtonToLane.Count); // Default bindings
        }

        [Fact]
        public void Constructor_NullConfigManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ModularInputManager(null));
        }

        [Fact]
        public void KeyBindings_DefaultBindings_AreLoadedCorrectly()
        {
            // Assert default keyboard bindings
            Assert.Equal(0, _inputManager.KeyBindings.GetLane("Key.A"));
            Assert.Equal(1, _inputManager.KeyBindings.GetLane("Key.S"));
            Assert.Equal(2, _inputManager.KeyBindings.GetLane("Key.D"));
            Assert.Equal(3, _inputManager.KeyBindings.GetLane("Key.F"));
            Assert.Equal(4, _inputManager.KeyBindings.GetLane("Key.Space"));
            Assert.Equal(5, _inputManager.KeyBindings.GetLane("Key.J"));
            Assert.Equal(6, _inputManager.KeyBindings.GetLane("Key.K"));
            Assert.Equal(7, _inputManager.KeyBindings.GetLane("Key.L"));
            Assert.Equal(8, _inputManager.KeyBindings.GetLane("Key.OemSemicolon"));
        }

        [Fact]
        public void BindButton_NewBinding_UpdatesKeyBindings()
        {
            // Arrange
            var buttonId = "Key.Q";
            var lane = 2;

            // Act
            _inputManager.KeyBindings.BindButton(buttonId, lane);

            // Assert
            Assert.Equal(lane, _inputManager.KeyBindings.GetLane(buttonId));
        }

        [Fact]
        public void SaveKeyBindings_AfterModification_SavesToConfig()
        {
            // Arrange
            _inputManager.KeyBindings.BindButton("Key.Q", 0);

            // Act
            _inputManager.SaveKeyBindings();

            // Assert
            Assert.True(_configManager.Config.KeyBindings.ContainsKey("Key.Q"));
            Assert.Equal(0, _configManager.Config.KeyBindings["Key.Q"]);
        }

        [Fact]
        public void ReloadKeyBindings_AfterModification_RestoresFromConfig()
        {
            // Arrange
            _configManager.Config.KeyBindings["Key.Q"] = 5;
            _inputManager.KeyBindings.BindButton("Key.W", 3); // Local change

            // Act
            _inputManager.ReloadKeyBindings();

            // Assert
            Assert.Equal(5, _inputManager.KeyBindings.GetLane("Key.Q"));
            Assert.Equal(-1, _inputManager.KeyBindings.GetLane("Key.W")); // Should be gone
        }

        [Fact]
        public void ResetKeyBindingsToDefaults_AfterModification_RestoresDefaults()
        {
            // Arrange
            _inputManager.KeyBindings.ClearAllBindings();
            _inputManager.KeyBindings.BindButton("Key.Q", 0);

            // Act
            _inputManager.ResetKeyBindingsToDefaults();

            // Assert
            Assert.Equal(9, _inputManager.KeyBindings.ButtonToLane.Count);
            Assert.Equal(0, _inputManager.KeyBindings.GetLane("Key.A")); // Default binding restored
            Assert.Equal(-1, _inputManager.KeyBindings.GetLane("Key.Q")); // Custom binding removed
        }

        [Fact]
        public void Update_CallsSuccessfully_NoExceptions()
        {
            // Act & Assert (should not throw)
            _inputManager.Update(16.67); // ~60 FPS
        }

        [Fact]
        public void GetDiagnosticsInfo_ReturnsValidInfo()
        {
            // Act
            var diagnostics = _inputManager.GetDiagnosticsInfo();

            // Assert
            Assert.Contains("ModularInputManager Diagnostics", diagnostics);
            Assert.Contains("Input Sources:", diagnostics);
            Assert.Contains("Key Bindings:", diagnostics);
            Assert.Contains("Keyboard", diagnostics);
        }

        [Fact]
        public void OnLaneHit_EventIsRaised_WhenInputRouterDetectsHit()
        {
            // Arrange
            LaneHitEventArgs? capturedEvent = null;
            _inputManager.OnLaneHit += (sender, e) => capturedEvent = e;

            // Simulate a lane hit through the input router
            var buttonState = new ButtonState("Key.A", true, 1.0f);

            // Act
            _inputManager.InputRouter.OnLaneHit?.Invoke(_inputManager.InputRouter, 
                new LaneHitEventArgs(0, buttonState));

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(0, capturedEvent.Lane);
            Assert.Equal("Key.A", capturedEvent.Button.Id);
        }

        [Theory]
        [InlineData("Key.A", 0)]
        [InlineData("Key.S", 1)]
        [InlineData("Key.D", 2)]
        [InlineData("Key.F", 3)]
        [InlineData("Key.Space", 4)]
        [InlineData("Key.J", 5)]
        [InlineData("Key.K", 6)]
        [InlineData("Key.L", 7)]
        [InlineData("Key.OemSemicolon", 8)]
        public void GetLane_DefaultBindings_ReturnsCorrectLane(string buttonId, int expectedLane)
        {
            // Act
            var actualLane = _inputManager.KeyBindings.GetLane(buttonId);

            // Assert
            Assert.Equal(expectedLane, actualLane);
        }

        [Fact]
        public void RuntimeRebind_ModifyBindings_EffectiveImmediately()
        {
            // Arrange
            var buttonId = "Key.Q";
            var lane = 2;
            bool bindingsChangedEventFired = false;
            _inputManager.OnBindingsChanged += (sender, e) => bindingsChangedEventFired = true;

            // Act
            _inputManager.KeyBindings.BindButton(buttonId, lane);

            // Assert
            Assert.Equal(lane, _inputManager.KeyBindings.GetLane(buttonId));
            Assert.True(bindingsChangedEventFired);
        }

        [Fact]
        public void MultipleButtonsToSameLane_AllWork()
        {
            // Arrange
            var lane = 4; // Bass drum

            // Act
            _inputManager.KeyBindings.BindButton("Key.B", lane);
            _inputManager.KeyBindings.BindButton("Key.N", lane);

            // Assert
            Assert.Equal(lane, _inputManager.KeyBindings.GetLane("Key.Space")); // Original
            Assert.Equal(lane, _inputManager.KeyBindings.GetLane("Key.B"));     // New
            Assert.Equal(lane, _inputManager.KeyBindings.GetLane("Key.N"));     // New

            var buttonsForLane = _inputManager.KeyBindings.GetButtonsForLane(lane);
            Assert.Contains("Key.Space", buttonsForLane);
            Assert.Contains("Key.B", buttonsForLane);
            Assert.Contains("Key.N", buttonsForLane);
        }

        [Fact]
        public void UnbindButton_RemovesBinding()
        {
            // Arrange
            var buttonId = "Key.A";
            var originalLane = _inputManager.KeyBindings.GetLane(buttonId);

            // Act
            _inputManager.KeyBindings.UnbindButton(buttonId);

            // Assert
            Assert.Equal(-1, _inputManager.KeyBindings.GetLane(buttonId));
            Assert.NotEqual(originalLane, -1); // Verify it was originally bound
        }

        [Fact]
        public void GetLaneDescription_MultipleButtons_ReturnsFormattedString()
        {
            // Arrange
            var lane = 0;
            _inputManager.KeyBindings.BindButton("Key.Q", lane);

            // Act
            var description = _inputManager.KeyBindings.GetLaneDescription(lane);

            // Assert
            Assert.Contains("A", description);
            Assert.Contains("Q", description);
        }

        [Fact]
        public void FormatButtonId_VariousInputs_ReturnsCorrectFormat()
        {
            // Act & Assert
            Assert.Equal("A", KeyBindings.FormatButtonId("Key.A"));
            Assert.Equal("Space", KeyBindings.FormatButtonId("Key.Space"));
            Assert.Equal(";", KeyBindings.FormatButtonId("Key.OemSemicolon"));
            Assert.Equal("MIDI 36", KeyBindings.FormatButtonId("MIDI.36"));
            Assert.Equal("Pad X", KeyBindings.FormatButtonId("Pad.X"));
        }

        [Theory]
        [InlineData(0, "LC (Left Cymbal)")]
        [InlineData(1, "LP (Left Pedal)")]
        [InlineData(2, "HH (Hi-Hat)")]
        [InlineData(3, "SD (Snare Drum)")]
        [InlineData(4, "BD (Bass Drum)")]
        [InlineData(5, "HT (High Tom)")]
        [InlineData(6, "LT (Low Tom)")]
        [InlineData(7, "FT (Floor Tom)")]
        [InlineData(8, "CY (Right Cymbal)")]
        public void GetLaneName_ValidLanes_ReturnsCorrectNames(int lane, string expectedName)
        {
            // Act
            var actualName = KeyBindings.GetLaneName(lane);

            // Assert
            Assert.Equal(expectedName, actualName);
        }

        [Fact]
        public void ButtonStateCreation_ValidParameters_CreatesCorrectly()
        {
            // Arrange
            var id = "Key.A";
            var isPressed = true;
            var velocity = 0.8f;

            // Act
            var buttonState = new ButtonState(id, isPressed, velocity);

            // Assert
            Assert.Equal(id, buttonState.Id);
            Assert.Equal(isPressed, buttonState.IsPressed);
            Assert.Equal(velocity, buttonState.Velocity);
            Assert.True((DateTime.UtcNow - buttonState.Timestamp).TotalSeconds < 1);
        }

        [Fact]
        public void LaneHitEventArgs_ValidParameters_CreatesCorrectly()
        {
            // Arrange
            var lane = 3;
            var buttonState = new ButtonState("Key.F", true, 1.0f);

            // Act
            var eventArgs = new LaneHitEventArgs(lane, buttonState);

            // Assert
            Assert.Equal(lane, eventArgs.Lane);
            Assert.Equal(buttonState, eventArgs.Button);
            Assert.True((DateTime.UtcNow - eventArgs.Timestamp).TotalSeconds < 1);
        }

        public void Dispose()
        {
            _inputManager?.Dispose();
        }
    }

    /// <summary>
    /// Integration tests for the complete input system
    /// </summary>
    public class InputSystemIntegrationTests : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly ModularInputManager _inputManager;

        public InputSystemIntegrationTests()
        {
            _configManager = new ConfigManager();
            _inputManager = new ModularInputManager(_configManager);
        }

        [Fact]
        public void EndToEndScenario_ModifyBindingsAndSave_PersistsCorrectly()
        {
            // Arrange
            var customButtonId = "Key.Q";
            var targetLane = 0;

            // Act - Modify bindings
            _inputManager.KeyBindings.BindButton(customButtonId, targetLane);
            _inputManager.SaveKeyBindings();

            // Create new manager to test persistence
            using var newManager = new ModularInputManager(_configManager);

            // Assert
            Assert.Equal(targetLane, newManager.KeyBindings.GetLane(customButtonId));
        }

        [Fact]
        public void PerformanceTest_UpdateLoop_MeetsLatencyTarget()
        {
            // Arrange - Run multiple update cycles
            var iterations = 1000;
            var totalTime = 0.0;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                _inputManager.Update(16.67); // ~60 FPS
                totalTime += _inputManager.LastUpdateTimeMs;
            }

            var averageTime = totalTime / iterations;

            // Assert - Should average well under 1ms
            Assert.True(averageTime < 1.0, $"Average update time {averageTime:F2}ms exceeds 1ms target");
        }

        [Fact]
        public void StressTest_ManyBindings_HandlesEfficiently()
        {
            // Arrange - Add many bindings
            for (int i = 0; i < 100; i++)
            {
                _inputManager.KeyBindings.BindButton($"TestButton{i}", i % 9);
            }

            // Act
            _inputManager.Update();

            // Assert - Should handle many bindings without issues
            Assert.True(_inputManager.KeyBindings.ButtonToLane.Count >= 100);
            Assert.True(_inputManager.LastUpdateTimeMs < 5.0); // Should still be fast
        }

        public void Dispose()
        {
            _inputManager?.Dispose();
        }
    }
}
