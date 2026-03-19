using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input;
using Moq;
using Xunit;

namespace DTXMania.Test.Input
{
    /// <summary>
    /// Tests for InputRouter - routes input events to lane hits
    /// </summary>
    [Trait("Category", "Input")]
    public class InputRouterTests : IDisposable
    {
        private readonly KeyBindings _keyBindings;
        private readonly InputRouter _router;

        public InputRouterTests()
        {
            _keyBindings = new KeyBindings();
            _router = new InputRouter(_keyBindings);
        }

        public void Dispose()
        {
            _router.Dispose();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeWithZeroSources()
        {
            using var router = new InputRouter(_keyBindings);
            Assert.Equal(0, router.GetSourceCount());
        }

        #endregion

        #region AddInputSource Tests

        [Fact]
        public void AddInputSource_ShouldIncreaseSourceCount()
        {
            var mockSource = new Mock<IInputSource>();
            mockSource.Setup(s => s.Update()).Returns(Enumerable.Empty<ButtonState>());

            _router.AddInputSource(mockSource.Object);

            Assert.Equal(1, _router.GetSourceCount());
        }

        [Fact]
        public void AddInputSource_MultipleSources_ShouldTrackAll()
        {
            var mock1 = new Mock<IInputSource>();
            var mock2 = new Mock<IInputSource>();
            mock1.Setup(s => s.Update()).Returns(Enumerable.Empty<ButtonState>());
            mock2.Setup(s => s.Update()).Returns(Enumerable.Empty<ButtonState>());

            _router.AddInputSource(mock1.Object);
            _router.AddInputSource(mock2.Object);

            Assert.Equal(2, _router.GetSourceCount());
        }

        [Fact]
        public void AddInputSource_AfterDispose_ShouldThrow()
        {
            _router.Dispose();
            var mockSource = new Mock<IInputSource>();

            Assert.Throws<ObjectDisposedException>(() => _router.AddInputSource(mockSource.Object));
        }

        [Fact]
        public void AddInputSource_Null_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _router.AddInputSource(null!));
        }

        #endregion

        #region Initialize Tests

        [Fact]
        public void Initialize_ShouldCallInitializeOnAllSources()
        {
            var mock1 = new Mock<IInputSource>();
            var mock2 = new Mock<IInputSource>();
            mock1.Setup(s => s.Update()).Returns(Enumerable.Empty<ButtonState>());
            mock2.Setup(s => s.Update()).Returns(Enumerable.Empty<ButtonState>());

            _router.AddInputSource(mock1.Object);
            _router.AddInputSource(mock2.Object);

            _router.Initialize();

            mock1.Verify(s => s.Initialize(), Times.Once);
            mock2.Verify(s => s.Initialize(), Times.Once);
        }

        [Fact]
        public void Initialize_WithNoSources_ShouldNotThrow()
        {
            // Should not throw when there are no sources
            _router.Initialize();
        }

