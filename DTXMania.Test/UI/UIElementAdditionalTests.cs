using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using Xunit;
using System;
using DTXMania.Test;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Additional unit tests for UIElement covering edge cases not covered by
    /// UIArchitectureTests, including AbsolutePosition with parent, HitTest,
    /// Focused events, Dispose, and double-Activate guards.
    /// </summary>
    [Trait("Category", "Unit")]
    public class UIElementAdditionalTests
    {
        #region AbsolutePosition Tests

        [Fact]
        public void AbsolutePosition_NoParent_ShouldEqualPosition()
        {
            var element = new ConcreteUIElement
            {
                Position = new Vector2(75, 120)
            };
            Assert.Equal(new Vector2(75, 120), element.AbsolutePosition);
        }

        [Fact]
        public void AbsolutePosition_WithParent_ShouldAddParentAbsolutePosition()
        {
            var parent = new UIContainer();
            parent.Position = new Vector2(100, 50);

            var child = new ConcreteUIElement();
            child.Position = new Vector2(20, 30);
            parent.AddChild(child);

            // Child's absolute = parent absolute (100,50) + child local (20,30)
            Assert.Equal(new Vector2(120, 80), child.AbsolutePosition);
        }

        [Fact]
        public void AbsolutePosition_NestedContainers_ShouldSumAllPositions()
        {
            var grandparent = new UIContainer { Position = new Vector2(10, 10) };
            var parent = new UIContainer { Position = new Vector2(20, 20) };
            var child = new ConcreteUIElement { Position = new Vector2(5, 5) };

            grandparent.AddChild(parent);
            parent.AddChild(child);

            // 10+20+5=35, 10+20+5=35
            Assert.Equal(new Vector2(35, 35), child.AbsolutePosition);
        }

        #endregion

        #region Bounds Tests

        [Fact]
        public void Bounds_ShouldUseAbsolutePositionAndSize()
        {
            var parent = new UIContainer { Position = new Vector2(50, 40) };
            var child = new ConcreteUIElement
            {
                Position = new Vector2(10, 5),
                Size = new Vector2(80, 60)
            };
            parent.AddChild(child);

            var bounds = child.Bounds;
            Assert.Equal(60, bounds.X);  // 50+10
            Assert.Equal(45, bounds.Y);  // 40+5
            Assert.Equal(80, bounds.Width);
            Assert.Equal(60, bounds.Height);
        }

        #endregion

        #region HitTest Tests

        [Fact]
        public void HitTest_PointInsideBounds_ShouldReturnTrue()
        {
            var element = new ConcreteUIElement
            {
                Position = new Vector2(10, 10),
                Size = new Vector2(100, 50)
            };
            Assert.True(element.HitTest(new Vector2(50, 30)));
        }

        [Fact]
        public void HitTest_PointAtTopLeftCorner_ShouldReturnTrue()
        {
            var element = new ConcreteUIElement
            {
                Position = new Vector2(10, 10),
                Size = new Vector2(100, 50)
            };
            Assert.True(element.HitTest(new Vector2(10, 10)));
        }

        [Fact]
        public void HitTest_PointOutsideBounds_ShouldReturnFalse()
        {
            var element = new ConcreteUIElement
            {
                Position = new Vector2(10, 10),
                Size = new Vector2(100, 50)
            };
            Assert.False(element.HitTest(new Vector2(200, 200)));
        }

        [Fact]
        public void HitTest_PointJustOutsideRight_ShouldReturnFalse()
        {
            var element = new ConcreteUIElement
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(100, 50)
            };
            // Rectangle.Contains excludes the right edge (x == Position.X + Size.X)
            Assert.False(element.HitTest(new Vector2(100, 25)));
        }

        #endregion

        #region Focused Event Tests

        [Fact]
        public void Focused_SetToTrue_ShouldFireOnFocusEvent()
        {
            var element = new ConcreteUIElement();
            bool focusFired = false;
            element.OnFocus += (s, e) => focusFired = true;

            element.Focused = true;

            Assert.True(focusFired);
        }

        [Fact]
        public void Focused_SetToFalse_ShouldFireOnBlurEvent()
        {
            var element = new ConcreteUIElement();
            element.Focused = true; // Set first so we can un-focus

            bool blurFired = false;
            element.OnBlur += (s, e) => blurFired = true;

            element.Focused = false;

            Assert.True(blurFired);
        }

        [Fact]
        public void Focused_SetToSameValue_ShouldNotFireEvent()
        {
            var element = new ConcreteUIElement();
            int focusCount = 0;
            int blurCount = 0;
            element.OnFocus += (s, e) => focusCount++;
            element.OnBlur += (s, e) => blurCount++;

            element.Focused = false; // Already false, no event
            element.Focused = false; // Still false, no event

            Assert.Equal(0, focusCount);
            Assert.Equal(0, blurCount);
        }

        [Fact]
        public void Focused_SetTrueThenFalse_ShouldFireBothEvents()
        {
            var element = new ConcreteUIElement();
            bool focusFired = false;
            bool blurFired = false;
            element.OnFocus += (s, e) => focusFired = true;
            element.OnBlur += (s, e) => blurFired = true;

            element.Focused = true;
            element.Focused = false;

            Assert.True(focusFired);
            Assert.True(blurFired);
        }

        #endregion

        #region Activate / Deactivate Guard Tests

        [Fact]
        public void Activate_WhenAlreadyActive_ShouldNotFireEventTwice()
        {
            var element = new ConcreteUIElement();
            int activatedCount = 0;
            element.OnActivated += (s, e) => activatedCount++;

            element.Activate();
            element.Activate(); // Second call should be ignored

            Assert.Equal(1, activatedCount);
        }

        [Fact]
        public void Deactivate_WhenNotActive_ShouldNotFireEventTwice()
        {
            var element = new ConcreteUIElement();
            element.Activate();

            int deactivatedCount = 0;
            element.OnDeactivated += (s, e) => deactivatedCount++;

            element.Deactivate();
            element.Deactivate(); // Should be no-op

            Assert.Equal(1, deactivatedCount);
        }

        [Fact]
        public void Update_WhenNotActive_ShouldNotCallOnUpdate()
        {
            var element = new TrackingUIElement();
            element.Update(0.016); // Not active, should not call OnUpdate
            Assert.Equal(0, element.UpdateCallCount);
        }

        [Fact]
        public void Update_WhenActive_ShouldCallOnUpdate()
        {
            var element = new TrackingUIElement();
            element.Activate();
            element.Update(0.016);
            Assert.Equal(1, element.UpdateCallCount);
        }

        #endregion

        #region Visibility Tests

        [Fact]
        public void Visible_DefaultIsTrue()
        {
            var element = new ConcreteUIElement();
            Assert.True(element.Visible);
        }

        [Fact]
        public void Visible_CanBeSetToFalse()
        {
            var element = new ConcreteUIElement();
            element.Visible = false;
            Assert.False(element.Visible);
        }

        [Fact]
        public void Enabled_DefaultIsTrue()
        {
            var element = new ConcreteUIElement();
            Assert.True(element.Enabled);
        }

        [Fact]
        public void Enabled_CanBeSetToFalse()
        {
            var element = new ConcreteUIElement();
            element.Enabled = false;
            Assert.False(element.Enabled);
        }

        #endregion

        #region Position Change Notification Tests

        [Fact]
        public void Position_ChangingValue_ShouldCallOnPositionChanged()
        {
            var element = new TrackingUIElement();
            element.Position = new Vector2(100, 200);
            Assert.True(element.PositionChangedCalled);
        }

        [Fact]
        public void Position_SameValue_ShouldNotCallOnPositionChanged()
        {
            var element = new TrackingUIElement();
            element.Position = new Vector2(100, 100);
            element.PositionChangedCalled = false; // Reset

            element.Position = new Vector2(100, 100); // Same value
            Assert.False(element.PositionChangedCalled);
        }

        [Fact]
        public void Size_ChangingValue_ShouldCallOnSizeChanged()
        {
            var element = new TrackingUIElement();
            element.Size = new Vector2(300, 150);
            Assert.True(element.SizeChangedCalled);
        }

        [Fact]
        public void Size_SameValue_ShouldNotCallOnSizeChanged()
        {
            var element = new TrackingUIElement();
            element.Size = new Vector2(100, 100);
            element.SizeChangedCalled = false;

            element.Size = new Vector2(100, 100);
            Assert.False(element.SizeChangedCalled);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_WhenActive_ShouldDeactivate()
        {
            var element = new ConcreteUIElement();
            element.Activate();
            Assert.True(element.IsActive);

            element.Dispose();

            Assert.False(element.IsActive);
        }

        [Fact]
        public void Dispose_WhenNotActive_ShouldNotThrow()
        {
            var element = new ConcreteUIElement();
            var ex = Record.Exception(() => element.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var element = new ConcreteUIElement();
            element.Activate();
            element.Dispose();
            var ex = Record.Exception(() => element.Dispose());
            Assert.Null(ex);
        }

        #endregion
    }

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
