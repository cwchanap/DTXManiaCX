using Moq;
using Xunit;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using Microsoft.Xna.Framework;
using System;
using DTXMania.Game.Lib.Song.Entities;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using DTXMania.Game.Lib.Resources;

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
            var mockResourceManager = new Mock<IResourceManager>();
            var button = new UIButton(mockResourceManager.Object, "Test Button")
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
            var mockResourceManager = new Mock<IResourceManager>();
            var button = new UIButton(mockResourceManager.Object, "Test Text");

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
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Center,
                VerticalAlignment = DTX.UI.Components.TextAlignment.Bottom,
                TextColor = Color.Yellow
            };

            // Assert
            Assert.Equal("Test Label", label.Text);
            Assert.Equal(DTX.UI.Components.TextAlignment.Center, label.HorizontalAlignment);
            Assert.Equal(DTX.UI.Components.TextAlignment.Bottom, label.VerticalAlignment);
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
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre"
            };
            
            var chart = new SongChart
            {
                BPM = 120,
                DrumLevel = 5,
                GuitarLevel = 4,
                BassLevel = 3
            };

            var mockSong = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                DatabaseSong = song,
                DatabaseChart = chart,
                Scores = new SongScore[]
                {
                    new SongScore
                    {
                        Instrument = EInstrumentPart.DRUMS,
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
