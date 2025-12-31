using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Game.Lib.UI
{
    /// <summary>
    /// Abstract base class for UI elements.
    /// Implements common functionality following DTXMania's CActivity pattern.
    /// </summary>
    /// <remarks>
    /// Nullable reference types are disabled. The Parent property may be null
    /// for root elements; callers should check before dereferencing.
    /// </remarks>
    public abstract class UIElement : IUIElement
    {
        #region Private Fields

        private Vector2 _position;
        private Vector2 _size;
        private bool _visible = true;
        private bool _enabled = true;
        private bool _focused = false;
        private bool _isActive = false;
        private IUIElement _parent;
        private bool _disposed = false;

        /// <summary>
        /// Flag indicating if this is the first update after activation
        /// (equivalent to DTXMania's bJustStartedUpdate)
        /// </summary>
        protected bool IsFirstUpdate { get; private set; } = true;

        #endregion

        #region Properties

        public Vector2 Position
        {
            get => _position;
            set
            {
                if (_position != value)
                {
                    _position = value;
                    OnPositionChanged();
                }
            }
        }

        public Vector2 Size
        {
            get => _size;
            set
            {
                if (_size != value)
                {
                    _size = value;
                    OnSizeChanged();
                }
            }
        }

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public bool Focused
        {
            get => _focused;
            set
            {
                if (_focused != value)
                {
                    _focused = value;
                    if (_focused)
                        OnFocus?.Invoke(this, EventArgs.Empty);
                    else
                        OnBlur?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool IsActive => _isActive;

        public IUIElement Parent
        {
            get => _parent;
            set => _parent = value;
        }

        public Vector2 AbsolutePosition
        {
            get
            {
                if (_parent != null)
                    return _parent.AbsolutePosition + _position;
                return _position;
            }
        }

        public Rectangle Bounds
        {
            get
            {
                var absPos = AbsolutePosition;
                return new Rectangle((int)absPos.X, (int)absPos.Y, (int)_size.X, (int)_size.Y);
            }
        }

        #endregion

        #region Events

        public event EventHandler OnFocus;
        public event EventHandler OnBlur;
        public event EventHandler<UIClickEventArgs> OnClick;
        public event EventHandler OnActivated;
        public event EventHandler OnDeactivated;

        #endregion

        #region Lifecycle Methods

        public virtual void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            IsFirstUpdate = true;

            // Create resources (equivalent to DTXMania's OnManagedCreateResources)
            OnCreateResources();

            // Notify activation
            OnActivated?.Invoke(this, EventArgs.Empty);
        }

        public virtual void Deactivate()
        {
            if (!_isActive)
                return;

            // Release resources (equivalent to DTXMania's OnManagedReleaseResources)
            OnReleaseResources();

            _isActive = false;

            // Notify deactivation
            OnDeactivated?.Invoke(this, EventArgs.Empty);
        }

        public virtual void Update(double deltaTime)
        {
            if (!_isActive)
                return;

            // Perform update logic
            OnUpdate(deltaTime);

            // Clear first update flag after first update
            if (IsFirstUpdate)
                IsFirstUpdate = false;
        }

        public virtual void Draw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!_isActive || !_visible)
                return;

            OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Input Handling

        public virtual bool HandleInput(IInputState inputState)
        {
            if (!_isActive || !_enabled)
                return false;

            // Check for mouse clicks
            if (inputState.IsMouseButtonPressed(MouseButton.Left))
            {
                if (HitTest(inputState.MousePosition))
                {
                    OnClick?.Invoke(this, new UIClickEventArgs(inputState.MousePosition, MouseButton.Left));
                    return true;
                }
            }

            return OnHandleInput(inputState);
        }

        #endregion

        #region Hit Testing

        public virtual bool HitTest(Vector2 point)
        {
            return Bounds.Contains(point);
        }

        #endregion

        #region Protected Virtual Methods (for derived classes)

        /// <summary>
        /// Called when resources should be created (equivalent to DTXMania's OnManagedCreateResources)
        /// </summary>
        protected virtual void OnCreateResources() { }

        /// <summary>
        /// Called when resources should be released (equivalent to DTXMania's OnManagedReleaseResources)
        /// </summary>
        protected virtual void OnReleaseResources() { }

        /// <summary>
        /// Called during update phase (equivalent to DTXMania's OnUpdateAndDraw update part)
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        protected virtual void OnUpdate(double deltaTime) { }

        /// <summary>
        /// Called during draw phase (equivalent to DTXMania's OnUpdateAndDraw draw part)
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="deltaTime">Time elapsed since last draw</param>
        protected virtual void OnDraw(SpriteBatch spriteBatch, double deltaTime) { }

        /// <summary>
        /// Called when input should be handled by this element
        /// </summary>
        /// <param name="inputState">Current input state</param>
        /// <returns>True if input was handled</returns>
        protected virtual bool OnHandleInput(IInputState inputState) => false;

        /// <summary>
        /// Called when position changes
        /// </summary>
        protected virtual void OnPositionChanged() { }

        /// <summary>
        /// Called when size changes
        /// </summary>
        protected virtual void OnSizeChanged() { }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_isActive)
                        Deactivate();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
