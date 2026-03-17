using System;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    /// <summary>
    /// Tests for KeyBindings class
    /// </summary>
    public class KeyBindingsTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldLoadDefaultBindings()
        {
            var bindings = new KeyBindings();
            Assert.True(bindings.ButtonToLane.Count > 0);
        }

        [Fact]
        public void Constructor_DefaultBindings_ShouldHave10Lanes()
        {
            var bindings = new KeyBindings();
            var lanes = bindings.ButtonToLane.Values.Distinct().OrderBy(x => x).ToList();
            Assert.Equal(10, lanes.Count);
        }

        #endregion

        #region GetLane Tests

        [Fact]
        public void GetLane_ExistingBinding_ShouldReturnLaneIndex()
        {
            var bindings = new KeyBindings();
            var lane = bindings.GetLane("Key.A");
            Assert.InRange(lane, 0, 9);
        }

        [Fact]
        public void GetLane_NonExistentBinding_ShouldReturnNegativeOne()
        {
            var bindings = new KeyBindings();
            var lane = bindings.GetLane("Key.NonExistent");
            Assert.Equal(-1, lane);
        }

        #endregion

        #region GetButtonsForLane Tests

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void GetButtonsForLane_ValidLane_ShouldReturnButtons(int lane)
        {
            var bindings = new KeyBindings();
            var buttons = bindings.GetButtonsForLane(lane).ToList();
            Assert.True(buttons.Count > 0);
        }

        [Fact]
        public void GetButtonsForLane_UnboundLane_ShouldReturnEmpty()
        {
            var bindings = new KeyBindings();
            bindings.ClearAllBindings();
            var buttons = bindings.GetButtonsForLane(5).ToList();
            Assert.Empty(buttons);
        }

        #endregion

        #region BindButton Tests

        [Fact]
        public void BindButton_ValidBinding_ShouldAddMapping()
        {
            var bindings = new KeyBindings();
            bindings.BindButton("Key.Q", 3);
            Assert.Equal(3, bindings.GetLane("Key.Q"));
        }

        [Fact]
        public void BindButton_RaisesBindingsChangedEvent()
        {
            var bindings = new KeyBindings();
            bool eventRaised = false;
            bindings.BindingsChanged += (s, e) => eventRaised = true;

            bindings.BindButton("Key.Q", 0);

            Assert.True(eventRaised);
        }

        [Fact]
        public void BindButton_NegativeLane_ShouldThrow()
        {
            var bindings = new KeyBindings();
            Assert.Throws<ArgumentOutOfRangeException>(() => bindings.BindButton("Key.Q", -1));
        }

        [Fact]
        public void BindButton_LaneTooLarge_ShouldThrow()
        {
            var bindings = new KeyBindings();
            Assert.Throws<ArgumentOutOfRangeException>(() => bindings.BindButton("Key.Q", 10));
        }

        #endregion

        #region UnbindButton Tests

        [Fact]
        public void UnbindButton_ExistingBinding_ShouldRemoveMapping()
        {
            var bindings = new KeyBindings();
            bindings.BindButton("Key.Q", 3);
            bindings.UnbindButton("Key.Q");
            Assert.Equal(-1, bindings.GetLane("Key.Q"));
        }

        [Fact]
        public void UnbindButton_ExistingBinding_RaisesBindingsChangedEvent()
        {
            var bindings = new KeyBindings();
            bindings.BindButton("Key.Q", 3);

            bool eventRaised = false;
            bindings.BindingsChanged += (s, e) => eventRaised = true;
            bindings.UnbindButton("Key.Q");

            Assert.True(eventRaised);
        }

        [Fact]
        public void UnbindButton_NonExistentBinding_ShouldNotRaiseEvent()
        {
            var bindings = new KeyBindings();
            bool eventRaised = false;
            bindings.BindingsChanged += (s, e) => eventRaised = true;

            bindings.UnbindButton("Key.NonExistent");

            Assert.False(eventRaised);
        }

        #endregion

        #region UnbindLane Tests

        [Fact]
        public void UnbindLane_BoundLane_ShouldRemoveAllButtons()
        {
            var bindings = new KeyBindings();
            bindings.BindButton("Key.Q", 5);
            bindings.BindButton("Key.W", 5);

            bindings.UnbindLane(5);

            Assert.Empty(bindings.GetButtonsForLane(5));
        }

        [Fact]
        public void UnbindLane_BoundLane_RaisesBindingsChangedEvent()
        {
            var bindings = new KeyBindings();
            bindings.BindButton("Key.Q", 5);

            bool eventRaised = false;
            bindings.BindingsChanged += (s, e) => eventRaised = true;
            bindings.UnbindLane(5);

            Assert.True(eventRaised);
        }

        [Fact]
        public void UnbindLane_UnboundLane_ShouldNotRaiseEvent()
        {
            var bindings = new KeyBindings();
            bindings.ClearAllBindings();

            bool eventRaised = false;
            bindings.BindingsChanged += (s, e) => eventRaised = true;
            bindings.UnbindLane(5);

            Assert.False(eventRaised);
        }

        #endregion

        #region ClearAllBindings Tests

        [Fact]
        public void ClearAllBindings_ShouldRemoveAllMappings()
        {
            var bindings = new KeyBindings();
            bindings.ClearAllBindings();
            Assert.Empty(bindings.ButtonToLane);
        }

        [Fact]
        public void ClearAllBindings_RaisesBindingsChangedEvent()
        {
            var bindings = new KeyBindings();
            bool eventRaised = false;
            bindings.BindingsChanged += (s, e) => eventRaised = true;

            bindings.ClearAllBindings();

            Assert.True(eventRaised);
        }

        #endregion

        #region GetLaneDescription Tests

        [Fact]
        public void GetLaneDescription_BoundLane_ShouldReturnButtonNames()
        {
            var bindings = new KeyBindings();
            var description = bindings.GetLaneDescription(0);
            Assert.False(string.IsNullOrEmpty(description));
            Assert.NotEqual("Unbound", description);
        }

        [Fact]
        public void GetLaneDescription_UnboundLane_ShouldReturnUnbound()
        {
            var bindings = new KeyBindings();
            bindings.ClearAllBindings();
            var description = bindings.GetLaneDescription(5);
            Assert.Equal("Unbound", description);
        }

        #endregion

        #region FormatButtonId Tests

        [Fact]
        public void FormatButtonId_KeyA_ShouldReturnA()
        {
            Assert.Equal("A", KeyBindings.FormatButtonId("Key.A"));
        }

        [Fact]
        public void FormatButtonId_Space_ShouldReturnSpace()
        {
            Assert.Equal("Space", KeyBindings.FormatButtonId("Key.Space"));
        }

        [Fact]
        public void FormatButtonId_OemSemicolon_ShouldReturnSemicolon()
        {
            Assert.Equal(";", KeyBindings.FormatButtonId("Key.OemSemicolon"));
        }

        [Fact]
        public void FormatButtonId_MidiNote_ShouldReturnMIDIFormat()
        {
            Assert.Equal("MIDI 36", KeyBindings.FormatButtonId("MIDI.36"));
        }

        [Fact]
        public void FormatButtonId_PadButton_ShouldReturnPadFormat()
        {
            Assert.Equal("Pad A", KeyBindings.FormatButtonId("Pad.A"));
        }

        [Fact]
        public void FormatButtonId_Unknown_ShouldReturnAsIs()
        {
            Assert.Equal("Unknown.Button", KeyBindings.FormatButtonId("Unknown.Button"));
        }

        #endregion

        #region CreateButtonId Tests

        [Fact]
        public void CreateKeyButtonId_ShouldReturnKeyPrefix()
        {
            var id = KeyBindings.CreateKeyButtonId(Keys.A);
            Assert.Equal("Key.A", id);
        }

        [Fact]
        public void CreateMidiButtonId_ShouldReturnMidiPrefix()
        {
            var id = KeyBindings.CreateMidiButtonId(36);
            Assert.Equal("MIDI.36", id);
        }

        [Fact]
        public void CreatePadButtonId_ShouldReturnPadPrefix()
        {
            var id = KeyBindings.CreatePadButtonId("Button1");
            Assert.Equal("Pad.Button1", id);
        }

        #endregion

        #region GetLaneName Tests

        [Theory]
        [InlineData(0, "Splash/Crash")]
        [InlineData(4, "Snare Drum")]
        [InlineData(6, "Bass Drum")]
        [InlineData(9, "Ride")]
        public void GetLaneName_KnownLane_ShouldReturnCorrectName(int lane, string expectedName)
        {
            Assert.Equal(expectedName, KeyBindings.GetLaneName(lane));
        }

        [Fact]
        public void GetLaneName_UnknownLane_ShouldReturnLanePrefix()
        {
            var name = KeyBindings.GetLaneName(99);
            Assert.StartsWith("Lane", name);
        }

        #endregion

        #region LoadDefaultBindings Tests

        [Fact]
        public void LoadDefaultBindings_ShouldResetToDefaults()
        {
            var bindings = new KeyBindings();
            bindings.ClearAllBindings();
            Assert.Empty(bindings.ButtonToLane);

            bindings.LoadDefaultBindings();
            Assert.True(bindings.ButtonToLane.Count > 0);
        }

        #endregion
    }
}
