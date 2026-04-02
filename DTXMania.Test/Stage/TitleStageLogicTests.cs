using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework.Input;
using Moq;

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
            SetPrivateField(stage, "_currentMenuIndex", 0);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            InvokePrivateMethod(stage, "MoveCursorUp");

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentMenuIndex"));
            Assert.True(GetPrivateField<bool>(stage, "_isMovingUp"));
            Assert.Equal(0d, GetPrivateField<double>(stage, "_menuMoveTimer"));
            cursorSound.Verify(x => x.Play(0.7f), Times.Once);
        }

        [Fact]
        public void MoveCursorDown_FromLastItem_ShouldWrapToFirstAndPlaySound()
        {
            var stage = CreateStage();
            var cursorSound = CreateSoundReturningInstance();
            SetPrivateField(stage, "_currentMenuIndex", 2);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            InvokePrivateMethod(stage, "MoveCursorDown");

            Assert.Equal(0, GetPrivateField<int>(stage, "_currentMenuIndex"));
            Assert.True(GetPrivateField<bool>(stage, "_isMovingDown"));
            Assert.Equal(0d, GetPrivateField<double>(stage, "_menuMoveTimer"));
            cursorSound.Verify(x => x.Play(0.7f), Times.Once);
        }

        [Fact]
        public void UpdateAnimations_ShouldResetFlashAndMovementFlagsWhenThresholdExceeded()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_cursorFlashTimer", 0.6d);
            SetPrivateField(stage, "_menuMoveTimer", 0.05d);
            SetPrivateField(stage, "_isMovingUp", true);
            SetPrivateField(stage, "_isMovingDown", true);

            InvokePrivateMethod(stage, "UpdateAnimations", 0.2d);

            Assert.Equal(0d, GetPrivateField<double>(stage, "_cursorFlashTimer"));
            Assert.Equal(0d, GetPrivateField<double>(stage, "_menuMoveTimer"));
            Assert.False(GetPrivateField<bool>(stage, "_isMovingUp"));
            Assert.False(GetPrivateField<bool>(stage, "_isMovingDown"));
        }

        [Fact]
        public void OnTransitionInStarted_WithStartupTransition_ShouldUseStartupFadePhase()
        {
            var stage = CreateStage();

            stage.OnTransitionIn(new StartupToTitleTransition());

            Assert.Equal("FadeInFromStartup", GetPrivateField<object>(stage, "_titlePhase")!.ToString());
        }

        [Fact]
        public void OnTransitionInStarted_WithNormalTransition_ShouldUseFadeInPhase()
        {
            var stage = CreateStage();

            stage.OnTransitionIn(new CrossfadeTransition());

            Assert.Equal("FadeIn", GetPrivateField<object>(stage, "_titlePhase")!.ToString());
        }

        [Fact]
        public void OnTransitionComplete_ShouldSetNormalPhase()
        {
            var stage = CreateStage();

            stage.OnTransitionComplete();

            Assert.Equal("Normal", GetPrivateField<object>(stage, "_titlePhase")!.ToString());
            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenGameStartAndTransitionAllowed_ShouldGoToSongSelect()
        {
            var game = CreateGame(totalGameTime: 1.0, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var gameStartSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentMenuIndex", 0);
            SetPrivateField(stage, "_gameStartSound", gameStartSound.Object);

            InvokePrivateMethod(stage, "SelectCurrentMenuItem");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition)),
                Times.Once);
            gameStartSound.Verify(x => x.Play(0.9f), Times.Once);
            Assert.Equal(1.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenConfigAndTransitionAllowed_ShouldGoToConfig()
        {
            var game = CreateGame(totalGameTime: 1.2, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var selectSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentMenuIndex", 1);
            SetPrivateField(stage, "_selectSound", selectSound.Object);

            InvokePrivateMethod(stage, "SelectCurrentMenuItem");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Config,
                    It.Is<IStageTransition>(transition => transition is CrossfadeTransition)),
                Times.Once);
            selectSound.Verify(x => x.Play(0.8f), Times.Once);
            Assert.Equal(1.2, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenExitAndTransitionAllowed_ShouldExitGame()
        {
            var game = CreateGame(totalGameTime: 1.4, lastStageTransitionTime: 0.0);
            var selectSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            SetPrivateField(stage, "_currentMenuIndex", 2);
            SetPrivateField(stage, "_selectSound", selectSound.Object);

            InvokePrivateMethod(stage, "SelectCurrentMenuItem");

            selectSound.Verify(x => x.Play(0.8f), Times.Once);
            Assert.Equal(1.4, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectCurrentMenuItem_WhenDebounceBlocksTransition_ShouldDoNothing()
        {
            var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var gameStartSound = CreateSoundReturningInstance();
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentMenuIndex", 0);
            SetPrivateField(stage, "_gameStartSound", gameStartSound.Object);

            InvokePrivateMethod(stage, "SelectCurrentMenuItem");

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

        private static TitleStage CreateStage(BaseGame? game = null)
        {
            return new TitleStage(game ?? CreateGame());
        }

        private static BaseGame CreateGame(double totalGameTime = 0.0, double lastStageTransitionTime = 0.0)
        {
#pragma warning disable SYSLIB0050
            var game = (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));
#pragma warning restore SYSLIB0050
            SetPrivateField(game, "_mainThreadActions", new System.Collections.Concurrent.ConcurrentQueue<Action>());
            SetPrivateField(game, "_pendingScreenshot", null);
            SetPrivateField(game, "_totalGameTime", totalGameTime);
            SetPrivateField(game, "_lastStageTransitionTime", lastStageTransitionTime);
            return game;
        }

        private static Mock<ISound> CreateSoundReturningInstance()
        {
            var sound = new Mock<ISound>();
            return sound;
        }

        private static T? GetPrivateField<T>(object target, string fieldName)
        {
            var field = GetField(target.GetType(), fieldName);
            Assert.NotNull(field);
            return (T?)field!.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = GetField(target.GetType(), fieldName);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        private static void InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(target, args);
        }

        private static FieldInfo? GetField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType!;
            }

            return null;
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
    }
}
