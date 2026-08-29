#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Pure hold/skip/stale selection tests. The selector has no GPU, FFmpeg,
    /// or filesystem dependency, so these run in both test projects.
    /// </summary>
    [Trait("Category", "Unit")]
    public class VideoFrameSelectorTests
    {
        private const double IntervalMs = 100.0;

        private static VideoFrameSelection Select(
            double targetMediaTimeMs,
            double? currentFrameTimestampMs,
            params double[] queuedFrameTimestamps)
        {
            return VideoFrameSelector.Select(
                targetMediaTimeMs,
                IntervalMs,
                currentFrameTimestampMs,
                queuedFrameTimestamps);
        }

        [Fact]
        public void Select_WithNoCurrentAndNoQueued_ShouldReturnNoFrame()
        {
            var selection = Select(500, currentFrameTimestampMs: null);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_AtMediaZero_ShouldConsumeZeroOriginFrame()
        {
            var selection = Select(0, currentFrameTimestampMs: null, 0, 100, 200);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(1, selection.ConsumeCount);
        }

        [Fact]
        public void Select_SlowProgression_ShouldConsumeThroughLatestDueFrame()
        {
            // Decoder delivered 200..400 while presentation is at 250.
            var selection = Select(250, currentFrameTimestampMs: 100, 200, 300, 400);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(1, selection.ConsumeCount);
        }

        [Fact]
        public void Select_JumpAhead_ShouldSkipObsoleteIntermediates()
        {
            // A 150%-style jump: only the latest due frame is displayed.
            var selection = Select(550, currentFrameTimestampMs: 100, 300, 400, 500, 600);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(3, selection.ConsumeCount);
        }

        [Fact]
        public void Select_UpdateHitch_ShouldConsumeThroughLatestDueQueuedFrame()
        {
            var selection = Select(1000, currentFrameTimestampMs: 500, 600, 700, 800);

            Assert.Equal(VideoFrameSelectionKind.Advance, selection.Kind);
            Assert.Equal(3, selection.ConsumeCount);
        }

        [Fact]
        public void Select_AsyncStartCatchUpWithoutDueFrame_ShouldReturnNoFrame()
        {
            // Startup decode is beyond the target and no frame was shown yet:
            // nothing timely exists, so Draw renders nothing.
            var selection = Select(250, currentFrameTimestampMs: null, 300, 400, 500);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_FreshCurrentAndOnlyFutureQueued_ShouldHoldCurrent()
        {
            var selection = Select(150, currentFrameTimestampMs: 100, 300, 400);

            Assert.Equal(VideoFrameSelectionKind.HoldCurrent, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_CurrentWithinFrameIntervalTolerance_ShouldHoldCurrent()
        {
            var selection = Select(180, currentFrameTimestampMs: 100);

            Assert.Equal(VideoFrameSelectionKind.HoldCurrent, selection.Kind);
        }

        [Fact]
        public void Select_AtExactStaleTolerance_ShouldHoldCurrent()
        {
            var selection = Select(200, currentFrameTimestampMs: 100);

            Assert.Equal(VideoFrameSelectionKind.HoldCurrent, selection.Kind);
        }

        [Fact]
        public void Select_BeyondStaleTolerance_ShouldReturnNoFrame()
        {
            // Queue is empty and the decoder fell behind by more than one
            // frame interval: the stale frame must not stay on screen.
            var selection = Select(201, currentFrameTimestampMs: 100);

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
            Assert.Equal(0, selection.ConsumeCount);
        }

        [Fact]
        public void Select_WithDegenerateFrameInterval_ShouldReturnNoFrame()
        {
            var selection = VideoFrameSelector.Select(
                500, frameIntervalMs: 0, currentFrameTimestampMs: null,
                new List<double> { 0, 100 });

            Assert.Equal(VideoFrameSelectionKind.NoFrame, selection.Kind);
        }
    }
}
