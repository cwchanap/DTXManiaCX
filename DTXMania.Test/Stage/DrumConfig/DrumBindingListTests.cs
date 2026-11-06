using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Stage.DrumConfig;
using L = DTXMania.Game.Lib.UI.Layout.DrumConfigUILayout;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumBindingListTests
    {
        // Fixed-width "font": 8px per character keeps geometry deterministic.
        private static float Measure(string text) => text.Length * 8f;

        private static int NoThreshold(int note) => 0;

        [Fact]
        public void GetBindings_ReturnsOnlyTheLane_KeysFirstThenMidiByNote()
        {
            var map = new Dictionary<string, int>
            {
                ["MIDI.46"] = 1,
                ["Key.J"] = 1,
                ["MIDI.42"] = 1,
                ["Pad.A"] = 1,
                ["Key.S"] = 3,
                ["Key.Space"] = 1,
            };

            var bindings = DrumBindingList.GetBindings(map, 1);

            Assert.Equal(new[] { "Key.J", "Key.Space", "MIDI.42", "MIDI.46", "Pad.A" }, bindings);
        }

        [Theory]
        [InlineData("Key.A", "KEY", "A")]
        [InlineData("Key.OemSemicolon", "KEY", ";")]
        [InlineData("MIDI.38", "MIDI", "38")]
        [InlineData("Pad.A", "PAD", "A")]
        public void ChipText_SplitsKindAndValue(string id, string prefix, string value)
        {
            Assert.Equal((prefix, value), DrumBindingList.ChipText(id));
        }

        [Fact]
        public void VelocityText_ShowsOnlyWhenSetOrFocused()
        {
            Assert.Null(DrumBindingList.VelocityText(0, focused: false));
            Assert.Equal("VEL 12", DrumBindingList.VelocityText(12, focused: false));
            Assert.Equal("VEL 0", DrumBindingList.VelocityText(0, focused: true));
        }

        [Fact]
        public void HitTestRow_FindsEachRowAndMissesTheGaps()
        {
            for (int lane = 0; lane < L.LaneCount; lane++)
            {
                var r = L.GetRowRect(lane);
                Assert.Equal(lane, DrumBindingList.HitTestRow(r.Center.X, r.Center.Y));
            }
            var first = L.GetRowRect(0);
            Assert.Equal(-1, DrumBindingList.HitTestRow(first.Center.X, first.Bottom + 1)); // gap between rows
            Assert.Equal(-1, DrumBindingList.HitTestRow(100, first.Center.Y));               // kit panel
        }

        [Fact]
        public void Rows_FitInsideTheBindingsPanelAboveTheStatusLine()
        {
            for (int lane = 0; lane < L.LaneCount; lane++)
                Assert.True(L.ListPanel.Contains(L.GetRowRect(lane)));
            Assert.True(L.GetRowRect(L.LaneCount - 1).Bottom < L.StatusPosition.Y);
        }

        [Fact]
        public void LayoutRow_PlacesChipsLeftToRightAfterTheName()
        {
            var row = DrumBindingList.LayoutRow(1, new[] { "Key.J", "MIDI.42" }, NoThreshold, -1, false, Measure);

            Assert.Equal(2, row.Chips.Count);
            var r = L.GetRowRect(1);
            var a = row.Chips[0].Bounds;
            var b = row.Chips[1].Bounds;
            Assert.Equal(r.X + L.ChipsOffsetX, a.X);
            Assert.Equal(a.Right + L.ChipGap, b.X);
            Assert.All(row.Chips, c => Assert.Equal(L.ChipHeight, c.Bounds.Height));
            Assert.All(row.Chips, c => Assert.True(r.Contains(c.Bounds)));
            Assert.Equal(0, row.HiddenCount);
            Assert.Equal(DrumChipKind.Key, row.Chips[0].Kind);
            Assert.Equal(DrumChipKind.Midi, row.Chips[1].Kind);
        }

        [Fact]
        public void LayoutRow_UnfocusedChipsHaveNoControls()
        {
            var row = DrumBindingList.LayoutRow(1, new[] { "MIDI.42" }, NoThreshold, -1, false, Measure);

            var chip = row.Chips[0];
            Assert.False(chip.Focused);
            Assert.True(chip.Remove.IsEmpty);
            Assert.True(chip.Decrement.IsEmpty);
            Assert.True(chip.Increment.IsEmpty);
            Assert.Null(chip.Velocity);
        }

        [Fact]
        public void LayoutRow_FocusedMidiChip_HasVelocityAndThreeControlsInsideTheChip()
        {
            var row = DrumBindingList.LayoutRow(1, new[] { "Key.J", "MIDI.42" }, n => n == 42 ? 12 : 0, 1, false, Measure);

            var chip = row.Chips[1];
            Assert.True(chip.Focused);
            Assert.Equal("VEL 12", chip.Velocity);
            Assert.Equal(42, chip.MidiNote);
            var controls = new[] { chip.Decrement, chip.Increment, chip.Remove };
            Assert.All(controls, c => Assert.True(chip.Bounds.Contains(c)));
            Assert.True(chip.Decrement.Right <= chip.Increment.Left);
            Assert.True(chip.Increment.Right <= chip.Remove.Left);
            // Focus makes the chip wider than its unfocused self.
            var plain = DrumBindingList.LayoutRow(1, new[] { "Key.J", "MIDI.42" }, n => 12, -1, false, Measure);
            Assert.True(chip.Bounds.Width > plain.Chips[1].Bounds.Width);
        }

        [Fact]
        public void LayoutRow_FocusedKeyChip_HasOnlyRemove()
        {
            var row = DrumBindingList.LayoutRow(3, new[] { "Key.S" }, NoThreshold, 0, false, Measure);

            var chip = row.Chips[0];
            Assert.False(chip.Remove.IsEmpty);
            Assert.True(chip.Decrement.IsEmpty);
            Assert.True(chip.Increment.IsEmpty);
            Assert.True(chip.Bounds.Contains(chip.Remove));
        }

        [Fact]
        public void LayoutRow_WhenListening_AppendsListeningChipAndUnfocusesBindings()
        {
            var row = DrumBindingList.LayoutRow(1, new[] { "Key.J" }, NoThreshold, 0, true, Measure);

            Assert.Equal(2, row.Chips.Count);
            Assert.False(row.Chips[0].Focused);
            Assert.Equal(DrumChipKind.Listening, row.Chips[1].Kind);
            Assert.Equal(-1, row.Chips[1].Index);
        }

        [Fact]
        public void LayoutRow_TooManyChips_SlidesToKeepTheFocusedChipVisible()
        {
            var many = Enumerable.Range(30, 12).Select(n => $"MIDI.{n}").ToArray();

            var start = DrumBindingList.LayoutRow(1, many, NoThreshold, 0, false, Measure);
            Assert.True(start.HiddenCount > 0);
            Assert.Equal(0, start.Chips[0].Index);

            var end = DrumBindingList.LayoutRow(1, many, NoThreshold, 11, false, Measure);
            Assert.Contains(end.Chips, c => c.Index == 11 && c.Focused);
            Assert.Equal(many.Length - end.Chips.Count, end.HiddenCount);
            var r = L.GetRowRect(1);
            Assert.All(end.Chips, c => Assert.True(c.Bounds.Right <= r.Right - L.ChipsRightInset));

            var listening = DrumBindingList.LayoutRow(1, many, NoThreshold, 0, true, Measure);
            Assert.Equal(DrumChipKind.Listening, listening.Chips[^1].Kind);
        }

        [Fact]
        public void LayoutRow_AnyWindow_KeepsChipsAndTheOverflowMarkerInsideTheRow()
        {
            // Every chip count and focus (or listening), including a window slid to the last
            // chip with earlier ones hidden.
            int right = L.GetRowRect(1).Right - L.ChipsRightInset;
            for (int count = 1; count <= 14; count++)
            {
                var buttons = Enumerable.Range(30, count).Select(n => $"MIDI.{n}").ToArray();
                for (int focus = -1; focus <= count; focus++)
                {
                    bool listening = focus == count;
                    var row = DrumBindingList.LayoutRow(1, buttons, NoThreshold, listening ? -1 : focus, listening, Measure);

                    Assert.All(row.Chips, c => Assert.True(c.Bounds.Right <= right, $"{count} chips, focus {focus}"));
                    if (row.HiddenCount > 0)
                    {
                        float markerRight = row.OverflowPosition.X + (Measure($"+{row.HiddenCount}") * L.SmallTextScale);
                        Assert.True(markerRight <= right, $"+N marker overflows: {count} chips, focus {focus}");
                    }
                }
            }
        }

        [Fact]
        public void HitTestChip_FindsTheChipUnderThePoint()
        {
            var row = DrumBindingList.LayoutRow(1, new[] { "Key.J", "MIDI.42" }, NoThreshold, -1, false, Measure);
            var second = row.Chips[1].Bounds;

            Assert.Equal(1, DrumBindingList.HitTestChip(row, second.Center.X, second.Center.Y)?.Index);
            Assert.Null(DrumBindingList.HitTestChip(row, row.Bounds.X + 20, row.Bounds.Center.Y)); // the tag
        }
    }
}
