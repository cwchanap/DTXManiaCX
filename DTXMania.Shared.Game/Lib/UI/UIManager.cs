using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace DTX.UI
{
    /// <summary>
    /// Central manager for the UI system
    /// Coordinates input, updates, and rendering for all UI elements
    /// </summary>
    public class UIManager : IDisposable
    {
        #region Private Fields

        private readonly InputStateManager _inputStateManager;
        private readonly List<UIContainer> _rootContainers;
        private UIContainer? _focusedContainer;
        private bool _disposed = false;

        #endregion

        #region Constructor

        public UIManager()
        {
            _inputStateManager = new InputStateManager();
            _rootContainers = new List<UIContainer>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Input state manager for the UI system
        /// </summary>
        public IInputState InputState => _inputStateManager;

        /// <summary>
        /// Currently focused root container
        /// </summary>
        public UIContainer? FocusedContainer
        {
            get => _focusedContainer;
            set
            {
                if (_focusedContainer != value)
                {
                    // Remove focus from previous container
                    if (_focusedContainer != null)
                        _focusedContainer.Focused = false;

                    _focusedContainer = value;

                    // Set focus on new container
                    if (_focusedContainer != null)
                        _focusedContainer.Focused = true;
                }
            }
        }

        /// <summary>
        /// Read-only collection of root containers
        /// </summary>
        public IReadOnlyList<UIContainer> RootContainers => _rootContainers.AsReadOnly();

        #endregion

        #region Container Management

        /// <summary>
        /// Add a root container to the UI system
        /// </summary>
        /// <param name="container">Container to add</param>
        public void AddRootContainer(UIContainer container)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            if (_rootContainers.Contains(container))
                return;

            _rootContainers.Add(container);
            container.Activate();

            // Set as focused if no other container is focused
            if (_focusedContainer == null)
                FocusedContainer = container;
        }

        /// <summary>
        /// Remove a root container from the UI system
        /// </summary>
        /// <param name="container">Container to remove</param>
        /// <returns>True if container was removed</returns>
        public bool RemoveRootContainer(UIContainer container)
        {
            if (container == null)
                return false;

            if (!_rootContainers.Contains(container))
                return false;

            // Clear focus if this container was focused
            if (_focusedContainer == container)
            {
                FocusedContainer = _rootContainers.Count > 1 ? 
                    _rootContainers.Find(c => c != container) : null;
            }

            // Deactivate container
            if (container.IsActive)
                container.Deactivate();

            return _rootContainers.Remove(container);
        }

        /// <summary>
        /// Remove all root containers
        /// </summary>
        public void ClearRootContainers()
        {
            // Deactivate all containers
            foreach (var container in _rootContainers)
            {
                if (container.IsActive)
                    container.Deactivate();
            }

            _focusedContainer = null;
            _rootContainers.Clear();
        }

        #endregion

        #region Update and Draw

        /// <summary>
        /// Update the UI system
        /// Call this once per frame before drawing
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            // Update input state first
            _inputStateManager.Update();

            // Update all active root containers
            foreach (var container in _rootContainers)
            {
                if (container.IsActive)
                    container.Update(deltaTime);
            }

            // Handle input for containers in reverse order (top-most first)
            for (int i = _rootContainers.Count - 1; i >= 0; i--)
            {
                var container = _rootContainers[i];
                if (container.IsActive && container.Enabled && 
                    container.HandleInput(_inputStateManager))
                {
                    // Container handled the input, stop processing
                    break;
                }
            }
        }

        /// <summary>
        /// Draw the UI system
        /// Call this during the draw phase
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="deltaTime">Time elapsed since last draw</param>
        public void Draw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (_disposed)
                return;

            // Draw all visible root containers in order
            foreach (var container in _rootContainers)
            {
                if (container.IsActive && container.Visible)
                    container.Draw(spriteBatch, deltaTime);
            }
        }

        #endregion

        #region Focus Management

        /// <summary>
        /// Move focus to the next root container
        /// </summary>
        public void FocusNextContainer()
        {
            if (_rootContainers.Count <= 1)
                return;

            int currentIndex = _focusedContainer != null ? 
                _rootContainers.IndexOf(_focusedContainer) : -1;
            int nextIndex = (currentIndex + 1) % _rootContainers.Count;
            FocusedContainer = _rootContainers[nextIndex];
        }

        /// <summary>
        /// Move focus to the previous root container
        /// </summary>
        public void FocusPreviousContainer()
        {
            if (_rootContainers.Count <= 1)
                return;

            int currentIndex = _focusedContainer != null ? 
                _rootContainers.IndexOf(_focusedContainer) : 0;
            int previousIndex = (currentIndex - 1 + _rootContainers.Count) % _rootContainers.Count;
            FocusedContainer = _rootContainers[previousIndex];
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Find the UI element at the specified screen position
        /// </summary>
        /// <param name="position">Screen position to test</param>
        /// <returns>UI element at position, or null if none found</returns>
        public IUIElement? GetElementAtPosition(Vector2 position)
        {
            // Check containers in reverse order (top-most first)
            for (int i = _rootContainers.Count - 1; i >= 0; i--)
            {
                var container = _rootContainers[i];
                if (container.IsActive && container.Visible && container.HitTest(position))
                {
                    return FindElementAtPosition(container, position);
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively find the deepest element at the specified position
        /// </summary>
        /// <param name="element">Element to search</param>
        /// <param name="position">Position to test</param>
        /// <returns>Deepest element at position</returns>
        private IUIElement? FindElementAtPosition(IUIElement element, Vector2 position)
        {
            if (element is UIContainer container)
            {
                // Check children in reverse order (top-most first)
                for (int i = container.Children.Count - 1; i >= 0; i--)
                {
                    var child = container.Children[i];
                    if (child.IsActive && child.Visible && child.HitTest(position))
                    {
                        var deeperElement = FindElementAtPosition(child, position);
                        return deeperElement ?? child;
                    }
                }
            }

            return element.HitTest(position) ? element : null;
        }

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
                    // Dispose all root containers
                    foreach (var container in _rootContainers)
                    {
                        container.Dispose();
                    }
                    ClearRootContainers();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
