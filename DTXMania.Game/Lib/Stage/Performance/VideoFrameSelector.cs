#nullable enable

using System;

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
        /// <param name="dueFrameCount">Number of queued frames with timestamp at or
        /// before the target time (the caller consumes them either way).</param>
        /// <param name="newestDueFrameTimestampMs">Timestamp of the newest due
        /// queued frame, or null when <paramref name="dueFrameCount"/> is 0.</param>
        public static VideoFrameSelection Select(
            double targetMediaTimeMs,
            double frameIntervalMs,
            double? currentFrameTimestampMs,
            int dueFrameCount,
            double? newestDueFrameTimestampMs)
        {
            if (frameIntervalMs <= 0)
            {
                return new VideoFrameSelection(VideoFrameSelectionKind.NoFrame, 0);
            }

            if (dueFrameCount > 0)
            {
                // Every due frame is consumed so the decoder keeps advancing.
                // Display the newest due frame only while it is within the
                // stale tolerance; when it is still materially behind the
                // target (async startup catch-up or a decoder that fell
                // behind), report NoFrame so the static background stays
                // visible instead of a fast-forwarding stale frame.
                if (targetMediaTimeMs - newestDueFrameTimestampMs!.Value <= frameIntervalMs)
                {
                    return new VideoFrameSelection(VideoFrameSelectionKind.Advance, dueFrameCount);
                }

                return new VideoFrameSelection(VideoFrameSelectionKind.NoFrame, dueFrameCount);
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
