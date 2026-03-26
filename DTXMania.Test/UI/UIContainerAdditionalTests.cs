using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;
using Xunit;
using System;
using DTXMania.Test;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Additional unit tests for UIContainer edge cases not covered by UIArchitectureTests.
    /// </summary>
    [Trait("Category", "Unit")]
    public class UIContainerAdditionalTests
    {
        #region ClearChildren Tests

        [Fact]
        public void ClearChildren_ShouldRemoveAllChildren()
        {
            var container = new UIContainer();
            container.AddChild(new ConcreteUIElement());
            container.AddChild(new ConcreteUIElement());
            container.AddChild(new ConcreteUIElement());

            container.ClearChildren();

            Assert.Empty(container.Children);
        }

        [Fact]
        public void ClearChildren_ShouldClearFocusedChild()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement { Enabled = true, Visible = true };
            container.AddChild(child);
            container.FocusedChild = child;

            container.ClearChildren();

            Assert.Null(container.FocusedChild);
        }

        [Fact]
        public void ClearChildren_WhenActive_ShouldDeactivateChildren()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement();
            container.AddChild(child);
            container.Activate(); // Activates child too

            Assert.True(child.IsActive);
            container.ClearChildren();
            Assert.False(child.IsActive);
        }

        [Fact]
        public void ClearChildren_ShouldClearParentReferenceOnChildren()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement();
            container.AddChild(child);

            container.ClearChildren();

            Assert.Null(child.Parent);
        }

        #endregion

        #region GetChild Tests

        [Fact]
        public void GetChild_ValidIndex_ShouldReturnCorrectChild()
        {
            var container = new UIContainer();
            var child1 = new ConcreteUIElement();
            var child2 = new ConcreteUIElement();
            container.AddChild(child1);
            container.AddChild(child2);

            Assert.Equal(child1, container.GetChild(0));
            Assert.Equal(child2, container.GetChild(1));
        }

        [Fact]
        public void GetChild_NegativeIndex_ShouldThrowArgumentOutOfRangeException()
        {
            var container = new UIContainer();
            container.AddChild(new ConcreteUIElement());

            Assert.Throws<ArgumentOutOfRangeException>(() => container.GetChild(-1));
        }

        [Fact]
        public void GetChild_IndexBeyondCount_ShouldThrowArgumentOutOfRangeException()
        {
            var container = new UIContainer();
            container.AddChild(new ConcreteUIElement());

            Assert.Throws<ArgumentOutOfRangeException>(() => container.GetChild(5));
        }

        #endregion

        #region GetChildIndex Tests

        [Fact]
        public void GetChildIndex_ExistingChild_ShouldReturnCorrectIndex()
        {
            var container = new UIContainer();
            var child1 = new ConcreteUIElement();
            var child2 = new ConcreteUIElement();
            container.AddChild(child1);
            container.AddChild(child2);

            Assert.Equal(0, container.GetChildIndex(child1));
            Assert.Equal(1, container.GetChildIndex(child2));
        }

        [Fact]
        public void GetChildIndex_NonExistentChild_ShouldReturnNegativeOne()
        {
            var container = new UIContainer();
            var stranger = new ConcreteUIElement();

            Assert.Equal(-1, container.GetChildIndex(stranger));
        }

        #endregion

        #region FocusPrevious Tests

        [Fact]
        public void FocusPrevious_WithChildren_ShouldFocusLastChild()
        {
            var container = new UIContainer();
            var child1 = new ConcreteUIElement { Enabled = true, Visible = true };
            var child2 = new ConcreteUIElement { Enabled = true, Visible = true };
            container.AddChild(child1);
            container.AddChild(child2);

            // No current focus; FocusPrevious with currentIndex=0 should go to last
            container.FocusPrevious();

            // With no focused child, currentIndex=0, previousIndex=(0-1+2)%2=1
            Assert.Equal(child2, container.FocusedChild);
        }

        [Fact]
        public void FocusPrevious_WhenAtFirst_ShouldWrapToLast()
        {
            var container = new UIContainer();
            var child1 = new ConcreteUIElement { Enabled = true, Visible = true };
            var child2 = new ConcreteUIElement { Enabled = true, Visible = true };
            var child3 = new ConcreteUIElement { Enabled = true, Visible = true };
            container.AddChild(child1);
            container.AddChild(child2);
            container.AddChild(child3);

            container.FocusedChild = child1;
            container.FocusPrevious();

            Assert.Equal(child3, container.FocusedChild);
        }

        [Fact]
        public void FocusPrevious_NoChildren_ShouldNotThrow()
        {
            var container = new UIContainer();
            var ex = Record.Exception(() => container.FocusPrevious());
            Assert.Null(ex);
        }

        [Fact]
        public void FocusPrevious_NoFocusableChildren_ShouldNotThrow()
        {
            var container = new UIContainer();
            var hidden = new ConcreteUIElement { Visible = false };
            var disabled = new ConcreteUIElement { Enabled = false };
            container.AddChild(hidden);
            container.AddChild(disabled);

            var ex = Record.Exception(() => container.FocusPrevious());
            Assert.Null(ex);
        }

        #endregion

        #region FocusedChild Tests

        [Fact]
        public void FocusedChild_SetToChild_ShouldSetFocusOnChild()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement();
            container.AddChild(child);

            container.FocusedChild = child;

            Assert.True(child.Focused);
        }

        [Fact]
        public void FocusedChild_ChangingFocus_ShouldBlurOldChild()
        {
            var container = new UIContainer();
            var child1 = new ConcreteUIElement { Enabled = true, Visible = true };
            var child2 = new ConcreteUIElement { Enabled = true, Visible = true };
            container.AddChild(child1);
            container.AddChild(child2);

            container.FocusedChild = child1;
            Assert.True(child1.Focused);

            container.FocusedChild = child2;
            Assert.False(child1.Focused);
            Assert.True(child2.Focused);
        }

        [Fact]
        public void FocusedChild_SetToNull_ShouldBlurCurrentChild()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement();
            container.AddChild(child);
            container.FocusedChild = child;

            container.FocusedChild = null;

            Assert.False(child.Focused);
            Assert.Null(container.FocusedChild);
        }

        #endregion

        #region AddChild Duplicate Tests

        [Fact]
        public void AddChild_SameTwice_ShouldOnlyAddOnce()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement();
            container.AddChild(child);
            container.AddChild(child); // Duplicate

            Assert.Equal(1, container.Children.Count);
        }

        [Fact]
        public void AddChild_NullChild_ShouldThrowArgumentNullException()
        {
            var container = new UIContainer();
            Assert.Throws<ArgumentNullException>(() => container.AddChild(null));
        }

        #endregion

        #region RemoveChild Tests

        [Fact]
        public void RemoveChild_NullChild_ShouldReturnFalse()
        {
            var container = new UIContainer();
            var result = container.RemoveChild(null);
            Assert.False(result);
        }

        [Fact]
        public void RemoveChild_FocusedChild_ShouldClearFocus()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement();
            container.AddChild(child);
            container.FocusedChild = child;

            container.RemoveChild(child);

            Assert.Null(container.FocusedChild);
        }

        [Fact]
        public void RemoveChild_ShouldClearParentReference()
        {
            var container = new UIContainer();
            var child = new ConcreteUIElement();
            container.AddChild(child);

            container.RemoveChild(child);

            Assert.Null(child.Parent);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldDisposeAllChildren()
        {
            var container = new UIContainer();
            var child1 = new ConcreteUIElement();
            var child2 = new ConcreteUIElement();
            container.AddChild(child1);
            container.AddChild(child2);
            container.Activate();

            container.Dispose();

            // Children should be deactivated after container disposal
            Assert.False(child1.IsActive);
            Assert.False(child2.IsActive);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var container = new UIContainer();
            container.AddChild(new ConcreteUIElement());
            container.Dispose();
            var ex = Record.Exception(() => container.Dispose());
            Assert.Null(ex);
        }

        #endregion

        #region Deactivation Cascade Tests

        [Fact]
        public void Deactivate_ShouldDeactivateAllActiveChildren()
        {
            var container = new UIContainer();
            var child1 = new ConcreteUIElement();
            var child2 = new ConcreteUIElement();
            container.AddChild(child1);
            container.AddChild(child2);
            container.Activate();

            Assert.True(child1.IsActive);
            Assert.True(child2.IsActive);

            container.Deactivate();

            Assert.False(child1.IsActive);
            Assert.False(child2.IsActive);
        }

        [Fact]
        public void AddChild_ToActiveContainer_ShouldActivateChild()
        {
            var container = new UIContainer();
            container.Activate();

            var child = new ConcreteUIElement();
            container.AddChild(child);

            Assert.True(child.IsActive);
        }

        #endregion
    }
}
