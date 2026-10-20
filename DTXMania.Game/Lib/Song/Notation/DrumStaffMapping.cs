using System;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Song.Notation
{
    /// <summary>
    /// Notehead glyph for a drum-staff position. Matches DTXWeb's VexFlow '/x2' (x) and '/x3' (circled x) keys.
    /// </summary>
    internal enum NoteheadShape
    {
        Normal,
        X,
        CircledX
    }

    /// <summary>
    /// Where a DTX drum channel sits on the drum staff.
    /// </summary>
    /// <param name="StaffStep">
    /// Diatonic step with origin E4 = 0. Each +1 is the next line or space up:
    /// D4 = -1, F4 = 1, A4 = 3, C5 = 5, D5 = 6, E5 = 7, F5 = 8, G5 = 9, A5 = 10, B5 = 11.
    /// </param>
    /// <param name="Shape">Notehead glyph.</param>
    internal readonly record struct DrumStaffPosition(int StaffStep, NoteheadShape Shape);

    /// <summary>
    /// Port of DTXWeb drumMapping.ts. Keyed on <see cref="Note.Channel"/>, never <see cref="Note.LaneIndex"/>:
    /// LaneIndex merges open/closed hi-hat and moves left bass into the left-pedal lane.
    /// </summary>
    internal static class DrumStaffMapping
    {
        private const int D4 = -1;
        private const int F4 = 1;
        private const int A4 = 3;
        private const int C5 = 5;
        private const int D5 = 6;
        private const int E5 = 7;
        private const int F5 = 8;
        private const int G5 = 9;
        private const int A5 = 10;
        private const int B5 = 11;

        /// <summary>
        /// Returns the staff position for one of the twelve playable DTX drum channels (0x11-0x1C).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The channel is not a playable drum channel.</exception>
        public static DrumStaffPosition GetPosition(int channel)
        {
            return channel switch
            {
                0x11 => new DrumStaffPosition(G5, NoteheadShape.X),        // closed hi-hat
                0x12 => new DrumStaffPosition(C5, NoteheadShape.Normal),   // snare
                0x13 => new DrumStaffPosition(F4, NoteheadShape.Normal),   // bass drum
                0x14 => new DrumStaffPosition(E5, NoteheadShape.Normal),   // high tom
                0x15 => new DrumStaffPosition(D5, NoteheadShape.Normal),   // low tom
                0x16 => new DrumStaffPosition(A5, NoteheadShape.X),        // right crash
                0x17 => new DrumStaffPosition(A4, NoteheadShape.Normal),   // floor tom
                0x18 => new DrumStaffPosition(G5, NoteheadShape.CircledX), // open hi-hat
                0x19 => new DrumStaffPosition(F5, NoteheadShape.X),        // ride
                0x1A => new DrumStaffPosition(B5, NoteheadShape.X),        // left crash
                0x1B => new DrumStaffPosition(D4, NoteheadShape.X),        // pedal hi-hat
                0x1C => new DrumStaffPosition(F4, NoteheadShape.Normal),   // left bass
                _ => throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    $"DTX channel 0x{channel:X2} has no drum staff position; notation supports 0x11-0x1C.")
            };
        }
    }
}
