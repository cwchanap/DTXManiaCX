using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;

namespace DTX.UI
{
    /// <summary>
    /// Manages input state tracking for UI system
    /// Tracks current and previous states to enable proper event detection
    /// </summary>
    public class InputStateManager : IInputState
    {
        #region Private Fields

        private KeyboardState _currentKeyboardState;
        private KeyboardState _previousKeyboardState;
        private MouseState _currentMouseState;
        private MouseState _previousMouseState;
        private readonly Dictionary<PlayerIndex, GamePadState> _currentGamepadStates;
        private readonly Dictionary<PlayerIndex, GamePadState> _previousGamepadStates;

        #endregion

        #region Constructor

        public InputStateManager()
        {
            _currentGamepadStates = new Dictionary<PlayerIndex, GamePadState>();
            _previousGamepadStates = new Dictionary<PlayerIndex, GamePadState>();

            // Initialize gamepad states for all players
            foreach (PlayerIndex playerIndex in System.Enum.GetValues<PlayerIndex>())
            {
                _currentGamepadStates[playerIndex] = GamePad.GetState(playerIndex);
                _previousGamepadStates[playerIndex] = GamePad.GetState(playerIndex);
            }
        }

        #endregion

        #region Public Properties

        public KeyboardState CurrentKeyboardState => _currentKeyboardState;
        public KeyboardState PreviousKeyboardState => _previousKeyboardState;
        public MouseState CurrentMouseState => _currentMouseState;
        public MouseState PreviousMouseState => _previousMouseState;

        public Vector2 MousePosition => new Vector2(_currentMouseState.X, _currentMouseState.Y);
        public Vector2 MouseDelta => MousePosition - new Vector2(_previousMouseState.X, _previousMouseState.Y);
        public int ScrollWheelDelta => _currentMouseState.ScrollWheelValue - _previousMouseState.ScrollWheelValue;

        #endregion

        #region Public Methods

        /// <summary>
        /// Update input states - call this once per frame before processing input
        /// </summary>
        public void Update()
        {
            // Update keyboard state
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

            // Update mouse state
            _previousMouseState = _currentMouseState;
            _currentMouseState = Mouse.GetState();

            // Update gamepad states
            foreach (PlayerIndex playerIndex in System.Enum.GetValues<PlayerIndex>())
            {
                _previousGamepadStates[playerIndex] = _currentGamepadStates[playerIndex];
                _currentGamepadStates[playerIndex] = GamePad.GetState(playerIndex);
            }
        }

        #endregion

        #region Keyboard Methods

        public bool IsKeyDown(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key);
        }

        public bool IsKeyPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        public bool IsKeyReleased(Keys key)
        {
            return !_currentKeyboardState.IsKeyDown(key) && _previousKeyboardState.IsKeyDown(key);
        }

        public IEnumerable<Keys> GetPressedKeys()
        {
            var currentKeys = _currentKeyboardState.GetPressedKeys();
            var previousKeys = _previousKeyboardState.GetPressedKeys();
            return currentKeys.Except(previousKeys);
        }

        #endregion

        #region Mouse Methods

        public bool IsMouseButtonDown(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => _currentMouseState.LeftButton == ButtonState.Pressed,
                MouseButton.Right => _currentMouseState.RightButton == ButtonState.Pressed,
                MouseButton.Middle => _currentMouseState.MiddleButton == ButtonState.Pressed,
                _ => false
            };
        }

        public bool IsMouseButtonPressed(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => _currentMouseState.LeftButton == ButtonState.Pressed && 
                                   _previousMouseState.LeftButton == ButtonState.Released,
                MouseButton.Right => _currentMouseState.RightButton == ButtonState.Pressed && 
                                    _previousMouseState.RightButton == ButtonState.Released,
                MouseButton.Middle => _currentMouseState.MiddleButton == ButtonState.Pressed && 
                                     _previousMouseState.MiddleButton == ButtonState.Released,
                _ => false
            };
        }

        public bool IsMouseButtonReleased(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => _currentMouseState.LeftButton == ButtonState.Released && 
                                   _previousMouseState.LeftButton == ButtonState.Pressed,
                MouseButton.Right => _currentMouseState.RightButton == ButtonState.Released && 
                                    _previousMouseState.RightButton == ButtonState.Pressed,
                MouseButton.Middle => _currentMouseState.MiddleButton == ButtonState.Released && 
                                     _previousMouseState.MiddleButton == ButtonState.Pressed,
                _ => false
            };
        }

        #endregion

        #region Gamepad Methods

        public bool IsGamepadButtonDown(PlayerIndex playerIndex, Buttons button)
        {
            return _currentGamepadStates.TryGetValue(playerIndex, out var state) && 
                   state.IsButtonDown(button);
        }

        public bool IsGamepadButtonPressed(PlayerIndex playerIndex, Buttons button)
        {
            return _currentGamepadStates.TryGetValue(playerIndex, out var currentState) &&
                   _previousGamepadStates.TryGetValue(playerIndex, out var previousState) &&
                   currentState.IsButtonDown(button) && !previousState.IsButtonDown(button);
        }

        #endregion
    }
}
