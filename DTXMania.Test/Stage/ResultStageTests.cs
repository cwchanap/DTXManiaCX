using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using static DTXMania.Test.TestData.ReflectionHelpers;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for ResultStage focusing on pure logic methods
    /// that do not require graphics initialization.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ResultStageTests
    {
        private const string PerformanceSummaryKey = "performanceSummary";
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ResultStage(null));
        }

        #endregion

        #region Type Property Tests

        [Fact]
        public void Type_Property_ShouldExistAndReturnStageType()
        {
            var property = typeof(ResultStage).GetProperty(
                "Type",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            Assert.NotNull(property);
            Assert.Equal(typeof(StageType), property!.PropertyType);
        }

        [Fact]
        public void Type_Value_ShouldBeResult()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            Assert.Equal(StageType.Result, stage.Type);
        }

        #endregion

        #region Telemetry Tests

        [Fact]
        public void PopulateTelemetry_WhenPerformanceSummaryExists_ShouldExposeResultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var selectedSong = new SongListNode { Title = "E2E AutoPlay Smoke" };
            var summary = new PerformanceSummary
            {
                Score = 1000000,
                MaxCombo = 4,
                ClearFlag = true,
                PerfectCount = 4,
                TotalNotes = 4,
                FinalLife = 100f,
                CompletionReason = CompletionReason.SongComplete
            };

            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_selectedDifficulty", 0);
            SetPrivateField(stage, "_performanceSummary", summary);

            var telemetry = new GameTelemetrySnapshot();

            stage.PopulateTelemetry(telemetry);

            Assert.Equal("E2E AutoPlay Smoke", telemetry.SelectedSongTitle);
            Assert.Equal(0, telemetry.SelectedDifficulty);
            Assert.Equal(1000000, telemetry.Score);
            Assert.Equal(4, telemetry.MaxCombo);
            Assert.Equal(4, telemetry.PerfectCount);
            Assert.Equal(4, telemetry.TotalNotes);
            Assert.True(telemetry.ClearFlag);
            Assert.True(telemetry.StageCompleted);
            Assert.Equal("SongComplete", telemetry.CompletionReason);
        }

        #endregion

        #region ExtractSharedData Tests

        [Fact]
        public void ExtractSharedData_WithNullSharedData_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_sharedData", null);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.Equal(0, summary.MaxCombo);
            Assert.False(summary.ClearFlag);
            Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_WithMissingPerformanceSummaryKey_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var sharedData = new Dictionary<string, object>
            {
                { "otherKey", "otherValue" }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.False(summary.ClearFlag);
        }

        [Fact]
        public void ExtractSharedData_WithValidPerformanceSummary_ShouldUseProvidedSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var expectedSummary = new PerformanceSummary
            {
                Score = 987654,
                MaxCombo = 250,
                ClearFlag = true,
                CompletionReason = CompletionReason.SongComplete
            };

            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, expectedSummary }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(987654, summary!.Score);
            Assert.Equal(250, summary.MaxCombo);
            Assert.True(summary.ClearFlag);
            Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_WithWrongTypeForSummaryKey_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            // Put wrong type under the performanceSummary key
            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, "not a PerformanceSummary" }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_DefaultSummary_ShouldHaveZeroJudgementCounts()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_sharedData", null);
            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.PerfectCount);
            Assert.Equal(0, summary.GreatCount);
            Assert.Equal(0, summary.GoodCount);
            Assert.Equal(0, summary.PoorCount);
            Assert.Equal(0, summary.MissCount);
        }

        [Fact]
        public void ExtractSharedData_ValidSummary_PreservesJudgementCounts()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var expectedSummary = new PerformanceSummary
            {
                Score = 500000,
                PerfectCount = 100,
                GreatCount = 50,
                GoodCount = 20,
                PoorCount = 5,
                MissCount = 10,
                MaxCombo = 80,
                ClearFlag = false
            };

            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, expectedSummary }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(100, summary!.PerfectCount);
            Assert.Equal(50, summary.GreatCount);
            Assert.Equal(20, summary.GoodCount);
            Assert.Equal(5, summary.PoorCount);
            Assert.Equal(10, summary.MissCount);
        }

        [Fact]
        public void ExtractSharedData_WhenSongKeysAreMissing_ShouldClearPreviousSelection()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var previousSong = new SongListNode { Title = "Previous" };

            SetPrivateField(stage, "_selectedSong", previousSong);
            SetPrivateField(stage, "_selectedDifficulty", 3);
            SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
            {
                { PerformanceSummaryKey, new PerformanceSummary { Score = 1000 } }
            });

            InvokePrivateMethod(stage, "ExtractSharedData");

            Assert.Null(GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Equal(0, GetPrivateField<int>(stage, "_selectedDifficulty"));
        }

        #endregion

        #region Inheritance and Interface Tests

        [Fact]
        public void ResultStage_ShouldInheritFromBaseStage()
        {
            Assert.True(typeof(BaseStage).IsAssignableFrom(typeof(ResultStage)));
        }

        [Fact]
        public void ResultStage_ShouldImplementIStage()
        {
            Assert.True(typeof(IStage).IsAssignableFrom(typeof(ResultStage)));
        }

        [Fact]
        public void HandleInput_WhenInputManagerIsNull_ShouldReturnWithoutThrowing()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleInput"));

            Assert.Null(exception);
        }

        [Fact]
        public void ExecuteInputCommand_WhenTransitionIsDebounced_ShouldNotChangeStage()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Back, 0.0));

            stageManager.Verify(
                manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
            Assert.Equal(0.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenCommandIsNotNavigation_ShouldIgnoreIt()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveDown, 0.0));

            stageManager.Verify(
                manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
            Assert.Equal(0.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenActivateAndTransitionAllowed_ShouldReturnToSongSelect()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate, 0.0));

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
            Assert.Equal(2.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenActivateIsDebounced_ShouldNotChangeStage()
        {
            var stage = CreateUninitializedResultStageWithStageManager(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Activate, 0.0));

            Assert.False(GetStageManagerMock(stage).Invocations.Any());
            Assert.Equal(
                0.0,
                DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(
                    GetPrivateField<BaseGame>(stage, "_game")!,
                    "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenBackAndTransitionAllowed_ShouldReturnToSongSelect()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            VerifySongSelectTransition(stage);
        }

        [Theory]
        [InlineData(InputCommandType.Activate)]
        [InlineData(InputCommandType.Back)]
        public void ExecuteInputCommand_WhenRevealIncomplete_ShouldCompleteRevealWithoutNavigating(InputCommandType commandType)
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var reveal = new ResultRevealState();

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_revealState", reveal);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(commandType, 0.0));

            Assert.True(reveal.IsComplete);
            stageManager.Verify(
                manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
            Assert.Equal(0.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenRevealAlreadyComplete_ShouldNavigate()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Activate, 0.0));

            VerifySongSelectTransition(stage);
        }

        [Fact]
        public void HandleInput_WhenTwoNavigationCommandsQueuedAndRevealIncomplete_ShouldCompleteRevealWithoutNavigating()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            var inputManager = new TrackingInputManager();
            var reveal = new ResultRevealState();

            inputManager.Enqueue(new InputCommand(InputCommandType.Activate, 0.0));
            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_revealState", reveal);

            InvokePrivateMethod(stage, "HandleInput");

            Assert.True(reveal.IsComplete);
            Assert.False(GetStageManagerMock(stage).Invocations.Any());
        }

        [Fact]
        public void OnUpdate_WhenQueuedBackCommandExists_ShouldProcessInputAndReturnToSongSelect()
        {
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var inputManager = new TrackingInputManager();

            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));
            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_uiManager", new UIManager());
            SetPrivateField(stage, "_elapsedTime", 0.0);
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "OnUpdate", 0.25);

            Assert.True(inputManager.UpdateCalled);
            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
            Assert.Equal(2.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void OnUpdate_WhenUiManagerIsNull_ShouldStillProcessQueuedInput()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            var inputManager = new TrackingInputManager();
            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));

            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_uiManager", null);
            SetPrivateField(stage, "_elapsedTime", 0.0);
            CompleteReveal(stage);

            InvokePrivateMethod(stage, "OnUpdate", 0.25);

            Assert.True(inputManager.UpdateCalled);
            Assert.Equal(0.25, GetPrivateField<double>(stage, "_elapsedTime"));
            VerifySongSelectTransition(stage);
        }

        [Fact]
        public void OnUpdate_WhenInputManagerIsNull_ShouldStillAdvanceElapsedTime()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            SetPrivateField(stage, "_inputManager", null);
            SetPrivateField(stage, "_uiManager", new UIManager());
            SetPrivateField(stage, "_elapsedTime", 0.0);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnUpdate", 0.25));

            Assert.Null(exception);
            Assert.Equal(0.25, GetPrivateField<double>(stage, "_elapsedTime"));
            Assert.False(GetStageManagerMock(stage).Invocations.Any());
        }

        [Fact]
        public void OnUpdate_WhenRevealCompletesAndNewRecordSoundExists_ShouldPlayNewRecordSoundOnce()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var sound = new Mock<ISound>();
            var model = ResultScreenModel.Create(
                new PerformanceSummary { Score = 900000, GameSkill = 100.0 },
                null,
                0,
                null,
                new SongScore { PlayCount = 1, BestScore = 100, HighSkill = 1.0 });
            var reveal = new ResultRevealState();

            SetPrivateField(stage, "_inputManager", null);
            SetPrivateField(stage, "_uiManager", new UIManager());
            SetPrivateField(stage, "_elapsedTime", 0.0);
            SetPrivateField(stage, "_resultModel", model);
            SetPrivateField(stage, "_revealState", reveal);
            SetPrivateField(stage, "_newRecordSound", sound.Object);
            SetPrivateField(stage, "_newRecordSoundPlayed", false);

            InvokePrivateMethod(stage, "OnUpdate", ResultRevealState.TotalRevealSeconds);
            InvokePrivateMethod(stage, "OnUpdate", 0.1);

            sound.Verify(s => s.Play(), Times.Once);
        }

        [Fact]
        public void HandleInput_WhenQueueIsEmpty_ShouldNotChangeStage()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            var inputManager = new TrackingInputManager();
            SetPrivateField(stage, "_inputManager", inputManager);

            InvokePrivateMethod(stage, "HandleInput");

            Assert.False(GetStageManagerMock(stage).Invocations.Any());
        }

        [Fact]
        public void ReturnToSongSelect_ShouldUseFadeTransitionWithNullSharedData()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ReturnToSongSelect");

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
        }

        [Fact]
        public void OnActivate_WhenInputManagerIsNotNull_ShouldClearInputQueue()
        {
#pragma warning disable SYSLIB0050
            var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
#pragma warning restore SYSLIB0050
            var inputManager = new TrackingInputManager();
            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
            {
                ["performanceSummary"] = new PerformanceSummary { Score = 123456 }
            });

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnActivate"));

            Assert.Null(exception);
            Assert.True(stage.WhitePixelRequested);
            Assert.True(inputManager.ClearPendingCommandsCalled);
            Assert.True(inputManager.ResetKeyRepeatStatesCalled);
            Assert.Empty(inputManager.GetInputCommands());
        }

        [Fact]
        public void OnActivate_WhenSelectedSongHasNoValidChart_ShouldNotPersistScore()
        {
            SongManager.ResetInstanceForTesting();
            try
            {
#pragma warning disable SYSLIB0050
                var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
#pragma warning restore SYSLIB0050
                var summary = new PerformanceSummary { Score = 750000 };
                var song = new SongListNode
                {
                    DatabaseSong = new SongEntity { Charts = new List<SongChart>() }
                };
                SetPrivateField(stage, "_inputManager", null);
                SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
                {
                    ["performanceSummary"] = summary,
                    ["selectedSong"] = song,
                    ["selectedDifficulty"] = 0
                });

                var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnActivate"));

                Assert.Null(exception);
                Assert.Same(summary, GetPrivateField<PerformanceSummary>(stage, "_performanceSummary"));
                Assert.Same(song, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
            }
        }

        [Fact]
        public void InitializeComponents_WhenFontCreationThrows_ShouldLeaveResultFontNull()
        {
#pragma warning disable SYSLIB0050
            var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
#pragma warning restore SYSLIB0050
            SetPrivateField(stage, "_game", DTXMania.Test.TestData.ReflectionHelpers.CreateGame());
            stage.WhitePixelToReturn = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            stage.FontExceptionToThrow = new InvalidOperationException("font load failed");

            InvokePrivateMethod(stage, "InitializeComponents");

            Assert.Same(stage.WhitePixelToReturn, GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.Null(GetPrivateField<IFont>(stage, "_resultFont"));
        }

        [Fact]
        public void DrawBackground_WhenBackgroundIsNotReady_ShouldFillNXViewportUsingWhitePixel()
        {
#pragma warning disable SYSLIB0050
            var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
            SetPrivateField(stage, "_spriteBatch", (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch)));
            var whitePixel = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            SetPrivateField(stage, "_whitePixel", whitePixel);
#pragma warning restore SYSLIB0050
            SetPrivateField(stage, "_game", DTXMania.Test.TestData.ReflectionHelpers.CreateGame());
            stage.ViewportToReturn = new Viewport(0, 0, 640, 480);

            InvokePrivateMethod(stage, "DrawBackground");

            Assert.Same(whitePixel, stage.DrawTextureArgument);
            // Fallback uses NX virtual dimensions (1280x720), not real viewport dims,
            // because SpriteBatch has an active 1280x720→screen viewport transform.
            Assert.Equal(new Rectangle(0, 0, ResultUILayout.NXViewport.Width, ResultUILayout.NXViewport.Height), stage.DrawTextureRectangle);
            Assert.Equal(ResultUILayout.Background.BackgroundColor, stage.DrawTextureColor);
        }

        [Fact]
        public void CleanupComponents_ShouldDisposeTrackedResourcesAndClearFields()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
            var whitePixel = (TrackingTexture2D)FormatterServices.GetUninitializedObject(typeof(TrackingTexture2D));
