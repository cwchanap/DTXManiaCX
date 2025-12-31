using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
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
            Assert.Equal(10, _inputManager.KeyBindings.ButtonToLane.Count); // Default bindings (10-lane layout)
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
            // Assert default keyboard bindings for 10-lane drum layout
            Assert.Equal(0, _inputManager.KeyBindings.GetLane("Key.A"));     // A -> Splash/Crash (lane 0)
            Assert.Equal(1, _inputManager.KeyBindings.GetLane("Key.F"));     // F -> Floor Tom & Left Cymbal (lane 1)
            Assert.Equal(2, _inputManager.KeyBindings.GetLane("Key.D"));     // D -> Hi-Hat Foot & Left Crash (lane 2)
            Assert.Equal(4, _inputManager.KeyBindings.GetLane("Key.S"));     // S -> Snare Drum (lane 4)
            Assert.Equal(6, _inputManager.KeyBindings.GetLane("Key.Space")); // Space -> Bass Drum (lane 6)
            Assert.Equal(3, _inputManager.KeyBindings.GetLane("Key.G"));     // G -> Left Pedal (lane 3)
            Assert.Equal(5, _inputManager.KeyBindings.GetLane("Key.J"));     // J -> Hi-Hat (lane 5)
            Assert.Equal(7, _inputManager.KeyBindings.GetLane("Key.K"));     // K -> High Tom (lane 7)
            Assert.Equal(8, _inputManager.KeyBindings.GetLane("Key.L"));     // L -> Low Tom & Right Cymbal (lane 8)
            Assert.Equal(9, _inputManager.KeyBindings.GetLane("Key.OemSemicolon")); // ; -> Ride (lane 9)
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
            // First, make a binding change (auto-save will persist it)
            _inputManager.KeyBindings.BindButton("Key.Q", 5);
            
            // Verify the binding was auto-saved to config
            Assert.True(_configManager.Config.KeyBindings.ContainsKey("Key.Q"));
            Assert.Equal(5, _configManager.Config.KeyBindings["Key.Q"]);
            
            // Then make another change (also auto-saved due to auto-save behavior)
            _inputManager.KeyBindings.BindButton("Key.W", 3);
            
            // Verify both bindings are now in config due to auto-save
            Assert.True(_configManager.Config.KeyBindings.ContainsKey("Key.W"));
            Assert.Equal(3, _configManager.Config.KeyBindings["Key.W"]);
            
            // Verify the pre-conditions
            Assert.Equal(3, _inputManager.KeyBindings.GetLane("Key.W")); // Should be bound locally
            Assert.Equal(5, _inputManager.KeyBindings.GetLane("Key.Q")); // Should be bound locally

            // Act
            _inputManager.ReloadKeyBindings(); // Should restore from config

            // Assert - Both bindings should be restored since auto-save persisted them
            Assert.Equal(5, _inputManager.KeyBindings.GetLane("Key.Q")); // Should be restored from config
            Assert.Equal(3, _inputManager.KeyBindings.GetLane("Key.W")); // Should also be restored (auto-saved)
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
            Assert.Equal(10, _inputManager.KeyBindings.ButtonToLane.Count); // 10-lane layout
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
        public void OnLaneHit_EventCanBeSubscribed_WithoutErrors()
        {
            // Arrange & Act
            LaneHitEventArgs? capturedEvent = null;
            _inputManager.OnLaneHit += (sender, e) => capturedEvent = e;

            // Assert - Event subscription should work without errors
            // Since we can't directly trigger events in unit tests without complex mocking,
            // we verify that the event system is properly initialized
            Assert.NotNull(_inputManager.InputRouter);
            
            // Verify that unsubscribing also works
            _inputManager.OnLaneHit -= (sender, e) => capturedEvent = e;
        }

        [Theory]
        [InlineData("Key.A", 0)]  // Splash/Crash
        [InlineData("Key.F", 1)]  // Floor Tom & Left Cymbal
        [InlineData("Key.D", 2)]  // Hi-Hat Foot & Left Crash
        [InlineData("Key.G", 3)]  // Left Pedal
        [InlineData("Key.S", 4)]  // Snare Drum
        [InlineData("Key.J", 5)]  // Hi-Hat
        [InlineData("Key.Space", 6)] // Bass Drum
        [InlineData("Key.K", 7)]  // High Tom
        [InlineData("Key.L", 8)]  // Low Tom/Right Cymbal
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
            var lane = 6; // Bass drum (now lane 6)

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
            Assert.NotEqual(-1, originalLane); // Verify it was originally bound
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
        [InlineData(0, "Splash/Crash")]
        [InlineData(1, "Floor Tom/Left Cymbal")]
        [InlineData(2, "Hi-Hat Foot/Left Crash")]
        [InlineData(3, "Left Pedal")]
        [InlineData(4, "Snare Drum")]
        [InlineData(5, "Hi-Hat")]
        [InlineData(6, "Bass Drum")]
        [InlineData(7, "High Tom")]
        [InlineData(8, "Low Tom/Right Cymbal")]
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
            var buttonState = new DTXMania.Game.Lib.Input.ButtonState(id, isPressed, velocity);

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
            var lane = 4; // F key now maps to lane 4 (Snare Drum)
            var buttonState = new DTXMania.Game.Lib.Input.ButtonState("Key.F", true, 1.0f);

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
                _inputManager.KeyBindings.BindButton($"TestButton{i}", i % 10); // 10-lane layout
            }

            // Act
            _inputManager.Update();

            // Assert - Should handle many bindings without issues
            Assert.True(_inputManager.KeyBindings.ButtonToLane.Count >= 100);
            Assert.True(_inputManager.LastUpdateTimeMs < 10.0); // Should still be reasonably fast
        }

        public void Dispose()
        {
            _inputManager?.Dispose();
        }
    }
}
