#nullable enable

using System;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// A player lane hit after <see cref="JudgementManager"/> processed it: the note it judged,
    /// or null for an empty hit.
    /// </summary>
    public sealed class PlayerHitResolvedEventArgs : EventArgs
    {
        public PlayerHitResolvedEventArgs(int lane, Note? judgedNote, double judgementTimeMs)
        {
            Lane = lane;
            JudgedNote = judgedNote;
            JudgementTimeMs = judgementTimeMs;
        }

        /// <summary>Lane the player hit.</summary>
        public int Lane { get; }

        /// <summary>The note this hit judged, or null when the hit judged nothing.</summary>
        public Note? JudgedNote { get; }

        /// <summary>Judgement clock time of the hit.</summary>
        public double JudgementTimeMs { get; }
    }
}
