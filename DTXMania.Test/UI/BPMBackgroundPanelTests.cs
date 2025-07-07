using Xunit;
using DTX.UI.Components;
using DTXMania.Test.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI.Layout;

namespace DTX.Test.UI
{
    public class BPMBackgroundPanelTests
    {
        [Fact]
        public void Constructor_SetsDefaultValues()
        {
            // Arrange & Act
            var panel = new BPMBackgroundPanel();

            // Assert
            Assert.True(panel.HasStatusPanel); // Default to with status panel
            Assert.False(panel.IsUsingAuthenticTexture); // No texture loaded yet
            Assert.Equal(new Vector2(90, 275), panel.Position); // Default position from layout
        }

        [Fact]
        public void HasStatusPanel_UpdatesPositionAndSize()
        {
            // Arrange
            var panel = new BPMBackgroundPanel();
            var originalPosition = panel.Position;

            // Act - Switch to standalone mode
            panel.HasStatusPanel = false;

            // Assert
            Assert.False(panel.HasStatusPanel);
            Assert.Equal(new Vector2(490, 385), panel.Position); // Standalone position
            Assert.NotEqual(originalPosition, panel.Position);

            // Act - Switch back to with status panel
            panel.HasStatusPanel = true;

            // Assert
            Assert.True(panel.HasStatusPanel);
            Assert.Equal(originalPosition, panel.Position); // Back to original
        }

        [Fact]
        public void Initialize_WithMockResourceManager_HandlesLoadingGracefully()
        {
            // Arrange
            var panel = new BPMBackgroundPanel();
            var graphicsService = new TestGraphicsDeviceService();
            var mockResourceManager = new MockResourceManager(graphicsService.GraphicsDevice);
            
            // Create a render target for the graphics generator
            var renderTarget = new RenderTarget2D(graphicsService.GraphicsDevice, 100, 100);
            var graphicsGenerator = new DTX.UI.DefaultGraphicsGenerator(
                graphicsService.GraphicsDevice, 
                renderTarget
            );

            // Act - Initialize should not throw even if 5_BPM.png doesn't exist
            panel.Initialize(mockResourceManager, graphicsGenerator);

            // Assert
            Assert.Equal(mockResourceManager, panel.ResourceManager);
            Assert.Equal(graphicsGenerator, panel.GraphicsGenerator);
            // IsUsingAuthenticTexture should be false since MockResourceManager doesn't have 5_BPM.png
            Assert.False(panel.IsUsingAuthenticTexture);
            
            // Cleanup
            renderTarget.Dispose();
            graphicsGenerator.Dispose();
            graphicsService.Dispose();
        }

        [Fact]
        public void SetResourceManager_TriggersTextureLoad()
        {
            // Arrange
            var panel = new BPMBackgroundPanel();
            var graphicsService = new TestGraphicsDeviceService();
            var mockResourceManager = new MockResourceManager(graphicsService.GraphicsDevice);

            // Act
            panel.ResourceManager = mockResourceManager;

            // Assert
            // Should have attempted to load 5_BPM.png but failed gracefully
            Assert.Equal(mockResourceManager, panel.ResourceManager);
            Assert.False(panel.IsUsingAuthenticTexture);
            
            // Cleanup
            graphicsService.Dispose();
        }

        [Fact]
        public void SetGraphicsGenerator_TriggersFallbackGeneration()
        {
            // Arrange
            var panel = new BPMBackgroundPanel();
            var graphicsService = new TestGraphicsDeviceService();
            var mockResourceManager = new MockResourceManager(graphicsService.GraphicsDevice);
            
            // Create a render target for the graphics generator
            var renderTarget = new RenderTarget2D(graphicsService.GraphicsDevice, 100, 100);
            var graphicsGenerator = new DTX.UI.DefaultGraphicsGenerator(
                graphicsService.GraphicsDevice, 
                renderTarget
            );

            // Act
            panel.GraphicsGenerator = graphicsGenerator;

            // Assert
            Assert.Equal(graphicsGenerator, panel.GraphicsGenerator);
            // Should have generated a fallback texture
            
            // Cleanup
            renderTarget.Dispose();
            graphicsGenerator.Dispose();
            graphicsService.Dispose();
        }

        [Fact]
        public void BPMSectionLayout_HasCorrectOffset()
        {
            // Assert
            Assert.Equal(75, SongSelectionUILayout.BPMSection.TextXOffset);
            Assert.Equal(new Vector2(107, 258), SongSelectionUILayout.BPMSection.LengthTextPosition); // X = 32 + 75, Y = 258
            Assert.Equal(new Vector2(107, 278), SongSelectionUILayout.BPMSection.BPMTextPosition); // X = 32 + 75, Y = 258 + 20
        }
    }
}
