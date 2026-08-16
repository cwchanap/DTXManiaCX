using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Song.Components
{
    internal sealed class ChartTimingMap
    {
        internal const int TicksPerMeasure = 192;

        private readonly Dictionary<int, double> _measureLengths = new();
        private readonly Dictionary<(int Bar, int Tick), double> _tempoChanges = new();
        private readonly List<TimingAnchor> _anchors = new();

        private int _throughBar = -1;

        internal static (int Bar, int Tick) NormalizePosition(int bar, int tick)
        {
            if (bar < 0)
                throw new ArgumentOutOfRangeException(nameof(bar));
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));

            return (
                bar + (tick / TicksPerMeasure),
                tick % TicksPerMeasure);
        }

        internal void SetMeasureLength(int bar, double multiplier)
        {
            if (bar < 0 || !double.IsFinite(multiplier) || !(multiplier > 0))
                return;

            _measureLengths[bar] = multiplier;
            InvalidateAnchors();
        }

        internal void SetTempoChange(int bar, int tick, double bpm)
        {
            if (bar < 0 || tick < 0 ||
                !double.IsFinite(bpm) || !(bpm > 0))
                return;

            var (normalizedBar, normalizedTick) = NormalizePosition(bar, tick);
            _tempoChanges[(normalizedBar, normalizedTick)] = bpm;
            InvalidateAnchors();
        }

        private void InvalidateAnchors()
        {
            _anchors.Clear();
            _throughBar = -1;
        }

        internal void Rebuild(double baseBpm, int throughBar)
        {
            if (!double.IsFinite(baseBpm) || !(baseBpm > 0))
                throw new ArgumentException(
                    "BPM must be greater than 0",
                    nameof(baseBpm));
            if (throughBar < 0)
                throw new ArgumentOutOfRangeException(nameof(throughBar));

            var tempoChanges = _tempoChanges
                .OrderBy(change => change.Key.Bar)
                .ThenBy(change => change.Key.Tick)
                .ToArray();

            _anchors.Clear();

            var tempoChangeIndex = 0;
            var currentTimeMs = 0.0;
            var currentBpm = baseBpm;

            for (var bar = 0; bar <= throughBar; bar++)
            {
                var measureLengthMultiplier = _measureLengths.TryGetValue(
                    bar,
                    out var configuredMultiplier)
                    ? configuredMultiplier
                    : 1.0;

                _anchors.Add(new TimingAnchor(
                    bar,
                    0,
                    currentTimeMs,
                    currentBpm,
                    measureLengthMultiplier));

                var cursorTick = 0;
                while (tempoChangeIndex < tempoChanges.Length &&
                       tempoChanges[tempoChangeIndex].Key.Bar == bar)
                {
                    var change = tempoChanges[tempoChangeIndex];
                    var tick = change.Key.Tick;

                    if (tick == 0)
                    {
                        currentBpm = change.Value;
                        _anchors[^1] = new TimingAnchor(
                            bar,
                            0,
                            currentTimeMs,
                            currentBpm,
                            measureLengthMultiplier);
                    }
                    else
                    {
                        currentTimeMs += CalculateIntervalMs(
                            tick - cursorTick,
                            measureLengthMultiplier,
                            currentBpm);

                        currentBpm = change.Value;
                        _anchors.Add(new TimingAnchor(
                            bar,
                            tick,
                            currentTimeMs,
                            currentBpm,
                            measureLengthMultiplier));
                    }

                    cursorTick = tick;
                    tempoChangeIndex++;
                }

                currentTimeMs += CalculateIntervalMs(
                    TicksPerMeasure - cursorTick,
                    measureLengthMultiplier,
                    currentBpm);
            }

            _throughBar = throughBar;
        }

        internal double CalculateTimeMs(int bar, double tick)
        {
            var position = NormalizeFractionalPosition(bar, tick);
            if (position.Bar > _throughBar)
                throw new ArgumentOutOfRangeException(
                    nameof(bar),
                    "The requested position is beyond the compiled timing map.");

            var anchor = _anchors[
                FindAnchorIndex(position.Bar, (int)Math.Floor(position.Tick))];
            var tickDelta =
                ((position.Bar - anchor.Bar) * (double)TicksPerMeasure) +
                (position.Tick - anchor.Tick);

            return anchor.TimeMs + CalculateIntervalMs(
                tickDelta,
                anchor.MeasureLengthMultiplier,
                anchor.Bpm);
        }

        internal double GetMeasureLengthMultiplier(int bar)
        {
            if (bar < 0 || bar > _throughBar)
                throw new ArgumentOutOfRangeException(nameof(bar));

            return _anchors[FindAnchorIndex(bar, 0)].MeasureLengthMultiplier;
        }

        private static (int Bar, double Tick) NormalizeFractionalPosition(
            int bar,
            double tick)
        {
            if (bar < 0)
                throw new ArgumentOutOfRangeException(nameof(bar));
            if (!double.IsFinite(tick) || tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));

            var measureOffset = Math.Floor(tick / TicksPerMeasure);
            if (measureOffset > int.MaxValue - bar)
                throw new ArgumentOutOfRangeException(nameof(tick));

            var normalizedTick = tick - (measureOffset * TicksPerMeasure);
            if (normalizedTick >= TicksPerMeasure)
            {
                if (measureOffset >= int.MaxValue - bar)
                    throw new ArgumentOutOfRangeException(nameof(tick));

                measureOffset++;
                normalizedTick -= TicksPerMeasure;
            }
            else if (normalizedTick < 0)
            {
                measureOffset--;
                normalizedTick += TicksPerMeasure;
            }

            return ((int)(bar + measureOffset), normalizedTick);
        }

        private static double CalculateIntervalMs(
            double tickDelta,
            double measureLengthMultiplier,
            double bpm)
        {
            var beats =
                (tickDelta / (double)TicksPerMeasure) *
                4.0 *
                measureLengthMultiplier;

            return beats * (60000.0 / bpm);
        }

        private int FindAnchorIndex(int bar, int tick)
        {
            var low = 0;
            var high = _anchors.Count - 1;
            var result = 0;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var anchor = _anchors[middle];

                if (anchor.Bar < bar ||
                    (anchor.Bar == bar && anchor.Tick <= tick))
                {
                    result = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return result;
        }

        private sealed class TimingAnchor
        {
            internal TimingAnchor(
                int bar,
                int tick,
                double timeMs,
                double bpm,
                double measureLengthMultiplier)
            {
                Bar = bar;
                Tick = tick;
                TimeMs = timeMs;
                Bpm = bpm;
                MeasureLengthMultiplier = measureLengthMultiplier;
            }

            internal int Bar { get; }
            internal int Tick { get; }
            internal double TimeMs { get; }
            internal double Bpm { get; }
            internal double MeasureLengthMultiplier { get; }
        }
    }
}
