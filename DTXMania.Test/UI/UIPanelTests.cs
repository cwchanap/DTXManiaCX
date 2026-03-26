using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using Xunit;
using DTXMania.Test;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for UIPanel component including layout modes, content area,
    /// and property validation.
    /// </summary>
    [Trait("Category", "Unit")]
    public class UIPanelTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldSetDefaultSize()
        {
            var panel = new UIPanel();
            Assert.Equal(new Vector2(200, 150), panel.Size);
        }

        [Fact]
        public void Constructor_ShouldDefaultToManualLayout()
        {
            var panel = new UIPanel();
            Assert.Equal(PanelLayoutMode.Manual, panel.LayoutMode);
        }

        [Fact]
        public void Constructor_ShouldDefaultToTransparentBackground()
        {
            var panel = new UIPanel();
            Assert.Equal(Color.Transparent, panel.BackgroundColor);
        }

        [Fact]
        public void Constructor_ShouldDefaultToGrayBorder()
        {
            var panel = new UIPanel();
            Assert.Equal(Color.Gray, panel.BorderColor);
        }

        [Fact]
        public void Constructor_ShouldDefaultToZeroBorderThickness()
        {
            var panel = new UIPanel();
            Assert.Equal(0, panel.BorderThickness);
        }

        [Fact]
        public void Constructor_ShouldDefaultToZeroPadding()
        {
            var panel = new UIPanel();
            Assert.Equal(Vector2.Zero, panel.Padding);
        }

        [Fact]
        public void Constructor_ShouldDefaultToZeroSpacing()
        {
            var panel = new UIPanel();
            Assert.Equal(0f, panel.Spacing);
        }

        #endregion

        #region Property Tests

        [Fact]
        public void BackgroundColor_ShouldSetAndGet()
        {
            var panel = new UIPanel();
            panel.BackgroundColor = Color.Red;
            Assert.Equal(Color.Red, panel.BackgroundColor);
        }

        [Fact]
        public void BorderColor_ShouldSetAndGet()
        {
            var panel = new UIPanel();
            panel.BorderColor = Color.Blue;
            Assert.Equal(Color.Blue, panel.BorderColor);
        }

        [Fact]
        public void BorderThickness_ShouldSetPositiveValue()
        {
            var panel = new UIPanel();
            panel.BorderThickness = 5;
            Assert.Equal(5, panel.BorderThickness);
        }

        [Fact]
        public void BorderThickness_NegativeValue_ShouldClampToZero()
        {
            var panel = new UIPanel();
            panel.BorderThickness = -3;
            Assert.Equal(0, panel.BorderThickness);
        }

        [Fact]
        public void BorderThickness_Zero_ShouldBeZero()
        {
            var panel = new UIPanel();
            panel.BorderThickness = 0;
            Assert.Equal(0, panel.BorderThickness);
        }

        [Fact]
        public void LayoutMode_ShouldSetAndGet()
        {
            var panel = new UIPanel();
            panel.LayoutMode = PanelLayoutMode.Vertical;
            Assert.Equal(PanelLayoutMode.Vertical, panel.LayoutMode);
        }

        [Fact]
        public void Padding_ShouldSetAndGet()
        {
            var panel = new UIPanel();
            panel.Padding = new Vector2(10, 20);
            Assert.Equal(new Vector2(10, 20), panel.Padding);
        }

        [Fact]
        public void Spacing_ShouldSetAndGet()
        {
            var panel = new UIPanel();
            panel.Spacing = 8f;
            Assert.Equal(8f, panel.Spacing);
        }

        [Fact]
        public void BackgroundTexture_DefaultIsNull()
        {
            var panel = new UIPanel();
            Assert.Null(panel.BackgroundTexture);
        }

        [Fact]
        public void BorderTexture_DefaultIsNull()
        {
            var panel = new UIPanel();
            Assert.Null(panel.BorderTexture);
        }

        [Fact]
        public void BackgroundSourceRectangle_DefaultIsNull()
        {
            var panel = new UIPanel();
            Assert.Null(panel.BackgroundSourceRectangle);
        }

        [Fact]
        public void BackgroundSourceRectangle_ShouldSetAndGet()
        {
            var panel = new UIPanel();
            var rect = new Rectangle(10, 20, 100, 50);
            panel.BackgroundSourceRectangle = rect;
            Assert.Equal(rect, panel.BackgroundSourceRectangle);
        }

        #endregion

        #region ContentArea Tests

        [Fact]
        public void ContentArea_NoBorderNoPadding_ShouldEqualBounds()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 150)
            };
            // BorderThickness=0, Padding=Zero
            var contentArea = panel.ContentArea;
            Assert.Equal(0, contentArea.X);
            Assert.Equal(0, contentArea.Y);
            Assert.Equal(200, contentArea.Width);
            Assert.Equal(150, contentArea.Height);
        }

        [Fact]
        public void ContentArea_WithBorder_ShouldReduceSizeByBorderThickness()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 150),
                BorderThickness = 5
            };
            var contentArea = panel.ContentArea;
            // X and Y offset by border thickness
            Assert.Equal(5, contentArea.X);
            Assert.Equal(5, contentArea.Y);
            // Width and Height reduced by 2 * border thickness
            Assert.Equal(190, contentArea.Width);
            Assert.Equal(140, contentArea.Height);
        }

        [Fact]
        public void ContentArea_WithPadding_ShouldReduceSizeByPadding()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 150),
                Padding = new Vector2(10, 15)
            };
            var contentArea = panel.ContentArea;
            Assert.Equal(10, contentArea.X);
            Assert.Equal(15, contentArea.Y);
            Assert.Equal(180, contentArea.Width);
            Assert.Equal(120, contentArea.Height);
        }

        [Fact]
        public void ContentArea_WithBorderAndPadding_ShouldReduceByBoth()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 150),
                BorderThickness = 3,
                Padding = new Vector2(7, 7)
            };
            var contentArea = panel.ContentArea;
            Assert.Equal(10, contentArea.X);  // 3 + 7
            Assert.Equal(10, contentArea.Y);
            Assert.Equal(180, contentArea.Width);  // 200 - 2*3 - 2*7
            Assert.Equal(130, contentArea.Height); // 150 - 2*3 - 2*7
        }

        [Fact]
        public void ContentArea_WithPosition_ShouldIncludePosition()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(50, 30),
                Size = new Vector2(200, 150)
            };
            var contentArea = panel.ContentArea;
            Assert.Equal(50, contentArea.X);
            Assert.Equal(30, contentArea.Y);
        }

        #endregion

        #region AddChild / RemoveChild Tests

        [Fact]
        public void AddChild_ShouldAddChildToChildren()
        {
            var panel = new UIPanel();
            var child = new ConcreteUIElement();
            panel.AddChild(child);
            Assert.Equal(1, panel.Children.Count);
        }

        [Fact]
        public void RemoveChild_ShouldReturnTrueAndRemoveChild()
        {
            var panel = new UIPanel();
            var child = new ConcreteUIElement();
            panel.AddChild(child);
            var result = panel.RemoveChild(child);
            Assert.True(result);
            Assert.Equal(0, panel.Children.Count);
        }

        [Fact]
        public void RemoveChild_NonExistentChild_ShouldReturnFalse()
        {
            var panel = new UIPanel();
            var child = new ConcreteUIElement();
            var result = panel.RemoveChild(child);
            Assert.False(result);
        }

        #endregion

        #region Layout Mode Tests

        [Fact]
        public void VerticalLayout_ShouldPositionChildrenVertically()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 300),
                LayoutMode = PanelLayoutMode.Vertical,
                Spacing = 0f
            };

            var child1 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };
            var child2 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };
            var child3 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };

            panel.AddChild(child1);
            panel.AddChild(child2);
            panel.AddChild(child3);

            // After adding children with Vertical layout, they should be positioned vertically
            Assert.Equal(0f, child1.Position.Y);
            Assert.Equal(30f, child2.Position.Y);
            Assert.Equal(60f, child3.Position.Y);
        }

        [Fact]
        public void VerticalLayout_WithSpacing_ShouldIncludeSpacingBetweenChildren()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 300),
                LayoutMode = PanelLayoutMode.Vertical,
                Spacing = 10f
            };

            var child1 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };
            var child2 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };

            panel.AddChild(child1);
            panel.AddChild(child2);

            Assert.Equal(0f, child1.Position.Y);
            Assert.Equal(40f, child2.Position.Y); // 30 + 10 spacing
        }

        [Fact]
        public void HorizontalLayout_ShouldPositionChildrenHorizontally()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(400, 100),
                LayoutMode = PanelLayoutMode.Horizontal,
                Spacing = 0f
            };

            var child1 = new ConcreteUIElement { Size = new Vector2(80, 50), Visible = true };
            var child2 = new ConcreteUIElement { Size = new Vector2(80, 50), Visible = true };
            var child3 = new ConcreteUIElement { Size = new Vector2(80, 50), Visible = true };

            panel.AddChild(child1);
            panel.AddChild(child2);
            panel.AddChild(child3);

            Assert.Equal(0f, child1.Position.X);
            Assert.Equal(80f, child2.Position.X);
            Assert.Equal(160f, child3.Position.X);
        }

        [Fact]
        public void HorizontalLayout_WithSpacing_ShouldIncludeSpacing()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(400, 100),
                LayoutMode = PanelLayoutMode.Horizontal,
                Spacing = 5f
            };

            var child1 = new ConcreteUIElement { Size = new Vector2(80, 50), Visible = true };
            var child2 = new ConcreteUIElement { Size = new Vector2(80, 50), Visible = true };

            panel.AddChild(child1);
            panel.AddChild(child2);

            Assert.Equal(0f, child1.Position.X);
            Assert.Equal(85f, child2.Position.X); // 80 + 5 spacing
        }

        [Fact]
        public void VerticalLayout_InvisibleChildrenSkipped_ShouldNotBePositioned()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 300),
                LayoutMode = PanelLayoutMode.Vertical,
                Spacing = 0f
            };

            var child1 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };
            var hiddenChild = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = false };
            var child2 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };

            panel.AddChild(child1);
            panel.AddChild(hiddenChild);
            panel.AddChild(child2);

            // child2 should be at Y=30, not Y=60, because hidden child is skipped
            Assert.Equal(0f, child1.Position.Y);
            Assert.Equal(30f, child2.Position.Y);
        }

        [Fact]
        public void GridLayout_ShouldPositionChildrenInGrid()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 200),
                LayoutMode = PanelLayoutMode.Grid,
                Spacing = 0f
            };

            // With width=200 and child width=100, should fit 2 columns
            var child1 = new ConcreteUIElement { Size = new Vector2(100, 50), Visible = true };
            var child2 = new ConcreteUIElement { Size = new Vector2(100, 50), Visible = true };
            var child3 = new ConcreteUIElement { Size = new Vector2(100, 50), Visible = true };
            var child4 = new ConcreteUIElement { Size = new Vector2(100, 50), Visible = true };

            panel.AddChild(child1);
            panel.AddChild(child2);
            panel.AddChild(child3);
            panel.AddChild(child4);

            // Row 0: child1 at (0,0), child2 at (100,0)
            // Row 1: child3 at (0,50), child4 at (100,50)
            Assert.Equal(0f, child1.Position.X);
            Assert.Equal(0f, child1.Position.Y);

            Assert.Equal(100f, child2.Position.X);
            Assert.Equal(0f, child2.Position.Y);

            Assert.Equal(0f, child3.Position.X);
            Assert.Equal(50f, child3.Position.Y);

            Assert.Equal(100f, child4.Position.X);
            Assert.Equal(50f, child4.Position.Y);
        }

        [Fact]
        public void ManualLayout_ShouldNotMoveChildren()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 200),
                LayoutMode = PanelLayoutMode.Manual
            };

            var child = new ConcreteUIElement
            {
                Position = new Vector2(50, 75),
                Size = new Vector2(100, 50),
                Visible = true
            };
            panel.AddChild(child);

            // Manual layout should not reposition the child
            Assert.Equal(50f, child.Position.X);
            Assert.Equal(75f, child.Position.Y);
        }

        [Fact]
        public void LayoutMode_ChangingFromManualToVertical_TriggersLayout()
        {
            var panel = new UIPanel
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(200, 300),
                Spacing = 0f
            };

            var child1 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };
            var child2 = new ConcreteUIElement { Size = new Vector2(100, 30), Visible = true };

            // Add children in Manual mode first
            panel.AddChild(child1);
            panel.AddChild(child2);

            // Manually position them
            child1.Position = new Vector2(20, 50);
            child2.Position = new Vector2(20, 120);

            // Switch to Vertical mode - should trigger layout
            panel.LayoutMode = PanelLayoutMode.Vertical;

            // After switching to Vertical, children should be re-laid out
            Assert.Equal(0f, child1.Position.Y);
            Assert.Equal(30f, child2.Position.Y);
        }

        #endregion

        #region PanelLayoutMode Enum Tests

        [Fact]
        public void PanelLayoutMode_ValuesAreDistinct()
        {
            Assert.NotEqual(PanelLayoutMode.Manual, PanelLayoutMode.Vertical);
            Assert.NotEqual(PanelLayoutMode.Manual, PanelLayoutMode.Horizontal);
            Assert.NotEqual(PanelLayoutMode.Manual, PanelLayoutMode.Grid);
            Assert.NotEqual(PanelLayoutMode.Vertical, PanelLayoutMode.Horizontal);
        }

        #endregion
    }

}
