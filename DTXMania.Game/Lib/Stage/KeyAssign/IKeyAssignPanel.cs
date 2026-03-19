using System;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.KeyAssign
{
    /// <summary>
    /// Interface for a key-assignment sub-panel that temporarily takes over
    /// input handling and rendering within ConfigStage.
    /// </summary>
    public interface IKeyAssignPanel
    {
        bool IsActive { get; }

        /// <summary>Raised when the panel closes (save or cancel).</summary>
        event EventHandler Closed;

        /// <summary>Raised when the panel commits its changes (save only, not cancel).</summary>
        event EventHandler Saved;

        void Activate();
        void Deactivate();

        void Update(double deltaTime, KeyboardState current, KeyboardState previous);

        void Draw(SpriteBatch spriteBatch, BitmapFont? bitmapFont, Texture2D? whitePixel,
                  int viewportWidth, int viewportHeight);
    }
}
