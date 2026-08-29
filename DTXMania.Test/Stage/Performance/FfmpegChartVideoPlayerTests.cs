#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using FFMpegCore;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Pins the FFMpegCore probe/pipe behavior the HPA-11 chart background
    /// video player relies on and exercises the cancellable generation flow
    /// against the committed rawvideo/bgr24 AVI fixture.
    /// </summary>
    [Collection("FfmpegRuntimeState")]
    [Trait("Category", "Unit")]
    public sealed class FfmpegChartVideoPlayerTests : IDisposable
    {
        private static readonly string FixturePath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "Video", "tiny-raw-bgr24.avi");

        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-video-player-" + Guid.NewGuid().ToString("N"));

        public FfmpegChartVideoPlayerTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        private static async Task EventuallyAsync(
            Func<bool> condition,
            TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(25);
            }

            Assert.True(condition(), "Condition was not met within the timeout.");
        }

        private static VideoFramePipeSink CreateSink(Channel<VideoFrame> queue, CancellationToken token)
        {
            return new VideoFramePipeSink(
                64, 48, frameIntervalMs: 100, queue.Writer, token);
        }

        private static FfmpegChartVideoPlayer CreatePlayer(
            Action<string> log,
            Func<Func<Task>, Task>? scheduler = null,
            IVideoFrameSurface? surface = null)
        {
            return new FfmpegChartVideoPlayer(
                scheduler ?? (work => Task.Run(work)),
                surface,
                log);
        }

        #region Step 2: FFMpegCore probe/pipe pins

        [Fact]
        public void EnsureConfigured_ShouldReportUsableRuntime()
        {
            var availability = FfmpegRuntime.EnsureConfigured();

            Assert.True(availability.IsAvailable, availability.DiagnosticReason);
        }

        [Fact]
        public async Task AnalyseAsync_Fixture_ShouldExposeUsableVideoStream()
        {
            Assert.True(File.Exists(FixturePath), $"Missing committed video fixture: {FixturePath}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var media = await FFProbe.AnalyseAsync(FixturePath, cancellationToken: cts.Token);

            var video = media.PrimaryVideoStream;
            Assert.NotNull(video);
            Assert.Equal(64, video!.Width);
            Assert.Equal(48, video!.Height);
            Assert.True(video!.FrameRate > 0, "Fixture frame rate must be usable.");
        }

        [Fact]
        public async Task OutputToPipe_DecodeFixture_ShouldYieldAtLeastTwoRgbaFrames()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var media = await FFProbe.AnalyseAsync(FixturePath, cancellationToken: cts.Token);
            var video = media.PrimaryVideoStream!;
            var frameBytes = video.Width * video.Height * 4;

            var queue = Channel.CreateBounded<VideoFrame>(
                new BoundedChannelOptions(3) { FullMode = BoundedChannelFullMode.Wait });
            var sink = new VideoFramePipeSink(
                video.Width, video.Height,
                1000.0 / video.FrameRate,
                queue.Writer,
                cts.Token);

            var frames = new List<VideoFrame>();
            var decodeTask = FFMpegArguments
                .FromFileInput(FixturePath)
                .OutputToPipe(sink, options => options
                    .SelectStream(0, channel: FFMpegCore.Enums.Channel.Video)
                    .DisableChannel(FFMpegCore.Enums.Channel.Audio)
                    .WithVideoCodec("rawvideo")
                    .ForcePixelFormat("rgba")
                    .ForceFormat("rawvideo"))
                .CancellableThrough(cts.Token)
                .ProcessAsynchronously();

            // Drain while the producer runs; the bounded queue blocks it otherwise.
            var pump = Task.Run(async () =>
            {
                try
                {
                    await foreach (var frame in queue.Reader.ReadAllAsync(cts.Token))
                    {
                        frames.Add(frame);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });

            var completed = await decodeTask;
            cts.Cancel();
            await pump;

            Assert.True(completed);

            Assert.True(frames.Count >= 2, $"Expected at least two RGBA frames, got {frames.Count}.");
            Assert.All(frames, frame => Assert.Equal(frameBytes, frame.Rgba.Length));
        }

        [Fact]
        public async Task AnalyseAsync_MissingInput_ShouldThrow()
        {
            var missing = Path.Combine(_tempDirectory, "does-not-exist.avi");

            await Assert.ThrowsAnyAsync<Exception>(
                () => FFProbe.AnalyseAsync(missing));
        }

        #endregion

        #region Step 4: non-blocking narrow player API

        [Fact]
        public async Task Start_WhileStartupWorkPending_ShouldReturnImmediately()
        {
            var scheduledWork = new List<Func<Task>>();
            var logs = new List<string>();
            using var player = CreatePlayer(
                logs.Add,
                scheduler: work =>
                {
                    lock (scheduledWork) { scheduledWork.Add(work); }
                    return Task.CompletedTask;
                });

            // The controlled async barrier: startup work is captured but never
            // invoked, so probe/process startup stays pending. Start must have
            // returned before any of that work completes.
            player.Start(FixturePath);

            lock (scheduledWork)
            {
                Assert.Single(scheduledWork);
            }
            Assert.Empty(logs);

            // Release the barrier; Stop cancels the generation because this
            // test drains no queue, and it must complete promptly.
            Func<Task>? work;
            lock (scheduledWork) { work = scheduledWork[0]; }
            var generation = work();
            player.Stop();
            await generation.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Empty(logs);
        }

        [Fact]
        public async Task Start_MissingFile_ShouldContainFailureAndLogOnce()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            var missing = Path.Combine(_tempDirectory, "missing.avi");
            player.Start(missing);

            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Single(logs);
        }

        [Fact]
        public async Task Start_CorruptFile_ShouldContainFailureAndLogOnce()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            var corrupt = Path.Combine(_tempDirectory, "corrupt.avi");
            await File.WriteAllBytesAsync(corrupt, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            player.Start(corrupt);

            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Single(logs);
        }

        [Fact]
        public void Draw_BeforeAnyFrameIsCurrent_ShouldRenderNothing()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            Assert.False(player.HasCurrentFrame);
        }

        #endregion

        #region Step 5: bounded queue and cancellation

        [Fact]
        public async Task Start_WhenQueueFull_StopShouldCompleteProducerWithoutDeadlock()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            player.Start(FixturePath);

            // The 10-frame fixture fills the capacity-3 queue and the producer
            // blocks on the full queue using the generation token: it cannot
            // grow the queue beyond three frames.
            await EventuallyAsync(() => player.QueuedFrameCount == FfmpegChartVideoPlayer.QueueCapacity);
            Assert.Empty(logs);

            player.Stop();

            // Cancellation must wake the blocked producer and let FFMpegCore
            // end the process generation within the test timeout: no deadlock.
            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Stop_WhileDecoding_ShouldKeepTimestampsFromMediaZero()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount >= 1);

            player.Update(0);
            Assert.Equal(0, player.CurrentFrameTimestampMs);

            await EventuallyAsync(() => player.QueuedFrameCount >= 3);

            // Zero origin: frame timestamps are frameIndex * frameIntervalMs
            // (100ms intervals), never offset by async startup time.
            player.Update(350);
            Assert.Equal(300, player.CurrentFrameTimestampMs);

            player.Stop();
            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Update_ShouldUploadOnlySelectedFrameToSurface()
        {
            var logs = new List<string>();
            var surface = new FakeVideoFrameSurface();
            using var player = CreatePlayer(logs.Add, surface: surface);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount >= 3);

            player.Update(0);
            Assert.Equal(1, surface.PresentCount);
            Assert.Equal(64, surface.LastWidth);
            Assert.Equal(48, surface.LastHeight);

            // Nothing due yet: hold current frame without re-uploading.
            player.Update(50);
            Assert.Equal(1, surface.PresentCount);

            // Consume through latest due frame, skipping intermediates.
            player.Update(250);
            Assert.Equal(2, surface.PresentCount);
            Assert.Equal(200, player.CurrentFrameTimestampMs);
        }

        #endregion

        #region Step 8: aspect-fit geometry

        [Fact]
        public void ComputeAspectFit_WidescreenBounds_ShouldLetterboxAndCenter()
        {
            var fit = FfmpegChartVideoPlayer.ComputeAspectFit(
                new Rectangle(0, 0, 1280, 720), 64, 48);

            Assert.Equal(new Rectangle(160, 0, 960, 720), fit);
        }

        [Fact]
        public void ComputeAspectFit_OffsetsToBoundsOrigin()
        {
            var fit = FfmpegChartVideoPlayer.ComputeAspectFit(
                new Rectangle(100, 50, 1280, 720), 64, 48);

            Assert.Equal(new Rectangle(260, 50, 960, 720), fit);
        }

        [Fact]
        public void ComputeAspectFit_TallerBounds_ShouldPillarboxAndCenter()
        {
            var fit = FfmpegChartVideoPlayer.ComputeAspectFit(
                new Rectangle(0, 0, 480, 720), 64, 48);

            Assert.Equal(new Rectangle(0, 180, 480, 360), fit);
        }

        #endregion

        private sealed class FakeVideoFrameSurface : IVideoFrameSurface
        {
            public int PresentCount { get; private set; }

            public int LastWidth { get; private set; }

            public int LastHeight { get; private set; }

            public Microsoft.Xna.Framework.Graphics.Texture2D? Texture => null;

            public void Present(byte[] rgba, int width, int height)
            {
                PresentCount++;
                LastWidth = width;
                LastHeight = height;
            }

            public void Reset()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