#pragma warning restore SYSLIB0050
            var fontMock = new Mock<IFont>();
            var smallFontMock = new Mock<IFont>();
            var largeFontMock = new Mock<IFont>();
            var resultSoundMock = new Mock<ISound>();
            var newRecordSoundMock = new Mock<ISound>();
            var resourceManager = new Mock<IResourceManager>();
            var renderer = new ResultScreenRenderer(resourceManager.Object, null, null, null);

            SetPrivateField(stage, "_whitePixel", whitePixel);
            SetPrivateField(stage, "_resultFont", fontMock.Object);
            SetPrivateField(stage, "_smallResultFont", smallFontMock.Object);
            SetPrivateField(stage, "_largeResultFont", largeFontMock.Object);
            SetPrivateField(stage, "_resultSound", resultSoundMock.Object);
            SetPrivateField(stage, "_newRecordSound", newRecordSoundMock.Object);
            SetPrivateField(stage, "_resultRenderer", renderer);

            InvokePrivateMethod(stage, "CleanupComponents");

            Assert.True(whitePixel.WasDisposed);
            fontMock.Verify(f => f.RemoveReference(), Times.Once);
            smallFontMock.Verify(f => f.RemoveReference(), Times.Once);
            largeFontMock.Verify(f => f.RemoveReference(), Times.Once);
            resultSoundMock.Verify(s => s.RemoveReference(), Times.Once);
            newRecordSoundMock.Verify(s => s.RemoveReference(), Times.Once);
            Assert.Throws<ObjectDisposedException>(() => renderer.Load(ResultScreenModel.Create(null, null, 0, null, null)));
            Assert.Null(GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.Null(GetPrivateField<IFont>(stage, "_resultFont"));
            Assert.Null(GetPrivateField<IFont>(stage, "_smallResultFont"));
            Assert.Null(GetPrivateField<IFont>(stage, "_largeResultFont"));
            Assert.Null(GetPrivateField<ISound>(stage, "_resultSound"));
            Assert.Null(GetPrivateField<ISound>(stage, "_newRecordSound"));
            Assert.Null(GetPrivateField<ResultScreenRenderer>(stage, "_resultRenderer"));
        }

        [Fact]
        public void Dispose_WhenDisposing_ShouldReleaseSpriteBatchAndCleanupComponents()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
            var spriteBatch = (TrackingSpriteBatch)FormatterServices.GetUninitializedObject(typeof(TrackingSpriteBatch));
            var whitePixel = (TrackingTexture2D)FormatterServices.GetUninitializedObject(typeof(TrackingTexture2D));
