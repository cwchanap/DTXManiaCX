#nullable enable

using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// What the game thread should do with decoded video frames for this update.
    /// </summary>
    public enum VideoFrameSelectionKind
    {
        /// <summary>No timely frame exists; Draw must render nothing.</summary>
        NoFrame,

        /// <summary>Keep displaying the current frame.</summary>
        HoldCurrent,

        /// <summary>Consume <see cref="VideoFrameSelection.ConsumeCount"/> queued
        /// frames, displaying only the last one (obsolete intermediates are skipped).</summary>
        Advance,
    }

    /// <summary>
    /// Result of a pure frame selection pass.
    /// </summary>
    public readonly record struct VideoFrameSelection(
        VideoFrameSelectionKind Kind,
        int ConsumeCount);

    /// <summary>
    /// Pure hold/skip/stale frame selector for chart background video playback.
    /// No GPU, FFmpeg, or filesystem dependency.
    ///
    /// Timestamp origin is media zero: decoded frames are stamped
    /// <c>frameTimeMs = frameIndex * frameIntervalMs</c>, never offset by
    /// async startup time. A current frame is held while it is within one
    /// frame interval of the target media time; beyond that stale tolerance
    /// (empty queue, decoder behind) selection reports NoFrame so the static
    /// background stays visible until the decoder catches up.
    /// </summary>
    public static class VideoFrameSelector
    {
        /// <summary>
        /// Selects what to display at <paramref name="targetMediaTimeMs"/>.
        /// </summary>
        /// <param name="targetMediaTimeMs">Current media time from generation zero.</param>
        /// <param name="frameIntervalMs">Constant frame interval in milliseconds.</param>
        /// <param name="currentFrameTimestampMs">Timestamp of the displayed frame, if any.</param>
        /// <param name="queuedFrameTimestampMs">Timestamps of queued frames, oldest first.</param>
        public static VideoFrameSelection Select(
            double targetMediaTimeMs,
            double frameIntervalMs,
            double? currentFrameTimestampMs,
            IReadOnlyList<double> queuedFrameTimestampMs)
        {
            if (frameIntervalMs <= 0)
            {
                return new VideoFrameSelection(VideoFrameSelectionKind.NoFrame, 0);
            }

            // Consume through the latest due queued frame; intermediate frames
            // are obsolete and skipped without display.
            int dueCount = 0;
            for (int index = 0; index < queuedFrameTimestampMs.Count; index++)
            {
                if (queuedFrameTimestampMs[index] <= targetMediaTimeMs)
                {
                    dueCount++;
                }
                else
                {
                    break;
                }
            }

            if (dueCount > 0)
            {
                return new VideoFrameSelection(VideoFrameSelectionKind.Advance, dueCount);
            }

            if (currentFrameTimestampMs.HasValue &&
                targetMediaTimeMs - currentFrameTimestampMs.Value <= frameIntervalMs)
            {
                return new VideoFrameSelection(VideoFrameSelectionKind.HoldCurrent, 0);
            }

            // Nothing queued is due and either nothing is displayed yet
            // (async-start catch-up) or the current frame is stale beyond the
            // small frame-interval tolerance.
            return new VideoFrameSelection(VideoFrameSelectionKind.NoFrame, 0);
        }
    }
}
