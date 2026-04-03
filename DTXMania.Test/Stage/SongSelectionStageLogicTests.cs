using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework.Audio;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;

using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageLogicTests
    {
        [Fact]
        public void PopulateSongList_WhenCurrentSongListEmpty_ShouldClearDisplay()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Existing")]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>());

            InvokePrivateMethod(stage, "PopulateSongList");

            Assert.Empty(display.CurrentList);
        }

        [Fact]
        public void PopulateSongList_WhenInSubfolder_ShouldPrependBackEntry()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                CreateScoreNode("Song A"),
                CreateBoxNode("Folder")
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", songs);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(new SongListNode { Title = "Root" });

            InvokePrivateMethod(stage, "PopulateSongList");

            Assert.Equal(3, display.CurrentList.Count);
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(songs[0], display.CurrentList[1]);
            Assert.Same(songs[1], display.CurrentList[2]);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenTaskCompleted_ShouldPromoteSongsAndClearTask()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var songs = new List<SongListNode> { CreateScoreNode("Loaded Song") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>());
            SetPrivateField(stage, "_songInitializationTask", Task.FromResult(songs));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.True(GetPrivateField<bool>(stage, "_songInitializationProcessed"));
            Assert.Null(GetPrivateField<object>(stage, "_songInitializationTask"));
            Assert.Same(songs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Single(display.CurrentList);
            Assert.Same(songs[0], display.CurrentList[0]);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenTaskFaulted_ShouldClearTaskAndFallbackToEmptyList()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Existing")]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_songInitializationTask", Task.FromException<List<SongListNode>>(new InvalidOperationException("boom")));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.True(GetPrivateField<bool>(stage, "_songInitializationProcessed"));
            Assert.Null(GetPrivateField<object>(stage, "_songInitializationTask"));
            Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
            Assert.Empty(display.CurrentList);
        }

        [Fact]
        public void OnSongSelectionChanged_WhenScoreAndScrollIncomplete_ShouldUpdateLightweightUiOnly()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Song A"), CreateScoreNode("Song B")]
            };
            var statusPanel = new SongStatusPanel { Visible = false };
            var previewPanel = new PreviewImagePanel();
            var breadcrumbLabel = new UILabel();
            var existingPreviewSound = new Mock<ISound>();

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_currentBreadcrumb", "Folder A");
            SetPrivateField(stage, "_previewSound", existingPreviewSound.Object);
            GetTextureQueue(display).Clear();

            var selectedSong = CreateScoreNode("Selected");
            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(selectedSong, 2, isScrollComplete: false));

            existingPreviewSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.True(statusPanel.Visible);
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
            Assert.Equal("Folder A", breadcrumbLabel.Text);
            Assert.Empty(GetTextureQueue(display));
        }

        [Fact]
        public void OnSongSelectionChanged_WhenScoreAndScrollComplete_ShouldInvalidateDisplayAndUpdatePanels()
        {
            var stage = CreateStage();
            var selectedSong = CreateScoreNode("Selected");
            var display = new SongListDisplay
            {
                CurrentList = [selectedSong, CreateScoreNode("Other")]
            };
            var statusPanel = new SongStatusPanel { Visible = false };
            var previewPanel = new PreviewImagePanel();
            var breadcrumbLabel = new UILabel();

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_currentBreadcrumb", "");
            GetTextureQueue(display).Clear();
            GetVisibleIndices(display).Clear();

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(selectedSong, 1, isScrollComplete: true));

            Assert.True(statusPanel.Visible);
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(1, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
            Assert.Equal("Root", breadcrumbLabel.Text);
            Assert.NotEmpty(GetTextureQueue(display));
            Assert.NotEmpty(GetVisibleIndices(display));
        }

        [Fact]
        public void OnSongSelectionChanged_WhenNonScore_ShouldExitStatusPanelAndHideStatus()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var statusPanel = new SongStatusPanel { Visible = true };
            var previewPanel = new PreviewImagePanel();
            var previewTexture = new Mock<ITexture>();
            var breadcrumbLabel = new UILabel();
            var folder = CreateBoxNode("Folder");

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(previewPanel, "_currentPreviewTexture", previewTexture.Object);

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(folder, 0, isScrollComplete: true));

            previewTexture.Verify(x => x.RemoveReference(), Times.Once);
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.False(statusPanel.Visible);
            Assert.Same(folder, GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
        }

        [Fact]
        public void OnDifficultyChanged_ShouldSyncStageAndStatusPanel()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel();
            var song = CreateScoreNode("Song", CreateScores(0, 3));

            AttachCoreUi(stage, statusPanel: statusPanel);

            InvokePrivateMethod(stage, "OnDifficultyChanged", stage, new DifficultyChangedEventArgs(song, 3));

            Assert.Equal(3, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Same(song, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(3, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
        }

        [Fact]
        public void NavigateIntoBox_ShouldPushCurrentStateAndPopulateChildren()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var childSongs = new List<SongListNode> { CreateScoreNode("Child Song") };
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var box = CreateBoxNode("Folder", childSongs.ToArray());

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", rootSongs);
            SetPrivateField(stage, "_currentBreadcrumb", "Root");

            InvokePrivateMethod(stage, "NavigateIntoBox", box);

            Assert.Equal(1, GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Count);
            var currentSongList = GetPrivateField<List<SongListNode>>(stage, "_currentSongList");
            Assert.NotNull(currentSongList);
            Assert.Single(currentSongList!);
            Assert.Same(childSongs[0], currentSongList[0]);
            Assert.Equal("Root > Folder", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(childSongs[0], display.CurrentList[1]);
        }

        [Fact]
        public void NavigateBack_ShouldRestorePreviousState()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "NavigateBack");

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Single(display.CurrentList);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
            Assert.Empty(stack);
        }

        [Fact]
        public void SelectSong_WhenTransitionAllowed_ShouldGoToSongTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var song = CreateScoreNode("Song", CreateScores(0, 2), songId: 17);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentDifficulty", 2);

            InvokePrivateMethod(stage, "SelectSong", song);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    It.Is<Dictionary<string, object>>(sharedData =>
                        ReferenceEquals(sharedData["selectedSong"], song) &&
                        (int)sharedData["selectedDifficulty"] == 2 &&
                        (int)sharedData["songId"] == 17)),
                Times.Once);
            Assert.Equal(2.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectSong_WhenDebounceBlocksTransition_ShouldDoNothing()
        {
            var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "SelectSong", CreateScoreNode("Song", songId: 12));

            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectRandomSong_WhenSinglePlayableSongExists_ShouldSelectThatSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var song = CreateScoreNode("Only Song", songId: 45);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                CreateBoxNode("Folder"),
                song
            });

            InvokePrivateMethod(stage, "SelectRandomSong");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(sharedData => ReferenceEquals(sharedData["selectedSong"], song))),
                Times.Once);
        }

        [Fact]
        public void UpdateBreadcrumb_ShouldUseRootFallbackAndCurrentBreadcrumb()
        {
            var stage = CreateStage();
            var label = new UILabel();

            AttachCoreUi(stage, breadcrumb: label);

            SetPrivateField(stage, "_currentBreadcrumb", "");
            InvokePrivateMethod(stage, "UpdateBreadcrumb");
            Assert.Equal("Root", label.Text);

            SetPrivateField(stage, "_currentBreadcrumb", "Genre > Folder");
            InvokePrivateMethod(stage, "UpdateBreadcrumb");
            Assert.Equal("Genre > Folder", label.Text);
        }

        [Fact]
        public void UpdatePhase_WhenFadeInDurationElapsed_ShouldEnterNormalPhase()
        {
            var stage = CreateStage();

            SetPrivateField(stage, "_selectionPhase", SongSelectionPhase.FadeIn);
            SetPrivateField(stage, "_currentPhase", StagePhase.FadeIn);
            SetPrivateField(stage, "_phaseStartTime", 0.0);
            SetPrivateField(stage, "_elapsedTime", SongSelectionUILayout.Timing.FadeInDuration);

            InvokePrivateMethod(stage, "UpdatePhase", 0.0);

            Assert.Equal(SongSelectionPhase.Normal, GetPrivateField<SongSelectionPhase>(stage, "_selectionPhase"));
            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
            Assert.Equal(SongSelectionUILayout.Timing.FadeInDuration, GetPrivateField<double>(stage, "_phaseStartTime"));
        }

        [Fact]
        public void ExecuteInputCommand_MoveDown_ShouldMoveSelectionAndPlayCursorSound()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_elapsedTime", 3.5);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveDown, 0.0));

            Assert.Equal("B", display.SelectedSong!.Title);
            Assert.Equal(3.5, GetPrivateField<double>(stage, "_lastNavigationTime"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_MoveLeftInStatusPanel_ShouldCycleBackwardDifficulty()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2, 4));
            var display = new SongListDisplay
            {
                CurrentList = [song]
            };
            var statusPanel = new SongStatusPanel();
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 4);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            display.CurrentDifficulty = 4;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveLeft, 0.0));

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_MoveRightInStatusPanel_ShouldUseSongListDifficultyCycling()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2));
            var display = new SongListDisplay
            {
                CurrentList = [song]
            };
            var statusPanel = new SongStatusPanel();
            var cursorSound = new Mock<ISound>();

            display.DifficultyChanged += (sender, e) => InvokePrivateMethod(stage, "OnDifficultyChanged", sender!, e);

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 0);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            display.CurrentDifficulty = 0;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveRight, 0.0));

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_BackInStatusPanel_ShouldOnlyExitStatusPanelMode()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
                Times.Never);
        }

        [Fact]
        public void ExecuteInputCommand_BackWithNavigationStack_ShouldRestorePreviousFolder()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Single(display.CurrentList);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
        }

        [Fact]
        public void ExecuteInputCommand_BackAtRoot_ShouldReturnToTitleWhenTransitionAllowed()
        {
            var game = CreateGame(totalGameTime: 1.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition)),
                Times.Once);
            Assert.Equal(1.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void HandleActivateInput_WhenSongSelected_ShouldEnterStatusPanel()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel { Visible = false };
            var selectedSong = CreateScoreNode("Song");

            AttachCoreUi(stage, statusPanel: statusPanel);
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_isInStatusPanel", false);

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.True(statusPanel.Visible);
        }

        [Fact]
        public void HandleActivateInput_WhenInStatusPanel_ShouldSelectSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var selectedSong = CreateScoreNode("Song", songId: 99);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_currentDifficulty", 1);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "HandleActivateInput");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(sharedData => ReferenceEquals(sharedData["selectedSong"], selectedSong))),
                Times.Once);
        }

        [Fact]
        public void HandleActivateInput_WhenBoxSelected_ShouldNavigateIntoBox()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var childSong = CreateScoreNode("Child");
            var box = CreateBoxNode("Folder", childSong);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", box);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { box });

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.Same(box.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(childSong, display.CurrentList[1]);
        }

        [Fact]
        public void ProcessInputCommands_ShouldDrainQueueAndExecuteCommands()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var inputManager = new QueuedInputManager();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_inputManager", inputManager);
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            InvokePrivateMethod(stage, "ProcessInputCommands");

            Assert.Equal("B", display.SelectedSong!.Title);
            Assert.False(inputManager.HasPendingCommands);
        }

        [Fact]
        public void LoadPreviewSound_WhenPreviewFileExists_ShouldLoadSoundAndActivateDelay()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previousSound = new Mock<ISound>();
            var loadedSound = new Mock<ISound>();
            var dir = Path.Combine(Path.GetTempPath(), "dtx-stage-preview", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(dir);

            try
            {
                var previewPath = Path.Combine(dir, "preview.ogg");
                File.WriteAllText(previewPath, "preview");

                AttachResourceManager(stage, resourceManager.Object);
                SetPrivateField(stage, "_previewSound", previousSound.Object);
                SetPrivateField(stage, "_previewPlayDelay", 5.0);
                SetPrivateField(stage, "_isPreviewDelayActive", false);

                resourceManager.Setup(x => x.LoadSound(previewPath)).Returns(loadedSound.Object);

                var song = CreateScoreNode(
                    "Song",
                    chart: new SongChart
                    {
                        FilePath = Path.Combine(dir, "chart.dtx"),
                        PreviewFile = "preview.ogg",
                        HasDrumChart = true,
                        DrumLevel = 30
                    });

                InvokePrivateMethod(stage, "LoadPreviewSound", song);

                previousSound.Verify(x => x.RemoveReference(), Times.Once);
                resourceManager.Verify(x => x.LoadSound(previewPath), Times.Once);
                Assert.Same(loadedSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
                Assert.Equal(0.0, GetPrivateField<double>(stage, "_previewPlayDelay"));
                Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadFailureEventFires_ShouldReleaseLoadedSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previousSound = new Mock<ISound>();
            var failedSound = new Mock<ISound>();
            const string previewPath = "/tmp/preview.ogg";

            AttachResourceManager(stage, resourceManager.Object);
            SetPrivateField(stage, "_previewSound", previousSound.Object);

            resourceManager
                .Setup(x => x.LoadSound(previewPath))
                .Callback(() => resourceManager.Raise(
                    x => x.ResourceLoadFailed += null,
                    new ResourceLoadFailedEventArgs(previewPath, new InvalidOperationException("load failed"))))
                .Returns(failedSound.Object);

            var loaded = InvokePrivateMethod<bool>(stage, "TryLoadPreviewSoundFile", previewPath);

            Assert.False(loaded);
            previousSound.Verify(x => x.RemoveReference(), Times.Once);
            failedSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
        }

        [Fact]
        public void StopCurrentPreview_ShouldResetDelayAndStartBgmFadeIn()
        {
            var stage = CreateStage();
            var previewSound = new Mock<ISound>();

            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_previewPlayDelay", 2.5);
            SetPrivateField(stage, "_isPreviewDelayActive", true);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "StopCurrentPreview");

            previewSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_previewPlayDelay"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadingOut_ShouldClampVolumeAndStopFade()
        {
            var stage = CreateStage();
            var backgroundMusicInstance = new Mock<ISoundInstance>();

            backgroundMusicInstance.SetupProperty(x => x.Volume, SongSelectionUILayout.Audio.BgmMaxVolume);
            backgroundMusicInstance.SetupGet(x => x.State).Returns(SoundState.Playing);

            SetPrivateField(stage, "_backgroundMusicInstance", backgroundMusicInstance.Object);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_bgmFadeOutTimer", 0.0);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 1.0);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmFadeOutDuration, GetPrivateField<double>(stage, "_bgmFadeOutTimer"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmMinVolume, backgroundMusicInstance.Object.Volume, 3);
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadingIn_ShouldClampVolumeAndStopFade()
        {
            var stage = CreateStage();
            var backgroundMusicInstance = new Mock<ISoundInstance>();

            backgroundMusicInstance.SetupProperty(x => x.Volume, SongSelectionUILayout.Audio.BgmMinVolume);
            backgroundMusicInstance.SetupGet(x => x.State).Returns(SoundState.Playing);

            SetPrivateField(stage, "_backgroundMusicInstance", backgroundMusicInstance.Object);
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_bgmFadeInTimer", 0.0);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 2.0);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmFadeInDuration, GetPrivateField<double>(stage, "_bgmFadeInTimer"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmMaxVolume, backgroundMusicInstance.Object.Volume, 3);
        }

        [Fact]
        public void SetBackgroundMusic_WhenReplacingExistingReferences_ShouldCleanupOldResources()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            var newSound = new Mock<ISound>();
            var newInstance = new Mock<ISoundInstance>();

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            stage.SetBackgroundMusic(newSound.Object, newInstance.Object);

            oldSound.Verify(x => x.RemoveReference(), Times.Once);
            oldInstance.Verify(x => x.Stop(), Times.Once);
            oldInstance.Verify(x => x.Dispose(), Times.Once);
            Assert.Same(newSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
            Assert.Same(newInstance.Object, GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
        }

        private static void AttachCoreUi(
            SongSelectionStage stage,
            SongListDisplay? display = null,
            SongStatusPanel? statusPanel = null,
            PreviewImagePanel? previewPanel = null,
            UILabel? breadcrumb = null)
        {
            SetPrivateField(stage, "_songListDisplay", display ?? new SongListDisplay());
            SetPrivateField(stage, "_statusPanel", statusPanel ?? new SongStatusPanel());
            SetPrivateField(stage, "_previewImagePanel", previewPanel ?? new PreviewImagePanel());
            SetPrivateField(stage, "_breadcrumbLabel", breadcrumb ?? new UILabel());
        }

        private static void AttachResourceManager(SongSelectionStage stage, IResourceManager resourceManager)
        {
            SetPrivateField(stage, "_resourceManager", resourceManager);
        }

        private static IList GetTextureQueue(SongListDisplay display)
        {
            return (IList)GetPrivateField<object>(display, "_textureGenerationQueue")!;
        }

        private static HashSet<int> GetVisibleIndices(SongListDisplay display)
        {
            return GetPrivateField<HashSet<int>>(display, "_visibleBarIndices")!;
        }

        private static SongSelectionStage CreateStage(BaseGame? game = null)
        {
            return new SongSelectionStage(game ?? CreateGame());
        }

        private static SongListNode CreateScoreNode(
            string title,
            SongScore[]? scores = null,
            int? songId = null,
            SongChart? chart = null)
        {
            chart ??= new SongChart
            {
                FilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dtx"),
                HasDrumChart = true,
                DrumLevel = 35
            };

            var song = new SongEntity
            {
                Title = title,
                Artist = "Test Artist",
                Charts = [chart]
            };
            chart.Song = song;

            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                Scores = scores ?? CreateScores(0),
                DatabaseSong = song,
                DatabaseChart = chart,
                DatabaseSongId = songId
            };
        }

        private static SongListNode CreateBoxNode(string title, params SongListNode[] children)
        {
            return new SongListNode
            {
                Type = NodeType.Box,
                Title = title,
                Children = new List<SongListNode>(children)
            };
        }

        private static SongScore[] CreateScores(params int[] difficulties)
        {
            var scores = new SongScore[5];
            foreach (var difficulty in difficulties)
            {
                scores[difficulty] = new SongScore
                {
                    DifficultyLevel = difficulty,
                    DifficultyLabel = $"Level {difficulty}"
                };
            }

            return scores;
        }

        private sealed class QueuedInputManager : InputManager
        {
            public void Enqueue(InputCommand command)
            {
                EnqueueCommand(command);
            }
        }
    }
}
