#nullable enable
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage
{
    public interface IStageGame
    {
        GraphicsDevice GraphicsDevice { get; }
        IStageManager StageManager { get; }
        IConfigManager ConfigManager { get; }
        InputManagerCompat InputManager { get; }
        IGraphicsManager GraphicsManager { get; }
        IResourceManager ResourceManager { get; }
        ILoggerFactory LoggerFactory { get; }

        bool CanPerformStageTransition();
        void MarkStageTransition();

        /// <summary>
        /// Maps raw window mouse coordinates into the fixed 1280x720 virtual render target.
        /// Returns null when the point lands outside the letterboxed virtual area.
        /// </summary>
        Point? MapMouseToVirtual(Point windowPoint);

        /// <summary>
        /// Builds a text-input source for OS text events (used by the song search modal).
        /// Returns null in headless/test environments where no OS window is available.
        /// </summary>
        /// <remarks>Each call returns a fresh disposable instance; the caller owns it and
        /// must dispose it when no longer needed (see <see cref="ITextInputSource"/>).</remarks>
        ITextInputSource? GetTextInputSource();

        /// <summary>
        /// Requests game process termination.
        /// </summary>
        void RequestExit();
    }
}
