using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace DTX.UI
{
    /// <summary>
    /// Interface for input state management
    /// Provides access to current and previous input states for proper event detection
    /// </summary>
    public interface IInputState
    {
        #region Keyboard State

        /// <summary>
        /// Current keyboard state
        /// </summary>
        KeyboardState CurrentKeyboardState { get; }

        /// <summary>
        /// Previous keyboard state
        /// </summary>
        KeyboardState PreviousKeyboardState { get; }

        /// <summary>
        /// Check if a key is currently pressed
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if key is pressed</returns>
        bool IsKeyDown(Keys key);

        /// <summary>
        /// Check if a key was just pressed (not held)
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if key was just pressed</returns>
        bool IsKeyPressed(Keys key);

        /// <summary>
        /// Check if a key was just released
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>True if key was just released</returns>
        bool IsKeyReleased(Keys key);

        /// <summary>
        /// Get all keys that were just pressed this frame
        /// </summary>
        /// <returns>Collection of pressed keys</returns>
        IEnumerable<Keys> GetPressedKeys();

        #endregion

        #region Mouse State

        /// <summary>
        /// Current mouse state
        /// </summary>
        MouseState CurrentMouseState { get; }

        /// <summary>
        /// Previous mouse state
        /// </summary>
        MouseState PreviousMouseState { get; }

        /// <summary>
        /// Current mouse position
        /// </summary>
        Vector2 MousePosition { get; }

        /// <summary>
        /// Mouse position delta since last frame
        /// </summary>
        Vector2 MouseDelta { get; }

        /// <summary>
        /// Check if a mouse button is currently pressed
        /// </summary>
        /// <param name="button">Button to check</param>
        /// <returns>True if button is pressed</returns>
        bool IsMouseButtonDown(MouseButton button);

        /// <summary>
        /// Check if a mouse button was just pressed
        /// </summary>
        /// <param name="button">Button to check</param>
        /// <returns>True if button was just pressed</returns>
        bool IsMouseButtonPressed(MouseButton button);

        /// <summary>
        /// Check if a mouse button was just released
        /// </summary>
        /// <param name="button">Button to check</param>
        /// <returns>True if button was just released</returns>
        bool IsMouseButtonReleased(MouseButton button);

        /// <summary>
        /// Mouse scroll wheel delta
        /// </summary>
        int ScrollWheelDelta { get; }

        #endregion

        #region Gamepad State (for future expansion)

        /// <summary>
        /// Check if a gamepad button is pressed
        /// </summary>
        /// <param name="playerIndex">Player index</param>
        /// <param name="button">Button to check</param>
        /// <returns>True if button is pressed</returns>
        bool IsGamepadButtonDown(PlayerIndex playerIndex, Buttons button);

        /// <summary>
        /// Check if a gamepad button was just pressed
        /// </summary>
        /// <param name="playerIndex">Player index</param>
        /// <param name="button">Button to check</param>
        /// <returns>True if button was just pressed</returns>
        bool IsGamepadButtonPressed(PlayerIndex playerIndex, Buttons button);

        #endregion
    }
}
