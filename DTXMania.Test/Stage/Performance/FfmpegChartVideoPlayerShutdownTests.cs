#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public sealed class FfmpegChartVideoPlayerShutdownTests
    {
        public static IEnumerable<object[]> ExpectedCallbackFailures()
        {
            yield return new object[] { new IOException("The pipe is being closed.") };
            yield return new object[] { new AggregateException(new IOException("Closed pipe")) };
            yield return new object[]
            {
                new AggregateException(new IOException("First pipe"),
                    new AggregateException(new IOException("Second pipe")))
            };
        }

        public static IEnumerable<object[]> UnexpectedCallbackFailures()
        {
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
        [MemberData(nameof(ExpectedCallbackFailures))]
        public void Stop_WhenCancellationCallbackHasIoFailure_ShouldCompleteCleanup(Exception callbackFailure)
        {
            var surface = new RecordingSurface();
            using var player = CreatePlayer(surface);
            var cts = StartWithPresentedFrame(player);
            var token = cts.Token;
            var remainingCallbackRan = false;
            using var remainingRegistration = token.Register(() => remainingCallbackRan = true);
            using var registration = token.Register(() => throw callbackFailure);
            var resetCount = surface.ResetCount;

            var exception = Record.Exception(player.Stop);

            Assert.Null(exception);
            Assert.True(remainingCallbackRan);
            AssertStopped(player, cts, token, surface, resetCount);
            player.Stop();
            Assert.False(player.HasCurrentFrame);
        }

        [Theory]
        [MemberData(nameof(UnexpectedCallbackFailures))]
        public void Stop_WhenCancellationCallbackHasUnexpectedFailure_ShouldPropagateAndCleanUp(
            Exception callbackFailure)
        {
            var surface = new RecordingSurface();
            using var player = CreatePlayer(surface);
            var cts = StartWithPresentedFrame(player);
            var token = cts.Token;
            using var registration = token.Register(() => throw callbackFailure);
            var resetCount = surface.ResetCount;

            var exception = Assert.Throws<AggregateException>(player.Stop);

            Assert.Contains(callbackFailure, exception.InnerExceptions);
            AssertStopped(player, cts, token, surface, resetCount);
        }

        [Fact]
        public async Task Stop_WhenProducerIsBlockedAndCallbackHasIoFailure_ShouldReleaseProducer()
        {
            var surface = new RecordingSurface();
            using var player = CreatePlayer(surface);
            var cts = StartWithPresentedFrame(player);
            var token = cts.Token;
            var queue = Channel.CreateBounded<VideoFrame>(FfmpegChartVideoPlayer.QueueCapacity);
            var sink = new VideoFramePipeSink(1, 1, 100, queue.Writer, token);
            using var input = new MemoryStream(new byte[10 * 4]);
            var producer = sink.ReadAsync(input, CancellationToken.None);
            Assert.Equal(FfmpegChartVideoPlayer.QueueCapacity, queue.Reader.Count);
            Assert.False(producer.IsCompleted);
            // Cancel invokes the newest callback first. Its exception must
            // not prevent the older linked-token callback waking the sink.
            using var registration = token.Register(() => throw new IOException("Closed pipe"));
            var resetCount = surface.ResetCount;

            player.Stop();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => producer.WaitAsync(TimeSpan.FromSeconds(10)));
            AssertStopped(player, cts, token, surface, resetCount);
        }

        [Fact]
        public void Start_WhenPriorCancellationCallbackHasIoFailure_ShouldStartNewGeneration()
        {
            var surface = new RecordingSurface();
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
            var currentCts = GetField<CancellationTokenSource>(player, "_generationCts");
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
        public void Dispose_WhenCancellationCallbackThrows_ShouldDisposeSurfaceAndRemainIdempotent(
            bool unexpectedFailure)
        {
            var surface = new RecordingSurface();
            var player = CreatePlayer(surface);
            var cts = StartWithPresentedFrame(player);
            var token = cts.Token;
            Exception callbackFailure = unexpectedFailure
                ? new InvalidOperationException("Unexpected callback failure")
                : new IOException("Closed pipe");
            using var registration = token.Register(() => throw callbackFailure);
            var resetCount = surface.ResetCount;

            var exception = Record.Exception(player.Dispose);

            if (unexpectedFailure)
                Assert.IsType<AggregateException>(exception);
            else
                Assert.Null(exception);
            AssertStopped(player, cts, token, surface, resetCount);
            Assert.Equal(1, surface.DisposeCount);
            player.Dispose();
            player.Stop();
            Assert.Equal(1, surface.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => player.Start("after-disposal.avi"));
        }

        private static FfmpegChartVideoPlayer CreatePlayer(RecordingSurface surface) =>
            new(_ => Task.CompletedTask, surface, log: null);

        private static CancellationTokenSource StartWithPresentedFrame(FfmpegChartVideoPlayer player)
        {
            // Defer startup so no FFmpeg process or timing race is needed.
            // Inject a callback on the real generation CTS, exactly where
            // FFMpegCore registers its shutdown callback.
            player.Start("deferred-generation.avi");
            var queue = Channel.CreateBounded<VideoFrame>(FfmpegChartVideoPlayer.QueueCapacity);
            queue.Writer.TryWrite(new VideoFrame(0, new byte[64 * 48 * 4]));
            queue.Writer.TryWrite(new VideoFrame(100, new byte[64 * 48 * 4]));
            var generationType = typeof(FfmpegChartVideoPlayer)
                .GetNestedType("GenerationState", BindingFlags.NonPublic)!;
            SetField(player, "_generation", Activator.CreateInstance(
                generationType, new object[] { queue.Reader, 64, 48, 100d }));
            player.Update(0);
            Assert.True(player.HasCurrentFrame);
            Assert.Equal(0, player.CurrentFrameTimestampMs);
            Assert.Equal(1, player.QueuedFrameCount);
            return GetField<CancellationTokenSource>(player, "_generationCts")!;
        }

        private static void AssertStopped(
            FfmpegChartVideoPlayer player,
            CancellationTokenSource cts,
            CancellationToken token,
            RecordingSurface surface,
            int priorResetCount)
        {
            Assert.True(token.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() => cts.Token);
            Assert.Null(GetField<CancellationTokenSource>(player, "_generationCts"));
            Assert.Equal(0, player.QueuedFrameCount);
            Assert.False(player.HasCurrentFrame);
            Assert.Null(player.CurrentFrameTimestampMs);
            Assert.Equal(priorResetCount + 1, surface.ResetCount);
        }

        private static T? GetField<T>(FfmpegChartVideoPlayer player, string name) where T : class =>
            (T?)typeof(FfmpegChartVideoPlayer)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player);

        private static void SetField(FfmpegChartVideoPlayer player, string name, object? value) =>
            typeof(FfmpegChartVideoPlayer)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);

        private sealed class RecordingSurface : IVideoFrameSurface
        {
            public Texture2D? Texture => null;
            public int ResetCount { get; private set; }
            public int DisposeCount { get; private set; }
            public void Present(byte[] rgba, int width, int height) { }
            public void Reset() => ResetCount++;
            public void Dispose() => DisposeCount++;
        }
    }
}
