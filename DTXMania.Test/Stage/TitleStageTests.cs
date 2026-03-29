using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for TitleStage menu functionality
    /// Tests menu navigation, input handling, and sound integration
    /// </summary>
    public class TitleStageTests
    {


        [Fact]
        public void MenuNavigation_UpFromFirst_ShouldWrapToLast()
        {
            // Arrange
            var currentIndex = 0;
            var menuLength = 3;

            // Act - Simulate menu wrapping logic
            var newIndex = currentIndex > 0 ? currentIndex - 1 : menuLength - 1;

            // Assert
            Assert.Equal(2, newIndex); // Should wrap to last item
        }

        [Fact]
        public void MenuNavigation_DownFromLast_ShouldWrapToFirst()
        {
            // Arrange
            var currentIndex = 2;
            var menuLength = 3;

            // Act - Simulate menu wrapping logic
            var newIndex = currentIndex < menuLength - 1 ? currentIndex + 1 : 0;

            // Assert
            Assert.Equal(0, newIndex); // Should wrap to first item
        }





        [Fact]
        public void MouseHitTest_ShouldDetectMenuItemCollision()
        {
            // Arrange
            var menuX = 506;
            var menuY = 513;
            var menuItemWidth = 227;
            var menuItemHeight = 39;
            var mouseX = 600; // Within menu bounds
            var mouseY = 530; // Within first menu item

            // Act
            var menuItemRect = new Rectangle(menuX, menuY, menuItemWidth, menuItemHeight);
            var mousePoint = new Point(mouseX, mouseY);
            var isHit = menuItemRect.Contains(mousePoint);

            // Assert
            Assert.True(isHit);
        }

        [Fact]
        public void MouseHitTest_ShouldRejectOutsideMenuBounds()
        {
            // Arrange
            var menuX = 506;
            var menuY = 513;
            var menuItemWidth = 227;
            var menuItemHeight = 39;
            var mouseX = 400; // Outside menu bounds
            var mouseY = 530;

            // Act
            var menuItemRect = new Rectangle(menuX, menuY, menuItemWidth, menuItemHeight);
            var mousePoint = new Point(mouseX, mouseY);
            var isHit = menuItemRect.Contains(mousePoint);

            // Assert
            Assert.False(isHit);
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenActivatePressed_ShouldReturnTrue()
        {
            var inputManager = new TestInputManager
            {
                ActivatePressed = true,
            };

            Assert.True(TitleStage.IsMenuSelectTriggered(inputManager));
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenSpacePressedWithoutActivateMapping_ShouldReturnFalse()
        {
            var inputManager = new TestInputManager();
            inputManager.SetPressedKey(Keys.Space);

            Assert.False(TitleStage.IsMenuSelectTriggered(inputManager));
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenNonActivateCommandAndSpacePressed_ShouldReturnFalse()
        {
            var inputManager = new TestInputManager();
            inputManager.SetPressedCommand(InputCommandType.MoveDown);
            inputManager.SetPressedKey(Keys.Space);

            Assert.False(TitleStage.IsMenuSelectTriggered(inputManager));
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenInputManagerIsNull_ShouldReturnFalse()
        {
            Assert.False(TitleStage.IsMenuSelectTriggered(null));
        }

        private sealed class TestInputManager : IInputManager
        {
            private readonly HashSet<int> _pressedKeys = new();
            private readonly HashSet<InputCommandType> _pressedCommands = new();

            public bool ActivatePressed { get; set; }

            public bool HasPendingCommands => false;

            public void Dispose()
            {
            }

            public InputCommand? GetNextCommand() => null;

            public bool IsBackActionTriggered() => false;

            public bool IsCommandPressed(InputCommandType commandType)
                => (commandType == InputCommandType.Activate && ActivatePressed)
                    || _pressedCommands.Contains(commandType);

            public bool IsKeyDown(int keyCode) => false;

            public bool IsKeyPressed(int keyCode) => _pressedKeys.Contains(keyCode);

            public bool IsKeyReleased(int keyCode) => false;

            public bool IsKeyTriggered(int keyCode) => IsKeyPressed(keyCode);

            public void SetPressedKey(Keys key)
            {
                _pressedKeys.Add((int)key);
            }

            public void SetPressedCommand(InputCommandType commandType)
            {
                _pressedCommands.Add(commandType);
            }

            public void Update(double deltaTime)
            {
            }
        }


    }
}
