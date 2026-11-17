using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Update;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using static DTXMania.Test.TestData.ReflectionHelpers;

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
        [InlineData(StartupPhase.LoadScoreCache, false)]
        [InlineData(StartupPhase.LoadScoreFiles, false)]
        [InlineData(StartupPhase.EnumerateSongs, true)]
        [InlineData(StartupPhase.BuildSongLists, false)]
        [InlineData(StartupPhase.SaveSongsDB, false)]
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
        public void UpdateCurrentPhase_WhenAsyncOperationPending_ShouldStayInCurrentPhase()
        {
            var stage = CreateControlledStage();
            stage.NextDatabaseTask = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.SongListDB);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.SongListDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("in progress", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Empty(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);

            // Draining without a posted terminal result must not advance the phase.
            scope.Drain();
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");
            Assert.Equal(StartupPhase.SongListDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenPostedSuccess_ShouldAdvanceAndRecordCompletion()
        {
            var stage = CreateControlledStage();
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.SongListDB);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 0.5);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");
            DrainUntilPostedTerminal(scope, stage);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.SongsDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Complete", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Contains("Initializing song database", ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")![0]);
            Assert.Equal(0.5, ReflectionHelpers.GetPrivateField<double>(stage, "_phaseStartTime"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenPostedError_ShouldAdvanceAndLogFailureOnce()
        {
            var stage = new ThrowingNeedsEnumerationStartupStage();
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");
            DrainUntilPostedTerminal(scope, stage);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.BuildSongLists, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Error", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Equal(1, stage.BuildHierarchyCalls);
        }

        [Fact]
        public async Task UpdateCurrentPhase_WhenEnumerationCanceledWhileScopeActive_ShouldPostTerminalAndContinue()
        {
            // Cancellation independent of stage retirement (e.g.
            // SongManager.CancelEnumeration) must still publish a terminal
            // outcome: _asyncPhaseSucceeded stays null otherwise and
            // UpdateCurrentPhase would never advance (startup hangs).
            var stage = CreateControlledStage();
            stage.PublishedHierarchy = new SongListNode { Title = "Original" };
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromCanceled<SongEnumerationResult>(new CancellationToken(canceled: true));
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");
            DrainUntilPostedTerminal(scope, stage);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.BuildSongLists, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Error", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.Equal("Original", stage.PublishedHierarchy!.Title);
        }

        [Fact]
        public void UpdateCurrentPhase_ShouldSetDefaultStatusOnEntryOnly_AndPreserveLatestProgress()
        {
            // HPA-799: the default status message is set on phase ENTRY, not on
            // every update, so Latest progress reports survive until Draw.
            var stage = CreateStage(
                phase: StartupPhase.SongListDB,
                elapsedTime: 0.5,
                phaseStartTime: 0.0);
            stage.InstallTestBackgroundScope();

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");
            // Phase entry sets the default, then the kick-off marks it in progress.
            Assert.Equal("Initializing song database... (in progress)", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));

            // A Latest progress report applied by the scope drain (update thread).
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage",
                "Initializing song database... (worker progress)");

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");
            Assert.Contains("worker progress", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Equal(StartupPhase.SongListDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenFirstUpdateArrivesAfterPhaseWindow_ShouldStillRunPhaseOperation()
        {
            // Regression: the per-phase kick-off used to be gated by
            // "phaseElapsed <= 0.1s". A single slow frame at a phase boundary
            // (background launch, load hitch) skipped the kick-off entirely, so
            // async phases waited forever on a task that was never started —
            // startup wedged at 0% CPU. The operation must run however late the
            // first update of the phase arrives.
            var config = CreateConfigWithSongRoots("LateKickoffSongs");
            var stage = CreateStage(
                phase: StartupPhase.ConfigValidation,
                elapsedTime: 5.0,   // far past the old 0.1s window
                phaseStartTime: 0.0,
                configData: config);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            var songPaths = ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths");
            Assert.Equal(new[] { "LateKickoffSongs" }, songPaths);
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
        public void UpdateCurrentPhase_WhenNonAsyncPhaseRuns_ShouldAdvanceImmediately()
        {
            var stage = CreateStage(
                phase: StartupPhase.SystemSounds,
                elapsedTime: 0.1,
                phaseStartTime: 0.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.ConfigValidation, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Equal("Loading system sounds...", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
        }

        [Fact]
        public void PerformPhaseOperationSync_WhenElapsedPastThreshold_ShouldStillRun()
        {
            var stage = CreateStage(configData: CreateConfigWithSongRoots("after"));
            ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "before" });

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.ConfigValidation, 0.2);

            Assert.Equal(new[] { "after" }, ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths"));
        }

        [Fact]
        public void PerformPhaseOperationSync_ConfigValidation_WithValidConfig_ShouldCaptureSongPath()
        {
            var config = CreateConfigWithSongRoots("/songs", "/more-songs");
            config.ScreenWidth = 1280;
            config.ScreenHeight = 720;
            var stage = CreateStage(configData: config);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.ConfigValidation, 0.0);
            config.SongRoots[0] = "/mutated-after-snapshot";

            Assert.Equal(
                new[] { "/songs", "/more-songs" },
                ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths"));
        }

        [Fact]
        public void PerformPhaseOperationSync_ConfigValidation_WithNullConfig_ShouldLeaveSongPathsUnchanged()
        {
            var stage = CreateStage(configData: null, allowNullConfig: true);
            ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "existing" });

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.ConfigValidation, 0.0);

            Assert.Equal(new[] { "existing" }, ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths"));
        }

        [Fact]
        public async Task PerformPhaseOperationSync_SongListDb_ShouldStartBackgroundWorkOnce()
        {
            var stage = CreateControlledStage();
            stage.InstallTestBackgroundScope();

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.SongListDB, 0.0);
            Assert.True(SpinWait.SpinUntil(
                () => stage.InitializeDatabaseCalls == 1,
                TimeSpan.FromSeconds(5)),
                "the database initialization was not started");

            // The same phase never kicks off a second operation (once-guard).
            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.SongListDB, 0.0);

            Assert.Equal(1, stage.InitializeDatabaseCalls);
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
        public void OnUpdate_WhenCompleteAfterDraw_ShouldPublishSummaryRequestBeforeTitleChange()
        {
            var game = ReflectionHelpers.CreateGame();
            var diagnostics = new RecordingGameCrashDiagnostics();
            ReflectionHelpers.SetPrivateField(game, "_gameCrashDiagnostics", diagnostics);
            var stageManager = new Mock<IStageManager>();
            var summaryPublishedBeforeTitleChange = false;
            ReflectionHelpers.SetPrivateField(
                game,
                "<StageManager>k__BackingField",
                stageManager.Object);
            var stage = new LifecycleControlledStartupStage(game);
            // BaseStage.Update skips inactive stages; this test drives Update directly.
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_startupPhase",
                StartupPhase.Complete);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_hasRenderedStartupFrame",
                true);
            stageManager.Setup(manager => manager.ChangeStage(
                    StageType.Title,
                    It.IsAny<IStageTransition>()))
                .Callback<StageType, IStageTransition>((_, _) =>
                {
                    summaryPublishedBeforeTitleChange =
                        diagnostics.Contexts.Snapshots.Any(
                            item => item.Kind == CrashContextKind.Startup &&
                                Equals(
                                    item.Fields["Milestone"],
                                    StartupMilestone.SummaryRequest));
                });

            stage.UpdateForTest(0.001);

            Assert.True(summaryPublishedBeforeTitleChange);
            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(
                        transition =>
                            transition is StartupToTitleTransition)),
                Times.Once);
        }

        [Fact]
        public async Task StartSongEnumeration_ShouldReportProgressViaLatest_UseScopeToken_AndApplyTerminalLast()
        {
            var songPaths = new[] { "SongsRoot" };
            var stage = CreateControlledStage(songPaths: songPaths);
            var enumerationCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextEnumerationTask = enumerationCompletion.Task;
            stage.ReportedEnumerationProgress = new EnumerationProgress
            {
                CurrentFile = Path.Combine("SongsRoot", "test-song.dtx"),
                ProcessedCount = 3,
                DiscoveredSongs = 2
            };
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            await WaitForEnumerationStartedAsync(stage);

            // Latest progress applies on the scope drain — no SynchronizationContext involved.
            scope.Drain();
            var progressMessage = ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage");
            Assert.Contains("test-song.dtx", progressMessage);
            Assert.Contains("3 processed", progressMessage);
            Assert.Contains("2 songs", progressMessage);
            Assert.True(stage.LastEnumerationToken.CanBeCanceled);

            // The posted terminal result must land after (and win over) progress.
            enumerationCompletion.SetResult(CreateEnumerationResult());
            DrainUntilPostedTerminal(scope, stage);
            Assert.Contains("Complete", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Equal(1, stage.EnumerateSongsCalls);
            Assert.Equal(songPaths, stage.LastSongPaths);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenProgressReportsCurrentOperation_ShouldShowItOnDrain()
        {
            var stage = CreateControlledStage(songPaths: new[] { "SongsRoot" });
            var enumerationCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextEnumerationTask = enumerationCompletion.Task;
            stage.ReportedEnumerationProgress = new EnumerationProgress
            {
                CurrentOperation = "Scanning"
            };
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            await WaitForEnumerationStartedAsync(stage);

            scope.Drain();
            Assert.Contains(
                "Scanning",
                ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
        }

        [Fact]
        public void StartSongEnumeration_WhenNeedsEnumerationThrows_ShouldFallbackThenPostError()
        {
            var stage = new ThrowingNeedsEnumerationStartupStage();
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            DrainUntilPostedTerminal(scope, stage);

            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Contains("Error", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
        }

        [Fact]
        public void PerformPhaseOperationSync_SaveSongsDb_ShouldOnlyMarkInitialized()
        {
            var stage = CreateControlledStage();

            ReflectionHelpers.InvokePrivateMethod(
                stage,
                "PerformPhaseOperationSync",
                StartupPhase.SaveSongsDB,
                0.0);

            Assert.Equal(0, stage.SaveSongsDatabaseCalls);
            Assert.True(stage.MarkSongManagerInitializedCalled);
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
        public void OnUpdate_WhenCompleteBeforeAnyDraw_ShouldNotRequestTitle()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(
                game,
                "<StageManager>k__BackingField",
                stageManager.Object);
            var stage = CreateStage(
                phase: StartupPhase.Complete,
                elapsedTime: 0.0,
                phaseStartTime: 0.0,
                game: game);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.001);

            stageManager.Verify(
                manager => manager.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>()),
                Times.Never);
        }

        [Fact]
        public void OnUpdate_WhenCompleteAfterOneDraw_ShouldRequestTitleOnce()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(
                game,
                "<StageManager>k__BackingField",
                stageManager.Object);
            var stage = new GraphicsControlledStartupStage(game);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_startupPhase",
                StartupPhase.Complete);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_spriteBatch",
                stage.SpriteBatchStub);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_whitePixel",
                stage.WhitePixelStub);

            stage.DrawForTest(0.001);
            stage.UpdateForTest(0.001);
            stage.UpdateForTest(0.001);

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(
                        transition => transition is StartupToTitleTransition)),
                Times.Once);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenEnumerationNeeded_ShouldNotBuildDatabaseHierarchy()
        {
            var stage = CreateControlledStage();
            var enumerationCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextEnumerationTask = enumerationCompletion.Task;
            stage.NextEnumerationResult = CreateEnumerationResult();
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            await WaitForEnumerationStartedAsync(stage);

            Assert.Equal(1, stage.NeedsEnumerationCalls);
            Assert.Equal(1, stage.EnumerateSongsCalls);
            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.True(stage.LastForceEnumeration);

            enumerationCompletion.SetResult(CreateEnumerationResult());
            DrainUntilPostedTerminal(scope, stage);

            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.Equal(0, stage.SaveSongsDatabaseCalls);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenEnumerationHasNoActiveRoots_ShouldPublishEmptyLibrary()
        {
            var stage = CreateControlledStage(songPaths: ["MissingRoot"]);
            var enumerationCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextEnumerationTask = enumerationCompletion.Task;
            stage.NextEnumerationResult = CreateEnumerationResult(
                outcome: SongEnumerationOutcome.NoActiveRoots,
                activeRoots: Array.Empty<string>());
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            await WaitForEnumerationStartedAsync(stage);
            enumerationCompletion.SetResult(stage.NextEnumerationResult);
            DrainUntilPostedTerminal(scope, stage);

            Assert.Equal(1, stage.PublishEmptyLibraryCalls);
            Assert.Equal(1, stage.EnumerateSongsCalls);
            Assert.Equal(0, stage.BuildHierarchyCalls);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenNoConfiguredRoots_ShouldPublishEmptyLibraryWithoutEnumeration()
        {
            var stage = CreateControlledStage(songPaths: Array.Empty<string>());
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            DrainUntilPostedTerminal(scope, stage);

            Assert.Equal(1, stage.PublishEmptyLibraryCalls);
            Assert.Equal(0, stage.NeedsEnumerationCalls);
            Assert.Equal(0, stage.EnumerateSongsCalls);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenCacheValid_ShouldBuildDatabaseHierarchyOnce()
        {
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = false;
            stage.ForceEnumerationForTest = false;
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            DrainUntilPostedTerminal(scope, stage);

            Assert.Equal(1, stage.NeedsEnumerationCalls);
            Assert.Equal(0, stage.EnumerateSongsCalls);
            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Equal(0, stage.SaveSongsDatabaseCalls);
            Assert.False(stage.LastForceEnumeration);
        }

        [Fact]
        public void ForceEnumeration_InProduction_ShouldLetTheFreshnessCheckChooseTheCache()
        {
            var stage = CreateStage();
            var forceEnumeration = typeof(StartupStage)
                .GetProperty("ForceEnumeration", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(stage);

            Assert.Equal(false, forceEnumeration);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenZeroCandidatesPublish_ShouldPreservePublishedHierarchyIdentity()
        {
            var publishedHierarchy = new SongListNode { Title = "Published" };
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationResult = CreateEnumerationResult(
                rootNodes: new List<SongListNode> { publishedHierarchy });
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            DrainUntilPostedTerminal(scope, stage);
            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.BuildSongLists, 0.0);

            Assert.Same(publishedHierarchy, stage.PublishedHierarchy);
            Assert.Equal(0, stage.BuildHierarchyCalls);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenEnumerationFails_ShouldFallbackOnceAndPostError()
        {
            var originalFailure = new IOException("enumeration failed");
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromException<SongEnumerationResult>(originalFailure);
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            DrainUntilPostedTerminal(scope, stage);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Equal(StartupPhase.BuildSongLists,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Contains(
                "Error",
                ReflectionHelpers.GetPrivateField<string>(
                    stage,
                    "_currentProgressMessage"));
        }

        [Fact]
        public async Task StartSongEnumeration_WhenEnumerationReturnsNoResult_ShouldFallbackOnceAndPostError()
        {
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromResult<SongEnumerationResult>(null!);
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            DrainUntilPostedTerminal(scope, stage);

            Assert.Equal(1, stage.BuildHierarchyCalls);
        }

        [Fact]
        public async Task StartSongEnumeration_WhenFallbackFails_ShouldPostOriginalErrorAndReachTerminalOutcome()
        {
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromException<SongEnumerationResult>(new IOException("original enumeration error"));
            stage.BuildHierarchyException =
                new InvalidOperationException("cache unavailable");
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            DrainUntilPostedTerminal(scope, stage);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Null(stage.PublishedHierarchy);
            Assert.Equal(StartupPhase.BuildSongLists,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
        }

        [Fact]
        public async Task OnDeactivate_DuringPendingEnumeration_ShouldCancelAndFenceStaleSuccess()
        {
            var stage = CreateLifecycleControlledStage();
            var oldCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var staleResult = CreateEnumerationResult(
                discoveredCharts: 1,
                parsedCharts: 1,
                logicalGroups: 1);
            stage.Activate();
            stage.NextEnumerationTask = oldCompletion.Task;

            stage.StartSongLoadForTest();
            // Park the chain on the enumeration await before retiring, so the
            // captured token is the scope's and stale completions stay observed.
            await WaitForEnumerationStartedAsync(stage);
            var oldToken = stage.LastEnumerationToken;
            stage.Deactivate();

            // Retiring the scope cancels the in-flight chain without awaiting it.
            Assert.True(oldToken!.IsCancellationRequested);

            stage.Activate();
            oldCompletion.SetResult(staleResult);
            // The retired scope can never apply the stale success; let the worker
            // settle before asserting (nothing it does can mutate the stage).
            await Task.Delay(100);

            Assert.Equal(
                StartupPhase.SystemSounds,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.Null(stage.PublishedHierarchy);

            var freshCompletion =
                new TaskCompletionSource<SongEnumerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            var freshResult = CreateEnumerationResult(
                discoveredCharts: 2,
                parsedCharts: 2,
                logicalGroups: 1);
            stage.NextEnumerationTask = freshCompletion.Task;
            stage.NextEnumerationResult = freshResult;
            var freshScope = stage.BackgroundWork!;

            stage.StartSongLoadForTest();
            freshCompletion.SetResult(freshResult);
            DrainUntilPostedTerminal(freshScope, stage);
            stage.UpdateForTest(0.001);

            Assert.Equal(2, stage.EnumerateSongsCalls);
        }

        [Fact]
        public async Task OnDeactivate_WhenOldEnumerationFaults_ShouldNotFallbackOrMutateReactivation()
        {
            var stage = CreateLifecycleControlledStage();
            var oldCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var original = new InvalidOperationException("stale failure");
            stage.Activate();
            stage.NextEnumerationTask = oldCompletion.Task;

            stage.StartSongLoadForTest();
            await WaitForEnumerationStartedAsync(stage);
            stage.Deactivate();
            stage.Activate();
            oldCompletion.SetException(original);
            await Task.Delay(100);

            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.Equal(
                StartupPhase.SystemSounds,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
        }

        [Fact]
        public async Task OnDeactivate_WhenOldEnumerationCancels_ShouldNotMutateReactivation()
        {
            var stage = CreateLifecycleControlledStage();
            var oldCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.Activate();
            stage.NextEnumerationTask = oldCompletion.Task;

            stage.StartSongLoadForTest();
            await WaitForEnumerationStartedAsync(stage);
            var oldToken = stage.LastEnumerationToken;
            stage.Deactivate();
            stage.Activate();
            oldCompletion.SetCanceled(oldToken);
            await Task.Delay(100);

            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.Equal(
                StartupPhase.SystemSounds,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
        }

        [Fact]
        public async Task OnDeactivate_WhenAbandonedEnumerationFaults_ShouldObserveException()
        {
            var unobservedFailures = new List<Exception>();
            EventHandler<UnobservedTaskExceptionEventArgs> handler =
                (_, args) =>
                {
                    if (args.Exception.Flatten().InnerExceptions.Any(
                        exception =>
                            exception.Message == "abandoned failure"))
                    {
                        unobservedFailures.Add(args.Exception);
                    }
                    args.SetObserved();
                };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                var abandonedTask =
                    await CreateAbandonedFaultedLoadAsync();

                for (var attempt = 0;
                     attempt < 10 && abandonedTask.IsAlive;
                     attempt++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(10);
                }

                Assert.Empty(unobservedFailures);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }

        [Fact]
        public void Dispose_WithPendingOperation_ShouldCancelScopeAndDisposeCleanly()
        {
            var stage = CreateControlledStage();
            var pendingCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextDatabaseTask = pendingCompletion.Task;
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.SongListDB);
            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.SongListDB, 0.0);
            // BaseStage.Dispose deactivates an active stage, which retires the scope.
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);

            var exception = Record.Exception(() => InvokeDispose(stage, true));

            Assert.Null(exception);
            Assert.True(scope.Token.IsCancellationRequested);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_disposed"));

            // The abandoned worker completes later without escaping any exception.
            pendingCompletion.TrySetResult(true);
        }

        [Fact]
        public async Task Dispose_WhenEnumerationFaultsAfterDisposal_ShouldNotThrow()
        {
            var stage = CreateControlledStage();
            var completion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var faultObserved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            // The chain awaits this wrapper (not completion.Task directly), so its
            // continuation runs only once the worker actually consumed the fault;
            // waiting on it below synchronizes with the real worker unwind.
            stage.NextEnumerationTask =
                ObserveEnumerationOutcomeAsync(completion.Task, faultObserved);
            var scope = stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.EnumerateSongs);
            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);
            // Park the chain on the enumeration await BEFORE disposal: disposing
            // while the worker is still en route would let it hit a canceled-token
            // checkpoint without ever consuming completion.Task, leaving the later
            // SetException fault unobserved and unrelated to the chain.
            await WaitForEnumerationStartedAsync(stage);
            // BaseStage.Dispose deactivates an active stage, which retires the scope
            // and cancels the chain token, so the later fault runs no fallback.
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);

            var exception = Record.Exception(() => InvokeDispose(stage, true));

            Assert.Null(exception);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_disposed"));

            // The chain faults after disposal; the retired scope drops the result.
            completion.SetException(new InvalidOperationException("boom"));
            // Deterministic unwind: wait until the worker actually consumed the
            // fault. Past that point the fallback guard's inputs are frozen (the
            // scope token was already canceled by disposal), so no fallback can
            // run after the signal — the assertion below cannot pass vacuously.
            Assert.True(
                SpinWait.SpinUntil(
                    () => faultObserved.Task.IsCompleted,
                    TimeSpan.FromSeconds(5)),
                "the worker did not consume the enumeration fault");
            Assert.Equal(0, stage.BuildHierarchyCalls);
        }

        [Fact]
        public void OnActivate_ShouldInitializeGraphicsStateAndResetProgress()
        {
            var game = ReflectionHelpers.CreateGame();
            var stage = new GraphicsControlledStartupStage(game);
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(manager => manager.CreateTextureFromColor(Color.White))
                .Returns(CreateWhitePixelResource(stage.WhitePixelStub));
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_progressMessages", new List<string> { "stale" });
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.BuildSongLists);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 12.0);
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", 4.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

            Assert.Same(stage.SpriteBatchStub, ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
            Assert.Same(stage.WhitePixelStub, ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.Same(resourceManager.Object, ReflectionHelpers.GetPrivateField<IResourceManager>(stage, "_resourceManager"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Equal(StartupPhase.SystemSounds, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_elapsedTime"));
            Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_phaseStartTime"));
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
            ReflectionHelpers.SetPrivateField(stage, "_font", null);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);
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

        [Fact]
        public void OnActivate_WhenFontLoadSucceeds_ShouldSetFontFields()
        {
            var game = ReflectionHelpers.CreateGame();
            var resourceManager = new Mock<IResourceManager>();
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);
            var regularFont = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            var stage = new FontControlledStartupStage(game)
            {
                RegularFont = regularFont.Object,
                BoldFont = boldFont.Object
            };

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

            Assert.Same(regularFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Same(boldFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
            regularFont.Verify(f => f.RemoveReference(), Times.Never);
            boldFont.Verify(f => f.RemoveReference(), Times.Never);
        }

        [Fact]
        public void OnActivate_WhenBoldFontLoadFails_ShouldReleaseRegularFontAndThrow()
        {
            var game = ReflectionHelpers.CreateGame();
            var resourceManager = new Mock<IResourceManager>();
            var whitePixelResource = new Mock<ITexture>();
            resourceManager.Setup(manager => manager.CreateTextureFromColor(Color.White))
                .Returns(whitePixelResource.Object);
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);
            var regularFont = new Mock<IFont>();
            var stage = new FontControlledStartupStage(game)
            {
                RegularFont = regularFont.Object,
                ThrowOnBoldFont = true
            };

            var exception = Assert.Throws<TargetInvocationException>(() => ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate"));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            regularFont.Verify(f => f.RemoveReference(), Times.Once);
            whitePixelResource.Verify(t => t.RemoveReference(), Times.Once);
            Assert.True(stage.SpriteBatchStub.IsDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
        }

        [Fact]
        public void OnActivate_WhenCreateWhitePixelThrows_ShouldDisposeSpriteBatchAndRethrow()
        {
            var game = ReflectionHelpers.CreateGame();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(manager => manager.CreateTextureFromColor(Color.White))
                .Throws(new InvalidOperationException("white-pixel-failed"));
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);
            var stage = new FontControlledStartupStage(game);

            var exception = Assert.Throws<TargetInvocationException>(
                () => ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate"));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.True(stage.SpriteBatchStub.IsDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_whitePixelResource"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
        }

        // HPA-796: fixture-owned managed pixel — the stage borrows its raw texture from
        // the ResourceManager-acquired ITexture instead of overriding a factory seam.
        private static ITexture CreateWhitePixelResource(Texture2D raw)
        {
            var resource = new Mock<ITexture>();
            resource.SetupGet(t => t.Texture).Returns(raw);
            return resource.Object;
        }

        // Uninitialized MonoGame GPU stubs must never reach their finalizers: the finalizer
        // calls GraphicsDevice.RemoveResourceReference(null) and aborts the test host.
        private static T CreateGpuStub<T>() where T : class
        {
#pragma warning disable SYSLIB0050
            var stub = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
            GC.SuppressFinalize(stub);
            return stub;
        }

        [Fact]
        public void CreateFontCore_BaseImplementation_ShouldCallResourceManagerLoadFont()
        {
            var game = ReflectionHelpers.CreateGame();
            var resourceManager = new Mock<IResourceManager>();
            var expectedFont = new Mock<IFont>();
            resourceManager.Setup(r => r.LoadFont("NotoSerifJP", 14, FontStyle.Bold))
                .Returns(expectedFont.Object);
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);

            var stage = new StartupStage(game);
            var result = ReflectionHelpers.InvokePrivateMethod(stage, "CreateFontCore", resourceManager.Object, string.Empty, 14, FontStyle.Bold);

            Assert.Same(expectedFont.Object, result);
            resourceManager.Verify(r => r.LoadFont("NotoSerifJP", 14, FontStyle.Bold), Times.Once);
        }

        [Fact]
        public void OnDeactivate_ShouldReleaseFontReferences()
        {
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

            font.Verify(f => f.RemoveReference(), Times.Once);
            boldFont.Verify(f => f.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
        }

        [Fact]
        public void OnDeactivate_ShouldReleaseCachedWhitePixel_AndBalancedReactivateAcquiresFreshReference()
        {
            // HPA-796 round trip: OnActivate acquires the cached pixel on every activation and
            // OnDeactivate previously released only the fonts, so a Startup → Title → Startup
            // round trip overwrote the outstanding reference. Each deactivation must release
            // its activation's reference.
            var game = ReflectionHelpers.CreateGame();
            var stage = new GraphicsControlledStartupStage(game);
            var firstPixelResource = new Mock<ITexture>();
            firstPixelResource.SetupGet(t => t.Texture).Returns(stage.WhitePixelStub);
            var secondPixelResource = new Mock<ITexture>();
            secondPixelResource.SetupGet(t => t.Texture).Returns(stage.WhitePixelStub);
            var pendingPixels = new Queue<ITexture>(new[] { firstPixelResource.Object, secondPixelResource.Object });
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(manager => manager.CreateTextureFromColor(Color.White))
                .Returns(() => pendingPixels.Dequeue());
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");
            Assert.Same(firstPixelResource.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_whitePixelResource"));
            Assert.Same(stage.WhitePixelStub, ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
            firstPixelResource.Verify(t => t.RemoveReference(), Times.Never);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");
            firstPixelResource.Verify(t => t.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_whitePixelResource"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");
            resourceManager.Verify(manager => manager.CreateTextureFromColor(Color.White), Times.Exactly(2));
            Assert.Same(secondPixelResource.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_whitePixelResource"));
            secondPixelResource.Verify(t => t.RemoveReference(), Times.Never);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");
            secondPixelResource.Verify(t => t.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_whitePixelResource"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
        }

        [Fact]
        public void Dispose_WhenNotDisposed_ShouldReleaseFontReferences()
        {
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Inactive);
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);

            InvokeDispose(stage, true);

            font.Verify(f => f.RemoveReference(), Times.Once);
            boldFont.Verify(f => f.RemoveReference(), Times.Once);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_disposed"));
        }

        [Fact]
        public void DrawTextWithFallback_WhenBoldTrue_ShouldUseBoldFont()
        {
            var boldFont = new Mock<IFont>();
            var stage = new GraphicsControlledStartupStage(ReflectionHelpers.CreateGame());
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", stage.SpriteBatchStub);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawTextWithFallback", "test", 10, 20, true, null);

            boldFont.Verify(f => f.DrawString(stage.SpriteBatchStub, "test", new Vector2(10, 20), Color.White), Times.Once);
        }

        [Fact]
        public void DrawTextWithFallback_WhenNoFont_ShouldUseFallbackRect()
        {
            var stage = new GraphicsControlledStartupStage(ReflectionHelpers.CreateGame());
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", stage.SpriteBatchStub);
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", stage.WhitePixelStub);
            ReflectionHelpers.SetPrivateField(stage, "_font", null);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawTextWithFallback", "test", 10, 20, false, null);

            Assert.Contains(stage.DrawCalls, call => call.Destination == new Rectangle(10, 20, 32, 16) && call.Color == Color.White);
        }

        [Fact]
        public void DrawVersionInfo_WhenFontExists_ShouldMeasureAndDraw()
        {
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(200, 14));
            var stage = new GraphicsControlledStartupStage(ReflectionHelpers.CreateGame());
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", stage.SpriteBatchStub);
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawVersionInfo");

            var versionText = $"DTXManiaCX v{ApplicationVersion.DisplayWithPrerelease} - MonoGame Edition";

            font.Verify(f => f.MeasureString(versionText), Times.Once);
            font.Verify(f => f.DrawString(stage.SpriteBatchStub, versionText, new Vector2(1070, 2), Color.White), Times.Once);
        }

        private static ConfigData CreateConfigWithSongRoots(params string[] roots)
        {
            var config = new ConfigData();
            config.SongRoots.Clear();
            config.SongRoots.AddRange(roots);
            return config;
        }

        private static StartupStage CreateStage(
            StartupPhase phase = StartupPhase.SystemSounds,
            double elapsedTime = 0.0,
            double phaseStartTime = 0.0,
            string currentProgressMessage = "",
            ConfigData? configData = null,
            bool allowNullConfig = false,
            BaseGame? game = null)
        {
#pragma warning disable SYSLIB0050
            var stage = (StartupStage)FormatterServices.GetUninitializedObject(typeof(StartupStage));
#pragma warning restore SYSLIB0050

            var effectiveConfigData = allowNullConfig ? configData : (configData ?? new ConfigData());
            ReflectionHelpers.SetPrivateField(stage, "_game", game ?? ReflectionHelpers.CreateGame());
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            ReflectionHelpers.SetPrivateField(stage, "_disposed", false);
            ReflectionHelpers.SetPrivateField(stage, "_isFirstUpdate", false);
            
            ReflectionHelpers.SetPrivateField(stage, "_progressMessages", new List<string>());
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", currentProgressMessage);
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", phase);
            ReflectionHelpers.SetPrivateField(stage, "_songManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_configManager", CreateConfigManager(effectiveConfigData));
            ReflectionHelpers.SetPrivateField(stage, "_logger", NullLogger<StartupStage>.Instance);
            ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "initial" });
            ReflectionHelpers.SetPrivateField(stage, "_phaseInfo", CreatePhaseInfo());
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", phaseStartTime);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", elapsedTime);

            return stage;
        }

        private static ControlledStartupStage CreateControlledStage(string[]? songPaths = null, ConfigData? configData = null)
        {
            var game = ReflectionHelpers.CreateGame();
            var fallbackConfig = new ConfigData
            {
                ScreenWidth = 1280,
                ScreenHeight = 720
            };
            fallbackConfig.SongRoots[0] = "Songs";
            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", CreateConfigManager(configData ?? fallbackConfig));

            var stage = new ControlledStartupStage(game);
            if (songPaths != null)
            {
                ReflectionHelpers.SetPrivateField(stage, "_songPaths", songPaths);
            }

            return stage;
        }

        /// <summary>
        /// Drains the stage scope until the posted terminal outcome of the current
        /// async phase has been applied (HPA-799 contract): async phases advance
        /// only through these posted success/error results.
        /// </summary>
        private static void DrainUntilPostedTerminal(
            StageBackgroundWorkScope scope,
            StartupStage stage)
        {
            var posted = SpinWait.SpinUntil(
                () =>
                {
                    scope.Drain();
                    return ReflectionHelpers.GetPrivateField<bool?>(stage, "_asyncPhaseSucceeded") != null;
                },
                TimeSpan.FromSeconds(5));
            Assert.True(posted, "the terminal success/error result was not posted and applied");
        }

        private static Task WaitForEnumerationStartedAsync(ControlledStartupStage stage)
        {
            // EnumerateSongsCalls increments before the fake captures its token,
            // reports progress or reads its completion task, so reaching it proves
            // nothing for the assertions that follow; wait for the fake's own
            // readiness signal instead, set only after all of those steps.
            var started = SpinWait.SpinUntil(
                () => stage.EnumerateSongsCalls >= 1 && stage.EnumerationStarted,
                TimeSpan.FromSeconds(5));
            Assert.True(started, "the enumeration chain did not start");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Wraps an enumeration completion task so a test can observe the moment
        /// the worker actually consumed its outcome: the wrapper's continuation
        /// runs only after the wrapped task settled. Waiting on
        /// <paramref name="outcome"/> is a deterministic unwind signal, unlike a
        /// grace delay which can assert before the worker resumed.
        /// </summary>
        private static async Task<SongEnumerationResult> ObserveEnumerationOutcomeAsync(
            Task<SongEnumerationResult> enumeration,
            TaskCompletionSource outcome)
        {
            try
            {
                return await enumeration.ConfigureAwait(false);
            }
            finally
            {
                outcome.TrySetResult();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<WeakReference>
            CreateAbandonedFaultedLoadAsync()
        {
            var stage = CreateLifecycleControlledStage();
            var completion =
                new TaskCompletionSource<SongEnumerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            stage.Activate();
            stage.NextEnumerationTask = completion.Task;
            stage.StartSongLoadForTest();
            // The chain must be parked on the enumeration await before the fault
            // fires, so the fault is observed (not left unobserved for the GC).
            await WaitForEnumerationStartedAsync(stage);
            stage.Deactivate();
            stage.Activate();
            completion.SetException(
                new InvalidOperationException("abandoned failure"));

            // The chain's worker observed (and the retired scope dropped) the fault;
            // settling the worker keeps the GC probe below deterministic.
            await Task.Delay(100);

            return new WeakReference(completion.Task);
        }

        private static LifecycleControlledStartupStage
            CreateLifecycleControlledStage()
        {
            var game = ReflectionHelpers.CreateGame();
            var configData = new ConfigData
            {
                ScreenWidth = 1280,
                ScreenHeight = 720
            };
            configData.SongRoots[0] = "Songs";
            ReflectionHelpers.SetPrivateField(
                game,
                "<ConfigManager>k__BackingField",
                CreateConfigManager(configData));

            var stage = new LifecycleControlledStartupStage(game);

            // HPA-796: OnActivate acquires the cached white pixel through the ResourceManager;
            // the lifecycle stage borrows the stub pixel like every other controlled seam.
            var whitePixelResourceManager = new Mock<IResourceManager>();
            whitePixelResourceManager.Setup(manager => manager.CreateTextureFromColor(Color.White))
                .Returns(CreateWhitePixelResource(stage.WhitePixelStub));
            ReflectionHelpers.SetPrivateField(
                game,
                "<ResourceManager>k__BackingField",
                whitePixelResourceManager.Object);

            return stage;
        }

        private static SongEnumerationResult CreateEnumerationResult(
            int discoveredCharts = 0,
            int parsedCharts = 0,
            int logicalGroups = 0,
            List<SongListNode>? rootNodes = null,
            SongEnumerationOutcome outcome = SongEnumerationOutcome.ImportedAndPublished,
            IReadOnlyList<string>? activeRoots = null)
        {
            var paths = Enumerable.Range(1, discoveredCharts)
                .Select(index => $"/songs/chart-{index}.dtx")
                .ToArray();
            var candidates = paths.Take(parsedCharts)
                .Select((path, index) =>
                {
                    var song = new SongEntity
                    {
                        Title = $"Song {index}",
                        Artist = "Artist"
                    };
                    var chart = new SongChart
                    {
                        Song = song,
                        FilePath = path
                    };
                    song.Charts.Add(chart);
                    return new SongImportCandidate(
                        song,
                        chart,
                        path,
                        $"group-{index}",
                        index);
                })
                .ToList();
            var pendingSongs = Enumerable.Range(1, logicalGroups)
                .Select(index => new PendingSongNode(
                    $"group-{index}",
                    new SongListNode { Title = $"Group {index}" },
                    Array.Empty<string>()))
                .ToList();
            var batch = new SongEnumerationBatch
            {
                ActiveRoots = activeRoots ?? new[] { "/songs" },
                DiscoveredChartPaths = new HashSet<string>(
                    paths,
                    SongPathIdentity.CanonicalComparer),
                Candidates = candidates,
                RootNodes = rootNodes ?? new List<SongListNode>(),
                PendingSongs = pendingSongs,
                Errors = new List<SongEnumerationError>(),
                IsComplete = true
            };
            var mutationCount = parsedCharts > 0 ? 1 : 0;
            var import = new SongBulkImportResult(
                new Dictionary<string, SongChart>(
                    SongPathIdentity.CanonicalComparer),
                Added: mutationCount,
                Updated: mutationCount,
                Preserved: mutationCount,
                Skipped: mutationCount,
                Conflicts: mutationCount,
                StaleCharts: mutationCount,
                StaleSongs: mutationCount);
            return new SongEnumerationResult(
                outcome,
                batch,
                import);
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

            public bool NextDatabaseResult { get; set; } = true;

            public Task<bool>? NextDatabaseTask { get; set; }

            public Exception? DatabaseInitializationException { get; set; }

            public int NeedsEnumerationCalls { get; private set; }

            public int EnumerateSongsCalls { get; private set; }

            private volatile TaskCompletionSource? _enumerationStartedSignal;

            /// <summary>
            /// True once the most recent enumeration invocation captured its
            /// token, reported progress and read its completion task; the
            /// <see cref="EnumerateSongsCalls"/> counter alone increments before
            /// those steps.
            /// </summary>
            public bool EnumerationStarted
            {
                get
                {
                    var signal = _enumerationStartedSignal;
                    return signal != null && signal.Task.IsCompleted;
                }
            }

            public int BuildHierarchyCalls { get; private set; }

            public int SaveSongsDatabaseCalls { get; private set; }

            public int PublishEmptyLibraryCalls { get; private set; }

            public SongListNode? PublishedHierarchy { get; set; }

            public bool NextNeedsEnumerationResult { get; set; } = true;

            public bool ForceEnumerationForTest { get; set; } = true;

            public bool MarkSongManagerInitializedCalled { get; private set; }

            public EnumerationProgress? ReportedEnumerationProgress { get; set; }

            public bool LastForceEnumeration { get; private set; }

            public SongEnumerationResult NextEnumerationResult { get; set; } =
                CreateEnumerationResult();

            public Task<SongEnumerationResult>? NextEnumerationTask { get; set; }

            public Exception? BuildHierarchyException { get; set; }
            public Task? NextBuildHierarchyTask { get; set; }

            protected override bool ForceEnumeration =>
                ForceEnumerationForTest;

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
                if (DatabaseInitializationException != null)
                {
                    throw DatabaseInitializationException;
                }
                return NextDatabaseTask ??
                    Task.FromResult(NextDatabaseResult);
            }

            protected override Task<bool> NeedsEnumerationCoreAsync(string[] songPaths, bool forceEnumeration)
            {
                NeedsEnumerationCalls++;
                LastSongPaths = songPaths;
                LastForceEnumeration = forceEnumeration;
                return Task.FromResult(NextNeedsEnumerationResult);
            }

            protected override Task<SongEnumerationResult>
                EnumerateSongsCoreAsync(
                    string[] songPaths,
                    IProgress<EnumerationProgress> progressReporter,
                    CancellationToken cancellationToken)
            {
                EnumerateSongsCalls++;
                LastSongPaths = songPaths;
                LastEnumerationToken = cancellationToken;
                if (ReportedEnumerationProgress != null)
                {
                    progressReporter.Report(ReportedEnumerationProgress);
                }

                var resultTask = NextEnumerationTask;
                if (resultTask == null)
                {
                    var result = NextEnumerationResult;
                    if (result.Batch.RootNodes.Count > 0)
                    {
                        PublishedHierarchy = result.Batch.RootNodes[0];
                    }

                    resultTask = Task.FromResult(result);
                }

                // Signal readiness only after the token was captured, progress was
                // reported and the completion task was read; waiters relying on the
                // call counter alone would race a paused worker mid-capture.
                var ready = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                ready.SetResult();
                _enumerationStartedSignal = ready;

                return resultTask;
            }

            protected override Task BuildHierarchyFromDatabaseOnceCoreAsync(
                string[] songPaths)
            {
                BuildHierarchyCalls++;
                LastSongPaths = songPaths;
                if (BuildHierarchyException != null)
                {
                    return Task.FromException(BuildHierarchyException);
                }
                PublishedHierarchy = new SongListNode { Title = "Rebuilt" };
                return NextBuildHierarchyTask ?? Task.CompletedTask;
            }

            protected override void MarkSongManagerInitialized()
            {
                MarkSongManagerInitializedCalled = true;
            }

            protected override void PublishEmptyLibraryCore()
            {
                PublishEmptyLibraryCalls++;
            }

        }

        private sealed class LifecycleControlledStartupStage :
            ControlledStartupStage
        {
            public LifecycleControlledStartupStage(BaseGame game) : base(game)
            {
            }

            public GraphicsDevice GraphicsDeviceStub { get; } = CreateGpuStub<GraphicsDevice>();

            public SpriteBatch SpriteBatchStub { get; } = CreateGpuStub<SpriteBatch>();

            public Texture2D WhitePixelStub { get; } = CreateGpuStub<Texture2D>();

            public void StartSongLoadForTest()
            {
                // Drive the kick-off through the public Update so the scope drain
                // (BaseStage.Update) is in the loop, matching production ordering.
                ReflectionHelpers.SetPrivateField(
                    this,
                    "_startupPhase",
                    StartupPhase.EnumerateSongs);
                ReflectionHelpers.SetPrivateField(
                    this,
                    "_operationPerformedForPhase",
                    null);
                UpdateForTest(0.001);
            }

            public void UpdateForTest(double deltaTime)
            {
                Update(deltaTime);
            }

            protected override GraphicsDevice GetGraphicsDeviceCore()
            {
                return GraphicsDeviceStub;
            }

            protected override SpriteBatch CreateSpriteBatchCore(
                GraphicsDevice graphicsDevice)
            {
                return SpriteBatchStub;
            }

            protected override IFont CreateFontCore(
                IResourceManager resourceManager,
                string fontFamily,
                int size,
                FontStyle style)
            {
                return null!;
            }

            protected override IFont CreateStatusFallbackFontCore(
                IResourceManager resourceManager,
                int size)
            {
                return null!;
            }

        }

        private class GraphicsControlledStartupStage : StartupStage
        {
            public GraphicsControlledStartupStage(BaseGame game) : base(game)
            {
            }

            public GraphicsDevice GraphicsDeviceStub { get; } = CreateGpuStub<GraphicsDevice>();

            public SpriteBatch SpriteBatchStub { get; } = CreateGpuStub<SpriteBatch>();

            public Texture2D WhitePixelStub { get; } = CreateGpuStub<Texture2D>();

            public Viewport ViewportValue { get; set; } = new(0, 0, 1280, 720);

            public int BeginCalls { get; private set; }

            public int EndCalls { get; private set; }

            public sealed record DrawCall(Rectangle Destination, Color Color);

            public List<DrawCall> DrawCalls { get; } = new();

            public void DrawForTest(double deltaTime)
            {
                OnDraw(deltaTime);
            }

            public void UpdateForTest(double deltaTime)
            {
                OnUpdate(deltaTime);
            }

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

            protected override IFont CreateFontCore(IResourceManager resourceManager, string fontFamily, int size, FontStyle style)
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

        private sealed class FontControlledStartupStage : GraphicsControlledStartupStage
        {
            public FontControlledStartupStage(BaseGame game) : base(game)
            {
            }

            public IFont? RegularFont { get; set; }

            public IFont? BoldFont { get; set; }

            public bool ThrowOnBoldFont { get; set; }

            protected override IFont CreateFontCore(IResourceManager resourceManager, string fontFamily, int size, FontStyle style)
            {
                if (style == FontStyle.Bold && ThrowOnBoldFont)
                {
                    throw new InvalidOperationException("Simulated bold font failure");
                }

                return style == FontStyle.Bold ? BoldFont! : RegularFont!;
            }
        }

        private sealed class ThrowingNeedsEnumerationStartupStage : ControlledStartupStage
        {
            public ThrowingNeedsEnumerationStartupStage() : base(ReflectionHelpers.CreateGame())
            {
            }

            protected override Task<bool> NeedsEnumerationCoreAsync(string[] songPaths, bool forceEnumeration)
            {
                throw new IOException("Simulated IO failure");
            }
        }

        #region Patch Coverage: Phase Operations and Lifecycle Helpers

        [Fact]
        public void PerformPhaseOperation_LoadScoreCache_ShouldAdvancePhaseGuard()
        {
            var stage = CreateControlledStage();
            ReflectionHelpers.SetPrivateField(stage, "_operationPerformedForPhase", StartupPhase.SystemSounds);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(
                stage,
                "PerformPhaseOperationSync",
                StartupPhase.LoadScoreCache,
                0.0));

            Assert.Null(exception);
            // The per-phase operation guard advances so the phase runs exactly once.
            Assert.Equal(
                StartupPhase.LoadScoreCache,
                ReflectionHelpers.GetPrivateField<StartupPhase?>(stage, "_operationPerformedForPhase"));
        }

        [Fact]
        public void PerformPhaseOperation_LoadScoreFiles_ShouldAdvancePhaseGuard()
        {
            var stage = CreateControlledStage();
            ReflectionHelpers.SetPrivateField(stage, "_operationPerformedForPhase", StartupPhase.SystemSounds);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(
                stage,
                "PerformPhaseOperationSync",
                StartupPhase.LoadScoreFiles,
                0.0));

            Assert.Null(exception);
            Assert.Equal(
                StartupPhase.LoadScoreFiles,
                ReflectionHelpers.GetPrivateField<StartupPhase?>(stage, "_operationPerformedForPhase"));
        }

        #endregion

        [Fact]
        public void Dispose_WithRunningTask_ShouldCancelWithoutWaiting()
        {
            var tcs = new TaskCompletionSource<bool>();
            var stage = CreateControlledStage();
            stage.NextDatabaseTask = tcs.Task;
            stage.InstallTestBackgroundScope();
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.SongListDB);
            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.SongListDB, 0.0);
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Inactive);

            InvokeDispose(stage, true);

            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_disposed"));

            tcs.TrySetResult(true);
        }

        [Fact]
        public void Dispose_WithoutCurrentTask_ShouldDisposeResourcesCleanly()
        {
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Inactive);
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);

            InvokeDispose(stage, true);

            font.Verify(f => f.RemoveReference(), Times.Once);
            boldFont.Verify(f => f.RemoveReference(), Times.Once);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_disposed"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
        }

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
        public void GetNextPhase_ShouldReturnCorrectSequence(StartupPhase current, StartupPhase expected)
        {
            var stage = CreateStage();

            var result = ReflectionHelpers.InvokePrivateMethod<StartupPhase>(stage, "GetNextPhase", current);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(StartupPhase.SystemSounds, false)]
        [InlineData(StartupPhase.ConfigValidation, false)]
        [InlineData(StartupPhase.SongListDB, true)]
        [InlineData(StartupPhase.SongsDB, false)]
        [InlineData(StartupPhase.LoadScoreCache, false)]
        [InlineData(StartupPhase.LoadScoreFiles, false)]
        [InlineData(StartupPhase.EnumerateSongs, true)]
        [InlineData(StartupPhase.BuildSongLists, false)]
        [InlineData(StartupPhase.SaveSongsDB, false)]
        [InlineData(StartupPhase.Complete, false)]
        public void HasAsyncOperation_ForAsyncPhases_ShouldReturnCorrectValue(StartupPhase phase, bool expected)
        {
            var stage = CreateStage();

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(stage, "HasAsyncOperation", phase);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void OnUpdate_WhenCompleteAfterRenderedFrame_ShouldTransitionToTitle()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager.Object);
            var stage = CreateStage(
                phase: StartupPhase.Complete,
                elapsedTime: 0.0,
                phaseStartTime: 0.0,
                game: game);
            ReflectionHelpers.SetPrivateField(stage, "_hasRenderedStartupFrame", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.2);

            stageManager.Verify(manager => manager.ChangeStage(
                StageType.Title,
                It.Is<IStageTransition>(transition => transition is StartupToTitleTransition)),
                Times.Once);
        }

        [Fact]
        public void OnUpdate_WhenCompleteBeforeRenderedFrame_ShouldNotTransition()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager.Object);
            var stage = CreateStage(
                phase: StartupPhase.Complete,
                elapsedTime: 0.0,
                phaseStartTime: 0.05,
                game: game);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.01);

            stageManager.Verify(manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()), Times.Never);
        }

        [Fact]
        public void Dispose_WhenNotDisposing_ShouldNotReleaseResources()
        {
            var font = new Mock<IFont>();
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Inactive);
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);

            InvokeDispose(stage, false);

            font.Verify(f => f.RemoveReference(), Times.Never);
        }

        [Fact]
        public void DrawStatusLine_WithFullSizeDisplayFont_ShouldCenterAndDrawNormally()
        {
            var stage = CreateStageWithTheme(
                "Startup.StatusCenterX=640",
                "Startup.StatusY=452",
                "Startup.StatusMaxWidth=560",
                "Startup.StatusText=#AABBCC");
            var font = new Mock<IFont>();
            font.Setup(value => value.MeasureString("Ready")).Returns(new Vector2(200, 17));

            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_statusFallbackFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", "Ready");

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            font.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                "Ready",
                new Vector2(540, 452),
                new Color(0xAA, 0xBB, 0xCC)), Times.Once);
        }

        [Fact]
        public void DrawStatusLine_WithWideNonAsciiText_ShouldUseFallbackFontAndScaledDraw()
        {
            var stage = CreateStageWithTheme(
                "Startup.StatusCenterX=640",
                "Startup.StatusY=452",
                "Startup.StatusMaxWidth=560",
                "Startup.StatusText=#F1F5F9");
            var displayFont = new Mock<IFont>();
            var fallbackFont = new Mock<IFont>();
            const string message = "Scanning: 星空のオルゴール.dtx";
            fallbackFont.Setup(value => value.MeasureString(message)).Returns(new Vector2(800, 17));

            ReflectionHelpers.SetPrivateField(stage, "_boldFont", displayFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_statusFallbackFont", fallbackFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", message);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            displayFont.Verify(value => value.DrawString(
                It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Never);
            fallbackFont.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                message,
                new Vector2(360, 452),
                new Color(0xF1, 0xF5, 0xF9),
                0f,
                Vector2.Zero,
                It.Is<Vector2>(scale => Math.Abs(scale.X - 0.7f) < 0.001f && Math.Abs(scale.Y - 0.7f) < 0.001f),
                SpriteEffects.None,
                0f), Times.Once);
        }

        [Fact]
        public void DrawStatusLine_WithoutFonts_ShouldDrawCenteredFallbackRectangle()
        {
            var stage = CreateStageWithTheme(
                "Startup.StatusCenterX=640",
                "Startup.StatusY=452",
                "Startup.StatusText=#22D3EE");
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_statusFallbackFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", "TEST");

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            Assert.Contains(stage.DrawCalls, call =>
                call.Destination == new Rectangle(624, 452, 32, 12) &&
                call.Color == new Color(0x22, 0xD3, 0xEE));
        }

        [Fact]
        public void DrawStatusLine_WithEmptyMessage_ShouldNotDraw()
        {
            var stage = CreateStageWithTheme("Startup.StatusCenterX=640");
            var font = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", string.Empty);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            font.VerifyNoOtherCalls();
            Assert.Empty(stage.DrawCalls);
        }

        [Fact]
        public void DrawCurrentProgress_WithoutWhitePixel_ShouldReturnWithoutDrawing()
        {
            var stage = CreateStageWithTheme("Startup.ProgressReadoutY=506");
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", null);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCurrentProgress");

            Assert.Empty(stage.DrawCalls);
        }

        [Fact]
        public void DrawCurrentProgress_WithThemedLedger_ShouldDrawRailStepAndPercent()
        {
            var stage = CreateStageWithTheme(
                "Startup.ProgressBarWidth=560",
                "Startup.ProgressBarHeight=6",
                "Startup.ProgressBarY=490",
                "Startup.ProgressBarBack=#1E293B",
                "Startup.ProgressBarFill=#22D3EE",
                "Startup.ProgressReadoutY=506",
                "Startup.ProgressReadoutText=#94A3B8");
            var font = new Mock<IFont>();
            font.Setup(value => value.MeasureString("55%")).Returns(new Vector2(30, 14));

            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.LoadScoreFiles);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 0.35d);
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", 0d);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCurrentProgress");

            Assert.Contains(stage.DrawCalls, call =>
                call.Destination == new Rectangle(360, 490, 560, 6) &&
                call.Color == new Color(0x1E, 0x29, 0x3B));
            Assert.Contains(stage.DrawCalls, call =>
                call.Destination == new Rectangle(360, 490, 308, 6) &&
                call.Color == new Color(0x22, 0xD3, 0xEE));
            font.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                "STEP 06 / 10",
                new Vector2(360, 506),
                new Color(0x94, 0xA3, 0xB8)), Times.Once);
            font.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                "55%",
                new Vector2(890, 506),
                new Color(0x94, 0xA3, 0xB8)), Times.Once);
        }

        private static DrawingStartupStage CreateStageWithTheme(params string[] themeLines)
        {
            var stage = new DrawingStartupStage(ReflectionHelpers.CreateGame());
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(themeLines));

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", stage.SpriteBatchStub);
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", stage.WhitePixelStub);
            ReflectionHelpers.SetPrivateField(stage, "_progressMessages", new List<string>());

            return stage;
        }

        private sealed record DrawCall(Rectangle Destination, Color Color);

        private sealed class DrawingStartupStage : StartupStage
        {
            public DrawingStartupStage(BaseGame game) : base(game)
            {
            }

            public SpriteBatch SpriteBatchStub { get; } = CreateGpuStub<SpriteBatch>();

            public Texture2D WhitePixelStub { get; } = CreateGpuStub<Texture2D>();

            public List<DrawCall> DrawCalls { get; } = new();

            protected override Viewport GetViewportCore() => new(0, 0, 1280, 720);

            protected override void DrawSolidRectCore(
                SpriteBatch spriteBatch,
                Texture2D texture,
                Rectangle destination,
                Color color)
            {
                DrawCalls.Add(new DrawCall(destination, color));
            }
        }
    }
}
