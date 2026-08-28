using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Utilities;
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
using Microsoft.Extensions.Logging;
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
    // TempAppDataRoot mutates the process-wide DTXMANIA_APPDATA_ROOT env var and
    // deletes directories on dispose, so this class must not run in parallel with
    // other AppPaths-touching tests (ConfigManagerTests, AppPathsTests, etc.).
    [Collection("AppPaths")]
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
                CompletionReason = CompletionReason.SongComplete,
                PlaySpeedPercent = 75,
                PitchSemitones = -4
            };

            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_selectedDifficulty", 0);
            SetPrivateField(stage, "_performanceSummary", summary);
            SetPrivateField(stage, "_scoreSaveState", ResultSaveState.Failed);
            SetPrivateField(stage, "_scoreSaveError", "database busy");

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
            Assert.Equal(75, telemetry.PlaySpeedPercent);
            Assert.Equal(-4, telemetry.PitchSemitones);
            Assert.True(telemetry.PlaybackProfileFrozen);
            Assert.Equal("Failed", telemetry.ScoreSaveStatus);
            Assert.Equal("database busy", telemetry.ScoreSaveError);
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

        #region StartPerformanceSummarySave Tests

        [Fact]
        public void StartPerformanceSummarySave_WithOutstandingTaskFromTimeout_ShouldNotStartSecondSave()
        {
            // After a timeout, ObservePerformanceSummarySave retains the still-running
            // _scoreSaveTask and sets state to Failed so a late completion can reconcile
            // the UI. Pressing Activate must not replace that task with a second save,
            // which would orphan the previous write and allow two concurrent database
            // operations for the same RunId.
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var tcs = new TaskCompletionSource<ScoreSaveResult>();
            var outstandingTask = tcs.Task;
            SetPrivateField(stage, "_scoreSaveTask", outstandingTask);
            SetPrivateField(stage, "_scoreSaveState", ResultSaveState.Failed);
            SetPrivateField(stage, "_scoreSaveTimedOut", true);
            SetPrivateField(stage, "_scoreSaveStopwatch", null);

            var chart = new SongChart { Id = 42 };
            SetPrivateField(stage, "_scoreSaveChart", chart);
            SetPrivateField(stage, "_performanceSummary", new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                CompletionReason = CompletionReason.SongComplete
            });

            InvokePrivateMethod(stage, "StartPerformanceSummarySave", chart);

            // The outstanding task must be retained (not replaced by a new save).
            Assert.Same(outstandingTask, GetPrivateField<Task<ScoreSaveResult>>(stage, "_scoreSaveTask"));
        }

        [Fact]
        public void StartPerformanceSummarySave_WithAssistedSummary_ShouldNotInvokePersistence()
        {
            // An assisted run (any automated lane) must never reach score
            // persistence: IsSavable rejects UsedAutoPlay summaries, so the
            // existing guard returns before SavePerformanceSummaryAsync runs.
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var assisted = new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                CompletionReason = CompletionReason.SongComplete,
                UsedAutoPlay = true,
                Score = 500_000
            };
            SetPrivateField(stage, "_performanceSummary", assisted);

            InvokePrivateMethod(
                stage,
                "StartPerformanceSummarySave",
                new SongChart { Id = 42 });

            Assert.Equal(ResultSaveState.NotStarted, stage.ScoreSaveState);
            Assert.Null(GetPrivateField<System.Threading.Tasks.Task<ScoreSaveResult>>(stage, "_scoreSaveTask"));
        }

        [Fact]
        public async Task StartPerformanceSummarySave_WithCompletedPriorTask_ShouldStartNewSave()
        {
            // Once the previous task has completed (late reconciliation resolved),
            // a retry must be allowed to proceed.
            //
            // Isolate from the shared SongManager singleton: StartPerformanceSummarySave
            // falls through to SongManager.Instance.UpdateScoreAsync when no
            // SavePerformanceSummaryAsync override is wired (uninitialized object).
            // Without a reset, the new save task would hit the real singleton and
            // any unobserved exception would leak across tests. A fresh instance has
            // no _databaseService, so UpdateScoreAsync returns a Failed result
            // synchronously — but we still await the replacement task so a late
            // fault cannot remain unobserved.
            SongManager.ResetInstanceForTesting();
            try
            {
#pragma warning disable SYSLIB0050
                var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

                var completedTask = Task.FromResult(ScoreSaveResult.Failed("previous"));
                SetPrivateField(stage, "_scoreSaveTask", completedTask);
                SetPrivateField(stage, "_scoreSaveState", ResultSaveState.Failed);
                SetPrivateField(stage, "_scoreSaveTimedOut", false);
                SetPrivateField(stage, "_scoreSaveStopwatch", null);

                var chart = new SongChart { Id = 42 };
                SetPrivateField(stage, "_scoreSaveChart", chart);
                SetPrivateField(stage, "_performanceSummary", new PerformanceSummary
                {
                    RunId = Guid.NewGuid(),
                    CompletionReason = CompletionReason.SongComplete
                });

                InvokePrivateMethod(stage, "StartPerformanceSummarySave", chart);

                // The prior completed task was replaced (the method proceeded past the
                // guard and started a new save).
                var replacementTask = GetPrivateField<Task<ScoreSaveResult>>(stage, "_scoreSaveTask");
                Assert.NotNull(replacementTask);
                Assert.NotSame(completedTask, replacementTask);

                // Observe the replacement task so a late fault cannot leak. With the
                // isolated singleton (no database service), this resolves to Failed.
                var replacementResult = await replacementTask!;
                Assert.NotNull(replacementResult);
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
            }
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
                new PerformanceSummary
                {
                    RunId = Guid.NewGuid(),
                    CompletionReason = CompletionReason.SongComplete,
                    Score = 900000,
                    GameSkill = 100.0
                },
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

        #region Automatic Result Screenshot Tests (HPA-16)

        [Fact]
        public void TryScheduleResultScreenshot_WhenRevealIncomplete_ShouldNotCallCaptureQueue()
        {
            var stage = CreateScreenshotSchedulingStage(out var gameMock, completeReveal: false);

            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Never);
            Assert.Equal(0, stage.AcceptedPersistenceCalls);
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenRevealCompleteAndRequestAccepted_ShouldStartExactlyOnePersistence()
        {
            var pendingCapture = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stage = CreateScreenshotSchedulingStage(out var gameMock, captureResult: pendingCapture.Task);

            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Once);
            Assert.Equal(1, stage.AcceptedPersistenceCalls);
            Assert.Same(pendingCapture.Task, stage.LastCaptureTask);
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenInvokedAgainAfterAcceptance_ShouldNotCallQueueOrPersistenceAgain()
        {
            var stage = CreateScreenshotSchedulingStage(out var gameMock);

            stage.TryScheduleResultScreenshot();
            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Once);
            Assert.Equal(1, stage.AcceptedPersistenceCalls);
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenBusySlotRejectsFirstDraw_ShouldAcceptOnLaterDraw()
        {
            var stage = CreateScreenshotSchedulingStage(out var gameMock, captureResult: Task.FromResult<byte[]?>(null));

            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Once);
            Assert.Equal(0, stage.AcceptedPersistenceCalls);

            var pendingCapture = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            gameMock.Setup(g => g.CaptureScreenshotAsync()).Returns(pendingCapture.Task);
            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Exactly(2));
            Assert.Equal(1, stage.AcceptedPersistenceCalls);
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenAcceptedPersistenceFails_ShouldLogWarningAndNotRetry()
        {
            var loggerFactory = new RecordingLoggerFactory();
            var stage = CreateScreenshotSchedulingStage(out var gameMock, loggerFactory: loggerFactory);
            stage.PersistenceExceptionToThrow = new IOException("disk full");

            stage.TryScheduleResultScreenshot();
            stage.TryScheduleResultScreenshot();

            Assert.Equal(1, stage.AcceptedPersistenceCalls);
            Assert.Equal(1, loggerFactory.WarningCount);
            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Once);
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenCaptureQueueThrows_ShouldConsumeOneShotAndLogWarning()
        {
            var loggerFactory = new RecordingLoggerFactory();
            var stage = CreateScreenshotSchedulingStage(out var gameMock, loggerFactory: loggerFactory);
            gameMock.Setup(g => g.CaptureScreenshotAsync())
                .Throws(new InvalidOperationException("capture queue unavailable"));

            stage.TryScheduleResultScreenshot();
            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Once);
            Assert.Equal(1, loggerFactory.WarningCount);
            Assert.Equal(0, stage.AcceptedPersistenceCalls);
        }

        [Fact]
        public void CaptureAndSaveResultScreenshotAsync_WhenCaptureHasBytes_ShouldWritePngUnderScreenshotsRoot()
        {
            using var appDataRoot = new TempAppDataRoot();
            var stage = CreateScreenshotSchedulingStage(out _, useRealPersistence: true);
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };

            stage.CaptureAndSaveResultScreenshotAsync(Task.FromResult<byte[]?>(pngBytes)).GetAwaiter().GetResult();

            var written = Directory.GetFiles(AppPaths.GetScreenshotsRoot(), "result-*.png");
            var file = Assert.Single(written);
            Assert.Equal(pngBytes, File.ReadAllBytes(file));
        }

        [Fact]
        public void CaptureAndSaveResultScreenshotAsync_WhenCaptureReturnsNoBytes_ShouldLogWarningAndWriteNothing()
        {
            using var appDataRoot = new TempAppDataRoot();
            var loggerFactory = new RecordingLoggerFactory();
            var stage = CreateScreenshotSchedulingStage(
                out _, useRealPersistence: true, loggerFactory: loggerFactory);

            // Both branch sides of the emptiness check (null and zero-length).
            stage.CaptureAndSaveResultScreenshotAsync(Task.FromResult<byte[]?>(null)).GetAwaiter().GetResult();
            stage.CaptureAndSaveResultScreenshotAsync(Task.FromResult<byte[]?>(Array.Empty<byte>())).GetAwaiter().GetResult();

            Assert.Equal(2, loggerFactory.WarningCount);
            Assert.False(Directory.Exists(AppPaths.GetScreenshotsRoot()));
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenRealPersistenceFails_ShouldLogWarningAndNotRetry()
        {
            using var appDataRoot = new TempAppDataRoot();
            var loggerFactory = new RecordingLoggerFactory();
            var pngBytes = new byte[] { 0x89, 0x50 };
            // Blocking the Screenshots root with a file makes EnsureDirectory throw,
            // exercising the real persistence failure path through the safe wrapper.
            var blockerPath = AppPaths.GetScreenshotsRoot();
            Directory.CreateDirectory(appDataRoot.Root);
            File.WriteAllText(blockerPath, string.Empty);
            var stage = CreateScreenshotSchedulingStage(
                out var gameMock,
                captureResult: Task.FromResult<byte[]?>(pngBytes),
                useRealPersistence: true,
                loggerFactory: loggerFactory);

            stage.TryScheduleResultScreenshot();
            stage.TryScheduleResultScreenshot();

            Assert.Equal(1, stage.AcceptedPersistenceCalls);
            Assert.Equal(1, loggerFactory.WarningCount);
            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Once);
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenRevealStateMissing_ShouldNotCallCaptureQueue()
        {
            var stage = CreateScreenshotSchedulingStage(out var gameMock);
            SetPrivateField(stage, "_revealState", null);

            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Never);
            Assert.Equal(0, stage.AcceptedPersistenceCalls);
        }

        [Fact]
        public void TryScheduleResultScreenshot_WhenGameIsMissing_ShouldConsumeOneShotWithoutThrowing()
        {
            var stage = CreateScreenshotSchedulingStage(out var gameMock);
            SetPrivateField(stage, "_game", null);

            stage.TryScheduleResultScreenshot();
            stage.TryScheduleResultScreenshot();

            gameMock.Verify(g => g.CaptureScreenshotAsync(), Times.Never);
            Assert.Equal(0, stage.AcceptedPersistenceCalls);
            // One-shot consumed despite the null game (warning went to the NullLogger fallback).
            Assert.True(GetPrivateField<bool>(stage, "_resultScreenshotRequested"));
        }

        [Fact]
        public void CaptureAndSaveResultScreenshotAsync_WhenLoggerFactoryMissing_ShouldFallBackToNullLogger()
        {
            using var appDataRoot = new TempAppDataRoot();
            var stage = CreateScreenshotSchedulingStage(out _, useRealPersistence: true);

            var exception = Record.Exception(() =>
                stage.CaptureAndSaveResultScreenshotAsync(Task.FromResult<byte[]?>(null)).GetAwaiter().GetResult());

            // Warning path ran against the NullLogger fallback without throwing or writing.
            Assert.Null(exception);
            Assert.False(Directory.Exists(AppPaths.GetScreenshotsRoot()));
        }

        [Fact]
        public void OnActivate_AfterPriorAcceptance_ShouldResetOneShotForNewActivation()
        {
            var stage = CreateScreenshotSchedulingStage(out _);
            SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
            {
                [PerformanceSummaryKey] = new PerformanceSummary { Score = 123456 }
            });
            stage.TryScheduleResultScreenshot();
            Assert.Equal(1, stage.AcceptedPersistenceCalls);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnActivate"));

            Assert.Null(exception);

            // OnActivate installs a fresh (incomplete) reveal; completing it must schedule
            // a new accepted attempt, proving the one-shot flag was reset.
            GetPrivateField<ResultRevealState>(stage, "_revealState")!.Complete();
            stage.TryScheduleResultScreenshot();

            Assert.Equal(2, stage.AcceptedPersistenceCalls);
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

        /// <summary>
        /// Uninitialized <see cref="InspectableResultStage"/> wired to a controllable
        /// <see cref="IStageGame"/> fake whose <c>CaptureScreenshotAsync</c> returns
        /// <paramref name="captureResult"/>, with a reveal state whose completion is optional.
        /// </summary>
        private static InspectableResultStage CreateScreenshotSchedulingStage(
            out Mock<IStageGame> gameMock,
            bool completeReveal = true,
            Task<byte[]?>? captureResult = null,
            RecordingLoggerFactory? loggerFactory = null,
            bool useRealPersistence = false)
        {
#pragma warning disable SYSLIB0050
            var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
#pragma warning restore SYSLIB0050

            gameMock = new Mock<IStageGame>();
            gameMock.Setup(g => g.CaptureScreenshotAsync())
                .Returns(captureResult ?? new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
            if (loggerFactory != null)
                gameMock.Setup(g => g.LoggerFactory).Returns(loggerFactory);
            SetPrivateField(stage, "_game", gameMock.Object);
            stage.UseRealPersistence = useRealPersistence;

            var reveal = new ResultRevealState();
            if (completeReveal)
                reveal.Complete();
            SetPrivateField(stage, "_revealState", reveal);

            return stage;
        }

        /// <summary>
        /// Counts warning-level log records so failure-observation tests can pin that a
        /// warning was emitted without pinning the exact log text.
        /// </summary>
        /// <summary>
        /// Redirects <c>DTXMANIA_APPDATA_ROOT</c> to a unique temp directory so tests can
        /// exercise real AppPaths-based persistence without touching user app data.
        /// </summary>
        private sealed class TempAppDataRoot : IDisposable
        {
            private readonly string? _previousOverride;

            public TempAppDataRoot()
            {
                _previousOverride = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
                Root = Path.Combine(Path.GetTempPath(), $"dtx-result-screenshot-{Guid.NewGuid():N}");
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", Root);
            }

            public string Root { get; }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _previousOverride);
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
        }

        private sealed class RecordingLoggerFactory : ILoggerFactory
        {
            public int WarningCount { get; private set; }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public ILogger CreateLogger(string categoryName) => new CountingLogger(this);

            public void Dispose()
            {
            }

            private sealed class CountingLogger : ILogger
            {
                private readonly RecordingLoggerFactory _owner;

                public CountingLogger(RecordingLoggerFactory owner)
                {
                    _owner = owner;
                }

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    if (logLevel == LogLevel.Warning)
                        _owner.WarningCount++;
                }
            }
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
            public InspectableResultStage(IStageGame game)
                : base(game)
            {
            }

            public Viewport ViewportToReturn { get; set; }

            public Texture2D? WhitePixelToReturn { get; set; }

            public Exception? FontExceptionToThrow { get; set; }

            public bool UseRealPersistence { get; set; }

            public Exception? PersistenceExceptionToThrow { get; set; }

            public int AcceptedPersistenceCalls { get; private set; }

            public Task<byte[]?>? LastCaptureTask { get; private set; }

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

            internal override async Task CaptureAndSaveResultScreenshotAsync(Task<byte[]?> captureTask)
            {
                AcceptedPersistenceCalls++;
                LastCaptureTask = captureTask;
                if (UseRealPersistence)
                {
                    await base.CaptureAndSaveResultScreenshotAsync(captureTask);
                    return;
                }
                if (PersistenceExceptionToThrow != null)
                    throw PersistenceExceptionToThrow;
            }
        }

        #endregion
    }
}
