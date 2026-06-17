using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.DrumConfig;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
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
        public void Constructor_WithNullBindings_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new DrumCapturePopup(null!, () => _system, key => _evicted.Add(key)));
        }

        [Fact]
        public void Constructor_WithNullSystemMappingProvider_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new DrumCapturePopup(_bindings, null!, key => _evicted.Add(key)));
        }

        [Fact]
        public void Constructor_WithNullEvictSystemBinding_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new DrumCapturePopup(_bindings, () => _system, null!));
        }

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

        [Fact]
        public void GetBindingChips_ReturnsOneChipPerCurrentBinding()
        {
            var popup = NewPopup();
            popup.Open(4);                                  // default lane 4 contains "Key.S"
            popup.TryCapture(new ButtonState("Key.Q", true)); // now "Key.S" + "Key.Q"

            var chips = popup.GetBindingChips(1280, 720);

            Assert.Equal(2, chips.Count);
            Assert.Equal(
                new HashSet<string> { "Key.S", "Key.Q" },
                chips.Select(c => c.ButtonId).ToHashSet());
        }

        [Fact]
        public void GetBindingChips_LabelsAreFormattedNotRawIds()
        {
            // Chips must show human-readable labels (FormatButtonId), matching the per-zone
            // labels in DrumKitRenderer — not raw ids like "Key.OemSemicolon".
            var popup = NewPopup();
            popup.Open(4); // default lane 4 binding is "Key.S"
            popup.TryCapture(new ButtonState("Key.Space", true));

            var chips = popup.GetBindingChips(1280, 720);
            var byId = chips.ToDictionary(c => c.ButtonId, c => c.Label);

            Assert.Equal("S", byId["Key.S"]);
            Assert.Equal("Space", byId["Key.Space"]);
        }

        [Fact]
        public void GetBindingChips_RemoveRects_AreNonEmptyAndDoNotOverlap()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Q", true));

            var chips = popup.GetBindingChips(1280, 720);

            foreach (var c in chips)
            {
                Assert.True(c.Remove.Width > 0 && c.Remove.Height > 0);
                Assert.True(c.Bounds.Contains(c.Remove)); // remove ✕ sits inside its chip
            }
            Assert.False(chips[0].Remove.Intersects(chips[1].Remove));
        }

        [Fact]
        public void RemovingChipById_RemovesOnlyThatBinding()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Q", true));

            var chips = popup.GetBindingChips(1280, 720);
            var sChip = chips.Single(c => c.ButtonId == "Key.S");
            popup.RemoveBinding(sChip.ButtonId);

            Assert.DoesNotContain("Key.S", popup.CurrentBindings);
            Assert.Contains("Key.Q", popup.CurrentBindings);
        }

        [Fact]
        public void GetBindingChips_WhenLaneEmpty_ReturnsNoChips()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.ClearLane();

            Assert.Empty(popup.GetBindingChips(1280, 720));
        }

        [Fact]
        public void GetBindingChips_ManyBindings_WrapsToSecondRow()
        {
            var popup = NewPopup();
            popup.Open(4);
            foreach (var id in new[] { "MIDI.1", "MIDI.2", "MIDI.3", "MIDI.4", "MIDI.5", "MIDI.6", "MIDI.7", "MIDI.8" })
                popup.TryCapture(new ButtonState(id, true));

            var chips = popup.GetBindingChips(400, 300); // many bindings overflow one row
            var rows = chips.Select(c => c.Bounds.Y).Distinct().Count();

            Assert.True(rows > 1, "Expected chips to wrap onto a second row");
        }

        [Fact]
        public void GetBindingChips_AreLargeBoxes()
        {
            // Bindings render as large boxes (not thin inline pills), so the corner ✕ reads naturally.
            var popup = NewPopup();
            popup.Open(4); // default "Key.S"

            var chip = popup.GetBindingChips(1280, 720).Single();

            Assert.True(chip.Bounds.Height >= 40, "Chips should be large boxes");
            Assert.True(chip.Bounds.Width >= 60, "Chips should be wide enough to read");
        }

        [Fact]
        public void GetBindingChips_RemoveBox_SitsInTopRightCorner()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Q", true));

            foreach (var c in popup.GetBindingChips(1280, 720))
            {
                // In the top-right region, but clearly inset on every side so it reads as INSIDE
                // the box (a flush/edge ✕ looked like it was outside the chip).
                int topMargin = c.Remove.Top - c.Bounds.Top;
                int rightMargin = c.Bounds.Right - c.Remove.Right;
                Assert.InRange(topMargin, 4, c.Bounds.Height / 2);   // top half, with a visible border
                Assert.InRange(rightMargin, 4, c.Bounds.Width / 2);  // right half, with a visible border
                // ... and is a small corner box, not a full-height strip.
                Assert.True(c.Remove.Height < c.Bounds.Height,
                    "Remove box should be smaller than the chip");
                Assert.True(c.Bounds.Contains(c.Remove));
            }
        }

        [Fact]
        public void GetPanelRect_ReturnsCenteredRectangle()
        {
            var popup = NewPopup();
            var rect = popup.GetPanelRect(1280, 720);

            // Should be centered
            Assert.Equal((1280 - DrumCapturePopup.PopupWidth) / 2, rect.X);
            Assert.Equal((720 - DrumCapturePopup.PopupHeight) / 2, rect.Y);
            Assert.Equal(DrumCapturePopup.PopupWidth, rect.Width);
            Assert.Equal(DrumCapturePopup.PopupHeight, rect.Height);
        }

        [Fact]
        public void GetDoneRect_ReturnsCorrectPosition()
        {
            var popup = NewPopup();
            var panelRect = popup.GetPanelRect(1280, 720);
            var doneRect = popup.GetDoneRect(1280, 720);

            // Done button should be in bottom-right of panel
            Assert.True(doneRect.X > panelRect.X);
            Assert.True(doneRect.Y > panelRect.Y);
            Assert.True(doneRect.Right <= panelRect.Right);
            Assert.True(doneRect.Bottom <= panelRect.Bottom);
        }

        [Fact]
        public void GetClearRect_ReturnsCorrectPosition()
        {
            var popup = NewPopup();
            var panelRect = popup.GetPanelRect(1280, 720);
            var clearRect = popup.GetClearRect(1280, 720);

            // Clear button should be in bottom-left of panel
            Assert.True(clearRect.X >= panelRect.X);
            Assert.True(clearRect.Y > panelRect.Y);
            Assert.True(clearRect.Right <= panelRect.Right);
            Assert.True(clearRect.Bottom <= panelRect.Bottom);
        }

        [Fact]
        public void TryCapture_WithNullButton_ShouldReturnIgnored()
        {
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(null!);

            Assert.Equal(DrumCaptureOutcome.Ignored, outcome);
        }

        [Fact]
        public void TryCapture_WithWhitespaceButtonId_ShouldReturnIgnored()
        {
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(new ButtonState("   ", true));

            Assert.Equal(DrumCaptureOutcome.Ignored, outcome);
        }

        [Fact]
        public void TryCapture_WhenNotListening_ShouldReturnIgnored()
        {
            var popup = NewPopup();
            // Don't open the popup, so it's not in listening state

            var outcome = popup.TryCapture(new ButtonState("Key.Q", true));

            Assert.Equal(DrumCaptureOutcome.Ignored, outcome);
        }

        [Fact]
        public void Tick_WhenClosed_ShouldReturnEarly()
        {
            var popup = NewPopup();
            // Don't open the popup

            var exception = Record.Exception(() => popup.Tick(1.0));
            Assert.Null(exception);
        }

        [Fact]
        public void Tick_WhenListening_ShouldNotChangeState()
        {
            var popup = NewPopup();
            popup.Open(4);

            popup.Tick(1.0);

            Assert.Equal(DrumCaptureState.Listening, popup.State);
        }

        [Fact]
        public void GetBindingChips_WithDifferentViewportSizes_ShouldReturnValidChips()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Q", true));

            var chips1 = popup.GetBindingChips(1280, 720);
            var chips2 = popup.GetBindingChips(800, 600);

            // Should return chips for both viewport sizes
            Assert.NotEmpty(chips1);
            Assert.NotEmpty(chips2);
            Assert.Equal(2, chips1.Count); // Default + Q
            Assert.Equal(2, chips2.Count);
        }
    }
}
