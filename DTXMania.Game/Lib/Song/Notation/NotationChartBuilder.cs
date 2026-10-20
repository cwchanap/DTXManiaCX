using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Song.Notation
{
    /// <summary>
    /// Builds drum notation from a finalized <see cref="ParsedChart"/>. Source notes are referenced, never copied,
    /// so <see cref="Note.Id"/> is read after <see cref="ChartManager"/> assigns it. Gameplay timing is untouched.
    /// </summary>
    /// <remarks>
    /// Notation onsets come from the authored slot fraction (<see cref="Note.PairIndex"/> over <see cref="Note.PairCount"/>),
    /// never from the truncated <see cref="Note.Tick"/>. Times come from source notes, or for rests from the canonical
    /// position that <see cref="ChartTimingMap"/> expects, so the bar multiplier is applied once.
    /// The chart must have been finalized after its last edit. Otherwise its times and horizon are stale.
    /// </remarks>
    internal static class NotationChartBuilder
    {
        /// <summary>
        /// Builds notation for every compiled bar, including rest-only bars. Returns an empty chart when the chart has no compiled timing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A note's channel has no drum staff position.</exception>
        public static NotationChart Build(ParsedChart chart)
        {
            ArgumentNullException.ThrowIfNull(chart);

            // MeasureLines ends with a terminal boundary, and FinalizeChart leaves none when there is no timing.
            int measureCount = chart.MeasureLines.Count - 1;
            if (measureCount < 1)
            {
                return new NotationChart(Array.Empty<NotationMeasure>());
            }

            var measureTicks = new int[measureCount];
            var onsetsByBar = new List<NotationOnset>[measureCount];
            for (int bar = 0; bar < measureCount; bar++)
            {
                measureTicks[bar] = MeasureTicksFor(chart.TimingMap.GetMeasureLengthMultiplier(bar));
                onsetsByBar[bar] = new List<NotationOnset>();
            }

            foreach (Note note in chart.Notes)
            {
                DrumStaffPosition position = DrumStaffMapping.GetPosition(note.Channel);
                (int bar, int tick) = ChartTimingMap.NormalizePosition(note.Bar, note.Tick);
                int onset = OnsetTick(note, tick, measureTicks[bar]);
                onsetsByBar[bar].Add(new NotationOnset(onset, position, note));
            }

            var measures = new NotationMeasure[measureCount];
            for (int bar = 0; bar < measureCount; bar++)
            {
                NotationMeasure quantized = NotationQuantizer.QuantizeMeasure(bar, measureTicks[bar], onsetsByBar[bar]);
                measures[bar] = WithTimes(chart.TimingMap, quantized);
            }

            return new NotationChart(measures);
        }

        // Round(192 * effective multiplier), at least one tick so a degenerate multiplier still yields a bar.
        // ponytail: no cap on measureTicks. The real ceiling is rest explosion, not int overflow: a carried-forward
        // #00002: 100000 makes ~100k rest entries per bar. Cap or guard measureTicks before wiring at chart load (HPA-411).
        private static int MeasureTicksFor(double multiplier) =>
            Math.Max(1, (int)Math.Round(ChartTimingMap.TicksPerMeasure * multiplier, MidpointRounding.AwayFromZero));

        // Parsed notes use their authored slot fraction. Hand-built notes use canonical tick / 192. The multiply comes
        // before the divide, so the quotient is one correctly rounded double and exact half-ticks round up as written.
        // The clamp keeps a position that rounds up to the bar end on the last tick, so the note is kept.
        private static int OnsetTick(Note note, int canonicalTick, int measureTicks)
        {
            double position = note.PairCount > 0
                ? (double)note.PairIndex * measureTicks / note.PairCount
                : (double)canonicalTick * measureTicks / ChartTimingMap.TicksPerMeasure;

            return (int)Math.Clamp(Math.Round(position, MidpointRounding.AwayFromZero), 0, measureTicks - 1);
        }

        // A source note takes the earliest TimeMs of its chord, which is never the scaled notation tick.
        // A rest has no source note, so its canonical start goes through the timing map, which applies the multiplier.
        private static NotationMeasure WithTimes(ChartTimingMap timing, NotationMeasure measure)
        {
            var entries = new NotationEntry[measure.Entries.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                NotationEntry entry = measure.Entries[i];
                double timeMs = entry.IsRest
                    ? timing.CalculateTimeMs(measure.Index, entry.StartTick * (double)ChartTimingMap.TicksPerMeasure / measure.MeasureTicks)
                    : entry.Heads.SelectMany(head => head.Notes).Min(note => note.TimeMs);
                entries[i] = entry with { TimeMs = timeMs };
            }

            return new NotationMeasure(measure.Index, measure.MeasureTicks, entries, measure.Tuplets);
        }
    }
}
