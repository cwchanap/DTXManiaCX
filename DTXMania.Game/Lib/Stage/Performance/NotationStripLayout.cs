using System;
using System.Collections.Generic;
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
}
