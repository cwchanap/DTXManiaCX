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

    }
}
