#nullable enable

using System;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Pure hold/skip/stale selection tests. The selector has no GPU, FFmpeg,
    /// or filesystem dependency, so these run in both test projects.
    ///
    /// The caller (player Update pass) supplies the due-head summary of the
    /// queue: how many queued frames are due (timestamp at or before the
    /// target) and the newest due timestamp. These tests pin the decision
    /// contract; the player tests pin the channel pass that feeds it.
    /// </summary>
    [Trait("Category", "Unit")]
    public class VideoFrameSelectorTests
    {
        private const double IntervalMs = 100.0;

        private static VideoFrameSelection Select(
            double targetMediaTimeMs,
            double? currentFrameTimestampMs,
            int dueFrameCount,
            double? newestDueFrameTimestampMs)
        {
            return VideoFrameSelector.Select(
                targetMediaTimeMs,
                IntervalMs,
                currentFrameTimestampMs,
                dueFrameCount,
                newestDueFrameTimestampMs);
        }

        [Fact]
        public void Select_WithNoCurrentAndNoQueued_ShouldReturnNoFrame()
        {
            var selection = Select(500, currentFrameTimestampMs: null, 0, null);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_AtMediaZero_ShouldConsumeZeroOriginFrame()
        {
            // Queued 0,100,200; only frame 0 is due at media zero.
            var selection = Select(0, currentFrameTimestampMs: null, 1, 0);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(1, selection.ConsumeCount);
        }

        [Fact]
        public void Select_SlowProgression_ShouldConsumeThroughLatestDueFrame()
        {
            // Decoder delivered 200..400 while presentation is at 250: frame
            // 200 is due, 300/400 are future.
            var selection = Select(250, currentFrameTimestampMs: 100, 1, 200);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(1, selection.ConsumeCount);
        }

        [Fact]
        public void Select_JumpAhead_ShouldSkipObsoleteIntermediates()
        {
            // A 150%-style jump: 300/400/500 are due, only the latest is
            // displayed.
            var selection = Select(550, currentFrameTimestampMs: 100, 3, 500);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(3, selection.ConsumeCount);
        }

        [Fact]
        public void Select_UpdateHitchWithinTolerance_ShouldConsumeThroughLatestDueQueuedFrame()
        {
            // Hitch to 880 with 600/700/800 queued: the newest due frame is
            // only 80ms behind the target, within one frame interval.
            var selection = Select(880, currentFrameTimestampMs: 500, 3, 800);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(3, selection.ConsumeCount);
        }

        [Fact]
        public void Select_UpdateHitchBeyondTolerance_ShouldConsumeObsoleteFramesAndReturnNoFrame()
        {
            // Hitch to 1000 with 600/700/800 queued: even the newest due frame
            // is 200ms stale (beyond one interval), so nothing drawable
            // remains while the obsolete frames are still consumed so the
            // decoder advances.
            var selection = Select(1000, currentFrameTimestampMs: 500, 3, 800);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(3, selection.ConsumeCount);
        }

        [Fact]
        public void Select_AsyncStartCatchUpWithoutDueFrame_ShouldReturnNoFrame()
        {
            // Startup decode is beyond the target and no frame was shown yet:
            // nothing timely exists, so Draw renders nothing.
            var selection = Select(250, currentFrameTimestampMs: null, 0, null);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_AsyncStartCatchUpWithDueFramesBeyondTolerance_ShouldConsumeAndReturnNoFrame()
        {
            // Startup delivered 300..500 while logical media time already ran
            // to 10000: the newest due frame is materially stale, so the
            // static background stays visible, but all three obsolete frames
            // are consumed so the decoder advances.
            var selection = Select(10_000, currentFrameTimestampMs: null, 3, 500);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(3, selection.ConsumeCount);
        }

        [Fact]
        public void Select_CatchUpWithinTolerance_ShouldAdvanceToDueFrame()
        {
            // Catch-up continuation: the decoder has delivered frames near the
            // target again, so a drawable frame exists once more.
            var selection = Select(10_450, currentFrameTimestampMs: null, 1, 10_400);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(1, selection.ConsumeCount);
        }

        [Fact]
        public void Select_NewestDueFrameAtExactTolerance_ShouldAdvance()
        {
            // The newest due frame exactly one interval behind the target is
            // still drawable.
            var selection = Select(600, currentFrameTimestampMs: null, 1, 500);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
        }

        [Fact]
        public void Select_FreshCurrentAndOnlyFutureQueued_ShouldHoldCurrent()
        {
            var selection = Select(150, currentFrameTimestampMs: 100, 0, null);

            Assert.Equal(VideoFrameSelectionKind.HoldCurrent, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_CurrentWithinFrameIntervalTolerance_ShouldHoldCurrent()
        {
            var selection = Select(180, currentFrameTimestampMs: 100, 0, null);

            Assert.Equal(VideoFrameSelectionKind.HoldCurrent, selection.Kind);
        }

        [Fact]
        public void Select_AtExactStaleTolerance_ShouldHoldCurrent()
        {
            var selection = Select(200, currentFrameTimestampMs: 100, 0, null);

            Assert.Equal(VideoFrameSelectionKind.HoldCurrent, selection.Kind);
        }

        [Fact]
        public void Select_BeyondStaleTolerance_ShouldReturnNoFrame()
        {
            // Queue is empty and the decoder fell behind by more than one
            // frame interval: the stale frame must not stay on screen.
            var selection = Select(201, currentFrameTimestampMs: 100, 0, null);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_WithDegenerateFrameInterval_ShouldReturnNoFrame()
        {
            var selection = VideoFrameSelector.Select(
                500, frameIntervalMs: 0, currentFrameTimestampMs: null,
                dueFrameCount: 1, newestDueFrameTimestampMs: 100);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
        }
    }
}
