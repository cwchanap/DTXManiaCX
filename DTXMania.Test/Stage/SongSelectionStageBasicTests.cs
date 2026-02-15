using DTXMania.Game.Lib.Stage;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using Moq;
using System;
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
            if (!IsGraphicsTestEnabled())
                return;

            var stage = CreateStageWithFakeGraphicsManager();

            // Assert
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Deactivate_WithoutActivation_ShouldNotThrowAndRemainInactive()
        {
            // Arrange
            if (!IsGraphicsTestEnabled())
                return;

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
            if (!IsGraphicsTestEnabled())
                return;

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
            if (!IsGraphicsTestEnabled())
                return;

            var stage = CreateStageWithFakeGraphicsManager();
            var mockSound = new Mock<ISound>();
            var mockSoundInstance = new Mock<ISoundInstance>();
            stage.SetBackgroundMusic(mockSound.Object, mockSoundInstance.Object);

            // Act
            stage.Deactivate();

            // Assert
            mockSoundInstance.Verify(x => x.Dispose(), Times.Once);
        }

        [Fact]
        public void StopCurrentPreview_ShouldReleaseManagedPreviewSoundReference()
        {
            var stage = CreateUninitializedStage();
            var previewSound = new Mock<ISound>();

            SetPrivateField(stage, "_previewSound", previewSound.Object);

            InvokePrivateMethod(stage, "StopCurrentPreview");

            previewSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
        }

        [Fact]
        public void ReleaseManagedSound_WhenSoundProvided_ShouldCallRemoveReferenceAndClearReference()
        {
            var sound = new Mock<ISound>();
            object? soundRef = sound.Object;

            var method = typeof(SongSelectionStage).GetMethod(
                "ReleaseManagedSound",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);

            object?[] args = { soundRef };
            method!.Invoke(null, args);

            sound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(args[0]);
        }

        private static SongSelectionStage CreateStageWithFakeGraphicsManager()
        {
            var mockGame = new Mock<BaseGame>();
            return new SongSelectionStage(mockGame.Object);
        }

        private static SongSelectionStage CreateUninitializedStage()
        {
#pragma warning disable SYSLIB0050
            return (SongSelectionStage)FormatterServices.GetUninitializedObject(typeof(SongSelectionStage));
#pragma warning restore SYSLIB0050
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        private static T? GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T?)field!.GetValue(target);
        }

        private static void InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(target, args);
        }

        private static bool IsGraphicsTestEnabled()
        {
            return string.Equals(Environment.GetEnvironmentVariable("DTXMANIACX_ENABLE_GRAPHICS_TESTS"), "1", StringComparison.OrdinalIgnoreCase);
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