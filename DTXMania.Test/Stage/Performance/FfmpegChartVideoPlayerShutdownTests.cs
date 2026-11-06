#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public sealed class FfmpegChartVideoPlayerShutdownTests
    {
        public static IEnumerable<object[]> CallbackFailures()
        {
            // FFMpegCore's own stdin callback is contained at registration in
            // RunGenerationAsync, so every callback landing in Stop()'s catch
            // is a foreign one — an IOException-shaped failure and anything
            // else alike. CancellationTokenSource.Cancel runs every callback
            // and rethrows the collected failures as an AggregateException;
            // Stop must contain and log all of them.
            yield return new object[] { new IOException("The pipe is being closed.") };
            yield return new object[] { new AggregateException(new IOException("Closed pipe")) };
            yield return new object[] { new InvalidOperationException("Unexpected callback failure") };
            yield return new object[] { new ObjectDisposedException("Unexpected disposed object") };
            yield return new object[]
            {
                new AggregateException(new IOException("Closed pipe"),
                    new InvalidOperationException("Unexpected callback failure"))
            };
            yield return new object[] { new AggregateException() };
        }

        [Theory]
        [MemberData(nameof(CallbackFailures))]
        public void Stop_WhenCancellationCallbackThrows_ShouldContainLogAndCleanUp(Exception callbackFailure)
        {
            var logs = new List<string>();
            var surface = new FakeVideoFrameSurface();
            using var player = CreatePlayer(surface, logs.Add);
            var cts = StartWithPresentedFrame(player);
            var token = cts.Token;
            var remainingCallbackRan = false;
            using var remainingRegistration = token.Register(() => remainingCallbackRan = true);
            using var registration = token.Register(() => throw callbackFailure);
            var resetCount = surface.ResetCount;

            var exception = Record.Exception(player.Stop);

            Assert.Null(exception);
            Assert.True(remainingCallbackRan);
            Assert.Contains(logs, message => message.Contains("shutdown callback failed"));
            AssertStopped(player, cts, token, surface, resetCount);
            player.Stop();
            Assert.False(player.HasCurrentFrame);
        }

        [Fact]
        public async Task Stop_WhenProducerIsBlockedAndCallbackHasIoFailure_ShouldReleaseProducer()
        {
            var surface = new FakeVideoFrameSurface();
            using var player = CreatePlayer(surface);
            var cts = StartWithPresentedFrame(player);
            var token = cts.Token;
            var queue = Channel.CreateBounded<VideoFrame>(FfmpegChartVideoPlayer.QueueCapacity);
            // Production order: FFMpegCore's shutdown callback registers on the
            // generation token first; the sink's linked source registers later,
            // when ReadAsync starts. Cancel() invokes callbacks newest-first,
            // so the sink wakes before the failing callback runs, and Stop
            // must still contain the thrown failure and finish cleanup.
            using var registration = token.Register(() => throw new IOException("Closed pipe"));
            var sink = new VideoFramePipeSink(1, 1, 100, queue.Writer, token);
            using var input = new MemoryStream(new byte[10 * 4]);
            var producer = sink.ReadAsync(input, CancellationToken.None);
            Assert.Equal(FfmpegChartVideoPlayer.QueueCapacity, queue.Reader.Count);
            Assert.False(producer.IsCompleted);
            var resetCount = surface.ResetCount;

            player.Stop();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => producer.WaitAsync(TimeSpan.FromSeconds(10)));
            AssertStopped(player, cts, token, surface, resetCount);
        }

        [Fact]
        public void Start_WhenPriorCancellationCallbackHasIoFailure_ShouldStartNewGeneration()
        {
            var surface = new FakeVideoFrameSurface();
            var scheduledCount = 0;
            using var player = new FfmpegChartVideoPlayer(
                _ => { scheduledCount++; return Task.CompletedTask; }, surface, log: null);
            var priorCts = StartWithPresentedFrame(player);
            var priorToken = priorCts.Token;
            using var registration = priorToken.Register(() => throw new IOException("Closed pipe"));
            var resetCount = surface.ResetCount;

            player.Start("next-generation.avi");

            Assert.Equal(2, scheduledCount);
            Assert.True(priorToken.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() => priorCts.Token);
            var currentCts = player.GenerationCts;
            Assert.NotNull(currentCts);
            Assert.NotSame(priorCts, currentCts);
            Assert.False(currentCts!.IsCancellationRequested);
            Assert.False(player.HasCurrentFrame);
            Assert.Null(player.CurrentFrameTimestampMs);
            Assert.Equal(resetCount + 1, surface.ResetCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Dispose_WhenCancellationCallbackThrows_ShouldContainDisposeSurfaceAndRemainIdempotent(
            bool unexpectedFailure)
        {
            var surface = new FakeVideoFrameSurface();
            var player = CreatePlayer(surface);
            var cts = StartWithPresentedFrame(player);
            var token = cts.Token;
            Exception callbackFailure = unexpectedFailure
                ? new InvalidOperationException("Unexpected callback failure")
                : new IOException("Closed pipe");
            using var registration = token.Register(() => throw callbackFailure);
            var resetCount = surface.ResetCount;

            var exception = Record.Exception(player.Dispose);

            Assert.Null(exception);
            AssertStopped(player, cts, token, surface, resetCount);
            Assert.Equal(1, surface.DisposeCount);
            player.Dispose();
            player.Stop();
            Assert.Equal(1, surface.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => player.Start("after-disposal.avi"));
        }

        [Fact]
        public void InvokeFfmpegShutdown_ShouldDriveCancelAndContainClosedPipeFailure()
        {
            var logs = new List<string>();
            var surface = new FakeVideoFrameSurface();
            using var player = CreatePlayer(surface, logs.Add);

            var ran = false;
            player.InvokeFfmpegShutdown(() => ran = true);
            Assert.True(ran);
            Assert.Empty(logs);

            player.InvokeFfmpegShutdown(() => throw new IOException("The pipe is being closed."));

            var message = Assert.Single(logs);
            Assert.Contains("shutdown request failed", message);
        }

        private static FfmpegChartVideoPlayer CreatePlayer(
            FakeVideoFrameSurface surface,
            Action<string>? log = null) =>
            new(_ => Task.CompletedTask, surface, log);

        private static CancellationTokenSource StartWithPresentedFrame(FfmpegChartVideoPlayer player)
        {
            // Defer startup so no FFmpeg process or timing race is needed.
            // Inject a callback on the real generation CTS, exactly where
            // FFMpegCore registers its shutdown callback.
            player.Start("deferred-generation.avi");
            var queue = Channel.CreateBounded<VideoFrame>(FfmpegChartVideoPlayer.QueueCapacity);
            queue.Writer.TryWrite(new VideoFrame(0, new byte[64 * 48 * 4]));
            queue.Writer.TryWrite(new VideoFrame(100, new byte[64 * 48 * 4]));
            player.ActiveGeneration = new FfmpegChartVideoPlayer.GenerationState(
                queue.Reader, 64, 48, 100d);
            player.Update(0);
            Assert.True(player.HasCurrentFrame);
            Assert.Equal(0, player.CurrentFrameTimestampMs);
            Assert.Equal(1, player.QueuedFrameCount);
            return player.GenerationCts!;
        }

        private static void AssertStopped(
            FfmpegChartVideoPlayer player,
            CancellationTokenSource cts,
            CancellationToken token,
            FakeVideoFrameSurface surface,
            int priorResetCount)
        {
            Assert.True(token.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() => cts.Token);
            Assert.Null(player.GenerationCts);
            Assert.Null(player.ActiveGeneration);
            Assert.Equal(0, player.QueuedFrameCount);
            Assert.False(player.HasCurrentFrame);
            Assert.Null(player.CurrentFrameTimestampMs);
            Assert.Equal(priorResetCount + 1, surface.ResetCount);
        }
    }
}
