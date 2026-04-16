using System;
using System.IO;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
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
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 38, HasDrumChart = true }));
            ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 0);

            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(38f, result);
        }

        [Theory]
        [InlineData(55f, true, 55, false, 0)]
        [InlineData(25f, false, 0, true, 25)]
        public void GetCurrentDifficultyLevel_WhenDrumLevelMissing_ShouldFallbackToAvailableNonDrumLevel(
            float expectedLevel,
            bool hasGuitarChart,
            int guitarLevel,
            bool hasBassChart,
            int bassLevel)
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart
            {
                DrumLevel = 0,
                HasDrumChart = false,
                GuitarLevel = guitarLevel,
                HasGuitarChart = hasGuitarChart,
                BassLevel = bassLevel,
                HasBassChart = hasBassChart,
            }));

            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(expectedLevel, result);
        }

        [Fact]
        public void UpdatePhase_WhenFadeInDurationElapsed_ShouldEnterNormalPhase()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.FadeIn);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", SongTransitionUILayout.Timing.FadeInDuration);

            InvokePrivateMethod(stage, "UpdatePhase");

            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
        }

        [Fact]
        public void UpdatePhase_WhenFadeOutDurationElapsed_ShouldPerformTransition()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.FadeOut);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", SongTransitionUILayout.Timing.FadeOutDuration);
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 65, HasDrumChart = true }));
            ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 2);
            ReflectionHelpers.SetPrivateField(stage, "_songId", 123);

            InvokePrivateMethod(stage, "UpdatePhase");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Performance,
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    It.Is<Dictionary<string, object>>(sharedData =>
                        (SongListNode)sharedData["selectedSong"] == ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong") &&
                        (int)sharedData["selectedDifficulty"] == 2 &&
                        (int)sharedData["songId"] == 123)),
                Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_WhenActivateAndTransitionAllowed_ShouldTransitionToPerformance()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 50, HasDrumChart = true }));

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Performance,
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Once);
            Assert.Equal(2.0, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenBackAndTransitionAllowed_ShouldReturnToSongSelect()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame(totalGameTime: 1.5, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Back, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is InstantTransition)),
                Times.Once);
            Assert.Equal(1.5, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenDebounceBlocksTransition_ShouldDoNothing()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
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
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", selectedSong);
            ReflectionHelpers.SetPrivateField(stage, "_selectedDifficulty", 1);
            ReflectionHelpers.SetPrivateField(stage, "_songId", 77);
            ReflectionHelpers.SetPrivateField(stage, "_parsedChart", parsedChart);
            ReflectionHelpers.SetPrivateField(stage, "_chartLoaded", true);

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
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.FadeOut);

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

            ReflectionHelpers.SetPrivateField(stage, "_uiManager", uiManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_backgroundTexture", backgroundTexture.Object);
            ReflectionHelpers.SetPrivateField(stage, "_previewTexture", previewTexture.Object);
            ReflectionHelpers.SetPrivateField(stage, "_titleFont", titleFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_artistFont", artistFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_nowLoadingSound", nowLoadingSound.Object);
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);

            stage.Deactivate();

            backgroundTexture.Verify(x => x.RemoveReference(), Times.Once);
            previewTexture.Verify(x => x.RemoveReference(), Times.Once);
            titleFont.Verify(x => x.RemoveReference(), Times.Once);
            artistFont.Verify(x => x.RemoveReference(), Times.Once);
            nowLoadingSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.True(inputManager.WasDisposed);
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_uiManager"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_backgroundTexture"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_previewTexture"));
        }

        [Fact]
        public void Activate_WhenSharedDataProvided_ShouldCaptureSelectionBeforeGraphicsInitialization()
        {
            var resourceManager = new Mock<IResourceManager>();
            var game = ReflectionHelpers.CreateGame();
            var selectedSong = CreateSongNode(new SongChart { DrumLevel = 42, HasDrumChart = true });
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);
            var stage = CreateStage(game);
            var sharedData = new Dictionary<string, object>
            {
                ["selectedSong"] = selectedSong,
                ["selectedDifficulty"] = 3,
                ["songId"] = 99
            };

            Record.Exception(() => stage.Activate(sharedData));

            Assert.Same(selectedSong, ReflectionHelpers.GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Equal(3, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedDifficulty"));
            Assert.Equal(99, ReflectionHelpers.GetPrivateField<int>(stage, "_songId"));
        }

        [Fact]
        public void LoadBackground_WhenReloadFails_ShouldReleaseExistingTextureAndFallbackToNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var existingBackground = new Mock<ITexture>();
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_backgroundTexture", existingBackground.Object);
            resourceManager
                .Setup(x => x.LoadTexture(SongTransitionUILayout.Background.DefaultBackgroundPath))
                .Throws(new InvalidOperationException("missing background"));

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadBackground");

            existingBackground.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_backgroundTexture"));
        }

        [Fact]
        public void LoadSound_WhenPrimarySoundFails_ShouldFallbackToDecideSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var fallbackSound = new Mock<ISound>();
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            resourceManager
                .Setup(x => x.LoadSound("Sounds/Now loading.ogg"))
                .Throws(new InvalidOperationException("missing now loading"));
            resourceManager
                .Setup(x => x.LoadSound("Sounds/Decide.ogg"))
                .Returns(fallbackSound.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadSound");

            Assert.Same(fallbackSound.Object, ReflectionHelpers.GetPrivateField<ISound>(stage, "_nowLoadingSound"));
        }

        [Fact]
        public void OnUpdate_WhenAutoTransitionDelayElapsed_ShouldTransitionToPerformance()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame(totalGameTime: 3.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);

            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 55, HasDrumChart = true }));
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", SongTransitionUILayout.Timing.AutoTransitionDelay);

            InvokePrivateMethod(stage, "OnUpdate", 0.0);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Performance,
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Once);
            Assert.Equal(3.0, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void LoadFonts_WhenReloadFails_ShouldReleaseExistingFontsAndLeaveFieldsNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var titleFont = new Mock<IFont>();
            var artistFont = new Mock<IFont>();

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_titleFont", titleFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_artistFont", artistFont.Object);

            resourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", SongTransitionUILayout.SongTitle.FontSize))
                .Throws(new InvalidOperationException("missing title font"));
            resourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", SongTransitionUILayout.Artist.FontSize))
                .Throws(new InvalidOperationException("missing artist font"));

            InvokePrivateMethod(stage, "LoadFonts");

            titleFont.Verify(x => x.RemoveReference(), Times.Once);
            artistFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_titleFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_artistFont"));
        }

        [Fact]
        public void LoadFonts_WhenReloadSucceeds_ShouldReplaceExistingFonts()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var oldTitleFont = new Mock<IFont>();
            var oldArtistFont = new Mock<IFont>();
            var newTitleFont = new Mock<IFont>();
            var newArtistFont = new Mock<IFont>();

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_titleFont", oldTitleFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_artistFont", oldArtistFont.Object);

            resourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", SongTransitionUILayout.SongTitle.FontSize))
                .Returns(newTitleFont.Object);
            resourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", SongTransitionUILayout.Artist.FontSize))
                .Returns(newArtistFont.Object);

            InvokePrivateMethod(stage, "LoadFonts");

            oldTitleFont.Verify(x => x.RemoveReference(), Times.Once);
            oldArtistFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(newTitleFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_titleFont"));
            Assert.Same(newArtistFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_artistFont"));
        }

        [Fact]
        public void LoadPreviewImage_WhenPrimaryPreviewExists_ShouldLoadAbsolutePreviewPath()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previewTexture = new Mock<ITexture>();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var chartPath = Path.Combine(tempDir, "sample.dtx");
                var previewFileName = "preview.png";
                var previewPath = Path.Combine(tempDir, previewFileName);
                File.WriteAllText(chartPath, "chart");
                File.WriteAllText(previewPath, "preview");

                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
                ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart
                {
                    FilePath = chartPath,
                    PreviewImage = previewFileName
                }));
                resourceManager
                    .Setup(x => x.LoadTexture(Path.GetFullPath(previewPath)))
                    .Returns(previewTexture.Object);

                InvokePrivateMethod(stage, "LoadPreviewImage");

                resourceManager.Verify(x => x.LoadTexture(Path.GetFullPath(previewPath)), Times.Once);
                Assert.Same(previewTexture.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_previewTexture"));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void LoadPreviewImage_WhenSelectedSongMissing_ShouldLoadDefaultPreview()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var defaultPreview = new Mock<ITexture>();

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", null);
            resourceManager
                .Setup(x => x.LoadTexture("Graphics/5_preimage default.png"))
                .Returns(defaultPreview.Object);

            InvokePrivateMethod(stage, "LoadPreviewImage");

            Assert.Same(defaultPreview.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_previewTexture"));
        }

        [Fact]
        public void GetDifficultyName_WhenDifficultyOutsideKnownRange_ShouldReturnUnknown()
        {
            var stage = CreateStage();

            var result = InvokePrivateMethod<string>(stage, "GetDifficultyName", 9);

            Assert.Equal("Unknown", result);
        }

        [Theory]
        [InlineData(0, "Basic")]
        [InlineData(1, "Advanced")]
        [InlineData(2, "Extreme")]
        [InlineData(3, "Master")]
        [InlineData(4, "Ultimate")]
        public void GetDifficultyName_WhenDifficultyInKnownRange_ShouldReturnExpectedLabel(int difficulty, string expected)
        {
            var stage = CreateStage();

            var result = InvokePrivateMethod<string>(stage, "GetDifficultyName", difficulty);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void LoadPreviewImage_WhenPrimaryPreviewMissingButFallbackExists_ShouldReleaseOldAndLoadFallback()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var existingPreview = new Mock<ITexture>();
            var fallbackTexture = new Mock<ITexture>();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var chartPath = Path.Combine(tempDir, "sample.dtx");
                var previewFileName = "missing-preview.png";
                var fallbackPreviewPath = Path.Combine(tempDir, "preview.jpg");
                File.WriteAllText(chartPath, "chart");
                File.WriteAllText(fallbackPreviewPath, "fallback");

                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
                ReflectionHelpers.SetPrivateField(stage, "_previewTexture", existingPreview.Object);
                ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart
                {
                    FilePath = chartPath,
                    PreviewImage = previewFileName
                }));

                resourceManager
                    .Setup(x => x.LoadTexture(fallbackPreviewPath))
                    .Returns(fallbackTexture.Object);

                InvokePrivateMethod(stage, "LoadPreviewImage");

                existingPreview.Verify(x => x.RemoveReference(), Times.Once);
                resourceManager.Verify(x => x.LoadTexture(fallbackPreviewPath), Times.Once);
                Assert.Same(fallbackTexture.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_previewTexture"));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void LoadPreviewImage_WhenPrimaryLoadThrows_ShouldReleaseOldAndLeavePreviewNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var existingPreview = new Mock<ITexture>();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var chartPath = Path.Combine(tempDir, "sample.dtx");
                var previewFileName = "badpreview.png";
                var primaryPreviewPath = Path.Combine(tempDir, previewFileName);
                File.WriteAllText(chartPath, "chart");
                File.WriteAllText(primaryPreviewPath, "primary");

                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
                ReflectionHelpers.SetPrivateField(stage, "_previewTexture", existingPreview.Object);
                ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart
                {
                    FilePath = chartPath,
                    PreviewImage = previewFileName
                }));

                resourceManager
                    .Setup(x => x.LoadTexture(Path.GetFullPath(primaryPreviewPath)))
                    .Throws(new InvalidOperationException("primary failed"));

                InvokePrivateMethod(stage, "LoadPreviewImage");

                existingPreview.Verify(x => x.RemoveReference(), Times.Once);
                Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_previewTexture"));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void LoadPreviewImage_WhenDirectoryPathContainsFallback_ShouldLoadFallbackTexture()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var fallbackTexture = new Mock<ITexture>();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var fallbackPreviewPath = Path.Combine(tempDir, "jacket.png");
                File.WriteAllText(fallbackPreviewPath, "fallback");

                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
                ReflectionHelpers.SetPrivateField(stage, "_selectedSong", new SongListNode
                {
                    DirectoryPath = tempDir,
                    DatabaseChart = new SongChart { PreviewImage = null },
                    DatabaseSong = new SongEntity { Charts = new List<SongChart>() },
                });

                resourceManager
                    .Setup(x => x.LoadTexture(fallbackPreviewPath))
                    .Returns(fallbackTexture.Object);

                InvokePrivateMethod(stage, "LoadPreviewImage");

                resourceManager.Verify(x => x.LoadTexture(fallbackPreviewPath), Times.Once);
                Assert.Same(fallbackTexture.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_previewTexture"));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void LoadPreviewImage_WhenAllFallbacksFail_ShouldAttemptDefaultPreview()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var defaultPreview = new Mock<ITexture>();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var chartPath = Path.Combine(tempDir, "sample.dtx");
                File.WriteAllText(chartPath, "chart");

                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
                ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart
                {
                    FilePath = chartPath,
                    PreviewImage = null
                }));

                resourceManager
                    .Setup(x => x.LoadTexture("Graphics/5_preimage default.png"))
                    .Returns(defaultPreview.Object);

                InvokePrivateMethod(stage, "LoadPreviewImage");

                Assert.Same(defaultPreview.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_previewTexture"));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void LoadPreviewImage_WhenDefaultPreviewFails_ShouldLeavePreviewNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var chartPath = Path.Combine(tempDir, "sample.dtx");
                File.WriteAllText(chartPath, "chart");

                ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
                ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart
                {
                    FilePath = chartPath,
                    PreviewImage = null
                }));

                resourceManager
                    .Setup(x => x.LoadTexture("Graphics/5_preimage default.png"))
                    .Throws(new InvalidOperationException("default failed"));

                InvokePrivateMethod(stage, "LoadPreviewImage");

                Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(stage, "_previewTexture"));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void LoadSound_WhenReloadWithExistingSound_ShouldReleaseOldBeforeLoadingNew()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var existingSound = new Mock<ISound>();
            var newSound = new Mock<ISound>();

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_nowLoadingSound", existingSound.Object);
            resourceManager
                .Setup(x => x.LoadSound("Sounds/Now loading.ogg"))
                .Returns(newSound.Object);

            InvokePrivateMethod(stage, "LoadSound");

            existingSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(newSound.Object, ReflectionHelpers.GetPrivateField<ISound>(stage, "_nowLoadingSound"));
        }

        [Fact]
        public void LoadSound_WhenBothPrimaryAndFallbackFail_ShouldLeaveNowLoadingSoundNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            resourceManager
                .Setup(x => x.LoadSound("Sounds/Now loading.ogg"))
                .Throws(new InvalidOperationException("primary failed"));
            resourceManager
                .Setup(x => x.LoadSound("Sounds/Decide.ogg"))
                .Throws(new InvalidOperationException("fallback failed"));

            InvokePrivateMethod(stage, "LoadSound");

            Assert.Null(ReflectionHelpers.GetPrivateField<ISound>(stage, "_nowLoadingSound"));
        }

        [Fact]
        public void PlayNowLoadingSound_WhenSoundIsNull_ShouldReturnEarly()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_nowLoadingSound", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "PlayNowLoadingSound"));

            Assert.Null(exception);
        }

        [Fact]
        public void PlayNowLoadingSound_WhenPlayThrows_ShouldSwallowException()
        {
            var stage = CreateStage();
            var sound = new Mock<ISound>();
            sound.Setup(x => x.Play(0.9f)).Throws(new InvalidOperationException("play failed"));
            ReflectionHelpers.SetPrivateField(stage, "_nowLoadingSound", sound.Object);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "PlayNowLoadingSound"));

            Assert.Null(exception);
        }

        [Fact]
        public void PlayNowLoadingSound_WhenSoundExists_ShouldPlayConfiguredVolume()
        {
            var stage = CreateStage();
            var sound = new Mock<ISound>();
            ReflectionHelpers.SetPrivateField(stage, "_nowLoadingSound", sound.Object);

            InvokePrivateMethod(stage, "PlayNowLoadingSound");

            sound.Verify(x => x.Play(0.9f), Times.Once);
        }

        [Fact]
        public void LoadBackground_WhenLoadSucceeds_ShouldReleaseExistingTextureAndStoreNewBackground()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var oldBackground = new Mock<ITexture>();
            var newBackground = new Mock<ITexture>();

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_backgroundTexture", oldBackground.Object);
            resourceManager
                .Setup(x => x.LoadTexture(SongTransitionUILayout.Background.DefaultBackgroundPath))
                .Returns(newBackground.Object);

            InvokePrivateMethod(stage, "LoadBackground");

            oldBackground.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(newBackground.Object, ReflectionHelpers.GetPrivateField<ITexture>(stage, "_backgroundTexture"));
        }

        [Fact]
        public void LoadDifficultySprite_WhenBaseTextureHasNoBackingTexture_ShouldLeaveDifficultySpriteNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var baseTexture = new Mock<ITexture>();
            baseTexture.SetupGet(x => x.Texture).Returns((Texture2D?)null);

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            resourceManager
                .Setup(x => x.LoadTexture(TexturePath.DifficultySprite))
                .Returns(baseTexture.Object);

            InvokePrivateMethod(stage, "LoadDifficultySprite");

            Assert.Null(ReflectionHelpers.GetPrivateField<ManagedSpriteTexture>(stage, "_difficultySprite"));
        }

        [Fact]
        public void HandleInput_WhenInputManagerIsNull_ShouldReturnEarly()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleInput"));

            Assert.Null(exception);
        }

        [Fact]
        public void HandleInput_WhenQueuedActivateCommandPresent_ShouldDrainQueueAndTransition()
        {
            var stageManager = new Mock<IStageManager>();
            var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var inputManager = new ProbeInputManager();

            stage.StageManager = stageManager.Object;
            inputManager.QueueCommand(new InputCommand(InputCommandType.Activate, 0.0));
            ReflectionHelpers.SetPrivateField(stage, "_inputManager", inputManager);
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", CreateSongNode(new SongChart { DrumLevel = 50, HasDrumChart = true }));

            InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Performance,
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Once);
            Assert.False(inputManager.HasPendingCommands);
        }

        [Fact]
        public void GetCurrentDifficultyLevel_WhenSelectedSongIsNull_ShouldReturnZero()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", null);

            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(0f, result);
        }

        [Fact]
        public void GetCurrentDifficultyLevel_WhenSongHasNoDatabaseSong_ShouldReturnZero()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_selectedSong", new SongListNode
            {
                DatabaseSong = null,
                DatabaseChart = new SongChart(),
            });

            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(0f, result);
        }

        [Fact]
        public void CreateConfiguredInputManager_WhenConfigManagerIsNotConcrete_ShouldReturnDefaultInputManager()
        {
            var game = ReflectionHelpers.CreateGame();
            var mockConfigManager = new Mock<IConfigManager>();
            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", mockConfigManager.Object);
            var stage = CreateStage(game);

            var result = InvokePrivateMethod<InputManager>(stage, "CreateConfiguredInputManager");

            Assert.NotNull(result);
            Assert.IsType<InputManager>(result);
        }

        [Fact]
        public void LoadDifficultySprite_WhenBaseTextureMissing_ShouldLeaveDifficultySpriteNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            resourceManager
                .Setup(x => x.LoadTexture(TexturePath.DifficultySprite))
                .Returns((ITexture?)null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "LoadDifficultySprite"));

            Assert.Null(exception);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(stage, "_difficultySprite"));
        }

        [Fact]
        public void OnDraw_WhenSpriteBatchIsNull_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnDraw", 0.0));

            Assert.Null(exception);
        }

        [Fact]
        public void DrawText_WhenFontsMissing_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_titleFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_artistFont", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawText"));

            Assert.Null(exception);
        }

        [Fact]
        public void DrawDifficultyBackground_WhenWhitePixelMissing_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawDifficultyBackground"));

            Assert.Null(exception);
        }

        [Fact]
        public void DrawDifficultySprite_WhenDifficultySpriteMissing_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_difficultySprite", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawDifficultySprite"));

            Assert.Null(exception);
        }

        [Fact]
        public void DrawDifficultyLevelNumber_WhenLevelNumberFontMissing_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_levelNumberFont", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawDifficultyLevelNumber"));

            Assert.Null(exception);
        }

        [Fact]
        public void DrawPreviewImage_WhenPreviewTextureMissing_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_previewTexture", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawPreviewImage"));

            Assert.Null(exception);
        }

        private static SongTransitionStage CreateStage(BaseGame? game = null)
        {
            game ??= ReflectionHelpers.CreateGame();
            return new SongTransitionStage(game);
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

        private sealed class ProbeInputManager : DTXMania.Game.Lib.Input.InputManager
        {
            public bool WasDisposed { get; private set; }

            public void QueueCommand(InputCommand command)
            {
                EnqueueCommand(command);
            }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
