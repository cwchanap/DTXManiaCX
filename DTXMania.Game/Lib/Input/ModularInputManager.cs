using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input.Midi;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Modular input manager that provides unified input handling through InputRouter.
    /// Maintains backward compatibility with existing InputManager API.
    /// Supports hot-swappable key bindings and multiple input sources.
    /// </summary>
    /// <remarks>
    /// Nullable reference types are disabled. Event handlers (OnLaneHit, OnBindingsChanged)
    /// may be null if no subscribers are attached; invocations use null-conditional operators.
    /// Device scan interval is configured via GameConstants.Input.DeviceScanIntervalMs.
    /// </remarks>
    public class ModularInputManager : IDisposable
    {
        #region Private Fields

        private readonly ConfigManager _configManager;
        private readonly KeyBindings _keyBindings;
        private readonly InputRouter _inputRouter;
        private readonly List<IInputSource> _inputSources;
#nullable enable annotations
        // These fields are genuinely nullable: _midiInputSource is null until
        // InitializeInputSources() runs; _midiNoteInjector is null when the MIDI
        // backend does not implement IMidiNoteInjector (e.g. production DryWetMIDI).
        private MidiInputSource? _midiInputSource;
        private readonly IMidiNoteInjector? _midiNoteInjector;
#nullable restore annotations
        private readonly ConcurrentQueue<ButtonState> _injectedButtonQueue;
        private readonly Dictionary<int, bool> _injectedKeyStates;
        // Queue of key codes whose press events were just dequeued this frame (for event-driven command dispatch).
        // ConcurrentQueue used for thread safety when ClearInjectedState is called from stage transitions.
        private readonly ConcurrentQueue<int> _injectedPressEvents;

        // Buttons that transitioned to pressed this frame (any source + injected).
        // Rebuilt every Update(); drained by ConsumePressedButtons() for binding-capture UIs.
        private readonly List<ButtonState> _pressedThisFrame = new();
        private bool _disposed = false;

        // Legacy compatibility fields
        private readonly Dictionary<int, bool> _keyStates;
        private readonly Dictionary<int, bool> _previousKeyStates;
        private readonly Keys[] _pressedKeysBuffer = new Keys[256]; // Reused each frame to avoid GetPressedKeys() allocation

        // ESC key debouncing state
        private bool _escapeLastState = false;

        // Performance diagnostics
        private readonly Stopwatch _updateStopwatch;
        private double _lastUpdateTimeMs = 0.0;

        // Hot-plug detection
        private double _lastDeviceScanTime = 0.0;

        #endregion

        #region Events

        /// <summary>
        /// Raised when a lane is hit (from InputRouter). May be null if no handler is attached.
        /// </summary>
        public event EventHandler<LaneHitEventArgs> OnLaneHit;

        /// <summary>
        /// Raised when key bindings are changed. May be null if no handler is attached.
        /// </summary>
        public event EventHandler OnBindingsChanged;

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
            : this(configManager, MidiDeviceBackendFactory.CreateDefault())
        {
        }

        internal ModularInputManager(ConfigManager configManager, IMidiDeviceBackend midiDeviceBackend)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            if (midiDeviceBackend is null) throw new ArgumentNullException(nameof(midiDeviceBackend));

            _keyBindings = new KeyBindings();
            _inputRouter = new InputRouter(_keyBindings);
            _inputSources = new List<IInputSource>();
            _injectedButtonQueue = new ConcurrentQueue<ButtonState>();
            _injectedKeyStates = new Dictionary<int, bool>();
            _injectedPressEvents = new ConcurrentQueue<int>();
            _keyStates = new Dictionary<int, bool>();
            _previousKeyStates = new Dictionary<int, bool>();
            _updateStopwatch = new Stopwatch();
            _midiNoteInjector = midiDeviceBackend as IMidiNoteInjector;

            _configManager.LoadKeyBindings(_keyBindings);

            // Set up event handlers
            _keyBindings.BindingsChanged += OnKeyBindingsChanged;
            _inputRouter.OnLaneHit += OnInputRouterLaneHit;
            _inputRouter.OnButtonPressed += OnInputRouterButtonPressed;

            // Initialize input sources
            InitializeInputSources(midiDeviceBackend);
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes all input sources.
        /// </summary>
        private void InitializeInputSources(IMidiDeviceBackend midiDeviceBackend)
        {
            var keyboardSource = new KeyboardInputSource();
            RegisterInputSource(keyboardSource);

            _midiInputSource = new MidiInputSource(
                midiDeviceBackend,
                noteNumber => _configManager.GetMidiVelocityThreshold(noteNumber));
            RegisterInputSource(_midiInputSource);

            // TODO: Phase 3: Add gamepad input source

            _inputRouter.Initialize();
        }

        private void RegisterInputSource(IInputSource source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            _inputSources.Add(source);
            _inputRouter.AddInputSource(source);
        }

        /// <summary>
        /// Adds an input source to the system
        /// </summary>
        /// <param name="source">Input source to add</param>
        public void AddInputSource(IInputSource source)
        {
            // Initialize BEFORE registering so a thrown Initialize() cannot leave _inputSources
            // and _inputRouter in a half-registered state. Registration only happens once init
            // has succeeded.
            source.Initialize();
            RegisterInputSource(source);
        }

        #endregion

        #region Update Methods

        /// <summary>
        /// Updates all input sources and processes input events
        /// Target: ≤1ms latency for keyboard input
        /// </summary>
        /// <param name="deltaTime">Time since last update (optional)</param>
        public void Update(double deltaTime = 0.0)
        {
            if (_disposed) return;

            // Start performance measurement
            _updateStopwatch.Restart();

            // Reset the per-frame pressed-buttons buffer before the router/injected processing
            // below (re)populate it, so synchronously-raised OnButtonPressed events this frame
            // aren't wiped out.
            _pressedThisFrame.Clear();

            // Update previous key states for legacy compatibility
            UpdateLegacyKeyStates();

            // Process externally injected inputs (e.g., MCP) on the main thread
            ProcessInjectedInputs();

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
                // Update took longer than target
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
                int pressedCount = currentState.GetPressedKeyCount();
                currentState.GetPressedKeys(_pressedKeysBuffer);

                // Update key states for all pressed keys
                for (int i = 0; i < pressedCount; i++)
                {
                    _keyStates[(int)_pressedKeysBuffer[i]] = true;
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

            if (_lastDeviceScanTime >= GameConstants.Input.DeviceScanIntervalMs)
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
            _midiInputSource?.RefreshDevices();
            // TODO: Phase 3 - Scan for gamepad devices
        }

        #endregion

        #region Key Bindings Management

        /// <summary>
        /// Reloads key bindings from configuration
        /// </summary>
        public void ReloadKeyBindings()
        {
            _keyBindings.BindingsChanged -= OnKeyBindingsChanged;
            try
            {
                _keyBindings.LoadDefaultBindings();
                _configManager.LoadKeyBindings(_keyBindings);
            }
            finally
            {
                _keyBindings.BindingsChanged += OnKeyBindingsChanged;
            }
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
        /// Drains and returns all injected key-press events that arrived this frame.
        /// Each entry is a key code corresponding to a press (not release) event from MCP/API injection.
        /// Used by InputManagerCompat to fire exactly one navigation command per injected press,
        /// without double-counting physical keyboard input that base.Update() already handles.
        /// </summary>
        public Queue<int> DrainInjectedPressEvents()
        {
            var copy = new Queue<int>();
            while (_injectedPressEvents.TryDequeue(out var item))
                copy.Enqueue(item);
            return copy;
        }

        /// <summary>
        /// Drains and returns the buttons that transitioned to pressed during the most recent
        /// Update(), from any input source (keyboard now; MIDI/gamepad once those sources exist)
        /// plus injected inputs. The internal buffer is cleared, so a second call in the same
        /// frame returns empty — callers that read it once per frame see each press exactly once.
        /// Device-agnostic: each entry's Id is a "Key.*"/"MIDI.*"/"Pad.*" string.
        /// </summary>
        public IReadOnlyList<ButtonState> ConsumePressedButtons()
        {
            var snapshot = _pressedThisFrame.ToArray();
            _pressedThisFrame.Clear();
            return snapshot;
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
        private void OnKeyBindingsChanged(object sender, EventArgs e)
        {
            // Config is the single source of truth. Runtime binding mutations no longer
            // write back to Config; edits flow Config -> runtime via the event subscription
            // in InputManagerCompat. This handler only forwards the notification.
            OnBindingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Forwards lane hit events from InputRouter
        /// </summary>
        private void OnInputRouterLaneHit(object sender, LaneHitEventArgs e)
        {
            OnLaneHit?.Invoke(this, e);
        }

        /// <summary>
        /// Records buttons that transitioned to pressed this frame, reported synchronously
        /// from InputRouter.Update().
        /// </summary>
        private void OnInputRouterButtonPressed(object sender, ButtonState button)
        {
            _pressedThisFrame.Add(button);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Injects a button state (e.g., from MCP/GameApi).
        /// Accepts input when <c>_keyBindings.GetLane(buttonId)</c> maps it to a lane or when
        /// <c>TryGetKeyCode(buttonId, out _)</c> recognizes a valid <c>Key.*</c> code.
        /// </summary>
        /// <param name="buttonId">Button identifier (e.g., "Key.A")</param>
        /// <param name="isPressed">Whether the button is pressed</param>
        /// <param name="velocity">Optional velocity/intensity that is clamped to [0, 1]</param>
        /// <returns>
        /// <see langword="true"/> when <see cref="InjectButton"/> accepts the input via either
        /// <c>_keyBindings.GetLane(buttonId)</c> or <c>TryGetKeyCode(buttonId, out _)</c> and enqueues it;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public bool InjectButton(string buttonId, bool isPressed, float velocity = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(buttonId))
                return false;

            bool hasLaneMapping = _keyBindings.GetLane(buttonId) >= 0;
            bool isKeyCode = TryGetKeyCode(buttonId, out _);
            if (!hasLaneMapping && !isKeyCode)
                return false;

            velocity = Math.Clamp(velocity, 0.0f, 1.0f);

            var state = new ButtonState(buttonId, isPressed, velocity);
            _injectedButtonQueue.Enqueue(state);
            return true;
        }

        public bool InjectMidiNote(int noteNumber, int velocity, bool isPressed)
        {
            if (_disposed)
                return false;

            return _midiNoteInjector?.TryInjectNote(noteNumber, velocity, isPressed) == true;
        }

        /// <summary>
        /// Clears all pending injected state: the button queue, injected key states, press events,
        /// and any pending/accepted injected MIDI notes. Call this when switching stages to prevent
        /// stale injected inputs from leaking into the next stage.
        /// </summary>
        public void ClearInjectedState()
        {
            while (_injectedButtonQueue.TryDequeue(out _)) { }
            _injectedKeyStates.Clear();
            while (_injectedPressEvents.TryDequeue(out _)) { }
            // Injected MIDI notes flow through MidiInputSource's own pending/accepted-press state,
            // which is separate from the button queue above. Reset it here so all injected input
            // types are cleared from the same centralized cleanup path.
            _midiInputSource?.ClearInjectedNotes();
        }

        /// <summary>
        /// Gets the keyboard input source
        /// </summary>
        /// <returns>Keyboard input source or null if not found</returns>
        private KeyboardInputSource GetKeyboardSource()
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

            if (_midiInputSource != null)
            {
                var deviceNames = _midiInputSource.DeviceNames;
                info += $"  MIDI Devices: {deviceNames.Count}\n";

                foreach (var deviceName in deviceNames)
                {
                    info += $"    - {deviceName}\n";
                }
            }

            return info;
        }

        /// <summary>
        /// Processes any externally injected button events, updating legacy key state and raising lane hits on the main thread.
        /// </summary>
        private void ProcessInjectedInputs()
        {
            while (_injectedPressEvents.TryDequeue(out _)) { }

            while (_injectedButtonQueue.TryDequeue(out var injected))
            {
                if (injected.IsPressed)
                    _pressedThisFrame.Add(injected);

                // Map to lane with current bindings
                var lane = _keyBindings.GetLane(injected.Id);
                if (lane >= 0)
                {
                    OnLaneHit?.Invoke(this, new LaneHitEventArgs(lane, injected));
                }

                // Update legacy key state for Key.* inputs
                if (TryGetKeyCode(injected.Id, out var keyCode))
                {
                    if (injected.IsPressed)
                    {
                        _injectedKeyStates[keyCode] = true;
                        // Record every press event so InputManagerCompat can fire one command per inject,
                        // regardless of how many frames the key stays held.
                        _injectedPressEvents.Enqueue(keyCode);
                    }
                    else
                    {
                        _injectedKeyStates.Remove(keyCode);
                    }
                }
            }

            // Overlay injected key states so legacy queries see them
            foreach (var kvp in _injectedKeyStates)
            {
                _keyStates[kvp.Key] = true;
            }
        }

        /// <summary>
        /// Attempts to parse a buttonId of form "Key.X" into a key code.
        /// </summary>
        private static bool TryGetKeyCode(string buttonId, out int keyCode)
        {
            const string prefix = "Key.";
            if (buttonId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var keyName = buttonId.Substring(prefix.Length);
                if (Enum.TryParse<Keys>(keyName, true, out var key))
                {
                    keyCode = (int)key;
                    return true;
                }
            }

            keyCode = default;
            return false;
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
                    _inputRouter.OnButtonPressed -= OnInputRouterButtonPressed;

                    // InputRouter owns disposal of registered input sources.
                    _inputRouter?.Dispose();
                    _inputSources.Clear();

                    // Clear collections
                    _keyStates?.Clear();
                    _previousKeyStates?.Clear();
                    _midiInputSource = null;
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
