using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Modular input manager that provides unified input handling through InputRouter
    /// Maintains backward compatibility with existing InputManager API
    /// Supports hot-swappable key bindings and multiple input sources
    /// </summary>
    public class ModularInputManager : IDisposable
    {
        #region Private Fields

        private readonly ConfigManager _configManager;
        private readonly KeyBindings _keyBindings;
        private readonly InputRouter _inputRouter;
        private readonly List<IInputSource> _inputSources;
        private bool _disposed = false;

        // Legacy compatibility fields
        private readonly Dictionary<int, bool> _keyStates;
        private readonly Dictionary<int, bool> _previousKeyStates;

        // ESC key debouncing state
        private bool _escapeLastState = false;

        // Performance diagnostics
        private readonly Stopwatch _updateStopwatch;
        private double _lastUpdateTimeMs = 0.0;

        // Hot-plug detection
        private double _lastDeviceScanTime = 0.0;
        private const double DeviceScanIntervalMs = 3000.0; // 3 seconds

        #endregion

        #region Events

        /// <summary>
        /// Raised when a lane is hit (from InputRouter)
        /// </summary>
        public event EventHandler<LaneHitEventArgs>? OnLaneHit;

        /// <summary>
        /// Raised when key bindings are changed
        /// </summary>
        public event EventHandler? OnBindingsChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current key bindings
        /// </summary>
        public KeyBindings KeyBindings => _keyBindings;

        /// <summary>
        /// Gets the input router
        /// </summary>
        public InputRouter InputRouter => _inputRouter;

        /// <summary>
        /// Gets performance statistics for the last update cycle
        /// </summary>
        public double LastUpdateTimeMs => _lastUpdateTimeMs;

        #endregion

        #region Constructor

        public ModularInputManager(ConfigManager configManager)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _keyBindings = new KeyBindings();
            _inputRouter = new InputRouter(_keyBindings);
            _inputSources = new List<IInputSource>();
            _keyStates = new Dictionary<int, bool>();
            _previousKeyStates = new Dictionary<int, bool>();
            _updateStopwatch = new Stopwatch();

            // Load key bindings from config
            _configManager.LoadKeyBindings(_keyBindings);

            // Set up event handlers
            _keyBindings.BindingsChanged += OnKeyBindingsChanged;
            _inputRouter.OnLaneHit += OnInputRouterLaneHit;

            // Initialize input sources
            InitializeInputSources();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes all input sources (Phase 1: Keyboard only)
        /// </summary>
        private void InitializeInputSources()
        {
            // Phase 1: Add keyboard input source
            var keyboardSource = new KeyboardInputSource();
            AddInputSource(keyboardSource);

            // TODO: Phase 2: Add MIDI input source
            // TODO: Phase 3: Add gamepad input source

            _inputRouter.Initialize();
        }

        /// <summary>
        /// Adds an input source to the system
        /// </summary>
        /// <param name="source">Input source to add</param>
        public void AddInputSource(IInputSource source)
        {
            _inputSources.Add(source);
            _inputRouter.AddInputSource(source);
            source.Initialize();
            Debug.WriteLine($"[ModularInputManager] Added input source: {source.Name}");
        }

        #endregion

        #region Update Methods

        /// <summary>
        /// Updates all input sources and processes input events
        /// Target: ≤1ms latency for keyboard input
        /// </summary>
        /// <param name="deltaTime">Time since last update (optional)</param>
        private int _frameCount = 0;
        
        public void Update(double deltaTime = 0.0)
        {
            if (_disposed) return;

            // Start performance measurement
            _updateStopwatch.Restart();

            // Update previous key states for legacy compatibility
            UpdateLegacyKeyStates();


            // Update input router (processes all input sources)
            _inputRouter.Update();

            // Hot-plug device detection
            if (deltaTime > 0)
            {
                CheckForDeviceChanges(deltaTime);
            }

            // Record performance
            _updateStopwatch.Stop();
            _lastUpdateTimeMs = _updateStopwatch.Elapsed.TotalMilliseconds;

            // Performance warning if update takes too long
            if (_lastUpdateTimeMs > 1.0) // Target: ≤1ms
            {
                Debug.WriteLine($"[ModularInputManager] Update took {_lastUpdateTimeMs:F2}ms (target: ≤1ms)");
            }
        }

        /// <summary>
        /// Updates legacy key states for backward compatibility
        /// </summary>
        private void UpdateLegacyKeyStates()
        {
            // Move current states to previous
            _previousKeyStates.Clear();
            foreach (var kvp in _keyStates)
            {
                _previousKeyStates[kvp.Key] = kvp.Value;
            }

            // Always clear current key states to prevent stale keys
            _keyStates.Clear();

            // Get current keyboard state directly from keyboard input source
            var keyboardSource = GetKeyboardSource();
            if (keyboardSource != null)
            {
                // Get currently pressed keys from MonoGame
                var currentState = Keyboard.GetState();
                var pressedKeys = currentState.GetPressedKeys();
                
                // Update key states for all pressed keys
                foreach (var key in pressedKeys)
                {
                    _keyStates[(int)key] = true;
                }
            }
        }

        /// <summary>
        /// Checks for device changes (hot-plug detection)
        /// </summary>
        /// <param name="deltaTime">Time since last update</param>
        private void CheckForDeviceChanges(double deltaTime)
        {
            _lastDeviceScanTime += deltaTime * 1000.0; // Convert to milliseconds

            if (_lastDeviceScanTime >= DeviceScanIntervalMs)
            {
                _lastDeviceScanTime = 0.0;
                ScanForNewDevices();
            }
        }

        /// <summary>
        /// Scans for new input devices (hot-plug support)
        /// </summary>
        private void ScanForNewDevices()
        {
            // TODO: Phase 2 - Scan for MIDI devices
            // TODO: Phase 3 - Scan for gamepad devices
            Debug.WriteLine("[ModularInputManager] Device scan completed");
        }

        #endregion

        #region Key Bindings Management

        /// <summary>
        /// Saves current key bindings to configuration
        /// Auto-saves when bindings change
        /// </summary>
        public void SaveKeyBindings()
        {
            _configManager.SaveKeyBindings(_keyBindings);
            Debug.WriteLine("[ModularInputManager] Key bindings saved to configuration");
        }

        /// <summary>
        /// Reloads key bindings from configuration
        /// </summary>
        public void ReloadKeyBindings()
        {
            // Clear existing bindings
            _keyBindings.ClearAllBindings();
            
            // Load from config
            _configManager.LoadKeyBindings(_keyBindings);
            
            Debug.WriteLine("[ModularInputManager] Key bindings reloaded from configuration");
        }

        /// <summary>
        /// Resets key bindings to defaults
        /// </summary>
        public void ResetKeyBindingsToDefaults()
        {
            _keyBindings.LoadDefaultBindings();
            SaveKeyBindings();
            Debug.WriteLine("[ModularInputManager] Key bindings reset to defaults");
        }
        
        /// <summary>
        /// Forces a complete reset of key bindings, clearing any saved overrides
        /// </summary>
        public void ForceResetKeyBindings()
        {
            // Clear the config completely
            _configManager.Config.KeyBindings.Clear();
            
            // Reload defaults
            _keyBindings.LoadDefaultBindings();
            
            // Save the new defaults
            SaveKeyBindings();
            Debug.WriteLine("[ModularInputManager] Key bindings force reset to defaults");
        }

        #endregion

        #region Legacy Compatibility API

        /// <summary>
        /// Legacy method: Checks if a key was just pressed (rising edge)
        /// Maintains compatibility with existing JudgementManager
        /// </summary>
        /// <param name="keyCode">Key code (cast from Keys enum)</param>
        /// <returns>True if key was just pressed</returns>
        public bool IsKeyPressed(int keyCode)
        {
            var currentPressed = _keyStates.TryGetValue(keyCode, out var current) && current;
            var previousPressed = _previousKeyStates.TryGetValue(keyCode, out var previous) && previous;
            
            return currentPressed && !previousPressed;
        }

        /// <summary>
        /// Legacy method: Checks if a key is currently down
        /// </summary>
        /// <param name="keyCode">Key code (cast from Keys enum)</param>
        /// <returns>True if key is currently pressed</returns>
        public bool IsKeyDown(int keyCode)
        {
            return _keyStates.TryGetValue(keyCode, out var pressed) && pressed;
        }

        /// <summary>
        /// Legacy method: Checks if a key was just released (falling edge)
        /// </summary>
        /// <param name="keyCode">Key code (cast from Keys enum)</param>
        /// <returns>True if key was just released</returns>
        public bool IsKeyReleased(int keyCode)
        {
            var currentPressed = _keyStates.TryGetValue(keyCode, out var current) && current;
            var previousPressed = _previousKeyStates.TryGetValue(keyCode, out var previous) && previous;
            
            return !currentPressed && previousPressed;
        }

        /// <summary>
        /// Check if a key was just triggered (edge-trigger)
        /// For ESC key, uses manual debouncing to prevent repeat issues
        /// </summary>
        /// <param name="keyCode">Key code (cast from Keys enum)</param>
        /// <returns>True if key was just triggered</returns>
        public bool IsKeyTriggered(int keyCode)
        {
            var key = (Keys)keyCode;
            
            // Special handling for ESC key with manual debouncing
            if (key == Keys.Escape)
            {
                var currentState = Keyboard.GetState();
                bool currentEscapeState = currentState.IsKeyDown(Keys.Escape);
                bool triggered = currentEscapeState && !_escapeLastState;
                _escapeLastState = currentEscapeState;
                return triggered;
            }
            
            // For other keys, use standard edge detection
            var currentPressed = _keyStates.TryGetValue(keyCode, out var current) && current;
            var previousPressed = _previousKeyStates.TryGetValue(keyCode, out var previous) && previous;
            
            return currentPressed && !previousPressed;
        }

        /// <summary>
        /// Consolidated method to detect "back" action from both ESC key and controller Back button
        /// Uses proper debouncing for ESC key to prevent repeat triggers
        /// This method should be used by all stages instead of duplicating the logic
        /// </summary>
        /// <returns>True if back action was triggered</returns>
        public bool IsBackActionTriggered()
        {
            // Check for ESC key using debounced trigger (already handled in IsKeyTriggered)
            bool escTriggered = IsKeyTriggered((int)Keys.Escape);
            
            // TODO: Add controller Back button support when gamepad input source is implemented
            // For now, only ESC key is supported in ModularInputManager
            
            return escTriggered;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles key bindings changes
        /// </summary>
        private void OnKeyBindingsChanged(object? sender, EventArgs e)
        {
            // Auto-save bindings when they change
            SaveKeyBindings();
            OnBindingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Forwards lane hit events from InputRouter
        /// </summary>
        private void OnInputRouterLaneHit(object? sender, LaneHitEventArgs e)
        {
            OnLaneHit?.Invoke(this, e);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets the keyboard input source
        /// </summary>
        /// <returns>Keyboard input source or null if not found</returns>
        private KeyboardInputSource? GetKeyboardSource()
        {
            foreach (var source in _inputSources)
            {
                if (source is KeyboardInputSource keyboardSource)
                    return keyboardSource;
            }
            return null;
        }

        /// <summary>
        /// Gets diagnostics information about the input system
        /// </summary>
        /// <returns>Diagnostics string</returns>
        public string GetDiagnosticsInfo()
        {
            var info = $"ModularInputManager Diagnostics:\n";
            info += $"  Input Sources: {_inputSources.Count}\n";
            info += $"  Key Bindings: {_keyBindings.ButtonToLane.Count}\n";
            info += $"  Last Update Time: {_lastUpdateTimeMs:F2}ms\n";
            info += $"  Active Keys: {_keyStates.Count(kvp => kvp.Value)}\n";

            foreach (var source in _inputSources)
            {
                info += $"  Source '{source.Name}': {(source.IsAvailable ? "Available" : "Unavailable")}\n";
            }

            return info;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from events
                    _keyBindings.BindingsChanged -= OnKeyBindingsChanged;
                    _inputRouter.OnLaneHit -= OnInputRouterLaneHit;

                    // Dispose input router and sources
                    _inputRouter?.Dispose();
                    
                    foreach (var source in _inputSources)
                    {
                        source?.Dispose();
                    }
                    _inputSources.Clear();

                    // Clear collections
                    _keyStates?.Clear();
                    _previousKeyStates?.Clear();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