#pragma warning restore SYSLIB0050
            var fontMock = new Mock<IFont>();
            var smallFontMock = new Mock<IFont>();
            var largeFontMock = new Mock<IFont>();
            var resultSoundMock = new Mock<ISound>();
            var newRecordSoundMock = new Mock<ISound>();

            SetPrivateField(stage, "_game", DTXMania.Test.TestData.ReflectionHelpers.CreateGame());
            SetPrivateField(stage, "_spriteBatch", spriteBatch);
            SetPrivateField(stage, "_uiManager", new UIManager());
            SetPrivateField(stage, "_whitePixel", whitePixel);
            SetPrivateField(stage, "_resultFont", fontMock.Object);
            SetPrivateField(stage, "_smallResultFont", smallFontMock.Object);
            SetPrivateField(stage, "_largeResultFont", largeFontMock.Object);
            SetPrivateField(stage, "_resultSound", resultSoundMock.Object);
            SetPrivateField(stage, "_newRecordSound", newRecordSoundMock.Object);
            SetPrivateField(stage, "_disposed", false);

            InvokeDispose(stage, true);

            Assert.True(spriteBatch.WasDisposed);
            Assert.True(whitePixel.WasDisposed);
            fontMock.Verify(f => f.RemoveReference(), Times.Once);
            smallFontMock.Verify(f => f.RemoveReference(), Times.Once);
            largeFontMock.Verify(f => f.RemoveReference(), Times.Once);
            resultSoundMock.Verify(s => s.RemoveReference(), Times.Once);
            newRecordSoundMock.Verify(s => s.RemoveReference(), Times.Once);
        }

        [Fact]
        public void LoadSoundForPlate_FailedPlate_ShouldLoadStageClearSound()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            resourceManager.Setup(r => r.ResourceExists("Sounds/Stage Clear.ogg")).Returns(true);
            resourceManager.Setup(r => r.LoadSound("Sounds/Stage Clear.ogg")).Returns(sound.Object);
            SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            var result = InvokePrivateMethod<ISound>(stage, "LoadSoundForPlate", ResultPlateKind.Failed);

            Assert.NotNull(result);
            resourceManager.Verify(r => r.LoadSound("Sounds/Stage Clear.ogg"), Times.Once);
        }

        [Theory]
        [InlineData(ResultPlateKind.Excellent, "Sounds/Excellent.ogg")]
        [InlineData(ResultPlateKind.FullCombo, "Sounds/Full Combo.ogg")]
        [InlineData(ResultPlateKind.StageCleared, "Sounds/Stage Clear.ogg")]
        [InlineData(ResultPlateKind.Failed, "Sounds/Stage Clear.ogg")]
        public void LoadSoundForPlate_ShouldMapPlateToCorrectSoundPath(ResultPlateKind plateKind, string expectedPath)
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var resourceManager = new Mock<IResourceManager>();
            var sound = new Mock<ISound>();
            resourceManager.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
            resourceManager.Setup(r => r.LoadSound(It.IsAny<string>())).Returns(sound.Object);
            SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            InvokePrivateMethod<ISound>(stage, "LoadSoundForPlate", plateKind);

            resourceManager.Verify(r => r.LoadSound(expectedPath), Times.Once);
        }

        #endregion

        #region Helper Methods

        private static void InvokeDispose(ResultStage stage, bool disposing)
        {
            var method = typeof(ResultStage).GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);
            method!.Invoke(stage, new object[] { disposing });
        }

        private static ResultStage CreateUninitializedResultStageWithStageManager(double totalGameTime = 2.0, double lastStageTransitionTime = 0.0)
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: totalGameTime, lastStageTransitionTime: lastStageTransitionTime);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_inputManager", null);
            SetPrivateField(stage, "_uiManager", new UIManager());

            return stage;
        }

        private static Mock<IStageManager> GetStageManagerMock(ResultStage stage)
        {
            return Mock.Get(stage.StageManager!);
        }

        private static void CompleteReveal(ResultStage stage)
        {
            var reveal = new ResultRevealState();
            reveal.Complete();
            SetPrivateField(stage, "_revealState", reveal);
        }

        private static void VerifySongSelectTransition(ResultStage stage, double expectedTransitionTime = 2.0)
        {
            GetStageManagerMock(stage).Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
            Assert.Equal(expectedTransitionTime, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(GetPrivateField<BaseGame>(stage, "_game")!, "_lastStageTransitionTime"));
        }

        private sealed class TrackingInputManager : InputManager
        {
            public bool UpdateCalled { get; private set; }
            public bool ClearPendingCommandsCalled { get; private set; }
            public bool ResetKeyRepeatStatesCalled { get; private set; }

            public void Enqueue(InputCommand command)
            {
                EnqueueCommand(command);
            }

            public override void Update(double deltaTime = 0)
            {
                UpdateCalled = true;
                base.Update(deltaTime);
            }

            public override void ClearPendingCommands()
            {
                ClearPendingCommandsCalled = true;
                base.ClearPendingCommands();
            }

            public override void ResetKeyRepeatStates()
            {
                ResetKeyRepeatStatesCalled = true;
                base.ResetKeyRepeatStates();
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

        private sealed class InspectableResultStage : ResultStage
        {
            public InspectableResultStage(BaseGame game)
                : base(game)
            {
            }

            public Viewport ViewportToReturn { get; set; }

            public Texture2D? WhitePixelToReturn { get; set; }

            public Exception? FontExceptionToThrow { get; set; }

            public Texture2D? DrawTextureArgument { get; private set; }

            public Rectangle? DrawTextureRectangle { get; private set; }

            public Color? DrawTextureColor { get; private set; }

            public bool WhitePixelRequested { get; private set; }

            public bool ResultFontRequested { get; private set; }

            internal override Texture2D CreateWhitePixel()
            {
                WhitePixelRequested = true;
                return WhitePixelToReturn!;
            }

            internal override IFont CreateResultFont()
            {
                ResultFontRequested = true;
                throw FontExceptionToThrow ?? new InvalidOperationException("No font exception configured.");
            }

            internal override IFont CreateSmallResultFont()
            {
                return null!;
            }

            internal override IFont CreateLargeResultFont()
            {
                return null!;
            }

            internal override ResultScreenRenderer CreateResultRenderer()
            {
                return null!;
            }

            internal override Viewport GetBackgroundViewport()
            {
                return ViewportToReturn;
            }

            internal override void DrawTexture(Texture2D texture, Rectangle destinationRectangle, Color color)
            {
                DrawTextureArgument = texture;
                DrawTextureRectangle = destinationRectangle;
                DrawTextureColor = color;
            }
        }

        #endregion
    }
}
