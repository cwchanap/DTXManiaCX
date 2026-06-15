using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.DrumConfig;
using Microsoft.Xna.Framework.Input;
using Xunit;
using ButtonState = DTXMania.Game.Lib.Input.ButtonState;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumCapturePopupTests
    {
        private readonly KeyBindings _bindings = new();
        private readonly Dictionary<Keys, InputCommandType> _system = new()
        {
            [Keys.Enter] = InputCommandType.Activate,           // required
            [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed, // non-required
        };
        private readonly List<Keys> _evicted = new();

        private DrumCapturePopup NewPopup() =>
            new(_bindings, () => _system, key => _evicted.Add(key));

        [Fact]
        public void Open_SetsListeningStateAndLane()
        {
            var popup = NewPopup();
            popup.Open(4);
            Assert.True(popup.IsOpen);
            Assert.Equal(DrumCaptureState.Listening, popup.State);
            Assert.Equal(4, popup.Lane);
        }

        [Fact]
        public void TryCapture_UnboundKey_AppendsBindingToLane()
        {
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(new ButtonState("Key.Q", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains("Key.Q", _bindings.GetButtonsForLane(4));
            Assert.Contains("Key.S", _bindings.GetButtonsForLane(4)); // default still present (append)
        }

        [Fact]
        public void TryCapture_RequiredNavKey_RejectsWithoutBinding()
        {
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(new ButtonState("Key.Enter", true));

            Assert.Equal(DrumCaptureOutcome.Rejected, outcome);
            Assert.Equal(DrumCaptureState.ShowingConflict, popup.State);
            Assert.DoesNotContain("Key.Enter", _bindings.GetButtonsForLane(4));
        }

        [Fact]
        public void TryCapture_NonRequiredSystemKey_EvictsAndBinds()
        {
            var popup = NewPopup();
            popup.Open(7);

            var outcome = popup.TryCapture(new ButtonState("Key.PageUp", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains(Keys.PageUp, _evicted);
            Assert.Contains("Key.PageUp", _bindings.GetButtonsForLane(7));
        }

        [Fact]
        public void TryCapture_NonKeyboardButton_BindsWithoutSystemCheck()
        {
            var popup = NewPopup();
            popup.Open(6);

            var outcome = popup.TryCapture(new ButtonState("MIDI.36", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains("MIDI.36", _bindings.GetButtonsForLane(6));
            Assert.Empty(_evicted);
        }

        [Fact]
        public void RemoveBinding_And_ClearLane_MutateWorkingBindings()
        {
            var popup = NewPopup();
            popup.Open(4);

            popup.RemoveBinding("Key.S");
            Assert.DoesNotContain("Key.S", _bindings.GetButtonsForLane(4));

            popup.TryCapture(new ButtonState("Key.Q", true));
            popup.ClearLane();
            Assert.Empty(_bindings.GetButtonsForLane(4));
        }

        [Fact]
        public void Tick_AfterConflict_ReturnsToListening()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Enter", true)); // -> ShowingConflict

            popup.Tick(2.5);

            Assert.Equal(DrumCaptureState.Listening, popup.State);
            Assert.Null(popup.ConflictMessage);
        }

        [Fact]
        public void Tick_BeforeConflictExpires_StaysShowingConflict()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Enter", true)); // -> ShowingConflict (2.0s notice)

            popup.Tick(1.0); // less than the 2.0s conflict duration

            Assert.Equal(DrumCaptureState.ShowingConflict, popup.State);
            Assert.NotNull(popup.ConflictMessage);
        }

        [Fact]
        public void TryCapture_WhileShowingConflict_ReturnsIgnored()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Enter", true)); // -> ShowingConflict

            var outcome = popup.TryCapture(new ButtonState("Key.Q", true));

            Assert.Equal(DrumCaptureOutcome.Ignored, outcome);
            Assert.DoesNotContain("Key.Q", _bindings.GetButtonsForLane(4));
        }

        [Fact]
        public void Close_ResetsToClosedState()
        {
            var popup = NewPopup();
            popup.Open(4);

            popup.Close();

            Assert.False(popup.IsOpen);
            Assert.Equal(DrumCaptureState.Closed, popup.State);
            Assert.Equal(-1, popup.Lane);
            Assert.Null(popup.ConflictMessage);
        }

        [Fact]
        public void CurrentBindings_ReflectsWorkingBindingsForLane()
        {
            var popup = NewPopup();
            popup.Open(4);

            Assert.Contains("Key.S", popup.CurrentBindings); // default snare binding

            popup.TryCapture(new ButtonState("Key.Q", true));

            Assert.Contains("Key.Q", popup.CurrentBindings);
        }
    }
}