        [Fact]
        public void Initialize_AfterDispose_ShouldThrow()
        {
            _router.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _router.Initialize());
        }

        #endregion

        #region Update / LaneHit Event Tests

        [Fact]
        public void Update_WithNoSources_ShouldNotThrow()
        {
            // Should not throw when there are no sources
            _router.Update();
        }

        [Fact]
        public void Update_WithPressedBoundButton_ShouldRaiseLaneHitEvent()
        {
            // Arrange
            _keyBindings.ClearAllBindings();
            _keyBindings.BindButton("Key.S", 4); // Snare = lane 4

            var mockSource = new Mock<IInputSource>();
            var pressedState = new ButtonState("Key.S", isPressed: true, velocity: 1.0f);
            mockSource.Setup(s => s.Update()).Returns(new[] { pressedState });

            _router.AddInputSource(mockSource.Object);

            LaneHitEventArgs? capturedArgs = null;
            _router.OnLaneHit += (sender, args) => capturedArgs = args;

            // Act
            _router.Update();

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(4, capturedArgs!.Lane);
            Assert.Equal("Key.S", capturedArgs.Button.Id);
        }

        [Fact]
        public void Update_WithReleasedButton_ShouldNotRaiseLaneHitEvent()
        {
            // Arrange
            _keyBindings.ClearAllBindings();
            _keyBindings.BindButton("Key.S", 4);

            var mockSource = new Mock<IInputSource>();
            var releasedState = new ButtonState("Key.S", isPressed: false);
            mockSource.Setup(s => s.Update()).Returns(new[] { releasedState });

            _router.AddInputSource(mockSource.Object);

            bool eventRaised = false;
            _router.OnLaneHit += (_, _) => eventRaised = true;

            // Act
            _router.Update();

            // Assert
            Assert.False(eventRaised);
        }

        [Fact]
        public void Update_WithUnboundButton_ShouldNotRaiseLaneHitEvent()
        {
            // Arrange
            _keyBindings.ClearAllBindings();
            // Don't bind "Key.Z"

            var mockSource = new Mock<IInputSource>();
            var pressedState = new ButtonState("Key.Z", isPressed: true);
            mockSource.Setup(s => s.Update()).Returns(new[] { pressedState });

            _router.AddInputSource(mockSource.Object);

            bool eventRaised = false;
            _router.OnLaneHit += (_, _) => eventRaised = true;

            // Act
            _router.Update();

            // Assert
            Assert.False(eventRaised);
        }

        [Fact]
        public void Update_AfterDispose_ShouldThrow()
        {
            _router.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _router.Update());
        }

        [Fact]
        public void Update_MultipleSources_ShouldProcessAllSources()
        {
            // Arrange
            _keyBindings.ClearAllBindings();
            _keyBindings.BindButton("Key.A", 0);
            _keyBindings.BindButton("Key.S", 4);

            var mock1 = new Mock<IInputSource>();
            mock1.Setup(s => s.Update()).Returns(new[] { new ButtonState("Key.A", true) });

            var mock2 = new Mock<IInputSource>();
            mock2.Setup(s => s.Update()).Returns(new[] { new ButtonState("Key.S", true) });

            _router.AddInputSource(mock1.Object);
            _router.AddInputSource(mock2.Object);

            var lanes = new List<int>();
            _router.OnLaneHit += (_, args) => lanes.Add(args.Lane);

            // Act
            _router.Update();

            // Assert
            Assert.Equal(2, lanes.Count);
            Assert.Contains(0, lanes);
            Assert.Contains(4, lanes);
        }

        #endregion

        #region GetSourceCount Tests

        [Fact]
        public void GetSourceCount_AfterDispose_ShouldThrow()
        {
            _router.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _router.GetSourceCount());
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldDisposeIDisposableSources()
        {
            var mockSource = new Mock<IInputSource>();
            mockSource.Setup(s => s.Update()).Returns(Enumerable.Empty<ButtonState>());

            using var router = new InputRouter(_keyBindings);
            router.AddInputSource(mockSource.Object);

            router.Dispose();

            mockSource.Verify(s => s.Dispose(), Times.Once);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var router = new InputRouter(_keyBindings);
            router.Dispose();
            router.Dispose(); // Should not throw
        }

        [Fact]
        public void Dispose_ShouldClearEventHandlers()
        {
            // Arrange: use a fresh router so we can verify the event handler count
            // after dispose without triggering ObjectDisposedException on the shared _router
            var keyBindings = new KeyBindings();
            keyBindings.ClearAllBindings();
            keyBindings.BindButton("Key.A", 0);

            var router = new InputRouter(keyBindings);
            bool eventRaised = false;
            router.OnLaneHit += (_, _) => eventRaised = true;

            // Act
            router.Dispose();

            // Assert: after dispose, OnLaneHit must be null so the handler cannot fire.
            // Events can only be used with += / -= from outside the declaring type (CS0070),
            // so use reflection to inspect the backing field directly.
            var eventField = typeof(InputRouter).GetField(
                "OnLaneHit",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(eventField);
            var handlerValue = eventField.GetValue(router);
            Assert.Null(handlerValue);
            Assert.False(eventRaised);
        }

        #endregion
    }

    /// <summary>
    /// Tests for ButtonState class
    /// </summary>
    [Trait("Category", "Input")]
    public class ButtonStateTests
    {
        [Fact]
        public void Constructor_ShouldSetAllProperties()
        {
            var before = DateTime.UtcNow;
            var state = new ButtonState("Key.A", isPressed: true, velocity: 0.8f);
            var after = DateTime.UtcNow;

            Assert.Equal("Key.A", state.Id);
            Assert.True(state.IsPressed);
            Assert.Equal(0.8f, state.Velocity);
            Assert.InRange(state.Timestamp, before, after);
        }

        [Fact]
        public void Constructor_DefaultVelocity_ShouldBe1()
        {
            var state = new ButtonState("Key.A", isPressed: true);
            Assert.Equal(1.0f, state.Velocity);
        }

        [Fact]
        public void ToString_PressedState_ShouldContainPRESSED()
        {
            var state = new ButtonState("Key.A", isPressed: true);
            Assert.Contains("PRESSED", state.ToString());
            Assert.Contains("Key.A", state.ToString());
        }

        [Fact]
        public void ToString_ReleasedState_ShouldContainRELEASED()
        {
            var state = new ButtonState("Key.A", isPressed: false);
            Assert.Contains("RELEASED", state.ToString());
        }
    }

    /// <summary>
    /// Tests for LaneHitEventArgs class
    /// </summary>
    [Trait("Category", "Input")]
    public class LaneHitEventArgsTests
    {
        [Fact]
        public void Constructor_ShouldSetLaneAndButton()
        {
            var button = new ButtonState("Key.S", true);
            var before = DateTime.UtcNow;
            var args = new LaneHitEventArgs(lane: 4, button: button);
            var after = DateTime.UtcNow;

            Assert.Equal(4, args.Lane);
            Assert.Equal(button, args.Button);
            Assert.InRange(args.Timestamp, before, after);
        }

        [Fact]
        public void Constructor_ShouldCaptureTimestamp()
        {
            var before = DateTime.UtcNow - TimeSpan.FromSeconds(1);
            var button = new ButtonState("Key.A", true);
            var args = new LaneHitEventArgs(0, button);
            var after = DateTime.UtcNow;

            // Timestamp must be within the last second — rules out default/far-past values
            Assert.InRange(args.Timestamp, before, after);
        }
    }
}
