using Xunit;
using DTX.UI;
using DTX.UI.Components;
using DTX.Song;
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
            var button = new UIButton("Test Text");

            // Assert
            Assert.Equal("Test Text", button.Text);
            Assert.NotNull(button.IdleAppearance);
            Assert.NotNull(button.HoverAppearance);
            Assert.NotNull(button.PressedAppearance);
            Assert.NotNull(button.DisabledAppearance);
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

            // Assert - Just verify the manager was created successfully
            // KeyboardState and MouseState are value types, so they're always "not null"
            Assert.NotNull(inputManager);
            Assert.Equal(Vector2.Zero, inputManager.MousePosition);
            Assert.Equal(Vector2.Zero, inputManager.MouseDelta);
        }

        #endregion

        #region Enhanced UILabel Tests

        [Fact]
        public void UILabel_ShadowAndOutlineEffects_ConfigureCorrectly()
        {
            // Arrange & Act
            var label = new UILabel("Test Label")
            {
                HasShadow = true,
                ShadowOffset = new Vector2(3, 3),
                ShadowColor = Color.Black,
                HasOutline = true,
                OutlineColor = Color.White,
                OutlineThickness = 2
            };

            // Assert
            Assert.True(label.HasShadow);
            Assert.Equal(new Vector2(3, 3), label.ShadowOffset);
            Assert.Equal(Color.Black, label.ShadowColor);
            Assert.True(label.HasOutline);
            Assert.Equal(Color.White, label.OutlineColor);
            Assert.Equal(2, label.OutlineThickness);
        }

        #endregion

        #region UIImage Tests

        [Fact]
        public void UIImage_Properties_SetCorrectly()
        {
            // Arrange & Act
            var image = new UIImage()
            {
                TintColor = Color.Red,
                Scale = new Vector2(2.0f, 2.0f),
                Rotation = 1.57f, // 90 degrees
                ScaleMode = ImageScaleMode.Uniform,
                MaintainAspectRatio = true
            };

            // Assert
            Assert.Equal(Color.Red, image.TintColor);
            Assert.Equal(new Vector2(2.0f, 2.0f), image.Scale);
            Assert.Equal(1.57f, image.Rotation);
            Assert.Equal(ImageScaleMode.Uniform, image.ScaleMode);
            Assert.True(image.MaintainAspectRatio);
        }

        #endregion

        #region Enhanced UIButton Tests

        [Fact]
        public void UIButton_StateManagement_WorksCorrectly()
        {
            // Arrange
            var button = new UIButton("Test Button");
            button.Activate(); // Need to activate for state management to work
            button.Enabled = true;

            // Act & Assert - Initial state
            button.Update(0.016);
            Assert.Equal(ButtonState.Idle, button.CurrentState);

            // Test disabled state
            button.Enabled = false;
            button.Update(0.016);
            Assert.Equal(ButtonState.Disabled, button.CurrentState);
        }

        [Fact]
        public void UIButton_StateAppearances_ConfigureCorrectly()
        {
            // Arrange
            var button = new UIButton("Test Button");

            // Act
            button.IdleAppearance.BackgroundColor = Color.Blue;
            button.HoverAppearance.BackgroundColor = Color.LightBlue;
            button.PressedAppearance.BackgroundColor = Color.DarkBlue;
            button.DisabledAppearance.BackgroundColor = Color.Gray;

            // Assert
            Assert.Equal(Color.Blue, button.IdleAppearance.BackgroundColor);
            Assert.Equal(Color.LightBlue, button.HoverAppearance.BackgroundColor);
            Assert.Equal(Color.DarkBlue, button.PressedAppearance.BackgroundColor);
            Assert.Equal(Color.Gray, button.DisabledAppearance.BackgroundColor);
        }

        #endregion

        #region UIPanel Tests

        [Fact]
        public void UIPanel_LayoutModes_SetCorrectly()
        {
            // Arrange & Act
            var panel = new UIPanel
            {
                LayoutMode = PanelLayoutMode.Vertical,
                Padding = new Vector2(10, 10),
                Spacing = 5,
                BackgroundColor = Color.DarkGray,
                BorderThickness = 2
            };

            // Assert
            Assert.Equal(PanelLayoutMode.Vertical, panel.LayoutMode);
            Assert.Equal(new Vector2(10, 10), panel.Padding);
            Assert.Equal(5, panel.Spacing);
            Assert.Equal(Color.DarkGray, panel.BackgroundColor);
            Assert.Equal(2, panel.BorderThickness);
        }

        [Fact]
        public void UIPanel_ContentArea_CalculatesCorrectly()
        {
            // Arrange
            var panel = new UIPanel
            {
                Position = new Vector2(100, 100),
                Size = new Vector2(200, 150),
                BorderThickness = 5,
                Padding = new Vector2(10, 10)
            };

            // Act
            var contentArea = panel.ContentArea;

            // Assert
            Assert.Equal(115, contentArea.X); // 100 + 5 + 10
            Assert.Equal(115, contentArea.Y); // 100 + 5 + 10
            Assert.Equal(170, contentArea.Width); // 200 - 2*5 - 2*10
            Assert.Equal(120, contentArea.Height); // 150 - 2*5 - 2*10
        }

        #endregion

        #region UIList Tests

        [Fact]
        public void UIList_ItemManagement_WorksCorrectly()
        {
            // Arrange
            var list = new UIList();

            // Act
            var item1 = list.AddItem("Item 1", "data1");
            var item2 = list.AddItem("Item 2", "data2");
            list.AddItem("Item 3", "data3");

            // Assert
            Assert.Equal(3, list.Items.Count);
            Assert.Equal("Item 1", list.Items[0].Text);
            Assert.Equal("data1", list.Items[0].Data);
            Assert.Equal("Item 2", item2.Text);
        }

        [Fact]
        public void UIList_Selection_WorksCorrectly()
        {
            // Arrange
            var list = new UIList();
            list.AddItem("Item 1");
            list.AddItem("Item 2");
            list.AddItem("Item 3");

            bool selectionChangedFired = false;
            list.SelectionChanged += (s, e) => selectionChangedFired = true;

            // Act
            list.SelectedIndex = 1;

            // Assert
            Assert.Equal(1, list.SelectedIndex);
            Assert.Equal("Item 2", list.SelectedItem?.Text);
            Assert.True(selectionChangedFired);
        }

        [Fact]
        public void UIList_ScrollOffset_WorksCorrectly()
        {
            // Arrange
            var list = new UIList { VisibleItemCount = 3 };
            for (int i = 0; i < 10; i++)
            {
                list.AddItem($"Item {i + 1}");
            }

            // Act
            list.ScrollOffset = 5;

            // Assert
            Assert.Equal(5, list.ScrollOffset);

            // Test bounds
            list.ScrollOffset = 20; // Should be clamped
            Assert.Equal(7, list.ScrollOffset); // 10 items - 3 visible = max offset 7
        }

        [Fact]
        public void UIList_DrawingConfiguration_IsSetCorrectly()
        {
            // Arrange & Act
            var list = new UIList
            {
                Position = new Vector2(100, 100),
                Size = new Vector2(200, 200),
                BackgroundColor = Color.DarkSlateGray,
                SelectedItemColor = Color.Blue,
                HoverItemColor = Color.LightBlue,
                TextColor = Color.White,
                SelectedTextColor = Color.Yellow,
                VisibleItemCount = 8,
                ItemHeight = 25
            };

            // Add some items
            list.AddItem("Song 1", "song1.dtx");
            list.AddItem("Song 2", "song2.dtx");
            list.SelectedIndex = 0;

            // Assert
            Assert.Equal(new Vector2(100, 100), list.Position);
            Assert.Equal(new Vector2(200, 200), list.Size);
            Assert.Equal(Color.DarkSlateGray, list.BackgroundColor);
            Assert.Equal(Color.Blue, list.SelectedItemColor);
            Assert.Equal(8, list.VisibleItemCount);
            Assert.Equal(25, list.ItemHeight);
            Assert.Equal(2, list.Items.Count);
            Assert.Equal(0, list.SelectedIndex);
            Assert.Equal("Song 1", list.SelectedItem?.Text);

            // Test bounds calculation
            var bounds = list.Bounds;
            Assert.Equal(100, bounds.X);
            Assert.Equal(100, bounds.Y);
            Assert.Equal(200, bounds.Width);
            Assert.Equal(200, bounds.Height);
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
            Assert.Empty(uiManager.RootContainers);
            Assert.Null(uiManager.FocusedContainer);
        }

        #endregion

        #region SongStatusPanel Tests

        [Fact]
        public void SongStatusPanel_UpdateSongInfo_UpdatesCorrectly()
        {
            var statusPanel = new SongStatusPanel();
            var mockSong = CreateMockSongNode();

            // Test updating song info
            statusPanel.UpdateSongInfo(mockSong, 0);

            // Verify the panel accepts the update without throwing
            var exception = Record.Exception(() => statusPanel.UpdateSongInfo(mockSong, 1));
            Assert.Null(exception);
        }

        [Fact]
        public void SongStatusPanel_WithNullSong_HandlesGracefully()
        {
            var statusPanel = new SongStatusPanel();

            // Test with null song
            var exception = Record.Exception(() => statusPanel.UpdateSongInfo(null, 0));
            Assert.Null(exception);
        }

        [Fact]
        public void SongStatusPanel_DTXManiaNXLayout_UsesCorrectPositioning()
        {
            var statusPanel = new SongStatusPanel();

            // Verify the panel uses DTXManiaNX sizing
            Assert.Equal(new Vector2(580, 320), statusPanel.Size);
        }

        private SongListNode CreateMockSongNode()
        {
            var metadata = new SongMetadata
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre",
                BPM = 120,
                DrumLevel = 5,
                GuitarLevel = 4,
                BassLevel = 3
            };

            var mockSong = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                Metadata = metadata,
                Scores = new SongScore[]
                {
                    new SongScore
                    {
                        Metadata = metadata,
                        BestScore = 950000,
                        BestRank = 85,
                        HighSkill = 87.42,
                        PlayCount = 5,
                        FullCombo = false
                    }
                }
            };
            return mockSong;
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
