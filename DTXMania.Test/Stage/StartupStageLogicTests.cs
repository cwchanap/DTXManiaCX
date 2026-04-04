using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
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
    }
}
