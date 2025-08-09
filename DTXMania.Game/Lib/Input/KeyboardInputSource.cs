using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Keyboard input source that emits ButtonState IDs "Key.<Enum>"
    /// Provides ≤1ms latency keyboard input detection
    /// </summary>
    public class KeyboardInputSource : IInputSource
    {
        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;
        private readonly Dictionary<Keys, bool> _pressedKeys;
        private bool _disposed = false;

        public string Name => "Keyboard";
        public bool IsAvailable => true; // Keyboard is always available

        public KeyboardInputSource()
        {
            _pressedKeys = new Dictionary<Keys, bool>();
        }

        public void Initialize()
        {
            // Get initial keyboard state
            _currentKeyboardState = Keyboard.GetState();
            _previousKeyboardState = _currentKeyboardState;
        }

        public IEnumerable<ButtonState> Update()
        {
            if (_disposed)
                yield break;

            // Update keyboard states
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

            // Check all keys for state changes
            var pressedKeys = _currentKeyboardState.GetPressedKeys();
            var previousPressedKeys = _previousKeyboardState.GetPressedKeys();

            // Create sets for efficient comparison
            var currentKeySet = new HashSet<Keys>(pressedKeys);
            var previousKeySet = new HashSet<Keys>(previousPressedKeys);

            // Find newly pressed keys (rising edge detection)
            foreach (var key in currentKeySet)
            {
                if (!previousKeySet.Contains(key))
                {
                    // Key was just pressed
                    var buttonId = KeyBindings.CreateKeyButtonId(key);
                    _pressedKeys[key] = true;
                    
                    // DEBUG: Log key press detection
                    System.Diagnostics.Debug.WriteLine($"[KeyboardInputSource] Key pressed: {key} -> ButtonId {buttonId}");
                    
                    yield return new ButtonState(buttonId, true, 1.0f);
                }
            }

            // Find released keys (falling edge detection)
            foreach (var key in previousKeySet)
            {
                if (!currentKeySet.Contains(key))
                {
                    // Key was just released
                    var buttonId = KeyBindings.CreateKeyButtonId(key);
                    _pressedKeys[key] = false;
                    yield return new ButtonState(buttonId, false, 0.0f);
                }
            }
        }

        public IEnumerable<ButtonState> GetPressedButtons()
        {
            if (_disposed)
                yield break;

            var pressedKeys = _currentKeyboardState.GetPressedKeys();
            foreach (var key in pressedKeys)
            {
                var buttonId = KeyBindings.CreateKeyButtonId(key);
                yield return new ButtonState(buttonId, true, 1.0f);
            }
        }

        /// <summary>
        /// Checks if a specific key is currently pressed
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if key is pressed</returns>
        public bool IsKeyPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key);
        }

        /// <summary>
        /// Checks if a specific key was just pressed (rising edge)
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if key was just pressed</returns>
        public bool WasKeyJustPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        /// <summary>
        /// Checks if a specific key was just released (falling edge)
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if key was just released</returns>
        public bool WasKeyJustReleased(Keys key)
        {
            return !_currentKeyboardState.IsKeyDown(key) && _previousKeyboardState.IsKeyDown(key);
        }

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
                    _pressedKeys?.Clear();
                }
                _disposed = true;
            }
        }
    }
}
