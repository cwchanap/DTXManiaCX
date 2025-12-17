using DTXMania.Game.Lib.Stage;
using DTXMania.Game;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Resources;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Basic unit tests for SongSelectionStage that don't require graphics initialization
    /// These tests focus on constructor validation and basic property testing
    /// </summary>
    public class SongSelectionStageBasicTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongSelectionStage(null));
        }

        #endregion

        #region Behaviour Tests (No Graphics Initialization)

        [Fact]
        public void InitialState_ShouldBeInactive()
        {
            // Arrange
            var stage = CreateStageWithFakeGraphicsManager();

            // Assert
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Deactivate_WithoutActivation_ShouldNotThrowAndRemainInactive()
        {
            // Arrange
            var stage = CreateStageWithFakeGraphicsManager();

            // Act
            stage.Deactivate();

            // Assert
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Deactivate_MultipleCallsShouldBeIdempotent()
        {
            // Arrange
            var stage = CreateStageWithFakeGraphicsManager();

            // Act
            stage.Deactivate();
            stage.Deactivate();

            // Assert
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Deactivate_WhenBackgroundMusicIsSet_ShouldDisposeBackgroundMusicInstance()
        {
            // Arrange
            var stage = CreateStageWithFakeGraphicsManager();
            var mockSound = new Mock<ISound>();
            var mockSoundInstance = new Mock<ISoundInstance>();
            stage.SetBackgroundMusic(mockSound.Object, mockSoundInstance.Object);

            // Act
            stage.Deactivate();

            // Assert
            mockSoundInstance.Verify(x => x.Dispose(), Times.Once);
        }

        private static SongSelectionStage CreateStageWithFakeGraphicsManager()
        {
            var game = (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));

            var renderTargetManager = CreateEmptyRenderTargetManager();
            var mockGraphicsManager = new Mock<IGraphicsManager>();
            mockGraphicsManager.SetupGet(x => x.RenderTargetManager).Returns(renderTargetManager);

            var graphicsManagerField = typeof(BaseGame).GetField("_graphicsManager", BindingFlags.NonPublic | BindingFlags.Instance);
            graphicsManagerField!.SetValue(game, mockGraphicsManager.Object);

            return new SongSelectionStage(game);
        }

        private static RenderTargetManager CreateEmptyRenderTargetManager()
        {
            var instance = (RenderTargetManager)FormatterServices.GetUninitializedObject(typeof(RenderTargetManager));

            var infoType = typeof(RenderTargetManager).GetNestedType("RenderTargetInfo", BindingFlags.NonPublic);
            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), infoType!);
            var dict = Activator.CreateInstance(dictType);

            var renderTargetsField = typeof(RenderTargetManager).GetField("_renderTargets", BindingFlags.NonPublic | BindingFlags.Instance);
            renderTargetsField!.SetValue(instance, dict);

            var disposedField = typeof(RenderTargetManager).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance);
            disposedField!.SetValue(instance, false);

            return instance;
        }

        #endregion

        #region Reflection-Based Shape Tests

        // Behavioural activation/fade-in lifecycle tests live in SongSelectionStageTests.cs (graphics-dependent and excluded for MAC_BUILD).

        [Fact]
        public void Type_Property_ShouldExistAndReturnStageType()
        {
            // Assert
            var property = typeof(SongSelectionStage).GetProperty(
                "Type",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            Assert.NotNull(property);
            Assert.Equal(typeof(StageType), property!.PropertyType);
        }

        [Fact]
        public void SongSelectionStage_ShouldInheritFromBaseStage()
        {
            Assert.True(typeof(BaseStage).IsAssignableFrom(typeof(SongSelectionStage)));
        }

        #endregion
    }
}