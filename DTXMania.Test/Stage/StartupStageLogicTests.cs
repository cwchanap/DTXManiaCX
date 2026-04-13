using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class StartupStageLogicTests
    {
        [Theory]
        [InlineData(StartupPhase.SystemSounds, StartupPhase.ConfigValidation)]
        [InlineData(StartupPhase.ConfigValidation, StartupPhase.SongListDB)]
        [InlineData(StartupPhase.SongListDB, StartupPhase.SongsDB)]
        [InlineData(StartupPhase.SongsDB, StartupPhase.LoadScoreCache)]
        [InlineData(StartupPhase.LoadScoreCache, StartupPhase.LoadScoreFiles)]
        [InlineData(StartupPhase.LoadScoreFiles, StartupPhase.EnumerateSongs)]
        [InlineData(StartupPhase.EnumerateSongs, StartupPhase.BuildSongLists)]
        [InlineData(StartupPhase.BuildSongLists, StartupPhase.SaveSongsDB)]
        [InlineData(StartupPhase.SaveSongsDB, StartupPhase.Complete)]
        [InlineData(StartupPhase.Complete, StartupPhase.Complete)]
        public void GetNextPhase_ShouldReturnExpectedSuccessor(StartupPhase currentPhase, StartupPhase expectedPhase)
        {
            var stage = CreateStage();

            var nextPhase = ReflectionHelpers.InvokePrivateMethod<StartupPhase>(stage, "GetNextPhase", currentPhase);

            Assert.Equal(expectedPhase, nextPhase);
        }

        [Theory]
        [InlineData(StartupPhase.SystemSounds, false)]
        [InlineData(StartupPhase.ConfigValidation, false)]
        [InlineData(StartupPhase.SongListDB, true)]
        [InlineData(StartupPhase.SongsDB, false)]
        [InlineData(StartupPhase.LoadScoreCache, true)]
        [InlineData(StartupPhase.LoadScoreFiles, true)]
        [InlineData(StartupPhase.EnumerateSongs, true)]
        [InlineData(StartupPhase.BuildSongLists, true)]
        [InlineData(StartupPhase.SaveSongsDB, true)]
        [InlineData(StartupPhase.Complete, false)]
        public void HasAsyncOperation_ShouldMatchPhaseRequirements(StartupPhase phase, bool expected)
        {
            var stage = CreateStage();

            var hasAsyncOperation = ReflectionHelpers.InvokePrivateMethod<bool>(stage, "HasAsyncOperation", phase);

            Assert.Equal(expected, hasAsyncOperation);
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAlreadyComplete_ShouldLeaveStateUnchanged()
        {
            var stage = CreateStage(phase: StartupPhase.Complete, elapsedTime: 1.0, phaseStartTime: 0.0, currentProgressMessage: "Setup done.");
            var progressMessages = ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages");
            progressMessages!.Add("already complete");

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.Complete, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Equal("Setup done.", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(progressMessages);
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskStillRunning_ShouldStayInCurrentPhase()
        {
            var pendingTask = new TaskCompletionSource<bool>();
            var stage = CreateStage(
                phase: StartupPhase.SongListDB,
                elapsedTime: 0.5,
                phaseStartTime: 0.0,
                currentAsyncTask: pendingTask.Task);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.SongListDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("in progress", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Empty(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskCompletedSuccessfully_ShouldAdvanceAndRecordCompletion()
        {
            var stage = CreateStage(
                phase: StartupPhase.SongListDB,
                elapsedTime: 0.5,
                phaseStartTime: 0.0,
                currentAsyncTask: Task.CompletedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.SongsDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Complete", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Contains("Initializing song database", ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")![0]);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
            Assert.Equal(0.5, ReflectionHelpers.GetPrivateField<double>(stage, "_phaseStartTime"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskCompletedBeforeMinimumDuration_ShouldStayInCurrentPhase()
        {
            var completedTask = Task.CompletedTask;
            var stage = CreateStage(
                phase: StartupPhase.SongListDB,
                elapsedTime: 0.1,
                phaseStartTime: 0.0,
                currentAsyncTask: completedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.SongListDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Complete", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Empty(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Same(completedTask, ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskFaulted_ShouldAdvanceAfterMinimumDuration()
        {
            var faultedTask = Task.FromException(new InvalidOperationException("boom"));
            var stage = CreateStage(
                phase: StartupPhase.LoadScoreCache,
                elapsedTime: 0.7,
                phaseStartTime: 0.0,
                currentAsyncTask: faultedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.LoadScoreFiles, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Error", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskFaultedBeforeMinimumDuration_ShouldStayInCurrentPhase()
        {
            var faultedTask = Task.FromException(new InvalidOperationException("boom"));
            var stage = CreateStage(
                phase: StartupPhase.LoadScoreCache,
                elapsedTime: 0.2,
                phaseStartTime: 0.0,
                currentAsyncTask: faultedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.LoadScoreCache, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Error", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Empty(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Same(faultedTask, ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncPhaseHasNoTask_ShouldRemainInCurrentPhase()
        {
            var stage = CreateStage(
                phase: StartupPhase.EnumerateSongs,
                elapsedTime: 2.0,
                phaseStartTime: 0.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.EnumerateSongs, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Empty(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
        }

        [Fact]
        public void UpdateCurrentPhase_WhenNonAsyncPhaseDurationElapsed_ShouldAdvance()
        {
            var stage = CreateStage(
                phase: StartupPhase.SystemSounds,
                elapsedTime: 0.6,
                phaseStartTime: 0.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.ConfigValidation, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Contains("Loading system sounds", ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")![0]);
        }

        [Fact]
        public void PerformPhaseOperationSync_WhenElapsedPastThreshold_ShouldDoNothing()
        {
            var stage = CreateStage(configData: new ConfigData { DTXPath = "before" });
            ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "before" });

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.ConfigValidation, 0.2);

            Assert.Equal(new[] { "before" }, ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void PerformPhaseOperationSync_ConfigValidation_WithValidConfig_ShouldCaptureSongPath()
        {
            var stage = CreateStage(configData: new ConfigData
            {
                DTXPath = "/songs",
                ScreenWidth = 1280,
                ScreenHeight = 720
            });

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.ConfigValidation, 0.0);

            Assert.Equal(new[] { "/songs" }, ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths"));
        }

        [Fact]
        public void PerformPhaseOperationSync_ConfigValidation_WithNullConfig_ShouldLeaveSongPathsUnchanged()
        {
            var stage = CreateStage(configData: null);
            ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "existing" });

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.ConfigValidation, 0.0);

            Assert.Equal(new[] { "existing" }, ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths"));
        }

        [Fact]
        public void PerformPhaseOperationSync_SongsDb_ShouldNotCreateAsyncTask()
        {
            var stage = CreateStage();

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.SongsDB, 0.0);

            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Theory]
        [InlineData(StartupPhase.SongListDB)]
        [InlineData(StartupPhase.LoadScoreCache)]
        [InlineData(StartupPhase.LoadScoreFiles)]
        [InlineData(StartupPhase.EnumerateSongs)]
        [InlineData(StartupPhase.BuildSongLists)]
        [InlineData(StartupPhase.SaveSongsDB)]
        public void PerformPhaseOperationSync_WhenAsyncPhaseStarts_ShouldCreateTask(StartupPhase phase)
        {
            var stage = CreateStage();

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", phase, 0.0);

            Assert.NotNull(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void PerformPhaseOperationSync_WhenAsyncTaskAlreadyExists_ShouldNotReplaceIt()
        {
            var existingTask = Task.Delay(Timeout.Infinite, new CancellationToken(true));
            var stage = CreateStage(currentAsyncTask: existingTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.LoadScoreCache, 0.0);

            Assert.Same(existingTask, ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public async Task InitializeDatabaseServiceAsync_WhenSongManagerMissing_ShouldCompleteWithoutThrowing()
        {
            var stage = CreateStage();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "InitializeDatabaseServiceAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task LoadScoreCacheAsync_WhenSongManagerMissing_ShouldCompleteWithoutThrowing()
        {
            var stage = CreateStage();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadScoreCacheAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task CheckFilesystemChangesAsync_WhenSongManagerMissing_ShouldMarkEnumerationNeeded()
        {
            var stage = CreateStage();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "CheckFilesystemChangesAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.True(ReflectionHelpers.GetPrivateField<bool?>(stage, "_needsEnumeration"));
        }

        [Fact]
        public async Task EnumerateSongsAsync_WhenEnumerationNotNeeded_ShouldReturnEarly()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_needsEnumeration", false);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", "unchanged");

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "EnumerateSongsAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal("unchanged", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
        }

        [Fact]
        public async Task BuildSongListsAsync_WhenSongManagerMissing_ShouldCompleteWithoutThrowing()
        {
            var stage = CreateStage();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "BuildSongListsAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task SaveSongsDbAsync_WhenSongManagerMissing_ShouldCompleteWithoutThrowing()
        {
            var stage = CreateStage();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "SaveSongsDBAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task InitializeDatabaseServiceAsync_WhenSongOperationsSucceed_ShouldUseOverriddenDatabasePath()
        {
            var stage = CreateControlledStage();
            stage.DatabasePath = Path.Combine(Path.GetTempPath(), "StartupStageLogicTests", Guid.NewGuid().ToString("N"), "songs.db");

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "InitializeDatabaseServiceAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(1, stage.InitializeDatabaseCalls);
            Assert.Equal(stage.DatabasePath, stage.LastDatabasePath);
            Assert.Equal(Path.GetDirectoryName(stage.DatabasePath), stage.LastEnsuredDirectoryPath);
        }

        [Fact]
        public async Task LoadScoreCacheAsync_WhenSongOperationsSucceed_ShouldPassSongPathsToWrapper()
        {
            var songPaths = new[] { "SongsA", "SongsB" };
            var stage = CreateControlledStage(songPaths: songPaths);

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadScoreCacheAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(1, stage.LoadScoreCacheCalls);
            Assert.Equal(songPaths, stage.LastSongPaths);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CheckFilesystemChangesAsync_WhenSongOperationsSucceed_ShouldCacheEnumerationResult(bool needsEnumeration)
        {
            var songPaths = new[] { "SongsRoot" };
            var stage = CreateControlledStage(songPaths: songPaths);
            stage.NextNeedsEnumerationResult = needsEnumeration;

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "CheckFilesystemChangesAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(1, stage.NeedsEnumerationCalls);
            Assert.Equal(songPaths, stage.LastSongPaths);
            Assert.Equal(needsEnumeration, ReflectionHelpers.GetPrivateField<bool?>(stage, "_needsEnumeration"));
        }

        [Fact]
        public async Task EnumerateSongsAsync_WhenEnumerationNeeded_ShouldUseProgressReporterAndCancellationToken()
        {
            var songPaths = new[] { "SongsRoot" };
            var stage = CreateControlledStage(songPaths: songPaths);
            stage.ReportedEnumerationProgress = new EnumerationProgress
            {
                CurrentFile = Path.Combine("SongsRoot", "test-song.dtx"),
                ProcessedCount = 3,
                DiscoveredSongs = 2
            };
            stage.NextEnumerationCount = 7;
            ReflectionHelpers.SetPrivateField(stage, "_needsEnumeration", true);

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "EnumerateSongsAsync")!;
            await task;

            SpinWait.SpinUntil(
                () => (ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage") ?? string.Empty).Contains("test-song.dtx", StringComparison.Ordinal),
                TimeSpan.FromSeconds(1));

            var progressMessage = ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage");
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(1, stage.EnumerateSongsCalls);
            Assert.Equal(songPaths, stage.LastSongPaths);
            Assert.True(stage.LastEnumerationToken.CanBeCanceled);
            Assert.Contains("test-song.dtx", progressMessage);
            Assert.Contains("3 processed", progressMessage);
            Assert.Contains("2 songs", progressMessage);
        }

        [Fact]
        public async Task BuildSongListsAsync_WhenSongOperationsSucceed_ShouldBuildFromDatabaseUsingCurrentSongPaths()
        {
            var songPaths = new[] { "SongsRoot" };
            var stage = CreateControlledStage(songPaths: songPaths);
            stage.RootSongCount = 12;

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "BuildSongListsAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(1, stage.BuildSongListCalls);
            Assert.Equal(songPaths, stage.LastSongPaths);
        }

        [Fact]
        public async Task SaveSongsDbAsync_WhenSaveSucceeds_ShouldMarkSongManagerInitialized()
        {
            var stage = CreateControlledStage();
            stage.NextSaveResult = true;
            stage.RootSongCount = 5;

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "SaveSongsDBAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(1, stage.SaveSongsDatabaseCalls);
            Assert.True(stage.MarkSongManagerInitializedCalled);
        }

        [Fact]
        public void OnUpdate_WhenCompletePhaseDurationElapsed_ShouldRequestTitleStageTransition()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager.Object);
            var stage = CreateStage(
                phase: StartupPhase.Complete,
                elapsedTime: 0.0,
                phaseStartTime: 0.0,
                game: game);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.2);

            stageManager.Verify(manager => manager.ChangeStage(
                StageType.Title,
                It.Is<IStageTransition>(transition => transition is StartupToTitleTransition)),
                Times.Once);
        }

        [Fact]
        public void OnUpdate_WhenCompletePhaseDurationNotElapsed_ShouldNotRequestStageTransition()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager.Object);
            var stage = CreateStage(
                phase: StartupPhase.Complete,
                elapsedTime: 0.0,
                phaseStartTime: 0.0,
                game: game);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.05);

            stageManager.Verify(manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()), Times.Never);
        }

        [Fact]
        public void Dispose_WhenAsyncTaskFaults_ShouldSwallowExceptionAndReleaseTaskState()
        {
            var stage = CreateStage(currentAsyncTask: Task.FromException(new InvalidOperationException("boom")));

            var exception = Record.Exception(() => InvokeDispose(stage, true));

            Assert.Null(exception);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
            Assert.Null(ReflectionHelpers.GetPrivateField<CancellationTokenSource>(stage, "_cancellationTokenSource"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_disposed"));
        }

        [Fact]
        public void OnActivate_ShouldInitializeGraphicsStateAndResetProgress()
        {
            var game = ReflectionHelpers.CreateGame();
            var resourceManager = new Mock<IResourceManager>();
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);
            var stage = new GraphicsControlledStartupStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_progressMessages", new List<string> { "stale" });
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.BuildSongLists);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 12.0);
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", 4.0);
            ReflectionHelpers.SetPrivateField(stage, "_currentAsyncTask", Task.CompletedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

            Assert.Same(stage.SpriteBatchStub, ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
            Assert.Same(stage.WhitePixelStub, ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.Same(resourceManager.Object, ReflectionHelpers.GetPrivateField<IResourceManager>(stage, "_resourceManager"));
            Assert.Null(ReflectionHelpers.GetPrivateField<BitmapFont>(stage, "_bitmapFont"));
            Assert.Equal(StartupPhase.SystemSounds, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_elapsedTime"));
            Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_phaseStartTime"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
            Assert.Equal(new[] { "DTXMania powered by YAMAHA Silent Session Drums" }, ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages"));
        }

        [Fact]
        public void OnDraw_WhenSpriteBatchMissing_ShouldReturnWithoutRendering()
        {
            var stage = new GraphicsControlledStartupStage(ReflectionHelpers.CreateGame());

            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "OnDraw", 0.016));

            Assert.Null(exception);
            Assert.Empty(stage.DrawCalls);
            Assert.Equal(0, stage.BeginCalls);
            Assert.Equal(0, stage.EndCalls);
        }

        [Fact]
        public void OnDraw_WhenUsingFallbackRendering_ShouldDrawBackgroundMessagesAndProgress()
        {
            var stage = new GraphicsControlledStartupStage(ReflectionHelpers.CreateGame());
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", stage.SpriteBatchStub);
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", stage.WhitePixelStub);
            ReflectionHelpers.SetPrivateField(stage, "_bitmapFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_progressMessages", new List<string> { "message one", "message two" });
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", "current");
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.LoadScoreFiles);
            ReflectionHelpers.SetPrivateField(stage, "_phaseInfo", CreatePhaseInfo());
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 0.35);
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", 0.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDraw", 0.016);

            Assert.Equal(1, stage.BeginCalls);
            Assert.Equal(1, stage.EndCalls);
            Assert.Contains(stage.DrawCalls, call => call.Color == new Color(16, 16, 32));
            Assert.Contains(stage.DrawCalls, call => call.Color == Color.White);
            Assert.Contains(stage.DrawCalls, call => call.Color == Color.Yellow);
            Assert.Contains(stage.DrawCalls, call => call.Color == Color.DarkGray);
            Assert.Contains(stage.DrawCalls, call => call.Color == Color.LightGreen);
            Assert.True(stage.DrawCalls.Count >= 6);
        }

        private static StartupStage CreateStage(
            StartupPhase phase = StartupPhase.SystemSounds,
            double elapsedTime = 0.0,
            double phaseStartTime = 0.0,
            Task? currentAsyncTask = null,
            string currentProgressMessage = "",
            ConfigData? configData = null,
            BaseGame? game = null)
        {
#pragma warning disable SYSLIB0050
            var stage = (StartupStage)FormatterServices.GetUninitializedObject(typeof(StartupStage));
#pragma warning restore SYSLIB0050

            ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            ReflectionHelpers.SetPrivateField(stage, "_disposed", false);
            ReflectionHelpers.SetPrivateField(stage, "_isFirstUpdate", false);
            ReflectionHelpers.SetPrivateField(stage, "_sharedData", new Dictionary<string, object>());
            ReflectionHelpers.SetPrivateField(stage, "_progressMessages", new List<string>());
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", currentProgressMessage);
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", phase);
            ReflectionHelpers.SetPrivateField(stage, "_songManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_configManager", CreateConfigManager(configData));
            ReflectionHelpers.SetPrivateField(stage, "_currentAsyncTask", currentAsyncTask);
            ReflectionHelpers.SetPrivateField(stage, "_cancellationTokenSource", new CancellationTokenSource());
            ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "initial" });
            ReflectionHelpers.SetPrivateField(stage, "_needsEnumeration", null);
            ReflectionHelpers.SetPrivateField(stage, "_phaseInfo", CreatePhaseInfo());
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", phaseStartTime);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", elapsedTime);

            return stage;
        }

        private static ControlledStartupStage CreateControlledStage(string[]? songPaths = null, ConfigData? configData = null)
        {
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", CreateConfigManager(configData ?? new ConfigData
            {
                DTXPath = "Songs",
                ScreenWidth = 1280,
                ScreenHeight = 720
            }));

            var stage = new ControlledStartupStage(game);
            if (songPaths != null)
            {
                ReflectionHelpers.SetPrivateField(stage, "_songPaths", songPaths);
            }

            return stage;
        }

        private static IConfigManager CreateConfigManager(ConfigData? configData)
        {
            var configManager = new Mock<IConfigManager>();
            configManager.SetupGet(manager => manager.Config).Returns(configData!);
            return configManager.Object;
        }

        private static Dictionary<StartupPhase, (string message, double duration)> CreatePhaseInfo()
        {
            return new Dictionary<StartupPhase, (string message, double duration)>
            {
                { StartupPhase.SystemSounds, ("Loading system sounds...", 0.5) },
                { StartupPhase.ConfigValidation, ("Validating configuration...", 0.3) },
                { StartupPhase.SongListDB, ("Initializing song database...", 0.3) },
                { StartupPhase.SongsDB, ("Loading songs.db...", 0.4) },
                { StartupPhase.LoadScoreCache, ("Loading cached song data...", 0.6) },
                { StartupPhase.LoadScoreFiles, ("Checking for filesystem changes...", 0.7) },
                { StartupPhase.EnumerateSongs, ("Scanning for new/modified songs...", 1.5) },
                { StartupPhase.BuildSongLists, ("Building song lists...", 0.3) },
                { StartupPhase.SaveSongsDB, ("Saving song database...", 0.2) },
                { StartupPhase.Complete, ("Setup done.", 0.1) }
            };
        }

        private static void InvokeDispose(StartupStage stage, bool disposing)
        {
            var method = typeof(StartupStage).GetMethod(
                "Dispose",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);
            method!.Invoke(stage, new object[] { disposing });
        }

        private class ControlledStartupStage : StartupStage
        {
            public ControlledStartupStage(BaseGame game) : base(game)
            {
            }

            public string DatabasePath { get; set; } = Path.Combine(Path.GetTempPath(), "controlled-startup-stage", "songs.db");

            public string? LastDatabasePath { get; private set; }

            public string? LastEnsuredDirectoryPath { get; private set; }

            public string[]? LastSongPaths { get; private set; }

            public CancellationToken LastEnumerationToken { get; private set; }

            public int InitializeDatabaseCalls { get; private set; }

            public int LoadScoreCacheCalls { get; private set; }

            public int NeedsEnumerationCalls { get; private set; }

            public int EnumerateSongsCalls { get; private set; }

            public int BuildSongListCalls { get; private set; }

            public int SaveSongsDatabaseCalls { get; private set; }

            public bool NextNeedsEnumerationResult { get; set; } = true;

            public int NextEnumerationCount { get; set; }

            public int RootSongCount { get; set; }

            public bool NextSaveResult { get; set; }

            public bool MarkSongManagerInitializedCalled { get; private set; }

            public EnumerationProgress? ReportedEnumerationProgress { get; set; }

            protected override string GetSongsDatabasePath()
            {
                return DatabasePath;
            }

            protected override void EnsureDirectory(string path)
            {
                LastEnsuredDirectoryPath = path;
            }

            protected override Task<bool> InitializeDatabaseServiceCoreAsync(string databasePath)
            {
                InitializeDatabaseCalls++;
                LastDatabasePath = databasePath;
                return Task.FromResult(true);
            }

            protected override Task<bool> LoadScoreCacheCoreAsync(string[] songPaths)
            {
                LoadScoreCacheCalls++;
                LastSongPaths = songPaths;
                return Task.FromResult(true);
            }

            protected override Task<bool> NeedsEnumerationCoreAsync(string[] songPaths, bool forceEnumeration)
            {
                NeedsEnumerationCalls++;
                LastSongPaths = songPaths;
                return Task.FromResult(NextNeedsEnumerationResult);
            }

            protected override Task<int> EnumerateSongsOnlyCoreAsync(string[] songPaths, IProgress<EnumerationProgress> progressReporter, CancellationToken cancellationToken)
            {
                EnumerateSongsCalls++;
                LastSongPaths = songPaths;
                LastEnumerationToken = cancellationToken;
                if (ReportedEnumerationProgress != null)
                {
                    progressReporter.Report(ReportedEnumerationProgress);
                }

                return Task.FromResult(NextEnumerationCount);
            }

            protected override Task BuildSongListFromDatabaseCoreAsync(string[] songPaths)
            {
                BuildSongListCalls++;
                LastSongPaths = songPaths;
                return Task.CompletedTask;
            }

            protected override int GetRootSongCount()
            {
                return RootSongCount;
            }

            protected override Task<bool> SaveSongsDatabaseCoreAsync()
            {
                SaveSongsDatabaseCalls++;
                return Task.FromResult(NextSaveResult);
            }

            protected override void MarkSongManagerInitialized()
            {
                MarkSongManagerInitializedCalled = true;
            }
        }

        private sealed record DrawCall(Rectangle Destination, Color Color);

        private sealed class GraphicsControlledStartupStage : StartupStage
        {
            public GraphicsControlledStartupStage(BaseGame game) : base(game)
            {
            }

            public GraphicsDevice GraphicsDeviceStub { get; } = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

            public SpriteBatch SpriteBatchStub { get; } = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

            public Texture2D WhitePixelStub { get; } = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));

            public Viewport ViewportValue { get; set; } = new(0, 0, 1280, 720);

            public int BeginCalls { get; private set; }

            public int EndCalls { get; private set; }

            public List<DrawCall> DrawCalls { get; } = new();

            protected override GraphicsDevice GetGraphicsDeviceCore()
            {
                return GraphicsDeviceStub;
            }

            protected override Viewport GetViewportCore()
            {
                return ViewportValue;
            }

            protected override SpriteBatch CreateSpriteBatchCore(GraphicsDevice graphicsDevice)
            {
                return SpriteBatchStub;
            }

            protected override Texture2D CreateWhitePixelCore(GraphicsDevice graphicsDevice)
            {
                return WhitePixelStub;
            }

            protected override BitmapFont CreateBitmapFontCore(GraphicsDevice graphicsDevice, IResourceManager resourceManager, BitmapFont.BitmapFontConfig config)
            {
                return null!;
            }

            protected override void BeginSpriteBatchCore(SpriteBatch spriteBatch)
            {
                BeginCalls++;
            }

            protected override void EndSpriteBatchCore(SpriteBatch spriteBatch)
            {
                EndCalls++;
            }

            protected override void DrawSolidRectCore(SpriteBatch spriteBatch, Texture2D texture, Rectangle destination, Color color)
            {
                DrawCalls.Add(new DrawCall(destination, color));
            }
        }
    }
}
