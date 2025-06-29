using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace DTX.Input
{
    /// <summary>
    /// Input command types for game navigation
    /// </summary>
    public enum InputCommandType
    {
        MoveUp,
        MoveDown,
        MoveLeft,
        MoveRight,
        Activate,
        Back
    }

    /// <summary>
    /// Input command with timestamp for processing
    /// </summary>
    public struct InputCommand
    {
        public InputCommandType Type { get; set; }
        public double Timestamp { get; set; }
        public bool IsRepeat { get; set; }

        public InputCommand(InputCommandType type, double timestamp, bool isRepeat = false)
        {
            Type = type;
            Timestamp = timestamp;
            IsRepeat = isRepeat;
        }
    }

    /// <summary>
    /// Tracks the repeat state of a key for continuous input
    /// </summary>
    public class KeyRepeatState
    {
        public bool IsPressed { get; set; }
        public double InitialPressTime { get; set; }
        public double LastRepeatTime { get; set; }
        public double CurrentRepeatInterval { get; set; }
        public bool HasStartedRepeating { get; set; }

        public KeyRepeatState()
        {
            Reset();
        }

        public void Reset()
        {
            IsPressed = false;
            InitialPressTime = 0;
            LastRepeatTime = 0;
            CurrentRepeatInterval = 0;
            HasStartedRepeating = false;
        }
    }

    public class InputManager
    {
        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;
        
        // Input command mapping and processing
        private readonly Dictionary<Keys, InputCommandType> _keyMapping;
        private readonly Dictionary<Keys, KeyRepeatState> _keyRepeatStates;
        private readonly Queue<InputCommand> _inputCommandQueue;
        private double _currentTime;
        
        // Key repeat configuration
        private const double INITIAL_REPEAT_DELAY = 0.2; // 200ms
        private const double FINAL_REPEAT_DELAY = 0.05;  // 50ms
        private const double ACCELERATION_TIME = 1.0;    // 1000ms

        public InputManager()
        {
            _keyMapping = new Dictionary<Keys, InputCommandType>();
            _keyRepeatStates = new Dictionary<Keys, KeyRepeatState>();
            _inputCommandQueue = new Queue<InputCommand>();
            
            // Initialize default keyboard mapping
            InitializeDefaultKeyMapping();
        }

        /// <summary>
        /// Initialize default keyboard to InputCommandType mapping
        /// </summary>
        private void InitializeDefaultKeyMapping()
        {
            _keyMapping[Keys.Up] = InputCommandType.MoveUp;
            _keyMapping[Keys.Down] = InputCommandType.MoveDown;
            _keyMapping[Keys.Left] = InputCommandType.MoveLeft;
            _keyMapping[Keys.Right] = InputCommandType.MoveRight;
            _keyMapping[Keys.Enter] = InputCommandType.Activate;
            _keyMapping[Keys.Escape] = InputCommandType.Back;
        }

        public void Update(double deltaTime = 0)
        {
            _currentTime += deltaTime;
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();
            
            // Update key repeat states and generate input commands
            UpdateKeyRepeatStates();
        }

        /// <summary>
        /// Check if a specific InputCommandType was just triggered
        /// </summary>
        public bool IsCommandPressed(InputCommandType command)
        {
            // Check if any key mapped to this command was just pressed
            foreach (var kvp in _keyMapping)
            {
                if (kvp.Value == command)
                {
                    var key = kvp.Key;
                    if (_currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Check if a specific InputCommandType is currently held down
        /// </summary>
        public bool IsCommandDown(InputCommandType command)
        {
            foreach (var kvp in _keyMapping)
            {
                if (kvp.Value == command && _currentKeyboardState.IsKeyDown(kvp.Key))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get all queued input commands and clear the queue
        /// </summary>
        public Queue<InputCommand> GetInputCommands()
        {
            var commands = new Queue<InputCommand>(_inputCommandQueue);
            _inputCommandQueue.Clear();
            return commands;
        }

        /// <summary>
        /// Check if there are any pending input commands
        /// </summary>
        public bool HasPendingCommands => _inputCommandQueue.Count > 0;

        // Legacy methods for backward compatibility
        public bool IsKeyPressed(int keyCode)
        {
            var key = (Keys)keyCode;
            return _currentKeyboardState.IsKeyDown(key) &&
                   !_previousKeyboardState.IsKeyDown(key);
        }

        public bool IsKeyDown(int keyCode)
        {
            return _currentKeyboardState.IsKeyDown((Keys)keyCode);
        }

        public bool IsKeyReleased(int keyCode)
        {
            var key = (Keys)keyCode;
            return !_currentKeyboardState.IsKeyDown(key) &&
                   _previousKeyboardState.IsKeyDown(key);
        }

        /// <summary>
        /// Update key repeat states for continuous input detection
        /// </summary>
        private void UpdateKeyRepeatStates()
        {
            foreach (var kvp in _keyMapping)
            {
                var key = kvp.Key;
                var commandType = kvp.Value;
                
                if (!_keyRepeatStates.ContainsKey(key))
                {
                    _keyRepeatStates[key] = new KeyRepeatState();
                }

                var state = _keyRepeatStates[key];
                bool isCurrentlyPressed = _currentKeyboardState.IsKeyDown(key);
                bool wasPressed = _previousKeyboardState.IsKeyDown(key);

                if (isCurrentlyPressed && !wasPressed)
                {
                    // Key just pressed - initial press
                    state.IsPressed = true;
                    state.InitialPressTime = _currentTime;
                    state.LastRepeatTime = _currentTime;
                    state.CurrentRepeatInterval = INITIAL_REPEAT_DELAY;
                    state.HasStartedRepeating = false;

                    // Queue initial command
                    _inputCommandQueue.Enqueue(new InputCommand(commandType, _currentTime, false));
                }
                else if (isCurrentlyPressed && wasPressed)
                {
                    // Key held down - check for repeat
                    double timeSinceInitialPress = _currentTime - state.InitialPressTime;
                    double timeSinceLastRepeat = _currentTime - state.LastRepeatTime;

                    if (timeSinceLastRepeat >= state.CurrentRepeatInterval)
                    {
                        // Time for a repeat
                        state.LastRepeatTime = _currentTime;
                        state.HasStartedRepeating = true;

                        // Calculate accelerated repeat interval
                        double accelerationProgress = Math.Min(1.0, timeSinceInitialPress / ACCELERATION_TIME);
                        state.CurrentRepeatInterval = MathHelper.Lerp(
                            (float)INITIAL_REPEAT_DELAY,
                            (float)FINAL_REPEAT_DELAY,
                            (float)accelerationProgress);

                        // Queue repeat command
                        _inputCommandQueue.Enqueue(new InputCommand(commandType, _currentTime, true));
                    }
                }
                else if (!isCurrentlyPressed && wasPressed)
                {
                    // Key released
                    state.Reset();
                }
            }
        }
    }
}