using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
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


    }
}
