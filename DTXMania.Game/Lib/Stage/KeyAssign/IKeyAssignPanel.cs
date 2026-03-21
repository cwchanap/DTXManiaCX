#nullable enable

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

        /// <summary>
        /// Raised when the panel closes (save or cancel).
        /// Implementers must raise <see cref="Saved"/> before <see cref="Closed"/> on a save operation,
        /// because <c>ConfigStage.OnPanelSaved</c> captures the working bindings in the <see cref="Saved"/>
        /// handler before the panel is torn down.
        /// <para>See <c>DrumKeyAssignPanel</c> and <c>SystemKeyAssignPanel</c> for reference implementations.</para>
        /// </summary>
        event EventHandler Closed;

        /// <summary>
        /// Raised when the panel commits its changes (save only, not cancel).
        /// Must be raised before <see cref="Closed"/> so that consumers can read the committed state
        /// while the panel is still active.
        /// </summary>
        event EventHandler Saved;

        void Activate();
        void Deactivate();

        void Update(double deltaTime, KeyboardState current, KeyboardState previous);

        void Draw(SpriteBatch spriteBatch, BitmapFont? bitmapFont, Texture2D? whitePixel,
                  int viewportWidth, int viewportHeight);
    }
}
