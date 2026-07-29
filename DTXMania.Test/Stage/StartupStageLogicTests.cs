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
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

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
        public void UpdateCurrentPhase_WhenAsyncTaskCompletedImmediately_ShouldAdvance()
        {
            var completedTask = Task.CompletedTask;
            var stage = CreateStage(
                phase: StartupPhase.SongListDB,
                elapsedTime: 0.1,
                phaseStartTime: 0.0,
                currentAsyncTask: completedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.SongsDB, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Complete", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskFaulted_ShouldAdvanceImmediately()
        {
            var faultedTask = Task.FromException(new InvalidOperationException("boom"));
            var stage = CreateStage(
                phase: StartupPhase.EnumerateSongs,
                elapsedTime: 0.7,
                phaseStartTime: 0.0,
                currentAsyncTask: faultedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.BuildSongLists, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Error", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskFaultedImmediately_ShouldAdvance()
        {
            var faultedTask = Task.FromException(new InvalidOperationException("boom"));
            var stage = CreateStage(
                phase: StartupPhase.EnumerateSongs,
                elapsedTime: 0.2,
                phaseStartTime: 0.0,
                currentAsyncTask: faultedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.BuildSongLists, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Contains("Error", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncTaskCanceledAfterMinimumDuration_ShouldAdvanceToNextPhase()
        {
            var canceledTask = Task.FromCanceled(new CancellationToken(canceled: true));
            var stage = CreateStage(
                phase: StartupPhase.EnumerateSongs,
                elapsedTime: 0.001,
                phaseStartTime: 0.0,
                currentAsyncTask: canceledTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.BuildSongLists, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Single(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
            Assert.Equal(0.001, ReflectionHelpers.GetPrivateField<double>(stage, "_phaseStartTime"));
        }

        [Fact]
        public void UpdateCurrentPhase_WhenAsyncPhaseHasNoTask_ShouldRemainInCurrentPhase()
        {
            var stage = CreateStage(
                phase: StartupPhase.EnumerateSongs,
                elapsedTime: 2.0,
                phaseStartTime: 0.0);
            // The kick-off for this phase has already run (otherwise UpdateCurrentPhase
            // would now perform it, however late); it just hasn't produced a task yet.
            ReflectionHelpers.SetPrivateField(stage, "_operationPerformedForPhase",
                (StartupPhase?)StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(StartupPhase.EnumerateSongs, ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
            Assert.Empty(ReflectionHelpers.GetPrivateField<List<string>>(stage, "_progressMessages")!);
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
            var config = new ConfigData { DTXPath = "LateKickoffSongs" };
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
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
        }

        [Fact]
        public void PerformPhaseOperationSync_WhenElapsedPastThreshold_ShouldStillRun()
        {
            var stage = CreateStage(configData: new ConfigData { DTXPath = "after" });
            ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "before" });

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.ConfigValidation, 0.2);

            Assert.Equal(new[] { "after" }, ReflectionHelpers.GetPrivateField<string[]>(stage, "_songPaths"));
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
        [InlineData(StartupPhase.EnumerateSongs)]
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

            ReflectionHelpers.InvokePrivateMethod(stage, "PerformPhaseOperationSync", StartupPhase.EnumerateSongs, 0.0);

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
        public void DatabaseTask_WhenSynchronous_ShouldRecordInvokeReturnTerminalAndObserved()
        {
            var (stage, trace, _) = CreateDiagnosticControlledStage();
            SetPhaseForUpdate(stage, StartupPhase.SongListDB);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecordedInOrder(
                trace,
                StartupCriticalPathMilestone.DatabaseInvoke,
                StartupCriticalPathMilestone.DatabaseTerminal,
                StartupCriticalPathMilestone.DatabaseTaskReturn,
                StartupCriticalPathMilestone.DatabaseObserved);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(
                trace,
                "_databaseTaskReturnedTerminal"));
        }

        [Fact]
        public async Task DatabaseTask_WhenDelayed_ShouldSeparateReturnTerminalAndObservation()
        {
            var (stage, trace, clock) = CreateDiagnosticControlledStage();
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextDatabaseTask = completion.Task;
            SetPhaseForUpdate(stage, StartupPhase.SongListDB);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecorded(
                trace,
                StartupCriticalPathMilestone.DatabaseInvoke,
                StartupCriticalPathMilestone.DatabaseTaskReturn);
            AssertNotRecorded(
                trace,
                StartupCriticalPathMilestone.DatabaseTerminal,
                StartupCriticalPathMilestone.DatabaseObserved);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(
                trace,
                "_databaseTaskReturnedTerminal"));

            var phaseTask = ReflectionHelpers.GetPrivateField<Task>(
                stage,
                "_currentAsyncTask")!;
            clock.Advance(10);
            completion.SetResult(true);
            await phaseTask.WaitAsync(TimeSpan.FromSeconds(2));

            AssertRecorded(trace, StartupCriticalPathMilestone.DatabaseTerminal);
            AssertNotRecorded(
                trace,
                StartupCriticalPathMilestone.DatabaseObserved);

            clock.Advance(10);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecordedInOrder(
                trace,
                StartupCriticalPathMilestone.DatabaseInvoke,
                StartupCriticalPathMilestone.DatabaseTaskReturn,
                StartupCriticalPathMilestone.DatabaseTerminal,
                StartupCriticalPathMilestone.DatabaseObserved);
        }

        [Fact]
        public async Task DatabaseTask_WhenCompletionWaitsFrames_ShouldRecordObservationLag()
        {
            var (stage, trace, clock) = CreateDiagnosticControlledStage();
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextDatabaseTask = completion.Task;
            SetPhaseForUpdate(stage, StartupPhase.SongListDB);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");
            var phaseTask = ReflectionHelpers.GetPrivateField<Task>(
                stage,
                "_currentAsyncTask")!;

            completion.SetResult(true);
            await phaseTask.WaitAsync(TimeSpan.FromSeconds(2));
            var terminal = Timestamp(
                trace,
                StartupCriticalPathMilestone.DatabaseTerminal);

            clock.Advance(16);
            clock.Advance(16);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            var observed = Timestamp(
                trace,
                StartupCriticalPathMilestone.DatabaseObserved);
            Assert.True(observed - terminal >= 32);
        }

        [Fact]
        public void DatabaseTask_WhenCoreReturnsFalse_ShouldFailDiagnosticOnly()
        {
            var (stage, trace, _) = CreateDiagnosticControlledStage();
            stage.NextDatabaseResult = false;
            SetPhaseForUpdate(stage, StartupPhase.SongListDB);

            var exception = Record.Exception(
                () => ReflectionHelpers.InvokePrivateMethod(
                    stage,
                    "UpdateCurrentPhase"));

            Assert.Null(exception);
            Assert.Equal(
                StartupPhase.SongsDB,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Contains(
                "outcome=failure error=database_initialization_failed",
                PublishTerminal(trace));
        }

        [Fact]
        public void DatabaseTask_WhenCoreThrows_ShouldRetainExistingStartupBehaviorAndFailTrace()
        {
            var (stage, trace, _) = CreateDiagnosticControlledStage();
            stage.DatabaseInitializationException =
                new IOException("database unavailable");
            SetPhaseForUpdate(stage, StartupPhase.SongListDB);

            var exception = Record.Exception(
                () => ReflectionHelpers.InvokePrivateMethod(
                    stage,
                    "UpdateCurrentPhase"));

            Assert.Null(exception);
            Assert.Equal(
                StartupPhase.SongsDB,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Contains(
                "outcome=failure error=database_initialization_failed",
                PublishTerminal(trace));
        }

        [Fact]
        public void EnumerationTask_WhenSynchronous_ShouldRecordAllFourMoments()
        {
            var (stage, trace, _) = CreateDiagnosticControlledStage();
            SetPhaseForUpdate(stage, StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecordedInOrder(
                trace,
                StartupCriticalPathMilestone.EnumerationInvoke,
                StartupCriticalPathMilestone.EnumerationTerminal,
                StartupCriticalPathMilestone.EnumerationTaskReturn,
                StartupCriticalPathMilestone.EnumerationObserved);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(
                trace,
                "_enumerationTaskReturnedTerminal"));
        }

        [Fact]
        public async Task EnumerationTask_WhenDelayed_ShouldSeparateReturnTerminalAndObserved()
        {
            var (stage, trace, clock) = CreateDiagnosticControlledStage();
            var completion =
                new TaskCompletionSource<SongEnumerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextEnumerationTask = completion.Task;
            SetPhaseForUpdate(stage, StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecorded(
                trace,
                StartupCriticalPathMilestone.EnumerationInvoke,
                StartupCriticalPathMilestone.EnumerationTaskReturn);
            AssertNotRecorded(
                trace,
                StartupCriticalPathMilestone.EnumerationTerminal,
                StartupCriticalPathMilestone.EnumerationObserved);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(
                trace,
                "_enumerationTaskReturnedTerminal"));

            var phaseTask = ReflectionHelpers.GetPrivateField<Task>(
                stage,
                "_currentAsyncTask")!;
            clock.Advance(10);
            completion.SetResult(CreateEnumerationResult());
            await phaseTask.WaitAsync(TimeSpan.FromSeconds(2));

            AssertRecorded(
                trace,
                StartupCriticalPathMilestone.EnumerationTerminal);
            AssertNotRecorded(
                trace,
                StartupCriticalPathMilestone.EnumerationObserved);

            clock.Advance(10);
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecordedInOrder(
                trace,
                StartupCriticalPathMilestone.EnumerationInvoke,
                StartupCriticalPathMilestone.EnumerationTaskReturn,
                StartupCriticalPathMilestone.EnumerationTerminal,
                StartupCriticalPathMilestone.EnumerationObserved);
        }

        [Fact]
        public void EnumerationTask_WhenFaulted_ShouldPublishFailureAfterCleanup()
        {
            var (stage, trace, _) = CreateDiagnosticControlledStage();
            stage.NextEnumerationTask =
                Task.FromException<SongEnumerationResult>(
                    new IOException("enumeration unavailable"));
            SetPhaseForUpdate(stage, StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecorded(
                trace,
                StartupCriticalPathMilestone.EnumerationTerminal);
            Assert.Contains(
                "outcome=failure error=enumeration_failure " +
                "last_milestone=EnumerationTerminal",
                PublishTerminal(trace));
        }

        [Fact]
        public void EnumerationTask_WhenCancelled_ShouldPublishCancellationAfterCleanup()
        {
            var (stage, trace, _) = CreateDiagnosticControlledStage();
            stage.NextEnumerationTask =
                Task.FromCanceled<SongEnumerationResult>(
                    new CancellationToken(canceled: true));
            SetPhaseForUpdate(stage, StartupPhase.EnumerateSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            AssertRecorded(
                trace,
                StartupCriticalPathMilestone.EnumerationTerminal);
            Assert.Contains(
                "outcome=cancellation error=enumeration_cancellation " +
                "last_milestone=EnumerationTerminal",
                PublishTerminal(trace));
        }

        [Fact]
        public void EnumerationTask_WhenActivationRetiresWhilePending_ShouldFailInvalidation()
        {
            var (stage, trace, _) =
                CreateDiagnosticLifecycleControlledStage();
            var completion =
                new TaskCompletionSource<SongEnumerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            stage.Activate();
            stage.NextEnumerationTask = completion.Task;
            _ = stage.StartSongLoadForTest();

            stage.Deactivate();

            Assert.True(stage.LastEnumerationToken.IsCancellationRequested);
            Assert.Contains(
                "outcome=failure error=activation_generation_invalidated",
                PublishTerminal(trace));
        }

        [Fact]
        public async Task EnumerationTask_WhenLateCompletionFollowsTerminal_ShouldNotMutateTrace()
        {
            var (stage, trace, _) =
                CreateDiagnosticLifecycleControlledStage();
            var completion =
                new TaskCompletionSource<SongEnumerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            stage.Activate();
            stage.NextEnumerationTask = completion.Task;
            var retiredTask = stage.StartSongLoadForTest();
            stage.Deactivate();
            var terminalError = ReflectionHelpers.GetPrivateField<string>(
                trace,
                "_terminalErrorRaw");

            completion.SetResult(CreateEnumerationResult());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await retiredTask);

            Assert.Equal(
                "activation_generation_invalidated",
                terminalError);
            Assert.Equal(
                terminalError,
                ReflectionHelpers.GetPrivateField<string>(
                    trace,
                    "_terminalErrorRaw"));
            AssertNotRecorded(
                trace,
                StartupCriticalPathMilestone.EnumerationTerminal);
            Assert.Contains(
                "outcome=failure error=activation_generation_invalidated",
                PublishTerminal(trace));
        }

        [Fact]
        public void OnDeactivate_WhenEnumerationAlreadyCompleted_ShouldNotInvalidateTrace()
        {
            var (stage, trace, _) =
                CreateDiagnosticLifecycleControlledStage();
            stage.Activate();
            _ = stage.StartSongLoadForTest();

            stage.Deactivate();

            Assert.Equal(
                "none",
                ReflectionHelpers.GetPrivateField<string>(
                    trace,
                    "_terminalErrorRaw"));
        }

        [Fact]
        public void OnUpdate_WhenCompleteAfterDraw_ShouldRecordSummaryRequestBeforeTitleChange()
        {
            var (game, timingTrace, trace, _) = CreateDiagnosticGame();
            timingTrace.MarkConfigLoaded();
            timingTrace.MarkLoadContentComplete();
            timingTrace.MarkStartupActivated();
            timingTrace.MarkStartupFirstDraw();
            var stageManager = new Mock<IStageManager>();
            ReflectionHelpers.SetPrivateField(
                game,
                "<StageManager>k__BackingField",
                stageManager.Object);
            var stage = new SummaryCapturingStartupStage(game);
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
                    Assert.Single(stage.StartupSummaries);
                    Assert.True(IsCompatibilityMilestoneRecorded(
                        timingTrace,
                        StartupTimingMilestone.SummaryAndTitleRequested));
                    AssertRecorded(
                        trace,
                        StartupCriticalPathMilestone.SummaryRequest);
                });

            stage.UpdateForTest(0.001);

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(
                        transition =>
                            transition is StartupToTitleTransition)),
                Times.Once);
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenEnumerationNeeded_ShouldUseProgressReporterAndCancellationToken()
        {
            var songPaths = new[] { "SongsRoot" };
            var stage = CreateControlledStage(songPaths: songPaths);
            stage.ReportedEnumerationProgress = new EnumerationProgress
            {
                CurrentFile = Path.Combine("SongsRoot", "test-song.dtx"),
                ProcessedCount = 3,
                DiscoveredSongs = 2
            };

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
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
        public async Task RunSongLoadAsync_WhenProgressReportsCurrentDirectory_ShouldShowDirectoryName()
        {
            var stage = CreateControlledStage(songPaths: new[] { "SongsRoot" });
            stage.ReportedEnumerationProgress = new EnumerationProgress
            {
                CurrentDirectory = Path.Combine("SongsRoot", "SubFolder"),
                ProcessedCount = 1,
                DiscoveredSongs = 0
            };
            using var synchronizationContextScope = new SynchronizationContextScope(new ImmediateSynchronizationContext());

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.Contains(
                "Scanning directory: SubFolder",
                ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenNeedsEnumerationThrows_ShouldFallbackAndPropagate()
        {
            var stage = new ThrowingNeedsEnumerationStartupStage();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await Assert.ThrowsAsync<IOException>(async () => await task);

            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Equal(
                StartupSongLoadOutcome.Failure,
                ReflectionHelpers.GetPrivateField<StartupSongLoadOutcome>(
                    stage,
                    "_songLoadOutcome"));
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenProgressOmitsFileAndDirectory_ShouldShowProcessedSummary()
        {
            var stage = new PartialProgressStartupStage();

            using var synchronizationContextScope = new SynchronizationContextScope(new ImmediateSynchronizationContext());

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            var message = ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage");
            Assert.Contains("1 processed", message);
            Assert.Contains("0 songs", message);
        }

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
        public void OnUpdate_WhenCompletePhaseTransitionIsEvaluatedAgain_ShouldWriteOneSuccessStartupSummary()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager.Object);
            var stage = new SummaryCapturingStartupStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.Complete);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 0.0);
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", 0.0);
            ReflectionHelpers.SetPrivateField(stage, "_hasRenderedStartupFrame", true);

            stage.UpdateForTest(0.2);
            stage.UpdateForTest(0.2);

            var summary = Assert.Single(stage.StartupSummaries);
            Assert.StartsWith("HPA192_STARTUP ", summary);
            Assert.Contains("outcome=success", summary);
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
        public void UpdateCurrentPhase_WhenAsyncTaskCompletesImmediately_ShouldAdvanceAtAnyElapsedTime()
        {
            var stage = CreateStage(
                phase: StartupPhase.SongListDB,
                elapsedTime: 0.001,
                phaseStartTime: 0.0,
                currentAsyncTask: Task.CompletedTask);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Equal(
                StartupPhase.SongsDB,
                ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
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
        public async Task RunSongLoadAsync_WhenEnumerationNeeded_ShouldNotBuildDatabaseHierarchy()
        {
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationResult = CreateEnumerationResult();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await task;

            Assert.Equal(1, stage.NeedsEnumerationCalls);
            Assert.Equal(1, stage.EnumerateSongsCalls);
            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.Equal(0, stage.SaveSongsDatabaseCalls);
            Assert.True(stage.LastForceEnumeration);
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenCacheValid_ShouldBuildDatabaseHierarchyOnce()
        {
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = false;
            stage.ForceEnumerationForTest = false;

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await task;

            Assert.Equal(1, stage.NeedsEnumerationCalls);
            Assert.Equal(0, stage.EnumerateSongsCalls);
            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Equal(0, stage.SaveSongsDatabaseCalls);
            Assert.False(stage.LastForceEnumeration);
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenZeroCandidatesPublish_ShouldPreservePublishedHierarchyIdentity()
        {
            var publishedHierarchy = new SongListNode { Title = "Published" };
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationResult = CreateEnumerationResult(
                rootNodes: new List<SongListNode> { publishedHierarchy });

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await task;
            ReflectionHelpers.InvokePrivateMethod(
                stage,
                "PerformPhaseOperationSync",
                StartupPhase.BuildSongLists,
                0.0);

            Assert.Same(publishedHierarchy, stage.PublishedHierarchy);
            Assert.Equal(0, stage.BuildHierarchyCalls);
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenEnumerationCanceled_ShouldNotRunCacheFallback()
        {
            var originalHierarchy = new SongListNode { Title = "Original" };
            var stage = CreateControlledStage();
            stage.PublishedHierarchy = originalHierarchy;
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromCanceled<SongEnumerationResult>(
                    new CancellationToken(canceled: true));

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await task);

            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.Same(originalHierarchy, stage.PublishedHierarchy);
            Assert.Equal(
                StartupSongLoadOutcome.Cancellation,
                ReflectionHelpers.GetPrivateField<StartupSongLoadOutcome>(
                    stage,
                    "_songLoadOutcome"));
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenEnumerationFails_ShouldFallbackOnceAndKeepOriginalFailure()
        {
            var originalFailure = new IOException("enumeration failed");
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromException<SongEnumerationResult>(originalFailure);

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            var thrown = await Assert.ThrowsAsync<IOException>(
                async () => await task);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_startupPhase",
                StartupPhase.EnumerateSongs);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_operationPerformedForPhase",
                (StartupPhase?)StartupPhase.EnumerateSongs);
            ReflectionHelpers.SetPrivateField(stage, "_currentAsyncTask", task);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Same(originalFailure, thrown);
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
        public async Task RunSongLoadAsync_WhenEnumerationReturnsNoResult_ShouldFallbackOnceAndFail()
        {
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromResult<SongEnumerationResult>(null!);

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await task);

            Assert.Equal(
                "Song enumeration completed without publishing a hierarchy.",
                thrown.Message);
            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Equal(
                StartupSongLoadOutcome.Failure,
                ReflectionHelpers.GetPrivateField<StartupSongLoadOutcome>(
                    stage,
                    "_songLoadOutcome"));
        }

        [Fact]
        public async Task RunSongLoadAsync_WhenFallbackFails_ShouldKeepOriginalErrorAndReachTerminalPhase()
        {
            var originalFailure = new IOException("original enumeration error");
            var stage = CreateControlledStage();
            stage.NextNeedsEnumerationResult = true;
            stage.NextEnumerationTask =
                Task.FromException<SongEnumerationResult>(originalFailure);
            stage.BuildHierarchyException =
                new InvalidOperationException("cache unavailable");

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            var thrown = await Assert.ThrowsAsync<IOException>(
                async () => await task);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_startupPhase",
                StartupPhase.EnumerateSongs);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_operationPerformedForPhase",
                (StartupPhase?)StartupPhase.EnumerateSongs);
            ReflectionHelpers.SetPrivateField(stage, "_currentAsyncTask", task);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

            Assert.Same(originalFailure, thrown);
            Assert.Equal(1, stage.BuildHierarchyCalls);
            Assert.Null(stage.PublishedHierarchy);
            Assert.Equal(StartupPhase.BuildSongLists,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Contains(
                "original enumeration error",
                ReflectionHelpers.GetPrivateField<string>(
                    stage,
                    "_songLoadError"));
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

            var oldTask = stage.StartSongLoadForTest();
            var oldToken = stage.LastEnumerationToken;
            stage.Deactivate();

            Assert.True(oldToken.IsCancellationRequested);

            stage.Activate();
            oldCompletion.SetResult(staleResult);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await oldTask);
            Assert.Equal(
                StartupSongLoadPath.Unknown,
                ReflectionHelpers.GetPrivateField<StartupSongLoadPath>(
                    stage,
                    "_selectedLoadPath"));
            Assert.Null(
                ReflectionHelpers.GetPrivateField<SongEnumerationResult>(
                    stage,
                    "_enumerationResult"));
            Assert.Equal(
                StartupSongLoadOutcome.Success,
                ReflectionHelpers.GetPrivateField<StartupSongLoadOutcome>(
                    stage,
                    "_songLoadOutcome"));
            Assert.Equal(
                StartupPhase.SystemSounds,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Empty(stage.StartupSummaries);
            Assert.Equal(0, stage.BuildHierarchyCalls);

            var freshResult = CreateEnumerationResult(
                discoveredCharts: 2,
                parsedCharts: 2,
                logicalGroups: 1);
            var freshCompletion =
                new TaskCompletionSource<SongEnumerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            stage.NextEnumerationTask = freshCompletion.Task;
            stage.NextEnumerationResult = freshResult;

            var freshTask = stage.StartSongLoadForTest();
            freshCompletion.SetResult(freshResult);
            await freshTask;
            stage.UpdateForTest(0.001);

            Assert.Equal(2, stage.EnumerateSongsCalls);
            Assert.Same(
                freshResult,
                ReflectionHelpers.GetPrivateField<SongEnumerationResult>(
                    stage,
                    "_enumerationResult"));
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

            var oldTask = stage.StartSongLoadForTest();
            stage.Deactivate();
            stage.Activate();
            oldCompletion.SetException(original);

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await oldTask);

            Assert.Same(original, actual);
            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.False(
                ReflectionHelpers.GetPrivateField<bool>(
                    stage,
                    "_cacheFallbackAttempted"));
            Assert.Equal(
                StartupSongLoadOutcome.Success,
                ReflectionHelpers.GetPrivateField<StartupSongLoadOutcome>(
                    stage,
                    "_songLoadOutcome"));
            Assert.Null(
                ReflectionHelpers.GetPrivateField<string>(
                    stage,
                    "_songLoadError"));
            Assert.Equal(
                StartupPhase.SystemSounds,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Empty(stage.StartupSummaries);
        }

        [Fact]
        public async Task OnDeactivate_WhenOldEnumerationCancels_ShouldNotMutateReactivation()
        {
            var stage = CreateLifecycleControlledStage();
            var oldCompletion = new TaskCompletionSource<SongEnumerationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage.Activate();
            stage.NextEnumerationTask = oldCompletion.Task;

            var oldTask = stage.StartSongLoadForTest();
            var oldToken = stage.LastEnumerationToken;
            stage.Deactivate();
            stage.Activate();
            oldCompletion.SetCanceled(oldToken);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await oldTask);
            Assert.Equal(
                StartupSongLoadOutcome.Success,
                ReflectionHelpers.GetPrivateField<StartupSongLoadOutcome>(
                    stage,
                    "_songLoadOutcome"));
            Assert.Null(
                ReflectionHelpers.GetPrivateField<string>(
                    stage,
                    "_songLoadError"));
            Assert.Equal(0, stage.BuildHierarchyCalls);
            Assert.False(
                ReflectionHelpers.GetPrivateField<bool>(
                    stage,
                    "_cacheFallbackAttempted"));
            Assert.Equal(
                StartupPhase.SystemSounds,
                ReflectionHelpers.GetPrivateField<StartupPhase>(
                    stage,
                    "_startupPhase"));
            Assert.Empty(stage.StartupSummaries);
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

                Assert.False(abandonedTask.IsAlive);
                Assert.Empty(unobservedFailures);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }

        [Fact]
        public void WriteSummaryOnce_WhenEnumerationCompletes_ShouldUseResultCountsAndDurations()
        {
            var stage = new SummaryCapturingStartupStage(
                ReflectionHelpers.CreateGame());
            ReflectionHelpers.SetPrivateField(
                stage,
                "_selectedLoadPath",
                StartupSongLoadPath.Enumeration);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_enumerationResult",
                CreateEnumerationResult(
                    discoveredCharts: 4,
                    parsedCharts: 3,
                    logicalGroups: 2,
                    persistenceDuration: TimeSpan.FromMilliseconds(33),
                    cleanupDuration: TimeSpan.FromMilliseconds(44),
                    hierarchyDuration: TimeSpan.FromMilliseconds(55)));
            ReflectionHelpers.SetPrivateField(
                stage,
                "_databaseInitializationDuration",
                TimeSpan.FromMilliseconds(11));

            ReflectionHelpers.InvokePrivateMethod(stage, "WriteSummaryOnce");

            var summary = Assert.Single(stage.StartupSummaries);
            Assert.Contains("path=enumeration", summary);
            Assert.Contains("db_init_ms=11", summary);
            Assert.Contains("discovery_parse_ms=22", summary);
            Assert.Contains("persistence_ms=33", summary);
            Assert.Contains("cleanup_ms=44", summary);
            Assert.Contains("hierarchy_ms=55", summary);
            Assert.Contains("discovered=4", summary);
            Assert.Contains("parsed=3", summary);
            Assert.Contains("groups=2", summary);
            Assert.Contains("added=1", summary);
            Assert.Contains("updated=1", summary);
            Assert.Contains("preserved=1", summary);
            Assert.Contains("skipped=1", summary);
            Assert.Contains("conflicts=1", summary);
            Assert.Contains("stale=1", summary);
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
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
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
            Assert.True(stage.SpriteBatchStub.IsDisposed);
            Assert.True(stage.WhitePixelStub.IsDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
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

            font.Verify(f => f.MeasureString("DTXManiaCX v1.0.0 - MonoGame Edition"), Times.Once);
            font.Verify(f => f.DrawString(stage.SpriteBatchStub, "DTXManiaCX v1.0.0 - MonoGame Edition", new Vector2(1070, 2), Color.White), Times.Once);
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
            ReflectionHelpers.SetPrivateField(stage, "_activationGate", new object());
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

        private static (
            ControlledStartupStage Stage,
            StartupCriticalPathTrace Trace,
            ManualMonotonicClock Clock)
            CreateDiagnosticControlledStage()
        {
            var (game, _, trace, clock) = CreateDiagnosticGame();
            return (
                new ControlledStartupStage(game),
                trace,
                clock);
        }

        private static (
            LifecycleControlledStartupStage Stage,
            StartupCriticalPathTrace Trace,
            ManualMonotonicClock Clock)
            CreateDiagnosticLifecycleControlledStage()
        {
            var (game, _, trace, clock) = CreateDiagnosticGame();
            return (
                new LifecycleControlledStartupStage(game),
                trace,
                clock);
        }

        private static (
            BaseGame Game,
            StartupTimingTrace TimingTrace,
            StartupCriticalPathTrace CriticalPathTrace,
            ManualMonotonicClock Clock)
            CreateDiagnosticGame()
        {
            var clock = new ManualMonotonicClock();
            var timingTrace = StartupTimingTrace.Start(
                clock,
                new FakeUtcMicrosecondClock(),
                enableCriticalPath: true);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(
                game,
                "_startupTimingTrace",
                timingTrace);
            ReflectionHelpers.SetPrivateField(
                game,
                "<ConfigManager>k__BackingField",
                CreateConfigManager(new ConfigData
                {
                    DTXPath = "Songs",
                    ScreenWidth = 1280,
                    ScreenHeight = 720
                }));
            return (
                game,
                timingTrace,
                timingTrace.CriticalPathTrace!,
                clock);
        }

        private static void SetPhaseForUpdate(
            StartupStage stage,
            StartupPhase phase)
        {
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", phase);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_operationPerformedForPhase",
                null);
            ReflectionHelpers.SetPrivateField(stage, "_currentAsyncTask", null);
        }

        private static void AssertRecorded(
            StartupCriticalPathTrace trace,
            params StartupCriticalPathMilestone[] milestones)
        {
            var recorded = ReflectionHelpers.GetPrivateField<bool[]>(
                trace,
                "_recorded")!;
            foreach (var milestone in milestones)
            {
                Assert.True(
                    recorded[(int)milestone],
                    $"Expected {milestone} to be recorded.");
            }
        }

        private static void AssertNotRecorded(
            StartupCriticalPathTrace trace,
            params StartupCriticalPathMilestone[] milestones)
        {
            var recorded = ReflectionHelpers.GetPrivateField<bool[]>(
                trace,
                "_recorded")!;
            foreach (var milestone in milestones)
            {
                Assert.False(
                    recorded[(int)milestone],
                    $"Expected {milestone} not to be recorded.");
            }
        }

        private static void AssertRecordedInOrder(
            StartupCriticalPathTrace trace,
            params StartupCriticalPathMilestone[] milestones)
        {
            AssertRecorded(trace, milestones);
            var previous = long.MinValue;
            foreach (var milestone in milestones)
            {
                var current = Timestamp(trace, milestone);
                Assert.True(
                    current > previous,
                    $"Expected {milestone} after the preceding milestone.");
                previous = current;
            }
        }

        private static long Timestamp(
            StartupCriticalPathTrace trace,
            StartupCriticalPathMilestone milestone)
        {
            var timestamps = ReflectionHelpers.GetPrivateField<long[]>(
                trace,
                "_timestamps")!;
            return timestamps[(int)milestone];
        }

        private static string PublishTerminal(
            StartupCriticalPathTrace trace)
        {
            using var writer = new StringWriter();
            Assert.True(trace.TryPublishTerminal(writer));
            return writer.ToString();
        }

        private static bool IsCompatibilityMilestoneRecorded(
            StartupTimingTrace trace,
            StartupTimingMilestone milestone)
        {
            var recorded = ReflectionHelpers.GetPrivateField<bool[]>(
                trace,
                "_recorded")!;
            return recorded[(int)milestone];
        }

        private static LifecycleControlledStartupStage
            CreateLifecycleControlledStage()
        {
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(
                game,
                "<ConfigManager>k__BackingField",
                CreateConfigManager(new ConfigData
                {
                    DTXPath = "Songs",
                    ScreenWidth = 1280,
                    ScreenHeight = 720
                }));
            return new LifecycleControlledStartupStage(game);
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
            var task = stage.StartSongLoadForTest();
            stage.Deactivate();
            stage.Activate();
            completion.SetException(
                new InvalidOperationException("abandoned failure"));

            while (!task.IsCompleted)
            {
                await Task.Yield();
            }

            return new WeakReference(task);
        }

        private static SongEnumerationResult CreateEnumerationResult(
            int discoveredCharts = 0,
            int parsedCharts = 0,
            int logicalGroups = 0,
            TimeSpan? persistenceDuration = null,
            TimeSpan? cleanupDuration = null,
            TimeSpan? hierarchyDuration = null,
            List<SongListNode>? rootNodes = null)
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
                ActiveRoots = new[] { "/songs" },
                DiscoveredChartPaths = new HashSet<string>(
                    paths,
                    SongPathIdentity.CanonicalComparer),
                Candidates = candidates,
                RootNodes = rootNodes ?? new List<SongListNode>(),
                PendingSongs = pendingSongs,
                Errors = new List<SongEnumerationError>(),
                DiscoveryAndParsingDuration = TimeSpan.FromMilliseconds(22),
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
                StaleSongs: mutationCount,
                persistenceDuration ?? TimeSpan.Zero,
                cleanupDuration ?? TimeSpan.Zero);
            return new SongEnumerationResult(
                batch,
                import,
                hierarchyDuration ?? TimeSpan.Zero);
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

            public int BuildHierarchyCalls { get; private set; }

            public int SaveSongsDatabaseCalls { get; private set; }

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

            private protected override Task<bool>
                InitializeDatabaseServiceCoreAsync(
                    string databasePath,
                    IStartupSongLoadTimingObserver? observer)
            {
                return InitializeDatabaseServiceCoreAsync(databasePath);
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
                var result = NextEnumerationTask == null
                    ? NextEnumerationResult
                    : null;
                if (result?.Batch.RootNodes.Count > 0)
                {
                    PublishedHierarchy = result.Batch.RootNodes[0];
                }

                return NextEnumerationTask ??
                    Task.FromResult(NextEnumerationResult);
            }

            private protected override Task<SongEnumerationResult>
                EnumerateSongsCoreAsync(
                    string[] songPaths,
                    IProgress<EnumerationProgress> progressReporter,
                    CancellationToken cancellationToken,
                    IStartupSongLoadTimingObserver? observer)
            {
                return EnumerateSongsWithObserverForTestAsync(
                    songPaths,
                    progressReporter,
                    cancellationToken,
                    observer);
            }

            private async Task<SongEnumerationResult>
                EnumerateSongsWithObserverForTestAsync(
                    string[] songPaths,
                    IProgress<EnumerationProgress> progressReporter,
                    CancellationToken cancellationToken,
                    IStartupSongLoadTimingObserver? observer)
            {
                SongEnumerationResult? result = null;
                var outcome = StartupOperationOutcome.Failure;
                try
                {
                    result = await EnumerateSongsCoreAsync(
                            songPaths,
                            progressReporter,
                            cancellationToken)
                        .ConfigureAwait(false);
                    outcome = StartupOperationOutcome.Success;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    outcome = StartupOperationOutcome.Cancellation;
                    throw;
                }
                finally
                {
                    observer.TryRecordEnumerationTerminal(
                        result,
                        outcome);
                }
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
                return Task.CompletedTask;
            }

            protected override void MarkSongManagerInitialized()
            {
                MarkSongManagerInitializedCalled = true;
            }
        }

        private sealed class LifecycleControlledStartupStage :
            ControlledStartupStage
        {
            public LifecycleControlledStartupStage(BaseGame game) : base(game)
            {
            }

            public GraphicsDevice GraphicsDeviceStub { get; } =
                (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(
                    typeof(GraphicsDevice));

            public SpriteBatch SpriteBatchStub { get; } =
                (SpriteBatch)RuntimeHelpers.GetUninitializedObject(
                    typeof(SpriteBatch));

            public Texture2D WhitePixelStub { get; } =
                (Texture2D)RuntimeHelpers.GetUninitializedObject(
                    typeof(Texture2D));

            public List<string> StartupSummaries { get; } = new();

            public Task StartSongLoadForTest()
            {
                ReflectionHelpers.SetPrivateField(
                    this,
                    "_startupPhase",
                    StartupPhase.EnumerateSongs);
                ReflectionHelpers.SetPrivateField(
                    this,
                    "_operationPerformedForPhase",
                    null);
                OnUpdate(0.001);
                return ReflectionHelpers.GetPrivateField<Task>(
                    this,
                    "_currentAsyncTask");
            }

            public void UpdateForTest(double deltaTime)
            {
                OnUpdate(deltaTime);
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

            protected override Texture2D CreateWhitePixelCore(
                GraphicsDevice graphicsDevice)
            {
                return WhitePixelStub;
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

            protected override void WriteStartupSummary(string line)
            {
                StartupSummaries.Add(line);
            }
        }

        private sealed class SummaryCapturingStartupStage : StartupStage
        {
            public SummaryCapturingStartupStage(BaseGame game) : base(game)
            {
            }

            public List<string> StartupSummaries { get; } = new();

            public void UpdateForTest(double deltaTime)
            {
                OnUpdate(deltaTime);
            }

            protected override void WriteStartupSummary(string line)
            {
                StartupSummaries.Add(line);
            }
        }

        private sealed class ManualMonotonicClock : IMonotonicClock
        {
            private long _timestamp;

            public long TimestampFrequency => TimeSpan.TicksPerSecond;

            public long GetTimestamp() =>
                Interlocked.Increment(ref _timestamp);

            public void Advance(long ticks) =>
                Interlocked.Add(ref _timestamp, ticks);
        }

        private sealed class FakeUtcMicrosecondClock :
            IUtcMicrosecondClock
        {
            public long GetUnixMicroseconds() => 1_000_000;
        }

        private sealed record DrawCall(Rectangle Destination, Color Color);

        private sealed class SynchronizationContextScope : IDisposable
        {
            private readonly SynchronizationContext? _previousContext;

            public SynchronizationContextScope(SynchronizationContext? synchronizationContext)
            {
                _previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            }

            public void Dispose()
            {
                SynchronizationContext.SetSynchronizationContext(_previousContext);
            }
        }

        private sealed class ImmediateSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                d(state);
            }
        }

        private class GraphicsControlledStartupStage : StartupStage
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

            protected override Texture2D CreateWhitePixelCore(GraphicsDevice graphicsDevice)
            {
                return WhitePixelStub;
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

        private sealed class PartialProgressStartupStage : ControlledStartupStage
        {
            public PartialProgressStartupStage() : base(ReflectionHelpers.CreateGame())
            {
            }

            private int _callCount;

            protected override Task<SongEnumerationResult>
                EnumerateSongsCoreAsync(
                    string[] songPaths,
                    IProgress<EnumerationProgress> progress,
                    CancellationToken cancellationToken)
            {
                _callCount++;
                if (_callCount == 1)
                {
                    progress.Report(new EnumerationProgress { ProcessedCount = 1, DiscoveredSongs = 0 });
                }
                else if (_callCount == 2)
                {
                    progress.Report(new EnumerationProgress { CurrentFile = "song2.dtx", ProcessedCount = 2, DiscoveredSongs = 1 });
                }

                return Task.FromResult(CreateEnumerationResult(
                    discoveredCharts: 1,
                    parsedCharts: 1,
                    logicalGroups: 1));
            }
        }
    }

    [Collection("ConsoleOut")]
    public class StartupStageConsoleOutputTests
    {
        [Fact]
        public void OnUpdate_WhenCompletePhaseTransitionIsEvaluatedAgain_ShouldWriteOneRawSuccessStartupSummary()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager.Object);
            var stage = new DefaultOutputStartupStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.Complete);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 0.0);
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", 0.0);
            ReflectionHelpers.SetPrivateField(stage, "_hasRenderedStartupFrame", true);

            using var writer = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(writer);
            try
            {
                stage.UpdateForTest(0.2);
                stage.UpdateForTest(0.2);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            var summary = Assert.Single(lines);
            Assert.StartsWith("HPA192_STARTUP ", summary);
            Assert.Contains("outcome=success", summary);
            stageManager.Verify(manager => manager.ChangeStage(
                StageType.Title,
                It.Is<IStageTransition>(transition => transition is StartupToTitleTransition)),
                Times.Once);
        }

        [Theory]
        [InlineData(true, "cancellation")]
        [InlineData(false, "failure")]
        public async Task OnUpdate_WhenSongLoadDoesNotPublish_ShouldWriteOneRawOutcomeSummary(
            bool cancel,
            string expectedOutcome)
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetPrivateField(
                game,
                "<StageManager>k__BackingField",
                stageManager.Object);
            var stage = new DefaultOutputOutcomeStartupStage(game, cancel);
            var loadTask = (Task)ReflectionHelpers.InvokePrivateMethod(
                stage,
                "RunSongLoadAsync")!;
            await Assert.ThrowsAnyAsync<Exception>(
                async () => await loadTask);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_startupPhase",
                StartupPhase.Complete);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_hasRenderedStartupFrame",
                true);

            using var writer = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(writer);
            try
            {
                stage.UpdateForTest(0.001);
                stage.UpdateForTest(0.001);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            var summary = Assert.Single(
                writer.ToString().Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries));
            Assert.StartsWith("HPA192_STARTUP ", summary);
            Assert.Contains($"outcome={expectedOutcome}", summary);
            Assert.Equal(cancel ? 0 : 1, stage.BuildHierarchyCalls);
            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(
                        transition =>
                            transition is StartupToTitleTransition)),
                Times.Once);
        }

        private sealed class DefaultOutputStartupStage : StartupStage
        {
            public DefaultOutputStartupStage(BaseGame game) : base(game)
            {
            }

            public void UpdateForTest(double deltaTime)
            {
                OnUpdate(deltaTime);
            }
        }

        private sealed class DefaultOutputOutcomeStartupStage : StartupStage
        {
            private readonly bool _cancel;

            public DefaultOutputOutcomeStartupStage(
                BaseGame game,
                bool cancel) : base(game)
            {
                _cancel = cancel;
            }

            public int BuildHierarchyCalls { get; private set; }

            public void UpdateForTest(double deltaTime)
            {
                OnUpdate(deltaTime);
            }

            protected override Task<bool> NeedsEnumerationCoreAsync(
                string[] songPaths,
                bool forceEnumeration) =>
                Task.FromResult(true);

            protected override Task<SongEnumerationResult>
                EnumerateSongsCoreAsync(
                    string[] songPaths,
                    IProgress<EnumerationProgress> progressReporter,
                    CancellationToken cancellationToken) =>
                _cancel
                    ? Task.FromCanceled<SongEnumerationResult>(
                        new CancellationToken(canceled: true))
                    : Task.FromException<SongEnumerationResult>(
                        new IOException("enumeration failed"));

            protected override Task
                BuildHierarchyFromDatabaseOnceCoreAsync(
                    string[] songPaths)
            {
                BuildHierarchyCalls++;
                return Task.CompletedTask;
            }
        }
    }

    [CollectionDefinition("ConsoleOut", DisableParallelization = true)]
    public sealed class ConsoleOutCollectionDefinition
    {
    }
}
