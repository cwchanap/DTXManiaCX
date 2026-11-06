#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.Input;
using L = DTXMania.Game.Lib.UI.Layout.DrumConfigUILayout;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    public enum DrumChipKind { Key, Midi, Other, Listening }

    /// <summary>
    /// One chip in a bindings row. <see cref="Index"/> is the binding's position in the lane's
    /// sorted list (-1 for the Listening chip). The control rectangles are empty unless the chip
    /// is focused; <see cref="Decrement"/>/<see cref="Increment"/> exist only on MIDI chips.
    /// </summary>
    public readonly record struct DrumChip(
        int Index,
        string ButtonId,
        DrumChipKind Kind,
        string Prefix,
        string Value,
        string? Velocity,
        int MidiNote,
        bool Focused,
        Rectangle Bounds,
        Rectangle Decrement,
        Rectangle Increment,
        Rectangle Remove);

    /// <summary>
    /// Laid-out row: the visible chips, how many did not fit, and where the "+N" count goes
    /// (left edge, vertical center).
    /// </summary>
    public sealed record DrumBindingRow(int Lane, Rectangle Bounds, IReadOnlyList<DrumChip> Chips, int HiddenCount, Vector2 OverflowPosition);

    /// <summary>
    /// Pure layout for the bindings list (no GraphicsDevice): binding order, chip geometry and
    /// hit-testing. Text widths come from a measure function so geometry matches the font in
    /// game and stays deterministic in tests.
    /// </summary>
    public static class DrumBindingList
    {
        /// <summary>
        /// Buttons bound to <paramref name="lane"/> in display order: keyboard keys (by label),
        /// then MIDI notes (by note number), then anything else (by id).
        /// </summary>
        public static IReadOnlyList<string> GetBindings(IReadOnlyDictionary<string, int> buttonToLane, int lane) =>
            buttonToLane
                .Where(kvp => kvp.Value == lane)
                .Select(kvp => kvp.Key)
                .OrderBy(KindOrder)
                .ThenBy(id => KeyBindings.TryParseMidiButtonId(id, out var note) ? note : 0)
                .ThenBy(id => KeyBindings.FormatButtonId(id), StringComparer.Ordinal)
                .ToList();

        private static int KindOrder(string id) => KindOf(id) switch
        {
            DrumChipKind.Key => 0,
            DrumChipKind.Midi => 1,
            _ => 2,
        };

        public static DrumChipKind KindOf(string buttonId)
        {
            if (KeyBindings.IsKeyboardButtonId(buttonId))
                return DrumChipKind.Key;
            if (KeyBindings.TryParseMidiButtonId(buttonId, out _))
                return DrumChipKind.Midi;
            return DrumChipKind.Other;
        }

        /// <summary>Chip text for a button: ("KEY", "A"), ("MIDI", "38") or ("PAD", "A").</summary>
        public static (string Prefix, string Value) ChipText(string buttonId)
        {
            if (KeyBindings.TryParseMidiButtonId(buttonId, out var note))
                return ("MIDI", note.ToString());
            var label = KeyBindings.FormatButtonId(buttonId);
            if (KeyBindings.IsKeyboardButtonId(buttonId))
                return ("KEY", label);
            if (label.StartsWith("Pad ", StringComparison.Ordinal))
                return ("PAD", label.Substring(4));
            return ("BTN", label);
        }

        /// <summary>
        /// The "VEL n" tag for a MIDI chip: always on the focused chip (it is the one being
        /// edited), otherwise only when the note has a threshold.
        /// </summary>
        public static string? VelocityText(int threshold, bool focused) =>
            focused || threshold > 0 ? $"VEL {threshold}" : null;

        /// <summary>Lane of the row under the point, or -1.</summary>
        public static int HitTestRow(int x, int y)
        {
            for (int lane = 0; lane < L.LaneCount; lane++)
            {
                if (L.GetRowRect(lane).Contains(x, y))
                    return lane;
            }
            return -1;
        }

        /// <summary>
        /// Lays out one row. <paramref name="focusedChip"/> is an index into
        /// <paramref name="buttons"/> (-1 for none); with <paramref name="listening"/> a Listening
        /// chip is appended and kept in view. When the chips do not fit, the visible window slides
        /// so the focused (or Listening) chip stays on screen and a "+N" count marks the rest.
        /// </summary>
        public static DrumBindingRow LayoutRow(
            int lane,
            IReadOnlyList<string> buttons,
            Func<int, int> velocityThreshold,
            int focusedChip,
            bool listening,
            Func<string, float> measure)
        {
            var row = L.GetRowRect(lane);
            int left = row.X + L.ChipsOffsetX;
            int right = row.Right - L.ChipsRightInset;
            int top = row.Y + L.ChipsOffsetY;

            var specs = new List<DrumChip>(buttons.Count + 1);
            var widths = new List<int>(buttons.Count + 1);
            for (int i = 0; i < buttons.Count; i++)
            {
                var id = buttons[i];
                var kind = KindOf(id);
                var (prefix, value) = ChipText(id);
                int note = kind == DrumChipKind.Midi && KeyBindings.TryParseMidiButtonId(id, out var n) ? n : -1;
                bool focused = !listening && i == focusedChip;
                var velocity = note >= 0 ? VelocityText(Math.Clamp(velocityThreshold(note), 0, 127), focused) : null;
                specs.Add(new DrumChip(i, id, kind, prefix, value, velocity, note, focused,
                    Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty));
                widths.Add(ChipWidth(kind, prefix, value, velocity, focused, measure));
            }
            if (listening)
            {
                specs.Add(new DrumChip(-1, string.Empty, DrumChipKind.Listening, string.Empty, L.ListeningLabel, null, -1,
                    false, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty));
                widths.Add(ChipWidth(DrumChipKind.Listening, string.Empty, L.ListeningLabel, null, false, measure));
            }

            int anchor = listening ? specs.Count - 1 : Math.Clamp(focusedChip, 0, Math.Max(0, specs.Count - 1));
            int markerReserve = (int)MathF.Ceiling(measure($"+{specs.Count}") * L.SmallTextScale) + L.ChipGap;
            int start = 0;
            int end = Fit(start);
            while (anchor >= end && start < anchor)
                end = Fit(++start);
            if (end == start && start < specs.Count)
                end = start + 1; // a single chip wider than the row still shows (clipped)

            var chips = new List<DrumChip>(end - start);
            int x = left;
            for (int i = start; i < end; i++)
            {
                var bounds = new Rectangle(x, top, widths[i], L.ChipHeight);
                chips.Add(PlaceControls(specs[i], bounds));
                x += widths[i] + L.ChipGap;
            }

            int hidden = specs.Count - (end - start);
            return new DrumBindingRow(lane, row, chips, hidden, new Vector2(x, row.Center.Y));

            int Fit(int from)
            {
                int cx = left;
                int last = from;
                for (int i = from; i < specs.Count; i++)
                {
                    int limit = i < specs.Count - 1 ? right - markerReserve : right;
                    if (cx + widths[i] > limit)
                        break;
                    cx += widths[i] + L.ChipGap;
                    last = i + 1;
                }
                return last;
            }
        }

        /// <summary>Chip of <paramref name="row"/> under the point, or null.</summary>
        public static DrumChip? HitTestChip(DrumBindingRow row, int x, int y)
        {
            foreach (var chip in row.Chips)
            {
                if (chip.Bounds.Contains(x, y))
                    return chip;
            }
            return null;
        }

        private static int ChipWidth(DrumChipKind kind, string prefix, string value, string? velocity, bool focused,
            Func<string, float> measure)
        {
            float w = L.ChipPadX;
            if (kind == DrumChipKind.Listening)
                w += L.ListeningDotSize + L.ListeningDotGap;
            else
                w += (measure(prefix) * L.SmallTextScale) + L.ChipPrefixGap;
            w += measure(value);
            if (velocity != null)
                w += L.ChipVelocityGap + (measure(velocity) * L.SmallTextScale);

            if (focused)
            {
                if (kind == DrumChipKind.Midi)
                    w += L.ControlLeadGap + (L.ControlSize * 2) + L.ControlGap + L.RemoveLeadGap;
                else
                    w += L.ControlLeadGap;
                w += L.ControlSize + L.ChipPadRightWithControls;
            }
            else
            {
                w += L.ChipPadX;
            }
            return (int)MathF.Ceiling(w);
        }

        private static DrumChip PlaceControls(DrumChip chip, Rectangle bounds)
        {
            if (!chip.Focused)
                return chip with { Bounds = bounds };

            int y = bounds.Y + ((bounds.Height - L.ControlSize) / 2);
            var remove = new Rectangle(bounds.Right - L.ChipPadRightWithControls - L.ControlSize, y, L.ControlSize, L.ControlSize);
            if (chip.Kind != DrumChipKind.Midi)
                return chip with { Bounds = bounds, Remove = remove };

            var increment = new Rectangle(remove.X - L.RemoveLeadGap - L.ControlSize, y, L.ControlSize, L.ControlSize);
            var decrement = new Rectangle(increment.X - L.ControlGap - L.ControlSize, y, L.ControlSize, L.ControlSize);
            return chip with { Bounds = bounds, Decrement = decrement, Increment = increment, Remove = remove };
        }
    }
}
