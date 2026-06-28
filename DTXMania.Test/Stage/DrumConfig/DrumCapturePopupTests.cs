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
        // Provider-driven popup: a mutable dict behind the drum-bindings provider stands in for the
        // stage's working copy. The popup is intent-only — TryCapture never touches this dict; the
        // stage would. Tests that need pre-existing lane bindings populate _drum directly.
        private readonly Dictionary<string, int> _drum = new();
        private readonly Dictionary<int, int> _thresholds = new();
        private readonly Dictionary<Keys, InputCommandType> _system = new()
        {
            [Keys.Enter] = InputCommandType.Activate,           // required
            [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed, // non-required
        };

        private DrumCapturePopup NewPopup() => new(
            () => _drum,
            () => _system,
            note => _thresholds.TryGetValue(note, out var threshold) ? threshold : 0);

        [Fact]
        public void Constructor_WithNullDrumBindingsProvider_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DrumCapturePopup(null!, () => _system));
        }

        [Fact]
        public void Constructor_WithNullSystemMappingProvider_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DrumCapturePopup(() => _drum, null!));
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
        public void TryCapture_UnboundKey_ReturnsCapturedWithoutMutating()
        {
            // Intent-only: a capturable key returns Captured, but the popup does NOT touch the
            // provider's underlying state — the stage applies the binding.
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(new ButtonState("Key.Q", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.False(_drum.ContainsKey("Key.Q")); // provider dict unchanged — popup is intent-only
        }

        [Fact]
        public void TryCapture_RequiredNavKey_RejectsWithoutBinding()
        {
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(new ButtonState("Key.Enter", true));

            Assert.Equal(DrumCaptureOutcome.Rejected, outcome);
            Assert.Equal(DrumCaptureState.ShowingConflict, popup.State);
            Assert.False(_drum.ContainsKey("Key.Enter")); // no binding applied (intent-only)
        }

        [Fact]
        public void TryCapture_NonRequiredSystemKey_ReturnsCapturedWithoutEvicting()
        {
            // Eviction of a claimed non-required system key is deferred to the stage's commit, so
            // capturing one must NOT mutate the system mapping (that would lose the shortcut if the
            // binding is later removed/cleared/reset before Save). The popup only reports Captured.
            var popup = NewPopup();
            popup.Open(7);

            var outcome = popup.TryCapture(new ButtonState("Key.PageUp", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.True(_system.ContainsKey(Keys.PageUp)); // system mapping untouched at capture
        }

        [Fact]
        public void TryCapture_NonKeyboardButton_ReturnsCapturedWithoutSystemCheck()
        {
            var popup = NewPopup();
            popup.Open(6);

            var outcome = popup.TryCapture(new ButtonState("MIDI.36", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
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
            Assert.False(_drum.ContainsKey("Key.Q")); // intent-only: nothing applied
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
        public void CurrentBindings_ReflectsProviderForLane()
        {
            // Pre-populate the provider dict (the stage's working copy) instead of capturing through
            // the popup, which is now intent-only.
            _drum["Key.S"] = 4;
            var popup = NewPopup();
            popup.Open(4);

            Assert.Contains("Key.S", popup.CurrentBindings);
        }

        [Fact]
        public void GetBindingChips_ReturnsOneChipPerCurrentBinding()
        {
            _drum["Key.S"] = 4;
            _drum["Key.Q"] = 4; // two bindings for lane 4
            var popup = NewPopup();
            popup.Open(4);

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
            _drum["Key.S"] = 4;
            _drum["Key.Space"] = 4;
            var popup = NewPopup();
            popup.Open(4);

            var chips = popup.GetBindingChips(1280, 720);
            var byId = chips.ToDictionary(c => c.ButtonId, c => c.Label);

            Assert.Equal("S", byId["Key.S"]);
            Assert.Equal("Space", byId["Key.Space"]);
        }

        [Fact]
        public void GetBindingChips_MidiBinding_IncludesThresholdAndAdjustmentRects()
        {
            _drum["MIDI.36"] = 4;
            _thresholds[36] = 20;
            var popup = NewPopup();
            popup.Open(4);

            var chip = popup.GetBindingChips(1280, 720).Single();

            Assert.True(chip.IsMidi);
            Assert.Equal(36, chip.MidiNoteNumber);
            Assert.Equal(20, chip.MidiVelocityThreshold);
            Assert.Contains("v>20", chip.Label);
            Assert.NotEqual(Rectangle.Empty, chip.DecrementVelocityThreshold);
            Assert.NotEqual(Rectangle.Empty, chip.IncrementVelocityThreshold);
            Assert.True(chip.Bounds.Contains(chip.DecrementVelocityThreshold));
            Assert.True(chip.Bounds.Contains(chip.IncrementVelocityThreshold));
        }

        [Fact]
        public void GetBindingChips_KeyboardBinding_HasNoThresholdControls()
        {
            _drum["Key.S"] = 4;
            var popup = NewPopup();
            popup.Open(4);

            var chip = popup.GetBindingChips(1280, 720).Single();

            Assert.False(chip.IsMidi);
            Assert.Equal(-1, chip.MidiNoteNumber);
            Assert.Equal(0, chip.MidiVelocityThreshold);
            Assert.Equal(Rectangle.Empty, chip.DecrementVelocityThreshold);
            Assert.Equal(Rectangle.Empty, chip.IncrementVelocityThreshold);
        }

        [Fact]
        public void GetBindingChips_RemoveRects_AreNonEmptyAndDoNotOverlap()
        {
            _drum["Key.S"] = 4;
            _drum["Key.Q"] = 4;
            var popup = NewPopup();
            popup.Open(4);

            var chips = popup.GetBindingChips(1280, 720);

            foreach (var c in chips)
            {
                Assert.True(c.Remove.Width > 0 && c.Remove.Height > 0);
                Assert.True(c.Bounds.Contains(c.Remove)); // remove ✕ sits inside its chip
            }
            Assert.False(chips[0].Remove.Intersects(chips[1].Remove));
        }

        [Fact]
        public void GetBindingChips_WhenLaneEmpty_ReturnsNoChips()
        {
            // Open the popup on a lane with no drum entries (no ClearLane — it's gone).
            var popup = NewPopup();
            popup.Open(4);

            Assert.Empty(popup.GetBindingChips(1280, 720));
        }

        [Fact]
        public void GetBindingChips_ManyBindings_WrapsToSecondRow()
        {
            foreach (var id in new[] { "MIDI.1", "MIDI.2", "MIDI.3", "MIDI.4", "MIDI.5", "MIDI.6", "MIDI.7", "MIDI.8" })
                _drum[id] = 4;
            var popup = NewPopup();
            popup.Open(4);

            var chips = popup.GetBindingChips(400, 300); // many bindings overflow one row
            var rows = chips.Select(c => c.Bounds.Y).Distinct().Count();

            Assert.True(rows > 1, "Expected chips to wrap onto a second row");
        }

        [Fact]
        public void GetBindingChips_AreLargeBoxes()
        {
            // Bindings render as large boxes (not thin inline pills), so the corner ✕ reads naturally.
            _drum["Key.S"] = 4;
            var popup = NewPopup();
            popup.Open(4);

            var chip = popup.GetBindingChips(1280, 720).Single();

            Assert.True(chip.Bounds.Height >= 40, "Chips should be large boxes");
            Assert.True(chip.Bounds.Width >= 60, "Chips should be wide enough to read");
        }

        [Fact]
        public void GetBindingChips_RemoveBox_SitsInTopRightCorner()
        {
            _drum["Key.S"] = 4;
            _drum["Key.Q"] = 4;
            var popup = NewPopup();
            popup.Open(4);

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
            _drum["Key.S"] = 4;
            _drum["Key.Q"] = 4;
            var popup = NewPopup();
            popup.Open(4);

            var chips1 = popup.GetBindingChips(1280, 720);
            var chips2 = popup.GetBindingChips(800, 600);

            // Should return chips for both viewport sizes
            Assert.NotEmpty(chips1);
            Assert.NotEmpty(chips2);
            Assert.Equal(2, chips1.Count);
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
            _drum["Key.S"] = 4; // one chip for lane 4
            var popup = NewPopup();
            popup.Open(4);
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
        public void Draw_WhenOpenWithMidiBinding_RendersThresholdLabel()
        {
            _drum["MIDI.36"] = 4;
            _thresholds[36] = 20;
            var popup = NewPopup();
            popup.Open(4);
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(10, 10));

            popup.Draw(CreateFakeSpriteBatch(), font.Object, whitePixel: null, 1280, 720);

            font.Verify(f => f.DrawString(
                It.IsAny<SpriteBatch>(),
                It.Is<string>(s => s.Contains("MIDI 36") && s.Contains("v>20")),
                It.IsAny<Vector2>(),
                It.IsAny<Color>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public void Draw_WhenOpenWithNoBindings_ShowsEmptyPlaceholder()
        {
            // Open on a lane with no drum entries (no ClearLane — it's gone) -> "(no bindings)".
            var popup = NewPopup();
            popup.Open(4);
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
            _drum["Key.S"] = 4;
            var popup = NewPopup();
            popup.Open(4);

            // whitePixel null + font null: computes chips then returns at the font-null guard.
            var ex = Record.Exception(() =>
                popup.Draw(CreateFakeSpriteBatch(), null, null, 1280, 720));

            Assert.Null(ex);
        }
    }
}
