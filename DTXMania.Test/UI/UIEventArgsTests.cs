using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Tests for UI event args and related data classes
    /// </summary>
    public class UIClickEventArgsTests
    {
        [Fact]
        public void UIClickEventArgs_Constructor_ShouldSetProperties()
        {
            var position = new Vector2(100, 200);
            var args = new UIClickEventArgs(position, MouseButton.Left);

            Assert.Equal(position, args.Position);
            Assert.Equal(MouseButton.Left, args.Button);
        }

        [Fact]
        public void UIClickEventArgs_WithRightButton_ShouldSetCorrectButton()
        {
            var args = new UIClickEventArgs(new Vector2(50, 75), MouseButton.Right);
            Assert.Equal(MouseButton.Right, args.Button);
        }

        [Fact]
        public void UIClickEventArgs_WithMiddleButton_ShouldSetCorrectButton()
        {
            var args = new UIClickEventArgs(new Vector2(0, 0), MouseButton.Middle);
            Assert.Equal(MouseButton.Middle, args.Button);
        }

        [Fact]
        public void MouseButton_Values_ShouldBeDistinct()
        {
            Assert.NotEqual(MouseButton.Left, MouseButton.Right);
            Assert.NotEqual(MouseButton.Left, MouseButton.Middle);
            Assert.NotEqual(MouseButton.Right, MouseButton.Middle);
        }
    }

    /// <summary>
    /// Tests for ListSelectionChangedEventArgs
    /// </summary>
    public class ListSelectionChangedEventArgsTests
    {
        [Fact]
        public void ListSelectionChangedEventArgs_Constructor_ShouldSetIndices()
        {
            var args = new ListSelectionChangedEventArgs(oldIndex: 2, newIndex: 5);
            Assert.Equal(2, args.OldIndex);
            Assert.Equal(5, args.NewIndex);
        }

        [Fact]
        public void ListSelectionChangedEventArgs_WithZeroIndices_ShouldWork()
        {
            var args = new ListSelectionChangedEventArgs(0, 0);
            Assert.Equal(0, args.OldIndex);
            Assert.Equal(0, args.NewIndex);
        }

        [Fact]
        public void ListSelectionChangedEventArgs_WithNegativeOldIndex_ShouldWork()
        {
            // -1 is often used for "no selection"
            var args = new ListSelectionChangedEventArgs(-1, 0);
            Assert.Equal(-1, args.OldIndex);
            Assert.Equal(0, args.NewIndex);
        }
    }

    /// <summary>
    /// Tests for ListItemActivatedEventArgs
    /// </summary>
    public class ListItemActivatedEventArgsTests
    {
        [Fact]
        public void ListItemActivatedEventArgs_Constructor_ShouldSetProperties()
        {
            var item = new UIListItem("Test Item");
            var args = new ListItemActivatedEventArgs(3, item);

            Assert.Equal(3, args.Index);
            Assert.Equal(item, args.Item);
        }

        [Fact]
        public void ListItemActivatedEventArgs_WithZeroIndex_ShouldWork()
        {
            var item = new UIListItem("First Item");
            var args = new ListItemActivatedEventArgs(0, item);
            Assert.Equal(0, args.Index);
        }
    }

    /// <summary>
    /// Tests for UIListItem
    /// </summary>
    public class UIListItemTests
    {
        [Fact]
        public void UIListItem_Constructor_ShouldSetText()
        {
            var item = new UIListItem("My Item");
            Assert.Equal("My Item", item.Text);
        }

        [Fact]
        public void UIListItem_ConstructorWithData_ShouldSetTextAndData()
        {
            var data = new object();
            var item = new UIListItem("My Item", data);
            Assert.Equal("My Item", item.Text);
            Assert.Equal(data, item.Data);
        }

        [Fact]
        public void UIListItem_Constructor_NullText_ShouldUseEmpty()
        {
            var item = new UIListItem(null);
            Assert.Equal(string.Empty, item.Text);
        }

        [Fact]
        public void UIListItem_ToString_ShouldReturnText()
        {
            var item = new UIListItem("Display Text");
            Assert.Equal("Display Text", item.ToString());
        }
    }
}
