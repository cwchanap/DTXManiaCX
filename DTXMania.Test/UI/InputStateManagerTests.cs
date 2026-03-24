using System.Linq;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for InputStateManager.
    ///
    /// These tests exercise the state-query methods against the default (all-zero) input
    /// state that exists immediately after construction — before any frame update has been
    /// performed.  Methods that call platform APIs (Keyboard.GetState, Mouse.GetState)
    /// are invoked only through the normal Update() path which is not called here, keeping
    /// the suite headless-safe.
    ///
    /// The constructor does call GamePad.GetState() internally; MonoGame returns a
    /// "disconnected" GamePadState when no physical device is present, so construction
    /// is safe in a CI environment.
    /// </summary>
    public class InputStateManagerTests
    {
        private readonly InputStateManager _manager;

        public InputStateManagerTests()
        {
            _manager = new InputStateManager();
        }

        #region Interface Compliance

        [Fact]
        public void InputStateManager_ShouldImplementIInputState()
        {
            Assert.True(typeof(IInputState).IsAssignableFrom(typeof(InputStateManager)));
        }

        #endregion

        #region Default Keyboard State

        [Fact]
        public void IsKeyDown_DefaultState_ShouldReturnFalse()
        {
            Assert.False(_manager.IsKeyDown(Keys.A));
        }

        [Theory]
        [InlineData(Keys.A)]
        [InlineData(Keys.Z)]
        [InlineData(Keys.Space)]
        [InlineData(Keys.Enter)]
        [InlineData(Keys.Escape)]
        [InlineData(Keys.Up)]
        [InlineData(Keys.Down)]
        public void IsKeyDown_AnyKey_DefaultState_ShouldReturnFalse(Keys key)
        {
            Assert.False(_manager.IsKeyDown(key));
        }

        [Fact]
        public void IsKeyPressed_DefaultState_ShouldReturnFalse()
        {
            // Both current and previous are default (empty), so no key was "just pressed".
            Assert.False(_manager.IsKeyPressed(Keys.A));
        }

        [Fact]
        public void IsKeyReleased_DefaultState_ShouldReturnFalse()
        {
            // Both states are empty, so no key was "just released".
            Assert.False(_manager.IsKeyReleased(Keys.A));
        }

        [Fact]
        public void GetPressedKeys_DefaultState_ShouldReturnEmpty()
        {
            var keys = _manager.GetPressedKeys().ToList();
            Assert.Empty(keys);
        }

        #endregion

        #region Default Mouse State

        [Fact]
        public void ScrollWheelDelta_DefaultState_ShouldBeZero()
        {
            Assert.Equal(0, _manager.ScrollWheelDelta);
        }

        [Theory]
        [InlineData(MouseButton.Left)]
        [InlineData(MouseButton.Right)]
        [InlineData(MouseButton.Middle)]
        public void IsMouseButtonDown_DefaultState_ShouldReturnFalse(MouseButton button)
        {
            Assert.False(_manager.IsMouseButtonDown(button));
        }

        [Theory]
        [InlineData(MouseButton.Left)]
        [InlineData(MouseButton.Right)]
        [InlineData(MouseButton.Middle)]
        public void IsMouseButtonPressed_DefaultState_ShouldReturnFalse(MouseButton button)
        {
            Assert.False(_manager.IsMouseButtonPressed(button));
        }

        [Theory]
        [InlineData(MouseButton.Left)]
        [InlineData(MouseButton.Right)]
        [InlineData(MouseButton.Middle)]
        public void IsMouseButtonReleased_DefaultState_ShouldReturnFalse(MouseButton button)
        {
            Assert.False(_manager.IsMouseButtonReleased(button));
        }

        [Fact]
        public void IsMouseButtonDown_UnknownButton_ShouldReturnFalse()
        {
            // Exercise the default branch in the switch statement.
            var unknownButton = (MouseButton)99;
            Assert.False(_manager.IsMouseButtonDown(unknownButton));
        }

        [Fact]
        public void IsMouseButtonPressed_UnknownButton_ShouldReturnFalse()
        {
            var unknownButton = (MouseButton)99;
            Assert.False(_manager.IsMouseButtonPressed(unknownButton));
        }

        [Fact]
        public void IsMouseButtonReleased_UnknownButton_ShouldReturnFalse()
        {
            var unknownButton = (MouseButton)99;
            Assert.False(_manager.IsMouseButtonReleased(unknownButton));
        }

        #endregion

        #region Default Gamepad State

        [Theory]
        [InlineData(PlayerIndex.One)]
        [InlineData(PlayerIndex.Two)]
        [InlineData(PlayerIndex.Three)]
        [InlineData(PlayerIndex.Four)]
        public void IsGamepadButtonDown_DefaultState_ShouldReturnFalse(PlayerIndex playerIndex)
        {
            Assert.False(_manager.IsGamepadButtonDown(playerIndex, Buttons.A));
        }

        [Theory]
        [InlineData(PlayerIndex.One)]
        [InlineData(PlayerIndex.Two)]
        [InlineData(PlayerIndex.Three)]
        [InlineData(PlayerIndex.Four)]
        public void IsGamepadButtonPressed_DefaultState_ShouldReturnFalse(PlayerIndex playerIndex)
        {
            Assert.False(_manager.IsGamepadButtonPressed(playerIndex, Buttons.A));
        }

        #endregion

        #region State Properties Are Exposed

        [Fact]
        public void CurrentKeyboardState_ShouldBeAccessible()
        {
            // Property must be readable without throwing.
            var state = _manager.CurrentKeyboardState;
            // Default KeyboardState has no pressed keys.
            Assert.Empty(state.GetPressedKeys());
        }

        [Fact]
        public void PreviousKeyboardState_ShouldBeAccessible()
        {
            var state = _manager.PreviousKeyboardState;
            Assert.Empty(state.GetPressedKeys());
        }

        [Fact]
        public void CurrentMouseState_ShouldBeAccessible()
        {
            _ = _manager.CurrentMouseState; // Must not throw.
        }

        [Fact]
        public void PreviousMouseState_ShouldBeAccessible()
        {
            _ = _manager.PreviousMouseState;
        }

        [Fact]
        public void MousePosition_DefaultState_ShouldBeZeroZero()
        {
            var pos = _manager.MousePosition;
            Assert.Equal(0f, pos.X);
            Assert.Equal(0f, pos.Y);
        }

        [Fact]
        public void MouseDelta_DefaultState_ShouldBeZeroZero()
        {
            var delta = _manager.MouseDelta;
            Assert.Equal(0f, delta.X);
            Assert.Equal(0f, delta.Y);
        }

        #endregion
    }
}
