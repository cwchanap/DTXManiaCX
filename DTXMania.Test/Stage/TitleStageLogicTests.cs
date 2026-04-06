using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
using System.Runtime.Serialization;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class TitleStageLogicTests
    {
        [Fact]
        public void MoveCursorUp_FromFirstItem_ShouldWrapToLastAndPlaySound()
        {
            var stage = CreateStage();
            var cursorSound = CreateSoundReturningInstance();
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "MoveCursorUp");

            Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_currentMenuIndex"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isMovingUp"));
            Assert.Equal(0d, ReflectionHelpers.GetPrivateField<double>(stage, "_menuMoveTimer"));
            cursorSound.Verify(x => x.Play(0.7f), Times.Once);
        }

        [Fact]
        public void MoveCursorDown_FromLastItem_ShouldWrapToFirstAndPlaySound()
        {
            var stage = CreateStage();
            var cursorSound = CreateSoundReturningInstance();
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 2);
            ReflectionHelpers.SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "MoveCursorDown");

            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_currentMenuIndex"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isMovingDown"));
            Assert.Equal(0d, ReflectionHelpers.GetPrivateField<double>(stage, "_menuMoveTimer"));
            cursorSound.Verify(x => x.Play(0.7f), Times.Once);
        }

        [Fact]
        public void UpdateAnimations_ShouldResetFlashAndMovementFlagsWhenThresholdExceeded()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_cursorFlashTimer", 0.6d);
            ReflectionHelpers.SetPrivateField(stage, "_menuMoveTimer", 0.05d);
            ReflectionHelpers.SetPrivateField(stage, "_isMovingUp", true);
            ReflectionHelpers.SetPrivateField(stage, "_isMovingDown", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateAnimations", 0.2d);

            Assert.Equal(0d, ReflectionHelpers.GetPrivateField<double>(stage, "_cursorFlashTimer"));
            Assert.Equal(0d, ReflectionHelpers.GetPrivateField<double>(stage, "_menuMoveTimer"));
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isMovingUp"));
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_isMovingDown"));
        }

        [Fact]
        public void OnTransitionInStarted_WithStartupTransition_ShouldUseStartupFadePhase()
        {
            var stage = CreateStage();

            stage.OnTransitionIn(new StartupToTitleTransition());

            Assert.Equal("FadeInFromStartup", ReflectionHelpers.GetPrivateField<object>(stage, "_titlePhase")!.ToString());
        }

        [Fact]
        public void OnTransitionInStarted_WithNormalTransition_ShouldUseFadeInPhase()
        {
            var stage = CreateStage();

            stage.OnTransitionIn(new CrossfadeTransition());

            Assert.Equal("FadeIn", ReflectionHelpers.GetPrivateField<object>(stage, "_titlePhase")!.ToString());
        }

        [Fact]
        public void OnTransitionComplete_ShouldSetNormalPhase()
        {
            var stage = CreateStage();

            stage.OnTransitionComplete();

            Assert.Equal("Normal", ReflectionHelpers.GetPrivateField<object>(stage, "_titlePhase")!.ToString());
            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenGameStartAndTransitionAllowed_ShouldGoToSongSelect()
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime: 1.0, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var gameStartSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_gameStartSound", gameStartSound.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "SelectCurrentMenuItem");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition)),
                Times.Once);
            gameStartSound.Verify(x => x.Play(0.9f), Times.Once);
            Assert.Equal(1.0, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenConfigAndTransitionAllowed_ShouldGoToConfig()
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime: 1.2, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var selectSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 1);
            ReflectionHelpers.SetPrivateField(stage, "_selectSound", selectSound.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "SelectCurrentMenuItem");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Config,
                    It.Is<IStageTransition>(transition => transition is CrossfadeTransition)),
                Times.Once);
            selectSound.Verify(x => x.Play(0.8f), Times.Once);
            Assert.Equal(1.2, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenExitAndTransitionAllowed_ShouldExitGame()
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime: 1.4, lastStageTransitionTime: 0.0);
            var selectSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 2);
            ReflectionHelpers.SetPrivateField(stage, "_selectSound", selectSound.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "SelectCurrentMenuItem");

            selectSound.Verify(x => x.Play(0.8f), Times.Once);
            Assert.Equal(1.4, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenDebounceBlocksTransition_ShouldDoNothing()
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var gameStartSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_gameStartSound", gameStartSound.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "SelectCurrentMenuItem");

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
                Times.Never);
            gameStartSound.Verify(x => x.Play(It.IsAny<float>()), Times.Never);
        }

        [Fact]
        public void IsMenuSelectTriggered_ShouldOnlyRespondToActivateCommand()
        {
            var inputManager = new TestInputManager();
            inputManager.SetPressedCommand(InputCommandType.Activate);
            Assert.True(TitleStage.IsMenuSelectTriggered(inputManager));

            var nonActivateInputManager = new TestInputManager();
            nonActivateInputManager.SetPressedCommand(InputCommandType.MoveDown);
            nonActivateInputManager.SetPressedKey(Keys.Enter);
            Assert.False(TitleStage.IsMenuSelectTriggered(nonActivateInputManager));
        }

        [Fact]
        public void LoadMenuTexture_WhenResourceLoadFails_ShouldLeaveMenuTextureUnset()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadTexture(TexturePath.TitleMenu))
                .Throws(new InvalidOperationException("missing texture"));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadMenuTexture");

            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_menuTexture"));
        }

        [Fact]
        public void LoadSoundEffects_WhenGameStartSoundMissing_ShouldFallbackToDecideSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var cursorSound = CreateSoundReturningInstance();
            var selectSound = CreateSoundReturningInstance();

            resourceManager
                .Setup(x => x.LoadSound("Sounds/Move.ogg"))
                .Returns(cursorSound.Object);
            resourceManager
                .Setup(x => x.LoadSound("Sounds/Decide.ogg"))
                .Returns(selectSound.Object);
            resourceManager
                .Setup(x => x.LoadSound("Sounds/Game start.ogg"))
                .Throws(new InvalidOperationException("missing game start"));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadSoundEffects");

            Assert.Same(selectSound.Object, ReflectionHelpers.GetPrivateField<ISound>(stage, "_gameStartSound"));
            resourceManager.Verify(x => x.LoadSound("Sounds/Decide.ogg"), Times.Exactly(2));
        }

        [Fact]
        public void Deactivate_ShouldResetMenuAndInputTrackingState()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 12.3d);
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 2);
            ReflectionHelpers.SetPrivateField(stage, "_hoveredMenuIndex", 1);
            ReflectionHelpers.SetPrivateField(stage, "_titlePhase", Enum.Parse(ReflectionHelpers.GetPrivateField<object>(stage, "_titlePhase")!.GetType(), "Normal"));

            stage.Deactivate();

            Assert.Equal(0d, ReflectionHelpers.GetPrivateField<double>(stage, "_elapsedTime"));
            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_currentMenuIndex"));
            Assert.Equal(-1, ReflectionHelpers.GetPrivateField<int>(stage, "_hoveredMenuIndex"));
            Assert.Equal("FadeIn", ReflectionHelpers.GetPrivateField<object>(stage, "_titlePhase")!.ToString());
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Dispose_ShouldReleaseTitleResourcesAndClearFields()
        {
            var stage = CreateStage();
            var menuTexture = new Mock<ITexture>();
            var resourceManager = new Mock<IResourceManager>();
            var cursorSound = CreateSoundReturningInstance();
            var selectSound = CreateSoundReturningInstance();
            var gameStartSound = CreateSoundReturningInstance();
#pragma warning disable SYSLIB0050
            var whitePixel = (TrackingTexture2D)FormatterServices.GetUninitializedObject(typeof(TrackingTexture2D));
            var spriteBatch = (TrackingSpriteBatch)FormatterServices.GetUninitializedObject(typeof(TrackingSpriteBatch));
#pragma warning restore SYSLIB0050

            ReflectionHelpers.SetPrivateField(stage, "_menuTexture", menuTexture.Object);
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", whitePixel);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            ReflectionHelpers.SetPrivateField(stage, "_selectSound", selectSound.Object);
            ReflectionHelpers.SetPrivateField(stage, "_gameStartSound", gameStartSound.Object);

            stage.Dispose();

            menuTexture.Verify(x => x.RemoveReference(), Times.Once);
            resourceManager.Verify(x => x.Dispose(), Times.Once);
            cursorSound.Verify(x => x.Dispose(), Times.Once);
            selectSound.Verify(x => x.Dispose(), Times.Once);
            gameStartSound.Verify(x => x.Dispose(), Times.Once);
            Assert.True(whitePixel.WasDisposed);
            Assert.True(spriteBatch.WasDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_menuTexture"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.Null(ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IResourceManager>(stage, "_resourceManager"));
            Assert.Null(ReflectionHelpers.GetPrivateField<ISound>(stage, "_cursorMoveSound"));
            Assert.Null(ReflectionHelpers.GetPrivateField<ISound>(stage, "_selectSound"));
            Assert.Null(ReflectionHelpers.GetPrivateField<ISound>(stage, "_gameStartSound"));
        }

        private static TitleStage CreateStage(BaseGame? game = null)
        {
            return new TitleStage(game ?? ReflectionHelpers.CreateGame());
        }

        private static Mock<ISound> CreateSoundReturningInstance()
        {
            var sound = new Mock<ISound>();
            return sound;
        }

        private sealed class TestInputManager : IInputManager
        {
            private readonly HashSet<int> _pressedKeys = new();
            private readonly HashSet<InputCommandType> _pressedCommands = new();

            public bool HasPendingCommands => false;

            public void Dispose()
            {
            }

            public InputCommand? GetNextCommand() => null;

            public bool IsBackActionTriggered() => false;

            public bool IsCommandPressed(InputCommandType commandType) => _pressedCommands.Contains(commandType);

            public bool IsKeyDown(int keyCode) => false;

            public bool IsKeyPressed(int keyCode) => _pressedKeys.Contains(keyCode);

            public bool IsKeyReleased(int keyCode) => false;

            public bool IsKeyTriggered(int keyCode) => IsKeyPressed(keyCode);

            public void SetPressedCommand(InputCommandType commandType)
            {
                _pressedCommands.Add(commandType);
            }

            public void SetPressedKey(Keys key)
            {
                _pressedKeys.Add((int)key);
            }

            public void Update(double deltaTime)
            {
            }
        }

        private sealed class TrackingSpriteBatch : SpriteBatch
        {
            public TrackingSpriteBatch() : base(null!)
            {
            }

            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
            }
        }

        private sealed class TrackingTexture2D : Texture2D
        {
            public TrackingTexture2D() : base(null!, 1, 1)
            {
            }

            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
            }
        }
    }
}
