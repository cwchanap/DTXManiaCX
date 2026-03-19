using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;
using Xunit;
using System;
using DTXMania.Test;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for UIManager covering container management, focus tracking,
    /// and element lookup — all without requiring a graphics device.
    /// </summary>
    [Trait("Category", "Unit")]
    public class UIManagerTests
    {
        #region AddRootContainer Tests

        [Fact]
        public void AddRootContainer_ShouldAddAndActivateContainer()
        {
            var manager = new UIManager();
            var container = new UIContainer();

            manager.AddRootContainer(container);

            Assert.Equal(1, manager.RootContainers.Count);
            Assert.True(container.IsActive);
        }

        [Fact]
        public void AddRootContainer_FirstContainer_ShouldBecomeFocused()
        {
            var manager = new UIManager();
            var container = new UIContainer();

            manager.AddRootContainer(container);

            Assert.Equal(container, manager.FocusedContainer);
        }

        [Fact]
        public void AddRootContainer_SecondContainer_ShouldNotChangeFocus()
        {
            var manager = new UIManager();
            var container1 = new UIContainer();
            var container2 = new UIContainer();

            manager.AddRootContainer(container1);
            manager.AddRootContainer(container2);

            // First container should remain focused
            Assert.Equal(container1, manager.FocusedContainer);
        }

        [Fact]
        public void AddRootContainer_Null_ShouldThrowArgumentNullException()
        {
            var manager = new UIManager();
            Assert.Throws<ArgumentNullException>(() => manager.AddRootContainer(null));
        }

        [Fact]
        public void AddRootContainer_SameTwice_ShouldOnlyAddOnce()
        {
            var manager = new UIManager();
            var container = new UIContainer();

            manager.AddRootContainer(container);
            manager.AddRootContainer(container);

            Assert.Equal(1, manager.RootContainers.Count);
        }

        #endregion

        #region RemoveRootContainer Tests

        [Fact]
        public void RemoveRootContainer_ExistingContainer_ShouldReturnTrue()
        {
            var manager = new UIManager();
            var container = new UIContainer();
            manager.AddRootContainer(container);

            var result = manager.RemoveRootContainer(container);

            Assert.True(result);
        }

        [Fact]
        public void RemoveRootContainer_ExistingContainer_ShouldRemoveFromList()
        {
            var manager = new UIManager();
            var container = new UIContainer();
            manager.AddRootContainer(container);

            manager.RemoveRootContainer(container);

            Assert.Empty(manager.RootContainers);
        }

        [Fact]
        public void RemoveRootContainer_ExistingContainer_ShouldDeactivateIt()
        {
            var manager = new UIManager();
            var container = new UIContainer();
            manager.AddRootContainer(container);
            Assert.True(container.IsActive);

            manager.RemoveRootContainer(container);

            Assert.False(container.IsActive);
        }

        [Fact]
        public void RemoveRootContainer_NonExistentContainer_ShouldReturnFalse()
        {
            var manager = new UIManager();
            var container = new UIContainer();

            var result = manager.RemoveRootContainer(container);

            Assert.False(result);
        }

        [Fact]
        public void RemoveRootContainer_Null_ShouldReturnFalse()
        {
            var manager = new UIManager();
            var result = manager.RemoveRootContainer(null);
            Assert.False(result);
        }

        [Fact]
        public void RemoveRootContainer_FocusedContainer_ShouldTransferFocusToNext()
        {
            var manager = new UIManager();
            var container1 = new UIContainer();
            var container2 = new UIContainer();
            manager.AddRootContainer(container1);
            manager.AddRootContainer(container2);

            // container1 is focused; remove it
            manager.RemoveRootContainer(container1);

            // Focus should transfer to container2
            Assert.Equal(container2, manager.FocusedContainer);
        }

        [Fact]
        public void RemoveRootContainer_LastContainer_ShouldClearFocus()
        {
            var manager = new UIManager();
            var container = new UIContainer();
            manager.AddRootContainer(container);

            manager.RemoveRootContainer(container);

            Assert.Null(manager.FocusedContainer);
        }

        #endregion

        #region ClearRootContainers Tests

        [Fact]
        public void ClearRootContainers_ShouldRemoveAllContainers()
        {
            var manager = new UIManager();
            manager.AddRootContainer(new UIContainer());
            manager.AddRootContainer(new UIContainer());

            manager.ClearRootContainers();

            Assert.Empty(manager.RootContainers);
        }

        [Fact]
        public void ClearRootContainers_ShouldClearFocusedContainer()
        {
            var manager = new UIManager();
            manager.AddRootContainer(new UIContainer());

            manager.ClearRootContainers();

            Assert.Null(manager.FocusedContainer);
        }

        [Fact]
        public void ClearRootContainers_ShouldDeactivateAllContainers()
        {
            var manager = new UIManager();
            var c1 = new UIContainer();
            var c2 = new UIContainer();
            manager.AddRootContainer(c1);
            manager.AddRootContainer(c2);

            manager.ClearRootContainers();

            Assert.False(c1.IsActive);
            Assert.False(c2.IsActive);
        }

        #endregion

        #region FocusNextContainer Tests

        [Fact]
        public void FocusNextContainer_WithMultipleContainers_ShouldAdvanceFocus()
        {
            var manager = new UIManager();
            var c1 = new UIContainer();
            var c2 = new UIContainer();
            var c3 = new UIContainer();
            manager.AddRootContainer(c1);
            manager.AddRootContainer(c2);
            manager.AddRootContainer(c3);
            // c1 is focused initially

            manager.FocusNextContainer();

            Assert.Equal(c2, manager.FocusedContainer);
        }

        [Fact]
        public void FocusNextContainer_AtLastContainer_ShouldWrapToFirst()
        {
            var manager = new UIManager();
            var c1 = new UIContainer();
            var c2 = new UIContainer();
            manager.AddRootContainer(c1);
            manager.AddRootContainer(c2);

            manager.FocusedContainer = c2;
            manager.FocusNextContainer();

            Assert.Equal(c1, manager.FocusedContainer);
        }

        [Fact]
        public void FocusNextContainer_WithSingleContainer_ShouldNotThrow()
        {
            var manager = new UIManager();
            manager.AddRootContainer(new UIContainer());

            var ex = Record.Exception(() => manager.FocusNextContainer());
            Assert.Null(ex);
        }

        #endregion

        #region FocusPreviousContainer Tests

        [Fact]
        public void FocusPreviousContainer_ShouldMoveFocusBack()
        {
            var manager = new UIManager();
            var c1 = new UIContainer();
            var c2 = new UIContainer();
            var c3 = new UIContainer();
            manager.AddRootContainer(c1);
            manager.AddRootContainer(c2);
            manager.AddRootContainer(c3);

            manager.FocusedContainer = c2;
            manager.FocusPreviousContainer();

            Assert.Equal(c1, manager.FocusedContainer);
        }

        [Fact]
        public void FocusPreviousContainer_AtFirstContainer_ShouldWrapToLast()
        {
            var manager = new UIManager();
            var c1 = new UIContainer();
            var c2 = new UIContainer();
            var c3 = new UIContainer();
            manager.AddRootContainer(c1);
            manager.AddRootContainer(c2);
            manager.AddRootContainer(c3);
            // c1 is focused

            manager.FocusPreviousContainer();

            Assert.Equal(c3, manager.FocusedContainer);
        }

        [Fact]
        public void FocusPreviousContainer_WithSingleContainer_ShouldNotThrow()
        {
            var manager = new UIManager();
            manager.AddRootContainer(new UIContainer());

            var ex = Record.Exception(() => manager.FocusPreviousContainer());
            Assert.Null(ex);
        }

        #endregion

        #region FocusedContainer Tests

        [Fact]
        public void FocusedContainer_SetToContainer_ShouldSetFocused()
        {
            var manager = new UIManager();
            var c1 = new UIContainer();
            var c2 = new UIContainer();
            manager.AddRootContainer(c1);
            manager.AddRootContainer(c2);

            manager.FocusedContainer = c2;

            Assert.True(c2.Focused);
            Assert.False(c1.Focused);
        }

        [Fact]
        public void FocusedContainer_SetToNull_ShouldBlurCurrentContainer()
        {
            var manager = new UIManager();
            var container = new UIContainer();
            manager.AddRootContainer(container);
            Assert.True(container.Focused);

            manager.FocusedContainer = null;

            Assert.False(container.Focused);
        }

        [Fact]
        public void FocusedContainer_SetToSameValue_ShouldNotChangeAnything()
        {
            var manager = new UIManager();
            var container = new UIContainer();
            manager.AddRootContainer(container);

            int focusCount = 0;
            container.OnFocus += (s, e) => focusCount++;

            manager.FocusedContainer = container; // Same value

            // Should not fire OnFocus again
            Assert.Equal(0, focusCount);
        }

        #endregion

        #region GetElementAtPosition Tests

        [Fact]
        public void GetElementAtPosition_NoContainers_ShouldReturnNull()
        {
            var manager = new UIManager();
            var result = manager.GetElementAtPosition(new Vector2(100, 100));
            Assert.Null(result);
        }

        [Fact]
        public void GetElementAtPosition_HitsContainer_ShouldReturnContainer()
        {
            var manager = new UIManager();
            var container = new UIContainer
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(400, 300)
            };
            manager.AddRootContainer(container);

            var result = manager.GetElementAtPosition(new Vector2(100, 100));

            Assert.Equal(container, result);
        }

        [Fact]
        public void GetElementAtPosition_MissesAll_ShouldReturnNull()
        {
            var manager = new UIManager();
            var container = new UIContainer
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(100, 100)
            };
            manager.AddRootContainer(container);

            // Position is outside the container
            var result = manager.GetElementAtPosition(new Vector2(500, 500));

            Assert.Null(result);
        }

        [Fact]
        public void GetElementAtPosition_HitsChildElement_ShouldReturnChild()
        {
            var manager = new UIManager();
            var container = new UIContainer
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(400, 300)
            };
            var child = new ConcreteUIElement
            {
                Position = new Vector2(50, 50),
                Size = new Vector2(100, 80)
            };
            container.AddChild(child);
            manager.AddRootContainer(container);

            // Hit inside the child's bounds
            var result = manager.GetElementAtPosition(new Vector2(80, 90));

            Assert.Equal(child, result);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldDeactivateAllContainers()
        {
            var manager = new UIManager();
            var c1 = new UIContainer();
            var c2 = new UIContainer();
            manager.AddRootContainer(c1);
            manager.AddRootContainer(c2);

            manager.Dispose();

            Assert.False(c1.IsActive);
            Assert.False(c2.IsActive);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var manager = new UIManager();
            manager.AddRootContainer(new UIContainer());
            manager.Dispose();

            var ex = Record.Exception(() => manager.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Update_AfterDispose_ShouldNotThrow()
        {
            var manager = new UIManager();
            manager.Dispose();

            var ex = Record.Exception(() => manager.Update(0.016));
            Assert.Null(ex);
        }

        #endregion
    }

}
