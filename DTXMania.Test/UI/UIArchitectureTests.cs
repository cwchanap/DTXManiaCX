using Xunit;
using DTX.UI;
using DTX.UI.Components;
using Microsoft.Xna.Framework;
using System;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for the UI architecture
    /// Tests major functionality and DTXMania pattern compliance
    /// </summary>
    public class UIArchitectureTests
    {
        #region UIElement Tests

        [Fact]
        public void UIElement_Activation_FollowsDTXManiaPattern()
        {
            // Arrange
            var element = new TestUIElement();
            bool activatedEventFired = false;
            element.OnActivated += (s, e) => activatedEventFired = true;

            // Act
            element.Activate();

            // Assert
            Assert.True(element.IsActive);
            Assert.True(activatedEventFired);
            Assert.True(element.ResourcesCreated);
        }

        [Fact]
        public void UIElement_Deactivation_FollowsDTXManiaPattern()
        {
            // Arrange
            var element = new TestUIElement();
            element.Activate();
            bool deactivatedEventFired = false;
            element.OnDeactivated += (s, e) => deactivatedEventFired = true;

            // Act
            element.Deactivate();

            // Assert
            Assert.False(element.IsActive);
            Assert.True(deactivatedEventFired);
            Assert.True(element.ResourcesReleased);
        }

        [Fact]
        public void UIElement_FirstUpdateFlag_WorksCorrectly()
        {
            // Arrange
            var element = new TestUIElement();
            element.Activate();

            // Act & Assert
            Assert.True(element.IsFirstUpdate);
            
            element.Update(0.016); // First update
            Assert.False(element.IsFirstUpdate);
        }

        [Fact]
        public void UIElement_PositionAndSize_CalculatesBoundsCorrectly()
        {
            // Arrange
            var element = new TestUIElement
            {
                Position = new Vector2(100, 50),
                Size = new Vector2(200, 100)
            };

            // Act
            var bounds = element.Bounds;

            // Assert
            Assert.Equal(100, bounds.X);
            Assert.Equal(50, bounds.Y);
            Assert.Equal(200, bounds.Width);
            Assert.Equal(100, bounds.Height);
        }

        #endregion

        #region UIContainer Tests

        [Fact]
        public void UIContainer_ChildManagement_WorksCorrectly()
        {
            // Arrange
            var container = new UIContainer();
            var child1 = new TestUIElement();
            var child2 = new TestUIElement();

            // Act
            container.AddChild(child1);
            container.AddChild(child2);

            // Assert
            Assert.Equal(2, container.Children.Count);
            Assert.Equal(container, child1.Parent);
            Assert.Equal(container, child2.Parent);
        }

        [Fact]
        public void UIContainer_ActivationCascade_FollowsDTXManiaPattern()
        {
            // Arrange
            var container = new UIContainer();
            var child = new TestUIElement();
            container.AddChild(child);

            // Act
            container.Activate();

            // Assert
            Assert.True(container.IsActive);
            Assert.True(child.IsActive);
        }

        [Fact]
        public void UIContainer_FocusManagement_WorksCorrectly()
        {
            // Arrange
            var container = new UIContainer();
            var child1 = new TestUIElement { Enabled = true, Visible = true };
            var child2 = new TestUIElement { Enabled = true, Visible = true };
            container.AddChild(child1);
            container.AddChild(child2);

            // Act
            container.FocusNext();

            // Assert
            Assert.Equal(child1, container.FocusedChild);
            Assert.True(child1.Focused);

            // Act
            container.FocusNext();

            // Assert
            Assert.Equal(child2, container.FocusedChild);
            Assert.True(child2.Focused);
            Assert.False(child1.Focused);
        }

        #endregion

        #region UIButton Tests

        [Fact]
        public void UIButton_ClickEvent_FiresCorrectly()
        {
            // Arrange
            var button = new UIButton("Test Button")
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(100, 50),
                Enabled = true
            };

            bool clickEventFired = false;
            button.ButtonClicked += (s, e) => clickEventFired = true;

            // Act
            button.Click(); // Programmatic click

            // Assert
            Assert.True(clickEventFired);
        }

        [Fact]
        public void UIButton_Properties_SetCorrectly()
        {
            // Arrange & Act
            var button = new UIButton("Test Text")
            {
                BackgroundColor = Color.Red,
                TextColor = Color.Blue,
                HoverColor = Color.Green
            };

            // Assert
            Assert.Equal("Test Text", button.Text);
            Assert.Equal(Color.Red, button.BackgroundColor);
            Assert.Equal(Color.Blue, button.TextColor);
            Assert.Equal(Color.Green, button.HoverColor);
        }

        #endregion

        #region UILabel Tests

        [Fact]
        public void UILabel_TextAlignment_WorksCorrectly()
        {
            // Arrange & Act
            var label = new UILabel("Test Label")
            {
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Bottom,
                TextColor = Color.Yellow
            };

            // Assert
            Assert.Equal("Test Label", label.Text);
            Assert.Equal(TextAlignment.Center, label.HorizontalAlignment);
            Assert.Equal(TextAlignment.Bottom, label.VerticalAlignment);
            Assert.Equal(Color.Yellow, label.TextColor);
        }

        #endregion

        #region InputStateManager Tests

        [Fact]
        public void InputStateManager_Initialization_WorksCorrectly()
        {
            // Arrange & Act
            var inputManager = new InputStateManager();

            // Assert
            Assert.NotNull(inputManager.CurrentKeyboardState);
            Assert.NotNull(inputManager.PreviousKeyboardState);
            Assert.NotNull(inputManager.CurrentMouseState);
            Assert.NotNull(inputManager.PreviousMouseState);
        }

        #endregion

        #region UIManager Tests

        [Fact]
        public void UIManager_ContainerManagement_WorksCorrectly()
        {
            // Arrange
            var uiManager = new UIManager();
            var container1 = new UIContainer();
            var container2 = new UIContainer();

            // Act
            uiManager.AddRootContainer(container1);
            uiManager.AddRootContainer(container2);

            // Assert
            Assert.Equal(2, uiManager.RootContainers.Count);
            Assert.True(container1.IsActive);
            Assert.True(container2.IsActive);
            Assert.Equal(container1, uiManager.FocusedContainer);
        }

        [Fact]
        public void UIManager_Disposal_CleansUpCorrectly()
        {
            // Arrange
            var uiManager = new UIManager();
            var container = new UIContainer();
            uiManager.AddRootContainer(container);

            // Act
            uiManager.Dispose();

            // Assert
            Assert.Equal(0, uiManager.RootContainers.Count);
            Assert.Null(uiManager.FocusedContainer);
        }

        #endregion
    }

    /// <summary>
    /// Test implementation of UIElement for unit testing
    /// </summary>
    internal class TestUIElement : UIElement
    {
        public bool ResourcesCreated { get; private set; }
        public bool ResourcesReleased { get; private set; }
        public new bool IsFirstUpdate => base.IsFirstUpdate;

        protected override void OnCreateResources()
        {
            base.OnCreateResources();
            ResourcesCreated = true;
        }

        protected override void OnReleaseResources()
        {
            base.OnReleaseResources();
            ResourcesReleased = true;
        }
    }
}
