using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Song.Notation
{
    /// <summary>
    /// One source note placed on the notation grid. Onsets that share a tick merge into one chord.
    /// </summary>
    /// <param name="Tick">Notation tick in [0, measureTicks), already rounded by the caller.</param>
    /// <param name="Position">Staff position from <see cref="DrumStaffMapping.GetPosition"/>.</param>
    /// <param name="Note">Gameplay-owned source note. It is referenced and never modified.</param>
    internal readonly record struct NotationOnset(int Tick, DrumStaffPosition Position, Note Note);

    /// <summary>
    /// Pure port of DTXWeb quantize.ts. Snaps one bar of onsets to chords, binary notes and rests, and
    /// 24/48/96/192-tick triplet groups. It has no BPM, timing map or geometry. The caller supplies
    /// notation ticks, and fills <see cref="NotationEntry.TimeMs"/> (this quantizer leaves it at 0).
    /// </summary>
    /// <remarks>
    /// Tiny-gap exceptions, kept from DTXWeb instead of inventing 1-2 tick glyphs:
    /// <list type="bullet">
    /// <item>An onset span of 1-2 ticks becomes a note of that raw length.</item>
    /// <item>A 1-2 tick remainder after an onset's note, or a 1-2 tick gap with no rest to absorb it, is dropped.</item>
    /// <item>A 1-2 tick remainder after rests folds into the last rest, which can then be non-binary, e.g. 5 or 7 ticks (any binary length +1/+2).</item>
    /// </list>
    /// </remarks>
    internal static class NotationQuantizer
    {
        // Largest first, index-aligned with <see cref="NoteValue"/>'s declaration order
        // (Whole..SixtyFourth). Internal so NotationGlyphs.FromTicks shares the ladder.
        // There is no 8-tick entry: the 24-tick triplet group covers 8-tick slots.
        internal static readonly int[] BinaryTicks = { 192, 96, 48, 24, 12, 6, 3 };

        // Ascending. Each group splits into three slots of groupTicks / 3.
        private static readonly int[] TripletGroupTicks = { 24, 48, 96, 192 };

        /// <summary>
        /// Quantizes one bar. Onsets may share a tick and merge into one chord, with heads unique by position.
        /// Every source note is kept in its head's <see cref="NotationHead.Notes"/>, in input order. Heads inside a chord
        /// are ordered by staff step, then notehead shape, so head order does not depend on onset input order.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// measureTicks is below 1, or an onset tick is outside [0, measureTicks). Such an onset would be lost silently.
        /// </exception>
        public static NotationMeasure QuantizeMeasure(int index, int measureTicks, IReadOnlyList<NotationOnset> onsets)
        {
            if (measureTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(measureTicks), measureTicks, "A bar needs at least one notation tick.");
            }

            foreach (NotationOnset onset in onsets)
            {
                if (onset.Tick < 0 || onset.Tick >= measureTicks)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(onsets),
                        onset.Tick,
                        $"Onset tick must be in [0, {measureTicks}); an out-of-bar onset would be lost silently.");
                }
            }

            List<Chord> chords = MergeOnsets(onsets);
            var chordByTick = chords.ToDictionary(chord => chord.Tick);
            var entries = new List<NotationEntry>();
            var tuplets = new List<NotationTuplet>();

            int cursor = 0;
            while (cursor < measureTicks)
            {
                Triplet? triplet = FindTriplet(cursor, chords, chordByTick, measureTicks);
                if (triplet is { } group)
                {
                    tuplets.Add(new NotationTuplet(entries.Count, 3, group.SlotTicks));
                    for (int slot = 0; slot < 3; slot++)
                    {
                        int slotStart = cursor + slot * group.SlotTicks;
                        entries.Add(chordByTick.TryGetValue(slotStart, out Chord slotChord)
                            ? new NotationEntry(slotStart, group.SlotTicks, 0.0, slotChord.Heads)
                            : Rest(slotStart, group.SlotTicks));
                    }

                    cursor += group.SlotTicks * 3;
                    continue;
                }

                int nextTick = NextOnsetTick(chords, cursor, measureTicks);
                int span = nextTick - cursor;
                if (chordByTick.TryGetValue(cursor, out Chord chord))
                {
                    // The note takes the largest binary value that fits its span, as ticksToDurations does.
                    IReadOnlyList<int> durations = TicksToBinaryDurations(span);
                    int noteTicks = durations.Count > 0 ? durations[0] : span;
                    entries.Add(new NotationEntry(cursor, noteTicks, 0.0, chord.Heads));
                    PushRests(entries, cursor + noteTicks, span - noteTicks);
                }
                else
                {
                    PushRests(entries, cursor, span);
                }

                cursor = nextTick;
            }

            // An empty bar shorter than 3 ticks has no binary rest. Keep one rest so the bar still has an entry.
            if (entries.Count == 0)
            {
                entries.Add(Rest(0, measureTicks));
            }

            return new NotationMeasure(index, measureTicks, entries, tuplets);
        }

        /// <summary>
        /// Greedy largest-first binary decomposition of a span (port of DTXWeb ticksToDurations).
        /// Any remainder under 3 ticks is not represented.
        /// </summary>
        internal static IReadOnlyList<int> TicksToBinaryDurations(int ticks)
        {
            var durations = new List<int>();
            int remaining = ticks;
            while (remaining >= 3)
            {
                int next = BinaryTicks.First(value => value <= remaining);
                durations.Add(next);
                remaining -= next;
            }

            return durations;
        }

        // Fills [start, start + span) with binary rests. A 1-2 tick remainder folds into the last rest this call
        // created. With no rest to fold into, it is dropped (documented exception).
        private static void PushRests(List<NotationEntry> entries, int start, int span)
        {
            int first = entries.Count;
            int cursor = start;
            foreach (int ticks in TicksToBinaryDurations(span))
            {
                entries.Add(Rest(cursor, ticks));
                cursor += ticks;
            }

            int remainder = span - (cursor - start);
            if (remainder > 0 && entries.Count > first)
            {
                NotationEntry last = entries[^1];
                entries[^1] = last with { DurationTicks = last.DurationTicks + remainder };
            }
        }

        // Ported from DTXWeb findTripletCandidate, ranked the same way: most occupied slots, then a group that ends
        // on an observed onset or bar end, then the smaller group.
        private static Triplet? FindTriplet(
            int cursor,
            IReadOnlyList<Chord> chords,
            IReadOnlyDictionary<int, Chord> chordByTick,
            int measureTicks)
        {
            var candidates = new List<Triplet>();
            foreach (int groupTicks in TripletGroupTicks)
            {
                int slotTicks = groupTicks / 3;
                int groupEnd = cursor + groupTicks;
                if (groupEnd > measureTicks)
                {
                    continue;
                }

                // An onset inside the group that is not on a slot start rules the group out.
                bool offSlotOnset = chords.Any(chord =>
                    chord.Tick > cursor && chord.Tick < groupEnd && (chord.Tick - cursor) % slotTicks != 0);
                if (offSlotOnset)
                {
                    continue;
                }

                int occupied = 0;
                for (int slot = 0; slot < 3; slot++)
                {
                    if (chordByTick.ContainsKey(cursor + slot * slotTicks))
                    {
                        occupied++;
                    }
                }

                bool observedEnd = groupEnd == measureTicks || chordByTick.ContainsKey(groupEnd);
                bool lastSlotOccupied = chordByTick.ContainsKey(cursor + 2 * slotTicks);
                if (occupied < 2 || (!lastSlotOccupied && !observedEnd))
                {
                    continue;
                }

                candidates.Add(new Triplet(slotTicks, occupied, observedEnd));
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates
                .OrderByDescending(candidate => candidate.OccupiedCount)
                .ThenByDescending(candidate => candidate.ObservedEnd)
                .ThenBy(candidate => candidate.SlotTicks)
                .First();
        }

        private static int NextOnsetTick(IReadOnlyList<Chord> chords, int tick, int measureTicks)
        {
            foreach (Chord chord in chords)
            {
                if (chord.Tick > tick)
                {
                    return chord.Tick;
                }
            }

            return measureTicks;
        }

        // Ascending by tick. Within a tick, heads are unique by position and keep every source note in input order.
        private static List<Chord> MergeOnsets(IReadOnlyList<NotationOnset> onsets) =>
            onsets
                .GroupBy(onset => onset.Tick)
                .OrderBy(group => group.Key)
                .Select(group => new Chord(
                    group.Key,
                    group
                        .GroupBy(onset => onset.Position)
                        .OrderBy(head => head.Key.StaffStep)
                        .ThenBy(head => head.Key.Shape)
                        .Select(head => new NotationHead(head.Key, head.Select(onset => onset.Note).ToArray()))
                        .ToArray()))
                .ToList();

        private static NotationEntry Rest(int startTick, int durationTicks) =>
            new(startTick, durationTicks, 0.0, Array.Empty<NotationHead>());

        private sealed record Chord(int Tick, IReadOnlyList<NotationHead> Heads);

        private readonly record struct Triplet(int SlotTicks, int OccupiedCount, bool ObservedEnd);
    }
}
