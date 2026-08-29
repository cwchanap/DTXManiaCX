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

        private static readonly string AltFixturePath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "Video", "tiny-raw-bgr24-32x24.avi");

        private static readonly string AudioFixturePath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "Audio", "ffmpeg-tone.mp3");

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

        /// <summary>
        /// Creates a copy of the rawvideo AVI fixture with its codec FourCC
        /// changed from rawvideo (0x00000000) to mpeg4 ("mp4v"). FFProbe reads
        /// the AVI container header and reports a valid mpeg4 video stream
        /// (correct dimensions and frame rate), but the bundled ffmpeg runtime
        /// lacks the mpeg4 decoder so decoding fails with a non-zero exit.
        /// This exercises the "decode failed" branch of RunGenerationAsync
        /// without requiring any external ffmpeg or committed binary fixture.
        /// </summary>
        private Task<string> CreateCorruptedMpeg4Avi()
        {
            var corruptedPath = Path.Combine(_tempDirectory, "modified-codec.avi");
            File.Copy(FixturePath, corruptedPath);

            // The AVI stream header (strh) starts at offset 108 with 'vids',
            // followed by the 4-byte codec FourCC at offset 112. The
            // BITMAPINFOHEADER biCompression field at offset 188 also holds
            // the codec FourCC. Both must be changed so FFProbe and ffmpeg
            // agree on the (undecodable) codec.
            using var stream = new FileStream(corruptedPath, FileMode.Open, FileAccess.Write);
            stream.Seek(112, SeekOrigin.Begin);
            stream.Write("mp4v"u8.ToArray());
            stream.Seek(188, SeekOrigin.Begin);
            stream.Write("mp4v"u8.ToArray());

            return Task.FromResult(corruptedPath);
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
        public async Task Start_WhenStopRunsBeforeDeferredWork_ShouldNotFaultOrLog()
        {
            // Regression for the deferred-startup ObjectDisposedException race:
            // Start() schedules RunGenerationAsync through a deferred scheduler.
            // If Stop() cancels + disposes the generation CTS before the
            // deferred callback runs, reading generationCts.Token inside it
            // would throw ObjectDisposedException before the try block. The
            // fix captures cts.Token in Start() before scheduling and passes
            // the cached token in, so the deferred work observes cancellation
            // gracefully instead of faulting.
            var scheduledWork = new List<Func<Task>>();
            var logs = new List<string>();
            using var player = CreatePlayer(
                logs.Add,
                scheduler: work =>
                {
                    lock (scheduledWork) { scheduledWork.Add(work); }
                    return Task.CompletedTask;
                });

            player.Start(FixturePath);

            Func<Task>? work;
            lock (scheduledWork) { work = scheduledWork[0]; }

            // Stop() disposes the generation CTS before the deferred work runs.
            player.Stop();

            // The deferred generation must complete without faulting or logging.
            var generation = work();
            await generation.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(generation.IsCompletedSuccessfully,
                $"Deferred generation should complete successfully, status={generation.Status}");
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

        #region Final-review fixes: bounded selection pass, stale catch-up, upload containment

        [Fact]
        public async Task Update_CatchUpBeyondTolerance_ShouldDiscardStaleFramesAndShowStaticBackground()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount == FfmpegChartVideoPlayer.QueueCapacity);

            // Logical media time already far ahead of the decoder (async
            // startup or an update hitch): nothing drawable — the static
            // background stays visible — while the obsolete frames are
            // consumed so the decoder can advance.
            player.Update(100_000);

            Assert.False(player.HasCurrentFrame);
            Assert.Equal(0, player.QueuedFrameCount);

            // The consumed queue unblocks the producer: decoding continues.
            await EventuallyAsync(() => player.QueuedFrameCount == FfmpegChartVideoPlayer.QueueCapacity);

            player.Stop();
            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(logs);
        }

        [Fact]
        public async Task Update_WhenTextureUploadFails_ShouldContainFailureAndLogOncePerGeneration()
        {
            var logs = new List<string>();
            var surface = new ThrowingVideoFrameSurface();
            using var player = CreatePlayer(logs.Add, surface: surface);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount >= 1);

            // Update must never throw for an upload failure: fall back to the
            // static background and keep the generation alive.
            player.Update(0);
            Assert.False(player.HasCurrentFrame);
            Assert.Single(logs);

            // A second failing upload in the same generation stays silent.
            player.Update(100);
            Assert.False(player.HasCurrentFrame);
            Assert.Single(logs);

            player.Stop();
            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }

        #endregion

        #region Final-review fixes: retrigger pins

        [Fact]
        public async Task Start_RetriggerWhileQueueFull_ShouldCompletePriorGenerationWithoutDeadlock()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount == FfmpegChartVideoPlayer.QueueCapacity);
            var priorTask = player.GenerationTask!;

            // Second Start while the first generation's producer is blocked on
            // the full queue: the prior generation must be cancelled and its
            // task completed (no deadlock), and the new generation must decode
            // normally afterwards.
            player.Start(FixturePath);

            await priorTask.WaitAsync(TimeSpan.FromSeconds(10));
            await EventuallyAsync(() => player.QueuedFrameCount == FfmpegChartVideoPlayer.QueueCapacity);

            player.Stop();
            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(logs);
        }

        [Fact]
        public async Task Start_Retrigger_ShouldCancelPriorGenerationTask()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount >= 1);
            var priorTask = player.GenerationTask!;

            player.Start(FixturePath);

            // The prior generation's task must observe the cancellation and
            // complete instead of lingering.
            await priorTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(priorTask.IsCompleted);

            player.Stop();
            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Start_RetriggerWithDifferentDimensions_ShouldReplaceSurfaceTexture()
        {
            var logs = new List<string>();
            var surface = new FakeVideoFrameSurface();
            using var player = CreatePlayer(logs.Add, surface: surface);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount >= 1);
            player.Update(0);
            Assert.Equal(64, surface.LastWidth);
            Assert.Equal(48, surface.LastHeight);

            // Retrigger with a 32x24 fixture: the dimension change must
            // replace the texture.
            player.Start(AltFixturePath);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (surface.LastWidth != 32 && DateTime.UtcNow < deadline)
            {
                player.Update(0); // the new generation restarts at media zero
                await Task.Delay(25);
            }

            Assert.Equal(32, surface.LastWidth);
            Assert.Equal(24, surface.LastHeight);
            Assert.True(surface.ResetCount >= 1, "Prior-generation texture must be reset on retrigger.");

            player.Stop();
            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }

        #endregion

        #region Draw guard paths (headless-testable without SpriteBatch)

        [Fact]
        public void Draw_BeforeAnyFrameIsCurrent_ShouldReturnWithoutThrowing()
        {
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            // No frame has been presented: Draw must early-return without
            // touching the SpriteBatch (null is safe because the guard fires).
            player.Draw(null!, new Rectangle(0, 0, 100, 100), 0.5f);

            Assert.False(player.HasCurrentFrame);
            Assert.Empty(logs);
        }

        [Fact]
        public async Task Draw_WhenCurrentFrameExistsButSurfaceTextureIsNull_ShouldReturnWithoutThrowing()
        {
            var logs = new List<string>();
            var surface = new FakeVideoFrameSurface(); // Texture => null
            using var player = CreatePlayer(logs.Add, surface: surface);

            player.Start(FixturePath);
            await EventuallyAsync(() => player.QueuedFrameCount >= 1);

            // Present a frame: HasCurrentFrame becomes true, but the fake
            // surface's Texture is null. Draw must return before calling
            // spriteBatch.Draw (null SpriteBatch is safe due to the guard).
            player.Update(0);
            Assert.True(player.HasCurrentFrame);

            player.Draw(null!, new Rectangle(0, 0, 100, 100), 0.5f);

            Assert.Empty(logs);
        }

        #endregion

        #region RunGenerationAsync edge cases

        [Fact]
        public async Task Start_AudioOnlyFile_ShouldLogNoUsableVideoStream()
        {
            // FFProbe succeeds on an audio-only file but PrimaryVideoStream is
            // null: the generation must log and return without decoding.
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            player.Start(AudioFixturePath);

            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Single(logs);
            Assert.Contains("no usable video stream", logs[0]);
        }

        [Fact]
        public async Task Start_CorruptedVideoFile_ShouldLogDecodeFailure()
        {
            // Modify the rawvideo AVI fixture's codec FourCC to mpeg4 ("mp4v").
            // FFProbe reads the container header and reports a valid mpeg4
            // video stream, but the bundled ffmpeg lacks the mpeg4 decoder so
            // decoding fails with a non-zero exit.
            var logs = new List<string>();
            using var player = CreatePlayer(logs.Add);

            var corruptedPath = await CreateCorruptedMpeg4Avi();
            player.Start(corruptedPath);

            await player.GenerationTask!.WaitAsync(TimeSpan.FromSeconds(30));

            // The decode failure log is distinct from the "no usable video
            // stream" log (FFProbe found a stream, but decoding failed).
            Assert.Single(logs);
            Assert.Contains("decode failed", logs[0]);
        }

        #endregion

        private sealed class FakeVideoFrameSurface : IVideoFrameSurface
        {
            public int PresentCount { get; private set; }

            public int LastWidth { get; private set; }

            public int LastHeight { get; private set; }

            public int ResetCount { get; private set; }

            public Microsoft.Xna.Framework.Graphics.Texture2D? Texture => null;

            public void Present(byte[] rgba, int width, int height)
            {
                PresentCount++;
                LastWidth = width;
                LastHeight = height;
            }

            public void Reset()
            {
                ResetCount++;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingVideoFrameSurface : IVideoFrameSurface
        {
            public Microsoft.Xna.Framework.Graphics.Texture2D? Texture => null;

            public void Present(byte[] rgba, int width, int height)
            {
                throw new InvalidOperationException("Simulated texture upload failure");
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
