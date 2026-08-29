#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// A decoded RGBA video frame stamped from media zero:
    /// <c>TimestampMs = frameIndex * frameIntervalMs</c>.
    /// </summary>
    internal sealed class VideoFrame
    {
        public VideoFrame(double timestampMs, byte[] rgba)
        {
            TimestampMs = timestampMs;
            Rgba = rgba;
        }

        public double TimestampMs { get; }

        public byte[] Rgba { get; }
    }

    /// <summary>
    /// HPA-11 <see cref="IPipeSink"/> that reads complete RGBA frame buffers
    /// from the ffmpeg rawvideo pipe and writes them into the bounded frame
    /// queue. When the queue is full the producer waits on the generation
    /// token; Stop/retrigger cancels the token, waking the producer and letting
    /// FFMpegCore end the process generation.
    /// </summary>
    internal sealed class VideoFramePipeSink : IPipeSink
    {
        private readonly int _frameBytes;
        private readonly double _frameIntervalMs;
        private readonly ChannelWriter<VideoFrame> _queue;
        private readonly CancellationToken _generationToken;
        private int _nextFrameIndex;

        public VideoFramePipeSink(
            int frameWidth,
            int frameHeight,
            double frameIntervalMs,
            ChannelWriter<VideoFrame> queue,
            CancellationToken generationToken)
        {
            _frameBytes = frameWidth * frameHeight * 4;
            _frameIntervalMs = frameIntervalMs;
            _queue = queue;
            _generationToken = generationToken;
        }

        public string GetFormat() => "rawvideo";

        public async Task ReadAsync(Stream inputStream, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _generationToken, cancellationToken);
            var buffer = new byte[_frameBytes];
            while (true)
            {
                try
                {
                    await inputStream.ReadExactlyAsync(buffer, linked.Token).ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    // Clean end of the rawvideo stream; keep frames already queued.
                    return;
                }

                var frame = new VideoFrame(_nextFrameIndex * _frameIntervalMs, (byte[])buffer.Clone());
                _nextFrameIndex++;
                await _queue.WriteAsync(frame, linked.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Game-thread texture surface for decoded frames. Implementations must
    /// only be used from the game thread.
    /// </summary>
    internal interface IVideoFrameSurface : IDisposable
    {
        Texture2D? Texture { get; }

        /// <summary>Uploads a frame, recreating the texture when dimensions change.</summary>
        void Present(byte[] rgba, int width, int height);

        /// <summary>Drops the texture (Stop or generation dimension change).</summary>
        void Reset();
    }

    [ExcludeFromCodeCoverage]
    internal sealed class Texture2DVideoFrameSurface : IVideoFrameSurface
    {
        private readonly GraphicsDevice _graphicsDevice;
        private Texture2D? _texture;

        public Texture2DVideoFrameSurface(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        }

        public Texture2D? Texture => _texture;

        public void Present(byte[] rgba, int width, int height)
        {
            if (_texture == null || _texture.Width != width || _texture.Height != height)
            {
                _texture?.Dispose();
                _texture = new Texture2D(_graphicsDevice, width, height);
            }

            _texture.SetData(rgba);
        }

        public void Reset()
        {
            _texture?.Dispose();
            _texture = null;
        }

        public void Dispose() => Reset();
    }

    /// <summary>
    /// Non-blocking game-owned AVI/rawvideo chart background video player.
    ///
    /// Generation flow: EnsureConfigured -> FFProbe.AnalyseAsync ->
    /// FFMpegArguments.FromFileInput -> OutputToPipe(VideoFramePipeSink) ->
    /// CancellableThrough(generationToken) -> ProcessAsynchronously.
    /// No seek: every generation starts at media zero and frames are stamped
    /// <c>frameIndex * frameIntervalMs</c>; async startup is absorbed by decode
    /// catch-up plus consumer skipping while Draw renders nothing.
    /// </summary>
    public sealed class FfmpegChartVideoPlayer : IChartVideoPlayer
    {
        /// <summary>Maximum queued frames; caps raw queue memory at
        /// 3 * width * height * 4 bytes (~10.5 MiB at 1280x720).</summary>
        public const int QueueCapacity = 3;

        private readonly Func<Func<Task>, Task> _startupScheduler;
        private readonly Action<string>? _log;
        private readonly IVideoFrameSurface? _surface;
        private readonly object _stateLock = new();

        private bool _disposed;
        private CancellationTokenSource? _generationCts;
        private Task? _generationTask;
        private GenerationState? _generation;
        private GenerationState? _textureFailureLoggedGeneration;
        private double? _currentTimestampMs;
        private bool _hasCurrentFrame;

        private sealed record GenerationState(
            ChannelReader<VideoFrame> Queue,
            int Width,
            int Height,
            double FrameIntervalMs);

        /// <summary>Creates the player for the game thread.</summary>
        [ExcludeFromCodeCoverage]
        public FfmpegChartVideoPlayer(GraphicsDevice graphicsDevice, Action<string>? log)
            : this(work => Task.Run(work), new Texture2DVideoFrameSurface(graphicsDevice), log)
        {
        }

        /// <summary>Test seam: injectable startup dispatch, texture surface, and logger.</summary>
        internal FfmpegChartVideoPlayer(
            Func<Func<Task>, Task> startupScheduler,
            IVideoFrameSurface? surface,
            Action<string>? log)
        {
            _startupScheduler = startupScheduler ?? throw new ArgumentNullException(nameof(startupScheduler));
            _surface = surface;
            _log = log;
        }

        /// <summary>Task observing the current generation, or null before Start.</summary>
        internal Task? GenerationTask => _generationTask;

        /// <summary>Frames queued for the active generation.</summary>
        internal int QueuedFrameCount
        {
            get
            {
                lock (_stateLock)
                {
                    return _generation?.Queue.Count ?? 0;
                }
            }
        }

        internal double? CurrentFrameTimestampMs => _currentTimestampMs;

        internal bool HasCurrentFrame => _hasCurrentFrame;

        /// <summary>
        /// Schedules a decode generation and returns before probe/process
        /// startup completes. Retriggering cancels the previous generation.
        /// </summary>
        public void Start(string videoFilePath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FfmpegChartVideoPlayer));
            if (string.IsNullOrEmpty(videoFilePath))
                throw new ArgumentException("Video file path is required", nameof(videoFilePath));

            Stop();

            var cts = new CancellationTokenSource();
            lock (_stateLock)
            {
                _generationCts = cts;
            }

            _generationTask = _startupScheduler(() => RunGenerationAsync(videoFilePath, cts));
        }

        /// <summary>Game-thread update: selects and uploads the due frame.</summary>
        public void Update(double mediaTimeMs)
        {
            if (_disposed)
                return;

            GenerationState? generation;
            lock (_stateLock)
            {
                generation = _generation;
            }

            if (generation == null)
            {
                _hasCurrentFrame = false;
                return;
            }

            // One bounded pass over the queue: consume-and-discard obsolete
            // frames via the selector contract, keeping only the newest due
            // frame. Total held frames never exceed the capacity-3 queue plus
            // this single in-flight frame; no pending list is accumulated.
            VideoFrame? newestDueFrame = null;
            var dueCount = 0;
            while (generation.Queue.TryPeek(out var head) && head.TimestampMs <= mediaTimeMs)
            {
                if (!generation.Queue.TryRead(out var frame))
                {
                    break;
                }

                dueCount++;
                newestDueFrame = frame;
            }

            var selection = VideoFrameSelector.Select(
                mediaTimeMs,
                generation.FrameIntervalMs,
                _currentTimestampMs,
                dueCount,
                newestDueFrame?.TimestampMs);

            switch (selection.Kind)
            {
                case VideoFrameSelectionKind.Advance:
                    PresentFrame(generation, newestDueFrame!);
                    break;

                case VideoFrameSelectionKind.NoFrame:
                    // Nothing timely exists (async-start catch-up, decoder
                    // behind by more than one frame interval, or empty queue):
                    // Draw renders nothing so the static background shows.
                    _hasCurrentFrame = false;
                    break;
            }
        }

        /// <summary>
        /// Uploads a frame to the texture surface. Texture upload failure is
        /// non-fatal per the spec: it is contained, logged once per generation,
        /// and falls back to the static background while the decode generation
        /// stays alive.
        /// </summary>
        private void PresentFrame(GenerationState generation, VideoFrame frame)
        {
            try
            {
                _surface?.Present(frame.Rgba, generation.Width, generation.Height);
                _currentTimestampMs = frame.TimestampMs;
                _hasCurrentFrame = true;
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(_textureFailureLoggedGeneration, generation))
                {
                    _textureFailureLoggedGeneration = generation;
                    _log?.Invoke($"Chart video texture upload failed: {ex.GetType().Name}: {ex.Message}");
                }

                _hasCurrentFrame = false;
            }
        }

        /// <summary>
        /// Draws the current frame aspect-fit inside the destination bounds.
        /// Renders nothing until a timely frame exists.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, Rectangle destinationBounds, float layerDepth)
        {
            if (_disposed || !_hasCurrentFrame)
                return;

            var texture = _surface?.Texture;
            if (texture == null)
                return;

            DrawChartVideoFrame(spriteBatch, destinationBounds, layerDepth, texture);
        }

        /// <summary>GPU draw call extracted so the headless-testable guard logic in
        /// <see cref="Draw"/> stays measurable.</summary>
        [ExcludeFromCodeCoverage]
        private void DrawChartVideoFrame(
            SpriteBatch spriteBatch, Rectangle destinationBounds, float layerDepth, Texture2D texture)
        {
            var destination = ComputeAspectFit(destinationBounds, texture.Width, texture.Height);
            spriteBatch.Draw(
                texture,
                destination,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                layerDepth);
        }

        /// <summary>Cancels the current generation; a blocked producer wakes and the process ends.</summary>
        public void Stop()
        {
            if (_disposed)
                return;

            CancellationTokenSource? cts;
            lock (_stateLock)
            {
                cts = _generationCts;
                _generationCts = null;
                _generation = null;
            }

            cts?.Cancel();
            cts?.Dispose();

            _currentTimestampMs = null;
            _hasCurrentFrame = false;
            _surface?.Reset();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            _surface?.Dispose();
            _disposed = true;
        }

        /// <summary>Aspect-fit geometry: largest centered fit, no overscan.</summary>
        internal static Rectangle ComputeAspectFit(Rectangle bounds, int contentWidth, int contentHeight)
        {
            if (contentWidth <= 0 || contentHeight <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return Rectangle.Empty;
            }

            var scale = Math.Min(
                bounds.Width / (double)contentWidth,
                bounds.Height / (double)contentHeight);
            var width = (int)Math.Round(contentWidth * scale);
            var height = (int)Math.Round(contentHeight * scale);
            return new Rectangle(
                bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (bounds.Height - height) / 2,
                width,
                height);
        }

        private async Task RunGenerationAsync(string videoFilePath, CancellationTokenSource generationCts)
        {
            var token = generationCts.Token;
            try
            {
                var runtime = FfmpegRuntime.EnsureConfigured();
                if (!runtime.IsAvailable)
                {
                    _log?.Invoke($"Chart video runtime unavailable: {runtime.DiagnosticReason}");
                    return;
                }

                var media = await FFProbe.AnalyseAsync(videoFilePath, cancellationToken: token)
                    .ConfigureAwait(false);
                var video = media.PrimaryVideoStream;
                if (video == null || video.Width <= 0 || video.Height <= 0 || video.FrameRate <= 0)
                {
                    _log?.Invoke($"Chart video has no usable video stream: {videoFilePath}");
                    return;
                }

                var frameIntervalMs = 1000.0 / video.FrameRate;
                var queue = Channel.CreateBounded<VideoFrame>(
                    new BoundedChannelOptions(QueueCapacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = true,
                    });

                // Publish only if this startup still owns the current
                // generation. Stop() clears _generationCts under the lock and
                // only cancels the token after releasing it, so a probe that
                // completes in that gap would otherwise see a non-cancelled
                // token and republish a generation Stop() just cleared. The
                // ownership check closes that window without a lifecycle
                // abstraction: a cleared/replaced _generationCts fails the
                // reference check and leaves _generation null.
                lock (_stateLock)
                {
                    if (!token.IsCancellationRequested && ReferenceEquals(_generationCts, generationCts))
                    {
                        _generation = new GenerationState(
                            queue.Reader, video.Width, video.Height, frameIntervalMs);
                    }
                }

                var sink = new VideoFramePipeSink(
                    video.Width, video.Height, frameIntervalMs, queue.Writer, token);

                var completed = await FFMpegArguments
                    .FromFileInput(videoFilePath)
                    .OutputToPipe(sink, options => options
                        .SelectStream(0, channel: FFMpegCore.Enums.Channel.Video)
                        .DisableChannel(FFMpegCore.Enums.Channel.Audio)
                        .WithVideoCodec("rawvideo")
                        .ForcePixelFormat("rgba")
                        .ForceFormat("rawvideo"))
                    .CancellableThrough(token)
                    .ProcessAsynchronously(throwOnError: false)
                    .ConfigureAwait(false);

                if (!completed && !token.IsCancellationRequested)
                {
                    _log?.Invoke($"Chart video decode failed: {videoFilePath}");
                }
            }
            catch (OperationCanceledException)
            {
                // Stop/retrigger cancelled this generation; expected.
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Chart video startup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
