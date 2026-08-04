#nullable enable

using System;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Stage.Config
{
    /// <summary>
    /// A configuration overlay that temporarily owns input and rendering within
    /// <see cref="DTXMania.Game.Lib.Stage.ConfigStage"/>.
    /// </summary>
    public interface IConfigOverlayPanel
    {
        bool IsActive { get; }

        /// <summary>Raised after the overlay has committed its edits.</summary>
        event EventHandler? Saved;

        /// <summary>Raised when the overlay closes, whether saved or cancelled.</summary>
        event EventHandler? Closed;

        void Activate();

        void Deactivate();

        void Update(double deltaTime, KeyboardState current, KeyboardState previous);

        void Draw(
            SpriteBatch spriteBatch,
            IFont? font,
            IFont? boldFont,
            Texture2D? whitePixel,
            int virtualWidth,
            int virtualHeight);
    }
}
