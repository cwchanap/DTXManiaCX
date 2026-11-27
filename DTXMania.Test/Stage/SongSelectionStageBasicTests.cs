using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Basic unit tests for SongSelectionStage that don't require graphics initialization
    /// These tests focus on constructor validation and basic property testing
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongSelectionStageBasicTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongSelectionStage(null));
        }

        [Fact]
        public void Constructor_WhenCreateTabsAndTabIoSeamsAreBothSupplied_ShouldThrowArgumentException()
        {
            var exception = Assert.Throws<ArgumentException>(() => new SongSelectionStage(
                CreateUninitializedGame(),
                createPreviewInstance: null,
                createTabs: (intake, getScope, logger) => null!,
                loadRecentPlaysAsync: () => Task.FromResult(new List<DTXMania.Game.Lib.Song.SongListNode>())));

            Assert.Contains("createTabs", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Constructor_WhenGameHasNoInputManager_ShouldCreateOwnedFallbackInputManager()
        {
            var stage = CreateStageWithFakeGraphicsManager();

            Assert.True(GetPrivateField<bool>(stage, "_ownsInputManager"));
            Assert.NotNull(GetPrivateField<InputManager>(stage, "_inputManager"));
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

        [Fact]
        public void Deactivate_WhenStageOwnsInputManager_ShouldDisposeOwnedInputManager()
        {
            var stage = CreateStageWithFakeGraphicsManager();
            var probeInputManager = new ProbeInputManager();

            SetPrivateField(stage, "_inputManager", probeInputManager);
            SetPrivateField(stage, "_ownsInputManager", true);

            stage.Deactivate();

            Assert.True(probeInputManager.WasDisposed);
        }

        [Fact]
        public void Deactivate_WhenStageUsesSharedInputManager_ShouldNotDisposeSharedInputManager()
        {
            var stage = CreateStageWithFakeGraphicsManager();
            var probeInputManager = new ProbeInputManager();

            SetPrivateField(stage, "_inputManager", probeInputManager);
            SetPrivateField(stage, "_ownsInputManager", false);

            stage.Deactivate();

            Assert.False(probeInputManager.WasDisposed);
        }

        private static SongSelectionStage CreateStageWithFakeGraphicsManager()
        {
            return new SongSelectionStage(CreateUninitializedGame());
        }

        private static BaseGame CreateUninitializedGame()
        {
#pragma warning disable SYSLIB0050
            return (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));
#pragma warning restore SYSLIB0050
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

        private sealed class ProbeInputManager : InputManager
        {
            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                base.Dispose(disposing);
            }
        }

        #endregion

        #region Reflection-Based Shape Tests

        // Behavioural activation/fade-in lifecycle tests live in SongSelectionStageTests.cs (graphics-dependent, so GraphicsFacts there).

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

        #region PopulateTelemetry Tests

        [Fact]
        public void PopulateTelemetry_WhenSongSelected_ShouldExposeTitleDifficultyAndPanelState()
        {
            // HPA-802: telemetry reads the preview controller, so the stage must be
            // constructor-built (an uninitialized object has no controller).
            var stage = CreateStageWithFakeGraphicsManager();
            var song = new SongListNode { Title = "My Song" };
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 3);
            SetPrivateField(stage, "_isInStatusPanel", true);
            var configManager = new ConfigManager();
            configManager.Config.PlaySpeedPercent = 75;
            configManager.Config.PitchSemitones = 3;
            SetPrivateField(stage, "_configManager", configManager);

            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);

            Assert.Equal("My Song", telemetry.SelectedSongTitle);
            Assert.Equal(3, telemetry.SelectedDifficulty);
            Assert.True(telemetry.InStatusPanel);
            Assert.Equal(75, telemetry.PlaySpeedPercent);
            Assert.Equal(3, telemetry.PitchSemitones);
            Assert.False(telemetry.PlaybackProfileFrozen);
        }

        [Fact]
        public void PopulateTelemetry_WhenNoSongSelected_ShouldSetDefaults()
        {
            // HPA-802: telemetry reads the preview controller, so the stage must be
            // constructor-built (an uninitialized object has no controller).
            var stage = CreateStageWithFakeGraphicsManager();
            // _selectedSong is null (fresh stage)

            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);

            Assert.Null(telemetry.SelectedSongTitle);
            Assert.Equal(PlaySpeedRange.Default, telemetry.PlaySpeedPercent);
            Assert.Equal(PitchRange.Default, telemetry.PitchSemitones);
            Assert.False(telemetry.PlaybackProfileFrozen);
        }

        [Fact]
        public void PopulateTelemetry_WhenNullTelemetry_ShouldThrowArgumentNullException()
        {
            var stage = CreateUninitializedStage();
            Assert.Throws<ArgumentNullException>(() => stage.PopulateTelemetry(null!));
        }

        #endregion
    }
}
