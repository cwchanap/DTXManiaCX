using System;
using System.Collections.Generic;
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
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class StartupStageAdditionalCoverageTests
    {
        [Fact]
        public void Dispose_WithRunningTask_ShouldCancelAndWait()
        {
            var tcs = new TaskCompletionSource<bool>();
            var stage = CreateStage(currentAsyncTask: tcs.Task);
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Inactive);

            InvokeDispose(stage, true);

            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
            Assert.Null(ReflectionHelpers.GetPrivateField<CancellationTokenSource>(stage, "_cancellationTokenSource"));
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
        [InlineData(StartupPhase.LoadScoreCache, true)]
        [InlineData(StartupPhase.LoadScoreFiles, true)]
        [InlineData(StartupPhase.EnumerateSongs, true)]
        [InlineData(StartupPhase.BuildSongLists, true)]
        [InlineData(StartupPhase.SaveSongsDB, true)]
        [InlineData(StartupPhase.Complete, false)]
        public void HasAsyncOperation_ForAsyncPhases_ShouldReturnCorrectValue(StartupPhase phase, bool expected)
        {
            var stage = CreateStage();

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(stage, "HasAsyncOperation", phase);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void OnUpdate_WhenCompletePhaseElapsed_ShouldTransitionToTitle()
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
        public void OnUpdate_WhenCompletePhaseNotElapsed_ShouldNotTransition()
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
        public void Dispose_WhenAsyncTaskFaults_ShouldSwallowExceptionAndReleaseState()
        {
            var stage = CreateStage(currentAsyncTask: Task.FromException(new InvalidOperationException("boom")));

            var exception = Record.Exception(() => InvokeDispose(stage, true));

            Assert.Null(exception);
            Assert.Null(ReflectionHelpers.GetPrivateField<Task>(stage, "_currentAsyncTask"));
            Assert.Null(ReflectionHelpers.GetPrivateField<CancellationTokenSource>(stage, "_cancellationTokenSource"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_disposed"));
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

        private static StartupStage CreateStage(
            StartupPhase phase = StartupPhase.SystemSounds,
            double elapsedTime = 0.0,
            double phaseStartTime = 0.0,
            Task? currentAsyncTask = null,
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
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", "");
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", phase);
            ReflectionHelpers.SetPrivateField(stage, "_songManager", null);
            ReflectionHelpers.SetPrivateField(stage, "_configManager", CreateConfigManager(new ConfigData()));
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
    }
}
