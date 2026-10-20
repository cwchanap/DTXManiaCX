using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Song.Notation
{
    /// <summary>
    /// Drum notation for one finalized chart: measures in bar order. Structural data only, with no
    /// rendering or gameplay state. Collections are copied on construction so the records stay immutable.
    /// </summary>
    internal sealed record NotationChart(IReadOnlyList<NotationMeasure> Measures)
    {
        public IReadOnlyList<NotationMeasure> Measures { get; } = Measures.ToArray();
    }

    /// <summary>
    /// One compiled bar, with entries in StartTick order.
    /// </summary>
    /// <param name="Index">Zero-based bar index in the compiled chart.</param>
    /// <param name="MeasureTicks">Notation ticks in this bar: Round(192 * effective length multiplier).</param>
    /// <param name="Entries">Chords and rests in onset order. Tuplets index into this list.</param>
    /// <param name="Tuplets">Triplet or other tuplet groups over runs of Entries.</param>
    internal sealed record NotationMeasure(
        int Index,
        int MeasureTicks,
        IReadOnlyList<NotationEntry> Entries,
        IReadOnlyList<NotationTuplet> Tuplets)
    {
        public IReadOnlyList<NotationEntry> Entries { get; } = Entries.ToArray();

        public IReadOnlyList<NotationTuplet> Tuplets { get; } = Tuplets.ToArray();
    }

    /// <summary>
    /// One rhythmic slot in a measure: a chord of heads, or a rest when <see cref="Heads"/> is empty.
    /// StartTick and DurationTicks are measure-relative notation ticks. TimeMs is absolute chart time.
    /// The quantizer emits TimeMs = 0, and the chart builder fills it with a 'with' expression.
    /// </summary>
    internal sealed record NotationEntry(
        int StartTick,
        int DurationTicks,
        double TimeMs,
        IReadOnlyList<NotationHead> Heads)
    {
        public IReadOnlyList<NotationHead> Heads { get; } = Heads.ToArray();

        public bool IsRest => Heads.Count == 0;
    }

    /// <summary>
    /// One drawn notehead. <see cref="Notes"/> are the gameplay-owned source notes that share this
    /// position and onset. They are referenced, never copied, so Note.Id is read after ChartManager assigns it.
    /// </summary>
    internal sealed record NotationHead(DrumStaffPosition Position, IReadOnlyList<Note> Notes)
    {
        public IReadOnlyList<Note> Notes { get; } = Notes.ToArray();
    }

    /// <summary>
    /// A run of entries [StartIndex, StartIndex + Count) that share one slot duration,
    /// for example a 48-tick triplet group.
    /// </summary>
    internal sealed record NotationTuplet(int StartIndex, int Count, int SlotTicks);
}
