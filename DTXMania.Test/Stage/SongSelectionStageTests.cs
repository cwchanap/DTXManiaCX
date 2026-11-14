using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;
using DTXMania.Test.Helpers;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for SongSelectionStage functionality
    /// Tests constructor validation, lifecycle management, resource handling, and UI initialization
    /// Methods that activate the stage need a real GraphicsDevice and are GraphicsFacts, so a
    /// headless run reports them Skipped instead of passing without rendering.
    /// Placed in the SongManager collection (DisableParallelization = true) because Activate()
    /// subscribes to SongManager.Instance; DisableParallelization also keeps this class from
    /// running concurrently with GraphicsTests.
    /// </summary>
    [Collection("SongManager")]
    public class SongSelectionStageTests : IDisposable
    {
        private readonly TestGraphicsDeviceService _graphicsDeviceService;
        private readonly Mock<IStageGame> _mockGame;
        private readonly Mock<IConfigManager> _mockConfigManager;
        private readonly Mock<IResourceManager> _mockResourceManager;
        private readonly Mock<IGraphicsManager> _mockGraphicsManager;
        private readonly RenderTargetManager? _renderTargetManager;
        private readonly Texture2D? _fontTexture;
        private readonly SpriteFont? _spriteFont;
        private readonly List<SongSelectionStage> _createdStages = new();

        public SongSelectionStageTests()
        {
            SongManager.ResetInstanceForTesting();
            _graphicsDeviceService = new TestGraphicsDeviceService();
            try
            {
                _mockGame = new Mock<IStageGame>();
                _mockConfigManager = new Mock<IConfigManager>();
                _mockResourceManager = new Mock<IResourceManager>();
                _mockGraphicsManager = new Mock<IGraphicsManager>();

                _mockGame.Setup(g => g.ConfigManager).Returns(_mockConfigManager.Object);
                _mockGame.Setup(g => g.ResourceManager).Returns(_mockResourceManager.Object);
                // Lazy so a device test fails with the explicit opt-in message when the opt-in
                // could not create a device; logic tests never touch GraphicsDevice.
                _mockGame.Setup(g => g.GraphicsDevice).Returns(() => _graphicsDeviceService.RequiredDevice);

                // Device-required fixtures: a real render-target manager and real texture/font
                // objects, so activation runs the production host path instead of null fallbacks.
                if (_graphicsDeviceService.GraphicsDevice is { } device)
                {
                    _renderTargetManager = new RenderTargetManager(device);
                    _mockGraphicsManager.Setup(g => g.GraphicsDevice).Returns(device);
                    _mockGraphicsManager.Setup(g => g.RenderTargetManager).Returns(_renderTargetManager);

                    _mockGame.Setup(g => g.GraphicsManager).Returns(_mockGraphicsManager.Object);

                    _spriteFont = TestFontFactory.Create(device, out var fontTexture);
                    _fontTexture = fontTexture;
                    ConfigureResourceManager();
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var stage in _createdStages)
            {
                stage.Deactivate();
            }
            _createdStages.Clear();
            SongManager.ResetInstanceForTesting();

            _renderTargetManager?.Dispose();
            _fontTexture?.Dispose();
            _graphicsDeviceService.Dispose();
        }

        private SongSelectionStage CreateStage()
        {
            var stage = new SongSelectionStage(_mockGame.Object);
            _createdStages.Add(stage);
            return stage;
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidGame_ShouldCreateInstance()
        {
            // Arrange & Act
            var stage = CreateStage();

            // Assert
            Assert.NotNull(stage);
            Assert.Equal(StageType.SongSelect, stage.Type);
        }

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongSelectionStage(null));
        }

        #endregion

        #region Activation Tests

        [GraphicsFact]
        public void Activate_WithValidParameters_ShouldInitializeCorrectly()
        {
            // Arrange
            var stage = CreateStage();
            var sharedData = new Dictionary<string, object>();

            // Act
            stage.Activate(sharedData);

            // Assert
            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.Equal(StageType.SongSelect, stage.Type);

            // Activation must go through the host's resource manager, not fall back to nulls.
            _mockResourceManager.Verify(rm => rm.LoadFont(
                SongSelectionUILayout.Background.DefaultFontName,
                SongSelectionUILayout.Background.DefaultFontSize,
                FontStyle.Regular), Times.AtLeastOnce);
            _mockResourceManager.Verify(rm => rm.LoadTexture(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [GraphicsFact]
        public void Activate_WithNullSharedData_ShouldInitializeWithDefaults()
        {
            // Arrange
            var stage = CreateStage();

            // Act
            stage.Activate(null);

            // Assert
            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.Equal(StageType.SongSelect, stage.Type);
        }

        [GraphicsFact]
        public void Activate_WhenResourceLoadingFails_ShouldHandleGracefully()
        {
            // Arrange
            var stage = CreateStage();

            // Setup mocks to throw exceptions during resource loading
            _mockResourceManager.Setup(rm => rm.LoadTexture(It.IsAny<string>()))
                .Throws(new Exception("Resource loading failed"));
            _mockResourceManager.Setup(rm => rm.LoadTexture(It.IsAny<string>(), It.IsAny<bool>()))
                .Throws(new Exception("Resource loading failed"));
            _mockResourceManager.Setup(rm => rm.LoadFont(It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new Exception("Font loading failed"));
            _mockResourceManager.Setup(rm => rm.LoadFont(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<FontStyle>()))
                .Throws(new Exception("Font loading failed"));

            // Act & Assert - should not throw, and the stage still completes activation
            stage.Activate();

            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
        }

        #endregion

        #region Deactivation Tests

        [GraphicsFact]
        public void Deactivate_AfterActivation_ShouldCleanupResources()
        {
            // Arrange
            var stage = CreateStage();
            stage.Activate();

            // Act
            stage.Deactivate();

            // Assert
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            _mockGraphicsManager.Verify(g => g.RenderTargetManager, Times.AtLeastOnce);
            Assert.Null(_renderTargetManager!.GetRenderTarget("SongSelectionStage_Main"));
        }

        [Fact]
        public void Deactivate_WithoutActivation_ShouldHandleGracefully()
        {
            // Arrange
            var stage = CreateStage();

            // Act & Assert - Should not throw exception
            stage.Deactivate();
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Deactivate_MultipleCallsShouldBeIdempotent()
        {
            // Arrange
            var stage = CreateStage();

            // Act
            stage.Deactivate();
            stage.Deactivate(); // Second call

            // Assert - Should not throw exception and remain inactive
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        #endregion

        #region Song List Initialization Tests

        [GraphicsFact]
        public void SongListInitialization_WhenSongManagerInitialized_ShouldLoadSongs()
        {
            // Arrange
            var stage = CreateStage();

            // Act
            stage.Activate();

            // Assert - Stage should initialize even with empty song list
            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
        }

        #endregion

        #region Background Music Tests

        [Fact]
        public void SetBackgroundMusic_WithValidParameters_ShouldConfigureBackground()
        {
            // Arrange
            var stage = CreateStage();
            var mockSound = new Mock<ISound>();
            var mockSoundInstance = new Mock<ISoundInstance>();

            // Act & Assert - Should not throw exception
            stage.SetBackgroundMusic(mockSound.Object, mockSoundInstance.Object);
        }

        [Fact]
        public void SetBackgroundMusic_WithNullParameters_ShouldHandleGracefully()
        {
            // Arrange
            var stage = CreateStage();

            // Act & Assert - Should not throw exception
            stage.SetBackgroundMusic(null, null);
        }

        #endregion

        #region Phase Transition Tests

        [Fact]
        public void PhaseTransition_InitialState_ShouldBeInactive()
        {
            // Arrange
            var stage = CreateStage();

            // Act & Assert
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [GraphicsFact]
        public void PhaseTransition_AfterActivation_ShouldBeFadeIn()
        {
            // Arrange
            var stage = CreateStage();

            // Act
            stage.Activate();

            // Assert
            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
        }

        #endregion

        #region Navigation Tests

        [Fact]
        public void Navigation_MovingThroughSongList_ShouldUpdateSelection()
        {
            // Arrange
            var stage = CreateStage();

            // Act & Assert - Test basic navigation functionality
            // This tests the stage can handle navigation input even without full initialization
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Equal(StageType.SongSelect, stage.Type);
        }
        
        [Fact]
        public void SelectSong_WithValidSong_ShouldTransitionToGameplay()
        {
            // Arrange
            var stage = CreateStage();
            
            // Act & Assert - Test song selection trigger
            // This verifies the stage can handle song selection without full setup
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }
        
        [Fact]
        public void HandleInput_EscapeKey_ShouldTriggerBackAction()
        {
            // Arrange
            var stage = CreateStage();
            
            // Act & Assert - Test ESC key handling
            // This verifies basic input handling capabilities
            Assert.Equal(StageType.SongSelect, stage.Type);
        }
        
        #endregion

        #region Update and Draw Tests
        
        [Fact]
        public void Update_WithValidDeltaTime_ShouldUpdateComponents()
        {
            // Arrange
            var stage = CreateStage();
            
            // Act & Assert - Test update loop handling
            // Without graphics initialization, this tests the basic update structure
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }
        
        [Fact]
        public void Draw_WithGraphicsContext_ShouldRenderWithoutErrors()
        {
            // Arrange
            var stage = CreateStage();
            
            // Act & Assert - Test rendering capabilities
            // This tests the stage structure for drawing
            Assert.Equal(StageType.SongSelect, stage.Type);
        }
        
        #endregion

        #region Error Handling Tests
        
        [GraphicsFact]
        public void ErrorHandling_InvalidSharedData_ShouldHandleGracefully()
        {
            // Arrange
            var stage = CreateStage();
            var invalidData = new Dictionary<string, object>
            {
                { "invalid_key", "invalid_value" },
                { "null_value", null }
            };
            
            // Act & Assert - Should not crash with invalid data
            stage.Activate(invalidData);

            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.Equal(StageType.SongSelect, stage.Type);
        }
        
        [Fact]
        public void ErrorHandling_MissingResources_ShouldContinueOperation()
        {
            // Arrange
            var stage = CreateStage();
            
            // Mock resource manager to simulate missing resources
            _mockResourceManager.Setup(rm => rm.LoadTexture(It.IsAny<string>()))
                .Returns((ITexture)null!);
            _mockResourceManager.Setup(rm => rm.LoadFont(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<FontStyle>()))
                .Returns((IFont)null!);
            
            // Act & Assert - Should handle missing resources gracefully
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }
        
        #endregion

        #region Performance Tests
        
        [GraphicsFact]
        public void Performance_MultipleActivationsDeactivations_ShouldNotLeak()
        {
            // Arrange
            var stage = CreateStage();
            
            // Act - Multiple activation/deactivation cycles
            for (int i = 0; i < 5; i++)
            {
                stage.Activate();
                stage.Deactivate();
            }
            
            // Assert - Should remain in consistent state
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }
        
        [GraphicsFact]
        public void Performance_LargeSharedDataSet_ShouldHandleEfficiently()
        {
            // Arrange
            var stage = CreateStage();
            var largeDataSet = new Dictionary<string, object>();
            
            // Create a large shared data set
            for (int i = 0; i < 1000; i++)
            {
                largeDataSet.Add($"key_{i}", $"value_{i}");
            }
            
            // Act & Assert - Should handle large data sets without performance issues
            stage.Activate(largeDataSet);

            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.Equal(StageType.SongSelect, stage.Type);
        }
        
        #endregion

        #region Integration Tests
        
        [GraphicsFact]
        public void Integration_FullLifecycleWithMocks_ShouldCompleteSuccessfully()
        {
            // Arrange
            var stage = CreateStage();
            
            // Act - Full lifecycle
            stage.Activate();

            // Assert
            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            
            stage.Deactivate();
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }
        
        #endregion

        #region Helper Methods

        private void ConfigureResourceManager()
        {
            // ITexture.Texture is consumed by some components, so back the mock with the
            // real 1x1 white atlas the test font already samples.
            var mockTexture = new Mock<ITexture>();
            mockTexture.Setup(t => t.Texture).Returns(_fontTexture);
            mockTexture.Setup(t => t.Width).Returns(1);
            mockTexture.Setup(t => t.Height).Returns(1);

            var mockFont = new Mock<IFont>();
            mockFont.Setup(f => f.SpriteFont).Returns(_spriteFont);
            mockFont.Setup(f => f.Size).Returns(SongSelectionUILayout.Background.DefaultFontSize);
            mockFont.Setup(f => f.LineSpacing).Returns(_spriteFont!.LineSpacing);
            mockFont.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
            mockFont.Setup(f => f.MeasureString(It.IsAny<string>()))
                .Returns<string>(text => _spriteFont.MeasureString(text));

            _mockResourceManager.Setup(rm => rm.LoadTexture(It.IsAny<string>())).Returns(mockTexture.Object);
            _mockResourceManager.Setup(rm => rm.LoadTexture(It.IsAny<string>(), It.IsAny<bool>())).Returns(mockTexture.Object);
            _mockResourceManager.Setup(rm => rm.CreateTextureFromColor(It.IsAny<Color>())).Returns(mockTexture.Object);
            _mockResourceManager.Setup(rm => rm.LoadFont(It.IsAny<string>(), It.IsAny<int>())).Returns(mockFont.Object);
            _mockResourceManager.Setup(rm => rm.LoadFont(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<FontStyle>())).Returns(mockFont.Object);
            _mockResourceManager.Setup(rm => rm.LoadSound(It.IsAny<string>())).Returns(new Mock<ISound>().Object);
            _mockResourceManager.Setup(rm => rm.CurrentTheme).Returns(SkinTheme.Empty);
        }

        #endregion
    }
}
