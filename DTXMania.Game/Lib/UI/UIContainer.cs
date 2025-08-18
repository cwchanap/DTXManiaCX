#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.UI
{
    /// <summary>
    /// Container for UI elements that manages child elements
    /// Equivalent to DTXMania's listChildActivities pattern
    /// </summary>
    public class UIContainer : UIElement
    {
        #region Private Fields

        private readonly List<IUIElement> _children;
        private IUIElement? _focusedChild;

        #endregion

        #region Constructor

        public UIContainer()
        {
            _children = new List<IUIElement>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Read-only collection of child elements
        /// </summary>
        public IReadOnlyList<IUIElement> Children => _children.AsReadOnly();

        /// <summary>
        /// Currently focused child element
        /// </summary>
        public IUIElement? FocusedChild
        {
            get => _focusedChild;
            set
            {
                if (_focusedChild != value)
                {
                    // Remove focus from previous child
                    if (_focusedChild != null)
                        _focusedChild.Focused = false;

                    _focusedChild = value;

                    // Set focus on new child
                    if (_focusedChild != null)
                        _focusedChild.Focused = true;
                }
            }
        }

        #endregion

        #region Child Management

        /// <summary>
        /// Add a child element to this container
        /// </summary>
        /// <param name="child">Child element to add</param>
        public virtual void AddChild(IUIElement child)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));

            if (_children.Contains(child))
                return;

            _children.Add(child);
            child.Parent = this;

            // If container is active, activate the child
            if (IsActive)
                child.Activate();
        }

        /// <summary>
        /// Remove a child element from this container
        /// </summary>
        /// <param name="child">Child element to remove</param>
        /// <returns>True if child was removed</returns>
        public virtual bool RemoveChild(IUIElement child)
        {
            if (child == null)
                return false;

            if (!_children.Contains(child))
                return false;

            // Clear focus if this child was focused
            if (_focusedChild == child)
                FocusedChild = null;

            // Deactivate child if container is active
            if (IsActive && child.IsActive)
                child.Deactivate();

            child.Parent = null;
            return _children.Remove(child);
        }

        /// <summary>
        /// Remove all child elements
        /// </summary>
        public virtual void ClearChildren()
        {
            // Deactivate all children if container is active
            if (IsActive)
            {
                foreach (var child in _children.Where(c => c.IsActive))
                {
                    child.Deactivate();
                }
            }

            // Clear focus
            FocusedChild = null;

            // Clear parent references
            foreach (var child in _children)
            {
                child.Parent = null;
            }

            _children.Clear();
        }

        /// <summary>
        /// Get child at specified index
        /// </summary>
        /// <param name="index">Index of child</param>
        /// <returns>Child element</returns>
        public IUIElement GetChild(int index)
        {
            if (index < 0 || index >= _children.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _children[index];
        }

        /// <summary>
        /// Get index of specified child
        /// </summary>
        /// <param name="child">Child element</param>
        /// <returns>Index of child, or -1 if not found</returns>
        public int GetChildIndex(IUIElement child)
        {
            return _children.IndexOf(child);
        }

        #endregion

        #region Focus Navigation

        /// <summary>
        /// Move focus to next focusable child
        /// </summary>
        public virtual void FocusNext()
        {
            var focusableChildren = _children.Where(c => c.Enabled && c.Visible).ToList();
            if (focusableChildren.Count == 0)
                return;

            int currentIndex = _focusedChild != null ? focusableChildren.IndexOf(_focusedChild) : -1;
            int nextIndex = (currentIndex + 1) % focusableChildren.Count;
            FocusedChild = focusableChildren[nextIndex];
        }

        /// <summary>
        /// Move focus to previous focusable child
        /// </summary>
        public virtual void FocusPrevious()
        {
            var focusableChildren = _children.Where(c => c.Enabled && c.Visible).ToList();
            if (focusableChildren.Count == 0)
                return;

            int currentIndex = _focusedChild != null ? focusableChildren.IndexOf(_focusedChild) : 0;
            int previousIndex = (currentIndex - 1 + focusableChildren.Count) % focusableChildren.Count;
            FocusedChild = focusableChildren[previousIndex];
        }

        #endregion

        #region Overridden Methods

        public override void Activate()
        {
            base.Activate();

            // Activate all children (equivalent to DTXMania's child activity activation)
            foreach (var child in _children)
            {
                child.Activate();
            }
        }

        public override void Deactivate()
        {
            // Deactivate all children first
            foreach (var child in _children.Where(c => c.IsActive))
            {
                child.Deactivate();
            }

            base.Deactivate();
        }

        protected override void OnUpdate(double deltaTime)
        {
            base.OnUpdate(deltaTime);

            // Update all active children
            foreach (var child in _children.Where(c => c.IsActive))
            {
                child.Update(deltaTime);
            }
        }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            base.OnDraw(spriteBatch, deltaTime);

            // Draw all visible children in order (Z-order)
            foreach (var child in _children.Where(c => c.IsActive && c.Visible))
            {
                child.Draw(spriteBatch, deltaTime);
            }
        }

        protected override bool OnHandleInput(IInputState inputState)
        {
            // Handle input for children in reverse order (top-most first)
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                var child = _children[i];
                if (child.IsActive && child.Enabled && child.HandleInput(inputState))
                {
                    // Child handled the input
                    return true;
                }
            }

            return base.OnHandleInput(inputState);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose all children
                foreach (var child in _children.ToList())
                {
                    child.Dispose();
                }
                ClearChildren();
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
