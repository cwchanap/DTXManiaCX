using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Test.TestData;
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

        private DrumCapturePopup NewPopup() =>
            new(_bindings, () => _system);

        [Fact]
        public void Constructor_WithNullBindings_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new DrumCapturePopup(null!, () => _system));
        }

        [Fact]
        public void Constructor_WithNullSystemMappingProvider_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new DrumCapturePopup(_bindings, null!));
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
        public void TryCapture_NonRequiredSystemKey_BindsWithoutEvicting()
        {
            // Eviction of a claimed non-required system key is deferred to the stage's commit, so
            // capturing one must NOT mutate the system mapping (that would lose the shortcut if the
            // binding is later removed/cleared/reset before Save). The popup only claims the lane.
            var popup = NewPopup();
            popup.Open(7);

            var outcome = popup.TryCapture(new ButtonState("Key.PageUp", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains("Key.PageUp", _bindings.GetButtonsForLane(7));
            Assert.True(_system.ContainsKey(Keys.PageUp)); // system mapping untouched at capture
        }

        [Fact]
        public void TryCapture_NonKeyboardButton_BindsWithoutSystemCheck()
        {
            var popup = NewPopup();
            popup.Open(6);

            var outcome = popup.TryCapture(new ButtonState("MIDI.36", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains("MIDI.36", _bindings.GetButtonsForLane(6));
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

        // ---- Draw ----
        // Draw is exercised with a non-null SpriteBatch reference but whitePixel=null, so the
        // spriteBatch is never called (all drawing is guarded behind `if (whitePixel != null)`).
        // The font calls go to a mock that ignores the spriteBatch argument. This covers the
        // panel/chip/prompt text layout without needing a working GraphicsDevice.

        private static SpriteBatch CreateFakeSpriteBatch()
        {
            // Never constructed; only used as a non-null reference (no methods are called on it).
            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();
            GC.SuppressFinalize(spriteBatch);
            return spriteBatch;
        }

        private static IFont CreateMockFont()
        {
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(10, 10));
            return font.Object;
        }

        [Fact]
        public void Draw_WhenClosed_ReturnsImmediately()
        {
            var popup = NewPopup(); // not opened

            var ex = Record.Exception(() =>
                popup.Draw(CreateFakeSpriteBatch(), CreateMockFont(), null, 1280, 720));

            Assert.Null(ex);
        }

        [Fact]
        public void Draw_WhenOpenWithBindings_RendersChipLabelsClearAndDone()
        {
            var popup = NewPopup();
            popup.Open(4); // default "Key.S" -> one chip
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(10, 10));

            popup.Draw(CreateFakeSpriteBatch(), font.Object, whitePixel: null, 1280, 720);

            // Lane header, chip label + ✕ marker, prompt, Clear, and Done are all drawn.
            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), It.Is<string>(s => s.Contains("Configure")), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.AtLeastOnce);
            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "S", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.AtLeastOnce);
            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), It.Is<string>(s => s.Contains("Listening")), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.AtLeastOnce);
            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "Clear", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.AtLeastOnce);
            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "Done", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.AtLeastOnce);
        }

        [Fact]
        public void Draw_WhenOpenWithNoBindings_ShowsEmptyPlaceholder()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.ClearLane(); // no bindings -> "(no bindings)" branch
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(10, 10));

            popup.Draw(CreateFakeSpriteBatch(), font.Object, whitePixel: null, 1280, 720);

            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), "(no bindings)", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Once);
        }

        [Fact]
        public void Draw_WhenOpenInConflictState_RendersConflictPromptInRed()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Enter", true)); // -> ShowingConflict

            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(10, 10));
            Color? drawnColor = null;
            font.Setup(f => f.DrawString(It.IsAny<SpriteBatch>(), It.Is<string>(s => s.Contains("reserved")), It.IsAny<Vector2>(), It.IsAny<Color>()))
                .Callback<SpriteBatch, string, Vector2, Color>((_, _, _, c) => drawnColor = c);

            popup.Draw(CreateFakeSpriteBatch(), font.Object, whitePixel: null, 1280, 720);

            Assert.NotNull(drawnColor);
            Assert.Equal(Color.Red, drawnColor);
        }

        [Fact]
        public void Draw_WhenFontNull_ReturnsBeforeAnyTextLayout()
        {
            var popup = NewPopup();
            popup.Open(4);

            // whitePixel null + font null: computes chips then returns at the font-null guard.
            var ex = Record.Exception(() =>
                popup.Draw(CreateFakeSpriteBatch(), null, null, 1280, 720));

            Assert.Null(ex);
        }
    }
}
