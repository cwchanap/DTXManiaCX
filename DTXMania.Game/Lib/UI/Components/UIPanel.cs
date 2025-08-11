using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Game.Lib.UI.Components
{
    /// <summary>
    /// Panel UI component - container with background and border rendering
    /// Supports child element layout and visual styling
    /// </summary>
    public class UIPanel : UIContainer
    {
        #region Private Fields

        private Color _backgroundColor = Color.Transparent;
        private Texture2D? _backgroundTexture;
        private Rectangle? _backgroundSourceRectangle;
        private Color _borderColor = Color.Gray;
        private int _borderThickness = 0;
        private Texture2D? _borderTexture;
        private PanelLayoutMode _layoutMode = PanelLayoutMode.Manual;
        private Vector2 _padding = Vector2.Zero;
        private float _spacing = 0f;

        #endregion

        #region Constructor

        public UIPanel()
        {
            // Default panel size
            Size = new Vector2(200, 150);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Background color of the panel
        /// </summary>
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set => _backgroundColor = value;
        }

        /// <summary>
        /// Background texture (optional)
        /// </summary>
        public Texture2D? BackgroundTexture
        {
            get => _backgroundTexture;
            set => _backgroundTexture = value;
        }

        /// <summary>
        /// Source rectangle for background texture (null for full texture)
        /// </summary>
        public Rectangle? BackgroundSourceRectangle
        {
            get => _backgroundSourceRectangle;
            set => _backgroundSourceRectangle = value;
        }

        /// <summary>
        /// Border color
        /// </summary>
        public Color BorderColor
        {
            get => _borderColor;
            set => _borderColor = value;
        }

        /// <summary>
        /// Border thickness in pixels
        /// </summary>
        public int BorderThickness
        {
            get => _borderThickness;
            set => _borderThickness = Math.Max(0, value);
        }

        /// <summary>
        /// Border texture (optional, for textured borders)
        /// </summary>
        public Texture2D? BorderTexture
        {
            get => _borderTexture;
            set => _borderTexture = value;
        }

        /// <summary>
        /// Layout mode for child elements
        /// </summary>
        public PanelLayoutMode LayoutMode
        {
            get => _layoutMode;
            set
            {
                if (_layoutMode != value)
                {
                    _layoutMode = value;
                    InvalidateLayout();
                }
            }
        }

        /// <summary>
        /// Padding around child elements
        /// </summary>
        public Vector2 Padding
        {
            get => _padding;
            set
            {
                if (_padding != value)
                {
                    _padding = value;
                    InvalidateLayout();
                }
            }
        }

        /// <summary>
        /// Spacing between child elements (for automatic layouts)
        /// </summary>
        public float Spacing
        {
            get => _spacing;
            set
            {
                if (_spacing != value)
                {
                    _spacing = value;
                    InvalidateLayout();
                }
            }
        }

        /// <summary>
        /// Content area (panel area minus padding and border)
        /// </summary>
        public Rectangle ContentArea
        {
            get
            {
                var bounds = Bounds;
                return new Rectangle(
                    bounds.X + _borderThickness + (int)_padding.X,
                    bounds.Y + _borderThickness + (int)_padding.Y,
                    bounds.Width - 2 * _borderThickness - 2 * (int)_padding.X,
                    bounds.Height - 2 * _borderThickness - 2 * (int)_padding.Y
                );
            }
        }

        #endregion

        #region Overridden Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible)
                return;

            var bounds = Bounds;

            // Draw background
            DrawBackground(spriteBatch, bounds);

            // Draw border
            DrawBorder(spriteBatch, bounds);

            // Draw children (handled by base class)
            base.OnDraw(spriteBatch, deltaTime);
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            InvalidateLayout();
        }

        public override void AddChild(IUIElement child)
        {
            base.AddChild(child);
            InvalidateLayout();
        }

        public override bool RemoveChild(IUIElement child)
        {
            var result = base.RemoveChild(child);
            if (result)
                InvalidateLayout();
            return result;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Draw the panel background
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="bounds">Panel bounds</param>
        private void DrawBackground(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_backgroundColor == Color.Transparent && _backgroundTexture == null)
                return;

            if (_backgroundTexture != null)
            {
                spriteBatch.Draw(_backgroundTexture, bounds, _backgroundSourceRectangle, _backgroundColor);
            }
            else if (_backgroundColor != Color.Transparent)
            {
                // Note: For solid color backgrounds, we would need a white pixel texture
                // This would typically be provided by the graphics manager
            }
        }

        /// <summary>
        /// Draw the panel border
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="bounds">Panel bounds</param>
        private void DrawBorder(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_borderThickness <= 0)
                return;

            if (_borderTexture != null)
            {
                // Draw textured border (would need more complex logic for proper border rendering)
                // This is a simplified version
                spriteBatch.Draw(_borderTexture, bounds, _borderColor);
            }
            else
            {
                // Note: Border drawing would require line drawing utilities or border textures
                // This would typically be provided by the graphics manager
            }
        }

        /// <summary>
        /// Invalidate the current layout and trigger re-layout
        /// </summary>
        private void InvalidateLayout()
        {
            if (_layoutMode != PanelLayoutMode.Manual)
            {
                PerformLayout();
            }
        }

        /// <summary>
        /// Perform automatic layout of child elements
        /// </summary>
        private void PerformLayout()
        {
            if (Children.Count == 0)
                return;

            var contentArea = ContentArea;

            switch (_layoutMode)
            {
                case PanelLayoutMode.Vertical:
                    LayoutVertical(contentArea);
                    break;
                case PanelLayoutMode.Horizontal:
                    LayoutHorizontal(contentArea);
                    break;
                case PanelLayoutMode.Grid:
                    LayoutGrid(contentArea);
                    break;
                case PanelLayoutMode.Manual:
                default:
                    // No automatic layout
                    break;
            }
        }

        /// <summary>
        /// Layout children vertically
        /// </summary>
        /// <param name="contentArea">Available content area</param>
        private void LayoutVertical(Rectangle contentArea)
        {
            float currentY = contentArea.Y;
            
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;

                child.Position = new Vector2(contentArea.X, currentY);
                currentY += child.Size.Y + _spacing;
            }
        }

        /// <summary>
        /// Layout children horizontally
        /// </summary>
        /// <param name="contentArea">Available content area</param>
        private void LayoutHorizontal(Rectangle contentArea)
        {
            float currentX = contentArea.X;
            
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;

                child.Position = new Vector2(currentX, contentArea.Y);
                currentX += child.Size.X + _spacing;
            }
        }

        /// <summary>
        /// Layout children in a grid
        /// </summary>
        /// <param name="contentArea">Available content area</param>
        private void LayoutGrid(Rectangle contentArea)
        {
            // Simple grid layout - calculate columns based on content width
            if (Children.Count == 0)
                return;

            var firstChild = Children[0];
            int columns = Math.Max(1, (int)(contentArea.Width / (firstChild.Size.X + _spacing)));
            
            int row = 0, col = 0;
            
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;

                float x = contentArea.X + col * (firstChild.Size.X + _spacing);
                float y = contentArea.Y + row * (firstChild.Size.Y + _spacing);
                
                child.Position = new Vector2(x, y);
                
                col++;
                if (col >= columns)
                {
                    col = 0;
                    row++;
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Panel layout modes
    /// </summary>
    public enum PanelLayoutMode
    {
        /// <summary>
        /// Manual positioning of child elements
        /// </summary>
        Manual,

        /// <summary>
        /// Arrange children vertically
        /// </summary>
        Vertical,

        /// <summary>
        /// Arrange children horizontally
        /// </summary>
        Horizontal,

        /// <summary>
        /// Arrange children in a grid
        /// </summary>
        Grid
    }
}
