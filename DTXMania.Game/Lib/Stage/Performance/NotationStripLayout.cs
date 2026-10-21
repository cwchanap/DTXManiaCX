using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// GPU-free placement contract for the scrolling notation strip (HPA-410).
    /// Pure time/pixel math only: no GraphicsDevice, fonts, sprites or owned resources.
    /// Task 2 fills in notehead/stem/beam/rest engraving on top of these anchors.
    /// </summary>
    public static class NotationStripLayout
    {
        /// <summary>
        /// Projects a chart time to strip x for the current playback time and scroll speed.
        /// </summary>
        /// <param name="timeMs">Chart time of the point being placed.</param>
        /// <param name="nowMs">Current playback time; it projects onto the hit marker x.</param>
        /// <param name="speedPercent">Scroll speed percentage (100 = normal, 200 = double).</param>
        /// <exception cref="ArgumentException"><paramref name="speedPercent"/> is not positive.</exception>
        public static double ToScreenX(double timeMs, double nowMs, double speedPercent)
        {
            if (speedPercent <= 0)
                throw new ArgumentException("Scroll speed must be greater than 0", nameof(speedPercent));

            return PerformanceUILayout.NotationStrip.HitMarkerX
                + (timeMs - nowMs) * PerformanceUILayout.NotationStrip.ScrollPixelsPerMs * (speedPercent / 100.0);
        }

        /// <summary>
        /// Maps a diatonic staff step to strip y: step 0 is the E4 bottom line, each +1 step is 10px up.
        /// </summary>
        public static float StaffStepToY(int staffStep)
        {
            return PerformanceUILayout.NotationStrip.E4LineY - staffStep * PerformanceUILayout.NotationStrip.DiatonicStep;
        }

        /// <summary>
        /// Label y for a DTX drum channel: the channel's staff line from <see cref="DrumStaffMapping"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The channel is not a playable drum channel.</exception>
        public static float GetChannelLabelY(int channel)
        {
            return StaffStepToY(DrumStaffMapping.GetPosition(channel).StaffStep);
        }

        /// <summary>
        /// The five staff line ys, top to bottom: F5=430, D5=450, B4=470, G4=490, E4=510 —
        /// the even diatonic steps at one staff-space (20px) pitch, not every diatonic step.
        /// </summary>
        public static IReadOnlyList<float> GetStaffLineYs()
        {
            return new[] { 8, 6, 4, 2, 0 }.Select(StaffStepToY).ToArray();
        }

        /// <summary>
        /// Selects the whole measures visible in [leftMs, rightMs): binary search over measure end
        /// times (MeasureLines[i+1].TimeMs) for the first measure crossing the left edge, then walk
        /// forward until a measure starts at or beyond the right edge. The terminal MeasureLines[^1]
        /// is only an end bound, never a measure start, so it is never selected.
        /// </summary>
        public static NotationMeasureWindow SelectVisibleMeasures(
            IReadOnlyList<MeasureLine> measureLines, double leftMs, double rightMs)
        {
            // MeasureLines[^1] terminates the final bar; measures are indices [0, Count-2].
            int lastMeasureIndex = measureLines.Count - 2;
            if (lastMeasureIndex < 0)
                return new NotationMeasureWindow(0, -1);

            int lo = 0;
            int hi = lastMeasureIndex;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (measureLines[mid + 1].TimeMs > leftMs)
                    hi = mid;
                else
                    lo = mid + 1;
            }

            if (measureLines[lo + 1].TimeMs <= leftMs)
                return new NotationMeasureWindow(0, -1);

            int last = lo - 1;
            for (int i = lo; i <= lastMeasureIndex; i++)
            {
                if (measureLines[i].TimeMs >= rightMs)
                    break;
                last = i;
            }

            return new NotationMeasureWindow(lo, last);
        }

        /// <summary>
        /// One barline anchor per visible measure, placed at the measure's compiled start time
        /// (MeasureLines[measure.Index].TimeMs). Barlines are vertical staff-height strokes; the
        /// renderer draws them from StaffTopY down to StaffBottomY.
        /// </summary>
        public static IReadOnlyList<NotationStripAnchor> GetVisibleBarlineAnchors(
            IReadOnlyList<MeasureLine> measureLines, double leftMs, double rightMs)
        {
            var window = SelectVisibleMeasures(measureLines, leftMs, rightMs);

            var anchors = new List<NotationStripAnchor>();
            for (int i = window.FirstIndex; i <= window.LastIndexInclusive; i++)
            {
                anchors.Add(new NotationStripAnchor(
                    measureLines[i].TimeMs,
                    dxPx: 0f,
                    yPx: PerformanceUILayout.NotationStrip.StaffTopY));
            }

            return anchors;
        }

        // The notation beat (quarter note) is 48 ticks per NotationQuantizer's grid.
        private const int BeatTicks = 48;

        // Ledger lines span 1.7 staff spaces, wide enough for every baked notehead shape.
        private const float LedgerLineWidthPx = 34f;

        // Fixed-length partial beam stub; flat beams only, so a constant stub suffices.
        private const float PartialBeamStubPx = 8f;

        /// <summary>
        /// Engraves one measure into strip-space anchors: noteheads at their staff steps, one shared
        /// up-stem per chord (x from the lowest head's Bravura StemUpSE, top at highest step + 7,
        /// extended to the beam for beamed groups), flat horizontal beams, rest glyphs, the tuplet
        /// numeral, ledger lines outside steps 0..8, and flags for isolated short notes. Whole notes
        /// and rests carry no stem. Ordinary beaming groups consecutive non-tuplet short notes inside
        /// one 48-tick beat without crossing rests; each <see cref="NotationTuplet"/> is its own group
        /// and may cross beat boundaries. Display values for tuplet slots derive from SlotTicks * 3 / 2.
        /// </summary>
        internal static NotationStripMeasureEngraving EngraveMeasure(NotationMeasure measure)
        {
            var glyphs = new List<NotationStripGlyphAnchor>();
            var rects = new List<NotationStripRectAnchor>();
            var group = new List<EngravedNote>();
            int groupBeat = -1;

            void FlushOrdinaryGroup()
            {
                if (group.Count == 0)
                    return;
                EngraveBeamGroup(group, glyphs, rects);
                group.Clear();
            }

            int i = 0;
            while (i < measure.Entries.Count)
            {
                NotationTuplet tuplet = FindTupletStartingAt(measure.Tuplets, i);
                if (tuplet != null)
                {
                    FlushOrdinaryGroup();
                    EngraveTuplet(measure.Entries, tuplet, glyphs, rects);
                    i += tuplet.Count;
                    continue;
                }

                NotationEntry entry = measure.Entries[i];
                NoteValue value = NotationGlyphs.FromTicks(entry.DurationTicks);
                EngraveEntryGlyphs(entry, value, glyphs, rects);
                EngravedNote? note = CreateEngravedNote(entry, value);

                if (note is { } beamable && NotationGlyphs.Flag(value) is not null)
                {
                    int beat = entry.StartTick / BeatTicks;
                    if (beat != groupBeat)
                    {
                        FlushOrdinaryGroup();
                        groupBeat = beat;
                    }
                    group.Add(beamable);
                }
                else
                {
                    FlushOrdinaryGroup();
                    if (note is { } stemmed)
                    {
                        // Quarter/half: a stem at the natural top, no beam and no flag.
                        rects.Add(StemRect(stemmed.TimeMs, stemmed.StemDx, stemmed.NaturalTopY, stemmed.StemBottomY));
                    }
                }

                i++;
            }

            FlushOrdinaryGroup();
            return new NotationStripMeasureEngraving(glyphs, rects);
        }

        private static NotationTuplet FindTupletStartingAt(IReadOnlyList<NotationTuplet> tuplets, int entryIndex)
        {
            foreach (var t in tuplets)
            {
                if (t.StartIndex == entryIndex)
                    return t;
            }

            return null;
        }

        /// <summary>
        /// Key-column instrument labels in display order (staff top to bottom), one per DTX drum
        /// lane; y comes from <see cref="DrumStaffMapping"/> via GetChannelLabelY. The HH and BD
        /// labels also stand in for their variant channels (open hi-hat 0x18, left bass 0x1C).
        /// </summary>
        internal static IReadOnlyList<NotationStripLabelAnchor> GetKeyColumnLabels()
        {
            return LabelDisplayOrder
                .Select(pair => new NotationStripLabelAnchor(pair.Label, GetChannelLabelY(pair.Channel)))
                .ToArray();
        }

        // Key-column display order, staff top to bottom (LC B5 .. LP D4). Each entry pairs the
        // channel whose DrumStaffMapping position sets the label y with its label text; the HH and
        // BD labels also stand in for their variant channels (open hi-hat 0x18, left bass 0x1C).
        private static readonly (int Channel, string Label)[] LabelDisplayOrder =
        {
            (0x1A, "LC"), (0x16, "CY"), (0x11, "HH"), (0x19, "RD"), (0x14, "HT"),
            (0x15, "LT"), (0x12, "SN"), (0x17, "FT"), (0x13, "BD"), (0x1B, "LP"),
        };

        private static void EngraveTuplet(
            IReadOnlyList<NotationEntry> entries,
            NotationTuplet tuplet,
            List<NotationStripGlyphAnchor> glyphs,
            List<NotationStripRectAnchor> rects)
        {
            // DTXWeb display rule: derive the shown base value from SlotTicks * 3 / 2 before FromTicks,
            // so a 32-tick triplet slot reads as a quarter, not an eighth.
            NoteValue value = NotationGlyphs.FromTicks(tuplet.SlotTicks * 3 / 2);

            var notes = new List<EngravedNote>();
            for (int s = 0; s < tuplet.Count; s++)
            {
                NotationEntry entry = entries[tuplet.StartIndex + s];
                EngraveEntryGlyphs(entry, value, glyphs, rects);
                if (CreateEngravedNote(entry, value) is { } note)
                    notes.Add(note);
            }

            // The whole tuplet is one beaming group, even across rest slots and beat boundaries;
            // quarter/half display values keep natural stems with no beams.
            if (NotationGlyphs.Flag(value) is not null)
            {
                if (notes.Count > 0)
                    EngraveBeamGroup(notes, glyphs, rects);
            }
            else
            {
                foreach (var note in notes)
                    rects.Add(StemRect(note.TimeMs, note.StemDx, note.NaturalTopY, note.StemBottomY));
            }

            // One numeral above the group: bottom edge one staff space above the highest beam/stem top,
            // centered over the first-to-last note span.
            var metrics = NotationGlyphs.GetMetrics(NotationGlyph.Tuplet3);
            float staffSpace = PerformanceUILayout.NotationStrip.StaffSpace;
            float beamTopY = notes.Min(n => n.NaturalTopY);
            float originY = beamTopY - staffSpace + metrics.BBoxSW.Y * staffSpace;
            float midDx = (notes[0].StemDx + notes[^1].StemDx + PerformanceUILayout.NotationStrip.StemThickness) / 2f;
            double midTimeMs = (notes[0].TimeMs + notes[^1].TimeMs) / 2.0;
            glyphs.Add(new NotationStripGlyphAnchor(
                midTimeMs, NotationGlyphs.ToChar(NotationGlyph.Tuplet3), midDx - metrics.Width * staffSpace / 2f, originY));
        }

        /// <summary>
        /// Draws one beaming group: stems up to a shared flat beam top (the group's highest natural
        /// stem top), the primary beam across the group, secondary levels over runs of notes short
        /// enough, partial stubs for isolated notes at a level, or a flag when the group is a single note.
        /// </summary>
        private static void EngraveBeamGroup(
            IReadOnlyList<EngravedNote> notes,
            List<NotationStripGlyphAnchor> glyphs,
            List<NotationStripRectAnchor> rects)
        {
            float beamTopY = notes.Min(n => n.NaturalTopY);
            foreach (var note in notes)
                rects.Add(StemRect(note.TimeMs, note.StemDx, beamTopY, note.StemBottomY));

            if (notes.Count == 1)
            {
                EngraveFlag(notes[0], beamTopY, glyphs);
                return;
            }

            rects.Add(BeamRect(notes[0], notes[^1], beamTopY));

            int maxBeams = notes.Max(n => BeamCount(n.Value));
            for (int level = 2; level <= maxBeams; level++)
            {
                float levelY = beamTopY + (level - 1) * PerformanceUILayout.NotationStrip.BeamLevelSpacing;
                int runStart = -1;
                for (int i = 0; i <= notes.Count; i++)
                {
                    bool inRun = i < notes.Count && BeamCount(notes[i].Value) >= level;
                    if (inRun && runStart < 0)
                        runStart = i;
                    if (inRun || runStart < 0)
                        continue;

                    if (i - runStart >= 2)
                    {
                        rects.Add(BeamRect(notes[runStart], notes[i - 1], levelY));
                    }
                    else
                    {
                        // Partial beam: stub toward a neighbor; right when one follows, else left.
                        EngravedNote stub = notes[runStart];
                        rects.Add(runStart < notes.Count - 1
                            ? new NotationStripRectAnchor(stub.TimeMs, null, stub.StemDx, stub.StemDx + PartialBeamStubPx, levelY, PerformanceUILayout.NotationStrip.BeamThickness)
                            : new NotationStripRectAnchor(stub.TimeMs, null, stub.StemDx - PartialBeamStubPx, stub.StemDx, levelY, PerformanceUILayout.NotationStrip.BeamThickness));
                    }

                    runStart = -1;
                }
            }
        }

        private static void EngraveFlag(EngravedNote note, float stemTopY, List<NotationStripGlyphAnchor> glyphs)
        {
            NotationGlyph? flag = NotationGlyphs.Flag(note.Value);
            if (flag is null)
                return;

            // The flag's StemUpNW anchor coincides with the stem's top-left corner.
            var nw = NotationGlyphs.GetMetrics(flag.Value).StemUpNW.Value;
            glyphs.Add(new NotationStripGlyphAnchor(
                note.TimeMs, NotationGlyphs.ToChar(flag.Value), note.StemDx,
                stemTopY + nw.Y * PerformanceUILayout.NotationStrip.StaffSpace));
        }

        private static void EngraveEntryGlyphs(
            NotationEntry entry,
            NoteValue value,
            List<NotationStripGlyphAnchor> glyphs,
            List<NotationStripRectAnchor> rects)
        {
            if (entry.IsRest)
            {
                glyphs.Add(new NotationStripGlyphAnchor(entry.TimeMs, NotationGlyphs.ToChar(NotationGlyphs.Rest(value)), 0f, RestOriginY(value)));
                return;
            }

            foreach (var head in entry.Heads)
            {
                var glyph = NotationGlyphs.Notehead(head.Position.Shape, value);
                glyphs.Add(new NotationStripGlyphAnchor(entry.TimeMs, NotationGlyphs.ToChar(glyph), 0f, StaffStepToY(head.Position.StaffStep)));
                EngraveLedgerLines(head, glyph, entry.TimeMs, rects);
            }
        }

        /// <summary>
        /// Rest y from Bravura metrics: the whole rest hangs below the D5 line (glyph top on the line),
        /// the half rest sits on the B4 line (glyph bottom on the line), every other rest's origin sits
        /// on the B4 line.
        /// </summary>
        private static float RestOriginY(NoteValue value)
        {
            float staffSpace = PerformanceUILayout.NotationStrip.StaffSpace;
            if (value == NoteValue.Whole)
                return PerformanceUILayout.NotationStrip.D5LineY + NotationGlyphs.GetMetrics(NotationGlyph.RestWhole).BBoxNE.Y * staffSpace;
            if (value == NoteValue.Half)
                return PerformanceUILayout.NotationStrip.B4LineY + NotationGlyphs.GetMetrics(NotationGlyph.RestHalf).BBoxSW.Y * staffSpace;
            return PerformanceUILayout.NotationStrip.B4LineY;
        }

        private static void EngraveLedgerLines(
            NotationHead head,
            NotationGlyph glyph,
            double timeMs,
            List<NotationStripRectAnchor> rects)
        {
            int step = head.Position.StaffStep;
            if (step is >= 0 and <= 8)
                return;

            // Ledger lines sit on even staff steps between the staff and the head.
            IEnumerable<int> ledgerSteps = step > 8
                ? StepRange(10, step, 2)
                : StepRange(-2, step, -2);

            var metrics = NotationGlyphs.GetMetrics(glyph);
            float centerDx = metrics.Width * PerformanceUILayout.NotationStrip.StaffSpace / 2f;
            float halfWidth = LedgerLineWidthPx / 2f;
            foreach (int ledgerStep in ledgerSteps)
            {
                rects.Add(new NotationStripRectAnchor(
                    timeMs, null, centerDx - halfWidth, centerDx + halfWidth,
                    StaffStepToY(ledgerStep), PerformanceUILayout.NotationStrip.StaffLineThickness));
            }
        }

        private static IEnumerable<int> StepRange(int first, int last, int step)
        {
            for (int s = first; step > 0 ? s <= last : s >= last; s += step)
                yield return s;
        }

        private static EngravedNote? CreateEngravedNote(NotationEntry entry, NoteValue value)
        {
            // Rests and whole notes carry no stem.
            if (entry.IsRest || value == NoteValue.Whole)
                return null;

            var lowest = entry.Heads.MinBy(h => h.Position.StaffStep);
            int highest = entry.Heads.Max(h => h.Position.StaffStep);
            var se = NotationGlyphs.GetMetrics(NotationGlyphs.Notehead(lowest.Position.Shape, value)).StemUpSE
                ?? throw new InvalidOperationException("Non-whole noteheads always expose a StemUpSE anchor.");

            float staffSpace = PerformanceUILayout.NotationStrip.StaffSpace;
            return new EngravedNote(
                entry.TimeMs,
                value,
                se.X * staffSpace,
                StaffStepToY(lowest.Position.StaffStep) - se.Y * staffSpace,
                StaffStepToY(highest + 7));
        }

        private static NotationStripRectAnchor StemRect(double timeMs, float dx, float topY, float bottomY)
        {
            return new NotationStripRectAnchor(
                timeMs, null, dx, dx + PerformanceUILayout.NotationStrip.StemThickness,
                topY, bottomY - topY);
        }

        private static NotationStripRectAnchor BeamRect(EngravedNote first, EngravedNote last, float topY)
        {
            return new NotationStripRectAnchor(
                first.TimeMs, last.TimeMs, first.StemDx, last.StemDx + PerformanceUILayout.NotationStrip.StemThickness,
                topY, PerformanceUILayout.NotationStrip.BeamThickness);
        }

        private static int BeamCount(NoteValue value) => value switch
        {
            NoteValue.Eighth => 1,
            NoteValue.Sixteenth => 2,
            NoteValue.ThirtySecond => 3,
            NoteValue.SixtyFourth => 4,
            _ => 0,
        };
    }

    /// <summary>
    /// Half-open index range [FirstIndex, LastIndexInclusive] of selected measures; empty when
    /// LastIndexInclusive &lt; FirstIndex.
    /// </summary>
    public readonly record struct NotationMeasureWindow(int FirstIndex, int LastIndexInclusive);

    /// <summary>
    /// One engraving placement in strip space, independent of nowMs and scroll speed.
    /// X is derived from TimeMs at draw time via <see cref="NotationStripLayout.ToScreenX"/>;
    /// DxPx/YPx are pixel offsets in strip space. A primitive spanning time (e.g. a beam across
    /// two notes) carries both endpoint times.
    /// </summary>
    public readonly record struct NotationStripAnchor(double TimeMs, double? EndTimeMs, float DxPx, float YPx)
    {
        public NotationStripAnchor(double timeMs, float dxPx, float yPx)
            : this(timeMs, null, dxPx, yPx)
        {
        }
    }

    /// <summary>
    /// One SMuFL glyph placement (notehead, rest, flag, tuplet numeral). At draw time the glyph
    /// origin renders at (ToScreenX(TimeMs) + DxPx, YPx); Bravura metrics place the ink relative
    /// to that origin. Glyphs render at the DepthGlyphs layer.
    /// </summary>
    internal readonly record struct NotationStripGlyphAnchor(double TimeMs, char Glyph, float DxPx, float YPx);

    /// <summary>
    /// One filled strip-space rectangle (stem, beam, ledger line). Left edge at
    /// ToScreenX(TimeMs) + StartDxPx; when EndTimeMs is set (spanning beams) the right edge is
    /// ToScreenX(EndTimeMs) + EndDxPx, otherwise EndDxPx - StartDxPx is the width. TopPx/HeightPx
    /// are absolute strip-space pixels. Rects render at the DepthBeamsAndStems layer.
    /// </summary>
    internal readonly record struct NotationStripRectAnchor(
        double TimeMs,
        double? EndTimeMs,
        float StartDxPx,
        float EndDxPx,
        float TopPx,
        float HeightPx);

    /// <summary>Key-column instrument label; the renderer right-aligns Text at LabelRightX.</summary>
    internal readonly record struct NotationStripLabelAnchor(string Text, float YPx);

    /// <summary>Engraving for one measure: glyph placements plus filled rectangles.</summary>
    internal sealed record NotationStripMeasureEngraving(
        IReadOnlyList<NotationStripGlyphAnchor> Glyphs,
        IReadOnlyList<NotationStripRectAnchor> Rects);

    /// <summary>Stem/beam geometry for one chord, computed once per entry.</summary>
    internal readonly record struct EngravedNote(
        double TimeMs,
        NoteValue Value,
        float StemDx,
        float StemBottomY,
        float NaturalTopY);
}
