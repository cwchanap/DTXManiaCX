using System;
using System.Linq;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    /// <summary>
    /// Unit tests for KeyboardInputSource.
    /// Tests cover properties, default-state query methods, and disposal.
    /// Methods that call Keyboard.GetState() (Initialize, Update) are intentionally
    /// excluded because they require an SDL/game-window context that is unavailable
    /// in a headless test environment.
    /// </summary>
    [Trait("Category", "Input")]
    public class KeyboardInputSourceTests : IDisposable
    {
        private readonly KeyboardInputSource _source;

        public KeyboardInputSourceTests()
        {
            _source = new KeyboardInputSource();
        }

        public void Dispose()
        {
            _source?.Dispose();
        }

        #region Property Tests

        [Fact]
        public void Name_ShouldReturnKeyboard()
        {
            Assert.Equal("Keyboard", _source.Name);
        }

        [Fact]
        public void IsAvailable_ShouldAlwaysBeTrue()
        {
            Assert.True(_source.IsAvailable);
        }

        #endregion

        #region Default State Query Tests

        // All query methods below operate on the KeyboardState struct stored in the object.
        // Because Initialize() has not been called, the struct is at its default value
        // (no keys pressed), so every query returns false / empty without touching SDL.

        [Fact]
        public void IsKeyPressed_BeforeInitialize_ShouldReturnFalse()
        {
            Assert.False(_source.IsKeyPressed(Keys.A));
        }

        [Fact]
        public void IsKeyPressed_SpaceKey_BeforeInitialize_ShouldReturnFalse()
        {
            Assert.False(_source.IsKeyPressed(Keys.Space));
        }

        [Theory]
        [InlineData(Keys.A)]
        [InlineData(Keys.Z)]
        [InlineData(Keys.Space)]
        [InlineData(Keys.Enter)]
        [InlineData(Keys.Escape)]
        [InlineData(Keys.F1)]
        [InlineData(Keys.Up)]
        [InlineData(Keys.Down)]
        [InlineData(Keys.Left)]
        [InlineData(Keys.Right)]
        public void IsKeyPressed_AnyKey_BeforeInitialize_ShouldReturnFalse(Keys key)
        {
            Assert.False(_source.IsKeyPressed(key));
        }

        [Theory]
        [InlineData(Keys.A)]
        [InlineData(Keys.Space)]
        [InlineData(Keys.Enter)]
        public void WasKeyJustPressed_BeforeInitialize_ShouldReturnFalse(Keys key)
        {
            // Both current and previous states are default (no keys pressed),
            // so WasKeyJustPressed must return false.
            Assert.False(_source.WasKeyJustPressed(key));
        }

        [Theory]
        [InlineData(Keys.A)]
        [InlineData(Keys.Space)]
        [InlineData(Keys.Enter)]
        public void WasKeyJustReleased_BeforeInitialize_ShouldReturnFalse(Keys key)
        {
            // Neither current nor previous state has any key pressed,
            // so WasKeyJustReleased must return false.
            Assert.False(_source.WasKeyJustReleased(key));
        }

        [Fact]
        public void GetPressedButtons_BeforeInitialize_ShouldReturnEmpty()
        {
            var buttons = _source.GetPressedButtons().ToList();
            Assert.Empty(buttons);
        }

        #endregion

        #region Disposal Tests

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            using var source = new KeyboardInputSource();
            // No exception expected
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var source = new KeyboardInputSource();
            source.Dispose();
            source.Dispose(); // Second call must be a no-op
        }

        [Fact]
        public void IsAvailable_AfterDispose_ShouldStillBeTrue()
        {
            // IsAvailable is a constant property unaffected by disposal state.
            var source = new KeyboardInputSource();
            source.Dispose();
            Assert.True(source.IsAvailable);
        }

        [Fact]
        public void Name_AfterDispose_ShouldStillBeKeyboard()
        {
            var source = new KeyboardInputSource();
            source.Dispose();
            Assert.Equal("Keyboard", source.Name);
        }

        [Fact]
        public void Update_AfterDispose_ShouldYieldNoResults()
        {
            var source = new KeyboardInputSource();
            source.Dispose();

            // After disposal the generator checks _disposed and yields break immediately,
            // without calling Keyboard.GetState() — so this is safe in a headless env.
            var results = source.Update().ToList();
            Assert.Empty(results);
        }

        [Fact]
        public void GetPressedButtons_AfterDispose_ShouldReturnEmpty()
        {
            var source = new KeyboardInputSource();
            source.Dispose();

            var buttons = source.GetPressedButtons().ToList();
            Assert.Empty(buttons);
        }

        #endregion
    }
}
