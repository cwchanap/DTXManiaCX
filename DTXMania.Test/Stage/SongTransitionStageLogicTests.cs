using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongTransitionStageLogicTests
    {
        [Fact]
        public void GetCurrentDifficultyLevel_WhenChartHasDrumLevel_ShouldReturnDrumLevel()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 38, HasDrumChart = true }));
            SetPrivateField(stage, "_selectedDifficulty", 0);

            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(38f, result);
        }

        [Fact]
        public void GetCurrentDifficultyLevel_WhenDrumLevelMissing_ShouldFallbackToGuitarThenBass()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart
            {
                DrumLevel = 0,
                HasDrumChart = false,
                GuitarLevel = 55,
                HasGuitarChart = true,
                BassLevel = 25,
                HasBassChart = true,
            }));

            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(55f, result);
        }

        [Fact]
        public void UpdatePhase_WhenFadeInDurationElapsed_ShouldEnterNormalPhase()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_currentPhase", StagePhase.FadeIn);
            SetPrivateField(stage, "_elapsedTime", SongTransitionUILayout.Timing.FadeInDuration);

            InvokePrivateMethod(stage, "UpdatePhase");

            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
        }

        [Fact]
        public void UpdatePhase_WhenFadeOutDurationElapsed_ShouldPerformTransition()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentPhase", StagePhase.FadeOut);
            SetPrivateField(stage, "_elapsedTime", SongTransitionUILayout.Timing.FadeOutDuration);
            SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 65, HasDrumChart = true }));
            SetPrivateField(stage, "_selectedDifficulty", 2);
            SetPrivateField(stage, "_songId", 123);

            InvokePrivateMethod(stage, "UpdatePhase");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Performance,
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    It.Is<Dictionary<string, object>>(sharedData =>
                        (SongListNode)sharedData["selectedSong"] == GetPrivateField<SongListNode>(stage, "_selectedSong") &&
                        (int)sharedData["selectedDifficulty"] == 2 &&
                        (int)sharedData["songId"] == 123)),
                Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_WhenActivateAndTransitionAllowed_ShouldTransitionToPerformance()
        {
            var stageManager = new Mock<IStageManager>();
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 50, HasDrumChart = true }));

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Performance,
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Once);
            Assert.Equal(2.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenBackAndTransitionAllowed_ShouldReturnToSongSelect()
        {
            var stageManager = new Mock<IStageManager>();
            var game = CreateGame(totalGameTime: 1.5, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Back, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is InstantTransition)),
                Times.Once);
            Assert.Equal(1.5, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenDebounceBlocksTransition_ShouldDoNothing()
        {
            var stageManager = new Mock<IStageManager>();
            var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate, 0.0));
            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Back, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>()),
                Times.Never);
            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void PerformTransition_WhenParsedChartLoaded_ShouldIncludeParsedChartInSharedData()
        {
            var stageManager = new Mock<IStageManager>();
            var stage = CreateStage();
            var selectedSong = CreateSongNode(new SongChart { DrumLevel = 40, HasDrumChart = true });
            var parsedChart = new ParsedChart("test.dtx");

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_selectedDifficulty", 1);
            SetPrivateField(stage, "_songId", 77);
            SetPrivateField(stage, "_parsedChart", parsedChart);
            SetPrivateField(stage, "_chartLoaded", true);

            InvokePrivateMethod(stage, "PerformTransition");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Performance,
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    It.Is<Dictionary<string, object>>(sharedData =>
                        sharedData.ContainsKey("parsedChart") &&
                        ReferenceEquals(sharedData["parsedChart"], parsedChart))),
                Times.Once);
        }

        [Fact]
        public void TransitionToPerformance_WhenAlreadyInFadeOut_ShouldNotTransitionAgain()
        {
            var stageManager = new Mock<IStageManager>();
            var stage = CreateStage();
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentPhase", StagePhase.FadeOut);

            InvokePrivateMethod(stage, "TransitionToPerformance");

            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void Deactivate_ShouldDisposeResourcesAndClearReferences()
        {
            var stage = CreateStage();
            var uiManager = new Mock<DTXMania.Game.Lib.UI.UIManager>();
            var backgroundTexture = new Mock<ITexture>();
            var previewTexture = new Mock<ITexture>();
            var titleFont = new Mock<IFont>();
            var artistFont = new Mock<IFont>();
            var nowLoadingSound = new Mock<ISound>();
            var inputManager = new ProbeInputManager();

            SetPrivateField(stage, "_uiManager", uiManager.Object);
            SetPrivateField(stage, "_backgroundTexture", backgroundTexture.Object);
            SetPrivateField(stage, "_previewTexture", previewTexture.Object);
            SetPrivateField(stage, "_titleFont", titleFont.Object);
            SetPrivateField(stage, "_artistFont", artistFont.Object);
            SetPrivateField(stage, "_nowLoadingSound", nowLoadingSound.Object);
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_currentPhase", StagePhase.Normal);

            stage.Deactivate();

            backgroundTexture.Verify(x => x.RemoveReference(), Times.Once);
            previewTexture.Verify(x => x.RemoveReference(), Times.Once);
            titleFont.Verify(x => x.RemoveReference(), Times.Once);
            artistFont.Verify(x => x.RemoveReference(), Times.Once);
            nowLoadingSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.True(inputManager.WasDisposed);
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Null(GetPrivateField<object>(stage, "_uiManager"));
            Assert.Null(GetPrivateField<object>(stage, "_backgroundTexture"));
            Assert.Null(GetPrivateField<object>(stage, "_previewTexture"));
        }

        private static SongTransitionStage CreateStage(BaseGame? game = null)
        {
            game ??= CreateGame();
            return new SongTransitionStage(game);
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

        private static SongListNode CreateSongNode(SongChart chart)
        {
            var song = new SongEntity
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Charts = new List<SongChart> { chart },
            };
            chart.Song = song;
            return new SongListNode
            {
                DatabaseSong = song,
                DatabaseChart = chart,
                Title = song.Title,
            };
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

        private static object? InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!.Invoke(target, args);
        }

        private static T? InvokePrivateMethod<T>(object target, string methodName, params object[] args)
        {
            var result = InvokePrivateMethod(target, methodName, args);
            if (result is null)
            {
                return default;
            }

            return (T)result;
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

        private sealed class ProbeInputManager : DTXMania.Game.Lib.Input.InputManager
        {
            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
