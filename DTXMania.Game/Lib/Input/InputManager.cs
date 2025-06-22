using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace DTX.Input
{
    public class InputManager
    {
        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;

        public void Update()
        {
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();
        }

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
    }
}