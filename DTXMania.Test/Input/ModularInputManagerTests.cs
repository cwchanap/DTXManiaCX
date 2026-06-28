using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    /// <summary>
    /// Unit tests for the ModularInputManager
    /// Tests keyboard input, key bindings, and runtime reconfiguration
    /// </summary>
    [Trait("Category", "Unit")]
    public class ModularInputManagerTests : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly TestMidiDeviceBackend _midiBackend;
        private readonly ModularInputManager _inputManager;

        public ModularInputManagerTests()
        {
            _configManager = new ConfigManager();
            _midiBackend = new TestMidiDeviceBackend();
            _inputManager = new ModularInputManager(_configManager, _midiBackend);
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
        public void Constructor_ShouldRegisterKeyboardAndMidiSources()
        {
            // Act
            var diagnostics = _inputManager.GetDiagnosticsInfo();

            // Assert
            Assert.Equal(2, _inputManager.InputRouter.GetSourceCount());
            Assert.Contains("Input Sources: 2", diagnostics);
            Assert.Contains("Keyboard", diagnostics);
            Assert.Contains("MIDI", diagnostics);
            Assert.Equal(1, _midiBackend.GetInputDevicesCallCount);
        }

        [Fact]
        public void Constructor_NullConfigManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ModularInputManager(null, new TestMidiDeviceBackend()));
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
        public void Constructor_LegacyOverrideOnlyConfig_ShouldPreserveDefaultsAndApplyOverride()
        {
            // Arrange
            using var legacyInputManager = CreateInputManagerWithConfig(config =>
            {
                config.KeyBindings["Key.Q"] = 5;
            });

            // Assert
            Assert.Equal(5, legacyInputManager.KeyBindings.GetLane("Key.Q"));
            Assert.Equal(0, legacyInputManager.KeyBindings.GetLane("Key.A"));
            Assert.Equal(6, legacyInputManager.KeyBindings.GetLane("Key.Space"));
        }

        [Fact]
        public void Constructor_UnboundLaneInSavedConfig_ShouldKeepLaneUnboundAndPreserveOtherDefaults()
        {
            // Arrange
            var configManager = new ConfigManager();
            var keyBindings = new KeyBindings();
            keyBindings.UnbindLane(4);
            configManager.SaveKeyBindings(keyBindings);

            using var configuredInputManager = new ModularInputManager(configManager, new TestMidiDeviceBackend());

            // Assert
            Assert.Equal(-1, configuredInputManager.KeyBindings.GetLane("Key.S"));
            Assert.Equal(0, configuredInputManager.KeyBindings.GetLane("Key.A"));
            Assert.Equal(6, configuredInputManager.KeyBindings.GetLane("Key.Space"));
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
        public void ReloadKeyBindings_AfterModification_RestoresFromConfig()
        {
            // Arrange
            // Runtime mutations no longer auto-save back to Config (unidirectional
            // Config -> runtime flow). Persist explicitly via ConfigManager, then
            // verify ReloadKeyBindings restores from Config.
            _inputManager.KeyBindings.BindButton("Key.Q", 5);
            _inputManager.KeyBindings.BindButton("Key.W", 3);
            _configManager.SaveKeyBindings(_inputManager.KeyBindings);

            // Verify both bindings are persisted to Config
            Assert.True(_configManager.Config.KeyBindings.ContainsKey("Key.Q"));
            Assert.Equal(5, _configManager.Config.KeyBindings["Key.Q"]);
            Assert.True(_configManager.Config.KeyBindings.ContainsKey("Key.W"));
            Assert.Equal(3, _configManager.Config.KeyBindings["Key.W"]);

            // Verify the pre-conditions
            Assert.Equal(3, _inputManager.KeyBindings.GetLane("Key.W")); // Should be bound locally
            Assert.Equal(5, _inputManager.KeyBindings.GetLane("Key.Q")); // Should be bound locally

            // Act
            _inputManager.ReloadKeyBindings(); // Should restore from config

            // Assert - Both bindings should be restored since they were persisted
            Assert.Equal(5, _inputManager.KeyBindings.GetLane("Key.Q")); // Should be restored from config
            Assert.Equal(3, _inputManager.KeyBindings.GetLane("Key.W")); // Should also be restored (persisted)
        }

        [Fact]
        public void ReloadKeyBindings_RemappedDefaultDrumKey_ShouldNotRestoreRemovedDefaultButton()
        {
            var persistedBindings = new KeyBindings();
            persistedBindings.UnbindButton("Key.Space");
            persistedBindings.BindButton("Key.B", 6);
            _configManager.SaveKeyBindings(persistedBindings);

            Assert.Contains("Key.Space", _configManager.Config.UnboundDrumButtons);
            Assert.Equal(6, _configManager.Config.KeyBindings["Key.B"]);

            _inputManager.ReloadKeyBindings();

            Assert.Equal(-1, _inputManager.KeyBindings.GetLane("Key.Space"));
            Assert.Equal(6, _inputManager.KeyBindings.GetLane("Key.B"));
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
        public void AddInputSource_ShouldInitializeSourceExactlyOnce()
        {
            // Arrange
            var source = new CountingInputSource("Counting");

            // Act
            _inputManager.AddInputSource(source);

            // Assert
            Assert.Equal(1, source.InitializeCount);
        }

        [Fact]
        public void Dispose_WithAddedInputSource_ShouldDisposeSourceExactlyOnce()
        {
            // Arrange
            var source = new CountingInputSource("Counting");
            _inputManager.AddInputSource(source);

            // Act
            _inputManager.Dispose();

            // Assert
            Assert.Equal(1, source.DisposeCount);
        }

        [Fact]
        public void AddInputSource_WhenInitializeThrows_ShouldNotRegisterSource()
        {
            // AddInputSource must initialize BEFORE registering (or roll back), so a thrown
            // Initialize() cannot leave the source half-registered in _inputSources/_inputRouter.
            var sourceBeforeCount = _inputManager.InputRouter.GetSourceCount();
            var throwingSource = new ThrowingInputSource("ThrowsOnInit");

            Assert.Throws<InvalidOperationException>(() => _inputManager.AddInputSource(throwingSource));

            // The source count must be unchanged — the failed source was never registered.
            Assert.Equal(sourceBeforeCount, _inputManager.InputRouter.GetSourceCount());
            // Initialize WAS attempted (and is what threw).
            Assert.Equal(1, throwingSource.InitializeCount);
        }

        [Fact]
        public void Update_WhenMidiDevicesChangePastScanInterval_ShouldRefreshDiagnostics()
        {
            // Arrange
            var midiBackend = new TestMidiDeviceBackend(new TestMidiInputDevice("legacy", "Legacy Module"));
            using var inputManager = new ModularInputManager(new ConfigManager(), midiBackend);

            midiBackend.SetDevices(
                new TestMidiInputDevice("nitro", "Alesis Nitro"),
                new TestMidiInputDevice("dtx", "Yamaha DTX"));

            // Act
            inputManager.Update(GameConstants.Input.DeviceScanIntervalMs / 1000.0);
            var diagnostics = inputManager.GetDiagnosticsInfo();

            // Assert
            Assert.Contains("MIDI Devices: 2", diagnostics);
            Assert.Contains("Alesis Nitro", diagnostics);
            Assert.Contains("Yamaha DTX", diagnostics);
            Assert.DoesNotContain("Legacy Module", diagnostics);
            Assert.Equal(2, midiBackend.GetInputDevicesCallCount);
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

        [Fact]
        public void InjectButton_WithLaneMapping_ShouldAddToPressedThisFrame()
        {
            // Arrange
            var buttonId = "Key.A"; // Maps to lane 0 by default
            
            // Act
            _inputManager.InjectButton(buttonId, true, 1.0f);
            _inputManager.Update();

            // Assert
            var pressedButtons = _inputManager.ConsumePressedButtons();
            Assert.Contains(pressedButtons, b => b.Id == buttonId && b.IsPressed);
        }

        [Fact]
        public void InjectButton_WithKeyCode_ShouldUpdateInjectedKeyStates()
        {
            // Arrange
            var buttonId = "Key.Q";
            
            // Act
            _inputManager.InjectButton(buttonId, true, 1.0f);
            _inputManager.Update();

            // Assert - The key should be in the pressed events queue
            var pressEvents = _inputManager.DrainInjectedPressEvents();
            Assert.Contains((int)Keys.Q, pressEvents);
        }

        [Fact]
        public void InjectButton_WithRelease_ShouldRemoveFromInjectedKeyStates()
        {
            // Arrange
            var buttonId = "Key.Q";
            _inputManager.InjectButton(buttonId, true, 1.0f);
            _inputManager.Update();
            
            // Act - Release the button
            _inputManager.InjectButton(buttonId, false, 0.0f);
            _inputManager.Update();

            // Assert - The key should no longer be in pressed events after release
            var pressEvents = _inputManager.DrainInjectedPressEvents();
            Assert.DoesNotContain((int)Keys.Q, pressEvents);
        }

        public void Dispose()
        {
            _inputManager?.Dispose();
        }

        private static ModularInputManager CreateInputManagerWithConfig(Action<ConfigData> configure)
        {
            var configManager = new ConfigManager();
            configure(configManager.Config);
            return new ModularInputManager(configManager, new TestMidiDeviceBackend());
        }

        private sealed class CountingInputSource : IInputSource
        {
            public CountingInputSource(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public bool IsAvailable => true;

            public int InitializeCount { get; private set; }

            public int DisposeCount { get; private set; }

            public void Initialize()
            {
                InitializeCount++;
            }

            public IEnumerable<DTXMania.Game.Lib.Input.ButtonState> Update()
            {
                return Array.Empty<DTXMania.Game.Lib.Input.ButtonState>();
            }

            public IEnumerable<DTXMania.Game.Lib.Input.ButtonState> GetPressedButtons()
            {
                return Array.Empty<DTXMania.Game.Lib.Input.ButtonState>();
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class ThrowingInputSource : IInputSource
        {
            public ThrowingInputSource(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public bool IsAvailable => true;

            public int InitializeCount { get; private set; }

            public void Initialize()
            {
                InitializeCount++;
                throw new InvalidOperationException("Initialize failed.");
            }

            public IEnumerable<DTXMania.Game.Lib.Input.ButtonState> Update()
                => Array.Empty<DTXMania.Game.Lib.Input.ButtonState>();

            public IEnumerable<DTXMania.Game.Lib.Input.ButtonState> GetPressedButtons()
                => Array.Empty<DTXMania.Game.Lib.Input.ButtonState>();

            public void Dispose() { }
        }

        // The shared TestMidiDeviceBackend / TestMidiInputDevice helpers (TestMidiDeviceBackend.cs)
        // are used instead of per-class duplicates so backend fake behavior stays in one place.
    }

    /// <summary>
    /// Integration tests for the complete input system
    /// </summary>
    [Trait("Category", "Integration")]
    public class InputSystemIntegrationTests : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly TestMidiDeviceBackend _midiBackend;
        private readonly ModularInputManager _inputManager;

        public InputSystemIntegrationTests()
        {
            _configManager = new ConfigManager();
            _midiBackend = new TestMidiDeviceBackend();
            _inputManager = new ModularInputManager(_configManager, _midiBackend);
        }

        [Fact]
        public void EndToEndScenario_ModifyBindingsAndSave_PersistsCorrectly()
        {
            // Arrange
            var customButtonId = "Key.Q";
            var targetLane = 0;

            // Act - Modify bindings
            _inputManager.KeyBindings.BindButton(customButtonId, targetLane);
            _configManager.SaveKeyBindings(_inputManager.KeyBindings);

            // Create new manager to test persistence
            using var newManager = new ModularInputManager(_configManager, new TestMidiDeviceBackend());

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

        [Fact]
        public void OnInputRouterButtonPressed_RecordsButtonForConsumePressedButtons()
        {
            // The router raises OnButtonPressed synchronously during Update for each press; the
            // manager's handler records it so ConsumePressedButtons can drain it once per frame.
            var button = new DTXMania.Game.Lib.Input.ButtonState("Key.Z", true, 0.8f);

            var method = typeof(ModularInputManager).GetMethod("OnInputRouterButtonPressed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(_inputManager, new object[] { _inputManager, button });

            var pressed = _inputManager.ConsumePressedButtons();
            Assert.Contains(pressed, b => b.Id == "Key.Z");

            // A second drain in the same frame returns empty (the buffer was consumed).
            Assert.Empty(_inputManager.ConsumePressedButtons());
        }

        public void Dispose()
        {
            _inputManager?.Dispose();
        }
    }
}
