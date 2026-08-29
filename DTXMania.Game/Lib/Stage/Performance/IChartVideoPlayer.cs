#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Non-blocking, game-owned chart background video player.
    ///
    /// Contracts:
    /// - <see cref="Start"/> schedules a decode generation and returns before
    ///   probe/process startup completes; no seek or playback-rate APIs.
    /// - Every generation starts at media zero: frames are stamped
    ///   <c>frameIndex * frameIntervalMs</c>; no -ss is used.
    /// - <see cref="Draw"/> renders nothing until a timely frame exists; the
    ///   stage owns bounds and layer depth.
    /// - <see cref="Stop"/> and retrigger cancel the generation token.
    /// </summary>
    public interface IChartVideoPlayer : IDisposable
    {
        /// <summary>Schedules playback of the given video file from media zero.</summary>
        void Start(string videoFilePath);

        /// <summary>Game-thread media-time update that selects and uploads the due frame.</summary>
        void Update(double mediaTimeMs);

        /// <summary>Draws the current frame aspect-fit inside the destination bounds.</summary>
        void Draw(SpriteBatch spriteBatch, Rectangle destinationBounds, float layerDepth);

        /// <summary>Cancels the current decode generation.</summary>
        void Stop();
    }
}
