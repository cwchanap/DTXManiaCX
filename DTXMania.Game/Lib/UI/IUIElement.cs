#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Game.Lib.UI
{
    /// <summary>
    /// Core interface for all UI elements in DTXMania
    /// Inspired by DTXMania's CActivity pattern but modernized for MonoGame
    /// </summary>
    public interface IUIElement : IDisposable
    {
        #region Properties

        /// <summary>
        /// Position of the element relative to its parent
        /// </summary>
        Vector2 Position { get; set; }

        /// <summary>
        /// Size of the element
        /// </summary>
        Vector2 Size { get; set; }

        /// <summary>
        /// Whether the element is visible and should be drawn
        /// </summary>
        bool Visible { get; set; }

        /// <summary>
        /// Whether the element is enabled and can receive input
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// Whether the element currently has focus
        /// </summary>
        bool Focused { get; set; }

        /// <summary>
        /// Whether the element is currently active (equivalent to DTXMania's bActivated)
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Parent element, null if this is a root element
        /// </summary>
        IUIElement? Parent { get; set; }

        /// <summary>
        /// Absolute position in screen coordinates
        /// </summary>
        Vector2 AbsolutePosition { get; }

        /// <summary>
        /// Bounding rectangle for hit testing
        /// </summary>
        Rectangle Bounds { get; }

        #endregion

        #region Events

        /// <summary>
        /// Fired when the element gains focus
        /// </summary>
        event EventHandler? OnFocus;

        /// <summary>
        /// Fired when the element loses focus
        /// </summary>
        event EventHandler? OnBlur;

        /// <summary>
        /// Fired when the element is clicked
        /// </summary>
        event EventHandler<UIClickEventArgs>? OnClick;

        /// <summary>
        /// Fired when the element is activated
        /// </summary>
        event EventHandler? OnActivated;

        /// <summary>
        /// Fired when the element is deactivated
        /// </summary>
        event EventHandler? OnDeactivated;

        #endregion

        #region Lifecycle Methods (DTXMania Pattern)

        /// <summary>
        /// Activate the element (equivalent to DTXMania's OnActivate)
        /// </summary>
        void Activate();

        /// <summary>
        /// Deactivate the element (equivalent to DTXMania's OnDeactivate)
        /// </summary>
        void Deactivate();

        /// <summary>
        /// Update the element's state (equivalent to DTXMania's On進行描画 update part)
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        void Update(double deltaTime);

        /// <summary>
        /// Draw the element (equivalent to DTXMania's On進行描画 draw part)
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="deltaTime">Time elapsed since last draw</param>
        void Draw(SpriteBatch spriteBatch, double deltaTime);

        #endregion

        #region Input Handling

        /// <summary>
        /// Handle input events
        /// </summary>
        /// <param name="inputState">Current input state</param>
        /// <returns>True if input was handled, false to pass to parent</returns>
        bool HandleInput(IInputState inputState);

        #endregion

        #region Hit Testing

        /// <summary>
        /// Test if a point is within this element
        /// </summary>
        /// <param name="point">Point to test in absolute coordinates</param>
        /// <returns>True if point is within element</returns>
        bool HitTest(Vector2 point);

        #endregion
    }

    /// <summary>
    /// Event arguments for UI click events
    /// </summary>
    public class UIClickEventArgs : EventArgs
    {
        public Vector2 Position { get; }
        public MouseButton Button { get; }

        public UIClickEventArgs(Vector2 position, MouseButton button)
        {
            Position = position;
            Button = button;
        }
    }

    /// <summary>
    /// Mouse button enumeration
    /// </summary>
    public enum MouseButton
    {
        Left,
        Right,
        Middle
    }
}
