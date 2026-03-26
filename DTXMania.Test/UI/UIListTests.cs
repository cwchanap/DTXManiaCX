using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using Moq;
using Xunit;
using System;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for UIList component including item management,
    /// selection, scrolling, and event firing.
    /// </summary>
    [Trait("Category", "Unit")]
    public class UIListTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldCreateEmptyList()
        {
            var list = new UIList();
            Assert.Empty(list.Items);
        }

        [Fact]
        public void Constructor_ShouldDefaultToNoSelection()
        {
            var list = new UIList();
            Assert.Equal(-1, list.SelectedIndex);
            Assert.Null(list.SelectedItem);
        }

        [Fact]
        public void Constructor_ShouldDefaultToFiveVisibleItems()
        {
            var list = new UIList();
            Assert.Equal(5, list.VisibleItemCount);
        }

        [Fact]
        public void Constructor_ShouldSetDefaultItemHeight()
        {
            var list = new UIList();
            Assert.Equal(30f, list.ItemHeight);
        }

        [Fact]
        public void Constructor_ShouldCalculateDefaultSize()
        {
            var list = new UIList();
            // Size should be 200 x (5 * 30)
            Assert.Equal(200f, list.Size.X);
            Assert.Equal(150f, list.Size.Y);
        }

        [Fact]
        public void Constructor_ShouldDefaultScrollOffsetToZero()
        {
            var list = new UIList();
            Assert.Equal(0, list.ScrollOffset);
        }

        #endregion

        #region AddItem Tests

        [Fact]
        public void AddItem_WithUIListItem_ShouldAddToItems()
        {
            var list = new UIList();
            var item = new UIListItem("Item 1");
            list.AddItem(item);
            Assert.Equal(1, list.Items.Count);
            Assert.Equal(item, list.Items[0]);
        }

        [Fact]
        public void AddItem_WithString_ShouldAddAndReturnNewItem()
        {
            var list = new UIList();
            var returned = list.AddItem("Item Text");
            Assert.Equal(1, list.Items.Count);
            Assert.Equal("Item Text", returned.Text);
        }

        [Fact]
        public void AddItem_WithStringAndData_ShouldSetData()
        {
            var list = new UIList();
            var data = new object();
            var item = list.AddItem("Item", data);
            Assert.Equal(data, item.Data);
        }

        [Fact]
        public void AddItem_NullUIListItem_ShouldThrowArgumentNullException()
        {
            var list = new UIList();
            Assert.Throws<ArgumentNullException>(() => list.AddItem((UIListItem)null));
        }

        [Fact]
        public void AddItem_MultipleItems_ShouldAllBePresent()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.AddItem("C");
            Assert.Equal(3, list.Items.Count);
        }

        #endregion

        #region RemoveItem Tests

        [Fact]
        public void RemoveItem_ExistingItem_ShouldReturnTrue()
        {
            var list = new UIList();
            var item = new UIListItem("Item");
            list.AddItem(item);
            var result = list.RemoveItem(item);
            Assert.True(result);
        }

        [Fact]
        public void RemoveItem_ExistingItem_ShouldRemoveFromList()
        {
            var list = new UIList();
            var item = new UIListItem("Item");
            list.AddItem(item);
            list.RemoveItem(item);
            Assert.Empty(list.Items);
        }

        [Fact]
        public void RemoveItem_NonExistentItem_ShouldReturnFalse()
        {
            var list = new UIList();
            var item = new UIListItem("Item");
            var result = list.RemoveItem(item);
            Assert.False(result);
        }

        [Fact]
        public void RemoveItemAt_ValidIndex_ShouldReturnTrue()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            var result = list.RemoveItemAt(0);
            Assert.True(result);
            Assert.Equal(1, list.Items.Count);
            Assert.Equal("B", list.Items[0].Text);
        }

        [Fact]
        public void RemoveItemAt_NegativeIndex_ShouldReturnFalse()
        {
            var list = new UIList();
            list.AddItem("A");
            var result = list.RemoveItemAt(-1);
            Assert.False(result);
        }

        [Fact]
        public void RemoveItemAt_IndexOutOfRange_ShouldReturnFalse()
        {
            var list = new UIList();
            list.AddItem("A");
            var result = list.RemoveItemAt(5);
            Assert.False(result);
        }

        [Fact]
        public void RemoveItemAt_SelectedItem_ShouldClearSelection()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.SelectedIndex = 0;
            list.RemoveItemAt(0);
            Assert.Equal(-1, list.SelectedIndex);
        }

        [Fact]
        public void RemoveItemAt_ItemBeforeSelected_ShouldAdjustSelectedIndex()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.AddItem("C");
            list.SelectedIndex = 2; // "C" is selected
            list.RemoveItemAt(0);   // Remove "A"
            // "C" was at index 2, now at index 1
            Assert.Equal(1, list.SelectedIndex);
        }

        [Fact]
        public void RemoveItemAt_ItemAfterSelected_ShouldNotChangeSelectedIndex()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.AddItem("C");
            list.SelectedIndex = 0; // "A" is selected
            list.RemoveItemAt(2);   // Remove "C"
            Assert.Equal(0, list.SelectedIndex);
        }

        #endregion

        #region ClearItems Tests

        [Fact]
        public void ClearItems_ShouldRemoveAllItems()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.AddItem("C");
            list.ClearItems();
            Assert.Empty(list.Items);
        }

        [Fact]
        public void ClearItems_ShouldResetSelection()
        {
            var list = new UIList();
            list.AddItem("A");
            list.SelectedIndex = 0;
            list.ClearItems();
            Assert.Equal(-1, list.SelectedIndex);
        }

        [Fact]
        public void ClearItems_ShouldResetScrollOffset()
        {
            var list = new UIList();
            for (int i = 0; i < 10; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = 5;
            list.ClearItems();
            Assert.Equal(0, list.ScrollOffset);
        }

        #endregion

        #region SelectedIndex Tests

        [Fact]
        public void SelectedIndex_ValidValue_ShouldSetSelection()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.SelectedIndex = 1;
            Assert.Equal(1, list.SelectedIndex);
        }

        [Fact]
        public void SelectedIndex_NegativeOne_ShouldClearSelection()
        {
            var list = new UIList();
            list.AddItem("A");
            list.SelectedIndex = 0;
            list.SelectedIndex = -1;
            Assert.Equal(-1, list.SelectedIndex);
            Assert.Null(list.SelectedItem);
        }

        [Fact]
        public void SelectedIndex_BeyondCount_ShouldClampToLastItem()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.SelectedIndex = 100; // Way out of bounds
            Assert.Equal(1, list.SelectedIndex); // Clamped to last valid index
        }

        [Fact]
        public void SelectedIndex_LessThanNegativeOne_ShouldClampToNegativeOne()
        {
            var list = new UIList();
            list.AddItem("A");
            list.SelectedIndex = -5;
            Assert.Equal(-1, list.SelectedIndex);
        }

        [Fact]
        public void SelectedItem_ReturnsCorrectItem()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.SelectedIndex = 1;
            Assert.Equal("B", list.SelectedItem?.Text);
        }

        [Fact]
        public void SelectedItem_NoSelection_ReturnsNull()
        {
            var list = new UIList();
            list.AddItem("A");
            Assert.Null(list.SelectedItem);
        }

        #endregion

        #region SelectionChanged Event Tests

        [Fact]
        public void SelectionChanged_ShouldFireWhenIndexChanges()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");

            ListSelectionChangedEventArgs? receivedArgs = null;
            list.SelectionChanged += (s, e) => receivedArgs = e;

            list.SelectedIndex = 1;

            Assert.NotNull(receivedArgs);
            Assert.Equal(-1, receivedArgs!.OldIndex);
            Assert.Equal(1, receivedArgs.NewIndex);
        }

        [Fact]
        public void SelectionChanged_ShouldProvideOldAndNewIndex()
        {
            var list = new UIList();
            list.AddItem("A");
            list.AddItem("B");
            list.AddItem("C");
            list.SelectedIndex = 0;

            ListSelectionChangedEventArgs? receivedArgs = null;
            list.SelectionChanged += (s, e) => receivedArgs = e;

            list.SelectedIndex = 2;

            Assert.NotNull(receivedArgs);
            Assert.Equal(0, receivedArgs!.OldIndex);
            Assert.Equal(2, receivedArgs.NewIndex);
        }

        [Fact]
        public void SelectionChanged_ShouldNotFireWhenIndexIsUnchanged()
        {
            var list = new UIList();
            list.AddItem("A");
            list.SelectedIndex = 0;

            int eventCount = 0;
            list.SelectionChanged += (s, e) => eventCount++;

            list.SelectedIndex = 0; // Same value - should not fire

            Assert.Equal(0, eventCount);
        }

        #endregion

        #region ScrollOffset Tests

        [Fact]
        public void ScrollOffset_ValidValue_ShouldSetOffset()
        {
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 10; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = 5;
            Assert.Equal(5, list.ScrollOffset);
        }

        [Fact]
        public void ScrollOffset_NegativeValue_ShouldClampToZero()
        {
            var list = new UIList();
            for (int i = 0; i < 10; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = -3;
            Assert.Equal(0, list.ScrollOffset);
        }

        [Fact]
        public void ScrollOffset_TooLarge_ShouldClampToMax()
        {
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 5; i++) list.AddItem($"Item {i}");
            // Max scroll = items.Count - visibleItemCount = 5 - 3 = 2
            list.ScrollOffset = 100;
            Assert.Equal(2, list.ScrollOffset);
        }

        [Fact]
        public void ScrollOffset_WhenItemsLessThanVisible_ShouldClampToZero()
        {
            var list = new UIList { VisibleItemCount = 5 };
            list.AddItem("A");
            list.AddItem("B");
            list.ScrollOffset = 1; // Can't scroll when all items fit
            Assert.Equal(0, list.ScrollOffset);
        }

        #endregion

        #region ScrollToItem Tests

        [Fact]
        public void ScrollToItem_ItemAboveView_ShouldScrollUp()
        {
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 10; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = 5; // Showing items 5-7
            list.ScrollToItem(2);  // Item 2 is above view
            Assert.Equal(2, list.ScrollOffset);
        }

        [Fact]
        public void ScrollToItem_ItemBelowView_ShouldScrollDown()
        {
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 10; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = 0; // Showing items 0-2
            list.ScrollToItem(5);  // Item 5 is below view
            // scrollOffset = 5 - 3 + 1 = 3
            Assert.Equal(3, list.ScrollOffset);
        }

        [Fact]
        public void ScrollToItem_ItemInView_ShouldNotScroll()
        {
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 10; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = 2; // Showing items 2-4
            list.ScrollToItem(3);  // Item 3 is in view
            Assert.Equal(2, list.ScrollOffset); // Unchanged
        }

        [Fact]
        public void ScrollToItem_NegativeIndex_ShouldNotScroll()
        {
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 5; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = 1;
            list.ScrollToItem(-1);
            Assert.Equal(1, list.ScrollOffset); // Unchanged
        }

        [Fact]
        public void ScrollToItem_IndexBeyondCount_ShouldNotScroll()
        {
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 5; i++) list.AddItem($"Item {i}");
            list.ScrollOffset = 1;
            list.ScrollToItem(100);
            Assert.Equal(1, list.ScrollOffset); // Unchanged
        }

        #endregion

        #region VisibleItemCount Tests

        [Fact]
        public void VisibleItemCount_ShouldResizeList()
        {
            var list = new UIList();
            list.VisibleItemCount = 8;
            Assert.Equal(8, list.VisibleItemCount);
            Assert.Equal(8 * list.ItemHeight, list.Size.Y);
        }

        [Fact]
        public void VisibleItemCount_ZeroOrNegative_ShouldNotChange()
        {
            var list = new UIList();
            var original = list.VisibleItemCount;
            list.VisibleItemCount = 0;
            Assert.Equal(original, list.VisibleItemCount);
            list.VisibleItemCount = -1;
            Assert.Equal(original, list.VisibleItemCount);
        }

        #endregion

        #region ItemHeight Tests

        [Fact]
        public void ItemHeight_ShouldResizeList()
        {
            var list = new UIList();
            list.ItemHeight = 50f;
            Assert.Equal(50f, list.ItemHeight);
            Assert.Equal(list.VisibleItemCount * 50f, list.Size.Y);
        }

        [Fact]
        public void ItemHeight_ZeroOrNegative_ShouldNotChange()
        {
            var list = new UIList();
            var original = list.ItemHeight;
            list.ItemHeight = 0;
            Assert.Equal(original, list.ItemHeight);
            list.ItemHeight = -5;
            Assert.Equal(original, list.ItemHeight);
        }

        #endregion

        #region AllowMultipleSelection Tests

        [Fact]
        public void AllowMultipleSelection_DefaultIsFalse()
        {
            var list = new UIList();
            Assert.False(list.AllowMultipleSelection);
        }

        [Fact]
        public void AllowMultipleSelection_ShouldSetAndGet()
        {
            var list = new UIList();
            list.AllowMultipleSelection = true;
            Assert.True(list.AllowMultipleSelection);
        }

        #endregion

        #region Color Property Tests

        [Fact]
        public void BackgroundColor_ShouldSetAndGet()
        {
            var list = new UIList();
            list.BackgroundColor = Color.Navy;
            Assert.Equal(Color.Navy, list.BackgroundColor);
        }

        [Fact]
        public void SelectedItemColor_ShouldSetAndGet()
        {
            var list = new UIList();
            list.SelectedItemColor = Color.Green;
            Assert.Equal(Color.Green, list.SelectedItemColor);
        }

        [Fact]
        public void HoverItemColor_ShouldSetAndGet()
        {
            var list = new UIList();
            list.HoverItemColor = Color.Yellow;
            Assert.Equal(Color.Yellow, list.HoverItemColor);
        }

        [Fact]
        public void TextColor_ShouldSetAndGet()
        {
            var list = new UIList();
            list.TextColor = Color.Cyan;
            Assert.Equal(Color.Cyan, list.TextColor);
        }

        [Fact]
        public void SelectedTextColor_ShouldSetAndGet()
        {
            var list = new UIList();
            list.SelectedTextColor = Color.Magenta;
            Assert.Equal(Color.Magenta, list.SelectedTextColor);
        }

        #endregion

        #region ItemActivated Event Tests

        [Fact]
        public void ItemActivated_NullSubscriber_ShouldNotThrowWhenEnterPressed()
        {
            // Arrange: active list with a selected item and no subscriber
            var list = new UIList
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 150)
            };
            list.Activate();
            list.AddItem("A");
            list.SelectedIndex = 0;

            // Mock IInputState so the Enter key reports as just-pressed and the
            // mouse cursor sits outside the list bounds (avoiding the mouse path).
            var mockInput = new Mock<IInputState>();
            mockInput.Setup(i => i.MousePosition).Returns(new Vector2(-1000, -1000));
            mockInput.Setup(i => i.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            mockInput.Setup(i => i.ScrollWheelDelta).Returns(0);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Up)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Down)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Home)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.End)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Enter)).Returns(true);

            // Act & Assert: ItemActivated is null; HandleInput must not throw
            var exception = Record.Exception(() => list.HandleInput(mockInput.Object));
            Assert.Null(exception);
        }

        [Fact]
        public void ItemActivated_WithSubscriber_ShouldFireWhenEnterPressed()
        {
            // Arrange
            var list = new UIList
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 150)
            };
            list.Activate();
            list.AddItem("A");
            list.SelectedIndex = 0;

            ListItemActivatedEventArgs? receivedArgs = null;
            list.ItemActivated += (s, e) => receivedArgs = e;

            var mockInput = new Mock<IInputState>();
            mockInput.Setup(i => i.MousePosition).Returns(new Vector2(-1000, -1000));
            mockInput.Setup(i => i.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            mockInput.Setup(i => i.ScrollWheelDelta).Returns(0);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Up)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Down)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Home)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.End)).Returns(false);
            mockInput.Setup(i => i.IsKeyPressed(Keys.Enter)).Returns(true);

            // Act
            list.HandleInput(mockInput.Object);

            // Assert
            Assert.NotNull(receivedArgs);
            Assert.Equal(0, receivedArgs!.Index);
            Assert.Equal("A", receivedArgs.Item.Text);
        }

        #endregion
    }
}
