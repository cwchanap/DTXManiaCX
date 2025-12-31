#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.UI.Components
{
    /// <summary>
    /// Scrollable list UI component with item selection and keyboard navigation
    /// Supports dynamic item management and customizable appearance
    /// </summary>
    public class UIList : UIElement
    {
        #region Private Fields

        private readonly List<UIListItem> _items;
        private int _selectedIndex = -1;
        private int _scrollOffset = 0;
        private int _visibleItemCount = 5;
        private float _itemHeight = 30f;
        private Color _backgroundColor = Color.DarkGray;
        private Color _selectedItemColor = Color.Blue;
        private Color _hoverItemColor = Color.LightGray;
        private Color _textColor = Color.White;
        private Color _selectedTextColor = Color.White;
        private SpriteFont? _font;
        private Texture2D? _backgroundTexture;
        private bool _allowMultipleSelection = false;
        private int _hoveredIndex = -1;

        #endregion

        #region Constructor

        public UIList()
        {
            _items = new List<UIListItem>();
            Size = new Vector2(200, _visibleItemCount * _itemHeight);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Items in the list
        /// </summary>
        public IReadOnlyList<UIListItem> Items => _items.AsReadOnly();

        /// <summary>
        /// Currently selected item index (-1 if none selected)
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var newIndex = Math.Max(-1, Math.Min(value, _items.Count - 1));
                if (_selectedIndex != newIndex)
                {
                    var oldIndex = _selectedIndex;
                    _selectedIndex = newIndex;
                    OnSelectionChanged(oldIndex, _selectedIndex);
                    EnsureSelectedItemVisible();
                }
            }
        }

        /// <summary>
        /// Currently selected item (null if none selected)
        /// </summary>
        public UIListItem? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        /// <summary>
        /// Number of visible items in the list
        /// </summary>
        public int VisibleItemCount
        {
            get => _visibleItemCount;
            set
            {
                if (_visibleItemCount != value && value > 0)
                {
                    _visibleItemCount = value;
                    Size = new Vector2(Size.X, _visibleItemCount * _itemHeight);
                }
            }
        }

        /// <summary>
        /// Height of each item in pixels
        /// </summary>
        public float ItemHeight
        {
            get => _itemHeight;
            set
            {
                if (_itemHeight != value && value > 0)
                {
                    _itemHeight = value;
                    Size = new Vector2(Size.X, _visibleItemCount * _itemHeight);
                }
            }
        }

        /// <summary>
        /// Background color of the list
        /// </summary>
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set => _backgroundColor = value;
        }

        /// <summary>
        /// Color of selected items
        /// </summary>
        public Color SelectedItemColor
        {
            get => _selectedItemColor;
            set => _selectedItemColor = value;
        }

        /// <summary>
        /// Color of hovered items
        /// </summary>
        public Color HoverItemColor
        {
            get => _hoverItemColor;
            set => _hoverItemColor = value;
        }

        /// <summary>
        /// Text color for normal items
        /// </summary>
        public Color TextColor
        {
            get => _textColor;
            set => _textColor = value;
        }

        /// <summary>
        /// Text color for selected items
        /// </summary>
        public Color SelectedTextColor
        {
            get => _selectedTextColor;
            set => _selectedTextColor = value;
        }

        /// <summary>
        /// Font used for item text
        /// </summary>
        public SpriteFont? Font
        {
            get => _font;
            set => _font = value;
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
        /// Whether multiple items can be selected
        /// </summary>
        public bool AllowMultipleSelection
        {
            get => _allowMultipleSelection;
            set => _allowMultipleSelection = value;
        }

        /// <summary>
        /// Current scroll offset
        /// </summary>
        public int ScrollOffset
        {
            get => _scrollOffset;
            set => _scrollOffset = Math.Max(0, Math.Min(value, Math.Max(0, _items.Count - _visibleItemCount)));
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when the selection changes
        /// </summary>
        public event EventHandler<ListSelectionChangedEventArgs>? SelectionChanged;

        /// <summary>
        /// Fired when an item is double-clicked or activated
        /// </summary>
        public event EventHandler<ListItemActivatedEventArgs>? ItemActivated;

        #endregion

        #region Public Methods

        /// <summary>
        /// Add an item to the list
        /// </summary>
        /// <param name="item">Item to add</param>
        public void AddItem(UIListItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            _items.Add(item);
        }

        /// <summary>
        /// Add an item with text
        /// </summary>
        /// <param name="text">Item text</param>
        /// <param name="data">Optional data object</param>
        /// <returns>Created item</returns>
        public UIListItem AddItem(string text, object? data = null)
        {
            var item = new UIListItem(text, data);
            AddItem(item);
            return item;
        }

        /// <summary>
        /// Remove an item from the list
        /// </summary>
        /// <param name="item">Item to remove</param>
        /// <returns>True if item was removed</returns>
        public bool RemoveItem(UIListItem item)
        {
            var index = _items.IndexOf(item);
            if (index >= 0)
            {
                return RemoveItemAt(index);
            }
            return false;
        }

        /// <summary>
        /// Remove item at specified index
        /// </summary>
        /// <param name="index">Index of item to remove</param>
        /// <returns>True if item was removed</returns>
        public bool RemoveItemAt(int index)
        {
            if (index < 0 || index >= _items.Count)
                return false;

            _items.RemoveAt(index);

            // Adjust selection if necessary
            if (_selectedIndex == index)
            {
                SelectedIndex = -1;
            }
            else if (_selectedIndex > index)
            {
                _selectedIndex--;
            }

            return true;
        }

        /// <summary>
        /// Clear all items
        /// </summary>
        public void ClearItems()
        {
            _items.Clear();
            SelectedIndex = -1;
            ScrollOffset = 0;
        }

        /// <summary>
        /// Scroll to show the specified item
        /// </summary>
        /// <param name="index">Item index</param>
        public void ScrollToItem(int index)
        {
            if (index < 0 || index >= _items.Count)
                return;

            if (index < _scrollOffset)
            {
                ScrollOffset = index;
            }
            else if (index >= _scrollOffset + _visibleItemCount)
            {
                ScrollOffset = index - _visibleItemCount + 1;
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

            // Draw visible items
            DrawItems(spriteBatch, bounds);

            base.OnDraw(spriteBatch, deltaTime);
        }

        protected override bool OnHandleInput(IInputState inputState)
        {
            if (!Enabled)
                return false;

            // Handle mouse input
            if (HandleMouseInput(inputState))
                return true;

            // Handle keyboard input
            if (HandleKeyboardInput(inputState))
                return true;

            return base.OnHandleInput(inputState);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Draw the list background
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="bounds">List bounds</param>
        private void DrawBackground(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_backgroundTexture != null && _backgroundColor != Color.Transparent)
            {
                spriteBatch.Draw(_backgroundTexture, bounds, _backgroundColor);
            }
        }

        /// <summary>
        /// Draw visible list items
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="bounds">List bounds</param>
        private void DrawItems(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Don't return early if font is null - we can still draw item backgrounds and indicators
            int endIndex = Math.Min(_scrollOffset + _visibleItemCount, _items.Count);

            for (int i = _scrollOffset; i < endIndex; i++)
            {
                var item = _items[i];
                var itemBounds = new Rectangle(
                    bounds.X,
                    bounds.Y + (i - _scrollOffset) * (int)_itemHeight,
                    bounds.Width,
                    (int)_itemHeight
                );

                DrawItem(spriteBatch, item, itemBounds, i);
            }
        }

        /// <summary>
        /// Draw a single list item
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="item">Item to draw</param>
        /// <param name="bounds">Item bounds</param>
        /// <param name="index">Item index</param>
        private void DrawItem(SpriteBatch spriteBatch, UIListItem item, Rectangle bounds, int index)
        {
            // Determine item colors
            Color backgroundColor = Color.Transparent;
            Color textColor = _textColor;

            if (index == _selectedIndex)
            {
                backgroundColor = _selectedItemColor;
                textColor = _selectedTextColor;
            }
            else if (index == _hoveredIndex)
            {
                backgroundColor = _hoverItemColor;
            }



            // Draw item background - always draw something to make the list visible
            if (_backgroundTexture != null)
            {
                // If we have a specific background color, use it
                if (backgroundColor != Color.Transparent)
                {
                    spriteBatch.Draw(_backgroundTexture, bounds, backgroundColor);
                }
                else
                {
                    // Draw a subtle border to show the item exists even when not selected
                    var borderColor = Color.Gray * 0.3f; // Very subtle
                    spriteBatch.Draw(_backgroundTexture, bounds, borderColor);
                }
            }

            // Draw item text
            if (_font != null && !string.IsNullOrEmpty(item.Text))
            {
                var textPosition = new Vector2(bounds.X + 5, bounds.Y + (bounds.Height - _font.LineSpacing) / 2);
                spriteBatch.DrawString(_font, item.Text, textPosition, textColor);
            }
            else if (_font == null && _backgroundTexture != null)
            {
                // If no font available, draw a more visible indicator rectangle to show the item exists
                var indicatorBounds = new Rectangle(bounds.X + 5, bounds.Y + bounds.Height / 2 - 2, bounds.Width - 10, 4);
                var indicatorColor = index == _selectedIndex ? Color.Yellow : Color.White;
                spriteBatch.Draw(_backgroundTexture, indicatorBounds, indicatorColor);
            }
        }

        /// <summary>
        /// Handle mouse input
        /// </summary>
        /// <param name="inputState">Input state</param>
        /// <returns>True if input was handled</returns>
        private bool HandleMouseInput(IInputState inputState)
        {
            var mousePos = inputState.MousePosition;
            var bounds = Bounds;

            if (!bounds.Contains(mousePos))
            {
                _hoveredIndex = -1;
                return false;
            }

            // Calculate hovered item
            int relativeY = (int)(mousePos.Y - bounds.Y);
            int hoveredIndex = _scrollOffset + (int)(relativeY / _itemHeight);

            if (hoveredIndex >= 0 && hoveredIndex < _items.Count)
            {
                _hoveredIndex = hoveredIndex;

                // Handle click
                if (inputState.IsMouseButtonPressed(MouseButton.Left))
                {
                    SelectedIndex = hoveredIndex;
                    return true;
                }

                // Handle double-click (simplified)
                // In a real implementation, you'd track click timing
            }

            // Handle scroll wheel
            if (inputState.ScrollWheelDelta != 0)
            {
                ScrollOffset -= Math.Sign(inputState.ScrollWheelDelta);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handle keyboard input
        /// </summary>
        /// <param name="inputState">Input state</param>
        /// <returns>True if input was handled</returns>
        private bool HandleKeyboardInput(IInputState inputState)
        {
            if (inputState.IsKeyPressed(Keys.Up))
            {
                if (_selectedIndex > 0)
                    SelectedIndex = _selectedIndex - 1;
                else if (_items.Count > 0)
                    SelectedIndex = _items.Count - 1; // Wrap to end
                return true;
            }

            if (inputState.IsKeyPressed(Keys.Down))
            {
                if (_selectedIndex < _items.Count - 1)
                    SelectedIndex = _selectedIndex + 1;
                else if (_items.Count > 0)
                    SelectedIndex = 0; // Wrap to beginning
                return true;
            }

            if (inputState.IsKeyPressed(Keys.Home))
            {
                if (_items.Count > 0)
                    SelectedIndex = 0;
                return true;
            }

            if (inputState.IsKeyPressed(Keys.End))
            {
                if (_items.Count > 0)
                    SelectedIndex = _items.Count - 1;
                return true;
            }

            if (inputState.IsKeyPressed(Keys.Enter))
            {
                if (_selectedIndex >= 0)
                {
                    ItemActivated?.Invoke(this, new ListItemActivatedEventArgs(_selectedIndex, SelectedItem!));
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ensure the selected item is visible
        /// </summary>
        private void EnsureSelectedItemVisible()
        {
            if (_selectedIndex >= 0)
            {
                ScrollToItem(_selectedIndex);
            }
        }

        /// <summary>
        /// Called when selection changes
        /// </summary>
        /// <param name="oldIndex">Previous selected index</param>
        /// <param name="newIndex">New selected index</param>
        private void OnSelectionChanged(int oldIndex, int newIndex)
        {
            SelectionChanged?.Invoke(this, new ListSelectionChangedEventArgs(oldIndex, newIndex));
        }

        #endregion
    }

    /// <summary>
    /// Represents an item in a UIList
    /// </summary>
    public class UIListItem
    {
        /// <summary>
        /// Text displayed for this item
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Optional data object associated with this item
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Whether this item is selected (for multi-selection lists)
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// Whether this item is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="text">Item text</param>
        /// <param name="data">Optional data object</param>
        public UIListItem(string text, object? data = null)
        {
            Text = text ?? string.Empty;
            Data = data;
        }

        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>
    /// Event arguments for list selection changes
    /// </summary>
    public class ListSelectionChangedEventArgs : EventArgs
    {
        public int OldIndex { get; }
        public int NewIndex { get; }

        public ListSelectionChangedEventArgs(int oldIndex, int newIndex)
        {
            OldIndex = oldIndex;
            NewIndex = newIndex;
        }
    }

    /// <summary>
    /// Event arguments for list item activation
    /// </summary>
    public class ListItemActivatedEventArgs : EventArgs
    {
        public int Index { get; }
        public UIListItem Item { get; }

        public ListItemActivatedEventArgs(int index, UIListItem item)
        {
            Index = index;
            Item = item;
        }
    }
}
